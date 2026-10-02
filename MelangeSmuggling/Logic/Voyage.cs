using System;
using System.Collections.Generic;

namespace Melange.Smuggling
{
    public enum VoyageEventKind
    {
        /// <summary>A new order is up: Dafydd texts it.</summary>
        OrderPosted,
        /// <summary>It was time for an order but the player makes nothing yet; asked again tomorrow.</summary>
        NoProduct,
        /// <summary>The boat sailed; <see cref="VoyageEvent.Settlement"/> says what it paid.</summary>
        Departed,
        /// <summary>The boat is back; <see cref="VoyageEvent.ImportsLanded"/> items wait at the boat.</summary>
        Returned,
    }

    public sealed class VoyageEvent
    {
        public VoyageEventKind Kind;
        public Order Order;
        public Settlement Settlement;
        public int ImportsLanded;
        public override string ToString() => Settlement != null ? $"{Kind} {Settlement.Outcome} ${Settlement.Payout:0}" : Kind.ToString();
    }

    /// <summary>
    /// The boat's week, as a state machine over the save's facts, driven by the clock: an order is posted on its day, the
    /// boat sails at the deadline with or without the product and pays for what's aboard, and comes back in the morning
    /// with any imports. Each step fires once however late the tick (sleep jumps the clock), and catching up after a long
    /// sleep runs the steps in order.
    /// </summary>
    public static class Voyage
    {
        /// <summary>
        /// Advances the state to <paramref name="now"/> (absolute minutes) and returns what happened, in order. Host only.
        /// <paramref name="market"/> is read only when an order is due.
        /// </summary>
        public static List<VoyageEvent> Tick(SmugglingState s, int now, int rank, Func<IReadOnlyList<MarketEntry>> market, SmugglingRules r, Random rng)
        {
            var events = new List<VoyageEvent>();
            if (!s.Unlocked) return events;
            if (s.NextOrderDay < 0) s.NextOrderDay = FirstOrderDay(now, r);

            for (int guard = 0; guard < 8; guard++)       // a long sleep can owe a return and the next order
            {
                if (s.Boat == BoatPhase.Away)
                {
                    if (now < s.BoatBackAt) break;
                    s.Boat = BoatPhase.Moored;
                    events.Add(new VoyageEvent { Kind = VoyageEventKind.Returned, ImportsLanded = Imports.Land(s) });
                    s.BoatBackAt = -1;
                    continue;
                }

                if (s.Order != null)
                {
                    if (now < s.Order.DepartsAt) break;
                    events.Add(Depart(s, r));
                    continue;
                }

                int due = Timing.At(s.NextOrderDay, r.PostTime);
                if (now < due) break;
                var order = Orders.Generate(rng, s.NextOrderId, Math.Max(now, due), rank, s.Reputation, market?.Invoke(), r);
                if (order == null)
                {
                    s.NextOrderDay = Timing.DayOf(now) + 1;
                    events.Add(new VoyageEvent { Kind = VoyageEventKind.NoProduct });
                    break;
                }
                // posted late (after a long sleep) with its deadline already gone: give it the next one instead
                if (order.DepartsAt <= now) order.DepartsAt = Timing.Departure(Timing.DayOf(now), r.LeadDays, r.DepartureTime);
                s.NextOrderId++;
                s.Order = order;
                events.Add(new VoyageEvent { Kind = VoyageEventKind.OrderPosted, Order = order });
            }
            return events;
        }

        /// <summary>The first order comes the morning after the number is passed on (or today, if it's still early).</summary>
        public static int FirstOrderDay(int now, SmugglingRules r)
        {
            int day = Timing.DayOf(now);
            return now < Timing.At(day, r.PostTime) ? day : day + 1;
        }

        /// <summary>The boat sails: pay for what's aboard, adjust reputation, empty the hold, schedule the return and the next order.</summary>
        public static VoyageEvent Depart(SmugglingState s, SmugglingRules r)
        {
            var o = s.Order;
            var settlement = Payment.Settle(o, s.UnitsAboard, r);
            s.Reputation = Payment.ApplyReputation(s.Reputation, settlement.ReputationChange);
            s.RunsSailed++;
            if (settlement.Payout > 0f) { s.RunsPaid++; s.TotalPaid += settlement.Payout; }
            s.Hold.Clear();
            s.Order = null;
            s.Boat = BoatPhase.Away;
            s.BoatBackAt = Timing.ReturnAfter(o.DepartsAt, r.ReturnTime);
            s.NextOrderDay = Math.Max(Timing.DayOf(s.BoatBackAt), Timing.NextOrderDay(o.DepartsAt, r.OrderIntervalDays));
            return new VoyageEvent { Kind = VoyageEventKind.Departed, Order = o, Settlement = settlement };
        }

        /// <summary>
        /// The player's text answer. Declining cancels the order (the boat stays in, nothing changes but the date of the
        /// next one); accepting commits them, so an empty hold then costs reputation. An answer after the boat sailed does nothing.
        /// </summary>
        public static bool Answer(SmugglingState s, int orderId, bool accept, int now, SmugglingRules r)
        {
            var o = s.Order;
            if (o == null || o.Id != orderId || now >= o.DepartsAt || o.Answer != OrderAnswer.None) return false;
            if (accept) { o.Answer = OrderAnswer.Accepted; return true; }
            if (s.UnitsAboard > 0) return false;            // product already aboard: too late to back out
            s.Order = null;
            s.NextOrderDay = Timing.DayOf(now) + Math.Max(1, r.OrderIntervalDays);
            return true;
        }

        /// <summary>Product went into the hold (after the police roll passed).</summary>
        public static void Load(SmugglingState s, string productId, string productName, int quality, int units)
        {
            if (units <= 0) return;
            foreach (var lot in s.Hold)
                if (lot.ProductId == productId && lot.Quality == quality) { lot.Units += units; return; }
            s.Hold.Add(new CargoLot { ProductId = productId, ProductName = productName, Quality = quality, Units = units });
        }
    }
}
