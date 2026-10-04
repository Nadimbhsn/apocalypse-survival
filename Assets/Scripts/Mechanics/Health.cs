using System;
using Platformer.Gameplay;
using UnityEngine;
using static Platformer.Core.Simulation;

namespace Platformer.Mechanics
{
    /// <summary>
    /// Represebts the current vital statistics of some game entity.
    /// </summary>
    public class Health : MonoBehaviour
    {
        /// <summary>
        /// The maximum hit points for the entity.
        /// </summary>
        public int maxHP = 1;

        /// <summary>
        /// Indicates if the entity should be considered 'alive'.
        /// </summary>
        public bool IsAlive => currentHP > 0;

        int currentHP;

        /// <summary>No damage is taken before this time (the grace after a revive).</summary>
        public float invulnerableUntil;

        /// <summary>Hits a shield will still swallow whole (the runner's shield power-up).</summary>
        public int shieldHits;

        /// <summary>Raised when the shield swallowed a hit.</summary>
        public Action onShieldBlocked;

        /// <summary>
        /// Indicates the current HP as a fraction of maxHP, in the range [0, 1].
        /// </summary>
        public float NormalizedHP => maxHP > 0 ? (float)currentHP / maxHP : 0f;

        /// <summary>
        /// Increment the HP of the entity.
        /// </summary>
        public void Increment()
        {
            currentHP = Mathf.Clamp(currentHP + 1, 0, maxHP);
        }

        /// <summary>
        /// Increment the HP of the entity by the given amount.
        /// </summary>
        public void Increment(int amount)
        {
            currentHP = Mathf.Clamp(currentHP + amount, 0, maxHP);
        }

        /// <summary>
        /// Decrement the HP of the entity. Will trigger a HealthIsZero event when
        /// current HP reaches 0.
        /// </summary>
        public void Decrement()
        {
            Decrement(1);
        }

        /// <summary>
        /// Decrement the HP of the entity by the given amount. Will trigger a
        /// HealthIsZero event when current HP reaches 0.
        /// </summary>
        public void Decrement(int amount)
        {
            if (Time.time < invulnerableUntil) return;
            if (shieldHits > 0 && amount > 0)
            {
                shieldHits--;
                onShieldBlocked?.Invoke();
                return;
            }
            currentHP = Mathf.Clamp(currentHP - amount, 0, maxHP);
            if (currentHP == 0)
            {
                var ev = Schedule<HealthIsZero>();
                ev.health = this;
            }
        }

        /// <summary>
        /// Decrement the HP of the entitiy until HP reaches 0.
        /// </summary>
        public void Die()
        {
            // Death itself (a fall out of the world) is not stopped by a grace period.
            invulnerableUntil = 0f;
            shieldHits = 0;
            while (currentHP > 0) Decrement();
        }

        void Awake()
        {
            currentHP = maxHP;
        }
    }
}
