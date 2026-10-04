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
    /// "BASTION": the tower defense, played in landscape on a floating island at sunset.
    ///
    /// The dead come out of a rift at the island's left end and follow the road to the
    /// bastion's gate on the right. The player builds towers on the stone pads beside the
    /// road - archers, canon, brazier, frost, pylon, each upgradable twice - from a ring
    /// menu that opens around the pad, and places their own character as a hero who fights
    /// and, every half minute, unleashes that character's power (BastionCatalog.PowerOf).
    /// Twenty lives; a colossus through the gate costs five.
    ///
    /// Two ways to play: a campaign of five maps with up to three stars each, and the
    /// endless island. Rewards: coins, materials, and a healing kit for a first three-star
    /// map or a long endless run.
    ///
    /// The look is painted, not drawn by code: each map is a picture of its island (grass,
    /// road, trees, cliffs - see Resources/Bastion, made by an offline painter from the
    /// same road and pad layout as BastionCatalog), the towers, castle, rift and effects
    /// are sprites, and the scene is staged in depth - a sunset sky, distant islands and
    /// clouds drifting by, leaves falling. Things lower on screen are drawn in front
    /// (their z follows their height), so the 3D dead pass behind or before the towers.
    ///
    /// The gameplay runs on its own clock (paused with the pause menu, sped up by x2) and
    /// is purely positional: the dead advance by distance along the road, towers aim at
    /// the one furthest along within reach.
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
        /// <summary>The painted map covers x -12..12, y -7..5 (2048 x 1024 px).</summary>
        const float MapPpu = 2048f / 24f;
        static readonly Vector2 MapCentre = new Vector2(0f, -1f);
        const float IslandHalfW = 8.75f, IslandHalfH = 4.8f;
        /// <summary>What the camera keeps in view: the island and the top of its cliff.</summary>
        const float ViewTop = 5.2f, ViewBottom = -6.0f, ViewHalfW = 9.3f;
        const float HudTop = 0.88f;
        const float BetweenWaves = 12f;
        const float HeroRange = 1.9f, HeroInterval = 0.7f, HeroSpeed = 3f;
        const float TowerWidth = 1.7f;

        // ---- world state ------------------------------------------------------------------

        class Creep
        {
            public CreepDef def;
            public float hp, maxHp, dist, slowUntil, slowFactor = 1f, stunUntil;
            public Vector2 pos;
            public GameObject go;
            public Transform hpFill;
            public GameObject hpBar;
            public Renderer[] model;
            public float bob, flashUntil;
        }

        class Tower
        {
            public TowerDef def;
            public int level, slot;
            public float cooldown;
            public GameObject go;
            public SpriteRenderer body;
            public Transform barrel;      // canon
            public SpriteRenderer flame;  // brazier
            public SpriteRenderer glow;   // frost, pylon
            public float anim;
        }

        class Zone
        {
            public Vector2 at;
            public float radius, dps, until, tick;
            public GameObject go;
        }

        /// <summary>A projectile in flight; its effect lands when it arrives.</summary>
        class Shot
        {
            public Transform t;
            public Vector3 from, to;
            public float time, duration, arc;
            public bool rotate;
            public Action onArrive;
        }

        class Bolt
        {
            public LineRenderer line;
            public float until;
        }

        Transform root;
        BastionMap map;
        int mapIndex;               // -1 for the endless map
        Vector2[] path;
        float pathLength;
        readonly List<Vector2> slots = new();
        Tower[] slotTowers = new Tower[0];
        readonly List<Creep> creeps = new();
        readonly List<Zone> zones = new();
        readonly List<Shot> shots = new();
        readonly List<Bolt> bolts = new();
        readonly List<(Transform t, float speed, float y0, float phase)> drifters = new();
        Transform rift;
        Texture2D mapTexture;

        bool playing;
        float clock;
        int gold, lives, wave, speed = 1;
        bool waveRunning;
        readonly List<CreepKind> queue = new();
        float spawnTimer, nextWaveTimer, healthScale;

        // the hero
        Transform hero;
        SpriteRenderer heroSr;
        GameObject heroRing;
        Vector2 heroPos, heroTarget;
        float heroCooldown, powerCooldown;
        bool heroSelected;
        BastionCatalog.HeroPower power;

        int selectedSlot = -1;
        GameObject rangeRing, padRing;
        bool pressActive, pressOverUi;

        // ---- art ----------------------------------------------------------------------------

        static readonly Dictionary<string, Sprite> art = new();

        /// <summary>A painted sprite from Resources/Bastion, cut with its own pivot and scale.</summary>
        static Sprite Art(string name, float pivotY = 0.5f, float pixelsPerUnit = 0f)
        {
            string key = $"{name}|{pivotY}|{pixelsPerUnit}";
            if (art.TryGetValue(key, out var s) && s != null) return s;
            var tex = Resources.Load<Texture2D>($"Bastion/{name}");
            if (tex == null) return PlaceholderVisuals.Circle(Color.white);
            float ppu = pixelsPerUnit > 0f ? pixelsPerUnit : tex.width;
            s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, pivotY), ppu);
            art[key] = s;
            return s;
        }

        static string TowerArt(TowerKind k, int level) => $"tower_{k.ToString().ToLowerInvariant()}_{Mathf.Clamp(level, 1, 3)}";

        /// <summary>Lower on screen is nearer: z (and the sorting order) follow the height on the island.</summary>
        static float DepthZ(float y) => y * 0.45f;
        static int DepthOrder(float y) => 200 - Mathf.RoundToInt(y * 10f);

        // ---- UI -------------------------------------------------------------------------

        GameObject selectPanel, hudRoot, ringMenu, overPanel, rotatePanel;
        RectTransform ringRoot, rotatePhone;
        Text waveText, livesText, goldText, messageText, ringInfo, overTitle, overDetail, callLabel, speedLabel, powerName, powerTimer;
        IconText overReward;
        Button callButton, speedButton, powerButton;
        Image powerFill, powerPortrait;
        readonly List<(Button button, Image icon, Text cost)> ringButtons = new();
        readonly List<(Button card, Text stars, RawImage thumb)> mapCards = new();
        Text endlessBestText;
        readonly Image[] overStars = new Image[3];
        bool towerRing;   // the ring shows a tower's actions rather than the five towers

        protected override void BuildUi()
        {
            var rt = UiKit.CreateRect("BastionPanel", ui.Canvas.transform, Vector2.zero, Vector2.one);
            panel = rt.gameObject;

            BuildHud(rt);
            BuildOver(rt);
            BuildSelect(rt);
            BuildRotatePrompt(rt);
        }

        static Button RoundButton(string name, Transform parent, Vector2 anchor, Vector2 size, Vector2 pos, UnityEngine.Events.UnityAction onClick, Color tint)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = r.anchorMax = anchor;
            r.pivot = new Vector2(0.5f, 0.5f);
            r.sizeDelta = size;
            r.anchoredPosition = pos;
            var img = go.AddComponent<Image>();
            img.sprite = ApogeeTheme.Round;
            img.color = tint;
            var b = go.AddComponent<Button>();
            b.targetGraphic = img;
            var colors = b.colors;
            colors.disabledColor = new Color(0.55f, 0.5f, 0.5f, 0.7f);
            b.colors = colors;
            b.onClick.AddListener(onClick);
            return b;
        }

        void BuildHud(RectTransform rt)
        {
            var hud = UiKit.CreateRect("Hud", rt, Vector2.zero, Vector2.one);
            hudRoot = hud.gameObject;

            // Top-left: lives, gold and the wave, on one parchment strip.
            var strip = UiKit.CreateRect("Stats", hud, new Vector2(0.012f, 0.9f), new Vector2(0.5f, 0.985f));
            var sImg = strip.gameObject.AddComponent<Image>();
            sImg.sprite = ApogeeTheme.Chip;
            sImg.type = Image.Type.Sliced;
            UiKit.CreateImage("Heart", strip, new Vector2(0.02f, 0.12f), new Vector2(0.1f, 0.88f), Art("heart"), Color.white);
            livesText = UiKit.Outlined(UiKit.CreateText("Lives", strip, "", 34, TextAnchor.MiddleLeft, new Vector2(0.11f, 0f), new Vector2(0.27f, 1f), ApogeeTheme.Cream));
            UiKit.CreateImage("Coin", strip, new Vector2(0.28f, 0.12f), new Vector2(0.36f, 0.88f), GameIcons.Coin, Color.white);
            goldText = UiKit.Outlined(UiKit.CreateText("Gold", strip, "", 34, TextAnchor.MiddleLeft, new Vector2(0.37f, 0f), new Vector2(0.56f, 1f), PlaceholderVisuals.CoinColor));
            UiKit.CreateImage("Skull", strip, new Vector2(0.57f, 0.12f), new Vector2(0.65f, 0.88f), Art("skull"), Color.white);
            waveText = UiKit.Outlined(UiKit.CreateText("Wave", strip, "", 32, TextAnchor.MiddleLeft, new Vector2(0.66f, 0f), new Vector2(0.98f, 1f), ApogeeTheme.Gold));
            UiKit.FitLabel(waveText, 32);

            ui.CreatePauseButton(hud, new Vector2(0.88f, 0.905f), new Vector2(0.988f, 0.982f), () => { if (playing) OpenPause(); });
            messageText = UiKit.Outlined(UiKit.CreateText("Message", hud, "", 28, TextAnchor.MiddleCenter, new Vector2(0.25f, 0.83f), new Vector2(0.75f, 0.89f), ApogeeTheme.Cream), 2f);

            // Bottom-right: the hero's power (big), x2, and the next wave.
            powerButton = RoundButton("Power", hud, new Vector2(1f, 0f), new Vector2(190, 190), new Vector2(-120, 120), OnPower, Color.white);
            powerPortrait = UiKit.CreateImage("Portrait", powerButton.transform, new Vector2(0.2f, 0.25f), new Vector2(0.8f, 0.85f), null, Color.white);
            powerPortrait.raycastTarget = false;
            powerFill = UiKit.CreateImage("Cooldown", powerButton.transform, new Vector2(0.06f, 0.06f), new Vector2(0.94f, 0.94f), ApogeeTheme.Round, new Color(0.05f, 0.02f, 0.02f, 0.62f), false);
            powerFill.type = Image.Type.Filled;
            powerFill.fillMethod = Image.FillMethod.Radial360;
            powerFill.raycastTarget = false;
            powerTimer = UiKit.Outlined(UiKit.CreateText("Timer", powerButton.transform, "", 44, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, ApogeeTheme.Cream), 2f);
            powerName = UiKit.Outlined(UiKit.CreateText("PowerName", hud, "", 22, TextAnchor.MiddleCenter, new Vector2(0.82f, 0.005f), new Vector2(0.995f, 0.05f), ApogeeTheme.Gold));

            speedButton = RoundButton("Speed", hud, new Vector2(1f, 0f), new Vector2(110, 110), new Vector2(-290, 80), OnSpeed, new Color(0.85f, 0.75f, 0.7f));
            speedLabel = UiKit.Outlined(UiKit.CreateText("x", speedButton.transform, "x1", 36, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, ApogeeTheme.Cream), 2f);

            callButton = RoundButton("Call", hud, new Vector2(1f, 0f), new Vector2(140, 140), new Vector2(-290, 230), OnCall, new Color(1f, 0.85f, 0.75f));
            UiKit.CreateImage("Skull", callButton.transform, new Vector2(0.22f, 0.3f), new Vector2(0.78f, 0.86f), Art("skull"), Color.white).raycastTarget = false;
            callLabel = UiKit.Outlined(UiKit.CreateText("CallLabel", hud, "", 22, TextAnchor.MiddleCenter, new Vector2(0.72f, 0.35f), new Vector2(0.98f, 0.40f), ApogeeTheme.Cream));
            var clr = (RectTransform)callLabel.transform;
            clr.anchorMin = clr.anchorMax = new Vector2(1f, 0f);
            clr.sizeDelta = new Vector2(320, 40);
            clr.anchoredPosition = new Vector2(-290, 330);

            // The ring menu that opens around a pad: five towers, or a tower's actions.
            var ring = UiKit.CreateRect("Ring", hud, Vector2.zero, Vector2.one);
            ringMenu = ring.gameObject;
            ringRoot = UiKit.CreateRect("RingRoot", ring, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            ringRoot.sizeDelta = Vector2.zero;
            for (int i = 0; i < 5; i++)
            {
                int captured = i;
                var b = RoundButton($"RingButton_{i}", ringRoot, new Vector2(0.5f, 0.5f), new Vector2(124, 124), Vector2.zero, () => OnRingButton(captured), new Color(0.95f, 0.85f, 0.75f));
                var icon = UiKit.CreateImage("Icon", b.transform, new Vector2(0.1f, 0.12f), new Vector2(0.9f, 0.92f), null, Color.white);
                icon.raycastTarget = false;
                var cost = UiKit.Outlined(UiKit.CreateText("Cost", b.transform, "", 26, TextAnchor.MiddleCenter, new Vector2(-0.3f, -0.42f), new Vector2(1.3f, -0.02f), PlaceholderVisuals.CoinColor), 2f);
                ringButtons.Add((b, icon, cost));
            }
            ringInfo = UiKit.Outlined(UiKit.CreateText("RingInfo", ringRoot, "", 26, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), ApogeeTheme.Cream), 2f);
            var rir = (RectTransform)ringInfo.transform;
            rir.sizeDelta = new Vector2(560, 46);
            ringMenu.SetActive(false);
        }

        void BuildOver(RectTransform rt)
        {
            var overRt = UiKit.CreatePanel("BastionOver", rt, UiKit.Overlay);
            overPanel = overRt.gameObject;
            UiKit.CreateFrame("OverFrame", overRt, new Vector2(0.25f, 0.12f), new Vector2(0.75f, 0.88f));
            overTitle = UiKit.Outlined(UiKit.CreateText("OverTitle", overRt, "", 64, TextAnchor.MiddleCenter, new Vector2(0.27f, 0.72f), new Vector2(0.73f, 0.84f), ApogeeTheme.Gold), 3f);
            UiKit.FitLabel(overTitle, 64);
            for (int s = 0; s < 3; s++)
            {
                float x = 0.41f + s * 0.06f;
                overStars[s] = UiKit.CreateImage($"Star_{s}", overRt, new Vector2(x, 0.6f), new Vector2(x + 0.06f, 0.71f), PlaceholderVisuals.Star(), ApogeeTheme.Gold);
            }
            overDetail = UiKit.CreateText("OverDetail", overRt, "", 30, TextAnchor.MiddleCenter, new Vector2(0.27f, 0.52f), new Vector2(0.73f, 0.6f), ApogeeTheme.Cream);
            UiKit.FitLabel(overDetail, 30);
            overReward = IconText.Create("OverReward", overRt, "", 36, TextAnchor.MiddleCenter, new Vector2(0.27f, 0.43f), new Vector2(0.73f, 0.52f), PlaceholderVisuals.CoinColor);
            UiKit.CreateButton("Again", overRt, "REJOUER", new Vector2(0.3f, 0.31f), new Vector2(0.7f, 0.4f), () => StartMap(mapIndex), 32);
            UiKit.CreateButton("Maps", overRt, "CARTES", new Vector2(0.3f, 0.21f), new Vector2(0.49f, 0.29f), ShowSelect, 26);
            UiKit.CreateButton("Menu", overRt, "MENU", new Vector2(0.51f, 0.21f), new Vector2(0.7f, 0.29f), ReturnToHub, 26);
            overPanel.SetActive(false);
        }

        void BuildSelect(RectTransform rt)
        {
            var sel = UiKit.CreatePanel("BastionSelect", rt, ApogeeTheme.Maroon);
            selectPanel = sel.gameObject;
            UiKit.CreateArtBackdrop("Backdrop", sel, "menu_bg", 0.5f, 0.5f).transform.SetAsFirstSibling();
            UiKit.CreateImage("Shade", sel, Vector2.zero, Vector2.one, PlaceholderVisuals.Square(Color.white), new Color(0.1f, 0.03f, 0.03f, 0.55f), false).raycastTarget = false;
            UiKit.Outlined(UiKit.CreateText("SelectTitle", sel, "BASTION", 76, TextAnchor.MiddleCenter, new Vector2(0.2f, 0.85f), new Vector2(0.8f, 0.97f), ApogeeTheme.Gold), 3f);
            UiKit.CreateText("SelectHint", sel, "Choisis une île à défendre", 28, TextAnchor.MiddleCenter, new Vector2(0.2f, 0.8f), new Vector2(0.8f, 0.85f), ApogeeTheme.Cream);

            int n = BastionCatalog.Campaign.Length + 1;
            float w = 0.94f / n;
            for (int i = 0; i < n; i++)
            {
                int captured = i < n - 1 ? i : -1;
                bool endless = captured < 0;
                var m = endless ? BastionCatalog.Endless : BastionCatalog.Campaign[i];
                float x0 = 0.03f + i * w + 0.006f, x1 = 0.03f + (i + 1) * w - 0.006f;
                var card = UiKit.CreateButton($"Map_{i}", sel, "", new Vector2(x0, 0.2f), new Vector2(x1, 0.77f), () => StartMap(captured), 24,
                    endless ? new Color(0.45f, 0.18f, 0.12f) : UiKit.CardColor);
                // A look at the island itself.
                var thumbHolder = UiKit.CreateRect("Thumb", card.transform, new Vector2(0.05f, 0.42f), new Vector2(0.95f, 0.95f));
                var thumb = thumbHolder.gameObject.AddComponent<RawImage>();
                thumb.texture = Resources.Load<Texture2D>(endless ? "Bastion/map_endless" : $"Bastion/map_{i}");
                thumb.uvRect = new Rect(0.12f, 0.1f, 0.76f, 0.85f);
                thumb.raycastTarget = false;
                var name = UiKit.Outlined(UiKit.CreateText("Name", card.transform, endless ? "INFINI" : $"{i + 1}. {m.Name}", 26, TextAnchor.MiddleCenter, new Vector2(0.04f, 0.27f), new Vector2(0.96f, 0.41f), ApogeeTheme.Gold), 1.5f);
                UiKit.FitLabel(name, 26);
                var sub = UiKit.CreateText("Sub", card.transform, endless ? m.Name : $"{m.Waves} vagues", 20, TextAnchor.MiddleCenter, new Vector2(0.04f, 0.15f), new Vector2(0.96f, 0.27f), UiKit.TextDim);
                UiKit.FitLabel(sub, 20);
                var stars = UiKit.CreateText("Stars", card.transform, "", 24, TextAnchor.MiddleCenter, new Vector2(0.04f, 0.02f), new Vector2(0.96f, 0.15f), ApogeeTheme.Gold);
                UiKit.FitLabel(stars, 24);
                if (endless) endlessBestText = stars;
                else mapCards.Add((card, stars, thumb));
            }
            UiKit.CreateButton("SelectBack", sel, "RETOUR", new Vector2(0.4f, 0.04f), new Vector2(0.6f, 0.13f), ReturnToHub, 30);
        }

        void BuildRotatePrompt(RectTransform rt)
        {
            var r = UiKit.CreatePanel("RotatePrompt", rt, new Color(0.12f, 0.03f, 0.03f, 0.96f));
            rotatePanel = r.gameObject;
            var phone = UiKit.CreateImage("Phone", r, new Vector2(0.5f, 0.6f), new Vector2(0.5f, 0.6f), ApogeeTheme.Button, ApogeeTheme.Gold, false);
            phone.type = Image.Type.Sliced;
            rotatePhone = phone.rectTransform;
            rotatePhone.sizeDelta = new Vector2(110f, 190f);
            UiKit.CreateImage("Screen", rotatePhone, new Vector2(0.14f, 0.1f), new Vector2(0.86f, 0.88f), PlaceholderVisuals.Square(Color.white), ApogeeTheme.CrimsonDark, false);
            UiKit.Outlined(UiKit.CreateText("Text", r, "Tourne ton téléphone\nBastion se joue en paysage", 40, TextAnchor.MiddleCenter,
                new Vector2(0.08f, 0.34f), new Vector2(0.92f, 0.5f), ApogeeTheme.Cream), 2f);
            UiKit.CreateButton("Back", r, "MENU", new Vector2(0.3f, 0.18f), new Vector2(0.7f, 0.26f), ReturnToHub, 28);
            rotatePanel.SetActive(false);
        }

        // ---- orientation -----------------------------------------------------------------

        ScreenOrientation savedOrientation;
        bool orientationLocked;

        void LockLandscape()
        {
            if (!Application.isMobilePlatform || Application.platform == RuntimePlatform.WebGLPlayer) return;
            savedOrientation = Screen.orientation;
            orientationLocked = true;
            Screen.orientation = ScreenOrientation.LandscapeLeft;
        }

        void RestoreOrientation()
        {
            if (!orientationLocked) return;
            orientationLocked = false;
            Screen.orientation = savedOrientation == ScreenOrientation.AutoRotation || savedOrientation == ScreenOrientation.Portrait
                ? savedOrientation : ScreenOrientation.Portrait;
        }

        /// <summary>A phone held upright gets a prompt to turn it; the game waits meanwhile.</summary>
        bool WaitingForLandscape()
        {
            bool upright = Screen.height > Screen.width;
            if (rotatePanel.activeSelf != upright) rotatePanel.SetActive(upright);
            if (!upright) return false;
            rotatePanel.transform.SetAsLastSibling();
            float t = Mathf.SmoothStep(0f, 1f, Mathf.PingPong(Time.unscaledTime * 0.8f, 1.3f) - 0.15f);
            rotatePhone.localRotation = Quaternion.Euler(0f, 0f, -90f * t);
            return true;
        }

        // ---- lifecycle --------------------------------------------------------------------

        protected override void OnEnter()
        {
            var runnerPlayer = ui.Director != null ? ui.Director.Player : null;
            if (runnerPlayer != null) runnerPlayer.controlEnabled = false;
            MobileInput.Reset();
            LockLandscape();
            ShowSelect();
        }

        protected override void OnExit()
        {
            playing = false;
            ClearWorld();
            ReleaseCamera();
            RestoreOrientation();
            rotatePanel.SetActive(false);
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
                mapCards[i].thumb.color = open ? Color.white : new Color(0.35f, 0.3f, 0.3f);
                mapCards[i].stars.text = open ? $"{stars} / 3 étoiles" : "VERROUILLÉ";
            }
            int best = SaveSystem.BastionEndlessBest;
            endlessBestText.text = best > 0 ? $"Record : vague {best}" : "Aucun record";
        }

        void ClearWorld()
        {
            foreach (var c in creeps) if (c.go != null) Destroy(c.go);
            creeps.Clear();
            zones.Clear();
            shots.Clear();
            bolts.Clear();
            drifters.Clear();
            if (root != null) Destroy(root.gameObject);
            root = null;
            hero = null;
            rangeRing = null;
            padRing = null;
            rift = null;
        }

        void StartMap(int index)
        {
            ClearWorld();
            mapIndex = index;
            map = index < 0 ? BastionCatalog.Endless : BastionCatalog.Campaign[Mathf.Clamp(index, 0, BastionCatalog.Campaign.Length - 1)];
            // The catalog's roads run top to bottom (portrait); the island is played lying on its side.
            path = new Vector2[map.Path.Length];
            for (int i = 0; i < path.Length; i++) path[i] = Turn(map.Path[i]);
            pathLength = BastionCatalog.PathLength(path);
            slots.Clear();
            foreach (var s in BastionCatalog.Slots(map.Path)) slots.Add(Turn(s));
            slotTowers = new Tower[slots.Count];

            gold = map.StartGold;
            lives = BastionCatalog.StartLives;
            wave = 0;
            clock = 0f;
            speed = 1;
            speedLabel.text = "x1";
            waveRunning = false;
            queue.Clear();
            nextWaveTimer = 0f;
            selectedSlot = -1;
            heroSelected = false;
            power = BastionCatalog.PowerOf(SaveSystem.SelectedSkinId);
            powerCooldown = 8f;

            BuildWorld();
            TakeOverCamera(new Vector3(Origin.x, Origin.y, -10f), 7f, new Color(0.95f, 0.58f, 0.42f));
            FrameCamera();

            selectPanel.SetActive(false);
            overPanel.SetActive(false);
            hudRoot.SetActive(true);
            CloseMenus();
            playing = true;
            messageText.text = "Touche un emplacement de pierre pour bâtir une tour";
            ui.ShowBanner(map.Name, map.Subtitle, 2.2f);
            RefreshHud();
        }

        /// <summary>Portrait catalog coordinates to the landscape island: a quarter turn.</summary>
        static Vector2 Turn(Vector2 p) => new Vector2(-p.y, p.x);

        void FrameCamera()
        {
            if (cam == null) return;
            float aspect = Mathf.Max(0.1f, cam.aspect);
            float needH = (ViewTop - ViewBottom) / HudTop;
            float ortho = Mathf.Max(needH / 2f, ViewHalfW / aspect);
            float visible = ortho * 2f;
            // The view's top edge sits a touch above the island, under the HUD strip.
            float topY = ViewTop + visible * (1f - HudTop);
            cam.orthographicSize = ortho;
            cam.transform.position = new Vector3(Origin.x, Origin.y + topY - ortho, -10f);
        }

        // ---- world ------------------------------------------------------------------------

        static Vector3 W(Vector2 p, float z = 0f) => new Vector3(Origin.x + p.x, Origin.y + p.y, z);

        SpriteRenderer Place(string name, Sprite sprite, Vector2 at, float scale, int order, float z, Transform parent = null, Color? color = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent != null ? parent : root, false);
            if (parent == null) go.transform.position = W(at, z);
            else go.transform.localPosition = new Vector3(at.x, at.y, z);
            go.transform.localScale = Vector3.one * scale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color ?? Color.white;
            sr.sortingOrder = order;
            return sr;
        }

        void BuildWorld()
        {
            root = new GameObject("BastionWorld").transform;
            BuildSky();

            // The island, painted.
            mapTexture = Resources.Load<Texture2D>(mapIndex < 0 ? "Bastion/map_endless" : $"Bastion/map_{mapIndex}");
            if (mapTexture != null)
            {
                var island = Sprite.Create(mapTexture, new Rect(0, 0, mapTexture.width, mapTexture.height), new Vector2(0.5f, 0.5f), MapPpu);
                Place("Island", island, MapCentre, 1f, -50, 6f);
            }

            // Build pads.
            for (int i = 0; i < slots.Count; i++)
                Place($"Pad_{i}", Art("pad"), slots[i] + new Vector2(0f, -0.06f), 1.15f, -40, 5.5f);
            padRing = Place("PadRing", Art("ring"), Vector2.zero, 1.15f, -39, 5.4f).gameObject;
            padRing.SetActive(false);
            rangeRing = Place("Range", PlaceholderVisuals.Circle(Color.white), Vector2.zero, 1f, -38, 5.3f, null, new Color(1f, 0.92f, 0.65f, 0.16f)).gameObject;
            rangeRing.SetActive(false);

            // The rift the dead pour out of, and the bastion at the end of the road.
            var start = path[0];
            rift = Place("Rift", Art("rift"), start + new Vector2(-0.35f, 0f), 2.3f, DepthOrder(start.y) - 1, DepthZ(start.y) + 0.3f).transform;
            Place("RiftGlow", Art("glow"), start + new Vector2(-0.35f, 0f), 3.2f, DepthOrder(start.y) - 2, DepthZ(start.y) + 0.35f, null, new Color(0.7f, 0.3f, 0.9f, 0.45f));
            var end = path[path.Length - 1];
            var castleSprite = Art("castle", 0.06f, 512f / 3.3f);
            Place("Castle", castleSprite, end + new Vector2(0.15f, -0.75f), 1f, DepthOrder(end.y - 0.75f), DepthZ(end.y - 0.75f));

            BuildHero();
            BuildLeaves();
        }

        /// <summary>
        /// The stage behind the island: a sunset gradient, distant islands floating at
        /// different depths, clouds drifting across (some below the island, which floats).
        /// </summary>
        void BuildSky()
        {
            var tex = new Texture2D(1, 256, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var top = new Color(0.86f, 0.36f, 0.34f);
            var mid = new Color(1f, 0.66f, 0.42f);
            var low = new Color(1f, 0.82f, 0.56f);
            for (int y = 0; y < 256; y++)
            {
                float t = y / 255f;
                tex.SetPixel(0, y, t > 0.5f ? Color.Lerp(mid, top, (t - 0.5f) * 2f) : Color.Lerp(low, mid, t * 2f));
            }
            tex.Apply();
            var sky = Sprite.Create(tex, new Rect(0, 0, 1, 256), new Vector2(0.5f, 0.5f), 1f);
            var skySr = Place("Sky", sky, new Vector2(0f, -0.5f), 1f, -100, 30f);
            skySr.transform.localScale = new Vector3(80f, 0.09f, 1f);
            // A low sun.
            Place("Sun", Art("glow"), new Vector2(-4f, 1.5f), 12f, -99, 29f, null, new Color(1f, 0.92f, 0.7f, 0.55f));

            for (int i = 0; i < 6; i++)
            {
                float x = -16f + i * 6.5f + Random.Range(-1.5f, 1.5f);
                float y = Random.Range(-3f, 4.5f);
                float s = Random.Range(1.4f, 2.6f);
                var sr = Place($"FarIsland_{i}", ApogeeTheme.Island(i), new Vector2(x, y), s, -95 + i, 25f, null, new Color(0.95f, 0.62f, 0.55f, 0.55f));
                drifters.Add((sr.transform, Random.Range(0.05f, 0.12f), Origin.y + y, Random.Range(0f, 6f)));
            }
            for (int i = 0; i < 8; i++)
            {
                bool under = i % 2 == 0;
                float y = under ? Random.Range(-7.5f, -5f) : Random.Range(2.5f, 6f);
                var sr = Place($"Cloud_{i}", Art("cloud"), new Vector2(Random.Range(-18f, 18f), y), Random.Range(3f, 5f), under ? -45 : -90, under ? 7f : 20f, null,
                    new Color(1f, 1f, 1f, under ? 0.75f : 0.55f));
                drifters.Add((sr.transform, Random.Range(0.25f, 0.5f), Origin.y + y, -1f));
            }
        }

        /// <summary>Crimson leaves drifting across the island, as on the home screen's art.</summary>
        void BuildLeaves()
        {
            var go = new GameObject("Leaves");
            go.transform.SetParent(root, false);
            go.transform.position = W(new Vector2(0f, 7f), -3f);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.startLifetime = 9f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.24f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.9f, 0.25f, 0.12f), new Color(1f, 0.6f, 0.22f));
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 0.03f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 60;
            main.prewarm = true;
            var emission = ps.emission;
            emission.rateOverTime = 5f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(26f, 0.5f, 0.1f);
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0.3f, 0.8f);
            vel.y = new ParticleSystem.MinMaxCurve(-1.4f, -0.8f);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-2f, 2f);
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = new Material(Shader.Find("Sprites/Default")) { mainTexture = ApogeeTheme.LeafTexture };
            renderer.sortingOrder = 400;
        }

        void BuildHero()
        {
            var skin = SkinCatalog.Find(SaveSystem.SelectedSkinId);
            var go = new GameObject("Hero");
            go.transform.SetParent(root, false);
            hero = go.transform;
            Place("Shadow", Art("shadow"), new Vector2(0f, -0.42f), 0.75f, 0, 0.05f, hero);
            heroRing = Place("HeroRing", Art("ring"), new Vector2(0f, -0.42f), 0.9f, 1, 0.04f, hero).gameObject;
            heroRing.SetActive(false);
            var artGo = new GameObject("Art");
            artGo.transform.SetParent(hero, false);
            heroSr = artGo.AddComponent<SpriteRenderer>();
            var portrait = SkinCatalog.LoadPortrait(skin, out bool custom);
            heroSr.sprite = custom ? portrait : ui.PlayerSprite;
            heroSr.color = custom ? Color.white : skin.Tint;
            if (heroSr.sprite != null)
            {
                var b = heroSr.sprite.bounds;
                float scale = 1.0f / Mathf.Max(0.01f, Mathf.Max(b.size.x, b.size.y));
                artGo.transform.localScale = Vector3.one * scale;
                artGo.transform.localPosition = -b.center * scale;
            }
            powerPortrait.sprite = heroSr.sprite;
            powerPortrait.color = heroSr.color;
            powerName.text = BastionCatalog.PowerName(power);

            // The hero starts guarding the road a little before the gate.
            float d = Mathf.Max(0f, pathLength - 3.5f);
            var onRoad = BastionCatalog.PointAt(path, d);
            heroPos = heroTarget = onRoad + new Vector2(0f, onRoad.y > 0f ? -1.0f : 1.0f);
            PlaceHero();
        }

        void PlaceHero()
        {
            float bob = (heroPos - heroTarget).sqrMagnitude > 0.001f ? Mathf.Abs(Mathf.Sin(Time.time * 12f)) * 0.05f : 0f;
            hero.position = W(heroPos + new Vector2(0f, bob), DepthZ(heroPos.y) - 0.1f);
            heroSr.sortingOrder = DepthOrder(heroPos.y);
        }

        // ---- frame ------------------------------------------------------------------------

        void Update()
        {
            if (!IsActive) return;
            if (WaitingForLandscape()) return;
            if (root == null) return;
            FrameCamera();
            float realDt = Time.deltaTime;
            AnimateScenery(realDt);
            UpdateShots(realDt * speed);
            UpdateBolts();
            if (selectedSlot >= 0) PlaceRing();
            if (!playing) return;

            HandleInput();
            float dt = realDt * speed;
            if (dt <= 0f) return;
            clock += dt;

            UpdateWaves(dt);
            UpdateCreeps(dt);
            if (!playing) return;
            UpdateTowers(dt);
            UpdateZones(dt);
            UpdateHero(dt);
            powerCooldown = Mathf.Max(0f, powerCooldown - dt);
            RefreshHud();
        }

        void AnimateScenery(float dt)
        {
            for (int i = 0; i < drifters.Count; i++)
            {
                var (t, sp, y0, phase) = drifters[i];
                if (t == null) continue;
                var p = t.position;
                p.x += sp * dt;
                if (p.x > Origin.x + 22f) p.x = Origin.x - 22f;
                if (phase >= 0f) p.y = y0 + Mathf.Sin(Time.time * 0.4f + phase) * 0.15f;
                t.position = p;
            }
            if (rift != null)
            {
                rift.Rotate(0f, 0f, -40f * dt);
                rift.localScale = Vector3.one * (2.3f + Mathf.Sin(Time.time * 2.5f) * 0.08f);
            }
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
                Fx.Text(W(path[0], -3f) + Vector3.up * 0.8f, $"+{bonus} OR", PlaceholderVisuals.CoinColor, 1.1f);
            }
            wave++;
            waveRunning = true;
            queue.Clear();
            queue.AddRange(BastionCatalog.WaveCreeps(wave));
            healthScale = BastionCatalog.HealthScale(wave, map.Toughness);
            spawnTimer = 0.3f;
            bool boss = wave % 10 == 0;
            ui.ShowBanner(boss ? "UN COLOSSE !" : $"VAGUE {wave}", boss ? "Il vaut cinq vies : arrête-le" : $"{queue.Count} morts sortent de la faille", 1.6f);
            messageText.text = "";
            if (rift != null) Fx.Burst(rift.position, new Color(0.8f, 0.4f, 1f), 30, 4f, 0.12f, 0f);
        }

        void WaveCleared()
        {
            waveRunning = false;
            int bonus = BastionCatalog.WaveBonus(wave);
            gold += bonus;
            Sfx.Milestone();
            if (!map.Endless && wave >= map.Waves) { EndMap(true); return; }
            nextWaveTimer = BetweenWaves;
            messageText.text = $"Vague {wave} repoussée   ·   +{bonus} or";
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
            float lift = def.Flies ? 0.55f : 0f;
            Transform rig = null;
            if (KenneyProps.Available)
            {
                var size = KenneyProps.Size(PropKit.Graveyard, def.Model);
                rig = KenneyProps.Spawn(PropKit.Graveyard, def.Model, go.transform, new Vector3(0f, -0.25f + lift, 0f),
                    h / Mathf.Max(0.01f, size.y), PropLayer.Character, 0f, -10f);
                if (rig != null) c.model = rig.GetComponentsInChildren<Renderer>();
            }
            if (rig == null)
            {
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = PlaceholderVisuals.Zombie();
                sr.sortingOrder = 150;
                go.transform.localScale = Vector3.one * h;
            }
            Place("Shadow", Art("shadow"), new Vector2(0f, -0.26f), def.Kind == CreepKind.Colossus ? 1.4f : 0.7f, 0, 0.6f, go.transform,
                new Color(1f, 1f, 1f, def.Flies ? 0.5f : 1f));
            if (def.Armored)
                Place("Armor", GameIcons.PowerUp(PowerUpKind.Shield), new Vector2(0.32f, h * 0.75f), 0.32f, 300, -0.4f, go.transform);

            // Health bar, shown once hurt.
            c.hpBar = new GameObject("HpBar");
            c.hpBar.transform.SetParent(go.transform, false);
            c.hpBar.transform.localPosition = new Vector3(0f, h + lift + 0.05f, -0.5f);
            var bg = Place("Bg", PlaceholderVisuals.Square(Color.white), Vector2.zero, 1f, 310, 0f, c.hpBar.transform, new Color(0.12f, 0.04f, 0.03f, 0.9f));
            bg.transform.localScale = new Vector3(0.74f, 0.12f, 1f);
            var fill = Place("Fill", PlaceholderVisuals.Square(Color.white), Vector2.zero, 1f, 311, -0.01f, c.hpBar.transform, new Color(0.95f, 0.3f, 0.2f));
            fill.transform.localScale = new Vector3(0.68f, 0.07f, 1f);
            c.hpFill = fill.transform;
            c.hpBar.SetActive(false);

            c.pos = path[0];
            go.transform.position = W(c.pos, DepthZ(c.pos.y));
            creeps.Add(c);
        }

        void UpdateCreeps(float dt)
        {
            for (int i = creeps.Count - 1; i >= 0; i--)
            {
                var c = creeps[i];
                float speedNow = c.def.Speed * (clock < c.slowUntil ? c.slowFactor : 1f) * (clock < c.stunUntil ? 0f : 1f);
                c.dist += speedNow * dt;
                if (c.dist >= pathLength)
                {
                    // Through the gate.
                    lives -= c.def.Lives;
                    Fx.Burst(W(path[path.Length - 1], -3f), new Color(0.9f, 0.2f, 0.15f), 18, 3f, 0.1f);
                    Fx.Shake(0.25f, 0.25f);
                    Sfx.Hit();
                    Destroy(c.go);
                    creeps.RemoveAt(i);
                    if (lives <= 0) { lives = 0; EndMap(false); return; }
                    continue;
                }
                var before = c.pos;
                c.pos = BastionCatalog.PointAt(path, c.dist);
                c.bob += dt * 7f;
                c.go.transform.position = W(c.pos + new Vector2(0f, Mathf.Abs(Mathf.Sin(c.bob)) * 0.04f), DepthZ(c.pos.y));
                // Face the way it walks.
                if (Mathf.Abs(c.pos.x - before.x) > 0.0001f)
                {
                    var s = c.go.transform.localScale;
                    s.x = Mathf.Abs(s.x) * (c.pos.x < before.x ? -1f : 1f);
                    c.go.transform.localScale = s;
                }
                if (c.model != null && c.flashUntil > 0f && clock >= c.flashUntil)
                {
                    c.flashUntil = 0f;
                    KenneyProps.SetFlash(c.model, 0f);
                }
            }
        }

        /// <summary>Damage from a tower (or the hero, src null): armour and the pylon's bonus on flyers apply.</summary>
        void Hit(Creep c, float damage, TowerDef src)
        {
            if (c == null || c.hp <= 0f || c.go == null) return;
            if (src != null && src.Physical && c.def.Armored) damage *= BastionCatalog.ArmorFactor;
            if (src != null && src.Kind == TowerKind.Pylon && c.def.Flies) damage *= 2f;
            c.hp -= damage;
            if (c.model != null)
            {
                KenneyProps.SetFlash(c.model, 0.7f);
                c.flashUntil = clock + 0.08f;
            }
            if (c.hp > 0f)
            {
                c.hpBar.SetActive(true);
                float f = Mathf.Clamp01(c.hp / c.maxHp);
                c.hpFill.localScale = new Vector3(0.68f * f, 0.07f, 1f);
                c.hpFill.localPosition = new Vector3(-0.34f * (1f - f), 0f, -0.01f);
                return;
            }
            gold += c.def.Gold;
            var at = c.go.transform.position;
            Fx.Burst(at, new Color(0.6f, 0.2f, 0.15f), c.def.Kind == CreepKind.Colossus ? 34 : 12, 3f, 0.09f);
            Fx.Text(at + Vector3.up * 0.9f, $"+{c.def.Gold}", PlaceholderVisuals.CoinColor, 0.75f);
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
                AnimateTower(t, dt);
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
                        if (Random.value < 0.35f) Fx.Burst(c.go.transform.position, new Color(1f, 0.55f, 0.15f), 3, 1.2f, 0.07f, -0.6f);
                        Hit(c, dmg, t.def);
                    }
                    if (any)
                    {
                        t.cooldown = BastionCatalog.IntervalAt(t.def, t.level);
                        if (t.flame != null) t.anim = 1f;
                    }
                    continue;
                }

                var target = FirstInRange(at, range, t.def.HitsAir);
                if (target == null) continue;
                t.cooldown = BastionCatalog.IntervalAt(t.def, t.level);
                Vector3 muzzle = t.go.transform.position + Vector3.up * 0.75f;
                var aim = target.go.transform.position + Vector3.up * 0.35f;
                var def = t.def;

                switch (def.Kind)
                {
                    case TowerKind.Archer:
                        Launch("arrow", muzzle, aim, 0.18f, 0.45f, 0.25f, true, () => Hit(target, dmg, def));
                        Sfx.Shoot();
                        break;
                    case TowerKind.Cannon:
                        if (t.barrel != null)
                        {
                            var dir = aim - t.barrel.position;
                            float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                            t.barrel.rotation = Quaternion.Euler(0f, 0f, ang);
                            t.anim = 1f;
                            muzzle = t.barrel.position + (Vector3)((Vector2)dir.normalized * 0.55f);
                        }
                        Vector2 impact = target.pos;
                        Launch("ball", muzzle, W(impact, aim.z) + Vector3.up * 0.2f, 0.45f, 0.24f, 1.4f, false, () => CannonImpact(impact, dmg, def));
                        Sfx.Attack();
                        break;
                    case TowerKind.Frost:
                        Launch("shard", muzzle, aim, 0.2f, 0.4f, 0.2f, true, () =>
                        {
                            if (target.go == null) return;
                            target.slowUntil = clock + def.SlowTime;
                            target.slowFactor = 1f - def.Slow;
                            Fx.Burst(target.go.transform.position + Vector3.up * 0.3f, new Color(0.7f, 0.92f, 1f), 6, 1.6f, 0.06f, 0f);
                            Hit(target, dmg, def);
                        });
                        if (t.glow != null) t.anim = 1f;
                        break;
                    case TowerKind.Pylon:
                        var chain = new List<Creep> { target };
                        Creep prev = target;
                        for (int j = 1; j < def.Chain; j++)
                        {
                            Creep next = null;
                            float best = 1.5f * 1.5f;
                            foreach (var c in creeps)
                            {
                                if (chain.Contains(c)) continue;
                                float d = (c.pos - prev.pos).sqrMagnitude;
                                if (d < best) { best = d; next = c; }
                            }
                            if (next == null) break;
                            chain.Add(next);
                            prev = next;
                        }
                        var points = new List<Vector3> { muzzle + Vector3.up * 0.25f };
                        foreach (var c in chain) points.Add(c.go.transform.position + Vector3.up * 0.4f);
                        Lightning(points, def.Color);
                        for (int j = 0; j < chain.Count; j++) Hit(chain[j], j == 0 ? dmg : dmg * 0.7f, def);
                        if (t.glow != null) t.anim = 1f;
                        Sfx.Bounce();
                        break;
                }
            }
        }

        void CannonImpact(Vector2 at, float dmg, TowerDef def)
        {
            var p = W(at, -3f);
            Fx.Burst(p, new Color(1f, 0.6f, 0.2f), 16, 3.5f, 0.1f, 0.2f);
            Fx.Burst(p, new Color(0.35f, 0.3f, 0.28f), 10, 2f, 0.13f, 0.1f);
            Fx.Shake(0.08f, 0.1f);
            for (int i = creeps.Count - 1; i >= 0; i--)
            {
                var c = creeps[i];
                if (!c.def.Flies && (c.pos - at).sqrMagnitude <= def.Splash * def.Splash) Hit(c, dmg, def);
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

        void Launch(string sprite, Vector3 from, Vector3 to, float duration, float scale, float arc, bool rotate, Action onArrive)
        {
            var sr = Place("Shot", Art(sprite), Vector2.zero, scale, 350, 0f);
            sr.transform.position = new Vector3(from.x, from.y, -4f);
            shots.Add(new Shot { t = sr.transform, from = new Vector3(from.x, from.y, -4f), to = new Vector3(to.x, to.y, -4f), duration = duration, arc = arc, rotate = rotate, onArrive = onArrive });
        }

        void UpdateShots(float dt)
        {
            for (int i = shots.Count - 1; i >= 0; i--)
            {
                var s = shots[i];
                s.time += dt;
                float k = Mathf.Clamp01(s.time / s.duration);
                if (s.t != null)
                {
                    var p = Vector3.Lerp(s.from, s.to, k) + Vector3.up * (Mathf.Sin(k * Mathf.PI) * s.arc);
                    if (s.rotate)
                    {
                        var dir = p - s.t.position;
                        if (dir.sqrMagnitude > 0.00001f) s.t.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
                    }
                    s.t.position = p;
                }
                if (k < 1f) continue;
                shots.RemoveAt(i);
                if (s.t != null) Destroy(s.t.gameObject);
                if (playing) s.onArrive?.Invoke();
            }
        }

        static Material boltMaterial;

        /// <summary>A jagged bolt through the given points, gone in a tenth of a second.</summary>
        void Lightning(List<Vector3> points, Color color)
        {
            var go = new GameObject("Bolt");
            go.transform.SetParent(root, false);
            var lr = go.AddComponent<LineRenderer>();
            if (boltMaterial == null) boltMaterial = new Material(Shader.Find("Sprites/Default"));
            lr.sharedMaterial = boltMaterial;
            lr.useWorldSpace = true;
            var pts = new List<Vector3>();
            for (int i = 0; i < points.Count - 1; i++)
            {
                for (int k = 0; k < 6; k++)
                {
                    var p = Vector3.Lerp(points[i], points[i + 1], k / 6f);
                    if (k > 0) p += (Vector3)(Random.insideUnitCircle * 0.12f);
                    p.z = -4.5f;
                    pts.Add(p);
                }
            }
            var last = points[points.Count - 1];
            last.z = -4.5f;
            pts.Add(last);
            lr.positionCount = pts.Count;
            lr.SetPositions(pts.ToArray());
            lr.widthMultiplier = 0.08f;
            lr.startColor = Color.white;
            lr.endColor = color;
            lr.sortingOrder = 360;
            bolts.Add(new Bolt { line = lr, until = Time.time + 0.12f });
        }

        void UpdateBolts()
        {
            for (int i = bolts.Count - 1; i >= 0; i--)
            {
                if (bolts[i].line != null && Time.time < bolts[i].until) continue;
                if (bolts[i].line != null) Destroy(bolts[i].line.gameObject);
                bolts.RemoveAt(i);
            }
        }

        void UpdateZones(float dt)
        {
            for (int i = zones.Count - 1; i >= 0; i--)
            {
                var z = zones[i];
                if (clock >= z.until)
                {
                    if (z.go != null) Destroy(z.go);
                    zones.RemoveAt(i);
                    continue;
                }
                if (z.go != null) z.go.transform.localScale = Vector3.one * z.radius * 2f * (1f + Mathf.Sin(Time.time * 6f) * 0.04f);
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
            var before = heroPos;
            heroPos = Vector2.MoveTowards(heroPos, heroTarget, HeroSpeed * dt);
            if (Mathf.Abs(heroPos.x - before.x) > 0.0001f) heroSr.flipX = heroPos.x < before.x;
            PlaceHero();
            heroRing.SetActive(heroSelected);

            heroCooldown -= dt;
            if (heroCooldown > 0f) return;
            var target = FirstInRange(heroPos, HeroRange, air: true);
            if (target == null) return;
            heroCooldown = HeroInterval;
            heroSr.flipX = target.pos.x < heroPos.x;
            Launch("arrow", hero.position + Vector3.up * 0.3f, target.go.transform.position + Vector3.up * 0.35f, 0.15f, 0.4f, 0.1f, true,
                () => Hit(target, HeroDamage, null));
        }

        void OnPower()
        {
            if (!playing || powerCooldown > 0f || hero == null) return;
            powerCooldown = BastionCatalog.PowerCooldown;
            float k = Mathf.Max(1f, healthScale);
            var at = heroPos;
            Sfx.Spring();
            ui.ShowBanner(BastionCatalog.PowerName(power), BastionCatalog.PowerBlurb(power), 1.2f);
            Fx.Burst(hero.position + Vector3.up * 0.4f, ApogeeTheme.Gold, 24, 4f, 0.1f, 0f);
            switch (power)
            {
                case BastionCatalog.HeroPower.Volley:
                    var near = new List<Creep>(creeps);
                    near.Sort((a, b) => (a.pos - at).sqrMagnitude.CompareTo((b.pos - at).sqrMagnitude));
                    for (int i = 0; i < Mathf.Min(6, near.Count); i++)
                    {
                        var c = near[i];
                        Launch("arrow", hero.position + Vector3.up * 0.4f, c.go.transform.position + Vector3.up * 0.35f, 0.2f + i * 0.04f, 0.5f, 0.3f, true, () => Hit(c, 15f * k, null));
                    }
                    break;
                case BastionCatalog.HeroPower.Grenade:
                    var spot = DensestPoint();
                    Launch("ball", hero.position + Vector3.up * 0.4f, W(spot, -3f), 0.5f, 0.3f, 1.6f, false, () => Blast(spot, 1.5f, 55f * k, new Color(1f, 0.55f, 0.15f)));
                    break;
                case BastionCatalog.HeroPower.Toxic:
                    AddZone(at, 1.9f, 7f * k, 6f, new Color(0.45f, 0.85f, 0.25f, 0.35f));
                    break;
                case BastionCatalog.HeroPower.Rampart:
                    lives = Mathf.Min(BastionCatalog.StartLives, lives + 3);
                    Fx.Burst(W(path[path.Length - 1], -3f), new Color(0.5f, 1f, 0.6f), 30, 4f, 0.1f, 0f);
                    break;
                case BastionCatalog.HeroPower.FireWall:
                    AddZone(at, 2.4f, 10f * k, 5f, new Color(1f, 0.45f, 0.15f, 0.35f));
                    break;
                case BastionCatalog.HeroPower.Salvage:
                    gold += 120;
                    Fx.Text(hero.position + Vector3.up, "+120 OR", PlaceholderVisuals.CoinColor, 1.2f);
                    break;
                case BastionCatalog.HeroPower.Calm:
                    foreach (var c in creeps) { c.slowUntil = clock + 5f; c.slowFactor = 0.5f; }
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
                        c.stunUntil = clock + 3f;
                        Hit(c, 20f * k, null);
                    }
                    Fx.Burst(hero.position, new Color(0.9f, 0.9f, 0.6f), 24, 4f, 0.1f, 0f);
                    break;
                case BastionCatalog.HeroPower.Meteor:
                    Creep big = null;
                    foreach (var c in creeps) if (big == null || c.hp > big.hp) big = c;
                    if (big != null)
                    {
                        var target = big.pos;
                        Launch("ball", W(target + new Vector2(-3f, 8f), -3f), W(target, -3f), 0.6f, 0.9f, 0f, false, () => Blast(target, 1.3f, 200f * k, new Color(1f, 0.4f, 0.1f)));
                    }
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
            Fx.Burst(W(at, -3f), color, 40, 6f, 0.16f, 0.3f);
            Fx.Burst(W(at, -3f), new Color(0.3f, 0.25f, 0.22f), 20, 3f, 0.18f, 0.1f);
            Fx.Shake(0.4f, 0.3f);
            for (int i = creeps.Count - 1; i >= 0; i--)
                if ((creeps[i].pos - at).sqrMagnitude <= radius * radius) Hit(creeps[i], damage, null);
        }

        void AddZone(Vector2 at, float radius, float dps, float duration, Color color)
        {
            var sr = Place("Zone", Art("glow"), at, radius * 2f, -30, 5f, null, color);
            zones.Add(new Zone { at = at, radius = radius, dps = dps, until = clock + duration, go = sr.gameObject });
        }

        // ---- towers' look ------------------------------------------------------------------

        /// <summary>Height above the tower's base of a point drawn at this pixel row of its 256 px art.</summary>
        static float ArtHeight(float pixelRow) => (238f - pixelRow) / 256f * TowerWidth;

        void DrawTower(Tower t)
        {
            if (t.go != null) Destroy(t.go);
            var at = slots[t.slot];
            var go = new GameObject($"Tower_{t.def.Kind}");
            go.transform.SetParent(root, false);
            go.transform.position = W(at + new Vector2(0f, -0.3f), DepthZ(at.y));
            t.go = go;
            int order = DepthOrder(at.y);
            float scale = 1f + 0.06f * (t.level - 1);
            var bodySprite = Art(TowerArt(t.def.Kind, t.level), 0.07f, 256f / TowerWidth);
            t.body = Place("Body", bodySprite, Vector2.zero, scale, order, 0f, go.transform);
            t.barrel = null; t.flame = null; t.glow = null;
            switch (t.def.Kind)
            {
                case TowerKind.Cannon:
                {
                    var b = Art($"barrel_{t.level}", 0.5f, 256f / TowerWidth);
                    var barrel = new GameObject("Barrel");
                    barrel.transform.SetParent(go.transform, false);
                    barrel.transform.localPosition = new Vector3(0f, ArtHeight(170f - 10f * t.level) * scale, -0.05f);
                    var bsr = barrel.AddComponent<SpriteRenderer>();
                    bsr.sprite = Sprite.Create(b.texture, b.rect, new Vector2(0.15f, 0.5f), 256f / TowerWidth);
                    bsr.sortingOrder = order + 1;
                    barrel.transform.localScale = Vector3.one * scale;
                    t.barrel = barrel.transform;
                    break;
                }
                case TowerKind.Brazier:
                    float bowl = ArtHeight(150f - 14f * t.level) * scale;
                    t.flame = Place("Flame", Art("flame", 0.12f), new Vector2(0f, bowl), 0.75f * scale, order + 1, -0.05f, go.transform);
                    Place("FireGlow", Art("glow"), new Vector2(0f, bowl + 0.2f), 1.8f * scale, order - 1, 0.02f, go.transform, new Color(1f, 0.55f, 0.2f, 0.45f));
                    break;
                case TowerKind.Frost:
                    t.glow = Place("Aura", Art("glow"), new Vector2(0f, ArtHeight(110f - 10f * t.level) * scale), 1.6f * scale, order + 1, -0.05f, go.transform, new Color(0.6f, 0.9f, 1f, 0.35f));
                    break;
                case TowerKind.Pylon:
                    float topY = ArtHeight(120f - 22f * t.level + 12f) * scale;
                    t.glow = Place("Orb", Art("orb"), new Vector2(0f, topY), 0.7f, order + 1, -0.05f, go.transform);
                    break;
            }
        }

        void AnimateTower(Tower t, float dt)
        {
            t.anim = Mathf.MoveTowards(t.anim, 0f, dt * 4f);
            if (t.flame != null)
            {
                // The fire breathes, and leaps when it bites.
                float f = 1f + Mathf.Sin(Time.time * 17f + t.slot) * 0.08f + t.anim * 0.25f;
                float s = 0.75f * (1f + 0.06f * (t.level - 1));
                t.flame.transform.localScale = new Vector3(s * (1.5f - f * 0.5f), s * f, 1f);
            }
            if (t.glow != null)
            {
                var c = t.glow.color;
                c.a = 0.35f + 0.2f * Mathf.Sin(Time.time * 3f + t.slot) + 0.4f * t.anim;
                t.glow.color = c;
            }
            if (t.barrel != null && t.anim > 0f)
                t.barrel.localPosition = new Vector3(-t.barrel.right.x * t.anim * 0.08f,
                    ArtHeight(170f - 10f * t.level) * (1f + 0.06f * (t.level - 1)) - t.barrel.right.y * t.anim * 0.08f, -0.05f);
        }

        // ---- input, ring menu ------------------------------------------------------------------

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
            OnTap(new Vector2(world.x - Origin.x, world.y - Origin.y));
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
            for (int i = 0; i < slots.Count; i++)
            {
                // A tower is tall: a tap on its body counts as well as on its pad.
                var d = p - slots[i];
                if (d.x * d.x / 0.36f + (d.y - (slotTowers[i] != null ? 0.4f : 0f)) * (d.y - (slotTowers[i] != null ? 0.4f : 0f)) / 0.64f > 1f) continue;
                heroSelected = false;
                SelectSlot(i);
                return;
            }
            if ((heroPos + new Vector2(0f, 0.2f) - p).sqrMagnitude < 0.6f * 0.6f)
            {
                CloseMenus();
                heroSelected = !heroSelected;
                messageText.text = heroSelected ? "Touche le terrain pour envoyer le héros" : "";
                return;
            }
            if (heroSelected)
            {
                heroTarget = new Vector2(Mathf.Clamp(p.x, -IslandHalfW + 0.5f, IslandHalfW - 0.5f), Mathf.Clamp(p.y, -IslandHalfH + 0.5f, IslandHalfH - 0.3f));
                heroSelected = false;
                messageText.text = "";
                Fx.Burst(W(heroTarget, -3f), ApogeeTheme.Gold, 8, 1.5f, 0.06f, 0f);
                return;
            }
            CloseMenus();
        }

        void SelectSlot(int i)
        {
            selectedSlot = i;
            var t = slotTowers[i];
            towerRing = t != null;
            rangeRing.SetActive(true);
            rangeRing.transform.position = W(slots[i], 5.3f);
            float r = t != null ? BastionCatalog.RangeAt(t.def, t.level) : 2.6f;
            rangeRing.transform.localScale = Vector3.one * r * 2f;
            padRing.SetActive(true);
            padRing.transform.position = W(slots[i] + new Vector2(0f, -0.06f), 5.4f);
            ringMenu.SetActive(true);
            ringMenu.transform.SetAsLastSibling();
            RefreshRing();
            PlaceRing();
            Sfx.Drop();
        }

        void CloseMenus()
        {
            selectedSlot = -1;
            if (ringMenu != null) ringMenu.SetActive(false);
            if (rangeRing != null) rangeRing.SetActive(false);
            if (padRing != null) padRing.SetActive(false);
        }

        /// <summary>Keeps the ring centred on its pad, in canvas units, and inside the screen.</summary>
        void PlaceRing()
        {
            if (cam == null || selectedSlot < 0) return;
            var canvasRt = (RectTransform)ui.Canvas.transform;
            Vector2 screen = cam.WorldToScreenPoint(W(slots[selectedSlot] + new Vector2(0f, 0.3f)));
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRt, screen, null, out var local);
            var size = canvasRt.rect.size;
            const float margin = 190f;
            local.x = Mathf.Clamp(local.x, -size.x / 2f + margin, size.x / 2f - margin);
            local.y = Mathf.Clamp(local.y, -size.y / 2f + margin, size.y / 2f - margin - 90f);
            ringRoot.anchoredPosition = local;
        }

        /// <summary>The ring: five towers around an empty pad; upgrade and sell around a tower.</summary>
        void RefreshRing()
        {
            if (selectedSlot < 0) return;
            const float radius = 150f;
            if (!towerRing)
            {
                for (int i = 0; i < 5; i++)
                {
                    var (b, icon, cost) = ringButtons[i];
                    var def = BastionCatalog.Towers[i];
                    b.gameObject.SetActive(true);
                    float a = Mathf.PI / 2f - i * 2f * Mathf.PI / 5f;
                    ((RectTransform)b.transform).anchoredPosition = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
                    icon.sprite = Art(TowerArt(def.Kind, 1));
                    icon.preserveAspect = true;
                    cost.text = $"{def.Cost}";
                    b.interactable = gold >= def.Cost;
                }
                ringInfo.text = "";
                ((RectTransform)ringInfo.transform).anchoredPosition = new Vector2(0f, -radius - 110f);
                return;
            }

            var t = slotTowers[selectedSlot];
            for (int i = 0; i < 5; i++) ringButtons[i].button.gameObject.SetActive(i < 2);
            var (ub, uicon, ucost) = ringButtons[0];
            ((RectTransform)ub.transform).anchoredPosition = new Vector2(0f, radius);
            if (t.level < BastionCatalog.MaxTowerLevel)
            {
                int c = BastionCatalog.UpgradeCost(t.def, t.level + 1);
                uicon.sprite = Art(TowerArt(t.def.Kind, t.level + 1));
                ucost.text = $"{c}";
                ub.interactable = gold >= c;
            }
            else
            {
                uicon.sprite = Art(TowerArt(t.def.Kind, t.level));
                ucost.text = "MAX";
                ub.interactable = false;
            }
            var (sb, sicon, scost) = ringButtons[1];
            ((RectTransform)sb.transform).anchoredPosition = new Vector2(0f, -radius);
            sicon.sprite = GameIcons.Coin;
            scost.text = $"+{Mathf.RoundToInt(BastionCatalog.Invested(t.def, t.level) * BastionCatalog.SellRefund)}";
            sb.interactable = true;
            ringInfo.text = $"{t.def.Name}  niv. {t.level}  ·  {t.def.Blurb}";
            ((RectTransform)ringInfo.transform).anchoredPosition = new Vector2(radius + 330f, 0f);
        }

        void OnRingButton(int i)
        {
            if (selectedSlot < 0) return;
            if (!towerRing) { Build((TowerKind)i); return; }
            if (i == 0) Upgrade();
            else Sell();
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
            Fx.Burst(W(slots[selectedSlot], -3f), new Color(0.75f, 0.65f, 0.55f), 18, 3f, 0.1f, 0.4f);
            CloseMenus();
            messageText.text = "";
            RefreshHud();
        }

        void Upgrade()
        {
            var t = slotTowers[selectedSlot];
            if (t == null || t.level >= BastionCatalog.MaxTowerLevel) return;
            int cost = BastionCatalog.UpgradeCost(t.def, t.level + 1);
            if (gold < cost) return;
            gold -= cost;
            t.level++;
            DrawTower(t);
            Sfx.Milestone();
            Fx.Burst(W(slots[selectedSlot], -3f) + Vector3.up * 0.6f, ApogeeTheme.Gold, 24, 3.5f, 0.1f, 0f);
            CloseMenus();
            RefreshHud();
        }

        void Sell()
        {
            var t = slotTowers[selectedSlot];
            if (t == null) return;
            gold += Mathf.RoundToInt(BastionCatalog.Invested(t.def, t.level) * BastionCatalog.SellRefund);
            if (t.go != null) Destroy(t.go);
            Fx.Burst(W(slots[selectedSlot], -3f), new Color(0.6f, 0.55f, 0.5f), 18, 3f, 0.1f, 0.5f);
            slotTowers[selectedSlot] = null;
            Sfx.Drop();
            CloseMenus();
            RefreshHud();
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
            overPanel.transform.SetAsLastSibling();
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
                if (record && held / 25 > old / 25 && SaveSystem.TryAddReviveKit()) extra = "      1 [k]";
                overTitle.text = record ? "NOUVEAU RECORD !" : "LE BASTION EST TOMBÉ";
                overDetail.text = $"Vagues tenues : {held}   ·   record : {Mathf.Max(held, old)}";
                for (int s = 0; s < 3; s++) overStars[s].gameObject.SetActive(false);
                Sfx.Death();
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
            waveText.text = map == null ? "" : map.Endless ? $"Vague {wave}" : $"Vague {wave} / {map.Waves}";
            livesText.text = lives.ToString();
            goldText.text = gold.ToString();

            if (wave == 0) callLabel.text = "LANCER LA VAGUE";
            else if (waveRunning) callLabel.text = queue.Count > 0 ? "VAGUE EN COURS" : "ACHÈVE-LES !";
            else callLabel.text = $"VAGUE SUIVANTE  {Mathf.CeilToInt(nextWaveTimer)} s";
            callButton.interactable = !waveRunning;

            powerFill.fillAmount = powerCooldown / BastionCatalog.PowerCooldown;
            powerTimer.text = powerCooldown > 0f ? Mathf.CeilToInt(powerCooldown).ToString() : "";
            powerButton.interactable = powerCooldown <= 0f && playing;
            if (ringMenu.activeSelf) RefreshRing();
        }
    }
}
