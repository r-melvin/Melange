using System;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Levelling;

namespace Melange.Core
{
    /// <summary>The main game scene finished loading (a save is being entered).</summary>
    public sealed class MainSceneLoaded { }

    /// <summary>The main menu finished loading (the game started, or the player quit to the menu).</summary>
    public sealed class MenuLoaded { }

    /// <summary>
    /// XP is about to be awarded, on the peer that awards it (the host for most awards; a co-op client for its own
    /// actions), just before it is sent to the server. Handlers may change <see cref="Amount"/> (multipliers, caps); a
    /// change to 0 or less means nothing is awarded. Left unchanged, the game's award goes through exactly as it was.
    /// A handler must compute from state every peer shares, so a client's award is changed the same way the host's is.
    /// </summary>
    public sealed class XpAwarding
    {
        public int Original { get; }
        public int Amount { get; set; }
        internal XpAwarding(int amount) { Original = amount; Amount = amount; }
    }

    /// <summary>XP was awarded (sent to the server), on the awarding peer, with the amount after any <see cref="XpAwarding"/> changes.</summary>
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

    /// <summary>The hub's game hooks, each the only Melange hook on its method or event. They turn game moments into <see cref="Events"/>.</summary>
    /// <remarks>
    /// On IL2CPP the game's one-line methods can be inlined, and a Harmony patch on them then never runs: checked in game,
    /// patches on <c>RpcLogic___AddXP</c> and <c>RpcLogic___IncreaseTierNetworked</c> applied but never fired. So XP is
    /// hooked on the multi-line method that sends the award to the server, and rank-ups through the game's own
    /// <c>onRankUp</c> event, which needs no patch.
    /// </remarks>
    internal static class GameEvents
    {
        private static LevelManager _subscribedTo;

        public static void Patch(HarmonyLib.Harmony harmony)
        {
            TryPatch(harmony, "XP", typeof(LevelManager), nameof(LevelManager.RpcWriter___Server_AddXP_3316948804), nameof(BeforeAddXp), nameof(AfterAddXp));
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
            if (scene == "Main") { SubscribeRankUp(); Events.Publish(new MainSceneLoaded()); }
            else if (scene == "Menu") { _subscribedTo = null; Events.Publish(new MenuLoaded()); }
        }

        /// <summary>The level manager is a scene object: subscribe to each new one's onRankUp, once.</summary>
        private static void SubscribeRankUp()
        {
            try
            {
                var lm = NetworkSingleton<LevelManager>.Instance;
                if (lm == null || lm == _subscribedTo) return;
                Il2CppSystem.Action<FullRank, FullRank> handler = new Action<FullRank, FullRank>(OnRankUp);
                lm.onRankUp = lm.onRankUp == null
                    ? handler
                    : Il2CppSystem.Delegate.Combine(lm.onRankUp, handler).Cast<Il2CppSystem.Action<FullRank, FullRank>>();
                _subscribedTo = lm;
            }
            catch (Exception e) { Core.Log.Warning($"rank up events are off: {e.Message}"); }
        }

        // Sends an XP award to the server; every award goes through it, on the awarding peer.
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

        private static void OnRankUp(FullRank before, FullRank after)
            => Events.Publish(new TierUp(new RankTier(before.Rank, before.Tier), new RankTier(after.Rank, after.Tier)));
    }
}
