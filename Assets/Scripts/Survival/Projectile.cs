using UnityEngine;

namespace Platformer.Survival
{
    /// <summary>
    /// Pooled straight-line projectile fired by PlayerCombat. Damages the first living
    /// Zombie it touches, then returns to its pool.
    /// </summary>
    public class Projectile : MonoBehaviour
    {
        public float speed = 12f;
        public float maxLifetime = 2.5f;

        Vector2 direction;
        int damage;
        float spawnTime;

        public void Launch(Vector2 origin, Vector2 dir, int dmg)
        {
            transform.position = origin;
            direction = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.right;
            damage = dmg;
            spawnTime = Time.time;
            gameObject.SetActive(true);
        }

        void Update()
        {
            transform.position += (Vector3)(direction * (speed * Time.deltaTime));
            if (Time.time - spawnTime > maxLifetime)
                gameObject.SetActive(false);
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            var zombie = other.GetComponent<Zombie>();
            if (zombie == null || !zombie.IsAlive) return;
            zombie.TakeDamage(damage);
            gameObject.SetActive(false);
        }
    }
}
