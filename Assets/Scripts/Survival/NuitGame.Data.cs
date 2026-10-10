using System;
using System.Collections.Generic;
using UnityEngine;

namespace Platformer.Survival
{
    /// <summary>
    /// DERNIÈRE NUIT - what the night is made of: the station's plan, the guns, the gadgets,
    /// the bonuses offered after each wave, the dead and the local leaderboard.
    /// </summary>
    public partial class NuitGame
    {
        // ---- the station (world units, relative to Origin; y up) ---------------------------------

        /// <summary>A part of the station. The forecourt is open from the start; the others are bought.</summary>
        class Zone
        {
            public string Name, Blurb;
            public Rect Area;
            public int Cost;
            /// <summary>The opening into the forecourt (its shutter), and where to stand to go through it.</summary>
            public Vector2 Gate;
            public bool GateHorizontal;
            public bool Open;
            public SpriteRenderer Veil, Shutter;
            public float ShutterAnim = -1f;
        }

        static Zone[] NewZones() => new[]
        {
            new Zone { Name = "Parvis", Blurb = "Les pompes, sous la lumière", Area = new Rect(-8f, -8f, 16f, 14f), Open = true },
            new Zone { Name = "Boutique", Blurb = "Des rayons pour souffler, une caisse à fouiller", Area = new Rect(-7f, 6f, 14f, 9f), Cost = 80, Gate = new Vector2(0f, 6f), GateHorizontal = true },
            new Zone { Name = "Garage", Blurb = "L'établi, le pont, une caisse d'outils", Area = new Rect(8f, -6f, 9f, 11f), Cost = 120, Gate = new Vector2(8f, 0f) },
            new Zone { Name = "Parking", Blurb = "De la place pour courir, des épaves à piller", Area = new Rect(-19f, -8f, 11f, 14f), Cost = 160, Gate = new Vector2(-8f, 0f) },
        };

        /// <summary>A barricaded way in: the dead come to it from outside and break it down.</summary>
        class Door
        {
            public Vector2 Pos;
            public bool Horizontal;
            /// <summary>Points out of the station.</summary>
            public Vector2 Out;
            public int Zone;
            public float Hp, MaxHp;
            public SpriteRenderer Sr;
            public int State = -1;
            public float Shake;
            public bool Intact => Hp > 0f;
        }

        static readonly (Vector2 pos, bool horizontal, Vector2 outward, int zone)[] DoorPlan =
        {
            (new Vector2(-4f, -8f), true, Vector2.down, 0),
            (new Vector2(4f, -8f), true, Vector2.down, 0),
            (new Vector2(0f, 15f), true, Vector2.up, 1),
            (new Vector2(12.5f, -6f), true, Vector2.down, 2),
            (new Vector2(17f, 0f), false, Vector2.right, 2),
            (new Vector2(-14f, -8f), true, Vector2.down, 3),
            (new Vector2(-19f, 0f), false, Vector2.left, 3),
        };

        /// <summary>Walls: kind (brick, metal, fence), horizontal or not, the line, and the gaps left in it.</summary>
        static readonly (string kind, bool horizontal, float at, float from, float to, float[] gaps)[] WallPlan =
        {
            ("fence", true, -8f, -19f, 8f, new[] { -5.2f, -2.8f, 2.8f, 5.2f, -15.2f, -12.8f }),
            ("brick", true, 6f, -8f, 8f, new[] { -1.2f, 1.2f }),
            ("brick", true, 15f, -7f, 7f, new[] { -1.2f, 1.2f }),
            ("fence", true, 6f, -19f, -8f, new float[0]),
            ("metal", true, -6f, 8f, 17f, new[] { 11.3f, 13.7f }),
            ("metal", true, 5f, 8f, 17f, new float[0]),
            ("brick", false, -7f, 6f, 15f, new float[0]),
            ("brick", false, 7f, 6f, 15f, new float[0]),
            ("fence", false, -19f, -8f, 6f, new[] { -1.2f, 1.2f }),
            ("fence", false, -8f, -8f, 6f, new[] { -1.5f, 1.5f }),
            ("metal", false, 8f, -8f, 6f, new[] { -1.5f, 1.5f }),
            ("metal", false, 17f, -6f, 5f, new[] { -1.2f, 1.2f }),
        };

        /// <summary>Painted props: sprite, position, footprint (relative to the feet; zero = walk over it).</summary>
        static readonly (string sprite, Vector2 pos, Rect foot)[] PropPlan =
        {
            ("pump", new Vector2(-2.2f, 0.5f), new Rect(-0.45f, -0.05f, 0.9f, 0.6f)),
            ("pump", new Vector2(2.2f, 0.5f), new Rect(-0.45f, -0.05f, 0.9f, 0.6f)),
            ("pillar", new Vector2(-5f, -2f), new Rect(-0.25f, -0.05f, 0.5f, 0.4f)),
            ("pillar", new Vector2(5f, -2f), new Rect(-0.25f, -0.05f, 0.5f, 0.4f)),
            ("pillar", new Vector2(-5f, 3f), new Rect(-0.25f, -0.05f, 0.5f, 0.4f)),
            ("pillar", new Vector2(5f, 3f), new Rect(-0.25f, -0.05f, 0.5f, 0.4f)),
            ("lamp", new Vector2(-7f, -7f), new Rect(-0.15f, -0.05f, 0.3f, 0.3f)),
            ("lamp", new Vector2(7f, -7f), new Rect(-0.15f, -0.05f, 0.3f, 0.3f)),
            ("lamp", new Vector2(-18f, 5f), new Rect(-0.15f, -0.05f, 0.3f, 0.3f)),
            ("lamp", new Vector2(16f, -5.5f), new Rect(-0.15f, -0.05f, 0.3f, 0.3f)),
            ("sign", new Vector2(9.5f, -9f), new Rect(-0.15f, -0.05f, 0.3f, 0.3f)),
            ("car_red", new Vector2(5.5f, -6.8f), new Rect(-1f, 0.1f, 2f, 3.7f)),
            ("barrel_red", new Vector2(-6.5f, 4.8f), new Rect(-0.35f, -0.05f, 0.7f, 0.45f)),
            ("barrel_blue", new Vector2(-6f, 5f), new Rect(-0.35f, -0.05f, 0.7f, 0.45f)),
            ("trash", new Vector2(6.5f, 4.8f), new Rect(0f, 0f, 0f, 0f)),
            ("shelf", new Vector2(-3f, 9f), new Rect(-1.95f, -0.05f, 3.9f, 0.7f)),
            ("shelf", new Vector2(3f, 9f), new Rect(-1.95f, -0.05f, 3.9f, 0.7f)),
            ("shelf", new Vector2(-3f, 12f), new Rect(-1.95f, -0.05f, 3.9f, 0.7f)),
            ("shelf", new Vector2(3f, 12f), new Rect(-1.95f, -0.05f, 3.9f, 0.7f)),
            ("counter", new Vector2(4.5f, 7f), new Rect(-1.45f, -0.05f, 2.9f, 0.8f)),
            ("fridge", new Vector2(-5.5f, 14.4f), new Rect(-0.7f, -0.05f, 1.4f, 0.5f)),
            ("fridge", new Vector2(-4f, 14.4f), new Rect(-0.7f, -0.05f, 1.4f, 0.5f)),
            ("lift", new Vector2(12.5f, -1.6f), new Rect(0f, 0f, 0f, 0f)),
            ("car_blue", new Vector2(12.5f, -1.4f), new Rect(-1f, 0.1f, 2f, 3.7f)),
            ("workbench", new Vector2(12f, 4.3f), new Rect(-1.55f, -0.05f, 3.1f, 0.6f)),
            ("tyres", new Vector2(15.8f, -5.2f), new Rect(-0.45f, -0.05f, 0.9f, 0.5f)),
            ("tyres", new Vector2(15.2f, -4.4f), new Rect(-0.45f, -0.05f, 0.9f, 0.5f)),
            ("barrel_red", new Vector2(9.4f, -5.3f), new Rect(-0.35f, -0.05f, 0.7f, 0.45f)),
            ("car_white", new Vector2(-15f, 2.6f), new Rect(-1.9f, 0.3f, 3.8f, 1.7f)),
            ("car_green", new Vector2(-12f, -5.5f), new Rect(-1.9f, 0.3f, 3.8f, 1.7f)),
            ("car_red", new Vector2(-17f, -2.5f), new Rect(-1f, 0.1f, 2f, 3.7f)),
            ("dumpster", new Vector2(-17.3f, 4.6f), new Rect(-1.1f, -0.05f, 2.2f, 0.9f)),
            ("trash", new Vector2(-10f, 4.8f), new Rect(0f, 0f, 0f, 0f)),
        };

        /// <summary>Where the lamps throw their warm light (zone -1: always lit).</summary>
        static readonly (Vector2 pos, float size, int zone)[] LightPlan =
        {
            (new Vector2(-6.4f, -6.6f), 8f, -1), (new Vector2(7.6f, -6.6f), 8f, -1), (new Vector2(0f, 1f), 11f, -1),
            (new Vector2(-17.4f, 5.3f), 8f, 3), (new Vector2(16.6f, -5.2f), 7f, 2), (new Vector2(0f, 10.5f), 11f, 1), (new Vector2(12.5f, 0f), 9f, 2),
        };

        /// <summary>A loot crate in each zone, opened when the zone is.</summary>
        static readonly Vector2[] LootPlan = { Vector2.zero, new Vector2(-5.6f, 7.4f), new Vector2(9.6f, 3.4f), new Vector2(-10.2f, -1.6f) };

        static readonly Rect WorldBounds = new Rect(-24f, -15f, 46f, 34f);

        // ---- guns ----------------------------------------------------------------------------------

        class Gun
        {
            public string Name, Art, Blurb;
            public float Damage, Rate, Range, Speed, Spread, Splash;
            public int Count = 1, Pierce, Cost;
        }

        static readonly Gun[] Guns =
        {
            new Gun { Name = "Pistolet", Art = "pistol", Blurb = "Fidèle, précis", Damage = 12f, Rate = 2.6f, Range = 6.5f, Speed = 16f },
            new Gun { Name = "Revolver", Art = "revolver", Blurb = "Lent, traverse un mort", Damage = 34f, Rate = 1.4f, Range = 7.5f, Speed = 18f, Pierce = 1, Cost = 80 },
            new Gun { Name = "Mitraillette", Art = "uzi", Blurb = "Une pluie de balles", Damage = 8f, Rate = 9f, Range = 5.5f, Speed = 16f, Spread = 9f, Cost = 110 },
            new Gun { Name = "Mitrailleuse", Art = "rifle", Blurb = "Loin, vite, perçant", Damage = 16f, Rate = 6f, Range = 8.5f, Speed = 21f, Pierce = 1, Cost = 220 },
            new Gun { Name = "Lance-grenade", Art = "launcher", Blurb = "Explose sur un groupe", Damage = 45f, Rate = 1.1f, Range = 7f, Speed = 11f, Splash = 1.7f, Cost = 300 },
        };

        /// <summary>The armoury's picture and grip for a gun of this mode (sharing the hero's painted guns).</summary>
        static WeaponDef ArtOf(Gun g) => WeaponCatalog.Find(g.Art);

        // ---- gadgets ---------------------------------------------------------------------------------

        enum Gadget { Turret, Shield, Dodge }

        static readonly string[] GadgetNames = { "Tourelle", "Bouclier", "Esquive" };
        static readonly string[] GadgetBlurbs =
        {
            "Posée à tes pieds, elle tire seule",
            "Absorbe les coups, se recharge",
            "Un bond intouchable (bouton ESQUIVE)",
        };
        static readonly int[][] GadgetCosts = { new[] { 70, 90, 140 }, new[] { 60, 90, 130 }, new[] { 50, 80, 110 } };

        // ---- bonuses -----------------------------------------------------------------------------------

        enum Perk { Damage, Rate, Speed, Vitality, Heal, Scrap, Pierce, Multi, Range, Crit, Regen, Thorns, Doors, Vampire, Blast }

        static readonly (Perk perk, string name, string text, int max)[] Perks =
        {
            (Perk.Damage, "Balles lourdes", "+20 % de dégâts", 10),
            (Perk.Rate, "Gâchette folle", "+15 % de cadence", 8),
            (Perk.Speed, "Semelles de vent", "+10 % de vitesse", 5),
            (Perk.Vitality, "Carrure", "+20 PV max, et soigne 20", 8),
            (Perk.Heal, "Bouffée d'air", "Soigne 60 % des PV", 99),
            (Perk.Scrap, "Ferrailleur", "+25 % de ferraille", 6),
            (Perk.Pierce, "Perce-cœur", "Les balles traversent un mort de plus", 3),
            (Perk.Multi, "Double canon", "+1 balle par tir", 3),
            (Perk.Range, "Longue vue", "+15 % de portée", 4),
            (Perk.Crit, "Coup critique", "+10 % de chances de dégâts x2,5", 5),
            (Perk.Regen, "Second souffle", "Régénère 1 PV par seconde", 5),
            (Perk.Thorns, "Épines", "Qui te frappe prend 15 dégâts", 5),
            (Perk.Doors, "Portes blindées", "+40 % de solidité, portes réparées", 4),
            (Perk.Vampire, "Vampire", "Chaque mort tué rend 1 PV", 3),
            (Perk.Blast, "Éclats", "Les morts explosent sur leurs voisins", 3),
        };

        // ---- the dead ------------------------------------------------------------------------------------

        enum FoeKind { Walker, Heavy, Hound, Wisp, Brute, Boss }

        static readonly (string sheet, float height, float radius, float hp, float speed, float dmg, float doorDmg, int scrap, Color tint)[] FoeDefs =
        {
            ("walker", 1.2f, 0.32f, 30f, 1.7f, 8f, 6f, 1, Color.white),
            ("walker_b", 1.25f, 0.35f, 60f, 1.35f, 12f, 9f, 2, new Color(0.85f, 0.9f, 1f)),
            ("hound", 0.75f, 0.32f, 24f, 3.6f, 6f, 4f, 2, Color.white),
            ("wisp", 1.1f, 0.3f, 26f, 2.1f, 9f, 0f, 3, Color.white),
            ("brute", 1.75f, 0.55f, 170f, 1.15f, 25f, 26f, 6, Color.white),
            ("brute", 3.0f, 0.95f, 1100f, 1.0f, 35f, 120f, 60, new Color(1f, 0.62f, 0.55f)),
        };

        static readonly string[] BossNames = { "Le Colosse de la route", "La Bête du parking", "Le Gardien rouillé", "Le Roi des cendres" };

        // ---- events of the approaching planet --------------------------------------------------------------

        enum NightEvent { None, LowGravity, DebrisRain, Blackout }

        static NightEvent EventFor(int wave) => wave % 10 != 0 ? NightEvent.None : ((wave / 10 - 1) % 3) switch
        {
            0 => NightEvent.LowGravity,
            1 => NightEvent.DebrisRain,
            _ => NightEvent.Blackout,
        };

        static string EventTitle(NightEvent e) => e switch
        {
            NightEvent.LowGravity => "LA PLANÈTE TIRE",
            NightEvent.DebrisRain => "PLUIE DE DÉBRIS",
            NightEvent.Blackout => "COUPURE DE COURANT",
            _ => "",
        };

        static string EventText(NightEvent e) => e switch
        {
            NightEvent.LowGravity => "La gravité s'effondre : tout glisse et les balles ralentissent",
            NightEvent.DebrisRain => "Des fragments tombent du ciel : surveille les ombres au sol",
            NightEvent.Blackout => "Plus une lampe : tu ne vois qu'autour de toi",
            _ => "",
        };

        // ---- leaderboard -------------------------------------------------------------------------------------

        [Serializable]
        class BoardEntry { public int score, wave, kills; public string date; }

        [Serializable]
        class Board { public List<BoardEntry> entries = new(); }

        const string BoardKey = "nuit_board";

        static Board LoadBoard()
        {
            try
            {
                var json = PlayerPrefs.GetString(BoardKey, "");
                if (!string.IsNullOrEmpty(json)) return JsonUtility.FromJson<Board>(json) ?? new Board();
            }
            catch { }
            return new Board();
        }

        /// <summary>Adds a finished night to the top ten; returns its rank (1-based), 0 if it did not make it.</summary>
        static int Record(BoardEntry e)
        {
            var b = LoadBoard();
            b.entries.Add(e);
            b.entries.Sort((x, y) => y.score.CompareTo(x.score));
            int rank = b.entries.IndexOf(e) + 1;
            if (b.entries.Count > 10) b.entries.RemoveRange(10, b.entries.Count - 10);
            PlayerPrefs.SetString(BoardKey, JsonUtility.ToJson(b));
            PlayerPrefs.Save();
            return rank <= 10 ? rank : 0;
        }

        static int BestWave
        {
            get => PlayerPrefs.GetInt("nuit_best_wave", 0);
            set { PlayerPrefs.SetInt("nuit_best_wave", value); PlayerPrefs.Save(); }
        }
    }
}
