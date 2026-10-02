using System;
using HarmonyLib;
using Il2CppScheduleOne.Levelling;

namespace Melange.Core
{
    /// <summary>The main game scene finished loading (a save is being entered).</summary>
    public sealed class MainSceneLoaded { }

    /// <summary>The main menu finished loading (the game started, or the player quit to the menu).</summary>
    public sealed class MenuLoaded { }

    /// <summary>
    /// XP is about to be added, on the host only. Handlers may change <see cref="Amount"/> (multipliers, caps); a
    /// change to 0 or less means nothing is added. Left unchanged, the game's award goes through exactly as it was.
    /// </summary>
    public sealed class XpAwarding
    {
        public int Original { get; }
        public int Amount { get; set; }
        internal XpAwarding(int amount) { Original = amount; Amount = amount; }
    }

    /// <summary>XP was added, on the host, with the amount after any <see cref="XpAwarding"/> changes.</summary>
    public sealed class XpAwarded
    {
        public int Amount { get; }
        internal XpAwarded(int amount) => Amount = amount;
    }

    /// <summary>A rank and tier, as the game counts them: Rank 0 (Street Rat) to 10 (Kingpin), tiers from 1.</summary>
    public readonly struct RankTier : IComparable<RankTier>
    {
        public ERank Rank { get; }
        public int Tier { get; }
        public RankTier(ERank rank, int tier) { Rank = rank; Tier = tier; }
        public int CompareTo(RankTier other) => Rank != other.Rank ? Rank.CompareTo(other.Rank) : Tier.CompareTo(other.Tier);
        public override string ToString() => $"{Rank} {Tier}";
    }

    /// <summary>The shared rank went up one or more tiers, on every peer. <see cref="RankChanged"/>: a new rank, not just a tier.</summary>
    public sealed class TierUp
    {
        public RankTier Before { get; }
        public RankTier After { get; }
        public bool RankChanged => Before.Rank != After.Rank;
        internal TierUp(RankTier before, RankTier after) { Before = before; After = after; }
    }

    /// <summary>The hub's game patches, each the only patch Melange puts on its method. They turn game moments into <see cref="Events"/>.</summary>
    internal static class GameEvents
    {
        public static void Patch(HarmonyLib.Harmony harmony)
        {
            TryPatch(harmony, "XP", typeof(LevelManager), nameof(LevelManager.RpcLogic___AddXP_3316948804), nameof(BeforeAddXp), nameof(AfterAddXp));
            TryPatch(harmony, "rank up", typeof(LevelManager), nameof(LevelManager.RpcLogic___IncreaseTierNetworked_3953286437), null, nameof(AfterTierUp));
        }

        /// <summary>A patch that fails to apply turns off its events only, never the hub.</summary>
        private static void TryPatch(HarmonyLib.Harmony harmony, string what, Type type, string method, string prefix, string postfix)
        {
            try
            {
                var target = AccessTools.Method(type, method) ?? throw new MissingMethodException(type.Name, method);
                harmony.Patch(target,
                    prefix: prefix == null ? null : new HarmonyMethod(typeof(GameEvents), prefix),
                    postfix: postfix == null ? null : new HarmonyMethod(typeof(GameEvents), postfix));
            }
            catch (Exception e) { Core.Log.Warning($"{what} events are off: {e.Message}"); }
        }

        public static void SceneInitialized(string scene)
        {
            if (scene == "Main") Events.Publish(new MainSceneLoaded());
            else if (scene == "Menu") Events.Publish(new MenuLoaded());
        }

        // The server-side logic of LevelManager.AddXP, which every award reaches (a ServerRpc any peer may call). It runs
        // only on the host, so a change to the amount happens once. It is a one-liner, and IL2CPP has been seen to inline
        // tiny methods so a patch never runs (ShopListing.Price, cartel spoke): verify in game before relying on it.
        private static bool BeforeAddXp(ref int xp, ref int __state)
        {
            var awarding = new XpAwarding(xp);
            Events.Publish(awarding);
            __state = awarding.Amount;
            if (awarding.Amount == xp) return true;          // untouched: the game's own award, whatever its sign
            xp = awarding.Amount;
            if (xp > 0) return true;
            __state = 0;                                       // a spoke reduced it to nothing: skip, and report no award
            return false;
        }

        private static void AfterAddXp(int __state)
        {
            if (__state != 0) Events.Publish(new XpAwarded(__state));
        }

        private static void AfterTierUp(FullRank before, FullRank after)
            => Events.Publish(new TierUp(new RankTier(before.Rank, before.Tier), new RankTier(after.Rank, after.Tier)));
    }
}
