using System.Collections.Generic;
using UnityEngine;

namespace Platformer.Survival
{
    /// <summary>
    /// Pooled straight-line projectile fired by PlayerCombat. It flies the weapon's range
    /// and no further, measured in distance rather than time so a fast gun does not outrange
    /// a slow one by accident. It damages the first living zombie it touches, or opens a
    /// cracked secret wall. A grenade also explodes - on impact, or at the end of its range
    /// - hurting every zombie within its blast radius.
    /// </summary>
    public class Projectile : MonoBehaviour
    {
        public float speed = 12f;

        Vector2 direction;
        Vector2 origin;
        float maxDistance;
        int damage;
        float explosionRadius;
        int explosionDamage;
        SpriteRenderer sr;

        static readonly List<Zombie> blastBuffer = new();

        /// <summary>Size and colour per weapon: a pistol pellet, a revolver slug, a grenade.</summary>
        public void Style(float size, Color color)
        {
            if (sr == null) sr = GetComponent<SpriteRenderer>();
            transform.localScale = Vector3.one * size;
            if (sr != null) sr.color = color;
        }

        public void Launch(Vector2 from, Vector2 dir, int dmg, float range, float blastRadius = 0f, int blastDamage = 0)
        {
            transform.position = from;
            origin = from;
            direction = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.right;
            damage = dmg;
            maxDistance = Mathf.Max(0.5f, range);
            explosionRadius = blastRadius;
            explosionDamage = blastDamage;
            gameObject.SetActive(true);
        }

        void Update()
        {
            transform.position += (Vector3)(direction * (speed * Time.deltaTime));
            if (((Vector2)transform.position - origin).sqrMagnitude < maxDistance * maxDistance) return;
            // Out of range: a bullet simply stops, a grenade goes off where it is.
            if (explosionRadius > 0f) Explode();
            gameObject.SetActive(false);
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            var zombie = other.GetComponent<Zombie>();
            if (zombie != null && zombie.IsAlive)
            {
                Fx.Burst(transform.position, PlaceholderVisuals.ProjectileColor, 4, 2f, 0.06f, 0.2f);
                zombie.TakeDamage(damage);
                if (explosionRadius > 0f) Explode();
                gameObject.SetActive(false);
                return;
            }

            // Cracked slabs sealing a campaign secret are the other thing the gun opens.
            var wall = other.GetComponent<BreakableWall>();
            if (wall != null)
            {
                wall.Hit(damage);
                if (explosionRadius > 0f) Explode();
                gameObject.SetActive(false);
            }
        }

        void Explode()
        {
            Vector2 at = transform.position;
            Fx.Burst(at, new Color(1f, 0.58f, 0.18f), 34, 5.5f, 0.15f, 0.3f);
            Fx.Burst(at, new Color(0.25f, 0.22f, 0.2f), 16, 3f, 0.18f, 0.1f);
            Fx.Shake(0.3f, 0.25f);
            Sfx.Kill();

            // Copy first: killing a zombie removes it from the live set mid-loop.
            blastBuffer.Clear();
            foreach (var z in Zombie.Active) if (z != null && z.IsAlive) blastBuffer.Add(z);
            float r2 = explosionRadius * explosionRadius;
            foreach (var z in blastBuffer)
                if (((Vector2)z.transform.position - at).sqrMagnitude <= r2) z.TakeDamage(explosionDamage);
            blastBuffer.Clear();
        }
    }
}
