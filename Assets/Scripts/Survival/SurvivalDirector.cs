using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Cinemachine;
using Platformer.Core;
using Platformer.Gameplay;
using Platformer.Mechanics;
using Platformer.Model;

namespace Platformer.Survival
{
    /// <summary>
    /// Drives the endless survival loop. This file owns the run lifecycle and the
    /// per-frame player-space logic (auto-run speed ramp, zone activation, the Doodle-Jump
    /// ascent state, the dynamic death line, camera framing, coin magnet, milestones);
    /// terrain generation lives in SurvivalDirector.Terrain.cs and entity spawning in
    /// SurvivalDirector.Spawning.cs.
    ///
    /// The player drives the character with the on-screen joystick / jump / fire buttons
    /// (see VirtualControls, MobileInput) or keyboard. Max run speed rises gently with
    /// distance, and every terrain distance is scaled by the jump reach at that max speed
    /// (see RunReach) so the layout stays fair as speed climbs.
    /// </summary>
    public partial class SurvivalDirector : MonoBehaviour
    {
        public static SurvivalDirector Instance { get; private set; }

        PlayerController player;
        RuntimeUI ui;
        PlatformerModel model;
        PlayerDamageFeedback damageFeedback;
        CinemachinePositionComposer composer;

        [Header("Ground generation")]
        public float genAheadDistance = 25f;
        public float despawnBehindDistance = 22f;
        public float groundThickness = 1f;

        [Header("Spawning")]
        public float zombieBaseInterval = 3.2f;
        public float pickupBaseInterval = 2.2f;
        public float minAheadSpawn = 8f;
        public float maxAheadSpawn = 16f;

        [Header("Run speed")]
        public float baseRunSpeed = 3.6f;
        /// <summary>Fraction added to the max run speed by the end of the ramp.</summary>
        public float runSpeedRampBonus = 0.2f;
        public float runSpeedRampDistance = 450f;
        public float ascentSteerSpeed = 4f;

        [Header("Camera")]
        public float normalOrthoSize = 3.5f;
        public float fastOrthoSize = 4.7f;
        public float ascentOrthoSize = 5.6f;

        [Header("Death line")]
        public float fallDepthBelowGround = 11f;
        public float ascentFallDepth = 11f;

        struct GroundSegment
        {
            public float xStart, xEnd, topY;
            public bool isGap;
            public GameObject go;
        }

        struct ZoneMarker
        {
            public float x;
            public ZoneKind kind;
        }

        readonly List<GroundSegment> segments = new();
        readonly List<Zombie> zombies = new();
        readonly List<Pickup> pickups = new();
        /// <summary>Bonus platforms, stepping stones, hazards, bounce platforms, tower backdrops: anything recycled purely by x.</summary>
        readonly List<GameObject> props = new();
        readonly List<GameObject> ruins = new();
        readonly List<ZoneMarker> zoneMarkers = new();
        /// <summary>Start x of every generated zone (not consumed), to know the zone at any x.</summary>
        readonly List<ZoneMarker> zoneSpans = new();

        // --- generation (frontier-space) state ---
        int introIndex;
        float frontierX;
        float lastTopY;
        bool lastSegmentWasGap;
        bool lastSegmentWasUnstable;
        float lastSegmentWidth;
        float decorFrontierX;
        /// <summary>Street level the terrain wanders around: 0 normally, the roof height while on the rooftops.</summary>
        float baselineY;
        ZoneDef genZone;
        float genZoneEndX;
        int zonesSinceAscent;
        float lastTowerBaseY;
        float shaftXStart, shaftXEnd, shaftTop;
        /// <summary>Frontier x where the current/last jetpack zone hands back to solid ground.</summary>
        float jetpackLandingX;

        // --- player-space state ---
        ZoneKind activeZone;
        Color skyTarget;
        bool ascentActive;
        bool shaftActive;
        float highestBounceY;
        float deathLineY;
        float zombieTimer, pickupTimer;
        float nextHordeDistance;
        int nextMilestone;
        float runStartX;
        float baseMaxSpeed;
        int baseMaxHP;
        bool running;

        GameObject fallDeathZone;
        ParticleSystem ashDrift;
        Transform entityParent;
        Camera mainCamera;

        public bool IsRunning => running;
        public PlayerController Player => player;
        /// <summary>The zone the player is currently running through (not the one being generated ahead).</summary>
        public ZoneKind ActiveZone => activeZone;
        public bool IsAscending => ascentActive;
        public bool IsFallingInShaft => shaftActive;
        public bool IsFlying => player != null && player.jetpackActive;
        /// <summary>Distance travelled this run, in meters (1 world unit = 1 meter) - the game's scoring metric.</summary>
        public float Distance => player != null ? Mathf.Max(0f, player.transform.position.x - runStartX) : 0f;
        /// <summary>Enemy stat scaler: 1 at the start, +1 every 40 m, uncapped (individual stats clamp themselves).</summary>
        float Difficulty => 1f + Distance / 40f;
        /// <summary>Terrain harshness scaler: 0 at the start of a run, 1 after 350 m.</summary>
        float Ramp => Mathf.Clamp01(Distance / 350f);
        float SpeedRamp => Mathf.Clamp01(Distance / runSpeedRampDistance);

        /// <summary>Max run speed right now: base, ramping up with distance, times the Speed upgrade.</summary>
        public float CurrentRunSpeed => baseRunSpeed * (1f + runSpeedRampBonus * SpeedRamp) * UpgradeManager.SpeedMultiplier;

        /// <summary>Time a full-height jump keeps the player airborne (take-off speed vs gravity).</summary>
        float JumpAirTime
        {
            get
            {
                float takeOff = player != null ? player.jumpTakeOffSpeed * (model != null ? model.jumpModifier : 1f) : 6.3f;
                return 2f * takeOff / Mathf.Max(0.01f, -Physics2D.gravity.y);
            }
        }

        /// <summary>Horizontal distance a full-speed jump covers right now - what every gap is sized against.</summary>
        public float RunReach => CurrentRunSpeed * JumpAirTime;

        void Awake()
        {
            Instance = this;
            entityParent = new GameObject("SurvivalEntities").transform;
        }

        public void Configure(PlayerController playerController, RuntimeUI runtimeUi)
        {
            player = playerController;
            ui = runtimeUi;
            model = Simulation.GetModel<PlatformerModel>();
            damageFeedback = player.GetComponent<PlayerDamageFeedback>();
            mainCamera = Camera.main;
            baseMaxSpeed = player.maxSpeed;
            baseMaxHP = player.health != null ? player.health.maxHP : 5;

            // Releasing jump early now shortens the jump smoothly instead of killing it dead.
            if (model != null) model.jumpDeceleration = 0.5f;
            SetupCameraFraming();
            SkyBackdrop.Create(mainCamera)?.SetTint(Color.white, true);
            MobileInput.Active = true;
            MobileInput.Reset();

            PlayerDeath.OnExecute += HandlePlayerDeath;
            CreateFallDeathZone();
            CreateAshDrift();
            SetupScenery();

            // The sample's hand-painted level is disabled at bootstrap (see GameBootstrap),
            // but the fixed intro ground (IntroGround_A..E in SampleScene.unity) always
            // exists and is never touched, so the player already has real ground under
            // them here without needing to generate anything. Player stays frozen
            // (controlEnabled = false) until StartRun().
            ResetGenerationState();
            skyTarget = ZoneCatalog.Get(ZoneKind.City).Sky;
            PlacePlayerAtStart();
            SkinCatalog.ApplyToPlayer(player);
            player.controlEnabled = false;
        }

        /// <summary>
        /// Frames the runner for speed: the character sits left of center and the composer
        /// looks ahead along its velocity, so the player sees what they are running into.
        /// </summary>
        void SetupCameraFraming()
        {
            if (model?.virtualCamera == null) return;
            composer = model.virtualCamera.GetComponent<CinemachinePositionComposer>();
            var baseOffset = new Vector3(0.6f, 0.5f, 0f);
            if (composer != null)
            {
                var lookahead = composer.Lookahead;
                lookahead.Enabled = true;
                lookahead.Time = 0.35f;
                lookahead.Smoothing = 8f;
                lookahead.IgnoreY = true;
                composer.Lookahead = lookahead;
            }
            Fx.BindCamera(composer, baseOffset);
        }

        void PlacePlayerAtStart()
        {
            player.Teleport(new Vector3(1f, 2f, 0f));
            player.jumpState = PlayerController.JumpState.Grounded;
        }

        void OnDestroy()
        {
            PlayerDeath.OnExecute -= HandlePlayerDeath;
        }

        void HandlePlayerDeath(PlayerDeath ev)
        {
            if (!running) return;
            running = false;
            SaveSystem.BestDistance = Mathf.Max(SaveSystem.BestDistance, Distance);
            Sfx.Death();
            Fx.Burst(player.transform.position, new Color(0.7f, 0.15f, 0.1f), 24, 4f, 0.14f);
            Fx.Shake(0.5f, 0.35f);
            MobileInput.Reset();
            // Freezes Time.time, which also stops the sample's own scheduled PlayerSpawn
            // (Simulation events compare against Time.time) so it can't silently teleport
            // the player back to the old hand-painted spawn point while Game Over is up.
            Time.timeScale = 0f;
            ui.ShowGameOver(Distance);
            AdService.OnPlayerDeath();
        }

        public void StartRun()
        {
            Time.timeScale = 1f;
            // The sample's PlayerDeath schedules a PlayerSpawn 2 s later that would teleport
            // the player back to the disabled level's spawn point and freeze input; drop it
            // (and anything else left over from the previous run) before starting fresh.
            Simulation.Clear();
            ClearAll();

            UpgradeManager.ApplyToPlayer(player, baseMaxSpeed, baseMaxHP);
            RestorePlayerAfterDeath();

            ResetGenerationState();
            runStartX = 1f;
            zombieTimer = zombieBaseInterval;
            pickupTimer = pickupBaseInterval;
            nextHordeDistance = UnityEngine.Random.Range(80f, 110f);
            nextMilestone = 100;

            activeZone = ZoneKind.City;
            skyTarget = ZoneCatalog.Get(ZoneKind.City).Sky;
            if (mainCamera != null) mainCamera.backgroundColor = skyTarget;
            SkyBackdrop.Instance?.SetTint(ZoneCatalog.Get(ZoneKind.City).Tint, true);
            ascentActive = false;
            shaftActive = false;
            player.maxFallSpeed = 16f;
            shaftXStart = shaftXEnd = -1f;
            player.jetpackActive = false;
            player.jetpackCeilingY = float.MaxValue;
            deathLineY = -fallDepthBelowGround;
            SetCameraOrthoSize(normalOrthoSize);
            ui.SetZoneLabel("");

            MobileInput.Reset();
            player.maxSpeed = CurrentRunSpeed;
            running = true;

            PlacePlayerAtStart();
            // Builds the fixed intro (see IntroLayout) up to genAheadDistance synchronously,
            // so the ground the player is standing on already exists before the first frame
            // renders; the rest fills in incrementally via Update() as they advance, exactly
            // like the procedural section past the intro.
            GenerateGroundAhead();
            SkinCatalog.ApplyToPlayer(player);
            damageFeedback?.ResetTracking();
            player.controlEnabled = true;
        }

        /// <summary>
        /// Undoes what the sample's PlayerDeath event did to the player and camera (it
        /// detaches the camera and flags the death animation, expecting its own respawn
        /// flow to restore them, which we deliberately skip).
        /// </summary>
        void RestorePlayerAfterDeath()
        {
            if (player.collider2d != null) player.collider2d.enabled = true;
            if (player.animator != null) player.animator.SetBool("dead", false);
            if (model?.virtualCamera != null)
            {
                model.virtualCamera.Follow = player.transform;
                model.virtualCamera.LookAt = player.transform;
            }
        }

        void ResetGenerationState()
        {
            frontierX = 0f;
            lastTopY = 0f;
            baselineY = 0f;
            lastSegmentWasGap = false;
            lastSegmentWasUnstable = false;
            lastSegmentWidth = 10f;
            decorFrontierX = 0f;
            introIndex = 0;
            zonesSinceAscent = 0;
            genZone = ZoneCatalog.Get(ZoneKind.City);
            genZoneEndX = float.MaxValue;
            zoneMarkers.Clear();
            zoneSpans.Clear();
        }

        void ClearAll()
        {
            foreach (var s in segments) if (s.go != null) Destroy(s.go);
            segments.Clear();
            foreach (var z in zombies) if (z != null) Destroy(z.gameObject);
            zombies.Clear();
            foreach (var p in pickups) if (p != null) Destroy(p.gameObject);
            pickups.Clear();
            foreach (var b in props) if (b != null) Destroy(b);
            props.Clear();
            foreach (var r in ruins) if (r != null) Destroy(r);
            ruins.Clear();
            ResetScenery();
        }

        void Update()
        {
            if (!running || player == null) return;

            ui.UpdateHud(player.health, Distance, SaveSystem.Coins, SaveSystem.Materials);

            // Slower, more precise steering while climbing the tower.
            player.maxSpeed = ascentActive ? ascentSteerSpeed : CurrentRunSpeed;

            GenerateGroundAhead();
            GenerateBackgroundDecorOrScenery();
            RecycleBehind();

            UpdateZoneMarkers();
            UpdateAscent();
            UpdateShaft();
            UpdateJetpack();
            UpdateDeathLine();
            UpdateCamera();
            UpdateAtmosphere();
            UpdateScenery();
            UpdateMagnet();
            UpdateMilestones();

            // Random spawns only kick in past the fixed, always-identical intro stretch.
            if (introIndex >= IntroLayout.Length) UpdateSpawnTimers();

            if (ashDrift != null)
                ashDrift.transform.position = new Vector3(player.transform.position.x, player.transform.position.y + 4f, 0f);
        }

        // ---- zones (player-space) ---------------------------------------------------

        void UpdateZoneMarkers()
        {
            float x = player.transform.position.x;
            while (zoneMarkers.Count > 0 && x >= zoneMarkers[0].x)
            {
                ActivateZone(zoneMarkers[0].kind);
                zoneMarkers.RemoveAt(0);
            }
        }

        void ActivateZone(ZoneKind kind)
        {
            activeZone = kind;
            var def = ZoneCatalog.Get(kind);
            skyTarget = def.Sky;
            SkyBackdrop.Instance?.SetTint(def.Tint);
            ui.ShowBanner(def.Title, def.Subtitle);
            ui.SetZoneLabel(def.Title);

            // Entering the infested zone is greeted by a welcoming committee.
            if (kind == ZoneKind.Infested)
                TrySpawnAhead(x => SpawnPack(x, 3, allowBrute: true));

            if (kind == ZoneKind.Jetpack)
            {
                player.jetpackActive = true;
                player.jetpackCeilingY = baselineY + 9f;
                Sfx.Spring();
                Fx.Burst(player.transform.position, new Color(1f, 0.7f, 0.2f), 16, 3f, 0.1f, 0f);
            }
        }

        // ---- shaft & jetpack (player-space) ------------------------------------------------

        void UpdateShaft()
        {
            float x = player.transform.position.x;
            bool inColumn = shaftXEnd > shaftXStart && x >= shaftXStart && x <= shaftXEnd;
            bool falling = inColumn && !player.IsGrounded && player.transform.position.y > 1.5f && player.velocity.y < 0f;
            if (falling && !shaftActive)
            {
                shaftActive = true;
                player.maxFallSpeed = 9f; // slower fall so the spike ledges can be steered around
                Fx.Shake(0.2f, 0.2f);
            }
            else if (shaftActive && (!inColumn || player.IsGrounded))
            {
                shaftActive = false;
                player.maxFallSpeed = 16f;
                ui.SetZoneLabel(ZoneCatalog.Get(activeZone).Title);
                if (player.IsGrounded)
                {
                    Fx.Shake(0.4f, 0.3f);
                    Fx.Burst(player.transform.position + Vector3.down * 0.5f, new Color(0.6f, 0.55f, 0.5f), 18, 3f, 0.1f);
                }
            }

            if (shaftActive)
                ui.SetZoneLabel($"CHUTE   -{Mathf.FloorToInt(Mathf.Max(0f, shaftTop - player.transform.position.y))} m");
        }

        void UpdateJetpack()
        {
            if (!player.jetpackActive) return;
            // Hand control back once the player stands on real ground (a registered solid
            // segment, not a floating debris block) past the flight - or anywhere once the
            // next zone has started, as a safety net.
            float x = player.transform.position.x;
            bool onRealGround = player.IsGrounded && IsWalkable(x);
            bool pastFlight = x >= jetpackLandingX - 1f || activeZone != ZoneKind.Jetpack;
            if (onRealGround && pastFlight)
            {
                player.jetpackActive = false;
                player.jetpackCeilingY = float.MaxValue;
                Fx.Burst(player.transform.position, new Color(0.6f, 0.55f, 0.5f), 12, 2f, 0.08f);
                Sfx.Medkit();
            }
        }

        // ---- milestones & magnet ---------------------------------------------------------

        void UpdateMilestones()
        {
            if (Distance < nextMilestone) return;
            const int bonus = 3;
            SaveSystem.AddCoins(bonus);
            Sfx.Milestone();
            Fx.Text(player.transform.position + Vector3.up * 0.8f, $"+{bonus}", PlaceholderVisuals.CoinColor, 1.2f);
            string subtitle = SpeedRamp < 1f ? "Vitesse max augmentée" : "Continue comme ça !";
            if (!ascentActive) ui.ShowBanner($"{nextMilestone} m", subtitle, 1.6f);
            nextMilestone += 100;
        }

        /// <summary>Magnet upgrade: pickups within range drift to the player.</summary>
        void UpdateMagnet()
        {
            float radius = UpgradeManager.MagnetRadius;
            if (radius <= 0f) return;
            Vector3 target = player.transform.position;
            float step = 10f * Time.deltaTime;
            for (int i = 0; i < pickups.Count; i++)
            {
                var p = pickups[i];
                if (p == null) continue;
                if ((p.transform.position - target).sqrMagnitude > radius * radius) continue;
                p.transform.position = Vector3.MoveTowards(p.transform.position, target, step);
            }
        }

        // ---- ascent (Doodle Jump section) ----------------------------------------------

        void OnBouncePlatformUsed(BouncePlatform platform, PlayerController controller)
        {
            if (!running) return;
            if (!ascentActive)
            {
                ascentActive = true;
                highestBounceY = platform.TopY;
            }
            else
            {
                highestBounceY = Mathf.Max(highestBounceY, platform.TopY);
            }
        }

        void UpdateAscent()
        {
            if (!ascentActive) return;

            if (player.IsGrounded)
            {
                // Landed on real ground (the roof) - back to running.
                ascentActive = false;
                ui.SetZoneLabel(ZoneCatalog.Get(activeZone).Title);
                return;
            }

            float altitude = Mathf.Max(0f, player.transform.position.y - lastTowerBaseY);
            ui.SetZoneLabel($"ASCENSION   +{Mathf.FloorToInt(altitude)} m");
        }

        // ---- death line ----------------------------------------------------------------

        /// <summary>
        /// The fall-death trigger tracks the terrain instead of sitting at a fixed world
        /// height: a fixed depth below the lowest nearby ground while running (so rooftop
        /// falls and street-level falls feel the same), and during an ascent a rising floor
        /// pinned a fixed distance under the highest platform reached - miss too many
        /// platforms on the way up and you drop out of the bottom, Doodle Jump style.
        /// </summary>
        void UpdateDeathLine()
        {
            if (ascentActive)
                deathLineY = Mathf.Max(deathLineY, highestBounceY - ascentFallDepth);
            else
                deathLineY = LocalGroundLevel(player.transform.position.x) - fallDepthBelowGround;

            if (fallDeathZone != null)
                fallDeathZone.transform.position = new Vector3(player.transform.position.x, deathLineY - 2f, 0f);
        }

        // ---- camera & atmosphere -------------------------------------------------------

        void UpdateCamera()
        {
            if (model?.virtualCamera == null) return;
            float target = ascentActive || shaftActive ? ascentOrthoSize
                : player.jetpackActive ? Mathf.Max(fastOrthoSize, 5.2f)
                : Mathf.Lerp(normalOrthoSize, fastOrthoSize, SpeedRamp);
            var lens = model.virtualCamera.Lens;
            if (Mathf.Approximately(lens.OrthographicSize, target)) return;
            lens.OrthographicSize = Mathf.MoveTowards(lens.OrthographicSize, target, 3f * Time.deltaTime);
            model.virtualCamera.Lens = lens;
        }

        void SetCameraOrthoSize(float size)
        {
            if (model?.virtualCamera == null) return;
            var lens = model.virtualCamera.Lens;
            lens.OrthographicSize = size;
            model.virtualCamera.Lens = lens;
        }

        void UpdateAtmosphere()
        {
            if (mainCamera == null) return;
            mainCamera.backgroundColor = Color.Lerp(mainCamera.backgroundColor, skyTarget, Time.deltaTime * 1.2f);
        }

        // ---- recycling -----------------------------------------------------------------

        void RecycleBehind()
        {
            float cutoff = player.transform.position.x - despawnBehindDistance;

            for (int i = segments.Count - 1; i >= 0; i--)
            {
                if (segments[i].xEnd < cutoff)
                {
                    if (segments[i].go != null) Destroy(segments[i].go);
                    segments.RemoveAt(i);
                }
            }
            for (int i = zombies.Count - 1; i >= 0; i--)
            {
                if (zombies[i] == null || zombies[i].transform.position.x < cutoff)
                {
                    if (zombies[i] != null) Destroy(zombies[i].gameObject);
                    zombies.RemoveAt(i);
                }
            }
            for (int i = pickups.Count - 1; i >= 0; i--)
            {
                if (pickups[i] == null || pickups[i].transform.position.x < cutoff)
                {
                    if (pickups[i] != null) Destroy(pickups[i].gameObject);
                    pickups.RemoveAt(i);
                }
            }
            RecycleList(props, cutoff);
            RecycleList(ruins, cutoff);
            RecycleScenery(cutoff);
        }

        static void RecycleList(List<GameObject> list, float cutoff)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i] == null || list[i].transform.position.x < cutoff)
                {
                    if (list[i] != null) Destroy(list[i]);
                    list.RemoveAt(i);
                }
            }
        }

        // ---- persistent helpers --------------------------------------------------------

        void CreateFallDeathZone()
        {
            fallDeathZone = new GameObject("SurvivalFallDeathZone");
            fallDeathZone.transform.SetParent(entityParent, false);
            var col = fallDeathZone.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(400f, 4f);
            fallDeathZone.transform.position = new Vector3(0f, -25f, 0f);
            fallDeathZone.AddComponent<DeathZone>();
        }

        /// <summary>
        /// Crimson autumn leaves drifting down across the view, like the Apogée key art -
        /// recentered on the player each frame (see Update) since it emits in world space
        /// over a bounded box. A few ember-orange ones mixed in, each spinning as it falls.
        /// </summary>
        void CreateAshDrift()
        {
            var go = new GameObject("FallingLeaves");
            go.transform.SetParent(entityParent, false);
            go.transform.position = new Vector3(player.transform.position.x, player.transform.position.y + 4f, 0f);

            ashDrift = go.AddComponent<ParticleSystem>();
            var main = ashDrift.main;
            main.loop = true;
            main.startLifetime = 9f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.24f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.78f, 0.14f, 0.08f, 0.95f), new Color(0.95f, 0.45f, 0.16f, 0.95f));
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 90;

            var emission = ashDrift.emission;
            emission.rateOverTime = 5f;

            var shape = ashDrift.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(30f, 16f, 0.1f);

            var vel = ashDrift.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.9f, -0.2f);
            vel.y = new ParticleSystem.MinMaxCurve(-0.7f, -0.3f);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            var rot = ashDrift.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-2.5f, 2.5f);

            // Side-to-side flutter.
            var noise = ashDrift.noise;
            noise.enabled = true;
            noise.strength = 0.5f;
            noise.frequency = 0.35f;
            noise.scrollSpeed = 0.2f;

            var fade = ashDrift.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(1f, 0.85f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = new Material(Shader.Find("Sprites/Default")) { mainTexture = ApogeeTheme.LeafTexture };
            renderer.sortingOrder = 8;
        }

        public void OnZombieKilled(Zombie zombie)
        {
            if (UnityEngine.Random.value < 0.4f)
            {
                var type = UnityEngine.Random.value < 0.25f ? PickupType.Material : PickupType.Coin;
                SpawnPickupAt(zombie.transform.position.x, zombie.transform.position.y, type);
            }
        }

        public void OnPickupCollected(Pickup pickup)
        {
            pickups.Remove(pickup);
        }
    }
}
