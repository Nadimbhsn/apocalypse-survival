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
    }
}
