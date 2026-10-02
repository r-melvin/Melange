using System;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Levelling;

namespace Melange.Core
{
    /// <summary>The main game scene finished loading (a save is being entered).</summary>
    public sealed class MainSceneLoaded { }

    /// <summary>The save has finished loading (the game's LoadManager reports it loaded): scene objects, shops and save data are ready.</summary>
    public sealed class SaveLoaded { }

    /// <summary>An in-game day passed (midnight), on every peer. <see cref="Day"/> is the game's elapsed day count.</summary>
    public sealed class DayPassed
    {
        public int Day { get; }
        internal DayPassed(int day) => Day = day;
    }

    /// <summary>An in-game week passed (Monday midnight), on every peer.</summary>
    public sealed class WeekPassed { }

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

    /// <summary>One tier reached, on every peer. A single rank-up can cover several tiers: each gets its own <see cref="TierReached"/>, in order.</summary>
    public sealed class TierReached
    {
        public RankTier Reached { get; }
        /// <summary>Tier 1 of a rank: a new rank, not just a tier.</summary>
        public bool IsNewRank => Reached.Tier == 1;
        internal TierReached(RankTier reached) => Reached = reached;
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
        private static Il2CppScheduleOne.GameTime.TimeManager _timeSubscribedTo;

        public static void Patch(HarmonyLib.Harmony harmony)
        {
            TryPatch(harmony, "XP", typeof(LevelManager), nameof(LevelManager.RpcWriter___Server_AddXP_3316948804), nameof(BeforeAddXp), nameof(AfterAddXp));
            OrderTotals.Patch(harmony);
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
            if (scene == "Main")
            {
                SubscribeRankUp();
                SubscribeDays();
                RankUpScreen.Apply();
                Prices.OnSceneLoaded();
                Events.Publish(new MainSceneLoaded());
                MelonLoader.MelonCoroutines.Start(WhenLoaded());
            }
            else if (scene == "Menu") { _subscribedTo = null; _timeSubscribedTo = null; EmployeeSlots.Reset(); Managers.Reset(); Events.Publish(new MenuLoaded()); }
        }

        private static System.Collections.IEnumerator WhenLoaded()
        {
            float until = UnityEngine.Time.realtimeSinceStartup + 300f;
            while (UnityEngine.Time.realtimeSinceStartup < until && !IsGameLoaded()) yield return null;
            if (!IsGameLoaded()) yield break;
            RankUpScreen.Apply();
            Prices.Refresh();
            EmployeeSlots.ApplyAll();
            Events.Publish(new SaveLoaded());
        }

        private static bool IsGameLoaded()
        {
            try
            {
                return Singleton<Il2CppScheduleOne.Persistence.LoadManager>.InstanceExists
                    && Singleton<Il2CppScheduleOne.Persistence.LoadManager>.Instance.IsGameLoaded
                    && !Singleton<Il2CppScheduleOne.Persistence.LoadManager>.Instance.IsLoading;
            }
            catch { return false; }
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

        /// <summary>The time manager clears its day and week events when its scene ends: subscribe to each new one, once.</summary>
        private static void SubscribeDays()
        {
            try
            {
                var tm = NetworkSingleton<Il2CppScheduleOne.GameTime.TimeManager>.Instance;
                if (tm == null || tm == _timeSubscribedTo) return;
                Il2CppSystem.Action day = new Action(OnDayPass);
                Il2CppSystem.Action week = new Action(() => Events.Publish(new WeekPassed()));
                tm.onDayPass = tm.onDayPass == null ? day : Il2CppSystem.Delegate.Combine(tm.onDayPass, day).Cast<Il2CppSystem.Action>();
                tm.onWeekPass = tm.onWeekPass == null ? week : Il2CppSystem.Delegate.Combine(tm.onWeekPass, week).Cast<Il2CppSystem.Action>();
                _timeSubscribedTo = tm;
            }
            catch (Exception e) { Core.Log.Warning($"day and week events are off: {e.Message}"); }
        }

        private static void OnDayPass()
        {
            int day = 0;
            try { day = NetworkSingleton<Il2CppScheduleOne.GameTime.TimeManager>.Instance.ElapsedDays; } catch { }
            Events.Publish(new DayPassed(day));
            Managers.OnDayPassed(day);
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
        {
            Events.Publish(new TierUp(new RankTier(before.Rank, before.Tier), new RankTier(after.Rank, after.Tier)));
            foreach (var (rank, tier) in Ranks.TiersCrossed((int)before.Rank, before.Tier, (int)after.Rank, after.Tier))
                Events.Publish(new TierReached(new RankTier((ERank)rank, tier)));
        }
    }
}
