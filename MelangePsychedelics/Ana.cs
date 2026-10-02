using System;
using S1API.Entities;
using S1API.Entities.Relation;
using S1API.Map;
using UnityEngine;

namespace Melange.Psychedelics
{
    /// <summary>
    /// Ana Slughin, the LSD chemist: an S1API supplier NPC (the game's own supplier flow: unlocked when a well-pleased
    /// customer recommends her, then phone orders, dead drops, debt and deliveries). She sells the reagent, blank blotter
    /// sheets and the blotter frame. Her contacts are three Uptown customers, none of them the ones Drug Expansion's Disco Davey uses, so the two
    /// don't stack on the same people. Unlocking her opens the LSD solution recipe.
    /// </summary>
    /// <remarks>
    /// S1API finds this class itself (it scans mod assemblies for NPC subclasses), so she exists even if the spoke turned
    /// itself off for an old Melange Core; then she sells items nobody can use. Her ID is save data: never rename it.
    /// </remarks>
    public sealed class AnaSlughin : NPC
    {
        public override bool IsPhysical => true;
        public override bool IsSupplier => true;

        /// <summary>Suppliers stay hidden until a meeting; this is only where she is made (S1API's own example spot).</summary>
        private static readonly Vector3 HiddenSpawn = new Vector3(-50f, 1.06f, 70f);

        protected override void ConfigurePrefab(NPCPrefabBuilder builder)
        {
            builder.WithIdentity(Ids.AnaNpc, "Ana", "Slughin")
                .WithVoice("female-2", 1.05f)
                .WithRegion(Region.Uptown)
                .WithAppearanceDefaults(a =>
                {
                    a.Gender = 0.85f;
                    a.Height = 0.97f;
                    a.Weight = 0.35f;
                    a.SkinColor = new Color32(214, 178, 150, 255);
                    a.LeftEyeLidColor = a.SkinColor;
                    a.RightEyeLidColor = a.SkinColor;
                    a.PupilDilation = 1.1f;
                    a.HairColor = new Color32(120, 70, 150, 255);
                })
                .WithSpawnPosition(HiddenSpawn)
                .WithSupplierDefaults(s => s
                    .WithOrderLimits(200f, 3000f)
                    .WithStashDeadDrop<S1API.DeadDrops.Native.BehindMedicalPractice>()
                    .WithDeliveryItem(Ids.Reagent)
                    .WithDeliveryItem(Ids.BlankSheet)
                    .WithDeliveryItem(Ids.BlotterFrame)
                    .WithRecommendationMessage("My friend <NAME> can sort you <PRODUCT>. She's careful. I've passed your number on.")
                    .WithUnlockHint("You can now order a <h1>reagent</h>, <h1>blank blotter sheets</h> and <h1>blotter frames</h> from <h1>Ana Slughin</h>."))
                .WithRelationshipDefaults(r => r
                    .WithDelta(2f)
                    .SetUnlocked(false)
                    .WithConnections<S1API.Entities.NPCs.Uptown.FionaHancock, S1API.Entities.NPCs.Uptown.LilyTurner, S1API.Entities.NPCs.Uptown.PearlMoore>());
        }

        protected override void OnCreated()
        {
            try
            {
                base.OnCreated();
                Appearance.Build();
                Relationship.OnUnlocked += Unlocked;
            }
            catch (Exception e) { Mod.Log?.Error("Ana Slughin: " + e); }
        }

        protected override void OnDestroyed()
        {
            try { Relationship.OnUnlocked -= Unlocked; } catch { }
            base.OnDestroyed();
        }

        private void Unlocked(NPCRelationship.UnlockType type, bool notify)
        {
            Ana.OpenRecipe(true);
            if (notify) SendTextMessage("Ana. I hear you're after something cleaner than mushrooms. Reagent and blank sheets, when you're ready. Ergot's your problem.");
        }
    }

    /// <summary>The recipe's lock follows Ana's: re-applied on every peer after each load, as S1API asks.</summary>
    internal static class Ana
    {
        public static void AfterLoad()
        {
            bool unlocked = false;
            try { unlocked = NPC.Get<AnaSlughin>()?.Relationship?.IsUnlocked ?? false; }
            catch (Exception e) { Mod.Log.Warning("Ana Slughin not found: " + e.Message); }
            OpenRecipe(unlocked);
        }

        public static void OpenRecipe(bool open)
        {
            try
            {
                Items.Recipe?.SetAvailability(open, open);
                Mod.Log.Msg($"LSD solution recipe {(open ? "open" : "locked")}");
            }
            catch (Exception e) { Mod.Log.Warning("LSD solution recipe availability: " + e.Message); }
        }
    }
}
