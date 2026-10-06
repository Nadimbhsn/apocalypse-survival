using System;
using System.Collections.Generic;
using UnityEngine;

namespace Platformer.Survival
{
    /// <summary>
    /// What lasts between two defences of the Barricade: Éclats earned by every run (more
    /// the further it went), spent at the Forge on permanent upgrades whose prices climb
    /// level after level; five lands opened one after the other (hold wave 15 in one to open
    /// the next), each with more lanes, tougher dead and better pay; and the Mill that turns
    /// out materials while the game is closed.
    /// </summary>
    public static class BarrSave
    {
        static string K(string k) => "barr3_" + k;

        public static int Shards
        {
            get => PlayerPrefs.GetInt(K("shards"), 0);
            set => PlayerPrefs.SetInt(K("shards"), Mathf.Max(0, value));
        }

        public static int GetForge(int id) => PlayerPrefs.GetInt(K("forge_" + id), 0);
        public static void SetForge(int id, int level) => PlayerPrefs.SetInt(K("forge_" + id), level);

        public static int Best(int region) => PlayerPrefs.GetInt(K("best_" + region), 0);
        public static void SetBest(int region, int wave) => PlayerPrefs.SetInt(K("best_" + region), wave);

        /// <summary>The furthest land opened (0 = only the first).</summary>
        public static int Unlocked
        {
            get => PlayerPrefs.GetInt(K("unlocked"), 0);
            set => PlayerPrefs.SetInt(K("unlocked"), value);
        }

        public static int Region
        {
            get => PlayerPrefs.GetInt(K("region"), 0);
            set => PlayerPrefs.SetInt(K("region"), value);
        }

        public static float MillStock
        {
            get => PlayerPrefs.GetFloat(K("mill_stock"), 0f);
            set => PlayerPrefs.SetFloat(K("mill_stock"), Mathf.Max(0f, value));
        }

        /// <summary>When the Mill was last counted (UTC ticks).</summary>
        public static long MillLast
        {
            get => long.TryParse(PlayerPrefs.GetString(K("mill_last"), "0"), out long t) ? t : 0L;
            set => PlayerPrefs.SetString(K("mill_last"), value.ToString());
        }

        public static int Runs
        {
            get => PlayerPrefs.GetInt(K("runs"), 0);
            set => PlayerPrefs.SetInt(K("runs"), value);
        }

        public static int TotalKills
        {
            get => PlayerPrefs.GetInt(K("kills"), 0);
            set => PlayerPrefs.SetInt(K("kills"), value);
        }
    }

    public partial class BarricadeGame
    {
        // ---- the lands -------------------------------------------------------------------

        struct Land
        {
            public string name, subtitle;
            public int lanes;
            public float hp, reward;
            public Color grass, lane, fog;
        }

        static readonly Land[] Lands =
        {
            new Land { name = "Le Bois brumeux", subtitle = "Là où tout commence", lanes = 3, hp = 1f, reward = 1f,
                grass = Color.white, lane = Color.white, fog = new Color(1f, 1f, 1f) },
            new Land { name = "Le Marais des âmes", subtitle = "Les Saboteurs démontent tes défenses", lanes = 4, hp = 1.8f, reward = 1.7f,
                grass = new Color(0.72f, 0.92f, 0.74f), lane = new Color(0.82f, 0.9f, 0.84f), fog = new Color(0.7f, 1f, 0.8f) },
            new Land { name = "Le Cimetière oublié", subtitle = "Les Nécromanciens relèvent les morts", lanes = 4, hp = 3f, reward = 2.6f,
                grass = new Color(0.78f, 0.72f, 0.95f), lane = new Color(0.85f, 0.82f, 0.95f), fog = new Color(0.8f, 0.7f, 1f) },
            new Land { name = "La Citadelle en ruine", subtitle = "Les Porte-boucliers encaissent tes tirs", lanes = 5, hp = 5f, reward = 3.8f,
                grass = new Color(1f, 0.8f, 0.68f), lane = new Color(1f, 0.9f, 0.82f), fog = new Color(1f, 0.75f, 0.65f) },
            new Land { name = "Le Cœur de l'Ombre", subtitle = "Tout ce qui rampe dans la nuit", lanes = 5, hp = 8f, reward = 5.5f,
                grass = new Color(0.62f, 0.5f, 0.68f), lane = new Color(0.72f, 0.62f, 0.78f), fog = new Color(0.6f, 0.4f, 0.65f) },
        };

        const int UnlockWave = 15;

        // ---- the Forge -------------------------------------------------------------------

        enum Forge { Damage, Rate, Crit, Palisade, StartDebris, Loot, Molotov, Pieux, Brasier, Arbalete, Volley, Fortune, Mill }

        struct ForgeDef
        {
            public string name, effect;
            public int max, cost;
            public float growth;
        }

        static readonly ForgeDef[] ForgeDefs =
        {
            new ForgeDef { name = "Force du tir", effect = "+10 % de dégâts par niveau", max = 40, cost = 10, growth = 1.2f },
            new ForgeDef { name = "Cadence", effect = "+5 % de cadence de tir", max = 30, cost = 12, growth = 1.22f },
            new ForgeDef { name = "Coup critique", effect = "+2 % de chances de frapper x2,5", max = 25, cost = 15, growth = 1.23f },
            new ForgeDef { name = "Palissade", effect = "+12 % de solidité", max = 40, cost = 10, growth = 1.2f },
            new ForgeDef { name = "Trésor de guerre", effect = "+12 débris au départ", max = 30, cost = 8, growth = 1.21f },
            new ForgeDef { name = "Butin", effect = "+6 % de débris par mort", max = 30, cost = 14, growth = 1.22f },
            new ForgeDef { name = "Molotov", effect = "Recharge -4 %, feu +8 %", max = 20, cost = 16, growth = 1.24f },
            new ForgeDef { name = "Maîtrise des Pieux", effect = "Pieux +10 %", max = 25, cost = 12, growth = 1.22f },
            new ForgeDef { name = "Maîtrise du Brasier", effect = "Brasier +10 %", max = 25, cost = 12, growth = 1.22f },
            new ForgeDef { name = "Maîtrise de l'Arbalète", effect = "Arbalète +10 %", max = 25, cost = 12, growth = 1.22f },
            new ForgeDef { name = "La Volée", effect = "Débloque une pluie de flèches sur tous les couloirs, puis la renforce", max = 20, cost = 60, growth = 1.25f },
            new ForgeDef { name = "Fortune", effect = "+5 % d'éclats par partie", max = 30, cost = 20, growth = 1.24f },
            new ForgeDef { name = "Le Moulin", effect = "+3 [g] par heure, même jeu fermé", max = 25, cost = 25, growth = 1.26f },
        };

        static int ForgeLevel(Forge f) => BarrSave.GetForge((int)f);
        static int ForgeCost(Forge f) => Mathf.RoundToInt(ForgeDefs[(int)f].cost * Mathf.Pow(ForgeDefs[(int)f].growth, ForgeLevel(f)));
        static float ForgeBonus(Forge f, float perLevel) => 1f + perLevel * ForgeLevel(f);

        // ---- the Blessings (one run only) ---------------------------------------------------

        enum Perk { Pierce, Twin, Venom, Inferno, QuickBow, Gold, Wall, Execute, Frost, Blast, Leech, Frenzy, Hunter }

        struct PerkDef
        {
            public string name, effect;
            public int max;
        }

        static readonly PerkDef[] PerkDefs =
        {
            new PerkDef { name = "Flèches perçantes", effect = "Tes tirs traversent un mort de plus", max = 3 },
            new PerkDef { name = "Tir jumeau", effect = "+25 % de chances de tirer aussi dans un couloir voisin", max = 3 },
            new PerkDef { name = "Pieux venimeux", effect = "Les Pieux enflamment aussi", max = 1 },
            new PerkDef { name = "Brasier dévorant", effect = "Le feu brûle 50 % plus fort", max = 3 },
            new PerkDef { name = "Arbalète vive", effect = "Les Arbalètes tirent 35 % plus vite", max = 3 },
            new PerkDef { name = "Butin doré", effect = "+30 % de débris", max = 3 },
            new PerkDef { name = "Palissade bénie", effect = "+35 % de solidité, et réparée", max = 3 },
            new PerkDef { name = "Coup de grâce", effect = "Achève les morts sous 15 % de vie", max = 1 },
            new PerkDef { name = "Flèches de givre", effect = "Tes tirs ralentissent", max = 1 },
            new PerkDef { name = "Éclatement", effect = "Les morts explosent et blessent leurs voisins", max = 3 },
            new PerkDef { name = "Vampirisme", effect = "Chaque mort abattu répare la palissade", max = 3 },
            new PerkDef { name = "Frénésie", effect = "+20 % de cadence", max = 3 },
            new PerkDef { name = "Œil du chasseur", effect = "+10 % de coups critiques", max = 3 },
        };

        readonly int[] perks = new int[13];
        int Perks(Perk p) => perks[(int)p];
        const int PerkEvery = 3;
    }
}
