using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Platformer.Survival
{
    /// <summary>
    /// "INVASION": Space Invaders in the Apogée world. The dead drift down from the sky in
    /// formation while the equipped character holds the last floating island, firing upward
    /// from behind three rock shelters that crumble shot by shot. The formation slides
    /// sideways, drops a step at each edge and speeds up as it thins out; every fifth
    /// assault a Colosse takes its place. Letting it reach the island, or losing every
    /// heart, ends the run and converts the score into coins.
    ///
    /// Everything is positional: bullets, shelters and invaders are checked against each
    /// other by distance instead of physics, which keeps hundreds of overlaps cheap and
    /// makes the movement perfectly predictable. The world sits far from the runner (around
    /// x = 6000) and borrows the main camera, like the other mini-games.
    /// </summary>
    public class InvasionGame : MiniGame
    {
        public override string Id => "invasion";
        public override string Title => "INVASION";
        public override string Description => "Repousse les spectres qui descendent du ciel";
        public override string BestLine => SaveSystem.InvasionBest > 0 ? $"Record : {SaveSystem.InvasionBest} pts" : "Aucun record";

        // ---- arena layout (world units, relative to Origin) ----
        static readonly Vector2 Origin = new Vector2(6000f, 6000f);
        const float HalfWidth = 3.9f;
        const float PlayerY = -5.0f;
        const float ShelterY = -3.4f;
        const float FormationTop = 4.4f;
        const float LoseLineY = -4.3f;
        const float CameraOrtho = 6.3f;
        const float SpacingX = 1.15f;
        const float SpacingY = 1.05f;
        const int Columns = 6;

        enum Invader { Ghost, Skeleton, Vampire, Zombie, Boss }

        class Enemy
        {
            public Invader kind;
            public int column, row;
            public float hp, maxHp;
            public int points;
            public Transform rig;
            public Renderer[] renderers;
            public SpriteRenderer fallback;
            public GameObject go;
            public float bob;
        }

        class Bullet
        {
            public GameObject go;
            public Vector2 position, velocity;
            public float damage, radius;
            public bool fromPlayer;
        }

        class ShelterBlock
        {
            public GameObject go;
            public Vector2 position;
            public float hp;
        }

        Transform root;
        Transform playerGo;
        SpriteRenderer playerSprite;

        readonly List<Enemy> enemies = new();
        readonly List<Bullet> bullets = new();
        readonly List<ShelterBlock> shelter = new();

        Vector2 formation;          // top-left anchor of the grid, in local space
        float formationDir = 1f;
        int waveCount;              // invaders spawned this wave (for the speed ramp)
        int wave, score, lives, bossesBeaten;
        float playerX, fireCooldown, enemyFireTimer, invulnerableUntil, bossShootTimer;
        bool playing;
        Enemy boss;

        Text scoreText, waveText, bestText, hintText;
        Image bossBar;
        GameObject bossBarRoot;
        readonly List<Image> hearts = new();
        GameObject overPanel;
        Text overScoreText, overRewardText;

        const float PlayerRadius = 0.42f;
        const float BulletSpeed = 13f;
        const int MaxLives = 3;

        int ShotDamage => Mathf.Max(1, Mathf.RoundToInt(UpgradeManager.FirePowerMultiplier));
        float FireInterval => Mathf.Max(0.16f, 0.36f - SaveSystem.GetLevel(UpgradeStat.Speed) * 0.012f);

        // ---- UI ------------------------------------------------------------------------

        protected override void BuildUi()
        {
            var rt = UiKit.CreateRect("InvasionPanel", ui.Canvas.transform, Vector2.zero, Vector2.one);
            panel = rt.gameObject;

            var topBar = UiKit.CreateRect("TopBar", rt, new Vector2(0f, 0.90f), new Vector2(1f, 1f));
            var topImg = topBar.gameObject.AddComponent<Image>();
            topImg.sprite = ApogeeTheme.Panel;
            topImg.type = Image.Type.Sliced;

            scoreText = UiKit.Outlined(UiKit.CreateText("Score", topBar, "0", 42, TextAnchor.MiddleLeft,
                new Vector2(0.04f, 0.42f), new Vector2(0.45f, 0.98f), ApogeeTheme.Cream));
            bestText = UiKit.CreateText("Best", topBar, "", 22, TextAnchor.LowerLeft,
                new Vector2(0.04f, 0.05f), new Vector2(0.45f, 0.42f), ApogeeTheme.Gold);
            waveText = UiKit.Outlined(UiKit.CreateText("Wave", topBar, "", 30, TextAnchor.MiddleRight,
                new Vector2(0.45f, 0.42f), new Vector2(0.72f, 0.98f), ApogeeTheme.Gold));

            for (int i = 0; i < MaxLives + 6; i++)
            {
                var heart = UiKit.CreateImage($"Heart_{i}", topBar, new Vector2(0.45f + i * 0.045f, 0.06f),
                    new Vector2(0.485f + i * 0.045f, 0.4f), PlaceholderVisuals.Circle(Color.white), new Color(0.86f, 0.2f, 0.16f));
                hearts.Add(heart);
                heart.gameObject.SetActive(false);
            }

            UiKit.CreateButton("Quit", rt, "QUITTER", new Vector2(0.74f, 0.845f), new Vector2(0.96f, 0.892f), ReturnToHub, 22);

            // Boss health bar, shown only during a Colosse assault.
            var bossRt = UiKit.CreateRect("BossBar", rt, new Vector2(0.12f, 0.855f), new Vector2(0.70f, 0.888f));
            bossBarRoot = bossRt.gameObject;
            bossBar = UiKit.CreateBar("BossFill", bossRt, Vector2.zero, Vector2.one, new Color(0.8f, 0.18f, 0.15f));
            bossBarRoot.SetActive(false);

            hintText = UiKit.Outlined(UiKit.CreateText("Hint", rt, "", 26, TextAnchor.MiddleCenter,
                new Vector2(0.05f, 0.60f), new Vector2(0.95f, 0.68f), ApogeeTheme.Gold), 2f);

            BuildControls(rt);

            var overRt = UiKit.CreatePanel("InvasionOver", rt, UiKit.Overlay);
            overPanel = overRt.gameObject;
            UiKit.CreateFrame("InvasionOverFrame", overRt, new Vector2(0.08f, 0.24f), new Vector2(0.92f, 0.8f));
            UiKit.Outlined(UiKit.CreateText("OverTitle", overRt, "L'ÎLE EST TOMBÉE", 50, TextAnchor.MiddleCenter,
                new Vector2(0.05f, 0.66f), new Vector2(0.95f, 0.78f), ApogeeTheme.Gold), 2.5f);
            overScoreText = UiKit.CreateText("OverScore", overRt, "", 34, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.58f), new Vector2(0.9f, 0.65f), ApogeeTheme.Cream);
            overRewardText = UiKit.CreateText("OverReward", overRt, "", 28, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.51f), new Vector2(0.9f, 0.58f), PlaceholderVisuals.CoinColor);
            UiKit.CreateButton("Retry", overRt, "REJOUER", new Vector2(0.25f, 0.38f), new Vector2(0.75f, 0.45f), ResetGame);
            UiKit.CreateButton("Menu", overRt, "MENU", new Vector2(0.25f, 0.29f), new Vector2(0.75f, 0.36f), ReturnToHub);
            overPanel.SetActive(false);
        }

        /// <summary>Same touch controls as the runner: a horizontal stick and a fire button.</summary>
        void BuildControls(RectTransform rt)
        {
            var pad = CreateFixed("Joystick", rt, new Vector2(0f, 0f), new Vector2(440, 170), new Vector2(40, 70));
            var padImg = pad.gameObject.AddComponent<Image>();
            padImg.sprite = ApogeeTheme.Chip;
            padImg.type = Image.Type.Sliced;
            padImg.color = new Color(1f, 1f, 1f, 0.8f);
            var knob = CreateFixed("Knob", pad, new Vector2(0.5f, 0.5f), new Vector2(140, 140), Vector2.zero);
            var knobImg = knob.gameObject.AddComponent<Image>();
            knobImg.sprite = ApogeeTheme.Round;
            knobImg.raycastTarget = false;
            var stick = pad.gameObject.AddComponent<VirtualJoystick>();
            stick.knob = knob;
            UiKit.CreateText("JoyHint", pad, "‹                 ›", 44, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one,
                new Color(ApogeeTheme.Gold.r, ApogeeTheme.Gold.g, ApogeeTheme.Gold.b, 0.6f));

            var fire = CreateFixed("FireButton", rt, new Vector2(1f, 0f), new Vector2(230, 230), new Vector2(-40, 70));
            var fireImg = fire.gameObject.AddComponent<Image>();
            fireImg.sprite = ApogeeTheme.Round;
            fireImg.color = new Color(1f, 0.72f, 0.4f, 0.92f);
            var hold = fire.gameObject.AddComponent<HoldButton>();
            hold.onDown = () => MobileInput.FireHeld = true;
            hold.onUp = () => MobileInput.FireHeld = false;
            UiKit.Outlined(UiKit.CreateText("FireLabel", fire, "TIR", 34, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, ApogeeTheme.Cream));
        }

        static RectTransform CreateFixed(string name, Transform parent, Vector2 anchor, Vector2 size, Vector2 anchoredPos)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor == Vector2.zero ? Vector2.zero : anchor;
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;
            return rt;
        }

        // ---- lifecycle -----------------------------------------------------------------

        protected override void OnEnter()
        {
            // The runner's player must not react to the shared touch input while we use it.
            var runnerPlayer = ui.Director != null ? ui.Director.Player : null;
            if (runnerPlayer != null) runnerPlayer.controlEnabled = false;
            MobileInput.Reset();

            BuildWorld();
            TakeOverCamera(new Vector3(Origin.x, Origin.y, -10f), CameraOrtho, ApogeeTheme.SkyAverage, HalfWidth * 2f + 0.8f);
            ResetGame();
        }

        protected override void OnExit()
        {
            playing = false;
            StopAllCoroutines();
            MobileInput.Reset();
            if (root != null) Destroy(root.gameObject);
            root = null;
            enemies.Clear();
            bullets.Clear();
            shelter.Clear();
            boss = null;
        }

        void BuildWorld()
        {
            root = new GameObject("InvasionWorld").transform;

            // The last island: a strip of ground with its rocky underside and crimson grass.
            var ground = CreateQuad("Island", new Vector2(0f, PlayerY - 0.75f), new Vector2(HalfWidth * 2f + 0.6f, 0.7f),
                new Color(0.36f, 0.22f, 0.18f), -1);
            CreateQuad("Grass", new Vector2(0f, PlayerY - 0.44f), new Vector2(HalfWidth * 2f + 0.6f, 0.14f), new Color(0.64f, 0.15f, 0.10f), 0);

            var rock = new GameObject("IslandRock");
            rock.transform.SetParent(root, false);
            rock.transform.position = Origin + new Vector2(0f, PlayerY - 1.05f);
            rock.transform.localScale = new Vector3(HalfWidth * 2f + 1.4f, 5f / ApogeeTheme.IslandAspect, 1f);
            var rockSr = rock.AddComponent<SpriteRenderer>();
            rockSr.sprite = ApogeeTheme.Island(0);
            rockSr.color = new Color(0.92f, 0.78f, 0.72f);
            rockSr.sortingOrder = -2;

            // The line the invaders must not cross.
            var line = CreateQuad("LoseLine", new Vector2(0f, LoseLineY), new Vector2(HalfWidth * 2f, 0.04f), new Color(0.85f, 0.25f, 0.18f, 0.5f), -1);
            line.GetComponent<SpriteRenderer>().color = new Color(0.85f, 0.25f, 0.18f, 0.45f);

            // The defender: the equipped character.
            var skin = SkinCatalog.Find(SaveSystem.SelectedSkinId);
            var go = new GameObject("Defender");
            go.transform.SetParent(root, false);
            go.transform.localScale = Vector3.one * 1.3f;
            playerSprite = go.AddComponent<SpriteRenderer>();
            var portrait = SkinCatalog.LoadPortrait(skin, out bool custom);
            playerSprite.sprite = custom ? portrait : ui.PlayerSprite;
            playerSprite.color = custom ? Color.white : skin.Tint;
            playerSprite.sortingOrder = 3;
            playerGo = go.transform;
        }

        GameObject CreateQuad(string name, Vector2 localPos, Vector2 size, Color color, int order)
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
            ClearWorldObjects();
            wave = 0;
            score = 0;
            lives = MaxLives + SaveSystem.GetLevel(UpgradeStat.MaxHealth) / 3;
            bossesBeaten = 0;
            playerX = 0f;
            fireCooldown = 0f;
            invulnerableUntil = 0f;
            overPanel.SetActive(false);
            playing = true;
            RefreshHud();
            StartWave();
        }

        void ClearWorldObjects()
        {
            foreach (var e in enemies) if (e.go != null) Destroy(e.go);
            enemies.Clear();
            foreach (var b in bullets) if (b.go != null) Destroy(b.go);
            bullets.Clear();
            foreach (var s in shelter) if (s.go != null) Destroy(s.go);
            shelter.Clear();
            boss = null;
            bossBarRoot.SetActive(false);
        }

        // ---- waves ---------------------------------------------------------------------

        void StartWave()
        {
            wave++;
            bool bossWave = wave % 5 == 0;
            formation = new Vector2(-SpacingX * (Columns - 1) / 2f, FormationTop);
            formationDir = 1f;
            enemyFireTimer = 1.2f;
            bossShootTimer = 1.4f;

            if (bossWave) SpawnBoss();
            else SpawnFormation();

            waveCount = Mathf.Max(1, enemies.Count);
            BuildShelters();
            RefreshHud();
            ui.ShowBanner(bossWave ? "LE COLOSSE" : $"ASSAUT {wave}",
                bossWave ? "Vise sa masse, évite ses salves" : "Ils descendent…", 1.8f);
        }

        void SpawnFormation()
        {
            int rows = Mathf.Clamp(3 + wave / 2, 3, 5);
            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < Columns; col++)
                {
                    // The top rows are the tougher dead; ghosts drift at the front.
                    Invader kind = row == 0 && wave >= 3 ? Invader.Vampire
                        : row <= 1 ? Invader.Skeleton
                        : row == rows - 1 && wave >= 2 ? Invader.Zombie
                        : Invader.Ghost;
                    SpawnEnemy(kind, col, row);
                }
            }
        }

        void SpawnBoss()
        {
            boss = SpawnEnemy(Invader.Boss, Columns / 2, 0);
            boss.maxHp = boss.hp = 26f + wave * 5f;
            bossBarRoot.SetActive(true);
            bossBar.fillAmount = 1f;
        }

        Enemy SpawnEnemy(Invader kind, int col, int row)
        {
            var go = new GameObject($"Invader_{kind}");
            go.transform.SetParent(root, false);

            var e = new Enemy { kind = kind, column = col, row = row, go = go, bob = Random.Range(0f, 6f) };
            string model;
            float height;
            switch (kind)
            {
                case Invader.Skeleton: model = "character-skeleton"; height = 0.95f; e.hp = 1; e.points = 15; break;
                case Invader.Vampire: model = "character-vampire"; height = 1.0f; e.hp = 2; e.points = 25; break;
                case Invader.Zombie: model = "character-zombie"; height = 1.0f; e.hp = 1; e.points = 10; break;
                case Invader.Boss: model = "character-keeper"; height = 2.6f; e.hp = 30; e.points = 300; break;
                default: model = "character-ghost"; height = 0.95f; e.hp = 1; e.points = 10; break;
            }
            e.maxHp = e.hp;

            if (KenneyProps.Available)
            {
                var size = KenneyProps.Size(PropKit.Graveyard, model);
                e.rig = KenneyProps.Spawn(PropKit.Graveyard, model, go.transform, new Vector3(0f, -height / 2f, 0f),
                    height / Mathf.Max(0.01f, size.y), PropLayer.Character, 0f, -6f);
                if (e.rig != null) e.renderers = e.rig.GetComponentsInChildren<Renderer>();
            }
            if (e.rig == null)
            {
                e.fallback = go.AddComponent<SpriteRenderer>();
                e.fallback.sprite = PlaceholderVisuals.Zombie();
                e.fallback.sortingOrder = 3;
                go.transform.localScale = Vector3.one * height;
            }

            go.transform.position = Origin + FormationSlot(e);
            enemies.Add(e);
            return e;
        }

        Vector2 FormationSlot(Enemy e) => formation + new Vector2(e.column * SpacingX, -e.row * SpacingY);

        /// <summary>Three rock shelters that both sides can chip away at, rebuilt each assault.</summary>
        void BuildShelters()
        {
            foreach (var old in shelter) if (old.go != null) Destroy(old.go);
            shelter.Clear();

            const int blocksX = 7, blocksY = 3;
            const float block = 0.26f;
            for (int s = 0; s < 3; s++)
            {
                float centerX = -2.4f + s * 2.4f;
                for (int y = 0; y < blocksY; y++)
                {
                    for (int x = 0; x < blocksX; x++)
                    {
                        // Carve a doorway in the middle of the bottom row, like the classic bunkers.
                        if (y == 0 && x >= 2 && x <= 4) continue;
                        var pos = new Vector2(centerX + (x - (blocksX - 1) / 2f) * block, ShelterY + y * block);
                        var go = CreateQuad("Block", pos, new Vector2(block * 0.96f, block * 0.96f), new Color(0.52f, 0.34f, 0.28f), 1);
                        shelter.Add(new ShelterBlock { go = go, position = pos, hp = 2f });
                    }
                }
            }
        }

        // ---- per-frame -----------------------------------------------------------------

        void Update()
        {
            if (!IsActive || !playing) return;
            float dt = Time.deltaTime;

            UpdatePlayer(dt);
            UpdateFormation(dt);
            UpdateBoss(dt);
            UpdateEnemyFire(dt);
            UpdateBullets(dt);
        }

        void UpdatePlayer(float dt)
        {
            float move = MobileInput.TouchMoveX;
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.leftArrowKey.isPressed || kb.aKey.isPressed || kb.qKey.isPressed) move -= 1f;
                if (kb.rightArrowKey.isPressed || kb.dKey.isPressed) move += 1f;
            }
            playerX = Mathf.Clamp(playerX + Mathf.Clamp(move, -1f, 1f) * 6f * dt, -HalfWidth + 0.4f, HalfWidth - 0.4f);
            playerGo.position = Origin + new Vector2(playerX, PlayerY);
            if (playerSprite != null && Mathf.Abs(move) > 0.05f) playerSprite.flipX = move < 0f;

            bool wantsFire = MobileInput.FireHeld
                || (kb != null && (kb.spaceKey.isPressed || kb.fKey.isPressed || kb.upArrowKey.isPressed))
                || (Gamepad.current != null && Gamepad.current.buttonSouth.isPressed);

            fireCooldown -= dt;
            if (wantsFire && fireCooldown <= 0f)
            {
                fireCooldown = FireInterval;
                SpawnBullet(new Vector2(playerX, PlayerY + 0.55f), new Vector2(0f, BulletSpeed), ShotDamage, true);
                Sfx.Shoot();
            }
        }

        void UpdateFormation(float dt)
        {
            if (enemies.Count == 0) { WaveCleared(); return; }
            if (boss != null) return;   // the Colosse moves on its own

            // Thinning the formation makes what is left move faster, like the original.
            float alive = enemies.Count / (float)waveCount;
            float speed = (0.9f + wave * 0.12f) * (1f + (1f - alive) * 1.9f);

            float minX = float.MaxValue, maxX = float.MinValue;
            foreach (var e in enemies)
            {
                float x = FormationSlot(e).x;
                minX = Mathf.Min(minX, x);
                maxX = Mathf.Max(maxX, x);
            }

            formation.x += formationDir * speed * dt;
            if (formationDir > 0f && maxX + formationDir * speed * dt > HalfWidth - 0.5f) StepDown();
            else if (formationDir < 0f && minX + formationDir * speed * dt < -HalfWidth + 0.5f) StepDown();

            foreach (var e in enemies)
            {
                e.bob += dt * 2.2f;
                var slot = FormationSlot(e);
                slot.y += Mathf.Sin(e.bob) * 0.06f;
                e.go.transform.position = Origin + slot;
                if (slot.y <= LoseLineY + 0.3f) { GameOver(true); return; }
            }
        }

        void StepDown()
        {
            formationDir = -formationDir;
            formation.y -= 0.42f;
            Fx.Shake(0.12f, 0.12f);
        }

        void UpdateBoss(float dt)
        {
            if (boss == null) return;

            float t = Time.time * 0.9f;
            var pos = new Vector2(Mathf.Sin(t) * (HalfWidth - 1.6f), FormationTop - 0.6f - wave * 0.05f);
            boss.go.transform.position = Origin + pos;

            bossShootTimer -= dt;
            if (bossShootTimer > 0f) return;
            bossShootTimer = Mathf.Max(0.9f, 2.2f - wave * 0.05f);

            // A three-way volley, plus a pair of ghosts once it is wounded.
            for (int i = -1; i <= 1; i++)
                SpawnBullet(pos + new Vector2(0f, -1.3f), new Vector2(i * 2.2f, -5.5f), 1f, false);

            if (boss.hp < boss.maxHp * 0.5f && enemies.Count < 6)
            {
                var ghost = SpawnEnemy(Invader.Ghost, Random.Range(0, Columns), Random.Range(1, 3));
                ghost.go.transform.position = Origin + pos;
            }
        }

        void UpdateEnemyFire(float dt)
        {
            if (boss != null || enemies.Count == 0) return;
            enemyFireTimer -= dt;
            if (enemyFireTimer > 0f) return;

            enemyFireTimer = Mathf.Max(0.35f, 1.5f - wave * 0.08f) * Random.Range(0.7f, 1.4f);

            // Only the lowest invader of a column can fire, so shots never come through friends.
            var shooter = LowestOfRandomColumn();
            if (shooter == null) return;
            var from = (Vector2)shooter.go.transform.position - Origin;
            SpawnBullet(from + new Vector2(0f, -0.5f), new Vector2(0f, -(4.2f + wave * 0.15f)), 1f, false);
        }

        Enemy LowestOfRandomColumn()
        {
            int column = enemies[Random.Range(0, enemies.Count)].column;
            Enemy lowest = null;
            foreach (var e in enemies)
                if (e.column == column && (lowest == null || e.row > lowest.row)) lowest = e;
            return lowest;
        }

        void SpawnBullet(Vector2 localPos, Vector2 velocity, float damage, bool fromPlayer)
        {
            var go = new GameObject(fromPlayer ? "Shot" : "Spit");
            go.transform.SetParent(root, false);
            go.transform.position = Origin + localPos;
            go.transform.localScale = Vector3.one * (fromPlayer ? 0.26f : 0.3f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = PlaceholderVisuals.RimCircle(fromPlayer ? PlaceholderVisuals.ProjectileColor : PlaceholderVisuals.SpitColor);
            sr.sortingOrder = 4;
            bullets.Add(new Bullet
            {
                go = go,
                position = localPos,
                velocity = velocity,
                damage = damage,
                radius = fromPlayer ? 0.16f : 0.2f,
                fromPlayer = fromPlayer,
            });
        }

        void UpdateBullets(float dt)
        {
            for (int i = bullets.Count - 1; i >= 0; i--)
            {
                var b = bullets[i];
                b.position += b.velocity * dt;
                b.go.transform.position = Origin + b.position;

                bool done = b.position.y > FormationTop + 2.5f || b.position.y < PlayerY - 1.5f;
                if (!done) done = HitShelter(b);
                if (!done) done = b.fromPlayer ? HitInvader(b) : HitPlayer(b);

                if (!done) continue;
                Destroy(b.go);
                bullets.RemoveAt(i);
            }
        }

        bool HitShelter(Bullet b)
        {
            for (int i = shelter.Count - 1; i >= 0; i--)
            {
                var block = shelter[i];
                if (Mathf.Abs(block.position.x - b.position.x) > 0.17f + b.radius) continue;
                if (Mathf.Abs(block.position.y - b.position.y) > 0.17f + b.radius) continue;

                block.hp -= b.damage;
                Fx.Burst(block.go.transform.position, new Color(0.55f, 0.36f, 0.3f), 5, 2f, 0.07f);
                if (block.hp <= 0f)
                {
                    Destroy(block.go);
                    shelter.RemoveAt(i);
                }
                else
                {
                    var sr = block.go.GetComponent<SpriteRenderer>();
                    sr.color = new Color(0.42f, 0.25f, 0.21f);
                }
                return true;
            }
            return false;
        }

        bool HitInvader(Bullet b)
        {
            for (int i = enemies.Count - 1; i >= 0; i--)
            {
                var e = enemies[i];
                float radius = e.kind == Invader.Boss ? 1.1f : 0.45f;
                if (((Vector2)e.go.transform.position - (Origin + b.position)).sqrMagnitude > (radius + b.radius) * (radius + b.radius)) continue;

                e.hp -= b.damage;
                if (e.renderers != null) StartCoroutine(FlashEnemy(e.renderers));
                Fx.Burst(e.go.transform.position, new Color(0.9f, 0.5f, 0.2f), 5, 2.2f, 0.08f);

                if (e.hp <= 0f) KillInvader(e, i);
                else if (e == boss) bossBar.fillAmount = Mathf.Clamp01(e.hp / e.maxHp);
                return true;
            }
            return false;
        }

        void KillInvader(Enemy e, int index)
        {
            score += e.points + wave * 2;
            Fx.Burst(e.go.transform.position, new Color(0.85f, 0.25f, 0.15f), e.kind == Invader.Boss ? 40 : 14, 3.5f, 0.12f);
            Fx.Text(e.go.transform.position, $"+{e.points}", ApogeeTheme.Gold, e.kind == Invader.Boss ? 1.5f : 0.9f);
            Sfx.Kill();
            if (e.kind == Invader.Boss)
            {
                boss = null;
                bossesBeaten++;
                bossBarRoot.SetActive(false);
                Fx.Shake(0.5f, 0.4f);
            }
            Destroy(e.go);
            enemies.RemoveAt(index);
            RefreshHud();
        }

        bool HitPlayer(Bullet b)
        {
            if (Time.time < invulnerableUntil) return false;
            var player = new Vector2(playerX, PlayerY);
            if ((player - b.position).sqrMagnitude > (PlayerRadius + b.radius) * (PlayerRadius + b.radius)) return false;

            lives--;
            invulnerableUntil = Time.time + 1.2f;
            RefreshHud();
            Sfx.Hit();
            Fx.Hitstop();
            Fx.Shake(0.4f, 0.3f);
            Fx.Burst(playerGo.position, new Color(0.85f, 0.2f, 0.15f), 14, 3f, 0.11f);
            StartCoroutine(BlinkPlayer());
            if (lives <= 0) GameOver(false);
            return true;
        }

        System.Collections.IEnumerator BlinkPlayer()
        {
            var baseColor = playerSprite.color;
            while (Time.time < invulnerableUntil && playing)
            {
                playerSprite.color = new Color(1f, 0.4f, 0.35f, 0.65f);
                yield return new WaitForSeconds(0.1f);
                playerSprite.color = baseColor;
                yield return new WaitForSeconds(0.1f);
            }
            playerSprite.color = baseColor;
        }

        System.Collections.IEnumerator FlashEnemy(Renderer[] renderers)
        {
            const float duration = 0.1f;
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                KenneyProps.SetFlash(renderers, 0.85f * (1f - t / duration));
                yield return null;
            }
            KenneyProps.SetFlash(renderers, 0f);
        }

        void WaveCleared()
        {
            score += 40 * wave;
            Sfx.Milestone();
            foreach (var b in bullets) if (b.go != null) Destroy(b.go);
            bullets.Clear();
            RefreshHud();
            StartWave();
        }

        void GameOver(bool overrun)
        {
            playing = false;
            int coins = score / 25;
            if (coins > 0) SaveSystem.AddCoins(coins);
            if (bossesBeaten > 0) SaveSystem.AddMaterials(bossesBeaten * 3);
            bool record = score > SaveSystem.InvasionBest;
            if (record) SaveSystem.InvasionBest = score;

            overScoreText.text = record
                ? $"Nouveau record : {score} pts (assaut {wave})"
                : $"Score : {score} pts — assaut {wave}";
            overRewardText.text = coins > 0
                ? $"+{coins} pièces" + (bossesBeaten > 0 ? $"   +{bossesBeaten * 3} matériaux" : "")
                : "Aucune pièce gagnée";
            hintText.text = overrun ? "Ils ont atteint l'île !" : "";
            overPanel.SetActive(true);
            Sfx.Death();
            Fx.Shake(0.6f, 0.4f);
            AdService.OnPlayerDeath();
        }

        void RefreshHud()
        {
            scoreText.text = score.ToString();
            waveText.text = $"Assaut {wave}";
            bestText.text = $"Record : {Mathf.Max(SaveSystem.InvasionBest, score)}";
            for (int i = 0; i < hearts.Count; i++) hearts[i].gameObject.SetActive(i < lives);
            hintText.text = "";
        }
    }
}
