using System.Collections.Generic;
using UnityEngine;

namespace Platformer.Survival
{
    /// <summary>
    /// The painted body of one of the dead (Resources/Runner/enemy_*.png, eight frames
    /// each): the walk cycle plays as fast as the body actually moves, a hit flashes it
    /// red-hot, and a rise out of a grave slides it up from under the ground behind a
    /// mask so it really comes out of the earth. Lives on a child of the Zombie, which
    /// keeps its collider and flips the root to face the player.
    /// </summary>
    public class EnemySprite : MonoBehaviour
    {
        public struct Sheet
        {
            public string name;
            public int frameW, frameH, count, cols;
            public float feet;
        }

        static readonly Dictionary<string, Sheet> Sheets = new()
        {
            ["walker"] = new Sheet { name = "enemy_walker", frameW = 276, frameH = 308, count = 8, cols = 4, feet = 0.0443f },
            ["walker_b"] = new Sheet { name = "enemy_walker_b", frameW = 276, frameH = 308, count = 8, cols = 4, feet = 0.0443f },
            ["hound"] = new Sheet { name = "enemy_hound", frameW = 308, frameH = 204, count = 8, cols = 4, feet = 0.0502f },
            ["wisp"] = new Sheet { name = "enemy_wisp", frameW = 240, frameH = 324, count = 8, cols = 4, feet = 0.0527f },
            ["brute"] = new Sheet { name = "enemy_brute", frameW = 376, frameH = 412, count = 8, cols = 4, feet = 0.0331f },
        };

        static readonly Dictionary<string, Sprite[]> cache = new();

        SpriteRenderer sr;
        Sprite[] frames;
        float fps, phase, flashUntil, riseStart, riseDuration, depth;
        Vector3 lastPos;
        SpriteMask mask;
        bool floating;

        public bool IsRising => riseDuration > 0f && Time.time < riseStart + riseDuration;

        /// <summary>
        /// Frames of a sheet with the pivot on the body's centre, groundOffset above the feet
        /// (the Zombie keeps its root that high above the ground).
        /// </summary>
        public static Sprite[] Frames(string key, float groundOffset)
        {
            string id = key + "|" + groundOffset.ToString("0.###");
            if (cache.TryGetValue(id, out var f)) return f;
            if (!Sheets.TryGetValue(key, out var s)) return null;
            var tex = Resources.Load<Texture2D>("Runner/" + s.name);
            if (tex == null) { cache[id] = null; return null; }
            float frameHUnits = s.frameH / RunnerArt.Ppu;
            var pivot = new Vector2(0.5f, s.feet + groundOffset / frameHUnits);
            f = new Sprite[s.count];
            for (int i = 0; i < s.count; i++)
            {
                int cx = i % s.cols, cy = i / s.cols;
                var rect = new Rect(cx * s.frameW, tex.height - (cy + 1) * s.frameH, s.frameW, s.frameH);
                f[i] = Sprite.Create(tex, rect, pivot, RunnerArt.Ppu, 0, SpriteMeshType.Tight);
            }
            cache[id] = f;
            return f;
        }

        public static EnemySprite Attach(GameObject zombie, string key, float groundOffset, float fps, bool floating)
        {
            var frames = Frames(key, groundOffset);
            if (frames == null) return null;
            var body = new GameObject("Body");
            body.transform.SetParent(zombie.transform, false);
            var es = body.AddComponent<EnemySprite>();
            es.sr = body.AddComponent<SpriteRenderer>();
            es.sr.sprite = frames[0];
            es.sr.sortingOrder = 3;
            es.frames = frames;
            es.fps = fps;
            es.floating = floating;
            es.depth = groundOffset * 2f;
            es.phase = Random.Range(0f, frames.Length);
            es.lastPos = zombie.transform.position;
            return es;
        }

        public void Flash() => flashUntil = Time.time + 0.1f;

        /// <summary>Climbs out of the ground over the given time (the grave's earth hides what is still below).</summary>
        public void Rise(float duration)
        {
            riseStart = Time.time;
            riseDuration = duration;
            if (mask == null)
            {
                var go = new GameObject("RiseMask");
                go.transform.SetParent(transform.parent, false);
                mask = go.AddComponent<SpriteMask>();
                mask.sprite = PlaceholderVisuals.Square(Color.white);
                // Everything above the ground line, around the body.
                go.transform.localPosition = new Vector3(0f, -depth / 2f + 2.5f, 0f);
                go.transform.localScale = new Vector3(4f, 5f, 1f);
            }
            mask.enabled = true;
            sr.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
        }

        void LateUpdate()
        {
            if (frames == null) return;
            float dt = Time.deltaTime;
            var pos = transform.parent != null ? transform.parent.position : transform.position;
            float speed = dt > 0f ? Mathf.Abs(pos.x - lastPos.x) / dt : 0f;
            lastPos = pos;
            // Walkers plod, hounds gallop: the cycle follows the ground covered (floaters drift on).
            float rate = floating ? 1f : Mathf.Clamp(speed / 1.6f, 0.35f, 2.4f);
            phase += dt * fps * rate;
            sr.sprite = frames[(int)phase % frames.Length];

            if (riseDuration > 0f)
            {
                float k = Mathf.Clamp01((Time.time - riseStart) / riseDuration);
                transform.localPosition = new Vector3(0f, -depth * (1f - Mathf.SmoothStep(0f, 1f, k)), 0f);
                if (k >= 1f)
                {
                    riseDuration = 0f;
                    transform.localPosition = Vector3.zero;
                    sr.maskInteraction = SpriteMaskInteraction.None;
                    if (mask != null) mask.enabled = false;
                }
            }
            else if (floating)
            {
                transform.localPosition = new Vector3(0f, Mathf.Sin(Time.time * 2.2f + phase) * 0.06f, 0f);
            }

            bool hot = Time.time < flashUntil;
            sr.color = hot ? new Color(1f, 0.45f, 0.35f) : Color.white;
            transform.localScale = hot ? new Vector3(1.08f, 0.94f, 1f) : Vector3.one;
        }
    }
}
