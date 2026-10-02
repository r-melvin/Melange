using System;
using System.Collections.Generic;
using Melange.Core;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Dialogue;
using Il2CppScheduleOne.Employees;
using Il2CppScheduleOne.Levelling;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.ObjectScripts;
using UnityEngine;
using UnityEngine.Events;
using PropertyType = Il2CppScheduleOne.Property.Property;

namespace Melange.Hydro
{
    /// <summary>
    /// Botanist training (decided with the user): talk to one of your botanists and pay for a course. Hydroponics (2x what
    /// Manny would charge to hire a botanist now) raises his pot limit to 16 and lets him tend hydro trays; aeroponics (5x,
    /// after hydroponics) raises it to 24 and adds aero towers. Only trained botanists may be assigned holes: an untrained one
    /// found with one is relieved of it (the clipboard has no hook for refusing it up front without patching its tiny
    /// checks).
    /// </summary>
    /// <remarks>
    /// The choices are added to each botanist's own dialogue controller, the way the game adds "fire" and "pay" to every
    /// employee. Training lives in the host's save, so only the host (or a single player) is offered it; the limits are
    /// set on the host, where botanists work. The pot limit is a field per botanist (Botanist.MaxAssignedPots), copied into
    /// his clipboard field when his configuration is built, so both are set, again after every load.
    /// </remarks>
    internal static class Courses
    {
        private sealed class Offer
        {
            public Botanist Botanist;
            public DialogueController.DialogueChoice Hydro, Aero;
            public int OwnLimit;
        }

        private static readonly Dictionary<IntPtr, Offer> _offers = new Dictionary<IntPtr, Offer>();
        private static float _nextTick;

        public static void Reset() { _offers.Clear(); _nextTick = 0f; }

        public static void Tick()
        {
            if (Time.unscaledTime < _nextTick) return;
            _nextTick = Time.unscaledTime + 1f;
            try
            {
                foreach (var b in Botanists())
                {
                    if (!_offers.TryGetValue(b.Pointer, out var offer)) offer = Install(b);
                    if (offer == null) continue;
                    Refresh(offer);
                    if (Host.IsHost) { ApplyLimit(offer); Enforce(offer); }
                }
            }
            catch (Exception e) { Mod.Log.Warning("botanist training: " + e.Message); _nextTick = Time.unscaledTime + 30f; }
        }

        private static IEnumerable<Botanist> Botanists()
        {
            var props = PropertyType.OwnedProperties;
            for (int p = 0; props != null && p < props.Count; p++)
            {
                var emps = props[p]?.Employees;
                for (int i = 0; emps != null && i < emps.Count; i++)
                {
                    var b = emps[i]?.TryCast<Botanist>();
                    if (b != null) yield return b;
                }
            }
        }

        private static Offer Install(Botanist b)
        {
            var controller = b.DialogueHandler?.GetComponent<DialogueController>();
            if (controller == null) return null;
            var offer = new Offer { Botanist = b, OwnLimit = b.MaxAssignedPots };
            offer.Hydro = AddChoice(controller, () => Train(offer, TrainingLevel.Hydroponics));
            offer.Aero = AddChoice(controller, () => Train(offer, TrainingLevel.Aeroponics));
            _offers[b.Pointer] = offer;
            return offer;
        }

        private static DialogueController.DialogueChoice AddChoice(DialogueController controller, Action chosen)
        {
            var choice = new DialogueController.DialogueChoice { ChoiceText = "", Enabled = false };
            choice.onChoosen.AddListener((UnityAction)new Action(() =>
            {
                try { chosen(); } catch (Exception e) { Mod.Log.Warning("training choice: " + e.Message); }
            }));
            controller.AddDialogueChoice(choice, 0);
            return choice;
        }

        /// <summary>
        /// Keeps the choices' text (the price moves as more employees are hired) and visibility current. The game's own
        /// show-check is an IL2CPP delegate with an out parameter, which managed code can't safely supply, so the plain Enabled
        /// flag is kept current instead (as the hub does for Oscar).
        /// </summary>
        private static void Refresh(Offer offer)
        {
            var level = Level(offer.Botanist);
            Rank(out int rank, out int tier);
            bool hydroOpen = Unlocks.Reached(Unlocks.Hydro, rank, tier), aeroOpen = Unlocks.Reached(Unlocks.Aero, rank, tier);
            float hire = HirePrice(offer.Botanist);
            foreach (var (choice, course) in new[] { (offer.Hydro, TrainingLevel.Hydroponics), (offer.Aero, TrainingLevel.Aeroponics) })
            {
                bool show = Host.IsHost && MelangeHydroData.Current != null && Training.CanOffer(level, course, hydroOpen, aeroOpen, out _);
                choice.Enabled = show;
                if (show) choice.ChoiceText = $"Train in {Training.Describe(course)} ({MoneyManager.FormatAmount(Training.Price(course, hire))})";
            }
        }

        private static void Train(Offer offer, TrainingLevel course)
        {
            var data = MelangeHydroData.Current;
            var b = offer.Botanist;
            if (data == null || !Host.IsHost || b == null) return;
            Rank(out int rank, out int tier);
            if (!Training.CanOffer(Level(b), course, Unlocks.Reached(Unlocks.Hydro, rank, tier), Unlocks.Reached(Unlocks.Aero, rank, tier), out var why))
            {
                Say(b, $"Not now: {why}.");
                return;
            }
            float price = Training.Price(course, HirePrice(b));
            var money = NetworkSingleton<MoneyManager>.Instance;
            if (money == null || money.cashBalance < price)
            {
                Say(b, $"That course costs {MoneyManager.FormatAmount(price)}, cash up front.");
                return;
            }
            money.ChangeCashBalance(-price, true, true);
            data.Trained[Guid(b)] = (int)course;
            ApplyLimit(offer);
            Mod.Log.Msg($"{b.FullName} trained in {Training.Describe(course)} for ${price:N0}; pot limit {b.MaxAssignedPots}");
            Say(b, course == TrainingLevel.Aeroponics
                ? $"Towers, misters, the lot. I can run {b.MaxAssignedPots} sites now."
                : $"Trays and reservoirs, got it. I can run {b.MaxAssignedPots} sites now.");
        }

        /// <summary>The pot limit for his training, on the botanist and on his clipboard field (built from it).</summary>
        private static void ApplyLimit(Offer offer)
        {
            var b = offer.Botanist;
            int limit = Training.PotLimit(Level(b), offer.OwnLimit);
            if (b.MaxAssignedPots != limit) b.MaxAssignedPots = limit;
            var assigns = b.configuration?.Assigns;
            if (assigns != null && assigns.MaxItems != limit) assigns.MaxItems = limit;
        }

        /// <summary>An untrained botanist found assigned to a hole he can't tend is relieved of it, and says why.</summary>
        private static void Enforce(Offer offer)
        {
            var b = offer.Botanist;
            var assigns = b.configuration?.Assigns;
            var selected = assigns?.SelectedObjects;
            if (selected == null) return;
            var level = Level(b);
            var refused = new List<Hole>();
            for (int i = 0; i < selected.Count; i++)
            {
                var hole = Holes.Get(selected[i]?.TryCast<Pot>());
                if (hole != null && !Training.CanOperate(level, hole.Kind)) refused.Add(hole);
            }
            foreach (var hole in refused) assigns.RemoveItem(hole.Pot);
            if (refused.Count == 0) return;
            var kind = refused[0].Kind;
            Mod.Log.Msg($"{b.FullName}: unassigned from {refused.Count} {kind} site(s) (training: {Training.Describe(level)})");
            Say(b, kind == HoleKind.Aero ? "I'm not trained for aeroponic towers." : "I'm not trained for hydro trays.");
        }

        public static TrainingLevel Level(Botanist b) => MelangeHydroData.Current?.TrainingOf(Guid(b)) ?? TrainingLevel.None;

        private static string Guid(Botanist b) => b.GUID.ToString();

        /// <summary>What Manny would charge for a botanist now: the botanist's signing fee plus the game's extra fee for staff already hired.</summary>
        private static float HirePrice(Botanist b)
        {
            float extra;
            try { extra = Il2CppScheduleOne.NPCs.CharacterClasses.Fixer.GetAdditionalSigningFee(); }
            catch { extra = Training.AdditionalSigningFee(0); }
            return Training.HirePrice(b.SigningFee, extra);
        }

        private static void Rank(out int rank, out int tier)
        {
            rank = 0; tier = 0;
            try
            {
                var lm = NetworkSingleton<LevelManager>.Instance;
                if (lm != null) { rank = (int)lm.Rank; tier = lm.Tier; }
            }
            catch { }
        }

        /// <summary>The botanist answers in a speech bubble once the conversation has closed (the game closes it right after a choice).</summary>
        private static void Say(Botanist b, string text) => MelonLoader.MelonCoroutines.Start(SayAfter(b, text));

        private static System.Collections.IEnumerator SayAfter(Botanist b, string text)
        {
            float until = Time.realtimeSinceStartup + 0.3f;
            while (Time.realtimeSinceStartup < until) yield return null;
            try { b?.DialogueHandler?.ShowWorldspaceDialogue(text, 5f); } catch { }
        }
    }
}
