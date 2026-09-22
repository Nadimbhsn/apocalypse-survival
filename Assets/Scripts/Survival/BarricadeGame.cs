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
    /// "BARRICADE": wave defense built for a portrait phone, around a base the player keeps.
    ///
    /// Zombies come down vertical lanes toward a barricade near the bottom, behind which the
    /// equipped character stands; hold a lane to shoot up it. Kills pay "débris", spent
    /// between waves on per-lane traps (spikes, toxic pool, barbed wire, turret), on repairs,
    /// and on widening the field from 3 lanes to 6 and then 9.
    ///
    /// The base persists: traps, débris and the number of lanes are saved, and a lost run
    /// only resets the wave and the barricade's health. A lane with all four traps maxed is
    /// "complete" and produces materials in real time for as long as the game is running,
    /// whichever screen is open - more complete lanes, more materials. That is the mode's
    /// reward: it pays materials only, never coins, so coins stay the business of the other
    /// games.
    ///
    /// Specials during a wave: a Molotov that sets the busiest lane on fire, reinforcements
    /// that march up the busiest lanes, and a burst weapon unlocked for three waves by
    /// watching an ad. From wave 10 a rare bomber lobs bombs that hit the barricade hard and
    /// knock one trap in its lane down a level.
    ///
    /// The lanes live in world space far from the runner (around x = 3000, y = 1500) and
    /// borrow the main camera while active.
    /// </summary>
    public class BarricadeGame : MiniGame
    {
        public override string Id => "barricade";
        public override string Title => "BARRICADE";
        public override string Description => "Bâtis ta base, tiens les vagues, récolte des matériaux";

        public override string BestLine
        {
            get
            {
                string best = SaveSystem.BarricadeBestWave > 0 ? $"Meilleure vague : {SaveSystem.BarricadeBestWave}" : "Aucune vague tenue";
                int stock = Mathf.FloorToInt(farmStock);
                return stock > 0 ? $"{best}   ·   {stock} mat. à récupérer" : best;
            }
        }

        // ---- layout (world units, relative to Origin) ----
        static readonly Vector2 Origin = new Vector2(3000f, 1500f);
        const int MaxLanes = 9;
        const float BaseSpawnY = 6.8f;
        const float BarricadeY = -3.3f;
        const float PlayerY = -4.5f;
        const float TrapY = -0.6f;
        const float TrapHalfHeight = 0.7f;
        const float SpitterStopY = 1.8f;
        const float BomberStopY = 2.7f;
        const float CameraOrtho = 5.6f;

        int laneCount = 3;
        float laneWidth = 2.1f;
        /// <summary>Where zombies appear: just above the top of the view, whatever the zoom.</summary>
        float spawnY = BaseSpawnY;
        /// <summary>Speed multiplier that keeps the walk to the barricade the same duration however far up they spawn.</summary>
        float walkScale = 1f;
        /// <summary>Shrinks bodies a little when nine lanes make each one narrow.</summary>
        float modelScale = 1f;

        /// <summary>A wider field gets narrower lanes, so nine still fit a portrait phone.</summary>
        static float WidthFor(int count) => count >= 9 ? 1.05f : count >= 6 ? 1.4f : 2.1f;
        float LaneX(int lane) => Origin.x + (lane - (laneCount - 1) * 0.5f) * laneWidth;
        float FieldWidth => laneWidth * laneCount;
        /// <summary>Trap and prop offsets were tuned on 2.1-wide lanes; this scales them to the current width.</summary>
        float LaneK => laneWidth / 2.1f;

        enum Kind { Walker, Runner, Spitter, Brute, Bomber }
        enum Phase { Build, Wave, Over }
        enum TrapType { Spikes, Toxic, Wire, Turret }

        class Enemy
        {
            public Renderer[] model;
            public Color blood;
            public Kind kind;
            public int lane;
            public float y, speed, hp, maxHp;
            public float attackDamage, attackInterval, attackTimer;
            public int debris;
            public float burnTimer, burnTick;
            public float tickTimer;
            public GameObject go;
            public SpriteRenderer sr;
            public Color baseColor;
            // bomber only
            public bool brokeTrap;
            public GameObject heldBomb;
            public SpriteRenderer fuse;
            public float bombHidden;
        }

        class Shot
        {
            public int lane;
            public float y, speed, damage;
            public GameObject go;
        }

        class Lane
        {
            public readonly int[] level = new int[4];
            public readonly GameObject[] visuals = new GameObject[4];
            public float turretTimer;

            public bool Complete
            {
                get { for (int t = 0; t < 4; t++) if (level[t] < MaxTrapLevel) return false; return true; }
            }

            public int MaxedTraps
            {
                get { int n = 0; for (int t = 0; t < 4; t++) if (level[t] >= MaxTrapLevel) n++; return n; }
            }
        }

        class Ally
        {
            public int lane;
            public float y, hp, hitTimer;
            public GameObject go;
        }

        Transform root;
        readonly List<Enemy> enemies = new();
        readonly List<Shot> shots = new();
        readonly List<GameObject> spits = new();
        readonly List<Ally> allies = new();
        readonly Lane[] lanes = new Lane[MaxLanes];
        readonly float[] laneFireCooldown = new float[MaxLanes];
        LaneTouchZone touchZone;

        Phase phase = Phase.Over;
        int wave;
        int spawnRemaining;
        float spawnTimer, spawnInterval;
        float buildTimer;
        float barricadeHp, barricadeMax;
        int debris, kills, brutesKilled, bombersKilled;
        float molotovCooldown, reinforceCooldown;
        int burstWaves;
        bool adPending;
        int selectedLane;
        SpriteRenderer barricadeSr;
        Color barricadeBaseColor;
        SpriteRenderer playerSr;
        GameObject laneHighlight;
        Drone drone;

        // materials farm, running whether or not this mode is open
        float farmStock;
        float farmSaveTimer;
        int completedLanes;

        Text waveText, debrisText, buildText, molotovText, reinforceText, messageText, burstText;
        Image barricadeFill;
        GameObject buildPanel, overPanel;
        Text overTitle, overBody;
        Button molotovButton, reinforceButton, repairButton, expandButton, burstButton, collectButton;
        Text repairLabel, expandLabel, burstLabel, farmText, laneInfoText;
        readonly Button[] laneChips = new Button[MaxLanes];
        readonly Button[] trapButtons = new Button[4];
        float farmTextTimer;

        // ---- tuning --------------------------------------------------------------------
        const float FireRate = 3.2f;
        const float BurstMultiplier = 3f;
        const int BurstWaves = 3;
        const float ShotSpeed = 15f;
        const float MolotovCooldown = 20f;
        const float ReinforceCooldown = 35f;
        const float BuildDuration = 14f;
        static readonly string[] TrapNames = { "Pics", "Toxique", "Barbelés", "Tourelle" };
        static readonly int[] TrapBaseCost = { 8, 10, 6, 14 };
        const int MaxTrapLevel = 3;
        const int RepairCost = 10;
        const int RepairAmount = 35;
        const int Expand6Cost = 500, Expand9Cost = 1500;
        /// <summary>
        /// One material every four minutes per complete lane, 15 an hour. Everything the
        /// game sells for materials (armor, drone) adds up to about 655, so a single lane
        /// is a steady trickle and a full nine-lane base clears it in an afternoon.
        /// </summary>
        const float FarmPerLanePerMinute = 0.25f;
        /// <summary>The stock stops filling after four hours of production, so it asks to be collected.</summary>
        const float FarmCapMinutes = 240f;
        const float BombDamage = 25f;
        const int AllySquad = 3;
        const float AllyBaseHp = 20f, AllyDps = 3f, AllySpeed = 1.8f;

        float ShotDamage => Mathf.Max(1f, 1f * UpgradeManager.FirePowerMultiplier);

        // ---- UI ------------------------------------------------------------------------

        protected override void BuildUi()
        {
            for (int i = 0; i < MaxLanes; i++) lanes[i] = new Lane();

            var rt = UiKit.CreateRect("BarricadePanel", ui.Canvas.transform, Vector2.zero, Vector2.one);
            panel = rt.gameObject;

            // One touch zone for the whole field: the finger's x picks the lane, several
            // fingers hold several lanes, and sliding a finger across switches lane.
            var zone = UiKit.CreateRect("LaneTouchZone", rt, new Vector2(0f, 0.13f), new Vector2(1f, 0.86f));
            zone.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0f);
            touchZone = zone.gameObject.AddComponent<LaneTouchZone>();
            touchZone.LaneAt = LaneFromScreen;

            var topBar = UiKit.CreateRect("TopBar", rt, new Vector2(0f, 0.90f), new Vector2(1f, 1f));
            var topImg = topBar.gameObject.AddComponent<Image>();
            topImg.sprite = ApogeeTheme.Panel;
            topImg.type = Image.Type.Sliced;
            waveText = UiKit.CreateText("Wave", topBar, "", 38, TextAnchor.MiddleLeft, new Vector2(0.04f, 0.45f), new Vector2(0.6f, 1f), Color.white);
            debrisText = UiKit.CreateText("Debris", topBar, "", 26, TextAnchor.MiddleLeft, new Vector2(0.04f, 0.05f), new Vector2(0.6f, 0.45f), PlaceholderVisuals.MaterialColor);
            UiKit.CreateText("BarricadeLabel", topBar, "Barricade", 20, TextAnchor.MiddleLeft, new Vector2(0.62f, 0.62f), new Vector2(0.95f, 0.98f), UiKit.Parchment);
            barricadeFill = UiKit.CreateBar("BarricadeHp", topBar, new Vector2(0.62f, 0.30f), new Vector2(0.95f, 0.6f), new Color(0.75f, 0.45f, 0.15f));
            UiKit.CreateButton("Quit", rt, "QUITTER", new Vector2(0.74f, 0.855f), new Vector2(0.96f, 0.895f), ReturnToHub, 20);

            messageText = UiKit.CreateText("Message", rt, "", 26, TextAnchor.MiddleCenter, new Vector2(0.05f, 0.80f), new Vector2(0.95f, 0.85f), UiKit.Gold);
            burstText = UiKit.Outlined(UiKit.CreateText("Burst", rt, "", 24, TextAnchor.MiddleCenter, new Vector2(0.05f, 0.765f), new Vector2(0.95f, 0.80f), ApogeeTheme.Gold));

            molotovButton = UiKit.CreateButton("Molotov", rt, "MOLOTOV", new Vector2(0.04f, 0.03f), new Vector2(0.48f, 0.115f), OnMolotov, 26, new Color(0.62f, 0.25f, 0.08f));
            molotovText = UiKit.ButtonLabel(molotovButton);
            reinforceButton = UiKit.CreateButton("Reinforce", rt, "RENFORTS", new Vector2(0.52f, 0.03f), new Vector2(0.96f, 0.115f), OnReinforce, 26, new Color(0.18f, 0.32f, 0.42f));
            reinforceText = UiKit.ButtonLabel(reinforceButton);
            UiKit.Outlined(UiKit.CreateText("Hint", rt, "Maintiens un couloir pour tirer", 20, TextAnchor.MiddleCenter, new Vector2(0.05f, 0.115f), new Vector2(0.95f, 0.145f), ApogeeTheme.Cream));

            BuildBuildPanel(rt);

            var overRt = UiKit.CreatePanel("BarricadeOver", rt, UiKit.Overlay);
            UiKit.CreateFrame("BarricadeOverFrame", overRt, new Vector2(0.08f, 0.24f), new Vector2(0.92f, 0.8f));
            overPanel = overRt.gameObject;
            overTitle = UiKit.Outlined(UiKit.CreateText("OverTitle", overRt, "LA BARRICADE EST TOMBÉE", 44, TextAnchor.MiddleCenter, new Vector2(0.05f, 0.66f), new Vector2(0.95f, 0.78f), ApogeeTheme.Gold), 2.5f);
            UiKit.FitLabel(overTitle, 44);
            overBody = UiKit.CreateText("OverBody", overRt, "", 28, TextAnchor.MiddleCenter, new Vector2(0.08f, 0.47f), new Vector2(0.92f, 0.65f), UiKit.Parchment);
            UiKit.FitLabel(overBody, 28);
            UiKit.CreateButton("Retry", overRt, "REJOUER", new Vector2(0.25f, 0.36f), new Vector2(0.75f, 0.43f), ResetGame);
            UiKit.CreateButton("Menu", overRt, "MENU", new Vector2(0.25f, 0.27f), new Vector2(0.75f, 0.34f), ReturnToHub);
            overPanel.SetActive(false);

            // The farm runs from the moment the game starts, not from the first visit here.
            farmStock = SaveSystem.BarricadeFarmStock;
            LoadLaneLevels();
            completedLanes = CountCompletedLanes();
        }

        /// <summary>
        /// Between-wave workshop. Nine lanes cannot each get a column of four buttons on a
        /// phone, so the traps are edited one lane at a time: pick the lane in the chip row,
        /// its four traps appear below and the lane lights up on the field.
        /// </summary>
        void BuildBuildPanel(RectTransform rt)
        {
            var bp = UiKit.CreateRect("BuildPanel", rt, new Vector2(0f, 0f), new Vector2(1f, 0.60f));
            var bpImg = bp.gameObject.AddComponent<Image>();
            bpImg.sprite = ApogeeTheme.Panel;
            bpImg.type = Image.Type.Sliced;
            buildPanel = bp.gameObject;

            buildText = UiKit.CreateText("BuildTitle", bp, "", 26, TextAnchor.MiddleCenter, new Vector2(0.04f, 0.91f), new Vector2(0.96f, 0.99f), UiKit.Parchment);
            UiKit.FitLabel(buildText, 26);

            for (int i = 0; i < MaxLanes; i++)
            {
                int captured = i;
                laneChips[i] = UiKit.CreateButton($"LaneChip_{i}", bp, (i + 1).ToString(), new Vector2(0f, 0.80f), new Vector2(0.1f, 0.90f),
                    () => SelectLane(captured), 26, UiKit.CardColor);
            }

            laneInfoText = UiKit.CreateText("LaneInfo", bp, "", 20, TextAnchor.MiddleCenter, new Vector2(0.04f, 0.735f), new Vector2(0.96f, 0.795f), ApogeeTheme.Cream);
            UiKit.FitLabel(laneInfoText, 20);

            for (int t = 0; t < 4; t++)
            {
                int col = t % 2, row = t / 2;
                float x0 = col == 0 ? 0.04f : 0.51f;
                float yMax = 0.725f - row * 0.14f;
                int capturedTrap = t;
                trapButtons[t] = UiKit.CreateButton($"Trap_{t}", bp, "", new Vector2(x0, yMax - 0.13f), new Vector2(x0 + 0.45f, yMax),
                    () => Buy(selectedLane, (TrapType)capturedTrap), 22, UiKit.CardColor);
                UiKit.ButtonLabel(trapButtons[t]).supportRichText = true;
            }

            farmText = UiKit.CreateText("Farm", bp, "", 19, TextAnchor.MiddleLeft, new Vector2(0.05f, 0.335f), new Vector2(0.64f, 0.435f), ApogeeTheme.Cream);
            UiKit.FitLabel(farmText, 19);
            collectButton = UiKit.CreateButton("Collect", bp, "RÉCUPÉRER", new Vector2(0.66f, 0.34f), new Vector2(0.96f, 0.43f), OnCollect, 20, new Color(0.22f, 0.34f, 0.44f));

            expandButton = UiKit.CreateButton("Expand", bp, "", new Vector2(0.04f, 0.195f), new Vector2(0.49f, 0.315f), OnExpand, 20, new Color(0.40f, 0.28f, 0.12f));
            expandLabel = UiKit.ButtonLabel(expandButton);
            expandLabel.supportRichText = true;
            burstButton = UiKit.CreateButton("BurstAd", bp, "", new Vector2(0.51f, 0.195f), new Vector2(0.96f, 0.315f), OnBurstAd, 20, new Color(0.55f, 0.36f, 0.08f));
            burstLabel = UiKit.ButtonLabel(burstButton);
            burstLabel.supportRichText = true;

            repairButton = UiKit.CreateButton("Repair", bp, "", new Vector2(0.04f, 0.03f), new Vector2(0.49f, 0.165f), Repair, 22, new Color(0.25f, 0.35f, 0.18f));
            repairLabel = UiKit.ButtonLabel(repairButton);
            repairLabel.supportRichText = true;
            UiKit.CreateButton("Launch", bp, "LANCER LA VAGUE", new Vector2(0.51f, 0.03f), new Vector2(0.96f, 0.165f), StartWave, 22);
        }

        // ---- lifecycle -----------------------------------------------------------------

        protected override void OnEnter()
        {
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

        void LoadLaneLevels()
        {
            laneCount = SaveSystem.BarricadeLanes;
            for (int l = 0; l < MaxLanes; l++)
                for (int t = 0; t < 4; t++)
                    lanes[l].level[t] = l < laneCount ? Mathf.Clamp(SaveSystem.GetBarricadeTrap(l, t), 0, MaxTrapLevel) : 0;
        }

        int CountCompletedLanes()
        {
            int n = 0;
            for (int l = 0; l < laneCount; l++) if (lanes[l].Complete) n++;
            return n;
        }

        /// <summary>
        /// (Re)builds the field for the current number of lanes: loads the saved base, frames
        /// the camera so every lane is on screen, moves the spawn line just above the view,
        /// and rebuilds the world with the traps already standing.
        /// </summary>
        void ApplyLayout()
        {
            ClearUnits();
            if (root != null) Destroy(root.gameObject);
            for (int l = 0; l < MaxLanes; l++)
                for (int t = 0; t < 4; t++) lanes[l].visuals[t] = null;

            LoadLaneLevels();
            laneWidth = WidthFor(laneCount);
            modelScale = Mathf.Min(1f, laneWidth / 1.4f);
            completedLanes = CountCompletedLanes();

            TakeOverCamera(new Vector3(Origin.x, Origin.y + 0.9f, -10f), CameraOrtho, ApogeeTheme.SkyAverage, FieldWidth + 0.7f);
            float top = 0.9f + (cam != null ? cam.orthographicSize : CameraOrtho);
            spawnY = Mathf.Max(BaseSpawnY, top + 0.6f);
            walkScale = (spawnY - BarricadeY) / (BaseSpawnY - BarricadeY);

            BuildWorld();
            for (int l = 0; l < laneCount; l++)
                for (int t = 0; t < 4; t++) RefreshTrapVisual(l, (TrapType)t);

            selectedLane = Mathf.Clamp(selectedLane, 0, laneCount - 1);
            touchZone?.Clear();
        }

        void BuildWorld()
        {
            root = new GameObject("BarricadeWorld").transform;
            float ortho = cam != null ? cam.orthographicSize : CameraOrtho;
            float viewTop = 0.9f + ortho, viewBottom = 0.9f - ortho;

            // Street and lane separators, tall enough to fill any zoom level.
            float streetBottom = PlayerY - 0.7f, streetTop = viewTop + 1.5f;
            CreateBox("Street", new Vector2(0f, (streetBottom + streetTop) * 0.5f), new Vector2(FieldWidth, streetTop - streetBottom), new Color(0.36f, 0.24f, 0.21f), -6);
            for (int i = 1; i < laneCount; i++)
            {
                float x = (i - laneCount * 0.5f) * laneWidth;
                CreateBox($"LaneLine_{i}", new Vector2(x, (streetBottom + streetTop) * 0.5f), new Vector2(0.05f, streetTop - streetBottom), new Color(0.62f, 0.42f, 0.34f, 0.6f), -5);
            }
            float curbTop = PlayerY - 0.65f, curbBottom = viewBottom - 1f;
            CreateBox("Curb", new Vector2(0f, (curbTop + curbBottom) * 0.5f), new Vector2(FieldWidth + 2f, curbTop - curbBottom), PlaceholderVisuals.GroundColor, -4);

            laneHighlight = CreateBox("LaneHighlight", new Vector2(0f, (BarricadeY + streetTop) * 0.5f), new Vector2(laneWidth, streetTop - BarricadeY),
                new Color(1f, 0.85f, 0.4f, 0.10f), -5);
            laneHighlight.SetActive(false);

            var barricade = CreateBox("Barricade", new Vector2(0f, BarricadeY), new Vector2(FieldWidth, 0.7f), new Color(0.50f, 0.30f, 0.14f), -1);
            barricadeSr = barricade.GetComponent<SpriteRenderer>();
            barricadeBaseColor = barricadeSr.color;
            int planks = Mathf.Max(4, Mathf.RoundToInt(FieldWidth / 0.97f));
            float pitch = FieldWidth / planks;
            for (int i = 0; i < planks; i++)
                CreateBox("Plank", new Vector2(-FieldWidth * 0.5f + pitch * (i + 0.5f), BarricadeY + 0.15f), new Vector2(0.25f, 1.1f), new Color(0.42f, 0.26f, 0.12f), 0);

            // The equipped character behind the barricade.
            var skin = SkinCatalog.Find(SaveSystem.SelectedSkinId);
            var player = new GameObject("Defender");
            player.transform.SetParent(root, false);
            player.transform.position = Origin + new Vector2(0f, PlayerY);
            player.transform.localScale = Vector3.one * 1.2f;
            playerSr = player.AddComponent<SpriteRenderer>();
            var portrait = SkinCatalog.LoadPortrait(skin, out bool custom);
            playerSr.sprite = custom ? portrait : ui.PlayerSprite;
            playerSr.color = custom ? Color.white : skin.Tint;
            playerSr.sortingOrder = 2;

            SpawnDrone(player.transform);
        }

        /// <summary>
        /// The shop's companion drone, hovering over the defender. It fires down whichever
        /// lane holds the enemy nearest the barricade, once every couple of seconds for a
        /// single point: enough to finish a crawler that got through, never enough to hold
        /// a lane on its own. Parented to the world root, so it dies with the round.
        /// </summary>
        void SpawnDrone(Transform defender)
        {
            int level = UpgradeManager.DroneLevel;
            if (level <= 0) return;

            drone = Drone.Create(root, defender, level);
            drone.offset = new Vector3(-0.75f, 0.95f, 0f);
            drone.mirrorWithTarget = false;   // the defender never turns around here
            drone.range = 30f;                // a lane is its business all the way up

            drone.FindTarget = _ =>
            {
                if (phase == Phase.Over) return null;
                Enemy best = null;
                foreach (var e in enemies)
                    if (e.hp > 0f && (best == null || e.y < best.y)) best = e;
                return best == null ? (Vector2?)null : new Vector2(LaneX(best.lane), Origin.y + best.y);
            };

            drone.Fire = (_, target) => FireShot(NearestLaneTo(target.x), PlayerY + 1.3f, 1f, false);
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
            return NearestLaneTo(world.x);
        }

        GameObject CreateBox(string name, Vector2 localPos, Vector2 size, Color color, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.position = Origin + localPos;
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = PlaceholderVisuals.Square(Color.white);
            sr.color = color;
            sr.sortingOrder = order;
            return go;
        }

        void ClearUnits()
        {
            foreach (var e in enemies) if (e.go != null) Destroy(e.go);
            foreach (var s in shots) if (s.go != null) Destroy(s.go);
            foreach (var s in spits) if (s != null) Destroy(s);
            foreach (var a in allies) if (a.go != null) Destroy(a.go);
            enemies.Clear();
            shots.Clear();
            spits.Clear();
            allies.Clear();
        }

        /// <summary>A new run: wave 1 and a whole barricade, on the base built so far.</summary>
        void ResetGame()
        {
            StopAllCoroutines();
            ClearUnits();
            for (int l = 0; l < MaxLanes; l++) { lanes[l].turretTimer = 0f; laneFireCooldown[l] = 0f; }
            touchZone?.Clear();

            wave = 1;
            debris = SaveSystem.BarricadeDebris;
            kills = 0;
            brutesKilled = 0;
            bombersKilled = 0;
            molotovCooldown = 0f;
            reinforceCooldown = 0f;
            burstWaves = 0;
            adPending = false;
            barricadeMax = 120f + SaveSystem.GetLevel(UpgradeStat.Armor) * 12f;
            barricadeHp = barricadeMax;
            if (barricadeSr != null) barricadeSr.color = barricadeBaseColor;
            overPanel.SetActive(false);
            messageText.text = "";
            EnterBuildPhase(first: true);
            RefreshHud();
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
            buildTimer = BuildDuration;
            buildPanel.SetActive(true);
            molotovButton.gameObject.SetActive(false);
            reinforceButton.gameObject.SetActive(false);
            if (laneHighlight != null) laneHighlight.SetActive(true);
            messageText.text = first
                ? (completedLanes > 0 || HasAnyTrap() ? "Ta base est prête, renforce-la" : "Prépare tes défenses")
                : $"Vague {wave - 1} repoussée !";
            SelectLane(selectedLane);
        }

        bool HasAnyTrap()
        {
            for (int l = 0; l < laneCount; l++)
                for (int t = 0; t < 4; t++) if (lanes[l].level[t] > 0) return true;
            return false;
        }

        void StartWave()
        {
            if (phase != Phase.Build || adPending) return;
            phase = Phase.Wave;
            buildPanel.SetActive(false);
            molotovButton.gameObject.SetActive(true);
            reinforceButton.gameObject.SetActive(true);
            if (laneHighlight != null) laneHighlight.SetActive(false);
            // A wider field is a longer front: the zombies scale with it, so each lane stays
            // as threatened as before and widening is a real commitment, not a free win.
            float frontScale = 1f + (laneCount - 3) / 6f;
            spawnRemaining = Mathf.RoundToInt((4 + wave * 2) * frontScale);
            spawnInterval = Mathf.Max(0.55f, 1.5f - wave * 0.08f) / frontScale;
            spawnTimer = 0.6f;
            messageText.text = $"Vague {wave}";
            ui.ShowBanner($"VAGUE {wave}", spawnRemaining + " zombies", 1.6f);
            RefreshHud();
        }

        void EndWave()
        {
            int bonus = 5 + wave;
            SetDebris(debris + bonus);
            if (wave > SaveSystem.BarricadeBestWave) SaveSystem.BarricadeBestWave = wave;
            wave++;
            if (burstWaves > 0) burstWaves--;
            SaveSystem.Flush();
            Sfx.Milestone();
            Fx.Text(Origin + new Vector2(0f, 1f), $"+{bonus} débris", PlaceholderVisuals.MaterialColor, 1.2f);
            foreach (var a in allies) if (a.go != null) Destroy(a.go);
            allies.Clear();
            EnterBuildPhase(first: false);
            RefreshHud();
        }

        void GameOver()
        {
            phase = Phase.Over;
            buildPanel.SetActive(false);
            molotovButton.gameObject.SetActive(false);
            reinforceButton.gameObject.SetActive(false);
            touchZone?.Clear();
            int wavesHeld = wave - 1;
            // Materials only, never coins: the barricade is where materials come from, and
            // coins stay the reward of the other games.
            int materials = brutesKilled + wavesHeld + bombersKilled * 2;
            if (materials > 0) SaveSystem.AddMaterials(materials);
            SaveSystem.BarricadeDebris = debris;
            SaveSystem.Flush();
            overBody.text = $"Vagues tenues : {wavesHeld}    Zombies abattus : {kills}\n+{materials} matériaux\n" +
                            $"Ta base et tes {debris} débris sont conservés.";
            overPanel.SetActive(true);
            Sfx.Death();
            Fx.Shake(0.5f, 0.4f);
            AdService.OnPlayerDeath();
        }

        // ---- per-frame -----------------------------------------------------------------

        void Update()
        {
            // The farm works whatever screen is open, as long as the game is running. Real
            // time, clamped so a stalled frame cannot pay out a burst.
            FarmTick(Mathf.Min(Time.unscaledDeltaTime, 1f));

            if (!IsActive || phase == Phase.Over) return;
            float dt = Time.deltaTime;

            if (phase == Phase.Build)
            {
                if (!adPending) buildTimer -= dt;
                buildText.text = adPending
                    ? "Préparation en pause pendant la publicité"
                    : $"Préparation — vague {wave} dans {Mathf.CeilToInt(Mathf.Max(0f, buildTimer))} s";
                farmTextTimer -= dt;
                if (farmTextTimer <= 0f) { farmTextTimer = 0.5f; RefreshFarmLine(); }
                if (buildTimer <= 0f && !adPending) StartWave();
                return;
            }

            UpdateSpawning(dt);
            UpdateEnemies(dt);
            UpdateAllies(dt);
            UpdateTraps(dt);
            UpdateShots(dt);
            UpdatePlayerFire(dt);
            molotovCooldown = Mathf.Max(0f, molotovCooldown - dt);
            reinforceCooldown = Mathf.Max(0f, reinforceCooldown - dt);
            molotovText.text = molotovCooldown > 0f ? $"MOLOTOV ({Mathf.CeilToInt(molotovCooldown)})" : "MOLOTOV";
            reinforceText.text = reinforceCooldown > 0f ? $"RENFORTS ({Mathf.CeilToInt(reinforceCooldown)})" : "RENFORTS";
            molotovButton.interactable = molotovCooldown <= 0f;
            reinforceButton.interactable = reinforceCooldown <= 0f;

            if (barricadeHp <= 0f) { GameOver(); return; }
            if (spawnRemaining == 0 && enemies.Count == 0) EndWave();
        }

        void UpdateSpawning(float dt)
        {
            if (spawnRemaining <= 0) return;
            spawnTimer -= dt;
            if (spawnTimer > 0f) return;
            spawnTimer = spawnInterval;
            spawnRemaining--;
            SpawnEnemy(Random.Range(0, laneCount), RollKind());
        }

        Kind RollKind()
        {
            // The bomber: rare, from wave 10, never two at once.
            if (wave >= 10 && Random.value < 0.05f && !BomberAlive()) return Kind.Bomber;
            float r = Random.value;
            if (wave >= 4 && r < 0.08f + wave * 0.012f) return Kind.Brute;
            if (wave >= 3 && r < 0.26f) return Kind.Spitter;
            if (wave >= 2 && r < 0.52f) return Kind.Runner;
            return Kind.Walker;
        }

        bool BomberAlive()
        {
            foreach (var e in enemies) if (e.kind == Kind.Bomber) return true;
            return false;
        }

        void SpawnEnemy(int lane, Kind kind)
        {
            var e = new Enemy { kind = kind, lane = lane, y = spawnY + Random.Range(0f, 0.8f) };
            var go = new GameObject($"Z_{kind}");
            go.transform.SetParent(root, false);
            e.sr = go.AddComponent<SpriteRenderer>();
            e.sr.sprite = PlaceholderVisuals.Zombie();
            e.sr.sortingOrder = 3;
            switch (kind)
            {
                case Kind.Runner:
                    go.transform.localScale = new Vector3(0.7f, 1.1f, 1f) * modelScale;
                    e.sr.color = new Color(1.3f, 1.15f, 0.6f);
                    e.hp = 1f + wave * 0.35f; e.speed = 2.6f; e.attackDamage = 4f; e.attackInterval = 0.8f; e.debris = 2;
                    break;
                case Kind.Spitter:
                    go.transform.localScale = new Vector3(0.85f, 1.25f, 1f) * modelScale;
                    e.sr.color = new Color(0.7f, 1.25f, 0.65f);
                    e.hp = 3f + wave * 0.5f; e.speed = 1.1f; e.attackDamage = 5f; e.attackInterval = 3f; e.debris = 3;
                    break;
                case Kind.Brute:
                    go.transform.localScale = new Vector3(1.25f, 1.75f, 1f) * modelScale;
                    e.sr.color = new Color(1.5f, 0.95f, 1.1f);
                    e.hp = 9f + wave * 1.2f; e.speed = 0.8f; e.attackDamage = 15f; e.attackInterval = 1.5f; e.debris = 6;
                    break;
                case Kind.Bomber:
                    go.transform.localScale = new Vector3(0.85f, 1.2f, 1f) * modelScale;
                    e.sr.color = new Color(1.4f, 0.8f, 0.55f);
                    // Killable before it throws: it stops far up the lane and winds up first.
                    e.hp = 5f + wave * 0.6f; e.speed = 1.0f; e.attackDamage = BombDamage; e.attackInterval = 4.5f; e.debris = 5;
                    break;
                default:
                    go.transform.localScale = new Vector3(0.8f, 1.2f, 1f) * modelScale;
                    e.sr.color = Color.white;
                    e.hp = 2f + wave * 0.5f; e.speed = 1.3f; e.attackDamage = 6f; e.attackInterval = 1.2f; e.debris = 2;
                    break;
            }
            e.speed *= walkScale;
            e.maxHp = e.hp;
            e.baseColor = e.sr.color;
            e.blood = e.sr.color * PlaceholderVisuals.ZombieColor;
            e.go = go;
            // The bomber takes its time before the first throw, the others hit on arrival.
            e.attackTimer = kind == Kind.Bomber ? 1.4f : e.attackInterval * 0.5f;
            if (KenneyProps.Available) GiveBody(e, go, kind);
            if (kind == Kind.Bomber) GiveHeldBomb(e);
            // z = -1: in front of the street sprite, so no part of a 3D body is hidden behind it.
            go.transform.position = new Vector3(LaneX(lane) + Random.Range(-0.4f, 0.4f) * LaneK, Origin.y + e.y, -1f);
            enemies.Add(e);

            if (kind == Kind.Bomber)
            {
                ui.ShowBanner("BOMBARDIER !", "Abats-le avant qu'il lance sa bombe", 1.8f);
                messageText.text = "Un bombardier approche";
            }
        }

        /// <summary>Kenney 3D body (same cast as the runner), walking straight at the camera.</summary>
        void GiveBody(Enemy e, GameObject go, Kind kind)
        {
            string model; float height;
            switch (kind)
            {
                case Kind.Runner: model = "character-skeleton"; height = 1.1f; e.blood = new Color(0.85f, 0.78f, 0.6f); break;
                case Kind.Spitter: model = "character-ghost"; height = 1.15f; e.blood = new Color(0.55f, 0.9f, 0.35f); break;
                case Kind.Brute: model = "character-keeper"; height = 1.8f; e.blood = new Color(0.55f, 0.18f, 0.12f); break;
                case Kind.Bomber: model = "character-zombie"; height = 1.2f; e.blood = new Color(0.7f, 0.35f, 0.15f); break;
                default:
                    bool vampire = Random.value < 0.3f;
                    model = vampire ? "character-vampire" : "character-zombie"; height = 1.2f;
                    e.blood = vampire ? new Color(0.55f, 0.1f, 0.12f) : new Color(0.35f, 0.55f, 0.22f);
                    break;
            }
            height *= modelScale;
            var size = KenneyProps.Size(PropKit.Graveyard, model);
            const float pitch = -16f;
            var rig = KenneyProps.Spawn(PropKit.Graveyard, model, go.transform, new Vector3(0f, -height / 2f, 0f), height / Mathf.Max(0.01f, size.y), PropLayer.Character, 0f, pitch);
            if (rig == null) return;
            go.transform.localScale = Vector3.one;
            e.sr.enabled = false;
            e.model = rig.GetComponentsInChildren<Renderer>();
            var motion = rig.gameObject.AddComponent<ModelMotion>();
            motion.height = height;
            motion.pitch = pitch;
            motion.floating = kind == Kind.Spitter;
        }

        /// <summary>
        /// The bomber carries its bomb over its head with a lit fuse, so it reads as
        /// different from a walker at a glance - the whole point of a rare threat is that
        /// the player spots it and changes target.
        /// </summary>
        void GiveHeldBomb(Enemy e)
        {
            var bomb = new GameObject("HeldBomb");
            bomb.transform.SetParent(e.go.transform, false);
            bomb.transform.localPosition = new Vector3(0f, 0.95f * modelScale, -0.1f);
            bomb.transform.localScale = Vector3.one * 0.42f * modelScale / Mathf.Max(0.01f, e.go.transform.localScale.x);
            var sr = bomb.AddComponent<SpriteRenderer>();
            sr.sprite = PlaceholderVisuals.RimCircle(new Color(0.12f, 0.1f, 0.1f));
            sr.sortingOrder = 6;

            var fuse = new GameObject("Fuse");
            fuse.transform.SetParent(bomb.transform, false);
            fuse.transform.localPosition = new Vector3(0.3f, 0.45f, -0.05f);
            fuse.transform.localScale = Vector3.one * 0.38f;
            e.fuse = fuse.AddComponent<SpriteRenderer>();
            e.fuse.sprite = PlaceholderVisuals.Circle(new Color(1f, 0.62f, 0.18f));
            e.fuse.sortingOrder = 7;
            e.heldBomb = bomb;
        }

        System.Collections.IEnumerator FlashModel(Renderer[] renderers)
        {
            const float duration = 0.12f;
            float t = 0f;
            while (t < duration && renderers != null && renderers.Length > 0 && renderers[0] != null)
            {
                t += Time.deltaTime;
                KenneyProps.SetFlash(renderers, 0.85f * (1f - t / duration));
                yield return null;
            }
            if (renderers != null && renderers.Length > 0 && renderers[0] != null) KenneyProps.SetFlash(renderers, 0f);
        }

        void UpdateEnemies(float dt)
        {
            for (int i = enemies.Count - 1; i >= 0; i--)
            {
                var e = enemies[i];
                var lane = lanes[e.lane];

                // Burn from a Molotov.
                if (e.burnTimer > 0f)
                {
                    e.burnTimer -= dt;
                    e.burnTick -= dt;
                    if (e.burnTick <= 0f) { e.burnTick = 0.6f; Damage(e, 1f, silent: true); if (e.hp <= 0f) continue; }
                }

                if (e.kind == Kind.Bomber) AnimateHeldBomb(e, dt);

                bool inTrapStrip = Mathf.Abs(e.y - TrapY) < TrapHalfHeight;
                float slow = 0f;
                if (inTrapStrip)
                {
                    slow += lane.level[(int)TrapType.Wire] * 0.2f;
                    slow += lane.level[(int)TrapType.Toxic] > 0 ? 0.3f : 0f;
                    e.tickTimer -= dt;
                    if (e.tickTimer <= 0f)
                    {
                        e.tickTimer = 0.8f;
                        float trapDamage = lane.level[(int)TrapType.Spikes] * 1f + lane.level[(int)TrapType.Toxic] * 0.8f;
                        if (trapDamage > 0f) { Damage(e, trapDamage, silent: true); if (e.hp <= 0f) continue; }
                    }
                }

                // An ally standing in the way stops the zombie, which turns on it instead.
                var blocker = AllyBlocking(e);
                if (blocker != null)
                {
                    e.attackTimer -= dt;
                    if (e.attackTimer <= 0f)
                    {
                        e.attackTimer = e.attackInterval;
                        // A bomber does not waste its bomb on a soldier: it shoves them.
                        blocker.hp -= e.kind == Kind.Bomber ? 6f : e.attackDamage;
                        Fx.Burst(blocker.go.transform.position, new Color(0.6f, 0.75f, 1f), 5, 2f, 0.07f);
                    }
                    continue;
                }

                float stopY = e.kind == Kind.Spitter ? SpitterStopY
                    : e.kind == Kind.Bomber ? BomberStopY
                    : BarricadeY + 0.75f;
                if (e.y > stopY)
                {
                    e.y = Mathf.Max(stopY, e.y - e.speed * (1f - Mathf.Clamp(slow, 0f, 0.7f)) * dt);
                    var p = e.go.transform.position;
                    p.y = Origin.y + e.y;
                    p.y += Mathf.Sin(Time.time * 9f + e.lane) * 0.02f; // shamble
                    e.go.transform.position = p;
                }
                else
                {
                    e.attackTimer -= dt;
                    if (e.attackTimer <= 0f)
                    {
                        e.attackTimer = e.attackInterval;
                        if (e.kind == Kind.Spitter) Spit(e);
                        else if (e.kind == Kind.Bomber) ThrowBomb(e);
                        else HitBarricade(e.attackDamage, e.go.transform.position);
                    }
                }
            }
        }

        void AnimateHeldBomb(Enemy e, float dt)
        {
            if (e.heldBomb == null) return;
            if (e.bombHidden > 0f)
            {
                e.bombHidden -= dt;
                e.heldBomb.SetActive(e.bombHidden <= 0f);
            }
            if (e.fuse != null)
            {
                float flicker = 0.6f + Mathf.Abs(Mathf.Sin(Time.time * 22f + e.lane)) * 0.4f;
                e.fuse.color = new Color(1f, 0.45f + flicker * 0.4f, 0.12f, flicker);
            }
        }

        void Spit(Enemy e)
        {
            var go = new GameObject("Spit");
            go.transform.SetParent(root, false);
            go.transform.position = e.go.transform.position + Vector3.down * 0.4f;
            go.transform.localScale = Vector3.one * 0.35f;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = PlaceholderVisuals.Circle(PlaceholderVisuals.SpitColor);
            sr.sortingOrder = 4;
            spits.Add(go);
            StartCoroutine(SpitFlight(go, e.attackDamage));
        }

        System.Collections.IEnumerator SpitFlight(GameObject spit, float damage)
        {
            float targetY = Origin.y + BarricadeY + 0.4f;
            while (spit != null && spit.transform.position.y > targetY && phase == Phase.Wave)
            {
                spit.transform.position += Vector3.down * (7f * Time.deltaTime);
                yield return null;
            }
            if (spit == null) yield break;
            if (phase == Phase.Wave) HitBarricade(damage, spit.transform.position);
            spits.Remove(spit);
            Destroy(spit);
        }

        /// <summary>
        /// A lobbed bomb: a visible arc down to the barricade, heavy damage on landing, and
        /// the first bomb from a given bomber also knocks one trap in its lane down a level.
        /// Only the first - so the harm a bomber can do to the base is bounded, and killing
        /// it fast is always worth it but never an emergency every wave.
        /// </summary>
        void ThrowBomb(Enemy e)
        {
            bool breaksTrap = !e.brokeTrap;
            e.brokeTrap = true;
            e.bombHidden = 1.6f;
            if (e.heldBomb != null) e.heldBomb.SetActive(false);

            var go = new GameObject("Bomb");
            go.transform.SetParent(root, false);
            go.transform.position = e.go.transform.position + Vector3.up * 0.8f;
            go.transform.localScale = Vector3.one * 0.42f * modelScale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = PlaceholderVisuals.RimCircle(new Color(0.12f, 0.1f, 0.1f));
            sr.sortingOrder = 6;
            spits.Add(go);
            Sfx.Drop();
            StartCoroutine(BombFlight(go, e.lane, breaksTrap));
        }

        System.Collections.IEnumerator BombFlight(GameObject bomb, int lane, bool breaksTrap)
        {
            Vector3 from = bomb.transform.position;
            Vector3 to = new Vector3(LaneX(lane), Origin.y + BarricadeY + 0.3f, from.z);
            const float duration = 1.05f;
            float t = 0f;
            while (bomb != null && t < duration && phase == Phase.Wave)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / duration);
                var pos = Vector3.Lerp(from, to, p);
                pos.y += Mathf.Sin(p * Mathf.PI) * 1.6f;
                bomb.transform.position = pos;
                bomb.transform.Rotate(0f, 0f, 540f * Time.deltaTime);
                yield return null;
            }
            if (bomb == null) yield break;
            spits.Remove(bomb);
            Destroy(bomb);
            if (phase != Phase.Wave) yield break;

            Fx.Burst(to, new Color(1f, 0.55f, 0.15f), 34, 5.5f, 0.15f, 0.4f);
            Fx.Burst(to, new Color(0.2f, 0.18f, 0.16f), 18, 3f, 0.18f, 0.2f);
            Fx.Shake(0.45f, 0.35f);
            HitBarricade(BombDamage, to);
            if (breaksTrap) BreakTrap(lane);
        }

        /// <summary>The bomb's toll on the base: the strongest trap in the lane loses a level.</summary>
        void BreakTrap(int laneIndex)
        {
            var lane = lanes[laneIndex];
            int best = -1;
            for (int t = 0; t < 4; t++)
                if (lane.level[t] > 0 && (best < 0 || lane.level[t] > lane.level[best])) best = t;
            if (best < 0) return;

            lane.level[best]--;
            SaveSystem.SetBarricadeTrap(laneIndex, best, lane.level[best]);
            SaveSystem.Flush();
            RefreshTrapVisual(laneIndex, (TrapType)best);
            completedLanes = CountCompletedLanes();
            Fx.Text(Origin + new Vector2(LaneX(laneIndex) - Origin.x, TrapY + 0.8f), $"{TrapNames[best]} endommagé !", new Color(1f, 0.4f, 0.3f), 1.1f);
        }

        void HitBarricade(float damage, Vector3 at)
        {
            barricadeHp = Mathf.Max(0f, barricadeHp - damage);
            Fx.Burst(at, new Color(0.6f, 0.4f, 0.2f), 6, 2f, 0.07f);
            Sfx.Hit();
            Fx.Shake(0.12f, 0.15f);
            StartCoroutine(BarricadeFlash());
            RefreshHud();
        }

        System.Collections.IEnumerator BarricadeFlash()
        {
            if (barricadeSr == null) yield break;
            barricadeSr.color = new Color(1f, 0.5f, 0.4f);
            yield return new WaitForSeconds(0.08f);
            if (barricadeSr != null) barricadeSr.color = barricadeBaseColor;
        }

        void UpdateTraps(float dt)
        {
            for (int l = 0; l < laneCount; l++)
            {
                var lane = lanes[l];
                int turret = lane.level[(int)TrapType.Turret];
                if (turret == 0) continue;
                lane.turretTimer -= dt;
                if (lane.turretTimer > 0f) continue;
                if (Nearest(l) == null) continue;
                lane.turretTimer = 1.3f - turret * 0.3f;
                FireShot(l, BarricadeY + 0.6f, 1f, false);
            }
        }

        Enemy Nearest(int lane)
        {
            Enemy best = null;
            foreach (var e in enemies)
                if (e.lane == lane && (best == null || e.y < best.y)) best = e;
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
                    // 1..9 pick a lane directly; the arrows cover the edges and the middle.
                    if (l < 9 && kb[(Key)((int)Key.Digit1 + l)].isPressed) held = true;
                    if (l == 0 && (kb.leftArrowKey.isPressed || kb.aKey.isPressed)) held = true;
                    if (l == laneCount - 1 && (kb.rightArrowKey.isPressed || kb.dKey.isPressed)) held = true;
                    if (l == laneCount / 2 && (kb.downArrowKey.isPressed || kb.upArrowKey.isPressed || kb.sKey.isPressed)) held = true;
                }
                if (!held || laneFireCooldown[l] > 0f) continue;
                laneFireCooldown[l] = 1f / rate;
                FireShot(l, PlayerY + 0.5f, ShotDamage, burst);
                Sfx.Shoot();
                if (playerSr != null) playerSr.flipX = LaneX(l) < Origin.x;
            }
        }

        void FireShot(int lane, float fromY, float damage, bool burst)
        {
            var go = new GameObject("Shot");
            go.transform.SetParent(root, false);
            go.transform.position = Origin + new Vector2(LaneX(lane) - Origin.x, fromY);
            go.transform.localScale = burst ? new Vector3(0.16f, 0.5f, 1f) : new Vector3(0.12f, 0.4f, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = PlaceholderVisuals.Square(Color.white);
            sr.color = burst ? new Color(1f, 0.78f, 0.25f) : PlaceholderVisuals.ProjectileColor;
            sr.sortingOrder = 5;
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
                    if (e.lane == s.lane && Mathf.Abs(e.y - s.y) < 0.55f) { hit = e; break; }

                if (hit != null)
                {
                    Damage(hit, s.damage, silent: false);
                    Destroy(s.go);
                    shots.RemoveAt(i);
                }
                else if (s.y > spawnY + 1f)
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
            var pos = e.go.transform.position;
            if (!silent) Fx.Burst(pos, e.blood, 4, 2f, 0.07f);
            if (e.model != null) StartCoroutine(FlashModel(e.model));
            if (e.hp > 0f)
            {
                float t = e.hp / e.maxHp;
                e.sr.color = Color.Lerp(new Color(0.6f, 0.2f, 0.2f), e.baseColor, t);
                return;
            }

            kills++;
            SetDebris(debris + e.debris);
            if (e.kind == Kind.Brute) brutesKilled++;
            if (e.kind == Kind.Bomber) bombersKilled++;
            Fx.Burst(pos, e.blood, e.kind == Kind.Brute ? 24 : 12, 3.5f, 0.11f);
            Fx.Text(pos, $"+{e.debris}", PlaceholderVisuals.MaterialColor, 0.9f);
            Sfx.Kill();
            enemies.Remove(e);
            Destroy(e.go);
            RefreshHud();
        }

        // ---- specials ------------------------------------------------------------------

        void OnMolotov()
        {
            if (phase != Phase.Wave || molotovCooldown > 0f) return;
            int bestLane = BusiestLanes(1)[0];
            molotovCooldown = MolotovCooldown;
            Vector3 center = Origin + new Vector2(LaneX(bestLane) - Origin.x, 2f);
            Fx.Burst(center, new Color(1f, 0.55f, 0.15f), 40, 5f, 0.16f, 0.3f);
            Fx.Shake(0.3f, 0.3f);
            Sfx.Spring();
            for (int i = enemies.Count - 1; i >= 0; i--)
            {
                var e = enemies[i];
                if (e.lane != bestLane) continue;
                e.burnTimer = 3f;
                e.burnTick = 0.3f;
                Damage(e, 5f, silent: false);
            }
        }

        /// <summary>Lane indices sorted by how many zombies are in them, busiest first.</summary>
        List<int> BusiestLanes(int take)
        {
            var counts = new int[laneCount];
            foreach (var e in enemies) if (e.lane < laneCount) counts[e.lane]++;
            var order = new List<int>();
            for (int l = 0; l < laneCount; l++) order.Add(l);
            order.Sort((a, b) => counts[b].CompareTo(counts[a]));
            if (order.Count > take) order.RemoveRange(take, order.Count - take);
            return order;
        }

        /// <summary>
        /// Reinforcements: a squad of survivors marches up the busiest lanes, one each, and
        /// holds whatever it meets. They stop zombies dead while they last, which buys the
        /// barricade time as much as their own blows kill anything.
        /// </summary>
        void OnReinforce()
        {
            if (phase != Phase.Wave || reinforceCooldown > 0f) return;
            reinforceCooldown = ReinforceCooldown;
            foreach (int lane in BusiestLanes(Mathf.Min(AllySquad, laneCount))) SpawnAlly(lane);
            Sfx.Milestone();
            messageText.text = "Renforts envoyés !";
        }

        void SpawnAlly(int lane)
        {
            var go = new GameObject("Ally");
            go.transform.SetParent(root, false);
            go.transform.localScale = Vector3.one * 0.85f * modelScale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = ui.PlayerSprite;
            sr.color = new Color(0.72f, 0.88f, 1.15f);
            sr.sortingOrder = 3;
            var a = new Ally { lane = lane, y = BarricadeY + 0.9f, hp = AllyBaseHp + wave, go = go };
            go.transform.position = new Vector3(LaneX(lane), Origin.y + a.y, -0.5f);
            Fx.Burst(go.transform.position, new Color(0.6f, 0.8f, 1f), 10, 2.5f, 0.09f);
            allies.Add(a);
        }

        Ally AllyBlocking(Enemy e)
        {
            foreach (var a in allies)
                if (a.lane == e.lane && a.y <= e.y && e.y - a.y < 0.8f) return a;
            return null;
        }

        void UpdateAllies(float dt)
        {
            for (int i = allies.Count - 1; i >= 0; i--)
            {
                var a = allies[i];
                if (a.hp <= 0f || a.y > spawnY)
                {
                    if (a.go != null)
                    {
                        if (a.hp <= 0f) Fx.Burst(a.go.transform.position, new Color(0.6f, 0.75f, 1f), 12, 3f, 0.1f);
                        Destroy(a.go);
                    }
                    allies.RemoveAt(i);
                    continue;
                }

                Enemy foe = null;
                foreach (var e in enemies)
                    if (e.lane == a.lane && e.y >= a.y && (foe == null || e.y < foe.y)) foe = e;

                if (foe != null && foe.y - a.y < 0.8f)
                {
                    a.hitTimer -= dt;
                    if (a.hitTimer <= 0f)
                    {
                        a.hitTimer = 0.4f;
                        Damage(foe, AllyDps * 0.4f, silent: false);
                    }
                }
                else
                {
                    a.y += AllySpeed * walkScale * dt;
                }

                var p = a.go.transform.position;
                p.y = Origin.y + a.y + Mathf.Abs(Mathf.Sin(Time.time * 10f + i)) * 0.05f;
                a.go.transform.position = p;
            }
        }

        /// <summary>
        /// The ad-only burst weapon: triple fire rate for three waves. The build countdown
        /// is frozen while the ad plays, so watching one never costs preparation time.
        /// </summary>
        void OnBurstAd()
        {
            if (phase != Phase.Build || burstWaves > 0 || adPending) return;
            adPending = true;
            ui.ShowAdOverlay(true, "Publicité en cours...");
            RefreshBuildPanel();
            AdService.ShowRewardedAd(() =>
            {
                adPending = false;
                ui.ShowAdOverlay(false);
                burstWaves = BurstWaves;
                Sfx.Milestone();
                ui.ShowBanner("RAFALE DÉBLOQUÉE", $"Cadence x3 pendant {BurstWaves} vagues", 2f);
                RefreshBuildPanel();
                RefreshHud();
            }, () =>
            {
                adPending = false;
                ui.ShowAdOverlay(false);
                messageText.text = "Publicité indisponible, réessaie plus tard";
                RefreshBuildPanel();
            });
        }

        // ---- materials farm ------------------------------------------------------------

        void FarmTick(float dt)
        {
            if (completedLanes <= 0 || dt <= 0f) return;
            float cap = completedLanes * FarmPerLanePerMinute * FarmCapMinutes;
            if (farmStock >= cap) return;
            farmStock = Mathf.Min(cap, farmStock + completedLanes * FarmPerLanePerMinute / 60f * dt);
            farmSaveTimer += dt;
            if (farmSaveTimer < 15f) return;
            farmSaveTimer = 0f;
            SaveSystem.BarricadeFarmStock = farmStock;
            SaveSystem.Flush();
        }

        void OnCollect()
        {
            int whole = Mathf.FloorToInt(farmStock);
            if (whole <= 0) return;
            farmStock -= whole;
            SaveSystem.BarricadeFarmStock = farmStock;
            SaveSystem.AddMaterials(whole);
            Sfx.Material();
            Fx.Text(Origin + new Vector2(0f, BarricadeY + 1.2f), $"+{whole} matériaux", PlaceholderVisuals.MaterialColor, 1.3f);
            messageText.text = $"+{whole} matériaux récupérés";
            RefreshBuildPanel();
        }

        // ---- shop ----------------------------------------------------------------------

        int CostFor(TrapType type, int level) => TrapBaseCost[(int)type] * (level + 1);

        void SelectLane(int lane)
        {
            selectedLane = Mathf.Clamp(lane, 0, laneCount - 1);
            if (laneHighlight != null)
            {
                var p = laneHighlight.transform.position;
                p.x = LaneX(selectedLane);
                laneHighlight.transform.position = p;
            }
            RefreshBuildPanel();
        }

        void Buy(int laneIndex, TrapType type)
        {
            if (phase != Phase.Build || laneIndex < 0 || laneIndex >= laneCount) return;
            var lane = lanes[laneIndex];
            int level = lane.level[(int)type];
            if (level >= MaxTrapLevel) return;
            int cost = CostFor(type, level);
            if (debris < cost) return;
            SetDebris(debris - cost);
            lane.level[(int)type] = level + 1;
            SaveSystem.SetBarricadeTrap(laneIndex, (int)type, level + 1);
            SaveSystem.Flush();
            RefreshTrapVisual(laneIndex, type);
            Sfx.Material();

            int before = completedLanes;
            completedLanes = CountCompletedLanes();
            if (completedLanes > before)
                ui.ShowBanner("COULOIR COMPLET", "Il produit maintenant des matériaux", 2f);

            RefreshHud();
            RefreshBuildPanel();
        }

        void Repair()
        {
            if (phase != Phase.Build || debris < RepairCost || barricadeHp >= barricadeMax) return;
            SetDebris(debris - RepairCost);
            barricadeHp = Mathf.Min(barricadeMax, barricadeHp + RepairAmount);
            Sfx.Medkit();
            RefreshHud();
            RefreshBuildPanel();
        }

        int NextLaneCount => laneCount >= 9 ? 0 : laneCount >= 6 ? 9 : 6;
        int ExpandCost => NextLaneCount == 9 ? Expand9Cost : Expand6Cost;

        void OnExpand()
        {
            if (phase != Phase.Build || adPending) return;
            int next = NextLaneCount;
            if (next == 0 || debris < ExpandCost) return;
            SetDebris(debris - ExpandCost);
            SaveSystem.BarricadeLanes = next;

            ApplyLayout();
            if (laneHighlight != null) laneHighlight.SetActive(true);
            Sfx.Milestone();
            ui.ShowBanner("TERRAIN AGRANDI", $"{next} couloirs à défendre", 2.2f);
            SelectLane(selectedLane);
            RefreshHud();
        }

        void RefreshTrapVisual(int laneIndex, TrapType type)
        {
            var lane = lanes[laneIndex];
            int slot = (int)type;
            if (lane.visuals[slot] != null) Destroy(lane.visuals[slot]);
            lane.visuals[slot] = null;
            int level = lane.level[slot];
            if (level <= 0 || root == null) return;
            float x = LaneX(laneIndex) - Origin.x;
            float k = LaneK;
            GameObject go = null;
            switch (type)
            {
                case TrapType.Spikes:
                    go = CreateBox("Spikes", new Vector2(x - 0.55f * k, TrapY + 0.3f), new Vector2(0.9f * k, 0.5f + level * 0.15f), Color.white, 1);
                    go.GetComponent<SpriteRenderer>().sprite = PlaceholderVisuals.Spikes();
                    break;
                case TrapType.Toxic:
                    go = CreateBox("Toxic", new Vector2(x + 0.5f * k, TrapY - 0.2f), new Vector2(0.9f * k, 0.5f), PlaceholderVisuals.ToxicColor, 1);
                    break;
                case TrapType.Wire:
                    go = CreateBox("Wire", new Vector2(x, TrapY - 0.55f), new Vector2(1.9f * k, 0.08f + level * 0.05f), new Color(0.55f, 0.5f, 0.45f), 1);
                    break;
                case TrapType.Turret:
                    go = CreateBox("Turret", new Vector2(x + 0.6f * k, BarricadeY + 0.65f), new Vector2((0.35f + level * 0.08f) * k, 0.5f), new Color(0.35f, 0.38f, 0.42f), 1);
                    break;
            }
            lane.visuals[slot] = go;
        }

        void RefreshBuildPanel()
        {
            if (buildPanel == null) return;

            // Lane chips: one per lane, gold when complete, crimson when selected.
            float w = 0.92f / Mathf.Max(1, laneCount);
            for (int i = 0; i < MaxLanes; i++)
            {
                var chip = laneChips[i];
                bool shown = i < laneCount;
                chip.gameObject.SetActive(shown);
                if (!shown) continue;
                var rt = (RectTransform)chip.transform;
                rt.anchorMin = new Vector2(0.04f + i * w + 0.005f, 0.80f);
                rt.anchorMax = new Vector2(0.04f + (i + 1) * w - 0.005f, 0.90f);
                var img = chip.GetComponent<Image>();
                img.color = i == selectedLane ? new Color(0.78f, 0.20f, 0.12f)
                    : lanes[i].Complete ? new Color(0.62f, 0.46f, 0.15f)
                    : UiKit.CardColor;
            }

            var lane = lanes[selectedLane];
            laneInfoText.text = lane.Complete
                ? $"Couloir {selectedLane + 1} · complet, il produit des matériaux"
                : $"Couloir {selectedLane + 1} · {lane.MaxedTraps}/4 pièges au maximum";

            for (int t = 0; t < 4; t++)
            {
                int level = lane.level[t];
                var btn = trapButtons[t];
                var label = UiKit.ButtonLabel(btn);
                if (level >= MaxTrapLevel)
                {
                    label.text = $"{TrapNames[t]}\n<size=16>niv. MAX</size>";
                    btn.interactable = false;
                }
                else
                {
                    int cost = CostFor((TrapType)t, level);
                    label.text = $"{TrapNames[t]} {(level > 0 ? $"niv. {level}" : "")}\n<size=16>{cost} débris</size>";
                    btn.interactable = debris >= cost && !adPending;
                }
            }

            RefreshFarmLine();

            int next = NextLaneCount;
            if (next == 0)
            {
                expandLabel.text = "TERRAIN\n<size=16>9 couloirs, maximum</size>";
                expandButton.interactable = false;
            }
            else
            {
                expandLabel.text = $"AGRANDIR · {next} COULOIRS\n<size=16>{ExpandCost} débris</size>";
                expandButton.interactable = debris >= ExpandCost && !adPending;
            }

            if (burstWaves > 0)
            {
                burstLabel.text = $"RAFALE ACTIVE\n<size=16>encore {burstWaves} vague{(burstWaves > 1 ? "s" : "")}</size>";
                burstButton.interactable = false;
            }
            else
            {
                burstLabel.text = "ARME RAFALE\n<size=16>une pub · x3 pendant 3 vagues</size>";
                burstButton.interactable = !adPending;
            }

            repairLabel.text = $"RÉPARER +{RepairAmount}\n<size=16>{RepairCost} débris</size>";
            repairButton.interactable = debris >= RepairCost && barricadeHp < barricadeMax && !adPending;
        }

        void RefreshFarmLine()
        {
            if (farmText == null) return;
            int stock = Mathf.FloorToInt(farmStock);
            if (completedLanes <= 0)
            {
                farmText.text = "Mets les 4 pièges d'un couloir au max : il produira des matériaux.";
            }
            else
            {
                float perHour = completedLanes * FarmPerLanePerMinute * 60f;
                float cap = completedLanes * FarmPerLanePerMinute * FarmCapMinutes;
                string full = farmStock >= cap ? " · plein" : "";
                farmText.text = $"{completedLanes} couloir{(completedLanes > 1 ? "s" : "")} complet{(completedLanes > 1 ? "s" : "")} · {perHour:0} mat./h\nEn stock : {stock}{full}";
            }
            collectButton.interactable = stock >= 1;
        }

        void RefreshHud()
        {
            waveText.text = $"Vague {wave}";
            debrisText.text = $"Débris : {debris}    Abattus : {kills}";
            burstText.text = burstWaves > 0 ? $"RAFALE x3 · encore {burstWaves} vague{(burstWaves > 1 ? "s" : "")}" : "";
            if (barricadeFill != null)
            {
                float t = barricadeMax > 0f ? barricadeHp / barricadeMax : 0f;
                barricadeFill.fillAmount = t;
                barricadeFill.color = t > 0.5f ? new Color(0.75f, 0.45f, 0.15f) : t > 0.25f ? new Color(0.9f, 0.6f, 0.2f) : new Color(0.8f, 0.15f, 0.15f);
            }
        }
    }

    /// <summary>
    /// One touch surface over the whole Barricade field. Each finger holds the lane under
    /// it, several fingers hold several lanes at once, and dragging a finger sideways moves
    /// it to the next lane - which a fixed button per lane could not do with nine of them.
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
