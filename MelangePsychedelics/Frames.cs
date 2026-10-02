using System;
using System.Collections.Generic;
using Il2CppScheduleOne.EntityFramework;
using Melange.Core;
using S1API.Interaction;
using UnityEngine;

namespace Melange.Psychedelics
{
    /// <summary>
    /// Placed blotter frames. Blank sheets and LSD solution go in the frame's slots; the base's prompt picks the design the
    /// frame prints (a design is a brand, see DesignBook); the sheet's prompt doses every sheet the solution covers, one vial a
    /// sheet, each a full sheet of 20 tabs (the game's brick packaging) at the solution's quality. Each sheet gets its own
    /// bad-batch roll, recorded in the batch ledger so the customers' trips can be put down to its design. Host only.
    /// </summary>
    /// <remarks>
    /// Five built-in designs, plus, with the experimental "PaintDesigns" setting, designs the player sprays onto the frame's
    /// sheet with the game's own spray canvas (Painting; SPRAY-SPIKE.md). Untested in game: TESTING.md, "Painted designs".
    /// </remarks>
    internal static class Frames
    {
        private const float ScanEvery = 1f;
        private static float _next;
        private static readonly Dictionary<IntPtr, Dressing> _dressed = new Dictionary<IntPtr, Dressing>();

        private sealed class Dressing
        {
            public GameObject Anchor, Sheet;
            public InteractionPrompt Dose, Design;
            public bool Failed, Modelled;
            public Painting.Canvas Canvas;
        }

        // the model's numbers (scripts/art/MODELS.md): the sheet's centre and size, leaning back 8 degrees
        private static readonly Vector3 SheetCentre = new Vector3(0f, 0.956f, 0.105f), SheetSize = new Vector3(0.6f, 0.4f, 0.08f);
        private static readonly Quaternion SheetTilt = Quaternion.Euler(-8f, 0f, 0f);

        public static void Reset() { _dressed.Clear(); _next = 0f; Painting.Reset(); }

        private static MelangePsychedelicsData Data => MelangePsychedelicsData.Current;

        public static void Tick()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + ScanEvery;
            try
            {
                foreach (var b in Placed.All(Ids.BlotterFrame))
                {
                    var d = Dress(b);
                    Placed.HideStored(b);
                    if (d.Failed || !Host.IsHost || Data == null) continue;
                    var s = Placed.Storage(b);
                    var design = DesignOf(Placed.Guid(b));
                    int sheets = s == null ? 0 : Math.Min(Placed.Count(s, Ids.BlankSheet), Placed.Count(s, Ids.LsdSolution));
                    bool paintable = false;
                    if (Settings.PaintDesigns && d.Modelled)
                    {
                        design = Paint(d, Placed.Guid(b), design);
                        paintable = Painting.Paintable(d.Canvas);
                        // the frame's dose prompt sits in front of the canvas: out of the way while a spray can could paint it
                        bool showDose = !(paintable && Painting.SprayCanInHand());
                        if (d.Sheet != null && d.Sheet.activeSelf != showDose) d.Sheet.SetActive(showDose);
                    }
                    d.Design?.SetMessage(design == null ? "Choose a design" : $"Design: {design.Name} ({Describe(design)})" + (paintable ? ", or spray your own on the sheet" : ""));
                    d.Dose?.SetMessage(sheets == 0 ? "Dose sheets (needs blank sheets and solution)" : $"Dose {sheets} sheet(s)");
                }
            }
            catch (Exception e) { Mod.Log.Warning("blotter frames: " + e.Message); _next = Time.unscaledTime + 30f; }
        }

        private static Dressing Dress(BuildableItem b)
        {
            if (_dressed.TryGetValue(b.Pointer, out var d) && (d.Anchor != null || d.Failed)) return d;
            d = new Dressing();
            _dressed[b.Pointer] = d;
            try
            {
                var s = Placed.Storage(b);
                if (s != null) Placed.Filter(s, Ids.BlankSheet, Ids.LsdSolution, Ids.LsdProduct);
                var model = Placed.Dress(b, "blotter_frame", out _);
                d.Modelled = model != null;
                d.Anchor = model ?? b.gameObject;
                string guid = Placed.Guid(b);
                var sheet = Placed.Target(d.Anchor, "Melange dose", SheetCentre, SheetSize);
                sheet.transform.localRotation = SheetTilt;
                d.Sheet = sheet;
                d.Dose = InteractionPrompt.CreateBuilder(sheet).WithMessage("Dose sheets").WithRange(3f).WithPriority(5)
                    .OnInteractionStarted(() => Dose(b, guid)).Build();
                var foot = Placed.Target(d.Anchor, "Melange design", new Vector3(0f, 0.2f, 0f), new Vector3(0.6f, 0.35f, 0.45f));
                d.Design = InteractionPrompt.CreateBuilder(foot).WithMessage("Choose a design").WithRange(3f).WithPriority(4)
                    .OnInteractionStarted(() => NextDesign(guid)).Build();
            }
            catch (Exception e) { d.Failed = true; Mod.Log.Warning("blotter frame look/prompts: " + e.Message); }
            return d;
        }

        /// <summary>
        /// The frame's spray canvas (made on first need): kept showing the frame's design, and a finished painting made the
        /// frame's design. Returns the frame's design after that.
        /// </summary>
        private static Design Paint(Dressing d, string guid, Design design)
        {
            try
            {
                d.Canvas ??= Painting.Create(d.Anchor, SheetCentre, SheetTilt, SheetSize.x);
                var painted = Painting.Sync(d.Canvas, design, Data.Designs);
                if (painted == null) return design;
                Data.FrameDesigns[guid] = painted.Id;
                Items.Notify("New design", $"{painted.Name}: dose sheets to print it.");
                return painted;
            }
            catch (Exception e)
            {
                Mod.Log.Warning("spray canvas: " + e.Message);
                if (d.Canvas != null) d.Canvas.Failed = true;
                return design;
            }
        }

        private static string Describe(Design d)
        {
            switch (DesignBook.StandingOf(d.Reputation))
            {
                case Standing.Loved: return "loved";
                case Standing.Known: return "known";
                case Standing.Burnt: return "burnt";
                default: return d.SheetsPrinted == 0 ? "new" : "unknown";
            }
        }

        private static Design DesignOf(string guid)
        {
            var data = Data;
            if (data == null) return null;
            data.Designs.EnsurePresets();
            if (data.FrameDesigns.TryGetValue(guid, out var id))
            {
                var d = data.Designs.Find(id);
                if (d != null && !d.Retired) return d;
            }
            var first = data.Designs.Next(null);
            if (first != null) data.FrameDesigns[guid] = first.Id;
            return first;
        }

        private static void NextDesign(string guid)
        {
            if (!Host.IsHost) { Items.Notify("Blotter frame", "Only the host can change designs in this version."); return; }
            var data = Data;
            if (data == null) return;
            var next = data.Designs.Next(DesignOf(guid)?.Id);
            if (next == null) return;
            data.FrameDesigns[guid] = next.Id;
            Mod.Log.Msg($"frame {guid}: design {next.Id} ({next.Name}, reputation {next.Reputation:F0})");
        }

        /// <summary>Doses as many sheets as there are vials for: each sheet its own batch and bad-batch roll.</summary>
        private static void Dose(BuildableItem b, string guid)
        {
            try
            {
                if (!Host.IsHost) { Items.Notify("Blotter frame", "Only the host can dose sheets in this version."); return; }
                var data = Data;
                var s = Placed.Storage(b);
                if (data == null || s == null) return;
                var design = DesignOf(guid);
                if (design == null) { Items.Notify("Blotter frame", "No design to print: un-retire one."); return; }
                int made = 0, bad = 0, lost = 0;
                while (Placed.Count(s, Ids.BlankSheet) > 0 && TakeSolution(s, out int tier))
                {
                    Placed.Take(s, Ids.BlankSheet, 1);
                    bool isBad = BadBatch.IsBad(tier, UnityEngine.Random.value, Settings.BadBatchOdds);
                    data.Batches.Add(design.Id, tier, isBad, Ids.TabsPerSheet);
                    design.SheetsPrinted++;
                    if (isBad) bad++;
                    if (!Placed.Put(s, Products.Make(Ids.LsdProduct, 1, tier, "brick"))) lost++;
                    made++;
                }
                if (made == 0) { Items.Notify("Blotter frame", "Load blank sheets and LSD solution into the frame first."); return; }
                Products.DiscoverOnce(Ids.LsdProduct);
                Items.Notify("Dosed", $"{made} sheet(s) of {design.Name}." + (lost > 0 ? $" {lost} lost: no room." : ""));
                // the player isn't told which went bad: a bad batch looks like any other
                Mod.Log.Msg($"frame {guid}: dosed {made} sheet(s) of {design.Id}, {bad} bad, {lost} lost");
            }
            catch (Exception e) { Mod.Log.Warning("dosing: " + e.Message); }
        }

        /// <summary>Takes one vial of solution and reports its quality.</summary>
        private static bool TakeSolution(Il2CppScheduleOne.Storage.StorageEntity s, out int tier)
        {
            tier = Tier.Standard;
            for (int i = 0; i < s.ItemSlots.Count; i++)
            {
                var slot = s.ItemSlots[i];
                if (slot?.ItemInstance == null || slot.ItemInstance.ID != Ids.LsdSolution || slot.Quantity <= 0) continue;
                tier = Items.TierOf(slot.ItemInstance);
                slot.ChangeQuantity(-1);
                return true;
            }
            return false;
        }
    }
}
