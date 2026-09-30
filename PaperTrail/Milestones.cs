using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Il2CppScheduleOne.Cartel;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.Levelling;
using Il2CppScheduleOne.Map;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.Property;
using Il2CppScheduleOne.Quests;
using MelonLoader;
using UnityEngine;

namespace PaperTrail
{
    /// <summary>
    /// Key moments - a quest completed, a dealer recruited, a property bought, a rank reached - that ask for a save.
    /// </summary>
    /// <remarks>
    /// The request waits for a safe moment, like an auto-save, and requests close together are folded into one save
    /// that names them all. The game resets its own static events each time a game loads, so they are subscribed
    /// again for every game.
    /// </remarks>
    internal static class Milestones
    {
        private static readonly List<string> Reasons = new List<string>();
        private static float _since;

        public static bool Pending => Reasons.Count > 0;
        public static float WaitingFor => Pending ? Time.realtimeSinceStartup - _since : 0f;

        public static void Request(string reason, bool enabled)
        {
            if (!enabled || Mod.Instance == null || Mod.Instance.CurrentSlot <= 0 || !Mod.Instance.PastLoad) return;
            if (Reasons.Contains(reason)) return;
            if (!Pending) _since = Time.realtimeSinceStartup;
            Reasons.Add(reason);
            Mod.Log.Msg($"key moment: {reason}");
        }

        /// <summary>The reason for the save to take now, several folded into one line.</summary>
        public static string Take()
        {
            if (!Pending) return null;
            string text = Reasons.Count == 1 ? Reasons[0] : $"{Reasons[0]} (+{Reasons.Count - 1} more)";
            Reasons.Clear();
            return text;
        }

        public static void Clear() => Reasons.Clear();

        // ---------------------------------------------------------------- quests: the game has no event for all of them

        public static void PatchGameEvents(HarmonyLib.Harmony harmony)
        {
            Patch(harmony, "quests", AccessTools.Method(typeof(Quest), nameof(Quest.Complete)), nameof(QuestCompleted));
            // Regions and suppliers unlock through methods rather than events. Both also run for everything already
            // unlocked while a save loads, which is not a key moment: Request ignores the first seconds of a game.
            Patch(harmony, "new areas", AccessTools.Method(typeof(MapRegionData), nameof(MapRegionData.SetUnlocked)), nameof(RegionUnlocked));
            Patch(harmony, "suppliers", AccessTools.Method(typeof(Supplier), "SupplierUnlocked"), nameof(SupplierUnlocked));
        }

        private static void Patch(HarmonyLib.Harmony harmony, string what, System.Reflection.MethodBase target, string postfix)
        {
            try { harmony.Patch(target, postfix: new HarmonyMethod(typeof(Milestones), postfix)); }
            catch (Exception e) { Mod.Log.Warning($"no saves on changes to {what}: {e.Message}"); }
        }

        private static void RegionUnlocked(MapRegionData __instance)
        {
            try
            {
                if (__instance == null) return;
                string name = string.IsNullOrWhiteSpace(__instance.Name) ? __instance.Region.ToString() : __instance.Name;
                Request("New area unlocked: " + Short(name), Settings.OnRegion);
            }
            catch { }
        }

        private static void SupplierUnlocked(Supplier __instance)
        {
            try
            {
                if (__instance == null) return;
                Request("Supplier unlocked: " + Short(__instance.FullName), Settings.OnSupplier);
            }
            catch { }
        }

        private static void QuestCompleted(Quest __instance)
        {
            try
            {
                if (__instance == null || __instance.State != EQuestState.Completed) return;
                // Every customer deal, dead drop and cartel deal is a "quest" too, and they complete all day: only
                // the story quests are key moments.
                if (__instance.TryCast<Contract>() != null || __instance.TryCast<DeaddropQuest>() != null
                    || __instance.TryCast<Quest_DealForCartel>() != null) return;
                Request("Quest completed: " + Short(__instance.Title), Settings.OnQuest);
            }
            catch { }
        }

        // ---------------------------------------------------------------- the rest, subscribed for each game

        private static Il2CppSystem.Action<Dealer> _dealer;
        private static Il2CppSystem.Action<Customer> _customer;
        private static Property.PropertyChange _property;

        public static void Subscribe()
        {
            Clear();
            Hook("dealers", () =>
            {
                _dealer ??= (Il2CppSystem.Action<Dealer>)new Action<Dealer>(d => Request("Dealer recruited: " + Short(d?.FullName), Settings.OnDealer));
                Dealer.onDealerRecruited -= _dealer;
                Dealer.onDealerRecruited += _dealer;
            });
            Hook("customers", () =>
            {
                _customer ??= (Il2CppSystem.Action<Customer>)new Action<Customer>(c => Request("Customer unlocked: " + Short(c?.NPC?.FullName), Settings.OnCustomer));
                Customer.onCustomerUnlocked -= _customer;
                Customer.onCustomerUnlocked += _customer;
            });
            Hook("properties", () =>
            {
                _property ??= (Property.PropertyChange)new Action<Property>(p => Request("Property bought: " + Short(p?.PropertyName), Settings.OnProperty));
                Property.onPropertyAcquired -= _property;
                Property.onPropertyAcquired += _property;
            });
            Hook("ranks", () =>
            {
                var levels = NetworkSingleton<LevelManager>.Instance;
                Il2CppSystem.Action<FullRank, FullRank> handler = null;
                handler = (Il2CppSystem.Action<FullRank, FullRank>)new Action<FullRank, FullRank>((before, after) =>
                    Request("Rank up: " + Short(after.ToString()), Settings.OnRank));
                levels.onRankUp += handler;
            });
            Hook("the cartel", () =>
            {
                var cartel = NetworkSingleton<Cartel>.Instance;
                cartel.OnStatusChange += (Il2CppSystem.Action<Il2Cpp.ECartelStatus, Il2Cpp.ECartelStatus>)new Action<Il2Cpp.ECartelStatus, Il2Cpp.ECartelStatus>((before, after) =>
                    Request("Cartel: " + after, Settings.OnCartel));
            });
        }

        private static void Hook(string what, Action subscribe)
        {
            try { subscribe(); }
            catch (Exception e) { Mod.Log.Warning($"no saves on changes to {what}: {e.Message}"); }
        }

        private static string Short(string text)
        {
            text = (text ?? "").Trim();
            return text.Length <= 40 ? text : text.Substring(0, 39) + "…";
        }
    }
}
