using UnityEngine;

namespace Platformer.Survival
{
    /// <summary>
    /// Terrain generation half of SurvivalDirector: the fixed intro, the zone planner,
    /// per-zone procedural ground (gaps, stepping stones, height changes, unstable slabs,
    /// hazards, bonus platforms, coin arcs), the Ascent tower and the descent staircase.
    /// Visual dressing of all of it lives in SurvivalDirector.Scenery.cs.
    ///
    /// Player physics that every layout rule is derived from (see PlayerController and
    /// the scene's PlatformerModel): take-off 7 * 0.9 = 6.3 m/s under 9.81 gravity gives a
    /// 2.0 m jump apex and ~1.3 s of air time at 3 m/s, i.e. ~3.8 m of horizontal reach.
    /// Gaps therefore never exceed 3.1 m and step-ups never exceed 1.5 m.
    /// </summary>
    public partial class SurvivalDirector
    {
        /// <summary>Widest gap at the design speed (3 m/s); multiplied by ReachScale at runtime.</summary>
        const float MaxGapWidth = 2.6f;
        const float TowerWidth = 8f;
        /// <summary>Reach at the speed the layout numbers were tuned for.</summary>
        const float DesignReach = 3.8f;

        /// <summary>How much longer every horizontal feature must be to stay as jumpable as at design speed.</summary>
        float ReachScale => Mathf.Max(1f, RunReach / DesignReach);

        /// <summary>
        /// Fixed, hand-tuned opening stretch (always identical between runs, unlike the
        /// random generation past it): width, height change from the previous segment, and
        /// whether it's a gap. Curated zombie/pickup placements tied to specific indices
        /// live in GenerateIntroSegment.
        /// </summary>
        static readonly (float width, float heightDelta, bool isGap)[] IntroLayout =
        {
            (10f, 0f, false),  // guaranteed safe landing zone
            (6f,  0f, false),  // a coin partway along here
            (2.0f, 0f, true),  // first gap - teaches jumping, always clearable
            (6f,  0f, false),
            (5f,  1.5f, false), // a small raised step, with a material on top
            (2.2f, 0f, true),   // second gap
            (8f,  0f, false),   // first zombie encounter, always a lone Walker
        };

        static readonly float IntroEndX = SumIntroWidth();
        static float SumIntroWidth()
        {
            float total = 0f;
            foreach (var s in IntroLayout) total += s.width;
            return total;
        }

        // ---- main loop -----------------------------------------------------------------

        void GenerateGroundAhead()
        {
            while (frontierX < player.transform.position.x + genAheadDistance * ReachScale)
            {
                if (introIndex < IntroLayout.Length)
                {
                    GenerateIntroSegment();
                    if (introIndex >= IntroLayout.Length) BeginZone(ZoneKind.City, 45f);
                    continue;
                }

                if (frontierX >= genZoneEndX) BeginZone(PickNextZone());

                switch (genZone.Kind)
                {
                    case ZoneKind.Ascent:
                        BuildTower(frontierX);
                        BeginZone(ZoneKind.Rooftops);
                        break;
                    case ZoneKind.Shaft:
                        BuildShaft(frontierX);
                        BeginZone(PickNextZone());
                        break;
                    case ZoneKind.Jetpack:
                        GenerateJetpackStretch();
                        break;
                    case ZoneKind.Descent:
                        GenerateDescentStep();
                        break;
                    default:
                        GenerateProceduralSegment();
                        break;
                }
            }
        }

        // ---- zone planning (frontier-space) --------------------------------------------

        void BeginZone(ZoneKind kind, float fixedLength = -1f)
        {
            genZone = ZoneCatalog.Get(kind);
            if (kind == ZoneKind.Descent || kind == ZoneKind.Ascent || kind == ZoneKind.Shaft)
                genZoneEndX = float.MaxValue; // these end on their own terms
            else
                genZoneEndX = frontierX + (fixedLength > 0f ? fixedLength : Random.Range(genZone.LengthMin, genZone.LengthMax));
            if (kind == ZoneKind.Jetpack) jetpackLandingX = genZoneEndX;

            zoneMarkers.Add(new ZoneMarker { x = frontierX, kind = kind });
            zoneSpans.Add(new ZoneMarker { x = frontierX, kind = kind });
        }

        /// <summary>The zone generated at world x (City before the first marker).</summary>
        ZoneKind ZoneAt(float x)
        {
            var kind = ZoneKind.City;
            for (int i = 0; i < zoneSpans.Count && zoneSpans[i].x <= x; i++) kind = zoneSpans[i].kind;
            return kind;
        }

        /// <summary>
        /// Ascent always chains into Rooftops then Descent. Otherwise rotate through the
        /// street-level zones without repeating the last one. The first tower comes after
        /// one street zone past the city (around 150 m); later ones after two.
        /// </summary>
        ZoneKind PickNextZone()
        {
            switch (genZone.Kind)
            {
                case ZoneKind.Ascent: return ZoneKind.Rooftops;
                // Back down from the rooftops either by staircase or by free-falling down a shaft.
                case ZoneKind.Rooftops: return lastTopY >= 15f && Random.value < 0.55f ? ZoneKind.Shaft : ZoneKind.Descent;
                case ZoneKind.Descent:
                case ZoneKind.Shaft: zonesSinceAscent = -1; break;
            }

            // A flight must always be followed by plain street so the jetpack has real ground
            // to land on (a tower pit right after the lake would break both mechanics).
            if (genZone.Kind == ZoneKind.Jetpack)
            {
                var street = new[] { ZoneKind.City, ZoneKind.Highway, ZoneKind.Infested, ZoneKind.Wasteland };
                return street[Random.Range(0, street.Length)];
            }

            zonesSinceAscent++;
            if (zonesSinceAscent >= 2) return ZoneKind.Ascent;

            // Street-level rotation; the jetpack flight joins it once the player has warmed up,
            // but never as the zone right before a tower (see above).
            bool towerNext = zonesSinceAscent >= 1;
            var candidates = Ramp > 0.05f && !towerNext
                ? new[] { ZoneKind.City, ZoneKind.Highway, ZoneKind.Infested, ZoneKind.Wasteland, ZoneKind.Jetpack }
                : new[] { ZoneKind.City, ZoneKind.Highway, ZoneKind.Infested, ZoneKind.Wasteland };
            ZoneKind pick;
            do pick = candidates[Random.Range(0, candidates.Length)];
            while (pick == genZone.Kind);
            return pick;
        }

        // ---- intro ---------------------------------------------------------------------

        /// <summary>Places the next fixed IntroLayout entry, plus any curated prop tied to that index.</summary>
        void GenerateIntroSegment()
        {
            int index = introIndex;
            var (width, heightDelta, isGap) = IntroLayout[index];
            introIndex++;

            if (isGap)
            {
                RegisterGap(frontierX, width);
                return;
            }

            float segStart = frontierX;
            float topY = Mathf.Clamp(lastTopY + heightDelta, -3f, 5f);
            // The ground itself already exists as real, hand-placed GameObjects in
            // SampleScene.unity (IntroGround_A..E) - just register its geometry for
            // GetGroundHeightAt/zombie queries, don't spawn a duplicate runtime copy.
            segments.Add(new GroundSegment { xStart = segStart, xEnd = segStart + width, topY = topY, isGap = false, go = null });
            frontierX = segStart + width;
            lastTopY = topY;
            lastSegmentWasGap = false;
            lastSegmentWasUnstable = false;
            lastSegmentWidth = width;

            DecorateSegment(null, segStart, width, topY);

            if (index == 1) SpawnPickupAt(segStart + 3f, topY + 0.6f, PickupType.Coin);
            if (index == 4) SpawnPickupAt(segStart + 3f, topY + 0.6f, PickupType.Material);
            if (index == 6) SpawnZombie(segStart + 4f, ZombieKind.Walker);
        }

        // ---- procedural street-level zones ---------------------------------------------

        void GenerateProceduralSegment()
        {
            var zone = genZone;
            float ramp = Ramp;
            float roll = Random.value;

            // A gap can never immediately follow another gap (that would silently chain
            // their widths into a jump distance far beyond the player's jump arc), and
            // the segment right after a gap always lands at the same height as takeoff
            // (no combined horizontal+vertical jump) so every gap stays cleanly jumpable.
            // A gap never follows another gap or an unstable slab (a collapsed slab already
            // leaves a hole; chaining a real gap onto it would exceed the jump arc).
            bool canGap = !lastSegmentWasGap && !lastSegmentWasUnstable && frontierX > 8f;
            if (canGap && roll < zone.GapChance)
            {
                if (ramp > 0.1f && Random.value < zone.SteppingStoneChance)
                    GenerateStoneBridgedGap();
                else
                {
                    // A short landing strip before the gap leaves no room for a run-up, so
                    // keep such gaps modest.
                    float cap = (lastSegmentWidth < 3.5f * ReachScale ? 2.4f : MaxGapWidth) * ReachScale;
                    GenerateGap(Mathf.Min(cap, (Random.Range(zone.GapMin, zone.GapMax) + ramp * 0.4f) * ReachScale));
                }
                return;
            }

            float width = Mathf.Max(2.5f, Random.Range(zone.SegMin, zone.SegMax) * Mathf.Lerp(1f, 0.8f, ramp)) * ReachScale;
            float topY = lastTopY;
            // No height change right after a gap (no combined long+high jump) nor right
            // after an unstable slab (the escape jump off a collapsing slab must land flat).
            if (!lastSegmentWasGap && !lastSegmentWasUnstable && Random.value < zone.HeightChance)
            {
                bool up = Random.value < 0.5f;
                float delta = up ? Random.Range(0.6f, zone.MaxStepUp) : -Random.Range(0.8f, zone.MaxDrop);
                topY = Mathf.Clamp(lastTopY + delta, baselineY - 3f, baselineY + 5f);
            }

            // Unstable slabs are short enough (<= 3 m) to be cleared in a single jump from
            // the solid ground before them, are never entered from a jump (no gap before),
            // and the shake lasts long enough that any jump started during it clears them.
            bool unstable = zone.UnstableChance > 0f
                && !lastSegmentWasUnstable
                && !lastSegmentWasGap
                && topY == lastTopY
                && Random.value < zone.UnstableChance * Mathf.Lerp(0.6f, 1.2f, ramp);
            if (unstable) width = Random.Range(2.4f, 3.0f) * ReachScale;

            float segStart = frontierX;
            GenerateSegment(segStart, width, topY, allowBonusPlatform: !unstable, unstable: unstable);
            lastTopY = topY;
            lastSegmentWasGap = false;
            lastSegmentWasUnstable = unstable;
            lastSegmentWidth = width;

            if (!unstable) MaybePlaceHazards(segStart, width, topY);
        }

        void GenerateGap(float width)
        {
            float gapStart = frontierX;
            RegisterGap(gapStart, width);
            if (Random.value < 0.45f) SpawnCoinArc(gapStart, width, lastTopY);
        }

        /// <summary>
        /// A gap too wide to clear in one go, bridged by a floating stone: two jumps instead
        /// of one. Sized from the jump reach so a full-speed jump from the edge lands squarely
        /// on the stone (which spans 0.55-1.25 reach) and a second jump from the stone clears
        /// the rest (the gap ends at 1.6 reach). The stone top is level with the ground so
        /// no height change sneaks into the arc. Registered as a single gap so zombies and
        /// spawns treat the whole span as a hole.
        /// </summary>
        void GenerateStoneBridgedGap()
        {
            float reach = RunReach;
            float total = 1.6f * reach;
            float stoneStart = 0.55f * reach;
            float stoneWidth = 0.7f * reach;
            const float thickness = 0.5f;
            float gapStart = frontierX;
            RegisterGap(gapStart, total);

            float stoneX = gapStart + stoneStart + stoneWidth / 2f;
            var stone = CreateSolidPlatform($"Stone_{stoneX:0}", stoneX, lastTopY - thickness / 2f, stoneWidth, thickness, PlaceholderVisuals.StoneColor);
            props.Add(stone);

            SpawnPickupAt(stoneX - 0.4f, lastTopY + 0.6f, PickupType.Coin);
            SpawnPickupAt(stoneX + 0.4f, lastTopY + 0.6f, PickupType.Coin);
        }

        void RegisterGap(float xStart, float width)
        {
            segments.Add(new GroundSegment { xStart = xStart, xEnd = xStart + width, topY = lastTopY, isGap = true, go = null });
            frontierX = xStart + width;
            lastSegmentWasGap = true;
            lastSegmentWasUnstable = false;
        }

        // ---- descent -------------------------------------------------------------------

        /// <summary>One step of the staircase from the rooftops back down to street level.</summary>
        void GenerateDescentStep()
        {
            float width = Random.Range(genZone.SegMin, genZone.SegMax) * ReachScale;
            float drop = Random.Range(2.5f, genZone.MaxDrop);
            float topY = Mathf.Max(lastTopY - drop, 0f);

            float segStart = frontierX;
            GenerateSegment(segStart, width, topY, allowBonusPlatform: false);
            lastTopY = topY;
            lastSegmentWasGap = false;
            lastSegmentWasUnstable = false;
            lastSegmentWidth = width;

            if (Random.value < 0.6f) SpawnPickupAt(segStart + width / 2f, topY + 0.6f, PickupType.Coin);

            if (topY <= 0.5f)
            {
                baselineY = 0f;
                genZoneEndX = frontierX; // staircase done, next zone starts right here
            }
        }

        // ---- ascent tower (Doodle Jump section) ----------------------------------------

        /// <summary>
        /// The street ends at a ledge over a bottomless pit; a column of bounce platforms
        /// climbs from just under the ledge up to a roof several storeys higher, where the
        /// run continues (Rooftops). Platform count, spacing, width and the share of
        /// fragile/moving ones all scale with Ramp so later towers are genuinely harder.
        /// </summary>
        void BuildTower(float xStart)
        {
            float ramp = Ramp;
            float baseY = lastTopY;
            lastTowerBaseY = baseY;

            // The pit: no ground for the whole column.
            segments.Add(new GroundSegment { xStart = xStart, xEnd = xStart + TowerWidth, topY = baseY, isGap = true, go = null });

            int count = Mathf.RoundToInt(Mathf.Lerp(8f, 14f, ramp));
            float spacingMin = 2.0f;
            float spacingMax = Mathf.Lerp(2.6f, 3.0f, ramp);
            float widthMin = Mathf.Lerp(2.6f, 1.7f, ramp);
            float widthMax = Mathf.Lerp(3.2f, 2.3f, ramp);
            float fragileChance = Mathf.Lerp(0.10f, 0.35f, ramp);
            float movingChance = ramp < 0.15f ? 0f : Mathf.Lerp(0.05f, 0.30f, ramp);
            const float springChance = 0.10f;

            float minX = xStart + 0.4f;
            float maxX = xStart + TowerWidth - 0.4f;

            // Catch platform spanning the whole column just under the ledge, so whether the
            // player runs, jumps or sprints off the edge they always land on it and start
            // the climb (a shorter one could be overshot with a jump = certain death).
            float x = xStart + 2.0f;
            float y = baseY - 1.5f;
            SpawnBouncePlatform(xStart + TowerWidth / 2f, y, TowerWidth - 0.8f, BounceKind.Normal, minX, maxX);

            bool lastWasFragile = false;
            for (int i = 0; i < count; i++)
            {
                y += Random.Range(spacingMin, spacingMax);
                x = Mathf.Clamp(x + Random.Range(-3.4f, 3.4f), minX + 1.2f, maxX - 1.2f);

                float kindRoll = Random.value;
                BounceKind kind;
                if (kindRoll < fragileChance && !lastWasFragile) kind = BounceKind.Fragile;
                else if (kindRoll < fragileChance + springChance) kind = BounceKind.Spring;
                else if (kindRoll < fragileChance + springChance + movingChance) kind = BounceKind.Moving;
                else kind = BounceKind.Normal;
                // Never two fragile platforms in a row: with 11 m of fall tolerance there
                // must always be a permanent platform to fall back onto after a miss.
                lastWasFragile = kind == BounceKind.Fragile;

                float width = Random.Range(widthMin, widthMax);
                if (kind == BounceKind.Spring) width = Mathf.Max(width, 2.4f);
                SpawnBouncePlatform(x, y, width, kind, minX, maxX);

                if (Random.value < 0.45f)
                    SpawnPickupAt(x, y + 1.2f, Random.value < 0.15f ? PickupType.Material : PickupType.Coin);
            }

            // Final platform hugs the right edge and is always plain and wide, so its bounce
            // carries the player onto the roof.
            y += Random.Range(spacingMin, spacingMax);
            x = xStart + TowerWidth - 1.9f;
            SpawnBouncePlatform(x, y, 3.0f, BounceKind.Normal, minX, maxX);

            float towerTop = y + 2.4f;
            CreateTowerBackdrop(xStart, baseY, towerTop);

            frontierX = xStart + TowerWidth;
            lastTopY = towerTop;
            baselineY = towerTop;
            lastSegmentWasGap = true;
            lastSegmentWasUnstable = false;

            // The roof landing: long, flat, safe, with a medkit as the reward for the climb.
            float roofStart = frontierX;
            float roofWidth = 10f * ReachScale;
            GenerateSegment(roofStart, roofWidth, towerTop, allowBonusPlatform: false);
            lastSegmentWasGap = false;
            lastSegmentWidth = roofWidth;
            SpawnPickupAt(roofStart + roofWidth / 2f, towerTop + 0.7f, PickupType.Medkit);
        }

        void SpawnBouncePlatform(float x, float y, float width, BounceKind kind, float minX, float maxX)
        {
            var platform = BouncePlatform.Create(entityParent, x, y, width, kind, minX, maxX);
            platform.OnBounced = OnBouncePlatformUsed;
            props.Add(platform.gameObject);
        }

        /// <summary>
        /// Castle tower behind the column (see BuildCastleTower); without the Kenney kits, a
        /// dark building silhouette with a few lit windows.
        /// </summary>
        void CreateTowerBackdrop(float xStart, float baseY, float towerTop)
        {
            if (BuildCastleTower(xStart, baseY, towerTop)) return;

            float bottom = baseY - 16f;
            float top = towerTop + 2f;
            float centerX = xStart + TowerWidth / 2f;

            var go = new GameObject($"TowerBackdrop_{xStart:0}");
            go.transform.SetParent(entityParent, false);
            go.transform.position = new Vector3(centerX, (bottom + top) / 2f, 0f);
            go.transform.localScale = new Vector3(TowerWidth + 1.2f, top - bottom, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = PlaceholderVisuals.Square(Color.white);
            sr.color = new Color(0.11f, 0.10f, 0.11f, 0.95f);
            sr.sortingOrder = -3;
            props.Add(go);

            for (float wy = baseY + 1.5f; wy < towerTop; wy += 3f)
            {
                for (int i = 0; i < 3; i++)
                {
                    if (Random.value < 0.35f) continue;
                    float wx = xStart + 1.4f + i * 2.6f;
                    var win = new GameObject("Window");
                    win.transform.SetParent(entityParent, false);
                    win.transform.position = new Vector3(wx, wy, 0f);
                    win.transform.localScale = new Vector3(0.6f, 0.9f, 1f);
                    var wsr = win.AddComponent<SpriteRenderer>();
                    wsr.sprite = PlaceholderVisuals.Square(Color.white);
                    wsr.color = new Color(0.45f, 0.36f, 0.22f, Random.Range(0.25f, 0.6f));
                    wsr.sortingOrder = -2;
                    props.Add(win);
                }
            }
        }

        // ---- shaft (free-fall section, the mirror of the tower) -------------------------

        /// <summary>
        /// The roof ends over a deep shaft down to street level. The player steers while
        /// falling past spike ledges that alternate sides, and lands on a wide street
        /// segment that spans the whole shaft (so there is no way to miss it). Terminal
        /// velocity (KinematicObject.maxFallSpeed) keeps the fall readable.
        /// </summary>
        void BuildShaft(float xStart)
        {
            float top = lastTopY;
            float width = Mathf.Max(8f, 1.5f * RunReach); // never jumpable across
            float landingWidth = width + 10f * ReachScale;

            shaftXStart = xStart;
            shaftXEnd = xStart + width;
            shaftTop = top;

            // Street-level landing under the whole shaft and a stretch beyond it.
            GenerateSegment(xStart, landingWidth, 0f, allowBonusPlatform: false);
            lastTopY = 0f;
            baselineY = 0f;
            lastSegmentWasGap = false;
            lastSegmentWasUnstable = false;
            lastSegmentWidth = landingWidth;

            // Backdrop (castle masonry, or a flat dark silhouette without the kits) + walls.
            float centerX = xStart + width / 2f;
            if (!BuildShaftMasonry(xStart, width, top))
            {
                var back = new GameObject($"ShaftBackdrop_{xStart:0}");
                back.transform.SetParent(entityParent, false);
                back.transform.position = new Vector3(centerX, top / 2f, 0f);
                back.transform.localScale = new Vector3(width + 1.2f, top + 2f, 1f);
                var bsr = back.AddComponent<SpriteRenderer>();
                bsr.sprite = PlaceholderVisuals.Square(Color.white);
                bsr.color = new Color(0.09f, 0.08f, 0.09f, 0.97f);
                bsr.sortingOrder = -3;
                props.Add(back);
            }
            foreach (float wx in new[] { xStart - 0.3f, xStart + width + 0.3f })
            {
                var wall = new GameObject("ShaftWall");
                wall.transform.SetParent(entityParent, false);
                wall.transform.position = new Vector3(wx, top / 2f, 0f);
                wall.transform.localScale = new Vector3(0.6f, top + 2f, 1f);
                var wsr = wall.AddComponent<SpriteRenderer>();
                wsr.sprite = PlaceholderVisuals.Square(PlaceholderVisuals.StoneColor);
                wsr.sortingOrder = -2;
                props.Add(wall);
            }

            // Spike ledges mostly alternating sides, leaving a free channel to steer through.
            // Spacing is tuned for the shaft's capped fall speed (see UpdateShaft): about a
            // second between ledges, enough to cross to the other side.
            bool left = Random.value < 0.5f;
            float freeWidth = Mathf.Lerp(4.8f, 3.8f, Ramp);
            for (float y = top - 6f; y > 4f; y -= Random.Range(7f, 9.5f))
            {
                float spikeWidth = width - freeWidth;
                float spikeX = left ? xStart + spikeWidth / 2f : xStart + width - spikeWidth / 2f;
                var spikes = Hazard.CreateSpikes(entityParent, spikeX, y, spikeWidth, 1.0f);
                props.Add(spikes.gameObject);

                float coinX = left ? xStart + width - freeWidth / 2f : xStart + freeWidth / 2f;
                SpawnPickupAt(coinX, y + 0.5f, PickupType.Coin);
                if (Random.value < 0.7f) left = !left;
            }
            // Something to land next to.
            SpawnPickupAt(xStart + width + 2f, 0.7f, PickupType.Medkit);
        }

        // ---- jetpack flight ---------------------------------------------------------------

        /// <summary>
        /// One stretch of the Survol zone: no ground, a toxic lake below, a floating debris
        /// block (sometimes spiked) at a random height, a live cable every other stretch and
        /// a few coins. The player flies through with the jetpack (see PlayerController).
        /// </summary>
        void GenerateJetpackStretch()
        {
            float step = Random.Range(genZone.SegMin, genZone.SegMax);
            float xStart = frontierX;
            RegisterGap(xStart, step);

            float lakeSurface = baselineY - 1.5f;
            var lake = Hazard.CreateToxicLake(entityParent, xStart + step / 2f, lakeSurface, step + 0.05f, 14f);
            props.Add(lake.gameObject);
            DecorateLakeStretch(xStart, step, lakeSurface);

            // Debris block.
            float blockW = Random.Range(1.4f, 2.8f);
            float blockY = baselineY + Random.Range(1.0f, 6.5f);
            float blockX = xStart + step / 2f;
            var block = CreateSolidPlatform($"Debris_{blockX:0}", blockX, blockY, blockW, 0.6f, PlaceholderVisuals.StoneColor);
            props.Add(block);
            if (Random.value < 0.45f)
                props.Add(Hazard.CreateSpikes(entityParent, blockX, blockY + 0.3f, Mathf.Min(blockW, 1.4f)).gameObject);

            // Cable at a height away from the block.
            if (Random.value < 0.5f)
            {
                float cableY = blockY > baselineY + 3.5f ? Random.Range(baselineY + 0.8f, blockY - 2.2f) : Random.Range(blockY + 2.2f, baselineY + 8f);
                props.Add(Hazard.CreateCable(entityParent, blockX + Random.Range(-1f, 1f), cableY, Random.Range(2.5f, 4f)).gameObject);
            }

            // Coins in a vertical string in the clear part of the stretch.
            float coinX = xStart + Random.Range(0.8f, 1.6f);
            float coinY = baselineY + Random.Range(1.5f, 6f);
            for (int i = 0; i < 3; i++) SpawnPickupAt(coinX, coinY + i * 0.8f, PickupType.Coin);
        }

        // ---- segment building ----------------------------------------------------------

        void GenerateSegment(float xStart, float width, float topY, bool allowBonusPlatform = true, bool unstable = false)
        {
            float thickness = unstable ? 0.6f : groundThickness;
            var go = new GameObject(unstable ? $"UnstableSlab_{xStart:0}" : $"Ground_{xStart:0}");
            go.transform.SetParent(entityParent, false);
            go.transform.position = new Vector3(xStart + width / 2f, topY - thickness / 2f, 0f);
            go.transform.localScale = new Vector3(width, thickness, 1f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = PlaceholderVisuals.Square(genZone.Ground);
            sr.sortingOrder = -1;
            if (unstable)
            {
                sr.color = new Color(1.1f, 0.78f, 0.7f);
            }
            else
            {
                float shade = 1f + Random.Range(-0.12f, 0.12f);
                sr.color = new Color(shade, shade, shade);
            }

            go.AddComponent<BoxCollider2D>();

            if (unstable)
            {
                var slab = go.AddComponent<UnstablePlatform>();
                slab.xStart = xStart;
                slab.xEnd = xStart + width;
                slab.topY = topY;
                slab.Init(player);
                slab.OnCollapsed = OnSlabCollapsed;
            }

            segments.Add(new GroundSegment { xStart = xStart, xEnd = xStart + width, topY = topY, isGap = false, go = go });
            frontierX = xStart + width;

            if (!unstable) DecorateSegment(go, xStart, width, topY);

            if (allowBonusPlatform && width > 5f && Random.value < genZone.BonusPlatformChance)
                SpawnBonusPlatform(xStart, width, topY);
        }

        void OnSlabCollapsed(UnstablePlatform slab)
        {
            for (int i = 0; i < segments.Count; i++)
            {
                if (Mathf.Abs(segments[i].xStart - slab.xStart) < 0.01f && !segments[i].isGap)
                {
                    var seg = segments[i];
                    seg.isGap = true;
                    seg.go = null; // the slab destroys itself once it has fallen out of view
                    segments[i] = seg;
                    return;
                }
            }
        }

        GameObject CreateSolidPlatform(string name, float centerX, float centerY, float width, float thickness, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(entityParent, false);
            go.transform.position = new Vector3(centerX, centerY, 0f);
            go.transform.localScale = new Vector3(width, thickness, 1f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = PlaceholderVisuals.Square(color);
            sr.sortingOrder = -1;

            go.AddComponent<BoxCollider2D>();
            return go;
        }

        /// <summary>
        /// A small floating platform above the main path with a pickup on it - purely
        /// optional, reachable with a normal jump (its top sits 1.5-1.9 m up, under the
        /// 2.0 m apex), never blocking the ground route below it.
        /// </summary>
        void SpawnBonusPlatform(float segStart, float segWidth, float topY)
        {
            const float platformWidth = 2.2f;
            const float thickness = 0.35f;
            float platformX = segStart + Random.Range(1f, Mathf.Max(1.5f, segWidth - platformWidth - 1f));
            float platformY = topY + Random.Range(1.3f, 1.7f);

            var go = CreateSolidPlatform($"BonusPlatform_{platformX:0}", platformX + platformWidth / 2f, platformY, platformWidth, thickness, genZone.Ground);
            go.GetComponent<SpriteRenderer>().color = new Color(1.15f, 1.1f, 1.05f);
            props.Add(go);

            float roll = Random.value;
            var type = roll < 0.1f ? PickupType.Medkit : roll < 0.4f ? PickupType.Material : PickupType.Coin;
            SpawnPickupAt(platformX + platformWidth / 2f, platformY + thickness / 2f + 0.5f, type);
        }

        /// <summary>Three coins tracing the jump arc over a gap - reward for committing to the jump.</summary>
        void SpawnCoinArc(float gapStart, float gapWidth, float topY)
        {
            for (int i = 1; i <= 3; i++)
            {
                float t = i / 4f;
                float arc = 1f - Mathf.Pow((t - 0.5f) / 0.5f, 2f);
                SpawnPickupAt(gapStart + gapWidth * t, topY + 0.8f + arc * 1.1f, PickupType.Coin);
            }
        }

        // ---- hazards -------------------------------------------------------------------

        /// <summary>
        /// Hazards keep 2 m clear of a segment's start and 3.5 m of its end, so a jump
        /// over one never carries the player straight into the following gap and a
        /// landing from the previous gap never comes down on top of one.
        /// </summary>
        void MaybePlaceHazards(float segStart, float width, float topY)
        {
            var zone = genZone;
            float scale = ReachScale;
            if (width < 6.5f * scale || (!zone.Spikes && !zone.Toxic)) return;

            float chance = zone.HazardChance * Mathf.Lerp(0.7f, 1.3f, Ramp);
            if (Random.value > chance) return;

            int hazardCount = width >= 10f * scale && Ramp > 0.4f && Random.value < 0.5f ? 2 : 1;
            float usableStart = segStart + 2f * scale;
            float usableEnd = segStart + width - 3.5f * scale;
            float slot = (usableEnd - usableStart) / hazardCount;

            for (int i = 0; i < hazardCount; i++)
            {
                float x = usableStart + slot * i + Random.Range(0.6f, Mathf.Max(0.7f, slot - 0.6f));
                bool toxic = zone.Toxic && (!zone.Spikes || Random.value < 0.55f);
                Hazard hazard = toxic
                    ? Hazard.CreateToxicPool(entityParent, x, topY, Random.Range(1.6f, 2.2f))
                    : Hazard.CreateSpikes(entityParent, x, topY, Random.Range(0.9f, 1.3f));
                props.Add(hazard.gameObject);
            }
        }

        // ---- background decor ----------------------------------------------------------

        /// <summary>
        /// Sparse silhouette ruins/rubble behind the gameplay, purely decorative (no
        /// collider), reinforcing the ruined-city apocalypse setting and making the
        /// procedurally generated map read as richer than a single flat strip of ground.
        /// Density follows the zone being generated (dense on the rooftops, sparse on the
        /// highway).
        /// </summary>
        void GenerateBackgroundDecor()
        {
            while (decorFrontierX < player.transform.position.x + genAheadDistance * ReachScale + 15f)
            {
                float x = decorFrontierX + Random.Range(genZone.RuinSpacingMin, genZone.RuinSpacingMax);
                float height = Random.Range(3f, 9f);
                float width = Random.Range(2f, 5f);
                float groundY = GetGroundHeightAt(x);

                var go = new GameObject($"Ruin_{x:0}");
                go.transform.SetParent(entityParent, false);
                go.transform.position = new Vector3(x, groundY + height / 2f - 0.5f, 0f);
                go.transform.localScale = new Vector3(width, height, 1f);

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = PlaceholderVisuals.Square(PlaceholderVisuals.RuinColor);
                sr.sortingOrder = -3;
                float shade = 1f + Random.Range(-0.15f, 0.1f);
                sr.color = new Color(shade, shade, shade, 0.9f);

                ruins.Add(go);
                decorFrontierX = x;
            }
        }
    }
}
