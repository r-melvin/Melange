using System.Collections.Generic;

namespace Melange.Smuggling
{
    /// <summary>Where Turnip Night's boat is: tied up at the Docks quay, or out at sea until <see cref="SmugglingState.BoatBackAt"/>.</summary>
    public enum BoatPhase { Moored = 0, Away = 1 }

    /// <summary>The player's answer to an order text. No answer counts as a quiet yes: the boat still comes, but an empty hold costs nothing.</summary>
    public enum OrderAnswer { None = 0, Accepted = 1, Declined = 2 }

    /// <summary>One of Dafydd's bulk orders: a drug type at a quality floor, a number of units, a price per unit and a departure.</summary>
    public sealed class Order
    {
        public int Id;
        /// <summary>The game's drug type name (Marijuana, Methamphetamine, Cocaine, ...), so Logic needs no game types.</summary>
        public string DrugType;
        /// <summary>The lowest quality taken, as the game's EQuality number (0 Trash .. 4 Heavenly).</summary>
        public int MinQuality;
        public int Units;
        public float UnitPrice;
        /// <summary>Absolute in-game minutes (day * 1440 + minutes since midnight).</summary>
        public int PostedAt;
        public int DepartsAt;
        public OrderAnswer Answer;
    }

    /// <summary>Product aboard: what it was, at what quality, how many units (a brick counts as its packaging quantity).</summary>
    public sealed class CargoLot
    {
        public string ProductId;
        public string ProductName;
        public int Quality;
        public int Units;
    }

    /// <summary>Goods bought for the return leg, or landed and waiting at the boat.</summary>
    public sealed class ImportLine
    {
        public string ItemId;
        public string Name;
        public int Quantity;
        public float Paid;
    }

    /// <summary>
    /// What the smuggling spoke remembers for one saved game. Plain fields, so S1API's JSON save writes it as is; every rule
    /// that reads it lives in Logic/ and is tested without the game.
    /// </summary>
    public sealed class SmugglingState
    {
        public const int CurrentVersion = 1;
        public const int StartingReputation = 50;

        public int Version = CurrentVersion;

        // ---- the way in: Oscar ----
        /// <summary>How many qualifying purchases from Oscar unlock Dafydd's number, rolled once per save; 0 until rolled.</summary>
        public int UnlockThreshold;
        /// <summary>Purchases from Oscar of at least the qualifying spend.</summary>
        public int QualifyingPurchases;
        /// <summary>Oscar has passed on the number: Dafydd texts, the boat takes orders.</summary>
        public bool Unlocked;
        /// <summary>Dafydd's first text has been sent (once per save).</summary>
        public bool Introduced;

        // ---- the sewer route (from the sewer spoke, through the hub) ----
        public bool RouteKnown;
        public bool RouteFromJournal;

        // ---- the boat ----
        public int Reputation = StartingReputation;
        public Order Order;
        public List<CargoLot> Hold = new List<CargoLot>();
        /// <summary>The in-game day the next order is posted (at the posting time); -1 until the first is scheduled.</summary>
        public int NextOrderDay = -1;
        public int NextOrderId = 1;
        public BoatPhase Boat = BoatPhase.Moored;
        public int BoatBackAt = -1;

        // ---- imports ----
        public List<ImportLine> ImportsOrdered = new List<ImportLine>();
        public List<ImportLine> ImportsWaiting = new List<ImportLine>();

        // ---- the methylamine tanker (experimental) ----
        public int LastTankerTipDay = -1;

        // ---- record ----
        public int RunsSailed;
        public int RunsPaid;
        public float TotalPaid;
        public int Seizures;

        public int UnitsAboard
        {
            get
            {
                int n = 0;
                foreach (var lot in Hold) if (lot != null) n += lot.Units;
                return n;
            }
        }

        /// <summary>Lists that came back null from an old or hand-edited save are made empty, so nothing else has to check.</summary>
        public void Repair()
        {
            if (Hold == null) Hold = new List<CargoLot>();
            if (ImportsOrdered == null) ImportsOrdered = new List<ImportLine>();
            if (ImportsWaiting == null) ImportsWaiting = new List<ImportLine>();
            Hold.RemoveAll(l => l == null || l.Units <= 0);
            ImportsOrdered.RemoveAll(l => l == null || l.Quantity <= 0);
            ImportsWaiting.RemoveAll(l => l == null || l.Quantity <= 0);
            if (Reputation < 0) Reputation = 0;
            if (Reputation > 100) Reputation = 100;
            if (NextOrderId < 1) NextOrderId = 1;
        }
    }
}
