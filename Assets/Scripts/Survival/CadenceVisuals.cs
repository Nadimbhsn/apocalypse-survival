using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Platformer.Mechanics;

namespace Platformer.Survival
{
    /// <summary>
    /// Procedural art for the runner's rhythm section (La Cadence): Geometry-Dash-style
    /// shapes - dark fills behind a bright rim - drawn once at runtime and tinted per use.
    /// Everything is baked white-on-dark so a single sprite serves every colour, and the
    /// beat pulse only has to change a renderer's colour.
    /// </summary>
    public static class CadenceArt
    {
        static readonly Dictionary<string, Sprite> cache = new();

        static readonly Color Rim = Color.white;
        static readonly Color Fill = new Color(0.15f, 0.07f, 0.10f, 1f);

        /// <summary>A floor spike, base on the pivot. 64 px = 1 unit, so a spike is 1 x 1 before scaling.</summary>
        public static Sprite Spike => Get("spike", 64, 64, new Vector2(0.5f, 0f), 64f, SpikeShade, default, SpriteMeshType.FullRect);

        /// <summary>A solid block, nine-sliced so any size keeps a crisp rim.</summary>
        public static Sprite Block => Get("block", 64, 64, new Vector2(0.5f, 0.5f), 64f, BlockShade, new Vector4(14, 14, 14, 14), SpriteMeshType.FullRect);

        /// <summary>The jump orb: a bright ring around a soft glowing core.</summary>
        public static Sprite Ring => Get("ring", 96, 96, new Vector2(0.5f, 0.5f), 96f, RingShade);

        /// <summary>The jump pad: a flat half-dome sitting on the pivot.</summary>
        public static Sprite Pad => Get("pad", 64, 32, new Vector2(0.5f, 0f), 64f, PadShade);

        /// <summary>A portal: a tall oval ring with a faint fill.</summary>
        public static Sprite Portal => Get("portal", 48, 144, new Vector2(0.5f, 0.5f), 48f, PortalShade);

        /// <summary>The checkpoint marker: a hollow diamond.</summary>
        public static Sprite Diamond => Get("diamond", 64, 64, new Vector2(0.5f, 0.5f), 64f, DiamondShade);

        /// <summary>A secret coin: a thick disc with a star-cut centre.</summary>
        public static Sprite Coin => Get("coin", 96, 96, new Vector2(0.5f, 0.5f), 96f, CoinShade);

        static Sprite Get(string key, int w, int h, Vector2 pivot, float ppu, Func<float, float, int, int, Color> shade,
            Vector4 border = default, SpriteMeshType mesh = SpriteMeshType.Tight)
        {
            if (cache.TryGetValue(key, out var s) && s != null) return s;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    // 2x2 supersampling: the rims are the whole look, they must not stair-step.
                    Color acc = Color.clear;
                    for (int sy = 0; sy < 2; sy++)
                        for (int sx = 0; sx < 2; sx++)
                            acc += shade(x + 0.25f + sx * 0.5f, y + 0.25f + sy * 0.5f, w, h);
                    px[y * w + x] = acc * 0.25f;
                }
            tex.SetPixels(px);
            tex.Apply();
            s = Sprite.Create(tex, new Rect(0, 0, w, h), pivot, ppu, 0, mesh, border);
            cache[key] = s;
            return s;
        }

        static Color Layered(float distInside, float rim)
        {
            // distInside: distance from the shape's edge towards its inside (negative = outside).
            if (distInside < 0f) return Color.clear;
            return distInside < rim ? Rim : Fill;
        }

        static float SegDist(float px, float py, float ax, float ay, float bx, float by)
        {
            float vx = bx - ax, vy = by - ay, wx = px - ax, wy = py - ay;
            float t = Mathf.Clamp01((wx * vx + wy * vy) / (vx * vx + vy * vy));
            float dx = wx - vx * t, dy = wy - vy * t;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        static Color SpikeShade(float x, float y, int w, int h)
        {
            float ax = 3f, ay = 1f, bx = w - 3f, by = 1f, cx = w * 0.5f, cy = h - 2f;
            // inside test via edge signs
            float e1 = (bx - ax) * (y - ay) - (by - ay) * (x - ax);
            float e2 = (cx - bx) * (y - by) - (cy - by) * (x - bx);
            float e3 = (ax - cx) * (y - cy) - (ay - cy) * (x - cx);
            bool inside = e1 >= 0 && e2 >= 0 && e3 >= 0;
            if (!inside) return Color.clear;
            float d = Mathf.Min(SegDist(x, y, ax, ay, bx, by), Mathf.Min(SegDist(x, y, bx, by, cx, cy), SegDist(x, y, cx, cy, ax, ay)));
            return Layered(d, 5f);
        }

        static Color BlockShade(float x, float y, int w, int h)
        {
            float d = Mathf.Min(Mathf.Min(x, w - x), Mathf.Min(y, h - y));
            if (d < 4.5f) return Rim;
            // A faint inner frame, the Geometry Dash block's signature.
            if (d > 10f && d < 12f) return Color.Lerp(Fill, Rim, 0.35f);
            return Fill;
        }

        static Color RingShade(float x, float y, int w, int h)
        {
            float cx = w * 0.5f, cy = h * 0.5f;
            float r = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
            float outer = w * 0.46f, inner = w * 0.36f;
            if (r <= outer && r >= inner) return Rim;
            if (r < inner)
            {
                float glow = Mathf.Clamp01(1f - r / inner) * 0.55f + 0.12f;
                return new Color(1f, 1f, 1f, glow);
            }
            return Color.clear;
        }

        static Color PadShade(float x, float y, int w, int h)
        {
            float cx = w * 0.5f, rx = w * 0.46f, ry = h * 0.9f;
            float nx = (x - cx) / rx, ny = y / ry;
            float r = nx * nx + ny * ny;
            if (r > 1f) return Color.clear;
            return r > 0.55f ? Rim : new Color(1f, 1f, 1f, 0.55f);
        }

        static Color PortalShade(float x, float y, int w, int h)
        {
            float nx = (x - w * 0.5f) / (w * 0.46f), ny = (y - h * 0.5f) / (h * 0.48f);
            float r = Mathf.Sqrt(nx * nx + ny * ny);
            if (r > 1f) return Color.clear;
            if (r > 0.78f) return Rim;
            return new Color(1f, 1f, 1f, 0.16f + 0.18f * (1f - r));
        }

        static Color DiamondShade(float x, float y, int w, int h)
        {
            float d = Mathf.Abs(x - w * 0.5f) + Mathf.Abs(y - h * 0.5f);
            float outer = w * 0.46f;
            if (d > outer) return Color.clear;
            return d > outer - 7f ? Rim : new Color(1f, 1f, 1f, 0.18f);
        }

        static Color CoinShade(float x, float y, int w, int h)
        {
            float cx = w * 0.5f, cy = h * 0.5f;
            float dx = x - cx, dy = y - cy;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            float outer = w * 0.46f;
            if (r > outer) return Color.clear;
            if (r > outer - 6f) return Rim;
            // five-pointed star cut into the face
            float a = Mathf.Atan2(dy, dx) + Mathf.PI / 2f;
            float star = 0.42f + 0.22f * Mathf.Cos(5f * a);
            return r < outer * star ? Rim : new Color(0.85f, 0.85f, 0.85f, 1f);
        }
    }

    /// <summary>
    /// The rhythm section's music, synthesised at runtime so it can be locked to the level:
    /// 140 BPM, four bars of A minor (Am - F - C - G), kick on every beat, clap on two and
    /// four, off-beat hats, a side-chained pumping bass and - in the second loop, which
    /// takes over from the flight onwards - a sixteenth-note arpeggio. Every obstacle in the
    /// section sits on this grid, which is what makes jumping to it feel like playing it.
    ///
    /// Building a loop costs a few tens of milliseconds, so both are synthesised a slice per
    /// frame as soon as a run starts, long before the section can come up.
    /// </summary>
    public static class CadenceMusic
    {
        public const float Bpm = 140f;
        public const int LoopBeats = 16;
        public const int SampleRate = 44100;
        public static float BeatDuration => 60f / Bpm;
        public static float LoopDuration => BeatDuration * LoopBeats;

        static LoopBuilder builderA, builderB;
        static AudioClip clipA, clipB;

        public static bool Ready => clipA != null && clipB != null;

        /// <summary>Main loop (drums and bass); the second adds the arpeggio for the climax.</summary>
        public static AudioClip LoopA { get { EnsureReady(); return clipA; } }
        public static AudioClip LoopB { get { EnsureReady(); return clipB; } }

        /// <summary>Spreads the synthesis over frames; call from any MonoBehaviour's coroutine.</summary>
        public static IEnumerator Warmup()
        {
            builderA ??= new LoopBuilder(false);
            builderB ??= new LoopBuilder(true);
            while (!builderA.Done) { builderA.Step(24000); yield return null; }
            while (!builderB.Done) { builderB.Step(24000); yield return null; }
            FinishClips();
        }

        public static void EnsureReady()
        {
            if (Ready) return;
            builderA ??= new LoopBuilder(false);
            builderB ??= new LoopBuilder(true);
            while (!builderA.Done) builderA.Step(int.MaxValue);
            while (!builderB.Done) builderB.Step(int.MaxValue);
            FinishClips();
        }

        static void FinishClips()
        {
            if (clipA == null && builderA.Done) clipA = builderA.ToClip("cadence_loop_a");
            if (clipB == null && builderB.Done) clipB = builderB.ToClip("cadence_loop_b");
        }

        class LoopBuilder
        {
            readonly bool arp;
            readonly float[] data;
            readonly System.Random rng = new System.Random(140);
            int i;
            float bassPhase, arpPhase, bassLp, hatLp, clapLp1, clapLp2;

            // A minor progression; bass an octave down, arpeggio two up.
            static readonly float[] Roots = { 55.000f, 43.654f, 65.406f, 48.999f };      // A1 F1 C2 G1
            static readonly int[][] Chords = { new[] { 0, 3, 7 }, new[] { 0, 4, 7 }, new[] { 0, 4, 7 }, new[] { 0, 4, 7 } };

            public bool Done => i >= data.Length;

            public LoopBuilder(bool withArp)
            {
                arp = withArp;
                data = new float[Mathf.RoundToInt(LoopDuration * SampleRate)];
            }

            public void Step(int count)
            {
                float beat = BeatDuration;
                int end = (int)Mathf.Min(data.Length, (long)i + count);
                for (; i < end; i++)
                {
                    float t = i / (float)SampleRate;
                    float bt = t / beat;
                    int beatIdx = (int)bt;
                    float inBeat = bt - beatIdx;
                    float tb = inBeat * beat;
                    int bar = (beatIdx / 4) % 4;
                    int beatInBar = beatIdx % 4;
                    float s = 0f;

                    // Kick: a pitched sine dive, rebuilt from the beat start each time.
                    float kickPhase = 2f * Mathf.PI * (48f * tb + 110f / 35f * (1f - Mathf.Exp(-35f * tb)));
                    s += Mathf.Sin(kickPhase) * Mathf.Exp(-tb * 16f) * 0.95f;

                    // Side-chain duck: everything melodic breathes around the kick.
                    float duck = 1f - 0.65f * Mathf.Exp(-tb * 9f);

                    // Clap on two and four: band-limited noise.
                    float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                    if (beatInBar == 1 || beatInBar == 3)
                    {
                        clapLp1 += (white - clapLp1) * 0.45f;
                        clapLp2 += (clapLp1 - clapLp2) * 0.06f;
                        s += (clapLp1 - clapLp2) * Mathf.Exp(-tb * 20f) * 0.55f;
                    }

                    // Off-beat hat: high-passed noise, short.
                    hatLp += (white - hatLp) * 0.25f;
                    if (inBeat >= 0.5f)
                    {
                        float th = tb - beat * 0.5f;
                        s += (white - hatLp) * Mathf.Exp(-th * 55f) * 0.16f;
                    }

                    // Bass: eighth notes, root then octave, a filtered saw that snaps open on each note.
                    bool upper = inBeat >= 0.5f;
                    float t8 = upper ? tb - beat * 0.5f : tb;
                    float bassFreq = Roots[bar] * (upper ? 2f : 1f);
                    bassPhase += bassFreq / SampleRate;
                    bassPhase -= Mathf.Floor(bassPhase);
                    float saw = 2f * bassPhase - 1f;
                    float env8 = Mathf.Exp(-t8 * 7f);
                    bassLp += (saw - bassLp) * (0.05f + 0.22f * env8);
                    s += bassLp * (0.35f + 0.25f * env8) * duck * 0.9f;

                    if (arp)
                    {
                        // Sixteenth-note arpeggio up the chord and its octave.
                        int step = (int)(bt * 4f) % 4;
                        float t16 = (bt * 4f - Mathf.Floor(bt * 4f)) * beat * 0.25f;
                        int semi = step == 3 ? 12 : Chords[bar][step];
                        float freq = Roots[bar] * 8f * Mathf.Pow(2f, semi / 12f);
                        arpPhase += freq / SampleRate;
                        arpPhase -= Mathf.Floor(arpPhase);
                        float tri = 1f - 4f * Mathf.Abs(arpPhase - 0.5f);
                        float square = arpPhase < 0.5f ? 1f : -1f;
                        s += (tri * 0.6f + square * 0.25f) * Mathf.Exp(-t16 * 16f) * duck * 0.2f;
                    }

                    data[i] = (float)Math.Tanh(s * 1.15f) * 0.72f;
                }
            }

            public AudioClip ToClip(string name)
            {
                var clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
                clip.SetData(data, 0);
                return clip;
            }
        }
    }

    /// <summary>
    /// Stands in for the player's sprite during the rhythm section so the character can
    /// spin through its jumps and tilt in flight - the Geometry Dash cube's signature -
    /// without rotating the player's collider, which would wreck the physics. It copies the
    /// real sprite, flip and colour every frame (animation and damage flashes included),
    /// and spins around the collider's centre rather than the sprite's.
    /// </summary>
    public class CadenceBody : MonoBehaviour
    {
        PlayerController player;
        SpriteRenderer source;
        Transform pivot;
        SpriteRenderer copy;
        TrailRenderer trail;
        float angle;
        public bool shipMode;

        /// <summary>Degrees per second while airborne: one full turn over a two-beat jump.</summary>
        const float SpinSpeed = 420f;

        public static CadenceBody Attach(PlayerController target, Color trailColor)
        {
            var go = new GameObject("CadenceBody");
            go.transform.SetParent(target.transform, false);
            var body = go.AddComponent<CadenceBody>();
            body.player = target;
            body.source = target.GetComponent<SpriteRenderer>();

            body.pivot = new GameObject("Pivot").transform;
            body.pivot.SetParent(go.transform, false);
            var art = new GameObject("Sprite");
            art.transform.SetParent(body.pivot, false);
            body.copy = art.AddComponent<SpriteRenderer>();
            body.copy.sortingOrder = (body.source != null ? body.source.sortingOrder : 0) + 6;

            // A short glowing trail behind the character, the section's other signature.
            body.trail = go.AddComponent<TrailRenderer>();
            body.trail.time = 0.22f;
            body.trail.startWidth = 0.28f;
            body.trail.endWidth = 0f;
            body.trail.minVertexDistance = 0.05f;
            body.trail.material = new Material(Shader.Find("Sprites/Default"));
            body.trail.startColor = new Color(trailColor.r, trailColor.g, trailColor.b, 0.75f);
            body.trail.endColor = new Color(trailColor.r, trailColor.g, trailColor.b, 0f);
            body.trail.sortingOrder = body.copy.sortingOrder - 1;

            if (body.source != null) body.source.enabled = false;
            return body;
        }

        public void Detach()
        {
            if (source != null) source.enabled = true;
            Destroy(gameObject);
        }

        /// <summary>A respawn is a teleport: the trail must not draw a streak back to the checkpoint.</summary>
        public void ResetPose()
        {
            angle = 0f;
            if (trail != null) trail.Clear();
        }

        public void SetTrailColor(Color c)
        {
            if (trail == null) return;
            trail.startColor = new Color(c.r, c.g, c.b, 0.75f);
            trail.endColor = new Color(c.r, c.g, c.b, 0f);
        }

        public void SetVisible(bool visible)
        {
            if (copy != null) copy.enabled = visible;
            if (trail != null) trail.emitting = visible;
        }

        void LateUpdate()
        {
            if (player == null || source == null) return;
            copy.sprite = source.sprite;
            copy.flipX = source.flipX;
            copy.flipY = source.flipY;
            copy.color = source.color;

            var offset = player.collider2d != null ? player.collider2d.offset : Vector2.zero;
            pivot.localPosition = offset;
            copy.transform.localPosition = -offset;

            float dt = Time.deltaTime;
            if (shipMode)
            {
                // Nose follows the flight path, like the ship.
                float target = Mathf.Clamp(Mathf.Atan2(player.velocity.y, Mathf.Max(1f, player.velocity.x)) * Mathf.Rad2Deg, -35f, 35f);
                angle = Mathf.LerpAngle(angle, target, 1f - Mathf.Exp(-14f * dt));
            }
            else if (!player.IsGrounded)
            {
                angle -= SpinSpeed * dt * player.gravitySign;
            }
            else
            {
                angle = Mathf.MoveTowardsAngle(angle, 0f, 1100f * dt);
            }
            pivot.localRotation = Quaternion.Euler(0f, 0f, angle);
        }
    }
}
