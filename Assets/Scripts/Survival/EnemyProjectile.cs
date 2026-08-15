using UnityEngine;
using Platformer.Mechanics;

namespace Platformer.Survival
{
    /// <summary>
    /// Straight-line projectile fired by a Spitter zombie at the player (the mirror image
    /// of Projectile, which flies the other way and damages zombies instead).
    /// </summary>
    public class EnemyProjectile : MonoBehaviour
    {
        public float speed = 8f;
        public float maxLifetime = 3f;

        Vector2 direction;
        int damage;
        float spawnTime;

        public void Launch(Vector2 origin, Vector2 dir, int dmg)
        {
            transform.position = origin;
            direction = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.left;
            damage = dmg;
            spawnTime = Time.time;
        }

        void Update()
        {
            transform.position += (Vector3)(direction * (speed * Time.deltaTime));
            if (Time.time - spawnTime > maxLifetime)
                Destroy(gameObject);
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            var controller = other.GetComponent<PlayerController>();
            if (controller == null || controller.health == null || !controller.health.IsAlive) return;

            controller.health.Decrement(UpgradeManager.ReduceDamageToPlayer(damage));
            Destroy(gameObject);
        }
    }
}
