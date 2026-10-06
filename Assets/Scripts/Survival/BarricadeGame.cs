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
    /// "BARRICADE", renewed: hold the palisade at the foot of a few cobbled lanes that come
    /// down out of a misty wood, at dusk, on a floating island.
    ///
    /// Made to be understood at a glance:
    ///  - Hold a lane to shoot up it.
    ///  - Each lane has ONE defence, built and upgraded between waves by tapping the lane:
    ///    Pieux (stakes that wound whatever walks over them), Brasier (a fire pit that sets
    ///    them alight) or Arbalète (a ballista that shoots up the lane on its own), level 1 to 5.
    ///  - Four kinds of dead, each introduced the first time it shows up: the Rôdeur, the
    ///    quick Furtif, the Feu-follet that floats over the ground defences and spits from
    ///    afar, and the Colosse that wrecks the defence of its lane for the rest of the wave.
    ///    Every fifth wave a Colosse leads the charge.
    ///  - Kills pay débris, spent on defences, on repairing the palisade and on opening a
    ///    fourth and a fifth lane. A defence at level 5 produces materials, in real time,
    ///    whatever screen is open - that is what the mode pays: materials, never coins.
    ///  - A fall only resets the wave and the palisade: the defences built stay.
    ///
    /// Older saves (four traps per lane, up to nine lanes) are converted on load: each lane
    /// keeps its best trap as the matching defence, at the same level.
    ///
    /// The field lives far from the runner (around x = 3000, y = 1500), painted
    /// (Resources/Barricade), and borrows the main camera while open.
    /// </summary>
    public class BarricadeGame : MiniGame
    {
        public override string Id => "barricade";
        public override string Title => "BARRICADE";
        public override string Description => "Tiens la palissade, bâtis tes défenses, récolte des matériaux";

        public override string BestLine
        {
            get
            {
                string best = SaveSystem.BarricadeBestWave > 0 ? $"Meilleure vague : {SaveSystem.BarricadeBestWave}" : "Aucune vague tenue";
                int stock = Mathf.FloorToInt(farmStock);
                return stock > 0 ? $"{best}   ·   {stock} [g] à récupérer" : best;
            }
        }

        // ---- layout (world units, relative to Origin) ------------------------------------
        static readonly Vector2 Origin = new Vector2(3000f, 1500f);
        const int MinLanes = 3, MaxLanes = 5;
        const float CamY = 0.9f, CameraOrtho = 5.6f;
        const float DefY = -1.7f, ZoneUp = 0.75f, ZoneDown = 0.45f;
        const float BarricadeY = -3.3f, PlayerY = -4.55f, FolletStopY = 1.4f;
        const float Ppu = 1024f / 6f;

        int laneCount = MinLanes;
        float spacing = 1.5f;
        float spawnY = 5f;
        float LaneX(int l) => Origin.x + (l - (laneCount - 1) * 0.5f) * spacing;
        float FieldWidth => spacing * laneCount;
        float LaneScale => Mathf.Min(1f, spacing / 1.5f);
        static float SpacingFor(int lanes) => lanes >= 5 ? 1.22f : lanes == 4 ? 1.4f : 1.55f;

        enum Foe { Rodeur, Furtif, Follet, Colosse }
        enum Def { None, Pieux, Brasier, Arbalete }
        enum Phase { Build, Wave, Over }

        class Enemy
        {
            public Foe kind;
            public int lane;
            public float y, x, speed, hp, maxHp, dmg, interval, attackTimer;
            public float burn, burnDps, burnTick, trapTick, anim, size;
            public int debris;
            public bool boss, smashed;
            public GameObject go;
            public SpriteRenderer sr;
            public Sprite[] frames;
            public Transform hpFill;
            public GameObject hpBar;
            public float flash;
            public bool Flies => kind == Foe.Follet;
        }

        class Shot
        {
            public int lane;
            public float y, speed, damage;
            public GameObject go;
        }

        class Lane
        {
            public Def def;
            public int level;
            public bool disabled;
            public float turretTimer;
            public GameObject visual;
            public SpriteRenderer defSr, ringSr;
            public readonly List<SpriteRenderer> pips = new();
            public bool Farming => def != Def.None && level >= MaxLevel;
        }

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
        const float MolotovCooldown = 18f;
        const int BossEvery = 5;
        const int Expand4Cost = 300, Expand5Cost = 900;
        /// <summary>One material every four minutes per level-5 defence, 15 an hour.</summary>
        const float FarmPerLanePerMinute = 0.25f;
        const float FarmCapMinutes = 240f;
        const float PrestigeFarmBonus = 0.1f;

        float ShotDamage => Mathf.Max(1f, UpgradeManager.FirePowerMultiplier);
        float FarmRatePerLane => FarmPerLanePerMinute * (1f + SaveSystem.BarricadePrestige * PrestigeFarmBonus);

        /// <summary>
        /// Health multiplier on every one of the dead: it climbs with the best wave ever held,
        /// and past wave 10 it compounds a little each wave, so no base stays comfortable for ever.
        /// </summary>
        float HealthScale => (1f + Mathf.Min(SaveSystem.BarricadeBestWave, 40) * 0.012f) * (wave > 10 ? Mathf.Pow(1.035f, wave - 10) : 1f);

        // ---- state ---------------------------------------------------------------------
        Transform root;
        readonly List<Enemy> enemies = new();
        readonly List<Shot> shots = new();
        readonly List<GameObject> spits = new();
        readonly Lane[] lanes = new Lane[MaxLanes];
        readonly float[] laneFireCooldown = new float[MaxLanes];
        readonly HashSet<Foe> introduced = new();
        LaneTouchZone touchZone;

        Phase phase = Phase.Over;
        int wave, spawnRemaining, debris, kills, colossiKilled;
        float spawnTimer, spawnInterval;
        float barricadeHp, barricadeMax;
        float molotovCooldown;
        int burstWaves;
        bool adPending, bossWave;
        int selectedLane;
        SpriteRenderer palisadeSr, heroSr;
        int palisadeState = -1;
        GameObject laneHighlight;
        Drone drone;

        float farmStock, farmSaveTimer;
        int farmingLanes;

        // ---- UI ----------------------------------------------------------------------------
        Text waveText, messageText, hpLabel, sheetTitle, sheetInfo, molotovTimer;
        IconText debrisChip, farmChip;
        Image barricadeFill, molotovFill;
        Button collectButton, launchButton, molotovButton, repairButton, expandButton, burstButton;
        IconText repairLabel, expandLabel, burstLabel;
        GameObject sheet, emptyCards, builtCard, overPanel, hintRoot;
        Text overTitle;
        IconText overBody;
        readonly (Button button, Text name, IconText cost)[] choice = new (Button, Text, IconText)[3];
        Image builtIcon;
        Text builtLevel, builtBlurb;
        Button upgradeButton, sellButton;
        IconText upgradeLabel, sellLabel;
        float farmTextTimer;

        static readonly Dictionary<string, Sprite> art = new();
        static readonly Dictionary<string, Sprite[]> sheets = new();

        static Sprite Art(string name, Vector2 pivot, float ppu = Ppu)
        {
            string key = $"{name}|{pivot}|{ppu}";
            if (art.TryGetValue(key, out var s)) return s;
            var tex = Resources.Load<Texture2D>("Barricade/" + name) ?? Resources.Load<Texture2D>("Runner/" + name) ?? Resources.Load<Texture2D>("Bastion/" + name);
            if (tex != null) s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), pivot, ppu, 0, SpriteMeshType.FullRect);
            art[key] = s;
            return s;
        }

        /// <summary>The eight walking frames of one of the dead, feet on the pivot.</summary>
        static Sprite[] Frames(Foe kind)
        {
            string name = kind switch { Foe.Furtif => "foe_furtif", Foe.Follet => "foe_follet", Foe.Colosse => "foe_colosse", _ => "foe_rodeur" };
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

            var rt = UiKit.CreateRect("BarricadePanel", ui.Canvas.transform, Vector2.zero, Vector2.one);
            panel = rt.gameObject;

            // One touch zone over the field: during a wave the finger's lane shoots, between
            // waves it picks the lane to build in.
            var zone = UiKit.CreateRect("LaneTouchZone", rt, new Vector2(0f, 0.11f), new Vector2(1f, 0.86f));
            zone.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0f);
            touchZone = zone.gameObject.AddComponent<LaneTouchZone>();
            touchZone.LaneAt = LaneFromScreen;

            BuildTop(rt);
            BuildSheet(rt);
            BuildWaveControls(rt);
            BuildOver(rt);

            farmStock = SaveSystem.BarricadeFarmStock;
            LoadBase();
        }

        void BuildTop(RectTransform rt)
        {
            var top = UiKit.CreateRect("Top", rt, new Vector2(0f, 0.925f), new Vector2(1f, 1f));
            var shade = UiKit.CreateImage("Shade", top, Vector2.zero, Vector2.one, ApogeeTheme.VerticalFade, new Color(0.1f, 0.03f, 0.04f, 0.85f), false);
            shade.rectTransform.localScale = new Vector3(1f, -1f, 1f);
            shade.raycastTarget = false;
            waveText = UiKit.Outlined(UiKit.CreateText("Wave", top, "", 46, TextAnchor.MiddleLeft, new Vector2(0.04f, 0.1f), new Vector2(0.6f, 0.95f), ApogeeTheme.Gold), 2.5f);
            ui.CreatePauseButton(top, new Vector2(0.74f, 0.2f), new Vector2(0.96f, 0.85f), () => { if (phase != Phase.Over) OpenPause(); });

            debrisChip = Pill(rt, "Debris", new Vector2(0.03f, 0.875f), new Vector2(0.33f, 0.92f), 28);
            farmChip = Pill(rt, "Farm", new Vector2(0.35f, 0.875f), new Vector2(0.68f, 0.92f), 26);
            collectButton = UiKit.CreateButton("Collect", rt, "RÉCOLTER", new Vector2(0.70f, 0.875f), new Vector2(0.97f, 0.92f), OnCollect, 22, new Color(0.22f, 0.40f, 0.24f));

            // The palisade's health, under the counters.
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
        /// and the base's three other actions. The big button to start sits at the bottom.
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

            // Empty lane: three choices.
            var cards = UiKit.CreateRect("Choices", sh, new Vector2(0.03f, 0.27f), new Vector2(0.97f, 0.77f));
            emptyCards = cards.gameObject;
            for (int i = 0; i < 3; i++)
            {
                var kind = (Def)(i + 1);
                float x0 = i / 3f + 0.01f, x1 = (i + 1) / 3f - 0.01f;
                var b = UiKit.CreateButton($"Choice_{i}", cards, "", new Vector2(x0, 0f), new Vector2(x1, 1f), () => Build(selectedLane, kind), 20, UiKit.CardColor);
                var icon = UiKit.CreateImage("Icon", b.transform, new Vector2(0.12f, 0.36f), new Vector2(0.88f, 0.97f), Art($"def_{DefArt[(int)kind]}_0", new Vector2(0.5f, 0.5f)), Color.white);
                icon.raycastTarget = false;
                var name = UiKit.Outlined(UiKit.CreateText("Name", b.transform, DefNames[(int)kind], 24, TextAnchor.MiddleCenter, new Vector2(0.03f, 0.2f), new Vector2(0.97f, 0.38f), ApogeeTheme.Cream), 1.5f);
                UiKit.FitLabel(name, 24);
                var cost = IconText.Create("Cost", b.transform, "", 24, TextAnchor.MiddleCenter, new Vector2(0.03f, 0.02f), new Vector2(0.97f, 0.2f), ApogeeTheme.Gold, 1.5f);
                choice[i] = (b, name, cost);
            }

            // Built lane: what it is, what it does, upgrade / sell.
            var built = UiKit.CreateRect("Built", sh, new Vector2(0.03f, 0.27f), new Vector2(0.97f, 0.77f));
            builtCard = built.gameObject;
            builtIcon = UiKit.CreateImage("Icon", built, new Vector2(0f, 0f), new Vector2(0.34f, 1f), null, Color.white);
            builtIcon.raycastTarget = false;
            builtLevel = UiKit.Outlined(UiKit.CreateText("Level", built, "", 30, TextAnchor.MiddleLeft, new Vector2(0.37f, 0.72f), new Vector2(1f, 1f), ApogeeTheme.Gold), 1.5f);
            UiKit.FitLabel(builtLevel, 30);
            builtBlurb = UiKit.CreateText("Blurb", built, "", 22, TextAnchor.UpperLeft, new Vector2(0.37f, 0.42f), new Vector2(1f, 0.72f), ApogeeTheme.Cream);
            UiKit.FitLabel(builtBlurb, 22);
            upgradeButton = UiKit.CreateButton("Upgrade", built, "", new Vector2(0.37f, 0.02f), new Vector2(0.74f, 0.38f), () => Upgrade(selectedLane), 22);
            upgradeLabel = IconText.OnButton(upgradeButton, 24);
            sellButton = UiKit.CreateButton("Sell", built, "", new Vector2(0.76f, 0.02f), new Vector2(1f, 0.38f), () => Sell(selectedLane), 20, new Color(0.3f, 0.16f, 0.12f));
            sellLabel = IconText.OnButton(sellButton, 20);

            // The base's other actions.
            float w = 0.94f / 3f;
            repairButton = UiKit.CreateButton("Repair", sh, "", new Vector2(0.03f, 0.04f), new Vector2(0.03f + w - 0.01f, 0.22f), Repair, 20, new Color(0.25f, 0.38f, 0.2f));
            repairLabel = IconText.OnButton(repairButton, 20);
            expandButton = UiKit.CreateButton("Expand", sh, "", new Vector2(0.03f + w, 0.04f), new Vector2(0.03f + 2f * w - 0.01f, 0.22f), Expand, 20, new Color(0.42f, 0.3f, 0.12f));
            expandLabel = IconText.OnButton(expandButton, 20);
            burstButton = UiKit.CreateButton("Burst", sh, "", new Vector2(0.03f + 2f * w, 0.04f), new Vector2(0.97f, 0.22f), OnBurstAd, 20, new Color(0.55f, 0.36f, 0.08f));
            burstLabel = IconText.OnButton(burstButton, 20);

            // Start.
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

        void BuildWaveControls(RectTransform rt)
        {
            // The Molotov: a big round button, bottom right, with its cooldown sweeping round.
            var m = UiKit.CreateRect("Molotov", rt, new Vector2(1f, 0f), new Vector2(1f, 0f));
            m.pivot = new Vector2(1f, 0f);
            m.sizeDelta = new Vector2(190f, 190f);
            m.anchoredPosition = new Vector2(-30f, 40f);
            var img = m.gameObject.AddComponent<Image>();
            img.sprite = HubArt.Get("ui_disc") ?? ApogeeTheme.Round;
            img.color = new Color(0.45f, 0.16f, 0.06f, 0.85f);
            molotovButton = m.gameObject.AddComponent<Button>();
            molotovButton.targetGraphic = img;
            molotovButton.onClick.AddListener(OnMolotov);
            m.gameObject.AddComponent<ButtonPop>();
            var ring = HubArt.Get("ui_ring");
            if (ring != null)
            {
                var r = UiKit.CreateImage("Ring", m, Vector2.zero, Vector2.one, ring, Color.white, false);
                r.rectTransform.offsetMin = new Vector2(-6, -6);
                r.rectTransform.offsetMax = new Vector2(6, 6);
                r.raycastTarget = false;
            }
            var flame = UiKit.CreateImage("Flame", m, new Vector2(0.2f, 0.3f), new Vector2(0.8f, 0.9f), Art("def_brasier_1", new Vector2(0.5f, 0.5f)), Color.white);
            flame.raycastTarget = false;
            UiKit.Outlined(UiKit.CreateText("Label", m, "MOLOTOV", 22, TextAnchor.MiddleCenter, new Vector2(0f, 0.06f), new Vector2(1f, 0.32f), ApogeeTheme.Gold), 1.5f);
            molotovFill = UiKit.CreateImage("Cooldown", m, new Vector2(0.04f, 0.04f), new Vector2(0.96f, 0.96f), HubArt.Get("ui_disc") ?? ApogeeTheme.Round, new Color(0.05f, 0.02f, 0.02f, 0.6f), false);
            molotovFill.type = Image.Type.Filled;
            molotovFill.fillMethod = Image.FillMethod.Radial360;
            molotovFill.raycastTarget = false;
            molotovTimer = UiKit.Outlined(UiKit.CreateText("Timer", m, "", 48, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, ApogeeTheme.Cream), 2f);

            var hint = UiKit.CreateRect("Hint", rt, new Vector2(0.04f, 0.03f), new Vector2(0.75f, 0.1f));
            hintRoot = hint.gameObject;
            UiKit.Outlined(UiKit.CreateText("Text", hint, "Maintiens un couloir pour tirer dedans", 26, TextAnchor.MiddleLeft, Vector2.zero, Vector2.one, ApogeeTheme.Cream), 2f);
        }

        void BuildOver(RectTransform rt)
        {
            var overRt = UiKit.CreatePanel("BarricadeOver", rt, UiKit.Overlay);
            overPanel = overRt.gameObject;
            UiKit.CreateFrame("Frame", overRt, new Vector2(0.08f, 0.26f), new Vector2(0.92f, 0.76f));
            overTitle = UiKit.Outlined(UiKit.CreateText("Title", overRt, "LA PALISSADE EST TOMBÉE", 44, TextAnchor.MiddleCenter, new Vector2(0.1f, 0.63f), new Vector2(0.9f, 0.74f), ApogeeTheme.Gold), 2.5f);
            UiKit.FitLabel(overTitle, 44);
            overBody = IconText.Create("Body", overRt, "", 30, TextAnchor.MiddleCenter, new Vector2(0.1f, 0.45f), new Vector2(0.9f, 0.62f), ApogeeTheme.Cream);
            UiKit.CreateButton("Retry", overRt, "REJOUER", new Vector2(0.2f, 0.36f), new Vector2(0.8f, 0.43f), ResetGame, 34);
            UiKit.CreateButton("Menu", overRt, "MENU", new Vector2(0.3f, 0.28f), new Vector2(0.7f, 0.34f), ReturnToHub, 26, new Color(0.22f, 0.10f, 0.08f));
            overPanel.SetActive(false);
        }

        // ---- the base on disk --------------------------------------------------------------

        /// <summary>
        /// Reads the base, converting an old one on the way: lanes 6 and 9 become 4 and 5, and
        /// each lane keeps its best trap as one defence - spikes and wire as Pieux, the toxic
        /// pool as Brasier, the turret as Arbalète - at the same level.
        /// </summary>
        void LoadBase()
        {
            int saved = PlayerPrefs.GetInt("barricade_lanes_v2", 0);
            laneCount = saved >= MinLanes ? Mathf.Clamp(saved, MinLanes, MaxLanes)
                : SaveSystem.BarricadeLanes >= 9 ? 5 : SaveSystem.BarricadeLanes >= 6 ? 4 : 3;
            PlayerPrefs.SetInt("barricade_lanes_v2", laneCount);
            for (int l = 0; l < MaxLanes; l++)
            {
                int pieux = Mathf.Max(SaveSystem.GetBarricadeTrap(l, 0), SaveSystem.GetBarricadeTrap(l, 2));
                int brasier = SaveSystem.GetBarricadeTrap(l, 1);
                int arbalete = SaveSystem.GetBarricadeTrap(l, 3);
                var lane = lanes[l];
                lane.def = Def.None; lane.level = 0;
                if (arbalete > 0 && arbalete >= brasier && arbalete >= pieux) { lane.def = Def.Arbalete; lane.level = arbalete; }
                else if (brasier > 0 && brasier >= pieux) { lane.def = Def.Brasier; lane.level = brasier; }
                else if (pieux > 0) { lane.def = Def.Pieux; lane.level = pieux; }
                lane.level = Mathf.Clamp(lane.level, 0, MaxLevel);
                if (l >= laneCount) { lane.def = Def.None; lane.level = 0; }
                SaveLane(l);
            }
            SaveSystem.Flush();
            farmingLanes = CountFarming();
        }

        void SaveLane(int l)
        {
            var lane = lanes[l];
            for (int t = 0; t < 4; t++) SaveSystem.SetBarricadeTrap(l, t, 0);
            int slot = lane.def switch { Def.Pieux => 0, Def.Brasier => 1, Def.Arbalete => 3, _ => -1 };
            if (slot >= 0) SaveSystem.SetBarricadeTrap(l, slot, lane.level);
        }

        int CountFarming()
        {
            int n = 0;
            for (int l = 0; l < laneCount; l++) if (lanes[l].Farming) n++;
            return n;
        }

        // ---- lifecycle -----------------------------------------------------------------

        protected override void OnEnter()
        {
            LoadBase();
            ApplyLayout();
            ResetGame();
        }

        protected override void OnExit()
        {
            phase = Phase.Over;
            ClearUnits();
            if (root != null) Destroy(root.gameObject);
            root = null;
            touchZone?.Clear();
            SaveSystem.BarricadeDebris = debris;
            SaveSystem.BarricadeFarmStock = farmStock;
            SaveSystem.Flush();
        }

        void OnApplicationPause(bool paused)
        {
            if (!paused) return;
            SaveSystem.BarricadeFarmStock = farmStock;
            SaveSystem.Flush();
        }

        void OnApplicationQuit()
        {
            SaveSystem.BarricadeFarmStock = farmStock;
            SaveSystem.Flush();
        }

        /// <summary>Frames the camera on the lanes and paints the field for the current number of lanes.</summary>
        void ApplyLayout()
        {
            ClearUnits();
            if (root != null) Destroy(root.gameObject);
            for (int l = 0; l < MaxLanes; l++) { lanes[l].visual = null; lanes[l].pips.Clear(); }
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
            float viewW = ortho * 2f * (cam != null ? cam.aspect : 0.5625f) + 2f;
            float viewTop = CamY + ortho, viewBottom = CamY - ortho;

            // Golden grass everywhere.
            var grass = Place("Grass", Art("field_grass", new Vector2(0.5f, 0.5f)), new Vector2(0f, CamY), -30);
            grass.drawMode = SpriteDrawMode.Tiled;
            grass.size = new Vector2(viewW + 2f, ortho * 2f + 3f);

            // The cobbled lanes, from the wood down to the palisade.
            float laneTop = spawnY + 1.6f, laneBottom = BarricadeY + 0.2f;
            for (int l = 0; l < laneCount; l++)
            {
                var lane = Place($"Lane_{l}", Art("field_lane", new Vector2(0.5f, 0.5f)), new Vector2(LaneX(l) - Origin.x, (laneTop + laneBottom) / 2f), -25);
                lane.drawMode = SpriteDrawMode.Tiled;
                lane.size = new Vector2(1.5f, laneTop - laneBottom);
                lane.transform.localScale = new Vector3(spacing * 0.86f / 1.5f, 1f, 1f);
            }

            // The misty wood they come out of: its fog sits on the spawn line.
            var forestTex = Resources.Load<Texture2D>("Barricade/field_forest");
            if (forestTex != null)
            {
                float fw = Mathf.Max(viewW + 1f, 7f);
                var forest = Place("Wood", Sprite.Create(forestTex, new Rect(0, 0, forestTex.width, forestTex.height), new Vector2(0.5f, 0.14f), forestTex.width / fw), new Vector2(0f, spawnY), 18);
                forest.transform.position += Vector3.up * 0.0f;
                // A band of shade over the top of the screen behind the wood.
                var dusk = Place("Dusk", ApogeeTheme.VerticalFade, new Vector2(0f, viewTop - 0.6f), 17);
                dusk.color = new Color(0.25f, 0.1f, 0.2f, 0.9f);
                dusk.transform.localScale = new Vector3(viewW / Mathf.Max(0.01f, ApogeeTheme.VerticalFade.bounds.size.x), 2.5f / Mathf.Max(0.01f, ApogeeTheme.VerticalFade.bounds.size.y), 1f);
                dusk.transform.localRotation = Quaternion.Euler(0f, 0f, 180f);
            }

            laneHighlight = Place("Highlight", PlaceholderVisuals.Square(Color.white), new Vector2(0f, (spawnY + BarricadeY) / 2f), -24).gameObject;
            laneHighlight.transform.localScale = new Vector3(spacing * 0.9f, spawnY - BarricadeY + 1f, 1f);
            laneHighlight.GetComponent<SpriteRenderer>().color = new Color(1f, 0.85f, 0.4f, 0.16f);
            laneHighlight.SetActive(false);

            // The terrace behind the palisade, and the palisade itself.
            var floor = Place("Terrace", Art("wall_castle", new Vector2(0.5f, 1f)), new Vector2(0f, BarricadeY - 0.15f), 58);
            floor.drawMode = SpriteDrawMode.Tiled;
            floor.size = new Vector2(viewW + 2f, BarricadeY - 0.15f - (viewBottom - 1f));
            floor.color = new Color(0.62f, 0.5f, 0.5f);
            palisadeSr = Place("Palisade", Art("palisade_0", new Vector2(0.5f, 0.07f)), new Vector2(0f, BarricadeY - 0.35f), 60);
            palisadeSr.drawMode = SpriteDrawMode.Tiled;
            palisadeSr.size = new Vector2(viewW + 2f, 200f / Ppu);
            palisadeState = -1;
            RefreshPalisade();

            // The defender, behind it.
            heroSr = Place("Defender", ui.PlayerSprite, new Vector2(0f, PlayerY), 62);
            var skin = SkinCatalog.Find(SaveSystem.SelectedSkinId);
            var portrait = SkinCatalog.LoadPortrait(skin, out bool custom);
            if (custom) { heroSr.sprite = portrait; heroSr.transform.localScale = Vector3.one * 1.1f; }
            else { heroSr.color = skin.Tint; heroSr.transform.localScale = Vector3.one * 1.15f; }
            SpawnDrone(heroSr.transform);

            for (int l = 0; l < laneCount; l++) BuildLanePad(l);
        }

        /// <summary>
        /// The shop's companion drone over the defender: it fires up whichever lane holds the
        /// dead nearest the palisade, once every couple of seconds for a single point.
        /// </summary>
        void SpawnDrone(Transform defender)
        {
            int level = UpgradeManager.DroneLevel;
            if (level <= 0) return;
            drone = Drone.Create(root, defender, level);
            // Over the terrace and the palisade, not behind them.
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

        /// <summary>The stone pad of a lane's defence, its glowing ring and its five level pips.</summary>
        void BuildLanePad(int l)
        {
            var lane = lanes[l];
            float x = LaneX(l) - Origin.x;
            var holder = new GameObject($"LanePad_{l}").transform;
            holder.SetParent(root, false);
            holder.position = new Vector3(Origin.x + x, Origin.y + DefY, 0f);
            lane.visual = holder.gameObject;
            int order = DepthOrder(DefY);
            var pad = Place("Pad", Art("pad", new Vector2(0.5f, 0.5f), 256f / (spacing * 0.95f)), new Vector2(x, DefY - 0.25f), order - 2, holder);
            pad.color = new Color(1f, 1f, 1f, 0.9f);
            lane.ringSr = Place("Ring", Art("ring", new Vector2(0.5f, 0.5f), 256f / (spacing * 0.95f)), new Vector2(x, DefY - 0.25f), order - 1, holder);
            lane.defSr = Place("Defence", null, new Vector2(x, DefY - 0.5f), order, holder);
            lane.defSr.transform.localScale = Vector3.one * LaneScale;
            for (int k = 0; k < MaxLevel; k++)
            {
                var pip = Place($"Pip_{k}", PlaceholderVisuals.Star(), new Vector2(x + (k - 2) * 0.2f * LaneScale, DefY - 0.72f), order + 1, holder);
                pip.transform.localScale = Vector3.one * 0.17f * LaneScale;
                lane.pips.Add(pip);
            }
        }

        void RefreshLaneVisual(int l)
        {
            var lane = lanes[l];
            if (lane.defSr == null) return;
            if (lane.def == Def.None)
            {
                lane.defSr.sprite = null;
            }
            else
            {
                int tier = lane.level >= 5 ? 2 : lane.level >= 3 ? 1 : 0;
                lane.defSr.sprite = Art($"def_{DefArt[(int)lane.def]}_{tier}", new Vector2(0.5f, 0.06f), 256f / 1.5f);
            }
            lane.defSr.color = lane.disabled ? new Color(0.35f, 0.32f, 0.36f, 0.9f) : Color.white;
            for (int k = 0; k < lane.pips.Count; k++)
            {
                lane.pips[k].gameObject.SetActive(lane.def != Def.None);
                lane.pips[k].color = k < lane.level ? (lane.Farming ? new Color(0.6f, 1f, 0.55f) : ApogeeTheme.Gold) : new Color(0.25f, 0.15f, 0.12f, 0.8f);
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

        /// <summary>A new defence: wave 1 and a whole palisade, on the base built so far.</summary>
        void ResetGame()
        {
            StopAllCoroutines();
            ClearUnits();
            for (int l = 0; l < MaxLanes; l++) { lanes[l].turretTimer = 0f; lanes[l].disabled = false; laneFireCooldown[l] = 0f; }
            for (int l = 0; l < laneCount; l++) RefreshLaneVisual(l);
            touchZone?.Clear();
            wave = 1;
            debris = SaveSystem.BarricadeDebris;
            kills = 0;
            colossiKilled = 0;
            molotovCooldown = 0f;
            burstWaves = 0;
            adPending = false;
            barricadeMax = 120f + SaveSystem.GetLevel(UpgradeStat.Armor) * 12f;
            barricadeHp = barricadeMax;
            RefreshPalisade();
            overPanel.SetActive(false);
            EnterBuildPhase(first: true);
        }

        void SetDebris(int value)
        {
            debris = Mathf.Max(0, value);
            SaveSystem.BarricadeDebris = debris;
        }

        // ---- phases --------------------------------------------------------------------

        void EnterBuildPhase(bool first)
        {
            phase = Phase.Build;
            sheet.SetActive(true);
            launchButton.gameObject.SetActive(true);
            molotovButton.gameObject.SetActive(false);
            hintRoot.SetActive(false);
            if (laneHighlight != null) laneHighlight.SetActive(true);
            bool anything = false;
            for (int l = 0; l < laneCount; l++) if (lanes[l].def != Def.None) anything = true;
            messageText.text = first
                ? (anything ? "Ta base est prête : renforce-la ou lance la vague" : "Touche un couloir pour y bâtir une défense")
                : $"Vague {wave - 1} repoussée !";
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
            hintRoot.SetActive(wave <= 2);
            if (laneHighlight != null) laneHighlight.SetActive(false);
            float front = 1f + (laneCount - 3) / 4f;
            spawnRemaining = Mathf.RoundToInt((4 + wave * 2) * front);
            spawnInterval = Mathf.Max(0.55f, 1.5f - wave * 0.08f) / front;
            spawnTimer = 0.6f;
            bossWave = wave % BossEvery == 0;
            messageText.text = "";
            if (bossWave) ui.ShowBanner("UN COLOSSE !", $"Vague {wave} : il mène la charge", 2f);
            else ui.ShowBanner($"VAGUE {wave}", $"{spawnRemaining} morts sortent du bois", 1.5f);
            RefreshHud();
        }

        void EndWave()
        {
            int bonus = 5 + wave;
            SetDebris(debris + bonus);
            // The palisade is patched up a little between waves.
            barricadeHp = Mathf.Min(barricadeMax, barricadeHp + barricadeMax * 0.2f);
            RefreshPalisade();
            if (bossWave)
            {
                int mats = 4 + wave / 2;
                SaveSystem.AddMaterials(mats);
                ui.ShowBanner("COLOSSE ABATTU !", $"{mats} [g]   ·   {bonus} [d]", 2.2f);
            }
            if (wave > SaveSystem.BarricadeBestWave) SaveSystem.BarricadeBestWave = wave;
            wave++;
            if (burstWaves > 0) burstWaves--;
            // Defences knocked out by a Colosse are back in order.
            for (int l = 0; l < laneCount; l++)
                if (lanes[l].disabled) { lanes[l].disabled = false; RefreshLaneVisual(l); }
            SaveSystem.Flush();
            Sfx.Milestone();
            RewardPopup.Show(new Vector3(Origin.x, Origin.y + 1f, 0f), 0, 0, bonus);
            EnterBuildPhase(first: false);
        }

        void GameOver()
        {
            phase = Phase.Over;
            sheet.SetActive(false);
            launchButton.gameObject.SetActive(false);
            molotovButton.gameObject.SetActive(false);
            hintRoot.SetActive(false);
            touchZone?.Clear();
            int held = wave - 1;
            int materials = held + colossiKilled * 2;
            if (materials > 0) SaveSystem.AddMaterials(materials);
            SaveSystem.BarricadeDebris = debris;
            SaveSystem.Flush();
            overBody.text = $"Vagues tenues : {held}      Abattus : {kills}\n+{materials} [g]\nTes défenses restent debout   ·   {debris} [d]";
            overPanel.SetActive(true);
            overPanel.transform.SetAsLastSibling();
            Sfx.Death();
            Fx.Shake(0.5f, 0.4f);
            AdService.OnPlayerDeath();
        }

        // ---- per frame -------------------------------------------------------------------

        void Update()
        {
            // The materials keep coming whatever screen is open, as long as the game runs.
            FarmTick(Mathf.Min(Time.unscaledDeltaTime, 1f));
            if (!IsActive || phase == Phase.Over || root == null) return;
            float dt = Time.deltaTime;
            AnimateWorld();

            if (phase == Phase.Build)
            {
                for (int l = 0; l < laneCount; l++)
                    if (touchZone != null && touchZone.IsHeld(l) && l != selectedLane) { SelectLane(l); Sfx.Drop(); }
                farmTextTimer -= dt;
                if (farmTextTimer <= 0f) { farmTextTimer = 0.5f; RefreshHud(); }
                return;
            }

            UpdateSpawning(dt);
            UpdateEnemies(dt);
            UpdateDefences(dt);
            UpdateShots(dt);
            UpdatePlayerFire(dt);
            molotovCooldown = Mathf.Max(0f, molotovCooldown - dt);
            molotovFill.fillAmount = molotovCooldown / MolotovCooldown;
            molotovTimer.text = molotovCooldown > 0f ? Mathf.CeilToInt(molotovCooldown).ToString() : "";

            if (barricadeHp <= 0f) { GameOver(); return; }
            if (spawnRemaining == 0 && enemies.Count == 0) EndWave();
        }

        void AnimateWorld()
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 4f);
            for (int l = 0; l < laneCount; l++)
            {
                var lane = lanes[l];
                if (lane.ringSr == null) continue;
                bool build = phase == Phase.Build;
                lane.ringSr.enabled = build || lane.def == Def.None;
                lane.ringSr.color = l == selectedLane && build
                    ? new Color(1f, 0.85f, 0.4f, 0.6f + 0.4f * pulse)
                    : new Color(1f, 1f, 1f, lane.def == Def.None ? 0.25f + 0.25f * pulse : 0.3f);
            }
            if (laneHighlight != null && laneHighlight.activeSelf)
            {
                var p = laneHighlight.transform.position;
                p.x = Mathf.Lerp(p.x, LaneX(selectedLane), Time.deltaTime * 14f);
                laneHighlight.transform.position = p;
            }
        }

        void UpdateSpawning(float dt)
        {
            if (spawnRemaining <= 0) return;
            spawnTimer -= dt;
            if (spawnTimer > 0f) return;
            spawnTimer = spawnInterval;
            spawnRemaining--;
            bool boss = bossWave && spawnRemaining == Mathf.RoundToInt((4 + wave * 2) * (1f + (laneCount - 3) / 4f)) - 1;
            SpawnEnemy(Random.Range(0, laneCount), boss ? Foe.Colosse : RollKind(), boss);
        }

        Foe RollKind()
        {
            float r = Random.value;
            if (wave >= 6 && r < 0.06f + wave * 0.006f) return Foe.Colosse;
            if (wave >= 4 && r < 0.3f) return Foe.Follet;
            if (wave >= 2 && r < 0.55f) return Foe.Furtif;
            return Foe.Rodeur;
        }

        void SpawnEnemy(int lane, Foe kind, bool boss)
        {
            var e = new Enemy { kind = kind, lane = lane, boss = boss, y = spawnY + Random.Range(0f, 0.4f), x = Random.Range(-0.18f, 0.18f) * spacing };
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
                    if (boss) { e.hp *= 2.2f; e.size = 1.25f; e.debris = 20; }
                    break;
                default:
                    e.hp = 2f + wave * 0.5f; e.speed = 1.2f; e.dmg = 6f; e.interval = 1.2f; e.debris = 2; e.size = 1f;
                    break;
            }
            e.hp *= HealthScale;
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
            e.hpBar.SetActive(false);

            PlaceEnemy(e);
            enemies.Add(e);

            if (!introduced.Contains(kind) && kind != Foe.Rodeur && !boss)
            {
                introduced.Add(kind);
                var (title, line) = kind switch
                {
                    Foe.Furtif => ("FURTIF !", "Rapide et fragile : tire vite"),
                    Foe.Follet => ("FEU-FOLLET !", "Il flotte au-dessus des Pieux et du Brasier, et crache de loin"),
                    _ => ("COLOSSE !", "Il met hors service la défense de son couloir"),
                };
                ui.ShowBanner(title, line, 2.2f);
            }
        }

        /// <summary>Puts a body where its numbers say: nearer is bigger and drawn in front.</summary>
        void PlaceEnemy(Enemy e)
        {
            float near = Mathf.InverseLerp(spawnY, BarricadeY, e.y);
            float scale = Mathf.Lerp(0.72f, 1f, near) * e.size * LaneScale;
            float hover = e.Flies ? 0.45f + Mathf.Sin(Time.time * 2.3f + e.lane) * 0.08f : 0f;
            e.go.transform.position = new Vector3(LaneX(e.lane) + e.x * near, Origin.y + e.y + hover, 0f);
            e.go.transform.localScale = Vector3.one * scale;
            e.sr.sortingOrder = DepthOrder(e.y);
            if (e.hpBar != null)
            {
                e.hpBar.transform.localPosition = new Vector3(0f, (e.kind == Foe.Colosse ? 2.1f : 1.55f), 0f);
                foreach (var r in e.hpBar.GetComponentsInChildren<SpriteRenderer>()) r.sortingOrder = DepthOrder(e.y) + 1;
            }
        }

        void UpdateEnemies(float dt)
        {
            for (int i = enemies.Count - 1; i >= 0; i--)
            {
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

                // The defence of the lane.
                bool inZone = e.y < DefY + ZoneUp && e.y > DefY - ZoneDown;
                float slow = 0f;
                if (inZone && lane.def != Def.None)
                {
                    if (e.kind == Foe.Colosse && !e.smashed && !lane.disabled)
                    {
                        e.smashed = true;
                        lane.disabled = true;
                        RefreshLaneVisual(e.lane);
                        var at = new Vector3(LaneX(e.lane), Origin.y + DefY, 0f);
                        Fx.Burst(at, new Color(0.5f, 0.45f, 0.45f), 26, 4.5f, 0.12f, 0.4f);
                        Fx.Shake(0.35f, 0.3f);
                        Fx.Text(at + Vector3.up * 1f, $"{DefNames[(int)lane.def]} hors service !", new Color(1f, 0.45f, 0.3f), 1.2f);
                        Sfx.Kill();
                    }
                    if (!lane.disabled && !e.Flies)
                    {
                        if (lane.def == Def.Pieux)
                        {
                            slow = 0.2f;
                            e.trapTick -= dt;
                            if (e.trapTick <= 0f)
                            {
                                e.trapTick = 0.7f;
                                Damage(e, 0.9f * lane.level * (e.kind == Foe.Colosse ? 0.5f : 1f), true);
                                if (e.hp <= 0f) continue;
                            }
                        }
                        else if (lane.def == Def.Brasier && e.burn <= 0.5f)
                        {
                            e.burn = 3f;
                            e.burnDps = 0.55f * lane.level * (e.kind == Foe.Colosse ? 0.5f : 1f);
                            e.burnTick = 0f;
                        }
                    }
                }

                float stopY = e.kind == Foe.Follet ? FolletStopY : BarricadeY + 0.55f;
                if (e.y > stopY)
                {
                    e.y = Mathf.Max(stopY, e.y - e.speed * (1f - slow) * dt);
                    e.anim += dt * (e.kind == Foe.Furtif ? 13f : 8f);
                }
                else
                {
                    e.anim += dt * 4f;
                    e.attackTimer -= dt;
                    if (e.attackTimer <= 0f)
                    {
                        e.attackTimer = e.interval;
                        if (e.kind == Foe.Follet) Spit(e);
                        else HitPalisade(e.dmg, e.go.transform.position + Vector3.up * 0.4f);
                    }
                }
                if (e.frames != null) e.sr.sprite = e.frames[(int)e.anim % e.frames.Length];
                e.flash = Mathf.Max(0f, e.flash - dt);
                var tint = e.burn > 0f ? new Color(1f, 0.72f, 0.55f) : Color.white;
                e.sr.color = e.flash > 0f ? new Color(1f, 0.45f, 0.35f) : tint;
                PlaceEnemy(e);
            }
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
            RefreshHud();
        }

        void UpdateDefences(float dt)
        {
            for (int l = 0; l < laneCount; l++)
            {
                var lane = lanes[l];
                if (lane.def != Def.Arbalete || lane.disabled) continue;
                lane.turretTimer -= dt;
                if (lane.turretTimer > 0f) continue;
                if (FirstInLane(l, DefY) == null) continue;
                lane.turretTimer = 1.25f / (1f + 0.4f * (lane.level - 1));
                FireShot(l, DefY + 0.9f, 1f + 0.6f * (lane.level - 1), false, true);
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
            bool burst = burstWaves > 0;
            float rate = FireRate * (burst ? BurstMultiplier : 1f);
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
                FireShot(l, PlayerY + 1.2f, ShotDamage, burst);
                Sfx.Shoot();
                if (heroSr != null) heroSr.flipX = LaneX(l) < heroSr.transform.position.x - 0.1f;
                if (hintRoot.activeSelf && wave > 1) hintRoot.SetActive(false);
            }
        }

        void FireShot(int lane, float fromY, float damage, bool burst, bool bolt = false)
        {
            var go = new GameObject(bolt ? "Bolt" : "Shot");
            go.transform.SetParent(root, false);
            go.transform.position = new Vector3(LaneX(lane), Origin.y + fromY, 0f);
            go.transform.localScale = bolt ? new Vector3(0.09f, 0.5f, 1f) : burst ? new Vector3(0.15f, 0.5f, 1f) : new Vector3(0.11f, 0.42f, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = PlaceholderVisuals.Square(Color.white);
            sr.color = bolt ? new Color(0.92f, 0.86f, 0.72f) : burst ? new Color(1f, 0.78f, 0.25f) : new Color(1f, 0.9f, 0.55f);
            sr.sortingOrder = 72;
            shots.Add(new Shot { lane = lane, y = fromY, speed = ShotSpeed, damage = damage, go = go });
        }

        void UpdateShots(float dt)
        {
            for (int i = shots.Count - 1; i >= 0; i--)
            {
                var s = shots[i];
                s.y += s.speed * dt;
                var p = s.go.transform.position;
                p.y = Origin.y + s.y;
                s.go.transform.position = p;
                Enemy hit = null;
                foreach (var e in enemies)
                {
                    float body = e.y + (e.Flies ? 0.9f : 0.6f);
                    if (e.lane == s.lane && Mathf.Abs(body - s.y) < 0.6f) { hit = e; break; }
                }
                if (hit != null)
                {
                    Damage(hit, s.damage, false);
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
            kills++;
            if (e.kind == Foe.Colosse) colossiKilled++;
            SetDebris(debris + e.debris);
            Fx.Burst(pos, new Color(0.45f, 0.18f, 0.35f), e.kind == Foe.Colosse ? 26 : 12, 3.5f, 0.11f);
            RewardPopup.Show(pos, 0, 0, e.debris);
            Sfx.Kill();
            enemies.Remove(e);
            Destroy(e.go);
            RefreshHud();
        }

        // ---- the Molotov ----------------------------------------------------------------

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
            molotovCooldown = MolotovCooldown;
            foreach (var e in enemies)
            {
                if (e.lane != best) continue;
                e.burn = 4f;
                e.burnDps = 1.6f + wave * 0.15f;
                e.burnTick = 0f;
            }
            for (float y = BarricadeY + 1f; y < spawnY; y += 0.8f)
                Fx.Burst(new Vector3(LaneX(best), Origin.y + y, 0f), new Color(1f, 0.55f, 0.15f), 6, 2.5f, 0.12f, -0.6f);
            Fx.Shake(0.25f, 0.2f);
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

        void Build(int l, Def def)
        {
            if (phase != Phase.Build) return;
            var lane = lanes[l];
            if (lane.def != Def.None) return;
            int cost = CostOf(def, 1);
            if (debris < cost) { Deny(); return; }
            SetDebris(debris - cost);
            lane.def = def;
            lane.level = 1;
            AfterChange(l, $"{DefNames[(int)def]} bâti !");
        }

        void Upgrade(int l)
        {
            if (phase != Phase.Build) return;
            var lane = lanes[l];
            if (lane.def == Def.None || lane.level >= MaxLevel) return;
            int cost = CostOf(lane.def, lane.level + 1);
            if (debris < cost) { Deny(); return; }
            SetDebris(debris - cost);
            lane.level++;
            AfterChange(l, lane.level >= MaxLevel ? "Niveau 5 : produit des matériaux !" : $"Niveau {lane.level} !");
        }

        void Sell(int l)
        {
            if (phase != Phase.Build) return;
            var lane = lanes[l];
            if (lane.def == Def.None) return;
            SetDebris(debris + InvestedIn(lane.def, lane.level) / 2);
            lane.def = Def.None;
            lane.level = 0;
            AfterChange(l, "Défense vendue");
        }

        void AfterChange(int l, string cheer)
        {
            SaveLane(l);
            SaveSystem.Flush();
            farmingLanes = CountFarming();
            RefreshLaneVisual(l);
            var at = new Vector3(LaneX(l), Origin.y + DefY, 0f);
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

        void Repair()
        {
            if (phase != Phase.Build || barricadeHp >= barricadeMax) return;
            int cost = RepairCost;
            if (debris < cost) { Deny(); return; }
            SetDebris(debris - cost);
            barricadeHp = barricadeMax;
            RefreshPalisade();
            Fx.Burst(new Vector3(Origin.x, Origin.y + BarricadeY + 0.5f, 0f), new Color(0.75f, 0.55f, 0.3f), 24, 3f, 0.1f, 0.4f);
            Sfx.Material();
            RefreshSheet();
            RefreshHud();
        }

        int RepairCost => 8 + wave * 2;

        int ExpandCost => laneCount == 3 ? Expand4Cost : Expand5Cost;

        void Expand()
        {
            if (phase != Phase.Build || laneCount >= MaxLanes) return;
            if (debris < ExpandCost) { Deny(); return; }
            SetDebris(debris - ExpandCost);
            laneCount++;
            PlayerPrefs.SetInt("barricade_lanes_v2", laneCount);
            lanes[laneCount - 1].def = Def.None;
            lanes[laneCount - 1].level = 0;
            SaveLane(laneCount - 1);
            SaveSystem.Flush();
            ApplyLayout();
            for (int l = 0; l < laneCount; l++) RefreshLaneVisual(l);
            ui.ShowBanner("NOUVEAU COULOIR !", $"{laneCount} couloirs : plus de morts, plus de débris", 2f);
            selectedLane = laneCount - 1;
            EnterBuildPhase(first: false);
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
            if (sheet == null) return;
            var lane = lanes[selectedLane];
            sheetTitle.text = $"COULOIR {selectedLane + 1}";
            bool empty = lane.def == Def.None;
            emptyCards.SetActive(empty);
            builtCard.SetActive(!empty);
            if (empty)
            {
                sheetInfo.text = "Choisis une défense pour ce couloir";
                for (int i = 0; i < 3; i++)
                {
                    var def = (Def)(i + 1);
                    int cost = CostOf(def, 1);
                    choice[i].cost.text = $"{cost} [d]";
                    choice[i].button.interactable = debris >= cost;
                }
            }
            else
            {
                sheetInfo.text = lane.Farming ? "Au niveau 5, elle produit des matériaux" : "Améliore-la jusqu'au niveau 5 : elle produira des matériaux";
                int tier = lane.level >= 5 ? 2 : lane.level >= 3 ? 1 : 0;
                builtIcon.sprite = Art($"def_{DefArt[(int)lane.def]}_{tier}", new Vector2(0.5f, 0.5f));
                builtIcon.preserveAspect = true;
                builtLevel.text = $"{DefNames[(int)lane.def].ToUpperInvariant()}   niv. {lane.level}/{MaxLevel}";
                builtBlurb.text = DefBlurb[(int)lane.def];
                bool maxed = lane.level >= MaxLevel;
                int cost = maxed ? 0 : CostOf(lane.def, lane.level + 1);
                upgradeLabel.text = maxed ? "MAXIMUM" : $"AMÉLIORER  {cost} [d]";
                upgradeButton.interactable = !maxed && debris >= cost;
                sellLabel.text = $"VENDRE  +{InvestedIn(lane.def, lane.level) / 2} [d]";
            }
            bool hurt = barricadeHp < barricadeMax - 0.5f;
            repairLabel.text = hurt ? $"RÉPARER  {RepairCost} [d]" : "PALISSADE OK";
            repairButton.interactable = hurt && debris >= RepairCost;
            expandLabel.text = laneCount < MaxLanes ? $"+ COULOIR  {ExpandCost} [d]" : "5 COULOIRS";
            expandButton.interactable = laneCount < MaxLanes && debris >= ExpandCost;
            burstLabel.text = adPending ? "PUB..." : burstWaves > 0 ? $"TIR RAPIDE ({burstWaves})" : "TIR RAPIDE  (pub)";
            burstButton.interactable = !adPending && burstWaves <= 0;
            if (launchButton != null) launchButton.interactable = !adPending;
        }

        // ---- the farm ------------------------------------------------------------------

        void FarmTick(float dt)
        {
            if (farmingLanes <= 0) return;
            float cap = FarmRatePerLane * farmingLanes * FarmCapMinutes;
            farmStock = Mathf.Min(cap, farmStock + FarmRatePerLane * farmingLanes * dt / 60f);
            farmSaveTimer -= dt;
            if (farmSaveTimer <= 0f)
            {
                farmSaveTimer = 10f;
                SaveSystem.BarricadeFarmStock = farmStock;
            }
        }

        void OnCollect()
        {
            int n = Mathf.FloorToInt(farmStock);
            if (n <= 0) return;
            farmStock -= n;
            SaveSystem.AddMaterials(n);
            SaveSystem.BarricadeFarmStock = farmStock;
            SaveSystem.Flush();
            RewardPopup.Show(new Vector3(Origin.x, Origin.y + 2f, 0f), 0, n, 0);
            Sfx.Material();
            RefreshHud();
        }

        // ---- HUD --------------------------------------------------------------------------

        void RefreshHud()
        {
            if (waveText == null) return;
            waveText.text = phase == Phase.Wave ? $"VAGUE {wave}" : phase == Phase.Build ? $"AVANT LA VAGUE {wave}" : $"VAGUE {wave}";
            debrisChip.text = $"{debris} [d]";
            int stock = Mathf.FloorToInt(farmStock);
            farmChip.text = farmingLanes > 0 ? $"{stock} [g]  ·  {farmingLanes} en prod." : "niv. 5 = [g]";
            collectButton.interactable = stock > 0;
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
        /// <summary>Screen position to lane index, or -1 for none.</summary>
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
