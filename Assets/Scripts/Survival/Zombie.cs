using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Platformer.Mechanics;

namespace Platformer.Survival
{
    public enum ZombieKind { Walker, Runner, Spitter, Brute }

    /// <summary>
    /// Ground-based zombie: drifts toward the player, charging once within aggro range,
    /// deals melee damage on contact, and can be killed by player projectiles. Stays
    /// snapped to the procedurally generated ground via SurvivalDirector rather than
    /// using physics, so it tracks terrain height without needing gravity/raycasts.
    /// The Spitter kind also lobs an occasional ranged shot - not a constant barrage,
    /// just a randomized cooldown so it reads as an unpredictable extra threat. The
    /// Brute is a slow, huge tank that hits for double damage. Only Runners leap over
    /// gaps; every other kind stops at the edge, so jumping a gap is a real way to
    /// shake off a pack.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class Zombie : MonoBehaviour
    {
        public static readonly HashSet<Zombie> Active = new();

        public ZombieKind kind = ZombieKind.Walker;
        public float aggroRange = 8f;
        public float chaseSpeed = 2.2f;
        public float shuffleSpeed = 0.5f;
        public int contactDamage = 1;
        public float contactCooldown = 1f;
        public float groundOffset = 0.5f;

        [Header("Spitter only")]
        public float spitRange = 7f;
        public float spitMinRange = 1.8f;
        public int spitDamage = 1;
        public float spitProjectileSpeed = 8f;
        public float spitCooldownMin = 2.5f;
        public float spitCooldownMax = 5f;

        Health health;
        Transform player;
        float nextContactTime;
        float nextSpitTime;

        // Optional Kenney 3D body (see AttachModel); the SpriteRenderer is hidden when present.
        ModelMotion motion;
        Renderer[] modelRenderers;
        Color bloodColor = PlaceholderVisuals.ZombieColor;
        Coroutine flashRoutine;

        bool IsRising => motion != null && motion.IsRising;

        public bool IsAlive => health != null && health.IsAlive;

        void Awake()
        {
            health = GetComponent<Health>();
        }

        void OnEnable() => Active.Add(this);
        void OnDisable() => Active.Remove(this);

        public void SetTarget(Transform target) => player = target;

        /// <summary>Gives this zombie a 3D body; blood is the particle color when it gets shot.</summary>
        public void AttachModel(Transform rig, ModelMotion modelMotion, Color blood)
        {
            motion = modelMotion;
            modelRenderers = rig.GetComponentsInChildren<Renderer>();
            bloodColor = blood;
            var sr = GetComponent<SpriteRenderer>();
            if (sr != null) sr.enabled = false;
        }

        /// <summary>Bursts out of the ground: frozen and harmless until the rise animation ends.</summary>
        public void RiseFromGround(float duration)
        {
            if (motion != null) motion.Rise(duration);
        }

        void Update()
        {
            if (!IsAlive || player == null) return;
            if (IsRising)
            {
                var d = SurvivalDirector.Instance;
                if (d != null)
                {
                    var p = transform.position;
                    p.y = d.GetGroundHeightAt(p.x) + groundOffset;
                    transform.position = p;
                }
                // Face the player while climbing out.
                float face = Mathf.Sign(player.position.x - transform.position.x);
                var sc = transform.localScale;
                sc.x = Mathf.Abs(sc.x) * (face == 0f ? 1f : face);
                transform.localScale = sc;
                return;
            }

            float toPlayerX = player.position.x - transform.position.x;
            float dist = Mathf.Abs(toPlayerX);
            float speed = dist <= aggroRange ? chaseSpeed : shuffleSpeed;
            float dir = dist > 0.05f ? Mathf.Sign(toPlayerX) : 0f;

            var pos = transform.position;
            float nextX = pos.x + dir * speed * Time.deltaTime;
            var director = SurvivalDirector.Instance;
            if (director != null)
            {
                // Probe a little ahead of the sprite's center so the body stops at the ledge
                // rather than hanging halfway over it.
                float probeX = nextX + dir * 0.3f;
                // Runners leap ordinary gaps (up to about a player's jump); nothing crosses
                // a pit, so the Ascent column can't be followed.
                float leapable = kind == ZombieKind.Runner ? 3.2f : 0f;
                bool cliff = director.GetGroundHeightAt(pos.x) - director.GetGroundHeightAt(probeX) > 3.6f;
                if (director.IsWalkable(probeX, leapable) && !cliff)
                    pos.x = nextX;
                pos.y = director.GetGroundHeightAt(pos.x) + groundOffset;
            }
            else
            {
                pos.x = nextX;
            }
            transform.position = pos;

            if (dir != 0f)
            {
                var scale = transform.localScale;
                scale.x = Mathf.Abs(scale.x) * dir;
                transform.localScale = scale;
            }

            if (kind == ZombieKind.Spitter)
                UpdateSpitter(dist);
        }

        void UpdateSpitter(float distanceToPlayer)
        {
            if (Time.time < nextSpitTime) return;
            if (distanceToPlayer > spitRange || distanceToPlayer < spitMinRange) return;

            nextSpitTime = Time.time + Random.Range(spitCooldownMin, spitCooldownMax);
            FireSpit();
        }

        void FireSpit()
        {
            var go = new GameObject("ZombieSpit");
            go.transform.position = transform.position + Vector3.up * 0.4f;
            go.transform.localScale = Vector3.one * 0.35f;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = PlaceholderVisuals.Circle(PlaceholderVisuals.SpitColor);
            sr.sortingOrder = 3;

            var col = go.AddComponent<CircleCollider2D>();
            col.isTrigger = true;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;

            var proj = go.AddComponent<EnemyProjectile>();
            Vector2 dir = new Vector2(Mathf.Sign(player.position.x - transform.position.x), 0f);
            proj.speed = spitProjectileSpeed;
            proj.Launch(go.transform.position, dir, spitDamage);
        }

        void OnCollisionStay2D(Collision2D collision) => TryDamagePlayer(collision.collider);
        void OnTriggerStay2D(Collider2D other) => TryDamagePlayer(other);

        void TryDamagePlayer(Collider2D other)
        {
            if (!IsAlive || IsRising || Time.time < nextContactTime) return;
            var controller = other.GetComponent<PlayerController>();
            if (controller == null || controller.health == null || !controller.health.IsAlive) return;

            nextContactTime = Time.time + contactCooldown;
            controller.health.Decrement(UpgradeManager.ReduceDamageToPlayer(contactDamage));
        }

        public void TakeDamage(int amount)
        {
            if (!IsAlive) return;
            health.Decrement(amount);
            var sr = GetComponent<SpriteRenderer>();
            var tint = motion != null ? bloodColor
                : sr != null ? sr.color * PlaceholderVisuals.ZombieColor : PlaceholderVisuals.ZombieColor;
            tint.a = 1f;
            if (modelRenderers != null)
            {
                if (flashRoutine != null) StopCoroutine(flashRoutine);
                flashRoutine = StartCoroutine(HitFlash());
            }
            if (!IsAlive)
            {
                Fx.Burst(transform.position, tint, kind == ZombieKind.Brute ? 26 : 14, 3.5f, 0.12f);
                Fx.Text(transform.position + Vector3.up * 0.6f, "KILL", new Color(0.95f, 0.35f, 0.25f), 0.9f);
                Sfx.Kill();
                SurvivalDirector.Instance?.OnZombieKilled(this);
                Die();
            }
            else
            {
                Fx.Burst(transform.position, tint, 4, 2f, 0.07f);
            }
        }

        IEnumerator HitFlash()
        {
            const float duration = 0.12f;
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                KenneyProps.SetFlash(modelRenderers, 0.85f * (1f - t / duration));
                yield return null;
            }
            KenneyProps.SetFlash(modelRenderers, 0f);
            flashRoutine = null;
        }

        void Die()
        {
            var col = GetComponent<Collider2D>();
            if (col != null) col.enabled = false;
            StartCoroutine(DeathFade());
            enabled = false; // also removes this zombie from Active via OnDisable
        }

        IEnumerator DeathFade()
        {
            const float duration = 0.25f;
            var sr = GetComponent<SpriteRenderer>();
            var startScale = transform.localScale;
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float p = t / duration;
                transform.localScale = Vector3.Lerp(startScale, Vector3.zero, p);
                if (sr != null)
                {
                    var c = sr.color;
                    c.a = 1f - p;
                    sr.color = c;
                }
                yield return null;
            }
            Destroy(gameObject);
        }
    }
}
