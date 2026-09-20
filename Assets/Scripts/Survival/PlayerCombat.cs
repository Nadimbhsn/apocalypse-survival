using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Platformer.Mechanics;

namespace Platformer.Survival
{
    /// <summary>
    /// The player's gun. Fires while the FIRE button (or F / left Ctrl / gamepad West /
    /// right trigger) is held, at a fixed rate: aim-assisted toward the nearest living
    /// zombie in range, otherwise straight ahead in the facing direction. Projectiles are
    /// pooled. Damage scales with the FirePower upgrade.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class PlayerCombat : MonoBehaviour
    {
        public float range = 7f;
        public float fireRate = 3f;
        public int baseDamage = 1;
        public float projectileSpeed = 14f;

        readonly List<Projectile> pool = new();
        Transform poolParent;
        PlayerController player;
        SpriteRenderer spriteRenderer;
        float fireCooldown;

        int Damage => Mathf.Max(1, Mathf.RoundToInt(baseDamage * UpgradeManager.FirePowerMultiplier));

        void Awake()
        {
            poolParent = new GameObject("ProjectilePool").transform;
            player = GetComponent<PlayerController>();
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        void Update()
        {
            fireCooldown -= Time.deltaTime;
            if (fireCooldown > 0f) return;
            if (!player.controlEnabled || !WantsFire()) return;
            var director = SurvivalDirector.Instance;
            if (director != null && !director.IsRunning) return;

            Vector2 origin = (Vector2)transform.position + Vector2.up * 0.1f;
            var target = FindNearestZombie();
            Vector2 dir = target != null
                ? (Vector2)target.transform.position - origin
                : (spriteRenderer != null && spriteRenderer.flipX ? Vector2.left : Vector2.right);

            Fire(origin, dir);
            fireCooldown = 1f / fireRate;
        }

        static bool WantsFire()
        {
            if (MobileInput.FireHeld) return true;
            var kb = Keyboard.current;
            if (kb != null && (kb.fKey.isPressed || kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed)) return true;
            var pad = Gamepad.current;
            return pad != null && (pad.buttonWest.isPressed || pad.rightTrigger.isPressed);
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

        void Fire(Vector2 origin, Vector2 dir)
        {
            var projectile = GetPooledProjectile();
            projectile.speed = projectileSpeed;
            projectile.Launch(origin, dir, Damage);
            Sfx.Shoot();
            Fx.Burst(origin + dir.normalized * 0.45f, PlaceholderVisuals.ProjectileColor, 3, 1.5f, 0.05f, 0f);
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
