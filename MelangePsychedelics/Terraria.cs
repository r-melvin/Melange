using System;
using System.Collections.Generic;
using Il2CppScheduleOne.EntityFramework;
using Melange.Core;
using S1API.Interaction;
using UnityEngine;

namespace Melange.Psychedelics
{
    /// <summary>
    /// Placed terrariums. Toads put in a terrarium's slots are released into it (the roster is the spoke's save data; the game
    /// can't hold a toad's milkings on an item); crickets in its slots are the feed, eaten at midnight; the lid is the
    /// "Milk toads" prompt, and the venom goes back into the slots (handlers can carry it to a drying rack). All of it runs
    /// on the host, which owns the save data; a client sees the slots change through the game's own storage networking.
    /// </summary>
    internal static class Terraria
    {
        private const float ScanEvery = 1f;
        private static float _next;
        private static readonly Dictionary<IntPtr, Dressing> _dressed = new Dictionary<IntPtr, Dressing>();
        private static readonly HashSet<string> _warnedFull = new HashSet<string>();

        private sealed class Dressing
        {
            public GameObject Model, Toads;
            public float Scale;
            public InteractionPrompt Milk;
            public int Shown = -1;
            public bool Failed;
        }

        // the model's own numbers (scripts/art/MODELS.md): the sand is 0.83 m up, the inside about 0.86 x 0.41 m, the lid on top
        private const float SandY = 0.83f, InsideX = 0.80f, InsideZ = 0.36f, LidY = 1.10f;

        public static void Reset() { _dressed.Clear(); _warnedFull.Clear(); _next = 0f; }

        private static MelangePsychedelicsData Data => MelangePsychedelicsData.Current;
        private static TerrariumRules Rules => Settings.Rules(Data?.KingSpared ?? false);

        public static void Tick()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + ScanEvery;
            try
            {
                foreach (var b in Placed.All(Ids.Terrarium))
                {
                    var d = Dress(b);
                    var s = Placed.Storage(b);
                    if (s == null) continue;
                    Placed.HideStored(b);
                    int count = -1, ready = 0;
                    if (Host.IsHost && Data != null)
                    {
                        var t = Data.TerrariumFor(Placed.Guid(b));
                        Absorb(s, t, Placed.Guid(b));
                        count = t.Count;
                        ready = Husbandry.Ready(t, Placed.Day);
                    }
                    if (d != null) Show(d, count, ready);
                }
            }
            catch (Exception e) { Mod.Log.Warning("terrariums: " + e.Message); _next = Time.unscaledTime + 30f; }
        }

        private static Dressing Dress(BuildableItem b)
        {
            // a cached dressing whose objects are gone belongs to a terrarium picked up since (its pointer reused)
            if (_dressed.TryGetValue(b.Pointer, out var d) && (d.Toads != null || d.Failed)) return d;
            d = new Dressing();
            _dressed[b.Pointer] = d;
            try
            {
                var s = Placed.Storage(b);
                if (s != null) Placed.Filter(s, Ids.LiveToad, Ids.Crickets, Ids.ToadProduct);
                d.Model = Placed.Dress(b, "terrarium", out d.Scale);
                // without the model the prompt sits on the rack's top shelf height, near enough
                var anchor = d.Model ?? b.gameObject;
                var lid = Placed.Target(anchor, "Melange milk toads", new Vector3(0f, LidY, 0f), new Vector3(0.95f, 0.12f, 0.5f));
                string guid = Placed.Guid(b);
                d.Milk = InteractionPrompt.CreateBuilder(lid).WithMessage("Milk toads").WithRange(3f).WithPriority(5)
                    .OnInteractionStarted(() => Milk(b, guid)).Build();
                d.Toads = new GameObject("Melange toads");
                d.Toads.transform.SetParent(anchor.transform, false);
            }
            catch (Exception e) { d.Failed = true; Mod.Log.Warning("terrarium look/prompt: " + e.Message); }
            return d;
        }

        /// <summary>Toad models on the sand, one per toad (host: the roster; clients: unknown, so none), and the prompt's count.</summary>
        private static void Show(Dressing d, int count, int ready)
        {
            d.Milk?.SetMessage(count < 0 ? "Milk toads" : count == 0 ? "No toads" : ready == 0 ? $"Milked today ({count} toads)" : $"Milk toads ({ready} ready)");
            if (d.Toads == null || count == d.Shown || count < 0) return;
            d.Shown = count;
            for (int i = d.Toads.transform.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(d.Toads.transform.GetChild(i).gameObject);
            int cols = 4;
            for (int i = 0; i < count; i++)
            {
                var toad = Looks.Build("toad", d.Toads.transform, "toad");
                if (toad == null) return;
                int row = i / cols, col = i % cols;
                float x = -InsideX / 2f + (col + 0.5f) * InsideX / cols, z = -InsideZ / 2f + (row % 2 + 0.5f) * InsideZ / 2f;
                toad.transform.localPosition = new Vector3(x, SandY, z);
                toad.transform.localRotation = Quaternion.Euler(0f, (i * 73) % 360, 0f);
            }
        }

        /// <summary>Toads put in the slots go into the tank (while there's room); the rest wait in their slot.</summary>
        private static void Absorb(Il2CppScheduleOne.Storage.StorageEntity s, Terrarium t, string guid)
        {
            var rules = Rules;
            for (int i = 0; i < s.ItemSlots.Count; i++)
            {
                var slot = s.ItemSlots[i];
                if (slot?.ItemInstance == null || slot.ItemInstance.ID != Ids.LiveToad) continue;
                var origin = LiveToads.OriginOf(Items.TierOf(slot.ItemInstance));
                int released = 0;
                while (released < slot.Quantity && t.Release(origin, rules)) released++;
                if (released > 0)
                {
                    slot.ChangeQuantity(-released);
                    Mod.Log.Msg($"terrarium {guid}: {released} {origin} toad(s) released ({t.Count}/{rules.Capacity})");
                }
                if (t.Count >= rules.Capacity && slot.Quantity > 0 && _warnedFull.Add(guid))
                    Items.Notify("Terrarium full", $"It holds {rules.Capacity} toads. The rest wait in the tank's tray.");
            }
        }

        /// <summary>Midnight, on the host: every terrarium eats its crickets, breeds and starves (Husbandry.Feed).</summary>
        public static void OnDayPassed(int day)
        {
            if (!Host.IsHost || Data == null) return;
            try
            {
                var rules = Rules;
                foreach (var b in Placed.All(Ids.Terrarium))
                {
                    var s = Placed.Storage(b);
                    if (s == null) continue;
                    string guid = Placed.Guid(b);
                    var t = Data.TerrariumFor(guid);
                    if (t.Count == 0) continue;
                    var r = Husbandry.Feed(t, Placed.Count(s, Ids.Crickets), day, rules);
                    if (r.Skipped) continue;
                    Placed.Take(s, Ids.Crickets, r.FeedEaten);
                    Mod.Log.Msg($"terrarium {guid} day {day}: ate {r.FeedEaten}, fed {r.Fed}, hungry {r.Hungry}, starved {r.Starved}, born {r.Born}, now {t.Count}");
                    if (r.Born > 0) Items.Notify("Toadlet", "A toad was born in a terrarium.");
                    if (r.Starved > 0) Items.Notify("Toad starved", $"{r.Starved} toad(s) died: keep crickets in the terrarium.");
                    else if (r.Hungry > 0) Items.Notify("Hungry toads", $"{r.Hungry} toad(s) went without crickets last night.");
                }
                _warnedFull.Clear();
            }
            catch (Exception e) { Mod.Log.Warning("terrarium day: " + e.Message); }
        }

        /// <summary>The lid prompt: venom from every toad not milked today, into the tank's slots (or the player's pockets).</summary>
        private static void Milk(BuildableItem b, string guid)
        {
            try
            {
                if (!Host.IsHost) { Items.Notify("Terrarium", "Only the host can milk toads in this version."); return; }
                if (Data == null || b == null) return;
                var rules = Rules;
                var t = Data.TerrariumFor(guid);
                if (t.Count == 0) { Items.Notify("Terrarium", "No toads in here. Put a toad in the tank's tray."); return; }
                int day = Placed.Day;
                var r = Husbandry.Milk(t, day, rules);
                if (r.Tiers.Count == 0) { Items.Notify("Terrarium", "They've all been milked today."); return; }
                var s = Placed.Storage(b);
                int lost = 0;
                var byTier = new int[5];
                foreach (var tier in r.Tiers) byTier[Tier.Clamp(tier)]++;
                for (int tier = 0; tier < byTier.Length; tier++)
                {
                    if (byTier[tier] == 0) continue;
                    if (!Placed.Put(s, Products.Make(Ids.ToadProduct, byTier[tier], tier))) lost += byTier[tier];
                }
                Products.DiscoverOnce(Ids.ToadProduct);
                string dust = r.Dust > 0 ? $" {r.Dust} toad(s) shrivelled to dust." : "";
                string spill = lost > 0 ? $" {lost} went to waste: no room." : "";
                Items.Notify("Milked", $"{r.Tiers.Count} venom.{dust}{spill}");
                Mod.Log.Msg($"terrarium {guid}: milked {r.Tiers.Count} (tiers {string.Join(",", r.Tiers)}), dust {r.Dust}, lost {lost}, left {t.Count}");
            }
            catch (Exception e) { Mod.Log.Warning("milking: " + e.Message); }
        }
    }
}
