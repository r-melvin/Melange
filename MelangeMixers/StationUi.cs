using System;
using System.Collections.Generic;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Effects;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Product;
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.UI.Stations;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Melange.Mixers
{
    /// <summary>
    /// The station screen for a machine. The game's screen is built for one mixer and works out its preview, Begin button and
    /// naming prompt from product + slot-1 mixer, so while it shows a machine this runs after every frame's Update and puts
    /// right what the game wrote: numbered copies of the mixer slot for the extra mixers, the preview of the final result,
    /// the Begin button, and the naming prompt for a new final product. No patches on the screen itself (its private methods
    /// are small enough to be inlined on IL2CPP).
    /// </summary>
    internal static class StationUi
    {
        private static Machine _for;
        private static readonly List<GameObject> _added = new List<GameObject>();
        private static readonly List<ItemSlotUI> _slotUis = new List<ItemSlotUI>();
        private static bool _ourNaming;
        private static IntPtr _promptedOp;
        private static Machine _namingFor;
        private static Il2CppSystem.Action<string> _onNamed;
        private static float _nextCollect;
        private static string _lastExplain;

        public static void Reset() { _for = null; _added.Clear(); _slotUis.Clear(); _ourNaming = false; _promptedOp = IntPtr.Zero; _namingFor = null; }

        public static void LateTick()
        {
            try
            {
                var ui = Singleton<MixingStationInterface>.InstanceExists ? Singleton<MixingStationInterface>.Instance : null;
                var m = ui != null && ui.IsOpen ? Stations.Get(ui.Station) : null;
                if (m == null) { Teardown(); return; }
                if (_for != m) { Teardown(); Build(ui, m); _for = m; }
                Stations.Refresh(m);
                var st = m.Station;
                var op = st.CurrentMixOperation;
                if (op != null && Stations.IsDone(st, op)) Naming(ui, m, op);
                else if (op == null && st.OutputSlot.Quantity == 0) Preview(ui, m);
            }
            catch (Exception e) { Mod.Log.Warning("mixer screen: " + e.Message); Teardown(); }
        }

        // ------------------------------------------------------------------ slots

        /// <summary>Copies of the game's mixer slot for the extra slots, numbered (order matters), placed after slot 1.</summary>
        private static void Build(MixingStationInterface ui, Machine m)
        {
            var template = ui.IngredientSlotUI;
            var parent = template.transform.parent;
            bool laidOut = parent.GetComponent<LayoutGroup>() != null;
            var rect = template.GetComponent<RectTransform>();
            float step = (rect != null ? rect.rect.height : 80f) + 12f;
            Number(ui, template.gameObject, 1);
            for (int i = 0; i < m.Extra.Count; i++)
            {
                var go = UnityEngine.Object.Instantiate(template.gameObject, parent);
                go.name = $"MelangeMixerSlot{i + 2}";
                var slotUi = go.GetComponent<ItemSlotUI>();
                // the copy carries whatever item slot 1 showed; clear it before it takes its own slot
                if (slotUi.ItemContainer != null)
                    for (int c = slotUi.ItemContainer.childCount - 1; c >= 0; c--) UnityEngine.Object.Destroy(slotUi.ItemContainer.GetChild(c).gameObject);
                slotUi.AssignSlot(m.Extra[i]);
                if (laidOut) go.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + i + 1);
                else
                {
                    var r = go.GetComponent<RectTransform>();
                    if (r != null && rect != null) r.anchoredPosition = rect.anchoredPosition + new Vector2(0f, -step * (i + 1));
                }
                Number(ui, go, i + 2);
                _added.Add(go);
                _slotUis.Add(slotUi);
            }
        }

        /// <summary>A small slot number in the slot's corner, a copy of the screen's own label so it uses the game's font.</summary>
        private static void Number(MixingStationInterface ui, GameObject slot, int n)
        {
            try
            {
                const string name = "MelangeSlotNumber";
                var existing = slot.transform.Find(name);
                var label = existing != null ? existing.gameObject : UnityEngine.Object.Instantiate(ui.PreviewLabel.gameObject, slot.transform);
                label.name = name;
                var tmp = label.GetComponent<TextMeshProUGUI>();
                tmp.text = n.ToString();
                tmp.fontSize = 18f;
                tmp.alignment = TextAlignmentOptions.TopLeft;
                tmp.enabled = true;
                var r = label.GetComponent<RectTransform>();
                r.anchorMin = new Vector2(0f, 1f); r.anchorMax = new Vector2(0f, 1f); r.pivot = new Vector2(0f, 1f);
                r.anchoredPosition = new Vector2(4f, -2f);
                r.sizeDelta = new Vector2(30f, 24f);
                var le = label.GetComponent<LayoutElement>() ?? label.AddComponent<LayoutElement>();
                le.ignoreLayout = true;
                if (existing == null && slot == ui.IngredientSlotUI.gameObject) _added.Add(label);   // the game's slot keeps nothing of ours
            }
            catch { }
        }

        private static void Teardown()
        {
            if (_for == null && _added.Count == 0) return;
            foreach (var s in _slotUis) try { s?.ClearSlot(); } catch { }
            foreach (var go in _added) if (go != null) UnityEngine.Object.Destroy(go);
            _added.Clear(); _slotUis.Clear();
            if (_ourNaming)
            {
                try { if (Singleton<NewMixScreen>.Instance.IsOpen) Singleton<NewMixScreen>.Instance.Close(); } catch { }
            }
            _for = null; _ourNaming = false; _promptedOp = IntPtr.Zero; _lastExplain = null;
        }

        // ------------------------------------------------------------------ preview

        private static void Preview(MixingStationInterface ui, Machine m)
        {
            var st = m.Station;
            var chain = Stations.ChainFromSlots(m, st.MixerSlot.ItemInstance?.ID);
            var check = Stations.Check(m, st.ProductSlot.ItemInstance?.ID, chain);
            if (!check.Ready)
            {
                ui.PreviewIcon.enabled = false;
                ui.PreviewLabel.enabled = false;
                ui.PreviewPropertiesLabel.enabled = false;
                ui.UnknownOutputIcon.gameObject.SetActive(false);
                ui.BeginButton.interactable = false;
                if (st.ProductSlot.ItemInstance != null || st.MixerSlot.ItemInstance != null || Any(m))
                {
                    ui.InstructionLabel.text = MixChain.Explain(check, m.Tier.Mixers);
                    ui.InstructionLabel.enabled = true;
                }
                return;
            }
            var res = Stations.Result(st.ProductSlot.ItemInstance.ID, chain);
            if (!res.Valid) return;
            if (res.Known != null)
            {
                ui.PreviewIcon.sprite = res.Known.Icon;
                ui.PreviewIcon.color = Color.white;
                ui.PreviewLabel.text = res.Known.Name;
                ui.UnknownOutputIcon.gameObject.SetActive(false);
                ui.PreviewPropertiesLabel.text = List(res.Effects, null);
            }
            else
            {
                ui.PreviewIcon.sprite = res.Product.Icon;
                ui.PreviewIcon.color = Color.black;
                ui.PreviewLabel.text = "Unknown";
                ui.UnknownOutputIcon.gameObject.SetActive(true);
                ui.PreviewPropertiesLabel.text = List(res.Effects, res.Product.Properties);   // new effects stay hidden, as in the game
            }
            // the game re-enables Begin only when its own two slots change, not when the last extra slot is filled
            ui.BeginButton.interactable = st.CanStartMix();
            ui.PreviewIcon.enabled = true;
            ui.PreviewLabel.enabled = true;
            ui.PreviewPropertiesLabel.enabled = true;
            string order = $"Mixed in slot order: {string.Join(" > ", Numbers(m.Tier.Mixers))}";
            if (ui.InstructionLabel.text != order) ui.InstructionLabel.text = order;
            ui.InstructionLabel.enabled = true;
            if (_lastExplain != res.Product.ID + string.Join(",", chain))
            {
                _lastExplain = res.Product.ID + string.Join(",", chain);
                LayoutRebuilder.ForceRebuildLayoutImmediate(ui.PreviewPropertiesLabel.rectTransform);
            }
        }

        private static bool Any(Machine m)
        {
            foreach (var s in m.Extra) if (s.ItemInstance != null) return true;
            return false;
        }

        private static IEnumerable<string> Numbers(int n) { for (int i = 1; i <= n; i++) yield return i.ToString(); }

        /// <summary>Effects as the game lists them; with <paramref name="shown"/>, effects not in it are a coloured "?".</summary>
        private static string List(Il2CppSystem.Collections.Generic.List<Effect> effects, Il2CppSystem.Collections.Generic.List<Effect> shown)
        {
            var lines = new List<string>();
            foreach (var e in effects)
            {
                string colour = ColorUtility.ToHtmlStringRGBA(e.LabelColor);
                lines.Add(shown == null || shown.Contains(e) ? $"<color=#{colour}>• {e.Name}</color>" : $"<color=#{colour}>• ?</color>");
            }
            return string.Join("\n", lines);
        }

        // ------------------------------------------------------------------ naming

        /// <summary>
        /// A done mix: a known result is collected; a new one is named. The game's own prompt (shown when product + slot-1 mixer
        /// is new) would name the wrong product, so it is closed and replaced, and its handler dropped.
        /// </summary>
        private static void Naming(MixingStationInterface ui, Machine m, Il2CppScheduleOne.ObjectScripts.MixOperation op)
        {
            var screen = Singleton<NewMixScreen>.Instance;
            var st = m.Station;
            var res = Stations.Result(op.ProductID, Stations.RunningChain(m, op));
            if (!res.Valid) return;
            if (res.Known != null)
            {
                if (screen.IsOpen && !_ourNaming) { screen.Close(); st.DiscoveryBox.gameObject.SetActive(false); }
                if (Time.unscaledTime >= _nextCollect) { _nextCollect = Time.unscaledTime + 1f; st.TryCreateOutputItems(); }
                return;
            }
            if (_ourNaming || _promptedOp == op.Pointer) return;
            if (screen.IsOpen) screen.Close();
            _promptedOp = op.Pointer;
            var box = st.DiscoveryBox;
            box.ShowProduct(res.Product, res.Effects);
            box.transform.SetParent(PlayerSingleton<Il2CppScheduleOne.PlayerScripts.PlayerCamera>.Instance.transform);
            box.transform.localPosition = Vector3.forward * (0.4f / Il2CppScheduleOne.UI.CanvasScaler.GlobalScaleFactor);
            box.transform.localRotation = st.DiscoveryBoxRotation;
            float value = ProductManager.CalculateProductValue(res.Product.BasePrice, res.Effects);
            screen.Open(res.Effects, res.Product.DrugType, value);
            _onNamed ??= new Action<string>(OnNamed);
            screen.onMixNamed = _onNamed;                        // replaces the game's handler, which would make product + slot-1 mixer
            _ourNaming = true;
            _namingFor = m;
        }

        private static void OnNamed(string name)
        {
            var m = _namingFor;
            _ourNaming = false;
            _namingFor = null;
            if (m == null || !m.Alive) return;
            try
            {
                Stations.Name(m, name);
                m.Station.DiscoveryBox.gameObject.SetActive(false);
            }
            catch (Exception e) { Mod.Log.Warning("naming a mix: " + e.Message); }
        }
    }
}
