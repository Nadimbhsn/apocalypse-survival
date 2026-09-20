using UnityEngine;
using Platformer.Mechanics;

namespace Platformer.Survival
{
    public enum HazardKind { Spikes, Toxic, Cable }

    /// <summary>
    /// Static terrain hazard: a row of rubble spikes (hurts on touch and pops the player
    /// upward so they can get clear) or a toxic puddle sunk into the ground (ticks damage
    /// while stood in). Both are jumpable, so they turn flat ground into timing tests.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class Hazard : MonoBehaviour
    {
        public HazardKind kind = HazardKind.Spikes;
        public int damage = 1;
        public float hitInterval = 0.9f;
        public float knockUpVelocity = 5.5f;

        float nextHitTime;
        float pulse;
        SpriteRenderer sr;
        Color baseColor;

        public static Hazard CreateSpikes(Transform parent, float x, float groundY, float width, float height = 0.55f)
        {
            var go = new GameObject($"Spikes_{x:0}");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(x, groundY + height / 2f, 0f);
            go.transform.localScale = new Vector3(width, height, 1f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = PlaceholderVisuals.Spikes();
            sr.sortingOrder = 1;

            // Collider a bit smaller than the visual so brushing the very edge is forgiven.
            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(0.8f, 0.7f);
            col.offset = new Vector2(0f, -0.1f);

            var hazard = go.AddComponent<Hazard>();
            hazard.kind = HazardKind.Spikes;
            return hazard;
        }

        public static Hazard CreateToxicPool(Transform parent, float x, float groundY, float width)
        {
            var go = new GameObject($"Toxic_{x:0}");
            go.transform.SetParent(parent, false);
            const float height = 0.3f;
            go.transform.position = new Vector3(x, groundY + height / 2f - 0.12f, 0f);
            go.transform.localScale = new Vector3(width, height, 1f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = PlaceholderVisuals.Square(PlaceholderVisuals.ToxicColor);
            sr.sortingOrder = 1;

            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(0.9f, 1.4f);
            col.offset = new Vector2(0f, 0.3f);

            var hazard = go.AddComponent<Hazard>();
            hazard.kind = HazardKind.Toxic;
            hazard.hitInterval = 0.7f;
            hazard.knockUpVelocity = 0f;
            return hazard;
        }

        /// <summary>A live electric cable: thin, horizontal, hurts on contact (Survol section).</summary>
        public static Hazard CreateCable(Transform parent, float x, float y, float width)
        {
            var go = new GameObject($"Cable_{x:0}");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(x, y, 0f);
            go.transform.localScale = new Vector3(width, 0.12f, 1f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = PlaceholderVisuals.Square(new Color(0.85f, 0.85f, 0.6f));
            sr.sortingOrder = 1;

            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(1f, 3f);

            var hazard = go.AddComponent<Hazard>();
            hazard.kind = HazardKind.Cable;
            hazard.hitInterval = 1f;
            hazard.knockUpVelocity = 0f;
            return hazard;
        }

        /// <summary>A deep toxic lake under the Survol section: falling in ticks damage until you fly back out.</summary>
        public static Hazard CreateToxicLake(Transform parent, float x, float surfaceY, float width, float depth)
        {
            var go = new GameObject($"Lake_{x:0}");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(x, surfaceY - depth / 2f, 0f);
            go.transform.localScale = new Vector3(width, depth, 1f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = PlaceholderVisuals.Square(PlaceholderVisuals.ToxicColor);
            sr.sortingOrder = -1;

            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;

            var hazard = go.AddComponent<Hazard>();
            hazard.kind = HazardKind.Toxic;
            hazard.hitInterval = 0.8f;
            hazard.knockUpVelocity = 0f;
            return hazard;
        }

        void Awake()
        {
            sr = GetComponent<SpriteRenderer>();
            if (sr != null) baseColor = sr.color;
            pulse = Random.Range(0f, 10f);
        }

        void Update()
        {
            if (sr == null || kind == HazardKind.Spikes) return;
            pulse += Time.deltaTime;
            var c = baseColor;
            c.a = kind == HazardKind.Cable
                ? 0.6f + Mathf.Abs(Mathf.Sin(pulse * 9f)) * 0.4f   // crackling
                : 0.75f + Mathf.Sin(pulse * 3f) * 0.18f;
            sr.color = c;
        }

        void OnTriggerStay2D(Collider2D other)
        {
            if (Time.time < nextHitTime) return;
            var controller = other.GetComponent<PlayerController>();
            if (controller == null || controller.health == null || !controller.health.IsAlive) return;

            nextHitTime = Time.time + hitInterval;
            controller.health.Decrement(UpgradeManager.ReduceDamageToPlayer(damage));
            if (knockUpVelocity > 0f && controller.velocity.y < knockUpVelocity)
                controller.Bounce(knockUpVelocity);
        }
    }
}
