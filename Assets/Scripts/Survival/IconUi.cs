using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Platformer.Survival
{
    /// <summary>
    /// The game's currency and resource icons, drawn at runtime in full colour: a gold coin
    /// (the 🪙 of the design notes), a steel gear for materials (⚙️), and a bundle of planks
    /// for the Barricade's débris, plus one pictogram per Barricade trap.
    ///
    /// They exist because emoji cannot be relied on: Unity's legacy UI text does not draw
    /// colour emoji, and the web build has no system font to fall back on at all, so 🪙 and
    /// ⚙️ typed into a label would show up as empty boxes. These are real sprites, so they
    /// look the same on every platform.
    /// </summary>
    public static class GameIcons
    {
        static readonly Dictionary<string, Sprite> cache = new();

        public static Sprite Coin => Get("coin", CoinShade);
        public static Sprite Gear => Get("gear", GearShade);
        public static Sprite Debris => Get("debris", DebrisShade);
        public static Sprite TrapSpikes => Get("trap_spikes", SpikesShade);
        public static Sprite TrapToxic => Get("trap_toxic", ToxicShade);
        public static Sprite TrapWire => Get("trap_wire", WireShade);
        public static Sprite TrapTurret => Get("trap_turret", TurretShade);

        public static Sprite ForToken(char token) => token switch
        {
            'c' => Coin,
            'g' => Gear,
            'd' => Debris,
            _ => null,
        };

        const int Size = 96;

        static Sprite Get(string key, Func<float, float, Color> shade)
        {
            if (cache.TryGetValue(key, out var s) && s != null) return s;
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    Color acc = Color.clear;
                    for (int sy = 0; sy < 2; sy++)
                        for (int sx = 0; sx < 2; sx++)
                        {
                            // normalised coordinates, -1..1, y up
                            float u = ((x + 0.25f + sx * 0.5f) / Size) * 2f - 1f;
                            float v = ((y + 0.25f + sy * 0.5f) / Size) * 2f - 1f;
                            var c = shade(u, v);
                            acc += new Color(c.r * c.a, c.g * c.a, c.b * c.a, c.a);
                        }
                    acc *= 0.25f;
                    px[y * Size + x] = acc.a > 0.001f ? new Color(acc.r / acc.a, acc.g / acc.a, acc.b / acc.a, acc.a) : Color.clear;
                }
            tex.SetPixels(px);
            tex.Apply();
            s = Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), Size);
            cache[key] = s;
            return s;
        }

        static Color Mix(Color a, Color b, float t) => Color.Lerp(a, b, Mathf.Clamp01(t));

        // A gold coin seen face-on: dark rim, bright face with an embossed inner ring and a
        // highlight in the upper left, like the 🪙 emoji.
        static Color CoinShade(float u, float v)
        {
            float r = Mathf.Sqrt(u * u + v * v);
            if (r > 0.94f) return Color.clear;
            var rim = new Color(0.62f, 0.38f, 0.06f);
            var face = new Color(1.00f, 0.80f, 0.22f);
            var deep = new Color(0.86f, 0.58f, 0.10f);
            if (r > 0.80f) return rim;
            if (r > 0.64f && r < 0.71f) return deep;              // embossed ring
            float light = Mathf.Clamp01(0.55f + (-u + v) * 0.35f);
            var c = Mix(deep, face, light);
            float hl = Mathf.Sqrt((u + 0.32f) * (u + 0.32f) + (v - 0.34f) * (v - 0.34f));
            if (hl < 0.16f) c = Mix(c, new Color(1f, 0.97f, 0.78f), 1f - hl / 0.16f);
            return c;
        }

        // A steel gear with eight teeth and an axle hole, like ⚙️.
        static Color GearShade(float u, float v)
        {
            float r = Mathf.Sqrt(u * u + v * v);
            float a = Mathf.Atan2(v, u);
            float teeth = Mathf.Cos(a * 8f);
            float outer = 0.72f + (teeth > 0.35f ? 0.2f : 0f);
            if (r > outer) return Color.clear;
            if (r < 0.24f) return Color.clear;                      // axle hole
            var steel = new Color(0.70f, 0.75f, 0.82f);
            var dark = new Color(0.38f, 0.42f, 0.50f);
            if (r > outer - 0.07f || r < 0.31f) return dark;        // outlines
            float light = Mathf.Clamp01(0.5f + (-u + v) * 0.4f);
            return Mix(dark, steel, 0.55f + light * 0.45f);
        }

        // Three planks lashed together: the Barricade's building currency.
        static Color DebrisShade(float u, float v)
        {
            var wood = new Color(0.66f, 0.44f, 0.24f);
            var dark = new Color(0.36f, 0.22f, 0.12f);
            var rope = new Color(0.86f, 0.76f, 0.52f);
            for (int i = 0; i < 3; i++)
            {
                float cy = -0.46f + i * 0.46f;
                float ang = i == 1 ? -0.12f : 0.1f;
                float x = u, y = v - cy - u * ang;
                if (Mathf.Abs(y) < 0.17f && Mathf.Abs(x) < 0.86f)
                {
                    if (Mathf.Abs(x) > 0.22f && Mathf.Abs(x) < 0.32f) return rope;
                    bool edge = Mathf.Abs(y) > 0.12f || Mathf.Abs(x) > 0.8f;
                    return edge ? dark : Mix(dark, wood, 0.75f + 0.25f * Mathf.Sin(x * 11f + i));
                }
            }
            return Color.clear;
        }

        static bool InTriangle(float u, float v, float cx, float baseY, float halfW, float h)
        {
            if (v < baseY || v > baseY + h) return false;
            float t = (v - baseY) / h;
            return Mathf.Abs(u - cx) <= halfW * (1f - t);
        }

        static Color SpikesShade(float u, float v)
        {
            var steel = new Color(0.78f, 0.80f, 0.84f);
            var dark = new Color(0.35f, 0.36f, 0.40f);
            if (v < -0.62f && v > -0.84f && Mathf.Abs(u) < 0.88f) return dark;         // base plate
            for (int i = 0; i < 3; i++)
            {
                float cx = -0.56f + i * 0.56f;
                if (InTriangle(u, v, cx, -0.62f, 0.26f, 1.3f))
                    return u < cx ? Mix(dark, steel, 0.85f) : steel;
            }
            return Color.clear;
        }

        static Color ToxicShade(float u, float v)
        {
            var green = new Color(0.45f, 0.86f, 0.22f);
            var dark = new Color(0.20f, 0.44f, 0.10f);
            // a drop above a puddle
            float dx = u, dy = v - 0.12f;
            float drop = Mathf.Sqrt(dx * dx + dy * dy);
            bool inDrop = drop < 0.4f || (dy > 0f && Mathf.Abs(dx) < 0.4f * (1f - dy / 0.72f));
            if (inDrop)
            {
                float hl = Mathf.Sqrt((u + 0.13f) * (u + 0.13f) + (v - 0.18f) * (v - 0.18f));
                return hl < 0.1f ? new Color(0.85f, 1f, 0.7f) : (drop > 0.33f && dy <= 0f ? dark : green);
            }
            float pu = u / 0.9f, pv = (v + 0.72f) / 0.16f;
            if (pu * pu + pv * pv < 1f) return pv > 0.3f ? green : dark;
            return Color.clear;
        }

        static Color WireShade(float u, float v)
        {
            var steel = new Color(0.72f, 0.72f, 0.74f);
            var dark = new Color(0.34f, 0.34f, 0.38f);
            // two posts and a zigzag of wire with barbs
            if ((Mathf.Abs(u + 0.78f) < 0.07f || Mathf.Abs(u - 0.78f) < 0.07f) && v > -0.85f && v < 0.6f) return dark;
            for (int k = 0; k < 2; k++)
            {
                float lineY = k == 0 ? 0.25f : -0.25f;
                float wave = lineY + Mathf.Sin(u * 7f + k) * 0.08f;
                if (Mathf.Abs(v - wave) < 0.05f && Mathf.Abs(u) < 0.8f) return steel;
                float barbX = Mathf.Repeat(u + 0.2f * k, 0.4f) - 0.2f;
                if (Mathf.Abs(barbX) < 0.09f && Mathf.Abs(v - wave) < 0.14f && Mathf.Abs(Mathf.Abs(barbX) - Mathf.Abs(v - wave) * 0.6f) < 0.035f && Mathf.Abs(u) < 0.76f)
                    return steel;
            }
            return Color.clear;
        }

        static Color TurretShade(float u, float v)
        {
            var body = new Color(0.52f, 0.56f, 0.62f);
            var dark = new Color(0.28f, 0.30f, 0.36f);
            var gold = new Color(1f, 0.76f, 0.3f);
            if (v < -0.62f && v > -0.86f && Mathf.Abs(u) < 0.7f) return dark;          // base
            if (v >= -0.62f && v < -0.1f && Mathf.Abs(u) < 0.46f)                         // housing
                return Mathf.Abs(u) > 0.39f || v < -0.55f ? dark : body;
            float du = u, dv = v + 0.1f;
            if (dv >= 0f && du * du + dv * dv < 0.36f * 0.36f) return Mathf.Sqrt(du * du + dv * dv) > 0.3f ? dark : body; // dome
            if (Mathf.Abs(v - 0.12f) < 0.08f && u > 0.1f && u < 0.9f) return u > 0.78f ? gold : dark;               // barrel
            return Color.clear;
        }
    }

    /// <summary>
    /// A label that mixes words and icons. Write [c] for a coin, [g] for a gear (materials)
    /// and [d] for débris anywhere in the string, and \n for a new line: "+25 [c]   +5 [g]".
    /// Legacy uGUI text cannot inline images, so the string is laid out as rows of text runs
    /// and icon images; each row is centred, left- or right-aligned as a whole. Setting the
    /// same text again costs nothing, so it can be refreshed every frame from the HUD.
    /// </summary>
    public class IconText : MonoBehaviour
    {
        public int fontSize = 24;
        public Color color = ApogeeTheme.Cream;
        public TextAnchor alignment = TextAnchor.MiddleCenter;
        public float outline;
        public float iconScale = 1.2f;

        string value;

        public string text
        {
            get => value;
            set
            {
                value ??= "";
                if (this.value == value) return;
                this.value = value;
                Rebuild();
            }
        }

        public static IconText Create(string name, Transform parent, string content, int size, TextAnchor alignment,
            Vector2 anchorMin, Vector2 anchorMax, Color color, float outline = 0f)
        {
            var rt = UiKit.CreateRect(name, parent, anchorMin, anchorMax);
            var it = rt.gameObject.AddComponent<IconText>();
            it.fontSize = size;
            it.alignment = alignment;
            it.color = color;
            it.outline = outline;
            var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            v.childAlignment = alignment;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = false;
            v.childForceExpandHeight = false;
            v.spacing = size * 0.1f;
            it.text = content;
            return it;
        }

        /// <summary>Replaces a button's plain label with an icon label filling the same space.</summary>
        public static IconText OnButton(Button button, int size, string content = "")
        {
            var label = UiKit.ButtonLabel(button);
            if (label != null) label.gameObject.SetActive(false);
            var it = Create("IconLabel", button.transform, content, size, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, ApogeeTheme.Cream, 1.5f);
            var rt = (RectTransform)it.transform;
            rt.offsetMin = new Vector2(10f, 6f);
            rt.offsetMax = new Vector2(-10f, -6f);
            return it;
        }

        /// <summary>Fades every run and icon together (banners fade in and out).</summary>
        public void SetAlpha(float alpha)
        {
            foreach (var t in GetComponentsInChildren<Text>())
                t.color = new Color(t.color.r, t.color.g, t.color.b, alpha);
            foreach (var img in GetComponentsInChildren<Image>())
                img.color = new Color(1f, 1f, 1f, alpha);
        }

        bool fitPending;

        /// <summary>
        /// A row wider than the label shrinks to fit rather than spilling out of its panel,
        /// the way FitLabel shrinks a plain label. Checked once, after the layout has run.
        /// </summary>
        void LateUpdate()
        {
            if (!fitPending) return;
            fitPending = false;
            float available = ((RectTransform)transform).rect.width;
            if (available <= 1f) return;
            float pivotX = alignment switch
            {
                TextAnchor.UpperLeft or TextAnchor.MiddleLeft or TextAnchor.LowerLeft => 0f,
                TextAnchor.UpperRight or TextAnchor.MiddleRight or TextAnchor.LowerRight => 1f,
                _ => 0.5f,
            };
            for (int i = 0; i < transform.childCount; i++)
            {
                var row = (RectTransform)transform.GetChild(i);
                if (!row.gameObject.activeSelf) continue;
                float width = LayoutUtility.GetPreferredWidth(row);
                row.pivot = new Vector2(pivotX, 0.5f);
                float scale = width > available ? available / width : 1f;
                row.localScale = new Vector3(scale, scale, 1f);
            }
        }

        void Rebuild()
        {
            fitPending = true;
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                child.SetActive(false); // out of the layout now, destroyed at the end of the frame
                Destroy(child);
            }

            foreach (var line in value.Split('\n'))
            {
                var row = new GameObject("Row", typeof(RectTransform));
                row.transform.SetParent(transform, false);
                var h = row.AddComponent<HorizontalLayoutGroup>();
                h.childAlignment = alignment;
                h.childControlWidth = true;
                h.childControlHeight = true;
                h.childForceExpandWidth = false;
                h.childForceExpandHeight = false;
                h.spacing = fontSize * 0.12f;

                var run = new StringBuilder();
                for (int i = 0; i < line.Length; i++)
                {
                    if (line[i] == '[' && i + 2 < line.Length && line[i + 2] == ']' && GameIcons.ForToken(line[i + 1]) != null)
                    {
                        FlushRun(row.transform, run);
                        AddIcon(row.transform, GameIcons.ForToken(line[i + 1]));
                        i += 2;
                        continue;
                    }
                    run.Append(line[i]);
                }
                FlushRun(row.transform, run);
            }
        }

        void FlushRun(Transform row, StringBuilder run)
        {
            if (run.Length == 0) return;
            var go = new GameObject("Run", typeof(RectTransform));
            go.transform.SetParent(row, false);
            var t = go.AddComponent<Text>();
            t.font = UiKit.Font;
            t.fontSize = fontSize;
            t.color = color;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = true;
            t.raycastTarget = false;
            t.text = run.ToString();
            if (outline > 0f) UiKit.Outlined(t, outline);
            run.Clear();
        }

        void AddIcon(Transform row, Sprite sprite)
        {
            var go = new GameObject("Icon", typeof(RectTransform));
            go.transform.SetParent(row, false);
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
            var le = go.AddComponent<LayoutElement>();
            float s = fontSize * iconScale;
            le.preferredWidth = s;
            le.preferredHeight = s;
            le.minWidth = s;
            le.minHeight = s;
        }
    }

    /// <summary>
    /// A reward popping up in the world - "+1" beside a coin, "+4" beside a gear - rising and
    /// fading like the game's other floating text, but with the icon instead of a word.
    /// </summary>
    public class RewardPopup : MonoBehaviour
    {
        const float Duration = 0.9f;
        float life;
        TextMesh mesh;
        SpriteRenderer icon;
        Color baseColor;

        /// <summary>Several rewards at once stack in a small column: coins over materials.</summary>
        public static void Show(Vector3 position, int coins, int materials, int debris = 0)
        {
            float y = 0f;
            if (coins > 0) { Spawn(position + Vector3.up * y, $"+{coins}", GameIcons.Coin, PlaceholderVisuals.CoinColor); y += 0.42f; }
            if (materials > 0) { Spawn(position + Vector3.up * y, $"+{materials}", GameIcons.Gear, new Color(0.82f, 0.88f, 0.96f)); y += 0.42f; }
            if (debris > 0) Spawn(position + Vector3.up * y, $"+{debris}", GameIcons.Debris, new Color(0.94f, 0.78f, 0.56f));
        }

        static void Spawn(Vector3 position, string text, Sprite sprite, Color color)
        {
            var go = new GameObject("RewardPopup");
            go.transform.position = position + new Vector3(UnityEngine.Random.Range(-0.12f, 0.12f), 0.3f, 0f);

            var textGo = new GameObject("Amount");
            textGo.transform.SetParent(go.transform, false);
            textGo.transform.localPosition = new Vector3(-0.08f, 0f, 0f);
            var mesh = textGo.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.font = UiKit.Font;
            mesh.fontSize = 48;
            mesh.characterSize = 0.055f;
            mesh.anchor = TextAnchor.MiddleRight;
            mesh.alignment = TextAlignment.Right;
            mesh.color = color;
            var mr = textGo.GetComponent<MeshRenderer>();
            mr.sharedMaterial = UiKit.Font.material;
            mr.sortingOrder = 20;

            var iconGo = new GameObject("Icon");
            iconGo.transform.SetParent(go.transform, false);
            iconGo.transform.localPosition = new Vector3(0.12f, 0.01f, 0f);
            iconGo.transform.localScale = Vector3.one * 0.34f;
            var sr = iconGo.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = 20;

            var popup = go.AddComponent<RewardPopup>();
            popup.mesh = mesh;
            popup.icon = sr;
            popup.baseColor = color;
        }

        void Update()
        {
            life += Time.deltaTime;
            float p = life / Duration;
            transform.position += Vector3.up * (1.6f * (1f - p) * Time.deltaTime);
            float a = 1f - Mathf.Clamp01((p - 0.5f) * 2f);
            if (mesh != null) mesh.color = new Color(baseColor.r, baseColor.g, baseColor.b, a);
            if (icon != null) icon.color = new Color(1f, 1f, 1f, a);
            if (life >= Duration) Destroy(gameObject);
        }
    }
}
