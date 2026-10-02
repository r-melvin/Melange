using System;
using HarmonyLib;
using Il2CppScheduleOne.UI.MainMenu;
using Il2CppTMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PaperTrail
{
    /// <summary>
    /// The game's own New Game and Import replace a slot outright. Before they do, the slot's campaign is backed up;
    /// when the slot already has as many backups as it keeps, the backups screen opens to make room first, and the
    /// game's action goes ahead once there is.
    /// </summary>
    internal static class SlotGuard
    {
        public static void Patch(HarmonyLib.Harmony harmony)
        {
            try
            {
                harmony.Patch(AccessTools.Method(typeof(SetupScreen), nameof(SetupScreen.StartGame)),
                    prefix: new HarmonyMethod(typeof(SlotGuard), nameof(BeforeNewGame)));
                harmony.Patch(AccessTools.Method(typeof(ImportScreen), nameof(ImportScreen.Confirm)),
                    prefix: new HarmonyMethod(typeof(SlotGuard), nameof(BeforeImport)));
            }
            catch (Exception e) { Mod.Log.Warning("a new game or import will not back up the slot first: " + e.Message); }
        }

        private static bool BeforeNewGame(SetupScreen __instance)
        {
            // An empty name: the game refuses it itself, so nothing is about to be replaced.
            if (__instance.InputField == null || string.IsNullOrEmpty(__instance.InputField.text)) return true;
            return BeforeReplacing(__instance.slotIndex + 1, "Before a new game", () => __instance.StartGame());
        }

        private static bool BeforeImport(ImportScreen __instance)
            => BeforeReplacing(__instance.slotToOverwrite + 1, "Before an import", () => __instance.Confirm());

        /// <summary>True: let the game go ahead. False: room is being made first, and <paramref name="again"/> runs after.</summary>
        private static bool BeforeReplacing(int slot, string reason, Action again)
        {
            try
            {
                if (slot < 1 || slot > 5 || !Backups.NeededBeforeReplacing(slot)) return true;
                if (Backups.Full(slot))
                {
                    SaveScreen.MakeRoomForBackup(slot, again);        // back here once there is room, and backed up then
                    return false;
                }
                Mod.Announce(Backups.BackUpCampaign(slot, reason), slot);
            }
            catch (Exception e) { Mod.Log.Error("could not back up the slot before replacing it: " + e); }
            return true;
        }
    }

    /// <summary>A short line at the bottom of the screen, for a few seconds, that survives the scene change after it.</summary>
    internal static class Notice
    {
        private static GameObject _root;
        private static float _until;

        public static void Show(string text)
        {
            try
            {
                if (_root == null)
                {
                    _root = new GameObject("PaperTrail.Notice");
                    Object.DontDestroyOnLoad(_root);
                    var canvas = _root.AddComponent<Canvas>();
                    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    canvas.sortingOrder = 32000;                 // above the loading screen
                    var scaler = _root.AddComponent<UnityEngine.UI.CanvasScaler>();
                    scaler.uiScaleMode = Templates.ScaleMode;
                    scaler.referenceResolution = Templates.ReferenceResolution;
                    scaler.matchWidthOrHeight = Templates.Match;
                    var rt = Ui.Place(Ui.Node("Line", _root.transform), 0.5f, 0f, 0.5f, 0f);
                    rt.sizeDelta = new Vector2(1200, 40);
                    rt.anchoredPosition = new Vector2(0, 90);
                    Ui.Label(rt, "", 18, new Color32(255, 225, 10, 255), TextAlignmentOptions.Center);
                }
                _root.GetComponentInChildren<TextMeshProUGUI>().text = text;
                _root.SetActive(true);
                _until = Time.unscaledTime + 6f;
                Mod.Log.Msg(text);
            }
            catch (Exception e) { Mod.Log.Warning("notice: " + e.Message); }
        }

        public static void Tick()
        {
            if (_root != null && _root.activeSelf && Time.unscaledTime > _until) _root.SetActive(false);
        }
    }
}
