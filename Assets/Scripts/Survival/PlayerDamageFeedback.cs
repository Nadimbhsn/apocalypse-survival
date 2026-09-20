using UnityEngine;
using Platformer.Mechanics;

namespace Platformer.Survival
{
    /// <summary>
    /// Flashes the player red for a moment whenever their HP drops, and briefly green when
    /// it goes up (medkit). Health has no damage event, so this simply watches the value
    /// each frame; it restores the equipped skin tint once the flash ends.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class PlayerDamageFeedback : MonoBehaviour
    {
        const float FlashDuration = 0.35f;

        PlayerController player;
        SpriteRenderer sr;
        float lastNormalized = -1f;
        float flashTimer;
        Color flashColor;

        void Awake()
        {
            player = GetComponent<PlayerController>();
            sr = GetComponent<SpriteRenderer>();
        }

        void LateUpdate()
        {
            if (player.health == null || sr == null) return;

            float normalized = player.health.NormalizedHP;
            if (lastNormalized >= 0f && normalized < lastNormalized - 0.001f)
            {
                flashTimer = FlashDuration;
                flashColor = new Color(1f, 0.25f, 0.2f);
                if (player.health.IsAlive)
                {
                    Sfx.Hit();
                    Fx.Hitstop();
                    Fx.Shake(0.35f, 0.25f);
                    Fx.Burst(transform.position, new Color(0.75f, 0.15f, 0.1f), 8, 3f, 0.09f);
                }
            }
            else if (lastNormalized >= 0f && normalized > lastNormalized + 0.001f && normalized < 1f + 0.001f && lastNormalized > 0f)
            {
                flashTimer = FlashDuration;
                flashColor = new Color(0.4f, 1f, 0.4f);
            }
            lastNormalized = normalized;

            if (flashTimer <= 0f) return;

            flashTimer -= Time.deltaTime;
            var skin = SkinCatalog.Find(SaveSystem.SelectedSkinId).Tint;
            if (flashTimer <= 0f)
            {
                sr.color = skin;
                return;
            }

            float p = flashTimer / FlashDuration;
            float blink = Mathf.Abs(Mathf.Sin(p * Mathf.PI * 3f));
            sr.color = Color.Lerp(skin, flashColor, blink * p);
        }

        /// <summary>Call when the run restarts so a refill to full HP isn't read as a heal flash.</summary>
        public void ResetTracking()
        {
            lastNormalized = -1f;
            flashTimer = 0f;
        }
    }
}
