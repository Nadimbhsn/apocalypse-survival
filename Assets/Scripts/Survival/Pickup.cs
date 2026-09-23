using UnityEngine;
using Platformer.Mechanics;

namespace Platformer.Survival
{
    public enum PickupType { Coin, Material, Medkit, Ammo }

    /// <summary>
    /// Ground pickup collected by the player; coins and materials credit SaveSystem's
    /// persistent wallet, a medkit heals the player on the spot.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class Pickup : MonoBehaviour
    {
        public PickupType type = PickupType.Coin;
        public int value = 1;

        float bobT;
        Vector3 baseScale;
        bool collected;

        void Awake()
        {
            baseScale = transform.localScale;
            GetComponent<Collider2D>().isTrigger = true;
        }

        void Update()
        {
            bobT += Time.deltaTime * 4f;
            transform.localScale = baseScale * (1f + Mathf.Sin(bobT) * 0.08f);
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            if (collected) return;
            var controller = other.GetComponent<PlayerController>();
            if (controller == null) return;

            collected = true;
            var pos = transform.position;
            switch (type)
            {
                case PickupType.Coin:
                    SaveSystem.AddCoins(value);
                    Sfx.Coin();
                    RewardPopup.Show(pos, value, 0);
                    Fx.Burst(pos, PlaceholderVisuals.CoinColor, 5, 1.8f, 0.07f);
                    break;
                case PickupType.Material:
                    SaveSystem.AddMaterials(value);
                    Sfx.Material();
                    RewardPopup.Show(pos, 0, value);
                    Fx.Burst(pos, PlaceholderVisuals.MaterialColor, 5, 1.8f, 0.07f);
                    break;
                case PickupType.Ammo:
                    // A box refills the gun in hand by that gun's own box size.
                    var combat = controller.GetComponent<PlayerCombat>();
                    int got = combat != null ? combat.AddAmmoBox() : 0;
                    Sfx.Material();
                    RewardPopup.ShowAmmo(pos, got);
                    Fx.Burst(pos, new Color(0.95f, 0.8f, 0.4f), 6, 1.8f, 0.07f);
                    break;
                case PickupType.Medkit:
                    if (controller.health != null) controller.health.Increment(value);
                    Sfx.Medkit();
                    Fx.Text(pos, $"+{value} PV", new Color(0.4f, 1f, 0.4f), 1.1f);
                    Fx.Burst(pos, new Color(0.5f, 1f, 0.5f), 10, 2.2f, 0.09f);
                    break;
            }

            SurvivalDirector.Instance?.OnPickupCollected(this);
            Destroy(gameObject);
        }
    }
}
