using System.Collections.Generic;
using System.Text;

namespace Melange.Smuggling
{
    /// <summary>
    /// Everything Dafydd "Turnip Night" Seabiscuit and Oscar say for this spoke. Dafydd is a Welshman from Porthcawl who
    /// dresses like a pirate and sails only on moonless nights (hence the name: you can't see a turnip lantern on a dark
    /// sea, and neither can the harbour police). Kept here so the voice stays in one place and the tests can check the
    /// fill-ins.
    /// </summary>
    public static class Lines
    {
        public const string Name = "Dafydd \"Turnip Night\" Seabiscuit";

        // ---- Oscar ----
        public const string OscarLead =
            "You spend like you mean it. A friend of mine, Dafydd, moves things by sea. Big orders, no questions. I've given him your number. Don't laugh at the hat.";
        public const string OscarChoice = "Who's your supplier?";
        public const string OscarRepeat = "Dafydd. Turnip Night, they call him. Boat at the Docks quay, he'll text you. And no, I don't know why the hat.";
        public const string OscarNotYet = "My supplier? Keep buying and we'll see.";

        // ---- Dafydd: texts ----
        public const string Intro =
            "Shwmae! Dafydd Seabiscuit here, Turnip Night to my friends. Oscar says you're good for it. I run a boat out of the Docks quay, east side, can't miss her. Big orders, cash on sailing. I'll text you the first one, butt.";

        public static string OrderText(Order o, int now)
        {
            return $"Right then. I'm after {o.Units} units of {DrugName(o.DrugType)}, {Quality.Name(o.MinQuality)} or better, ${o.UnitPrice:0.00} a unit. " +
                   $"Load her at the quay before she sails at {Timing.Clock(Timing.HhmmOf(o.DepartsAt))} ({Timing.Left(o.DepartsAt - now)} from now). " +
                   "She goes with or without it, mind.";
        }

        public const string AcceptChoice = "Aye, it'll be there";
        public const string DeclineChoice = "Not this time";
        public const string AcceptReply = "Tidy. See you on the quay.";
        public const string DeclineReply = "Fair play. Another time.";
        public const string DeclineTooLate = "Too late for that, there's product aboard already.";

        public static string Departed(Settlement s)
        {
            switch (s.Outcome)
            {
                case RunOutcome.Full:
                    return s.ExcessUnits > 0
                        ? $"Cast off with {s.Loaded} aboard, the extra {s.ExcessUnits} at the cheap rate. ${s.Payout:0} sent. Lush."
                        : $"Cast off, hold full. ${s.Payout:0} sent. Lush.";
                case RunOutcome.Partial: return $"Cast off with {s.Loaded} of {s.Ordered} aboard. ${s.Payout:0} sent. Fill her next time.";
                case RunOutcome.Short: return $"Cast off near empty, {s.Loaded} aboard. ${s.Payout:0} sent. The buyers won't like it, and neither do I.";
                case RunOutcome.NoShow: return "Sailed empty. You said it'd be there, butt. I'll remember that.";
                default: return "Sailed without you this time. No harm done.";
            }
        }

        public static string Returned(int imports) => imports > 0
            ? "Back in port. Your crates are waiting at the boat."
            : "Back in port. Next order in a few days.";

        public const string NoProduct = "Nothing I can sell yet, is it? Cook something and I'll be in touch.";

        public static string Seized(int units) => $"Harbour police lifted that lot on the quay, {units} units gone. Watch yourself.";

        public const string TankerTip =
            "Word to the wise: a tanker's leaving Billy's chemical plant shortly, full of methylamine, headed for the Docks. Stop it on the road and it's yours.";
        public const string TankerTaken = "Ha! Saw the tanker stop. That's yours, butt.";
        public const string TankerMissed = "The tanker's made it to the Docks. Next time.";

        // ---- Dafydd: in person ----
        public const string Greeting = "Shwmae. What'll it be?";
        public const string StatusChoice = "What's the job?";
        public const string LoadChoice = "Load the hold";
        public const string ImportsChoice = "Bring something back";
        public const string CollectChoice = "My crates";
        public const string ByeChoice = "Nothing. Fair winds.";
        public const string Stranger = "Never seen you before in my life, butt. Oscar's the man to ask.";
        public const string HostOnly = "Talk to whoever's running things. I deal with one boss.";

        public static string Status(SmugglingState s, int now)
        {
            if (s.Boat == BoatPhase.Away) return "She's at sea. Back in the morning.";
            if (s.Order == null) return "No order on. I'll text when there is.";
            var o = s.Order;
            return $"{Orders.Describe(o)}. {s.UnitsAboard} aboard. Sails in {Timing.Left(o.DepartsAt - now)}.";
        }

        public static string Loaded(int units, int aboard, int ordered, float risk, bool viaRoute)
        {
            string how = viaRoute ? " Came the old way, did you? Nobody saw a thing." : "";
            return $"{units} units aboard, {aboard} of {ordered} now.{how}";
        }

        public const string NothingToLoad = "Nothing on you I can take for this order.";
        public const string NoOrder = "No order on, nothing to load.";
        public const string AtSea = "She's at sea.";

        public static string ImportsMenu(IList<(string Name, int Crate, float Price)> offers)
        {
            var sb = new StringBuilder("I can bring back: ");
            for (int i = 0; i < offers.Count; i++)
            {
                if (i > 0) sb.Append("; ");
                sb.Append($"{offers[i].Crate} {offers[i].Name} for ${offers[i].Price:0}");
            }
            return sb.ToString();
        }

        public const string ImportsClosed = "Fill an order for me first, then we'll talk about the return leg.";
        public static string ImportBought(string name, int crate) => $"{crate} {name} on the return leg. Paid up front, cheers.";
        public const string ImportNoCash = "Cash up front for imports, butt.";
        public static string Collected(int items, int left) => left > 0 ? $"There's {items}. {left} more when you've room." : $"There's {items}. All yours.";
        public const string NothingWaiting = "Nothing waiting for you.";

        public static string DrugName(string drugType)
        {
            switch (drugType)
            {
                case "Marijuana": return "weed";
                case "Methamphetamine": return "meth";
                case "Cocaine": return "coke";
                case "Shrooms": return "shrooms";
                case "MDMA": return "MDMA";
                case "Heroin": return "heroin";
                default: return string.IsNullOrEmpty(drugType) ? "product" : drugType.ToLowerInvariant();
            }
        }
    }
}
