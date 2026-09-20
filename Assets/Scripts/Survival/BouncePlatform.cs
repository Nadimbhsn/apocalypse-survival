using System.Collections;
using UnityEngine;
using Platformer.Mechanics;

namespace Platformer.Survival
{
    public enum BounceKind { Normal, Fragile, Spring, Moving }

    /// <summary>
    /// Doodle-Jump-style platform used in the Ascent zone. It is a trigger, not a solid
    /// collider: the player passes through it from below and is launched upward the
    /// moment their feet cross its top surface while falling. Fragile ones crumble after
    /// a single bounce, Spring ones launch much higher, Moving ones slide sideways.
    /// </summary>
    public class BouncePlatform : MonoBehaviour
    {
        public BounceKind kind = BounceKind.Normal;
        public float bounceVelocity = 9.2f;
        public float springMultiplier = 1.35f;
        public float moveAmplitude = 0f;
        public float moveSpeed = 1.5f;
        public float minX, maxX;

        const float Thickness = 0.3f;

        float baseX, phase, lastBounceTime;
        bool used;
        SpriteRenderer sr;
        BoxCollider2D col;
        Vector3 baseScale;

        public System.Action<BouncePlatform, PlayerController> OnBounced;

        public float TopY => transform.position.y + Thickness / 2f;

        public static BouncePlatform Create(Transform parent, float x, float y, float width, BounceKind kind, float minX, float maxX)
        {
            var go = new GameObject($"Bounce_{kind}_{y:0}");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(x, y, 0f);
            go.transform.localScale = new Vector3(width, Thickness, 1f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = PlaceholderVisuals.Square(Color.white);
            sr.sortingOrder = 0;
            sr.color = kind switch
            {
                BounceKind.Fragile => new Color(0.66f, 0.42f, 0.30f, 0.9f),
                BounceKind.Spring => new Color(0.90f, 0.50f, 0.15f),
                BounceKind.Moving => new Color(0.48f, 0.56f, 0.66f),
                _ => new Color(0.58f, 0.53f, 0.47f),
            };

            // Slightly taller than the visual so a fast fall (up to ~0.25 units per physics
            // step) can't skip straight through without ever overlapping it.
            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(1f, 3f);
            col.offset = new Vector2(0f, -0.8f);

            var platform = go.AddComponent<BouncePlatform>();
            platform.kind = kind;
            platform.minX = minX;
            platform.maxX = maxX;
            if (kind == BounceKind.Moving)
            {
                platform.moveAmplitude = Random.Range(1.4f, 2.4f);
                platform.moveSpeed = Random.Range(1.1f, 1.9f);
            }
            return platform;
        }

        void Awake()
        {
            sr = GetComponent<SpriteRenderer>();
            col = GetComponent<BoxCollider2D>();
            baseX = transform.position.x;
            baseScale = transform.localScale;
            phase = Random.Range(0f, Mathf.PI * 2f);
        }

        void Update()
        {
            if (kind != BounceKind.Moving || used) return;
            float x = baseX + Mathf.Sin(Time.time * moveSpeed + phase) * moveAmplitude;
            x = Mathf.Clamp(x, minX + baseScale.x / 2f, maxX - baseScale.x / 2f);
            var pos = transform.position;
            pos.x = x;
            transform.position = pos;
        }

        void OnTriggerEnter2D(Collider2D other) => TryBounce(other);
        void OnTriggerStay2D(Collider2D other) => TryBounce(other);

        void TryBounce(Collider2D other)
        {
            if (used || Time.time - lastBounceTime < 0.25f) return;
            var player = other.GetComponent<PlayerController>();
            if (player == null || player.health == null || !player.health.IsAlive) return;
            if (player.velocity.y > 0.05f) return; // rising: pass straight through from below

            float feet = other.bounds.min.y;
            float top = TopY;
            if (feet < top - 0.9f || feet > top + 0.4f) return;

            var rb = other.attachedRigidbody;
            Vector2 pos = rb != null ? rb.position : (Vector2)player.transform.position;
            player.Teleport(new Vector3(pos.x, pos.y + (top - feet), 0f));
            player.Bounce(kind == BounceKind.Spring ? bounceVelocity * springMultiplier : bounceVelocity);
            player.jumpState = PlayerController.JumpState.InFlight;
            lastBounceTime = Time.time;

            if (kind == BounceKind.Spring) Sfx.Spring(); else Sfx.Bounce();
            var dust = sr != null ? sr.color : Color.white;
            dust.a = 0.9f;
            Fx.Burst(new Vector3(pos.x, top, 0f), dust, kind == BounceKind.Spring ? 14 : 8, 2.5f, 0.08f, 0.3f);

            OnBounced?.Invoke(this, player);

            if (kind == BounceKind.Fragile)
            {
                used = true;
                col.enabled = false;
                StartCoroutine(Crumble());
            }
            else
            {
                StartCoroutine(Squash());
            }
        }

        IEnumerator Squash()
        {
            const float duration = 0.18f;
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float p = t / duration;
                float squash = 1f - Mathf.Sin(p * Mathf.PI) * 0.45f;
                transform.localScale = new Vector3(baseScale.x, baseScale.y * squash, 1f);
                yield return null;
            }
            transform.localScale = baseScale;
        }

        IEnumerator Crumble()
        {
            const float duration = 0.45f;
            float t = 0f;
            float fallVelocity = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                fallVelocity -= 25f * Time.deltaTime;
                transform.position += new Vector3(Mathf.Sin(t * 70f) * 0.03f, fallVelocity * Time.deltaTime, 0f);
                if (sr != null)
                {
                    var c = sr.color;
                    c.a = 1f - t / duration;
                    sr.color = c;
                }
                yield return null;
            }
            Destroy(gameObject);
        }
    }
}
