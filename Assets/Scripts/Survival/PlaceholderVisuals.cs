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

        // Desaturated, ash-and-rust apocalypse palette - no clean/bright colors, everything
        // reads as decayed, scorched or rusted rather than a friendly primary-color prototype.
        public static readonly Color ZombieColor = new Color(0.30f, 0.36f, 0.22f);
        public static readonly Color RunnerZombieColor = new Color(0.40f, 0.29f, 0.20f);
        public static readonly Color MineColor = new Color(0.72f, 0.24f, 0.09f);
        public static readonly Color CoinColor = new Color(0.70f, 0.60f, 0.30f);
        public static readonly Color MaterialColor = new Color(0.46f, 0.43f, 0.47f);
        public static readonly Color ProjectileColor = new Color(0.92f, 0.70f, 0.28f);
        public static readonly Color ExplosionColor = new Color(0.82f, 0.34f, 0.09f);
        public static readonly Color VehicleColor = new Color(0.34f, 0.32f, 0.30f);
        public static readonly Color GroundColor = new Color(0.22f, 0.19f, 0.15f);
        public static readonly Color SkyColor = new Color(0.35f, 0.30f, 0.26f);
        public static readonly Color AshColor = new Color(0.55f, 0.51f, 0.46f, 0.55f);
        public static readonly Color SpitColor = new Color(0.55f, 0.75f, 0.22f);
        public static readonly Color RuinColor = new Color(0.16f, 0.14f, 0.13f, 0.9f);

        public static Sprite Circle(Color color) => Get(color, true);
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
            Color edge = new Color(0.04f, 0.03f, 0.02f, 0.9f);

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
