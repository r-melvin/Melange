using System;
using System.Collections.Generic;
using S1API.Entities;
using S1API.Entities.Appearances.AccessoryFields;
using S1API.Entities.Appearances.BodyLayerFields;
using S1API.Entities.Appearances.CustomizationFields;
using S1API.Messaging;
using UnityEngine;
using FaceLayers = S1API.Entities.Appearances.FaceLayerFields;

namespace Melange.Smuggling
{
    /// <summary>
    /// Dafydd "Turnip Night" Seabiscuit: the smuggler, an S1API custom NPC who stands on the Docks quay by his boat and
    /// texts the player orders. Dressed as a pirate: long black curls, a twirled moustache, a white shirt under a burgundy
    /// coat, a belt, boots, a gold chain and a tattooed arm from the game's wardrobe, and a tricorn and an eyepatch of our own
    /// on his head bone (<see cref="Pirate"/>). The game has neither, so his appearance keeps a black cowboy hat, which shows
    /// if the tricorn can't be put on.
    /// </summary>
    public sealed class Dafydd : NPC
    {
        public const string NpcId = "melange_dafydd_seabiscuit";
        private const string Container = "melange_smuggling_dafydd";

        public override bool IsPhysical => true;

        /// <summary>The live NPC this session, once S1API has made it.</summary>
        internal static Dafydd Instance { get; private set; }

        private bool _wired;
        private readonly Pirate _pirate = new Pirate();

        /// <summary>Leaving a save: the NPC goes with the scene, and the next load makes a new one.</summary>
        internal static void Forget() => Instance = null;

        protected override void ConfigurePrefab(NPCPrefabBuilder b)
        {
            var stand = StandPoint();
            b.WithIdentity(NpcId, "Dafydd", "Seabiscuit")
             .WithSpawnPosition(stand)
             .WithAppearanceDefaults(Look)
             // he keeps to his spot by the boat all day; if anything moves him, he walks (or warps) back
             .WithSchedule(plan =>
             {
                 plan.WalkTo(stand, 0, faceDestinationDir: false, within: 1.5f, warpIfSkipped: true);
                 plan.WalkTo(stand, 1200, faceDestinationDir: false, within: 1.5f, warpIfSkipped: true);
             });
        }

        protected override void OnCreated()
        {
            base.OnCreated();
            Instance = this;
            _wired = false;
            try { Appearance.Build(); } catch (Exception e) { Mod.Log.Warning("Dafydd's look: " + e.Message); }
            try { Schedule.Enable(); } catch (Exception e) { Mod.Log.Warning("Dafydd's schedule: " + e.Message); }
            MelonLoader.MelonCoroutines.Start(KeepDressed());
        }

        /// <summary>
        /// Every couple of seconds while he exists (on every peer): the tricorn and eyepatch go on once his avatar has a head,
        /// and the cowboy hat stays hidden under the tricorn.
        /// </summary>
        private System.Collections.IEnumerator KeepDressed()
        {
            while (Instance == this)
            {
                GameObject go = null;
                try { go = gameObject; } catch { }
                if (go == null) yield break;
                _pirate.Dress(go);
                float until = Time.realtimeSinceStartup + 2f;
                while (Time.realtimeSinceStartup < until) yield return null;
            }
        }

        /// <summary>
        /// Text replies come back from the save without their callbacks; this puts them back by label. Labels carry the
        /// order's id, so an old order's buttons can't answer the current one.
        /// </summary>
        protected override void OnResponseLoaded(Response response)
        {
            if (response == null || string.IsNullOrEmpty(response.Label)) return;
            if (TryOrderId(response.Label, "msm_accept_", out int id)) response.OnTriggered = () => Smuggling.Answer(id, true);
            else if (TryOrderId(response.Label, "msm_decline_", out id)) response.OnTriggered = () => Smuggling.Answer(id, false);
        }

        private static bool TryOrderId(string label, string prefix, out int id)
        {
            id = 0;
            return label.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(label.Substring(prefix.Length), out id);
        }

        internal static Vector3 StandPoint()
        {
            var m = Quay.Between(Settings.Berth);
            return new Vector3(m.StandX, -2.5f, m.StandZ);   // the quay's height is about the Docks Warehouse's (-2.5)
        }

        private static void Look(NPCPrefabBuilder.AvatarDefaultsBuilder av)
        {
            av.Gender = 0.05f;
            av.Weight = 0.55f;
            av.Height = 1.03f;
            av.SkinColor = new Color32(205, 160, 125, 255);      // weathered
            av.HairPath = HairStyle.LongCurly;
            av.HairColor = new Color(0.06f, 0.05f, 0.05f);
            av.EyebrowThickness = 1.4f;
            av.EyebrowRestingAngle = -8f;
            av.WithFaceLayer(FaceLayers.Face.SmugPout, Color.black);
            av.WithFaceLayer(FaceLayers.FacialHair.Swirl, new Color(0.06f, 0.05f, 0.05f));
            av.WithFaceLayer(FaceLayers.FacialHair.Goatee, new Color(0.06f, 0.05f, 0.05f));
            av.WithBodyLayer(Shirts.Buttonup, new Color(0.95f, 0.93f, 0.86f));
            av.WithBodyLayer(Pants.CargoPants, new Color(0.18f, 0.13f, 0.09f));
            av.WithBodyLayer(RightArmTattoos.Heart, new Color(0.1f, 0.15f, 0.35f));
            av.WithAccessoryLayer(Head.CowboyHat, new Color(0.07f, 0.07f, 0.07f));   // hidden under the tricorn (Pirate), the fallback
            av.WithAccessoryLayer(Chest.CollarJacket, new Color(0.45f, 0.06f, 0.1f));
            av.WithAccessoryLayer(Waist.Belt, new Color(0.15f, 0.1f, 0.05f));
            av.WithAccessoryLayer(Feet.CombatBoots, new Color(0.1f, 0.08f, 0.06f));
            av.WithAccessoryLayer(Neck.GoldChain, Color.white);
        }

        // ---- talking in person ----

        /// <summary>
        /// His conversation, built once per spawned NPC (after the save loads, when item names are known). Every choice
        /// ends the conversation and he answers in a speech bubble, so what he says is always current.
        /// </summary>
        internal void Wire(IList<(ImportOffer Offer, string Name)> imports)
        {
            if (_wired) return;
            _wired = true;
            try
            {
                Dialogue.BuildAndRegisterContainer(Container, c =>
                {
                    c.AddNode("ENTRY", Lines.Greeting, ch =>
                    {
                        ch.Add("msm_status", Lines.StatusChoice)
                          .Add("msm_load", Lines.LoadChoice)
                          .Add("msm_collect", Lines.CollectChoice);
                        if (imports.Count > 0) ch.Add("msm_imports", Lines.ImportsChoice, "IMPORTS");
                        ch.Add("msm_bye", Lines.ByeChoice);
                    });
                    if (imports.Count > 0)
                        c.AddNode("IMPORTS", "What'll it be? Paid up front.", ch =>
                        {
                            for (int i = 0; i < imports.Count; i++) ch.Add("msm_imp_" + i, $"{imports[i].Offer.Crate} x {imports[i].Name}");
                            ch.Add("msm_imp_none", "Never mind");
                        });
                });
                Dialogue.OnChoiceSelected("msm_status", () => Say(Smuggling.StatusLine()));
                Dialogue.OnChoiceSelected("msm_load", () => Say(Smuggling.LoadHold()));
                Dialogue.OnChoiceSelected("msm_collect", () => Say(Smuggling.CollectImports()));
                for (int i = 0; i < imports.Count; i++)
                {
                    var offer = imports[i];
                    Dialogue.OnChoiceSelected("msm_imp_" + i, () => Say(Smuggling.BuyImport(offer.Offer, offer.Name)));
                }
                Dialogue.UseContainerOnInteract(Container);
            }
            catch (Exception e) { Mod.Log.Warning("Dafydd's dialogue: " + e.Message); }
        }

        /// <summary>A speech bubble, once the conversation that triggered it has closed.</summary>
        internal void Say(string text, float seconds = 6f)
        {
            if (string.IsNullOrEmpty(text)) return;
            MelonLoader.MelonCoroutines.Start(SayLater(text, seconds));
        }

        private System.Collections.IEnumerator SayLater(string text, float seconds)
        {
            float until = Time.realtimeSinceStartup + 0.3f;
            while (Time.realtimeSinceStartup < until) yield return null;
            try { Dialogue.ShowWorldText(text, seconds); }
            catch (Exception e) { Mod.Log.Warning("Dafydd can't speak: " + e.Message); }
        }

        // ---- texting ----

        internal void Text(string message)
        {
            try { SendTextMessage(message); }
            catch (Exception e) { Mod.Log.Warning($"Dafydd's text failed ({e.Message}): {message}"); }
        }

        internal void TextOrder(Order o, int now)
        {
            try
            {
                var accept = new Response { Label = "msm_accept_" + o.Id, Text = Lines.AcceptChoice, OnTriggered = () => Smuggling.Answer(o.Id, true) };
                var decline = new Response { Label = "msm_decline_" + o.Id, Text = Lines.DeclineChoice, OnTriggered = () => Smuggling.Answer(o.Id, false) };
                SendTextMessage(Lines.OrderText(o, now), new[] { accept, decline });
            }
            catch (Exception e) { Mod.Log.Warning($"Dafydd's order text failed: {e.Message}"); }
        }
    }
}
