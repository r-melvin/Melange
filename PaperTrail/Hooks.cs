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

        public static IEnumerator AttachWhenReady(string scene)
        {
            float until = Time.realtimeSinceStartup + 1.5f;
            while (Time.realtimeSinceStartup < until) yield return null;
            try
            {
                if (scene == "Menu") { Templates.Capture(); AttachMainMenu(); }
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

        private static string PathOf(Transform t)
        {
            var parts = new System.Collections.Generic.List<string>();
            for (; t != null; t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        /// <summary>For the dev preview: presses the visible Continue the way a click does.</summary>
        public static bool PressContinue()
        {
            var b = FindButtons("Continue").FirstOrDefault(x => x.gameObject.activeInHierarchy);
            if (b == null) return false;
            b.onClick.Invoke();
            return true;
        }

        /// <summary>The main menu's Continue opens the load screen instead of the game's slot list.</summary>
        private static void AttachMainMenu()
        {
            var buttons = FindButtons("Continue");
            if (buttons.Length == 0) { Mod.Log.Warning("main menu: no Continue button found - the load screen is not on the menu"); return; }
            Ui.FindFont(buttons[0]);
            foreach (var cont in buttons)
            {
                cont.onClick = new Button.ButtonClickedEvent();
                cont.onClick.AddListener((UnityAction)new Action(() => SaveScreen.Open(SaveScreen.Mode.Load)));
                cont.interactable = true;
            }
            var visible = buttons.FirstOrDefault(b => b.gameObject.activeInHierarchy);
            Mod.Log.Msg($"main menu: Continue opens the Paper Trail load screen ({buttons.Length} Continue button(s) hooked; "
                      + (visible != null ? "visible one: " + PathOf(visible.transform) : "none of them is visible yet") + ")");
        }

        /// <summary>A Save button below Resume in the pause menu, copied from it so it looks the same.</summary>
        private static void AttachPauseMenu()
        {
            if (!Singleton<PauseMenu>.InstanceExists) return;
            var menu = Singleton<PauseMenu>.Instance.transform;
            if (menu.GetComponentsInChildren<Transform>(true).Any(t => t.name == SaveButtonName)) return;
            var resume = FindButton("Resume", menu);
            if (resume == null) { Mod.Log.Warning("pause menu: no Resume button found - no Save button added"); return; }
            Ui.FindFont(resume);

            var parent = resume.transform.parent;
            var copy = Object.Instantiate(resume.gameObject, parent);
            copy.name = SaveButtonName;
            copy.transform.SetSiblingIndex(resume.transform.GetSiblingIndex() + 1);
            var button = copy.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener((UnityAction)new Action(() => SaveScreen.Open(SaveScreen.Mode.Save)));
            var text = copy.GetComponentInChildren<TextMeshProUGUI>(true);
            if (text != null) text.text = "Save";

            // Without a layout group the copy sits on top of Resume: move it and everything below down a step.
            if (parent.GetComponent<LayoutGroup>() == null)
            {
                var rt = resume.GetComponent<RectTransform>();
                float step = rt.rect.height + 10f;
                var below = parent.GetComponentsInChildren<Button>(true)
                    .Where(b => b.transform.parent == parent && b.gameObject != copy
                                && b.GetComponent<RectTransform>().anchoredPosition.y < rt.anchoredPosition.y)
                    .ToList();
                foreach (var b in below) b.GetComponent<RectTransform>().anchoredPosition -= new Vector2(0, step);
                copy.GetComponent<RectTransform>().anchoredPosition = rt.anchoredPosition - new Vector2(0, step);
                Mod.Log.Msg($"pause menu: Save added below Resume (moved {below.Count} button(s) down)");
            }
            else Mod.Log.Msg("pause menu: Save added below Resume");
        }
    }
}
