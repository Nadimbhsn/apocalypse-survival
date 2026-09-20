using System.Collections.Generic;
using UnityEngine;

namespace Platformer.Survival
{
    /// <summary>
    /// The "Apogée" art direction shared by every screen and mode: a warm sunset world of
    /// floating islands and crimson autumn foliage (see Assets/Resources/Art). Holds the
    /// palette, the painted art (key art, logo, runner sky, blurred menu backdrop), the
    /// Cinzel font, and procedurally generated skin sprites: 9-sliced crimson buttons with
    /// a gold rim, dark panels, round buttons, floating-island undersides and falling leaves.
    /// </summary>
    public static class ApogeeTheme
    {
        // ---- palette ----------------------------------------------------------------------
        public static readonly Color Cream = new Color(1.00f, 0.93f, 0.80f);
        public static readonly Color Gold = new Color(0.96f, 0.76f, 0.36f);
        public static readonly Color GoldDark = new Color(0.62f, 0.42f, 0.16f);
        public static readonly Color Crimson = new Color(0.66f, 0.14f, 0.10f);
        public static readonly Color CrimsonDark = new Color(0.34f, 0.05f, 0.05f);
        public static readonly Color Maroon = new Color(0.17f, 0.05f, 0.05f);
        public static readonly Color Ink = new Color(0.12f, 0.04f, 0.04f);
        public static readonly Color Leaf = new Color(0.78f, 0.16f, 0.10f);
        /// <summary>Average color of the painted sky around the horizon: the fog color of every 3D layer.</summary>
        public static readonly Color SkyAverage = new Color(0.92f, 0.51f, 0.32f);

        // ---- art ----------------------------------------------------------------------------
        static readonly Dictionary<string, Texture2D> art = new();
        static readonly Dictionary<string, Sprite> artSprites = new();

        public static Texture2D Art(string name)
        {
            if (art.TryGetValue(name, out var t)) return t;
            t = Resources.Load<Texture2D>("Art/" + name);
            art[name] = t;
            return t;
        }

        public static Sprite ArtSprite(string name)
        {
            if (artSprites.TryGetValue(name, out var s)) return s;
            var t = Art(name);
            s = t != null ? Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f) : null;
            artSprites[name] = s;
            return s;
        }

        static Font font;
        public static Font Font
        {
            get
            {
                if (font == null) font = Resources.Load<Font>("Fonts/Cinzel-Bold");
                if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return font;
            }
        }

        // ---- skin sprites ---------------------------------------------------------------------
        static Sprite button, frameFill, frameBorder, panel, round, roundBorder, chip;

        /// <summary>Primary button: crimson vertical gradient, glossy top, double gold rim.</summary>
        public static Sprite Button => button ??= RoundedRect(96, 24, 5f,
            y => Color.Lerp(CrimsonDark, Crimson, Mathf.SmoothStep(0f, 1f, y)) + (y > 0.72f ? new Color(0.08f, 0.04f, 0.02f, 0f) : Color.clear),
            Gold, 30);

        /// <summary>Neutral light fill for tinted buttons/cards (the tint shows its true color); pair with FrameBorder.</summary>
        public static Sprite FrameFill => frameFill ??= RoundedRect(96, 24, 0f, y => Color.Lerp(new Color(0.78f, 0.78f, 0.78f), Color.white, y), Color.clear, 30);

        /// <summary>Gold rim only, drawn over a tinted FrameFill so the rim is never tinted.</summary>
        public static Sprite FrameBorder => frameBorder ??= RoundedRect(96, 24, 4f, _ => Color.clear, Gold, 30);

        /// <summary>Dark translucent panel with a thin gold rim.</summary>
        public static Sprite Panel => panel ??= RoundedRect(96, 22, 2.5f,
            y => Color.Lerp(new Color(0.12f, 0.03f, 0.03f, 0.9f), new Color(0.24f, 0.07f, 0.06f, 0.9f), y), GoldDark, 28);

        /// <summary>Small pill for counters (coins, materials).</summary>
        public static Sprite Chip => chip ??= RoundedRect(64, 30, 2.5f, _ => new Color(0.12f, 0.03f, 0.03f, 0.78f), GoldDark, 31);

        /// <summary>Round button (jump / fire) with a gold ring.</summary>
        public static Sprite Round => round ??= Disc(128, 5f, y => Color.Lerp(CrimsonDark, Crimson, y), Gold);

        /// <summary>Gold ring only (joystick base).</summary>
        public static Sprite RoundBorder => roundBorder ??= Disc(128, 4f, _ => new Color(0.1f, 0.03f, 0.03f, 0.45f), Gold);

        static Sprite RoundedRect(int size, float radius, float borderWidth, System.Func<float, Color> fill, Color border, int slice)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[size * size];
            float half = size / 2f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Signed distance to the rounded rectangle (negative inside).
                    float dx = Mathf.Max(Mathf.Abs(x + 0.5f - half) - (half - radius), 0f);
                    float dy = Mathf.Max(Mathf.Abs(y + 0.5f - half) - (half - radius), 0f);
                    float dist = Mathf.Sqrt(dx * dx + dy * dy) - radius;
                    float coverage = Mathf.Clamp01(0.5f - dist);
                    if (coverage <= 0f) { px[y * size + x] = Color.clear; continue; }

                    Color c = fill(y / (float)(size - 1));
                    if (borderWidth > 0f)
                    {
                        // Outer rim, then a thin dark gap, then a fine inner gold line.
                        float inside = -dist;
                        float rim = Mathf.Clamp01(borderWidth + 0.5f - inside);
                        float inner = Mathf.Clamp01(1f - Mathf.Abs(inside - (borderWidth + 3f)) * 0.9f) * 0.55f;
                        c = Color.Lerp(c, border, Mathf.Max(rim, inner * border.a));
                        if (rim > 0f && border.a > 0f) c.a = Mathf.Max(c.a, rim * border.a);
                    }
                    c.a *= coverage;
                    px[y * size + x] = c;
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                new Vector4(slice, slice, slice, slice));
        }

        static Sprite Disc(int size, float borderWidth, System.Func<float, Color> fill, Color border)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[size * size];
            float r = size / 2f - 1f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size / 2f, size / 2f));
                    float coverage = Mathf.Clamp01(r - d + 0.5f);
                    if (coverage <= 0f) { px[y * size + x] = Color.clear; continue; }
                    Color c = fill(y / (float)(size - 1));
                    float rim = Mathf.Clamp01(borderWidth + 0.5f - (r - d));
                    c = Color.Lerp(c, border, rim);
                    if (rim > 0f) c.a = Mathf.Max(c.a, rim);
                    c.a *= coverage;
                    px[y * size + x] = c;
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }

        // ---- world sprites ------------------------------------------------------------------

        static Sprite verticalFade;

        /// <summary>White, opaque at the bottom fading to transparent at the top (tint it for shades).</summary>
        public static Sprite VerticalFade
        {
            get
            {
                if (verticalFade != null) return verticalFade;
                const int h = 128;
                var tex = new Texture2D(4, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                var px = new Color[4 * h];
                for (int y = 0; y < h; y++)
                {
                    float a = Mathf.SmoothStep(1f, 0f, y / (float)(h - 1));
                    for (int x = 0; x < 4; x++) px[y * 4 + x] = new Color(1f, 1f, 1f, a);
                }
                tex.SetPixels(px);
                tex.Apply();
                verticalFade = Sprite.Create(tex, new Rect(0, 0, 4, h), new Vector2(0.5f, 0.5f), 100f);
                return verticalFade;
            }
        }

        static readonly Sprite[] islands = new Sprite[3];

        /// <summary>
        /// Underside of a floating island: an irregular inverted cone of layered rock, light
        /// under the rim and darker toward the tip, with a few crimson vines hanging off the
        /// edge. Pivot at the top center; 1x1 world unit so it scales to any footprint.
        /// </summary>
        public static Sprite Island(int variant)
        {
            variant = Mathf.Abs(variant) % islands.Length;
            if (islands[variant] != null) return islands[variant];

            const int w = 256, h = 192;
            var rng = new System.Random(1234 + variant * 97);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[w * h];

            // Half-width of the rock at each depth (0 = rim, 1 = tip), jagged, tip off-center.
            float tipX = 0.5f + (float)(rng.NextDouble() - 0.5) * 0.18f;
            var left = new float[h];
            var right = new float[h];
            float jl = 0f, jr = 0f;
            for (int y = 0; y < h; y++)
            {
                float depth = 1f - y / (float)(h - 1); // texture rows go bottom-up: 0 at the rim, 1 at the tip
                float taper = Mathf.Pow(1f - depth, 0.75f);
                if (y % 9 == 0) { jl = (float)(rng.NextDouble() - 0.5) * 0.06f; jr = (float)(rng.NextDouble() - 0.5) * 0.06f; }
                left[y] = Mathf.Lerp(tipX, 0f, taper) + jl * taper;
                right[y] = Mathf.Lerp(tipX, 1f, taper) + jr * taper;
            }

            var top = new Color(0.58f, 0.40f, 0.33f);
            var bottom = new Color(0.20f, 0.11f, 0.13f);
            for (int y = 0; y < h; y++)
            {
                float depth = 1f - y / (float)(h - 1);
                // Strata: horizontal bands of slightly different shade.
                float band = Mathf.PerlinNoise(0.5f + variant * 3.1f, y * 0.09f) * 0.18f - 0.09f;
                for (int x = 0; x < w; x++)
                {
                    float u = x / (float)(w - 1);
                    float l = left[y], r = right[y];
                    float edge = Mathf.Min(u - l, r - u) * w; // pixels from the silhouette edge
                    if (edge < -0.5f) { px[y * w + x] = Color.clear; continue; }

                    float n = Mathf.PerlinNoise(x * 0.05f + variant * 7f, y * 0.05f) * 0.16f - 0.08f;
                    // Side shading: the rock face turns away from the light on the right.
                    float side = (u - (l + r) * 0.5f) / Mathf.Max(0.01f, (r - l) * 0.5f);
                    Color c = Color.Lerp(top, bottom, Mathf.Pow(depth, 0.8f)) * (1f + band + n - side * 0.12f);
                    // A rim of darker earth right under the walkway.
                    if (depth < 0.06f) c *= 0.8f;
                    c.a = Mathf.Clamp01(edge + 0.5f);
                    px[y * w + x] = c;
                }
            }

            // Crimson vines hanging from the rim.
            int vines = 5 + variant * 2;
            for (int v = 0; v < vines; v++)
            {
                int vx = (int)(w * (0.08f + 0.84f * rng.NextDouble()));
                int len = (int)(h * (0.08f + 0.22f * rng.NextDouble()));
                for (int k = 0; k < len; k++)
                {
                    int y = h - 1 - k;
                    int x = vx + (int)(Mathf.Sin(k * 0.3f + v) * 1.5f);
                    for (int t = -1; t <= 1; t++)
                    {
                        int xx = Mathf.Clamp(x + t, 0, w - 1);
                        if (px[y * w + xx].a < 0.5f) continue;
                        float fade = 1f - k / (float)len;
                        px[y * w + xx] = Color.Lerp(px[y * w + xx], new Color(0.62f, 0.12f, 0.09f, 1f), 0.85f * fade);
                    }
                }
            }

            tex.SetPixels(px);
            tex.Apply();
            islands[variant] = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 1f), w);
            // 1 world unit wide; height = h / w units, callers scale y to the depth they want.
            return islands[variant];
        }

        /// <summary>Native height of an island sprite in world units at scale 1 (width is 1).</summary>
        public const float IslandAspect = 192f / 256f;

        static Texture2D leafTexture;

        /// <summary>A small maple-ish leaf (white, tinted by the particle color).</summary>
        public static Texture2D LeafTexture
        {
            get
            {
                if (leafTexture != null) return leafTexture;
                const int s = 32;
                leafTexture = new Texture2D(s, s, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                var px = new Color[s * s];
                for (int y = 0; y < s; y++)
                {
                    for (int x = 0; x < s; x++)
                    {
                        float u = (x + 0.5f) / s * 2f - 1f, v = (y + 0.5f) / s * 2f - 1f;
                        float a = Mathf.Atan2(v, u);
                        float r = Mathf.Sqrt(u * u + v * v);
                        // Five lobes, pointy tips.
                        float lobe = 0.62f + 0.3f * Mathf.Pow(Mathf.Abs(Mathf.Cos(a * 2.5f)), 3f);
                        float cov = Mathf.Clamp01((lobe - r) * s * 0.5f);
                        float vein = Mathf.Abs(u) < 0.06f && v < 0.2f ? 0.75f : 1f;
                        px[y * s + x] = new Color(vein, vein, vein, cov);
                    }
                }
                leafTexture.SetPixels(px);
                leafTexture.Apply();
                return leafTexture;
            }
        }
    }
}
