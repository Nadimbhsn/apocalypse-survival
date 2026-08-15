using System.Collections.Generic;
using UnityEngine;
using Platformer.Mechanics;

namespace Platformer.Survival
{
    /// <summary>
    /// Auto-fire weapon attached to the player: targets the nearest living zombie in range
    /// and fires a pooled projectile at a fixed rate. Damage scales with the FirePower upgrade.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class PlayerCombat : MonoBehaviour
    {
        public float range = 6f;
        public float baseFireRate = 1.4f;
        public int baseDamage = 1;
        public float projectileSpeed = 12f;

        readonly List<Projectile> pool = new();
        Transform poolParent;
        float fireCooldown;

        int Damage => Mathf.Max(1, Mathf.RoundToInt(baseDamage * UpgradeManager.FirePowerMultiplier));

        void Awake()
        {
            poolParent = new GameObject("ProjectilePool").transform;
        }

        void Update()
        {
            fireCooldown -= Time.deltaTime;
            if (fireCooldown > 0f) return;

            var target = FindNearestZombie();
            if (target == null) return;

            Fire(target);
            fireCooldown = 1f / baseFireRate;
        }

        Zombie FindNearestZombie()
        {
            Zombie nearest = null;
            float best = range * range;
            foreach (var zombie in Zombie.Active)
            {
                if (!zombie.IsAlive) continue;
                float d = ((Vector2)zombie.transform.position - (Vector2)transform.position).sqrMagnitude;
                if (d <= best)
                {
                    best = d;
                    nearest = zombie;
                }
            }
            return nearest;
        }

        void Fire(Zombie target)
        {
            Vector2 origin = transform.position;
            Vector2 dir = (Vector2)target.transform.position - origin;
            var projectile = GetPooledProjectile();
            projectile.speed = projectileSpeed;
            projectile.Launch(origin, dir, Damage);
        }

        Projectile GetPooledProjectile()
        {
            foreach (var p in pool)
                if (!p.gameObject.activeSelf) return p;

            var go = new GameObject("Projectile");
            go.transform.SetParent(poolParent, false);
            go.transform.localScale = Vector3.one * 0.25f;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = PlaceholderVisuals.Circle(PlaceholderVisuals.ProjectileColor);
            sr.sortingOrder = 5;

            var col = go.AddComponent<CircleCollider2D>();
            col.isTrigger = true;
            col.radius = 0.5f;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;

            var projectile = go.AddComponent<Projectile>();
            pool.Add(projectile);
            return projectile;
        }
    }
}
