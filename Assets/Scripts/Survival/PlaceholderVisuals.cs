using System.Collections.Generic;
using UnityEngine;

namespace Platformer.Survival
{
    /// <summary>
    /// Generates and caches simple colored circle/square sprites at runtime so every
    /// survival-mode gameplay object can render without needing hand-authored art assets.
    /// </summary>
    public static class PlaceholderVisuals
    {
        const int Resolution = 64;
        static readonly Dictionary<(Color32 color, bool circle), Sprite> cache = new();

        // Apogée palette: warm sunset sky, rust-brown stone, crimson autumn accents (see ApogeeTheme).
        public static readonly Color ZombieColor = new Color(0.30f, 0.36f, 0.22f);
        public static readonly Color RunnerZombieColor = new Color(0.40f, 0.29f, 0.20f);
        public static readonly Color MineColor = new Color(0.72f, 0.24f, 0.09f);
        public static readonly Color CoinColor = new Color(1.00f, 0.84f, 0.30f);
        public static readonly Color MaterialColor = new Color(0.62f, 0.72f, 0.86f);
        public static readonly Color ProjectileColor = new Color(0.92f, 0.70f, 0.28f);
        public static readonly Color ExplosionColor = new Color(0.82f, 0.34f, 0.09f);
        public static readonly Color VehicleColor = new Color(0.34f, 0.32f, 0.30f);
        public static readonly Color GroundColor = new Color(0.36f, 0.22f, 0.18f);
        public static readonly Color SkyColor = new Color(0.92f, 0.51f, 0.32f);
        public static readonly Color AshColor = new Color(0.80f, 0.20f, 0.10f, 0.9f);
        public static readonly Color SpitColor = new Color(0.55f, 0.75f, 0.22f);
        public static readonly Color RuinColor = new Color(0.45f, 0.24f, 0.2f, 0.9f);
        public static readonly Color ToxicColor = new Color(0.45f, 0.70f, 0.18f, 0.85f);
        public static readonly Color SpikeColor = new Color(0.42f, 0.38f, 0.34f);
        public static readonly Color MedkitColor = new Color(0.85f, 0.25f, 0.22f);
        public static readonly Color BruteColor = new Color(0.55f, 0.32f, 0.40f);
        public static readonly Color StoneColor = new Color(0.30f, 0.28f, 0.26f);

        static Sprite padlockCache;

        /// <summary>A chunky padlock silhouette (body + shackle) for locked character cards.</summary>
        public static Sprite Padlock()
        {
            if (padlockCache != null) return padlockCache;

            const int w = 48, h = 64;
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color32[w * h];
            Color32 body = new Color32(225, 200, 150, 255);
            Color32 hole = new Color32(60, 40, 30, 255);

            FillRect(pixels, w, h, 4, 2, 40, 30, body);          // body
            FillCircle(pixels, w, h, 24, 44, 15, body);           // shackle outer
            FillCircle(pixels, w, h, 24, 44, 9, new Color32(0, 0, 0, 0)); // shackle inner
            FillRect(pixels, w, h, 9, 30, 30, 14, new Color32(0, 0, 0, 0)); // open the shackle bottom
            FillRect(pixels, w, h, 9, 30, 6, 8, body);            // shackle legs
            FillRect(pixels, w, h, 33, 30, 6, 8, body);
            FillCircle(pixels, w, h, 24, 19, 4, hole);            // keyhole
            FillRect(pixels, w, h, 22, 8, 5, 10, hole);

            texture.SetPixels32(pixels);
            texture.Apply();

            padlockCache = Sprite.Create(texture, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), h);
            return padlockCache;
        }

        static Sprite spikesCache;

        /// <summary>A row of three jagged rubble spikes on a transparent background, exactly 1x1 world unit so it scales to any footprint.</summary>
        public static Sprite Spikes()
        {
            if (spikesCache != null) return spikesCache;

            const int w = 48, h = 48;
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color32[w * h];
            Color32 dark = new Color(SpikeColor.r * 0.6f, SpikeColor.g * 0.6f, SpikeColor.b * 0.6f, 1f);
            Color32 light = SpikeColor;
            for (int spike = 0; spike < 3; spike++)
            {
                int cx = 8 + spike * 16;
                int height = spike == 1 ? h - 1 : h - 12;
                for (int y = 0; y < height; y++)
                {
                    int halfWidth = Mathf.RoundToInt(7.5f * (1f - y / (float)height));
                    for (int x = cx - halfWidth; x <= cx + halfWidth; x++)
                        SetPixel(pixels, w, h, x, y, x < cx ? light : dark);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            spikesCache = Sprite.Create(texture, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), w);
            return spikesCache;
        }

        public static Sprite Circle(Color color) => Get(color, true);

        static readonly Dictionary<Color32, Sprite> rimCache = new();

        /// <summary>A disc with a dark rim and a small highlight, so pickups read against the bright sky.</summary>
        public static Sprite RimCircle(Color color)
        {
            Color32 key = color;
            if (rimCache.TryGetValue(key, out var cached)) return cached;
            const int s = 64;
            var texture = new Texture2D(s, s, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[s * s];
            var rim = new Color(color.r * 0.35f, color.g * 0.25f, color.b * 0.2f, 1f);
            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    float dx = x + 0.5f - s / 2f, dy = y + 0.5f - s / 2f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float cov = Mathf.Clamp01(s / 2f - 1f - d + 0.5f);
                    if (cov <= 0f) { px[y * s + x] = Color.clear; continue; }
                    Color c = d > s / 2f - 7f ? rim : color;
                    float hl = Mathf.Clamp01(1f - Vector2.Distance(new Vector2(dx, dy), new Vector2(-8f, 9f)) / 10f);
                    c = Color.Lerp(c, Color.white, hl * 0.7f);
                    c.a = cov;
                    px[y * s + x] = c;
                }
            }
            texture.SetPixels(px);
            texture.Apply();
            var sprite = Sprite.Create(texture, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), s);
            rimCache[key] = sprite;
            return sprite;
        }
        public static Sprite Square(Color color) => Get(color, false);

        static Sprite gradientCache;

        /// <summary>A simple top-to-bottom gradient used as an opaque backdrop for menu screens.</summary>
        public static Sprite VerticalGradient(Color top, Color bottom)
        {
            if (gradientCache != null) return gradientCache;

            const int h = 256;
            var texture = new Texture2D(4, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color32[4 * h];
            for (int y = 0; y < h; y++)
            {
                Color c = Color.Lerp(bottom, top, y / (float)(h - 1));
                for (int x = 0; x < 4; x++) pixels[y * 4 + x] = c;
            }
            texture.SetPixels32(pixels);
            texture.Apply();

            gradientCache = Sprite.Create(texture, new Rect(0, 0, 4, h), new Vector2(0.5f, 0.5f), h);
            return gradientCache;
        }

        static Sprite vignetteCache;

        /// <summary>
        /// A radial gradient (transparent center, dark opaque edges) used as a permanent
        /// full-screen overlay so the HUD reads as a grimy, vignetted viewfinder rather
        /// than a clean UI.
        /// </summary>
        public static Sprite Vignette()
        {
            if (vignetteCache != null) return vignetteCache;

            const int res = 256;
            var texture = new Texture2D(res, res, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color32[res * res];
            Vector2 center = new Vector2(res / 2f, res / 2f);
            float maxDist = center.magnitude;
            Color edge = new Color(0.22f, 0.04f, 0.03f, 0.5f);

            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center) / maxDist;
                    float alpha = Mathf.Clamp01((dist - 0.3f) / 0.7f);
                    alpha = alpha * alpha * edge.a;
                    var c = edge;
                    c.a = alpha;
                    pixels[y * res + x] = c;
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();

            vignetteCache = Sprite.Create(texture, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), res);
            return vignetteCache;
        }

        static Sprite zombieSpriteCache;

        /// <summary>
        /// A rough humanoid zombie silhouette (head, torso, uneven shambling arms, torn
        /// trouser legs, red eyes, mottled decay texture) instead of a plain colored shape,
        /// drawn procedurally so no external art asset is needed.
        /// </summary>
        public static Sprite Zombie()
        {
            if (zombieSpriteCache != null) return zombieSpriteCache;

            const int w = 48, h = 64;
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color32[w * h];

            Color32 flesh = ZombieColor;
            Color32 fleshDark = new Color(ZombieColor.r * 0.55f, ZombieColor.g * 0.55f, ZombieColor.b * 0.55f, 1f);
            Color32 cloth = new Color32(26, 23, 19, 255);
            Color32 eye = new Color32(185, 30, 20, 255);

            FillRect(pixels, w, h, 15, 0, 7, 20, cloth);   // left leg
            FillRect(pixels, w, h, 26, 0, 7, 20, cloth);   // right leg
            FillRect(pixels, w, h, 11, 18, 26, 24, flesh); // torso
            FillRect(pixels, w, h, 3, 22, 8, 18, flesh);   // arm hanging low
            FillRect(pixels, w, h, 37, 28, 8, 14, flesh);  // arm raised, shambling
            FillCircle(pixels, w, h, 22, 50, 10, flesh);   // head, slightly off-center (hunched)
            SetPixel(pixels, w, h, 18, 51, eye);
            SetPixel(pixels, w, h, 19, 51, eye);
            SetPixel(pixels, w, h, 25, 51, eye);
            SetPixel(pixels, w, h, 26, 51, eye);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (pixels[i].a == 0) continue;
                    float n = Mathf.PerlinNoise(x * 0.3f, y * 0.3f);
                    if (n < 0.3f) pixels[i] = fleshDark;
                    if (n < 0.08f && y < 22) pixels[i] = new Color32(0, 0, 0, 0); // ragged trouser hem
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            zombieSpriteCache = Sprite.Create(texture, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), h);
            return zombieSpriteCache;
        }

        static void SetPixel(Color32[] pixels, int w, int h, int x, int y, Color32 c)
        {
            if (x < 0 || x >= w || y < 0 || y >= h) return;
            pixels[y * w + x] = c;
        }

        static void FillRect(Color32[] pixels, int w, int h, int x0, int y0, int rw, int rh, Color32 c)
        {
            for (int y = y0; y < y0 + rh; y++)
                for (int x = x0; x < x0 + rw; x++)
                    SetPixel(pixels, w, h, x, y, c);
        }

        static void FillCircle(Color32[] pixels, int w, int h, int cx, int cy, int r, Color32 c)
        {
            for (int y = cy - r; y <= cy + r; y++)
                for (int x = cx - r; x <= cx + r; x++)
                    if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r)
                        SetPixel(pixels, w, h, x, y, c);
        }

        static Sprite Get(Color color, bool circle)
        {
            Color32 c32 = color;
            var key = (c32, circle);
            if (cache.TryGetValue(key, out var existing)) return existing;

            var texture = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color32[Resolution * Resolution];
            float radius = Resolution / 2f;
            for (int y = 0; y < Resolution; y++)
            {
                for (int x = 0; x < Resolution; x++)
                {
                    bool inside = true;
                    if (circle)
                    {
                        float dx = x + 0.5f - radius;
                        float dy = y + 0.5f - radius;
                        inside = dx * dx + dy * dy <= radius * radius;
                    }
                    pixels[y * Resolution + x] = inside ? c32 : new Color32(0, 0, 0, 0);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();

            var sprite = Sprite.Create(texture, new Rect(0, 0, Resolution, Resolution), new Vector2(0.5f, 0.5f), Resolution);
            cache[key] = sprite;
            return sprite;
        }
    }
}
