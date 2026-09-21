using System;
using UnityEngine;

namespace Platformer.Survival
{
    /// <summary>
    /// The little companion drone bought in the shop: it hovers just behind and above the
    /// character and fires on its own at whatever is closest.
    ///
    /// It is deliberately a helper, not a second player. Its shot always does exactly 1
    /// damage, never scaled by the Fire Power upgrade, and even fully upgraded it fires
    /// about four times slower than the player's own gun. So it chips in, finishes off a
    /// wounded walker, and covers the player while they are busy jumping - but a run can
    /// never be won by standing still and letting it work.
    ///
    /// The drone itself only knows how to fly, aim and keep time. What counts as a target
    /// and what a shot actually is are supplied by whichever mode built it (see FindTarget
    /// and Fire), which is why the same component serves the runner, the Barricade and the
    /// Invasion without knowing anything about any of them.
    /// </summary>
    public class Drone : MonoBehaviour
    {
        /// <summary>Who to escort. Null (or destroyed) parks the drone where it is.</summary>
        public Transform follow;
        /// <summary>Resting position relative to the escorted transform, before mirroring.</summary>
        public Vector3 offset = new Vector3(-1.05f, 1.55f, 0f);
        /// <summary>Set false for modes where the character does not turn around (Barricade, Invasion).</summary>
        public bool mirrorWithTarget = true;

        public float fireInterval = 2.6f;
        public float range = 6.5f;

        /// <summary>Given the drone's position, the world point worth shooting at, or null.</summary>
        public Func<Vector2, Vector2?> FindTarget;
        /// <summary>Fires one shot from the drone toward the target.</summary>
        public Action<Vector2, Vector2> Fire;

        const float Smoothing = 7f;

        Transform hull, eye, rotor;
        SpriteRenderer eyeSr;
        SpriteRenderer followSr;
        float cooldown;
        float bobPhase;
        float recoil;

        /// <summary>Fire interval and reach for a given shop level (1..3). Level 0 has no drone.</summary>
        public static float IntervalForLevel(int level) => level >= 3 ? 1.7f : level == 2 ? 2.2f : 2.8f;
        public static float RangeForLevel(int level) => 6f + Mathf.Clamp(level, 1, 3) * 0.6f;

        public static Drone Create(Transform parent, Transform follow, int level)
        {
            var go = new GameObject("CompanionDrone");
            go.transform.SetParent(parent, false);

            var drone = go.AddComponent<Drone>();
            drone.follow = follow;
            drone.fireInterval = IntervalForLevel(level);
            drone.range = RangeForLevel(level);
            drone.followSr = follow != null ? follow.GetComponent<SpriteRenderer>() : null;
            drone.bobPhase = UnityEngine.Random.Range(0f, 6.28f);
            drone.Build(level);

            if (follow != null) go.transform.position = follow.position + drone.offset;
            return drone;
        }

        void Build(int level)
        {
            var brass = new Color(0.74f, 0.56f, 0.30f);
            var dark = new Color(0.26f, 0.13f, 0.10f);

            hull = NewPart("Hull", transform, PlaceholderVisuals.RimCircle(brass), brass, new Vector3(0.46f, 0.38f, 1f), Vector3.zero, 6);

            // A stubby cowl under the hull so it reads as a machine, not a floating coin.
            NewPart("Cowl", hull, PlaceholderVisuals.Square(dark), dark, new Vector3(0.62f, 0.18f, 1f), new Vector3(0f, -0.42f, 0f), 5);

            // Spinning rotor bar on top.
            rotor = NewPart("Rotor", hull, PlaceholderVisuals.Square(dark), dark, new Vector3(1.5f, 0.1f, 1f), new Vector3(0f, 0.52f, 0f), 7);
            NewPart("Mast", hull, PlaceholderVisuals.Square(dark), dark, new Vector3(0.12f, 0.35f, 1f), new Vector3(0f, 0.3f, 0f), 5);

            // The eye, on the side the drone is facing; brighter the better the drone is.
            var glow = Color.Lerp(ApogeeTheme.Gold, new Color(1f, 0.45f, 0.25f), 0.35f);
            eye = NewPart("Eye", hull, PlaceholderVisuals.Circle(glow), glow, new Vector3(0.4f, 0.48f, 1f), new Vector3(0.28f, 0f, 0f), 8);
            eyeSr = eye.GetComponent<SpriteRenderer>();

            // One small fin per upgrade level, so the drone visibly grows with the money spent.
            for (int i = 0; i < Mathf.Clamp(level, 1, 3); i++)
            {
                float y = -0.1f - i * 0.22f;
                NewPart($"Fin_{i}", hull, PlaceholderVisuals.Square(brass), brass, new Vector3(0.28f, 0.1f, 1f), new Vector3(-0.42f, y, 0f), 5);
            }
        }

        static Transform NewPart(string name, Transform parent, Sprite sprite, Color color, Vector3 scale, Vector3 localPos, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = order;
            return go.transform;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            bobPhase += dt * 2.7f;
            recoil = Mathf.MoveTowards(recoil, 0f, dt * 3.5f);

            float facing = mirrorWithTarget && followSr != null && followSr.flipX ? -1f : 1f;
            if (follow != null)
            {
                var want = follow.position + new Vector3(offset.x * facing, offset.y, offset.z);
                want.y += Mathf.Sin(bobPhase) * 0.13f;
                want.x -= recoil * 0.35f * facing;
                // Frame-rate independent smoothing: the drone always takes the same time to
                // catch up whether the phone renders at 30 or 120.
                transform.position = Vector3.Lerp(transform.position, want, 1f - Mathf.Exp(-Smoothing * dt));
            }

            if (hull != null)
            {
                var s = hull.localScale;
                hull.localScale = new Vector3(Mathf.Abs(s.x) * facing, s.y, s.z);
                hull.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(bobPhase * 0.8f) * 5f - recoil * 14f);
            }
            if (rotor != null) rotor.localRotation = Quaternion.Euler(0f, 0f, Time.time * 900f);
            if (eyeSr != null)
            {
                float pulse = 0.75f + Mathf.Abs(Mathf.Sin(bobPhase * 1.4f)) * 0.25f;
                var c = eyeSr.color;
                eyeSr.color = new Color(c.r, c.g, c.b, pulse);
            }

            if (FindTarget == null || Fire == null) return;
            cooldown -= dt;
            if (cooldown > 0f) return;

            Vector2 from = transform.position;
            var target = FindTarget(from);
            if (!target.HasValue) return;
            if ((target.Value - from).sqrMagnitude > range * range) return;

            Fire(from, target.Value);
            cooldown = fireInterval;
            recoil = 1f;
            Fx.Burst(transform.position, ApogeeTheme.Gold, 3, 1.4f, 0.05f, 0f);
        }
    }

    /// <summary>
    /// The drone's shot in the runner and the campaign: a small bolt that flies straight
    /// and takes one point off the first living zombie it touches. It deliberately ignores
    /// the cracked walls sealing a secret - a drone opening those by accident would give
    /// away a passage the player is meant to find themselves.
    /// </summary>
    public class DroneBolt : MonoBehaviour
    {
        const float Speed = 15f;
        const float Lifetime = 1.4f;

        Vector2 direction;
        float spawnTime;

        public static void Launch(Transform parent, Vector2 origin, Vector2 target)
        {
            var go = new GameObject("DroneBolt");
            go.transform.SetParent(parent, false);
            go.transform.position = origin;
            go.transform.localScale = Vector3.one * 0.18f;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = PlaceholderVisuals.Circle(ApogeeTheme.Gold);
            sr.sortingOrder = 5;

            var col = go.AddComponent<CircleCollider2D>();
            col.isTrigger = true;
            col.radius = 0.5f;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;

            var bolt = go.AddComponent<DroneBolt>();
            var delta = target - origin;
            bolt.direction = delta.sqrMagnitude > 0.0001f ? delta.normalized : Vector2.right;
            bolt.spawnTime = Time.time;
        }

        void Update()
        {
            transform.position += (Vector3)(direction * (Speed * Time.deltaTime));
            if (Time.time - spawnTime > Lifetime) Destroy(gameObject);
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            var zombie = other.GetComponent<Zombie>();
            if (zombie == null || !zombie.IsAlive) return;
            Fx.Burst(transform.position, ApogeeTheme.Gold, 4, 2f, 0.06f, 0.2f);
            zombie.TakeDamage(1);
            Destroy(gameObject);
        }
    }
}
