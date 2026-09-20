using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Platformer.Survival
{
    /// <summary>
    /// Shared runtime-uGUI builders used by every screen and mini-game, skinned with the
    /// Apogée theme (see ApogeeTheme): Cinzel font, crimson buttons with a gold rim, dark
    /// panels. Everything is anchored by screen fraction so layouts hold on any phone
    /// aspect ratio, and button labels shrink to fit instead of overflowing.
    /// </summary>
    public static class UiKit
    {
        public static Font Font => ApogeeTheme.Font;

        // Semantic colors (kept under their historical names so every screen follows the theme).
        public static readonly Color Parchment = ApogeeTheme.Cream;
        public static readonly Color Gold = ApogeeTheme.Gold;
        public static readonly Color ButtonColor = ApogeeTheme.Crimson;
        public static readonly Color PanelDark = new Color(0.14f, 0.04f, 0.04f, 0.92f);
        public static readonly Color CardColor = new Color(0.30f, 0.09f, 0.07f, 0.92f);
        public static readonly Color Overlay = new Color(0.12f, 0.03f, 0.03f, 0.86f);
        public static readonly Color TextDim = new Color(0.93f, 0.82f, 0.68f);

        public static RectTransform CreateRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        /// <summary>Full-screen flat color layer (overlays, dimmers).</summary>
        public static RectTransform CreatePanel(string name, Transform parent, Color color)
        {
            var rt = CreateRect(name, parent, Vector2.zero, Vector2.one);
            rt.gameObject.AddComponent<Image>().color = color;
            return rt;
        }

        /// <summary>Framed dark panel (gold rim) filling the given anchors.</summary>
        public static RectTransform CreateFrame(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
        {
            var rt = CreateRect(name, parent, anchorMin, anchorMax);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = ApogeeTheme.Panel;
            img.type = Image.Type.Sliced;
            img.raycastTarget = false;
            return rt;
        }

        /// <summary>Full-screen painted backdrop (cover-fitted, never stretched).</summary>
        public static RawImage CreateArtBackdrop(string name, Transform parent, string art, float focusU = 0.5f, float focusV = 0.5f)
        {
            var rt = CreateRect(name, parent, Vector2.zero, Vector2.one);
            var raw = rt.gameObject.AddComponent<RawImage>();
            raw.texture = ApogeeTheme.Art(art);
            raw.raycastTarget = true; // blocks clicks to whatever is behind the screen
            var cover = rt.gameObject.AddComponent<CoverImage>();
            cover.focus = new Vector2(focusU, focusV);
            return raw;
        }

        public static Image CreateImage(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Sprite sprite, Color color, bool preserveAspect = true)
        {
            var rt = CreateRect(name, parent, anchorMin, anchorMax);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.preserveAspect = preserveAspect;
            img.raycastTarget = false;
            return img;
        }

        public static Text CreateText(string name, Transform parent, string content, int size, TextAnchor alignment,
            Vector2 anchorMin, Vector2 anchorMax, Color color)
        {
            var rt = CreateRect(name, parent, anchorMin, anchorMax);
            var text = rt.gameObject.AddComponent<Text>();
            text.font = Font;
            text.fontSize = size;
            text.alignment = alignment;
            text.text = content;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>Pixel-sized text placed relative to an anchor point (for HUD corners).</summary>
        public static Text CreateTextFixed(string name, Transform parent, string content, int size, TextAnchor alignment,
            Vector2 anchor, Vector2 pivot, Vector2 sizeDelta, Vector2 anchoredPos, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.sizeDelta = sizeDelta;
            rt.anchoredPosition = anchoredPos;

            var text = go.AddComponent<Text>();
            text.font = Font;
            text.fontSize = size;
            text.alignment = alignment;
            text.text = content;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>Dark outline so text stays readable over the bright painted sky.</summary>
        public static Text Outlined(Text text, float distance = 2f)
        {
            var o = text.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(0.14f, 0.03f, 0.02f, 0.9f);
            o.effectDistance = new Vector2(distance, -distance);
            return text;
        }

        /// <summary>
        /// Skins a button. No color: the crimson primary style. With a color: a neutral
        /// frame tinted by it (so dark/colored buttons keep their meaning) under an
        /// untinted gold rim.
        /// </summary>
        public static void ApplyButtonStyle(Button btn, Image img, Color? baseColor = null)
        {
            img.type = Image.Type.Sliced;
            if (baseColor == null)
            {
                img.sprite = ApogeeTheme.Button;
                img.color = Color.white;
            }
            else
            {
                img.sprite = ApogeeTheme.FrameFill;
                img.color = baseColor.Value;
                var rim = CreateRect("Rim", img.transform, Vector2.zero, Vector2.one);
                var rimImg = rim.gameObject.AddComponent<Image>();
                rimImg.sprite = ApogeeTheme.FrameBorder;
                rimImg.type = Image.Type.Sliced;
                rimImg.raycastTarget = false;
            }
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.1f, 1.05f);
            colors.pressedColor = new Color(0.75f, 0.7f, 0.68f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.55f, 0.5f, 0.5f, 0.85f);
            colors.fadeDuration = 0.08f;
            btn.colors = colors;
            if (btn.GetComponent<ButtonPop>() == null) btn.gameObject.AddComponent<ButtonPop>();
        }

        public static Button CreateButton(string name, Transform parent, string label, Vector2 anchorMin, Vector2 anchorMax,
            UnityAction onClick, int fontSize = 32, Color? color = null)
        {
            var rt = CreateRect(name, parent, anchorMin, anchorMax);
            var img = rt.gameObject.AddComponent<Image>();
            var btn = rt.gameObject.AddComponent<Button>();
            ApplyButtonStyle(btn, img, color);
            if (onClick != null) btn.onClick.AddListener(onClick);

            var text = CreateText(name + "_Label", rt, label, fontSize, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, ApogeeTheme.Cream);
            var textRt = text.rectTransform;
            textRt.offsetMin = new Vector2(14f, 8f);
            textRt.offsetMax = new Vector2(-14f, -8f);
            FitLabel(text, fontSize);
            Outlined(text, 1.5f);
            return btn;
        }

        /// <summary>Lets a label shrink (never grow) to fit its box.</summary>
        public static void FitLabel(Text text, int maxSize)
        {
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = true;
            text.resizeTextMaxSize = maxSize;
            text.resizeTextMinSize = Mathf.Min(maxSize, 12);
        }

        public static Text ButtonLabel(Button btn) => btn.GetComponentInChildren<Text>();

        /// <summary>A simple filled bar (framed background + fill) whose fill is set through fillAmount.</summary>
        public static Image CreateBar(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Color fillColor)
        {
            var bg = CreateRect(name, parent, anchorMin, anchorMax);
            var bgImg = bg.gameObject.AddComponent<Image>();
            bgImg.sprite = ApogeeTheme.Chip;
            bgImg.type = Image.Type.Sliced;
            bgImg.raycastTarget = false;

            var fillRt = CreateRect("Fill", bg, Vector2.zero, Vector2.one);
            fillRt.offsetMin = new Vector2(5, 5);
            fillRt.offsetMax = new Vector2(-5, -5);
            var fill = fillRt.gameObject.AddComponent<Image>();
            fill.color = fillColor;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = 0;
            fill.sprite = PlaceholderVisuals.Square(Color.white);
            fill.fillAmount = 1f;
            fill.raycastTarget = false;
            return fill;
        }

        public static void SetPanel(GameObject panel, bool active)
        {
            if (panel != null && panel.activeSelf != active) panel.SetActive(active);
        }
    }

    /// <summary>Tiny press feedback: the button squashes a little while held.</summary>
    public class ButtonPop : MonoBehaviour, UnityEngine.EventSystems.IPointerDownHandler, UnityEngine.EventSystems.IPointerUpHandler
    {
        Vector3 baseScale = Vector3.one;
        bool captured;

        public void OnPointerDown(UnityEngine.EventSystems.PointerEventData e)
        {
            if (!captured) { baseScale = transform.localScale; captured = true; }
            var b = GetComponent<Button>();
            if (b != null && !b.interactable) return;
            transform.localScale = baseScale * 0.95f;
        }

        public void OnPointerUp(UnityEngine.EventSystems.PointerEventData e)
        {
            if (captured) transform.localScale = baseScale;
        }

        void OnDisable()
        {
            if (captured) transform.localScale = baseScale;
        }
    }

    /// <summary>
    /// Makes a RawImage cover its rect like CSS "object-fit: cover": the texture keeps its
    /// aspect ratio and is cropped, keeping the focus point (0..1 in texture space) as
    /// central as the crop allows. Recomputed whenever the rect changes size.
    /// </summary>
    [RequireComponent(typeof(RawImage))]
    public class CoverImage : MonoBehaviour
    {
        public Vector2 focus = new Vector2(0.5f, 0.5f);
        RawImage raw;
        Vector2 lastSize;

        void Awake() => raw = GetComponent<RawImage>();
        void OnEnable() => Apply();
        void OnRectTransformDimensionsChange() => Apply();

        void Update()
        {
            var size = ((RectTransform)transform).rect.size;
            if (size != lastSize) Apply();
        }

        public void Apply()
        {
            if (raw == null) raw = GetComponent<RawImage>();
            if (raw == null || raw.texture == null) return;
            var size = ((RectTransform)transform).rect.size;
            lastSize = size;
            if (size.x <= 0f || size.y <= 0f) return;

            float rectAspect = size.x / size.y;
            float texAspect = raw.texture.width / (float)raw.texture.height;
            float w = 1f, h = 1f;
            if (texAspect > rectAspect) w = rectAspect / texAspect; else h = texAspect / rectAspect;
            float x = Mathf.Clamp(focus.x - w / 2f, 0f, 1f - w);
            float y = Mathf.Clamp(focus.y - h / 2f, 0f, 1f - h);
            raw.uvRect = new Rect(x, y, w, h);
        }
    }
}
