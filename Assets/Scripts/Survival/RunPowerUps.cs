using UnityEngine;
using Platformer.Mechanics;

namespace Platformer.Survival
{
    public enum PowerUpKind { Shield, DoubleCoins, Magnet, InfiniteAmmo }

    /// <summary>
    /// Short-lived bonuses picked up on the runner's track: a shield that swallows the next
    /// hit, coins worth double, a giant magnet, and a gun that never runs dry. Timers run on
    /// game time, so the pause menu freezes them with everything else. The shield lives on
    /// the player's Health (see Health.shieldHits) because every source of damage goes
    /// through it.
    /// </summary>
    public static class RunPowerUps
    {
        public const float DoubleCoinsTime = 12f, MagnetTime = 12f, InfiniteAmmoTime = 7f;
        public const float MagnetRadius = 7f;

        static float doubleUntil, magnetUntil, ammoUntil;

        public static bool DoubleCoins => Time.time < doubleUntil;
        public static bool Magnet => Time.time < magnetUntil;
        public static bool InfiniteAmmo => Time.time < ammoUntil;

        public static void Reset(Health health)
        {
            doubleUntil = magnetUntil = ammoUntil = 0f;
            if (health != null) health.shieldHits = 0;
        }

        public static void Activate(PowerUpKind kind, Health health)
        {
            switch (kind)
            {
                case PowerUpKind.Shield:
                    if (health != null) health.shieldHits = 1;
                    break;
                case PowerUpKind.DoubleCoins:
                    doubleUntil = Time.time + DoubleCoinsTime;
                    break;
                case PowerUpKind.Magnet:
                    magnetUntil = Time.time + MagnetTime;
                    break;
                case PowerUpKind.InfiniteAmmo:
                    ammoUntil = Time.time + InfiniteAmmoTime;
                    break;
            }
        }

        public static string Name(PowerUpKind kind) => kind switch
        {
            PowerUpKind.Shield => "BOUCLIER",
            PowerUpKind.DoubleCoins => "PIÈCES x2",
            PowerUpKind.Magnet => "SUPER AIMANT",
            _ => "MUNITIONS INFINIES",
        };

        public static Color Tint(PowerUpKind kind) => kind switch
        {
            PowerUpKind.Shield => new Color(0.45f, 0.82f, 1f),
            PowerUpKind.DoubleCoins => new Color(1f, 0.82f, 0.3f),
            PowerUpKind.Magnet => new Color(1f, 0.42f, 0.38f),
            _ => new Color(1f, 0.62f, 0.25f),
        };

        /// <summary>One line for the HUD with what is active and for how long.</summary>
        public static string HudLine(Health health)
        {
            var sb = new System.Text.StringBuilder();
            void Add(string s)
            {
                if (sb.Length > 0) sb.Append("    ");
                sb.Append(s);
            }
            if (health != null && health.shieldHits > 0) Add("BOUCLIER");
            if (DoubleCoins) Add($"PIÈCES x2  {Mathf.CeilToInt(doubleUntil - Time.time)}");
            if (Magnet) Add($"AIMANT  {Mathf.CeilToInt(magnetUntil - Time.time)}");
            if (InfiniteAmmo) Add($"TIR ILLIMITÉ  {Mathf.CeilToInt(ammoUntil - Time.time)}");
            return sb.ToString();
        }
    }
}
