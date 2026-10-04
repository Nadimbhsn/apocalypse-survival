using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Platformer.Mechanics;

namespace Platformer.Survival
{
    /// <summary>
    /// Procedural art for the runner's rhythm section (La Cadence), in the Apogée's own
    /// look rather than an arcade one: crimson thorns, warm stone blocks with a lip of red
    /// grass, paper lanterns to tap, red mushrooms that fling, stone arches for the gates,
    /// pennants for checkpoints and golden feathers as the hidden prizes. Drawn once at
    /// runtime; a renderer's tint recolours the lit parts.
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

        // ---- the Apogée look: thorns, stone, lanterns, mushrooms, arches, feathers -------
        // Colours are baked in (warm stone, crimson thorns); the renderer's tint only warms
        // or recolours the lit parts, and the beat pulse only nudges their brightness.

        static float Hash(float x, float y)
        {
            float h = Mathf.Sin(x * 12.9898f + y * 78.233f) * 43758.5453f;
            return h - Mathf.Floor(h);
        }

        static bool InTri(float x, float y, float ax, float ay, float bx, float by, float cx, float cy)
        {
            float e1 = (bx - ax) * (y - ay) - (by - ay) * (x - ax);
            float e2 = (cx - bx) * (y - by) - (cy - by) * (x - bx);
            float e3 = (ax - cx) * (y - cy) - (ay - cy) * (x - cx);
            return (e1 >= 0 && e2 >= 0 && e3 >= 0) || (e1 <= 0 && e2 <= 0 && e3 <= 0);
        }

        /// <summary>A crimson thorn bush: a dark woody tuft with three thorns and two leaves.</summary>
        static Color SpikeShade(float x, float y, int w, int h)
        {
            var thorn = new Color(0.80f, 0.17f, 0.11f);
            var thornLit = new Color(1f, 0.42f, 0.28f);
            var wood = new Color(0.30f, 0.08f, 0.06f);
            var leaf = new Color(0.95f, 0.42f, 0.16f);
            // leaves first so the thorns sit in front of them
            float l1 = Mathf.Pow((x - 17f) / 8f, 2f) + Mathf.Pow((y - 24f) / 4.5f, 2f);
            float l2 = Mathf.Pow((x - 47f) / 8f, 2f) + Mathf.Pow((y - 28f) / 4.5f, 2f);
            bool thornC = InTri(x, y, 23f, 8f, 41f, 8f, 32f, 62f);
            bool thornL = InTri(x, y, 7f, 8f, 23f, 8f, 9f, 42f);
            bool thornR = InTri(x, y, 41f, 8f, 57f, 8f, 55f, 42f);
            if (thornC) return x < 31f ? thornLit : thorn;
            if (thornL) return x < 12f ? thornLit : thorn;
            if (thornR) return x < 48f ? thornLit : thorn;
            if (l1 < 1f || l2 < 1f) return leaf;
            float mound = Mathf.Pow((x - 32f) / 29f, 2f) + Mathf.Pow((y - 4f) / 9f, 2f);
            if (mound < 1f && y >= 0f) return wood;
            return Color.clear;
        }

        /// <summary>A block of warm stone with a lip of crimson grass on top (nine-sliced).</summary>
        static Color BlockShade(float x, float y, int w, int h)
        {
            var edge = new Color(0.20f, 0.12f, 0.10f);
            float grassTop = h - 4f + 2.5f * Mathf.Sin(x * 0.7f);
            if (y > grassTop) return Color.clear;
            if (y > h - 12f) return y > h - 6f ? new Color(0.92f, 0.30f, 0.18f) : new Color(0.70f, 0.14f, 0.10f);
            if (x < 2.5f || x > w - 2.5f || y < 2.5f) return edge;
            float n = Hash(Mathf.Floor(x / 3f), Mathf.Floor(y / 3f)) * 0.08f;
            var stone = new Color(0.48f + n, 0.36f + n, 0.31f + n);
            return y > h - 15f ? stone * 0.8f : stone;
        }

        /// <summary>A paper lantern glowing gold: ribbed body, dark caps, a soft halo.</summary>
        static Color RingShade(float x, float y, int w, int h)
        {
            float cx = w * 0.5f, cy = h * 0.48f;
            if (Mathf.Abs(x - cx) < 9f && y > cy + 28f && y < cy + 36f) return new Color(0.35f, 0.18f, 0.10f);   // top cap
            if (Mathf.Abs(x - cx) < 9f && y < cy - 28f && y > cy - 33f) return new Color(0.35f, 0.18f, 0.10f);   // bottom cap
            float nx = (x - cx) / 25f, ny = (y - cy) / 29f;
            float r = nx * nx + ny * ny;
            if (r <= 1f)
            {
                float rib = Mathf.Abs(Mathf.Sin(nx * Mathf.PI * 2.2f));
                float glow = 0.75f + 0.25f * (1f - r);
                var c = rib < 0.12f ? new Color(0.85f, 0.62f, 0.35f) : new Color(1f, 0.95f, 0.78f);
                return new Color(c.r * glow, c.g * glow, c.b * glow, 1f);
            }
            float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
            if (d < w * 0.48f) return new Color(1f, 0.9f, 0.6f, 0.28f * (1f - d / (w * 0.48f)));
            return Color.clear;
        }

        /// <summary>A springy red mushroom with white spots.</summary>
        static Color PadShade(float x, float y, int w, int h)
        {
            float cx = w * 0.5f;
            if (y < 11f) return Mathf.Abs(x - cx) < 7f ? new Color(0.95f, 0.90f, 0.80f) : Color.clear;   // stem
            float nx = (x - cx) / 30f, ny = (y - 10f) / 21f;
            if (nx * nx + ny * ny > 1f) return Color.clear;
            float s1 = Mathf.Pow(x - cx, 2f) + Mathf.Pow(y - 23f, 2f);
            float s2 = Mathf.Pow(x - cx + 15f, 2f) + Mathf.Pow(y - 15f, 2f);
            float s3 = Mathf.Pow(x - cx - 15f, 2f) + Mathf.Pow(y - 15f, 2f);
            if (s1 < 16f || s2 < 10f || s3 < 10f) return new Color(1f, 0.97f, 0.92f);
            return y < 13f ? new Color(0.55f, 0.10f, 0.08f) : new Color(0.86f, 0.18f, 0.14f);
        }

        /// <summary>A stone arch with a glow inside it; the tint gives each gate its colour.</summary>
        static Color PortalShade(float x, float y, int w, int h)
        {
            float cx = w * 0.5f;
            const float spring = 116f;               // where the pillars turn into the arch
            bool outer = y < spring ? (x > 2f && x < w - 2f) : Mathf.Pow(x - cx, 2f) + Mathf.Pow(y - spring, 2f) < 22f * 22f;
            if (!outer || y < 1f) return Color.clear;
            bool inner = y < spring ? (x > 11f && x < w - 11f) : Mathf.Pow(x - cx, 2f) + Mathf.Pow(y - spring, 2f) < 13f * 13f;
            if (inner) return new Color(1f, 1f, 1f, 0.14f + 0.12f * Mathf.Clamp01(y / h));
            // stones of the frame, with joints every 16 px and a glowing rune now and then
            bool joint = y < spring && Mathf.Repeat(y, 16f) < 1.5f;
            bool rune = y < spring && Mathf.Repeat(y + 8f, 32f) < 4f && Mathf.Abs(x - (x < cx ? 6.5f : w - 6.5f)) < 1.6f;
            if (rune) return Color.white;
            float n = Hash(Mathf.Floor(x / 4f), Mathf.Floor(y / 8f)) * 0.1f;
            return joint ? new Color(0.45f, 0.42f, 0.40f) : new Color(0.78f + n, 0.75f + n, 0.72f + n);
        }

        /// <summary>A checkpoint pennant: a dark pole and a pale flag that the tint colours.</summary>
        static Color DiamondShade(float x, float y, int w, int h)
        {
            if (x > 13f && x < 18f && y > 3f && y < 61f) return new Color(0.30f, 0.18f, 0.12f);
            float wave = 2.5f * Mathf.Sin(y * 0.25f);
            if (InTri(x, y, 18f, 60f, 18f, 32f, 57f + wave, 46f)) return Color.white;
            return Color.clear;
        }

        /// <summary>A golden feather: a curved vane on a bright quill, its barbs drawn in.</summary>
        static Color CoinShade(float x, float y, int w, int h)
        {
            // along the feather (diagonal, tip up-right) and across it
            float u = ((x - w * 0.5f) + (y - h * 0.5f)) * 0.7071f;
            float v = (-(x - w * 0.5f) + (y - h * 0.5f)) * 0.7071f;
            float t = (u + 38f) / 76f;                  // 0 at the quill end, 1 at the tip
            if (t < -0.08f || t > 1f) return Color.clear;
            if (t < 0.05f) return Mathf.Abs(v) < 1.8f ? new Color(0.95f, 0.9f, 0.75f) : Color.clear;   // bare quill
            float half = 15f * Mathf.Sin(Mathf.PI * Mathf.Clamp01((t - 0.05f) / 0.95f)) + v * 0.08f;
            if (Mathf.Abs(v) > half) return Color.clear;
            if (Mathf.Abs(v) < 1.6f) return Color.white;                                              // the quill
            bool barb = Mathf.Repeat(u * 0.5f + Mathf.Abs(v) * 0.35f, 3f) < 0.7f;
            return barb ? new Color(0.78f, 0.72f, 0.62f) : new Color(1f, 0.97f, 0.88f);
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
        SpriteRenderer glider;
        float angle;
        Vector2 squash = Vector2.one;
        bool wasGrounded = true;
        public bool shipMode;

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

            // A faint wisp of wind behind the character - not a neon streak.
            body.trail = go.AddComponent<TrailRenderer>();
            body.trail.time = 0.12f;
            body.trail.startWidth = 0.1f;
            body.trail.endWidth = 0f;
            body.trail.minVertexDistance = 0.05f;
            body.trail.material = new Material(Shader.Find("Sprites/Default"));
            body.trail.startColor = new Color(1f, 0.95f, 0.85f, 0.35f);
            body.trail.endColor = new Color(1f, 0.95f, 0.85f, 0f);
            body.trail.sortingOrder = body.copy.sortingOrder - 1;

            // The glider for the flying parts: a great crimson leaf held overhead.
            var wing = new GameObject("Glider");
            wing.transform.SetParent(body.pivot, false);
            wing.transform.localPosition = new Vector3(0f, 0.62f, 0f);
            wing.transform.localRotation = Quaternion.Euler(0f, 0f, -8f);
            body.glider = wing.AddComponent<SpriteRenderer>();
            var leafTex = ApogeeTheme.LeafTexture;
            if (leafTex != null)
                body.glider.sprite = Sprite.Create(leafTex, new Rect(0, 0, leafTex.width, leafTex.height), new Vector2(0.5f, 0.5f), leafTex.width / 1.5f);
            body.glider.color = new Color(1f, 0.55f, 0.4f);
            body.glider.sortingOrder = body.copy.sortingOrder + 1;
            body.glider.enabled = false;

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
            trail.startColor = new Color(c.r, c.g, c.b, 0.35f);
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
            if (glider != null) glider.enabled = shipMode && copy.enabled;
            if (shipMode)
            {
                // Gliding: the body leans gently with the flight path.
                float target = Mathf.Clamp(Mathf.Atan2(player.velocity.y, Mathf.Max(1f, player.velocity.x)) * Mathf.Rad2Deg, -20f, 20f);
                angle = Mathf.LerpAngle(angle, target, 1f - Mathf.Exp(-10f * dt));
            }
            else
            {
                // On foot the character stays upright - no spinning: it stretches as it takes
                // off and squashes as it lands, and leans a little into the run.
                bool grounded = player.IsGrounded;
                if (wasGrounded && !grounded) squash = new Vector2(0.86f, 1.16f);
                else if (!wasGrounded && grounded) squash = new Vector2(1.18f, 0.82f);
                wasGrounded = grounded;
                float lean = grounded ? -6f : -2f;
                angle = Mathf.MoveTowardsAngle(angle, lean * player.gravitySign, 400f * dt);
            }
            squash = Vector2.Lerp(squash, Vector2.one, 1f - Mathf.Exp(-12f * dt));
            pivot.localRotation = Quaternion.Euler(0f, 0f, angle);
            pivot.localScale = new Vector3(squash.x, squash.y, 1f);
        }
    }
}
