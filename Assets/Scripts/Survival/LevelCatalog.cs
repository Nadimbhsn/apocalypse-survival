using System.Collections.Generic;
using UnityEngine;

namespace Platformer.Survival
{
    /// <summary>
    /// One authored instruction in a campaign level. A level is just an ordered list of
    /// these, executed by SurvivalDirector.Campaign.cs against the very same terrain
    /// primitives the endless runner uses - so an authored level and a generated run are
    /// made of the same material, and a future in-game editor only has to produce this
    /// list.
    ///
    /// Horizontal distances are expressed in REACH UNITS, where 1.0 is exactly how far a
    /// full-speed jump carries the player (SurvivalDirector.RunReach). Authoring in reach
    /// units instead of meters means the layout stays equally jumpable whatever the Speed
    /// upgrade does to the character: a faster runner gets a proportionally longer level,
    /// not an easier or an impossible one.
    /// </summary>
    public enum Beat
    {
        /// <summary>Switch the sector theme (sky tint, ground color, decoration, banner).</summary>
        Zone,
        /// <summary>Solid ground. A = width in reach units, B = height change from the previous ground.</summary>
        Ground,
        /// <summary>A cracked slab that collapses shortly after being stood on. A = width.</summary>
        Slab,
        /// <summary>A hole to jump. A = width in reach units (capped at what a jump actually clears).</summary>
        Gap,
        /// <summary>A wide hole crossed in two jumps via a floating stone.</summary>
        StoneGap,
        /// <summary>Spikes on the ground just laid. A = width, B = position along that ground (0..1).</summary>
        Spikes,
        /// <summary>A toxic pool on the ground just laid. A = width, B = position along it (0..1).</summary>
        Toxic,
        /// <summary>A small platform floating above the ground just laid. A = width, B = height above it.</summary>
        Ledge,
        /// <summary>A bounce pad on the ground just laid. B = height above it, I = 1 for a strong spring.</summary>
        Spring,
        /// <summary>The Doodle-Jump climb: a column of bounce platforms up to a roof.</summary>
        Tower,
        /// <summary>The mirror of the tower: a free fall down a shaft past spike ledges.</summary>
        Shaft,
        /// <summary>A chain of drifting islets over the void. I = islet count.</summary>
        Archipel,
        /// <summary>Jetpack flight over a toxic lake. I = number of stretches.</summary>
        Jetpack,
        /// <summary>Starts the storm chasing the player; it dissipates at the next StormEnd.</summary>
        Storm,
        StormEnd,
        /// <summary>Enemies on the ground just laid. I = ZombieKind, A = count, B = position along it (0..1).</summary>
        Foes,
        /// <summary>A line of coins above the ground just laid. I = count, B = height.</summary>
        Coins,
        /// <summary>A single pickup on the ground just laid. I = PickupType, B = position along it (0..1).</summary>
        Item,
        /// <summary>A hidden stash. I = SecretKind. Builds its own piece of terrain.</summary>
        Secret,
        /// <summary>A banner the run comes back to after a death.</summary>
        Checkpoint,
        /// <summary>An on-screen hint when the player reaches this point. S = text.</summary>
        Sign,
        /// <summary>The gate that ends the level and opens the boss duel.</summary>
        Goal,
    }

    /// <summary>The three ways a secret is hidden. All three end in the same treasure room.</summary>
    public enum SecretKind
    {
        /// <summary>A narrow hole in the floor most players jump straight over.</summary>
        Pit,
        /// <summary>The same hole, but sealed by a cracked slab that has to be shot open.</summary>
        Sealed,
        /// <summary>A ledge high above the path, reached with the spring pad below it.</summary>
        Sky,
    }

    public struct LevelCmd
    {
        public Beat Beat;
        public float A, B;
        public int I;
        public string S;
    }

    public struct LevelDef
    {
        public string Id;
        public string Name;
        public string Subtitle;
        /// <summary>Sector the level opens in (each Zone command changes it afterwards).</summary>
        public ZoneKind Theme;
        /// <summary>Which ArenaCatalog boss waits at the gate.</summary>
        public int BossIndex;
        /// <summary>Enemy stat scaler, fixed for the whole level (the endless run derives it from distance).</summary>
        public float EnemyTier;
        /// <summary>Terrain harshness for the tower and the shaft, fixed for the whole level.</summary>
        public float Harshness;
        public LevelCmd[] Script;

        public int SecretCount
        {
            get
            {
                int n = 0;
                if (Script != null)
                    foreach (var c in Script) if (c.Beat == Beat.Secret) n++;
                return n;
            }
        }
    }

    /// <summary>
    /// The campaign: hand-authored levels with a start, a finish and a boss, as opposed to
    /// the endless run. Levels are written with the short helpers below so a script reads
    /// as the level itself - one line per feature the player will meet, in order.
    /// </summary>
    public static class LevelCatalog
    {
        // ---- authoring helpers ---------------------------------------------------------

        static LevelCmd Zone(ZoneKind kind) => new() { Beat = Beat.Zone, I = (int)kind };
        static LevelCmd Run(float widthR, float dy = 0f) => new() { Beat = Beat.Ground, A = widthR, B = dy };
        static LevelCmd Slab(float widthR = 0.7f) => new() { Beat = Beat.Slab, A = widthR };
        static LevelCmd Gap(float widthR = 0.6f) => new() { Beat = Beat.Gap, A = widthR };
        static LevelCmd StoneGap() => new() { Beat = Beat.StoneGap };
        static LevelCmd Spikes(float width = 1.1f, float at = 0.5f) => new() { Beat = Beat.Spikes, A = width, B = at };
        static LevelCmd Toxic(float width = 1.8f, float at = 0.5f) => new() { Beat = Beat.Toxic, A = width, B = at };
        static LevelCmd Ledge(float width = 2.2f, float height = 1.5f) => new() { Beat = Beat.Ledge, A = width, B = height };
        static LevelCmd Spring(float height = 0.35f, bool strong = false) => new() { Beat = Beat.Spring, B = height, I = strong ? 1 : 0 };
        static LevelCmd Tower() => new() { Beat = Beat.Tower };
        static LevelCmd Shaft() => new() { Beat = Beat.Shaft };
        static LevelCmd Archipel(int count = 7) => new() { Beat = Beat.Archipel, I = count };
        static LevelCmd Jetpack(int stretches = 4) => new() { Beat = Beat.Jetpack, I = stretches };
        static LevelCmd Storm() => new() { Beat = Beat.Storm };
        static LevelCmd StormEnd() => new() { Beat = Beat.StormEnd };
        static LevelCmd Foes(ZombieKind kind, int count = 1, float at = 0.5f) => new() { Beat = Beat.Foes, I = (int)kind, A = count, B = at };
        static LevelCmd Coins(int count = 3, float height = 0.8f) => new() { Beat = Beat.Coins, I = count, B = height };
        static LevelCmd Item(PickupType type, float at = 0.5f) => new() { Beat = Beat.Item, I = (int)type, B = at };
        static LevelCmd Secret(SecretKind kind) => new() { Beat = Beat.Secret, I = (int)kind };
        static LevelCmd Flag() => new() { Beat = Beat.Checkpoint };
        static LevelCmd Sign(string text) => new() { Beat = Beat.Sign, S = text };
        static LevelCmd Goal() => new() { Beat = Beat.Goal };

        // ---- the levels ----------------------------------------------------------------

        public static readonly LevelDef[] Levels =
        {
            // =========================================================================
            // 1. LES FAUBOURGS - the teaching level. Every mechanic appears once, in a
            // safe version, before it is ever combined with another one.
            // =========================================================================
            new LevelDef
            {
                Id = "faubourgs",
                Name = "LES FAUBOURGS",
                Subtitle = "Quitte la ville avant la nuit",
                Theme = ZoneKind.City,
                BossIndex = 0,
                EnemyTier = 1.6f,
                Harshness = 0.15f,
                Script = new[]
                {
                    Zone(ZoneKind.City),
                    Run(2.4f), Sign("Joystick pour courir, SAUT pour sauter"), Coins(3),
                    Run(1.6f), Item(PickupType.Coin),
                    Gap(0.5f),
                    Run(2.0f), Foes(ZombieKind.Walker),
                    Gap(0.55f),
                    Run(1.8f, 0.9f), Item(PickupType.Material),
                    Sign("Le bouton TIR sert aussi à ouvrir certains murs"),
                    Run(2.2f, -0.9f), Foes(ZombieKind.Walker, 2),
                    Flag(),

                    // First secret, deliberately obvious: a hole you can see coming.
                    Run(1.6f), Ledge(2.4f, 1.6f),
                    Secret(SecretKind.Pit),
                    Run(2.0f), Coins(4, 1.2f),
                    Gap(0.62f),
                    Run(1.4f), Spikes(1.0f, 0.5f),
                    Run(1.8f), Foes(ZombieKind.Runner),
                    StoneGap(),
                    Run(2.6f), Item(PickupType.Medkit), Foes(ZombieKind.Walker, 2),
                    Flag(),

                    // The islands: the first real test of timing.
                    Zone(ZoneKind.Highway),
                    Sign("Les îlots dérivent, prends ton temps"),
                    Run(1.8f),
                    Archipel(6),
                    Run(1.6f), Foes(ZombieKind.Walker),
                    Slab(0.7f),
                    Run(1.4f),
                    Slab(0.7f),
                    Run(2.2f), Coins(3), Item(PickupType.Material),
                    Gap(0.6f),
                    Run(2.0f), Foes(ZombieKind.Runner, 2),
                    Flag(),

                    // The cracked causeway: pure timing, nothing else going on.
                    Sign("Les dalles fissurées cèdent sous tes pieds"),
                    Run(2.0f), Coins(3),
                    Slab(0.7f), Run(1.0f), Slab(0.7f), Run(1.0f), Slab(0.7f),
                    Run(2.2f), Item(PickupType.Medkit),
                    Gap(0.62f),
                    Run(1.6f, 1.3f), Foes(ZombieKind.Spitter),
                    Run(1.8f, -1.3f), Coins(4),
                    StoneGap(),
                    Run(2.4f), Foes(ZombieKind.Walker, 2), Item(PickupType.Material),
                    Flag(),

                    // The climb, then the ramparts and the way back down.
                    Zone(ZoneKind.Ascent),
                    Sign("Rebondis de plateforme en plateforme"),
                    Run(1.2f),
                    Tower(),
                    Zone(ZoneKind.Rooftops),
                    Run(1.8f), Foes(ZombieKind.Spitter),
                    Gap(0.6f),
                    Run(1.6f), Ledge(2.0f, 1.6f),
                    Secret(SecretKind.Sky),
                    Run(1.8f, -1.2f), Foes(ZombieKind.Walker),
                    Gap(0.58f),
                    Run(2.0f, -1.4f), Coins(3),
                    Run(2.2f, -1.6f), Foes(ZombieKind.Runner),
                    Run(2.4f, -1.8f), Item(PickupType.Medkit),
                    Flag(),

                    // The run to the gate, with the storm on the player's heels.
                    Zone(ZoneKind.Storm),
                    Sign("La déferlante arrive, ne t'arrête plus !"),
                    Storm(),
                    Run(2.6f), Coins(4),
                    Gap(0.55f),
                    Run(2.8f), Coins(4),
                    Gap(0.58f),
                    Run(3.0f), Coins(4), Item(PickupType.Material),
                    StormEnd(),
                    Run(2.4f), Item(PickupType.Medkit),
                    Goal(),
                },
            },

            // =========================================================================
            // 2. LE CIMETIERE OUBLIE - the dead and the drop. Tighter ground, poison,
            // a free-fall shaft and the first sealed secret.
            // =========================================================================
            new LevelDef
            {
                Id = "cimetiere",
                Name = "LE CIMETIÈRE OUBLIÉ",
                Subtitle = "Traverse la nécropole sans t'y perdre",
                Theme = ZoneKind.Infested,
                BossIndex = 1,
                EnemyTier = 2.2f,
                Harshness = 0.4f,
                Script = new[]
                {
                    Zone(ZoneKind.Infested),
                    Run(2.2f), Sign("Ici ne marchent que les morts"), Coins(3), Foes(ZombieKind.Walker),
                    Gap(0.6f),
                    Run(1.6f), Foes(ZombieKind.Spitter),
                    Run(1.8f, 1.0f), Item(PickupType.Material),
                    Gap(0.62f),
                    Run(2.4f), Foes(ZombieKind.Walker, 3, 0.6f),
                    Flag(),

                    Sign("Un sol fissuré se tire dessus"),
                    Run(1.4f),
                    Secret(SecretKind.Sealed),
                    Run(1.8f), Coins(3), Foes(ZombieKind.Spitter),
                    Slab(0.65f),
                    Run(1.2f),
                    Slab(0.65f),
                    Run(2.0f), Item(PickupType.Medkit),
                    StoneGap(),
                    Run(1.6f), Spikes(1.2f, 0.45f),
                    Run(1.8f), Foes(ZombieKind.Walker, 2),
                    Flag(),

                    // Toxic ground: the terres brûlées bite into the cemetery.
                    Zone(ZoneKind.Wasteland),
                    Sign("Ne pose pas le pied dans les flaques"),
                    Run(2.4f), Toxic(2.0f, 0.4f), Coins(3, 1.4f),
                    Gap(0.6f),
                    Run(2.2f), Toxic(1.8f, 0.55f), Foes(ZombieKind.Spitter),
                    Run(1.6f, 1.2f), Item(PickupType.Material),
                    Gap(0.64f),
                    Run(2.6f), Toxic(1.6f, 0.3f), Toxic(1.6f, 0.75f),
                    Run(1.8f), Foes(ZombieKind.Runner, 2),
                    Flag(),

                    // The drop.
                    Zone(ZoneKind.Ascent),
                    Run(1.2f),
                    Tower(),
                    Zone(ZoneKind.Rooftops),
                    Run(1.6f), Foes(ZombieKind.Spitter, 2),
                    Gap(0.6f),
                    Run(1.4f), Ledge(2.0f, 1.7f),
                    Secret(SecretKind.Sky),
                    Run(2.0f), Coins(4), Item(PickupType.Medkit),
                    Sign("En bas ! Vise le couloir entre les piques"),
                    Shaft(),
                    Zone(ZoneKind.Infested),
                    Run(1.8f), Foes(ZombieKind.Walker, 2),
                    Flag(),

                    Secret(SecretKind.Pit),
                    Run(2.0f), Coins(3),
                    Archipel(7),
                    Run(1.8f), Foes(ZombieKind.Spitter),
                    Gap(0.62f),
                    Run(2.2f), Foes(ZombieKind.Walker, 3, 0.65f), Item(PickupType.Medkit),
                    Run(2.0f),
                    Goal(),
                },
            },

            // =========================================================================
            // 3. LA CITADELLE - the long one. Everything the game has, combined: flight,
            // brutes, collapsing ramparts, the storm, three secrets.
            // =========================================================================
            new LevelDef
            {
                Id = "citadelle",
                Name = "LA CITADELLE",
                Subtitle = "Monte jusqu'au sommet du château",
                Theme = ZoneKind.Highway,
                BossIndex = 2,
                EnemyTier = 3.0f,
                Harshness = 0.7f,
                Script = new[]
                {
                    Zone(ZoneKind.Highway),
                    Run(2.0f), Sign("Le pont brisé mène à la citadelle"), Coins(3), Foes(ZombieKind.Runner),
                    Gap(0.64f),
                    Run(1.4f),
                    Slab(0.7f),
                    Run(1.2f),
                    Slab(0.7f),
                    Run(1.8f), Foes(ZombieKind.Walker, 2), Item(PickupType.Material),
                    StoneGap(),
                    Run(2.2f), Foes(ZombieKind.Brute),
                    Flag(),

                    Run(1.4f), Ledge(2.2f, 1.6f),
                    Secret(SecretKind.Sealed),
                    Run(1.8f), Coins(4, 1.3f),
                    Gap(0.66f),
                    Run(1.6f), Spikes(1.2f, 0.4f), Foes(ZombieKind.Spitter),
                    Gap(0.62f),
                    Run(2.0f), Item(PickupType.Medkit), Foes(ZombieKind.Runner, 2),
                    Flag(),

                    // Over the moat, on the jetpack.
                    Zone(ZoneKind.Jetpack),
                    Sign("Le jetpack décolle tout seul, dirige-le"),
                    Jetpack(5),
                    Zone(ZoneKind.Wasteland),
                    Run(2.0f), Item(PickupType.Medkit), Foes(ZombieKind.Spitter, 2),
                    Toxic(1.8f, 0.8f),
                    Run(1.6f, 1.2f), Coins(3),
                    Gap(0.64f),
                    Run(2.2f), Foes(ZombieKind.Brute), Item(PickupType.Material),
                    Flag(),

                    // The crypt under the keep: the dead, the dark and a hole in the floor.
                    Zone(ZoneKind.Infested),
                    Sign("La crypte du donjon"),
                    Run(1.8f), Foes(ZombieKind.Spitter, 2),
                    Secret(SecretKind.Pit),
                    Run(2.0f), Coins(4),
                    Slab(0.65f), Run(1.0f), Slab(0.65f),
                    Run(2.2f), Foes(ZombieKind.Walker, 3, 0.6f), Item(PickupType.Medkit),
                    Gap(0.66f),
                    Run(1.6f), Spikes(1.2f, 0.45f),
                    Run(2.0f), Foes(ZombieKind.Spitter, 2),
                    StoneGap(),
                    Run(2.4f), Coins(4), Item(PickupType.Material),
                    Flag(),

                    // The climb up the keep.
                    Zone(ZoneKind.Ascent),
                    Run(1.2f),
                    Tower(),
                    Zone(ZoneKind.Rooftops),
                    Sign("Les remparts s'effondrent, ne traîne pas"),
                    Run(1.6f), Foes(ZombieKind.Spitter, 2),
                    Slab(0.65f),
                    Run(1.2f),
                    Slab(0.65f),
                    Run(1.8f), Ledge(2.0f, 1.7f),
                    Secret(SecretKind.Sky),
                    Gap(0.66f),
                    Run(1.6f), Foes(ZombieKind.Runner, 2),
                    Run(2.0f), Item(PickupType.Medkit), Coins(4),
                    Flag(),

                    Archipel(9),
                    Run(1.6f), Foes(ZombieKind.Brute),
                    Shaft(),
                    Zone(ZoneKind.Storm),
                    Sign("Dernière ligne droite, elle arrive !"),
                    Storm(),
                    Run(2.8f), Coins(4),
                    Gap(0.6f),
                    Run(2.6f), Coins(4), Foes(ZombieKind.Runner),
                    Gap(0.62f),
                    Run(3.0f), Coins(5), Item(PickupType.Material),
                    StormEnd(),
                    Run(2.4f), Item(PickupType.Medkit),
                    Goal(),
                },
            },
        };

        public static int Count => Levels.Length;

        public static LevelDef Get(int index) => Levels[Mathf.Clamp(index, 0, Levels.Length - 1)];

        /// <summary>A level is playable once the one before it has been finished.</summary>
        public static bool IsUnlocked(int index) => index <= 0 || SaveSystem.IsLevelCleared(index - 1);
    }
}
