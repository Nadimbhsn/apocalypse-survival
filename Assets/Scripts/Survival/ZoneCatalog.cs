using UnityEngine;

namespace Platformer.Survival
{
    /// <summary>
    /// The distinct "sectors" a run cycles through. Each one changes how terrain is
    /// generated (gaps, height, hazards, unstable slabs), what spawns, and the palette,
    /// so the endless runner reads as a sequence of clearly different places instead of
    /// one flat strip. Ascent is the Doodle-Jump-style vertical climb, always followed by
    /// Rooftops (running along the top) and Descent (a staircase back down).
    /// </summary>
    public enum ZoneKind { City, Highway, Infested, Wasteland, Ascent, Rooftops, Descent, Shaft, Jetpack, Archipel, Storm }

    public struct ZoneDef
    {
        public ZoneKind Kind;
        public string Title;
        public string Subtitle;
        /// <summary>Multiplier on the painted sky (dusk purple, golden haze...).</summary>
        public Color Tint;
        /// <summary>Average sky color after the tint: what the 3D props fog toward.</summary>
        public Color Sky;
        public Color Ground;

        /// <summary>Chance that the next segment is a gap (never two in a row).</summary>
        public float GapChance;
        public float GapMin, GapMax;
        /// <summary>Chance a gap becomes a wide one bridged by a floating stepping stone.</summary>
        public float SteppingStoneChance;

        public float HeightChance;
        public float MaxStepUp, MaxDrop;
        public float SegMin, SegMax;

        public float HazardChance;
        public bool Spikes, Toxic;
        public float BonusPlatformChance;
        /// <summary>Fraction of short solid segments that are unstable slabs collapsing when stood on.</summary>
        public float UnstableChance;

        public float ZombieIntervalMult;
        public float PackChance;
        public bool AllowBrute;

        public float RuinSpacingMin, RuinSpacingMax;
        public float LengthMin, LengthMax;
    }

    public static class ZoneCatalog
    {
        public static ZoneDef Get(ZoneKind kind)
        {
            var def = Raw(kind);
            if (def.Tint.a <= 0f) def.Tint = Color.white;
            def.Sky = ApogeeTheme.SkyAverage * def.Tint;
            def.Sky.a = 1f;
            return def;
        }

        static ZoneDef Raw(ZoneKind kind)
        {
            switch (kind)
            {
                case ZoneKind.Highway:
                    return new ZoneDef
                    {
                        Kind = kind, Title = "LE PONT BRISÉ", Subtitle = "Enchaîne les sauts, méfie-toi des dalles fissurées",
                        Tint = new Color(0.96f, 0.92f, 0.92f), Ground = new Color(0.34f, 0.25f, 0.24f),
                        GapChance = 0.42f, GapMin = 2.0f, GapMax = 2.8f, SteppingStoneChance = 0.35f,
                        HeightChance = 0.35f, MaxStepUp = 1.2f, MaxDrop = 3.0f, SegMin = 3f, SegMax = 6f,
                        HazardChance = 0.06f, Spikes = true, Toxic = false, BonusPlatformChance = 0.25f, UnstableChance = 0.35f,
                        ZombieIntervalMult = 1.6f, PackChance = 0.1f, AllowBrute = false,
                        RuinSpacingMin = 14f, RuinSpacingMax = 26f, LengthMin = 50f, LengthMax = 75f,
                    };

                case ZoneKind.Infested:
                    return new ZoneDef
                    {
                        Kind = kind, Title = "CIMETIÈRE INFESTÉ", Subtitle = "Les morts sortent de terre !",
                        Tint = new Color(0.78f, 0.68f, 0.92f), Ground = new Color(0.27f, 0.17f, 0.22f),
                        GapChance = 0.06f, GapMin = 1.6f, GapMax = 2.2f, SteppingStoneChance = 0f,
                        HeightChance = 0.15f, MaxStepUp = 1.2f, MaxDrop = 2.0f, SegMin = 7f, SegMax = 12f,
                        HazardChance = 0.04f, Spikes = true, Toxic = false, BonusPlatformChance = 0.15f, UnstableChance = 0f,
                        ZombieIntervalMult = 1.15f, PackChance = 0.2f, AllowBrute = false,
                        RuinSpacingMin = 8f, RuinSpacingMax = 16f, LengthMin = 45f, LengthMax = 70f,
                    };

                case ZoneKind.Wasteland:
                    return new ZoneDef
                    {
                        Kind = kind, Title = "TERRES BRÛLÉES", Subtitle = "Ne touche pas les flaques toxiques",
                        Tint = new Color(1.02f, 0.92f, 0.72f), Ground = new Color(0.40f, 0.26f, 0.14f),
                        GapChance = 0.18f, GapMin = 1.8f, GapMax = 2.6f, SteppingStoneChance = 0.12f,
                        HeightChance = 0.30f, MaxStepUp = 1.5f, MaxDrop = 2.5f, SegMin = 4f, SegMax = 8f,
                        HazardChance = 0.50f, Spikes = true, Toxic = true, BonusPlatformChance = 0.24f, UnstableChance = 0f,
                        ZombieIntervalMult = 1.2f, PackChance = 0.15f, AllowBrute = false,
                        RuinSpacingMin = 12f, RuinSpacingMax = 24f, LengthMin = 45f, LengthMax = 70f,
                    };

                case ZoneKind.Ascent:
                    return new ZoneDef
                    {
                        Kind = kind, Title = "ASCENSION", Subtitle = "Grimpe la tour du château de plateforme en plateforme !",
                        Tint = new Color(0.92f, 0.84f, 0.96f), Ground = new Color(0.40f, 0.27f, 0.24f),
                        ZombieIntervalMult = 2f, RuinSpacingMin = 10f, RuinSpacingMax = 20f,
                    };

                case ZoneKind.Rooftops:
                    return new ZoneDef
                    {
                        Kind = kind, Title = "LES REMPARTS", Subtitle = "Cours sur les murailles du château",
                        Tint = new Color(0.94f, 0.86f, 0.94f), Ground = new Color(0.42f, 0.29f, 0.25f),
                        GapChance = 0.32f, GapMin = 1.6f, GapMax = 2.6f, SteppingStoneChance = 0.15f,
                        HeightChance = 0.50f, MaxStepUp = 1.5f, MaxDrop = 3.0f, SegMin = 3f, SegMax = 6f,
                        HazardChance = 0.12f, Spikes = true, Toxic = false, BonusPlatformChance = 0.2f, UnstableChance = 0.1f,
                        ZombieIntervalMult = 1.3f, PackChance = 0.15f, AllowBrute = false,
                        RuinSpacingMin = 6f, RuinSpacingMax = 12f, LengthMin = 40f, LengthMax = 60f,
                    };

                case ZoneKind.Storm:
                    return new ZoneDef
                    {
                        Kind = kind, Title = "LA DÉFERLANTE", Subtitle = "Cours ! La tempête arrive derrière toi",
                        Tint = new Color(0.72f, 0.58f, 0.78f), Ground = new Color(0.33f, 0.20f, 0.20f),
                        // Wide, flowing ground: the sector is about speed, not precision.
                        GapChance = 0.12f, GapMin = 1.6f, GapMax = 2.2f, SteppingStoneChance = 0f,
                        HeightChance = 0.18f, MaxStepUp = 1.0f, MaxDrop = 1.8f, SegMin = 7f, SegMax = 11f,
                        HazardChance = 0.05f, Spikes = true, Toxic = false, BonusPlatformChance = 0.12f, UnstableChance = 0f,
                        ZombieIntervalMult = 2.4f, PackChance = 0f, AllowBrute = false,
                        RuinSpacingMin = 9f, RuinSpacingMax = 18f, LengthMin = 75f, LengthMax = 105f,
                    };

                case ZoneKind.Archipel:
                    return new ZoneDef
                    {
                        Kind = kind, Title = "ARCHIPEL", Subtitle = "Les îlots dérivent : vise bien tes sauts !",
                        Tint = new Color(1.0f, 0.94f, 0.92f), Ground = new Color(0.38f, 0.24f, 0.20f),
                        ZombieIntervalMult = 3f, RuinSpacingMin = 10f, RuinSpacingMax = 20f,
                    };

                case ZoneKind.Shaft:
                    return new ZoneDef
                    {
                        Kind = kind, Title = "CHUTE LIBRE", Subtitle = "Dirige-toi pendant la chute, évite les pics !",
                        Tint = new Color(0.80f, 0.70f, 0.86f), Ground = new Color(0.36f, 0.24f, 0.22f),
                        ZombieIntervalMult = 2f, RuinSpacingMin = 10f, RuinSpacingMax = 20f,
                    };

                case ZoneKind.Jetpack:
                    return new ZoneDef
                    {
                        Kind = kind, Title = "SURVOL", Subtitle = "Maintiens SAUT pour voler, évite débris et câbles",
                        Tint = new Color(0.92f, 0.96f, 1.04f), Ground = new Color(0.34f, 0.24f, 0.22f),
                        GapChance = 0f, HeightChance = 0f, SegMin = 5f, SegMax = 7f,
                        ZombieIntervalMult = 3f, PackChance = 0f, AllowBrute = false,
                        RuinSpacingMin = 7f, RuinSpacingMax = 13f, LengthMin = 45f, LengthMax = 70f,
                    };

                case ZoneKind.Descent:
                    return new ZoneDef
                    {
                        Kind = kind, Title = "DESCENTE", Subtitle = "Retour au sol",
                        Tint = new Color(0.97f, 0.9f, 0.9f), Ground = new Color(0.40f, 0.27f, 0.23f),
                        GapChance = 0f, HeightChance = 1f, MaxDrop = 3.5f, SegMin = 3.5f, SegMax = 5f,
                        ZombieIntervalMult = 1.6f, PackChance = 0f, AllowBrute = false,
                        RuinSpacingMin = 10f, RuinSpacingMax = 20f,
                    };

                default: // City
                    return new ZoneDef
                    {
                        Kind = ZoneKind.City, Title = "RUINES CÉLESTES", Subtitle = "Les îles de la cité perdue",
                        Tint = Color.white, Ground = PlaceholderVisuals.GroundColor,
                        GapChance = 0.22f, GapMin = 1.6f, GapMax = 2.6f, SteppingStoneChance = 0.14f,
                        HeightChance = 0.25f, MaxStepUp = 1.5f, MaxDrop = 2.0f, SegMin = 4f, SegMax = 9f,
                        HazardChance = 0.10f, Spikes = true, Toxic = false, BonusPlatformChance = 0.26f, UnstableChance = 0.10f,
                        ZombieIntervalMult = 1f, PackChance = 0.2f, AllowBrute = true,
                        RuinSpacingMin = 10f, RuinSpacingMax = 22f, LengthMin = 45f, LengthMax = 70f,
                    };
            }
        }
    }
}
