using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Platformer.Survival
{
    /// <summary>
    /// "BARRICADE": wave defense built for a portrait phone. Three vertical lanes; zombies
    /// come down from the top toward a barricade near the bottom, behind which the equipped
    /// character stands. Hold a lane to shoot up it (aim-assisted at the closest zombie in
    /// that lane). Kills pay "débris" spent between waves on per-lane traps (spikes, toxic
    /// pool, barbed wire, turret) and repairs. A Molotov special clears the busiest lane on
    /// a cooldown. The barricade's HP is boosted by the Armor upgrade, shot damage by
    /// FirePower. Surviving waves pays coins and materials into the shared wallet.
    ///
    /// The lanes live in world space far from the runner (around x = 3000, y = 1500) and
    /// borrow the main camera while active.
    /// </summary>
    public class BarricadeGame : MiniGame
    {
        public override string Id => "barricade";
        public override string Title => "BARRICADE";
        public override string Description => "Défends la barricade contre les vagues";
        public override string BestLine => SaveSystem.BarricadeBestWave > 0 ? $"Meilleure vague : {SaveSystem.BarricadeBestWave}" : "Aucune vague tenue";

        // ---- layout (world units, relative to Origin) ----
        static readonly Vector2 Origin = new Vector2(3000f, 1500f);
        const int LaneCount = 3;
        const float LaneWidth = 2.1f;
        const float SpawnY = 6.8f;
        const float BarricadeY = -3.3f;
        const float PlayerY = -4.5f;
        const float TrapY = -0.6f;
        const float TrapHalfHeight = 0.7f;
        const float SpitterStopY = 1.8f;
        const float CameraOrtho = 5.6f;

        static float LaneX(int lane) => Origin.x + (lane - 1) * LaneWidth;

        enum Kind { Walker, Runner, Spitter, Brute }
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
            public Button[] buyButtons = new Button[4];
        }

        Transform root;
        readonly List<Enemy> enemies = new();
        readonly List<Shot> shots = new();
        readonly Lane[] lanes = { new Lane(), new Lane(), new Lane() };
        readonly bool[] laneHeld = new bool[LaneCount];
        readonly float[] laneFireCooldown = new float[LaneCount];
        readonly List<GameObject> spits = new();

        Phase phase;
        int wave;
        int spawnRemaining;
        float spawnTimer, spawnInterval;
        float buildTimer;
        float barricadeHp, barricadeMax;
        int debris, kills, brutesKilled;
        float molotovCooldown;
        SpriteRenderer barricadeSr;
        Color barricadeBaseColor;
        SpriteRenderer playerSr;

        Text waveText, debrisText, buildText, molotovText, messageText;
        Image barricadeFill;
        GameObject buildPanel, overPanel;
        Text overTitle, overBody;
        Button molotovButton, repairButton;
        Text repairLabel;

        const float FireRate = 3.2f;
        const float ShotSpeed = 15f;
        const float MolotovCooldown = 20f;
        const float BuildDuration = 14f;
        static readonly string[] TrapNames = { "Pics", "Toxique", "Barbelés", "Tourelle" };
        static readonly int[] TrapBaseCost = { 8, 10, 6, 14 };
        const int MaxTrapLevel = 3;
        const int RepairCost = 10;
        const int RepairAmount = 35;

        float ShotDamage => Mathf.Max(1f, 1f * UpgradeManager.FirePowerMultiplier);

        // ---- UI ------------------------------------------------------------------------

        protected override void BuildUi()
        {
            var rt = UiKit.CreateRect("BarricadePanel", ui.Canvas.transform, Vector2.zero, Vector2.one);
            panel = rt.gameObject;

            // Lane touch zones (hold to fire), under everything else.
            for (int i = 0; i < LaneCount; i++)
            {
                var zone = UiKit.CreateRect($"LaneZone_{i}", rt, new Vector2(i / 3f, 0.13f), new Vector2((i + 1) / 3f, 0.86f));
                var img = zone.gameObject.AddComponent<Image>();
                img.color = new Color(1f, 1f, 1f, 0f);
                var hold = zone.gameObject.AddComponent<HoldButton>();
                int lane = i;
                hold.onDown = () => laneHeld[lane] = true;
                hold.onUp = () => laneHeld[lane] = false;
            }

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

            molotovButton = UiKit.CreateButton("Molotov", rt, "MOLOTOV", new Vector2(0.33f, 0.03f), new Vector2(0.67f, 0.115f), OnMolotov, 26, new Color(0.62f, 0.25f, 0.08f));
            molotovText = UiKit.ButtonLabel(molotovButton);
            UiKit.Outlined(UiKit.CreateText("Hint", rt, "Maintiens un couloir pour tirer", 20, TextAnchor.MiddleCenter, new Vector2(0.05f, 0.115f), new Vector2(0.95f, 0.145f), ApogeeTheme.Cream));

            BuildBuildPanel(rt);

            var overRt = UiKit.CreatePanel("BarricadeOver", rt, UiKit.Overlay);
            UiKit.CreateFrame("BarricadeOverFrame", overRt, new Vector2(0.08f, 0.24f), new Vector2(0.92f, 0.8f));
            overPanel = overRt.gameObject;
            overTitle = UiKit.Outlined(UiKit.CreateText("OverTitle", overRt, "LA BARRICADE EST TOMBÉE", 44, TextAnchor.MiddleCenter, new Vector2(0.05f, 0.66f), new Vector2(0.95f, 0.78f), ApogeeTheme.Gold), 2.5f);
            UiKit.FitLabel(overTitle, 44);
            overBody = UiKit.CreateText("OverBody", overRt, "", 30, TextAnchor.MiddleCenter, new Vector2(0.08f, 0.50f), new Vector2(0.92f, 0.65f), UiKit.Parchment);
            UiKit.CreateButton("Retry", overRt, "REJOUER", new Vector2(0.25f, 0.38f), new Vector2(0.75f, 0.45f), ResetGame);
            UiKit.CreateButton("Menu", overRt, "MENU", new Vector2(0.25f, 0.29f), new Vector2(0.75f, 0.36f), ReturnToHub);
            overPanel.SetActive(false);
        }

        /// <summary>Between-wave shop: one column per lane with the four traps, plus repair and launch.</summary>
        void BuildBuildPanel(RectTransform rt)
        {
            var bp = UiKit.CreateRect("BuildPanel", rt, new Vector2(0f, 0f), new Vector2(1f, 0.56f));
            var bpImg = bp.gameObject.AddComponent<Image>();
            bpImg.sprite = ApogeeTheme.Panel;
            bpImg.type = Image.Type.Sliced;
            buildPanel = bp.gameObject;

            buildText = UiKit.CreateText("BuildTitle", bp, "", 28, TextAnchor.MiddleCenter, new Vector2(0.05f, 0.90f), new Vector2(0.95f, 0.99f), UiKit.Parchment);

            for (int lane = 0; lane < LaneCount; lane++)
            {
                float x0 = 0.03f + lane * 0.32f;
                float x1 = x0 + 0.30f;
                UiKit.CreateText($"LaneTitle_{lane}", bp, lane == 0 ? "Couloir gauche" : lane == 1 ? "Couloir centre" : "Couloir droit", 20, TextAnchor.MiddleCenter,
                    new Vector2(x0, 0.82f), new Vector2(x1, 0.89f), Color.white);
                for (int t = 0; t < 4; t++)
                {
                    float yMax = 0.80f - t * 0.145f;
                    int capturedLane = lane, capturedTrap = t;
                    lanes[lane].buyButtons[t] = UiKit.CreateButton($"Buy_{lane}_{t}", bp, "", new Vector2(x0, yMax - 0.13f), new Vector2(x1, yMax),
                        () => Buy(capturedLane, (TrapType)capturedTrap), 20, UiKit.CardColor);
                    UiKit.ButtonLabel(lanes[lane].buyButtons[t]).supportRichText = true;
                }
            }

            repairButton = UiKit.CreateButton("Repair", bp, "", new Vector2(0.05f, 0.05f), new Vector2(0.47f, 0.18f), Repair, 22, new Color(0.25f, 0.35f, 0.18f));
            repairLabel = UiKit.ButtonLabel(repairButton);
            UiKit.CreateButton("Launch", bp, "LANCER LA VAGUE", new Vector2(0.53f, 0.05f), new Vector2(0.95f, 0.18f), StartWave, 22);
        }

        // ---- lifecycle -----------------------------------------------------------------

        protected override void OnEnter()
        {
            BuildWorld();
            TakeOverCamera(new Vector3(Origin.x, Origin.y + 0.9f, -10f), CameraOrtho, ApogeeTheme.SkyAverage);
            ResetGame();
        }

        protected override void OnExit()
        {
            phase = Phase.Over;
            if (root != null) Destroy(root.gameObject);
            root = null;
            enemies.Clear();
            shots.Clear();
            spits.Clear();
        }

        void BuildWorld()
        {
            root = new GameObject("BarricadeWorld").transform;

            // Street backdrop and lane separators.
            CreateBox("Street", new Vector2(0f, 1.2f), new Vector2(LaneWidth * LaneCount, 15f), new Color(0.36f, 0.24f, 0.21f), -6);
            for (int i = 1; i < LaneCount; i++)
                CreateBox($"LaneLine_{i}", new Vector2((i - 1.5f) * LaneWidth, 1.2f), new Vector2(0.05f, 15f), new Color(0.62f, 0.42f, 0.34f, 0.6f), -5);
            CreateBox("Curb", new Vector2(0f, PlayerY - 1.4f), new Vector2(LaneWidth * LaneCount + 2f, 1.5f), PlaceholderVisuals.GroundColor, -4);

            var barricade = CreateBox("Barricade", new Vector2(0f, BarricadeY), new Vector2(LaneWidth * LaneCount, 0.7f), new Color(0.50f, 0.30f, 0.14f), -1);
            barricadeSr = barricade.GetComponent<SpriteRenderer>();
            barricadeBaseColor = barricadeSr.color;
            for (int i = 0; i < 7; i++) // planks
                CreateBox("Plank", new Vector2(-2.9f + i * 0.97f, BarricadeY + 0.15f), new Vector2(0.25f, 1.1f), new Color(0.42f, 0.26f, 0.12f), 0);

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

        void ResetGame()
        {
            foreach (var e in enemies) if (e.go != null) Destroy(e.go);
            foreach (var s in shots) if (s.go != null) Destroy(s.go);
            foreach (var s in spits) if (s != null) Destroy(s);
            enemies.Clear();
            shots.Clear();
            spits.Clear();
            foreach (var lane in lanes)
            {
                for (int t = 0; t < 4; t++)
                {
                    lane.level[t] = 0;
                    if (lane.visuals[t] != null) Destroy(lane.visuals[t]);
                    lane.visuals[t] = null;
                }
                lane.turretTimer = 0f;
            }
            for (int i = 0; i < LaneCount; i++) { laneHeld[i] = false; laneFireCooldown[i] = 0f; }

            wave = 1;
            debris = 12;
            kills = 0;
            brutesKilled = 0;
            molotovCooldown = 0f;
            barricadeMax = 120f + SaveSystem.GetLevel(UpgradeStat.Armor) * 12f;
            barricadeHp = barricadeMax;
            overPanel.SetActive(false);
            messageText.text = "";
            EnterBuildPhase(first: true);
            RefreshHud();
        }

        // ---- phases --------------------------------------------------------------------

        void EnterBuildPhase(bool first)
        {
            phase = Phase.Build;
            buildTimer = BuildDuration;
            buildPanel.SetActive(true);
            molotovButton.gameObject.SetActive(false);
            messageText.text = first ? "Prépare tes défenses" : $"Vague {wave - 1} repoussée !";
            RefreshBuildPanel();
        }

        void StartWave()
        {
            if (phase != Phase.Build) return;
            phase = Phase.Wave;
            buildPanel.SetActive(false);
            molotovButton.gameObject.SetActive(true);
            spawnRemaining = 4 + wave * 2;
            spawnInterval = Mathf.Max(0.55f, 1.5f - wave * 0.08f);
            spawnTimer = 0.6f;
            messageText.text = $"Vague {wave}";
            ui.ShowBanner($"VAGUE {wave}", spawnRemaining + " zombies", 1.6f);
            RefreshHud();
        }

        void EndWave()
        {
            int bonus = 5 + wave;
            debris += bonus;
            if (wave > SaveSystem.BarricadeBestWave) SaveSystem.BarricadeBestWave = wave;
            wave++;
            Sfx.Milestone();
            Fx.Text(Origin + new Vector2(0f, 1f), $"+{bonus} débris", PlaceholderVisuals.MaterialColor, 1.2f);
            EnterBuildPhase(first: false);
            RefreshHud();
        }

        void GameOver()
        {
            phase = Phase.Over;
            buildPanel.SetActive(false);
            molotovButton.gameObject.SetActive(false);
            int wavesHeld = wave - 1;
            int coins = kills / 2 + wavesHeld * 8;
            int materials = brutesKilled + wavesHeld / 2;
            if (coins > 0) SaveSystem.AddCoins(coins);
            if (materials > 0) SaveSystem.AddMaterials(materials);
            overBody.text = $"Vagues tenues : {wavesHeld}    Zombies abattus : {kills}\n+{coins} pièces   +{materials} matériaux";
            overPanel.SetActive(true);
            Sfx.Death();
            Fx.Shake(0.5f, 0.4f);
            AdService.OnPlayerDeath();
        }

        // ---- per-frame -----------------------------------------------------------------

        void Update()
        {
            if (!IsActive || phase == Phase.Over) return;
            float dt = Time.deltaTime;

            if (phase == Phase.Build)
            {
                buildTimer -= dt;
                buildText.text = $"Préparation — vague {wave} dans {Mathf.CeilToInt(Mathf.Max(0f, buildTimer))} s";
                if (buildTimer <= 0f) StartWave();
                return;
            }

            UpdateSpawning(dt);
            UpdateEnemies(dt);
            UpdateTraps(dt);
            UpdateShots(dt);
            UpdatePlayerFire(dt);
            molotovCooldown = Mathf.Max(0f, molotovCooldown - dt);
            molotovText.text = molotovCooldown > 0f ? $"MOLOTOV ({Mathf.CeilToInt(molotovCooldown)})" : "MOLOTOV";
            molotovButton.interactable = molotovCooldown <= 0f;

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
            SpawnEnemy(Random.Range(0, LaneCount), RollKind());
        }

        Kind RollKind()
        {
            float r = Random.value;
            if (wave >= 4 && r < 0.08f + wave * 0.012f) return Kind.Brute;
            if (wave >= 3 && r < 0.26f) return Kind.Spitter;
            if (wave >= 2 && r < 0.52f) return Kind.Runner;
            return Kind.Walker;
        }

        void SpawnEnemy(int lane, Kind kind)
        {
            var e = new Enemy { kind = kind, lane = lane, y = SpawnY + Random.Range(0f, 0.8f) };
            var go = new GameObject($"Z_{kind}");
            go.transform.SetParent(root, false);
            e.sr = go.AddComponent<SpriteRenderer>();
            e.sr.sprite = PlaceholderVisuals.Zombie();
            e.sr.sortingOrder = 3;
            switch (kind)
            {
                case Kind.Runner:
                    go.transform.localScale = new Vector3(0.7f, 1.1f, 1f);
                    e.sr.color = new Color(1.3f, 1.15f, 0.6f);
                    e.hp = 1f + wave * 0.35f; e.speed = 2.6f; e.attackDamage = 4f; e.attackInterval = 0.8f; e.debris = 2;
                    break;
                case Kind.Spitter:
                    go.transform.localScale = new Vector3(0.85f, 1.25f, 1f);
                    e.sr.color = new Color(0.7f, 1.25f, 0.65f);
                    e.hp = 3f + wave * 0.5f; e.speed = 1.1f; e.attackDamage = 5f; e.attackInterval = 3f; e.debris = 3;
                    break;
                case Kind.Brute:
                    go.transform.localScale = new Vector3(1.25f, 1.75f, 1f);
                    e.sr.color = new Color(1.5f, 0.95f, 1.1f);
                    e.hp = 9f + wave * 1.2f; e.speed = 0.8f; e.attackDamage = 15f; e.attackInterval = 1.5f; e.debris = 6;
                    break;
                default:
                    go.transform.localScale = new Vector3(0.8f, 1.2f, 1f);
                    e.sr.color = Color.white;
                    e.hp = 2f + wave * 0.5f; e.speed = 1.3f; e.attackDamage = 6f; e.attackInterval = 1.2f; e.debris = 2;
                    break;
            }
            e.maxHp = e.hp;
            e.baseColor = e.sr.color;
            e.blood = e.sr.color * PlaceholderVisuals.ZombieColor;
            e.go = go;
            e.attackTimer = e.attackInterval * 0.5f;
            if (KenneyProps.Available) GiveBody(e, go, kind);
            // z = -1: in front of the street sprite, so no part of a 3D body is hidden behind it.
            go.transform.position = new Vector3(LaneX(lane) + Random.Range(-0.4f, 0.4f), Origin.y + e.y, -1f);
            enemies.Add(e);
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
                default:
                    bool vampire = Random.value < 0.3f;
                    model = vampire ? "character-vampire" : "character-zombie"; height = 1.2f;
                    e.blood = vampire ? new Color(0.55f, 0.1f, 0.12f) : new Color(0.35f, 0.55f, 0.22f);
                    break;
            }
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

                float stopY = e.kind == Kind.Spitter ? SpitterStopY : BarricadeY + 0.75f;
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
                        else HitBarricade(e.attackDamage, e.go.transform.position);
                    }
                }
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
            for (int l = 0; l < LaneCount; l++)
            {
                var lane = lanes[l];
                int turret = lane.level[(int)TrapType.Turret];
                if (turret == 0) continue;
                lane.turretTimer -= dt;
                if (lane.turretTimer > 0f) continue;
                if (Nearest(l) == null) continue;
                lane.turretTimer = 1.3f - turret * 0.3f;
                FireShot(l, BarricadeY + 0.6f, 1f);
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
            for (int l = 0; l < LaneCount; l++)
            {
                laneFireCooldown[l] -= dt;
                bool held = laneHeld[l];
                if (kb != null)
                {
                    if (l == 0 && (kb.leftArrowKey.isPressed || kb.digit1Key.isPressed || kb.aKey.isPressed)) held = true;
                    if (l == 1 && (kb.downArrowKey.isPressed || kb.upArrowKey.isPressed || kb.digit2Key.isPressed || kb.sKey.isPressed)) held = true;
                    if (l == 2 && (kb.rightArrowKey.isPressed || kb.digit3Key.isPressed || kb.dKey.isPressed)) held = true;
                }
                if (!held || laneFireCooldown[l] > 0f) continue;
                laneFireCooldown[l] = 1f / FireRate;
                FireShot(l, PlayerY + 0.5f, ShotDamage);
                Sfx.Shoot();
                if (playerSr != null) playerSr.flipX = l == 0;
            }
        }

        void FireShot(int lane, float fromY, float damage)
        {
            var go = new GameObject("Shot");
            go.transform.SetParent(root, false);
            go.transform.position = Origin + new Vector2(LaneX(lane) - Origin.x, fromY);
            go.transform.localScale = new Vector3(0.12f, 0.4f, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = PlaceholderVisuals.Square(PlaceholderVisuals.ProjectileColor);
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
                else if (s.y > SpawnY + 1f)
                {
                    Destroy(s.go);
                    shots.RemoveAt(i);
                }
            }
        }

        void Damage(Enemy e, float amount, bool silent)
        {
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
            debris += e.debris;
            if (e.kind == Kind.Brute) brutesKilled++;
            Fx.Burst(pos, e.blood, e.kind == Kind.Brute ? 24 : 12, 3.5f, 0.11f);
            Fx.Text(pos, $"+{e.debris}", PlaceholderVisuals.MaterialColor, 0.9f);
            Sfx.Kill();
            enemies.Remove(e);
            Destroy(e.go);
            RefreshHud();
        }

        void OnMolotov()
        {
            if (phase != Phase.Wave || molotovCooldown > 0f) return;
            int bestLane = 0, bestCount = -1;
            for (int l = 0; l < LaneCount; l++)
            {
                int c = 0;
                foreach (var e in enemies) if (e.lane == l) c++;
                if (c > bestCount) { bestCount = c; bestLane = l; }
            }
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

        // ---- shop ----------------------------------------------------------------------

        int CostFor(TrapType type, int level) => TrapBaseCost[(int)type] * (level + 1);

        void Buy(int laneIndex, TrapType type)
        {
            if (phase != Phase.Build) return;
            var lane = lanes[laneIndex];
            int level = lane.level[(int)type];
            if (level >= MaxTrapLevel) return;
            int cost = CostFor(type, level);
            if (debris < cost) return;
            debris -= cost;
            lane.level[(int)type] = level + 1;
            RefreshTrapVisual(laneIndex, type);
            Sfx.Material();
            RefreshHud();
            RefreshBuildPanel();
        }

        void Repair()
        {
            if (phase != Phase.Build || debris < RepairCost || barricadeHp >= barricadeMax) return;
            debris -= RepairCost;
            barricadeHp = Mathf.Min(barricadeMax, barricadeHp + RepairAmount);
            Sfx.Medkit();
            RefreshHud();
            RefreshBuildPanel();
        }

        void RefreshTrapVisual(int laneIndex, TrapType type)
        {
            var lane = lanes[laneIndex];
            int slot = (int)type;
            if (lane.visuals[slot] != null) Destroy(lane.visuals[slot]);
            int level = lane.level[slot];
            float x = LaneX(laneIndex) - Origin.x;
            GameObject go = null;
            switch (type)
            {
                case TrapType.Spikes:
                    go = CreateBox("Spikes", new Vector2(x - 0.55f, TrapY + 0.3f), new Vector2(0.9f, 0.5f + level * 0.15f), Color.white, 1);
                    go.GetComponent<SpriteRenderer>().sprite = PlaceholderVisuals.Spikes();
                    break;
                case TrapType.Toxic:
                    go = CreateBox("Toxic", new Vector2(x + 0.5f, TrapY - 0.2f), new Vector2(0.9f, 0.5f), PlaceholderVisuals.ToxicColor, 1);
                    break;
                case TrapType.Wire:
                    go = CreateBox("Wire", new Vector2(x, TrapY - 0.55f), new Vector2(1.9f, 0.08f + level * 0.05f), new Color(0.55f, 0.5f, 0.45f), 1);
                    break;
                case TrapType.Turret:
                    go = CreateBox("Turret", new Vector2(x + 0.6f, BarricadeY + 0.65f), new Vector2(0.35f + level * 0.08f, 0.5f), new Color(0.35f, 0.38f, 0.42f), 1);
                    break;
            }
            lane.visuals[slot] = go;
        }

        void RefreshBuildPanel()
        {
            for (int l = 0; l < LaneCount; l++)
            {
                for (int t = 0; t < 4; t++)
                {
                    int level = lanes[l].level[t];
                    var btn = lanes[l].buyButtons[t];
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
                        btn.interactable = debris >= cost;
                    }
                }
            }
            repairLabel.text = $"RÉPARER +{RepairAmount}\n<size=16>{RepairCost} débris</size>";
            repairLabel.supportRichText = true;
            repairButton.interactable = debris >= RepairCost && barricadeHp < barricadeMax;
        }

        void RefreshHud()
        {
            waveText.text = $"Vague {wave}";
            debrisText.text = $"Débris : {debris}    Abattus : {kills}";
            if (barricadeFill != null)
            {
                float t = barricadeMax > 0f ? barricadeHp / barricadeMax : 0f;
                barricadeFill.fillAmount = t;
                barricadeFill.color = t > 0.5f ? new Color(0.75f, 0.45f, 0.15f) : t > 0.25f ? new Color(0.9f, 0.6f, 0.2f) : new Color(0.8f, 0.15f, 0.15f);
            }
        }
    }
}
