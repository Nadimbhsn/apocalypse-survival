using UnityEngine;
using Platformer.Mechanics;

namespace Platformer.Survival
{
    public enum UpgradeStat { Speed, FirePower, MaxHealth, Armor }

    /// <summary>
    /// Computes stat bonuses from persisted upgrade levels (see SaveSystem) and applies
    /// them to a run's player/combat components at the start of a game.
    /// </summary>
    public static class UpgradeManager
    {
        public const int MaxLevel = 10;

        public static int CostForNextLevel(UpgradeStat stat)
        {
            int level = SaveSystem.GetLevel(stat);
            return 10 + level * 8;
        }

        public static bool TryPurchase(UpgradeStat stat)
        {
            int level = SaveSystem.GetLevel(stat);
            if (level >= MaxLevel) return false;
            int cost = CostForNextLevel(stat);
            if (!SaveSystem.TrySpend(stat, cost)) return false;
            SaveSystem.IncrementLevel(stat);
            return true;
        }

        public static float SpeedMultiplier => 1f + SaveSystem.GetLevel(UpgradeStat.Speed) * 0.08f;
        public static float FirePowerMultiplier => 1f + SaveSystem.GetLevel(UpgradeStat.FirePower) * 0.15f;
        public static int BonusMaxHealth => SaveSystem.GetLevel(UpgradeStat.MaxHealth);
        public static float DamageReduction => Mathf.Clamp01(SaveSystem.GetLevel(UpgradeStat.Armor) * 0.05f);

        /// <summary>
        /// Applies all currently-owned upgrade levels to a freshly spawned run, computed
        /// from the given un-upgraded base stats so repeated calls (one per run) never
        /// compound on top of a previously-modified value.
        /// </summary>
        public static void ApplyToPlayer(PlayerController player, float baseMaxSpeed, int baseMaxHP)
        {
            if (player == null) return;
            player.maxSpeed = baseMaxSpeed * SpeedMultiplier;
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
