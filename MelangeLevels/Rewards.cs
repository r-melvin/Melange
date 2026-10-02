using System;
using Melange.Core;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Levelling;

namespace Melange.Levels
{
    /// <summary>
    /// Grants the schedule's rewards as ranks are reached, once each, on the host; catches up on load (a game already past
    /// the milestones when the mod is installed gets them all); and re-applies the employee bonuses the game doesn't save.
    /// </summary>
    internal static class Rewards
    {
        public const string SlotSource = "levels";

        public static void Start()
        {
            foreach (var (rank, tier, kinds) in Schedule.AllMilestones())
                foreach (var kind in kinds)
                    RankUpScreen.Register(rank, tier, Schedule.Describe(kind), null);
            Events.Subscribe<TierReached>(e => { if (Host.IsHost) Grant((int)e.Reached.Rank, e.Reached.Tier); });
            Events.Subscribe<SaveLoaded>(_ => AfterLoad());
        }

        /// <summary>Once the save has loaded: re-apply employee bonuses, then grant anything reached but not yet rewarded.</summary>
        private static void AfterLoad()
        {
            var data = MelangeLevelsData.Current;
            var lm = NetworkSingleton<LevelManager>.Instance;
            if (data == null || lm == null) return;
            foreach (var kv in data.EmployeeSlotsPlaced) EmployeeSlots.SetBonus(SlotSource, kv.Key, kv.Value);
            if (!Host.IsHost) return;
            int fromRank = data.RewardedUpTo / 1000, fromTier = data.RewardedUpTo % 1000;
            foreach (var (rank, tier) in Ranks.TiersCrossed(fromRank, fromTier, (int)lm.Rank, lm.Tier))
                Grant(rank, tier);
            Events.Publish(new BulkDiscountStepChanged(data.DiscountStep));
            Events.Publish(new PrestigeChanged(data.Prestige));
        }

        public static void Grant(int rank, int tier)
        {
            var data = MelangeLevelsData.Current;
            if (data == null) return;
            int key = MelangeLevelsData.Key(rank, tier);
            if (key <= data.RewardedUpTo) return;                      // already rewarded (a reload, or a repeated event)
            foreach (var kind in Schedule.At(rank, tier))
            {
                switch (kind)
                {
                    case RewardKind.EmployeeSlot:
                        data.EmployeeSlotsEarned++;
                        break;
                    case RewardKind.BulkDiscount:
                        data.DiscountStep = Schedule.DiscountStep(rank, tier);
                        Events.Publish(new BulkDiscountStepChanged(data.DiscountStep));
                        break;
                    case RewardKind.UnderbossCandidate:
                        data.UnderbossCandidates++;
                        Events.Publish(new UnderbossCandidateUnlocked(data.UnderbossCandidates));
                        break;
                    case RewardKind.Prestige:
                        data.Prestige++;
                        Events.Publish(new PrestigeChanged(data.Prestige));
                        break;
                }
                Mod.Log.Msg($"reward at rank {rank} tier {tier}: {Schedule.Describe(kind)}");
            }
            data.RewardedUpTo = key;
        }

        /// <summary>Places one earned employee slot at a property (from the phone app).</summary>
        public static bool PlaceSlot(string propertyCode)
        {
            var data = MelangeLevelsData.Current;
            if (data == null || data.SlotsLeft <= 0 || string.IsNullOrEmpty(propertyCode)) return false;
            if (!EmployeeSlots.CanExtend(EmployeeSlots.Find(propertyCode))) return false;   // no idle points to stand at
            data.EmployeeSlotsPlaced.TryGetValue(propertyCode, out int n);
            data.EmployeeSlotsPlaced[propertyCode] = n + 1;
            EmployeeSlots.SetBonus(SlotSource, propertyCode, n + 1);
            Mod.Log.Msg($"employee slot placed at {propertyCode} ({n + 1} there)");
            return true;
        }
    }
}
