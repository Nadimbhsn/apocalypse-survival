using UnityEngine;
using Platformer.Mechanics;

namespace Platformer.Survival
{
    /// <summary>
    /// A thicket of crimson thorns across the ground of the Déferlante. It does not hurt:
    /// it slows. Running through it cuts the player's speed by more than half, which is
    /// exactly what they cannot afford with the storm on their heels - so each thicket asks
    /// for a well-timed jump over it instead.
    /// </summary>
    public class Brambles : MonoBehaviour
    {
        /// <summary>Share of the run speed kept while wading through thorns.</summary>
        public const float SpeedFactor = 0.45f;

        static float lastTouch = -10f;

        /// <summary>True while the player is in a thicket (the director reads it each frame).</summary>
        public static bool Slowing => Time.time - lastTouch < 0.1f;

        public static Brambles Create(Transform parent, float x, float groundY, float length)
        {
            var go = new GameObject($"Brambles_{x:0}");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(x, groundY, 0f);

            // A dark tangle along the ground...
            Part(go.transform, PlaceholderVisuals.Square(Color.white), new Color(0.26f, 0.07f, 0.06f, 0.95f),
                new Vector2(0f, 0.14f), new Vector2(length, 0.3f), 1);

            // ...bristling with thorns of uneven height...
            int count = Mathf.Max(3, Mathf.RoundToInt(length / 0.42f));
            float step = length / count;
            for (int i = 0; i < count; i++)
            {
                float h = Random.Range(0.38f, 0.66f);
                Part(go.transform, PlaceholderVisuals.Spikes(), new Color(0.78f, 0.22f, 0.16f),
                    new Vector2(-length / 2f + (i + 0.5f) * step, h / 2f), new Vector2(step * 1.35f, h), 2);
            }

            // ...and a few red leaves caught in it, the Apogée colour.
            for (int i = 0; i < count / 2; i++)
            {
                float s = Random.Range(0.12f, 0.2f);
                Part(go.transform, PlaceholderVisuals.Circle(Color.white), ApogeeTheme.Leaf,
                    new Vector2(Random.Range(-length / 2f, length / 2f), Random.Range(0.15f, 0.5f)), new Vector2(s, s * 0.7f), 3);
            }

            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            // As tall as the thorns, no more: a jump that clears them is never caught.
            col.size = new Vector2(length, 0.7f);
            col.offset = new Vector2(0f, 0.35f);

            return go.AddComponent<Brambles>();
        }

        static void Part(Transform parent, Sprite sprite, Color color, Vector2 local, Vector2 size, int order)
        {
            var go = new GameObject("Part");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = order;
        }

        void OnTriggerStay2D(Collider2D other)
        {
            var controller = other.GetComponent<PlayerController>();
            if (controller == null) return;
            // A puff of leaves when the player plunges in, so the slowdown has a visible cause.
            if (!Slowing) Fx.Burst(controller.transform.position, ApogeeTheme.Leaf, 8, 2.2f, 0.08f, 0.6f);
            lastTouch = Time.time;
        }
    }
}
