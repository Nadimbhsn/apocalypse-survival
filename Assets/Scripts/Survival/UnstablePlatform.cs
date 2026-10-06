using UnityEngine;
using Platformer.Mechanics;

namespace Platformer.Survival
{
    /// <summary>
    /// A cracked ground slab: standing on it starts a short shake (the warning), then it
    /// drops away, leaving a gap. The player has to keep moving or jump before it goes.
    /// Attached to a regular generated ground segment; reports back to SurvivalDirector so
    /// the segment is re-registered as a gap for zombies and spawning once it has fallen.
    /// </summary>
    public class UnstablePlatform : MonoBehaviour
    {
        public float xStart, xEnd, topY;
        public float shakeDuration = 0.8f;

        public System.Action<UnstablePlatform> OnCollapsed;

        enum State { Idle, Shaking, Falling }

        State state;
        float timer;
        float fallVelocity;
        Vector3 basePos;
        PlayerController player;
        Collider2D col;
        SpriteRenderer sr;
        SpriteRenderer[] parts;
        Color[] partColors;

        public void Init(PlayerController target)
        {
            player = target;
        }

        void Awake()
        {
            col = GetComponent<Collider2D>();
            sr = GetComponent<SpriteRenderer>();
            basePos = transform.position;
        }

        void Start()
        {
            // Painted art (RunnerArt) is added after Awake: pick it up here.
            parts = GetComponentsInChildren<SpriteRenderer>();
            partColors = new Color[parts.Length];
            for (int i = 0; i < parts.Length; i++) partColors[i] = parts[i].color;
        }

        void TintParts(float flash, float alphaStep)
        {
            if (parts == null) return;
            for (int i = 0; i < parts.Length; i++)
            {
                var p = parts[i];
                if (p == null || p == sr) continue;
                var c = partColors[i];
                if (alphaStep > 0f) { c = p.color; c.a = Mathf.MoveTowards(c.a, 0f, alphaStep); }
                else c = new Color(c.r, c.g * flash, c.b * flash, c.a);
                p.color = c;
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            switch (state)
            {
                case State.Idle:
                    if (player == null || !player.IsGrounded) return;
                    var bounds = player.Bounds;
                    if (bounds.center.x < xStart - 0.1f || bounds.center.x > xEnd + 0.1f) return;
                    if (Mathf.Abs(bounds.min.y - topY) > 0.25f) return;
                    state = State.Shaking;
                    timer = 0f;
                    break;

                case State.Shaking:
                    timer += dt;
                    transform.position = basePos + new Vector3(Mathf.Sin(timer * 65f) * 0.05f, 0f, 0f);
                    if (sr != null)
                    {
                        float flash = 0.5f + Mathf.Abs(Mathf.Sin(timer * 20f)) * 0.5f;
                        sr.color = new Color(1f, flash, flash);
                    }
                    TintParts(0.5f + Mathf.Abs(Mathf.Sin(timer * 20f)) * 0.5f, 0f);
                    if (timer >= shakeDuration)
                    {
                        state = State.Falling;
                        transform.position = basePos;
                        if (col != null) col.enabled = false;
                        fallVelocity = -1.5f;
                        OnCollapsed?.Invoke(this);
                    }
                    break;

                case State.Falling:
                    fallVelocity -= 22f * dt;
                    transform.position += new Vector3(0f, fallVelocity * dt, 0f);
                    transform.Rotate(0f, 0f, 25f * dt);
                    if (sr != null)
                    {
                        var c = sr.color;
                        c.a = Mathf.MoveTowards(c.a, 0f, dt * 1.2f);
                        sr.color = c;
                    }
                    TintParts(1f, dt * 1.2f);
                    if (transform.position.y < basePos.y - 14f) Destroy(gameObject);
                    break;
            }
        }
    }
}
