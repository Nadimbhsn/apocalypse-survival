using UnityEngine;

namespace Platformer.Survival
{
    /// <summary>
    /// Thin PlayerPrefs wrapper for persistent currency and upgrade levels that carry over
    /// between runs. Coins fund the common upgrades, materials fund armor (rarer, tougher drop).
    /// </summary>
    public static class SaveSystem
    {
        const string CoinsKey = "survival_coins";
        const string MaterialsKey = "survival_materials";
        const string BestDistanceKey = "survival_best_distance";

        public static int Coins
        {
            get => PlayerPrefs.GetInt(CoinsKey, 0);
            private set => PlayerPrefs.SetInt(CoinsKey, Mathf.Max(0, value));
        }

        public static int Materials
        {
            get => PlayerPrefs.GetInt(MaterialsKey, 0);
            private set => PlayerPrefs.SetInt(MaterialsKey, Mathf.Max(0, value));
        }

        /// <summary>Best distance ever reached in a single run, in meters - the game's high score.</summary>
        public static float BestDistance
        {
            get => PlayerPrefs.GetFloat(BestDistanceKey, 0f);
            set => PlayerPrefs.SetFloat(BestDistanceKey, value);
        }

        /// <summary>Best score in the Fusion (Suika-style) mini-game.</summary>
        public static int FusionBest
        {
            get => PlayerPrefs.GetInt("fusion_best", 0);
            set { PlayerPrefs.SetInt("fusion_best", value); PlayerPrefs.Save(); }
        }

        /// <summary>Total bosses beaten in the Arena mini-game; drives which boss comes next and how strong.</summary>
        public static int ArenaBossesBeaten
        {
            get => PlayerPrefs.GetInt("arena_bosses_beaten", 0);
            set { PlayerPrefs.SetInt("arena_bosses_beaten", value); PlayerPrefs.Save(); }
        }

        /// <summary>Best score in the Invasion mini-game.</summary>
        public static int InvasionBest
        {
            get => PlayerPrefs.GetInt("invasion_best", 0);
            set { PlayerPrefs.SetInt("invasion_best", value); PlayerPrefs.Save(); }
        }

        /// <summary>Highest wave fully survived in the Barricade mini-game.</summary>
        public static int BarricadeBestWave
        {
            get => PlayerPrefs.GetInt("barricade_best_wave", 0);
            set { PlayerPrefs.SetInt("barricade_best_wave", value); PlayerPrefs.Save(); }
        }

        public static void AddCoins(int amount)
        {
            Coins += amount;
            PlayerPrefs.Save();
        }

        public static void AddMaterials(int amount)
        {
            Materials += amount;
            PlayerPrefs.Save();
        }

        public static bool TrySpend(UpgradeStat stat, int cost)
        {
            if (stat == UpgradeStat.Armor)
            {
                if (Materials < cost) return false;
                Materials -= cost;
            }
            else
            {
                if (Coins < cost) return false;
                Coins -= cost;
            }
            PlayerPrefs.Save();
            return true;
        }

        public static int GetLevel(UpgradeStat stat) => PlayerPrefs.GetInt(LevelKey(stat), 0);

        public static void IncrementLevel(UpgradeStat stat)
        {
            PlayerPrefs.SetInt(LevelKey(stat), GetLevel(stat) + 1);
            PlayerPrefs.Save();
        }

        static string LevelKey(UpgradeStat stat) => $"survival_upgrade_{stat}";

        public static bool TrySpendCoins(int cost)
        {
            if (Coins < cost) return false;
            Coins -= cost;
            PlayerPrefs.Save();
            return true;
        }

        public static bool IsSkinUnlocked(string skinId)
        {
            if (skinId == SkinCatalog.Default.Id) return true;
            return PlayerPrefs.GetInt(SkinUnlockKey(skinId), 0) == 1;
        }

        public static void UnlockSkin(string skinId)
        {
            PlayerPrefs.SetInt(SkinUnlockKey(skinId), 1);
            PlayerPrefs.Save();
        }

        public static string SelectedSkinId
        {
            get => PlayerPrefs.GetString(SelectedSkinKey, SkinCatalog.Default.Id);
            set { PlayerPrefs.SetString(SelectedSkinKey, value); PlayerPrefs.Save(); }
        }

        const string SelectedSkinKey = "survival_selected_skin";
        static string SkinUnlockKey(string skinId) => $"survival_skin_unlocked_{skinId}";

        // ---- campaign progress ---------------------------------------------------------

        /// <summary>
        /// Stars earned on a campaign level, as a bitmask: 1 = level finished and boss
        /// beaten, 2 = every secret found, 4 = reached the boss above 60 % health. Stars
        /// are cumulative across attempts, so a player can come back for the ones they
        /// missed without losing the ones they have.
        /// </summary>
        public static int GetLevelStars(int index) => PlayerPrefs.GetInt(LevelStarsKey(index), 0);

        public static void AddLevelStars(int index, int starMask)
        {
            int merged = GetLevelStars(index) | starMask;
            PlayerPrefs.SetInt(LevelStarsKey(index), merged);
            PlayerPrefs.Save();
        }

        /// <summary>True once the level has been finished at least once (its first star).</summary>
        public static bool IsLevelCleared(int index) => (GetLevelStars(index) & 1) != 0;

        /// <summary>Total stars across the whole campaign, for the expedition card on the hub.</summary>
        public static int TotalStars
        {
            get
            {
                int total = 0;
                for (int i = 0; i < LevelCatalog.Count; i++)
                {
                    int mask = GetLevelStars(i);
                    for (int bit = 0; bit < 3; bit++) if ((mask & (1 << bit)) != 0) total++;
                }
                return total;
            }
        }

        /// <summary>Best remaining health (0..1) carried into a level's boss duel.</summary>
        public static float GetLevelBestHealth(int index) => PlayerPrefs.GetFloat(LevelHealthKey(index), 0f);

        public static void SetLevelBestHealth(int index, float fraction)
        {
            if (fraction <= GetLevelBestHealth(index)) return;
            PlayerPrefs.SetFloat(LevelHealthKey(index), Mathf.Clamp01(fraction));
            PlayerPrefs.Save();
        }

        static string LevelStarsKey(int index) => $"campaign_stars_{index}";
        static string LevelHealthKey(int index) => $"campaign_health_{index}";
    }
}
