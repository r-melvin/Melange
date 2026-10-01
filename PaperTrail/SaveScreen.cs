using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppScheduleOne;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.UI;
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
            public SnapshotInfo Snap;        // null: the "New save" row
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
        private static readonly List<Entry> Entries = new List<Entry>();
        private static int _selected = -1;
        private static RectTransform _content;
        private static TextMeshProUGUI _title, _campaign, _status;
        private static SmallBtn _load, _save, _overwrite, _rename, _pin, _delete, _prevCampaign, _nextCampaign;
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
        private static float _lastClick;
        private static int _lastClickIndex = -1;

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
            if (Ui.Font == null) Ui.FindFont();

            // At the main menu the game's own screen steps aside, as when it opens one of its own.
            if (playing <= 0)
                try { _hiddenMenuScreen = MenuScreen.Current; if (_hiddenMenuScreen != null) _hiddenMenuScreen.Close(); }
                catch { _hiddenMenuScreen = null; }

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
            _load = _save = _overwrite = _rename = _pin = _delete = _prevCampaign = _nextCampaign = null;
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
            if (_hiddenMenuScreen != null)
            {
                try { _hiddenMenuScreen.Open(); } catch { }
                _hiddenMenuScreen = null;
            }
        }

        private static void OnExit(ExitAction action)
        {
            if (!_open || action.Used) return;
            action.Used = true;
            if (_modal != null) CloseModal();
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

        private const float WheelStep = 75f;              // one row (70) and its spacing
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
                else return;

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
            if (_content == null || _viewport == null || index < 0 || index >= Entries.Count || Entries[index].Row == null) return;
            try
            {
                var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(_content, Entries[index].Row);
                float top = -bounds.max.y, bottom = -bounds.min.y;            // distances down from the top of the list
                float view = _viewport.rect.height, margin = 8f;
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

        private static string CampaignName(int slot)
        {
            var info = SaveInfoOf(slot);
            string name = info?.OrganisationName;
            if (string.IsNullOrEmpty(name)) name = Store.List(slot).FirstOrDefault()?.Organisation;
            return $"{Escape(string.IsNullOrEmpty(name) ? "Empty slot" : name)}  <color=#A0A0A0>Slot {slot}</color>";
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
            _prevCampaign = SmallButton(Ui.Place(Ui.Node("Prev", _listPage), 0, 1, 0, 1, 20, -86, -56, 60), "<", () => StepCampaign(-1));
            _nextCampaign = SmallButton(Ui.Place(Ui.Node("Next", _listPage), 1, 1, 1, 1, -56, -86, 20, 60), ">", () => StepCampaign(1));

            _content = List(Ui.Place(Ui.Node("List", _listPage), 0, 0, 1, 1, 20, 56, 20, 100));

            var bar = Ui.Place(Ui.Node("Buttons", _listPage), 0, 0, 1, 0, 20, 18, 20, -44);
            var row = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 8;
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = false;
            row.childControlHeight = false;
            row.childForceExpandWidth = false;
            SmallBtn Add(string label, Action a)
            {
                var rt = Ui.Node(label, bar);
                rt.sizeDelta = new Vector2(92, 26);
                return SmallButton(rt, label, a);
            }
            if (_mode == Mode.Save)
            {
                _save = Add("Save", NewSave);
                _overwrite = Add("Overwrite", Overwrite);
            }
            else
            {
                // Only Save mode has these. They are static, so a Save screen used earlier in the session (and
                // destroyed with its scene) would still be here, and Select / CoverForLoading would reach for a
                // dead button: the Load screen at the main menu then threw and the load never started.
                _save = null;
                _overwrite = null;
            }
            _load = Add("Load", Load);
            _rename = Add("Rename", Rename);
            _pin = Add("Pin", TogglePin);
            _delete = Add("Delete", Delete);
            Add("Close", CloseIfOpen);

            _status = HintText(root);
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
            var colors = button.colors;
            colors.disabledColor = colors.normalColor;      // a disabled button only fades its text, as the game's do
            button.colors = colors;
            text.text = label;
            return new SmallBtn { Button = button, Text = text };
        }

        private static RectTransform List(RectTransform rt)
        {
            var viewport = Ui.Place(Ui.Node("Viewport", rt), 0, 0, 1, 1);
            viewport.gameObject.AddComponent<RectMask2D>();
            Ui.Box(viewport, new Color(0, 0, 0, 0.001f));          // scroll-wheel target between rows
            var content = Ui.Node("Content", viewport);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 5;                                     // the game's slot spacing
            layout.padding = new RectOffset(6, 6, 6, 6);            // room for the selection frame, which overhangs a row by 5
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
            bool browse = _mode == Mode.Load && Mod.Instance.CurrentSlot <= 0 && Campaigns().Count() > 1;
            _prevCampaign.Button.gameObject.SetActive(browse);
            _nextCampaign.Button.gameObject.SetActive(browse);

            for (int i = _content.childCount - 1; i >= 0; i--) Object.Destroy(_content.GetChild(i).gameObject);
            Entries.Clear();
            if (_mode == Mode.Save) AddRow(null, 0);
            var snaps = Store.List(_slot);
            for (int i = 0; i < snaps.Count; i++) AddRow(snaps[i], i + 1);

            if (Entries.Count == 0)
                Note(SaveInfoOf(_slot) != null
                    ? "No saves recorded for this campaign yet. Load starts it as last saved; from then on every save is kept here."
                    : "Nothing saved in this slot.");
            else if (!_saving)
                Note(_mode == Mode.Save ? $"The newest {Settings.AutoSavesKept} auto-saves are kept. Manual, sleep and pinned saves are never removed." : "");
            if (_selected >= Entries.Count || _selected < 0) _selected = Entries.Count > 0 ? 0 : -1;
            Select(_selected);
            // Only as tall as the saves need, like the game's own screens, up to a scrolling maximum.
            _listHeight = Mathf.Clamp(100 + 56 + 12 + Entries.Count * 75, PanelMinHeight, PanelHeight);
            if (_modal == null) SetPanelHeight(_listHeight);
        }

        private static void SetPanelHeight(float height, float width = PanelWidth)
        {
            _panel.sizeDelta = new Vector2(width, height);
            if (_status != null) _status.rectTransform.anchoredPosition = new Vector2(0, -height / 2 - 34);
        }

        private static void AddRow(SnapshotInfo snap, int number)
        {
            int index = Entries.Count;
            var entry = new Entry { Snap = snap };
            GameObject row;
            Button button;
            if (Templates.Row != null)
            {
                row = Templates.Make(Templates.Row, _content);
                var c = row.transform.Find("Container");
                button = c.Find("Button").GetComponent<Button>();
                entry.Frame = c.Find("Button/Selected Frame")?.gameObject;
                var info = c.Find("Info");
                Set(row.transform.Find("Index"), snap == null ? "+" : number.ToString());
                if (snap == null)
                {
                    Set(info.Find("Organisation"), "New save");
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
            le.preferredHeight = 70;
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener((UnityAction)new Action(() => Clicked(index)));
            NoNavigation(button);
            Entries.Add(entry);
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
            return (s.Suspect ? "\u26A0 " : "") + (s.Pinned ? "\u2605 " : "") + kind;
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

        private static void Clicked(int index)
        {
            bool twice = index == _lastClickIndex && Time.unscaledTime - _lastClick < 0.35f;
            _lastClick = Time.unscaledTime;
            _lastClickIndex = index;
            Select(index);
            ScrollIntoView(index);
            if (!twice) return;
            if (_mode == Mode.Save) { if (Entries[index].Snap == null) NewSave(); else Overwrite(); }
            else Load();
        }

        private static void Select(int index)
        {
            _selected = index;
            for (int i = 0; i < Entries.Count; i++)
                if (Entries[i].Frame != null) Entries[i].Frame.SetActive(i == index);
            var snap = Current;
            bool isSnap = snap != null;
            bool canLoadSlot = !isSnap && _mode == Mode.Load && SaveInfoOf(_slot) != null && Entries.Count == 0;

            _load.SetEnabled(!_saving && (isSnap || canLoadSlot));
            _rename.SetEnabled(!_saving && isSnap);
            _pin.SetEnabled(!_saving && isSnap);
            _delete.SetEnabled(!_saving && isSnap);
            _pin.Text.text = isSnap && snap.Pinned ? "Unpin" : "Pin";
            int wait = _mode == Mode.Save ? Mathf.CeilToInt(Mod.CooldownLeft()) : 0;
            bool atLimit = _mode == Mode.Save && Store.List(_slot).Count(x => x.Kind == SaveKind.Manual) >= Settings.ManualSavesKept;
            bool newRow = _mode == Mode.Save && _selected == 0 && !isSnap;
            _save?.SetEnabled(!_saving && wait <= 0 && !(atLimit && newRow));
            _overwrite?.SetEnabled(!_saving && isSnap && wait <= 0);
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
            if (snap == null && !(Entries.Count == 0 && SaveInfoOf(_slot) != null)) return;
            // The newest snapshot of a slot nothing has saved over since is the slot itself: start it as it is.
            var restore = snap;
            if (snap != null && Store.SlotMatches(_slot, snap)) restore = null;      // already what is on disk
            int slot = _slot;
            if (Mod.Instance.CurrentSlot > 0)
            {
                Modal("Load this save?", "Anything since your last save will be lost.", null, "Load",
                      _ => Mod.LoadNow(slot, restore, true), danger: true);
                return;
            }
            Mod.LoadNow(slot, restore, false);
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
            foreach (var b in new[] { _load, _save, _overwrite, _rename, _pin, _delete, _prevCampaign, _nextCampaign })
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
