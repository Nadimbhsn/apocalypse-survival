using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Platformer.Survival
{
    /// <summary>
    /// "LE BOLIDE": the runner's Rider section. The character jumps into a small car on a
    /// golden track of ramps, gaps, bumps and drops, and one button does everything, as in
    /// the original: held on the ground it accelerates, held in the air it spins the car
    /// backwards. Land with the car roughly parallel to the slope, or crash. Full turns in
    /// the air are saltos and pay a bonus.
    ///
    /// The track is generated when the section is built, from the activity level (see
    /// ActivityLevel): more features, faster gaps, steeper drops and, from level 2, kickers
    /// whose landing only a full salto fits. The generator and the physics are the ones a
    /// bot proved on hundreds of tracks per level: every track is finishable from its start
    /// and from each checkpoint, with a maximum difficulty that stays that way.
    ///
    /// The car is not a physics body: it runs in fixed 1/60 s steps along the track's
    /// polylines and flies on simple ballistics, and the player object is carried along
    /// (its own controller switched off) so the camera, pickups and the drone follow it.
    /// A crash costs a heart and restarts at the last checkpoint.
    /// </summary>
    public partial class SurvivalDirector
    {
        // ---- physics (the bot's numbers, do not tune one without the other) ----------------
        const float RiderGravity = 22f;
        const float RiderSlopeGravity = 0.55f;
        const float RiderAccel = 9f;
        const float RiderDrag = 2.5f;
        const float RiderVMin = 6f, RiderVMax = 14f;
        const float RiderSpin = 400f;
        const float RiderLandTolerance = 55f;
        const float RiderFallMargin = 6f;
        const float RiderStep = 1f / 60f;
        const float RiderLeadIn = 8f;
        const float RiderRunOut = 20f;
        const float RiderDepth = 9f;

        static readonly Color RiderGold = new Color(1f, 0.78f, 0.36f);
        static readonly Color RiderCrimson = new Color(0.78f, 0.16f, 0.12f);

        class RiderTrack
        {
            public readonly List<List<Vector2>> pieces = new();
            public readonly List<float> checks = new();
            public readonly List<(Vector2 lip, float speed, float angle)> arcs = new();
            public float endX, minY;

            public int PieceAt(float x)
            {
                for (int i = 0; i < pieces.Count; i++)
                {
                    var p = pieces[i];
                    if (p[0].x <= x && x <= p[p.Count - 1].x) return i;
                }
                return -1;
            }

            /// <summary>Height and slope (degrees) of piece i at x.</summary>
            public (float y, float angle) Ground(int i, float x)
            {
                var p = pieces[i];
                int lo = 0, hi = p.Count - 1;
                while (hi - lo > 1)
                {
                    int m = (lo + hi) / 2;
                    if (p[m].x <= x) lo = m; else hi = m;
                }
                var a = p[lo];
                var b = p[hi];
                float t = Mathf.Approximately(a.x, b.x) ? 0f : (x - a.x) / (b.x - a.x);
                return (a.y + (b.y - a.y) * t, Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg);
            }
        }

        struct RiderState
        {
            public float x, y, vx, vy, v, phi, spin;
            public int piece;   // -1 while airborne
        }

        RiderTrack riderTrack;
        RiderState rider;
        bool riderBuilt, riderActive, riderFinished, riderRespawning;
        float riderStartX, riderOriginY, riderCheckX, riderAccumulator, riderLevel;
        int riderAttempts, riderFlips, riderPerfects;
        readonly List<GameObject> riderObjects = new();
        readonly List<Mesh> riderMeshes = new();
        Transform riderCar, riderPivot, riderWheelA, riderWheelB;
        TrailRenderer riderTrail;
        SpriteRenderer riderHidden;
        float riderFeet;
        static Material riderMaterial;

        bool InRiderSpan(float x) => riderBuilt && riderTrack != null && x >= riderStartX - RiderLeadIn - 30f && x <= riderTrack.endX + RiderRunOut;

        // =====================================================================================
        // Generation (a port of the bot's generator)
        // =====================================================================================

        class RiderGen
        {
            readonly float L;
            public readonly RiderTrack t = new();
            List<Vector2> cur = new();
            float x, y;

            public RiderGen(float level)
            {
                L = level;
                cur.Add(Vector2.zero);
            }

            float Lerp(float a, float b) => a + (b - a) * L;

            void Seg(float dx, float slopeFrom, float slopeTo, float step = 0.5f)
            {
                int n = Mathf.Max(1, Mathf.CeilToInt(dx / step));
                for (int k = 1; k <= n; k++)
                {
                    float f = (k - 0.5f) / n;
                    float s = slopeFrom + (slopeTo - slopeFrom) * f;
                    x += dx / n;
                    y += s * dx / n;
                    cur.Add(new Vector2(x, y));
                }
            }

            void Flat(float dx) => Seg(dx, 0f, 0f);

            void Hill(float dx, float h)
            {
                int n = Mathf.Max(4, (int)(dx / 0.5f));
                float x0 = x, y0 = y;
                for (int k = 1; k <= n; k++)
                {
                    float f = k / (float)n;
                    float s = Mathf.Sin(Mathf.PI * f);
                    x = x0 + dx * f;
                    y = y0 + h * s * s;
                    cur.Add(new Vector2(x, y));
                }
            }

            void Gap(float w, float dy)
            {
                t.pieces.Add(cur);
                x += w;
                y += dy;
                cur = new List<Vector2> { new Vector2(x, y) };
            }

            void Check()
            {
                Flat(3f);
                t.checks.Add(x);
                Flat(5f);
            }

            /// <summary>Horizontal distance, and angle, where a car leaving at v and ang (deg) comes down dy.</summary>
            static bool Cross(float v, float ang, float dy, out float dist, out float landAngle)
            {
                float vx = v * Mathf.Cos(ang * Mathf.Deg2Rad), vy = v * Mathf.Sin(ang * Mathf.Deg2Rad);
                float disc = vy * vy - 2f * RiderGravity * dy;
                dist = 0f;
                landAngle = 0f;
                if (disc < 0f) return false;
                float time = (vy + Mathf.Sqrt(disc)) / RiderGravity;
                dist = vx * time;
                landAngle = Mathf.Atan2(vy - RiderGravity * time, vx) * Mathf.Rad2Deg;
                return true;
            }

            /// <summary>
            /// A curved ramp up, a gap and a landing slope. The gap needs a take-off speed of
            /// at least vReq (holding on the ramp gets there); the landing is long enough for
            /// anything up to top speed. A "flip" kicker is steep and lands on a slope the
            /// take-off angle does not fit: only a full salto lands it.
            /// </summary>
            void Kicker(bool flip)
            {
                float ang = flip ? Random.Range(38f, 45f) : Random.Range(Lerp(14f, 22f), Lerp(24f, 34f));
                float ramp = Random.Range(4f, 5.5f);
                Seg(ramp, 0f, Mathf.Tan(ang * Mathf.Deg2Rad));
                float dy = flip ? -Random.Range(2f, 4f) : -Random.Range(0.5f, Lerp(2f, 4f));
                float vReq = flip ? Lerp(8.5f, 10.5f) : Lerp(7.5f, 11f);
                Cross(vReq, ang, dy, out float xa, out float gamA);
                Cross(RiderVMax, ang, dy, out float xb, out float gamB);
                float w = xa - 0.6f;
                float landLen = (xb - w) + 5f;
                float theta = flip ? -Random.Range(25f, 32f) : Mathf.Max(-38f, (gamA + gamB) / 2f * 0.75f);

                var lip = new Vector2(x, y);
                Gap(w, dy);
                float k = Mathf.Tan(theta * Mathf.Deg2Rad);
                Seg(landLen * 0.6f, k, k);
                Seg(landLen * 0.4f + 3f, k, 0f);
                t.arcs.Add((lip, (vReq + RiderVMax) / 2f, ang));
            }

            void Drop()
            {
                Flat(Random.Range(2f, 4f));
                float dy = -Random.Range(Lerp(2f, 3f), Lerp(3.5f, 6f));
                float w = Random.Range(1f, Lerp(2.5f, 4.5f));
                Cross(RiderVMin, 0f, dy, out _, out float gam);
                Gap(w, dy);
                float theta = Mathf.Clamp(gam * 0.6f, -35f, -12f);
                float k = Mathf.Tan(theta * Mathf.Deg2Rad);
                Seg(6f, k, k);
                Seg(4f, k, 0f);
            }

            void Bumps()
            {
                int n = Random.Range(2, 4);
                for (int i = 0; i < n; i++)
                    Hill(Random.Range(7f, 10f), Random.Range(Lerp(0.8f, 1.4f), Lerp(1.4f, 2.4f)));
            }

            void Valley() => Hill(Random.Range(12f, 16f), -Random.Range(2.5f, Lerp(3.5f, 5.5f)));

            public RiderTrack Build()
            {
                Flat(12f);
                int features = Mathf.RoundToInt(Lerp(6f, 9f));
                int perCheck = L < 0.5f ? 3 : 4;
                float flipChance = L < 0.25f ? 0f : Lerp(0.1f, 0.45f);
                for (int i = 0; i < features; i++)
                {
                    // Never sink too far below the run: climb back up gently first.
                    while (y < -6f)
                    {
                        Seg(10f, 0f, 0.35f);
                        Seg(8f, 0.35f, 0f);
                    }
                    float roll = Random.value;
                    if (roll < flipChance) Kicker(true);
                    else if (roll < flipChance + 0.35f) Kicker(false);
                    else if (roll < flipChance + 0.55f) Drop();
                    else if (roll < flipChance + 0.8f) Bumps();
                    else Valley();
                    Flat(Random.Range(2.5f, 4f));
                    if ((i + 1) % perCheck == 0 && i < features - 1) Check();
                }
                // Home: back to the height the section started at, then a short flat.
                float dy = -y;
                float len = Mathf.Max(8f, Mathf.Abs(dy) * 2.5f);
                Seg(len, dy / len * 2f, 0f);
                Flat(4f);
                t.pieces.Add(cur);
                t.endX = x - 1f;

                t.minY = float.MaxValue;
                foreach (var p in t.pieces)
                    foreach (var q in p)
                        t.minY = Mathf.Min(t.minY, q.y);
                return t;
            }
        }

        // =====================================================================================
        // Building
        // =====================================================================================

        /// <summary>
        /// Lays the whole section at once: a short lead-in of ordinary ground, the track (in
        /// its own coordinates, shifted to start here), and ordinary ground again beyond it
        /// where the character gets out and runs on.
        /// </summary>
        void BuildRider(float xStart)
        {
            ClearRider();
            riderBuilt = true;
            riderFinished = false;
            riderLevel = inCampaign ? Ramp : genLevel;
            riderOriginY = lastTopY;

            GenerateSegment(xStart, RiderLeadIn, lastTopY, allowBonusPlatform: false);
            riderStartX = xStart + RiderLeadIn;

            var local = new RiderGen(riderLevel).Build();
            // Shift to world coordinates.
            riderTrack = new RiderTrack();
            var offset = new Vector2(riderStartX, riderOriginY);
            foreach (var p in local.pieces)
            {
                var wp = new List<Vector2>(p.Count);
                foreach (var q in p) wp.Add(q + offset);
                riderTrack.pieces.Add(wp);
            }
            foreach (float c in local.checks) riderTrack.checks.Add(c + offset.x);
            riderTrack.endX = local.endX + offset.x;
            riderTrack.minY = local.minY + offset.y;
            foreach (var (lip, speed, angle) in local.arcs) riderTrack.arcs.Add((lip + offset, speed, angle));

            foreach (var p in riderTrack.pieces) BuildRiderPiece(p);
            foreach (float c in riderTrack.checks) BuildRiderCheckpoint(c);
            foreach (var arc in riderTrack.arcs) PlaceRiderArcCoins(arc.lip, arc.speed, arc.angle);
            BuildRiderArch(riderStartX, CadenceGold);
            BuildRiderArch(riderTrack.endX, RiderGold);

            // The ordinary run picks up right where the track ends.
            var last = riderTrack.pieces[riderTrack.pieces.Count - 1];
            float groundX = last[last.Count - 1].x;
            GenerateSegment(groundX, RiderRunOut, riderOriginY, allowBonusPlatform: false);
            lastTopY = riderOriginY;
            lastSegmentWasGap = false;
            lastSegmentWasUnstable = false;
            lastSegmentWidth = RiderRunOut;
        }

        static Material RiderMaterial => riderMaterial != null ? riderMaterial : riderMaterial = new Material(Shader.Find("Sprites/Default"));

        /// <summary>One solid stretch of track: a filled band under a golden rail.</summary>
        void BuildRiderPiece(List<Vector2> pts)
        {
            int n = pts.Count;
            if (n < 2) return;
            var verts = new Vector3[n * 2];
            var cols = new Color[n * 2];
            var tris = new int[(n - 1) * 6];
            var top = new Color(0.36f, 0.12f, 0.12f);
            var bottom = new Color(0.12f, 0.03f, 0.05f, 0f);
            for (int i = 0; i < n; i++)
            {
                verts[i * 2] = new Vector3(pts[i].x, pts[i].y, 0f);
                verts[i * 2 + 1] = new Vector3(pts[i].x, pts[i].y - RiderDepth, 0f);
                cols[i * 2] = top;
                cols[i * 2 + 1] = bottom;
            }
            for (int i = 0; i < n - 1; i++)
            {
                int a = i * 2, t = i * 6;
                tris[t] = a; tris[t + 1] = a + 2; tris[t + 2] = a + 1;
                tris[t + 3] = a + 1; tris[t + 4] = a + 2; tris[t + 5] = a + 3;
            }
            var mesh = new Mesh { name = "RiderTrack" };
            mesh.vertices = verts;
            mesh.colors = cols;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            riderMeshes.Add(mesh);

            var go = new GameObject("RiderTrack");
            go.transform.SetParent(entityParent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = RiderMaterial;
            mr.sortingOrder = -1;
            riderObjects.Add(go);

            var rail = new GameObject("RiderRail");
            rail.transform.SetParent(entityParent, false);
            var lr = rail.AddComponent<LineRenderer>();
            lr.sharedMaterial = RiderMaterial;
            lr.useWorldSpace = true;
            lr.positionCount = n;
            for (int i = 0; i < n; i++) lr.SetPosition(i, new Vector3(pts[i].x, pts[i].y, 0f));
            lr.widthMultiplier = 0.16f;
            lr.startColor = lr.endColor = RiderGold;
            lr.numCapVertices = 3;
            lr.numCornerVertices = 2;
            lr.sortingOrder = 0;
            riderObjects.Add(rail);

            // Register it as ground for the rest of the director (decor, death line queries).
            float minY = float.MaxValue;
            foreach (var p in pts) minY = Mathf.Min(minY, p.y);
            segments.Add(new GroundSegment { xStart = pts[0].x, xEnd = pts[n - 1].x, topY = minY, isGap = false, go = null });
        }

        void BuildRiderCheckpoint(float x)
        {
            int i = riderTrack.PieceAt(x);
            if (i < 0) return;
            var (y, _) = riderTrack.Ground(i, x);
            var go = new GameObject("RiderCheckpoint");
            go.transform.SetParent(entityParent, false);
            go.transform.position = new Vector3(x, y + 1.3f, 0f);
            go.transform.localScale = Vector3.one * 0.62f;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = CadenceArt.Diamond;
            sr.color = new Color(CadenceCheckColor.r, CadenceCheckColor.g, CadenceCheckColor.b, 0.55f);
            sr.sortingOrder = 0;
            riderObjects.Add(go);
        }

        void BuildRiderArch(float x, Color color)
        {
            int i = riderTrack.PieceAt(x);
            float y = i >= 0 ? riderTrack.Ground(i, x).y : riderOriginY;
            var go = new GameObject("RiderArch");
            go.transform.SetParent(entityParent, false);
            go.transform.position = new Vector3(x, y + 2.1f, 0f);
            go.transform.localScale = new Vector3(1f, 4.2f / 3f, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = CadenceArt.Portal;
            sr.color = color;
            sr.sortingOrder = 1;
            riderObjects.Add(go);
        }

        /// <summary>Three coins along the arc a car takes at a good speed over a gap.</summary>
        void PlaceRiderArcCoins(Vector2 lip, float speed, float angle)
        {
            float vx = speed * Mathf.Cos(angle * Mathf.Deg2Rad), vy = speed * Mathf.Sin(angle * Mathf.Deg2Rad);
            float apex = vy / RiderGravity;
            for (int k = 1; k <= 3; k++)
            {
                float t = apex * (0.5f + 0.5f * k);
                SpawnPickupAt(lip.x + vx * t, lip.y + vy * t - 0.5f * RiderGravity * t * t + 0.6f, PickupType.Coin);
            }
        }

        // =====================================================================================
        // Playing
        // =====================================================================================

        static bool RiderHoldInput()
        {
            if (MobileInput.JumpHeld) return true;
            var kb = Keyboard.current;
            if (kb != null && (kb.spaceKey.isPressed || kb.upArrowKey.isPressed || kb.wKey.isPressed || kb.zKey.isPressed)) return true;
            var pad = Gamepad.current;
            return pad != null && pad.buttonSouth.isPressed;
        }

        void UpdateRider()
        {
            if (!riderBuilt) return;
            float px = player.transform.position.x;
            if (!riderActive)
            {
                if (!riderFinished && px >= riderStartX && px < riderTrack.endX) StartRider();
                else if (riderFinished && px > riderTrack.endX + RiderRunOut + 25f) ClearRider();
                return;
            }
            if (riderRespawning) return;

            bool hold = RiderHoldInput();
            riderAccumulator += Time.deltaTime;
            while (riderAccumulator >= RiderStep && riderActive && !riderRespawning)
            {
                riderAccumulator -= RiderStep;
                if (!RiderStepOnce(hold)) { RiderCrash(); return; }
                if (rider.x >= riderTrack.endX) { FinishRider(); return; }
            }
            PlaceRiderPlayer();
            UpdateRiderPresentation(hold);
        }

        /// <summary>One fixed physics step. False when the car crashed.</summary>
        bool RiderStepOnce(bool hold)
        {
            var s = rider;
            float dt = RiderStep;
            if (s.piece >= 0)
            {
                var (_, th) = riderTrack.Ground(s.piece, s.x);
                float rad = th * Mathf.Deg2Rad;
                s.v += (hold ? RiderAccel : -RiderDrag) * dt - RiderGravity * RiderSlopeGravity * Mathf.Sin(rad) * dt;
                s.v = Mathf.Clamp(s.v, RiderVMin, RiderVMax);
                float nx = s.x + s.v * Mathf.Cos(rad) * dt;
                var p = riderTrack.pieces[s.piece];
                if (nx > p[p.Count - 1].x)
                {
                    // Off the lip.
                    s.vx = s.v * Mathf.Cos(rad);
                    s.vy = s.v * Mathf.Sin(rad);
                    s.y += s.vy * dt;
                    s.x = nx;
                    s.phi = th;
                    s.piece = -1;
                    s.spin = 0f;
                    rider = s;
                    return true;
                }
                var (ny, nth) = riderTrack.Ground(s.piece, nx);
                // Over a crest faster than gravity can hold the car down: it takes off.
                float vyb = s.v * Mathf.Sin(rad) - RiderGravity * dt;
                float yb = s.y + s.v * Mathf.Sin(rad) * dt - 0.5f * RiderGravity * dt * dt;
                if (ny < yb - 0.03f)
                {
                    s.vx = s.v * Mathf.Cos(rad);
                    s.vy = vyb;
                    s.x = nx;
                    s.y = yb;
                    s.phi = th;
                    s.piece = -1;
                    s.spin = 0f;
                    rider = s;
                    return true;
                }
                s.x = nx;
                s.y = ny;
                s.phi = nth;
                rider = s;
                return true;
            }

            // In the air.
            s.vy -= RiderGravity * dt;
            float ax = s.x + s.vx * dt, ay = s.y + s.vy * dt;
            if (hold)
            {
                s.phi += RiderSpin * dt;
                s.spin += RiderSpin * dt;
            }
            int i = riderTrack.PieceAt(ax);
            if (i >= 0)
            {
                var (gy, th) = riderTrack.Ground(i, ax);
                if (ay <= gy)
                {
                    // Coming in under the front edge of the next stretch is a wall.
                    if (riderTrack.pieces[i][0].x > s.x && ay < gy - 0.4f) return false;
                    float d = Mathf.DeltaAngle(th, s.phi);
                    if (Mathf.Abs(d) > RiderLandTolerance) return false;
                    float rad = th * Mathf.Deg2Rad;
                    float along = s.vx * Mathf.Cos(rad) + s.vy * Mathf.Sin(rad);
                    OnRiderLanded(s.spin, Mathf.Abs(d), ax, gy);
                    s.v = Mathf.Clamp(along, RiderVMin, RiderVMax);
                    if (Mathf.Abs(d) < 12f) s.v = Mathf.Min(RiderVMax, s.v + 1.5f);
                    s.x = ax;
                    s.y = gy;
                    s.phi = th;
                    s.piece = i;
                    s.spin = 0f;
                    rider = s;
                    return true;
                }
            }
            if (ay < riderTrack.minY - RiderFallMargin) return false;
            s.x = ax;
            s.y = ay;
            rider = s;
            return true;
        }

        void OnRiderLanded(float spin, float mismatch, float x, float y)
        {
            int flips = Mathf.FloorToInt((spin + 40f) / 360f);
            var at = new Vector3(x, y + 1.4f, 0f);
            if (flips > 0)
            {
                riderFlips += flips;
                Sfx.Milestone();
                Fx.Text(at, flips == 1 ? "SALTO !" : $"SALTO x{flips} !", RiderGold, 1.1f);
                Fx.Burst(new Vector3(x, y, 0f), RiderGold, 18, 3.5f, 0.1f, 0.3f);
            }
            else if (mismatch < 12f)
            {
                riderPerfects++;
                Fx.Text(at, "PARFAIT", CadenceCheckColor, 0.9f);
                Sfx.Bounce();
            }
            else
            {
                Sfx.Drop();
            }
            Fx.Burst(new Vector3(x, y, 0f), new Color(1f, 0.85f, 0.6f), 6, 2f, 0.07f, 0.4f);
        }

        void PlaceRiderPlayer()
        {
            var p = new Vector3(rider.x, rider.y + riderFeet, 0f);
            player.transform.position = p;
            var rb = player.GetComponent<Rigidbody2D>();
            if (rb != null) rb.position = p;
            if (riderPivot != null) riderPivot.localRotation = Quaternion.Euler(0f, 0f, rider.phi);

            // Checkpoints: passing one moves the respawn point.
            foreach (float c in riderTrack.checks)
            {
                if (c <= riderCheckX || rider.x < c || rider.piece < 0) continue;
                riderCheckX = c;
                Sfx.Coin();
                Fx.Burst(new Vector3(c, rider.y + 1.3f, 0f), CadenceCheckColor, 16, 2.5f, 0.09f, 0f);
            }
        }

        void UpdateRiderPresentation(bool hold)
        {
            float spin = -rider.v * Time.deltaTime * 200f;
            if (riderWheelA != null) riderWheelA.Rotate(0f, 0f, spin);
            if (riderWheelB != null) riderWheelB.Rotate(0f, 0f, spin);

            float progress = Mathf.Clamp01((rider.x - riderStartX) / Mathf.Max(1f, riderTrack.endX - riderStartX));
            ui.SetZoneLabel($"LE BOLIDE   {Mathf.FloorToInt(progress * 100f)} %");
            ui.SetSectionHud(progress, $"Tentative {riderAttempts}   ·   saltos {riderFlips}");
        }

        // ---- start, crash, finish ------------------------------------------------------------

        void StartRider()
        {
            riderActive = true;
            riderRespawning = false;
            riderAttempts = 1;
            riderFlips = 0;
            riderPerfects = 0;
            riderAccumulator = 0f;
            riderCheckX = riderStartX;
            riderFeet = 0.41f;
            if (player.collider2d != null && player.collider2d.enabled)
                riderFeet = Mathf.Max(0.2f, player.transform.position.y - player.collider2d.bounds.min.y);

            ResetRiderState(riderStartX, RiderVMin + 2f);

            // The car carries the player: its own controller and physics step aside.
            player.controlEnabled = false;
            player.velocity = Vector2.zero;
            player.enabled = false;
            // Disabling a KinematicObject hands its body to the physics engine (Dynamic):
            // keep it kinematic, or gravity and the ground would fight the car for it.
            var rb = player.GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.bodyType = RigidbodyType2D.Kinematic;
                rb.linearVelocity = Vector2.zero;
            }
            BuildRiderCar();
            SetupRiderCamera(true);
            ui.ShowBanner($"NIVEAU {TierOf(riderLevel)} · EN VOITURE", "Maintiens : accélère au sol, salto en l'air", 1.6f);
            PlaceRiderPlayer();
        }

        void ResetRiderState(float x, float speed)
        {
            int i = riderTrack.PieceAt(x);
            if (i < 0) i = 0;
            var (y, th) = riderTrack.Ground(i, x);
            rider = new RiderState { x = x, y = y, v = speed, phi = th, piece = i };
        }

        void BuildRiderCar()
        {
            DestroyRiderCar();
            var root = new GameObject("RiderCar").transform;
            root.SetParent(player.transform, false);
            root.localPosition = new Vector3(0f, -riderFeet, 0f);
            riderCar = root;
            riderPivot = new GameObject("Pivot").transform;
            riderPivot.SetParent(root, false);

            var src = player.GetComponent<SpriteRenderer>();
            int order = (src != null ? src.sortingOrder : 0) + 6;

            // The driver: a copy of the character, sitting in the cockpit.
            if (src != null)
            {
                var driver = new GameObject("Driver");
                driver.transform.SetParent(riderPivot, false);
                driver.transform.localPosition = new Vector3(-0.1f, 0.62f, 0f);
                driver.transform.localScale = Vector3.one * 0.72f;
                var dsr = driver.AddComponent<SpriteRenderer>();
                dsr.sprite = src.sprite;
                dsr.color = src.color;
                dsr.sortingOrder = order;
                riderHidden = src;
                src.enabled = false;
            }

            CarPart("Body", PlaceholderVisuals.Square(Color.white), RiderCrimson, new Vector2(0f, 0.42f), new Vector2(1.5f, 0.34f), order + 1);
            CarPart("Nose", PlaceholderVisuals.Square(Color.white), RiderGold, new Vector2(0.62f, 0.5f), new Vector2(0.3f, 0.18f), order + 2);
            CarPart("Spoiler", PlaceholderVisuals.Square(Color.white), RiderGold, new Vector2(-0.72f, 0.66f), new Vector2(0.14f, 0.3f), order + 2);
            riderWheelA = CarPart("WheelBack", PlaceholderVisuals.RimCircle(new Color(0.12f, 0.06f, 0.06f)), Color.white, new Vector2(-0.48f, 0.2f), new Vector2(0.42f, 0.42f), order + 3);
            riderWheelB = CarPart("WheelFront", PlaceholderVisuals.RimCircle(new Color(0.12f, 0.06f, 0.06f)), Color.white, new Vector2(0.5f, 0.2f), new Vector2(0.42f, 0.42f), order + 3);

            riderTrail = riderPivot.gameObject.AddComponent<TrailRenderer>();
            riderTrail.time = 0.25f;
            riderTrail.startWidth = 0.3f;
            riderTrail.endWidth = 0f;
            riderTrail.minVertexDistance = 0.05f;
            riderTrail.sharedMaterial = RiderMaterial;
            riderTrail.startColor = new Color(RiderGold.r, RiderGold.g, RiderGold.b, 0.7f);
            riderTrail.endColor = new Color(RiderGold.r, RiderGold.g, RiderGold.b, 0f);
            riderTrail.sortingOrder = order - 1;
        }

        Transform CarPart(string name, Sprite sprite, Color color, Vector2 pos, Vector2 size, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(riderPivot, false);
            go.transform.localPosition = pos;
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = order;
            return go.transform;
        }

        void DestroyRiderCar()
        {
            if (riderCar != null) Destroy(riderCar.gameObject);
            riderCar = null;
            riderPivot = null;
            riderWheelA = riderWheelB = null;
            riderTrail = null;
            if (riderHidden != null) riderHidden.enabled = true;
            riderHidden = null;
        }

        void RiderCrash()
        {
            if (!riderActive || riderRespawning) return;
            var at = new Vector3(rider.x, rider.y + 0.5f, 0f);
            Fx.Burst(at, RiderCrimson, 26, 5f, 0.13f, 0.5f);
            Fx.Burst(at, RiderGold, 12, 3f, 0.08f, 0.2f);
            Fx.Shake(0.4f, 0.3f);
            Sfx.Death();

            if (player.health != null) player.health.Decrement(1);
            if (player.health != null && !player.health.IsAlive)
            {
                AbortRider();
                return;
            }
            riderAttempts++;
            StartCoroutine(RiderRespawnRoutine());
        }

        IEnumerator RiderRespawnRoutine()
        {
            riderRespawning = true;
            if (riderCar != null) riderCar.gameObject.SetActive(false);
            yield return new WaitForSeconds(0.6f);
            if (!riderActive) yield break;

            ResetRiderState(riderCheckX, RiderVMin + 2f);
            riderAccumulator = 0f;
            if (riderCar != null) riderCar.gameObject.SetActive(true);
            PlaceRiderPlayer();
            if (riderTrail != null) riderTrail.Clear();
            riderRespawning = false;
            Fx.Text(new Vector3(rider.x, rider.y + 2f, 0f), $"TENTATIVE {riderAttempts}", ApogeeTheme.Cream, 1.1f);
        }

        void FinishRider()
        {
            // Out of the car onto the ordinary ground past the track.
            var last = riderTrack.pieces[riderTrack.pieces.Count - 1];
            float outX = last[last.Count - 1].x + 0.4f;
            RestoreFromRider();
            riderActive = false;
            riderFinished = true;
            PlacePlayerAt(outX, riderOriginY + riderFeet + 0.03f);

            int tier = TierOf(riderLevel);
            int coins = 15 + 10 * tier + 2 * Mathf.Min(riderFlips, 15);
            SaveSystem.AddCoins(coins);
            int materials = riderAttempts == 1 ? tier : 0;
            if (materials > 0) SaveSystem.AddMaterials(materials);

            Sfx.Milestone();
            Fx.Burst(player.transform.position, RiderGold, 40, 6f, 0.14f, 0.4f);
            string tries = riderAttempts == 1 ? "sans accident !" : $"en {riderAttempts} tentatives";
            ui.ShowBanner("BOLIDE TERMINÉ", $"{tries}  ·  {riderFlips} saltos  ·  +{coins} [c]" + (materials > 0 ? $"   +{materials} [g]" : ""), 3.2f);
        }

        /// <summary>Stops the section without rewards (death, leaving the run).</summary>
        void AbortRider()
        {
            if (!riderActive) return;
            RestoreFromRider();
            riderActive = false;
            riderRespawning = false;
        }

        void RestoreFromRider()
        {
            DestroyRiderCar();
            if (player != null)
            {
                player.enabled = true;
                player.velocity = Vector2.zero;
                if (running) player.controlEnabled = true;
            }
            SetupRiderCamera(false);
            ui.ClearSectionHud();
            ui.SetZoneLabel(ZoneCatalog.Get(activeZone).Title);
        }

        void ClearRider()
        {
            if (riderActive) AbortRider();
            foreach (var go in riderObjects) if (go != null) Destroy(go);
            riderObjects.Clear();
            foreach (var m in riderMeshes) if (m != null) Destroy(m);
            riderMeshes.Clear();
            riderBuilt = false;
            riderFinished = false;
            riderRespawning = false;
            riderTrack = null;
        }

        // ---- camera --------------------------------------------------------------------------

        /// <summary>Wider than the run: at up to 14 m/s the player needs to see the next ramp coming.</summary>
        float RiderOrtho()
        {
            float aspect = mainCamera != null ? Mathf.Max(0.3f, mainCamera.aspect) : 1.78f;
            return Mathf.Clamp(15f / (1.5f * aspect), 5.2f, 9f);
        }

        void SetupRiderCamera(bool on)
        {
            if (composer == null) return;
            if (on)
            {
                var la = composer.Lookahead;
                cadenceLookaheadWas = la.Enabled;
                la.Enabled = false;
                composer.Lookahead = la;
                cadenceDampingWas = composer.Damping;
                composer.Damping = new Vector3(0f, cadenceDampingWas.y * 0.5f, cadenceDampingWas.z);
                float aspect = mainCamera != null ? Mathf.Max(0.3f, mainCamera.aspect) : 1.78f;
                Fx.SetCameraRestOffset(new Vector3(RiderOrtho() * aspect * 0.45f, 1.2f, 0f));
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
    }
}
