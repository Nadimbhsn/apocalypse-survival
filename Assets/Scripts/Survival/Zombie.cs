using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Platformer.Mechanics;

namespace Platformer.Survival
{
    public enum ZombieKind { Walker, Runner, Spitter }

    /// <summary>
    /// Ground-based zombie: drifts toward the player, charging once within aggro range,
    /// deals melee damage on contact, and can be killed by player projectiles. Stays
    /// snapped to the procedurally generated ground via SurvivalDirector rather than
    /// using physics, so it tracks terrain height without needing gravity/raycasts.
    /// The Spitter kind also lobs an occasional ranged shot - not a constant barrage,
    /// just a randomized cooldown so it reads as an unpredictable extra threat.
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

        public bool IsAlive => health != null && health.IsAlive;

        void Awake()
        {
            health = GetComponent<Health>();
        }

        void OnEnable() => Active.Add(this);
        void OnDisable() => Active.Remove(this);

        public void SetTarget(Transform target) => player = target;

        void Update()
        {
            if (!IsAlive || player == null) return;

            float toPlayerX = player.position.x - transform.position.x;
            float dist = Mathf.Abs(toPlayerX);
            float speed = dist <= aggroRange ? chaseSpeed : shuffleSpeed;
            float dir = dist > 0.05f ? Mathf.Sign(toPlayerX) : 0f;

            var pos = transform.position;
            pos.x += dir * speed * Time.deltaTime;
            if (SurvivalDirector.Instance != null)
                pos.y = SurvivalDirector.Instance.GetGroundHeightAt(pos.x) + groundOffset;
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
            if (!IsAlive || Time.time < nextContactTime) return;
            var controller = other.GetComponent<PlayerController>();
            if (controller == null || controller.health == null || !controller.health.IsAlive) return;

            nextContactTime = Time.time + contactCooldown;
            controller.health.Decrement(UpgradeManager.ReduceDamageToPlayer(contactDamage));
        }

        public void TakeDamage(int amount)
        {
            if (!IsAlive) return;
            health.Decrement(amount);
            if (!IsAlive)
            {
                SurvivalDirector.Instance?.OnZombieKilled(this);
                Die();
            }
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
