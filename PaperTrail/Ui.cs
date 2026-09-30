using System;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace PaperTrail
{
    /// <summary>Small builders for runtime uGUI, in the game's font.</summary>
    internal static class Ui
    {
        public static readonly Color Panel = new Color(0f, 0f, 0f, 0.784f);
        public static readonly Color PanelEdge = new Color(0.55f, 0.42f, 0.25f, 0.9f);
        public static readonly Color Text = Color.white;
        public static readonly Color Dim = new Color(0.62f, 0.57f, 0.49f, 1f);
        public static readonly Color Accent = new Color(0.9f, 0.7f, 0.38f, 1f);
        public static readonly Color RowHover = new Color(1f, 1f, 1f, 0.05f);
        public static readonly Color RowSelected = new Color(0.9f, 0.7f, 0.38f, 0.18f);
        public static readonly Color Button = new Color(0f, 0f, 0f, 0.3f);
        public static readonly Color ButtonDisabled = new Color(0.1f, 0.09f, 0.08f, 1f);

        /// <summary>The game's UI font, taken from a button it drew itself.</summary>
        public static TMP_FontAsset Font;

        public static void FindFont(Component near = null)
        {
            if (Font != null) return;
            var text = near != null ? near.GetComponentInChildren<TextMeshProUGUI>(true) : null;
            if (text != null && text.font != null) { Font = text.font; return; }
            var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            if (fonts.Length > 0) Font = fonts[0];
        }

        public static RectTransform Node(string name, Transform parent)
        {
            var go = new GameObject(name);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            return rt;
        }

        /// <summary>Anchors in 0-1 of the parent, plus pixel insets (left, bottom, right, top).</summary>
        public static RectTransform Place(RectTransform rt, float minX, float minY, float maxX, float maxY,
                                          float l = 0, float b = 0, float r = 0, float t = 0)
        {
            rt.anchorMin = new Vector2(minX, minY);
            rt.anchorMax = new Vector2(maxX, maxY);
            rt.offsetMin = new Vector2(l, b);
            rt.offsetMax = new Vector2(-r, -t);
            return rt;
        }

        public static Image Box(RectTransform rt, Color color, bool raycast = true)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        public static TextMeshProUGUI Label(RectTransform parent, string text, float size, Color color,
                                            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            var rt = Place(Node("Text", parent), 0, 0, 1, 1);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (Font != null) t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.enableWordWrapping = true;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.raycastTarget = false;
            t.richText = true;
            return t;
        }

        public sealed class Btn
        {
            public Button Button;
            public Image Background;
            public TextMeshProUGUI Text;

            public void SetEnabled(bool on)
            {
                Button.interactable = on;
                Background.color = on ? Ui.Button : ButtonDisabled;
                Text.color = on ? Ui.Text : new Color(0.4f, 0.37f, 0.33f, 1f);
            }
        }

        public static Btn MakeButton(RectTransform rt, string label, Action onClick, float size = 26)
        {
            var btn = new Btn { Background = Box(rt, Button) };
            btn.Button = rt.gameObject.AddComponent<Button>();
            var colors = btn.Button.colors;
            colors.highlightedColor = new Color(1.35f, 1.3f, 1.2f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            btn.Button.colors = colors;
            btn.Button.targetGraphic = btn.Background;
            btn.Button.onClick.AddListener((UnityAction)onClick);
            btn.Text = Label(rt, label, size, Text, TextAlignmentOptions.Center);
            return btn;
        }

        /// <summary>A vertical scrolling list; add rows to the returned content.</summary>
        public static RectTransform ScrollList(RectTransform rt, out ScrollRect scroll)
        {
            Box(rt, new Color(0, 0, 0, 0.25f));
            var viewport = Place(Node("Viewport", rt), 0, 0, 1, 1, 6, 6, 6, 6);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = Node("Content", viewport);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.offsetMin = new Vector2(0, 0);
            content.offsetMax = new Vector2(0, 0);
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 2;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            var fit = content.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll = rt.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;
            return content;
        }

        public static TMP_InputField Input(RectTransform rt, string text, int limit)
        {
            Box(rt, new Color(0, 0, 0, 0.45f));
            var area = Place(Node("TextArea", rt), 0, 0, 1, 1, 14, 6, 14, 6);
            area.gameObject.AddComponent<RectMask2D>();
            var label = Label(area, "", 28, Text);
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Overflow;
            var input = rt.gameObject.AddComponent<TMP_InputField>();
            input.textViewport = area;
            input.textComponent = label;
            if (Font != null) input.fontAsset = Font;
            input.characterLimit = limit;
            input.text = text ?? "";
            return input;
        }
    }
}
