using System;
using UnityEngine;
using Platformer.Mechanics;

namespace Platformer.Survival
{
    /// <summary>
    /// Entity spawning half of SurvivalDirector: timed zombie/pickup spawns tuned by the
    /// zone the player is currently in, packs and hordes, the four zombie kinds' stats,
    /// pickups, and the terrain queries every non-physics entity relies on. Entities are
    /// plain destroy-on-recycle (not pooled) since spawn rates here are low (roughly one
    /// every few seconds); only Projectile (see PlayerCombat) is hot enough to need real
    /// pooling.
    /// </summary>
    public partial class SurvivalDirector
    {
        void UpdateSpawnTimers()
        {
            if (cadenceActive) return; // the rhythm section is pure platforming
            var zone = ZoneCatalog.Get(activeZone);

            zombieTimer -= Time.deltaTime;
            if (zombieTimer <= 0f)
            {
                // Spawns get faster with distance, but far more gently than the enemies' stats,
                // and never below a floor - the runner is about the course, not a crowd.
                zombieTimer = Mathf.Max(1.5f, zombieBaseInterval * zone.ZombieIntervalMult / (1f + Distance / 250f));
                if (AliveZombies() < MaxAliveZombies)
                    TrySpawnAhead(x => SpawnZombieGroup(x, zone));
            }

            pickupTimer -= Time.deltaTime;
            if (pickupTimer <= 0f)
            {
                pickupTimer = pickupBaseInterval;
                TrySpawnAhead(SpawnPickup);
            }

            // Hordes are paced by distance, not time, so they can't stack up while the
            // player stands still; the target only advances once one actually spawned.
            if (Distance >= nextHordeDistance && Difficulty > 1.6f && !ascentActive)
            {
                if (TrySpawnAhead(SpawnHorde))
                    nextHordeDistance = Distance + UnityEngine.Random.Range(110f, 160f);
            }
        }

        /// <summary>How many zombies may be alive at once (they also despawn behind the player).</summary>
        int MaxAliveZombies => 5 + Mathf.FloorToInt(Ramp * 4f);

        int AliveZombies()
        {
            int n = 0;
            for (int i = 0; i < zombies.Count; i++)
                if (zombies[i] != null && zombies[i].IsAlive) n++;
            return n;
        }

        /// <summary>Runs spawn at a random point ahead on solid, already-generated ground. Returns false if no valid spot was found this time.</summary>
        bool TrySpawnAhead(Action<float> spawn)
        {
            float x = player.transform.position.x + UnityEngine.Random.Range(minAheadSpawn, maxAheadSpawn) * ReachScale;
            if (x < IntroEndX) return false; // never overwrite the fixed, always-identical intro
            if (x > frontierX) return false; // ground not generated that far yet, try again next timer
            if (InCadenceSpan(x)) return false; // never drop a zombie into the rhythm section
            if (!GetSegmentAt(x, out var seg) || seg.isGap) return false;
            spawn(x);
            return true;
        }

        // ---- zombies -------------------------------------------------------------------

        void SpawnZombieGroup(float x, ZoneDef zone)
        {
            int count = 1;
            if (UnityEngine.Random.value < zone.PackChance * 0.7f)
                count = UnityEngine.Random.Range(2, Difficulty > 3f ? 4 : 3);
            count = Mathf.Min(count, Mathf.Max(1, MaxAliveZombies - AliveZombies()));
            SpawnPack(x, count, zone.AllowBrute);
        }

        /// <summary>Spawns up to count zombies in a tight line starting at x, skipping any spot that isn't solid ground.</summary>
        void SpawnPack(float x, int count, bool allowBrute)
        {
            for (int i = 0; i < count; i++)
            {
                float px = x + i * 1.1f;
                if (!IsWalkable(px)) break;
                SpawnZombie(px, null, allowBrute);
            }
        }

        /// <summary>A wall of 4-7 walkers/runners spread over several meters, announced on screen.</summary>
        void SpawnHorde(float x)
        {
            int count = Mathf.Min(4 + Mathf.FloorToInt(Ramp * 3f), Mathf.Max(2, MaxAliveZombies - AliveZombies()));
            int spawned = 0;
            for (int i = 0; i < count; i++)
            {
                float px = x + i * 1.3f;
                if (!IsWalkable(px)) break;
                var kind = UnityEngine.Random.value < 0.3f ? ZombieKind.Runner : ZombieKind.Walker;
                SpawnZombie(px, kind);
                spawned++;
            }
            if (spawned >= 3) ui.ShowBanner("HORDE !", "Cours ou combats", 2f);
        }

        /// <summary>
        /// Chooses a zombie kind (Walker only for the first ~12m so the player isn't
        /// ambushed with variety before they've even moved, then a weighted mix that
        /// unlocks Brutes past ~30 m in zones that allow them) and tunes its stats/tint.
        /// </summary>
        void SpawnZombie(float x, ZombieKind? forcedKind = null, bool allowBrute = false, bool rising = false)
        {
            ZombieKind kind = forcedKind ?? ZombieKind.Walker;
            bool cemetery = ZoneAt(x) == ZoneKind.Infested;
            if (cemetery)
            {
                // The cemetery only holds the dead: skeletons (walking or running) and ghosts.
                if (forcedKind == null) kind = UnityEngine.Random.value < 0.35f ? ZombieKind.Spitter : ZombieKind.Walker;
                else if (kind == ZombieKind.Brute) kind = ZombieKind.Walker;
            }
            else if (forcedKind == null && Difficulty > 1.3f)
            {
                float spitterWeight = activeZone == ZoneKind.Wasteland ? 0.32f : 0.18f;
                float bruteWeight = allowBrute && Difficulty > 1.8f ? 0.12f : 0f;
                float runnerWeight = 0.25f;
                float roll = UnityEngine.Random.value * (0.45f + runnerWeight + spitterWeight + bruteWeight);
                if (roll < 0.45f) kind = ZombieKind.Walker;
                else if (roll < 0.45f + runnerWeight) kind = ZombieKind.Runner;
                else if (roll < 0.45f + runnerWeight + spitterWeight) kind = ZombieKind.Spitter;
                else kind = ZombieKind.Brute;
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

                case ZombieKind.Brute:
                    go.transform.localScale = new Vector3(1.25f, 1.75f, 1f);
                    sr.color = new Color(1.5f, 0.95f, 1.1f);
                    health.maxHP = Mathf.Clamp(4 + Mathf.FloorToInt(Difficulty * 1.2f), 4, 14);
                    zombie.chaseSpeed = Mathf.Min(2.6f, 1.5f + Difficulty * 0.1f);
                    zombie.shuffleSpeed = 0.4f;
                    zombie.aggroRange = 9f;
                    zombie.contactDamage = 2;
                    zombie.contactCooldown = 1.2f;
                    break;

                default: // Walker
                    go.transform.localScale = new Vector3(0.8f, 1.2f, 1f);
                    health.maxHP = Mathf.Clamp(1 + Mathf.FloorToInt(Difficulty), 1, 6);
                    zombie.chaseSpeed = Mathf.Min(4.5f, 2.2f + Difficulty * 0.15f);
                    break;
            }

            // The zombie sprite is 1 unit tall with a centered pivot, so half its scaled
            // height keeps the feet exactly on the ground line.
            zombie.groundOffset = go.transform.localScale.y * 0.5f;
            if (KenneyProps.Available) GiveZombieBody(go, zombie, kind, col, cemetery);
            go.transform.position = new Vector3(x, GetGroundHeightAt(x) + zombie.groundOffset, 0f);
            if (rising) zombie.RiseFromGround(0.55f);
            zombies.Add(zombie);
        }

        /// <summary>
        /// Swaps the flat sprite for a Kenney 3D character: zombie or vampire walkers,
        /// skeleton runners, a floating ghost for the spitter and a giant gravedigger brute.
        /// The root gets a uniform scale (so the model isn't skewed and flipping x mirrors it
        /// to face left), and the trigger collider is sized to the body.
        /// </summary>
        void GiveZombieBody(GameObject go, Zombie zombie, ZombieKind kind, BoxCollider2D col, bool cemetery)
        {
            string model;
            float height, width;
            Color blood;
            switch (kind)
            {
                case ZombieKind.Walker when cemetery:
                    model = "character-skeleton"; height = 1.1f; width = 0.55f; blood = new Color(0.85f, 0.78f, 0.6f);
                    break;
                case ZombieKind.Runner:
                    model = "character-skeleton"; height = 1.1f; width = 0.55f; blood = new Color(0.85f, 0.78f, 0.6f);
                    break;
                case ZombieKind.Spitter:
                    model = "character-ghost"; height = 1.15f; width = 0.65f; blood = new Color(0.55f, 0.9f, 0.35f);
                    break;
                case ZombieKind.Brute:
                    model = "character-keeper"; height = 1.9f; width = 1.0f; blood = new Color(0.55f, 0.18f, 0.12f);
                    break;
                default:
                    bool vampire = UnityEngine.Random.value < 0.3f;
                    model = vampire ? "character-vampire" : "character-zombie";
                    height = 1.2f; width = 0.6f;
                    blood = vampire ? new Color(0.55f, 0.1f, 0.12f) : new Color(0.35f, 0.55f, 0.22f);
                    break;
            }

            var size = KenneyProps.Size(PropKit.Graveyard, model);
            // Facing +X, turned 55 degrees toward the camera; the root's x flip mirrors it left.
            const float pitch = -8f;
            var rig = KenneyProps.Spawn(PropKit.Graveyard, model, go.transform, new Vector3(0f, -height / 2f, 0f),
                height / Mathf.Max(0.01f, size.y), PropLayer.Character, -55f, pitch);
            if (rig == null) return;

            go.transform.localScale = Vector3.one;
            col.size = new Vector2(width, height * 0.95f);
            col.offset = Vector2.zero;
            zombie.groundOffset = height / 2f;

            var motion = rig.gameObject.AddComponent<ModelMotion>();
            motion.height = height;
            motion.pitch = pitch;
            motion.floating = kind == ZombieKind.Spitter;
            zombie.AttachModel(rig, motion, blood);
        }

        // ---- pickups -------------------------------------------------------------------

        void SpawnPickup(float x)
        {
            // Ammo is a quarter of what lies on the road: enough to keep a careful shooter
            // going, not enough to hold the trigger down.
            float roll = UnityEngine.Random.value;
            var type = roll < 0.07f ? PickupType.Medkit
                : roll < 0.32f ? PickupType.Ammo
                : roll < 0.52f ? PickupType.Material
                : PickupType.Coin;
            SpawnPickupAt(x, GetGroundHeightAt(x) + 0.6f, type);
        }

        void SpawnPickupAt(float x, float y, PickupType type)
        {
            var go = new GameObject(type.ToString());
            go.transform.SetParent(entityParent, false);
            go.transform.localScale = Vector3.one * (type == PickupType.Medkit ? 0.6f : type == PickupType.Ammo ? 0.62f : 0.5f);

            Color color = type switch
            {
                PickupType.Material => PlaceholderVisuals.MaterialColor,
                PickupType.Medkit => PlaceholderVisuals.MedkitColor,
                _ => PlaceholderVisuals.CoinColor,
            };

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = type == PickupType.Medkit ? PlaceholderVisuals.Square(color)
                : type == PickupType.Ammo ? GameIcons.Ammo
                : PlaceholderVisuals.RimCircle(color);
            if (type == PickupType.Ammo) sr.color = Color.white;
            sr.sortingOrder = 4;

            if (type == PickupType.Medkit)
            {
                // A small pale cross on the red box so it reads as a medkit at a glance.
                var cross = new GameObject("Cross");
                cross.transform.SetParent(go.transform, false);
                cross.transform.localScale = new Vector3(0.6f, 0.2f, 1f);
                var csr = cross.AddComponent<SpriteRenderer>();
                csr.sprite = PlaceholderVisuals.Square(Color.white);
                csr.color = new Color(0.95f, 0.92f, 0.88f);
                csr.sortingOrder = 5;
                var cross2 = new GameObject("Cross2");
                cross2.transform.SetParent(go.transform, false);
                cross2.transform.localScale = new Vector3(0.2f, 0.6f, 1f);
                var csr2 = cross2.AddComponent<SpriteRenderer>();
                csr2.sprite = PlaceholderVisuals.Square(Color.white);
                csr2.color = csr.color;
                csr2.sortingOrder = 5;
            }

            var col = go.AddComponent<CircleCollider2D>();
            col.isTrigger = true;

            var pickup = go.AddComponent<Pickup>();
            pickup.type = type;
            pickup.value = type == PickupType.Medkit ? 2 : 1;

            go.transform.position = new Vector3(x, y, 0f);
            pickups.Add(pickup);
        }

        // ---- terrain queries -----------------------------------------------------------

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
        /// True if there is solid, generated ground at x (false over gaps, pits and
        /// collapsed slabs). A gap no wider than leapableGapWidth also counts, for
        /// entities that can jump.
        /// </summary>
        public bool IsWalkable(float x, float leapableGapWidth = 0f)
        {
            if (!GetSegmentAt(x, out var seg)) return false;
            if (!seg.isGap) return true;
            return leapableGapWidth > 0f && seg.xEnd - seg.xStart <= leapableGapWidth;
        }

        /// <summary>
        /// Analytic ground height query used by non-physics entities (zombies, spawn
        /// placement) instead of raycasts, since SurvivalDirector already authoritatively
        /// knows the generated terrain. Over a gap it returns the take-off height recorded
        /// for that gap; beyond generated terrain, the frontier height.
        /// </summary>
        public float GetGroundHeightAt(float x)
        {
            if (GetSegmentAt(x, out var seg)) return seg.topY;
            return lastTopY;
        }

        /// <summary>Lowest solid ground within 10 m either side of x - what "falling too far" is measured from.</summary>
        float LocalGroundLevel(float x)
        {
            float min = float.MaxValue;
            for (int i = 0; i < segments.Count; i++)
            {
                var seg = segments[i];
                if (seg.isGap || seg.xEnd < x - 10f || seg.xStart > x + 10f) continue;
                if (seg.topY < min) min = seg.topY;
            }
            return min == float.MaxValue ? lastTopY : min;
        }
    }
}
