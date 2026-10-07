using UnityEngine;
using Platformer.Mechanics;

namespace Platformer.Survival
{
    public enum PickupType { Coin, Material, Medkit, Ammo, PowerUp }

    /// <summary>
    /// Ground pickup collected by the player; coins and materials credit SaveSystem's
    /// persistent wallet, a medkit heals the player on the spot.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class Pickup : MonoBehaviour
    {
        public PickupType type = PickupType.Coin;
        public int value = 1;
        public PowerUpKind powerUp;

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
            // The physics box only covers the hero's legs and belly (it is sized for
            // platforming); anything touching his painted body - head, shoulders, cape - counts.
            var hero = Hero;
            if (!collected && hero != null && hero.gameObject.activeInHierarchy && TouchesBody(hero)) Collect(hero);
        }

        static PlayerController hero;
        static PlayerController Hero
        {
            get
            {
                if (hero == null) hero = Platformer.Core.Simulation.GetModel<Platformer.Model.PlatformerModel>().player;
                if (hero == null) hero = FindAnyObjectByType<PlayerController>();
                return hero;
            }
        }

        /// <summary>The hero's painted silhouette, about 1.2 units tall, feet 0.4 under his origin.</summary>
        static readonly Rect Body = new Rect(-0.22f, -0.42f, 0.42f, 1.22f);

        bool TouchesBody(PlayerController p)
        {
            if (p.health != null && !p.health.IsAlive) return false;
            Vector2 local = p.transform.InverseTransformPoint(transform.position);
            if (p.gravitySign < 0f) local.y = -local.y;   // upside down: the head is below
            var col = GetComponent<Collider2D>();
            float r = col != null ? Mathf.Min(col.bounds.extents.x, col.bounds.extents.y) : 0.15f;
            return local.x > Body.xMin - r && local.x < Body.xMax + r && local.y > Body.yMin - r && local.y < Body.yMax + r;
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            var controller = other.GetComponent<PlayerController>();
            if (controller != null) Collect(controller);
        }

        void Collect(PlayerController controller)
        {
            if (collected) return;
            collected = true;
            var pos = transform.position;
            switch (type)
            {
                case PickupType.Coin:
                    int coins = RunPowerUps.DoubleCoins ? value * 2 : value;
                    SaveSystem.AddCoins(coins);
                    Sfx.Coin();
                    RewardPopup.Show(pos, coins, 0);
                    DailyMissions.Report(MissionKind.Coins, coins);
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
                case PickupType.PowerUp:
                    RunPowerUps.Activate(powerUp, controller.health);
                    Sfx.Spring();
                    Fx.Text(pos + Vector3.up * 0.4f, RunPowerUps.Name(powerUp), RunPowerUps.Tint(powerUp), 1.2f);
                    Fx.Burst(pos, RunPowerUps.Tint(powerUp), 18, 3f, 0.1f, 0f);
                    DailyMissions.Report(MissionKind.PowerUps, 1);
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
