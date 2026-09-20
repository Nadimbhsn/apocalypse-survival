using UnityEngine;
using Platformer.Mechanics;

namespace Platformer.Survival
{
    /// <summary>
    /// A small floating island that drifts sideways, used by the Archipel section of the
    /// runner. Horizontal only: a platform moving up into the player would push them
    /// through it, whereas a sliding one just asks the player to time their jump.
    ///
    /// Everything happens in physics time on a kinematic body, and a rider is carried by
    /// *adding* the islet's movement to their own body position. Writing an absolute
    /// position instead would overwrite the movement the player's own physics step just
    /// applied, and they would run in place.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class MovingIsland : MonoBehaviour
    {
        public float amplitude = 1f;
        public float speed = 1.2f;
        public float phase;

        Rigidbody2D body;
        float baseX;

        void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            baseX = transform.position.x;
        }

        void FixedUpdate()
        {
            float newX = baseX + Mathf.Sin(Time.fixedTime * speed + phase) * amplitude;
            float dx = newX - body.position.x;
            if (Mathf.Abs(dx) < 0.00001f) return;

            body.MovePosition(new Vector2(newX, body.position.y));
            CarryRider(dx, newX);
        }

        void CarryRider(float dx, float newX)
        {
            var director = SurvivalDirector.Instance;
            var player = director != null ? director.Player : null;
            if (player == null || !player.IsGrounded || player.collider2d == null) return;

            var bounds = player.collider2d.bounds;
            float top = transform.position.y + transform.localScale.y * 0.5f;
            if (Mathf.Abs(bounds.min.y - top) > 0.3f) return;

            float half = transform.localScale.x * 0.5f + 0.2f;
            if (bounds.center.x < newX - half || bounds.center.x > newX + half) return;

            var rb = player.GetComponent<Rigidbody2D>();
            if (rb != null) rb.position += new Vector2(dx, 0f);
        }
    }
}
