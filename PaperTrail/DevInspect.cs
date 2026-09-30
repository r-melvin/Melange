using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using Il2CppScheduleOne;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Interaction;
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
        private static bool _secondPass;

        private static string PlayerState(string when)
        {
            var cam = PlayerSingleton<PlayerCamera>.InstanceExists ? PlayerSingleton<PlayerCamera>.Instance : null;
            var move = PlayerSingleton<PlayerMovement>.InstanceExists ? PlayerSingleton<PlayerMovement>.Instance : null;
            return $"[preview] {when}: saveScreenOpen={SaveScreen.IsOpen} uiElements={cam?.ActiveUIElementCount} canLook={cam?.CanLook} "
                 + $"canMove={move?.CanMove} timeScale={Time.timeScale} typing={GameInput.IsTyping} "
                 + $"cursorLock={Cursor.lockState} cursorVisible={Cursor.visible} "
                 + $"paused={(Singleton<PauseMenu>.InstanceExists && Singleton<PauseMenu>.Instance.IsPaused)}";
        }

        private static InteractableObject Hovered()
            => Singleton<InteractionManager>.InstanceExists ? Singleton<InteractionManager>.Instance.HoveredInteractableObject : null;

        /// <summary>
        /// The player starts beside a safehouse save point. Aim at it, step forward and click it - the script presses
        /// the real key and mouse button at the markers - then close the screen with Escape and check that the camera,
        /// movement and clock are all handed back.
        /// </summary>
        private static IEnumerator SavePointScenario()
        {
            var player = Player.Local;
            var points = Object.FindObjectsOfType<SavePoint>();
            SavePoint point = null;
            float best = float.MaxValue;
            foreach (var sp in points)
            {
                float d = Vector3.Distance(sp.transform.position, player.transform.position);
                if (d < best) { best = d; point = sp; }
            }
            Mod.Log.Msg($"[preview] savepoints={points.Length} nearest={(point != null ? point.name : "none")} distance={best:0.0}");
            if (point == null) yield break;
            Mod.Log.Msg(PlayerState("before"));

            // The intercom refuses for a minute after any save, the game's own cooldown: wait it out first.
            float cool = Time.realtimeSinceStartup + 90f;
            while (Singleton<SaveManager>.Instance.SecondsSinceLastSave < 62f && Time.realtimeSinceStartup < cool) yield return null;
            Mod.Log.Msg($"[preview] savepoint-cooldown-over secondsSinceSave={Singleton<SaveManager>.Instance.SecondsSinceLastSave:0}");
            var cam = PlayerSingleton<PlayerCamera>.Instance;
            var target = point.IntObj.transform.position;
            cam.LookAt(target, 0.4f);                            // "rotate the camera"
            yield return Wait(1.5f);
            Mod.Log.Msg("[preview] savepoint-step");             // the script holds W for half a second
            bool sawW = false; float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 2.5f) { sawW |= Input.GetKey(KeyCode.W); yield return null; }
            Mod.Log.Msg($"[preview] probe: key W reached the game = {sawW}");
            for (int attempt = 0; attempt < 6 && Hovered() != point.IntObj; attempt++)
            {
                cam.LookAt(target, 0.3f);
                yield return Wait(0.7f);
            }
            var hovered = Hovered();
            Mod.Log.Msg($"[preview] savepoint-hovered={hovered == point.IntObj} hoveredName={(hovered != null ? hovered.name : "none")} "
                      + $"distance={Vector3.Distance(cam.transform.position, target):0.0} type={point.IntObj.interactionType} state={point.IntObj._interactionState}");
            yield return Wait(0.5f);
            Mod.Log.Msg("[preview] savepoint-click");            // the script holds E
            float until = Time.realtimeSinceStartup + 6f;
            bool sawE = false; bool sawInteract = false;
            while (!SaveScreen.IsOpen && Time.realtimeSinceStartup < until)
            {
                sawE |= Input.GetKey(KeyCode.E);
                sawInteract |= GameInput.GetButton(GameInput.ButtonCode.Interact);
                yield return null;
            }
            Mod.Log.Msg($"[preview] probe: key E reached the game = {sawE}, the game's Interact button = {sawInteract}, "
                      + $"save point presses = {Mod.SavePointPresses}");
            yield return Wait(1.2f);
            Mod.Log.Msg(PlayerState("after-click"));
            Mod.Log.Msg("[preview] savepoint-screen-open");      // screenshot
            yield return Wait(2f);
            Mod.Log.Msg("[preview] savepoint-escape");           // the script presses Escape
            bool sawEsc = false; t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 2.5f) { sawEsc |= Input.GetKey(KeyCode.Escape); yield return null; }
            Mod.Log.Msg($"[preview] probe: Escape reached the game = {sawEsc}");
            Mod.Log.Msg(PlayerState("after-escape"));
            Mod.Log.Msg("[preview] savepoint-done");
        }

        private static string PreviewMarker => Path.Combine(MelonEnvironment.UserDataDirectory, "PaperTrail.preview");
        public static bool On => File.Exists(Marker) || File.Exists(PreviewMarker);

        /// <summary>Drives the save screen at the menu and in a game with real button presses, for screenshots and logs.</summary>
        private static IEnumerator Preview(string scene)
        {
            if (scene == "Menu" && _secondPass) yield break;          // the menu scene seen in passing while loading
            if (scene == "Menu")
            {
                yield return Wait(4f);
                bool pressed = Hooks.PressContinue();            // the real button, as a click would
                yield return Wait(0.5f);
                Mod.Log.Msg($"[preview] load-screen-open pressed={pressed} " + SaveScreen.DebugState());
                yield return Wait(4f);
                SaveScreen.DevSelect(0);
                bool load = SaveScreen.DevPress("Load");
                Mod.Log.Msg($"[preview] load-pressed={load}");
            }
            else if (scene == "Main" && _secondPass)
            {
                float wait = Time.realtimeSinceStartup + 240f;
                while (Mod.Instance.CurrentSlot <= 0 && Time.realtimeSinceStartup < wait) yield return null;
                yield return Wait(8f);
                var cam = PlayerSingleton<PlayerCamera>.InstanceExists ? PlayerSingleton<PlayerCamera>.Instance : null;
                Mod.Log.Msg($"[preview] second-game-ready slot={Mod.Instance.CurrentSlot} timeScale={Time.timeScale} "
                          + $"paused={(Singleton<PauseMenu>.InstanceExists && Singleton<PauseMenu>.Instance.IsPaused)} "
                          + $"uiElements={cam?.ActiveUIElementCount} typing={GameInput.IsTyping} saveScreenOpen={SaveScreen.IsOpen} "
                          + $"canMove={(PlayerSingleton<PlayerMovement>.InstanceExists ? PlayerSingleton<PlayerMovement>.Instance.CanMove : false)}");
            }
            else if (scene == "Main")
            {
                float until = Time.realtimeSinceStartup + 120f;
                while (Mod.Instance.CurrentSlot <= 0 && Time.realtimeSinceStartup < until) yield return null;
                yield return Wait(75f);                          // the world is still streaming in for a while after "loaded"
                if (File.Exists(Path.Combine(MelonEnvironment.UserDataDirectory, "PaperTrail.savepoint")))
                {
                    yield return SavePointScenario();
                    yield return Wait(3f);
                }
                if (File.Exists(Path.Combine(MelonEnvironment.UserDataDirectory, "PaperTrail.milestone")))
                {
                    Milestones.Request("New area unlocked: Test Area", true);
                    Milestones.Request("Supplier unlocked: Test Supplier", true);
                    yield return Wait(45f);
                    Mod.Log.Msg($"[preview] milestone-done pending={Milestones.Pending}");
                }
                Singleton<PauseMenu>.Instance.Pause();
                yield return Wait(1.5f);
                SaveScreen.Open(SaveScreen.Mode.Save);
                yield return Wait(1f);
                SaveScreen.DevSelect(0);
                SaveScreen.DevPress("Save");                     // "+ New save" asks for a name
                yield return Wait(1f);
                SaveScreen.DevType("before the cartel war");
                yield return Wait(0.5f);
                Mod.Log.Msg("[preview] dialog-open");
                yield return Wait(4f);
                SaveScreen.DevCancelDialog();
                yield return Wait(0.5f);
                SaveScreen.DevSelect(1);                         // the newest save
                bool load = SaveScreen.DevPress("Load");
                yield return Wait(0.3f);
                Mod.Log.Msg($"[preview] ingame-load-pressed={load}");
                // the confirm page: press its OK
                yield return Wait(1f);
                Mod.Log.Msg("[preview] confirm-open");
                yield return Wait(3f);
                SaveScreen.DevConfirm();
                yield return Wait(0.3f);
                Mod.Log.Msg("[preview] ingame-loading");
                _secondPass = true;
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
