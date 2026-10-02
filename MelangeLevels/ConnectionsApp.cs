using System;
using Melange.Core;
using S1API.PhoneApp;
using S1API.UI;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using GameProperty = Il2CppScheduleOne.Property.Property;

namespace Melange.Levels
{
    /// <summary>
    /// "Connections" on the phone: the late-game rewards. Shows Prestige and underbosses to hire, and places earned employee
    /// slots at the player's properties.
    /// </summary>
    public sealed class ConnectionsApp : PhoneApp
    {
        protected override string AppName => "MelangeConnections";
        protected override string AppTitle => "Connections";
        protected override string IconLabel => "Connections";
        protected override string IconFileName => "";
        protected override Sprite IconSprite => _icon ??= MakeIcon();

        private static Sprite _icon;
        private RectTransform _list;

        protected override void OnCreatedUI(GameObject container)
        {
            var bg = UIFactory.Panel("Bg", container.transform, new Color(0.08f, 0.08f, 0.09f), fullAnchor: true);
            UIFactory.TopBar("Top", bg.transform, "Connections", 0.88f, 20, 20, 10, 10);
            var body = UIFactory.Panel("Body", bg.transform, new Color(0, 0, 0, 0), new Vector2(0f, 0f), new Vector2(1f, 0.88f));
            _list = UIFactory.ScrollableVerticalList("List", body.transform, out _);
            Refresh();
            // the UI is built once per scene; keep it current as rewards change (a destroyed list is skipped in Refresh)
            Events.Subscribe<PrestigeChanged>(_ => Refresh());
            Events.Subscribe<UnderbossCandidateUnlocked>(_ => Refresh());
            Events.Subscribe<TierReached>(_ => Refresh());
            Events.Subscribe<MainSceneLoaded>(_ => Refresh());
        }

        protected override void OnPhoneClosed() { }

        /// <summary>Rebuilds the list from the save data (called on opening and after every action).</summary>
        public void Refresh()
        {
            if (_list == null) return;
            UIFactory.ClearChildren(_list);
            var data = MelangeLevelsData.Current;
            if (data == null) { Line("Load a game first.", 18); return; }

            Line($"Prestige: {data.Prestige}", 22, FontStyle.Bold);
            Line("Spend Prestige to make offers they can't refuse. Earned at every Kingpin tier after the first.", 14);
            Line($"Underbosses to hire: {data.UnderbossCandidates}", 18);

            Line($"Employee slots to place: {data.SlotsLeft} (earned {data.EmployeeSlotsEarned} of {Schedule.EmployeeSlotsInAll})", 18, FontStyle.Bold);
            var owned = GameProperty.OwnedProperties;
            if (owned == null || owned.Count == 0) { Line("You own no properties.", 14); }
            else
            {
                for (int i = 0; i < owned.Count; i++)
                {
                    var p = owned[i];
                    if (p == null || !EmployeeSlots.CanExtend(p)) continue;      // e.g. the RV: nowhere for staff to stand
                    data.EmployeeSlotsPlaced.TryGetValue(p.PropertyCode, out int extra);
                    string label = $"{p.PropertyName}: {p.EmployeeCapacity} employees" + (extra > 0 ? $" (+{extra})" : "");
                    if (data.SlotsLeft > 0)
                    {
                        string code = p.PropertyCode;
                        var (_, button, _) = UIFactory.ButtonWithLabel("Place_" + code, label + "   [+1 here]", _list, new Color(0.18f, 0.32f, 0.2f), 520, 44);
                        button.onClick.AddListener((UnityAction)new Action(() => { Rewards.PlaceSlot(code); Refresh(); }));
                    }
                    else Line(label, 14);
                }
            }
            UIFactory.FitContentHeight(_list);
        }

        private void Line(string text, int size, FontStyle style = FontStyle.Normal)
        {
            var t = UIFactory.Text("Line", text, _list, size, TextAnchor.MiddleLeft, style);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        /// <summary>A gold coin with a dark crown band: drawn in code, so there is no file to ship.</summary>
        private static Sprite MakeIcon()
        {
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var gold = new Color(0.86f, 0.68f, 0.22f);
            var dark = new Color(0.16f, 0.12f, 0.06f);
            var bg = new Color(0.10f, 0.10f, 0.12f);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = x - n / 2f + 0.5f, dy = y - n / 2f + 0.5f, r = Mathf.Sqrt(dx * dx + dy * dy);
                    Color c = bg;
                    if (r < n * 0.40f) c = gold;
                    if (r < n * 0.40f && dy > -6 && dy < 10) c = dark;          // the band across the coin
                    if (r >= n * 0.40f && r < n * 0.43f) c = dark;             // the rim
                    tex.SetPixel(x, y, c);
                }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
        }
    }
}
