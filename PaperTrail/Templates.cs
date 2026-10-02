using System;
using Il2CppScheduleOne.UI.MainMenu;
using Il2CppTMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PaperTrail
{
    /// <summary>
    /// The game's own UI pieces, copied from its Continue screen at the main menu and kept for the whole
    /// session, so the save screen is made of the same panel, title, save-slot rows and buttons in-game too.
    /// </summary>
    internal static class Templates
    {
        public static GameObject Background, Title, Row, SmallButton, Hint, DialogBack, DialogConfirm, InputField;
        public static Sprite FrameSprite, RoundedSprite;

        /// <summary>The game's own UI icons the save screen uses (its pencil, bin, star and arrows), by sprite name.</summary>
        private static readonly System.Collections.Generic.Dictionary<string, Sprite> Icons = new System.Collections.Generic.Dictionary<string, Sprite>();
        public static Sprite Icon(string name)
        {
            if (Icons.TryGetValue(name, out var found) && found != null) return found;
            // Captured at the main menu; a scene change can unload it, so look again (the game's UI has them loaded).
            foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
                if (sprite != null && sprite.name == name) { Icons[name] = sprite; return sprite; }
            return null;
        }
        public static UnityEngine.UI.CanvasScaler.ScaleMode ScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        public static Vector2 ReferenceResolution = new Vector2(1920, 1080);
        public static float Match = 0.5f;
        public static UnityEngine.UI.CanvasScaler.ScreenMatchMode ScreenMatch = UnityEngine.UI.CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;

        private static GameObject _holder;

        public static bool Ready => Row != null && Background != null && SmallButton != null && Title != null;

        /// <summary>At the main menu: copy the Continue screen's pieces, once per session.</summary>
        public static void Capture()
        {
            if (Ready) return;
            try
            {
                var cont = Object.FindObjectOfType<ContinueScreen>(true);
                if (cont == null) { Mod.Log.Warning("templates: the game's Continue screen was not found; using plain styling"); return; }

                UnityEngine.UI.CanvasScaler scaler = null;
                for (var p = cont.transform; p != null && scaler == null; p = p.parent)
                    scaler = p.GetComponent<UnityEngine.UI.CanvasScaler>();
                if (scaler != null)
                {
                    ScaleMode = scaler.uiScaleMode;
                    ReferenceResolution = scaler.referenceResolution;
                    Match = scaler.matchWidthOrHeight;
                    ScreenMatch = scaler.screenMatchMode;
                }

                _holder = new GameObject("PaperTrail.Templates");
                _holder.SetActive(false);
                Object.DontDestroyOnLoad(_holder);

                var t = cont.transform;
                Background = Keep(t.Find("Background"));
                Title = Keep(t.Find("Title"));
                Hint = Keep(t.Find("Beta"));
                var slot = t.Find("Container/Slot");
                Row = Keep(slot);
                if (Row != null)
                {
                    Strip(Row.transform.Find("Container/Info/Export"));
                    Strip(Row.transform.Find("Container/Import"));
                    var info = Row.transform.Find("Container/Info");
                    if (info != null) info.gameObject.SetActive(true);
                    RemoveNavigation(Row);
                }
                FrameSprite = slot != null ? slot.Find("Container/Button/Selected Frame")?.GetComponent<UnityEngine.UI.Image>()?.sprite : null;
                var export = slot != null ? slot.Find("Container/Info/Export") : null;
                RoundedSprite = export != null ? export.GetComponent<UnityEngine.UI.Image>()?.sprite : null;
                SmallButton = Keep(export);
                if (SmallButton != null)
                {
                    foreach (var c in SmallButton.GetComponents<SaveExportButton>()) Object.DestroyImmediate(c);
                    RemoveNavigation(SmallButton);
                }
                // The game's own question dialogs: its grey Back and coloured Confirm buttons (from the overwrite
                // confirmation) and the name box from organisation setup.
                var confirm = Object.FindObjectOfType<ConfirmOverwriteScreen>(true);
                if (confirm != null)
                {
                    DialogBack = Keep(confirm.transform.Find("Background/Back"));
                    DialogConfirm = Keep(confirm.transform.Find("Background/Confirm"));
                    foreach (var b in new[] { DialogBack, DialogConfirm }) if (b != null) RemoveNavigation(b);
                }
                var setup = Object.FindObjectOfType<SetupScreen>(true);
                if (setup != null)
                {
                    InputField = Keep(setup.transform.Find("InputField (TMP)"));
                    if (InputField != null)
                    {
                        foreach (var c in InputField.GetComponents<Component>())
                        {
                            string n = c != null ? c.GetIl2CppType().Name : "";
                            if (n == "InputFieldAttachment" || n == "UISelectable_OSK" || n == "UISelectable") Object.DestroyImmediate(c);
                        }
                    }
                }
                foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
                    if (sprite != null && (sprite.name == "edit" || sprite.name == "trash can" || sprite.name == "star"
                                           || sprite.name == "arrow" || sprite.name == "arrow 1"))
                        Icons[sprite.name] = sprite;
                var title = Title != null ? Title.GetComponent<TextMeshProUGUI>() : null;
                if (title != null) Ui.Font = title.font;
                Mod.Log.Msg(Ready ? "templates: using the game's own panel, rows and buttons"
                                  : "templates: some of the game's pieces were missing; using plain styling for those");
            }
            catch (Exception e) { Mod.Log.Warning("templates: " + e.Message); }
        }

        private static GameObject Keep(Transform source)
        {
            if (source == null) return null;
            var copy = Object.Instantiate(source.gameObject, _holder.transform, false);
            copy.name = source.name;
            copy.SetActive(true);
            return copy;
        }

        private static void Strip(Transform t)
        {
            if (t != null) Object.DestroyImmediate(t.gameObject);
        }

        /// <summary>The game's controller-navigation script belongs to its own screens; the copies do without it.</summary>
        private static void RemoveNavigation(GameObject go)
        {
            foreach (var c in go.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                string name = c.GetIl2CppType().Name;
                if (name == "UISelectable" || name == "PlatformConditionalActive") Object.DestroyImmediate(c);
            }
        }

        public static GameObject Make(GameObject template, Transform parent)
        {
            var go = Object.Instantiate(template, parent, false);
            go.SetActive(true);
            return go;
        }
    }
}
