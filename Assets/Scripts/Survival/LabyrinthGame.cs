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
    /// "LABYRINTHE": a Pac-Man maze in the Apogée world. The character has to gather every
    /// survival ration of the floor (water, food, care kits) and carry them back to the
    /// camp to go one floor deeper. Two kinds of dead share the corridors:
    ///
    ///  - the Guetteurs (watchers) walk a fixed beat and light the corridor in front of
    ///    them. Being seen for a moment raises the alarm: a Rôdeur is let loose where the
    ///    watcher stands. They never hurt on their own - they must simply not notice you.
    ///  - the Traqueurs (hunters) leave the nest at the top and hunt the character through
    ///    the maze, a little slower than them. Their touch costs a heart.
    ///
    /// Crimson bushes hide the character: no watcher sees into them, and the hunters lose
    /// the trail and search where they last saw them. A "Réserves" gauge slowly drains and
    /// only water and food refill it, so hiding forever is not an option.
    ///
    /// Movement is on the tile grid, as in the original: a swipe (or an arrow key) sets the
    /// direction the character will take at the next junction where it is open, and they
    /// keep going until a wall stops them. Everything is positional - no physics - and the
    /// maze is drawn into a single texture, so a floor is cheap to build. A new maze is
    /// generated for every floor.
    /// </summary>
    public class LabyrinthGame : MiniGame
    {
        public override string Id => "labyrinth";
        public override string Title => "LABYRINTHE";
        public override string Description => "Rapporte les rations sans te faire repérer";
        public override string BestLine => SaveSystem.LabyrinthBest > 0
            ? $"Record : {SaveSystem.LabyrinthBest} pts   ·   étage {SaveSystem.LabyrinthBestFloor}"
            : "Aucun record";

        // ---- layout -------------------------------------------------------------------

        static readonly Vector2 Origin = new Vector2(9000f, 3000f);
        /// <summary>
        /// Maze size in tiles, wide for landscape. Odd on both axes: cells sit on odd coordinates.
        /// </summary>
        const int W = 37, H = 17;
        /// <summary>Screen band (viewport height fraction) the maze is framed in, below the HUD.</summary>
        const float RegionBottom = 0.02f, RegionTop = 0.862f;
        const int TexelsPerTile = 24;

        /// <summary>The camp on the left edge, the nest on the right: the whole maze between them.</summary>
        static readonly Vector2Int Camp = new Vector2Int(1, H / 2);
        static readonly Vector2Int Nest = new Vector2Int(W - 2, H / 2);
        static readonly Vector2Int[] Dirs = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };

        // ---- tuning -------------------------------------------------------------------

        const int VisionRange = 4;
        const float ContactRadius = 0.58f;
        const float ReserveMax = 100f;
        const float ReserveGain = 24f;
        const float WatcherSpeed = 1.7f;
        const float WatcherPause = 1.3f;
        const float WatcherBlindTime = 4f;

        float PlayerSpeed => 4.6f * (1f + SaveSystem.GetLevel(UpgradeStat.Speed) * 0.02f);
        int MaxLives => 3 + SaveSystem.GetLevel(UpgradeStat.MaxHealth) / 3;
        float HunterSpeed => PlayerSpeed * Mathf.Min(0.70f + floor * 0.03f, 0.92f);
        float ReserveDrain => Mathf.Min(1.4f + floor * 0.1f, 2.4f);
        int RationCount => Mathf.Min(8 + floor * 2, 18);
        int WatcherCount => Mathf.Min(1 + floor / 2, 5);
        int HunterCount => floor < 3 ? 1 : floor < 6 ? 2 : 3;
        int BushCount => Mathf.Min(3 + floor / 2, 6);
        int MaxHunters => Mathf.Min(3 + floor / 2, 6);

        // ---- state --------------------------------------------------------------------

        /// <summary>Something that walks the tile grid: it left tile `from` and is `prog` of the way to from + dir.</summary>
        class Mover
        {
            public Vector2Int from;
            public Vector2Int dir;
            public Vector2Int lastDir = Vector2Int.up;
            public float prog;
            public Vector2 Pos => (Vector2)from + (Vector2)dir * prog;
            public Vector2Int Tile => prog < 0.5f ? from : from + dir;

            public void Place(Vector2Int tile)
            {
                from = tile;
                dir = Vector2Int.zero;
                prog = 0f;
            }
        }

        enum RationKind { Water, Food, Care }

        class Ration
        {
            public RationKind kind;
            public GameObject go;
            public float phase;
        }

        class Watcher
        {
            public Mover m = new();
            public Vector2Int facing;
            public Vector2Int runA, runB;
            public float pause, blindUntil, suspicion;
            public GameObject go;
            public Transform lantern;
            public readonly List<SpriteRenderer> sight = new();
            public readonly List<Vector2Int> seen = new();
        }

        class Hunter
        {
            public Mover m = new();
            public int brain;
            public bool rodeur, released, searching;
            public float releaseAt, stunUntil;
            public GameObject go;
            public Renderer[] renderers;
            public float bob;
            public bool flashing;
        }

        Transform root;        // the whole game world, kept across floors
        Transform floorRoot;   // one floor's maze, rations and monsters
        Texture2D mazeTexture;
        Sprite mazeSprite;
        Transform playerGo;
        SpriteRenderer playerSprite;
        Drone drone;

        bool[,] wall = new bool[W, H];
        readonly int[,] distBuffer = new int[W, H];
        readonly Queue<Vector2Int> bfsQueue = new();
        readonly List<Vector2Int> options = new();

        readonly Mover player = new();
        Vector2Int want;
        readonly Dictionary<Vector2Int, Ration> rations = new();
        readonly HashSet<Vector2Int> bushes = new();
        readonly List<Watcher> watchers = new();
        readonly List<Hunter> hunters = new();
        SpriteRenderer campGlow;

        int floor, score, lives, rationsTotal, rationsTaken, floorsCleared, silentFloors;
        float reserve, invulnerableUntil;
        bool playing, spottedThisFloor, hidden;
        Vector2Int lastKnown;

        // ---- UI -----------------------------------------------------------------------

        Text scoreText, bestText, floorText, rationText;
        Image reserveBar;
        readonly List<Image> hearts = new();
        GameObject overPanel;
        Text overTitle, overScoreText;
        IconText overRewardText;

        protected override void BuildUi()
        {
            var rt = UiKit.CreateRect("LabyrinthPanel", ui.Canvas.transform, Vector2.zero, Vector2.one);
            panel = rt.gameObject;

            // Swipes are read anywhere on the screen. First child, so every button is above it.
            var swipeRt = UiKit.CreateRect("SwipeZone", rt, Vector2.zero, Vector2.one);
            swipeRt.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            swipeRt.gameObject.AddComponent<SwipePad>().onSwipe = d => want = d;

            var topBar = UiKit.CreateRect("TopBar", rt, new Vector2(0f, 0.905f), new Vector2(1f, 1f));
            var topImg = topBar.gameObject.AddComponent<Image>();
            topImg.sprite = ApogeeTheme.Panel;
            topImg.type = Image.Type.Sliced;

            scoreText = UiKit.Outlined(UiKit.CreateText("Score", topBar, "0", 40, TextAnchor.MiddleLeft,
                new Vector2(0.04f, 0.44f), new Vector2(0.36f, 0.98f), ApogeeTheme.Cream));
            bestText = UiKit.CreateText("Best", topBar, "", 20, TextAnchor.UpperLeft,
                new Vector2(0.04f, 0.04f), new Vector2(0.36f, 0.44f), ApogeeTheme.Gold);
            floorText = UiKit.Outlined(UiKit.CreateText("Floor", topBar, "", 32, TextAnchor.MiddleCenter,
                new Vector2(0.36f, 0.46f), new Vector2(0.72f, 0.98f), ApogeeTheme.Gold));
            rationText = UiKit.CreateText("Rations", topBar, "", 22, TextAnchor.UpperCenter,
                new Vector2(0.36f, 0.04f), new Vector2(0.72f, 0.46f), ApogeeTheme.Cream);
            UiKit.CreateButton("Quit", topBar, "QUITTER", new Vector2(0.74f, 0.22f), new Vector2(0.97f, 0.78f), ReturnToHub, 20);

            // Second row: hearts on the left, the reserves gauge on the right.
            for (int i = 0; i < 7; i++)
            {
                var heart = UiKit.CreateImage($"Heart_{i}", rt, new Vector2(0.03f + i * 0.045f, 0.870f),
                    new Vector2(0.065f + i * 0.045f, 0.898f), PlaceholderVisuals.Circle(Color.white), new Color(0.86f, 0.2f, 0.16f));
                hearts.Add(heart);
                heart.gameObject.SetActive(false);
            }
            UiKit.Outlined(UiKit.CreateText("ReserveLabel", rt, "Réserves", 20, TextAnchor.MiddleRight,
                new Vector2(0.36f, 0.866f), new Vector2(0.55f, 0.902f), ApogeeTheme.Cream));
            reserveBar = UiKit.CreateBar("Reserve", rt, new Vector2(0.56f, 0.871f), new Vector2(0.96f, 0.897f), new Color(0.35f, 0.68f, 0.92f));

            var overRt = UiKit.CreatePanel("LabyrinthOver", rt, UiKit.Overlay);
            overPanel = overRt.gameObject;
            UiKit.CreateFrame("LabyrinthOverFrame", overRt, new Vector2(0.08f, 0.24f), new Vector2(0.92f, 0.8f));
            overTitle = UiKit.Outlined(UiKit.CreateText("OverTitle", overRt, "", 50, TextAnchor.MiddleCenter,
                new Vector2(0.05f, 0.66f), new Vector2(0.95f, 0.78f), ApogeeTheme.Gold), 2.5f);
            overScoreText = UiKit.CreateText("OverScore", overRt, "", 32, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.56f), new Vector2(0.9f, 0.65f), ApogeeTheme.Cream);
            UiKit.FitLabel(overScoreText, 32);
            overRewardText = IconText.Create("OverReward", overRt, "", 32, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.49f), new Vector2(0.9f, 0.56f), PlaceholderVisuals.CoinColor);
            UiKit.CreateButton("Retry", overRt, "REJOUER", new Vector2(0.25f, 0.38f), new Vector2(0.75f, 0.45f), ResetGame);
            UiKit.CreateButton("Menu", overRt, "MENU", new Vector2(0.25f, 0.29f), new Vector2(0.75f, 0.36f), ReturnToHub);
            overPanel.SetActive(false);

            var rotateRt = UiKit.CreatePanel("RotatePrompt", rt, new Color(0.12f, 0.03f, 0.03f, 0.94f));
            rotatePanel = rotateRt.gameObject;
            // A phone outline that keeps turning on its side, instead of words alone.
            var phone = UiKit.CreateImage("RotatePhone", rotateRt, new Vector2(0.5f, 0.6f), new Vector2(0.5f, 0.6f), ApogeeTheme.Button, ApogeeTheme.Gold, false);
            rotatePhone = phone.rectTransform;
            rotatePhone.sizeDelta = new Vector2(110f, 190f);
            phone.type = Image.Type.Sliced;
            var screen = UiKit.CreateImage("Screen", rotatePhone, new Vector2(0.14f, 0.1f), new Vector2(0.86f, 0.88f), PlaceholderVisuals.Square(Color.white), ApogeeTheme.CrimsonDark, false);
            screen.raycastTarget = false;
            UiKit.Outlined(UiKit.CreateText("RotateText", rotateRt, "Tourne ton téléphone\nle Labyrinthe se joue en paysage", 40, TextAnchor.MiddleCenter,
                new Vector2(0.08f, 0.36f), new Vector2(0.92f, 0.52f), ApogeeTheme.Cream), 2f);
            UiKit.CreateButton("RotateQuit", rotateRt, "MENU", new Vector2(0.3f, 0.2f), new Vector2(0.7f, 0.27f), ReturnToHub, 28);
            rotatePanel.SetActive(false);
        }

        // ---- lifecycle ----------------------------------------------------------------

        protected override void OnEnter()
        {
            var runnerPlayer = ui.Director != null ? ui.Director.Player : null;
            if (runnerPlayer != null) runnerPlayer.controlEnabled = false;
            MobileInput.Reset();
            LockLandscape();

            root = new GameObject("LabyrinthWorld").transform;
            BuildPlayer();
            TakeOverCamera(new Vector3(Origin.x, Origin.y, -10f), H * 0.6f, new Color(0.13f, 0.04f, 0.04f));
            FrameCamera();
            ResetGame();
        }

        protected override void OnExit()
        {
            playing = false;
            StopAllCoroutines();
            MobileInput.Reset();
            RestoreOrientation();
            if (rotatePanel != null) rotatePanel.SetActive(false);
            ClearFloor();
            if (root != null) Destroy(root.gameObject);
            root = null;
            drone = null;
        }

        void ResetGame()
        {
            floor = 0;
            score = 0;
            floorsCleared = 0;
            silentFloors = 0;
            lives = MaxLives;
            overPanel.SetActive(false);
            playing = true;
            NextFloor();
        }

        void NextFloor()
        {
            floor++;
            ClearFloor();
            BuildFloor();
            player.Place(Camp);
            player.lastDir = Vector2Int.right;
            want = Vector2Int.zero;
            lastKnown = Camp;
            hidden = false;
            reserve = floor == 1 ? ReserveMax : Mathf.Min(ReserveMax, reserve + 30f);
            spottedThisFloor = false;
            invulnerableUntil = Time.time + 1f;
            PlacePlayerVisual();
            RefreshHud();
            ui.ShowBanner($"ÉTAGE {floor}", floor == 1
                ? "Ramasse les rations et reviens au camp"
                : "Plus profond, plus de regards", 2f);
        }

        // ---- orientation --------------------------------------------------------------

        ScreenOrientation savedOrientation;
        bool orientationLocked;
        GameObject rotatePanel;
        RectTransform rotatePhone;

        /// <summary>
        /// The maze is wide, so a phone turns to landscape for this game and back to
        /// portrait when it ends. Where the app cannot turn the screen itself (a browser),
        /// a panel asks the player to turn their phone, and the game waits meanwhile.
        /// </summary>
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
                ? savedOrientation
                : ScreenOrientation.Portrait;
        }

        /// <summary>True while the screen is still upright: nothing moves until it is turned.</summary>
        bool WaitingForLandscape()
        {
            bool upright = Screen.height > Screen.width;
            if (rotatePanel != null && rotatePanel.activeSelf != upright) rotatePanel.SetActive(upright);
            if (!upright) return false;

            float t = Mathf.SmoothStep(0f, 1f, Mathf.PingPong(Time.unscaledTime * 0.8f, 1.3f) - 0.15f);
            if (rotatePhone != null) rotatePhone.localRotation = Quaternion.Euler(0f, 0f, -90f * t);

            // The clock is frozen for the game while it waits: nobody leaves the nest early.
            float dt = Time.deltaTime;
            invulnerableUntil += dt;
            foreach (var h in hunters)
            {
                if (!h.released) h.releaseAt += dt;
                h.stunUntil += dt;
            }
            foreach (var w in watchers) w.blindUntil += dt;
            return true;
        }

        // ---- maze ---------------------------------------------------------------------

        static bool IsCell(int x, int y) => x >= 1 && y >= 1 && x <= W - 2 && y <= H - 2 && (x & 1) == 1 && (y & 1) == 1;
        static bool Inside(Vector2Int t) => t.x >= 0 && t.y >= 0 && t.x < W && t.y < H;
        bool Open(Vector2Int t) => Inside(t) && !wall[t.x, t.y];
        /// <summary>3D monsters stand a little in front of the sprite floor so it never cuts through them.</summary>
        static readonly Vector3 MonsterDepth = new Vector3(0f, 0f, -1f);

        static Vector3 World(Vector2 tile) => new Vector3(Origin.x + tile.x - (W - 1) / 2f, Origin.y + tile.y - (H - 1) / 2f, 0f);

        /// <summary>
        /// A recursive-backtracker maze, then "braided": most dead ends get knocked through
        /// into a neighbour, which turns the tree into a web of loops. Loops are what makes a
        /// Pac-Man maze playable - with a single path the hunters would always corner you.
        /// The camp and the nest are then widened into small halls at the bottom and top.
        /// </summary>
        void GenerateMaze()
        {
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                    wall[x, y] = true;

            var stack = new Stack<Vector2Int>();
            var start = new Vector2Int(1, 1);
            wall[1, 1] = false;
            stack.Push(start);
            var next = new List<Vector2Int>(4);
            while (stack.Count > 0)
            {
                var c = stack.Peek();
                next.Clear();
                foreach (var d in Dirs)
                {
                    var n = c + d * 2;
                    if (IsCell(n.x, n.y) && wall[n.x, n.y]) next.Add(d);
                }
                if (next.Count == 0) { stack.Pop(); continue; }
                var pick = next[Random.Range(0, next.Count)];
                var between = c + pick;
                var cell = c + pick * 2;
                wall[between.x, between.y] = false;
                wall[cell.x, cell.y] = false;
                stack.Push(cell);
            }

            // Braid: almost every dead end opens into a second neighbour.
            for (int x = 1; x < W - 1; x += 2)
            {
                for (int y = 1; y < H - 1; y += 2)
                {
                    var c = new Vector2Int(x, y);
                    if (OpenNeighbours(c) != 1 || Random.value > 0.8f) continue;
                    next.Clear();
                    foreach (var d in Dirs)
                    {
                        var n = c + d * 2;
                        if (IsCell(n.x, n.y) && wall[c.x + d.x, c.y + d.y]) next.Add(d);
                    }
                    if (next.Count == 0) continue;
                    var pick = next[Random.Range(0, next.Count)];
                    wall[c.x + pick.x, c.y + pick.y] = false;
                }
            }

            // A few more random openings, so long corridors get side exits.
            for (int i = 0; i < 6; i++)
            {
                int x = Random.Range(1, W - 1), y = Random.Range(1, H - 1);
                if (!wall[x, y]) continue;
                bool horizontal = (x & 1) == 0 && (y & 1) == 1;
                bool vertical = (x & 1) == 1 && (y & 1) == 0;
                if (horizontal || vertical) wall[x, y] = false;
            }

            // The camp and the nest: three tiles tall, open to the maze.
            for (int dy = -1; dy <= 1; dy++)
            {
                wall[Camp.x, Camp.y + dy] = false;
                wall[Nest.x, Nest.y + dy] = false;
            }
        }

        int OpenNeighbours(Vector2Int t)
        {
            int n = 0;
            foreach (var d in Dirs) if (Open(t + d)) n++;
            return n;
        }

        /// <summary>Breadth-first distances (in tiles) from `from` to every open tile; -1 where unreachable.</summary>
        int[,] Distances(Vector2Int from)
        {
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                    distBuffer[x, y] = -1;
            bfsQueue.Clear();
            if (!Open(from)) return distBuffer;
            distBuffer[from.x, from.y] = 0;
            bfsQueue.Enqueue(from);
            while (bfsQueue.Count > 0)
            {
                var c = bfsQueue.Dequeue();
                int dist = distBuffer[c.x, c.y] + 1;
                foreach (var d in Dirs)
                {
                    var n = c + d;
                    if (!Open(n) || distBuffer[n.x, n.y] >= 0) continue;
                    distBuffer[n.x, n.y] = dist;
                    bfsQueue.Enqueue(n);
                }
            }
            return distBuffer;
        }

        void BuildFloor()
        {
            floorRoot = new GameObject($"Floor_{floor}").transform;
            floorRoot.SetParent(root, false);

            GenerateMaze();
            DrawMaze();
            BuildCampAndNest();

            // Tiles far enough from the camp to hold something, shuffled.
            var fromCamp = Distances(Camp);
            var spots = new List<Vector2Int>();
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                    if (!wall[x, y] && fromCamp[x, y] >= 4 && !NearNest(new Vector2Int(x, y)))
                        spots.Add(new Vector2Int(x, y));
            Shuffle(spots);

            PlaceWatchers(fromCamp);
            PlaceRations(spots);
            PlaceBushes(spots);
            PlaceHunters();
        }

        static void Shuffle<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        /// <summary>
        /// The whole maze painted into one texture: warm floor, rock walls with a crimson
        /// grass lip where a corridor runs above them and a dark cliff face where one runs
        /// below, so the top-down maze keeps the islands' look.
        /// </summary>
        void DrawMaze()
        {
            int tw = W * TexelsPerTile, th = H * TexelsPerTile;
            if (mazeTexture == null || mazeTexture.width != tw || mazeTexture.height != th)
            {
                if (mazeTexture != null) Destroy(mazeTexture);
                mazeTexture = new Texture2D(tw, th, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            }

            var px = new Color32[tw * th];
            var floorColor = new Color(0.33f, 0.15f, 0.11f);
            var floorDark = new Color(0.27f, 0.11f, 0.08f);
            var rock = new Color(0.50f, 0.30f, 0.23f);
            var rockDark = new Color(0.24f, 0.12f, 0.09f);
            var grass = new Color(0.72f, 0.17f, 0.11f);
            var grassLight = new Color(0.90f, 0.35f, 0.20f);
            const int T = TexelsPerTile;

            for (int ty = 0; ty < H; ty++)
            {
                for (int tx = 0; tx < W; tx++)
                {
                    bool isWall = wall[tx, ty];
                    bool openAbove = ty + 1 < H && !wall[tx, ty + 1];
                    bool openBelow = ty > 0 && !wall[tx, ty - 1];
                    bool openLeft = tx > 0 && !wall[tx - 1, ty];
                    bool openRight = tx + 1 < W && !wall[tx + 1, ty];
                    for (int py = 0; py < T; py++)
                    {
                        for (int pxX = 0; pxX < T; pxX++)
                        {
                            Color c;
                            float n = Hash(tx * T + pxX, ty * T + py) * 0.05f - 0.025f;
                            if (!isWall)
                            {
                                // Floor, with a soft shadow along the foot of the wall above.
                                c = ty + 1 < H && wall[tx, ty + 1] && py > T - 6 ? floorDark : floorColor;
                            }
                            else if (openAbove && py >= T - 4)
                            {
                                c = py == T - 1 ? grassLight : grass;
                            }
                            else if (openBelow && py < 7)
                            {
                                c = Color.Lerp(rockDark, rock, py / 10f);
                            }
                            else if ((openLeft && pxX < 2) || (openRight && pxX >= T - 2))
                            {
                                c = rockDark;
                            }
                            else
                            {
                                c = rock;
                            }
                            c.r += n; c.g += n; c.b += n;
                            px[(ty * T + py) * tw + tx * T + pxX] = c;
                        }
                    }
                }
            }
            mazeTexture.SetPixels32(px);
            mazeTexture.Apply();

            var go = new GameObject("Maze");
            go.transform.SetParent(floorRoot, false);
            go.transform.position = new Vector3(Origin.x, Origin.y, 0f);
            var sr = go.AddComponent<SpriteRenderer>();
            mazeSprite = Sprite.Create(mazeTexture, new Rect(0, 0, tw, th), new Vector2(0.5f, 0.5f), TexelsPerTile);
            sr.sprite = mazeSprite;
            sr.sortingOrder = -5;
        }

        static float Hash(int x, int y)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263);
                h = (h ^ (h >> 13)) * 1274126177u;
                return (h & 0xffff) / 65535f;
            }
        }

        SpriteRenderer Quad(Transform parent, string name, Vector2 tile, Vector2 size, Color color, int order, Sprite sprite = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = World(tile);
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite != null ? sprite : PlaceholderVisuals.Square(Color.white);
            sr.color = color;
            sr.sortingOrder = order;
            return sr;
        }

        void BuildCampAndNest()
        {
            // The camp: a campfire that flares up once every ration is gathered.
            campGlow = Quad(floorRoot, "CampGlow", Camp, new Vector2(1.6f, 2.6f), new Color(1f, 0.6f, 0.25f, 0.25f), -4, PlaceholderVisuals.Circle(Color.white));
            var log1 = Quad(floorRoot, "Log", Camp + new Vector2(0f, -0.18f), new Vector2(0.62f, 0.12f), new Color(0.35f, 0.2f, 0.12f), 1);
            log1.transform.rotation = Quaternion.Euler(0f, 0f, 20f);
            var log2 = Quad(floorRoot, "Log", Camp + new Vector2(0f, -0.18f), new Vector2(0.62f, 0.12f), new Color(0.3f, 0.17f, 0.1f), 1);
            log2.transform.rotation = Quaternion.Euler(0f, 0f, -20f);
            Quad(floorRoot, "Flame", Camp + new Vector2(0f, 0.02f), new Vector2(0.36f, 0.46f), new Color(1f, 0.62f, 0.2f), 2, PlaceholderVisuals.Circle(Color.white));
            Quad(floorRoot, "FlameCore", Camp + new Vector2(0f, -0.04f), new Vector2(0.18f, 0.24f), new Color(1f, 0.92f, 0.55f), 2, PlaceholderVisuals.Circle(Color.white));

            // The nest the hunters crawl out of.
            Quad(floorRoot, "Nest", Nest, new Vector2(0.9f, 3f), new Color(0.12f, 0.03f, 0.08f, 0.85f), -4, PlaceholderVisuals.Circle(Color.white));
        }

        /// <summary>
        /// Each watcher walks back and forth along one straight corridor at least five tiles
        /// long, away from the camp (nobody should be spotted before taking a single step).
        /// </summary>
        void PlaceWatchers(int[,] fromCamp)
        {
            var runs = new List<(Vector2Int a, Vector2Int b)>();
            for (int y = 0; y < H; y++)
                FindRuns(runs, W, x => new Vector2Int(x, y));
            for (int x = 0; x < W; x++)
                FindRuns(runs, H, y => new Vector2Int(x, y));
            Shuffle(runs);

            var used = new HashSet<Vector2Int>();
            foreach (var (a, b) in runs)
            {
                if (watchers.Count >= WatcherCount) break;
                var step = new Vector2Int(Math.Sign(b.x - a.x), Math.Sign(b.y - a.y));
                bool ok = true;
                for (var t = a; ; t += step)
                {
                    if (used.Contains(t) || fromCamp[t.x, t.y] < 6 || NearNest(t)) { ok = false; break; }
                    if (t == b) break;
                }
                if (!ok) continue;
                for (var t = a; ; t += step)
                {
                    used.Add(t);
                    foreach (var d in Dirs) used.Add(t + d);
                    if (t == b) break;
                }
                SpawnWatcher(a, b, step);
            }
        }

        void FindRuns(List<(Vector2Int, Vector2Int)> runs, int length, Func<int, Vector2Int> at)
        {
            int start = -1;
            for (int i = 0; i <= length; i++)
            {
                bool open = i < length && Open(at(i));
                if (open && start < 0) start = i;
                if (!open && start >= 0)
                {
                    if (i - start >= 5) runs.Add((at(start), at(i - 1)));
                    start = -1;
                }
            }
        }

        void SpawnWatcher(Vector2Int a, Vector2Int b, Vector2Int step)
        {
            var w = new Watcher { runA = a, runB = b };
            // Start somewhere along the beat, facing a random way.
            int len = Mathf.Max(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y));
            w.m.Place(a + step * Random.Range(0, len + 1));
            w.facing = Random.value < 0.5f ? step : -step;
            w.go = SpawnMonster("Guetteur", "character-skeleton", new Color(0.85f, 0.8f, 0.7f), out _);

            // The lantern that gives away where it looks.
            var lantern = new GameObject("Lantern");
            lantern.transform.SetParent(w.go.transform, false);
            lantern.transform.localScale = Vector3.one * 0.26f;
            var lsr = lantern.AddComponent<SpriteRenderer>();
            lsr.sprite = PlaceholderVisuals.RimCircle(ApogeeTheme.Gold);
            lsr.sortingOrder = 6;
            w.lantern = lantern.transform;

            for (int i = 0; i <= VisionRange; i++)
                w.sight.Add(Quad(floorRoot, "Sight", Vector2.zero, new Vector2(0.96f, 0.96f), Color.clear, -3));
            watchers.Add(w);
        }

        void PlaceRations(List<Vector2Int> spots)
        {
            rationsTotal = RationCount;
            rationsTaken = 0;
            int care = Mathf.Max(1, rationsTotal / 6);
            int placed = 0;
            for (int i = spots.Count - 1; i >= 0 && placed < rationsTotal; i--)
            {
                var t = spots[i];
                spots.RemoveAt(i);
                var kind = placed < care ? RationKind.Care : placed % 2 == 0 ? RationKind.Water : RationKind.Food;
                rations[t] = new Ration { kind = kind, go = BuildRation(kind, t), phase = Random.Range(0f, 6f) };
                placed++;
            }
            rationsTotal = placed;
        }

        GameObject BuildRation(RationKind kind, Vector2Int tile)
        {
            var go = new GameObject($"Ration_{kind}");
            go.transform.SetParent(floorRoot, false);
            go.transform.position = World(tile);
            var t = go.transform;
            switch (kind)
            {
                case RationKind.Water:
                    Part(t, PlaceholderVisuals.RimCircle(new Color(0.35f, 0.68f, 0.95f)), Color.white, new Vector2(0f, -0.04f), new Vector2(0.4f, 0.44f), 2);
                    Part(t, PlaceholderVisuals.Square(Color.white), new Color(0.35f, 0.68f, 0.95f), new Vector2(0f, 0.18f), new Vector2(0.14f, 0.16f), 2);
                    Part(t, PlaceholderVisuals.Circle(Color.white), new Color(1f, 1f, 1f, 0.8f), new Vector2(-0.07f, 0.0f), new Vector2(0.09f, 0.12f), 3);
                    break;
                case RationKind.Food:
                    Part(t, PlaceholderVisuals.Square(Color.white), new Color(0.86f, 0.5f, 0.18f), Vector2.zero, new Vector2(0.4f, 0.34f), 2);
                    Part(t, PlaceholderVisuals.Square(Color.white), new Color(0.55f, 0.28f, 0.1f), Vector2.zero, new Vector2(0.4f, 0.1f), 3);
                    Part(t, PlaceholderVisuals.Square(Color.white), new Color(0.95f, 0.85f, 0.7f), new Vector2(0f, 0.19f), new Vector2(0.42f, 0.06f), 3);
                    break;
                default:
                    Part(t, PlaceholderVisuals.Square(Color.white), new Color(0.97f, 0.95f, 0.9f), Vector2.zero, new Vector2(0.42f, 0.38f), 2);
                    Part(t, PlaceholderVisuals.Square(Color.white), new Color(0.85f, 0.18f, 0.15f), Vector2.zero, new Vector2(0.24f, 0.08f), 3);
                    Part(t, PlaceholderVisuals.Square(Color.white), new Color(0.85f, 0.18f, 0.15f), Vector2.zero, new Vector2(0.08f, 0.24f), 3);
                    break;
            }
            return go;
        }

        static void Part(Transform parent, Sprite sprite, Color color, Vector2 local, Vector2 size, int order)
        {
            var go = new GameObject("Part");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = order;
        }

        /// <summary>
        /// Crimson bushes, preferably right next to a watcher's beat: that is where a hiding
        /// place is worth something.
        /// </summary>
        void PlaceBushes(List<Vector2Int> spots)
        {
            var near = new List<Vector2Int>();
            var far = new List<Vector2Int>();
            foreach (var s in spots)
            {
                bool close = false;
                foreach (var w in watchers)
                {
                    var d = s - w.m.from;
                    if (Mathf.Abs(d.x) + Mathf.Abs(d.y) <= 5) { close = true; break; }
                }
                (close ? near : far).Add(s);
            }
            near.AddRange(far);
            foreach (var s in near)
            {
                if (bushes.Count >= BushCount) break;
                bool crowded = false;
                foreach (var w in watchers) if (InRun(w, s)) { crowded = true; break; }
                foreach (var b in bushes) if (Mathf.Abs(b.x - s.x) + Mathf.Abs(b.y - s.y) < 4) { crowded = true; break; }
                if (crowded) continue;
                bushes.Add(s);
                var leaf = ApogeeTheme.Leaf;
                Quad(floorRoot, "Bush", s + new Vector2(-0.2f, -0.12f), new Vector2(0.62f, 0.58f), new Color(leaf.r * 0.8f, leaf.g * 0.8f, leaf.b * 0.8f, 0.82f), 7, PlaceholderVisuals.Circle(Color.white));
                Quad(floorRoot, "Bush", s + new Vector2(0.22f, -0.1f), new Vector2(0.6f, 0.56f), new Color(leaf.r * 0.9f, leaf.g * 0.9f, leaf.b * 0.9f, 0.82f), 7, PlaceholderVisuals.Circle(Color.white));
                Quad(floorRoot, "Bush", s + new Vector2(0f, 0.16f), new Vector2(0.66f, 0.6f), new Color(leaf.r, leaf.g, leaf.b, 0.82f), 8, PlaceholderVisuals.Circle(Color.white));
            }
        }

        void PlaceHunters()
        {
            for (int i = 0; i < HunterCount; i++)
                AddHunter(false, NestSlot(i), 2.5f + i * 5f);
        }

        static Vector2Int NestSlot(int i) => Nest + new Vector2Int(0, i % 3 - 1);
        static bool NearNest(Vector2Int t) => Mathf.Abs(t.x - Nest.x) <= 1 && Mathf.Abs(t.y - Nest.y) <= 1;

        Hunter AddHunter(bool rodeur, Vector2Int at, float delay)
        {
            var h = new Hunter
            {
                rodeur = rodeur,
                brain = hunters.Count % 3,
                releaseAt = Time.time + delay,
                bob = Random.Range(0f, 6f),
            };
            h.m.Place(at);
            h.go = rodeur
                ? SpawnMonster("Rodeur", "character-vampire", new Color(0.7f, 0.4f, 0.55f), out h.renderers)
                : SpawnMonster("Traqueur", "character-zombie", PlaceholderVisuals.ZombieColor, out h.renderers);
            h.m.lastDir = Vector2Int.left;
            h.go.transform.position = World(h.m.Pos) + MonsterDepth;
            hunters.Add(h);
            return h;
        }

        GameObject SpawnMonster(string name, string model, Color fallbackTint, out Renderer[] renderers)
        {
            const float height = 0.95f;
            var go = new GameObject(name);
            go.transform.SetParent(floorRoot, false);
            renderers = null;
            Transform rig = null;
            if (KenneyProps.Available)
            {
                var size = KenneyProps.Size(PropKit.Graveyard, model);
                rig = KenneyProps.Spawn(PropKit.Graveyard, model, go.transform, new Vector3(0f, -height / 2f, 0f),
                    height / Mathf.Max(0.01f, size.y), PropLayer.Character, 0f, -6f);
                if (rig != null) renderers = rig.GetComponentsInChildren<Renderer>();
            }
            if (rig == null)
            {
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = PlaceholderVisuals.Zombie();
                sr.color = fallbackTint;
                sr.sortingOrder = 4;
                go.transform.localScale = Vector3.one * height;
            }
            return go;
        }

        void ClearFloor()
        {
            if (floorRoot != null) Destroy(floorRoot.gameObject);
            floorRoot = null;
            if (mazeSprite != null) Destroy(mazeSprite);
            mazeSprite = null;
            rations.Clear();
            bushes.Clear();
            watchers.Clear();
            hunters.Clear();
            campGlow = null;
        }

        // ---- the character ------------------------------------------------------------

        void BuildPlayer()
        {
            var skin = SkinCatalog.Find(SaveSystem.SelectedSkinId);
            var go = new GameObject("Explorer");
            go.transform.SetParent(root, false);
            var art = new GameObject("Art");
            art.transform.SetParent(go.transform, false);
            playerSprite = art.AddComponent<SpriteRenderer>();
            var portrait = SkinCatalog.LoadPortrait(skin, out bool custom);
            playerSprite.sprite = custom ? portrait : ui.PlayerSprite;
            playerSprite.color = custom ? Color.white : skin.Tint;
            playerSprite.sortingOrder = 5;

            // Fit the sprite into one tile, centred whatever its pivot.
            if (playerSprite.sprite != null)
            {
                var b = playerSprite.sprite.bounds;
                float scale = 0.95f / Mathf.Max(0.01f, Mathf.Max(b.size.x, b.size.y));
                art.transform.localScale = Vector3.one * scale;
                art.transform.localPosition = -b.center * scale;
            }
            playerGo = go.transform;

            int level = UpgradeManager.DroneLevel;
            if (level <= 0) return;
            // The drone stuns the nearest hunter now and then - a breather, never a weapon.
            drone = Drone.Create(root, playerGo, level);
            drone.offset = new Vector3(-0.38f, 0.5f, 0f);
            drone.fireInterval = level >= 3 ? 4.5f : level == 2 ? 6f : 7.5f;
            drone.range = 3.5f;
            drone.FindTarget = from =>
            {
                var h = NearestHunter(from, 3.5f);
                return h != null ? (Vector2?)World(h.m.Pos) : null;
            };
            drone.Fire = (from, target) =>
            {
                var h = NearestHunter(target, 0.6f);
                if (h == null) return;
                h.stunUntil = Time.time + 2.2f;
                Fx.Burst(target, ApogeeTheme.Gold, 12, 3f, 0.08f, 0f);
                Fx.Text((Vector3)target + Vector3.up * 0.7f, "ÉTOURDI", ApogeeTheme.Gold, 0.7f);
                Sfx.Shoot();
            };
        }

        Hunter NearestHunter(Vector2 worldPos, float range)
        {
            if (!playing) return null;
            Hunter best = null;
            float bestD = range * range;
            foreach (var h in hunters)
            {
                if (!h.released || Time.time < h.stunUntil) continue;
                float d = ((Vector2)World(h.m.Pos) - worldPos).sqrMagnitude;
                if (d < bestD) { bestD = d; best = h; }
            }
            return best;
        }

        void PlacePlayerVisual()
        {
            if (playerGo != null) playerGo.position = World(player.Pos);
            if (drone != null) drone.transform.position = playerGo.position + drone.offset;
        }

        // ---- per frame ----------------------------------------------------------------

        void Update()
        {
            if (!IsActive) return;
            FrameCamera();
            if (WaitingForLandscape()) return;
            if (!playing) return;
            float dt = Time.deltaTime;

            ReadKeys();
            MovePlayer(dt);
            UpdateRations(dt);
            UpdateWatchers(dt);
            UpdateHunters(dt);
            if (!playing) return;
            UpdateReserves(dt);
            if (!playing) return;
            UpdateCamp();
        }

        /// <summary>Frames the maze in the band under the HUD, on any screen shape.</summary>
        void FrameCamera()
        {
            if (cam == null) return;
            float band = RegionTop - RegionBottom;
            float ortho = Mathf.Max((H + 0.4f) / band / 2f, (W + 0.6f) / (2f * Mathf.Max(0.1f, cam.aspect)));
            float visible = ortho * 2f;
            float bandCentre = (RegionBottom + RegionTop) / 2f;
            cam.orthographicSize = ortho;
            cam.transform.position = new Vector3(Origin.x, Origin.y + (0.5f - bandCentre) * visible, -10f);
        }

        void ReadKeys()
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame || kb.zKey.wasPressedThisFrame) want = Vector2Int.up;
                if (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame) want = Vector2Int.down;
                if (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame || kb.qKey.wasPressedThisFrame) want = Vector2Int.left;
                if (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame) want = Vector2Int.right;
            }
            var pad = Gamepad.current;
            if (pad != null)
            {
                var s = pad.leftStick.ReadValue() + pad.dpad.ReadValue();
                if (s.sqrMagnitude > 0.35f)
                    want = Mathf.Abs(s.x) > Mathf.Abs(s.y)
                        ? (s.x > 0f ? Vector2Int.right : Vector2Int.left)
                        : (s.y > 0f ? Vector2Int.up : Vector2Int.down);
            }
        }

        /// <summary>
        /// Moves a walker `dist` tiles along the grid. Between two tiles it only goes on; on
        /// reaching a tile's centre it asks `choose` for the next direction (zero = stop).
        /// </summary>
        void Advance(Mover m, float dist, Func<Vector2Int> choose)
        {
            for (int guard = 0; dist > 0f && guard < 8; guard++)
            {
                if (m.dir == Vector2Int.zero)
                {
                    var d = choose();
                    if (d == Vector2Int.zero || !Open(m.from + d)) return;
                    m.dir = d;
                }
                float step = Mathf.Min(dist, 1f - m.prog);
                m.prog += step;
                dist -= step;
                if (m.prog < 0.999f) continue;
                m.from += m.dir;
                m.lastDir = m.dir;
                m.dir = Vector2Int.zero;
                m.prog = 0f;
            }
        }

        void MovePlayer(float dt)
        {
            // Turning back is allowed at any moment, as in the original.
            if (player.dir != Vector2Int.zero && want == -player.dir)
            {
                player.from += player.dir;
                player.prog = 1f - player.prog;
                player.dir = want;
            }

            // At each tile: take the wanted turn if it is open, otherwise keep going straight.
            Advance(player, PlayerSpeed * dt, () =>
            {
                if (want == Vector2Int.zero) return Vector2Int.zero;
                if (Open(player.from + want)) return want;
                return Open(player.from + player.lastDir) ? player.lastDir : Vector2Int.zero;
            });

            var face = player.dir != Vector2Int.zero ? player.dir : player.lastDir;
            if (face.x != 0) playerSprite.flipX = face.x < 0;
            playerGo.position = World(player.Pos) + Vector3.up * (player.dir != Vector2Int.zero ? Mathf.Abs(Mathf.Sin(Time.time * 14f)) * 0.06f : 0f);

            var tile = player.Tile;
            hidden = bushes.Contains(tile);
            if (!hidden) lastKnown = tile;

            float alpha = hidden ? 0.5f : 1f;
            if (Time.time < invulnerableUntil && Mathf.Repeat(Time.time * 10f, 1f) < 0.5f) alpha *= 0.35f;
            var c = playerSprite.color;
            playerSprite.color = new Color(c.r, c.g, c.b, alpha);
        }

        void UpdateRations(float dt)
        {
            foreach (var r in rations.Values)
            {
                r.phase += dt * 3f;
                r.go.transform.localScale = Vector3.one * (1f + Mathf.Sin(r.phase) * 0.07f);
            }

            var tile = player.Tile;
            if (!rations.TryGetValue(tile, out var ration)) return;
            rations.Remove(tile);
            rationsTaken++;
            int points = 10 + floor * 2;
            score += points;
            var at = ration.go.transform.position;
            switch (ration.kind)
            {
                case RationKind.Water:
                    reserve = Mathf.Min(ReserveMax, reserve + ReserveGain);
                    Fx.Text(at + Vector3.up * 0.6f, "EAU", new Color(0.55f, 0.8f, 1f), 0.8f);
                    Sfx.Coin();
                    break;
                case RationKind.Food:
                    reserve = Mathf.Min(ReserveMax, reserve + ReserveGain);
                    Fx.Text(at + Vector3.up * 0.6f, "NOURRITURE", new Color(1f, 0.7f, 0.35f), 0.8f);
                    Sfx.Coin();
                    break;
                default:
                    reserve = Mathf.Min(ReserveMax, reserve + 10f);
                    if (lives < MaxLives) lives++;
                    Fx.Text(at + Vector3.up * 0.6f, "SOIN", new Color(1f, 0.5f, 0.45f), 0.8f);
                    Sfx.Heal();
                    break;
            }
            Fx.Burst(at, ApogeeTheme.Gold, 10, 2.4f, 0.07f, 0f);
            Destroy(ration.go);

            if (rationsTaken >= rationsTotal)
            {
                Sfx.Milestone();
                ui.ShowBanner("TOUT EST LÀ", "Retourne au camp, en bas", 1.6f);
            }
            RefreshHud();
        }

        // ---- watchers -----------------------------------------------------------------

        bool InRun(Watcher w, Vector2Int t)
        {
            int minX = Mathf.Min(w.runA.x, w.runB.x), maxX = Mathf.Max(w.runA.x, w.runB.x);
            int minY = Mathf.Min(w.runA.y, w.runB.y), maxY = Mathf.Max(w.runA.y, w.runB.y);
            return t.x >= minX && t.x <= maxX && t.y >= minY && t.y <= maxY;
        }

        void UpdateWatchers(float dt)
        {
            var playerTile = player.Tile;
            foreach (var w in watchers)
            {
                if (w.pause > 0f) w.pause -= dt;
                else
                {
                    Advance(w.m, WatcherSpeed * dt, () =>
                    {
                        if (InRun(w, w.m.from + w.facing)) return w.facing;
                        // End of the beat: stop, look back the other way, then walk on.
                        w.facing = -w.facing;
                        w.pause = WatcherPause;
                        return Vector2Int.zero;
                    });
                }

                var pos = World(w.m.Pos);
                w.go.transform.position = pos + MonsterDepth;
                w.lantern.localPosition = new Vector3(w.facing.x * 0.34f, w.facing.y * 0.34f + 0.1f, 0f);

                // What it sees: its own tile and the corridor ahead, up to a wall.
                w.seen.Clear();
                var eye = w.m.Tile;
                w.seen.Add(eye);
                for (int i = 1; i <= VisionRange; i++)
                {
                    var t = eye + w.facing * i;
                    if (!Open(t)) break;
                    w.seen.Add(t);
                }

                bool blind = Time.time < w.blindUntil;
                int seenAt = -1;
                if (!blind && !hidden && Time.time >= invulnerableUntil)
                    for (int i = 0; i < w.seen.Count; i++)
                        if (w.seen[i] == playerTile) { seenAt = i; break; }

                // Bumping into a watcher is being seen, bush or not.
                if (!blind && Time.time >= invulnerableUntil && ((Vector2)(World(player.Pos) - pos)).sqrMagnitude < ContactRadius * ContactRadius)
                    seenAt = 0;

                if (seenAt >= 0)
                {
                    // Up close it notices at once; at the end of its light it takes a beat.
                    float need = seenAt <= 1 ? 0.05f : 0.1f + 0.07f * seenAt;
                    w.suspicion += dt / need;
                    if (w.suspicion >= 1f) RaiseAlarm(w);
                }
                else
                {
                    w.suspicion = Mathf.Max(0f, w.suspicion - dt * 2f);
                }

                for (int i = 0; i < w.sight.Count; i++)
                {
                    var sr = w.sight[i];
                    if (i >= w.seen.Count) { sr.color = Color.clear; continue; }
                    sr.transform.position = World(w.seen[i]);
                    float fade = 1f - i / (float)(VisionRange + 1) * 0.6f;
                    var c = blind
                        ? new Color(0.5f, 0.5f, 0.55f, 0.12f)
                        : Color.Lerp(new Color(1f, 0.86f, 0.42f, 0.24f), new Color(1f, 0.25f, 0.15f, 0.42f), w.suspicion);
                    c.a *= fade;
                    sr.color = c;
                }
            }
        }

        /// <summary>
        /// A watcher saw the character: it cries out and a Rôdeur crawls out where it stands.
        /// If the maze is already full, every hunter goes straight for the character instead.
        /// </summary>
        void RaiseAlarm(Watcher w)
        {
            w.suspicion = 0f;
            w.blindUntil = Time.time + WatcherBlindTime;
            w.pause = 1.2f;
            spottedThisFloor = true;

            var at = World(w.m.Pos);
            Fx.Text(at + Vector3.up * 0.9f, "!", new Color(1f, 0.3f, 0.2f), 1.8f);
            Fx.Shake(0.25f, 0.25f);
            Sfx.Attack();

            if (hunters.Count < MaxHunters)
            {
                var h = AddHunter(true, w.m.Tile, 0.8f);
                h.brain = 0;
                ui.ShowBanner("REPÉRÉ !", "Un rôdeur est lâché", 1.3f);
            }
            else
            {
                foreach (var h in hunters) { h.releaseAt = Mathf.Min(h.releaseAt, Time.time); h.searching = false; }
                ui.ShowBanner("REPÉRÉ !", "Ils savent où tu es", 1.3f);
            }
        }

        // ---- hunters ------------------------------------------------------------------

        void UpdateHunters(float dt)
        {
            var playerWorld = (Vector2)World(player.Pos);
            foreach (var h in hunters)
            {
                h.bob += dt * 6f;
                if (!h.released && Time.time >= h.releaseAt)
                {
                    h.released = true;
                    Fx.Burst(World(h.m.Pos), new Color(0.4f, 0.1f, 0.3f), 10, 2.5f, 0.08f, 0f);
                }

                bool stunned = Time.time < h.stunUntil;
                if (h.released && !stunned)
                {
                    float speed = HunterSpeed * (h.rodeur ? 1.05f : 1f);
                    Advance(h.m, speed * dt, () => ChooseHunterDir(h));
                }
                if (stunned) KenneyProps.SetFlash(h.renderers, 0.4f + Mathf.Sin(Time.time * 20f) * 0.3f);
                else if (h.flashing) KenneyProps.SetFlash(h.renderers, 0f);
                h.flashing = stunned;

                var pos = World(h.m.Pos) + Vector3.up * (h.released ? Mathf.Abs(Mathf.Sin(h.bob)) * 0.05f : 0f);
                h.go.transform.position = pos + MonsterDepth;

                if (!h.released || stunned || Time.time < invulnerableUntil) continue;
                if (((Vector2)pos - playerWorld).sqrMagnitude < ContactRadius * ContactRadius)
                {
                    Caught();
                    return;
                }
            }
        }

        /// <summary>
        /// A hunter's choice at a junction: the step that shortens the maze distance to its
        /// target. The first goes straight for the character, the second cuts them off a few
        /// tiles ahead, the third hesitates now and then - three behaviours, like the
        /// original ghosts, so they spread out instead of queuing in one line. Never back the
        /// way it came unless it is a dead end.
        /// </summary>
        Vector2Int ChooseHunterDir(Hunter h)
        {
            var at = h.m.from;
            options.Clear();
            foreach (var d in Dirs)
                if (d != -h.m.lastDir && Open(at + d)) options.Add(d);
            if (options.Count == 0) return -h.m.lastDir;

            var playerTile = player.Tile;
            Vector2Int target;
            if (!hidden)
            {
                h.searching = false;
                target = playerTile;
                int manhattan = Mathf.Abs(at.x - playerTile.x) + Mathf.Abs(at.y - playerTile.y);
                if (h.brain == 1 && manhattan > 3) target = AheadOfPlayer(3);
                if (h.brain == 2 && Random.value < 0.18f) return options[Random.Range(0, options.Count)];
            }
            else
            {
                // Lost them: go to where they were last seen, then search at random.
                if (at == lastKnown) h.searching = true;
                if (h.searching) return options[Random.Range(0, options.Count)];
                target = lastKnown;
            }

            var dist = Distances(target);
            var best = options[0];
            int bestD = int.MaxValue;
            foreach (var d in options)
            {
                var n = at + d;
                int v = dist[n.x, n.y];
                if (v < 0) v = 9999;
                if (v < bestD) { bestD = v; best = d; }
            }
            return best;
        }

        Vector2Int AheadOfPlayer(int tiles)
        {
            var t = player.Tile;
            var dir = player.dir != Vector2Int.zero ? player.dir : player.lastDir;
            for (int i = 0; i < tiles && Open(t + dir); i++) t += dir;
            return t;
        }

        void Caught()
        {
            lives--;
            Sfx.Hit();
            Fx.Hitstop();
            Fx.Shake(0.45f, 0.3f);
            Fx.Burst(playerGo.position, new Color(0.85f, 0.2f, 0.15f), 16, 3f, 0.11f);
            RefreshHud();
            if (lives <= 0) { GameOver("DÉVORÉ"); return; }

            // Back to the camp; the hunters go back to their nest and come out again one by one.
            player.Place(Camp);
            player.lastDir = Vector2Int.right;
            want = Vector2Int.zero;
            lastKnown = Camp;
            invulnerableUntil = Time.time + 1.6f;
            for (int i = 0; i < hunters.Count; i++)
            {
                var h = hunters[i];
                h.m.Place(NestSlot(i));
                h.m.lastDir = Vector2Int.left;
                h.released = false;
                h.searching = false;
                h.releaseAt = Time.time + 2f + i * 3f;
            }
            PlacePlayerVisual();
            ui.ShowBanner("ATTRAPÉ", lives == 1 ? "Plus qu'un cœur" : $"Encore {lives} cœurs", 1.3f);
        }

        // ---- reserves, camp, end ------------------------------------------------------

        void UpdateReserves(float dt)
        {
            reserve -= ReserveDrain * dt;
            if (reserve <= 0f)
            {
                reserve = 45f;
                lives--;
                Sfx.Hit();
                Fx.Text(playerGo.position + Vector3.up * 0.8f, "ÉPUISÉ", new Color(1f, 0.5f, 0.4f), 1f);
                RefreshHud();
                if (lives <= 0) { GameOver("ÉPUISÉ"); return; }
            }
            reserveBar.fillAmount = reserve / ReserveMax;
            reserveBar.color = reserve < 25f
                ? Color.Lerp(new Color(0.9f, 0.3f, 0.2f), new Color(1f, 0.6f, 0.3f), Mathf.PingPong(Time.time * 3f, 1f))
                : new Color(0.35f, 0.68f, 0.92f);
        }

        void UpdateCamp()
        {
            bool ready = rationsTaken >= rationsTotal;
            if (campGlow != null)
            {
                float pulse = ready ? 0.45f + Mathf.Sin(Time.time * 6f) * 0.2f : 0.22f + Mathf.Sin(Time.time * 2f) * 0.04f;
                campGlow.color = new Color(1f, ready ? 0.8f : 0.6f, 0.3f, pulse);
            }
            if (!ready || player.Tile != Camp) return;
            FloorCleared();
        }

        void FloorCleared()
        {
            floorsCleared++;
            int bonus = 50 + floor * 25;
            score += bonus;
            string subtitle = $"+{bonus} pts";
            if (!spottedThisFloor)
            {
                silentFloors++;
                score += 40 * floor;
                subtitle += $"  ·  discrétion +{40 * floor}";
            }
            Sfx.Milestone();
            Fx.Burst(playerGo.position, ApogeeTheme.Gold, 30, 4.5f, 0.12f, 0.3f);
            NextFloor();
            ui.ShowBanner($"ÉTAGE {floor}", subtitle, 2f);
        }

        void GameOver(string reason)
        {
            playing = false;
            int coins = score / 15;
            int materials = floorsCleared + silentFloors;
            if (coins > 0) SaveSystem.AddCoins(coins);
            if (materials > 0) SaveSystem.AddMaterials(materials);
            bool record = score > SaveSystem.LabyrinthBest;
            if (record) SaveSystem.LabyrinthBest = score;
            if (floor > SaveSystem.LabyrinthBestFloor) SaveSystem.LabyrinthBestFloor = floor;

            overTitle.text = reason;
            overScoreText.text = record
                ? $"Nouveau record : {score} pts (étage {floor})"
                : $"Score : {score} pts — étage {floor}";
            overRewardText.text = coins > 0 || materials > 0
                ? (coins > 0 ? $"{coins} [c]" : "") + (materials > 0 ? $"      {materials} [g]" : "")
                : "Aucun gain cette fois";
            overPanel.SetActive(true);
            Sfx.Death();
            Fx.Shake(0.6f, 0.4f);
            AdService.OnPlayerDeath();
        }

        void RefreshHud()
        {
            scoreText.text = score.ToString();
            bestText.text = $"Record : {Mathf.Max(SaveSystem.LabyrinthBest, score)}";
            floorText.text = $"ÉTAGE {floor}";
            rationText.text = rationsTaken >= rationsTotal ? "Retour au camp !" : $"Rations {rationsTaken} / {rationsTotal}";
            for (int i = 0; i < hearts.Count; i++) hearts[i].gameObject.SetActive(i < lives);
        }

        void OnDestroy()
        {
            if (mazeTexture != null) Destroy(mazeTexture);
        }
    }

    /// <summary>
    /// Reads swipes anywhere on its rectangle and reports them as one of four directions.
    /// A long drag counts again every time it travels far enough, so a player can steer
    /// through several corners without lifting their thumb.
    /// </summary>
    public class SwipePad : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public Action<Vector2Int> onSwipe;
        Vector2 anchor;
        int pointer = int.MinValue;

        float Threshold => Mathf.Max(18f, Mathf.Min(Screen.width, Screen.height) * 0.035f);

        public void OnPointerDown(PointerEventData e)
        {
            if (pointer != int.MinValue) return;
            pointer = e.pointerId;
            anchor = e.position;
        }

        public void OnDrag(PointerEventData e)
        {
            if (e.pointerId != pointer) return;
            var delta = e.position - anchor;
            if (delta.magnitude < Threshold) return;
            var dir = Mathf.Abs(delta.x) > Mathf.Abs(delta.y)
                ? (delta.x > 0f ? Vector2Int.right : Vector2Int.left)
                : (delta.y > 0f ? Vector2Int.up : Vector2Int.down);
            onSwipe?.Invoke(dir);
            anchor = e.position;
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (e.pointerId == pointer) pointer = int.MinValue;
        }

        void OnDisable() => pointer = int.MinValue;
    }
}
