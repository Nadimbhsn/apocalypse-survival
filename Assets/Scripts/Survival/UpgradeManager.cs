using UnityEngine;
using Platformer.Mechanics;

namespace Platformer.Survival
{
    public enum UpgradeStat { Speed, FirePower, MaxHealth, Armor, DoubleJump, Magnet, Drone }

    /// <summary>
    /// Computes stat bonuses from persisted upgrade levels (see SaveSystem) and applies
    /// them to a run's player/combat components at the start of a game. Also feeds the
    /// Arena's fighter stats.
    /// </summary>
    public static class UpgradeManager
    {
        public const int MaxLevel = 10;

        public static int MaxLevelFor(UpgradeStat stat) => stat switch
        {
            UpgradeStat.DoubleJump => 1,
            UpgradeStat.Magnet => 3,
            UpgradeStat.Drone => 3,
            _ => MaxLevel,
        };

        public static int CostForNextLevel(UpgradeStat stat)
        {
            int level = SaveSystem.GetLevel(stat);
            return stat switch
            {
                UpgradeStat.DoubleJump => 150,
                UpgradeStat.Magnet => 40 + level * 50,
                // Paid in materials, the scarcer currency: the drone is a machine, and its
                // price is what keeps it a treat rather than something every run starts with.
                UpgradeStat.Drone => 30 + level * 35,
                _ => 10 + level * 8,
            };
        }

        public static bool TryPurchase(UpgradeStat stat)
        {
            int level = SaveSystem.GetLevel(stat);
            if (level >= MaxLevelFor(stat)) return false;
            int cost = CostForNextLevel(stat);
            if (!SaveSystem.TrySpend(stat, cost)) return false;
            SaveSystem.IncrementLevel(stat);
            return true;
        }

        /// <summary>Modest per level: the runner auto-runs, and too much speed outpaces what a phone screen can show ahead.</summary>
        public static float SpeedMultiplier => 1f + SaveSystem.GetLevel(UpgradeStat.Speed) * 0.03f;
        public static float FirePowerMultiplier => 1f + SaveSystem.GetLevel(UpgradeStat.FirePower) * 0.15f;
        public static int BonusMaxHealth => SaveSystem.GetLevel(UpgradeStat.MaxHealth);
        public static float DamageReduction => Mathf.Clamp01(SaveSystem.GetLevel(UpgradeStat.Armor) * 0.05f);
        public static int AirJumps => SaveSystem.GetLevel(UpgradeStat.DoubleJump);
        /// <summary>Radius within which coins are pulled to the player (0 = no magnet).</summary>
        public static float MagnetRadius => SaveSystem.GetLevel(UpgradeStat.Magnet) * 1.3f;
        /// <summary>Companion drone level, 0 for none (see Drone).</summary>
        public static int DroneLevel => SaveSystem.GetLevel(UpgradeStat.Drone);

        /// <summary>
        /// Applies all currently-owned upgrade levels to a freshly spawned run, computed
        /// from the given un-upgraded base stats so repeated calls (one per run) never
        /// compound on top of a previously-modified value.
        /// </summary>
        public static void ApplyToPlayer(PlayerController player, float baseMaxSpeed, int baseMaxHP)
        {
            if (player == null) return;
            player.maxSpeed = baseMaxSpeed * SpeedMultiplier;
            player.airJumps = AirJumps;
            if (player.health != null)
            {
                player.health.maxHP = baseMaxHP + BonusMaxHealth;
                player.health.Increment(player.health.maxHP);
            }
        }

        /// <summary>
        /// Applies the player's armor upgrade to an incoming raw damage amount.
        /// Always leaves at least 1 damage through so armor never grants full immunity.
        /// </summary>
        public static int ReduceDamageToPlayer(int rawDamage)
        {
            float reduced = rawDamage * (1f - DamageReduction);
            return Mathf.Max(1, Mathf.RoundToInt(reduced));
        }
    }
}
