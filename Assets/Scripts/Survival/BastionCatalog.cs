using System.Collections.Generic;
using UnityEngine;

namespace Platformer.Survival
{
    public enum TowerKind { Archer, Cannon, Brazier, Frost, Pylon }
    public enum CreepKind { Walker, Runner, Brute, Armored, Specter, Colossus }

    /// <summary>One kind of tower, with its numbers at level 1 (see BastionCatalog.Stat for 2 and 3).</summary>
    public class TowerDef
    {
        public TowerKind Kind;
        public string Name;
        public string Blurb;
        public int Cost;
        public float Range;
        public float Interval;
        public float Damage;
        /// <summary>Canon: everything within this radius of the impact is hit.</summary>
        public float Splash;
        /// <summary>Givre: share of speed taken away, for SlowTime seconds.</summary>
        public float Slow, SlowTime;
        /// <summary>Pylône: how many enemies the lightning jumps to.</summary>
        public int Chain;
        public bool HitsAir;
        /// <summary>Archers and canon are stopped by armour; fire, frost and lightning are not.</summary>
        public bool Physical;
        public Color Color;
    }

    public class CreepDef
    {
        public CreepKind Kind;
        public string Name;
        public float Hp;
        public float Speed;
        public int Gold;
        public int Lives;
        public bool Flies;
        public bool Armored;
        public string Model;
        public float Height;
    }

    /// <summary>A campaign map (or the endless one): the road, how many waves, how tough.</summary>
    public class BastionMap
    {
        public string Name;
        public string Subtitle;
        /// <summary>Road waypoints, from where the dead come in (top) to the gate (bottom).</summary>
        public Vector2[] Path;
        /// <summary>0 for the endless map.</summary>
        public int Waves;
        public float Toughness;
        public int StartGold;
        public bool Endless => Waves <= 0;
    }

    /// <summary>
    /// "BASTION": every number of the tower defense - the five towers and their three levels,
    /// the six kinds of dead, the maps and how each wave is made - kept together so the
    /// balance can be read in one place.
    /// </summary>
    public static class BastionCatalog
    {
        public const int MaxTowerLevel = 3;
        public const int StartLives = 20;
        public const float SellRefund = 0.6f;

        public static readonly TowerDef[] Towers =
        {
            new TowerDef { Kind = TowerKind.Archer, Name = "Archers", Blurb = "Rapides, touchent les volants",
                Cost = 50, Range = 2.7f, Interval = 0.6f, Damage = 4f, HitsAir = true, Physical = true, Color = new Color(0.85f, 0.65f, 0.35f) },
            new TowerDef { Kind = TowerKind.Cannon, Name = "Canon", Blurb = "Lent, touche tout un groupe",
                Cost = 80, Range = 2.4f, Interval = 1.6f, Damage = 11f, Splash = 0.95f, Physical = true, Color = new Color(0.45f, 0.45f, 0.5f) },
            new TowerDef { Kind = TowerKind.Brazier, Name = "Brasero", Blurb = "Brûle tout ce qui passe près",
                Cost = 70, Range = 1.8f, Interval = 0.5f, Damage = 2.2f, Color = new Color(0.95f, 0.45f, 0.18f) },
            new TowerDef { Kind = TowerKind.Frost, Name = "Givre", Blurb = "Ralentit, touche les volants",
                Cost = 60, Range = 2.3f, Interval = 1.0f, Damage = 1.5f, Slow = 0.4f, SlowTime = 1.6f, HitsAir = true, Color = new Color(0.55f, 0.85f, 1f) },
            new TowerDef { Kind = TowerKind.Pylon, Name = "Pylône", Blurb = "Éclair en chaîne, double sur les volants",
                Cost = 100, Range = 2.8f, Interval = 1.2f, Damage = 6f, Chain = 3, HitsAir = true, Color = new Color(0.75f, 0.6f, 1f) },
        };

        public static TowerDef Tower(TowerKind k) => Towers[(int)k];

        /// <summary>Price to reach a level (2 or 3) from the one below.</summary>
        public static int UpgradeCost(TowerDef t, int toLevel) => Mathf.RoundToInt(t.Cost * (toLevel == 2 ? 0.8f : 1.25f));

        /// <summary>Gold spent on a tower up to its level, for the resale price.</summary>
        public static int Invested(TowerDef t, int level)
        {
            int sum = t.Cost;
            for (int l = 2; l <= level; l++) sum += UpgradeCost(t, l);
            return sum;
        }

        /// <summary>Each level: half again the damage, a little more range, a little faster.</summary>
        public static float DamageAt(TowerDef t, int level) => t.Damage * Mathf.Pow(1.6f, level - 1);
        public static float RangeAt(TowerDef t, int level) => t.Range + 0.3f * (level - 1);
        public static float IntervalAt(TowerDef t, int level) => t.Interval * Mathf.Pow(0.87f, level - 1);

        public static readonly CreepDef[] Creeps =
        {
            new CreepDef { Kind = CreepKind.Walker, Name = "Marcheur", Hp = 20f, Speed = 1.0f, Gold = 5, Lives = 1, Model = "character-zombie", Height = 0.9f },
            new CreepDef { Kind = CreepKind.Runner, Name = "Coureur", Hp = 12f, Speed = 1.8f, Gold = 4, Lives = 1, Model = "character-skeleton", Height = 0.85f },
            new CreepDef { Kind = CreepKind.Brute, Name = "Brute", Hp = 85f, Speed = 0.6f, Gold = 12, Lives = 2, Model = "character-keeper", Height = 1.3f },
            new CreepDef { Kind = CreepKind.Armored, Name = "Cuirassé", Hp = 45f, Speed = 0.8f, Gold = 9, Lives = 1, Armored = true, Model = "character-zombie", Height = 1.0f },
            new CreepDef { Kind = CreepKind.Specter, Name = "Spectre", Hp = 24f, Speed = 1.25f, Gold = 7, Lives = 1, Flies = true, Model = "character-ghost", Height = 0.9f },
            new CreepDef { Kind = CreepKind.Colossus, Name = "Colosse", Hp = 300f, Speed = 0.45f, Gold = 70, Lives = 5, Model = "character-keeper", Height = 2f },
        };

        public static CreepDef Creep(CreepKind k) => Creeps[(int)k];

        /// <summary>Armour halves arrows and cannonballs.</summary>
        public const float ArmorFactor = 0.5f;

        public static readonly BastionMap[] Campaign =
        {
            new BastionMap { Name = "La Clairière", Subtitle = "Un chemin tranquille... pour l'instant", Waves = 10, Toughness = 1.0f, StartGold = 160,
                Path = new[] { V(-2.8f, 7.8f), V(-2.8f, 3.2f), V(2.8f, 3.2f), V(2.8f, -0.8f), V(-2.8f, -0.8f), V(-2.8f, -4.6f), V(0f, -4.6f), V(0f, -7.8f) } },
            new BastionMap { Name = "Le Gué", Subtitle = "Ils serpentent entre les rochers", Waves = 12, Toughness = 1.1f, StartGold = 170,
                Path = new[] { V(3f, 7.8f), V(3f, 4.6f), V(-3f, 4.6f), V(-3f, 1.6f), V(3f, 1.6f), V(3f, -1.4f), V(-3f, -1.4f), V(-3f, -4.6f), V(1.5f, -4.6f), V(1.5f, -7.8f) } },
            new BastionMap { Name = "La Spirale", Subtitle = "Un long détour autour de la colline", Waves = 14, Toughness = 1.25f, StartGold = 180,
                Path = new[] { V(-3.2f, 7.8f), V(-3.2f, -2.2f), V(0f, -2.2f), V(0f, 4.2f), V(3.2f, 4.2f), V(3.2f, -5f), V(-1f, -5f), V(-1f, -7.8f) } },
            new BastionMap { Name = "La Falaise", Subtitle = "Un chemin court : chaque tour compte", Waves = 16, Toughness = 1.15f, StartGold = 280,
                Path = new[] { V(0f, 7.8f), V(0f, 2.2f), V(-3f, 2.2f), V(-3f, -3f), V(2.6f, -3f), V(2.6f, -7.8f) } },
            new BastionMap { Name = "Le Cœur de l'Archipel", Subtitle = "La dernière ligne avant le bastion", Waves = 20, Toughness = 1.2f, StartGold = 320,
                Path = new[] { V(3.2f, 7.8f), V(3.2f, 2.6f), V(-0.4f, 2.6f), V(-0.4f, -1.2f), V(-3.2f, -1.2f), V(-3.2f, -7.8f) } },
        };

        /// <summary>The endless map: a long winding road and waves that never stop growing.</summary>
        public static readonly BastionMap Endless = new BastionMap
        {
            Name = "L'Île sans fin", Subtitle = "Combien de vagues tiendras-tu ?", Waves = 0, Toughness = 1f, StartGold = 180,
            Path = new[] { V(-3.2f, 7.8f), V(-3.2f, 5f), V(3.2f, 5f), V(3.2f, 2.2f), V(-3.2f, 2.2f), V(-3.2f, -0.6f), V(3.2f, -0.6f), V(3.2f, -3.6f), V(-2f, -3.6f), V(-2f, -7.8f) },
        };

        static Vector2 V(float x, float y) => new Vector2(x, y);

        /// <summary>
        /// Build spots for a map, found rather than hand-placed: points of a 0.9 m grid that
        /// stand 0.95 to 2 m off the road (close enough to reach it, never on it), the
        /// closest first, at least 1.15 m apart, sixteen at most. The same rule a balance
        /// simulation used to check every map.
        /// </summary>
        public static List<Vector2> Slots(Vector2[] path)
        {
            var cands = new List<(float d, int order, Vector2 p)>();
            for (int iy = 0; iy <= 14; iy++)
                for (int ix = 0; ix <= 8; ix++)
                {
                    var p = new Vector2(-3.6f + ix * 0.9f, 6.6f - iy * 0.9f);
                    float d = DistanceToPath(path, p);
                    if (d >= 0.95f && d <= 2f) cands.Add((d, cands.Count, p));
                }
            // Ties keep the grid order (top to bottom, left to right), like the simulation's stable sort.
            cands.Sort((a, b) => a.d != b.d ? a.d.CompareTo(b.d) : a.order.CompareTo(b.order));
            var slots = new List<Vector2>();
            foreach (var (_, _, p) in cands)
            {
                bool ok = true;
                foreach (var o in slots) if ((o - p).sqrMagnitude < 1.15f * 1.15f) { ok = false; break; }
                if (ok) slots.Add(p);
                if (slots.Count >= 16) break;
            }
            return slots;
        }

        public static float DistanceToPath(Vector2[] path, Vector2 q)
        {
            float best = float.MaxValue;
            for (int i = 0; i < path.Length - 1; i++)
            {
                Vector2 a = path[i], b = path[i + 1], v = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(q - a, v) / v.sqrMagnitude);
                best = Mathf.Min(best, (q - (a + v * t)).magnitude);
            }
            return best;
        }

        public static float PathLength(Vector2[] path)
        {
            float l = 0f;
            for (int i = 0; i < path.Length - 1; i++) l += (path[i + 1] - path[i]).magnitude;
            return l;
        }

        public static Vector2 PointAt(Vector2[] path, float d)
        {
            for (int i = 0; i < path.Length - 1; i++)
            {
                float l = (path[i + 1] - path[i]).magnitude;
                if (d <= l) return Vector2.Lerp(path[i], path[i + 1], d / l);
                d -= l;
            }
            return path[path.Length - 1];
        }

        /// <summary>
        /// The dead of wave n, in order of arrival. The kinds join one by one (runners from
        /// wave 2, brutes 4, spectres 5, armoured 7), every tenth wave is led by a colossus,
        /// and their health grows a little faster than linearly so that a fixed set of towers
        /// is always eventually overrun - the endless mode's ceiling is the player.
        /// </summary>
        public static List<CreepKind> WaveCreeps(int n)
        {
            var list = new List<CreepKind>();
            int count = 6 + n * 2;
            if (n % 10 == 0)
            {
                list.Add(CreepKind.Colossus);
                for (int i = 1; i < n / 10; i++) list.Add(CreepKind.Colossus);
                count = Mathf.RoundToInt(count * 0.6f);
            }
            var rng = new System.Random(n * 7919);
            for (int i = 0; i < count; i++)
            {
                double r = rng.NextDouble();
                CreepKind k = CreepKind.Walker;
                if (n >= 7 && r < 0.12) k = CreepKind.Armored;
                else if (n >= 5 && r < 0.26) k = CreepKind.Specter;
                else if (n >= 4 && r < 0.36) k = CreepKind.Brute;
                else if (n >= 2 && r < 0.62) k = CreepKind.Runner;
                list.Add(k);
            }
            return list;
        }

        public static float HealthScale(int n, float toughness)
        {
            float k = n - 1;
            return (1f + 0.16f * k + 0.006f * k * k) * toughness;
        }

        /// <summary>Seconds between two arrivals: tighter as the waves go on.</summary>
        public static float SpawnGap(int n) => Mathf.Max(0.42f, 0.95f - n * 0.025f);

        /// <summary>Gold for clearing a wave.</summary>
        public static int WaveBonus(int n) => 20 + n * 4;

        // ---- heroes -----------------------------------------------------------------------

        public enum HeroPower { Volley, Grenade, Toxic, Rampart, FireWall, Salvage, Calm, Drain, Ambush, Meteor }

        /// <summary>Each character brings its own power to the field.</summary>
        public static HeroPower PowerOf(string skinId) => skinId switch
        {
            "veteran" => HeroPower.Grenade,
            "toxic" => HeroPower.Toxic,
            "sentinel" => HeroPower.Rampart,
            "ember" => HeroPower.FireWall,
            "scavenger" => HeroPower.Salvage,
            "monk" => HeroPower.Calm,
            "spectre" => HeroPower.Drain,
            "prowler" => HeroPower.Ambush,
            "golden" => HeroPower.Meteor,
            _ => HeroPower.Volley,
        };

        public static string PowerName(HeroPower p) => p switch
        {
            HeroPower.Grenade => "GRENADE",
            HeroPower.Toxic => "NUAGE TOXIQUE",
            HeroPower.Rampart => "REMPART",
            HeroPower.FireWall => "MUR DE FEU",
            HeroPower.Salvage => "RÉCUPÉRATION",
            HeroPower.Calm => "MÉDITATION",
            HeroPower.Drain => "DRAIN",
            HeroPower.Ambush => "EMBUSCADE",
            HeroPower.Meteor => "MÉTÉORE",
            _ => "RAFALE",
        };

        public static string PowerBlurb(HeroPower p) => p switch
        {
            HeroPower.Grenade => "Une grenade sur le plus gros groupe",
            HeroPower.Toxic => "Un nuage qui ronge tout autour du héros",
            HeroPower.Rampart => "Rend 3 vies au bastion",
            HeroPower.FireWall => "Embrase le chemin autour du héros",
            HeroPower.Salvage => "Récupère 120 or",
            HeroPower.Calm => "Tous les morts ralentissent",
            HeroPower.Drain => "Vole la vie des morts proches",
            HeroPower.Ambush => "Étourdit et blesse les morts proches",
            HeroPower.Meteor => "Écrase le mort le plus coriace",
            _ => "Six tirs sur les morts les plus proches",
        };

        public const float PowerCooldown = 28f;
    }
}
