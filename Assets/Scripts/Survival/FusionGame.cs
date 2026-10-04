using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Platformer.Survival
{
    /// <summary>
    /// "FUSION": a Suika-Game-style mini-game in the Apogée world. Remedies drop into a bin;
    /// two identical ones touching fuse into the next: pill, capsule, tablet, plaster,
    /// syrup, syringe, vial, flask, élixir - and two élixirs make a healing kit, which leaves the bin and
    /// goes into the player's stock (three at most). A kit brings the player back into a
    /// Runner run where they fell. Letting the pile rest above the red line for more than a
    /// second ends the game, and the score converts into coins for the shared wallet.
    ///
    /// The bin is a glass beaker in an alchemist's laboratory: a tiled wall and shelves of
    /// flasks behind, a bench under it, a bubbling liquid at the bottom, graduations and
    /// a red MAX mark on the glass, and each remedy falls from a pipette.
    ///
    /// Remedies are drawn placeholders until painted ones are dropped into
    /// Assets/Resources/Fusion/Remedes/ as tier0 .. tier8 (pill .. élixir).
    ///
    /// The bin lives in world space far from the runner (around x = 3000) and borrows
    /// the main camera while active (see MiniGame.TakeOverCamera).
    /// </summary>
    public class FusionGame : MiniGame
    {
        public override string Id => "fusion";
        public override string Title => "FUSION";
        public override string Description => "Le laboratoire : fusionne les remèdes jusqu'au kit de soin";
        public override string BestLine => SaveSystem.FusionBest > 0 ? $"Record : {SaveSystem.FusionBest} pts" : "Aucun record";

        public const int TierCount = 11;
        public static readonly float[] Radii = { 0.24f, 0.30f, 0.37f, 0.45f, 0.54f, 0.64f, 0.75f, 0.88f, 1.02f, 1.18f, 1.36f };
        public static readonly string[] Names = { "Pilule", "Gélule", "Comprimé", "Pansement", "Sirop", "Seringue", "Fiole", "Flacon", "Élixir", "Kit de soin", "Planète" };
        /// <summary>
        /// Fusing two of the tier below (two élixirs, the biggest pieces the beaker holds) makes
        /// a healing kit: it leaves the beaker for the player's stock. Nine fusions deep - a
        /// real goal, not a by-product.
        /// </summary>
        public const int KitTier = 9;
        public static readonly int[] MergeScores = { 0, 2, 4, 8, 12, 18, 26, 36, 48, 62, 80 };
        static readonly Color[] Colors =
        {
            new Color(0.95f, 0.38f, 0.34f), // pilule (red)
            new Color(0.45f, 0.62f, 0.96f), // gélule (blue)
            new Color(0.96f, 0.94f, 0.88f), // comprimé (white)
            new Color(0.93f, 0.74f, 0.55f), // pansement (beige)
            new Color(0.72f, 0.30f, 0.58f), // sirop (purple)
            new Color(0.55f, 0.86f, 0.92f), // seringue (cyan)
            new Color(0.42f, 0.86f, 0.42f), // fiole (green)
            new Color(0.96f, 0.62f, 0.22f), // flacon (amber)
            new Color(0.78f, 0.50f, 0.98f), // élixir (glowing violet)
            new Color(0.90f, 0.20f, 0.18f), // kit de soin (red cross)
            new Color(0.98f, 0.55f, 0.26f), // planète (the orange giant of the key art)
        };

        const int MaxDropTier = 5; // only the five smallest tiers ever drop
        static readonly Vector2 Origin = new Vector2(3000f, 3000f);
        const float HalfWidth = 2.6f;
        const float FloorY = -3.8f;
        const float TopY = 4.4f;
        const float DropY = 3.6f;
        const float LoseLineY = 2.7f;
        const float WallThickness = 0.3f;

        Transform root;
        readonly List<FusionPiece> pieces = new();
        FusionPiece held;
        int pieceSerial;
        int nextTier;
        int score;
        int kitsMade;
        bool playing;
        float overflowTimer;
        float dropCooldown;
        bool pressActive, pressStartedOverUi;
        SpriteRenderer loseLine;

        Text scoreText, bestText, nextText;
        IconText kitHint;
        Image nextPreview;
        GameObject overPanel;
        Text overScoreText;
        IconText overRewardText;

        // ---- UI ------------------------------------------------------------------------

        protected override void BuildUi()
        {
            var rt = UiKit.CreateRect("FusionPanel", ui.Canvas.transform, Vector2.zero, Vector2.one);
            panel = rt.gameObject;

            var topBar = UiKit.CreateRect("TopBar", rt, new Vector2(0f, 0.90f), new Vector2(1f, 1f));
            var topImg = topBar.gameObject.AddComponent<Image>();
            topImg.sprite = ApogeeTheme.Panel;
            topImg.type = Image.Type.Sliced;
            scoreText = UiKit.CreateText("Score", topBar, "0", 44, TextAnchor.MiddleLeft, new Vector2(0.05f, 0f), new Vector2(0.5f, 1f), Color.white);
            bestText = UiKit.CreateText("Best", topBar, "", 24, TextAnchor.LowerLeft, new Vector2(0.05f, 0.05f), new Vector2(0.5f, 0.35f), UiKit.Gold);
            nextText = UiKit.CreateText("NextLabel", topBar, "Suivant", 22, TextAnchor.MiddleRight, new Vector2(0.5f, 0f), new Vector2(0.76f, 1f), UiKit.Parchment);
            nextPreview = UiKit.CreateImage("NextPreview", topBar, new Vector2(0.78f, 0.15f), new Vector2(0.95f, 0.85f), PlaceholderVisuals.Circle(Color.white), Color.white);

            UiKit.CreateButton("Quit", rt, "QUITTER", new Vector2(0.72f, 0.845f), new Vector2(0.96f, 0.89f), ReturnToHub, 22);
            kitHint = IconText.Create("KitHint", rt, "", 22, TextAnchor.MiddleCenter,
                new Vector2(0.05f, 0.02f), new Vector2(0.95f, 0.065f), UiKit.TextDim);

            var overRt = UiKit.CreatePanel("FusionOver", rt, UiKit.Overlay);
            UiKit.CreateFrame("FusionOverFrame", overRt, new Vector2(0.08f, 0.24f), new Vector2(0.92f, 0.8f));
            overPanel = overRt.gameObject;
            UiKit.Outlined(UiKit.CreateText("OverTitle", overRt, "ÇA DÉBORDE !", 54, TextAnchor.MiddleCenter, new Vector2(0.05f, 0.66f), new Vector2(0.95f, 0.78f), ApogeeTheme.Gold), 2.5f);
            overScoreText = UiKit.CreateText("OverScore", overRt, "", 36, TextAnchor.MiddleCenter, new Vector2(0.1f, 0.58f), new Vector2(0.9f, 0.65f), UiKit.Parchment);
            overRewardText = IconText.Create("OverReward", overRt, "", 34, TextAnchor.MiddleCenter, new Vector2(0.1f, 0.51f), new Vector2(0.9f, 0.58f), PlaceholderVisuals.CoinColor);
            UiKit.CreateButton("Retry", overRt, "REJOUER", new Vector2(0.25f, 0.38f), new Vector2(0.75f, 0.45f), ResetGame);
            UiKit.CreateButton("Menu", overRt, "MENU", new Vector2(0.25f, 0.29f), new Vector2(0.75f, 0.36f), ReturnToHub);
            overPanel.SetActive(false);
        }

        // ---- lifecycle -----------------------------------------------------------------

        protected override void OnEnter()
        {
            BuildWorld();
            // The bin (walls included) plus a small margin must always be on screen.
            const float binWidth = (HalfWidth + WallThickness) * 2f + 0.5f;
            TakeOverCamera(new Vector3(Origin.x, Origin.y + 0.3f, -10f), 5.3f, new Color(0.16f, 0.08f, 0.06f), binWidth);
            ResetGame();
        }

        protected override void OnExit()
        {
            playing = false;
            StopAllCoroutines();
            if (root != null) Destroy(root.gameObject);
            root = null;
            pieces.Clear();
            held = null;
        }

        // ---- the laboratory -------------------------------------------------------------

        static readonly Color GlassColor = new Color(0.78f, 0.93f, 0.95f, 0.30f);
        static readonly Color GlassEdge = new Color(0.86f, 0.97f, 1f, 0.75f);
        static readonly Color LiquidColor = new Color(0.36f, 0.86f, 0.55f, 0.32f);
        static readonly Color WoodColor = new Color(0.42f, 0.24f, 0.14f);
        static readonly Color WoodDark = new Color(0.26f, 0.14f, 0.08f);
        static readonly Color BrassColor = new Color(0.86f, 0.64f, 0.30f);

        Transform pipette, pipetteBulb;
        readonly List<Transform> bubbles = new();
        float pipetteSqueeze;

        void BuildWorld()
        {
            root = new GameObject("FusionWorld").transform;

            // The laboratory wall: warm tiles from edge to edge of any screen.
            var wall = new GameObject("LabWall");
            wall.transform.SetParent(root, false);
            wall.transform.position = Origin;
            var wsr = wall.AddComponent<SpriteRenderer>();
            wsr.sprite = LabTile;
            wsr.drawMode = SpriteDrawMode.Tiled;
            wsr.size = new Vector2(40f, 30f);
            wsr.sortingOrder = -10;

            // Shelves of jars on both sides (a wide screen sees them; a phone sees the beaker).
            BuildShelf(new Vector2(-HalfWidth - 2.6f, 2.2f), 3f);
            BuildShelf(new Vector2(-HalfWidth - 2.6f, -0.6f), 3f);
            BuildShelf(new Vector2(HalfWidth + 2.6f, 1.4f), 3f);
            BuildShelf(new Vector2(HalfWidth + 2.6f, -1.4f), 3f);

            // The bench the beaker stands on.
            CreateBox("Bench", new Vector2(0f, FloorY - 0.85f), new Vector2(40f, 1.1f), WoodColor, -6, false);
            CreateBox("BenchEdge", new Vector2(0f, FloorY - 0.32f), new Vector2(40f, 0.12f), new Color(0.6f, 0.38f, 0.22f), -5, false);
            CreateBox("BenchShade", new Vector2(0f, FloorY - 3.4f), new Vector2(40f, 4f), WoodDark, -6, false);

            // The beaker: glass walls and floor (solid), the liquid inside, its lip and marks.
            CreateBox("Glass", new Vector2(0f, (FloorY + TopY) / 2f), new Vector2(HalfWidth * 2f, TopY - FloorY), new Color(0.75f, 0.9f, 0.95f, 0.10f), -4, false);
            CreateBox("Liquid", new Vector2(0f, FloorY + 0.35f), new Vector2(HalfWidth * 2f, 0.7f), LiquidColor, 1, false);
            CreateBox("LiquidTop", new Vector2(0f, FloorY + 0.71f), new Vector2(HalfWidth * 2f, 0.04f), new Color(0.7f, 1f, 0.8f, 0.55f), 1, false);
            CreateBox("Floor", new Vector2(0f, FloorY - WallThickness / 2f), new Vector2(HalfWidth * 2f + WallThickness * 2f, WallThickness), GlassEdge, 3, true);
            CreateBox("LeftWall", new Vector2(-HalfWidth - WallThickness / 2f, (FloorY + TopY) / 2f), new Vector2(WallThickness, TopY - FloorY + WallThickness), GlassColor, 3, true);
            CreateBox("RightWall", new Vector2(HalfWidth + WallThickness / 2f, (FloorY + TopY) / 2f), new Vector2(WallThickness, TopY - FloorY + WallThickness), GlassColor, 3, true);
            // A bright edge and a long highlight, so the walls read as glass.
            CreateBox("LeftEdge", new Vector2(-HalfWidth - WallThickness + 0.03f, (FloorY + TopY) / 2f), new Vector2(0.05f, TopY - FloorY), GlassEdge, 4, false);
            CreateBox("RightEdge", new Vector2(HalfWidth + WallThickness - 0.03f, (FloorY + TopY) / 2f), new Vector2(0.05f, TopY - FloorY), GlassEdge, 4, false);
            CreateBox("Shine", new Vector2(-HalfWidth + 0.35f, (FloorY + TopY) / 2f + 0.6f), new Vector2(0.09f, (TopY - FloorY) * 0.7f), new Color(1f, 1f, 1f, 0.16f), 4, false);
            // Lip: the rim flares out a little at the top.
            CreateBox("LipL", new Vector2(-HalfWidth - WallThickness, TopY + 0.05f), new Vector2(0.55f, 0.12f), GlassEdge, 4, false);
            CreateBox("LipR", new Vector2(HalfWidth + WallThickness, TopY + 0.05f), new Vector2(0.55f, 0.12f), GlassEdge, 4, false);
            // Graduations up the left side, a longer one every metre.
            for (int i = 1; FloorY + i * 0.5f < TopY - 0.2f; i++)
            {
                bool major = i % 2 == 0;
                CreateBox("Mark", new Vector2(-HalfWidth + (major ? 0.22f : 0.14f), FloorY + i * 0.5f), new Vector2(major ? 0.42f : 0.26f, 0.03f), new Color(1f, 1f, 1f, 0.35f), 4, false);
            }

            // The MAX mark: a red line with a tab on each side of the glass.
            var line = CreateBox("LoseLine", new Vector2(0f, LoseLineY), new Vector2(HalfWidth * 2f, 0.05f), new Color(0.9f, 0.2f, 0.15f, 0.55f), 4, false);
            loseLine = line.GetComponent<SpriteRenderer>();
            CreateBox("MaxTabL", new Vector2(-HalfWidth - WallThickness - 0.2f, LoseLineY), new Vector2(0.4f, 0.16f), new Color(0.9f, 0.2f, 0.15f), 4, false);
            CreateBox("MaxTabR", new Vector2(HalfWidth + WallThickness + 0.2f, LoseLineY), new Vector2(0.4f, 0.16f), new Color(0.9f, 0.2f, 0.15f), 4, false);

            // Bubbles rising through the liquid.
            bubbles.Clear();
            for (int i = 0; i < 9; i++)
            {
                var b = CreateBox("Bubble", new Vector2(Random.Range(-HalfWidth + 0.2f, HalfWidth - 0.2f), FloorY + Random.Range(0.05f, 0.65f)),
                    Vector2.one * Random.Range(0.06f, 0.12f), new Color(0.85f, 1f, 0.9f, 0.6f), 1, false);
                b.GetComponent<SpriteRenderer>().sprite = PlaceholderVisuals.Circle(Color.white);
                bubbles.Add(b.transform);
            }

            // The pipette the remedies fall from: a glass tube and a red rubber bulb.
            pipette = new GameObject("Pipette").transform;
            pipette.SetParent(root, false);
            Part(pipette, PlaceholderVisuals.Square(Color.white), new Color(0.85f, 0.97f, 1f, 0.55f), new Vector2(0f, 0.45f), new Vector2(0.22f, 0.9f), 5);
            Part(pipette, PlaceholderVisuals.Square(Color.white), new Color(1f, 1f, 1f, 0.7f), new Vector2(-0.06f, 0.45f), new Vector2(0.04f, 0.8f), 6);
            Part(pipette, PlaceholderVisuals.Square(Color.white), BrassColor, new Vector2(0f, 0.95f), new Vector2(0.3f, 0.1f), 6);
            pipetteBulb = Part(pipette, PlaceholderVisuals.Circle(Color.white), new Color(0.82f, 0.18f, 0.14f), new Vector2(0f, 1.22f), new Vector2(0.5f, 0.5f), 6);
        }

        void BuildShelf(Vector2 at, float width)
        {
            CreateBox("Shelf", at, new Vector2(width, 0.14f), WoodColor, -8, false);
            CreateBox("ShelfShade", at + new Vector2(0f, -0.1f), new Vector2(width, 0.06f), WoodDark, -8, false);
            // Jars of the remedies themselves, small, on the plank.
            int n = Mathf.Max(2, Mathf.FloorToInt(width / 0.8f));
            for (int i = 0; i < n; i++)
            {
                int tier = Random.Range(0, KitTier);
                float size = Random.Range(0.42f, 0.6f);
                var jar = CreateBox("Jar", at + new Vector2(-width / 2f + (i + 0.5f) * width / n, 0.07f + size / 2f), Vector2.one, Color.white, -7, false);
                var sr = jar.GetComponent<SpriteRenderer>();
                sr.sprite = PieceSprite(tier);
                sr.color = new Color(0.85f, 0.85f, 0.85f, 0.9f);
                jar.transform.localScale = Vector3.one * ScaleFor(sr.sprite, size);
            }
        }

        Transform Part(Transform parent, Sprite sprite, Color color, Vector2 local, Vector2 size, int order)
        {
            var go = new GameObject("Part");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = order;
            return go.transform;
        }

        static Sprite labTile;

        /// <summary>One wall tile with its grout, repeated across the laboratory wall.</summary>
        static Sprite LabTile
        {
            get
            {
                if (labTile != null) return labTile;
                const int n = 32;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat };
                var px = new Color[n * n];
                var tile = new Color(0.30f, 0.16f, 0.13f);
                var grout = new Color(0.18f, 0.09f, 0.07f);
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        bool g = x < 2 || y < 2;
                        float shade = 1f + ((x * 7 + y * 13) % 5) * 0.012f - (y / (float)n) * 0.06f;
                        px[y * n + x] = g ? grout : tile * shade;
                    }
                tex.SetPixels(px);
                tex.Apply();
                labTile = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n / 0.9f, 0, SpriteMeshType.FullRect);
                return labTile;
            }
        }

        /// <summary>The pipette follows the remedy it holds and squeezes on a drop; bubbles rise.</summary>
        void UpdateLab()
        {
            if (pipette != null)
            {
                float x = held != null ? held.transform.position.x : pipette.position.x;
                float r = held != null ? Radii[held.tier] : 0.3f;
                pipette.position = new Vector3(x, Origin.y + DropY + r + 0.02f, 0f);
                pipetteSqueeze = Mathf.MoveTowards(pipetteSqueeze, 0f, Time.deltaTime * 4f);
                if (pipetteBulb != null)
                    pipetteBulb.localScale = new Vector3(0.5f + pipetteSqueeze * 0.18f, 0.5f - pipetteSqueeze * 0.2f, 1f);
            }
            foreach (var b in bubbles)
            {
                if (b == null) continue;
                var p = b.position;
                p.y += Time.deltaTime * 0.45f;
                p.x += Mathf.Sin(Time.time * 3f + p.y * 6f) * 0.002f;
                if (p.y > Origin.y + FloorY + 0.68f)
                {
                    p.y = Origin.y + FloorY + 0.04f;
                    p.x = Origin.x + Random.Range(-HalfWidth + 0.2f, HalfWidth - 0.2f);
                }
                b.position = p;
            }
        }

        GameObject CreateBox(string name, Vector2 localPos, Vector2 size, Color color, int order, bool solid)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.position = Origin + localPos;
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = PlaceholderVisuals.Square(Color.white);
            sr.color = color;
            sr.sortingOrder = order;
            if (solid) go.AddComponent<BoxCollider2D>();
            return go;
        }

        void ResetGame()
        {
            kitsMade = 0;
            RefreshKitHint();
            StopAllCoroutines();
            foreach (var p in pieces) if (p != null) Destroy(p.gameObject);
            pieces.Clear();
            held = null;
            score = 0;
            overflowTimer = 0f;
            dropCooldown = 0f;
            pressActive = false;
            playing = true;
            overPanel.SetActive(false);
            nextTier = Random.Range(0, MaxDropTier);
            SpawnHeld();
            RefreshScore();
        }

        // ---- pieces --------------------------------------------------------------------

        void SpawnHeld()
        {
            if (!playing) return;
            held = CreatePiece(nextTier, Origin + new Vector2(0f, DropY), heldPiece: true);
            nextTier = Random.Range(0, MaxDropTier);
            RefreshNextPreview();
        }

        /// <summary>
        /// World scale that makes a sprite exactly `diameter` wide, whatever its pixels-per-unit
        /// (the generated circles are 1 unit; imported art in Resources/Fusion is usually 100 PPU).
        /// </summary>
        static float ScaleFor(Sprite sprite, float diameter)
        {
            float unit = sprite != null ? Mathf.Max(0.0001f, sprite.bounds.size.x) : 1f;
            return diameter / unit;
        }

        FusionPiece CreatePiece(int tier, Vector2 position, bool heldPiece)
        {
            float radius = Radii[tier];
            var sprite = PieceSprite(tier);
            float unit = sprite != null ? Mathf.Max(0.0001f, sprite.bounds.size.x) : 1f;
            var go = new GameObject($"Piece_{Names[tier]}");
            go.transform.SetParent(root, false);
            go.transform.position = position;
            go.transform.localScale = Vector3.one * ScaleFor(sprite, radius * 2f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = Color.white;
            sr.sortingOrder = 2;

            var col = go.AddComponent<CircleCollider2D>();
            col.radius = 0.5f * unit;   // half the sprite, so the world radius stays Radii[tier]
            col.enabled = !heldPiece;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = heldPiece ? RigidbodyType2D.Kinematic : RigidbodyType2D.Dynamic;
            rb.gravityScale = 2.2f;
            rb.mass = 0.5f + tier * 0.35f;
            rb.linearDamping = 0.2f;
            rb.angularDamping = 0.6f;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            var piece = go.AddComponent<FusionPiece>();
            piece.tier = tier;
            piece.game = this;
            piece.rb = rb;
            piece.spawnTime = Time.time;
            piece.serial = ++pieceSerial;
            pieces.Add(piece);
            return piece;
        }

        static readonly Dictionary<int, Sprite> customSprites = new();

        /// <summary>
        /// The remedy's picture: painted art dropped into Resources/Fusion/Remedes/tierN
        /// (pill = tier0 .. vial = tier6) when it exists, else a drawn placeholder.
        /// </summary>
        static Sprite PieceSprite(int tier)
        {
            if (!customSprites.TryGetValue(tier, out var sprite))
            {
                sprite = Resources.Load<Sprite>($"Fusion/Remedes/tier{tier}");
                customSprites[tier] = sprite;
            }
            return sprite != null ? sprite : GameIcons.Remedy(tier, Colors[tier]);
        }

        void Drop()
        {
            if (held == null) return;
            var piece = held;
            held = null;
            piece.GetComponent<CircleCollider2D>().enabled = true;
            piece.rb.bodyType = RigidbodyType2D.Dynamic;
            piece.dropped = true;
            piece.spawnTime = Time.time;
            Sfx.Drop();
            pipetteSqueeze = 1f;
            dropCooldown = 0.5f;
            StartCoroutine(SpawnHeldAfter(0.5f));
        }

        IEnumerator SpawnHeldAfter(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (playing && held == null) SpawnHeld();
        }

        /// <summary>Called by both pieces of a collision; only the lower-id one acts so a merge happens once.</summary>
        public void TryMerge(FusionPiece a, FusionPiece b)
        {
            if (!playing || a == null || b == null) return;
            if (a.merged || b.merged || a.tier != b.tier) return;
            if (!a.dropped || !b.dropped) return;
            if (a.serial > b.serial) return;

            a.merged = true;
            b.merged = true;
            Vector2 mid = (a.transform.position + b.transform.position) / 2f;
            int tier = a.tier;
            pieces.Remove(a);
            pieces.Remove(b);
            Destroy(a.gameObject);
            Destroy(b.gameObject);

            if (tier + 1 == KitTier)
            {
                BankKit(mid);
            }
            else if (tier + 1 < TierCount)
            {
                var merged = CreatePiece(tier + 1, mid, heldPiece: false);
                merged.dropped = true;
                score += MergeScores[tier + 1];
                Sfx.Merge(tier);
                Fx.Burst(mid, Colors[tier + 1], 8 + tier * 2, 2.5f, 0.1f, 0.5f);
                Fx.Text(mid, $"+{MergeScores[tier + 1]}", UiKit.Gold, 0.9f);
                StartCoroutine(Pop(merged.transform, ScaleFor(PieceSprite(tier + 1), Radii[tier + 1] * 2f)));
            }
            else
            {
                score += 150; // two Météores annihilate each other
                Sfx.Milestone();
                Fx.Burst(mid, Colors[TierCount - 1], 40, 5f, 0.16f, 0.4f);
                Fx.Text(mid, "+150", UiKit.Gold, 1.3f);
            }
            RefreshScore();
        }

        /// <summary>Squash-and-stretch pop when two pieces merge; finalScale is a transform scale.</summary>
        IEnumerator Pop(Transform target, float finalScale)
        {
            const float duration = 0.18f;
            float t = 0f;
            while (t < duration && target != null)
            {
                t += Time.deltaTime;
                float p = t / duration;
                float s = finalScale * (0.6f + 0.55f * Mathf.Sin(p * Mathf.PI * 0.5f) + 0.15f * Mathf.Sin(p * Mathf.PI));
                target.localScale = Vector3.one * s;
                yield return null;
            }
            if (target != null) target.localScale = Vector3.one * finalScale;
        }

        // ---- per-frame -----------------------------------------------------------------

        void Update()
        {
            if (!IsActive) return;
            UpdateLab();
            if (!playing) return;
            dropCooldown -= Time.deltaTime;
            HandleInput();
            CheckOverflow();
        }

        void HandleInput()
        {
            var pointer = Pointer.current;
            if (pointer == null || cam == null) return;

            Vector2 screen = pointer.position.ReadValue();
            if (pointer.press.wasPressedThisFrame)
            {
                pressActive = true;
                pressStartedOverUi = IsPointerOverUi();
            }

            if (held != null && (pressActive || Mouse.current != null))
            {
                Vector3 world = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 10f));
                float r = Radii[held.tier];
                float x = Mathf.Clamp(world.x, Origin.x - HalfWidth + r + 0.02f, Origin.x + HalfWidth - r - 0.02f);
                held.transform.position = new Vector3(x, Origin.y + DropY, 0f);
            }

            if (pointer.press.wasReleasedThisFrame && pressActive)
            {
                pressActive = false;
                if (!pressStartedOverUi && dropCooldown <= 0f && held != null) Drop();
            }
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

        void CheckOverflow()
        {
            bool overflowing = false;
            for (int i = 0; i < pieces.Count; i++)
            {
                var p = pieces[i];
                if (p == null || p == held || !p.dropped) continue;
                if (Time.time - p.spawnTime < 1.2f) continue;
                float top = p.transform.position.y + Radii[p.tier];
                if (top > Origin.y + LoseLineY && p.rb.linearVelocity.magnitude < 0.6f)
                {
                    overflowing = true;
                    break;
                }
            }

            overflowTimer = overflowing ? overflowTimer + Time.deltaTime : 0f;
            if (loseLine != null)
            {
                var c = loseLine.color;
                c.a = overflowing ? 0.5f + Mathf.Abs(Mathf.Sin(Time.time * 12f)) * 0.5f : 0.55f;
                loseLine.color = c;
            }
            if (overflowTimer > 1.2f) GameOver();
        }

        /// <summary>Two vials made a healing kit: it flies out of the bin into the stock.</summary>
        void BankKit(Vector2 at)
        {
            score += MergeScores[KitTier];
            kitsMade++;
            Sfx.Heal();
            Fx.Burst(at, Colors[KitTier], 36, 5f, 0.14f, 0.2f);
            Fx.Burst(at, Color.white, 16, 3f, 0.1f, 0f);
            if (SaveSystem.TryAddReviveKit())
            {
                Fx.Text(at, $"KIT DE SOIN !  {SaveSystem.ReviveKits}/{SaveSystem.MaxReviveKits}", new Color(1f, 0.6f, 0.55f), 1.3f);
            }
            else
            {
                // Stock full: the kit is worth coins instead.
                const int coinsInstead = 25;
                SaveSystem.AddCoins(coinsInstead);
                Fx.Text(at, $"STOCK PLEIN  +{coinsInstead}", UiKit.Gold, 1.2f);
            }
            RefreshKitHint();
        }

        void RefreshKitHint()
        {
            if (kitHint != null)
                kitHint.text = $"Kits de soin {SaveSystem.ReviveKits}/{SaveSystem.MaxReviveKits} [k]    deux élixirs en font un";
        }

        void GameOver()
        {
            playing = false;
            if (held != null)
            {
                pieces.Remove(held);
                Destroy(held.gameObject);
                held = null;
            }

            int reward = score / 20;
            if (reward > 0) SaveSystem.AddCoins(reward);
            bool record = score > SaveSystem.FusionBest;
            if (record) SaveSystem.FusionBest = score;

            overScoreText.text = record ? $"Nouveau record : {score} pts !" : $"Score : {score} pts";
            overRewardText.text = kitsMade > 0
                ? $"{reward} [c]      {kitsMade} [k]"
                : reward > 0 ? $"{reward} [c]" : "Aucun gain cette fois";
            overPanel.SetActive(true);
            Sfx.Death();
            AdService.OnPlayerDeath();
        }

        void RefreshScore()
        {
            scoreText.text = score.ToString();
            bestText.text = $"Record : {Mathf.Max(SaveSystem.FusionBest, score)}";
        }

        void RefreshNextPreview()
        {
            nextPreview.sprite = PieceSprite(nextTier);
            nextText.text = $"Suivant : {Names[nextTier]}";
            float scale = Mathf.Lerp(0.55f, 1f, nextTier / (float)(MaxDropTier - 1));
            nextPreview.rectTransform.localScale = Vector3.one * scale;
        }
    }

    /// <summary>One piece of debris in the Fusion bin; forwards contacts to the game for merging.</summary>
    public class FusionPiece : MonoBehaviour
    {
        public int tier;
        public FusionGame game;
        public Rigidbody2D rb;
        public bool merged;
        public bool dropped;
        public int serial;
        public float spawnTime;

        void OnCollisionEnter2D(Collision2D collision)
        {
            var other = collision.collider.GetComponent<FusionPiece>();
            if (other != null && game != null) game.TryMerge(this, other);
        }
    }
}
