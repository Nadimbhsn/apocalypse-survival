using UnityEngine;

namespace Platformer.Survival
{
    public enum MoveKind { Physique, Feu, Toxique, Soin, Bouclier }

    /// <summary>One of a fighter's four moves, Pokémon-style: power, accuracy, PP and optional side effects.</summary>
    public class BattleMove
    {
        public string Name;
        public MoveKind Kind;
        public int Power;        // 0 for pure support moves
        public int Accuracy = 100;
        public int MaxPP = 10;
        public int Heal;         // flat HP restored to the user
        public int DotDamage;    // damage per turn inflicted on the target...
        public int DotTurns;     // ...for this many turns (burn for Feu, poison for Toxique)
        public int ShieldTurns;  // halves damage taken by the user for this many turns

        public bool IsAttack => Power > 0;
    }

    public class BossDef
    {
        public string Name;
        public string Intro;
        public int MaxHP, Attack, Defense;
        public MoveKind Weakness, Resist;
        public BattleMove[] Moves;
        public int RewardCoins, RewardMaterials;
        public Color Tint;
        public float Scale = 1f;
    }

    /// <summary>
    /// Static data for the Arena: each character's move set (keyed by SkinCatalog id) and
    /// the boss ladder. Bosses reuse the runner's zombie archetypes so the arena reads as
    /// the same world. Boss portraits can be overridden by dropping Sprites named
    /// boss0 .. bossN into Assets/Resources/Bosses/.
    /// </summary>
    public static class ArenaCatalog
    {
        static BattleMove Attack(string name, MoveKind kind, int power, int accuracy, int pp) =>
            new BattleMove { Name = name, Kind = kind, Power = power, Accuracy = accuracy, MaxPP = pp };

        static BattleMove Dot(string name, MoveKind kind, int power, int accuracy, int pp, int dot, int turns) =>
            new BattleMove { Name = name, Kind = kind, Power = power, Accuracy = accuracy, MaxPP = pp, DotDamage = dot, DotTurns = turns };

        static BattleMove Heal(string name, int amount, int pp) =>
            new BattleMove { Name = name, Kind = MoveKind.Soin, Heal = amount, MaxPP = pp };

        static BattleMove Shield(string name, int turns, int pp) =>
            new BattleMove { Name = name, Kind = MoveKind.Bouclier, ShieldTurns = turns, MaxPP = pp };

        public static BattleMove[] MovesFor(string skinId)
        {
            switch (skinId)
            {
                case "veteran":
                    return new[]
                    {
                        Attack("Tir de barrage", MoveKind.Physique, 26, 95, 15),
                        Attack("Grenade", MoveKind.Feu, 38, 80, 6),
                        Shield("Position défensive", 2, 5),
                        Heal("Ration", 28, 4),
                    };
                case "toxic":
                    return new[]
                    {
                        Dot("Griffes toxiques", MoveKind.Toxique, 14, 95, 12, 8, 3),
                        Attack("Crachat acide", MoveKind.Toxique, 30, 90, 8),
                        Attack("Coup de tête", MoveKind.Physique, 24, 100, 15),
                        Heal("Régénération", 36, 3),
                    };
                case "ember":
                    return new[]
                    {
                        Attack("Lance-flammes", MoveKind.Feu, 30, 90, 10),
                        Dot("Brasier", MoveKind.Feu, 20, 90, 8, 9, 2),
                        Attack("Coup brûlant", MoveKind.Physique, 22, 100, 15),
                        Shield("Pare-feu", 2, 4),
                    };
                case "spectre":
                    return new[]
                    {
                        Attack("Frappe fantôme", MoveKind.Physique, 28, 100, 12),
                        new BattleMove { Name = "Drain", Kind = MoveKind.Toxique, Power = 18, Accuracy = 95, MaxPP = 8, Heal = 12 },
                        Dot("Hantise", MoveKind.Toxique, 12, 90, 6, 10, 3),
                        Shield("Voile", 2, 5),
                    };
                case "golden":
                    return new[]
                    {
                        Attack("Jugement", MoveKind.Feu, 40, 85, 6),
                        Attack("Frappe dorée", MoveKind.Physique, 30, 100, 12),
                        Attack("Éclat", MoveKind.Toxique, 26, 95, 10),
                        Heal("Aura", 40, 3),
                    };
                case "sentinel":
                    return new[]
                    {
                        Attack("Tir de couverture", MoveKind.Physique, 24, 100, 16),
                        Attack("Mine", MoveKind.Feu, 34, 85, 6),
                        Shield("Rempart", 3, 4),
                        Heal("Trousse", 30, 3),
                    };
                case "scavenger":
                    return new[]
                    {
                        Attack("Barre à mine", MoveKind.Physique, 30, 95, 12),
                        Dot("Bouteille enflammée", MoveKind.Feu, 24, 90, 6, 8, 2),
                        Attack("Ferraille rouillée", MoveKind.Toxique, 20, 100, 12),
                        Heal("Récup'", 26, 5),
                    };
                case "monk":
                    return new[]
                    {
                        Attack("Paume", MoveKind.Physique, 26, 100, 15),
                        Attack("Souffle", MoveKind.Feu, 28, 90, 8),
                        Shield("Sceau", 2, 6),
                        Heal("Méditation", 45, 3),
                    };
                case "prowler":
                    return new[]
                    {
                        Attack("Dague", MoveKind.Physique, 22, 100, 20),
                        Dot("Venin", MoveKind.Toxique, 12, 95, 8, 12, 3),
                        Attack("Embuscade", MoveKind.Physique, 42, 75, 5),
                        Shield("Fumigène", 2, 5),
                    };
                default: // Survivant
                    return new[]
                    {
                        Attack("Coup de crosse", MoveKind.Physique, 22, 100, 20),
                        Attack("Tir précis", MoveKind.Physique, 34, 85, 10),
                        Dot("Cocktail Molotov", MoveKind.Feu, 16, 90, 6, 7, 3),
                        Heal("Bandage", 32, 4),
                    };
            }
        }

        public static readonly BossDef[] Bosses =
        {
            new BossDef
            {
                Name = "Le Marcheur", Intro = "Un zombie ordinaire... mais coriace.",
                MaxHP = 120, Attack = 16, Defense = 10, Weakness = MoveKind.Feu, Resist = MoveKind.Toxique,
                Moves = new[] { Attack("Morsure", MoveKind.Physique, 18, 100, 99), Dot("Griffure", MoveKind.Toxique, 12, 100, 99, 4, 2) },
                RewardCoins = 40, RewardMaterials = 3, Tint = new Color(1f, 1f, 1f), Scale = 1f,
            },
            new BossDef
            {
                Name = "La Cracheuse", Intro = "Elle crache un acide qui ronge tout.",
                MaxHP = 150, Attack = 19, Defense = 11, Weakness = MoveKind.Physique, Resist = MoveKind.Toxique,
                Moves = new[] { Attack("Crachat", MoveKind.Toxique, 20, 90, 99), Dot("Jet acide", MoveKind.Toxique, 10, 95, 99, 7, 3), Attack("Morsure", MoveKind.Physique, 16, 100, 99) },
                RewardCoins = 60, RewardMaterials = 5, Tint = new Color(0.7f, 1.25f, 0.65f), Scale = 1.1f,
            },
            new BossDef
            {
                Name = "Le Colosse", Intro = "Une montagne de chair. Sa peau résiste aux coups.",
                MaxHP = 220, Attack = 24, Defense = 16, Weakness = MoveKind.Feu, Resist = MoveKind.Physique,
                Moves = new[] { Attack("Écrasement", MoveKind.Physique, 30, 85, 99), Attack("Charge", MoveKind.Physique, 20, 100, 99), Shield("Rugissement", 2, 99) },
                RewardCoins = 90, RewardMaterials = 8, Tint = new Color(1.5f, 0.95f, 1.1f), Scale = 1.45f,
            },
            new BossDef
            {
                Name = "Le Pyromane", Intro = "Il a mis le feu à la ville. Le feu ne lui fait rien.",
                MaxHP = 200, Attack = 26, Defense = 13, Weakness = MoveKind.Toxique, Resist = MoveKind.Feu,
                Moves = new[] { Dot("Brûlure", MoveKind.Feu, 22, 90, 99, 8, 2), Attack("Explosion", MoveKind.Feu, 42, 70, 99), Attack("Coup", MoveKind.Physique, 18, 100, 99) },
                RewardCoins = 120, RewardMaterials = 10, Tint = new Color(1.4f, 0.7f, 0.4f), Scale = 1.2f,
            },
            new BossDef
            {
                Name = "Le Patient Zéro", Intro = "Le premier infecté. Tout a commencé avec lui.",
                MaxHP = 300, Attack = 28, Defense = 15, Weakness = MoveKind.Feu, Resist = MoveKind.Toxique,
                Moves = new[] { Dot("Contagion", MoveKind.Toxique, 16, 95, 99, 10, 3), Attack("Frénésie", MoveKind.Physique, 34, 85, 99), Heal("Mutation", 40, 99), Attack("Morsure", MoveKind.Physique, 20, 100, 99) },
                RewardCoins = 200, RewardMaterials = 16, Tint = new Color(0.9f, 0.6f, 0.9f), Scale = 1.3f,
            },
        };

        public static string KindLabel(MoveKind kind) => kind switch
        {
            MoveKind.Physique => "Physique",
            MoveKind.Feu => "Feu",
            MoveKind.Toxique => "Toxique",
            MoveKind.Soin => "Soin",
            MoveKind.Bouclier => "Bouclier",
            _ => kind.ToString(),
        };

        public static Color KindColor(MoveKind kind) => kind switch
        {
            MoveKind.Physique => new Color(0.75f, 0.70f, 0.62f),
            MoveKind.Feu => new Color(0.95f, 0.50f, 0.20f),
            MoveKind.Toxique => new Color(0.55f, 0.85f, 0.30f),
            MoveKind.Soin => new Color(0.95f, 0.55f, 0.60f),
            MoveKind.Bouclier => new Color(0.55f, 0.70f, 0.95f),
            _ => Color.white,
        };
    }
}
