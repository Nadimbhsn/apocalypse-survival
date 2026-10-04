using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Random = UnityEngine.Random;

namespace Platformer.Survival
{
    /// <summary>
    /// "BASTION": the tower defense. The dead follow a road across a floating island to
    /// the bastion's gate; the player builds towers on the stone spots beside the road -
    /// archers, canon, brazier, frost, pylon, each upgradable twice - and places their own
    /// character as a hero who fights and, every half minute, unleashes the power of that
    /// character (see BastionCatalog.PowerOf). Twenty lives; each dead that reaches the gate
    /// costs one (a colossus five).
    ///
    /// Two ways to play: a campaign of five maps, each a fixed number of waves and up to
    /// three stars for the lives kept; and the endless island, where the waves never stop
    /// and only the record counts. Rewards: coins, materials, and a healing kit for a
    /// first three-star map or a long endless run.
    ///
    /// Everything is positional (no physics): the dead advance by distance along the road,
    /// towers pick the one furthest along within reach. The world lives far from the runner
    /// (around x = 12000) and borrows the main camera while a map is being played.
    /// </summary>
    public class BastionGame : MiniGame
    {
        public override string Id => "bastion";
        public override string Title => "BASTION";
        public override string Description => "Pose des tours, arrête les morts sur le chemin";

        public override string BestLine
        {
            get
            {
                int stars = 0;
                for (int i = 0; i < BastionCatalog.Campaign.Length; i++) stars += SaveSystem.GetBastionStars(i);
                int best = SaveSystem.BastionEndlessBest;
                return $"Étoiles {stars}/{BastionCatalog.Campaign.Length * 3}" + (best > 0 ? $"   ·   infini : vague {best}" : "");
            }
        }

        static readonly Vector2 Origin = new Vector2(12000f, 3000f);
        const float FieldHalfW = 4.3f, FieldHalfH = 7.9f;
        /// <summary>Screen band (viewport height) the island is framed in, between the bars.</summary>
        const float RegionBottom = 0.155f, RegionTop = 0.9f;
        const float BetweenWaves = 12f;
        const float HeroRange = 1.9f, HeroInterval = 0.7f, HeroSpeed = 3f;

        // ---- world state ------------------------------------------------------------------

        class Creep
        {
            public CreepDef def;
            public float hp, maxHp, dist, slowUntil, slowFactor = 1f, stunUntil;
            public Vector2 pos;
            public GameObject go;
            public Transform hpFill;
            public GameObject hpBar;
            public float bob;
        }

        class Tower
        {
            public TowerDef def;
            public int level, slot;
            public float cooldown;
            public GameObject go;
        }

        class Zone
        {
            public Vector2 at;
            public float radius, dps, until, tick;
            public GameObject go;
        }

        class Tracer
        {
            public Transform t;
            public Vector3 from, to;
            public float time, duration;
        }

        Transform root;
        BastionMap map;
        int mapIndex;               // -1 for the endless map
        Vector2[] path;
        float pathLength;
        List<Vector2> slots = new();
        Tower[] slotTowers = new Tower[0];
        readonly List<Creep> creeps = new();
        readonly List<Zone> zones = new();
        readonly List<Tracer> tracers = new();

        bool playing;
        int gold, lives, wave, speed = 1;
        bool waveRunning;
        readonly List<CreepKind> queue = new();
        float spawnTimer, nextWaveTimer, healthScale;
        int kills;

        // the hero
        Transform hero;
        SpriteRenderer heroSr;
        GameObject heroRing;
        Vector2 heroPos, heroTarget;
        float heroCooldown, powerCooldown;
        bool heroSelected;
        BastionCatalog.HeroPower power;

        int selectedSlot = -1;
        GameObject rangeRing;
        bool pressActive, pressOverUi;

        // ---- UI -------------------------------------------------------------------------

        GameObject selectPanel, hudRoot, buildMenu, towerMenu, overPanel;
        Text waveText, livesText, goldText, messageText, towerInfo, overTitle, overDetail;
        IconText overReward;
        Button callButton, speedButton, powerButton, upgradeButton, sellButton;
        Text callLabel, speedLabel, powerLabel;
        IconText upgradeLabel, sellLabel;
        readonly Button[] buildButtons = new Button[5];
        readonly Text[] buildCosts = new Text[5];
        readonly List<(Button card, Text stars)> mapCards = new();
        Text endlessBestText;
        readonly Image[] overStars = new Image[3];

        protected override void BuildUi()
        {
            var rt = UiKit.CreateRect("BastionPanel", ui.Canvas.transform, Vector2.zero, Vector2.one);
            panel = rt.gameObject;

            // ---- the game's HUD ----
            var hud = UiKit.CreateRect("Hud", rt, Vector2.zero, Vector2.one);
            hudRoot = hud.gameObject;

            var topBar = UiKit.CreateRect("TopBar", hud, new Vector2(0f, 0.905f), new Vector2(1f, 1f));
            var topImg = topBar.gameObject.AddComponent<Image>();
            topImg.sprite = ApogeeTheme.Panel;
            topImg.type = Image.Type.Sliced;
            waveText = UiKit.Outlined(UiKit.CreateText("Wave", topBar, "", 30, TextAnchor.MiddleLeft, new Vector2(0.04f, 0.1f), new Vector2(0.36f, 0.9f), ApogeeTheme.Gold));
            UiKit.FitLabel(waveText, 30);
            livesText = UiKit.CreateText("Lives", topBar, "", 28, TextAnchor.MiddleCenter, new Vector2(0.36f, 0.1f), new Vector2(0.56f, 0.9f), new Color(1f, 0.55f, 0.5f));
            goldText = UiKit.CreateText("Gold", topBar, "", 28, TextAnchor.MiddleCenter, new Vector2(0.56f, 0.1f), new Vector2(0.77f, 0.9f), PlaceholderVisuals.CoinColor);
            ui.CreatePauseButton(topBar, new Vector2(0.79f, 0.2f), new Vector2(0.97f, 0.8f), () => { if (playing) OpenPause(); });

            messageText = UiKit.Outlined(UiKit.CreateText("Message", hud, "", 24, TextAnchor.MiddleCenter, new Vector2(0.04f, 0.86f), new Vector2(0.96f, 0.9f), ApogeeTheme.Cream));

            var bottom = UiKit.CreateRect("BottomBar", hud, new Vector2(0f, 0f), new Vector2(1f, 0.145f));
            var bImg = bottom.gameObject.AddComponent<Image>();
            bImg.sprite = ApogeeTheme.Panel;
            bImg.type = Image.Type.Sliced;
            callButton = UiKit.CreateButton("Call", bottom, "", new Vector2(0.03f, 0.18f), new Vector2(0.43f, 0.82f), OnCall, 22);
            callLabel = UiKit.ButtonLabel(callButton);
            speedButton = UiKit.CreateButton("Speed", bottom, "x1", new Vector2(0.45f, 0.18f), new Vector2(0.57f, 0.82f), OnSpeed, 28, new Color(0.22f, 0.10f, 0.08f));
            speedLabel = UiKit.ButtonLabel(speedButton);
            powerButton = UiKit.CreateButton("Power", bottom, "", new Vector2(0.59f, 0.18f), new Vector2(0.97f, 0.82f), OnPower, 20, new Color(0.45f, 0.22f, 0.5f));
            powerLabel = UiKit.ButtonLabel(powerButton);

            // Build menu: the five towers, shown when an empty spot is tapped.
            var bm = UiKit.CreateRect("BuildMenu", hud, new Vector2(0f, 0.15f), new Vector2(1f, 0.33f));
            var bmImg = bm.gameObject.AddComponent<Image>();
            bmImg.sprite = ApogeeTheme.Panel;
            bmImg.type = Image.Type.Sliced;
            buildMenu = bm.gameObject;
            UiKit.Outlined(UiKit.CreateText("BuildTitle", bm, "CONSTRUIRE", 24, TextAnchor.MiddleLeft, new Vector2(0.04f, 0.8f), new Vector2(0.6f, 0.97f), ApogeeTheme.Gold));
            UiKit.CreateButton("BuildClose", bm, "FERMER", new Vector2(0.74f, 0.79f), new Vector2(0.97f, 0.97f), CloseMenus, 18, new Color(0.22f, 0.10f, 0.08f));
            float w = 0.94f / 5f;
            for (int i = 0; i < 5; i++)
            {
                int captured = i;
                var def = BastionCatalog.Towers[i];
                var b = UiKit.CreateButton($"Build_{i}", bm, "", new Vector2(0.03f + i * w + 0.005f, 0.05f), new Vector2(0.03f + (i + 1) * w - 0.005f, 0.76f),
                    () => Build((TowerKind)captured), 18, UiKit.CardColor);
                var icon = UiKit.CreateImage("Icon", b.transform, new Vector2(0.15f, 0.42f), new Vector2(0.85f, 0.97f), BastionArt.Icon(def.Kind), Color.white);
                icon.raycastTarget = false;
                var name = UiKit.Outlined(UiKit.CreateText("Name", b.transform, def.Name, 18, TextAnchor.MiddleCenter, new Vector2(0.02f, 0.22f), new Vector2(0.98f, 0.42f), ApogeeTheme.Cream), 1.2f);
                UiKit.FitLabel(name, 18);
                buildCosts[i] = UiKit.CreateText("Cost", b.transform, "", 18, TextAnchor.MiddleCenter, new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.22f), PlaceholderVisuals.CoinColor);
                buildButtons[i] = b;
            }
            buildMenu.SetActive(false);

            // Tower menu: what it is, upgrade, sell.
            var tm = UiKit.CreateRect("TowerMenu", hud, new Vector2(0f, 0.15f), new Vector2(1f, 0.29f));
            var tmImg = tm.gameObject.AddComponent<Image>();
            tmImg.sprite = ApogeeTheme.Panel;
            tmImg.type = Image.Type.Sliced;
            towerMenu = tm.gameObject;
            towerInfo = UiKit.CreateText("Info", tm, "", 22, TextAnchor.MiddleLeft, new Vector2(0.04f, 0.55f), new Vector2(0.74f, 0.95f), ApogeeTheme.Cream);
            UiKit.FitLabel(towerInfo, 22);
            UiKit.CreateButton("TowerClose", tm, "FERMER", new Vector2(0.76f, 0.6f), new Vector2(0.97f, 0.95f), CloseMenus, 18, new Color(0.22f, 0.10f, 0.08f));
            upgradeButton = UiKit.CreateButton("Upgrade", tm, "", new Vector2(0.04f, 0.07f), new Vector2(0.58f, 0.5f), Upgrade, 20, new Color(0.25f, 0.4f, 0.18f));
            upgradeLabel = IconText.OnButton(upgradeButton, 21);
            sellButton = UiKit.CreateButton("Sell", tm, "", new Vector2(0.61f, 0.07f), new Vector2(0.97f, 0.5f), Sell, 20, new Color(0.45f, 0.22f, 0.12f));
            sellLabel = IconText.OnButton(sellButton, 21);
            towerMenu.SetActive(false);

            // ---- end of a map ----
            var overRt = UiKit.CreatePanel("BastionOver", rt, UiKit.Overlay);
            overPanel = overRt.gameObject;
            UiKit.CreateFrame("OverFrame", overRt, new Vector2(0.08f, 0.22f), new Vector2(0.92f, 0.8f));
            overTitle = UiKit.Outlined(UiKit.CreateText("OverTitle", overRt, "", 50, TextAnchor.MiddleCenter, new Vector2(0.05f, 0.68f), new Vector2(0.95f, 0.77f), ApogeeTheme.Gold), 2.5f);
            for (int s = 0; s < 3; s++)
            {
                float x = 0.32f + s * 0.12f;
                overStars[s] = UiKit.CreateImage($"Star_{s}", overRt, new Vector2(x, 0.6f), new Vector2(x + 0.12f, 0.67f), PlaceholderVisuals.Star(), ApogeeTheme.Gold);
            }
            overDetail = UiKit.CreateText("OverDetail", overRt, "", 26, TextAnchor.MiddleCenter, new Vector2(0.1f, 0.53f), new Vector2(0.9f, 0.6f), ApogeeTheme.Cream);
            UiKit.FitLabel(overDetail, 26);
            overReward = IconText.Create("OverReward", overRt, "", 30, TextAnchor.MiddleCenter, new Vector2(0.1f, 0.46f), new Vector2(0.9f, 0.53f), PlaceholderVisuals.CoinColor);
            UiKit.CreateButton("Again", overRt, "REJOUER", new Vector2(0.22f, 0.37f), new Vector2(0.78f, 0.44f), () => StartMap(mapIndex));
            UiKit.CreateButton("Maps", overRt, "CARTES", new Vector2(0.22f, 0.29f), new Vector2(0.78f, 0.355f), ShowSelect);
            UiKit.CreateButton("Menu", overRt, "MENU", new Vector2(0.3f, 0.23f), new Vector2(0.7f, 0.28f), ReturnToHub, 22);
            overPanel.SetActive(false);

            // ---- map select ----
            var sel = UiKit.CreatePanel("BastionSelect", rt, ApogeeTheme.Maroon);
            selectPanel = sel.gameObject;
            UiKit.Outlined(UiKit.CreateText("SelectTitle", sel, "BASTION", 56, TextAnchor.MiddleCenter, new Vector2(0.05f, 0.88f), new Vector2(0.95f, 0.96f), ApogeeTheme.Gold), 2.5f);
            UiKit.CreateText("SelectHint", sel, "Défends le bastion : une tour à la fois", 22, TextAnchor.MiddleCenter, new Vector2(0.05f, 0.84f), new Vector2(0.95f, 0.88f), UiKit.TextDim);
            int n = BastionCatalog.Campaign.Length;
            const float top = 0.82f, bottomY = 0.3f;
            float slot = (top - bottomY) / n;
            for (int i = 0; i < n; i++)
            {
                int captured = i;
                var m = BastionCatalog.Campaign[i];
                float yMax = top - i * slot, yMin = yMax - slot + 0.012f;
                var card = UiKit.CreateButton($"Map_{i}", sel, "", new Vector2(0.06f, yMin), new Vector2(0.94f, yMax), () => StartMap(captured), 24, UiKit.CardColor);
                var title = UiKit.Outlined(UiKit.CreateText("Name", card.transform, $"{i + 1}. {m.Name}", 28, TextAnchor.MiddleLeft, new Vector2(0.05f, 0.5f), new Vector2(0.7f, 0.95f), ApogeeTheme.Gold), 1.5f);
                UiKit.FitLabel(title, 28);
                UiKit.CreateText("Sub", card.transform, $"{m.Subtitle}   ·   {m.Waves} vagues", 18, TextAnchor.MiddleLeft, new Vector2(0.05f, 0.08f), new Vector2(0.95f, 0.48f), UiKit.TextDim);
                var stars = UiKit.CreateText("Stars", card.transform, "", 26, TextAnchor.MiddleRight, new Vector2(0.6f, 0.5f), new Vector2(0.95f, 0.95f), ApogeeTheme.Gold);
                mapCards.Add((card, stars));
            }
            var endless = UiKit.CreateButton("Endless", sel, "", new Vector2(0.06f, 0.155f), new Vector2(0.94f, 0.285f), () => StartMap(-1), 24, new Color(0.45f, 0.18f, 0.12f));
            UiKit.Outlined(UiKit.CreateText("EndlessName", endless.transform, "INFINI : " + BastionCatalog.Endless.Name, 28, TextAnchor.MiddleLeft, new Vector2(0.05f, 0.5f), new Vector2(0.95f, 0.95f), ApogeeTheme.Gold), 1.5f);
            endlessBestText = UiKit.CreateText("EndlessBest", endless.transform, "", 20, TextAnchor.MiddleLeft, new Vector2(0.05f, 0.08f), new Vector2(0.95f, 0.48f), ApogeeTheme.Cream);
            UiKit.CreateButton("SelectBack", sel, "RETOUR", new Vector2(0.32f, 0.04f), new Vector2(0.68f, 0.12f), ReturnToHub);
        }

        // ---- lifecycle --------------------------------------------------------------------

        protected override void OnEnter()
        {
            var runnerPlayer = ui.Director != null ? ui.Director.Player : null;
            if (runnerPlayer != null) runnerPlayer.controlEnabled = false;
            MobileInput.Reset();
            ShowSelect();
        }

        protected override void OnExit()
        {
            playing = false;
            ClearWorld();
            ReleaseCamera();
        }

        void ShowSelect()
        {
            playing = false;
            ClearWorld();
            ReleaseCamera();
            overPanel.SetActive(false);
            hudRoot.SetActive(false);
            selectPanel.SetActive(true);
            for (int i = 0; i < mapCards.Count; i++)
            {
                int stars = SaveSystem.GetBastionStars(i);
                bool open = i == 0 || SaveSystem.GetBastionStars(i - 1) > 0;
                mapCards[i].card.interactable = open;
                mapCards[i].stars.text = open ? $"{stars}/3 étoiles" : "VERROUILLÉ";
            }
            int best = SaveSystem.BastionEndlessBest;
            endlessBestText.text = best > 0 ? $"Record : vague {best}   ·   des vagues sans fin" : "Des vagues sans fin : tiens le plus longtemps possible";
        }

        void ClearWorld()
        {
            foreach (var c in creeps) if (c.go != null) Destroy(c.go);
            creeps.Clear();
            zones.Clear();
            tracers.Clear();
            if (root != null) Destroy(root.gameObject);
            root = null;
            hero = null;
            rangeRing = null;
        }

        void StartMap(int index)
        {
            ClearWorld();
            mapIndex = index;
            map = index < 0 ? BastionCatalog.Endless : BastionCatalog.Campaign[Mathf.Clamp(index, 0, BastionCatalog.Campaign.Length - 1)];
            path = map.Path;
            pathLength = BastionCatalog.PathLength(path);
            slots = BastionCatalog.Slots(path);
            slotTowers = new Tower[slots.Count];

            gold = map.StartGold;
            lives = BastionCatalog.StartLives;
            wave = 0;
            kills = 0;
            speed = 1;
            waveRunning = false;
            queue.Clear();
            nextWaveTimer = 0f;
            selectedSlot = -1;
            heroSelected = false;
            power = BastionCatalog.PowerOf(SaveSystem.SelectedSkinId);
            powerCooldown = 8f;

            BuildWorld();
            TakeOverCamera(new Vector3(Origin.x, Origin.y, -10f), 10f, ApogeeTheme.SkyAverage);
            FrameCamera();

            selectPanel.SetActive(false);
            overPanel.SetActive(false);
            hudRoot.SetActive(true);
            CloseMenus();
            playing = true;
            messageText.text = "Touche un emplacement pour construire une tour";
            ui.ShowBanner(map.Name, map.Subtitle, 2f);
            RefreshHud();
        }

        void FrameCamera()
        {
            if (cam == null) return;
            float band = RegionTop - RegionBottom;
            float ortho = Mathf.Max((FieldHalfH * 2f + 0.4f) / band / 2f, (FieldHalfW * 2f + 0.4f) / (2f * Mathf.Max(0.1f, cam.aspect)));
            float visible = ortho * 2f;
            float bandCentre = (RegionBottom + RegionTop) / 2f;
            cam.orthographicSize = ortho;
            cam.transform.position = new Vector3(Origin.x, Origin.y + (0.5f - bandCentre) * visible, -10f);
        }

        // ---- world ------------------------------------------------------------------------

        static Vector3 W(Vector2 p, float z = 0f) => new Vector3(Origin.x + p.x, Origin.y + p.y, z);

        SpriteRenderer Quad(string name, Vector2 at, Vector2 size, Color color, int order, Sprite sprite = null, Transform parent = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent != null ? parent : root, false);
            go.transform.position = W(at);
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite != null ? sprite : PlaceholderVisuals.Square(Color.white);
            sr.color = color;
            sr.sortingOrder = order;
            return sr;
        }

        static Material lineMaterial;

        LineRenderer Road(string name, float width, Color color, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            var lr = go.AddComponent<LineRenderer>();
            if (lineMaterial == null) lineMaterial = new Material(Shader.Find("Sprites/Default"));
            lr.sharedMaterial = lineMaterial;
            lr.useWorldSpace = true;
            lr.positionCount = path.Length;
            for (int i = 0; i < path.Length; i++) lr.SetPosition(i, W(path[i]));
            lr.widthMultiplier = width;
            lr.startColor = lr.endColor = color;
            lr.numCornerVertices = 5;
            lr.numCapVertices = 3;
            lr.sortingOrder = order;
            return lr;
        }

        void BuildWorld()
        {
            root = new GameObject("BastionWorld").transform;

            // The island: crimson grass with a darker rim, floating in the sunset.
            Quad("IslandRim", Vector2.zero, new Vector2(FieldHalfW * 2f + 0.9f, FieldHalfH * 2f + 0.9f), new Color(0.24f, 0.10f, 0.08f), -9);
            Quad("Island", Vector2.zero, new Vector2(FieldHalfW * 2f + 0.5f, FieldHalfH * 2f + 0.5f), new Color(0.42f, 0.17f, 0.12f), -8);

            // The road, with a darker verge.
            Road("RoadVerge", 1.2f, new Color(0.30f, 0.17f, 0.12f), -7);
            Road("Road", 0.95f, new Color(0.74f, 0.56f, 0.38f), -6);

            // Entry (a dark rift) and the bastion's gate.
            Quad("Rift", path[0], new Vector2(1.6f, 0.8f), new Color(0.12f, 0.03f, 0.08f), -5, PlaceholderVisuals.Circle(Color.white));
            var gate = path[path.Length - 1] + new Vector2(0f, 0.2f);
            Quad("Gate", gate, new Vector2(2.2f, 1.0f), new Color(0.55f, 0.42f, 0.32f), 2);
            for (int i = 0; i < 4; i++)
                Quad("Merlon", gate + new Vector2(-0.82f + i * 0.55f, 0.62f), new Vector2(0.3f, 0.3f), new Color(0.55f, 0.42f, 0.32f), 2);
            Quad("GateDoor", gate + new Vector2(0f, -0.15f), new Vector2(0.6f, 0.7f), new Color(0.25f, 0.13f, 0.08f), 3);
            Quad("Banner", gate + new Vector2(0f, 0.95f), new Vector2(0.18f, 0.5f), ApogeeTheme.Gold, 3);

            // Build spots.
            for (int i = 0; i < slots.Count; i++)
                Quad($"Spot_{i}", slots[i], new Vector2(0.86f, 0.86f), Color.white, -4, PlaceholderVisuals.RimCircle(new Color(0.42f, 0.38f, 0.35f)));

            // Trees and rocks, away from the road and the spots.
            for (int i = 0; i < 40; i++)
            {
                var p = new Vector2(Random.Range(-FieldHalfW, FieldHalfW), Random.Range(-FieldHalfH + 0.5f, FieldHalfH - 0.5f));
                if (BastionCatalog.DistanceToPath(path, p) < 1.0f) continue;
                bool nearSpot = false;
                foreach (var s in slots) if ((s - p).sqrMagnitude < 0.8f * 0.8f) { nearSpot = true; break; }
                if (nearSpot) continue;
                if (Random.value < 0.6f)
                {
                    Quad("Trunk", p + new Vector2(0f, -0.2f), new Vector2(0.1f, 0.3f), new Color(0.3f, 0.16f, 0.1f), -3);
                    float s2 = Random.Range(0.45f, 0.75f);
                    Quad("Leaves", p + new Vector2(0f, 0.05f), new Vector2(s2, s2), new Color(0.8f, Random.Range(0.18f, 0.32f), 0.12f), -2, PlaceholderVisuals.Circle(Color.white));
                }
                else
                {
                    float s2 = Random.Range(0.25f, 0.45f);
                    Quad("Rock", p, new Vector2(s2, s2 * 0.7f), new Color(0.5f, 0.45f, 0.42f), -3, PlaceholderVisuals.Circle(Color.white));
                }
            }

            // Range ring, reused for whatever is selected.
            rangeRing = Quad("RangeRing", Vector2.zero, Vector2.one, new Color(1f, 0.9f, 0.6f, 0.18f), -1, PlaceholderVisuals.Circle(Color.white)).gameObject;
            rangeRing.SetActive(false);

            BuildHero();
        }

        void BuildHero()
        {
            var skin = SkinCatalog.Find(SaveSystem.SelectedSkinId);
            var go = new GameObject("Hero");
            go.transform.SetParent(root, false);
            hero = go.transform;
            heroRing = Quad("HeroRing", Vector2.zero, new Vector2(0.9f, 0.45f), new Color(1f, 0.85f, 0.4f, 0.55f), 3, PlaceholderVisuals.RimCircle(ApogeeTheme.Gold), hero).gameObject;
            heroRing.transform.localPosition = new Vector3(0f, -0.38f, 0f);
            heroRing.SetActive(false);
            var art = new GameObject("Art");
            art.transform.SetParent(hero, false);
            heroSr = art.AddComponent<SpriteRenderer>();
            var portrait = SkinCatalog.LoadPortrait(skin, out bool custom);
            heroSr.sprite = custom ? portrait : ui.PlayerSprite;
            heroSr.color = custom ? Color.white : skin.Tint;
            heroSr.sortingOrder = 6;
            if (heroSr.sprite != null)
            {
                var b = heroSr.sprite.bounds;
                float scale = 0.95f / Mathf.Max(0.01f, Mathf.Max(b.size.x, b.size.y));
                art.transform.localScale = Vector3.one * scale;
                art.transform.localPosition = -b.center * scale;
            }
            // The hero starts guarding the road a little before the gate.
            float d = Mathf.Max(0f, pathLength - 3f);
            var onRoad = BastionCatalog.PointAt(path, d);
            heroPos = heroTarget = onRoad + new Vector2(onRoad.x > 0f ? -1.1f : 1.1f, 0f);
            hero.position = W(heroPos, -0.5f);
        }

        // ---- frame ------------------------------------------------------------------------

        void Update()
        {
            if (!IsActive || root == null) return;
            FrameCamera();
            UpdateTracers(Time.deltaTime);
            if (!playing) return;

            HandleInput();
            float dt = Time.deltaTime * speed;
            if (dt <= 0f) return;

            UpdateWaves(dt);
            UpdateCreeps(dt);
            if (!playing) return;
            UpdateTowers(dt);
            UpdateZones(dt);
            UpdateHero(dt);
            powerCooldown = Mathf.Max(0f, powerCooldown - dt);
            RefreshHud();
        }

        void UpdateWaves(float dt)
        {
            if (waveRunning)
            {
                if (queue.Count > 0)
                {
                    spawnTimer -= dt;
                    if (spawnTimer <= 0f)
                    {
                        spawnTimer = BastionCatalog.SpawnGap(wave);
                        SpawnCreep(queue[0]);
                        queue.RemoveAt(0);
                    }
                }
                else if (creeps.Count == 0)
                {
                    WaveCleared();
                }
            }
            else if (wave > 0)
            {
                nextWaveTimer -= dt;
                if (nextWaveTimer <= 0f) StartNextWave(early: false);
            }
        }

        void StartNextWave(bool early)
        {
            if (waveRunning || !playing) return;
            if (early && wave > 0 && nextWaveTimer > 0f)
            {
                // Calling the wave early pays for the time given up.
                int bonus = Mathf.CeilToInt(nextWaveTimer * 2f);
                gold += bonus;
                Fx.Text(W(path[0]) + Vector3.down * 0.6f, $"+{bonus} OR", PlaceholderVisuals.CoinColor, 1f);
            }
            wave++;
            waveRunning = true;
            queue.Clear();
            queue.AddRange(BastionCatalog.WaveCreeps(wave));
            healthScale = BastionCatalog.HealthScale(wave, map.Toughness);
            spawnTimer = 0.3f;
            bool boss = wave % 10 == 0;
            ui.ShowBanner(boss ? "UN COLOSSE !" : $"VAGUE {wave}", boss ? "Il vaut cinq vies : arrête-le" : $"{queue.Count} morts en approche", 1.6f);
            messageText.text = "";
        }

        void WaveCleared()
        {
            waveRunning = false;
            int bonus = BastionCatalog.WaveBonus(wave);
            gold += bonus;
            Sfx.Milestone();
            if (!map.Endless && wave >= map.Waves) { EndMap(true); return; }
            nextWaveTimer = BetweenWaves;
            messageText.text = $"Vague {wave} repoussée  ·  +{bonus} or";
        }

        // ---- creeps -------------------------------------------------------------------------

        void SpawnCreep(CreepKind kind)
        {
            var def = BastionCatalog.Creep(kind);
            var c = new Creep { def = def, maxHp = def.Hp * healthScale, dist = 0f, bob = Random.Range(0f, 6f) };
            c.hp = c.maxHp;
            var go = new GameObject($"Creep_{kind}");
            go.transform.SetParent(root, false);
            c.go = go;
            float h = def.Height;
            Transform rig = null;
            if (KenneyProps.Available)
            {
                var size = KenneyProps.Size(PropKit.Graveyard, def.Model);
                rig = KenneyProps.Spawn(PropKit.Graveyard, def.Model, go.transform, new Vector3(0f, -h / 2f + (def.Flies ? 0.35f : 0f), 0f),
                    h / Mathf.Max(0.01f, size.y), PropLayer.Character, 0f, -8f);
            }
            if (rig == null)
            {
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = PlaceholderVisuals.Zombie();
                sr.sortingOrder = 5;
                go.transform.localScale = Vector3.one * h;
            }
            if (def.Flies)
                Quad("Shadow", Vector2.zero, new Vector2(0.6f, 0.2f), new Color(0f, 0f, 0f, 0.3f), 1, PlaceholderVisuals.Circle(Color.white), go.transform).transform.localPosition = new Vector3(0f, -h / 2f, 0.5f);
            if (def.Armored)
                Quad("Armor", Vector2.zero, new Vector2(0.32f, 0.32f), Color.white, 8, GameIcons.PowerUp(PowerUpKind.Shield), go.transform).transform.localPosition = new Vector3(0.3f, h * 0.45f, -0.2f);

            // Health bar, shown once hurt.
            c.hpBar = new GameObject("HpBar");
            c.hpBar.transform.SetParent(go.transform, false);
            c.hpBar.transform.localPosition = new Vector3(0f, h / 2f + 0.25f + (def.Flies ? 0.35f : 0f), -0.3f);
            Quad("Bg", Vector2.zero, new Vector2(0.7f, 0.09f), new Color(0.1f, 0.03f, 0.03f), 9, null, c.hpBar.transform).transform.localPosition = Vector3.zero;
            var fill = Quad("Fill", Vector2.zero, new Vector2(0.66f, 0.06f), new Color(0.9f, 0.25f, 0.2f), 10, null, c.hpBar.transform).transform;
            fill.localPosition = new Vector3(0f, 0f, -0.01f);
            c.hpFill = fill;
            c.hpBar.SetActive(false);

            c.pos = path[0];
            go.transform.position = W(c.pos, -1f);
            creeps.Add(c);
        }

        void UpdateCreeps(float dt)
        {
            float now = Time.time;
            for (int i = creeps.Count - 1; i >= 0; i--)
            {
                var c = creeps[i];
                float speedNow = c.def.Speed * (now < c.slowUntil ? c.slowFactor : 1f) * (now < c.stunUntil ? 0f : 1f);
                c.dist += speedNow * dt;
                if (c.dist >= pathLength)
                {
                    // Through the gate.
                    lives -= c.def.Lives;
                    Fx.Burst(W(path[path.Length - 1]), new Color(0.9f, 0.2f, 0.15f), 14, 3f, 0.1f);
                    Fx.Shake(0.2f, 0.2f);
                    Sfx.Hit();
                    Destroy(c.go);
                    creeps.RemoveAt(i);
                    if (lives <= 0) { lives = 0; EndMap(false); return; }
                    continue;
                }
                c.pos = BastionCatalog.PointAt(path, c.dist);
                c.bob += dt * 7f;
                c.go.transform.position = W(c.pos + new Vector2(0f, Mathf.Abs(Mathf.Sin(c.bob)) * 0.04f), -1f);
            }
        }

        /// <summary>Damage from a tower (or the hero, src null): armour and the pylon's bonus on flyers apply.</summary>
        void Hit(Creep c, float damage, TowerDef src)
        {
            if (c.hp <= 0f) return;
            if (src != null && src.Physical && c.def.Armored) damage *= BastionCatalog.ArmorFactor;
            if (src != null && src.Kind == TowerKind.Pylon && c.def.Flies) damage *= 2f;
            c.hp -= damage;
            if (c.hp > 0f)
            {
                c.hpBar.SetActive(true);
                float f = Mathf.Clamp01(c.hp / c.maxHp);
                c.hpFill.localScale = new Vector3(0.66f * f, 0.06f, 1f);
                c.hpFill.localPosition = new Vector3(-0.33f * (1f - f), 0f, -0.01f);
                return;
            }
            gold += c.def.Gold;
            kills++;
            Fx.Burst(c.go.transform.position, new Color(0.7f, 0.25f, 0.15f), c.def.Kind == CreepKind.Colossus ? 30 : 10, 3f, 0.09f);
            if (c.def.Kind == CreepKind.Colossus) { Fx.Shake(0.4f, 0.3f); Sfx.Milestone(); }
            else Sfx.Kill();
            Destroy(c.go);
            creeps.Remove(c);
        }

        // ---- towers -----------------------------------------------------------------------

        void UpdateTowers(float dt)
        {
            for (int s = 0; s < slotTowers.Length; s++)
            {
                var t = slotTowers[s];
                if (t == null) continue;
                t.cooldown -= dt;
                if (t.cooldown > 0f) continue;
                Vector2 at = slots[s];
                float range = BastionCatalog.RangeAt(t.def, t.level);
                float dmg = BastionCatalog.DamageAt(t.def, t.level);

                if (t.def.Kind == TowerKind.Brazier)
                {
                    // The brazier burns everything on the ground around it.
                    bool any = false;
                    for (int i = creeps.Count - 1; i >= 0; i--)
                    {
                        var c = creeps[i];
                        if (c.def.Flies || (c.pos - at).sqrMagnitude > range * range) continue;
                        any = true;
                        Hit(c, dmg, t.def);
                    }
                    if (any)
                    {
                        t.cooldown = BastionCatalog.IntervalAt(t.def, t.level);
                        Fx.Burst(W(at) + Vector3.up * 0.2f, new Color(1f, 0.55f, 0.2f), 3, 1.5f, 0.06f, -0.5f);
                    }
                    continue;
                }

                var target = FirstInRange(at, range, t.def.HitsAir);
                if (target == null) continue;
                t.cooldown = BastionCatalog.IntervalAt(t.def, t.level);
                Shoot(W(at, -0.2f) + Vector3.up * 0.3f, target.go.transform.position, t.def.Color, t.def.Kind == TowerKind.Cannon ? 0.28f : 0.14f);

                switch (t.def.Kind)
                {
                    case TowerKind.Cannon:
                        Vector2 impact = target.pos;
                        Fx.Burst(W(impact), new Color(0.5f, 0.45f, 0.4f), 8, 2.5f, 0.08f);
                        for (int i = creeps.Count - 1; i >= 0; i--)
                        {
                            var c = creeps[i];
                            if (!c.def.Flies && (c.pos - impact).sqrMagnitude <= t.def.Splash * t.def.Splash) Hit(c, dmg, t.def);
                        }
                        break;
                    case TowerKind.Frost:
                        target.slowUntil = Time.time + t.def.SlowTime;
                        target.slowFactor = 1f - t.def.Slow;
                        Hit(target, dmg, t.def);
                        break;
                    case TowerKind.Pylon:
                        Hit(target, dmg, t.def);
                        Creep prev = target;
                        var hitSet = new HashSet<Creep> { target };
                        for (int j = 1; j < t.def.Chain; j++)
                        {
                            Creep next = null;
                            float best = 1.5f * 1.5f;
                            foreach (var c in creeps)
                            {
                                if (hitSet.Contains(c)) continue;
                                float d = (c.pos - prev.pos).sqrMagnitude;
                                if (d < best) { best = d; next = c; }
                            }
                            if (next == null) break;
                            Shoot(prev.go != null ? prev.go.transform.position : W(prev.pos), next.go.transform.position, t.def.Color, 0.1f);
                            hitSet.Add(next);
                            Hit(next, dmg * 0.7f, t.def);
                            prev = next;
                        }
                        break;
                    default:
                        Hit(target, dmg, t.def);
                        break;
                }
            }
        }

        /// <summary>The creep furthest along the road within reach (the classic "first" target).</summary>
        Creep FirstInRange(Vector2 at, float range, bool air)
        {
            Creep best = null;
            foreach (var c in creeps)
            {
                if (c.def.Flies && !air) continue;
                if ((c.pos - at).sqrMagnitude > range * range) continue;
                if (best == null || c.dist > best.dist) best = c;
            }
            return best;
        }

        void Shoot(Vector3 from, Vector3 to, Color color, float size)
        {
            var go = new GameObject("Shot");
            go.transform.SetParent(root, false);
            go.transform.position = from;
            go.transform.localScale = Vector3.one * size;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = PlaceholderVisuals.Circle(Color.white);
            sr.color = color;
            sr.sortingOrder = 11;
            tracers.Add(new Tracer { t = go.transform, from = from, to = to, duration = 0.12f });
        }

        void UpdateTracers(float dt)
        {
            for (int i = tracers.Count - 1; i >= 0; i--)
            {
                var tr = tracers[i];
                tr.time += dt;
                if (tr.t == null || tr.time >= tr.duration)
                {
                    if (tr.t != null) Destroy(tr.t.gameObject);
                    tracers.RemoveAt(i);
                    continue;
                }
                tr.t.position = Vector3.Lerp(tr.from, tr.to, tr.time / tr.duration);
            }
        }

        void UpdateZones(float dt)
        {
            for (int i = zones.Count - 1; i >= 0; i--)
            {
                var z = zones[i];
                if (Time.time >= z.until)
                {
                    if (z.go != null) Destroy(z.go);
                    zones.RemoveAt(i);
                    continue;
                }
                z.tick -= dt;
                if (z.tick > 0f) continue;
                z.tick = 0.5f;
                for (int j = creeps.Count - 1; j >= 0; j--)
                {
                    var c = creeps[j];
                    if (!c.def.Flies && (c.pos - z.at).sqrMagnitude <= z.radius * z.radius) Hit(c, z.dps * 0.5f, null);
                }
            }
        }

        // ---- the hero ------------------------------------------------------------------------

        float HeroDamage => 5f * UpgradeManager.FirePowerMultiplier * (1f + wave * 0.08f);

        void UpdateHero(float dt)
        {
            if (hero == null) return;
            heroPos = Vector2.MoveTowards(heroPos, heroTarget, HeroSpeed * dt);
            hero.position = W(heroPos + new Vector2(0f, (heroPos - heroTarget).sqrMagnitude > 0.001f ? Mathf.Abs(Mathf.Sin(Time.time * 12f)) * 0.05f : 0f), -0.5f);
            if (heroTarget.x != heroPos.x) heroSr.flipX = heroTarget.x < heroPos.x;
            heroRing.SetActive(heroSelected);

            heroCooldown -= dt;
            if (heroCooldown > 0f) return;
            var target = FirstInRange(heroPos, HeroRange, air: true);
            if (target == null) return;
            heroCooldown = HeroInterval;
            heroSr.flipX = target.pos.x < heroPos.x;
            Shoot(hero.position + Vector3.up * 0.2f, target.go.transform.position, PlaceholderVisuals.ProjectileColor, 0.14f);
            Hit(target, HeroDamage, null);
        }

        void OnPower()
        {
            if (!playing || powerCooldown > 0f || hero == null) return;
            powerCooldown = BastionCatalog.PowerCooldown;
            float k = Mathf.Max(1f, healthScale);
            var at = heroPos;
            Sfx.Spring();
            ui.ShowBanner(BastionCatalog.PowerName(power), BastionCatalog.PowerBlurb(power), 1.2f);
            switch (power)
            {
                case BastionCatalog.HeroPower.Volley:
                    var near = new List<Creep>(creeps);
                    near.Sort((a, b) => (a.pos - at).sqrMagnitude.CompareTo((b.pos - at).sqrMagnitude));
                    for (int i = 0; i < Mathf.Min(6, near.Count); i++)
                    {
                        Shoot(hero.position, near[i].go.transform.position, ApogeeTheme.Gold, 0.18f);
                        Hit(near[i], 15f * k, null);
                    }
                    break;
                case BastionCatalog.HeroPower.Grenade:
                    Blast(DensestPoint(), 1.5f, 55f * k, new Color(1f, 0.55f, 0.15f));
                    break;
                case BastionCatalog.HeroPower.Toxic:
                    AddZone(at, 1.9f, 7f * k, 6f, new Color(0.45f, 0.85f, 0.25f, 0.35f));
                    break;
                case BastionCatalog.HeroPower.Rampart:
                    lives = Mathf.Min(BastionCatalog.StartLives, lives + 3);
                    Fx.Burst(W(path[path.Length - 1]), new Color(0.5f, 1f, 0.6f), 26, 4f, 0.1f, 0f);
                    break;
                case BastionCatalog.HeroPower.FireWall:
                    AddZone(at, 2.4f, 10f * k, 5f, new Color(1f, 0.45f, 0.15f, 0.35f));
                    break;
                case BastionCatalog.HeroPower.Salvage:
                    gold += 120;
                    Fx.Text(hero.position + Vector3.up, "+120 OR", PlaceholderVisuals.CoinColor, 1.2f);
                    break;
                case BastionCatalog.HeroPower.Calm:
                    foreach (var c in creeps) { c.slowUntil = Time.time + 5f; c.slowFactor = 0.5f; }
                    Fx.Burst(hero.position, new Color(0.7f, 0.85f, 1f), 30, 5f, 0.1f, 0f);
                    break;
                case BastionCatalog.HeroPower.Drain:
                    for (int i = creeps.Count - 1; i >= 0; i--)
                        if ((creeps[i].pos - at).sqrMagnitude <= 9f) Hit(creeps[i], 25f * k, null);
                    lives = Mathf.Min(BastionCatalog.StartLives, lives + 1);
                    Fx.Burst(hero.position, new Color(0.6f, 0.2f, 0.7f), 26, 4f, 0.1f, 0f);
                    break;
                case BastionCatalog.HeroPower.Ambush:
                    for (int i = creeps.Count - 1; i >= 0; i--)
                    {
                        var c = creeps[i];
                        if ((c.pos - at).sqrMagnitude > 2.5f * 2.5f) continue;
                        c.stunUntil = Time.time + 3f;
                        Hit(c, 20f * k, null);
                    }
                    Fx.Burst(hero.position, new Color(0.9f, 0.9f, 0.6f), 24, 4f, 0.1f, 0f);
                    break;
                case BastionCatalog.HeroPower.Meteor:
                    Creep big = null;
                    foreach (var c in creeps) if (big == null || c.hp > big.hp) big = c;
                    if (big != null) Blast(big.pos, 1.3f, 200f * k, new Color(1f, 0.4f, 0.1f));
                    break;
            }
            RefreshHud();
        }

        Vector2 DensestPoint()
        {
            Vector2 best = heroPos;
            int bestCount = -1;
            foreach (var c in creeps)
            {
                int n = 0;
                foreach (var o in creeps) if ((o.pos - c.pos).sqrMagnitude < 1.69f) n++;
                if (n > bestCount) { bestCount = n; best = c.pos; }
            }
            return best;
        }

        void Blast(Vector2 at, float radius, float damage, Color color)
        {
            Fx.Burst(W(at), color, 34, 5.5f, 0.15f, 0.3f);
            Fx.Shake(0.35f, 0.3f);
            for (int i = creeps.Count - 1; i >= 0; i--)
                if ((creeps[i].pos - at).sqrMagnitude <= radius * radius) Hit(creeps[i], damage, null);
        }

        void AddZone(Vector2 at, float radius, float dps, float duration, Color color)
        {
            var sr = Quad("Zone", at, Vector2.one * radius * 2f, color, -1, PlaceholderVisuals.Circle(Color.white));
            zones.Add(new Zone { at = at, radius = radius, dps = dps, until = Time.time + duration, go = sr.gameObject });
        }

        // ---- input, building ---------------------------------------------------------------

        void HandleInput()
        {
            var pointer = Pointer.current;
            if (pointer == null || cam == null) return;
            if (pointer.press.wasPressedThisFrame)
            {
                pressActive = true;
                pressOverUi = IsPointerOverUi();
            }
            if (!pointer.press.wasReleasedThisFrame || !pressActive) return;
            pressActive = false;
            if (pressOverUi) return;

            Vector2 screen = pointer.position.ReadValue();
            var world = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 10f));
            var p = new Vector2(world.x - Origin.x, world.y - Origin.y);
            OnTap(p);
        }

        static bool IsPointerOverUi()
        {
            var es = EventSystem.current;
            if (es == null) return false;
            if (es.IsPointerOverGameObject()) return true;
            var touch = Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.isPressed)
                return es.IsPointerOverGameObject(touch.primaryTouch.touchId.ReadValue());
            return false;
        }

        void OnTap(Vector2 p)
        {
            // A spot?
            for (int i = 0; i < slots.Count; i++)
            {
                if ((slots[i] - p).sqrMagnitude > 0.55f * 0.55f) continue;
                heroSelected = false;
                SelectSlot(i);
                return;
            }
            // The hero?
            if ((heroPos - p).sqrMagnitude < 0.6f * 0.6f)
            {
                CloseMenus();
                heroSelected = !heroSelected;
                messageText.text = heroSelected ? "Touche le terrain pour envoyer le héros" : "";
                return;
            }
            // Somewhere else: move the selected hero there, or just close the menus.
            if (heroSelected)
            {
                heroTarget = new Vector2(Mathf.Clamp(p.x, -FieldHalfW, FieldHalfW), Mathf.Clamp(p.y, -FieldHalfH, FieldHalfH));
                heroSelected = false;
                messageText.text = "";
                return;
            }
            CloseMenus();
        }

        void SelectSlot(int i)
        {
            selectedSlot = i;
            var t = slotTowers[i];
            if (rangeRing != null)
            {
                rangeRing.SetActive(true);
                rangeRing.transform.position = W(slots[i], 0.1f);
                float r = t != null ? BastionCatalog.RangeAt(t.def, t.level) : 2.5f;
                rangeRing.transform.localScale = Vector3.one * r * 2f;
            }
            buildMenu.SetActive(t == null);
            towerMenu.SetActive(t != null);
            RefreshMenus();
        }

        void CloseMenus()
        {
            selectedSlot = -1;
            if (buildMenu != null) buildMenu.SetActive(false);
            if (towerMenu != null) towerMenu.SetActive(false);
            if (rangeRing != null) rangeRing.SetActive(false);
        }

        void Build(TowerKind kind)
        {
            if (selectedSlot < 0 || slotTowers[selectedSlot] != null) return;
            var def = BastionCatalog.Tower(kind);
            if (gold < def.Cost) return;
            gold -= def.Cost;
            var t = new Tower { def = def, level = 1, slot = selectedSlot };
            slotTowers[selectedSlot] = t;
            DrawTower(t);
            Sfx.Material();
            Fx.Burst(W(slots[selectedSlot]), def.Color, 12, 2.5f, 0.08f, 0f);
            SelectSlot(selectedSlot);
            RefreshHud();
        }

        void Upgrade()
        {
            if (selectedSlot < 0) return;
            var t = slotTowers[selectedSlot];
            if (t == null || t.level >= BastionCatalog.MaxTowerLevel) return;
            int cost = BastionCatalog.UpgradeCost(t.def, t.level + 1);
            if (gold < cost) return;
            gold -= cost;
            t.level++;
            DrawTower(t);
            Sfx.Milestone();
            Fx.Burst(W(slots[selectedSlot]), ApogeeTheme.Gold, 16, 3f, 0.09f, 0f);
            SelectSlot(selectedSlot);
            RefreshHud();
        }

        void Sell()
        {
            if (selectedSlot < 0) return;
            var t = slotTowers[selectedSlot];
            if (t == null) return;
            gold += Mathf.RoundToInt(BastionCatalog.Invested(t.def, t.level) * BastionCatalog.SellRefund);
            if (t.go != null) Destroy(t.go);
            slotTowers[selectedSlot] = null;
            Sfx.Drop();
            CloseMenus();
            RefreshHud();
        }

        /// <summary>A tower: a stone base, its emblem, and one gold pip per level.</summary>
        void DrawTower(Tower t)
        {
            if (t.go != null) Destroy(t.go);
            var go = new GameObject($"Tower_{t.def.Kind}");
            go.transform.SetParent(root, false);
            go.transform.position = W(slots[t.slot]);
            t.go = go;
            float s = 0.9f + 0.08f * (t.level - 1);
            Quad("Base", Vector2.zero, new Vector2(s, s), new Color(0.6f, 0.55f, 0.5f), 4, PlaceholderVisuals.RimCircle(new Color(0.55f, 0.5f, 0.46f)), go.transform)
                .transform.localPosition = Vector3.zero;
            var icon = Quad("Icon", Vector2.zero, new Vector2(0.62f, 0.62f), Color.white, 5, BastionArt.Icon(t.def.Kind), go.transform);
            icon.transform.localPosition = new Vector3(0f, 0.05f, -0.1f);
            for (int l = 0; l < t.level; l++)
                Quad("Pip", Vector2.zero, new Vector2(0.12f, 0.12f), ApogeeTheme.Gold, 6, PlaceholderVisuals.Circle(Color.white), go.transform)
                    .transform.localPosition = new Vector3(-0.15f + l * 0.15f, -0.4f, -0.1f);
        }

        // ---- buttons -------------------------------------------------------------------------

        void OnCall() => StartNextWave(early: true);

        void OnSpeed()
        {
            speed = speed == 1 ? 2 : 1;
            speedLabel.text = $"x{speed}";
        }

        // ---- end ---------------------------------------------------------------------------

        void EndMap(bool won)
        {
            playing = false;
            waveRunning = false;
            CloseMenus();
            overPanel.SetActive(true);
            int coins, materials = 0;
            string extra = "";

            if (map.Endless)
            {
                int held = Mathf.Max(0, wave - 1);
                int old = SaveSystem.BastionEndlessBest;
                bool record = held > old;
                if (record) SaveSystem.BastionEndlessBest = held;
                coins = 5 * held;
                materials = held / 4;
                // A long stand is worth a healing kit, once per new milestone of 25 waves.
                if (record && held / 25 > old / 25 && SaveSystem.TryAddReviveKit()) extra = "      1 [k]";
                overTitle.text = record ? "NOUVEAU RECORD !" : "LE BASTION EST TOMBÉ";
                overDetail.text = $"Vagues tenues : {held}   ·   record : {Mathf.Max(held, old)}";
                for (int s = 0; s < 3; s++) overStars[s].gameObject.SetActive(false);
            }
            else if (won)
            {
                int stars = lives >= 18 ? 3 : lives >= 10 ? 2 : 1;
                bool firstThree = stars == 3 && SaveSystem.GetBastionStars(mapIndex) < 3;
                SaveSystem.SetBastionStars(mapIndex, stars);
                coins = 30 + 15 * (mapIndex + 1) + 10 * stars;
                materials = stars + mapIndex;
                if (firstThree && SaveSystem.TryAddReviveKit()) extra = "      1 [k]";
                overTitle.text = "VICTOIRE !";
                overDetail.text = $"{map.Name}   ·   {lives} vies sur {BastionCatalog.StartLives}";
                for (int s = 0; s < 3; s++)
                {
                    overStars[s].gameObject.SetActive(true);
                    overStars[s].color = s < stars ? ApogeeTheme.Gold : new Color(0.35f, 0.22f, 0.16f, 0.8f);
                }
                Sfx.Milestone();
            }
            else
            {
                coins = 3 * Mathf.Max(0, wave - 1);
                overTitle.text = "LE BASTION EST TOMBÉ";
                overDetail.text = $"{map.Name}   ·   vague {wave} sur {map.Waves}";
                for (int s = 0; s < 3; s++) overStars[s].gameObject.SetActive(false);
                Sfx.Death();
            }

            if (coins > 0) SaveSystem.AddCoins(coins);
            if (materials > 0) SaveSystem.AddMaterials(materials);
            overReward.text = (coins > 0 || materials > 0 || extra.Length > 0)
                ? $"{coins} [c]" + (materials > 0 ? $"      {materials} [g]" : "") + extra
                : "Aucun gain cette fois";
            AdService.OnPlayerDeath();
        }

        // ---- HUD ---------------------------------------------------------------------------

        void RefreshHud()
        {
            waveText.text = map == null ? "" : map.Endless ? $"Vague {wave}" : $"Vague {wave}/{map.Waves}";
            livesText.text = $"Vies {lives}";
            goldText.text = $"Or {gold}";

            if (wave == 0) callLabel.text = "LANCER LA VAGUE 1";
            else if (waveRunning) callLabel.text = queue.Count > 0 ? "VAGUE EN COURS" : "ACHÈVE-LES !";
            else callLabel.text = $"VAGUE SUIVANTE  {Mathf.CeilToInt(nextWaveTimer)} s";
            callButton.interactable = !waveRunning;

            powerLabel.text = powerCooldown > 0f
                ? $"{BastionCatalog.PowerName(power)}\n{Mathf.CeilToInt(powerCooldown)} s"
                : $"{BastionCatalog.PowerName(power)}\nPRÊT";
            powerButton.interactable = powerCooldown <= 0f && playing;
            if (buildMenu.activeSelf || towerMenu.activeSelf) RefreshMenus();
        }

        void RefreshMenus()
        {
            for (int i = 0; i < 5; i++)
            {
                var def = BastionCatalog.Towers[i];
                buildCosts[i].text = $"{def.Cost} or";
                buildButtons[i].interactable = gold >= def.Cost;
            }
            if (selectedSlot < 0) return;
            var t = slotTowers[selectedSlot];
            if (t == null) return;
            towerInfo.text = $"{t.def.Name}  niv. {t.level}   ·   {t.def.Blurb}";
            if (t.level >= BastionCatalog.MaxTowerLevel)
            {
                upgradeLabel.text = "NIVEAU MAX";
                upgradeButton.interactable = false;
            }
            else
            {
                int cost = BastionCatalog.UpgradeCost(t.def, t.level + 1);
                upgradeLabel.text = $"AMÉLIORER  {cost} or";
                upgradeButton.interactable = gold >= cost;
            }
            sellLabel.text = $"VENDRE  {Mathf.RoundToInt(BastionCatalog.Invested(t.def, t.level) * BastionCatalog.SellRefund)} or";
        }
    }

    /// <summary>The towers' emblems, drawn at runtime: a bow, a cannon, a flame, a snowflake, a lightning bolt.</summary>
    public static class BastionArt
    {
        static readonly Dictionary<TowerKind, Sprite> cache = new();

        public static Sprite Icon(TowerKind kind)
        {
            if (cache.TryGetValue(kind, out var s) && s != null) return s;
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    Color acc = Color.clear;
                    for (int sy = 0; sy < 2; sy++)
                        for (int sx = 0; sx < 2; sx++)
                        {
                            float u = ((x + 0.25f + sx * 0.5f) / n) * 2f - 1f, v = ((y + 0.25f + sy * 0.5f) / n) * 2f - 1f;
                            var c = Shade(kind, u, v);
                            acc += new Color(c.r * c.a, c.g * c.a, c.b * c.a, c.a);
                        }
                    acc *= 0.25f;
                    px[y * n + x] = acc.a > 0.001f ? new Color(acc.r / acc.a, acc.g / acc.a, acc.b / acc.a, acc.a) : Color.clear;
                }
            tex.SetPixels(px);
            tex.Apply();
            s = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
            cache[kind] = s;
            return s;
        }

        static readonly Color Ink = new Color(0.18f, 0.08f, 0.06f);

        static float Seg(float px, float py, float ax, float ay, float bx, float by)
        {
            float vx = bx - ax, vy = by - ay, wx = px - ax, wy = py - ay;
            float t = Mathf.Clamp01((wx * vx + wy * vy) / (vx * vx + vy * vy));
            float dx = wx - vx * t, dy = wy - vy * t;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        static Color Shade(TowerKind kind, float u, float v)
        {
            switch (kind)
            {
                case TowerKind.Archer:
                {
                    // a bow (arc), its string and an arrow
                    float r = Mathf.Sqrt((u + 0.25f) * (u + 0.25f) + v * v);
                    if (u > -0.25f && Mathf.Abs(r - 0.62f) < 0.07f) return new Color(0.55f, 0.32f, 0.15f);
                    if (Mathf.Abs(u + 0.27f) < 0.025f && Mathf.Abs(v) < 0.6f) return new Color(0.9f, 0.88f, 0.8f);
                    if (Seg(u, v, -0.45f, 0f, 0.6f, 0f) < 0.035f) return new Color(0.8f, 0.7f, 0.5f);
                    if (u > 0.45f && u < 0.72f && Mathf.Abs(v) < (0.72f - u) * 0.6f) return new Color(0.85f, 0.85f, 0.88f);
                    return Color.clear;
                }
                case TowerKind.Cannon:
                {
                    // a barrel aimed up-right on a wheel
                    if (Seg(u, v, -0.35f, -0.15f, 0.5f, 0.38f) < 0.17f) return new Color(0.28f, 0.28f, 0.32f);
                    float w = Mathf.Sqrt((u + 0.25f) * (u + 0.25f) + (v + 0.35f) * (v + 0.35f));
                    if (w < 0.3f) return w < 0.08f ? Ink : new Color(0.5f, 0.32f, 0.18f);
                    return Color.clear;
                }
                case TowerKind.Brazier:
                {
                    // a flame: a teardrop with a bright core
                    float y = v + 0.25f;
                    float half = 0.42f * Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow((y - 0.1f) / 0.75f, 2f))) * (y < 0.1f ? 1f : Mathf.Lerp(1f, 0f, (y - 0.1f) / 0.75f) * 1.2f);
                    if (y < -0.55f || Mathf.Abs(u) > half) return Color.clear;
                    float core = Mathf.Abs(u) / Mathf.Max(0.01f, half);
                    return core < 0.4f && y < 0.3f ? new Color(1f, 0.92f, 0.5f) : new Color(1f, 0.45f, 0.12f);
                }
                case TowerKind.Frost:
                {
                    // a six-armed snowflake
                    for (int i = 0; i < 3; i++)
                    {
                        float a = i * Mathf.PI / 3f;
                        float cx = Mathf.Cos(a), cy = Mathf.Sin(a);
                        if (Seg(u, v, -cx * 0.7f, -cy * 0.7f, cx * 0.7f, cy * 0.7f) < 0.06f) return new Color(0.75f, 0.92f, 1f);
                    }
                    if (u * u + v * v < 0.03f) return Color.white;
                    return Color.clear;
                }
                default:
                {
                    // a lightning bolt
                    if (Seg(u, v, 0.25f, 0.75f, -0.2f, 0.05f) < 0.1f || Seg(u, v, -0.2f, 0.05f, 0.2f, 0.05f) < 0.1f || Seg(u, v, 0.2f, 0.05f, -0.25f, -0.75f) < 0.1f)
                        return new Color(0.95f, 0.85f, 1f);
                    return Color.clear;
                }
            }
        }
    }
}
