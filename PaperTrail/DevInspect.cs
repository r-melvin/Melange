using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.UI.MainMenu;
using Il2CppTMPro;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PaperTrail
{
    /// <summary>
    /// Development only, and only with UserData/PaperTrail.inspect present: writes the structure of the game's
    /// menu and pause-menu UI (sprites, colours, fonts, sizes) and opens those screens for a screenshot, so
    /// the save screen can be built from the game's own elements.
    /// </summary>
    internal static class DevInspect
    {
        private static string Marker => Path.Combine(MelonEnvironment.UserDataDirectory, "PaperTrail.inspect");
        private static string PreviewMarker => Path.Combine(MelonEnvironment.UserDataDirectory, "PaperTrail.preview");
        public static bool On => File.Exists(Marker) || File.Exists(PreviewMarker);

        /// <summary>Opens the save screen at the menu and from the pause menu for screenshots. Saves nothing.</summary>
        private static IEnumerator Preview(string scene)
        {
            if (scene == "Menu")
            {
                yield return Wait(4f);
                SaveScreen.Open(SaveScreen.Mode.Load);
                yield return Wait(0.5f);
                Mod.Log.Msg("[preview] load-screen-open " + SaveScreen.DebugState());
                yield return Wait(6f);
                SaveScreen.CloseIfOpen();
                yield return Wait(1f);
                Singleton<LoadManager>.Instance.StartGame(LoadManager.SaveGames[0], false, true);
            }
            else if (scene == "Main")
            {
                float until = Time.realtimeSinceStartup + 120f;
                while (Mod.Instance.CurrentSlot <= 0 && Time.realtimeSinceStartup < until) yield return null;
                yield return Wait(5f);
                Singleton<PauseMenu>.Instance.Pause();
                yield return Wait(1.5f);
                Mod.Log.Msg("[preview] pause-open");
                yield return Wait(4f);
                SaveScreen.Open(SaveScreen.Mode.Save);
                yield return Wait(0.5f);
                Mod.Log.Msg("[preview] save-screen-open " + SaveScreen.DebugState() + " slot=" + Mod.Instance.CurrentSlot);
                yield return Wait(6f);
                SaveScreen.CloseIfOpen();
                Mod.Log.Msg("[preview] done");
            }
        }

        public static IEnumerator Run(string scene)
        {
            if (!On) yield break;
            if (File.Exists(PreviewMarker)) { yield return Preview(scene); yield break; }
            yield return Wait(4f);
            if (scene == "Menu")
            {
                Write("PaperTrail.ui-menu.txt", SceneRoots());
                var cont = Object.FindObjectOfType<ContinueScreen>(true);
                if (cont != null) cont.Open();
                Mod.Log.Msg("[inspect] continue-screen-open");
                yield return Wait(6f);
                if (cont != null) Write("PaperTrail.ui-continue.txt", Tree(cont.transform));
                Mod.Log.Msg("[inspect] loading slot 1 for the pause menu");
                Singleton<LoadManager>.Instance.StartGame(LoadManager.SaveGames[0], false, true);
            }
            else if (scene == "Main")
            {
                yield return Wait(20f);
                var pause = Singleton<PauseMenu>.Instance;
                pause.Pause();
                yield return Wait(2f);
                Write("PaperTrail.ui-pause.txt", Tree(pause.transform));
                Mod.Log.Msg("[inspect] pause-menu-open");
                yield return Wait(6f);
                Mod.Log.Msg("[inspect] done");
            }
        }

        private static IEnumerator Wait(float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until) yield return null;
        }

        private static string SceneRoots()
        {
            var sb = new StringBuilder();
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
                if (go.GetComponentInChildren<Canvas>(true) != null) Dump(go.transform, sb, 0);
            return sb.ToString();
        }

        private static string Tree(Transform t)
        {
            var sb = new StringBuilder();
            Dump(t, sb, 0);
            return sb.ToString();
        }

        private static void Dump(Transform t, StringBuilder sb, int depth)
        {
            if (depth > 14) return;
            var line = new StringBuilder(new string(' ', depth * 2) + t.name + (t.gameObject.activeSelf ? "" : " [off]"));
            var rt = t.GetComponent<RectTransform>();
            if (rt != null)
                line.Append($" | size {rt.rect.width:0}x{rt.rect.height:0} anchor {rt.anchorMin.x:0.##},{rt.anchorMin.y:0.##}-{rt.anchorMax.x:0.##},{rt.anchorMax.y:0.##} pos {rt.anchoredPosition.x:0},{rt.anchoredPosition.y:0}");
            var img = t.GetComponent<Image>();
            if (img != null)
                line.Append($" | Image sprite={(img.sprite != null ? img.sprite.name : "-")} color={Hex(img.color)} type={img.type}");
            var tmp = t.GetComponent<TextMeshProUGUI>();
            if (tmp != null)
                line.Append($" | TMP \"{(tmp.text ?? "").Replace("\n", "\\n")}\" font={(tmp.font != null ? tmp.font.name : "-")} size={tmp.fontSize:0.#} color={Hex(tmp.color)} style={tmp.fontStyle} align={tmp.alignment} spacing={tmp.characterSpacing}");
            var btn = t.GetComponent<Button>();
            if (btn != null)
                line.Append($" | Button transition={btn.transition} normal={Hex(btn.colors.normalColor)} hi={Hex(btn.colors.highlightedColor)} press={Hex(btn.colors.pressedColor)} target={(btn.targetGraphic != null ? btn.targetGraphic.name : "-")}");
            var layout = t.GetComponent<LayoutGroup>();
            if (layout != null) line.Append($" | {layout.GetIl2CppType().Name} pad={layout.padding.left},{layout.padding.top}");
            var outline = t.GetComponent<Outline>();
            if (outline != null) line.Append($" | Outline {Hex(outline.effectColor)} {outline.effectDistance}");
            var names = t.GetComponents<Component>().Select(c => c.GetIl2CppType().Name)
                         .Where(n => n != "RectTransform" && n != "Transform" && n != "CanvasRenderer" && n != "Image"
                                     && n != "TextMeshProUGUI" && n != "Button");
            string extra = string.Join(",", names);
            if (extra.Length > 0) line.Append(" | +" + extra);
            sb.AppendLine(line.ToString());
            for (int i = 0; i < t.childCount; i++) Dump(t.GetChild(i), sb, depth + 1);
        }

        private static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGBA(c);

        private static void Write(string file, string text)
        {
            File.WriteAllText(Path.Combine(MelonEnvironment.UserDataDirectory, file), text);
            Mod.Log.Msg($"[inspect] wrote UserData/{file}");
        }
    }
}
