using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppScheduleOne;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.UI.Input;
using Il2CppScheduleOne.UI.MainMenu;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PaperTrail
{
    /// <summary>
    /// The save/load screen: one list of a campaign's saves, newest first, with Load, Save, Overwrite, Rename,
    /// Pin and Delete. Opened from the main menu (load), the pause menu and the safehouse save button (save).
    /// </summary>
    /// <remarks>
    /// Built from the game's own Continue screen - its panel, title, save-slot rows and buttons (see
    /// <see cref="Templates"/>) - on a canvas scaled the way the game's menus are, and opened with the same
    /// scale-and-fade the game's menu screens use.
    /// </remarks>
    internal static class SaveScreen
    {
        public enum Mode { Load, Save }

        private sealed class Entry
        {
            public SnapshotInfo Snap;        // null: the "New save" row, or a backup
            public Backup Backup;            // set on the backups page
            public GameObject Frame;         // the game's blue selection frame
            public RectTransform Row;        // the row itself, to scroll it into view
        }

        private const string UiElement = "PaperTrail";
        private const float PanelWidth = 800f, PanelHeight = 560f, PanelMinHeight = 300f;
        private const float LerpTime = 0.075f, LerpScale = 1.25f;       // the game's MenuScreen open animation

        private static GameObject _root;
        private static RectTransform _panel;
        private static CanvasGroup _group;
        private static float _openedAt;
        private static Mode _mode;
        private static int _slot;
        private static bool _backupsView;        // the slot's backups are on show instead of its saves
        private static Action _afterRoom;        // what goes ahead once the slot has room for another backup
        private static bool _roomOnly;           // the screen was opened only to make that room (a new game or import)
        private static readonly List<Entry> Entries = new List<Entry>();
        private static int _selected = -1;
        private static RectTransform _content;
        private static TextMeshProUGUI _title, _campaign, _status;
        private static SmallBtn _import, _backupsButton, _createBackup, _continue, _prevCampaign, _nextCampaign;
        private static RectTransform _prevArrow, _nextArrow;
        private static GameObject _modal;
        private static RectTransform _listPage, _dialogPage;
        private static ScrollRect _scroll;
        private static RectTransform _viewport;
        private static float _scrollTarget, _scrollWritten;
        private static bool _wheelOurs;                  // we read the wheel (Unity's ScrollRect jumps a notch at a time)
        private static KeyCode _repeatKey;
        private static float _repeatAt;
        private static float _listHeight = PanelHeight;
        private static bool _focusPending;
        private static int _focusFrame;
        private static int _lastWait = -1;
        private static GameObject _inputFrame;
        private static TMP_InputField _modalInput;
        private static Action<string> _modalOk;
        private static GameInput.ExitDelegate _exit;
        private static MenuScreen _hiddenMenuScreen;
        private static bool _tookInput, _saving;

        // A flag of our own: the game destroys the screen's objects itself when a scene changes, and a destroyed
        // object reads as null - which used to leave the rest of the state (buttons disabled while "saving", the
        // Escape listener, the borrowed mouse) behind for the next time the screen was opened.
        private static bool _open;

        public static bool IsOpen => _open;

#if PT_DEV
        /// <summary>For the dev preview: presses a button by its label, as a click does.</summary>
        public static bool DevPress(string label)
        {
            var b = new[] { _load, _save, _overwrite, _rename, _pin, _delete }.FirstOrDefault(x => x != null && x.Text.text == label);
            bool ok = b != null && b.Button.interactable;
            Mod.Log.Msg($"[preview] press '{label}': found={b != null} interactable={b?.Button.interactable} selected={_selected} "
                      + $"rows={Entries.Count} saving={_saving} modal={_modal != null}");
            if (!ok) return false;
            b.Button.onClick.Invoke();
            return true;
        }

        public static void DevSelect(int index) { if (_root != null && index < Entries.Count) Select(index); }

        public static void DevType(string text) { if (_modalInput != null) _modalInput.text = text; }

        public static void DevCancelDialog() => CloseModal();

        public static void DevConfirm() { if (_modal != null) ConfirmModal(); }

        /// <summary>For the dev preview: what the screen looks like from the inside.</summary>
        public static string DevStatus()
            => $"note=\"{_status?.text}\" save={(_save != null && _save.Button.interactable)} overwrite={(_overwrite != null && _overwrite.Button.interactable)} selected={_selected}";

        public static SnapshotInfo DevCurrent => Current;
#endif

#if PT_DEV
        public static string DebugState()
        {
            if (!_open || _root == null) return "closed";
            var canvas = _root.GetComponent<Canvas>();
            return $"active={_root.activeInHierarchy} canvas={(canvas != null && canvas.enabled)} order={canvas?.sortingOrder} "
                 + $"alpha={_group?.alpha} scale={_panel?.localScale.x} rows={Entries.Count} templates={Templates.Ready} "
                 + $"screen={Screen.width}x{Screen.height} rect={_root.GetComponent<RectTransform>().rect.size}";
        }
#endif

        // ---------------------------------------------------------------- open / close

        public static void Open(Mode mode)
        {
            try { OpenInner(mode); }
            catch (Exception e) { Mod.Log.Error("save screen: " + e); CloseIfOpen(); }
        }

        private static void OpenInner(Mode mode)
        {
            CloseIfOpen();
            _saving = false;
            _modal = null;
            _modalInput = null;
            Entries.Clear();
            _selected = -1;
            _mode = mode;
            int playing = Mod.Instance.CurrentSlot;
            if (mode == Mode.Save && playing <= 0) return;
            _slot = playing > 0 ? playing : DefaultSlot();
            _backupsView = false;
            _afterRoom = null;
            _roomOnly = false;
            if (Ui.Font == null) Ui.FindFont();

            // At the main menu the game's own screen steps aside, as when it opens one of its own.
            if (playing <= 0)
                try { _hiddenMenuScreen = MenuScreen.Current; if (_hiddenMenuScreen != null) _hiddenMenuScreen.Close(); }
                catch { _hiddenMenuScreen = null; }
            ShowMenuPrompts();

            ForgetWidgets();            // a screen the game destroyed with its scene never got to CloseIfOpen
            Build();
            _open = true;
            Refresh();

            _exit = (GameInput.ExitDelegate)new Action<ExitAction>(OnExit);
            GameInput.RegisterExitListener(_exit, 100);
            TakeInput();
            Mod.SnapshotTaken += OnSnapshot;
        }

        /// <summary>Drops every reference to the panel's widgets: they are destroyed with the screen, or with the scene.</summary>
        private static void ForgetWidgets()
        {
            _panel = null;
            _group = null;
            _content = null;
            _title = _campaign = _status = null;
            _import = _backupsButton = _createBackup = _continue = _prevCampaign = _nextCampaign = null;
            _prevArrow = _nextArrow = null;
            _listPage = _dialogPage = null;
            _scroll = null;
            _viewport = null;
            _inputFrame = null;
            _modalOk = null;
        }

        public static void CloseIfOpen()
        {
            if (!_open) return;
            _open = false;
            Mod.SnapshotTaken -= OnSnapshot;
            if (_exit != null) { try { GameInput.DeregisterExitListener(_exit); } catch { } _exit = null; }
            GameInput.IsTyping = false;
            ReleaseInput();
            if (_root != null) Object.Destroy(_root);
            _root = null;
            _modal = null;
            _modalInput = null;
            ForgetWidgets();
            Entries.Clear();
            _saving = false;
            HideMenuPrompts();
            ShowOtherScreens();
            if (_hiddenMenuScreen != null)
            {
                try { _hiddenMenuScreen.Open(); } catch { }
                _hiddenMenuScreen = null;
            }
        }

        // The game's menu screens hidden under the panel, with how visible each was.
        private static readonly Dictionary<MenuScreen, (float alpha, bool raycasts)> _covered = new Dictionary<MenuScreen, (float, bool)>();

        /// <summary>
        /// Keeps the game's other menu screens out of sight under the panel. Closing the screen the panel opened from
        /// is not enough: from New Game's name screen, the slot list it came from fades back in behind it.
        /// </summary>
        private static void HideOtherScreens()
        {
            if (Mod.Instance.CurrentSlot > 0 || Time.frameCount % 5 != 0) return;      // a scan of the scene: not every frame
            foreach (var screen in Object.FindObjectsOfType<MenuScreen>())
            {
                var group = screen.Group;
                if (group == null || group.alpha <= 0f) continue;
                if (!_covered.ContainsKey(screen)) _covered[screen] = (group.alpha, group.blocksRaycasts);
                group.alpha = 0f;
                group.blocksRaycasts = false;
            }
        }

        private static void ShowOtherScreens()
        {
            // Only a screen the game still has open comes back; one that was fading out stays gone.
            foreach (var kv in _covered)
                if (kv.Key != null && kv.Key.IsOpen && kv.Key.Group != null)
                {
                    kv.Key.Group.alpha = kv.Value.alpha;
                    kv.Key.Group.blocksRaycasts = kv.Value.raycasts;
                }
            _covered.Clear();
        }

        private static InputPromptsData _menuPrompts;

        /// <summary>
        /// At the main menu, the game's Continue screen's prompts ("Escape  Back"), which the game shows only while one of
        /// its own screens is open. In a game the pause menu's own prompts are still showing.
        /// </summary>
        private static void ShowMenuPrompts()
        {
            try
            {
                if (Mod.Instance.CurrentSlot > 0) return;
                // The game's Continue screen, which this one stands in for (the home screen it replaces has no Back).
                var continueScreen = Object.FindObjectOfType<ContinueScreen>(true);
                _menuPrompts = continueScreen?.State?._defaultInputPrompts ?? _hiddenMenuScreen?.State?._defaultInputPrompts;
                if (_menuPrompts != null && Singleton<InputPromptsManager>.InstanceExists)
                    Singleton<InputPromptsManager>.Instance.LoadModule(_menuPrompts, EInputPromptPosition.BottomLeftMenu);
            }
            catch (Exception e) { Mod.Log.Warning("save screen: menu prompts: " + e.Message); _menuPrompts = null; }
        }

        private static void HideMenuPrompts()
        {
            try
            {
                if (_menuPrompts != null && Singleton<InputPromptsManager>.InstanceExists)
                    Singleton<InputPromptsManager>.Instance.UnloadModule(_menuPrompts);
            }
            catch { }
            _menuPrompts = null;
        }

        private static void OnExit(ExitAction action)
        {
            if (!_open || action.Used) return;
            action.Used = true;
            if (_modal != null) CloseModal();
            else if (_backupsView && !_roomOnly) { _backupsView = false; _afterRoom = null; _selected = -1; Refresh(); }
            else CloseIfOpen();
        }

        /// <summary>In-game, the screen takes the mouse and stops the player looking and walking about.</summary>
        private static void TakeInput()
        {
            if (Mod.Instance.CurrentSlot <= 0 || !PlayerSingleton<PlayerCamera>.InstanceExists) return;
            if (Singleton<PauseMenu>.InstanceExists && Singleton<PauseMenu>.Instance.IsPaused) return;   // it already has
            var cam = PlayerSingleton<PlayerCamera>.Instance;
            cam.AddActiveUIElement(UiElement);
            cam.SetCanLook(false);
            cam.FreeMouse(true);
            if (PlayerSingleton<PlayerMovement>.InstanceExists) PlayerSingleton<PlayerMovement>.Instance.CanMove = false;
            _tookInput = true;
        }

        private static void ReleaseInput()
        {
            if (!_tookInput) return;
            _tookInput = false;
            if (!PlayerSingleton<PlayerCamera>.InstanceExists) return;
            var cam = PlayerSingleton<PlayerCamera>.Instance;
            cam.RemoveActiveUIElement(UiElement);
            cam.SetCanLook(true);
            cam.LockMouse(true);
            if (PlayerSingleton<PlayerMovement>.InstanceExists) PlayerSingleton<PlayerMovement>.Instance.CanMove = true;
        }

        public static void Tick()
        {
            if (!_open) return;
            if (_root == null) { CloseIfOpen(); return; }          // the game destroyed it with a scene change
            HideOtherScreens();
            float t = Mathf.Clamp01((Time.unscaledTime - _openedAt) / LerpTime);
            if (_group != null) _group.alpha = t;
            if (_panel != null) _panel.localScale = Vector3.one * Mathf.Lerp(LerpScale, 1f, t);
            if (_focusPending && _modalInput != null && Time.frameCount >= _focusFrame)
            {
                _focusPending = false;
                _modalInput.ActivateInputField();
                _modalInput.MoveTextEnd(false);
            }
            if (_mode == Mode.Save && _modal == null && !_saving)
            {
                int wait = Mathf.CeilToInt(Mod.CooldownLeft());
                if (wait != _lastWait) { _lastWait = wait; Select(_selected); }
            }
            if (_modal == null && !_saving) NavigateWithKeys();
            EaseScroll();
            bool typing = _modalInput != null && _modalInput.isFocused;
            GameInput.IsTyping = typing;
            if (_inputFrame != null) _inputFrame.SetActive(typing);
            if (_modal != null && _modalOk != null && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)))
                ConfirmModal();
        }

        // ---------------------------------------------------------------- keys and scrolling

        /// <summary>
        /// The arrow keys drive the row selection here. Unity's own button navigation listens to them too and would
        /// highlight a button as well (and Enter would then press it), so these buttons opt out of it.
        /// </summary>
        private static void NoNavigation(Button button)
        {
            if (button == null) return;
            var nav = button.navigation;
            nav.mode = Navigation.Mode.None;
            button.navigation = nav;
        }

        // The list's metrics: the layout below uses them, and scrolling a row into view works out where it is from them
        // (the stripped game build cannot measure it: RectTransformUtility.CalculateRelativeRectTransformBounds is gone).
        private const float RowHeight = 70f, RowSpacing = 5f, ListPadding = 6f;
        private const int ListFade = 16;        // rows fade out over this many pixels at the list's top and bottom edge
        private const float WheelStep = RowHeight + RowSpacing;
        private const float EaseRate = 16f;               // higher is snappier; the wheel lands in about a fifth of a second
        private const int PageRows = 5;

        /// <summary>True on the press, then again at a steady rate while the key is held.</summary>
        private static bool Pressed(KeyCode key)
        {
            if (Input.GetKeyDown(key))
            {
                _repeatKey = key;
                _repeatAt = Time.unscaledTime + 0.4f;
                return true;
            }
            if (_repeatKey == key && Input.GetKey(key) && Time.unscaledTime >= _repeatAt)
            {
                _repeatAt = Time.unscaledTime + 0.06f;
                return true;
            }
            return false;
        }

        /// <summary>Up and Down move the blue selection a row; PageUp and PageDown a page; Home and End the ends.</summary>
        private static void NavigateWithKeys()
        {
            if (Entries.Count == 0) return;
            try
            {
                int to = _selected;
                if (Pressed(KeyCode.UpArrow)) to = _selected < 0 ? 0 : _selected - 1;
                else if (Pressed(KeyCode.DownArrow)) to = _selected + 1;
                else if (Input.GetKeyDown(KeyCode.PageUp)) to = _selected - PageRows;
                else if (Input.GetKeyDown(KeyCode.PageDown)) to = _selected + PageRows;
                else if (Input.GetKeyDown(KeyCode.Home)) to = 0;
                else if (Input.GetKeyDown(KeyCode.End)) to = Entries.Count - 1;
                else
                {
                    // The row's actions from the keyboard: Enter acts on it, as a click does.
                    if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) Activate();
                    else if (Input.GetKeyDown(KeyCode.F2) && Current != null) Rename();
                    else if (Input.GetKeyDown(KeyCode.P) && Current != null) TogglePin();
                    else if (Input.GetKeyDown(KeyCode.Delete) && (Current != null || CurrentBackup != null)) Delete();
                    return;
                }

                to = Mathf.Clamp(to, 0, Entries.Count - 1);
                if (to == _selected) return;
                Select(to);
                ScrollIntoView(to);
            }
            catch (Exception e) { Mod.Log.Warning("save screen: keys: " + e.Message); }
        }

        /// <summary>Slides the list towards where the wheel or the keys want it, instead of jumping a notch at a time.</summary>
        private static void EaseScroll()
        {
            if (_scroll == null || _content == null || _viewport == null) return;
            try
            {
                float now = _content.anchoredPosition.y;
                if (Mathf.Abs(now - _scrollWritten) > 0.5f) _scrollTarget = now;        // the user dragged the list

                if (_wheelOurs && _modal == null)
                {
                    float wheel = 0f;
                    try
                    {
                        if (RectTransformUtility.RectangleContainsScreenPoint(_panel, Input.mousePosition, null))
                            wheel = Input.mouseScrollDelta.y;
                    }
                    catch
                    {
                        // No legacy input here: hand the wheel back to Unity's own scrolling.
                        _wheelOurs = false;
                        _scroll.scrollSensitivity = 25f;
                    }
                    if (_wheelOurs) _scroll.scrollSensitivity = 0f;
                    if (wheel != 0f) _scrollTarget -= wheel * WheelStep;
                }

                float max = Mathf.Max(0f, _content.rect.height - _viewport.rect.height);
                _scrollTarget = Mathf.Clamp(_scrollTarget, 0f, max);
                if (Mathf.Abs(_scrollTarget - now) < 0.3f)
                {
                    if (now != _scrollTarget) _content.anchoredPosition = new Vector2(_content.anchoredPosition.x, _scrollTarget);
                    _scrollWritten = _scrollTarget;
                    return;
                }
                float next = now + (_scrollTarget - now) * (1f - Mathf.Exp(-EaseRate * Time.unscaledDeltaTime));
                _content.anchoredPosition = new Vector2(_content.anchoredPosition.x, next);
                _scrollWritten = next;
            }
            catch (Exception e) { Mod.Log.Warning("save screen: scrolling: " + e.Message); }
        }

        /// <summary>Scrolls the list just far enough that the row is fully in view, with a little room around it.</summary>
        private static void ScrollIntoView(int index)
        {
            if (_content == null || _viewport == null || index < 0 || index >= Entries.Count) return;
            try
            {
                float top = ListFade + index * (RowHeight + RowSpacing);          // distance down from the top of the list
                float bottom = top + RowHeight;
                float view = _viewport.rect.height, margin = ListFade + 2f;     // clear of the faded edge
                float offset = _scrollTarget;
                if (top - margin < offset) offset = top - margin;
                else if (bottom + margin > offset + view) offset = bottom + margin - view;
                _scrollTarget = Mathf.Max(0f, offset);
            }
            catch (Exception e) { Mod.Log.Warning("save screen: scroll into view: " + e.Message); }
        }

        // ---------------------------------------------------------------- campaigns

        private static IEnumerable<int> Campaigns()
            => Enumerable.Range(1, 5).Where(i => SaveInfoOf(i) != null || Store.List(i).Count > 0);

        private static int DefaultSlot()
        {
            var last = LoadManager.LastPlayedGame;
            if (last != null) return last.SaveSlotNumber;
            return Campaigns().DefaultIfEmpty(1).First();
        }

        private static SaveInfo SaveInfoOf(int slot)
        {
            try { var games = LoadManager.SaveGames; return games != null && slot - 1 < games.Length ? games[slot - 1] : null; }
            catch { return null; }
        }

        /// <remarks>
        /// The copies older builds kept in the list before a load replaced the slot are not shown: backups do that now.
        /// </remarks>
        private static List<SnapshotInfo> Snapshots() => Store.List(_slot).Where(s => s.Kind != SaveKind.BeforeRestore).ToList();

        private static string CampaignName(int slot)
        {
            var info = SaveInfoOf(slot);
            string name = info?.OrganisationName;
            if (string.IsNullOrEmpty(name)) name = Store.List(slot).FirstOrDefault()?.Organisation;
            return $"Slot {slot} - {Escape(string.IsNullOrEmpty(name) ? "Empty" : name)}";
        }

        private static void StepCampaign(int step)
        {
            var list = Campaigns().ToList();
            if (list.Count == 0) return;
            int at = list.IndexOf(_slot);
            _slot = list[((at < 0 ? 0 : at) + step + list.Count) % list.Count];
            _selected = -1;
            Refresh();
        }

        // ---------------------------------------------------------------- layout

        private static void Build()
        {
            _root = new GameObject("PaperTrail.SaveScreen");
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;
            var scaler = _root.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = Templates.ScaleMode;
            scaler.referenceResolution = Templates.ReferenceResolution;
            scaler.screenMatchMode = Templates.ScreenMatch;
            scaler.matchWidthOrHeight = Templates.Match;
            _root.AddComponent<GraphicRaycaster>();
            var root = _root.GetComponent<RectTransform>();

            // A full-screen catcher, so nothing behind the screen takes clicks; in-game it also dims the view.
            var catcher = Ui.Box(Ui.Place(Ui.Node("Catcher", root), 0, 0, 1, 1),
                                 new Color(0, 0, 0, Mod.Instance.CurrentSlot > 0 && !Paused() ? 0.45f : 0f));
            catcher.raycastTarget = true;

            _panel = Ui.Node("Panel", root);
            _panel.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            _group = _panel.gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _openedAt = Time.unscaledTime;
            Background(_panel);

            _title = TitleText(_panel, _mode == Mode.Save ? "Save Game" : "Load Game");

            _listPage = Ui.Place(Ui.Node("ListPage", _panel), 0, 0, 1, 1);
            _dialogPage = Ui.Place(Ui.Node("DialogPage", _panel), 0, 0, 1, 1);
            _dialogPage.gameObject.SetActive(false);

            var camp = Ui.Place(Ui.Node("Campaign", _listPage), 0, 1, 1, 1, 60, -92, 60, 54);
            _campaign = Ui.Label(camp, "", 14, Color.white, TextAlignmentOptions.Center);
            _prevArrow = CampaignArrow("Prev");
            _nextArrow = CampaignArrow("Next");
            _prevCampaign = SmallButton(_prevArrow, "<", () => StepCampaign(-1));
            _nextCampaign = SmallButton(_nextArrow, ">", () => StepCampaign(1));
            IconOnButton(_prevCampaign, "arrow", 180f);         // the game's chevron in place of the "<" and ">"
            IconOnButton(_nextCampaign, "arrow", 0f);

            _content = List(Ui.Place(Ui.Node("List", _listPage), 0, 0, 1, 1, 20, 56, 20, 100));

            // What acts on the slot sits in the top corners; what acts on one save is an icon on its row, and clicking a
            // row loads (or saves over) it, as on the game's own Continue screen. No button bar, no Close: Escape goes back.
            _import = SmallButton(Corner("Import", 0f, 92), "Import", ImportSave);
            _createBackup = SmallButton(Corner("CreateBackup", 0f, 124), "Create backup", CreateBackup);
            _backupsButton = SmallButton(Corner("Backups", 1f, 124), "Backups", ShowBackups);
            _continue = SmallButton(Corner("Continue", 1f, 92), "Continue", ContinueAfterRoom);

            _status = HintText(root);
        }

        /// <summary>A button-sized spot in the panel's top-left (x 0) or top-right (x 1) corner.</summary>
        private static RectTransform Corner(string name, float x, float width)
        {
            var rt = Ui.Node(name, _listPage);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(x, 1f);
            rt.sizeDelta = new Vector2(width, 26);
            rt.anchoredPosition = new Vector2(x == 0f ? 24f : -24f, -24f);
            return rt;
        }

        /// <summary>A campaign arrow, placed beside the campaign's name by <see cref="PlaceCampaignArrows"/>.</summary>
        private static RectTransform CampaignArrow(string name)
        {
            var rt = Ui.Node(name, _listPage);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(36, 26);
            return rt;
        }

        /// <summary>The arrows sit just either side of the campaign's name, which they switch, not out at the panel's edges.</summary>
        private static void PlaceCampaignArrows()
        {
            float half = Mathf.Min(_campaign.preferredWidth / 2f + 34f, PanelWidth / 2f - 40f);
            _prevArrow.anchoredPosition = new Vector2(-half, -73);
            _nextArrow.anchoredPosition = new Vector2(half, -73);
        }

        private static bool Paused() => Singleton<PauseMenu>.InstanceExists && Singleton<PauseMenu>.Instance.IsPaused;

        private static void Background(RectTransform panel)
        {
            if (Templates.Background != null)
            {
                var bg = Templates.Make(Templates.Background, panel).GetComponent<RectTransform>();
                Ui.Place(bg, 0, 0, 1, 1);
                bg.SetAsFirstSibling();
            }
            else Ui.Box(panel, new Color(0, 0, 0, 0.784f));
        }

        private static TextMeshProUGUI TitleText(RectTransform panel, string text)
        {
            TextMeshProUGUI t;
            if (Templates.Title != null)
            {
                var go = Templates.Make(Templates.Title, panel);
                t = go.GetComponent<TextMeshProUGUI>();
            }
            else
            {
                t = Ui.Label(Ui.Place(Ui.Node("Title", panel), 0, 1, 1, 1, 0, -55, 0, 5), "", 18, Color.white, TextAlignmentOptions.Center);
            }
            t.text = text;
            return t;
        }

        /// <summary>The game's orange note line under the panel, as under its Continue screen.</summary>
        private static TextMeshProUGUI HintText(RectTransform root)
        {
            TextMeshProUGUI t;
            if (Templates.Hint != null)
            {
                var rt = Templates.Make(Templates.Hint, root).GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(0, -PanelHeight / 2 - 34);
                rt.sizeDelta = new Vector2(PanelWidth, rt.sizeDelta.y);     // wraps under the panel, not past it
                t = rt.GetComponent<TextMeshProUGUI>();
            }
            else
            {
                var rt = Ui.Node("Hint", root);
                rt.sizeDelta = new Vector2(915, 50);
                rt.anchoredPosition = new Vector2(0, -PanelHeight / 2 - 34);
                t = Ui.Label(rt, "", 16, new Color(1f, 0.6f, 0.2f), TextAlignmentOptions.Center);
            }
            t.text = "";
            _hintWarning = t.color;
            return t;
        }

        private static Color _hintWarning = new Color(1f, 0.6f, 0.2f);
        private static readonly Color HintNeutral = new Color32(160, 160, 160, 255);

        private static void Note(string text, bool warning = false)
        {
            if (_status == null) return;
            _status.text = text;
            _status.color = warning ? _hintWarning : HintNeutral;
        }

        private sealed class SmallBtn
        {
            public Button Button;
            public TextMeshProUGUI Text;
            public void SetEnabled(bool on)
            {
                // The game destroys a screen's buttons with its scene. A button that is gone is skipped, and a failed
                // tweak here must never stop what the caller is doing (CoverForLoading runs this on the way to a load).
                try
                {
                    if (Button != null) Button.interactable = on;
                    if (Text != null) Text.alpha = on ? 1f : 0.35f;
                }
                catch (Exception e) { Mod.Log.Warning("save screen: could not update a button: " + e.Message); }
            }
        }

        /// <summary>The game's small rounded button (its Export button).</summary>
        private static SmallBtn SmallButton(RectTransform rt, string label, Action onClick)
        {
            Button button;
            TextMeshProUGUI text;
            if (Templates.SmallButton != null)
            {
                var go = Templates.Make(Templates.SmallButton, rt);
                var brt = go.GetComponent<RectTransform>();
                Ui.Place(brt, 0, 0, 1, 1);
                button = go.GetComponent<Button>();
                text = go.GetComponentInChildren<TextMeshProUGUI>(true);
                text.fontSize = 13;
            }
            else
            {
                var b = Ui.MakeButton(Ui.Place(Ui.Node("Button", rt), 0, 0, 1, 1), label, onClick, 13);
                NoNavigation(b.Button);
                return new SmallBtn { Button = b.Button, Text = b.Text };
            }
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener((UnityAction)onClick);
            NoNavigation(button);
            // The game tints this button black at 30% (its Export button), which shows on the light save-slot cards it
            // sits on but all but vanishes on the dark panel here. A light tint makes it the game's grey button instead.
            var colors = button.colors;
            colors.normalColor = colors.selectedColor = new Color(1f, 1f, 1f, 0.12f);
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.2f);
            colors.pressedColor = new Color(1f, 1f, 1f, 0.28f);
            colors.disabledColor = colors.normalColor;      // a disabled button only fades its text, as the game's do
            button.colors = colors;
            text.text = label;
            return new SmallBtn { Button = button, Text = text };
        }

        private static RectTransform List(RectTransform rt)
        {
            var viewport = Ui.Place(Ui.Node("Viewport", rt), 0, 0, 1, 1);
            // A soft edge, so a row scrolled half out of view fades rather than being cut off by the buttons.
            viewport.gameObject.AddComponent<RectMask2D>().softness = new Vector2Int(0, ListFade);
            Ui.Box(viewport, new Color(0, 0, 0, 0.001f));          // scroll-wheel target between rows
            var content = Ui.Node("Content", viewport);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = RowSpacing;                            // the game's slot spacing
            int pad = (int)ListPadding;
            layout.padding = new RectOffset(pad, pad, ListFade, ListFade);  // room for the selection frame (it overhangs a row by 5), and rows at rest stay clear of the faded edges
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = rt.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = false;                    // dragging follows the pointer exactly; the wheel is eased in Tick
            scroll.scrollSensitivity = 25f;
            _scroll = scroll;
            _viewport = viewport;
            _scrollTarget = _scrollWritten = 0f;
            _wheelOurs = true;
            return content;
        }

        // ---------------------------------------------------------------- list

        private static void Refresh()
        {
            if (!_open || _root == null) return;
            _campaign.text = CampaignName(_slot);
            bool browse = _mode == Mode.Load && !_roomOnly && Campaigns().Count() > 1;      // any campaign, also from inside a game
            _prevCampaign.Button.gameObject.SetActive(browse);
            _nextCampaign.Button.gameObject.SetActive(browse);
            if (browse) PlaceCampaignArrows();

            int backups = Backups.Of(_slot).Count;
            _title.text = _backupsView ? "Backups" : _mode == Mode.Save ? "Save Game" : "Load Game";
            Show(_import, !_backupsView && _mode == Mode.Load);
            Show(_backupsButton, !_backupsView);
            Show(_createBackup, _backupsView);
            Show(_continue, _backupsView && _afterRoom != null);
            if (_backupsButton != null) _backupsButton.Text.text = $"Backups {backups}/{Backups.Limit}";
            _createBackup?.SetEnabled(!_saving && SaveInfoOf(_slot) != null && backups < Backups.Limit);
            _continue?.SetEnabled(backups < Backups.Limit);

            for (int i = _content.childCount - 1; i >= 0; i--) Object.Destroy(_content.GetChild(i).gameObject);
            Entries.Clear();
            if (_backupsView)
            {
                var list = Backups.Of(_slot);
                for (int i = 0; i < list.Count; i++) AddRow(null, i + 1, list[i]);
                if (_afterRoom != null)
                    Note(backups < Backups.Limit
                        ? "There is room now. Continue to go ahead."
                        : $"Slot {_slot} is full ({backups}/{Backups.Limit} backups). Delete one to go ahead, exporting it first to keep it.", backups >= Backups.Limit);
                else
                    Note(list.Count == 0
                        ? "No backups yet. One is made before anything replaces this slot: a new game, an import, or loading an older save."
                        : $"Up to {Backups.Limit} backups are kept per slot. Click one to restore it.");
            }
            else
            {
                if (_mode == Mode.Save) AddRow(null, 0);
                var snaps = Snapshots();
                for (int i = 0; i < snaps.Count; i++) AddRow(snaps[i], i + 1);
                if (Entries.Count == 0 && SaveInfoOf(_slot) != null)
                {
                    AddRow(null, 1);                      // the slot as the game saved it: Paper Trail has no saves of it yet
                    Note("No saves recorded for this campaign yet. Load it, and every save is kept from then on.");
                }
                else if (Entries.Count == 0)
                    Note("Nothing saved in this slot.");
                else if (!_saving)
                    Note(DefaultNote());
            }
            if (_selected >= Entries.Count || _selected < 0) _selected = Entries.Count > 0 ? 0 : -1;
            Select(_selected);
            // Only as tall as the rows need, like the game's own screens, up to a scrolling maximum.
            _listHeight = Mathf.Clamp(100 + 56 + 2 * ListFade + Entries.Count * 75, PanelMinHeight, PanelHeight);
            if (_modal == null) SetPanelHeight(_listHeight);
        }

        private static void Show(SmallBtn button, bool on)
        {
            try { if (button?.Button != null) button.Button.transform.parent.gameObject.SetActive(on); } catch { }
        }

        private static void SetPanelHeight(float height, float width = PanelWidth)
        {
            _panel.sizeDelta = new Vector2(width, height);
            if (_status != null) _status.rectTransform.anchoredPosition = new Vector2(0, -height / 2 - 34);
        }

        private static void AddRow(SnapshotInfo snap, int number, Backup backup = null)
        {
            int index = Entries.Count;
            var entry = new Entry { Snap = snap, Backup = backup };
            GameObject row;
            Button button;
            if (Templates.Row != null)
            {
                row = Templates.Make(Templates.Row, _content);
                var c = row.transform.Find("Container");
                button = c.Find("Button").GetComponent<Button>();
                entry.Frame = c.Find("Button/Selected Frame")?.gameObject;
                var info = c.Find("Info");
                var indexText = row.transform.Find("Index");
                Set(indexText, snap == null && backup == null ? "+" : number.ToString());
                FitIndex(indexText);
                if (backup != null)
                {
                    var newest = backup.Newest;
                    Set(info.Find("Organisation"), $"{Escape(backup.Info.Organisation)}  <color=#A0A0A0>{Escape(backup.Info.BackupReason)}</color>");
                    var worth = info.Find("NetWorth/Text")?.GetComponent<TextMeshProUGUI>();
                    if (worth != null) { worth.text = Money(newest.NetWorth, out var colour); worth.color = colour; }
                    Set(info.Find("Created"), "Played");
                    Set(info.Find("Created/Text"), Store.PlayTime(newest.PlaySeconds));
                    Set(info.Find("LastPlayed"), "Backed up");
                    Set(info.Find("LastPlayed/Text"), Ago(backup.MadeUtc));
                    Set(info.Find("Version"), backup.Saves.Count == 1 ? "One save" : $"Whole campaign, {backup.Saves.Count} saves");
                }
                else if (snap == null)
                {
                    Set(info.Find("Organisation"), _mode == Mode.Save ? "New save" : "Last save  <color=#A0A0A0>as the game saved it</color>");
                    foreach (var n in new[] { "NetWorth", "Created", "LastPlayed" }) info.Find(n)?.gameObject.SetActive(false);
                    Set(info.Find("Version"), "");
                }
                else
                {
                    Set(info.Find("Organisation"), Headline(snap));
                    var worth = info.Find("NetWorth/Text")?.GetComponent<TextMeshProUGUI>();
                    if (worth != null) { worth.text = Money(snap.NetWorth, out var colour); worth.color = colour; }
                    Set(info.Find("Created"), "Played");
                    Set(info.Find("Created/Text"), Store.PlayTime(snap.PlaySeconds));
                    Set(info.Find("LastPlayed"), "Saved");
                    Set(info.Find("LastPlayed/Text"), Ago(snap.CreatedUtc));
                    Set(info.Find("Version"), Tag(snap));
                }
            }
            else
            {
                var rt = Ui.Node("Row", _content);
                Ui.Box(rt, new Color(1, 1, 1, 0.02f));
                row = rt.gameObject;
                button = row.AddComponent<Button>();
                Ui.Label(Ui.Place(Ui.Node("Text", rt), 0, 0, 1, 1, 30, 0, 20, 0),
                         snap == null ? "New save" : $"{Headline(snap)}\n<size=12><color=#A0A0A0>{Tag(snap)}  {Store.PlayTime(snap.PlaySeconds)}  {Ago(snap.CreatedUtc)}</color></size>",
                         18, Color.white);
                var frame = Ui.Place(Ui.Node("Frame", rt), 0, 0, 1, 1);
                Ui.Box(frame, new Color(0, 0.71f, 1f, 0.35f), false);
                entry.Frame = frame.gameObject;
            }
            entry.Row = row.GetComponent<RectTransform>();
            var le = row.GetComponent<LayoutElement>() ?? row.AddComponent<LayoutElement>();
            le.preferredHeight = RowHeight;
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener((UnityAction)new Action(() => Clicked(index)));
            NoNavigation(button);
            Entries.Add(entry);

            // A save's own actions, as the game's icons, in the row's top-right corner (after the row's button, so
            // they take the click and the row does not load).
            if (backup != null)
            {
                RowIcon(entry.Row, 0, "arrow 1", 90f, "Export", () => { Select(index); ExportCurrent(); });
                RowIcon(entry.Row, 1, "trash can", 0f, "Delete", () => { Select(index); Delete(); });
            }
            else if (snap != null)
            {
                RowIcon(entry.Row, 0, "trash can", 0f, "Delete", () => { Select(index); Delete(); });
                RowIcon(entry.Row, 1, "arrow 1", 90f, "Export", () => { Select(index); ExportCurrent(); });
                RowIcon(entry.Row, 2, "star", 0f, "Pin", () => { Select(index); TogglePin(); }, snap.Pinned ? 1f : 0.3f);
                RowIcon(entry.Row, 3, "edit", 0f, "Rename", () => { Select(index); Rename(); });
            }
        }

        /// <summary>One of a row's icons, counted from the right (0 is the rightmost).</summary>
        private static void RowIcon(RectTransform row, int fromRight, string sprite, float rotation, string fallback, Action onClick, float alpha = 0.8f)
        {
            var rt = Ui.Node("Icon " + fallback, row);
            // Pivot in the middle, so a rotated icon turns in its own place rather than about a corner.
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(24, 24);
            rt.anchoredPosition = new Vector2(-26 - fromRight * 32, -22);
            var image = rt.gameObject.AddComponent<Image>();
            image.color = new Color(1f, 1f, 1f, alpha);
            var icon = Templates.Icon(sprite);
            if (icon != null) { image.sprite = icon; image.preserveAspect = true; rt.localEulerAngles = new Vector3(0, 0, rotation); }
            else
            {
                // Without the game's icon, its first letter on a dark chip.
                image.color = new Color(0, 0, 0, 0.3f);
                Ui.Label(rt, fallback.Substring(0, 1), 13, Color.white, TextAlignmentOptions.Center);
            }
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colours = button.colors;
            colours.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            button.colors = colours;
            button.onClick.AddListener((UnityAction)onClick);
            NoNavigation(button);
        }

        /// <summary>The game's icon in place of a small button's text (the campaign chevrons).</summary>
        private static void IconOnButton(SmallBtn button, string sprite, float rotation)
        {
            var icon = Templates.Icon(sprite);
            if (icon == null || button?.Button == null) return;
            button.Text.text = "";
            var rt = Ui.Node("Icon", button.Button.transform);
            rt.sizeDelta = new Vector2(14, 14);
            rt.localEulerAngles = new Vector3(0, 0, rotation);
            var image = rt.gameObject.AddComponent<Image>();
            image.sprite = icon;
            image.preserveAspect = true;
            image.raycastTarget = false;
        }

        /// <summary>
        /// The big faint numeral beside a row is sized for one digit. Left to wrap, "10" put its zero on a second line
        /// over the row below, so it shrinks to fit its box instead.
        /// </summary>
        private static void FitIndex(Transform t)
        {
            var tmp = t != null ? t.GetComponent<TextMeshProUGUI>() : null;
            if (tmp == null) return;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMax = tmp.fontSize;
            tmp.fontSizeMin = Mathf.Max(14f, tmp.fontSize * 0.4f);
        }

        private static void Set(Transform t, string text)
        {
            var tmp = t != null ? t.GetComponent<TextMeshProUGUI>() : null;
            if (tmp != null) tmp.text = text;
        }

        /// <summary>The row's main line: where and when, and the player's name for it.</summary>
        private static string Headline(SnapshotInfo s)
        {
            string when = s.GameDay > 0 ? $"  <color=#A0A0A0>Day {s.GameDay}, {Store.Clock(s.GameTime)}</color>" : "";
            string note = string.IsNullOrEmpty(s.Note) ? "" : $"  <color=#FFE10A>\"{Escape(s.Note)}\"</color>";
            return Escape(s.Location) + when + note;
        }

        private static string Tag(SnapshotInfo s)
        {
            string kind = s.Kind switch
            {
                SaveKind.Auto => $"AutoSave {s.AutoNumber}",
                SaveKind.Sleep => "Sleep",
                SaveKind.BeforeRestore => "Kept",
                SaveKind.Milestone => "Key moment",
                SaveKind.Safeguard => "Safeguard",
                _ => "Manual",
            };
            // Words, not symbols: the game's font has no star or warning sign (they showed as an empty box).
            var parts = new System.Collections.Generic.List<string>();
            if (s.Suspect) parts.Add("Looks incomplete");
            if (s.Pinned) parts.Add("Pinned");
            parts.Add(kind);
            return string.Join(", ", parts);
        }

        /// <summary>Money the way the game's save slots show it: $62.2K in green, $1.2M in gold.</summary>
        private static string Money(float amount, out Color colour)
        {
            colour = new Color32(75, 255, 10, 255);
            if (amount > 1000000f) { colour = new Color32(255, 225, 10, 255); return "$" + Math.Round(amount / 1000000f, 1) + "M"; }
            if (amount > 1000f) return "$" + Math.Round(amount / 1000f, 1) + "K";
            return "$" + Math.Round(amount);
        }

        private static string Ago(DateTime utc)
        {
            var span = DateTime.UtcNow - utc;
            if (span.TotalMinutes < 1) return "Just now";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} min ago";
            if (span.TotalHours < 24 && utc.ToLocalTime().Date == DateTime.Now.Date) return $"Today {utc.ToLocalTime():HH:mm}";
            if (utc.ToLocalTime().Date == DateTime.Now.Date.AddDays(-1)) return $"Yesterday {utc.ToLocalTime():HH:mm}";
            return $"{(int)Math.Max(1, span.TotalDays)} days ago";
        }

        /// <summary>A click acts at once, as on the game's own Continue screen: it loads, saves, or restores a backup.</summary>
        private static void Clicked(int index)
        {
            Select(index);
            ScrollIntoView(index);
            Activate();
        }

        private static void Activate()
        {
            if (_selected < 0 || _selected >= Entries.Count || _saving) return;
            var entry = Entries[_selected];
            if (entry.Backup != null) RestoreBackup(entry.Backup);
            else if (_mode == Mode.Save) { if (entry.Snap == null) NewSave(); else Overwrite(); }
            else Load();
        }

        private static void Select(int index)
        {
            _selected = index;
            for (int i = 0; i < Entries.Count; i++)
                if (Entries[i].Frame != null) Entries[i].Frame.SetActive(i == index);
            var snap = Current;
            bool isSnap = snap != null;
            int wait = _mode == Mode.Save ? Mathf.CeilToInt(Mod.CooldownLeft()) : 0;
            bool atLimit = _mode == Mode.Save && Store.List(_slot).Count(x => x.Kind == SaveKind.Manual) >= Settings.ManualSavesKept;
            bool newRow = _mode == Mode.Save && _selected == 0 && !isSnap;
            if (wait > 0 && !_saving)
                Note($"Wait {wait}s before saving again.", true);
            else if (atLimit && newRow && !_saving)
                Note($"You have {Settings.ManualSavesKept} manual saves, the limit. Delete one, or overwrite a save, to make room.", true);
            else if (_waitShown && !_saving)
                Note(DefaultNote());
            _waitShown = wait > 0 || (atLimit && newRow);
            if (wait <= 0 && isSnap && snap.Suspect && !_saving)
                Note("This save looks incomplete: it has far less in it than the save before it. Older saves were kept.", true);
            else if (wait <= 0 && isSnap && snap.SaveHadErrors && !_saving)
                Note("The game reported errors while making this save.", true);
        }

        private static bool _waitShown;

        private static string DefaultNote()
            => _mode == Mode.Save ? $"The newest {Settings.AutoSavesKept} auto-saves are kept. Manual, sleep and pinned saves are never removed." : "";

        private static SnapshotInfo Current => _selected >= 0 && _selected < Entries.Count ? Entries[_selected].Snap : null;

        private static string Escape(string s) => (s ?? "").Replace("<", "‹").Replace(">", "›");

        // ---------------------------------------------------------------- actions

        private static void Load()
        {
            var snap = Current;
            if (snap == null && SaveInfoOf(_slot) == null) return;
            // The newest snapshot of a slot nothing has saved over since is the slot itself: start it as it is.
            var restore = snap;
            if (snap != null && Store.SlotMatches(_slot, snap)) restore = null;      // already what is on disk
            int slot = _slot;
            bool fromGame = Mod.Instance.CurrentSlot > 0;
            void Go()
            {
                if (fromGame)
                    Modal("Load this save?", "Anything since your last save will be lost.", null, "Load",
                          _ => Mod.LoadNow(slot, restore, true), danger: true);
                else Mod.LoadNow(slot, restore, false);
            }
            // What the slot holds is backed up before the load replaces it; with no room, make some first.
            bool backup = restore != null && ((restore.Kind == SaveKind.Imported && Store.IsOtherCampaign(slot, restore)) || !Store.SlotIsKept(slot));
            if (backup && Backups.Full(slot)) MakeRoomForBackup(slot, Go);
            else Go();
        }

        private static Backup CurrentBackup => _selected >= 0 && _selected < Entries.Count ? Entries[_selected].Backup : null;

        // ---------------------------------------------------------------- backups

        private static void ShowBackups()
        {
            _backupsView = true;
            _selected = -1;
            Refresh();
        }

        private static void CreateBackup()
        {
            if (Backups.Full(_slot)) { Note($"Slot {_slot} has {Backups.Limit} backups already. Delete or export one first.", true); return; }
            try
            {
                var made = Backups.BackUpSave(_slot, "Made by you");
                Note(made != null ? $"Backed up slot {_slot}." : "This slot has no save to back up.", made == null);
            }
            catch (Exception e) { Mod.Log.Warning("create backup: " + e.Message); Note("The backup could not be made; see the log.", true); }
            _selected = 0;
            Refresh();
        }

        private static void RestoreBackup(Backup backup)
        {
            int slot = _slot;
            bool fromGame = Mod.Instance.CurrentSlot > 0;
            var now = SaveInfoOf(slot);
            bool same = Store.Campaign(slot).SaveCreatedTicks != 0 && Store.Campaign(slot).SaveCreatedTicks == backup.Info.SaveCreatedTicks;
            string body = same
                ? $"Slot {slot} goes back to this save. Its current save becomes a backup in its place."
                : now == null
                    ? $"{Escape(backup.Info.Organisation)} goes back into slot {slot}, with all its saves."
                    : $"{Escape(backup.Info.Organisation)} goes back into slot {slot}, with all its saves. {Escape(now.OrganisationName)} becomes a backup in its place, so nothing is lost.";
            if (fromGame) body += " Anything since your last save will be lost.";
            Modal("Restore this backup?", body, null, "Restore", _ => Mod.LoadBackup(backup, slot, fromGame), danger: true);
        }

        /// <summary>
        /// The slot is about to be replaced but already has all the backups it keeps: its backups open so one can be
        /// exported or deleted, and <paramref name="then"/> goes ahead from the Continue button. Opens the screen when a
        /// new game or an import (the game's own screens) asks.
        /// </summary>
        public static void MakeRoomForBackup(int slot, Action then)
        {
            if (!_open) { Open(Mode.Load); _roomOnly = true; }
            if (!_open) return;
            _slot = slot;
            _backupsView = true;
            _afterRoom = then;
            _selected = -1;
            if (_modal != null) CloseModal();
            Refresh();
        }

        private static void ContinueAfterRoom()
        {
            if (_afterRoom == null || Backups.Full(_slot)) return;
            var then = _afterRoom;
            _afterRoom = null;
            if (_roomOnly) CloseIfOpen();
            else { _backupsView = false; _selected = -1; Refresh(); }
            then();
        }

        // ---------------------------------------------------------------- import and export of single saves

        /// <summary>Adds a save file (a zip, as the game's Export makes) to the slot's list. Nothing is replaced until it is loaded.</summary>
        private static void ImportSave()
        {
            try
            {
                string path = SaveImportButton.ShowOpenFileDialog();
                if (string.IsNullOrEmpty(path)) return;
                var imported = Store.ImportSave(_slot, path);
                if (imported == null) { Note("That file holds no Schedule I save.", true); return; }
                _selected = -1;
                Refresh();
                Note($"Imported {Escape(imported.Organisation)} into slot {_slot}'s list. Click it to load it.");
            }
            catch (Exception e) { Mod.Log.Warning("import: " + e.Message); Note("The file could not be imported; see the log.", true); }
        }

        /// <summary>Saves the selected save (or backup) as a zip. A save is in the game's export layout, so the game's Import takes it.</summary>
        private static void ExportCurrent()
        {
            try
            {
                var backup = CurrentBackup;
                var snap = Current;
                if (backup == null && snap == null) return;
                string name = backup != null
                    ? $"{backup.Info.Organisation} backup {backup.MadeUtc.ToLocalTime():yyyy-MM-dd HHmm}"
                    : $"{snap.Organisation} {snap.Location} Day {snap.GameDay}";
                string path = SaveExportButton.ShowSaveFileDialog(SaveManager.MakeFileSafe(name));
                if (string.IsNullOrEmpty(path)) return;
                if (backup != null) Backups.Export(backup, path);
                else if (snap.IsZip) System.IO.File.Copy(snap.ZipPath, path, true);
                else Store.ZipTree(snap.DataFolder, path, "SaveGame_" + _slot);
                Note("Exported to " + Escape(path));
            }
            catch (Exception e) { Mod.Log.Warning("export: " + e.Message); Note("The export failed; see the log.", true); }
        }

        /// <summary>A load that failed before it started: the list is read again (folders may have moved) and the reason shown.</summary>
        public static void ShowFailure(string message)
        {
            if (!_open) return;
            if (_modal != null) CloseModal();
            _selected = -1;
            Refresh();
            Note(message, true);
        }

        /// <summary>
        /// A load has been chosen: nothing more can be clicked, and the screen drops below the game's loading
        /// screen, which fades in over it. The screen stays until the next scene starts, so nothing else - the main
        /// menu least of all - shows in between.
        /// </summary>
        public static void CoverForLoading()
        {
            if (!_open || _root == null) return;
            _hiddenMenuScreen = null;           // the game is loading: nothing to bring back
            if (_modal != null) CloseModal();
            _saving = true;
            foreach (var b in new[] { _import, _backupsButton, _createBackup, _continue, _prevCampaign, _nextCampaign })
                b?.SetEnabled(false);
            try
            {
                var canvas = _root.GetComponent<Canvas>();
                var loading = Singleton<LoadingScreen>.Instance.Canvas;
                if (canvas != null && loading != null) canvas.sortingOrder = loading.sortingOrder - 1;
            }
            catch (Exception e) { Mod.Log.Warning("could not put the save screen under the loading screen: " + e.Message); }
        }

        private static void NewSave() => Ask("New save", "Give it a name, or leave it blank.", "", "Save", note => StartSave(note, null));

        private static void Overwrite()
        {
            var snap = Current;
            if (snap == null) { NewSave(); return; }
            Modal("Overwrite this save?", Escape(Store.Describe(snap)), null, "Overwrite", _ => StartSave(snap.Note, snap), danger: true);
        }

        private static void StartSave(string note, SnapshotInfo replace)
        {
            if (!Mod.Instance.RequestManualSave(string.IsNullOrWhiteSpace(note) ? null : note.Trim(), replace))
            {
                Note("Can't save right now.", true);
                return;
            }
            _saving = true;
            Note("Saving...");
            Select(_selected);
        }

        private static void OnSnapshot(SnapshotInfo info)
        {
            if (!_open || _root == null) return;
            if (_saving && _mode == Mode.Save) { CloseIfOpen(); return; }
            Refresh();
        }

        private static void Rename()
        {
            var snap = Current;
            if (snap == null) return;
            Ask("Rename save", Escape(Store.Describe(snap)), snap.Note, "Rename", note =>
            {
                snap.Note = (note ?? "").Trim();
                Store.Update(snap);
                Refresh();
            });
        }

        private static void TogglePin()
        {
            var snap = Current;
            if (snap == null) return;
            snap.Pinned = !snap.Pinned;
            snap.PinnedByUser = snap.Pinned;
            Store.Update(snap);
            Refresh();
        }

        private static void Delete()
        {
            var backup = CurrentBackup;
            if (backup != null)
            {
                Modal("Delete this backup?", $"{Escape(backup.Info.Organisation)}, {Escape(backup.Info.BackupReason)}, {backup.Saves.Count} save(s).\nThis can't be undone.", null, "Delete", _ =>
                {
                    try { Backups.Delete(backup); } catch (Exception e) { Mod.Log.Warning("delete backup failed: " + e.Message); }
                    _selected = -1;
                    Refresh();
                }, danger: true);
                return;
            }
            var snap = Current;
            if (snap == null) return;
            Modal("Delete this save?", Escape(Store.Describe(snap)) + "\nThis can't be undone.", null, "Delete", _ =>
            {
                try { Store.Delete(snap); } catch (Exception e) { Mod.Log.Warning("delete failed: " + e.Message); }
                Refresh();
            }, danger: true);
        }

        // ---------------------------------------------------------------- dialogs, in the same panel style

        private static void Confirm(string title, string message, string ok, Action<string> onOk) => Modal(title, message, null, ok, onOk);

        private static void Ask(string title, string message, string text, string ok, Action<string> onOk) => Modal(title, message, text ?? "", ok, onOk);

        private const float DialogHeight = 250f, DialogWidth = 440f;

        /// <summary>
        /// A question, shown as a page of the same panel in place of the list, so there is never one menu on top
        /// of another: the list and its buttons step aside and come back when it is answered.
        /// </summary>
        private static void Modal(string title, string message, string input, string ok, Action<string> onOk, bool danger = false)
        {
            CloseModal();
            _modal = _dialogPage.gameObject;
            _listPage.gameObject.SetActive(false);
            _dialogPage.gameObject.SetActive(true);
            if (_status != null) _status.gameObject.SetActive(false);      // the list's hint (a save wait, say) is not the dialog's
            _title.text = title;
            SetPanelHeight(DialogHeight, DialogWidth);

            // The message sits where the game puts the instructions above its name box: small, light grey.
            var msg = Ui.Label(Ui.Place(Ui.Node("Message", _dialogPage), 0, 1, 1, 1, 30, input != null ? -108 : -150, 30, 62), message, 14,
                               new Color32(168, 168, 168, 255), TextAlignmentOptions.Top);
            msg.enableWordWrapping = true;
            if (input != null) _modalInput = InputBox(_dialogPage, input);

            DialogButton(new Vector2(0.5f, 0), new Vector2(-88, 48), "Back", CloseModal, new Color32(113, 113, 113, 255));
            var confirmColour = danger ? new Color32(255, 103, 93, 255) : new Color32(39, 130, 24, 255);
            DialogButton(new Vector2(0.5f, 0), new Vector2(88, 48), ok, ConfirmModal, confirmColour);
            _modalOk = onOk;
            _focusPending = _modalInput != null;
            _focusFrame = Time.frameCount + 2;
        }

        /// <summary>The game's 150x40 dialog button, in its colours: grey for Back, green or red to confirm.</summary>
        private static void DialogButton(Vector2 anchor, Vector2 position, string label, Action onClick, Color colour)
        {
            RectTransform rt;
            Button button;
            TextMeshProUGUI text;
            if (Templates.DialogConfirm != null)
            {
                rt = Templates.Make(Templates.DialogConfirm, _dialogPage).GetComponent<RectTransform>();
                button = rt.GetComponent<Button>();
                text = rt.GetComponentInChildren<TextMeshProUGUI>(true);
                rt.GetComponent<Image>().color = colour;
                var frame = rt.Find("Selected Frame");
                if (frame != null) frame.gameObject.SetActive(false);
            }
            else
            {
                rt = Ui.Node(label, _dialogPage);
                var b = Ui.MakeButton(rt, label, onClick, 18);
                rt.GetComponent<Image>().color = colour;
                button = b.Button;
                text = b.Text;
            }
            rt.anchorMin = rt.anchorMax = anchor;
            rt.sizeDelta = new Vector2(150, 40);
            rt.anchoredPosition = position;
            text.text = label;
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener((UnityAction)onClick);
        }

        /// <summary>The game's own name box, copied from its organisation setup; a plain dark box if that was not found.</summary>
        private static TMP_InputField InputBox(RectTransform parent, string text)
        {
            TMP_InputField input;
            if (Templates.InputField != null)
            {
                var go = Templates.Make(Templates.InputField, parent);
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(0, -6);
                rt.sizeDelta = new Vector2(380, 40);
                input = go.GetComponent<TMP_InputField>();
                var instructions = go.transform.Find("Instructions");
                if (instructions != null) instructions.gameObject.SetActive(false);
                var placeholder = go.transform.Find("Text Area/Placeholder")?.GetComponent<TextMeshProUGUI>();
                if (placeholder != null) placeholder.text = "Name (optional)";
                var frame = go.transform.Find("Selected Frame");
                _inputFrame = frame != null ? frame.gameObject : null;
                if (_inputFrame != null) _inputFrame.SetActive(false);
            }
            else
            {
                var rt = Ui.Place(Ui.Node("Input", parent), 0.5f, 0.5f, 0.5f, 0.5f);
                rt.sizeDelta = new Vector2(380, 40);
                rt.anchoredPosition = new Vector2(0, -6);
                Ui.Box(rt, new Color32(55, 55, 55, 255));
                var area = Ui.Place(Ui.Node("TextArea", rt), 0, 0, 1, 1, 10, 2, 10, 2);
                area.gameObject.AddComponent<RectMask2D>();
                var label = Ui.Label(area, "", 18, Color.white, TextAlignmentOptions.Center);
                label.enableWordWrapping = false;
                input = rt.gameObject.AddComponent<TMP_InputField>();
                input.textViewport = area;
                input.textComponent = label;
            }
            input.characterLimit = 60;
            input.text = text ?? "";
            return input;
        }

        private static void ConfirmModal()
        {
            var ok = _modalOk;
            string text = _modalInput != null ? _modalInput.text : null;
            CloseModal();
            ok?.Invoke(text);
        }

        private static void CloseModal()
        {
            bool was = _modal != null;
            if (was)
            {
                for (int i = _dialogPage.childCount - 1; i >= 0; i--) Object.Destroy(_dialogPage.GetChild(i).gameObject);
                _dialogPage.gameObject.SetActive(false);
                _listPage.gameObject.SetActive(true);
                if (_status != null) _status.gameObject.SetActive(true);
                _title.text = _mode == Mode.Save ? "Save Game" : "Load Game";
                SetPanelHeight(_listHeight);
            }
            _modal = null;
            _modalInput = null;
            _modalOk = null;
            _inputFrame = null;
            _focusPending = false;
            GameInput.IsTyping = false;
        }
    }
}
