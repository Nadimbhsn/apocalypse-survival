using System;
using System.Collections.Generic;
using UnityEngine;
using Platformer.Mechanics;
using Platformer.Gameplay;

namespace Platformer.Survival
{
    /// <summary>
    /// Drives the endless survival loop: procedural ground generation/recycling (plus
    /// decorative ruins and bonus platforms), timed zombie/pickup spawning with a
    /// difficulty ramp, and the run start/end lifecycle. Entities are plain
    /// destroy-on-recycle (not pooled) since spawn rates here are low (roughly one every
    /// few seconds); only Projectile (see PlayerCombat) is hot enough to need real pooling.
    /// </summary>
    public class SurvivalDirector : MonoBehaviour
    {
        public static SurvivalDirector Instance { get; private set; }

        PlayerController player;
        RuntimeUI ui;

        [Header("Ground generation")]
        public float genAheadDistance = 25f;
        public float despawnBehindDistance = 22f;
        public float minSegmentWidth = 4f;
        public float maxSegmentWidth = 9f;
        public float groundThickness = 1f;

        [Header("Spawning")]
        public float zombieBaseInterval = 3.2f;
        public float pickupBaseInterval = 2.2f;
        public float minAheadSpawn = 8f;
        public float maxAheadSpawn = 16f;

        struct GroundSegment
        {
            public float xStart, xEnd, topY;
            public bool isGap;
            public GameObject go;
        }

        readonly List<GroundSegment> segments = new();
        readonly List<Zombie> zombies = new();
        readonly List<Pickup> pickups = new();
        readonly List<GameObject> bonusPlatforms = new();
        readonly List<GameObject> ruins = new();

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

        int introIndex;
        float frontierX;
        float lastTopY;
        bool lastSegmentWasGap;
        float decorFrontierX;
        float zombieTimer, pickupTimer;
        float runStartX;
        float baseMaxSpeed;
        int baseMaxHP;
        bool running;

        GameObject fallDeathZone;
        ParticleSystem ashDrift;
        Transform entityParent;

        public bool IsRunning => running;
        public PlayerController Player => player;
        /// <summary>Distance travelled this run, in meters (1 world unit = 1 meter) - the game's scoring metric.</summary>
        public float Distance => player != null ? Mathf.Max(0f, player.transform.position.x - runStartX) : 0f;
        float Difficulty => 1f + Distance / 40f;

        void Awake()
        {
            Instance = this;
            entityParent = new GameObject("SurvivalEntities").transform;
        }

        public void Configure(PlayerController playerController, RuntimeUI runtimeUi)
        {
            player = playerController;
            ui = runtimeUi;
            baseMaxSpeed = player.maxSpeed;
            baseMaxHP = player.health != null ? player.health.maxHP : 5;

            PlayerDeath.OnExecute += HandlePlayerDeath;
            CreateFallDeathZone();
            CreateAshDrift();

            // The sample's hand-painted level is disabled at bootstrap (see GameBootstrap),
            // but the fixed intro ground (IntroGround_A..E in SampleScene.unity) always
            // exists and is never touched, so the player already has real ground under
            // them here without needing to generate anything. Player stays frozen
            // (controlEnabled = false) until StartRun().
            frontierX = 0f;
            lastTopY = 0f;
            decorFrontierX = 0f;
            runStartX = 1f;
            PlacePlayerAtStart();
            SkinCatalog.ApplyToPlayer(player);
            player.controlEnabled = false;
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
            // Freezes Time.time, which also stops the sample's own scheduled PlayerSpawn
            // (Simulation events compare against Time.time) so it can't silently teleport
            // the player back to the old hand-painted spawn point while Game Over is up.
            Time.timeScale = 0f;
            ui.ShowGameOver(Distance);
        }

        public void StartRun()
        {
            Time.timeScale = 1f;
            ClearAll();

            UpgradeManager.ApplyToPlayer(player, baseMaxSpeed, baseMaxHP);

            frontierX = 0f;
            lastTopY = 0f;
            lastSegmentWasGap = false;
            decorFrontierX = 0f;
            introIndex = 0;
            runStartX = 1f;
            zombieTimer = zombieBaseInterval;
            pickupTimer = pickupBaseInterval;
            running = true;

            PlacePlayerAtStart();
            // Builds the fixed intro (see IntroLayout) up to genAheadDistance synchronously,
            // so the ground the player is standing on already exists before the first frame
            // renders; the rest fills in incrementally via Update() as they advance, exactly
            // like the procedural section past the intro.
            GenerateGroundAhead();
            SkinCatalog.ApplyToPlayer(player);
            player.controlEnabled = true;
        }

        void ClearAll()
        {
            foreach (var s in segments) if (s.go != null) Destroy(s.go);
            segments.Clear();
            foreach (var z in zombies) if (z != null) Destroy(z.gameObject);
            zombies.Clear();
            foreach (var p in pickups) if (p != null) Destroy(p.gameObject);
            pickups.Clear();
            foreach (var b in bonusPlatforms) if (b != null) Destroy(b);
            bonusPlatforms.Clear();
            foreach (var r in ruins) if (r != null) Destroy(r);
            ruins.Clear();
        }

        void Update()
        {
            if (!running || player == null) return;

            ui.UpdateHud(player.health, Distance, SaveSystem.Coins, SaveSystem.Materials);

            GenerateGroundAhead();
            GenerateBackgroundDecor();
            RecycleBehind();
            // Random spawns only kick in past the fixed, always-identical intro stretch.
            if (introIndex >= IntroLayout.Length) UpdateSpawnTimers();

            if (fallDeathZone != null)
                fallDeathZone.transform.position = new Vector3(player.transform.position.x, -25f, 0f);

            if (ashDrift != null)
                ashDrift.transform.position = new Vector3(player.transform.position.x, player.transform.position.y + 4f, 0f);
        }

        void GenerateGroundAhead()
        {
            while (frontierX < player.transform.position.x + genAheadDistance)
            {
                if (introIndex < IntroLayout.Length)
                {
                    GenerateIntroSegment();
                    continue;
                }

                float roll = UnityEngine.Random.value;

                // A gap can never immediately follow another gap (that would silently chain
                // their widths into a jump distance far beyond the player's jump arc), and
                // the segment right after a gap always lands at the same height as takeoff
                // (no combined horizontal+vertical jump) so every gap stays cleanly jumpable.
                if (!lastSegmentWasGap && roll < 0.12f && frontierX > 8f)
                {
                    float gapWidth = UnityEngine.Random.Range(1.6f, 2.6f);
                    segments.Add(new GroundSegment { xStart = frontierX, xEnd = frontierX + gapWidth, topY = lastTopY, isGap = true, go = null });
                    frontierX += gapWidth;
                    lastSegmentWasGap = true;
                    continue;
                }

                float width = UnityEngine.Random.Range(minSegmentWidth, maxSegmentWidth);
                float topY = lastTopY;
                if (!lastSegmentWasGap && roll > 0.75f)
                    topY = Mathf.Clamp(lastTopY + UnityEngine.Random.Range(-2f, 2f), -3f, 5f);

                GenerateSegment(frontierX, width, topY);
                lastTopY = topY;
                lastSegmentWasGap = false;
            }
        }

        /// <summary>Places the next fixed IntroLayout entry, plus any curated prop tied to that index.</summary>
        void GenerateIntroSegment()
        {
            int index = introIndex;
            var (width, heightDelta, isGap) = IntroLayout[index];
            introIndex++;

            if (isGap)
            {
                segments.Add(new GroundSegment { xStart = frontierX, xEnd = frontierX + width, topY = lastTopY, isGap = true, go = null });
                frontierX += width;
                lastSegmentWasGap = true;
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

            if (index == 1) SpawnPickupAt(segStart + 3f, topY + 0.6f, false);
            if (index == 4) SpawnPickupAt(segStart + 3f, topY + 0.6f, true);
            if (index == 6) SpawnZombie(segStart + 4f, ZombieKind.Walker);
        }

        void GenerateSegment(float xStart, float width, float topY, bool allowBonusPlatform = true)
        {
            var go = new GameObject($"Ground_{xStart:0}");
            go.transform.SetParent(entityParent, false);
            go.transform.position = new Vector3(xStart + width / 2f, topY - groundThickness / 2f, 0f);
            go.transform.localScale = new Vector3(width, groundThickness, 1f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = PlaceholderVisuals.Square(PlaceholderVisuals.GroundColor);
            sr.sortingOrder = -1;
            float shade = 1f + UnityEngine.Random.Range(-0.12f, 0.12f);
            sr.color = new Color(shade, shade, shade);

            go.AddComponent<BoxCollider2D>();

            segments.Add(new GroundSegment { xStart = xStart, xEnd = xStart + width, topY = topY, isGap = false, go = go });
            frontierX = xStart + width;

            if (allowBonusPlatform && width > 5f) MaybeSpawnBonusPlatform(xStart, width, topY);
        }

        /// <summary>
        /// A small floating platform above the main path with a pickup on it - purely
        /// optional, reachable with a jump, never blocking the ground route below it.
        /// </summary>
        void MaybeSpawnBonusPlatform(float segStart, float segWidth, float topY)
        {
            if (UnityEngine.Random.value > 0.18f) return;

            float platformWidth = 2.2f;
            float platformX = segStart + UnityEngine.Random.Range(1f, Mathf.Max(1.5f, segWidth - platformWidth - 1f));
            float platformY = topY + UnityEngine.Random.Range(2.2f, 3.2f);

            var go = new GameObject($"BonusPlatform_{platformX:0}");
            go.transform.SetParent(entityParent, false);
            go.transform.position = new Vector3(platformX + platformWidth / 2f, platformY, 0f);
            go.transform.localScale = new Vector3(platformWidth, 0.35f, 1f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = PlaceholderVisuals.Square(PlaceholderVisuals.GroundColor);
            sr.sortingOrder = -1;
            sr.color = new Color(1.15f, 1.1f, 1.05f);

            go.AddComponent<BoxCollider2D>();
            bonusPlatforms.Add(go);

            SpawnPickupAt(platformX + platformWidth / 2f, platformY + 0.5f, UnityEngine.Random.value < 0.35f);
        }

        /// <summary>
        /// Sparse silhouette ruins/rubble behind the gameplay, purely decorative (no
        /// collider), reinforcing the ruined-city apocalypse setting and making the
        /// procedurally generated map read as richer than a single flat strip of ground.
        /// </summary>
        void GenerateBackgroundDecor()
        {
            while (decorFrontierX < player.transform.position.x + genAheadDistance + 15f)
            {
                float x = decorFrontierX + UnityEngine.Random.Range(10f, 22f);
                float height = UnityEngine.Random.Range(3f, 9f);
                float width = UnityEngine.Random.Range(2f, 5f);
                float groundY = GetGroundHeightAt(x);

                var go = new GameObject($"Ruin_{x:0}");
                go.transform.SetParent(entityParent, false);
                go.transform.position = new Vector3(x, groundY + height / 2f - 0.5f, 0f);
                go.transform.localScale = new Vector3(width, height, 1f);

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = PlaceholderVisuals.Square(PlaceholderVisuals.RuinColor);
                sr.sortingOrder = -3;
                float shade = 1f + UnityEngine.Random.Range(-0.15f, 0.1f);
                sr.color = new Color(shade, shade, shade, 0.9f);

                ruins.Add(go);
                decorFrontierX = x;
            }
        }

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
            for (int i = bonusPlatforms.Count - 1; i >= 0; i--)
            {
                if (bonusPlatforms[i] == null || bonusPlatforms[i].transform.position.x < cutoff)
                {
                    if (bonusPlatforms[i] != null) Destroy(bonusPlatforms[i]);
                    bonusPlatforms.RemoveAt(i);
                }
            }
            for (int i = ruins.Count - 1; i >= 0; i--)
            {
                if (ruins[i] == null || ruins[i].transform.position.x < cutoff)
                {
                    if (ruins[i] != null) Destroy(ruins[i]);
                    ruins.RemoveAt(i);
                }
            }
        }

        void UpdateSpawnTimers()
        {
            zombieTimer -= Time.deltaTime;
            if (zombieTimer <= 0f)
            {
                zombieTimer = Mathf.Max(0.6f, zombieBaseInterval / Difficulty);
                TrySpawnAhead(x => SpawnZombie(x));
            }

            pickupTimer -= Time.deltaTime;
            if (pickupTimer <= 0f)
            {
                pickupTimer = pickupBaseInterval;
                TrySpawnAhead(SpawnPickup);
            }
        }

        void TrySpawnAhead(Action<float> spawn)
        {
            float x = player.transform.position.x + UnityEngine.Random.Range(minAheadSpawn, maxAheadSpawn);
            if (x < IntroEndX) return; // never overwrite the fixed, always-identical intro
            if (x > frontierX) return; // ground not generated that far yet, try again next timer
            if (GetSegmentAt(x, out var seg) && !seg.isGap)
                spawn(x);
        }

        /// <summary>
        /// Chooses a zombie kind (Walker only for the first ~12m so the player isn't
        /// ambushed with variety before they've even moved, then a weighted mix) and tunes
        /// its stats/tint accordingly.
        /// </summary>
        void SpawnZombie(float x, ZombieKind? forcedKind = null)
        {
            ZombieKind kind = forcedKind ?? ZombieKind.Walker;
            if (forcedKind == null && Difficulty > 1.3f)
            {
                float roll = UnityEngine.Random.value;
                if (roll < 0.50f) kind = ZombieKind.Walker;
                else if (roll < 0.78f) kind = ZombieKind.Runner;
                else kind = ZombieKind.Spitter;
            }

            var go = new GameObject($"Zombie_{kind}");
            go.transform.SetParent(entityParent, false);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = PlaceholderVisuals.Zombie();
            sr.sortingOrder = 3;

            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;

            var health = go.AddComponent<Health>();
            var zombie = go.AddComponent<Zombie>();
            zombie.kind = kind;
            zombie.SetTarget(player.transform);

            switch (kind)
            {
                case ZombieKind.Runner:
                    go.transform.localScale = new Vector3(0.7f, 1.1f, 1f);
                    sr.color = new Color(1.3f, 1.15f, 0.6f);
                    health.maxHP = Mathf.Clamp(1 + Mathf.FloorToInt(Difficulty * 0.6f), 1, 4);
                    zombie.chaseSpeed = Mathf.Min(6.5f, 3.6f + Difficulty * 0.2f);
                    zombie.shuffleSpeed = 1f;
                    zombie.aggroRange = 10f;
                    zombie.contactCooldown = 0.7f;
                    break;

                case ZombieKind.Spitter:
                    go.transform.localScale = new Vector3(0.85f, 1.25f, 1f);
                    sr.color = new Color(0.7f, 1.25f, 0.65f);
                    health.maxHP = Mathf.Clamp(1 + Mathf.FloorToInt(Difficulty), 1, 6);
                    zombie.chaseSpeed = Mathf.Min(3f, 1.8f + Difficulty * 0.1f);
                    zombie.contactCooldown = 1.3f;
                    zombie.spitDamage = 1;
                    zombie.spitRange = 7.5f;
                    break;

                default: // Walker
                    go.transform.localScale = new Vector3(0.8f, 1.2f, 1f);
                    health.maxHP = Mathf.Clamp(1 + Mathf.FloorToInt(Difficulty), 1, 6);
                    zombie.chaseSpeed = Mathf.Min(4.5f, 2.2f + Difficulty * 0.15f);
                    break;
            }

            go.transform.position = new Vector3(x, GetGroundHeightAt(x) + 0.6f, 0f);
            zombies.Add(zombie);
        }

        void SpawnPickup(float x)
        {
            bool material = UnityEngine.Random.value < 0.3f;
            SpawnPickupAt(x, GetGroundHeightAt(x) + 0.6f, material);
        }

        void SpawnPickupAt(float x, float y, bool material)
        {
            var go = new GameObject(material ? "Material" : "Coin");
            go.transform.SetParent(entityParent, false);
            go.transform.localScale = Vector3.one * 0.5f;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = PlaceholderVisuals.Circle(material ? PlaceholderVisuals.MaterialColor : PlaceholderVisuals.CoinColor);
            sr.sortingOrder = 4;

            var col = go.AddComponent<CircleCollider2D>();
            col.isTrigger = true;

            var pickup = go.AddComponent<Pickup>();
            pickup.type = material ? PickupType.Material : PickupType.Coin;
            pickup.value = 1;

            go.transform.position = new Vector3(x, y, 0f);
            pickups.Add(pickup);
        }

        bool GetSegmentAt(float x, out GroundSegment segment)
        {
            for (int i = 0; i < segments.Count; i++)
            {
                if (x >= segments[i].xStart && x <= segments[i].xEnd)
                {
                    segment = segments[i];
                    return true;
                }
            }
            segment = default;
            return false;
        }

        /// <summary>
        /// Analytic ground height query used by non-physics entities (zombies, spawn
        /// placement) instead of raycasts, since SurvivalDirector already authoritatively
        /// knows the generated terrain. Falls back to the last known height over a gap.
        /// </summary>
        public float GetGroundHeightAt(float x)
        {
            if (GetSegmentAt(x, out var seg) && !seg.isGap) return seg.topY;
            return lastTopY;
        }

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
        /// Slow drifting ash/dust for the apocalypse atmosphere - recentered on the player
        /// each frame (see Update) since it emits in world space over a bounded box.
        /// </summary>
        void CreateAshDrift()
        {
            var go = new GameObject("AshDrift");
            go.transform.SetParent(entityParent, false);
            go.transform.position = new Vector3(player.transform.position.x, player.transform.position.y + 4f, 0f);

            ashDrift = go.AddComponent<ParticleSystem>();
            var main = ashDrift.main;
            main.loop = true;
            main.startLifetime = 7f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.1f);
            main.startColor = PlaceholderVisuals.AshColor;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 150;

            var emission = ashDrift.emission;
            emission.rateOverTime = 7f;

            var shape = ashDrift.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(28f, 14f, 0.1f);

            var vel = ashDrift.velocityOverLifetime;
            vel.enabled = true;
            vel.x = new ParticleSystem.MinMaxCurve(-0.4f, 0.15f);
            vel.y = new ParticleSystem.MinMaxCurve(-0.5f, -0.15f);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = new Material(Shader.Find("Sprites/Default"));
            renderer.sortingOrder = 8;
        }

        public void OnZombieKilled(Zombie zombie)
        {
            if (UnityEngine.Random.value < 0.4f)
                SpawnPickupAt(zombie.transform.position.x, zombie.transform.position.y, UnityEngine.Random.value < 0.25f);
        }

        public void OnPickupCollected(Pickup pickup)
        {
            pickups.Remove(pickup);
        }
    }
}
