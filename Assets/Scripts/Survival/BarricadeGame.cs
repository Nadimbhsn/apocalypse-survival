using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Random = UnityEngine.Random;

namespace Platformer.Survival
{
    /// <summary>
    /// "BARRICADE": hold the palisade at the foot of cobbled lanes that come down out of a
    /// misty wood, wave after wave, for as long as it stands - then spend what the run
    /// earned and go again, a little stronger, a little further.
    ///
    /// Within a run (easy to read, hard to put down):
    ///  - Hold a lane to shoot up it. Kills in quick succession make a streak that pays extra.
    ///  - Each lane has ONE defence, built and upgraded between waves by tapping the lane:
    ///    Pieux (wound and slow), Brasier (sets alight) or Arbalète (shoots up the lane by
    ///    itself, hits the floaters), level 1 to 5. Defences belong to the run.
    ///  - Every third wave held, a Blessing: one of three gifts for this run only
    ///    (piercing shots, twin shots, hungrier fire, a vampiric palisade...).
    ///  - Every fifth wave a Colosse leads the charge and drops a chest of Éclats.
    ///  - The Molotov sets the busiest lane on fire; the Volley (unlocked at the Forge)
    ///    rains arrows on every lane.
    ///
    /// Between runs (see BarricadeGame.Meta): Éclats buy permanent upgrades at the Forge,
    /// holding wave 15 opens the next of five lands (more lanes, new dead, better pay), and
    /// the Mill makes materials, even with the game closed. The dead grow tougher every wave
    /// past the tenth, so every run ends - and the next one goes further.
    ///
    /// The field lives far from the runner (around x = 3000, y = 1500), painted
    /// (Resources/Barricade), and borrows the main camera while open.
    /// </summary>
    public partial class BarricadeGame : MiniGame
    {
        public override string Id => "barricade";
        public override string Title => "BARRICADE";
        public override string Description => "Tiens la palissade, forge ta puissance, conquiers les cinq terres";

        public override string BestLine
        {
            get
            {
                int land = BarrSave.Unlocked;
                int best = BarrSave.Best(land);
                string line = best > 0 ? $"{Lands[land].name} : vague {best}" : "Aucune vague tenue";
                int stock = Mathf.FloorToInt(BarrSave.MillStock);
                return stock > 0 ? $"{line}   ·   {stock} [g] au moulin" : line;
            }
        }

        // ---- layout (world units, relative to Origin) ------------------------------------
        static readonly Vector2 Origin = new Vector2(3000f, 1500f);
        const int MaxLanes = 5;
        const float CamY = 0.9f, CameraOrtho = 5.6f;
        const float DefY = -1.7f, ZoneUp = 0.75f, ZoneDown = 0.45f;
        const float BarricadeY = -3.3f, PlayerY = -4.55f, FolletStopY = 1.4f, NecroStopY = 2.6f;
        const float Ppu = 1024f / 6f;

        int land;
        int laneCount = 3;
        float spacing = 1.5f;
        float spawnY = 5f;
        float LaneX(int l) => Origin.x + (l - (laneCount - 1) * 0.5f) * spacing;
        float FieldWidth => spacing * laneCount;
        float LaneScale => Mathf.Min(1f, spacing / 1.5f);
        static float SpacingFor(int lanes) => lanes >= 5 ? 1.22f : lanes == 4 ? 1.4f : 1.55f;

        enum Foe { Rodeur, Furtif, Follet, Colosse, Saboteur, Necro, Shield }
        enum Def { None, Pieux, Brasier, Arbalete }
        enum Phase { Menu, Build, Wave, Perk, Over }

        class Enemy
        {
            public Foe kind;
            public int lane;
            public float y, x, speed, hp, maxHp, dmg, interval, attackTimer;
            public float burn, burnDps, burnTick, trapTick, anim, size, slowUntil;
            public float shield, summonTimer;
            public int debris, summons;
            public bool boss, smashed;
            public GameObject go;
            public SpriteRenderer sr;
            public Sprite[] frames;
            public Color tint = Color.white;
            public Transform hpFill;
            public GameObject hpBar, shieldBadge;
            public float flash;
            public bool Flies => kind == Foe.Follet || kind == Foe.Necro;
        }

        class Shot
        {
            public int lane, pierce;
            public float y, speed, damage;
            public bool fromHero;
            public GameObject go;
            public readonly List<Enemy> hit = new();
        }

        /// <summary>A lane holds all three defences at once, each at its own level (0 = not built).</summary>
        class Lane
        {
            public readonly int[] level = new int[4];
            public readonly bool[] disabled = new bool[4];
            public float turretTimer;
            public GameObject visual;
            public readonly SpriteRenderer[] defSr = new SpriteRenderer[4], ringSr = new SpriteRenderer[4];
            public readonly List<SpriteRenderer>[] pips = { new(), new(), new(), new() };
            public bool Has(Def d) => level[(int)d] > 0;
            public bool Active(Def d) => level[(int)d] > 0 && !disabled[(int)d];
            public int Level(Def d) => level[(int)d];
        }

        /// <summary>Where each defence stands in its lane: the fire at the top, the stakes low, the ballista to the side.</summary>
        static float SlotY(Def d) => d switch { Def.Brasier => 0.6f, Def.Arbalete => -0.55f, _ => DefY };
        float SlotX(Def d) => d == Def.Arbalete ? spacing * 0.3f : 0f;

        // ---- tuning --------------------------------------------------------------------
        const int MaxLevel = 5;
        static readonly string[] DefNames = { "", "Pieux", "Brasier", "Arbalète" };
        static readonly string[] DefArt = { "", "pieux", "brasier", "arbalete" };
        static readonly string[] DefBlurb =
        {
            "",
            "Blessent et ralentissent ce qui marche dessus",
            "Enflamme les morts qui passent",
            "Tire seule dans le couloir, touche les volants",
        };
        static readonly int[] DefBaseCost = { 0, 10, 14, 18 };
        static readonly int[] LevelCostMultiplier = { 1, 2, 3, 6, 10 };
        const float FireRate = 3.2f, BurstMultiplier = 3f, ShotSpeed = 16f;
        const int BurstWaves = 3;
        const int BossEvery = 5;
        const float StreakWindow = 1.6f;

        // Forge and blessings, read live.
        float ShotDamage => Mathf.Max(1f, UpgradeManager.FirePowerMultiplier) * ForgeBonus(Forge.Damage, 0.1f);
        float HeroRate => FireRate * ForgeBonus(Forge.Rate, 0.05f) * (1f + 0.2f * Perks(Perk.Frenzy)) * (burstWaves > 0 ? BurstMultiplier : 1f);
        float CritChance => 0.02f * ForgeLevel(Forge.Crit) + 0.1f * Perks(Perk.Hunter);
        float LootFactor => ForgeBonus(Forge.Loot, 0.06f) * (1f + 0.3f * Perks(Perk.Gold));
        float MolotovCooldownMax => Mathf.Max(6f, 18f * (1f - 0.04f * ForgeLevel(Forge.Molotov)));
        bool VolleyUnlocked => ForgeLevel(Forge.Volley) > 0;
        float VolleyCooldownMax => Mathf.Max(10f, 30f * (1f - 0.035f * (ForgeLevel(Forge.Volley) - 1)));
        float BurnFactor => 1f + 0.5f * Perks(Perk.Inferno);

        /// <summary>
        /// Health of the dead: the land's own toughness, and past wave 10 a compounding climb
        /// that ends every run sooner or later - however strong the Forge has made you.
        /// </summary>
        float HealthScale => Lands[land].hp * (wave > 10 ? Mathf.Pow(1.045f, wave - 10) : 1f);

        // ---- state ---------------------------------------------------------------------
        Transform root;
        readonly List<Enemy> enemies = new();
        readonly List<Shot> shots = new();
        readonly List<GameObject> spits = new();
        readonly Lane[] lanes = new Lane[MaxLanes];
        readonly float[] laneFireCooldown = new float[MaxLanes];
        readonly HashSet<Foe> introduced = new();
        LaneTouchZone touchZone;

        Phase phase = Phase.Menu;
        int wave, spawnRemaining, debris, kills, bossesKilled, streak, bestStreak;
        float spawnTimer, spawnInterval, lastKillTime;
        float barricadeHp, barricadeMax;
        float molotovCooldown, volleyCooldown;
        /// <summary>Éclats are paid as the run goes (waves held, kills, chests), not at the end: quitting loses none.</summary>
        float shardCarry;
        int shardsThisRun;
        int burstWaves;
        bool adPending, bossWave, bossSpawned;
        int selectedLane;
        SpriteRenderer palisadeSr, heroSr;
        int palisadeState = -1;
        GameObject laneHighlight;
        Drone drone;
        float hudTimer;

        // ---- UI ----------------------------------------------------------------------------
        RectTransform uiRoot;
        GameObject gameUi;
        Text waveText, landText, messageText, hpLabel, sheetTitle, sheetInfo, molotovTimer, volleyTimer, streakText;
        IconText debrisChip, shardChip;
        Image barricadeFill, molotovFill, volleyFill;
        Button launchButton, molotovButton, volleyButton, repairButton, burstButton;
        IconText repairLabel, burstLabel;
        GameObject sheet, hintRoot;
        readonly (Button button, Text name, IconText cost, Image icon, Text level)[] choice = new (Button, Text, IconText, Image, Text)[3];

        static readonly Dictionary<string, Sprite> art = new();
        static readonly Dictionary<string, Sprite[]> sheets = new();

        static Sprite Art(string name, Vector2 pivot, float ppu = Ppu)
        {
            string key = $"{name}|{pivot}|{ppu}";
            if (art.TryGetValue(key, out var s)) return s;
            var tex = Resources.Load<Texture2D>("Barricade/" + name);
            if (tex == null) tex = Resources.Load<Texture2D>("Runner/" + name);
            if (tex == null) tex = Resources.Load<Texture2D>("Bastion/" + name);
            if (tex != null) s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), pivot, ppu, 0, SpriteMeshType.FullRect);
            art[key] = s;
            return s;
        }

        /// <summary>The eight walking frames of one of the dead, feet on the pivot.</summary>
        static Sprite[] Frames(Foe kind)
        {
            string name = kind switch
            {
                Foe.Furtif or Foe.Saboteur => "foe_furtif",
                Foe.Follet or Foe.Necro => "foe_follet",
                Foe.Colosse => "foe_colosse",
                _ => "foe_rodeur",
            };
            if (sheets.TryGetValue(name, out var f)) return f;
            var tex = Resources.Load<Texture2D>("Barricade/" + name);
            if (tex == null) { sheets[name] = null; return null; }
            int fw = tex.width / 4, fh = tex.height / 2;
            f = new Sprite[8];
            for (int i = 0; i < 8; i++)
                f[i] = Sprite.Create(tex, new Rect((i % 4) * fw, tex.height - (i / 4 + 1) * fh, fw, fh), new Vector2(0.5f, 0.04f), Ppu, 0, SpriteMeshType.Tight);
            sheets[name] = f;
            return f;
        }

        protected override void BuildUi()
        {
            for (int i = 0; i < MaxLanes; i++) lanes[i] = new Lane();

            uiRoot = UiKit.CreateRect("BarricadePanel", ui.Canvas.transform, Vector2.zero, Vector2.one);
            panel = uiRoot.gameObject;

            var g = UiKit.CreateRect("GameUi", uiRoot, Vector2.zero, Vector2.one);
            gameUi = g.gameObject;

            // One touch zone over the field: during a wave the finger's lane shoots, between
            // waves it picks the lane to build in.
            var zone = UiKit.CreateRect("LaneTouchZone", g, new Vector2(0f, 0.11f), new Vector2(1f, 0.86f));
            zone.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0f);
            touchZone = zone.gameObject.AddComponent<LaneTouchZone>();
            touchZone.LaneAt = LaneFromScreen;

            BuildTop(g);
            BuildSheet(g);
            BuildWaveControls(g);
            BuildScreens(uiRoot);

            MillCatchUp();
        }

        void BuildTop(RectTransform rt)
        {
            var top = UiKit.CreateRect("Top", rt, new Vector2(0f, 0.925f), new Vector2(1f, 1f));
            var shade = UiKit.CreateImage("Shade", top, Vector2.zero, Vector2.one, ApogeeTheme.VerticalFade, new Color(0.1f, 0.03f, 0.04f, 0.85f), false);
            shade.rectTransform.localScale = new Vector3(1f, -1f, 1f);
            shade.raycastTarget = false;
            waveText = UiKit.Outlined(UiKit.CreateText("Wave", top, "", 44, TextAnchor.MiddleLeft, new Vector2(0.04f, 0.35f), new Vector2(0.7f, 1f), ApogeeTheme.Gold), 2.5f);
            UiKit.FitLabel(waveText, 44);
            landText = UiKit.Outlined(UiKit.CreateText("Land", top, "", 22, TextAnchor.MiddleLeft, new Vector2(0.04f, 0.0f), new Vector2(0.7f, 0.38f), ApogeeTheme.Cream), 1.5f);
            ui.CreatePauseButton(top, new Vector2(0.74f, 0.2f), new Vector2(0.96f, 0.85f), () => { if (phase == Phase.Build || phase == Phase.Wave) OpenPause(); });

            debrisChip = Pill(rt, "Debris", new Vector2(0.03f, 0.875f), new Vector2(0.48f, 0.92f), 28);
            shardChip = Pill(rt, "Shards", new Vector2(0.52f, 0.875f), new Vector2(0.97f, 0.92f), 26);

            var bar = UiKit.CreateRect("PalisadeBar", rt, new Vector2(0.03f, 0.845f), new Vector2(0.97f, 0.868f));
            var bg = bar.gameObject.AddComponent<Image>();
            bg.sprite = HubArt.Get("ui_pill", 30f) ?? ApogeeTheme.Chip;
            bg.type = Image.Type.Sliced;
            bg.raycastTarget = false;
            var fillRt = UiKit.CreateRect("Fill", bar, Vector2.zero, Vector2.one);
            fillRt.offsetMin = new Vector2(6, 5);
            fillRt.offsetMax = new Vector2(-6, -5);
            barricadeFill = fillRt.gameObject.AddComponent<Image>();
            barricadeFill.sprite = ApogeeTheme.FrameFill;
            barricadeFill.type = Image.Type.Filled;
            barricadeFill.fillMethod = Image.FillMethod.Horizontal;
            barricadeFill.color = new Color(0.86f, 0.55f, 0.22f);
            barricadeFill.raycastTarget = false;
            hpLabel = UiKit.Outlined(UiKit.CreateText("Label", bar, "PALISSADE", 18, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, ApogeeTheme.Cream), 1.2f);

            messageText = UiKit.Outlined(UiKit.CreateText("Message", rt, "", 30, TextAnchor.MiddleCenter, new Vector2(0.04f, 0.78f), new Vector2(0.96f, 0.835f), ApogeeTheme.Cream), 2f);
            UiKit.FitLabel(messageText, 30);
            streakText = UiKit.Outlined(UiKit.CreateText("Streak", rt, "", 34, TextAnchor.MiddleRight, new Vector2(0.4f, 0.74f), new Vector2(0.96f, 0.785f), ApogeeTheme.Gold), 2f);
        }

        static IconText Pill(RectTransform parent, string name, Vector2 min, Vector2 max, int size)
        {
            var chip = UiKit.CreateRect(name, parent, min, max);
            var img = chip.gameObject.AddComponent<Image>();
            img.sprite = HubArt.Get("ui_pill", 30f) ?? ApogeeTheme.Chip;
            img.type = Image.Type.Sliced;
            img.raycastTarget = false;
            var label = IconText.Create(name + "_Text", chip, "", size, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, ApogeeTheme.Cream, 1.2f);
            var lrt = (RectTransform)label.transform;
            lrt.offsetMin = new Vector2(12, 4);
            lrt.offsetMax = new Vector2(-12, -4);
            return label;
        }

        /// <summary>
        /// The workshop, between waves, over the empty top of the field: the chosen lane, its
        /// three possible defences as picture cards (or its defence with upgrade and sell),
        /// and two other actions. The big button to start sits at the bottom.
        /// </summary>
        void BuildSheet(RectTransform rt)
        {
            var sh = UiKit.CreateRect("Workshop", rt, new Vector2(0.02f, 0.5f), new Vector2(0.98f, 0.835f));
            sheet = sh.gameObject;
            var img = sh.gameObject.AddComponent<Image>();
            img.sprite = HubArt.Get("ui_tile", 32f) ?? ApogeeTheme.Panel;
            img.type = Image.Type.Sliced;
            img.color = new Color(1f, 1f, 1f, 0.96f);

            sheetTitle = UiKit.Outlined(UiKit.CreateText("Title", sh, "", 34, TextAnchor.MiddleLeft, new Vector2(0.05f, 0.86f), new Vector2(0.95f, 0.97f), ApogeeTheme.Gold), 2f);
            UiKit.FitLabel(sheetTitle, 34);
            sheetInfo = UiKit.CreateText("Info", sh, "", 22, TextAnchor.MiddleLeft, new Vector2(0.05f, 0.78f), new Vector2(0.95f, 0.86f), ApogeeTheme.Cream);
            UiKit.FitLabel(sheetInfo, 22);

            var cards = UiKit.CreateRect("Choices", sh, new Vector2(0.03f, 0.25f), new Vector2(0.97f, 0.77f));
            for (int i = 0; i < 3; i++)
            {
                var kind = (Def)(i + 1);
                float x0 = i / 3f + 0.01f, x1 = (i + 1) / 3f - 0.01f;
                var b = UiKit.CreateButton($"Choice_{i}", cards, "", new Vector2(x0, 0f), new Vector2(x1, 1f), () => BuildOrUpgrade(selectedLane, kind), 20, UiKit.CardColor);
                var icon = UiKit.CreateImage("Icon", b.transform, new Vector2(0.14f, 0.42f), new Vector2(0.86f, 0.97f), Art($"def_{DefArt[(int)kind]}_0", new Vector2(0.5f, 0.5f)), Color.white);
                icon.raycastTarget = false;
                var name = UiKit.Outlined(UiKit.CreateText("Name", b.transform, DefNames[(int)kind], 24, TextAnchor.MiddleCenter, new Vector2(0.03f, 0.3f), new Vector2(0.97f, 0.44f), ApogeeTheme.Cream), 1.5f);
                UiKit.FitLabel(name, 24);
                var level = UiKit.Outlined(UiKit.CreateText("Level", b.transform, "", 20, TextAnchor.MiddleCenter, new Vector2(0.03f, 0.18f), new Vector2(0.97f, 0.3f), ApogeeTheme.Gold), 1.2f);
                var cost = IconText.Create("Cost", b.transform, "", 22, TextAnchor.MiddleCenter, new Vector2(0.03f, 0.02f), new Vector2(0.97f, 0.18f), ApogeeTheme.Gold, 1.5f);
                choice[i] = (b, name, cost, icon, level);
            }

            repairButton = UiKit.CreateButton("Repair", sh, "", new Vector2(0.03f, 0.04f), new Vector2(0.495f, 0.22f), Repair, 20, new Color(0.25f, 0.38f, 0.2f));
            repairLabel = IconText.OnButton(repairButton, 20);
            burstButton = UiKit.CreateButton("Burst", sh, "", new Vector2(0.505f, 0.04f), new Vector2(0.97f, 0.22f), OnBurstAd, 20, new Color(0.55f, 0.36f, 0.08f));
            burstLabel = IconText.OnButton(burstButton, 20);

            var launchRt = UiKit.CreateRect("Launch", rt, new Vector2(0.1f, 0.025f), new Vector2(0.9f, 0.095f));
            var limg = launchRt.gameObject.AddComponent<Image>();
            limg.sprite = HubArt.Get("ui_play", 50f) ?? ApogeeTheme.Button;
            limg.type = Image.Type.Sliced;
            launchButton = launchRt.gameObject.AddComponent<Button>();
            launchButton.targetGraphic = limg;
            launchButton.onClick.AddListener(StartWave);
            launchRt.gameObject.AddComponent<ButtonPop>();
            var ll = UiKit.CreateText("Label", launchRt, "LANCER LA VAGUE", 40, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, new Color(0.3f, 0.09f, 0.02f));
            ll.fontStyle = FontStyle.Bold;
            UiKit.FitLabel(ll, 40);
        }

        Button RoundSkill(RectTransform rt, string name, string label, Vector2 anchor, Vector2 pos, Sprite icon, Color tint, UnityEngine.Events.UnityAction onClick, out Image fill, out Text timer)
        {
            var m = UiKit.CreateRect(name, rt, anchor, anchor);
            m.pivot = anchor;
            m.sizeDelta = new Vector2(180f, 180f);
            m.anchoredPosition = pos;
            var img = m.gameObject.AddComponent<Image>();
            img.sprite = HubArt.Get("ui_disc") ?? ApogeeTheme.Round;
            img.color = tint;
            var b = m.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            b.onClick.AddListener(onClick);
            m.gameObject.AddComponent<ButtonPop>();
            var ring = HubArt.Get("ui_ring");
            if (ring != null)
            {
                var r = UiKit.CreateImage("Ring", m, Vector2.zero, Vector2.one, ring, Color.white, false);
                r.rectTransform.offsetMin = new Vector2(-6, -6);
                r.rectTransform.offsetMax = new Vector2(6, 6);
                r.raycastTarget = false;
            }
            var ic = UiKit.CreateImage("Icon", m, new Vector2(0.2f, 0.3f), new Vector2(0.8f, 0.9f), icon, Color.white);
            ic.raycastTarget = false;
            UiKit.Outlined(UiKit.CreateText("Label", m, label, 22, TextAnchor.MiddleCenter, new Vector2(0f, 0.06f), new Vector2(1f, 0.32f), ApogeeTheme.Gold), 1.5f);
            fill = UiKit.CreateImage("Cooldown", m, new Vector2(0.04f, 0.04f), new Vector2(0.96f, 0.96f), HubArt.Get("ui_disc") ?? ApogeeTheme.Round, new Color(0.05f, 0.02f, 0.02f, 0.6f), false);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Radial360;
            fill.raycastTarget = false;
            timer = UiKit.Outlined(UiKit.CreateText("Timer", m, "", 48, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, ApogeeTheme.Cream), 2f);
            return b;
        }

        void BuildWaveControls(RectTransform rt)
        {
            molotovButton = RoundSkill(rt, "Molotov", "MOLOTOV", new Vector2(1f, 0f), new Vector2(-30f, 36f), Art("def_brasier_1", new Vector2(0.5f, 0.5f)),
                new Color(0.45f, 0.16f, 0.06f, 0.85f), OnMolotov, out molotovFill, out molotovTimer);
            volleyButton = RoundSkill(rt, "Volley", "VOLÉE", new Vector2(0f, 0f), new Vector2(30f, 36f), Art("def_arbalete_2", new Vector2(0.5f, 0.5f)),
                new Color(0.18f, 0.12f, 0.3f, 0.85f), OnVolley, out volleyFill, out volleyTimer);

            var hint = UiKit.CreateRect("Hint", rt, new Vector2(0.24f, 0.11f), new Vector2(0.76f, 0.15f));
            hintRoot = hint.gameObject;
            var ht = UiKit.Outlined(UiKit.CreateText("Text", hint, "Maintiens un couloir pour tirer", 26, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, ApogeeTheme.Cream), 2f);
            UiKit.FitLabel(ht, 26);
        }

        // ---- lifecycle -----------------------------------------------------------------

        protected override void OnEnter()
        {
            // A record already good enough opens the next land (records made before the rule changed count too).
            while (BarrSave.Unlocked < Lands.Length - 1 && BarrSave.Best(BarrSave.Unlocked) >= UnlockWave) BarrSave.Unlocked++;
            land = Mathf.Clamp(BarrSave.Region, 0, BarrSave.Unlocked);
            MillCatchUp();
            ApplyLayout();
            ShowMenu();
        }

        protected override void OnExit()
        {
            phase = Phase.Menu;
            ClearUnits();
            if (root != null) Destroy(root.gameObject);
            root = null;
            touchZone?.Clear();
            MillSave();
            SaveSystem.Flush();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) { MillSave(); SaveSystem.Flush(); }
            else MillCatchUp();
        }

        void OnApplicationQuit() { MillSave(); SaveSystem.Flush(); }

        /// <summary>Frames the camera on the land's lanes and paints its field.</summary>
        void ApplyLayout()
        {
            ClearUnits();
            if (root != null) Destroy(root.gameObject);
            laneCount = Lands[land].lanes;
            for (int l = 0; l < MaxLanes; l++) { lanes[l].visual = null; foreach (var list in lanes[l].pips) list.Clear(); }
            spacing = SpacingFor(laneCount);
            TakeOverCamera(new Vector3(Origin.x, Origin.y + CamY, -10f), CameraOrtho, new Color(0.3f, 0.12f, 0.16f), FieldWidth + 0.8f);
            float ortho = cam != null ? cam.orthographicSize : CameraOrtho;
            spawnY = CamY + ortho - 2.6f;
            BuildWorld(ortho);
            for (int l = 0; l < laneCount; l++) RefreshLaneVisual(l);
            selectedLane = Mathf.Clamp(selectedLane, 0, laneCount - 1);
            touchZone?.Clear();
        }

        SpriteRenderer Place(string name, Sprite sprite, Vector2 local, int order, Transform parent = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent != null ? parent : root, false);
            go.transform.position = new Vector3(Origin.x + local.x, Origin.y + local.y, 0f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            return sr;
        }

        static int DepthOrder(float y) => 20 + Mathf.RoundToInt((6f - y) * 4f);

        void BuildWorld(float ortho)
        {
            root = new GameObject("BarricadeWorld").transform;
            var L = Lands[land];
            float viewW = ortho * 2f * (cam != null ? cam.aspect : 0.5625f) + 2f;
            float viewBottom = CamY - ortho;

            var grass = Place("Grass", Art("field_grass", new Vector2(0.5f, 0.5f)), new Vector2(0f, CamY), -30);
            grass.drawMode = SpriteDrawMode.Tiled;
            grass.size = new Vector2(viewW + 2f, ortho * 2f + 3f);
            grass.color = L.grass;

            float laneTop = spawnY + 1.6f, laneBottom = BarricadeY + 0.2f;
            for (int l = 0; l < laneCount; l++)
            {
                var lane = Place($"Lane_{l}", Art("field_lane", new Vector2(0.5f, 0.5f)), new Vector2(LaneX(l) - Origin.x, (laneTop + laneBottom) / 2f), -25);
                lane.drawMode = SpriteDrawMode.Tiled;
                lane.size = new Vector2(1.5f, laneTop - laneBottom);
                lane.transform.localScale = new Vector3(spacing * 0.86f / 1.5f, 1f, 1f);
                lane.color = L.lane;
            }

            var forestTex = Resources.Load<Texture2D>("Barricade/field_forest");
            if (forestTex != null)
            {
                float fw = Mathf.Max(viewW + 1f, 7f);
                var forest = Place("Wood", Sprite.Create(forestTex, new Rect(0, 0, forestTex.width, forestTex.height), new Vector2(0.5f, 0.14f), forestTex.width / fw), new Vector2(0f, spawnY), 18);
                forest.color = L.fog;
            }

            // Fireflies over the field, for life.
            var flies = new GameObject("Fireflies");
            flies.transform.SetParent(root, false);
            flies.transform.position = new Vector3(Origin.x, Origin.y + 0.5f, 0f);
            var ps = flies.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.25f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.11f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.4f, 0.9f), new Color(1f, 0.6f, 0.3f, 0.7f));
            main.maxParticles = 50;
            var em = ps.emission; em.rateOverTime = 7f;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(FieldWidth + 1f, 7f, 0.1f);
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.4f; noise.frequency = 0.5f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            var pr = flies.GetComponent<ParticleSystemRenderer>();
            pr.material = new Material(Shader.Find("Sprites/Default"));
            pr.sortingOrder = 19;

            laneHighlight = Place("Highlight", PlaceholderVisuals.Square(Color.white), new Vector2(0f, (spawnY + BarricadeY) / 2f), -24).gameObject;
            laneHighlight.transform.localScale = new Vector3(spacing * 0.9f, spawnY - BarricadeY + 1f, 1f);
            laneHighlight.GetComponent<SpriteRenderer>().color = new Color(1f, 0.85f, 0.4f, 0.16f);
            laneHighlight.SetActive(false);

            var floor = Place("Terrace", Art("wall_castle", new Vector2(0.5f, 1f)), new Vector2(0f, BarricadeY - 0.15f), 58);
            floor.drawMode = SpriteDrawMode.Tiled;
            floor.size = new Vector2(viewW + 2f, BarricadeY - 0.15f - (viewBottom - 1f));
            floor.color = new Color(0.62f, 0.5f, 0.5f);
            palisadeSr = Place("Palisade", Art("palisade_0", new Vector2(0.5f, 0.07f)), new Vector2(0f, BarricadeY - 0.35f), 60);
            palisadeSr.drawMode = SpriteDrawMode.Tiled;
            palisadeSr.size = new Vector2(viewW + 2f, 200f / Ppu);
            palisadeState = -1;
            RefreshPalisade();

            heroSr = Place("Defender", ui.PlayerSprite, new Vector2(0f, PlayerY), 62);
            var skin = SkinCatalog.Find(SaveSystem.SelectedSkinId);
            var portrait = SkinCatalog.LoadPortrait(skin, out bool custom);
            if (custom) { heroSr.sprite = portrait; heroSr.transform.localScale = Vector3.one * 1.1f; }
            else { heroSr.color = skin.Tint; heroSr.transform.localScale = Vector3.one * 1.15f; }
            SpawnDrone(heroSr.transform);

            for (int l = 0; l < laneCount; l++) BuildLanePad(l);
        }

        void SpawnDrone(Transform defender)
        {
            int level = UpgradeManager.DroneLevel;
            if (level <= 0) return;
            drone = Drone.Create(root, defender, level);
            foreach (var r in drone.GetComponentsInChildren<Renderer>()) r.sortingOrder += 70;
            drone.offset = new Vector3(-0.75f, 0.95f, 0f);
            drone.mirrorWithTarget = false;
            drone.range = 30f;
            drone.FindTarget = _ =>
            {
                if (phase != Phase.Wave) return null;
                Enemy best = null;
                foreach (var e in enemies) if (e.hp > 0f && (best == null || e.y < best.y)) best = e;
                return best == null ? (Vector2?)null : new Vector2(LaneX(best.lane), Origin.y + best.y + 0.5f);
            };
            drone.Fire = (_, target) => FireShot(NearestLaneTo(target.x), PlayerY + 1.3f, 1f, false);
        }

        void BuildLanePad(int l)
        {
            var lane = lanes[l];
            float x = LaneX(l) - Origin.x;
            var holder = new GameObject($"LanePad_{l}").transform;
            holder.SetParent(root, false);
            holder.position = new Vector3(Origin.x + x, Origin.y + DefY, 0f);
            lane.visual = holder.gameObject;
            for (int d = 1; d <= 3; d++)
            {
                var def = (Def)d;
                float sx = x + SlotX(def), sy = SlotY(def);
                float k = def == Def.Arbalete ? 0.7f : 1f;
                int order = DepthOrder(sy);
                var pad = Place("Pad", Art("pad", new Vector2(0.5f, 0.5f), 256f / (spacing * 0.8f * k)), new Vector2(sx, sy - 0.25f), order - 2, holder);
                pad.color = new Color(1f, 1f, 1f, 0.85f);
                lane.ringSr[d] = Place("Ring", Art("ring", new Vector2(0.5f, 0.5f), 256f / (spacing * 0.8f * k)), new Vector2(sx, sy - 0.25f), order - 1, holder);
                lane.defSr[d] = Place("Defence", null, new Vector2(sx, sy - 0.5f), order, holder);
                lane.defSr[d].transform.localScale = Vector3.one * LaneScale * k * 0.9f;
                for (int p = 0; p < MaxLevel; p++)
                {
                    var pip = Place($"Pip_{p}", PlaceholderVisuals.Star(), new Vector2(sx + (p - 2) * 0.17f * LaneScale * k, sy - 0.66f), order + 1, holder);
                    pip.transform.localScale = Vector3.one * 0.15f * LaneScale * k;
                    lane.pips[d].Add(pip);
                }
            }
        }

        void RefreshLaneVisual(int l)
        {
            var lane = lanes[l];
            for (int d = 1; d <= 3; d++)
            {
                var sr = lane.defSr[d];
                if (sr == null) continue;
                int level = lane.level[d];
                if (level <= 0) sr.sprite = null;
                else
                {
                    int tier = level >= 5 ? 2 : level >= 3 ? 1 : 0;
                    sr.sprite = Art($"def_{DefArt[d]}_{tier}", new Vector2(0.5f, 0.06f), 256f / 1.5f);
                }
                sr.color = lane.disabled[d] ? new Color(0.35f, 0.32f, 0.36f, 0.9f) : Color.white;
                var pips = lane.pips[d];
                for (int k = 0; k < pips.Count; k++)
                {
                    pips[k].gameObject.SetActive(level > 0);
                    pips[k].color = k < level ? ApogeeTheme.Gold : new Color(0.25f, 0.15f, 0.12f, 0.8f);
                }
            }
        }

        void RefreshPalisade()
        {
            if (palisadeSr == null) return;
            float f = barricadeMax > 0f ? barricadeHp / barricadeMax : 1f;
            int state = f > 0.66f ? 0 : f > 0.33f ? 1 : 2;
            if (state == palisadeState) return;
            palisadeState = state;
            var size = palisadeSr.size;
            palisadeSr.sprite = Art($"palisade_{state}", new Vector2(0.5f, 0.07f));
            palisadeSr.size = size;
        }

        int NearestLaneTo(float worldX)
        {
            int lane = 0;
            float nearest = float.MaxValue;
            for (int l = 0; l < laneCount; l++)
            {
                float d = Mathf.Abs(LaneX(l) - worldX);
                if (d >= nearest) continue;
                nearest = d;
                lane = l;
            }
            return lane;
        }

        int LaneFromScreen(Vector2 screen)
        {
            if (cam == null || laneCount <= 0) return -1;
            var world = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 10f));
            if (Mathf.Abs(world.x - Origin.x) > FieldWidth / 2f + 0.4f) return -1;
            return NearestLaneTo(world.x);
        }

        void ClearUnits()
        {
            foreach (var e in enemies) if (e.go != null) Destroy(e.go);
            foreach (var s in shots) if (s.go != null) Destroy(s.go);
            foreach (var s in spits) if (s != null) Destroy(s);
            enemies.Clear();
            shots.Clear();
            spits.Clear();
        }

        /// <summary>A new run on the chosen land: empty lanes, a whole palisade, the Forge's gifts.</summary>
        void StartRun()
        {
            StopAllCoroutines();
            ApplyLayout();
            for (int l = 0; l < MaxLanes; l++)
            {
                Array.Clear(lanes[l].level, 0, 4); Array.Clear(lanes[l].disabled, 0, 4);
                lanes[l].turretTimer = 0f; laneFireCooldown[l] = 0f;
            }
            for (int l = 0; l < laneCount; l++) RefreshLaneVisual(l);
            Array.Clear(perks, 0, perks.Length);
            touchZone?.Clear();
            wave = 1;
            debris = 15 + 12 * ForgeLevel(Forge.StartDebris);
            kills = 0; bossesKilled = 0; streak = 0; bestStreak = 0;
            shardCarry = 0f;
            shardsThisRun = 0;
            molotovCooldown = 0f; volleyCooldown = 0f;
            burstWaves = 0;
            adPending = false;
            barricadeMax = (120f + SaveSystem.GetLevel(UpgradeStat.Armor) * 12f) * ForgeBonus(Forge.Palisade, 0.12f);
            barricadeHp = barricadeMax;
            RefreshPalisade();
            HideScreens();
            gameUi.SetActive(true);
            BarrSave.Runs++;
            ClearRunSave();
            EnterBuildPhase(first: true);
            SaveRun();
        }

        // ---- phases --------------------------------------------------------------------

        void EnterBuildPhase(bool first)
        {
            phase = Phase.Build;
            sheet.SetActive(true);
            launchButton.gameObject.SetActive(true);
            molotovButton.gameObject.SetActive(false);
            volleyButton.gameObject.SetActive(false);
            hintRoot.SetActive(false);
            streakText.text = "";
            if (laneHighlight != null) laneHighlight.SetActive(true);
            messageText.text = first ? "Touche un couloir pour y bâtir tes défenses" : $"Vague {wave - 1} repoussée !";
            SelectLane(selectedLane);
            RefreshHud();
        }

        void StartWave()
        {
            if (phase != Phase.Build || adPending) return;
            phase = Phase.Wave;
            sheet.SetActive(false);
            launchButton.gameObject.SetActive(false);
            molotovButton.gameObject.SetActive(true);
            volleyButton.gameObject.SetActive(VolleyUnlocked);
            hintRoot.SetActive(wave <= 2 && BarrSave.Runs <= 2);
            if (laneHighlight != null) laneHighlight.SetActive(false);
            float front = 1f + (laneCount - 3) / 4f;
            spawnRemaining = Mathf.RoundToInt((4 + wave * 2) * front);
            spawnInterval = Mathf.Max(0.5f, 1.5f - wave * 0.07f) / front;
            spawnTimer = 0.6f;
            bossWave = wave % BossEvery == 0;
            bossSpawned = false;
            messageText.text = "";
            if (bossWave) ui.ShowBanner("UN COLOSSE !", $"Vague {wave} : abats-le, il porte un coffre", 2f);
            else ui.ShowBanner($"VAGUE {wave}", $"{spawnRemaining} morts sortent du bois", 1.5f);
            RefreshHud();
        }

        void EndWave()
        {
            int bonus = 5 + wave;
            debris += bonus;
            barricadeHp = Mathf.Min(barricadeMax, barricadeHp + barricadeMax * 0.2f);
            RefreshPalisade();
            int held = wave;
            int shards = AwardShards(held);
            if (held > BarrSave.Best(land)) BarrSave.SetBest(land, held);
            if (held > SaveSystem.BarricadeBestWave) SaveSystem.BarricadeBestWave = held;
            if (held >= UnlockWave && land == BarrSave.Unlocked && land < Lands.Length - 1)
            {
                BarrSave.Unlocked = land + 1;
                ui.ShowBanner("NOUVELLE TERRE !", $"{Lands[land + 1].name} est ouverte", 2.6f);
            }
            wave++;
            if (burstWaves > 0) burstWaves--;
            for (int l = 0; l < laneCount; l++)
            { Array.Clear(lanes[l].disabled, 0, 4); RefreshLaneVisual(l); }
            SaveSystem.Flush();
            Sfx.Milestone();
            RewardPopup.Show(new Vector3(Origin.x, Origin.y + 1f, 0f), 0, 0, bonus);
            if (shards > 0) Fx.Text(new Vector3(Origin.x, Origin.y + 2f, 0f), $"+{shards} ÉCLATS", new Color(0.8f, 0.65f, 1f), 1.3f);
            SaveRun();
            if (held % PerkEvery == 0 && OfferPerks()) return;
            EnterBuildPhase(first: false);
        }

        /// <summary>Banks Éclats now (raw value before the land's and the Fortune's multipliers); returns how many.</summary>
        int AwardShards(float raw)
        {
            shardCarry += raw * Lands[land].reward * ForgeBonus(Forge.Fortune, 0.05f);
            int n = Mathf.FloorToInt(shardCarry);
            if (n <= 0) return 0;
            shardCarry -= n;
            shardsThisRun += n;
            BarrSave.Shards += n;
            return n;
        }

        void GameOver()
        {
            phase = Phase.Over;
            sheet.SetActive(false);
            launchButton.gameObject.SetActive(false);
            molotovButton.gameObject.SetActive(false);
            volleyButton.gameObject.SetActive(false);
            hintRoot.SetActive(false);
            touchZone?.Clear();
            int held = wave - 1;
            int earned = shardsThisRun;
            ClearRunSave();
            BarrSave.TotalKills += kills;
            int materials = held / 2 + bossesKilled;
            if (materials > 0) SaveSystem.AddMaterials(materials);
            SaveSystem.Flush();
            ShowOver(held, earned, materials);
            Sfx.Death();
            Fx.Shake(0.5f, 0.4f);
            AdService.OnPlayerDeath();
        }

        // ---- per frame -------------------------------------------------------------------

        void Update()
        {
            MillTick(Mathf.Min(Time.unscaledDeltaTime, 1f));
            if (!IsActive || root == null) return;
            AnimateWorld();
            UpdateScreens();
            if (phase != Phase.Build && phase != Phase.Wave) return;
            float dt = Time.deltaTime;

            if (phase == Phase.Build)
            {
                for (int l = 0; l < laneCount; l++)
                    if (touchZone != null && touchZone.IsHeld(l) && l != selectedLane) { SelectLane(l); Sfx.Drop(); }
                hudTimer -= dt;
                if (hudTimer <= 0f) { hudTimer = 0.5f; RefreshHud(); }
                return;
            }

            UpdateSpawning(dt);
            UpdateEnemies(dt);
            UpdateDefences(dt);
            UpdateShots(dt);
            UpdatePlayerFire(dt);
            molotovCooldown = Mathf.Max(0f, molotovCooldown - dt);
            volleyCooldown = Mathf.Max(0f, volleyCooldown - dt);
            molotovFill.fillAmount = molotovCooldown / MolotovCooldownMax;
            molotovTimer.text = molotovCooldown > 0f ? Mathf.CeilToInt(molotovCooldown).ToString() : "";
            volleyFill.fillAmount = volleyCooldown / VolleyCooldownMax;
            volleyTimer.text = volleyCooldown > 0f ? Mathf.CeilToInt(volleyCooldown).ToString() : "";
            if (streak > 0 && Time.time - lastKillTime > StreakWindow) { streak = 0; streakText.text = ""; }
            hudTimer -= dt;
            if (hudTimer <= 0f) { hudTimer = 0.25f; RefreshHud(); }

            if (barricadeHp <= 0f) { GameOver(); return; }
            if (spawnRemaining == 0 && enemies.Count == 0) EndWave();
        }

        void AnimateWorld()
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 4f);
            for (int l = 0; l < laneCount; l++)
            {
                var lane = lanes[l];
                bool build = phase == Phase.Build;
                for (int d = 1; d <= 3; d++)
                {
                    var ring = lane.ringSr[d];
                    if (ring == null) continue;
                    bool empty = lane.level[d] <= 0;
                    ring.enabled = build && (empty || l == selectedLane);
                    ring.color = l == selectedLane
                        ? new Color(1f, 0.85f, 0.4f, 0.55f + 0.4f * pulse)
                        : new Color(1f, 1f, 1f, 0.2f + 0.2f * pulse);
                }
            }
            if (laneHighlight != null && laneHighlight.activeSelf)
            {
                var p = laneHighlight.transform.position;
                p.x = Mathf.Lerp(p.x, LaneX(selectedLane), Time.deltaTime * 14f);
                laneHighlight.transform.position = p;
            }
        }

        // ---- the dead -------------------------------------------------------------------

        void UpdateSpawning(float dt)
        {
            if (spawnRemaining <= 0) return;
            spawnTimer -= dt;
            if (spawnTimer > 0f) return;
            spawnTimer = spawnInterval;
            spawnRemaining--;
            if (bossWave && !bossSpawned) { bossSpawned = true; SpawnEnemy(Random.Range(0, laneCount), Foe.Colosse, true); return; }
            SpawnEnemy(Random.Range(0, laneCount), RollKind(), false);
        }

        /// <summary>The kinds of dead that walk this land, joining one by one as the waves go on.</summary>
        Foe RollKind()
        {
            var pool = new List<(Foe kind, float weight)> { (Foe.Rodeur, 1f) };
            int early = land >= 4 ? 2 : 0;   // the Heart of Shadow brings everything early
            if (wave >= 2) pool.Add((Foe.Furtif, 0.6f));
            if (wave >= 4 - early) pool.Add((Foe.Follet, 0.4f));
            if (wave >= 6 - early) pool.Add((Foe.Colosse, 0.08f + wave * 0.004f));
            if (land >= 1 && wave >= 3 - early) pool.Add((Foe.Saboteur, 0.3f));
            if (land >= 2 && wave >= 4 - early) pool.Add((Foe.Necro, 0.15f));
            if (land >= 3 && wave >= 3 - early) pool.Add((Foe.Shield, 0.35f));
            float total = 0f;
            foreach (var p in pool) total += p.weight;
            float r = Random.value * total;
            foreach (var p in pool) { r -= p.weight; if (r <= 0f) return p.kind; }
            return Foe.Rodeur;
        }

        Enemy SpawnEnemy(int lane, Foe kind, bool boss, float atY = float.NaN)
        {
            var e = new Enemy { kind = kind, lane = lane, boss = boss, y = float.IsNaN(atY) ? spawnY + Random.Range(0f, 0.4f) : atY, x = Random.Range(-0.18f, 0.18f) * spacing };
            switch (kind)
            {
                case Foe.Furtif:
                    e.hp = 1f + wave * 0.32f; e.speed = 2.4f; e.dmg = 4f; e.interval = 0.8f; e.debris = 2; e.size = 0.8f;
                    break;
                case Foe.Follet:
                    e.hp = 2.5f + wave * 0.45f; e.speed = 1.0f; e.dmg = 5f; e.interval = 3f; e.debris = 3; e.size = 0.95f;
                    break;
                case Foe.Colosse:
                    e.hp = 10f + wave * 1.4f; e.speed = 0.7f; e.dmg = 16f; e.interval = 1.6f; e.debris = 8; e.size = 1.0f;
                    if (boss) { e.hp *= 2.4f; e.size = 1.28f; e.debris = 20; }
                    break;
                case Foe.Saboteur:
                    e.hp = 1.6f + wave * 0.36f; e.speed = 2.0f; e.dmg = 3f; e.interval = 1f; e.debris = 4; e.size = 0.78f;
                    e.tint = new Color(1f, 0.72f, 0.45f);
                    break;
                case Foe.Necro:
                    e.hp = 4f + wave * 0.6f; e.speed = 0.8f; e.dmg = 4f; e.interval = 3.5f; e.debris = 7; e.size = 1.05f; e.summonTimer = 2.5f;
                    e.tint = new Color(0.8f, 0.6f, 1f);
                    break;
                case Foe.Shield:
                    e.hp = 4f + wave * 0.7f; e.speed = 0.9f; e.dmg = 8f; e.interval = 1.3f; e.debris = 5; e.size = 1.05f;
                    e.shield = 3f + wave * 0.6f;
                    e.tint = new Color(0.82f, 0.88f, 1f);
                    break;
                default:
                    e.hp = 2f + wave * 0.5f; e.speed = 1.2f; e.dmg = 6f; e.interval = 1.2f; e.debris = 2; e.size = 1f;
                    break;
            }
            e.hp *= HealthScale;
            e.shield *= HealthScale;
            e.maxHp = e.hp;
            e.attackTimer = e.interval * 0.5f;
            e.frames = Frames(kind);

            var go = new GameObject($"Foe_{kind}");
            go.transform.SetParent(root, false);
            e.go = go;
            e.sr = go.AddComponent<SpriteRenderer>();
            e.sr.sprite = e.frames != null ? e.frames[0] : PlaceholderVisuals.Zombie();
            e.anim = Random.Range(0f, 8f);

            e.hpBar = new GameObject("HpBar");
            e.hpBar.transform.SetParent(go.transform, false);
            var bg = e.hpBar.AddComponent<SpriteRenderer>();
            bg.sprite = PlaceholderVisuals.Square(Color.white);
            bg.color = new Color(0.12f, 0.04f, 0.03f, 0.9f);
            e.hpBar.transform.localScale = new Vector3(0.62f, 0.08f, 1f);
            var fill = new GameObject("Fill");
            fill.transform.SetParent(e.hpBar.transform, false);
            var fsr = fill.AddComponent<SpriteRenderer>();
            fsr.sprite = PlaceholderVisuals.Square(Color.white);
            fsr.color = kind == Foe.Colosse ? new Color(1f, 0.5f, 0.15f) : new Color(0.95f, 0.3f, 0.2f);
            fill.transform.localScale = new Vector3(0.94f, 0.6f, 1f);
            e.hpFill = fill.transform;
            e.hpBar.SetActive(boss);

            if (kind == Foe.Shield)
            {
                e.shieldBadge = new GameObject("Shield");
                e.shieldBadge.transform.SetParent(go.transform, false);
                e.shieldBadge.transform.localPosition = new Vector3(0f, 0.55f, 0f);
                e.shieldBadge.transform.localScale = Vector3.one * 0.62f;
                var ssr = e.shieldBadge.AddComponent<SpriteRenderer>();
                ssr.sprite = GameIcons.PowerUp(PowerUpKind.Shield);
            }

            PlaceEnemy(e);
            enemies.Add(e);

            if (!introduced.Contains(kind) && kind != Foe.Rodeur && !boss)
            {
                introduced.Add(kind);
                var (title, line) = kind switch
                {
                    Foe.Furtif => ("FURTIF !", "Rapide et fragile : tire vite"),
                    Foe.Follet => ("FEU-FOLLET !", "Il flotte au-dessus des Pieux et du Brasier, et crache de loin"),
                    Foe.Colosse => ("COLOSSE !", "Il met hors service la défense de son couloir"),
                    Foe.Saboteur => ("SABOTEUR !", "Il fait perdre un niveau à la défense de son couloir"),
                    Foe.Necro => ("NÉCROMANCIEN !", "Il relève des morts dans son couloir : abats-le vite"),
                    _ => ("PORTE-BOUCLIER !", "Son bouclier encaisse tes tirs : le feu et les Pieux le contournent"),
                };
                ui.ShowBanner(title, line, 2.4f);
            }
            return e;
        }

        /// <summary>Puts a body where its numbers say: nearer is bigger and drawn in front.</summary>
        void PlaceEnemy(Enemy e)
        {
            float near = Mathf.InverseLerp(spawnY, BarricadeY, e.y);
            float scale = Mathf.Lerp(0.72f, 1f, near) * e.size * LaneScale;
            float hover = e.Flies ? 0.45f + Mathf.Sin(Time.time * 2.3f + e.lane) * 0.08f : 0f;
            e.go.transform.position = new Vector3(LaneX(e.lane) + e.x * near, Origin.y + e.y + hover, 0f);
            e.go.transform.localScale = Vector3.one * scale;
            int order = DepthOrder(e.y);
            e.sr.sortingOrder = order;
            if (e.hpBar != null)
            {
                e.hpBar.transform.localPosition = new Vector3(0f, e.kind == Foe.Colosse ? 2.1f : 1.55f, 0f);
                foreach (var r in e.hpBar.GetComponentsInChildren<SpriteRenderer>()) r.sortingOrder = order + 1;
            }
            if (e.shieldBadge != null) e.shieldBadge.GetComponent<SpriteRenderer>().sortingOrder = order + 1;
        }

        void UpdateEnemies(float dt)
        {
            for (int i = enemies.Count - 1; i >= 0; i--)
            {
                if (i >= enemies.Count) continue;
                var e = enemies[i];
                var lane = lanes[e.lane];

                if (e.burn > 0f)
                {
                    e.burn -= dt;
                    e.burnTick -= dt;
                    if (e.burnTick <= 0f)
                    {
                        e.burnTick = 0.5f;
                        Fx.Burst(e.go.transform.position + Vector3.up * 0.5f, new Color(1f, 0.55f, 0.15f), 3, 1.5f, 0.07f, -0.5f);
                        Damage(e, e.burnDps * 0.5f, true);
                        if (e.hp <= 0f) continue;
                    }
                }

                float slow = Time.time < e.slowUntil ? 0.25f : 0f;
                // The wreckers knock out the first defence they reach (the saboteur also takes a level off it).
                if ((e.kind == Foe.Colosse || e.kind == Foe.Saboteur) && !e.smashed)
                {
                    foreach (var d in new[] { Def.Brasier, Def.Arbalete, Def.Pieux })
                    {
                        if (!lane.Active(d) || Mathf.Abs(e.y - SlotY(d)) > 0.4f) continue;
                        e.smashed = true;
                        lane.disabled[(int)d] = true;
                        if (e.kind == Foe.Saboteur && lane.level[(int)d] > 1) lane.level[(int)d]--;
                        RefreshLaneVisual(e.lane);
                        var at = new Vector3(LaneX(e.lane) + SlotX(d), Origin.y + SlotY(d), 0f);
                        Fx.Burst(at, new Color(0.5f, 0.45f, 0.45f), 26, 4.5f, 0.12f, 0.4f);
                        Fx.Shake(0.3f, 0.25f);
                        Fx.Text(at + Vector3.up * 1f, e.kind == Foe.Saboteur ? $"{DefNames[(int)d]} saboté !" : $"{DefNames[(int)d]} hors service !",
                            new Color(1f, 0.45f, 0.3f), 1.2f);
                        Sfx.Kill();
                        break;
                    }
                }
                if (!e.Flies)
                {
                    bool inStakes = e.y < SlotY(Def.Pieux) + ZoneUp && e.y > SlotY(Def.Pieux) - ZoneDown;
                    bool inFire = e.y < SlotY(Def.Brasier) + ZoneUp && e.y > SlotY(Def.Brasier) - ZoneDown;
                    if (inStakes && lane.Active(Def.Pieux))
                    {
                        int lv = lane.Level(Def.Pieux);
                        slow = Mathf.Max(slow, 0.2f);
                        e.trapTick -= dt;
                        if (e.trapTick <= 0f)
                        {
                            e.trapTick = 0.7f;
                            Damage(e, 0.9f * lv * ForgeBonus(Forge.Pieux, 0.1f) * (e.kind == Foe.Colosse ? 0.5f : 1f), true);
                            if (e.hp <= 0f) continue;
                            if (Perks(Perk.Venom) > 0 && e.burn <= 0.5f) Ignite(e, 0.4f * lv);
                        }
                    }
                    if (inFire && lane.Active(Def.Brasier) && e.burn <= 0.5f)
                        Ignite(e, 0.55f * lane.Level(Def.Brasier) * ForgeBonus(Forge.Brasier, 0.1f) * (e.kind == Foe.Colosse ? 0.5f : 1f));
                }

                // The Necromancer stops mid-lane and raises the dead.
                if (e.kind == Foe.Necro && e.y <= NecroStopY + 0.01f && e.summons < 6)
                {
                    e.summonTimer -= dt;
                    if (e.summonTimer <= 0f)
                    {
                        e.summonTimer = 4f;
                        e.summons++;
                        var raised = SpawnEnemy(e.lane, Foe.Rodeur, false, e.y - 0.7f);
                        raised.debris = 1;
                        Fx.Burst(raised.go.transform.position, new Color(0.6f, 0.35f, 0.8f), 14, 2.5f, 0.1f, 0.3f);
                    }
                }

                float stopY = e.kind == Foe.Follet ? FolletStopY : e.kind == Foe.Necro ? NecroStopY : BarricadeY + 0.55f;
                if (e.y > stopY)
                {
                    e.y = Mathf.Max(stopY, e.y - e.speed * (1f - slow) * dt);
                    e.anim += dt * (e.kind == Foe.Furtif || e.kind == Foe.Saboteur ? 13f : 8f);
                }
                else
                {
                    e.anim += dt * 4f;
                    if (e.kind != Foe.Necro)
                    {
                        e.attackTimer -= dt;
                        if (e.attackTimer <= 0f)
                        {
                            e.attackTimer = e.interval;
                            if (e.kind == Foe.Follet) Spit(e);
                            else HitPalisade(e.dmg, e.go.transform.position + Vector3.up * 0.4f);
                        }
                    }
                }
                if (e.frames != null) e.sr.sprite = e.frames[(int)e.anim % e.frames.Length];
                e.flash = Mathf.Max(0f, e.flash - dt);
                var tint = e.burn > 0f ? e.tint * new Color(1f, 0.72f, 0.55f) : Time.time < e.slowUntil ? e.tint * new Color(0.7f, 0.85f, 1f) : e.tint;
                e.sr.color = e.flash > 0f ? new Color(1f, 0.45f, 0.35f) : tint;
                PlaceEnemy(e);
            }
        }

        void Ignite(Enemy e, float dps)
        {
            e.burn = 3f;
            e.burnDps = dps * BurnFactor;
            e.burnTick = 0f;
        }

        void Spit(Enemy e)
        {
            var go = new GameObject("Spit");
            go.transform.SetParent(root, false);
            go.transform.position = e.go.transform.position + Vector3.up * 0.6f;
            go.transform.localScale = Vector3.one * 0.32f;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = PlaceholderVisuals.Circle(new Color(0.7f, 1f, 0.4f));
            sr.sortingOrder = 70;
            spits.Add(go);
            StartCoroutine(SpitFlight(go, e.dmg));
        }

        System.Collections.IEnumerator SpitFlight(GameObject spit, float damage)
        {
            float targetY = Origin.y + BarricadeY + 0.5f;
            while (spit != null && spit.transform.position.y > targetY && phase == Phase.Wave)
            {
                spit.transform.position += Vector3.down * (7f * Time.deltaTime);
                yield return null;
            }
            if (spit == null) yield break;
            if (phase == Phase.Wave) HitPalisade(damage, spit.transform.position);
            spits.Remove(spit);
            Destroy(spit);
        }

        void HitPalisade(float damage, Vector3 at)
        {
            barricadeHp = Mathf.Max(0f, barricadeHp - damage);
            Fx.Burst(at, new Color(0.6f, 0.4f, 0.2f), 6, 2f, 0.07f);
            Sfx.Hit();
            Fx.Shake(0.12f, 0.15f);
            RefreshPalisade();
        }

        // ---- the defences and the shooting ----------------------------------------------

        void UpdateDefences(float dt)
        {
            for (int l = 0; l < laneCount; l++)
            {
                var lane = lanes[l];
                if (!lane.Active(Def.Arbalete)) continue;
                lane.turretTimer -= dt;
                if (lane.turretTimer > 0f) continue;
                float from = SlotY(Def.Arbalete);
                if (FirstInLane(l, from) == null) continue;
                int lv = lane.Level(Def.Arbalete);
                lane.turretTimer = 1.25f / ((1f + 0.4f * (lv - 1)) * (1f + 0.35f * Perks(Perk.QuickBow)));
                FireShot(l, from + 0.9f, (1f + 0.6f * (lv - 1)) * ForgeBonus(Forge.Arbalete, 0.1f), false, true);
            }
        }

        Enemy FirstInLane(int lane, float above)
        {
            Enemy best = null;
            foreach (var e in enemies)
                if (e.lane == lane && e.y > above - 0.2f && (best == null || e.y < best.y)) best = e;
            return best;
        }

        void UpdatePlayerFire(float dt)
        {
            var kb = Keyboard.current;
            float rate = HeroRate;
            for (int l = 0; l < laneCount; l++)
            {
                laneFireCooldown[l] -= dt;
                bool held = touchZone != null && touchZone.IsHeld(l);
                if (kb != null)
                {
                    if (kb[(Key)((int)Key.Digit1 + l)].isPressed) held = true;
                    if (l == 0 && (kb.leftArrowKey.isPressed || kb.aKey.isPressed)) held = true;
                    if (l == laneCount - 1 && (kb.rightArrowKey.isPressed || kb.dKey.isPressed)) held = true;
                    if (l == laneCount / 2 && (kb.upArrowKey.isPressed || kb.wKey.isPressed || kb.sKey.isPressed)) held = true;
                }
                if (!held || laneFireCooldown[l] > 0f) continue;
                laneFireCooldown[l] = 1f / rate;
                FireShot(l, PlayerY + 1.2f, ShotDamage, burstWaves > 0, false, true);
                // Twin shot: sometimes a second arrow goes up a neighbouring lane.
                if (Perks(Perk.Twin) > 0 && Random.value < 0.25f * Perks(Perk.Twin) && laneCount > 1)
                {
                    int other = l == 0 ? 1 : l == laneCount - 1 ? l - 1 : l + (Random.value < 0.5f ? -1 : 1);
                    FireShot(other, PlayerY + 1.2f, ShotDamage, burstWaves > 0, false, true);
                }
                Sfx.Shoot();
                if (heroSr != null) heroSr.flipX = LaneX(l) < heroSr.transform.position.x - 0.1f;
                if (hintRoot.activeSelf && wave > 1) hintRoot.SetActive(false);
            }
        }

        void FireShot(int lane, float fromY, float damage, bool burst, bool bolt = false, bool fromHero = false)
        {
            var go = new GameObject(bolt ? "Bolt" : "Shot");
            go.transform.SetParent(root, false);
            go.transform.position = new Vector3(LaneX(lane), Origin.y + fromY, 0f);
            go.transform.localScale = bolt ? new Vector3(0.09f, 0.5f, 1f) : burst ? new Vector3(0.15f, 0.5f, 1f) : new Vector3(0.11f, 0.42f, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = PlaceholderVisuals.Square(Color.white);
            sr.color = bolt ? new Color(0.92f, 0.86f, 0.72f) : Perks(Perk.Frost) > 0 && fromHero ? new Color(0.7f, 0.9f, 1f)
                : burst ? new Color(1f, 0.78f, 0.25f) : new Color(1f, 0.9f, 0.55f);
            sr.sortingOrder = 72;
            shots.Add(new Shot { lane = lane, y = fromY, speed = ShotSpeed, damage = damage, go = go, fromHero = fromHero, pierce = fromHero ? Perks(Perk.Pierce) : 0 });
        }

        void UpdateShots(float dt)
        {
            for (int i = shots.Count - 1; i >= 0; i--)
            {
                if (i >= shots.Count) continue;
                var s = shots[i];
                s.y += s.speed * dt;
                var p = s.go.transform.position;
                p.y = Origin.y + s.y;
                s.go.transform.position = p;
                Enemy hit = null;
                foreach (var e in enemies)
                {
                    if (e.lane != s.lane || s.hit.Contains(e)) continue;
                    float body = e.y + (e.Flies ? 0.9f : 0.6f);
                    if (Mathf.Abs(body - s.y) < 0.6f) { hit = e; break; }
                }
                if (hit != null)
                {
                    s.hit.Add(hit);
                    float dmg = s.damage;
                    bool crit = s.fromHero && Random.value < CritChance;
                    if (crit) dmg *= 2.5f;
                    // A shield takes the blow first.
                    if (hit.shield > 0f)
                    {
                        hit.shield -= dmg;
                        Fx.Burst(hit.go.transform.position + Vector3.up * 0.6f, new Color(0.75f, 0.85f, 1f), 4, 2f, 0.06f);
                        if (hit.shield <= 0f && hit.shieldBadge != null)
                        {
                            Destroy(hit.shieldBadge);
                            hit.shieldBadge = null;
                            Fx.Text(hit.go.transform.position + Vector3.up * 1.2f, "BOUCLIER BRISÉ", new Color(0.75f, 0.85f, 1f), 0.8f);
                        }
                    }
                    else
                    {
                        if (crit) Fx.Text(hit.go.transform.position + Vector3.up * 1.3f, "CRITIQUE !", ApogeeTheme.Gold, 0.7f);
                        if (s.fromHero && Perks(Perk.Frost) > 0) hit.slowUntil = Time.time + 1.5f;
                        Damage(hit, dmg, false);
                        if (s.fromHero && Perks(Perk.Execute) > 0 && hit.hp > 0f && hit.hp < hit.maxHp * 0.15f) Damage(hit, hit.hp + 1f, true);
                    }
                    if (s.pierce > 0) { s.pierce--; continue; }
                    Destroy(s.go);
                    shots.RemoveAt(i);
                }
                else if (s.y > spawnY + 2f)
                {
                    Destroy(s.go);
                    shots.RemoveAt(i);
                }
            }
        }

        void Damage(Enemy e, float amount, bool silent)
        {
            if (e.hp <= 0f) return;
            e.hp -= amount;
            var pos = e.go.transform.position + Vector3.up * 0.6f;
            if (!silent) { Fx.Burst(pos, new Color(0.45f, 0.18f, 0.35f), 4, 2f, 0.07f); e.flash = 0.08f; }
            if (e.hp > 0f)
            {
                e.hpBar.SetActive(true);
                float t = Mathf.Clamp01(e.hp / e.maxHp);
                e.hpFill.localScale = new Vector3(0.94f * t, 0.6f, 1f);
                e.hpFill.localPosition = new Vector3(-0.47f * (1f - t), 0f, 0f);
                return;
            }
            Kill(e, pos);
        }

        void Kill(Enemy e, Vector3 pos)
        {
            kills++;
            // Streak: kills close together.
            streak = Time.time - lastKillTime <= StreakWindow ? streak + 1 : 1;
            lastKillTime = Time.time;
            bestStreak = Mathf.Max(bestStreak, streak);
            int gain = Mathf.Max(e.debris, Mathf.RoundToInt(e.debris * LootFactor)) + (streak >= 5 ? 1 + streak / 10 : 0);
            debris += gain;
            if (streak >= 5) streakText.text = $"SÉRIE x{streak}";
            if (streak == 10 || streak == 25 || streak == 50 || streak == 100)
            {
                ui.ShowBanner($"SÉRIE x{streak} !", "Les débris pleuvent", 1.2f);
                Sfx.Milestone();
            }
            AwardShards(0.1f);
            if (Perks(Perk.Leech) > 0) barricadeHp = Mathf.Min(barricadeMax, barricadeHp + Perks(Perk.Leech));
            Fx.Burst(pos, new Color(0.45f, 0.18f, 0.35f), e.kind == Foe.Colosse ? 26 : 12, 3.5f, 0.11f);
            RewardPopup.Show(pos, 0, 0, gain);
            Sfx.Kill();
            int lane = e.lane;
            float y = e.y;
            enemies.Remove(e);
            Destroy(e.go);

            if (e.kind == Foe.Colosse) bossesKilled++;
            if (e.boss)
            {
                // The chest: Éclats for the Forge, débris for the run.
                int chest = AwardShards(5f + wave * 0.5f);
                debris += 20;
                Fx.Burst(pos, ApogeeTheme.Gold, 40, 6f, 0.14f, 0f);
                Fx.Text(pos + Vector3.up * 0.8f, "COFFRE !", ApogeeTheme.Gold, 1.4f);
                ui.ShowBanner("COFFRE !", $"+{chest} [e]   ·   +20 [d]", 2f);
                Fx.Shake(0.4f, 0.3f);
            }

            // Bursting dead: they hurt their neighbours.
            if (Perks(Perk.Blast) > 0)
            {
                float blast = 1.5f * Perks(Perk.Blast) * (1f + wave * 0.1f);
                for (int i = enemies.Count - 1; i >= 0; i--)
                {
                    if (i >= enemies.Count) continue;
                    var o = enemies[i];
                    if (o.lane == lane && Mathf.Abs(o.y - y) < 1.3f) Damage(o, blast, true);
                }
                Fx.Burst(pos, new Color(0.9f, 0.4f, 0.8f), 10, 3f, 0.1f);
            }
        }

        // ---- skills -------------------------------------------------------------------------

        /// <summary>A bottle of fire on the busiest lane: everything in it burns.</summary>
        void OnMolotov()
        {
            if (phase != Phase.Wave || molotovCooldown > 0f) return;
            int best = -1, bestCount = 0;
            for (int l = 0; l < laneCount; l++)
            {
                int n = 0;
                foreach (var e in enemies) if (e.lane == l) n++;
                if (n > bestCount) { bestCount = n; best = l; }
            }
            if (best < 0) return;
            molotovCooldown = MolotovCooldownMax;
            float dps = (1.6f + wave * 0.15f) * ForgeBonus(Forge.Molotov, 0.08f) * Mathf.Max(1f, Lands[land].hp * 0.6f);
            foreach (var e in enemies)
            {
                if (e.lane != best) continue;
                e.burn = 4f;
                e.burnDps = dps * BurnFactor;
                e.burnTick = 0f;
            }
            for (float y = BarricadeY + 1f; y < spawnY; y += 0.8f)
                Fx.Burst(new Vector3(LaneX(best), Origin.y + y, 0f), new Color(1f, 0.55f, 0.15f), 6, 2.5f, 0.12f, -0.6f);
            Fx.Shake(0.25f, 0.2f);
            Sfx.Attack();
        }

        /// <summary>The Volley: arrows rain on every lane at once.</summary>
        void OnVolley()
        {
            if (phase != Phase.Wave || volleyCooldown > 0f || !VolleyUnlocked) return;
            volleyCooldown = VolleyCooldownMax;
            float dmg = (2f + wave * 0.3f) * (1f + 0.12f * (ForgeLevel(Forge.Volley) - 1)) * Mathf.Max(1f, Lands[land].hp * 0.6f);
            for (int l = 0; l < laneCount; l++)
                for (float y = BarricadeY + 1f; y < spawnY; y += 1.1f)
                    Fx.Burst(new Vector3(LaneX(l) + Random.Range(-0.3f, 0.3f), Origin.y + y, 0f), new Color(0.95f, 0.9f, 0.75f), 3, 3f, 0.06f, 0.8f);
            for (int i = enemies.Count - 1; i >= 0; i--)
                if (i < enemies.Count) Damage(enemies[i], dmg, false);
            Fx.Shake(0.3f, 0.25f);
            Sfx.Attack();
        }

        // ---- the workshop ---------------------------------------------------------------

        static int CostOf(Def def, int toLevel) => DefBaseCost[(int)def] * LevelCostMultiplier[Mathf.Clamp(toLevel - 1, 0, MaxLevel - 1)];

        static int InvestedIn(Def def, int level)
        {
            int sum = 0;
            for (int l = 1; l <= level; l++) sum += CostOf(def, l);
            return sum;
        }

        void SelectLane(int l)
        {
            selectedLane = Mathf.Clamp(l, 0, laneCount - 1);
            RefreshSheet();
        }

        /// <summary>Builds the defence in this lane, or raises it a level if it is already there.</summary>
        void BuildOrUpgrade(int l, Def def)
        {
            if (phase != Phase.Build) return;
            var lane = lanes[l];
            int level = lane.Level(def);
            if (level >= MaxLevel) return;
            int cost = CostOf(def, level + 1);
            if (debris < cost) { Deny(); return; }
            debris -= cost;
            lane.level[(int)def] = level + 1;
            AfterChange(l, level == 0 ? $"{DefNames[(int)def]} bâti !" : $"{DefNames[(int)def]} niveau {level + 1} !", def);
        }

        void AfterChange(int l, string cheer, Def def)
        {
            RefreshLaneVisual(l);
            var at = new Vector3(LaneX(l) + SlotX(def), Origin.y + SlotY(def), 0f);
            Fx.Burst(at, ApogeeTheme.Gold, 20, 3.5f, 0.1f, 0.3f);
            Fx.Text(at + Vector3.up * 1.2f, cheer, ApogeeTheme.Gold, 1f);
            Sfx.Material();
            RefreshSheet();
            RefreshHud();
        }

        void Deny()
        {
            messageText.text = "Pas assez de débris : abats plus de morts !";
            Sfx.Hit();
        }

        int RepairCost => 8 + wave * 2;

        void Repair()
        {
            if (phase != Phase.Build || barricadeHp >= barricadeMax) return;
            if (debris < RepairCost) { Deny(); return; }
            debris -= RepairCost;
            barricadeHp = barricadeMax;
            RefreshPalisade();
            Fx.Burst(new Vector3(Origin.x, Origin.y + BarricadeY + 0.5f, 0f), new Color(0.75f, 0.55f, 0.3f), 24, 3f, 0.1f, 0.4f);
            Sfx.Material();
            RefreshSheet();
            RefreshHud();
        }

        void OnBurstAd()
        {
            if (phase != Phase.Build || burstWaves > 0 || adPending) return;
            adPending = true;
            ui.ShowAdOverlay(true, "Publicité en cours...");
            RefreshSheet();
            AdService.ShowRewardedAd(() =>
            {
                adPending = false;
                ui.ShowAdOverlay(false);
                burstWaves = BurstWaves;
                Sfx.Milestone();
                ui.ShowBanner("TIR RAPIDE !", $"Cadence x3 pendant {BurstWaves} vagues", 2f);
                RefreshSheet();
                RefreshHud();
            }, () =>
            {
                adPending = false;
                ui.ShowAdOverlay(false);
                messageText.text = "Publicité indisponible, réessaie plus tard";
                RefreshSheet();
            });
        }

        void RefreshSheet()
        {
            if (sheet == null || !sheet.activeInHierarchy) return;
            var lane = lanes[selectedLane];
            sheetTitle.text = $"COULOIR {selectedLane + 1}";
            sheetInfo.text = "Bâtis et améliore ses trois défenses : elles se cumulent";
            for (int i = 0; i < 3; i++)
            {
                var def = (Def)(i + 1);
                int level = lane.Level(def);
                bool maxed = level >= MaxLevel;
                int cost = maxed ? 0 : CostOf(def, level + 1);
                int tier = level >= 5 ? 2 : level >= 3 ? 1 : 0;
                choice[i].icon.sprite = Art($"def_{DefArt[i + 1]}_{tier}", new Vector2(0.5f, 0.5f));
                choice[i].icon.color = level > 0 ? Color.white : new Color(1f, 1f, 1f, 0.55f);
                choice[i].level.text = level > 0 ? $"niv. {level}/{MaxLevel}" : "à bâtir";
                choice[i].cost.text = maxed ? "MAX" : level == 0 ? $"BÂTIR {cost} [d]" : $"+1 : {cost} [d]";
                choice[i].button.interactable = !maxed && debris >= cost;
            }
            bool hurt = barricadeHp < barricadeMax - 0.5f;
            repairLabel.text = hurt ? $"RÉPARER  {RepairCost} [d]" : "PALISSADE OK";
            repairButton.interactable = hurt && debris >= RepairCost;
            burstLabel.text = adPending ? "PUB..." : burstWaves > 0 ? $"TIR RAPIDE ({burstWaves})" : "TIR RAPIDE  (pub)";
            burstButton.interactable = !adPending && burstWaves <= 0;
            if (launchButton != null) launchButton.interactable = !adPending;
        }

        // ---- HUD --------------------------------------------------------------------------

        void RefreshHud()
        {
            if (waveText == null) return;
            waveText.text = phase == Phase.Build ? $"AVANT LA VAGUE {wave}" : $"VAGUE {wave}";
            landText.text = $"{Lands[land].name}   ·   record {BarrSave.Best(land)}";
            debrisChip.text = $"{debris} [d]";
            shardChip.text = shardsThisRun > 0 ? $"{BarrSave.Shards} [e]  (+{shardsThisRun})" : $"{BarrSave.Shards} [e]";
            float f = barricadeMax > 0f ? barricadeHp / barricadeMax : 0f;
            barricadeFill.fillAmount = f;
            barricadeFill.color = f > 0.5f ? new Color(0.86f, 0.55f, 0.22f) : f > 0.25f ? new Color(0.95f, 0.4f, 0.15f) : new Color(0.9f, 0.18f, 0.12f);
            hpLabel.text = $"PALISSADE  {Mathf.CeilToInt(barricadeHp)} / {Mathf.CeilToInt(barricadeMax)}";
            if (phase == Phase.Build) RefreshSheet();
        }
    }

    /// <summary>
    /// Multi-touch lanes for the Barricade: a finger's x picks the lane, several fingers
    /// hold several lanes, and sliding a finger across switches lane.
    /// </summary>
    public class LaneTouchZone : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public Func<Vector2, int> LaneAt;
        readonly Dictionary<int, int> pointerLane = new();

        public bool IsHeld(int lane)
        {
            foreach (var kv in pointerLane)
                if (kv.Value == lane) return true;
            return false;
        }

        public void Clear() => pointerLane.Clear();
        public void OnPointerDown(PointerEventData e) => Track(e);
        public void OnDrag(PointerEventData e) => Track(e);
        public void OnPointerUp(PointerEventData e) => pointerLane.Remove(e.pointerId);
        void OnDisable() => pointerLane.Clear();

        void Track(PointerEventData e)
        {
            int lane = LaneAt != null ? LaneAt(e.position) : -1;
            if (lane < 0) pointerLane.Remove(e.pointerId);
            else pointerLane[e.pointerId] = lane;
        }
    }
}
