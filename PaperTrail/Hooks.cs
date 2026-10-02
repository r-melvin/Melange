using System;
using System.Collections;
using System.Linq;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.UI;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PaperTrail
{
    /// <summary>Where the save screen is reached from: the main menu's Continue, and Save in the pause menu.</summary>
    /// <remarks>Buttons are found by their label, so a moved or renamed object in the game's scenes does not break it.</remarks>
    internal static class Hooks
    {
        private const string SaveButtonName = "PaperTrail.Save";
        private const string LoadButtonName = "PaperTrail.Load";
        private const string LoadGameButtonName = "PaperTrail.LoadGame";

        public static IEnumerator AttachWhenReady(string scene)
        {
            // As soon as the menu's buttons exist, so Load Game is there when the menu shows rather than popping in;
            // the pause menu is not seen until later, so it can wait the same short while as before.
            float until = Time.realtimeSinceStartup + (scene == "Menu" ? 10f : 1.5f);
            while (Time.realtimeSinceStartup < until
                   && (scene != "Menu" || !FindButtons("Continue").Any(b => b.gameObject.activeInHierarchy)))
                yield return null;
            try
            {
                if (scene == "Menu") { AttachMainMenu(); Templates.Capture(); }
                else if (scene == "Main") AttachPauseMenu();
            }
            catch (Exception e) { Mod.Log.Warning($"could not add the save screen to the {scene} scene: {e.Message}"); }
        }

        /// <summary>
        /// Every button carrying this label. The game keeps old, hidden copies of some (the main menu has a
        /// "Deprecated" Continue), so callers take the visible one, or all of them.
        /// </summary>
        private static Button[] FindButtons(string label, Transform under = null)
        {
            var buttons = under != null ? under.GetComponentsInChildren<Button>(true).ToArray()
                                        : Object.FindObjectsOfType<Button>(true).ToArray();
            return buttons.Where(b =>
            {
                var t = b.GetComponentInChildren<TextMeshProUGUI>(true);
                return t != null && string.Equals(t.text?.Trim(), label, StringComparison.OrdinalIgnoreCase);
            }).OrderByDescending(b => b.gameObject.activeInHierarchy).ToArray();
        }

        private static Button FindButton(string label, Transform under = null) => FindButtons(label, under).FirstOrDefault();

        internal static string PathOf(Transform t)
        {
            var parts = new System.Collections.Generic.List<string>();
            for (; t != null; t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

#if PT_DEV
        /// <summary>For the dev preview: presses the visible Continue the way a click does.</summary>
        public static bool PressContinue()
        {
            var b = FindButtons("Continue").FirstOrDefault(x => x.gameObject.activeInHierarchy);
            if (b == null) return false;
            b.onClick.Invoke();
            return true;
        }
#endif

        /// <summary>
        /// The main menu's Continue loads the last played save straight away, and a Load Game button below it opens the
        /// load screen (the game's Continue opened its slot list).
        /// </summary>
        private static void AttachMainMenu()
        {
            var buttons = FindButtons("Continue");
            if (buttons.Length == 0) { Mod.Log.Warning("main menu: no Continue button found - Continue and Load Game are not hooked"); return; }
            Ui.FindFont(buttons[0]);
            foreach (var cont in buttons)
            {
                cont.onClick = new Button.ButtonClickedEvent();
                cont.onClick.AddListener((UnityAction)new Action(ContinueLastPlayed));      // greyed out by the game with no saves
            }
            var visible = buttons.FirstOrDefault(b => b.gameObject.activeInHierarchy);
            if (visible == null) { Mod.Log.Warning("main menu: no visible Continue button - no Load Game button added"); return; }
            if (!visible.transform.parent.GetComponentsInChildren<Transform>(true).Any(t => t.name == LoadGameButtonName))
                AddButtonBelow(visible, visible.gameObject, LoadGameButtonName, "Load Game", () => SaveScreen.Open(SaveScreen.Mode.Load));
            Mod.Log.Msg($"main menu: Continue loads the last played save, Load Game opens the load screen ({PathOf(visible.transform)})");
        }

        /// <summary>Loads the most recently played save as it was last saved; with none, opens the load screen.</summary>
        private static void ContinueLastPlayed()
        {
            var last = Il2CppScheduleOne.Persistence.LoadManager.LastPlayedGame;
            if (last == null) { SaveScreen.Open(SaveScreen.Mode.Load); return; }
            Mod.Log.Msg($"continue: loading {last.OrganisationName} (slot {last.SaveSlotNumber})");
            Mod.LoadNow(last.SaveSlotNumber, null, false);
        }

        /// <summary>Save and Load buttons below Resume in the pause menu, copied from it so they look the same.</summary>
        private static void AttachPauseMenu()
        {
            if (!Singleton<PauseMenu>.InstanceExists) return;
            var menu = Singleton<PauseMenu>.Instance.transform;
            if (menu.GetComponentsInChildren<Transform>(true).Any(t => t.name == SaveButtonName)) return;
            var resume = FindButton("Resume", menu);
            if (resume == null) { Mod.Log.Warning("pause menu: no Resume button found - no Save or Load button added"); return; }
            Ui.FindFont(resume);

            var save = AddButtonBelow(resume, resume.gameObject, SaveButtonName, "Save", () => SaveScreen.Open(SaveScreen.Mode.Save));
            AddButtonBelow(resume, save, LoadButtonName, "Load", () => SaveScreen.Open(SaveScreen.Mode.Load));
            Mod.Log.Msg("pause menu: Save and Load added below Resume");
        }

        /// <summary>A copy of <paramref name="model"/> placed just below <paramref name="above"/>, doing <paramref name="onClick"/>.</summary>
        private static GameObject AddButtonBelow(Button model, GameObject above, string name, string label, Action onClick)
        {
            var parent = model.transform.parent;
            var copy = Object.Instantiate(model.gameObject, parent);
            copy.name = name;
            copy.transform.SetSiblingIndex(above.transform.GetSiblingIndex() + 1);
            var button = copy.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener((UnityAction)onClick);
            var text = copy.GetComponentInChildren<TextMeshProUGUI>(true);
            if (text != null) text.text = label;

            // Without a layout group the copy sits on top of the button it was copied from: move it and
            // everything below a step down.
            if (parent.GetComponent<LayoutGroup>() == null)
            {
                var anchor = above.GetComponent<RectTransform>();
                float step = model.GetComponent<RectTransform>().rect.height + 10f;
                foreach (var b in parent.GetComponentsInChildren<Button>(true))
                {
                    var rt = b.GetComponent<RectTransform>();
                    if (b.transform.parent == parent && b.gameObject != copy && rt.anchoredPosition.y < anchor.anchoredPosition.y)
                        rt.anchoredPosition -= new Vector2(0, step);
                }
                copy.GetComponent<RectTransform>().anchoredPosition = anchor.anchoredPosition - new Vector2(0, step);
            }
            return copy;
        }
    }
}
