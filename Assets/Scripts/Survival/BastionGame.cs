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
    /// bastion's gate on the right. The player builds towers anywhere on the grass beside
    /// the road (not on it, nor on a tree, nor too close to another tower) - archers, canon,
    /// brazier, frost, pylon, each upgradable four times, each level dearer - from a ring
    /// menu that opens where they tapped; a card shows each tower's damage, range and rate
    /// of fire. Waves can be called at any time, even over one still running: each wave
    /// spawns on its own and pays its bonus once its last dead falls. The player also
    /// places their own character as a hero who fights
    /// and, every half minute, unleashes that character's power (BastionCatalog.PowerOf).
    /// Twenty lives; a colossus through the gate costs five.
    ///
    /// Two ways to play: a campaign of five maps with up to three stars each, and the
    /// endless island, which is saved when left alive and picks up where it stopped. Rewards: coins, materials, and a healing kit for a first three-star
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
        /// <summary>The painted map covers x -12..12, y -7..5, whatever its resolution.</summary>
        const float MapWidth = 24f;
        static readonly Vector2 MapCentre = new Vector2(0f, -1f);
        const float IslandHalfW = 8.75f, IslandHalfH = 4.8f;
        /// <summary>What the camera fits at zoom 1: the island's grass, edge to edge (the HUD floats over it).</summary>
        const float FitTop = 4.95f, FitBottom = -5.1f, FitHalfW = 9.5f;
        /// <summary>How far the view may pan when zoomed in.</summary>
        const float PanLeft = -9.8f, PanRight = 9.8f, PanBottom = -6.2f, PanTop = 5.3f;
        const float MaxZoom = 2.6f;
        const float BetweenWaves = 12f;
        const float HeroRange = 1.9f, HeroInterval = 0.7f, HeroSpeed = 3f;
        const float TowerWidth = 1.7f;

        // ---- world state ------------------------------------------------------------------

        class Creep
        {
            public CreepDef def;
            public float hp, maxHp, dist, slowUntil, slowFactor = 1f, stunUntil;
            public int wave;
            public Vector2 pos;
            public GameObject go;
            public Transform hpFill;
            public GameObject hpBar;
            public Renderer[] model;
            public float bob, flashUntil;
            // painted body (the runner's shades), when the art is there
            public SpriteRenderer body;
            public Sprite[] frames;
            public float anim;
        }

        class Tower
        {
            public TowerDef def;
            public int level, seed;
            public Vector2 at;
            public float cooldown;
            public GameObject pad;
            public GameObject go;
            public SpriteRenderer body;
            public Transform barrel;      // canon
            public SpriteRenderer flame;  // brazier
            public SpriteRenderer glow;   // frost, pylon
            public float anim;
        }

        /// <summary>A wave on its way out of the rift: its dead still to come, one by one.</summary>
        class WaveStream
        {
            public int n;
            public float timer, scale;
            public readonly List<CreepKind> queue = new();
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
        readonly List<Tower> towers = new();
        /// <summary>Painted trees, bushes and rocks of this map (x, y, radius kept clear).</summary>
        Vector3[] obstacles = new Vector3[0];
        int towerSeed;
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
        readonly List<WaveStream> streams = new();
        /// <summary>Waves called whose dead are not all down yet.</summary>
        readonly List<int> openWaves = new();
        float nextWaveTimer, healthScale, lastCall = -9f, messageUntil;

        // the hero
        Transform hero;
        SpriteRenderer heroSr;
        GameObject heroRing;
        Vector2 heroPos, heroTarget;
        float heroCooldown, powerCooldown;
        bool heroSelected;
        BastionCatalog.HeroPower power;

        // the open ring menu: around a tower, or on a free spot of grass
        Tower selected;
        bool spotOpen;
        Vector2 spot;
        int previewKind = -1;    // the tower shown on the card before building it
        bool MenuOpen => selected != null || spotOpen;
        Vector2 MenuPoint => selected != null ? selected.at : spot;
        GameObject rangeRing, padRing;
        bool pressActive, pressOverUi, dragged, pinched;
        Vector2 pressStart, lastDrag;
        float lastPinchDist;
        Vector2 lastPinchMid;

        // zoom and pan
        float zoom = 1f;
        Vector2 viewCentre;
        bool viewSet;

        // the overlay showing where a tower may stand
        bool zonesShown;
        SpriteRenderer zonesSr;
        Texture2D zonesTex;
        Button zonesButton;
        Text zonesLabel;

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
        Button endlessNewButton;
        GameObject infoPanel;
        Text infoTitle, infoBody;
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

            // Top left, under the strip: where towers may go, and the zoom.
            var zr = UiKit.CreateRect("Zones", hud, new Vector2(0f, 1f), new Vector2(0f, 1f));
            zr.pivot = new Vector2(0f, 1f);
            zr.sizeDelta = new Vector2(250f, 84f);
            zr.anchoredPosition = new Vector2(24f, -128f);
            var zImg = zr.gameObject.AddComponent<Image>();
            zImg.sprite = ApogeeTheme.Chip;
            zImg.type = Image.Type.Sliced;
            zonesButton = zr.gameObject.AddComponent<Button>();
            zonesButton.targetGraphic = zImg;
            zonesButton.onClick.AddListener(ToggleZones);
            zonesLabel = UiKit.Outlined(UiKit.CreateText("Label", zr, "ZONES", 30, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, ApogeeTheme.Cream), 1.5f);
            zonesLabel.rectTransform.offsetMin = new Vector2(12f, 4f);
            zonesLabel.rectTransform.offsetMax = new Vector2(-12f, -4f);
            UiKit.FitLabel(zonesLabel, 30);
            RoundButton("ZoomIn", hud, new Vector2(0f, 1f), new Vector2(84, 84), new Vector2(330f, -170f), () => ZoomButton(1.35f), new Color(0.95f, 0.85f, 0.75f));
            RoundButton("ZoomOut", hud, new Vector2(0f, 1f), new Vector2(84, 84), new Vector2(430f, -170f), () => ZoomButton(1f / 1.35f), new Color(0.95f, 0.85f, 0.75f));
            foreach (var (nm, txt, x) in new[] { ("ZoomIn", "+", 330f), ("ZoomOut", "-", 430f) })
            {
                var b = hud.Find(nm);
                UiKit.Outlined(UiKit.CreateText("Sign", b, txt, 54, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, ApogeeTheme.Cream), 2f).raycastTarget = false;
            }

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

            // The tower card, bottom left: what a tower does, and what its next level adds.
            var info = UiKit.CreateRect("TowerCard", hud, new Vector2(0.012f, 0.03f), new Vector2(0.33f, 0.47f));
            infoPanel = info.gameObject;
            var infoImg = info.gameObject.AddComponent<Image>();
            infoImg.sprite = ApogeeTheme.Panel;
            infoImg.type = Image.Type.Sliced;
            infoImg.color = new Color(1f, 1f, 1f, 0.94f);
            infoImg.raycastTarget = false;
            infoTitle = UiKit.Outlined(UiKit.CreateText("Title", info, "", 36, TextAnchor.UpperLeft, new Vector2(0.07f, 0.8f), new Vector2(0.95f, 0.95f), ApogeeTheme.Gold), 1.5f);
            UiKit.FitLabel(infoTitle, 36);
            infoBody = UiKit.CreateText("Body", info, "", 26, TextAnchor.UpperLeft, new Vector2(0.07f, 0.05f), new Vector2(0.95f, 0.8f), ApogeeTheme.Cream);
            infoBody.supportRichText = true;
            infoBody.lineSpacing = 1.1f;
            UiKit.FitLabel(infoBody, 26);
            infoPanel.SetActive(false);
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
                UnityEngine.Events.UnityAction open = endless ? () => { if (!ResumeEndless()) StartMap(-1); } : () => StartMap(captured);
                var m = endless ? BastionCatalog.Endless : BastionCatalog.Campaign[i];
                float x0 = 0.03f + i * w + 0.006f, x1 = 0.03f + (i + 1) * w - 0.006f;
                var card = UiKit.CreateButton($"Map_{i}", sel, "", new Vector2(x0, 0.2f), new Vector2(x1, 0.77f), open, 24,
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
                if (endless)
                {
                    endlessBestText = stars;
                    // With a run saved, the card resumes it and this starts a new one.
                    endlessNewButton = UiKit.CreateButton("EndlessNew", sel, "NOUVELLE PARTIE", new Vector2(x0, 0.135f), new Vector2(x1, 0.19f),
                        () => { ClearEndlessSave(); StartMap(-1); }, 20, new Color(0.22f, 0.10f, 0.08f));
                }
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
            SaveEndlessIfRunning();
            playing = false;
            ClearWorld();
            ReleaseCamera();
            RestoreOrientation();
            rotatePanel.SetActive(false);
        }

        void ShowSelect()
        {
            SaveEndlessIfRunning();
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
            var saved = LoadEndlessSave();
            endlessBestText.text = saved != null ? $"REPRENDRE : vague {saved.wave + 1}" : best > 0 ? $"Record : vague {best}" : "Aucun record";
            endlessNewButton.gameObject.SetActive(saved != null);
        }

        void ClearWorld()
        {
            foreach (var c in creeps) if (c.go != null) Destroy(c.go);
            creeps.Clear();
            towers.Clear();
            streams.Clear();
            openWaves.Clear();
            selected = null;
            spotOpen = false;
            zones.Clear();
            shots.Clear();
            bolts.Clear();
            drifters.Clear();
            if (root != null) Destroy(root.gameObject);
            root = null;
            zonesSr = null;
            if (zonesTex != null) Destroy(zonesTex);
            zonesTex = null;
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
            obstacles = index < 0 ? BastionObstacles.Endless
                : BastionObstacles.Campaign[Mathf.Clamp(index, 0, BastionObstacles.Campaign.Length - 1)];

            gold = map.StartGold;
            lives = BastionCatalog.StartLives;
            wave = 0;
            clock = 0f;
            speed = 1;
            speedLabel.text = "x1";
            nextWaveTimer = 0f;
            lastCall = -9f;
            towerSeed = 0;
            heroSelected = false;
            power = BastionCatalog.PowerOf(SaveSystem.SelectedSkinId);
            powerCooldown = 8f;

            BuildWorld();
            TakeOverCamera(new Vector3(Origin.x, Origin.y, -10f), 7f, new Color(0.95f, 0.58f, 0.42f));
            zoom = 1f;
            viewSet = false;
            FrameCamera();
            if (zonesShown) RefreshZones();
            UpdateZonesLabel();

            selectPanel.SetActive(false);
            overPanel.SetActive(false);
            hudRoot.SetActive(true);
            CloseMenus();
            playing = true;
            messageText.text = "Touche l'herbe au bord du chemin pour bâtir une tour";
            messageUntil = 0f;
            ui.ShowBanner(map.Name, map.Subtitle, 2.2f);
            RefreshHud();
        }

        /// <summary>Portrait catalog coordinates to the landscape island: a quarter turn.</summary>
        static Vector2 Turn(Vector2 p) => new Vector2(-p.y, p.x);

        /// <summary>Orthographic size that fits the whole island at zoom 1.</summary>
        float BaseOrtho => Mathf.Max((FitTop - FitBottom) / 2f, FitHalfW / Mathf.Max(0.1f, cam != null ? cam.aspect : 1.78f));

        void FrameCamera()
        {
            if (cam == null) return;
            if (!viewSet) { viewCentre = new Vector2(0f, (FitTop + FitBottom) / 2f); viewSet = true; }
            float ortho = BaseOrtho / zoom;
            cam.orthographicSize = ortho;
            viewCentre = ClampView(viewCentre, ortho);
            cam.transform.position = new Vector3(Origin.x + viewCentre.x, Origin.y + viewCentre.y, -10f);
        }

        Vector2 ClampView(Vector2 c, float ortho)
        {
            float halfW = ortho * Mathf.Max(0.1f, cam.aspect), halfH = ortho;
            c.x = halfW * 2f >= PanRight - PanLeft ? (PanLeft + PanRight) / 2f : Mathf.Clamp(c.x, PanLeft + halfW, PanRight - halfW);
            float fitMid = (FitTop + FitBottom) / 2f;
            c.y = halfH * 2f >= PanTop - PanBottom ? fitMid : Mathf.Clamp(c.y, PanBottom + halfH, PanTop - halfH);
            if (zoom <= 1.001f) c = new Vector2(0f, fitMid);
            return c;
        }

        /// <summary>Zooms keeping the point under the finger (or cursor) where it is.</summary>
        void ZoomAt(Vector2 screen, float newZoom)
        {
            if (cam == null) return;
            newZoom = Mathf.Clamp(newZoom, 1f, MaxZoom);
            if (Mathf.Approximately(newZoom, zoom)) return;
            var before = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 10f));
            Vector2 wp = new Vector2(before.x - Origin.x, before.y - Origin.y);
            float k = zoom / newZoom;
            viewCentre = wp + (viewCentre - wp) * k;
            zoom = newZoom;
            FrameCamera();
        }

        void PanByScreen(Vector2 delta)
        {
            if (cam == null || zoom <= 1.001f) return;
            float unitsPerPixel = 2f * cam.orthographicSize / Mathf.Max(1, Screen.height);
            viewCentre -= delta * unitsPerPixel;
            FrameCamera();
        }

        void ZoomButton(float factor)
        {
            if (!playing) return;
            ZoomAt(new Vector2(Screen.width / 2f, Screen.height / 2f), zoom * factor);
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
                var island = Sprite.Create(mapTexture, new Rect(0, 0, mapTexture.width, mapTexture.height), new Vector2(0.5f, 0.5f), mapTexture.width / MapWidth);
                Place("Island", island, MapCentre, 1f, -50, 6f);
            }

            // The cursor ring on the chosen spot, and the reach of the tower there.
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
            AnimateZones();
            UpdateShots(realDt * speed);
            UpdateBolts();
            if (MenuOpen) PlaceRing();
            if (!playing) return;
            if (messageUntil > 0f && Time.time > messageUntil) { messageUntil = 0f; messageText.text = ""; }

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
            // Every wave called spawns on its own clock, so several can pour out at once.
            for (int i = streams.Count - 1; i >= 0; i--)
            {
                var st = streams[i];
                st.timer -= dt;
                if (st.timer > 0f) continue;
                st.timer = BastionCatalog.SpawnGap(st.n);
                SpawnCreep(st.queue[0], st);
                st.queue.RemoveAt(0);
                if (st.queue.Count == 0) streams.RemoveAt(i);
            }

            // A wave is beaten once it has nothing left to send and its last dead is down.
            for (int i = openWaves.Count - 1; i >= 0; i--)
            {
                int n = openWaves[i];
                if (WaveStillOut(n)) continue;
                openWaves.RemoveAt(i);
                WaveCleared(n);
                if (!playing) return;
            }

            // Between waves, the next one comes by itself after a breather.
            if (openWaves.Count == 0 && wave > 0 && CanCallWave)
            {
                nextWaveTimer -= dt;
                if (nextWaveTimer <= 0f) StartNextWave(early: false);
            }
        }

        bool WaveStillOut(int n)
        {
            foreach (var st in streams) if (st.n == n) return true;
            foreach (var c in creeps) if (c.wave == n) return true;
            return false;
        }

        /// <summary>The campaign stops at its last wave; the endless island never does.</summary>
        bool CanCallWave => playing && (map.Endless || wave < map.Waves);

        /// <summary>Gold for calling the next wave now: the time given up, or more risk taken.</summary>
        int EarlyBonus => openWaves.Count > 0 ? 10 + wave * 2 : wave > 0 ? Mathf.CeilToInt(Mathf.Max(0f, nextWaveTimer) * 2f) : 0;

        void StartNextWave(bool early)
        {
            if (!CanCallWave) return;
            if (early)
            {
                // A double tap must not send two waves.
                if (Time.time - lastCall < 0.6f) return;
                lastCall = Time.time;
                int bonus = EarlyBonus;
                if (bonus > 0)
                {
                    gold += bonus;
                    Fx.Text(W(path[0], -3f) + Vector3.up * 0.8f, $"+{bonus} OR", PlaceholderVisuals.CoinColor, 1.1f);
                }
            }
            wave++;
            var st = new WaveStream { n = wave, timer = 0.3f, scale = BastionCatalog.HealthScale(wave, map.Toughness) };
            st.queue.AddRange(BastionCatalog.WaveCreeps(wave));
            streams.Add(st);
            openWaves.Add(wave);
            healthScale = st.scale;
            nextWaveTimer = BetweenWaves;
            bool boss = wave % 10 == 0;
            ui.ShowBanner(boss ? "UN COLOSSE !" : $"VAGUE {wave}", boss ? "Il vaut cinq vies : arrête-le" : $"{st.queue.Count} morts sortent de la faille", 1.6f);
            if (messageUntil <= 0f) messageText.text = "";
            if (rift != null) Fx.Burst(rift.position, new Color(0.8f, 0.4f, 1f), 30, 4f, 0.12f, 0f);
            RefreshHud();
        }

        void WaveCleared(int n)
        {
            int bonus = BastionCatalog.WaveBonus(n);
            gold += bonus;
            Sfx.Milestone();
            if (!map.Endless && openWaves.Count == 0 && wave >= map.Waves) { EndMap(true); return; }
            if (openWaves.Count == 0) nextWaveTimer = BetweenWaves;
            Say($"Vague {n} repoussée   ·   +{bonus} or", 2.5f);
        }

        void Say(string text, float seconds)
        {
            messageText.text = text;
            messageUntil = Time.time + seconds;
        }

        // ---- creeps -------------------------------------------------------------------------

        void SpawnCreep(CreepKind kind, WaveStream from)
        {
            var def = BastionCatalog.Creep(kind);
            var c = new Creep { def = def, maxHp = def.Hp * from.scale, dist = 0f, bob = Random.Range(0f, 6f), wave = from.n };
            c.hp = c.maxHp;
            var go = new GameObject($"Creep_{kind}");
            go.transform.SetParent(root, false);
            c.go = go;
            float h = def.Height;
            float lift = def.Flies ? 0.55f : 0f;
            Transform rig = null;
            // The painted shades of the runner: Rôdeurs, shadow hounds, wisps, stone-plated colossi.
            var (sheet, bodyH, tint) = kind switch
            {
                CreepKind.Runner => ("hound", 0.8f, Color.white),
                CreepKind.Brute => ("brute", 1.85f, Color.white),
                CreepKind.Armored => ("walker_b", 1.25f, new Color(0.85f, 0.9f, 1f)),
                CreepKind.Specter => ("wisp", 1.3f, Color.white),
                CreepKind.Colossus => ("brute", 1.85f, new Color(1f, 0.75f, 0.7f)),
                _ => ("walker", 1.25f, Color.white),
            };
            var frames = EnemySprite.Frames(sheet, 0f);
            if (frames != null)
            {
                var bodyGo = new GameObject("Body");
                bodyGo.transform.SetParent(go.transform, false);
                bodyGo.transform.localPosition = new Vector3(0f, -0.26f + lift, -0.05f);
                bodyGo.transform.localScale = Vector3.one * (h / bodyH);
                c.body = bodyGo.AddComponent<SpriteRenderer>();
                c.body.sprite = frames[0];
                c.body.color = tint;
                c.frames = frames;
                c.anim = Random.Range(0f, frames.Length);
                rig = bodyGo.transform;
            }
            else if (KenneyProps.Available)
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
                c.go.transform.position = W(c.pos + new Vector2(0f, c.body != null ? 0f : Mathf.Abs(Mathf.Sin(c.bob)) * 0.04f), DepthZ(c.pos.y));
                if (c.body != null)
                {
                    // The walk cycle keeps pace with the ground covered; drawn in depth like everything else.
                    c.anim += dt * 9f * (speedNow / Mathf.Max(0.1f, c.def.Speed)) * Mathf.Clamp(c.def.Speed, 0.6f, 1.6f);
                    c.body.sprite = c.frames[(int)c.anim % c.frames.Length];
                    c.body.sortingOrder = DepthOrder(c.pos.y);
                    if (c.flashUntil > 0f && clock >= c.flashUntil) { c.flashUntil = 0f; c.body.color = c.def.Armored ? new Color(0.85f, 0.9f, 1f) : c.def.Kind == CreepKind.Colossus ? new Color(1f, 0.75f, 0.7f) : Color.white; }
                }
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
            else if (c.body != null)
            {
                c.body.color = new Color(1f, 0.45f, 0.35f);
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
            foreach (var t in towers)
            {
                AnimateTower(t, dt);
                t.cooldown -= dt;
                if (t.cooldown > 0f) continue;
                Vector2 at = t.at;
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
            var at = t.at;
            if (t.pad == null) t.pad = Place("Pad", Art("pad"), at + new Vector2(0f, -0.06f), 1.15f, -40, 5.5f).gameObject;
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
            // Levels 4 and 5 keep the level 3 look, crowned by a golden aura and a star each.
            if (t.level > 3)
            {
                Place("Aura", Art("glow"), new Vector2(0f, 0.25f), 2.1f * scale, order - 2, 0.05f, go.transform,
                    new Color(1f, 0.82f, 0.4f, t.level >= 5 ? 0.5f : 0.32f));
                for (int k = 0; k < t.level - 3; k++)
                {
                    float x = (k - (t.level - 4) * 0.5f) * 0.32f;
                    var star = Place($"Star_{k}", PlaceholderVisuals.Star(), new Vector2(x, -0.12f), 0.3f, order + 3, -0.1f, go.transform, ApogeeTheme.Gold);
                    star.transform.localScale = Vector3.one * 0.3f;
                }
            }
        }

        void AnimateTower(Tower t, float dt)
        {
            t.anim = Mathf.MoveTowards(t.anim, 0f, dt * 4f);
            if (t.flame != null)
            {
                // The fire breathes, and leaps when it bites.
                float f = 1f + Mathf.Sin(Time.time * 17f + t.seed) * 0.08f + t.anim * 0.25f;
                float s = 0.75f * (1f + 0.06f * (t.level - 1));
                t.flame.transform.localScale = new Vector3(s * (1.5f - f * 0.5f), s * f, 1f);
            }
            if (t.glow != null)
            {
                var c = t.glow.color;
                c.a = 0.35f + 0.2f * Mathf.Sin(Time.time * 3f + t.seed) + 0.4f * t.anim;
                t.glow.color = c;
            }
            if (t.barrel != null && t.anim > 0f)
                t.barrel.localPosition = new Vector3(-t.barrel.right.x * t.anim * 0.08f,
                    ArtHeight(170f - 10f * t.level) * (1f + 0.06f * (t.level - 1)) - t.barrel.right.y * t.anim * 0.08f, -0.05f);
        }

        // ---- input, ring menu ------------------------------------------------------------------

        /// <summary>
        /// A tap builds or selects; a drag pans the zoomed view; two fingers pinch to zoom; the
        /// mouse wheel zooms on the cursor.
        /// </summary>
        void HandleInput()
        {
            var pointer = Pointer.current;
            if (pointer == null || cam == null) return;

            var mouse = Mouse.current;
            if (mouse != null)
            {
                float wheel = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(wheel) > 0.01f && !IsPointerOverUi())
                    ZoomAt(mouse.position.ReadValue(), zoom * (wheel > 0f ? 1.15f : 1f / 1.15f));
            }

            var ts = Touchscreen.current;
            if (ts != null)
            {
                int n = 0;
                Vector2 a = default, b = default;
                foreach (var t in ts.touches)
                {
                    if (!t.press.isPressed) continue;
                    if (n == 0) a = t.position.ReadValue(); else if (n == 1) b = t.position.ReadValue();
                    n++;
                }
                if (n >= 2)
                {
                    float dist = Vector2.Distance(a, b);
                    Vector2 mid = (a + b) / 2f;
                    if (pinched && lastPinchDist > 1f)
                    {
                        ZoomAt(mid, zoom * dist / lastPinchDist);
                        PanByScreen(mid - lastPinchMid);
                    }
                    pinched = true;
                    lastPinchDist = dist;
                    lastPinchMid = mid;
                    return;
                }
                lastPinchDist = 0f;
            }

            Vector2 pos = pointer.position.ReadValue();
            if (pointer.press.wasPressedThisFrame)
            {
                pressActive = true;
                pressOverUi = IsPointerOverUi();
                pressStart = lastDrag = pos;
                dragged = false;
                pinched = false;
            }
            if (pressActive && pointer.press.isPressed && !pressOverUi)
            {
                if (!dragged && (pos - pressStart).magnitude > Screen.height * 0.025f) dragged = true;
                if (dragged) PanByScreen(pos - lastDrag);
                lastDrag = pos;
            }
            if (!pointer.press.wasReleasedThisFrame || !pressActive) return;
            pressActive = false;
            if (pressOverUi || dragged || pinched) return;

            var world = cam.ScreenToWorldPoint(new Vector3(pos.x, pos.y, 10f));
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
            // A tower is tall: a tap on its body counts as well as on its pad.
            foreach (var t in towers)
            {
                var d = p - t.at;
                if (d.x * d.x / 0.36f + (d.y - 0.4f) * (d.y - 0.4f) / 0.64f > 1f) continue;
                heroSelected = false;
                OpenTowerMenu(t);
                return;
            }
            if ((heroPos + new Vector2(0f, 0.2f) - p).sqrMagnitude < 0.6f * 0.6f)
            {
                CloseMenus();
                heroSelected = !heroSelected;
                messageText.text = heroSelected ? "Touche le terrain pour envoyer le héros" : "";
                messageUntil = 0f;
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
            // A tap beside an open menu closes it; otherwise the grass tapped becomes a building spot.
            if (MenuOpen) { CloseMenus(); return; }
            string problem = PlacementProblem(p);
            if (problem != null)
            {
                Say(problem, 1.6f);
                Fx.Burst(W(p, -3f), new Color(0.9f, 0.3f, 0.2f), 6, 1.2f, 0.05f, 0f);
                Sfx.Hit();
                return;
            }
            OpenSpotMenu(p);
        }

        /// <summary>Why a tower cannot stand here (null when it can).</summary>
        string PlacementProblem(Vector2 p)
        {
            float edge = Mathf.Pow(Mathf.Pow(Mathf.Abs(p.x / IslandHalfW), 4f) + Mathf.Pow(Mathf.Abs(p.y / IslandHalfH), 4f), 0.25f);
            if (edge > 0.88f) return "Trop près du bord de l'île";
            if (BastionCatalog.DistanceToPath(path, p) < BastionCatalog.RoadClearance) return "Pas sur le chemin : à côté";
            var start = path[0] + new Vector2(-0.35f, 0f);
            var gate = path[path.Length - 1] + new Vector2(0.15f, -0.75f);
            if ((p - start).sqrMagnitude < 1.3f * 1.3f) return "Trop près de la faille";
            // The castle stands on the gate and rises about three metres.
            if (Mathf.Abs(p.x - gate.x) < 2f && p.y > gate.y - 0.7f && p.y < gate.y + 3.2f) return "Trop près du bastion";
            foreach (var t in towers)
                if ((t.at - p).sqrMagnitude < BastionCatalog.TowerSpacing * BastionCatalog.TowerSpacing) return "Trop près d'une autre tour";
            foreach (var o in obstacles)
            {
                float r = o.z + 0.35f;
                if (((Vector2)o - p).sqrMagnitude < r * r) return "Un arbre ou un rocher gêne";
            }
            return null;
        }

        void OpenTowerMenu(Tower t)
        {
            selected = t;
            spotOpen = false;
            previewKind = -1;
            OpenMenuAt(t.at, BastionCatalog.RangeAt(t.def, t.level));
        }

        void OpenSpotMenu(Vector2 p)
        {
            selected = null;
            spotOpen = true;
            spot = p;
            previewKind = -1;
            OpenMenuAt(p, 0f);
        }

        void OpenMenuAt(Vector2 at, float range)
        {
            towerRing = selected != null;
            rangeRing.SetActive(range > 0f);
            rangeRing.transform.position = W(at, 5.3f);
            rangeRing.transform.localScale = Vector3.one * range * 2f;
            padRing.SetActive(true);
            padRing.transform.position = W(at + new Vector2(0f, -0.06f), 5.4f);
            ringMenu.SetActive(true);
            ringMenu.transform.SetAsLastSibling();
            RefreshRing();
            PlaceRing();
            Sfx.Drop();
        }

        void CloseMenus()
        {
            selected = null;
            spotOpen = false;
            previewKind = -1;
            if (ringMenu != null) ringMenu.SetActive(false);
            if (rangeRing != null) rangeRing.SetActive(false);
            if (padRing != null) padRing.SetActive(false);
            if (infoPanel != null) infoPanel.SetActive(false);
        }

        /// <summary>Keeps the ring centred on its spot, in canvas units, and inside the screen.</summary>
        void PlaceRing()
        {
            if (cam == null || !MenuOpen) return;
            var canvasRt = (RectTransform)ui.Canvas.transform;
            Vector2 screen = cam.WorldToScreenPoint(W(MenuPoint + new Vector2(0f, 0.3f)));
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRt, screen, null, out var local);
            var size = canvasRt.rect.size;
            const float margin = 190f;
            local.x = Mathf.Clamp(local.x, -size.x / 2f + margin, size.x / 2f - margin);
            local.y = Mathf.Clamp(local.y, -size.y / 2f + margin, size.y / 2f - margin - 90f);
            ringRoot.anchoredPosition = local;
            // The tower card goes on the side the ring leaves free.
            var card = (RectTransform)infoPanel.transform;
            bool cardLeft = local.x > -size.x * 0.12f;
            card.anchorMin = cardLeft ? new Vector2(0.012f, 0.03f) : new Vector2(0.67f, 0.4f);
            card.anchorMax = cardLeft ? new Vector2(0.33f, 0.47f) : new Vector2(0.988f, 0.86f);
        }

        /// <summary>
        /// The ring: the five towers around a free spot (the first touch shows a tower's card
        /// and reach, the second builds it); upgrade and sell around a tower.
        /// </summary>
        void RefreshRing()
        {
            if (!MenuOpen) return;
            const float radius = 150f;
            if (!towerRing)
            {
                for (int i = 0; i < 5; i++)
                {
                    var (b, icon, cost) = ringButtons[i];
                    var def = BastionCatalog.Towers[i];
                    b.gameObject.SetActive(true);
                    float a = Mathf.PI / 2f - i * 2f * Mathf.PI / 5f;
                    var brt = (RectTransform)b.transform;
                    brt.anchoredPosition = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
                    brt.localScale = Vector3.one * (previewKind == i ? 1.18f : 1f);
                    icon.sprite = Art(TowerArt(def.Kind, 1));
                    icon.preserveAspect = true;
                    cost.text = previewKind == i ? $"OK {def.Cost}" : $"{def.Cost}";
                    b.interactable = gold >= def.Cost || previewKind != i;
                    ((Image)b.targetGraphic).color = previewKind == i ? new Color(1f, 0.92f, 0.6f) : new Color(0.95f, 0.85f, 0.75f);
                }
                if (previewKind >= 0)
                {
                    var def = BastionCatalog.Towers[previewKind];
                    ringInfo.text = gold >= def.Cost ? "Touche encore pour bâtir" : $"Il manque {def.Cost - gold} or";
                    ShowCard(def, 1, false);
                }
                else
                {
                    ringInfo.text = "Choisis une tour";
                    infoPanel.SetActive(false);
                }
                ((RectTransform)ringInfo.transform).anchoredPosition = new Vector2(0f, -radius - 110f);
                return;
            }

            var t = selected;
            for (int i = 0; i < 5; i++)
            {
                ringButtons[i].button.gameObject.SetActive(i < 2);
                ringButtons[i].button.transform.localScale = Vector3.one;
                ((Image)ringButtons[i].button.targetGraphic).color = new Color(0.95f, 0.85f, 0.75f);
            }
            var (ub, uicon, ucost) = ringButtons[0];
            ((RectTransform)ub.transform).anchoredPosition = new Vector2(0f, radius);
            bool maxed = t.level >= BastionCatalog.MaxTowerLevel;
            if (!maxed)
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
            ringInfo.text = "";
            ShowCard(t.def, t.level, !maxed);
        }

        static string Num(float v) => v >= 10f ? Mathf.RoundToInt(v).ToString() : v.ToString("0.#");
        static string Next(string now, string next, bool show) => show && next != now ? $"{now} <color=#9BFF8A>→ {next}</color>" : now;

        /// <summary>The tower card: damage, range and rate of fire, with the next level's numbers in green.</summary>
        void ShowCard(TowerDef d, int level, bool withNext)
        {
            infoPanel.SetActive(true);
            int nl = Mathf.Min(level + 1, BastionCatalog.MaxTowerLevel);
            float dmg = BastionCatalog.DamageAt(d, level), dmg2 = BastionCatalog.DamageAt(d, nl);
            float rng = BastionCatalog.RangeAt(d, level), rng2 = BastionCatalog.RangeAt(d, nl);
            float rate = 1f / BastionCatalog.IntervalAt(d, level), rate2 = 1f / BastionCatalog.IntervalAt(d, nl);
            string stars = level > 1 || withNext ? $"   niv. {level}/{BastionCatalog.MaxTowerLevel}" : "";
            infoTitle.text = d.Name.ToUpperInvariant() + stars;

            var sb = new System.Text.StringBuilder();
            sb.Append($"<color=#F5C25C>Dégâts</color>   {Next(Num(dmg), Num(dmg2), withNext)}\n");
            sb.Append($"<color=#F5C25C>Portée</color>   {Next(Num(rng) + " m", Num(rng2) + " m", withNext)}\n");
            sb.Append($"<color=#F5C25C>Cadence</color>   {Next(Num(rate) + " /s", Num(rate2) + " /s", withNext)}\n");
            sb.Append($"<color=#F5C25C>Dégâts/s</color>   {Next(Num(dmg * rate), Num(dmg2 * rate2), withNext)}\n");
            switch (d.Kind)
            {
                case TowerKind.Cannon: sb.Append($"Explose sur {Num(d.Splash)} m autour de l'impact\n"); break;
                case TowerKind.Brazier: sb.Append("Brûle tout ce qui marche à portée\n"); break;
                case TowerKind.Frost: sb.Append($"Ralentit de {Mathf.RoundToInt(d.Slow * 100f)} % pendant {Num(d.SlowTime)} s\n"); break;
                case TowerKind.Pylon: sb.Append($"Éclair sur {d.Chain} morts, double sur les volants\n"); break;
            }
            sb.Append(d.HitsAir ? "Touche les volants" : "Ne touche pas les volants");
            if (d.Physical) sb.Append("\nMoitié des dégâts sur les cuirassés");
            if (withNext) sb.Append($"\n<color=#F5C25C>Niveau {nl} :</color> {BastionCatalog.UpgradeCost(d, nl)} or");
            infoBody.text = sb.ToString();
        }

        void OnRingButton(int i)
        {
            if (!MenuOpen) return;
            if (!towerRing)
            {
                if (previewKind != i)
                {
                    // First touch: show the tower and its reach.
                    previewKind = i;
                    var def = BastionCatalog.Towers[i];
                    rangeRing.SetActive(true);
                    rangeRing.transform.localScale = Vector3.one * def.Range * 2f;
                    Sfx.Drop();
                    RefreshRing();
                    return;
                }
                Build((TowerKind)i);
                return;
            }
            if (i == 0) Upgrade();
            else Sell();
        }

        void Build(TowerKind kind)
        {
            if (!spotOpen) return;
            var def = BastionCatalog.Tower(kind);
            if (gold < def.Cost) return;
            gold -= def.Cost;
            var t = new Tower { def = def, level = 1, at = spot, seed = towerSeed++ };
            towers.Add(t);
            DrawTower(t);
            Sfx.Material();
            Fx.Burst(W(spot, -3f), new Color(0.75f, 0.65f, 0.55f), 18, 3f, 0.1f, 0.4f);
            CloseMenus();
            if (zonesShown) RefreshZones();
            messageText.text = "";
            RefreshHud();
        }

        void Upgrade()
        {
            var t = selected;
            if (t == null || t.level >= BastionCatalog.MaxTowerLevel) return;
            int cost = BastionCatalog.UpgradeCost(t.def, t.level + 1);
            if (gold < cost) return;
            gold -= cost;
            t.level++;
            DrawTower(t);
            Sfx.Milestone();
            Fx.Burst(W(t.at, -3f) + Vector3.up * 0.6f, ApogeeTheme.Gold, 24, 3.5f, 0.1f, 0f);
            // Stay on the tower: its card now shows the next step.
            rangeRing.transform.localScale = Vector3.one * BastionCatalog.RangeAt(t.def, t.level) * 2f;
            RefreshRing();
            RefreshHud();
        }

        void Sell()
        {
            var t = selected;
            if (t == null) return;
            gold += Mathf.RoundToInt(BastionCatalog.Invested(t.def, t.level) * BastionCatalog.SellRefund);
            if (t.go != null) Destroy(t.go);
            if (t.pad != null) Destroy(t.pad);
            Fx.Burst(W(t.at, -3f), new Color(0.6f, 0.55f, 0.5f), 18, 3f, 0.1f, 0.5f);
            towers.Remove(t);
            if (zonesShown) RefreshZones();
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
            streams.Clear();
            CloseMenus();
            if (map.Endless) ClearEndlessSave();
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

        // ---- where towers may go ----------------------------------------------------------------

        const int ZonesW = 400, ZonesH = 220;
        const float ZonesLeft = -10f, ZonesRight = 10f, ZonesBottom = -5.5f, ZonesTop = 5.5f;

        void ToggleZones()
        {
            if (!playing) return;
            zonesShown = !zonesShown;
            if (zonesShown) RefreshZones();
            else if (zonesSr != null) zonesSr.gameObject.SetActive(false);
            UpdateZonesLabel();
            Sfx.Drop();
        }

        void UpdateZonesLabel()
        {
            if (zonesLabel == null) return;
            zonesLabel.text = zonesShown ? "CACHER ZONES" : "VOIR ZONES";
            zonesLabel.color = zonesShown ? new Color(0.6f, 1f, 0.55f) : ApogeeTheme.Cream;
        }

        /// <summary>
        /// Paints every point of the island where a tower could stand in soft green, with a
        /// brighter outline, using the very rule a tap checks (PlacementProblem).
        /// </summary>
        void RefreshZones()
        {
            if (root == null) return;
            if (zonesTex == null)
                zonesTex = new Texture2D(ZonesW, ZonesH, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var ok = new bool[ZonesW * ZonesH];
            float sx = (ZonesRight - ZonesLeft) / ZonesW, sy = (ZonesTop - ZonesBottom) / ZonesH;
            for (int y = 0; y < ZonesH; y++)
                for (int x = 0; x < ZonesW; x++)
                    ok[y * ZonesW + x] = PlacementProblem(new Vector2(ZonesLeft + (x + 0.5f) * sx, ZonesBottom + (y + 0.5f) * sy)) == null;
            var px = new Color32[ZonesW * ZonesH];
            var fill = new Color32(110, 255, 120, 80);
            var edge = new Color32(190, 255, 170, 230);
            for (int y = 0; y < ZonesH; y++)
                for (int x = 0; x < ZonesW; x++)
                {
                    int i = y * ZonesW + x;
                    if (!ok[i]) { px[i] = new Color32(0, 0, 0, 0); continue; }
                    bool border = x == 0 || y == 0 || x == ZonesW - 1 || y == ZonesH - 1
                        || !ok[i - 1] || !ok[i + 1] || !ok[i - ZonesW] || !ok[i + ZonesW];
                    px[i] = border ? edge : fill;
                }
            zonesTex.SetPixels32(px);
            zonesTex.Apply();
            if (zonesSr == null)
            {
                var sprite = Sprite.Create(zonesTex, new Rect(0, 0, ZonesW, ZonesH), new Vector2(0.5f, 0.5f), ZonesW / (ZonesRight - ZonesLeft));
                zonesSr = Place("Zones", sprite, new Vector2((ZonesLeft + ZonesRight) / 2f, (ZonesBottom + ZonesTop) / 2f), 1f, -45, 5.8f);
            }
            zonesSr.gameObject.SetActive(true);
        }

        void AnimateZones()
        {
            if (zonesSr == null || !zonesSr.gameObject.activeSelf) return;
            zonesSr.color = new Color(1f, 1f, 1f, 0.75f + 0.25f * Mathf.Sin(Time.time * 3f));
        }

        // ---- the endless run, kept when left alive ------------------------------------------

        const string EndlessSaveKey = "bastion_endless_run";

        [Serializable]
        class SavedTower { public int kind, level; public float x, y; }

        [Serializable]
        class EndlessSave
        {
            public int wave, gold, lives;
            public float heroX, heroY;
            public List<SavedTower> towers = new();
        }

        static EndlessSave LoadEndlessSave()
        {
            string json = PlayerPrefs.GetString(EndlessSaveKey, "");
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonUtility.FromJson<EndlessSave>(json); }
            catch (Exception) { return null; }
        }

        static void ClearEndlessSave()
        {
            PlayerPrefs.DeleteKey(EndlessSaveKey);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Leaving the endless island alive keeps the run: towers, gold, lives and the last
        /// wave fully beaten (a wave left half-fought is fought again on return).
        /// </summary>
        void SaveEndlessIfRunning()
        {
            if (!playing || map == null || !map.Endless || lives <= 0) return;
            int beaten = wave;
            foreach (int n in openWaves) beaten = Mathf.Min(beaten, n - 1);
            var save = new EndlessSave { wave = Mathf.Max(0, beaten), gold = gold, lives = lives, heroX = heroTarget.x, heroY = heroTarget.y };
            foreach (var t in towers)
                save.towers.Add(new SavedTower { kind = (int)t.def.Kind, level = t.level, x = t.at.x, y = t.at.y });
            PlayerPrefs.SetString(EndlessSaveKey, JsonUtility.ToJson(save));
            PlayerPrefs.Save();
        }

        bool ResumeEndless()
        {
            var save = LoadEndlessSave();
            if (save == null) return false;
            StartMap(-1);
            gold = save.gold;
            lives = Mathf.Clamp(save.lives, 1, BastionCatalog.StartLives);
            wave = save.wave;
            healthScale = BastionCatalog.HealthScale(Mathf.Max(1, wave), map.Toughness);
            nextWaveTimer = BetweenWaves;
            foreach (var st in save.towers)
            {
                if (st.kind < 0 || st.kind >= BastionCatalog.Towers.Length) continue;
                var t = new Tower { def = BastionCatalog.Towers[st.kind], level = Mathf.Clamp(st.level, 1, BastionCatalog.MaxTowerLevel), at = new Vector2(st.x, st.y), seed = towerSeed++ };
                towers.Add(t);
                DrawTower(t);
            }
            heroPos = heroTarget = new Vector2(save.heroX, save.heroY);
            PlaceHero();
            if (zonesShown) RefreshZones();
            Say(wave > 0 ? $"Partie reprise après la vague {wave}" : "Partie reprise", 3f);
            RefreshHud();
            return true;
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) SaveEndlessIfRunning();
        }

        void OnApplicationQuit() => SaveEndlessIfRunning();

        // ---- HUD ---------------------------------------------------------------------------

        void RefreshHud()
        {
            waveText.text = map == null ? "" : map.Endless ? $"Vague {wave}" : $"Vague {wave} / {map.Waves}";
            livesText.text = lives.ToString();
            goldText.text = gold.ToString();

            int bonus = EarlyBonus;
            if (!CanCallWave) callLabel.text = playing ? "ACHÈVE-LES !" : "";
            else if (wave == 0) callLabel.text = "LANCER LA VAGUE";
            else if (openWaves.Count > 0) callLabel.text = $"VAGUE {wave + 1} MAINTENANT  +{bonus}";
            else callLabel.text = $"VAGUE SUIVANTE  {Mathf.CeilToInt(nextWaveTimer)} s  +{bonus}";
            callButton.interactable = CanCallWave;

            powerFill.fillAmount = powerCooldown / BastionCatalog.PowerCooldown;
            powerTimer.text = powerCooldown > 0f ? Mathf.CeilToInt(powerCooldown).ToString() : "";
            powerButton.interactable = powerCooldown <= 0f && playing;
            if (ringMenu.activeSelf) RefreshRing();
        }
    }
}
