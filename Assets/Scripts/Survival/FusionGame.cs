using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Platformer.Survival
{
    /// <summary>
    /// "FUSION": a Suika-Game-style mini-game in the Apogée world. Celestial treasures of 11
    /// tiers drop into a bin; two identical pieces touching fuse into the next tier and
    /// score. Letting the pile rest above the red line for more than a second ends the
    /// game, and the score converts into coins for the shared wallet.
    ///
    /// Pieces render as colored circles by default. To use your own art, drop Sprites
    /// named tier0 .. tier10 into Assets/Resources/Fusion/ (Texture Type = Sprite) and
    /// they are picked up automatically, scaled to each tier's radius.
    ///
    /// The bin lives in world space far from the runner (around x = 3000) and borrows
    /// the main camera while active (see MiniGame.TakeOverCamera).
    /// </summary>
    public class FusionGame : MiniGame
    {
        // Hidden from the home screen for now.
        public override bool ShowOnHub => false;
        public override string Id => "fusion";
        public override string Title => "FUSION";
        public override string Description => "Fusionne les trésors célestes jusqu'à la planète";
        public override string BestLine => SaveSystem.FusionBest > 0 ? $"Record : {SaveSystem.FusionBest} pts" : "Aucun record";

        public const int TierCount = 11;
        public static readonly float[] Radii = { 0.24f, 0.30f, 0.37f, 0.45f, 0.54f, 0.64f, 0.75f, 0.88f, 1.02f, 1.18f, 1.36f };
        public static readonly string[] Names = { "Feuille", "Pétale", "Braise", "Gland", "Lanterne", "Plume", "Gemme", "Bouclier", "Couronne", "Île", "Planète" };
        public static readonly int[] MergeScores = { 0, 2, 4, 8, 12, 18, 26, 36, 48, 62, 80 };
        static readonly Color[] Colors =
        {
            new Color(0.84f, 0.20f, 0.12f), // feuille (crimson maple)
            new Color(0.98f, 0.62f, 0.58f), // pétale
            new Color(1.00f, 0.52f, 0.14f), // braise
            new Color(0.62f, 0.40f, 0.20f), // gland
            new Color(1.00f, 0.82f, 0.36f), // lanterne
            new Color(0.96f, 0.92f, 0.84f), // plume
            new Color(0.32f, 0.74f, 0.80f), // gemme
            new Color(0.56f, 0.58f, 0.66f), // bouclier
            new Color(0.95f, 0.70f, 0.18f), // couronne
            new Color(0.52f, 0.36f, 0.30f), // île flottante
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
        bool playing;
        float overflowTimer;
        float dropCooldown;
        bool pressActive, pressStartedOverUi;
        SpriteRenderer loseLine;

        Text scoreText, bestText, nextText;
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
            UiKit.CreateText("Hint", rt, "Glisse pour viser, relâche pour lâcher", 22, TextAnchor.MiddleCenter,
                new Vector2(0.05f, 0.02f), new Vector2(0.95f, 0.06f), UiKit.TextDim);

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
            TakeOverCamera(new Vector3(Origin.x, Origin.y + 0.3f, -10f), 5.3f, ApogeeTheme.SkyAverage, binWidth);
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

        void BuildWorld()
        {
            root = new GameObject("FusionWorld").transform;

            CreateBox("Backdrop", new Vector2(0f, (FloorY + TopY) / 2f), new Vector2(HalfWidth * 2f, TopY - FloorY), new Color(0.20f, 0.06f, 0.05f, 0.82f), -5, false);
            CreateBox("Floor", new Vector2(0f, FloorY - WallThickness / 2f), new Vector2(HalfWidth * 2f + WallThickness * 2f, WallThickness), new Color(0.46f, 0.30f, 0.25f), -1, true);
            CreateBox("LeftWall", new Vector2(-HalfWidth - WallThickness / 2f, (FloorY + TopY) / 2f), new Vector2(WallThickness, TopY - FloorY + WallThickness), new Color(0.46f, 0.30f, 0.25f), -1, true);
            CreateBox("RightWall", new Vector2(HalfWidth + WallThickness / 2f, (FloorY + TopY) / 2f), new Vector2(WallThickness, TopY - FloorY + WallThickness), new Color(0.46f, 0.30f, 0.25f), -1, true);

            var line = CreateBox("LoseLine", new Vector2(0f, LoseLineY), new Vector2(HalfWidth * 2f, 0.05f), new Color(0.8f, 0.2f, 0.15f, 0.55f), -2, false);
            loseLine = line.GetComponent<SpriteRenderer>();
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

        static Sprite PieceSprite(int tier)
        {
            if (!customSprites.TryGetValue(tier, out var sprite))
            {
                sprite = Resources.Load<Sprite>($"Fusion/tier{tier}");
                customSprites[tier] = sprite;
            }
            return sprite != null ? sprite : PlaceholderVisuals.RimCircle(Colors[tier]);
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

            if (tier + 1 < TierCount)
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
            if (!IsActive || !playing) return;
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
            overRewardText.text = reward > 0 ? $"+{reward} [c]" : "Aucun gain cette fois";
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
