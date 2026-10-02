using System;

namespace Melange.Smuggling
{
    public enum RunOutcome
    {
        /// <summary>The order was filled (anything over it bought cheaper).</summary>
        Full,
        /// <summary>Half or more of it.</summary>
        Partial,
        /// <summary>Something, but under half.</summary>
        Short,
        /// <summary>Accepted, and nothing aboard.</summary>
        NoShow,
        /// <summary>Never answered and nothing aboard: the boat sails empty and nobody holds it against you.</summary>
        Skipped,
    }

    public sealed class Settlement
    {
        public int Ordered;
        public int Loaded;
        public int PaidUnits;
        public int ExcessUnits;
        public float Payout;
        public int ReputationChange;
        public RunOutcome Outcome;
    }

    /// <summary>
    /// What the boat pays when it sails: the order's price for every unit aboard up to the order, the over-delivery rate for
    /// the rest (the hold never takes more than its capacity), nothing for an empty hold. The hold only ever takes product
    /// that matches the order (Orders.ItemsTaken), so everything aboard counts.
    /// </summary>
    public static class Payment
    {
        public static Settlement Settle(Order o, int loaded, SmugglingRules r)
        {
            var s = new Settlement { Loaded = Math.Max(0, loaded) };
            if (o == null || o.Units <= 0) { s.Outcome = RunOutcome.Skipped; return s; }
            s.Ordered = o.Units;
            s.PaidUnits = Math.Min(s.Loaded, o.Units);
            s.ExcessUnits = Math.Max(0, Math.Min(s.Loaded, Orders.Capacity(o, r)) - o.Units);
            s.Payout = (float)Math.Round(s.PaidUnits * o.UnitPrice + s.ExcessUnits * o.UnitPrice * r.OverDeliveryRate, 0);

            double fill = (double)s.Loaded / o.Units;
            if (s.Loaded == 0)
            {
                s.Outcome = o.Answer == OrderAnswer.Accepted ? RunOutcome.NoShow : RunOutcome.Skipped;
                s.ReputationChange = s.Outcome == RunOutcome.NoShow ? r.ReputationNoShow : 0;
            }
            else if (fill >= 1.0) { s.Outcome = RunOutcome.Full; s.ReputationChange = r.ReputationFull; }
            else if (fill >= 0.5) { s.Outcome = RunOutcome.Partial; s.ReputationChange = 0; }
            else { s.Outcome = RunOutcome.Short; s.ReputationChange = r.ReputationShort; }
            return s;
        }

        public static int ApplyReputation(int reputation, int change) => Math.Max(0, Math.Min(100, reputation + change));
    }
}
