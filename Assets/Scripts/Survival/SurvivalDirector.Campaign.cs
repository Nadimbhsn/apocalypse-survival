using UnityEngine;
using Platformer.Core;
using Platformer.Mechanics;

namespace Platformer.Survival
{
    /// <summary>
    /// Campaign half of SurvivalDirector: plays an authored level (see LevelCatalog)
    /// instead of the endless generator. The level script is executed command by command
    /// against the same terrain primitives the endless run uses, streamed ahead of the
    /// player exactly the same way, so an authored level costs no more memory than a run.
    ///
    /// Three rules separate a level from a run:
    ///  - the run speed never ramps, so a jump measured once is that jump forever;
    ///  - nothing is spawned at random, every enemy and every pickup is placed by hand;
    ///  - death sends the player back to the last checkpoint instead of ending the game,
    ///    and the level ends at a gate that opens a boss duel carrying the health left.
    ///
    /// Levels are laid out far from the origin (see CampaignOriginX) because the sample
    /// scene's own hand-placed intro ground still physically exists around x = 0.
    /// </summary>
    public partial class SurvivalDirector
    {
        /// <summary>Where every authored level is built, clear of the sample scene's own geometry.</summary>
        const float CampaignOriginX = 3000f;

        struct CampaignState
        {
            public int cmdIndex;
            public float frontierX, lastTopY, baselineY, lastSegmentWidth, decorFrontierX;
            public bool wasGap, wasUnstable;
            public ZoneKind genZoneKind, activeZoneKind;
            public float playerX, playerY;
            public int secretsFound;
        }

        LevelDef level;
        bool inCampaign;
        int levelIndex;
        int cmdIndex;
        int secretsFound;
        bool levelFinished;
        float levelLengthEstimate;
        CampaignState checkpoint;
        bool hasCheckpoint;

        /// <summary>Anchor for the commands that decorate "the ground just laid".</summary>
        float lastGroundStart, lastGroundWidth, lastGroundTopY;

        public bool InCampaign => inCampaign;
        public LevelDef CurrentLevel => level;
        public int CurrentLevelIndex => levelIndex;
        public int SecretsFound => secretsFound;

        /// <summary>How far through the level the player is, 0..1, for the HUD bar.</summary>
        public float LevelProgress => levelLengthEstimate <= 1f ? 0f
            : Mathf.Clamp01((player.transform.position.x - runStartX) / levelLengthEstimate);

        // ---- lifecycle -----------------------------------------------------------------

        public void StartLevel(int index)
        {
            Time.timeScale = 1f;
            Simulation.Clear();
            ClearAll();

            levelIndex = Mathf.Clamp(index, 0, LevelCatalog.Count - 1);
            level = LevelCatalog.Get(levelIndex);
            inCampaign = true;
            cmdIndex = 0;
            secretsFound = 0;
            levelFinished = false;
            hasCheckpoint = false;

            UpgradeManager.ApplyToPlayer(player, baseMaxSpeed, baseMaxHP);
            RestorePlayerAfterDeath();
            ResetGenerationState();

            frontierX = CampaignOriginX;
            decorFrontierX = CampaignOriginX;
            runStartX = CampaignOriginX;
            introIndex = IntroLayout.Length; // the campaign has its own opening
            levelLengthEstimate = EstimateLevelLength();

            genZone = ZoneCatalog.Get(level.Theme);
            genZoneEndX = float.MaxValue;
            activeZone = level.Theme;
            skyTarget = genZone.Sky;
            if (mainCamera != null) mainCamera.backgroundColor = skyTarget;
            SkyBackdrop.Instance?.SetTint(genZone.Tint, true);
            zoneSpans.Add(new ZoneMarker { x = CampaignOriginX, kind = level.Theme });

            ascentActive = false;
            shaftActive = false;
            shaftXStart = shaftXEnd = -1f;
            player.maxFallSpeed = 16f;
            player.jetpackActive = false;
            player.jetpackCeilingY = float.MaxValue;
            deathLineY = -fallDepthBelowGround;
            SetCameraOrthoSize(normalOrthoSize);
            ui.SetZoneLabel("");

            MobileInput.Reset();
            player.maxSpeed = CurrentRunSpeed;
            running = true;

            PlacePlayerAt(CampaignOriginX + 1.5f, 2f);
            EnsureDrone();
            GenerateGroundAhead();
            SkinCatalog.ApplyToPlayer(player);
            damageFeedback?.ResetTracking();
            player.controlEnabled = true;

            ui.ShowBanner(level.Name, level.Subtitle, 2.8f);
        }

        /// <summary>Back to the hub, abandoning the level in progress.</summary>
        public void LeaveCampaign()
        {
            running = false;
            inCampaign = false;
            levelFinished = false;
            Time.timeScale = 1f;
            ClearAll();
            MobileInput.Reset();
            player.controlEnabled = false;
        }

        void PlacePlayerAt(float x, float y)
        {
            // Teleport() only moves the Rigidbody2D and that move lands on the Transform at
            // the next physics step, so the Transform has to be set too - everything that
            // reads the player's position this frame (terrain generation, progress) would
            // otherwise still see the previous spot. Same trap as PlacePlayerAtStart.
            var start = new Vector3(x, y, player.transform.position.z);
            player.transform.position = start;
            player.Teleport(start);
            player.jumpState = PlayerController.JumpState.Grounded;
        }

        // ---- death and checkpoints -------------------------------------------------------

        /// <summary>
        /// Campaign deaths are not game overs: the level is rebuilt from the last checkpoint
        /// (or from its start) with full health. The player keeps whatever they picked up,
        /// including secrets already found, so a retry never costs progress they earned.
        /// </summary>
        void HandleCampaignDeath()
        {
            running = false;
            Sfx.Death();
            Fx.Burst(player.transform.position, new Color(0.7f, 0.15f, 0.1f), 24, 4f, 0.14f);
            Fx.Shake(0.5f, 0.35f);
            MobileInput.Reset();
            Time.timeScale = 0f;
            ui.ShowLevelFailed(level.Name, hasCheckpoint);
            AdService.OnPlayerDeath();
        }

        /// <summary>Rebuilds the level from the last checkpoint and hands control back.</summary>
        public void RetryFromCheckpoint()
        {
            if (!inCampaign) return;
            Time.timeScale = 1f;
            Simulation.Clear();
            ClearAll();

            if (!hasCheckpoint)
            {
                StartLevel(levelIndex);
                return;
            }

            UpgradeManager.ApplyToPlayer(player, baseMaxSpeed, baseMaxHP);
            RestorePlayerAfterDeath();
            ResetGenerationState();

            cmdIndex = checkpoint.cmdIndex;
            secretsFound = checkpoint.secretsFound;
            frontierX = checkpoint.frontierX;
            lastTopY = checkpoint.lastTopY;
            baselineY = checkpoint.baselineY;
            lastSegmentWasGap = checkpoint.wasGap;
            lastSegmentWasUnstable = checkpoint.wasUnstable;
            lastSegmentWidth = checkpoint.lastSegmentWidth;
            decorFrontierX = checkpoint.decorFrontierX;
            introIndex = IntroLayout.Length;
            levelFinished = false;

            genZone = ZoneCatalog.Get(checkpoint.genZoneKind);
            genZoneEndX = float.MaxValue;
            activeZone = checkpoint.activeZoneKind;
            var activeDef = ZoneCatalog.Get(activeZone);
            skyTarget = activeDef.Sky;
            if (mainCamera != null) mainCamera.backgroundColor = skyTarget;
            SkyBackdrop.Instance?.SetTint(activeDef.Tint, true);
            zoneSpans.Add(new ZoneMarker { x = checkpoint.frontierX - 1f, kind = checkpoint.genZoneKind });

            ascentActive = false;
            shaftActive = false;
            shaftXStart = shaftXEnd = -1f;
            player.maxFallSpeed = 16f;
            player.jetpackActive = false;
            player.jetpackCeilingY = float.MaxValue;
            deathLineY = checkpoint.playerY - fallDepthBelowGround;
            SetCameraOrthoSize(normalOrthoSize);

            MobileInput.Reset();
            player.maxSpeed = CurrentRunSpeed;
            running = true;

            PlacePlayerAt(checkpoint.playerX, checkpoint.playerY + 0.6f);
            EnsureDrone();
            GenerateGroundAhead();
            SkinCatalog.ApplyToPlayer(player);
            damageFeedback?.ResetTracking();
            player.controlEnabled = true;

            ui.SetZoneLabel(activeDef.Title);
            ui.ShowBanner("REPRISE", "Au dernier drapeau", 1.6f);
        }

        void RecordCheckpoint(CampaignState state)
        {
            // Respawning rebuilds this very flag, so its trigger fires again the instant the
            // player lands on it. Re-recording is harmless, but announcing it again is not.
            bool alreadyHere = hasCheckpoint && checkpoint.cmdIndex == state.cmdIndex;
            hasCheckpoint = true;
            state.secretsFound = secretsFound; // whatever was found on the way here, is kept
            checkpoint = state;
            if (alreadyHere) return;
            Sfx.Milestone();
            Fx.Burst(new Vector3(state.playerX, state.playerY + 1f, 0f), ApogeeTheme.Gold, 18, 3.5f, 0.11f);
            ui.ShowBanner("POINT DE CONTRÔLE", "Tu repartiras d'ici", 1.6f);
        }

        void OnSecretFound()
        {
            secretsFound++;
            if (secretsFound >= level.SecretCount && level.SecretCount > 0)
                ui.ShowBanner("TOUS LES SECRETS !", "Étoile débloquée", 2f);
        }

        // ---- reaching the gate -----------------------------------------------------------

        void CompleteLevel()
        {
            if (levelFinished) return;
            levelFinished = true;
            running = false;

            float healthLeft = player.health != null ? player.health.NormalizedHP : 1f;
            SaveSystem.SetLevelBestHealth(levelIndex, healthLeft);

            MobileInput.Reset();
            Sfx.Milestone();
            Fx.Burst(player.transform.position, ApogeeTheme.Gold, 40, 6f, 0.15f, 0.5f);

            // Tear the level down before the duel: the arena is a full-screen panel, but the
            // zombies still standing behind it would keep chasing and biting through it.
            player.controlEnabled = false;
            ClearAll();
            Time.timeScale = 0f;

            ui.ShowLevelCleared(levelIndex, level, secretsFound, healthLeft);
        }

        // ---- the executor ----------------------------------------------------------------

        /// <summary>
        /// Streams the authored script ahead of the player. Commands that lay terrain push
        /// the frontier forward; commands that decorate ("spikes here, two walkers there")
        /// consume no space and attach to the ground the previous command laid.
        /// </summary>
        void GenerateCampaignAhead()
        {
            int guard = 0;
            while (frontierX < player.transform.position.x + genAheadDistance * ReachScale)
            {
                if (cmdIndex >= level.Script.Length) return;
                if (++guard > 200) return; // never stall the frame on a malformed script
                var cmd = level.Script[cmdIndex++];
                ExecuteCommand(cmd);
            }
        }

        void ExecuteCommand(LevelCmd cmd)
        {
            float reach = RunReach;
            switch (cmd.Beat)
            {
                case Beat.Zone:
                    CampaignZone((ZoneKind)cmd.I);
                    break;

                case Beat.Ground:
                    LayGround(Mathf.Max(2.5f, cmd.A * reach), cmd.B, unstable: false);
                    break;

                case Beat.Slab:
                    // Collapsing slabs stay short enough to be cleared in one jump from the
                    // solid ground before them, as in the endless run.
                    LayGround(Mathf.Clamp(cmd.A * reach, 2.4f, 3.2f), 0f, unstable: true);
                    break;

                case Beat.Gap:
                    // Capped at what a full-speed jump actually clears, whatever the script says.
                    GenerateGap(Mathf.Min(cmd.A, 0.72f) * reach);
                    break;

                case Beat.StoneGap:
                    GenerateStoneBridgedGap();
                    break;

                case Beat.Spikes:
                    PlaceOnLastGround(cmd.B, x => props.Add(Hazard.CreateSpikes(entityParent, x, lastGroundTopY, cmd.A).gameObject));
                    break;

                case Beat.Toxic:
                    PlaceOnLastGround(cmd.B, x => props.Add(Hazard.CreateToxicPool(entityParent, x, lastGroundTopY, cmd.A).gameObject));
                    break;

                case Beat.Ledge:
                    PlaceOnLastGround(0.5f, x =>
                    {
                        var go = CreateSolidPlatform($"Ledge_{x:0}", x, lastGroundTopY + cmd.B, cmd.A, 0.35f, genZone.Ground);
                        go.GetComponent<SpriteRenderer>().color = new Color(1.15f, 1.1f, 1.05f);
                        props.Add(go);
                        SpawnPickupAt(x, lastGroundTopY + cmd.B + 0.6f, PickupType.Coin);
                    });
                    break;

                case Beat.Spring:
                    PlaceOnLastGround(0.5f, x =>
                    {
                        var pad = BouncePlatform.Create(entityParent, x, lastGroundTopY + cmd.B, 1.8f,
                            cmd.I == 1 ? BounceKind.Spring : BounceKind.Normal, x - 1f, x + 1f);
                        props.Add(pad.gameObject);
                    });
                    break;

                case Beat.Tower:
                    BuildTower(frontierX);
                    break;

                case Beat.Shaft:
                    BuildShaft(frontierX);
                    break;

                case Beat.Archipel:
                    BuildArchipelago(frontierX, Mathf.Max(3, cmd.I));
                    break;

                case Beat.Jetpack:
                    for (int i = 0; i < Mathf.Max(1, cmd.I); i++) GenerateJetpackStretch();
                    jetpackLandingX = frontierX;
                    LayGround(9f * ReachScale, 0f, unstable: false);
                    break;

                case Beat.Storm:
                    SpawnLevelTrigger("StormStart", 3f, () =>
                    {
                        if (chaseWall != null) chaseWall.Dissipate();
                        chaseWall = ChaseWall.Create(entityParent, this, player, player.transform.position.x - 16f);
                        Fx.Shake(0.4f, 0.5f);
                    });
                    break;

                case Beat.StormEnd:
                    SpawnLevelTrigger("StormEnd", 3f, () =>
                    {
                        if (chaseWall == null) return;
                        chaseWall.Dissipate();
                        chaseWall = null;
                    });
                    break;

                case Beat.Foes:
                    PlaceOnLastGround(cmd.B, x =>
                    {
                        int count = Mathf.Max(1, Mathf.RoundToInt(cmd.A));
                        for (int i = 0; i < count; i++)
                        {
                            float px = x + i * 1.2f;
                            if (!IsWalkable(px)) break;
                            SpawnZombie(px, (ZombieKind)cmd.I);
                        }
                    });
                    break;

                case Beat.Coins:
                    PlaceOnLastGround(0.5f, x =>
                    {
                        int count = Mathf.Max(1, cmd.I);
                        float start = x - (count - 1) * 0.5f;
                        for (int i = 0; i < count; i++)
                            SpawnPickupAt(start + i, lastGroundTopY + Mathf.Max(0.6f, cmd.B), PickupType.Coin);
                    });
                    break;

                case Beat.Item:
                    PlaceOnLastGround(cmd.B, x => SpawnPickupAt(x, lastGroundTopY + 0.6f, (PickupType)cmd.I));
                    break;

                case Beat.Secret:
                    BuildSecret((SecretKind)cmd.I);
                    break;

                case Beat.Checkpoint:
                    BuildCheckpoint();
                    break;

                case Beat.Sign:
                    string text = cmd.S;
                    SpawnLevelTrigger("Sign", 5f, () => ui.ShowBanner(level.Name, text, 2.4f));
                    break;

                case Beat.Goal:
                    BuildGoal();
                    break;
            }
        }

        /// <summary>Lays one authored ground segment and remembers it as the decoration anchor.</summary>
        void LayGround(float width, float heightDelta, bool unstable)
        {
            float topY = lastTopY + heightDelta;
            float segStart = frontierX;
            GenerateSegment(segStart, width, topY, allowBonusPlatform: false, unstable: unstable);
            lastTopY = topY;
            lastSegmentWasGap = false;
            lastSegmentWasUnstable = unstable;
            lastSegmentWidth = width;

            lastGroundStart = segStart;
            lastGroundWidth = width;
            lastGroundTopY = topY;
        }

        /// <summary>Runs an action at a position along the ground the last Ground command laid.</summary>
        void PlaceOnLastGround(float t, System.Action<float> place)
        {
            if (lastGroundWidth <= 0f) return;
            // Keep clear of both edges so nothing is placed where a jump lands or takes off.
            float margin = Mathf.Min(1.2f, lastGroundWidth * 0.25f);
            float x = Mathf.Lerp(lastGroundStart + margin, lastGroundStart + lastGroundWidth - margin, Mathf.Clamp01(t));
            place(x);
        }

        void CampaignZone(ZoneKind kind)
        {
            genZone = ZoneCatalog.Get(kind);
            zoneMarkers.Add(new ZoneMarker { x = frontierX, kind = kind });
            zoneSpans.Add(new ZoneMarker { x = frontierX, kind = kind });
        }

        /// <summary>
        /// A trigger covering the span that starts at the frontier, so it reacts to a player
        /// arriving from behind AND to one who begins the level already standing inside it
        /// (the opening sign sits at the very first meter of the level).
        /// </summary>
        LevelTrigger SpawnLevelTrigger(string name, float width, System.Action onEntered)
        {
            var trigger = LevelTrigger.Create(entityParent, name, frontierX + width * 0.5f, lastTopY + 2.5f, width, 8f);
            trigger.OnEntered = onEntered;
            props.Add(trigger.gameObject);
            return trigger;
        }

        // ---- checkpoint flag --------------------------------------------------------------

        void BuildCheckpoint()
        {
            // Freeze the generation state as it was *before* this command ran, and resume
            // from the Checkpoint command itself rather than from the one after it: the
            // rebuild then re-lays the flag's own ground, so the player respawns standing
            // on it. Resuming one command later would drop them wherever the next command
            // happens to start - over an archipelago's void, or into a jetpack lake.
            // The state is captured into the trigger's closure, not into fields, because
            // the next checkpoint can be generated before this one is ever reached.
            var state = new CampaignState
            {
                cmdIndex = cmdIndex - 1,
                frontierX = frontierX,
                lastTopY = lastTopY,
                baselineY = baselineY,
                lastSegmentWidth = lastSegmentWidth,
                decorFrontierX = frontierX,
                wasGap = false,
                wasUnstable = false,
                genZoneKind = genZone.Kind,
                activeZoneKind = genZone.Kind,
            };

            float width = 1.6f * ReachScale * 2.4f;
            LayGround(width, 0f, unstable: false);

            float flagX = lastGroundStart + width * 0.5f;
            float groundY = lastGroundTopY;

            var pole = CreateSolidPlatform($"FlagPole_{flagX:0}", flagX, groundY + 1.4f, 0.14f, 2.8f, ApogeeTheme.GoldDark);
            Destroy(pole.GetComponent<BoxCollider2D>()); // decoration only, never a wall
            props.Add(pole);

            var banner = new GameObject("FlagBanner");
            banner.transform.SetParent(entityParent, false);
            banner.transform.position = new Vector3(flagX + 0.55f, groundY + 2.4f, 0f);
            banner.transform.localScale = new Vector3(1.0f, 0.62f, 1f);
            var bsr = banner.AddComponent<SpriteRenderer>();
            bsr.sprite = PlaceholderVisuals.Square(ApogeeTheme.Crimson);
            bsr.sortingOrder = 2;
            props.Add(banner);

            state.playerX = flagX;
            state.playerY = groundY;
            var trigger = LevelTrigger.Create(entityParent, "Checkpoint", flagX, groundY + 1.5f, 1.6f, 5f);
            trigger.OnEntered = () => RecordCheckpoint(state);
            props.Add(trigger.gameObject);
        }

        // ---- the gate ---------------------------------------------------------------------

        void BuildGoal()
        {
            float width = 5f * ReachScale * 2.4f;
            LayGround(width, 0f, unstable: false);

            float gateX = lastGroundStart + width * 0.42f;
            float groundY = lastGroundTopY;

            foreach (float side in new[] { -1.1f, 1.1f })
            {
                var pillar = CreateSolidPlatform($"GatePillar_{side}", gateX + side, groundY + 1.8f, 0.5f, 3.6f, new Color(0.38f, 0.30f, 0.24f));
                Destroy(pillar.GetComponent<BoxCollider2D>()); // the player runs through the gate, not into it
                pillar.GetComponent<SpriteRenderer>().sortingOrder = 1;
                props.Add(pillar);
            }
            var lintel = CreateSolidPlatform("GateLintel", gateX, groundY + 3.8f, 3.2f, 0.55f, ApogeeTheme.GoldDark);
            Destroy(lintel.GetComponent<BoxCollider2D>());
            lintel.GetComponent<SpriteRenderer>().sortingOrder = 1;
            props.Add(lintel);

            // The glowing curtain between the pillars, so the exit is unmistakable.
            var glow = new GameObject("GateGlow");
            glow.transform.SetParent(entityParent, false);
            glow.transform.position = new Vector3(gateX, groundY + 1.8f, 0f);
            glow.transform.localScale = new Vector3(1.8f, 3.6f, 1f);
            var gsr = glow.AddComponent<SpriteRenderer>();
            gsr.sprite = PlaceholderVisuals.Square(Color.white);
            gsr.color = new Color(ApogeeTheme.Gold.r, ApogeeTheme.Gold.g, ApogeeTheme.Gold.b, 0.35f);
            gsr.sortingOrder = 0;
            props.Add(glow);

            var trigger = LevelTrigger.Create(entityParent, "LevelGoal", gateX, groundY + 1.6f, 1.8f, 5f);
            trigger.OnEntered = CompleteLevel;
            props.Add(trigger.gameObject);
        }

        // ---- secrets -----------------------------------------------------------------------

        void BuildSecret(SecretKind kind)
        {
            if (kind == SecretKind.Sky) BuildSkySecret();
            else BuildUndergroundSecret(sealedIn: kind == SecretKind.Sealed);
        }

        /// <summary>
        /// A treasure room under the path, entered through a narrow hole in the floor that
        /// most players jump straight over. The room is walled at both ends and has a spring
        /// pad right under the hole, so getting out is never a puzzle - finding it is.
        ///
        /// The sealed variant puts a cracked wall across the room with the chest behind it.
        /// The wall stands upright on purpose: the gun only ever fires horizontally (see
        /// PlayerCombat), so a cracked slab lying in the floor would be unbreakable and the
        /// secret unreachable. Standing it up puts it squarely in the line of fire.
        /// </summary>
        void BuildUndergroundSecret(bool sealedIn)
        {
            float reach = RunReach;
            float groundY = lastTopY;

            LayGround(1.3f * reach, 0f, unstable: false);

            float holeX = frontierX;
            float holeW = 0.5f * reach;
            RegisterGap(holeX, holeW);

            LayGround(1.7f * reach, 0f, unstable: false);
            // The ground after the hole is the anchor again, so a following Coins/Foes lands there.

            // The drop shaft sits under the hole; a sealed room adds a wing to its right,
            // behind the cracked wall.
            float holeCx = holeX + holeW * 0.5f;
            float roomY = groundY - 5f;
            float roomLeft = holeCx - 3f;
            float roomRight = holeCx + (sealedIn ? 6f : 3f);
            float roomW = roomRight - roomLeft;
            float roomCx = (roomLeft + roomRight) * 0.5f;
            const float ceilingY = -1.1f; // relative to the path above
            var stone = new Color(0.26f, 0.21f, 0.19f);

            props.Add(CreateSolidPlatform($"SecretFloor_{roomCx:0}", roomCx, roomY - 0.35f, roomW, 0.7f, stone));
            foreach (float edge in new[] { roomLeft, roomRight })
                props.Add(CreateSolidPlatform($"SecretWall_{edge:0}", edge, roomY + 1.9f, 0.5f, 4.4f, stone));

            // Ceiling either side of the hole, so the room reads as a room and the only way
            // back up is through the opening.
            foreach (var (from, to) in new[] { (roomLeft, holeX), (holeX + holeW, roomRight) })
            {
                float w = to - from;
                if (w < 0.4f) continue;
                props.Add(CreateSolidPlatform($"SecretCeil_{from:0}", (from + to) * 0.5f, groundY + ceilingY, w, 0.4f, stone));
            }

            // Straight up through the hole, always on the landing side of any wall.
            var pad = BouncePlatform.Create(entityParent, holeCx, roomY + 0.4f, 1.9f, BounceKind.Spring, holeCx - 1f, holeCx + 1f);
            props.Add(pad.gameObject);

            SpawnPickupAt(holeCx, roomY + 2.2f, PickupType.Coin);
            SpawnPickupAt(holeCx, roomY + 3.4f, PickupType.Coin);
            SpawnPickupAt(holeCx - 1f, roomY + 0.8f, PickupType.Coin);

            float stashX = holeCx - 2f;
            if (sealedIn)
            {
                // The cracked wall stands between the landing spot and the treasure wing:
                // upright, at the height the player's gun actually fires, and tall enough
                // that they cannot simply jump it.
                float wallX = holeCx + 1.8f;
                float wallHeight = (groundY + ceilingY) - roomY;
                var cracked = BreakableWall.Create(entityParent, wallX, roomY + wallHeight * 0.5f, 0.5f, wallHeight);
                props.Add(cracked.gameObject);

                stashX = holeCx + 4f;
                SpawnPickupAt(holeCx + 2.9f, roomY + 0.8f, PickupType.Material);
            }

            var stash = SecretStash.Create(entityParent, stashX, roomY + 0.6f);
            stash.OnFound = OnSecretFound;
            props.Add(stash.gameObject);
            SpawnPickupAt(sealedIn ? holeCx + 5f : holeCx + 1.8f, roomY + 0.8f, PickupType.Material);
        }

        /// <summary>
        /// A balcony high over the path with the treasure on it. The spring pad on the
        /// ground throws the player well above the balcony's underside before they reach
        /// its edge, so holding forward on the bounce carries them onto it cleanly.
        /// </summary>
        void BuildSkySecret()
        {
            float reach = RunReach;
            float width = 3.4f * reach;
            LayGround(width, 0f, unstable: false);

            float groundY = lastGroundTopY;
            float padX = lastGroundStart + 2.5f;
            const float ledgeTopOffset = 4.2f;
            float ledgeTop = groundY + ledgeTopOffset;

            // Geometry worked out from the spring itself (BouncePlatform bounces the player
            // at 9.2 * 1.35 = 12.42 m/s, a 7.9 m rise, 2.5 s in the air at 3.6 m/s forward):
            //  - the player clears the balcony's underside about 1 m past the pad, so the
            //    balcony must start further right than that or they bump their head;
            //  - holding forward the whole bounce, they come back down to balcony height
            //    about 7.9 m past the pad, so the balcony must still be under them there.
            // Hence a wide balcony from 2 m to 10 m past the pad, with the chest where a
            // full-speed bounce actually lands.
            const float ledgeStart = 2f, ledgeEnd = 10f;
            float ledgeCx = padX + (ledgeStart + ledgeEnd) * 0.5f;

            var pad = BouncePlatform.Create(entityParent, padX, groundY + 0.35f, 1.9f, BounceKind.Spring, padX - 1f, padX + 1f);
            props.Add(pad.gameObject);

            var ledge = CreateSolidPlatform($"SecretLedge_{ledgeCx:0}", ledgeCx, ledgeTop - 0.25f, ledgeEnd - ledgeStart, 0.5f,
                new Color(0.30f, 0.24f, 0.21f));
            props.Add(ledge);

            var stash = SecretStash.Create(entityParent, padX + 7.6f, ledgeTop + 0.7f);
            stash.OnFound = OnSecretFound;
            props.Add(stash.gameObject);

            // Coins tracing the bounce, so the balcony reads as reachable rather than scenery.
            for (int i = 0; i < 4; i++)
                SpawnPickupAt(padX + 0.6f + i * 0.9f, groundY + 1.6f + i * 0.9f, PickupType.Coin);
            SpawnPickupAt(padX + 3.2f, ledgeTop + 0.7f, PickupType.Material);
        }

        // ---- progress estimate ---------------------------------------------------------------

        /// <summary>
        /// Roughly how long the level is in meters, only ever used to fill the HUD's progress
        /// bar. Set pieces contribute the span they actually occupy on the x axis.
        /// </summary>
        float EstimateLevelLength()
        {
            float reach = RunReach;
            float total = 0f;
            foreach (var cmd in level.Script)
            {
                switch (cmd.Beat)
                {
                    case Beat.Ground: total += Mathf.Max(2.5f, cmd.A * reach); break;
                    case Beat.Slab: total += Mathf.Clamp(cmd.A * reach, 2.4f, 3.2f); break;
                    case Beat.Gap: total += Mathf.Min(cmd.A, 0.72f) * reach; break;
                    case Beat.StoneGap: total += 1.6f * reach; break;
                    case Beat.Tower: total += TowerWidth + 10f * ReachScale; break;
                    case Beat.Shaft: total += Mathf.Max(8f, 1.5f * reach) + 10f * ReachScale; break;
                    case Beat.Archipel: total += 0.95f * reach * Mathf.Max(3, cmd.I) + 1.5f + 9f * ReachScale; break;
                    case Beat.Jetpack: total += 6f * Mathf.Max(1, cmd.I) + 9f * ReachScale; break;
                    case Beat.Secret: total += cmd.I == (int)SecretKind.Sky ? 3.2f * reach : 3.5f * reach; break;
                    case Beat.Checkpoint: total += 1.6f * ReachScale * 2.4f; break;
                    case Beat.Goal: total += 5f * ReachScale * 2.4f * 0.42f; break;
                }
            }
            return Mathf.Max(10f, total);
        }
    }
}
