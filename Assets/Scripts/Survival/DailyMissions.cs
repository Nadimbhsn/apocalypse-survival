using System;
using UnityEngine;

namespace Platformer.Survival
{
    public enum MissionKind { RunDistance, TotalDistance, Kills, Coins, Cadence, PowerUps, Boss }

    /// <summary>One of the day's three challenges, with its progress.</summary>
    public class Mission
    {
        public int Slot;
        public MissionKind Kind;
        public int Target;
        public int RewardCoins;
        public int RewardMaterials;
        public int Progress;
        public bool Done;

        public string Text => Kind switch
        {
            MissionKind.RunDistance => $"Parcours {Target} m en une seule course",
            MissionKind.TotalDistance => $"Parcours {Target} m au total",
            MissionKind.Kills => $"Abats {Target} morts",
            MissionKind.Coins => $"Ramasse {Target} pièces",
            MissionKind.Cadence => "Termine La Cadence",
            MissionKind.PowerUps => $"Attrape {Target} bonus sur la piste",
            _ => "Bats un boss du Runner",
        };

        public string RewardText => RewardMaterials > 0 ? $"{RewardCoins} [c]   {RewardMaterials} [g]" : $"{RewardCoins} [c]";
    }

    /// <summary>
    /// Three runner challenges a day, the same for everyone on a given date (they are drawn
    /// from the date), with rewards paid the moment each is done; finishing all three adds a
    /// healing kit (or coins when the stock is full). Progress is kept per day in PlayerPrefs,
    /// so a new day simply starts three new ones.
    /// </summary>
    public static class DailyMissions
    {
        public const int Count = 3;
        public const int BonusCoinsWhenFull = 60;

        /// <summary>Raised when a mission is completed (its reward already paid).</summary>
        public static event Action<Mission> Completed;
        /// <summary>Raised when the third mission of the day is done and the bonus paid.</summary>
        public static event Action<bool> AllCompleted;

        static string loadedDay;
        static Mission[] missions;

        static string Today => DateTime.Now.ToString("yyyyMMdd");

        // kind, possible targets, coins per target, materials
        static readonly (MissionKind kind, int[] targets, int[] coins, int materials)[] Pool =
        {
            (MissionKind.RunDistance, new[] { 400, 700, 1000 }, new[] { 40, 70, 110 }, 0),
            (MissionKind.TotalDistance, new[] { 1500, 2500 }, new[] { 50, 90 }, 0),
            (MissionKind.Kills, new[] { 20, 40, 60 }, new[] { 40, 70, 100 }, 0),
            (MissionKind.Coins, new[] { 60, 120 }, new[] { 40, 80 }, 0),
            (MissionKind.Cadence, new[] { 1 }, new[] { 60 }, 3),
            (MissionKind.PowerUps, new[] { 2, 4 }, new[] { 40, 70 }, 0),
            (MissionKind.Boss, new[] { 1 }, new[] { 80 }, 4),
        };

        public static Mission[] Current
        {
            get
            {
                if (missions == null || loadedDay != Today) Load();
                return missions;
            }
        }

        public static int DoneCount
        {
            get
            {
                int n = 0;
                foreach (var m in Current) if (m.Done) n++;
                return n;
            }
        }

        public static bool BonusPaid => PlayerPrefs.GetInt($"missions_{Today}_bonus", 0) == 1;

        static void Load()
        {
            loadedDay = Today;
            // The day decides the draw: three different kinds, a target for each.
            var rng = new System.Random(int.Parse(loadedDay));
            var order = new int[Pool.Length];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            for (int i = order.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }
            missions = new Mission[Count];
            for (int s = 0; s < Count; s++)
            {
                var p = Pool[order[s]];
                int t = rng.Next(p.targets.Length);
                missions[s] = new Mission
                {
                    Slot = s,
                    Kind = p.kind,
                    Target = p.targets[t],
                    RewardCoins = p.coins[t],
                    RewardMaterials = p.materials,
                    Progress = PlayerPrefs.GetInt(Key(s), 0),
                    Done = PlayerPrefs.GetInt(Key(s) + "_done", 0) == 1,
                };
            }
        }

        static string Key(int slot) => $"missions_{loadedDay}_{slot}";

        /// <summary>Adds to every unfinished mission of that kind.</summary>
        public static void Report(MissionKind kind, int amount)
        {
            if (amount <= 0) return;
            foreach (var m in Current)
            {
                if (m.Done || m.Kind != kind) continue;
                SetProgress(m, m.Progress + amount);
            }
        }

        /// <summary>For "in a single run" missions: keeps the best value reached.</summary>
        public static void ReportBest(MissionKind kind, int value)
        {
            foreach (var m in Current)
            {
                if (m.Done || m.Kind != kind || value <= m.Progress) continue;
                SetProgress(m, value);
            }
        }

        static void SetProgress(Mission m, int value)
        {
            m.Progress = Mathf.Min(value, m.Target);
            PlayerPrefs.SetInt(Key(m.Slot), m.Progress);
            if (m.Progress < m.Target) return;

            m.Done = true;
            PlayerPrefs.SetInt(Key(m.Slot) + "_done", 1);
            SaveSystem.AddCoins(m.RewardCoins);
            if (m.RewardMaterials > 0) SaveSystem.AddMaterials(m.RewardMaterials);
            PlayerPrefs.Save();
            Completed?.Invoke(m);

            if (DoneCount < Count || BonusPaid) return;
            PlayerPrefs.SetInt($"missions_{Today}_bonus", 1);
            bool kit = SaveSystem.TryAddReviveKit();
            if (!kit) SaveSystem.AddCoins(BonusCoinsWhenFull);
            PlayerPrefs.Save();
            AllCompleted?.Invoke(kit);
        }
    }
}
