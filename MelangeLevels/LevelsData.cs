using System.Collections.Generic;
using S1API.Saveables;
using Melange.Core;
using S1API.Internal.Abstraction;

namespace Melange.Levels
{
    /// <summary>
    /// What the levels spoke remembers for one saved game. An S1API saveable, so the game's own save system writes it into the
    /// slot. Its type name is prefixed so it can't collide with another mod's save data (S1API names saves by short type name).
    /// </summary>
    public sealed class MelangeLevelsData : Saveable, IResettableSaveData
    {
        public static MelangeLevelsData Current { get; private set; }

        public const int CurrentVersion = 1;
        [SaveableField("version")] public int Version = CurrentVersion;
        /// <summary>The highest rank and tier already rewarded, as rank*1000+tier, so nothing is granted twice.</summary>
        [SaveableField("rewardedUpTo")] public int RewardedUpTo;
        [SaveableField("slotsEarned")] public int EmployeeSlotsEarned;
        /// <summary>Property code -> extra employees placed there.</summary>
        [SaveableField("slotsPlaced")] public Dictionary<string, int> EmployeeSlotsPlaced = new Dictionary<string, int>();
        [SaveableField("candidates")] public int UnderbossCandidates;
        [SaveableField("discountStep")] public int DiscountStep;
        [SaveableField("prestige")] public int Prestige;
        /// <summary>Offer kind -> the in-game day it was last used.</summary>
        [SaveableField("offersUsed")] public Dictionary<string, int> OffersLastUsed = new Dictionary<string, int>();
        [SaveableField("warehouseDay")] public int WarehouseDiscountDay = -1;
        /// <summary>The police look away until this in-game minute (<see cref="Offers.AbsoluteMinute"/>); -1: not bought.</summary>
        [SaveableField("lenientUntil")] public int LenientUntil = -1;

        public int LastUsed(OfferKind kind) => OffersLastUsed.TryGetValue(kind.ToString(), out int d) ? d : -1;

        public MelangeLevelsData() { Current = this; SaveData.Track(this); }

        /// <summary>
        /// Back to a fresh game's values. S1API keeps one instance for the whole session and, loading a save, only sets the
        /// fields that save has files for: without this, a save with no Levels data loaded after one with it would inherit
        /// that save's rewards. Called on returning to the menu, before the next save loads.
        /// </summary>
        public void ResetToDefaults()
        {
            Version = CurrentVersion;
            RewardedUpTo = 0;
            EmployeeSlotsEarned = 0;
            EmployeeSlotsPlaced = new Dictionary<string, int>();
            UnderbossCandidates = 0;
            DiscountStep = 0;
            Prestige = 0;
            OffersLastUsed = new Dictionary<string, int>();
            WarehouseDiscountDay = -1;
            LenientUntil = -1;
        }

        public int SlotsLeft
        {
            get
            {
                int placed = 0;
                foreach (var n in EmployeeSlotsPlaced.Values) placed += n;
                return EmployeeSlotsEarned - placed;
            }
        }

        public static int Key(int rank, int tier) => rank * 1000 + tier;
    }
}
