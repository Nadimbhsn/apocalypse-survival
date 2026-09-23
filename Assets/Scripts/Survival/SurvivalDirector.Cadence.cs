using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Platformer.Survival
{
    /// <summary>
    /// "LA CADENCE": the runner's Geometry Dash section - a long, hand-authored rhythm
    /// level that interrupts the run for about eighty seconds.
    ///
    /// Inside it the character runs forward on its own and the only input is jump. Every
    /// obstacle sits on the beat of a 140 BPM track synthesised for it (see CadenceMusic),
    /// and because the run speed is constant, distance is time: an obstacle placed on beat
    /// 20 is reached exactly on the twentieth beat of the music. The jump is tuned to last
    /// exactly two beats, so jumping on the kick clears a spike on the next one.
    ///
    /// The vocabulary is Geometry Dash's: spikes, blocks to land on (running into their
    /// side is a crash), pits, yellow pads that fling you, jump orbs to tap in mid-air,
    /// gravity portals that stand you on the ceiling, a ship section flown by holding the
    /// button, a speed portal for the finale, three hidden coins on riskier lines, and
    /// practice-mode checkpoints. A crash costs one heart and restarts from the last
    /// checkpoint with the music rewound to match; running out of hearts ends the run.
    ///
    /// Collisions with obstacles are positional (a list of hitboxes checked against the
    /// player each frame) rather than physics triggers, so they are exact and
    /// deterministic; only floors, blocks and ceilings are real colliders.
    /// </summary>
    public partial class SurvivalDirector
    {
        // ---- tuning: everything is derived from one jump lasting exactly two beats ----
        const float CadenceSpeed = 6.4f;
        const float CadenceFastSpeed = 8.3f;
        /// <summary>Apex 2.0 m in 2 beats at 140 BPM: g = 8h / T^2 = 21.8 m/s^2, i.e. 2.22 g.</summary>
        const float CadenceGravityScale = 2.22f;
        const float CadenceJumpVelocity = 9.33f;
        /// <summary>A pad throws the player 3.8 m high for 2.8 beats.</summary>
        const float CadencePadVelocity = 12.9f;
        const float CadenceShipGravityScale = 1.3f;
        const float CadenceShipMaxFall = 7f;
        const float CadenceLeadIn = 10f;
        const float CadenceRunOut = 18f;
        /// <summary>From here on the music gains its arpeggio: the flight and the finale.</summary>
        const float CadenceArpBeat = 100f;
        const float CadenceGravityCeiling = 4.6f;
        const float CadenceShipCeiling = 6.5f;

        static readonly Color CadenceGold = new Color(0.96f, 0.74f, 0.36f);
        static readonly Color CadenceSpikeColor = new Color(1f, 0.44f, 0.30f);
        static readonly Color CadencePadColor = new Color(1f, 0.84f, 0.22f);
        static readonly Color CadenceOrbColor = new Color(1f, 0.90f, 0.38f);
        static readonly Color CadenceFlipColor = new Color(0.35f, 0.85f, 1f);
        static readonly Color CadenceShipColor = new Color(1f, 0.45f, 0.80f);
        static readonly Color CadenceCubeColor = new Color(0.50f, 1f, 0.60f);
        static readonly Color CadenceSpeedColor = new Color(1f, 0.60f, 0.20f);
        static readonly Color CadenceCheckColor = new Color(0.45f, 1f, 0.78f);

        enum CadenceKind { Spike, Pad, Orb, FlipGravity, NormalGravity, ShipOn, ShipOff, Speed, Checkpoint, Coin, End }

        class CadenceElement
        {
            public CadenceKind kind;
            public float x, y;
            public Rect hit;
            public float radius;
            public float value;
            public int coinIndex;
            public bool passed;
            public GameObject go;
            public SpriteRenderer sr;
            public Color baseColor;
            public Vector3 baseScale;
        }

        struct CadenceCheckpoint
        {
            public float x, y, beat, speed, gravity;
            public bool ship;
        }

        readonly List<CadenceElement> cadenceElements = new();
        readonly List<GameObject> cadenceObjects = new();
        readonly List<Rect> cadenceBlocks = new();
        readonly List<SpriteRenderer> cadencePulse = new();
        readonly List<(float beat, float speed)> cadenceSpeedMap = new();
        readonly bool[] cadenceCoins = new bool[3];

        bool cadenceBuilt, cadenceActive, cadenceRespawning, cadenceFinished;
        float cadenceStartX, cadenceEndX, cadenceFloorY, cadenceArpX, cadenceFeetOffset;
        float cadenceSpeed;
        bool cadenceShip, cadenceMusicB;
        int cadenceAttempts;
        CadenceCheckpoint cadenceCheckpoint;
        float cadenceStuckTimer, cadenceStuckRefX;
        Vector2 cadenceColliderOffset;
        CadenceBody cadenceBody;
        AudioSource cadenceMusicSource;
        readonly List<AudioSource> cadencePausedMusic = new();
        Coroutine cadenceWarmup;

        // camera state set aside for the section
        bool cadenceLookaheadWas;
        Vector3 cadenceDampingWas;

        float CadenceBeatDuration => CadenceMusic.BeatDuration;

        /// <summary>True for any x inside the section, lead-in included: nothing else spawns there.</summary>
        bool InCadenceSpan(float x) => cadenceBuilt && x >= cadenceStartX - CadenceLeadIn - 30f && x <= cadenceEndX + 8f;

        /// <summary>Starts synthesising the section's music in the background, once per session.</summary>
        void WarmUpCadenceMusic()
        {
            if (CadenceMusic.Ready || cadenceWarmup != null) return;
            cadenceWarmup = StartCoroutine(CadenceMusic.Warmup());
        }

        // =====================================================================================
        // Building
        // =====================================================================================

        float CadenceX(float beat)
        {
            float x = cadenceStartX, prevBeat = 0f, speed = cadenceSpeedMap[0].speed;
            for (int i = 1; i < cadenceSpeedMap.Count; i++)
            {
                if (beat <= cadenceSpeedMap[i].beat) break;
                x += (cadenceSpeedMap[i].beat - prevBeat) * speed * CadenceBeatDuration;
                prevBeat = cadenceSpeedMap[i].beat;
                speed = cadenceSpeedMap[i].speed;
            }
            return x + (beat - prevBeat) * speed * CadenceBeatDuration;
        }

        float CadenceY(float height) => cadenceFloorY + height;

        /// <summary>
        /// Lays the whole section at once, from its lead-in to its run-out. Unlike the rest
        /// of the run it is never recycled from behind while it is being played, because a
        /// crash sends the player back to a checkpoint up to a hundred metres earlier.
        /// </summary>
        void BuildCadence(float xStart)
        {
            ClearCadence();
            cadenceBuilt = true;
            cadenceFinished = false;
            cadenceFloorY = lastTopY;
            cadenceStartX = xStart + CadenceLeadIn;
            for (int i = 0; i < cadenceCoins.Length; i++) cadenceCoins[i] = false;

            cadenceFeetOffset = 0.41f;
            if (player != null && player.collider2d != null && player.collider2d.enabled)
                cadenceFeetOffset = Mathf.Max(0.2f, player.transform.position.y - player.collider2d.bounds.min.y);

            cadenceSpeedMap.Clear();
            cadenceSpeedMap.Add((0f, CadenceSpeed));
            cadenceSpeedMap.Add((141f, CadenceFastSpeed));

            const float endBeat = 188f;
            cadenceEndX = CadenceX(endBeat);
            cadenceArpX = CadenceX(CadenceArpBeat);

            // ---- floor, broken by the pits --------------------------------------------------
            var pits = new List<(float from, float to)>
            {
                (23f, 24.25f), (43.7f, 45.8f), (156.3f, 158.4f), (174.7f, 181.9f),
            };
            float floorFrom = xStart;
            foreach (var (from, to) in pits)
            {
                float a = CadenceX(from), b = CadenceX(to);
                CadenceFloor(floorFrom, a);
                segments.Add(new GroundSegment { xStart = a, xEnd = b, topY = cadenceFloorY, isGap = true, go = null });
                floorFrom = b;
            }
            float runOutEnd = cadenceEndX + CadenceRunOut;
            CadenceFloor(floorFrom, runOutEnd);

            // ---- ceilings: the gravity room (with a hole) and the flight corridor -----------
            CadenceCeiling(CadenceX(73f), CadenceX(88.9f), CadenceGravityCeiling);
            CadenceCeiling(CadenceX(90.1f), CadenceX(97f), CadenceGravityCeiling);
            CadenceCeiling(CadenceX(101.5f), CadenceX(136f), CadenceShipCeiling);

            CadencePortal(CadenceKind.End, 0f, CadenceGold, 4.2f, visualOnly: true); // entry arch at beat 0
            WriteCadenceLevel();

            // ---- hand the frontier back to the ordinary run ---------------------------------
            frontierX = runOutEnd;
            lastTopY = cadenceFloorY;
            lastSegmentWasGap = false;
            lastSegmentWasUnstable = false;
            lastSegmentWidth = CadenceRunOut;

            cadenceElements.Sort((p, q) => p.x.CompareTo(q.x));
        }

        /// <summary>
        /// The level itself, in beats. Obstacles on whole beats are jumped from the beat
        /// before; the comments give the intended line. Positions were checked against the
        /// jump arc (2.0 m apex, two beats), the pad arc (3.8 m, 2.8 beats) and the orb
        /// chains (an orb every two beats at the same height).
        /// </summary>
        void WriteCadenceLevel()
        {
            // ---- 1. First steps: single, double, a held pair, the triple, a pit -------------
            Spike(4f); Coins(4f);
            Spike(8f); Coins(8f);
            Spike(11f, 2);
            Spike(14f); Spike(16f);                 // hold the button: two jumps in a row
            Spike(20f, 3); Coins(20.3f);            // the triple
            // pit 23 - 24.25

            // ---- 2. Blocks: a ledge, the first coin, stairs, a pad over a pit ---------------
            Platform(27f, 29f, 1f);
            Coin(0, 29.8f, 3.3f);                   // jump off the very end of the ledge
            Spike(32f); Coins(32f);
            Platform(34f, 36f, 1f);                 // step one...
            Platform(36f, 38f, 2f);                 // ...and two
            Spike(40.5f);
            Pad(43.3f);                             // pad over the pit 43.7 - 45.8
            Checkpoint(48f);

            // ---- 3. Orbs: tap on every other beat over a bed of spikes ----------------------
            SpikeStrip(50.5f, 57f, 0f, false);
            Orb(50.5f, 2.3f); Orb(52.5f, 2.3f); Orb(54.5f, 2.3f); Orb(56.5f, 2.3f);
            Spike(60f); Coins(60f);
            Platform(62.2f, 63.0f, 1f);             // hop block to block...
            Spike(63.6f);
            Platform(64.2f, 65.0f, 1f);             // ...over the floor spikes
            Spike(65.4f);
            Spike(68.5f, 2);
            Checkpoint(72f);

            // ---- 4. Gravity: stand on the ceiling, jump "down" --------------------------------
            CadencePortal(CadenceKind.FlipGravity, 74f, CadenceFlipColor, CadenceGravityCeiling);
            SpikeStrip(76f, 93f, 0f, false);        // the floor you must not fall back to
            CeilingSpike(78f);
            CeilingSpike(81f);
            CeilingSpike(83.5f, 2);
            Coin(1, 86.5f, CadenceGravityCeiling - 2.35f); // an upside-down jump reaches it
            // hole in the ceiling 88.9 - 90.1
            CeilingSpike(93f);
            CadencePortal(CadenceKind.NormalGravity, 95f, CadenceGold, CadenceGravityCeiling);
            Checkpoint(100f);
            SpawnPickupAt(CadenceX(100f) + 1.6f, CadenceY(0.6f), PickupType.Medkit);

            // ---- 5. The ship: hold to climb, release to dive, weave between pillars ---------
            CadencePortal(CadenceKind.ShipOn, 102f, CadenceShipColor, CadenceShipCeiling);
            SpikeStrip(104f, 134f, 0f, false);
            SpikeStrip(104f, 134f, CadenceShipCeiling, true);
            FloorPillar(107f, 3.4f);                // over
            CeilingPillar(110f, 3.0f);              // under
            FloorPillar(113f, 3.4f);
            CeilingPillar(116f, 3.0f);
            FloorPillar(119.5f, 2.0f); CeilingPillar(119.5f, 4.6f); // through the middle
            FloorPillar(122.5f, 3.8f);
            CeilingPillar(125f, 2.4f);
            FloorPillar(127.5f, 1.8f); CeilingPillar(127.5f, 4.3f);
            Coin(2, 129.5f, 5.3f);                  // climb for it, then dive for the next gap
            CeilingPillar(132.5f, 2.6f);
            CadencePortal(CadenceKind.ShipOff, 136f, CadenceCubeColor, CadenceShipCeiling);
            Checkpoint(140f);

            // ---- 6. Faster: same rhythm, the world goes by a third quicker ------------------
            SpeedPortal(141f, CadenceFastSpeed);
            Spike(144f); Coins(144f);
            Spike(146f, 2);
            Spike(148f); Coins(148f);
            Platform(150.5f, 153.5f, 1f);
            Spike(152f, 1, 1f);                     // a spike on the ledge itself
            Pad(156f);                              // over the pit 156.3 - 158.4
            SpikeStrip(159.5f, 162f, 0f, false);
            Orb(160f, 2.3f);
            Spike(165f, 3); Coins(165.3f);
            Pad(167f);                              // too tall to jump: let the pad do it
            FloorPillar(168f, 2.0f);
            Checkpoint(170.5f);

            // ---- 7. Finale: a pad into three orbs over the widest pit, and home -------------
            Spike(173f); Coins(173f);
            Pad(174.3f);                            // over the pit 174.7 - 181.9
            Orb(175.68f, 4.05f); Orb(177.68f, 4.05f); Orb(179.68f, 4.05f);
            Spike(185.5f);
            CadencePortal(CadenceKind.End, 188f, CadenceGold, 4.2f);
        }

        // ---- builder vocabulary -------------------------------------------------------------

        void CadenceFloor(float from, float to)
        {
            if (to - from < 0.05f) return;
            const float depth = 3f;
            CadenceBlock(from, to, cadenceFloorY - depth, cadenceFloorY, registerLedge: true);
            segments.Add(new GroundSegment { xStart = from, xEnd = to, topY = cadenceFloorY, isGap = false, go = null });
        }

        void CadenceCeiling(float from, float to, float underside)
        {
            if (to - from < 0.05f) return;
            CadenceBlock(from, to, CadenceY(underside), CadenceY(underside) + 1f, registerLedge: true);
        }

        void Platform(float fromBeat, float toBeat, float top)
        {
            CadenceBlock(CadenceX(fromBeat), CadenceX(toBeat), cadenceFloorY, CadenceY(top), registerLedge: true);
        }

        void FloorPillar(float beat, float top)
        {
            float x = CadenceX(beat);
            CadenceBlock(x - 0.5f, x + 0.5f, cadenceFloorY, CadenceY(top), registerLedge: true);
        }

        void CeilingPillar(float beat, float bottom)
        {
            float x = CadenceX(beat);
            CadenceBlock(x - 0.5f, x + 0.5f, CadenceY(bottom), CadenceY(CadenceShipCeiling), registerLedge: true);
        }

        GameObject CadenceBlock(float xMin, float xMax, float yMin, float yMax, bool registerLedge)
        {
            var go = new GameObject("CadenceBlock");
            go.transform.SetParent(entityParent, false);
            go.transform.position = new Vector3((xMin + xMax) * 0.5f, (yMin + yMax) * 0.5f, 0f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = CadenceArt.Block;
            sr.drawMode = SpriteDrawMode.Sliced;
            sr.size = new Vector2(xMax - xMin, yMax - yMin);
            sr.color = CadenceGold;
            sr.sortingOrder = -1;
            var col = go.AddComponent<BoxCollider2D>();
            col.size = sr.size;
            cadenceObjects.Add(go);
            cadencePulse.Add(sr);
            if (registerLedge) cadenceBlocks.Add(Rect.MinMaxRect(xMin, yMin, xMax, yMax));
            return go;
        }

        /// <summary>
        /// One to three spikes, 0.8 m apart, standing on the floor (or on a ledge of the given
        /// height). The hitbox is far smaller than the drawing, as in Geometry Dash: a spike
        /// kills when you land in it, not when you brush its outline.
        /// </summary>
        void Spike(float beat, int count = 1, float height = 0f)
        {
            float x0 = CadenceX(beat);
            for (int i = 0; i < count; i++)
                AddSpike(x0 + i * 0.8f, CadenceY(height), hanging: false);
        }

        void CeilingSpike(float beat, int count = 1)
        {
            float x0 = CadenceX(beat);
            for (int i = 0; i < count; i++)
                AddSpike(x0 + i * 0.8f, CadenceY(CadenceGravityCeiling), hanging: true);
        }

        void AddSpike(float x, float baseY, bool hanging)
        {
            var go = new GameObject("CadenceSpike");
            go.transform.SetParent(entityParent, false);
            go.transform.position = new Vector3(x, baseY, 0f);
            go.transform.localScale = Vector3.one * 0.8f;
            if (hanging) go.transform.rotation = Quaternion.Euler(0f, 0f, 180f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = CadenceArt.Spike;
            sr.color = CadenceSpikeColor;
            sr.sortingOrder = -1;
            cadenceObjects.Add(go);

            const float w = 0.30f, h = 0.44f;
            var hit = hanging ? new Rect(x - w * 0.5f, baseY - h, w, h) : new Rect(x - w * 0.5f, baseY, w, h);
            cadenceElements.Add(new CadenceElement { kind = CadenceKind.Spike, x = x, y = baseY, hit = hit, go = go, sr = sr, baseColor = sr.color });
        }

        /// <summary>A continuous bed of spikes drawn as one tiled strip with one long hitbox.</summary>
        void SpikeStrip(float fromBeat, float toBeat, float height, bool hanging)
        {
            float a = CadenceX(fromBeat), b = CadenceX(toBeat);
            float baseY = CadenceY(height);
            var go = new GameObject("CadenceSpikeStrip");
            go.transform.SetParent(entityParent, false);
            go.transform.position = new Vector3((a + b) * 0.5f, baseY, 0f);
            go.transform.localScale = Vector3.one * 0.8f;
            if (hanging) go.transform.rotation = Quaternion.Euler(0f, 0f, 180f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = CadenceArt.Spike;
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.tileMode = SpriteTileMode.Continuous;
            sr.size = new Vector2((b - a) / 0.8f, 1f);
            sr.color = CadenceSpikeColor;
            sr.sortingOrder = -1;
            cadenceObjects.Add(go);

            const float h = 0.42f;
            var hit = hanging ? Rect.MinMaxRect(a + 0.12f, baseY - h, b - 0.12f, baseY) : Rect.MinMaxRect(a + 0.12f, baseY, b - 0.12f, baseY + h);
            cadenceElements.Add(new CadenceElement { kind = CadenceKind.Spike, x = a, y = baseY, hit = hit, go = go, sr = sr, baseColor = sr.color });
        }

        void Pad(float beat)
        {
            float x = CadenceX(beat);
            var go = new GameObject("CadencePad");
            go.transform.SetParent(entityParent, false);
            go.transform.position = new Vector3(x, cadenceFloorY, 0f);
            go.transform.localScale = new Vector3(0.95f, 0.8f, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = CadenceArt.Pad;
            sr.color = CadencePadColor;
            sr.sortingOrder = 1;
            cadenceObjects.Add(go);
            cadenceElements.Add(new CadenceElement { kind = CadenceKind.Pad, x = x, y = cadenceFloorY, go = go, sr = sr, baseColor = sr.color, baseScale = go.transform.localScale });
        }

        void Orb(float beat, float height)
        {
            float x = CadenceX(beat);
            var go = new GameObject("CadenceOrb");
            go.transform.SetParent(entityParent, false);
            go.transform.position = new Vector3(x, CadenceY(height), 0f);
            go.transform.localScale = Vector3.one * 1.1f;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = CadenceArt.Ring;
            sr.color = CadenceOrbColor;
            sr.sortingOrder = 1;
            cadenceObjects.Add(go);
            cadenceElements.Add(new CadenceElement { kind = CadenceKind.Orb, x = x, y = CadenceY(height), radius = 0.58f, go = go, sr = sr, baseColor = sr.color, baseScale = go.transform.localScale });
        }

        void CadencePortal(CadenceKind kind, float beat, Color color, float height, bool visualOnly = false)
        {
            float x = CadenceX(beat);
            var go = new GameObject($"CadencePortal_{kind}");
            go.transform.SetParent(entityParent, false);
            go.transform.position = new Vector3(x, CadenceY(height * 0.5f), 0f);
            go.transform.localScale = new Vector3(1f, height / 3f, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = CadenceArt.Portal;
            sr.color = color;
            sr.sortingOrder = 1;
            cadenceObjects.Add(go);
            if (visualOnly) { cadencePulse.Add(sr); return; }
            cadenceElements.Add(new CadenceElement { kind = kind, x = x, y = cadenceFloorY, go = go, sr = sr, baseColor = color, baseScale = go.transform.localScale });
        }

        void SpeedPortal(float beat, float speed)
        {
            CadencePortal(CadenceKind.Speed, beat, CadenceSpeedColor, 3.2f);
            cadenceElements[cadenceElements.Count - 1].value = speed;
        }

        void Checkpoint(float beat)
        {
            float x = CadenceX(beat);
            var go = new GameObject("CadenceCheckpoint");
            go.transform.SetParent(entityParent, false);
            go.transform.position = new Vector3(x, CadenceY(1.3f), 0f);
            go.transform.localScale = Vector3.one * 0.62f;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = CadenceArt.Diamond;
            sr.color = new Color(CadenceCheckColor.r, CadenceCheckColor.g, CadenceCheckColor.b, 0.55f);
            sr.sortingOrder = 0;
            cadenceObjects.Add(go);
            cadenceElements.Add(new CadenceElement
            {
                kind = CadenceKind.Checkpoint, x = x, y = cadenceFloorY + cadenceFeetOffset + 0.03f, value = beat,
                go = go, sr = sr, baseColor = sr.color, baseScale = go.transform.localScale,
            });
        }

        void Coin(int index, float beat, float height)
        {
            float x = CadenceX(beat);
            var go = new GameObject("CadenceSecretCoin");
            go.transform.SetParent(entityParent, false);
            go.transform.position = new Vector3(x, CadenceY(height), 0f);
            go.transform.localScale = Vector3.one * 0.9f;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = CadenceArt.Coin;
            sr.color = CadenceGold;
            sr.sortingOrder = 2;
            cadenceObjects.Add(go);
            cadenceElements.Add(new CadenceElement
            {
                kind = CadenceKind.Coin, x = x, y = CadenceY(height), radius = 0.45f, coinIndex = index,
                go = go, sr = sr, baseColor = sr.color, baseScale = go.transform.localScale,
            });
        }

        /// <summary>A small row of ordinary coins at the top of the jump over an obstacle.</summary>
        void Coins(float beat)
        {
            float x = CadenceX(beat);
            for (int i = -1; i <= 1; i++)
                SpawnPickupAt(x + i * 0.7f, CadenceY(2.3f - Mathf.Abs(i) * 0.25f), PickupType.Coin);
        }

        // =====================================================================================
        // Playing
        // =====================================================================================

        void UpdateCadence()
        {
            if (!cadenceBuilt) return;
            float px = player.transform.position.x;

            if (!cadenceActive)
            {
                if (!cadenceFinished && px >= cadenceStartX && px < cadenceEndX) StartCadence();
                // Once the run has moved well past it, the finished section is torn down.
                else if (cadenceFinished && px > cadenceEndX + CadenceRunOut + 25f) ClearCadence();
                return;
            }
            if (cadenceRespawning) return;

            float dt = Time.deltaTime;
            var b = player.Bounds;
            var body = Rect.MinMaxRect(b.min.x + 0.03f, b.min.y + 0.03f, b.max.x - 0.03f, b.max.y - 0.03f);
            float centerX = b.center.x;

            for (int i = 0; i < cadenceElements.Count; i++)
            {
                var e = cadenceElements[i];
                if (e.x > px + 6f) break;          // sorted by x: nothing further can touch yet
                if (e.kind != CadenceKind.Spike && e.passed) continue;

                switch (e.kind)
                {
                    case CadenceKind.Spike:
                        if (e.hit.xMax < px - 3f) continue;
                        if (e.hit.Overlaps(body)) { CadenceFail(); return; }
                        break;

                    case CadenceKind.Pad:
                        // Catches a runner and one just leaving the ground, not one flying over.
                        if (Mathf.Abs(centerX - e.x) < 0.3f && b.min.y <= e.y + 0.9f && b.min.y >= e.y - 0.3f && player.gravitySign > 0f)
                        {
                            e.passed = true;
                            player.RhythmLaunch(CadencePadVelocity);
                            Sfx.Spring();
                            Fx.Burst(new Vector3(e.x, e.y + 0.2f, 0f), CadencePadColor, 12, 3f, 0.09f, 0.2f);
                            if (e.go != null) e.go.transform.localScale = new Vector3(e.baseScale.x * 1.2f, e.baseScale.y * 0.6f, 1f);
                        }
                        break;

                    case CadenceKind.Orb:
                        if (CircleTouches(body, e.x, e.y, e.radius) && (player.JumpPressedRecently || player.JumpHeldNow))
                        {
                            e.passed = true;
                            player.RhythmLaunch(CadenceJumpVelocity);
                            Sfx.Bounce();
                            Fx.Burst(new Vector3(e.x, e.y, 0f), CadenceOrbColor, 14, 3.2f, 0.09f, 0f);
                            if (e.sr != null) e.sr.color = new Color(1f, 1f, 1f, 0.35f);
                        }
                        break;

                    case CadenceKind.Coin:
                        if (CircleTouches(body, e.x, e.y, e.radius))
                        {
                            e.passed = true;
                            cadenceCoins[e.coinIndex] = true;
                            Sfx.Milestone();
                            Fx.Burst(new Vector3(e.x, e.y, 0f), CadenceGold, 26, 4f, 0.12f, 0.3f);
                            Fx.Text(new Vector3(e.x, e.y + 0.7f, 0f), "PIÈCE SECRÈTE !", CadenceGold, 1.1f);
                            if (e.go != null) e.go.SetActive(false);
                        }
                        break;

                    default:
                        if (px < e.x) break;
                        e.passed = true;
                        OnCadenceGate(e);
                        if (!cadenceActive) return; // the end gate closes the section
                        break;
                }
            }

            // Out of the level: below the floor, or above the room when walking the ceiling.
            if (player.gravitySign > 0f ? b.max.y < cadenceFloorY - 2.5f : b.min.y > CadenceY(CadenceGravityCeiling) + 3.5f)
            {
                CadenceFail();
                return;
            }

            UpdateCadenceCrash(dt);
            if (!cadenceActive || cadenceRespawning) return;

            if (!cadenceMusicB && px >= cadenceArpX) SwapCadenceMusic(true);
            UpdateCadencePresentation();
        }

        static bool CircleTouches(Rect r, float cx, float cy, float radius)
        {
            float nx = Mathf.Clamp(cx, r.xMin, r.xMax), ny = Mathf.Clamp(cy, r.yMin, r.yMax);
            float dx = cx - nx, dy = cy - ny;
            return dx * dx + dy * dy <= radius * radius;
        }

        void OnCadenceGate(CadenceElement e)
        {
            switch (e.kind)
            {
                case CadenceKind.FlipGravity:
                    ApplyCadenceGravity(-1f);
                    GateFlash(e, "");
                    break;
                case CadenceKind.NormalGravity:
                    ApplyCadenceGravity(1f);
                    GateFlash(e, "");
                    break;
                case CadenceKind.ShipOn:
                    SetCadenceShip(true);
                    GateFlash(e, "MAINTIENS POUR VOLER");
                    break;
                case CadenceKind.ShipOff:
                    SetCadenceShip(false);
                    GateFlash(e, "");
                    break;
                case CadenceKind.Speed:
                    cadenceSpeed = e.value;
                    GateFlash(e, "PLUS VITE !");
                    Fx.Shake(0.25f, 0.3f);
                    break;
                case CadenceKind.Checkpoint:
                    cadenceCheckpoint = new CadenceCheckpoint
                    {
                        x = e.x, y = e.y, beat = e.value, speed = cadenceSpeed,
                        gravity = player.gravitySign, ship = cadenceShip,
                    };
                    if (e.sr != null) e.sr.color = new Color(CadenceCheckColor.r, CadenceCheckColor.g, CadenceCheckColor.b, 1f);
                    Fx.Burst(e.go != null ? e.go.transform.position : new Vector3(e.x, e.y, 0f), CadenceCheckColor, 16, 2.5f, 0.09f, 0f);
                    Sfx.Coin();
                    break;
                case CadenceKind.End:
                    FinishCadence();
                    break;
            }
        }

        void GateFlash(CadenceElement e, string label)
        {
            Sfx.Spring();
            var at = e.go != null ? e.go.transform.position : new Vector3(e.x, e.y, 0f);
            Fx.Burst(at, e.baseColor, 22, 4f, 0.1f, 0f);
            if (!string.IsNullOrEmpty(label)) Fx.Text(at + Vector3.up * 1.6f, label, e.baseColor, 1f);
        }

        /// <summary>
        /// A crash into a block's side. The physics simply stops the player there, so a crash
        /// shows up as the forced run making no progress. Before calling it one, give the
        /// Geometry Dash corner grace: if the ledge in front is barely above the feet (or
        /// barely below the head upside down), the player is lifted onto it instead.
        /// </summary>
        void UpdateCadenceCrash(float dt)
        {
            cadenceStuckTimer += dt;
            if (cadenceStuckTimer < 0.1f) return;
            float px = player.transform.position.x;
            float progress = px - cadenceStuckRefX;
            float expected = cadenceSpeed * cadenceStuckTimer;
            cadenceStuckTimer = 0f;
            cadenceStuckRefX = px;
            if (progress >= expected * 0.3f) return;

            if (TryCadenceLedgeGrace()) return;
            CadenceFail();
        }

        bool TryCadenceLedgeGrace()
        {
            var b = player.Bounds;
            foreach (var r in cadenceBlocks)
            {
                if (r.xMin < b.max.x - 0.05f || r.xMin > b.max.x + 0.3f) continue;
                if (player.gravitySign > 0f)
                {
                    float lift = r.yMax - b.min.y;
                    if (lift <= 0f || lift > 0.35f) continue;
                    NudgePlayer(lift + 0.03f);
                    return true;
                }
                else
                {
                    float drop = b.max.y - r.yMin;
                    if (drop <= 0f || drop > 0.35f) continue;
                    NudgePlayer(-(drop + 0.03f));
                    return true;
                }
            }
            return false;
        }

        void NudgePlayer(float dy)
        {
            var p = player.transform.position + new Vector3(0.05f, dy, 0f);
            player.transform.position = p;
            var rb = player.GetComponent<Rigidbody2D>();
            if (rb != null) rb.position = p;
        }

        // ---- start, crash, finish ------------------------------------------------------------

        void StartCadence()
        {
            cadenceActive = true;
            cadenceRespawning = false;
            cadenceAttempts = 1;
            cadenceSpeed = CadenceSpeed;
            cadenceShip = false;
            cadenceColliderOffset = player.collider2d != null ? player.collider2d.offset : Vector2.zero;

            player.rhythmMode = true;
            player.rhythmJumpVelocity = CadenceJumpVelocity;
            player.gravityScale = CadenceGravityScale;
            ApplyCadenceGravity(1f);

            cadenceCheckpoint = new CadenceCheckpoint
            {
                x = cadenceStartX, y = cadenceFloorY + cadenceFeetOffset + 0.03f, beat = 0f,
                speed = CadenceSpeed, gravity = 1f, ship = false,
            };

            if (cadenceBody != null) cadenceBody.Detach();
            cadenceBody = CadenceBody.Attach(player, CadenceGold);
            SetupCadenceCamera(true);
            StartCadenceMusic(0f);
            cadenceStuckTimer = 0f;
            cadenceStuckRefX = player.transform.position.x;

            // The sector banner already named the section a few metres back; this one only
            // says go, and gets out of the way before the first spike (four beats in).
            ui.ShowBanner("C'EST PARTI", "Appuie sur SAUT au rythme de la musique", 1.5f);
            UpdateCadencePresentation();
        }

        void CadenceFail()
        {
            if (!cadenceActive || cadenceRespawning) return;
            var at = player.transform.position;
            Fx.Burst(at, CadenceSpikeColor, 26, 5f, 0.13f, 0.4f);
            Fx.Burst(at, Color.white, 12, 3f, 0.08f, 0f);
            Fx.Shake(0.35f, 0.25f);
            Sfx.Death();

            if (player.health != null) player.health.Decrement(1);
            if (player.health != null && !player.health.IsAlive)
            {
                // Out of hearts: the run ends the usual way (PlayerDeath -> HandlePlayerDeath).
                AbortCadence();
                return;
            }
            cadenceAttempts++;
            StartCoroutine(CadenceRespawnRoutine());
        }

        IEnumerator CadenceRespawnRoutine()
        {
            cadenceRespawning = true;
            player.controlEnabled = false;
            player.velocity = Vector2.zero;
            if (cadenceBody != null) cadenceBody.SetVisible(false);
            if (cadenceMusicSource != null) cadenceMusicSource.Pause();

            yield return new WaitForSeconds(0.6f);
            if (!cadenceActive || player == null) yield break;

            var cp = cadenceCheckpoint;
            SetCadenceShip(cp.ship);
            ApplyCadenceGravity(cp.gravity);
            cadenceSpeed = cp.speed;
            // Everything after the checkpoint is live again, except coins already taken.
            foreach (var e in cadenceElements)
            {
                if (e.x <= cp.x + 0.01f || e.kind == CadenceKind.Coin) continue;
                e.passed = false;
                if (e.sr != null) e.sr.color = e.baseColor;
                if (e.go != null && e.baseScale != Vector3.zero) e.go.transform.localScale = e.baseScale;
            }

            PlacePlayerAt(cp.x, cp.y);
            player.velocity = Vector2.zero;
            if (cadenceBody != null) { cadenceBody.ResetPose(); cadenceBody.SetVisible(true); }
            StartCadenceMusic(cp.beat);
            cadenceStuckTimer = 0f;
            cadenceStuckRefX = cp.x;
            player.controlEnabled = true;
            cadenceRespawning = false;

            Fx.Text(new Vector3(cp.x, cp.y + 1.4f, 0f), $"TENTATIVE {cadenceAttempts}", ApogeeTheme.Cream, 1.1f);
        }

        void FinishCadence()
        {
            RestoreFromCadence();
            cadenceActive = false;
            cadenceFinished = true;

            int coinsFound = 0;
            foreach (bool c in cadenceCoins) if (c) coinsFound++;
            const int coinReward = 25;
            int materialReward = coinsFound * 5;
            SaveSystem.AddCoins(coinReward);
            if (materialReward > 0) SaveSystem.AddMaterials(materialReward);

            Sfx.Milestone();
            Fx.Burst(player.transform.position, CadenceGold, 40, 6f, 0.14f, 0.4f);
            string tries = cadenceAttempts == 1 ? "sans une seule chute !" : $"en {cadenceAttempts} tentatives";
            ui.ShowBanner("CADENCE TERMINÉE", $"{tries}  ·  {coinsFound}/3 secrètes  ·  +{coinReward} [c]" +
                                              (materialReward > 0 ? $"   +{materialReward} [g]" : ""), 3.2f);
        }

        /// <summary>Stops the section without rewards (death, leaving the run).</summary>
        void AbortCadence()
        {
            if (!cadenceActive) return;
            RestoreFromCadence();
            cadenceActive = false;
            cadenceRespawning = false;
        }

        void RestoreFromCadence()
        {
            StopCadenceMusic();
            if (player != null)
            {
                player.rhythmMode = false;
                player.gravityScale = 1f;
                player.jetpackActive = false;
                player.jetpackCeilingY = float.MaxValue;
                player.maxFallSpeed = 16f;
                player.SetGravitySign(1f);
                if (player.collider2d != null) player.collider2d.offset = cadenceColliderOffset;
                // Leaving mid-respawn would otherwise strand the player without control.
                if (running) player.controlEnabled = true;
            }
            cadenceShip = false;
            if (cadenceBody != null) { cadenceBody.Detach(); cadenceBody = null; }
            SetupCadenceCamera(false);
            ui.ClearSectionHud();
            SkyBackdrop.Instance?.SetTint(ZoneCatalog.Get(activeZone).Tint);
        }

        /// <summary>Removes the section's objects entirely (a new run, or long after finishing).</summary>
        void ClearCadence()
        {
            if (cadenceActive) AbortCadence();
            StopAllCadenceRoutines();
            foreach (var go in cadenceObjects) if (go != null) Destroy(go);
            cadenceObjects.Clear();
            cadenceElements.Clear();
            cadenceBlocks.Clear();
            cadencePulse.Clear();
            cadenceBuilt = false;
            cadenceFinished = false;
            cadenceRespawning = false;
        }

        void StopAllCadenceRoutines()
        {
            // The respawn coroutine is the only one; it checks cadenceActive after its wait.
            cadenceRespawning = false;
        }

        // ---- physics modes ---------------------------------------------------------------------

        /// <summary>
        /// Flips gravity, and the collider's offset with it: the player's box sits low in its
        /// sprite, so upside down it must sit high, or the character would hang half inside
        /// the ceiling it stands on.
        /// </summary>
        void ApplyCadenceGravity(float sign)
        {
            player.SetGravitySign(sign);
            if (player.collider2d != null)
                player.collider2d.offset = new Vector2(cadenceColliderOffset.x, sign < 0f ? -cadenceColliderOffset.y : cadenceColliderOffset.y);
        }

        void SetCadenceShip(bool on)
        {
            cadenceShip = on;
            player.jetpackActive = on;
            player.jetpackCeilingY = float.MaxValue;
            player.gravityScale = on ? CadenceShipGravityScale : CadenceGravityScale;
            player.maxFallSpeed = on ? CadenceShipMaxFall : 16f;
            if (cadenceBody != null)
            {
                cadenceBody.shipMode = on;
                cadenceBody.SetTrailColor(on ? CadenceShipColor : CadenceGold);
            }
        }

        // ---- camera --------------------------------------------------------------------------

        /// <summary>
        /// Zoom that shows about eleven metres ahead of the player, who is pushed to the left
        /// quarter of the screen. Landscape needs little; portrait needs a lot, capped so the
        /// character never shrinks to nothing.
        /// </summary>
        float CadenceOrtho()
        {
            float aspect = mainCamera != null ? Mathf.Max(0.3f, mainCamera.aspect) : 1.78f;
            return Mathf.Clamp(11f / (1.5f * aspect), 4.6f, 8.5f);
        }

        void SetupCadenceCamera(bool on)
        {
            if (composer == null) return;
            if (on)
            {
                var la = composer.Lookahead;
                cadenceLookaheadWas = la.Enabled;
                la.Enabled = false;
                composer.Lookahead = la;
                cadenceDampingWas = composer.Damping;
                composer.Damping = new Vector3(0f, cadenceDampingWas.y, cadenceDampingWas.z);
                float aspect = mainCamera != null ? Mathf.Max(0.3f, mainCamera.aspect) : 1.78f;
                Fx.SetCameraRestOffset(new Vector3(CadenceOrtho() * aspect * 0.5f, 0.9f, 0f));
            }
            else
            {
                var la = composer.Lookahead;
                la.Enabled = cadenceLookaheadWas;
                composer.Lookahead = la;
                if (cadenceDampingWas != Vector3.zero) composer.Damping = cadenceDampingWas;
                Fx.SetCameraRestOffset(new Vector3(0.6f, 0.5f, 0f));
            }
        }

        // ---- music -----------------------------------------------------------------------------

        void StartCadenceMusic(float beat)
        {
            PauseSceneMusic();
            if (cadenceMusicSource == null)
            {
                var go = new GameObject("CadenceMusic");
                go.transform.SetParent(transform, false);
                cadenceMusicSource = go.AddComponent<AudioSource>();
                cadenceMusicSource.loop = true;
                cadenceMusicSource.playOnAwake = false;
                cadenceMusicSource.spatialBlend = 0f;
                cadenceMusicSource.volume = 0.85f;
            }
            cadenceMusicB = beat >= CadenceArpBeat;
            cadenceMusicSource.clip = cadenceMusicB ? CadenceMusic.LoopB : CadenceMusic.LoopA;
            cadenceMusicSource.Play();
            cadenceMusicSource.time = Mathf.Repeat(beat * CadenceBeatDuration, CadenceMusic.LoopDuration);
        }

        /// <summary>Both loops share the same grid, so switching keeps the exact position.</summary>
        void SwapCadenceMusic(bool toB)
        {
            if (cadenceMusicSource == null) return;
            float t = cadenceMusicSource.time;
            cadenceMusicB = toB;
            cadenceMusicSource.clip = toB ? CadenceMusic.LoopB : CadenceMusic.LoopA;
            cadenceMusicSource.Play();
            cadenceMusicSource.time = t;
        }

        void StopCadenceMusic()
        {
            if (cadenceMusicSource != null) cadenceMusicSource.Stop();
            foreach (var src in cadencePausedMusic) if (src != null) src.UnPause();
            cadencePausedMusic.Clear();
        }

        /// <summary>The scene's own background music steps aside while the section plays its track.</summary>
        void PauseSceneMusic()
        {
            if (cadencePausedMusic.Count > 0) return;
            foreach (var src in FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
            {
                if (src == cadenceMusicSource || !src.isPlaying || !src.loop) continue;
                src.Pause();
                cadencePausedMusic.Add(src);
            }
        }

        // ---- presentation ------------------------------------------------------------------

        /// <summary>
        /// Everything that breathes with the music: the rims of blocks and spikes flare on
        /// each kick, orbs swell, the coins spin, the sky brightens; plus the HUD progress bar
        /// and attempt counter that Geometry Dash players look for.
        /// </summary>
        void UpdateCadencePresentation()
        {
            float musicTime = cadenceMusicSource != null && cadenceMusicSource.isPlaying ? cadenceMusicSource.time : 0f;
            float frac = Mathf.Repeat(musicTime / CadenceBeatDuration, 1f);
            float pulse = Mathf.Exp(-frac * 7f);

            float rim = 0.72f + 0.5f * pulse;
            foreach (var sr in cadencePulse)
                if (sr != null) sr.color = new Color(CadenceGold.r * rim, CadenceGold.g * rim, CadenceGold.b * rim, 1f);

            float px = player.transform.position.x;
            foreach (var e in cadenceElements)
            {
                if (e.sr == null || Mathf.Abs(e.x - px) > 30f) continue;
                switch (e.kind)
                {
                    case CadenceKind.Spike:
                        float k = 0.78f + 0.4f * pulse;
                        e.sr.color = new Color(e.baseColor.r * k, e.baseColor.g * k, e.baseColor.b * k, 1f);
                        break;
                    case CadenceKind.Orb:
                        if (!e.passed) e.go.transform.localScale = e.baseScale * (1f + 0.14f * pulse);
                        e.go.transform.Rotate(0f, 0f, -90f * Time.deltaTime);
                        break;
                    case CadenceKind.Coin:
                        e.go.transform.localScale = new Vector3(e.baseScale.x * Mathf.Cos(Time.time * 3f), e.baseScale.y, 1f);
                        break;
                    case CadenceKind.Pad:
                        e.go.transform.localScale = Vector3.Lerp(e.go.transform.localScale, e.baseScale, 1f - Mathf.Exp(-10f * Time.deltaTime));
                        break;
                }
            }

            var tint = ZoneCatalog.Get(ZoneKind.Cadence).Tint;
            SkyBackdrop.Instance?.SetTint(tint * (0.9f + 0.18f * pulse), true);

            float progress = Mathf.Clamp01((px - cadenceStartX) / Mathf.Max(1f, cadenceEndX - cadenceStartX));
            int coins = 0;
            foreach (bool c in cadenceCoins) if (c) coins++;
            ui.SetZoneLabel($"LA CADENCE   {Mathf.FloorToInt(progress * 100f)} %");
            ui.SetSectionHud(progress, $"Tentative {cadenceAttempts}   ·   secrètes {coins}/3");
        }
    }
}
