using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Platformer.Mechanics;

namespace Platformer.Survival
{
    /// <summary>
    /// The whole shell UI, built at runtime with legacy uGUI (see UiKit): the hub (choose
    /// a mini-game), the characters page (Brawl-Stars-style card grid with padlocks), the
    /// upgrade shop, the runner's HUD / game over, plus the shared sector banner and
    /// rewarded-ad overlay. Mini-games (Fusion, Arena) build their own panels into the same
    /// canvas via MiniGame.Setup and return here through ShowHub().
    /// </summary>
    public class RuntimeUI : MonoBehaviour
    {
        SurvivalDirector director;
        MiniGame[] miniGames = Array.Empty<MiniGame>();
        ArenaGame arena;

        Canvas canvas;
        GameObject hubPanel, charactersPanel, hudPanel, gameOverPanel, shopPanel;
        GameObject expeditionPanel, levelClearedPanel, levelFailedPanel, levelStarsPanel, runnerModesPanel;

        const int PreviewLayer = 31;
        Camera previewCamera;
        RenderTexture previewTexture;
        GameObject previewCharacter;

        Text hubWalletText, hubCharacterText;
        readonly List<Text> hubCardBestTexts = new();
        Text runnerBestText;

        // characters page
        readonly List<CharacterCard> characterCards = new();
        Text characterDetailText, charactersWalletText;
        Button characterActionButton;
        int selectedCharacterIndex;

        struct CharacterCard
        {
            public Image portrait;
            public GameObject lockOverlay;
            public Outline equippedOutline;
        }

        // runner HUD / game over
        Slider healthSlider;
        Text hudDistanceText, hudCoinsText, hudMaterialsText, hudZoneText;
        Text gameOverDistanceText, gameOverBestText;
        GameObject bannerGo;
        Text bannerTitleText, bannerSubtitleText;
        Coroutine bannerRoutine;

        Text shopWalletText;
        readonly Dictionary<UpgradeStat, Text> shopLevelTexts = new();
        readonly Dictionary<UpgradeStat, Button> shopButtons = new();

        // campaign
        struct LevelCard
        {
            public Button button;
            public Text title, subtitle, status;
            public Image[] stars;
            public GameObject lockOverlay;
        }
        readonly List<LevelCard> levelCards = new();
        Text expeditionStarsText;
        Text hudSecretsText;
        Image hudProgressFill;
        GameObject hudProgressRoot;
        Text levelClearedTitle, levelClearedBody, levelFailedTitle, levelFailedBody, levelStarsTitle, levelStarsBody;
        Button levelFailedResumeButton;
        readonly Image[] levelStarsIcons = new Image[3];
        float pendingBossHealth;

        GameObject adOverlay;
        Text adOverlayText;

        public Canvas Canvas => canvas;
        public SurvivalDirector Director => director;

        public void Init(SurvivalDirector survivalDirector, MiniGame[] games)
        {
            director = survivalDirector;
            miniGames = games ?? Array.Empty<MiniGame>();
            foreach (var game in miniGames) if (game is ArenaGame arenaGame) arena = arenaGame;

            EnsureEventSystem();
            SetupCharacterPreview();
            BuildCanvas();
            BuildVignette();
            BuildHubPanel();
            BuildCharactersPanel();
            BuildRunnerModesPanel();
            BuildExpeditionPanel();
            BuildHudPanel();
            BuildGameOverPanel();
            BuildCampaignPanels();
            BuildShopPanel();
            BuildAdOverlay();

            foreach (var game in miniGames) game.Setup(this);

            ShowHub();
        }

        // ---- character preview -------------------------------------------------------

        /// <summary>
        /// Builds an isolated character preview: a lightweight clone of the player's sprite
        /// and animator (same idle/run animations, no physics/gameplay components) parked
        /// far outside the play area, rendered by its own camera into a RenderTexture on a
        /// dedicated layer. The hub shows this texture instead of compositing over the
        /// live game camera, so it reads as a genuinely separate page.
        /// </summary>
        void SetupCharacterPreview()
        {
            var player = director.Player;
            if (player == null) return;

            var farAway = new Vector3(9500f, 9500f, 0f);

            previewCharacter = new GameObject("SkinPreviewCharacter");
            previewCharacter.layer = PreviewLayer;
            previewCharacter.transform.position = farAway;

            var sourceSr = player.GetComponent<SpriteRenderer>();
            var sr = previewCharacter.AddComponent<SpriteRenderer>();
            if (sourceSr != null) sr.sprite = sourceSr.sprite;

            var sourceAnimator = player.GetComponent<Animator>();
            var animator = previewCharacter.AddComponent<Animator>();
            if (sourceAnimator != null) animator.runtimeAnimatorController = sourceAnimator.runtimeAnimatorController;

            var camGo = new GameObject("SkinPreviewCamera");
            camGo.transform.position = farAway + new Vector3(0f, 0f, -10f);
            previewCamera = camGo.AddComponent<Camera>();
            previewCamera.orthographic = true;
            previewCamera.orthographicSize = 0.95f;
            previewCamera.cullingMask = 1 << PreviewLayer;
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            previewCamera.enabled = false;

            previewTexture = new RenderTexture(512, 512, 16) { name = "SkinPreviewRT" };
            previewCamera.targetTexture = previewTexture;
        }

        /// <summary>The player's own sprite (used as the fallback portrait everywhere).</summary>
        public Sprite PlayerSprite
        {
            get
            {
                var sr = director.Player != null ? director.Player.GetComponent<SpriteRenderer>() : null;
                return sr != null ? sr.sprite : null;
            }
        }

        void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        void BuildCanvas()
        {
            var canvasGo = new GameObject("SurvivalCanvas");
            canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<CanvasOrientation>();
            canvasGo.AddComponent<GraphicRaycaster>();
        }

        void BuildVignette()
        {
            var img = UiKit.CreateImage("Vignette", canvas.transform, Vector2.zero, Vector2.one, PlaceholderVisuals.Vignette(), Color.white, false);
            img.raycastTarget = false;
        }

        static string FormatDistance(float meters) => $"{Mathf.FloorToInt(meters)} m";

        /// <summary>Opaque painted menu backdrop (blurred Apogée sky) so the runner never shows through a menu.</summary>
        static void ApplyOpaqueBackdrop(RectTransform panel)
        {
            var img = panel.GetComponent<Image>();
            img.color = ApogeeTheme.Maroon;
            var art = UiKit.CreateArtBackdrop("Backdrop", panel, "menu_bg", 0.62f, 0.5f);
            art.transform.SetAsFirstSibling();
        }

        /// <summary>Screen title in the theme: gold Cinzel with a dark outline and a thin rule under it.</summary>
        static Text CreateTitle(Transform parent, string text, float yMin, float yMax)
        {
            var t = UiKit.Outlined(UiKit.CreateText("Title", parent, text, 52, TextAnchor.MiddleCenter,
                new Vector2(0.05f, yMin), new Vector2(0.95f, yMax), ApogeeTheme.Gold), 2.5f);
            var rule = UiKit.CreateImage("Rule", parent, new Vector2(0.25f, yMin - 0.004f), new Vector2(0.75f, yMin), PlaceholderVisuals.Square(Color.white), ApogeeTheme.Gold, false);
            rule.color = new Color(ApogeeTheme.Gold.r, ApogeeTheme.Gold.g, ApogeeTheme.Gold.b, 0.7f);
            return t;
        }

        /// <summary>A dark pill with a line of text (wallet counters).</summary>
        static Text CreateChip(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, int size = 24)
        {
            var chip = UiKit.CreateRect(name, parent, anchorMin, anchorMax);
            var img = chip.gameObject.AddComponent<Image>();
            img.sprite = ApogeeTheme.Chip;
            img.type = Image.Type.Sliced;
            img.raycastTarget = false;
            var text = UiKit.CreateText(name + "_Text", chip, "", size, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, ApogeeTheme.Gold);
            UiKit.FitLabel(text, size);
            text.rectTransform.offsetMin = new Vector2(16, 4);
            text.rectTransform.offsetMax = new Vector2(-16, -4);
            return text;
        }

        static string StatLabel(UpgradeStat stat) => stat switch
        {
            UpgradeStat.Speed => "Vitesse",
            UpgradeStat.FirePower => "Puissance de tir",
            UpgradeStat.MaxHealth => "Vie max",
            UpgradeStat.Armor => "Armure",
            UpgradeStat.DoubleJump => "Double saut",
            UpgradeStat.Magnet => "Aimant à pièces",
            UpgradeStat.Drone => "Drone compagnon",
            _ => stat.ToString()
        };

        // ---- hub -----------------------------------------------------------------------

        /// <summary>
        /// Home screen on the Apogée key art: the painted hero stays visible on the left
        /// (the art is cover-cropped around him), the logo sits on top, the mini-game cards
        /// stack in a column on the right, and the bottom row holds the characters card
        /// (with the equipped character animated) and the upgrades button.
        /// </summary>
        void BuildHubPanel()
        {
            var rt = UiKit.CreateRect("HubPanel", canvas.transform, Vector2.zero, Vector2.one);
            hubPanel = rt.gameObject;
            rt.gameObject.AddComponent<Image>().color = ApogeeTheme.Maroon;
            UiKit.CreateArtBackdrop("KeyArt", rt, "home_art", 0.29f, 0.5f);

            // Soft darkening at the bottom and on the right so cards read on any part of the art.
            var bottomShade = UiKit.CreateImage("BottomShade", rt, Vector2.zero, new Vector2(1f, 0.42f), ApogeeTheme.VerticalFade, new Color(0.16f, 0.03f, 0.03f, 0.85f), false);
            bottomShade.raycastTarget = false;

            var logo = UiKit.CreateImage("Logo", rt, new Vector2(0.06f, 0.80f), new Vector2(0.94f, 0.965f), ApogeeTheme.ArtSprite("logo_dark"), Color.white);
            logo.preserveAspect = true;

            hubWalletText = CreateChip("Wallet", rt, new Vector2(0.50f, 0.748f), new Vector2(0.96f, 0.792f), 24);

            // Mini-game cards: the runner first, then every registered mini-game.
            var cards = new List<(string title, string desc, Action onClick, Func<string> best)>
            {
                ("RUNNER", "Course sans fin ou niveaux à terminer", ShowRunnerModes,
                    () => SaveSystem.BestDistance > 0f
                        ? $"Record : {FormatDistance(SaveSystem.BestDistance)}   ·   {SaveSystem.TotalStars} étoiles"
                        : "Aucun record"),
            };
            foreach (var game in miniGames)
            {
                var captured = game;
                cards.Add((game.Title, game.Description, () => EnterMiniGame(captured), () => captured.BestLine));
            }

            const float cardsTop = 0.735f;
            const float cardsBottom = 0.195f;
            float slot = (cardsTop - cardsBottom) / cards.Count;
            hubCardBestTexts.Clear();
            for (int i = 0; i < cards.Count; i++)
            {
                var (title, desc, onClick, best) = cards[i];
                float yMax = cardsTop - i * slot;
                float yMin = yMax - slot + 0.012f;
                var card = UiKit.CreateButton($"Card_{title}", rt, "", new Vector2(0.50f, yMin), new Vector2(0.96f, yMax),
                    () => onClick(), 30, UiKit.CardColor);
                UiKit.Outlined(UiKit.CreateText("CardTitle", card.transform, title, 34, TextAnchor.MiddleLeft,
                    new Vector2(0.07f, 0.56f), new Vector2(0.95f, 0.94f), ApogeeTheme.Gold), 1.5f);
                var descText = UiKit.CreateText("CardDesc", card.transform, desc, 20, TextAnchor.UpperLeft,
                    new Vector2(0.07f, 0.24f), new Vector2(0.93f, 0.56f), UiKit.TextDim);
                UiKit.FitLabel(descText, 20);
                var bestText = UiKit.CreateText("CardBest", card.transform, "", 18, TextAnchor.LowerLeft,
                    new Vector2(0.07f, 0.06f), new Vector2(0.75f, 0.26f), ApogeeTheme.Cream);
                UiKit.FitLabel(bestText, 18);
                hubCardBestTexts.Add(bestText);
                UiKit.Outlined(UiKit.CreateText("CardArrow", card.transform, "›", 54, TextAnchor.MiddleRight,
                    new Vector2(0.75f, 0f), new Vector2(0.95f, 0.5f), ApogeeTheme.Gold), 1.5f);
            }
            hubCardBestProviders = cards.ConvertAll(c => c.best);

            // Characters card with the equipped character animated (isolated preview camera).
            var charBtn = UiKit.CreateButton("CharactersButton", rt, "", new Vector2(0.04f, 0.035f), new Vector2(0.49f, 0.168f), ShowCharacters, 28, UiKit.CardColor);
            var previewArea = UiKit.CreateRect("CharacterPreviewArea", charBtn.transform, new Vector2(0.02f, 0.04f), new Vector2(0.44f, 0.96f));
            var previewRt = UiKit.CreateRect("CharacterPreview", previewArea, Vector2.zero, Vector2.one);
            var fitter = previewRt.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = 1f;
            var previewImg = previewRt.gameObject.AddComponent<RawImage>();
            previewImg.texture = previewTexture;
            previewImg.raycastTarget = false;
            var charLabel = UiKit.Outlined(UiKit.CreateText("CharactersLabel", charBtn.transform, "PERSONNAGES", 24, TextAnchor.MiddleLeft,
                new Vector2(0.44f, 0.5f), new Vector2(0.97f, 0.9f), ApogeeTheme.Gold), 1.5f);
            UiKit.FitLabel(charLabel, 24);
            hubCharacterText = UiKit.CreateText("HubCharacter", charBtn.transform, "", 22, TextAnchor.MiddleLeft,
                new Vector2(0.44f, 0.12f), new Vector2(0.97f, 0.5f), ApogeeTheme.Cream);
            UiKit.FitLabel(hubCharacterText, 22);

            UiKit.CreateButton("ShopButtonHub", rt, "AMÉLIORATIONS", new Vector2(0.51f, 0.035f), new Vector2(0.96f, 0.168f), ShowShop, 28);
        }

        List<Func<string>> hubCardBestProviders = new();

        void RefreshHub()
        {
            hubWalletText.text = $"{SaveSystem.Coins} pièces   ·   {SaveSystem.Materials} matériaux";
            var skin = SkinCatalog.Find(SaveSystem.SelectedSkinId);
            hubCharacterText.text = skin.Name;
            if (previewCharacter != null)
            {
                var sr = previewCharacter.GetComponent<SpriteRenderer>();
                if (sr != null) sr.color = skin.Tint;
            }
            for (int i = 0; i < hubCardBestTexts.Count && i < hubCardBestProviders.Count; i++)
                hubCardBestTexts[i].text = hubCardBestProviders[i]() ?? "";
        }

        // ---- characters page -----------------------------------------------------------

        /// <summary>
        /// Card grid of every character: portrait (custom sprite from Resources/Portraits if
        /// present, else the tinted player sprite), name, a padlock overlay while locked and
        /// a gold outline on the equipped one. Tapping a card selects it; the detail bar
        /// below offers ÉQUIPER / ACHETER / REGARDER UNE PUB depending on its state.
        /// </summary>
        void BuildCharactersPanel()
        {
            var rt = UiKit.CreatePanel("CharactersPanel", canvas.transform, Color.white);
            charactersPanel = rt.gameObject;
            ApplyOpaqueBackdrop(rt);

            CreateTitle(rt, "PERSONNAGES", 0.905f, 0.965f);
            charactersWalletText = CreateChip("Wallet", rt, new Vector2(0.30f, 0.855f), new Vector2(0.70f, 0.893f), 24);

            var skins = SkinCatalog.All;
            const int columns = 3;
            int rows = Mathf.CeilToInt(skins.Length / (float)columns);
            const float gridTop = 0.845f, gridBottom = 0.275f;
            float rowH = (gridTop - gridBottom) / rows;
            const float gap = 0.008f;

            characterCards.Clear();
            for (int i = 0; i < skins.Length; i++)
            {
                int col = i % columns, row = i / columns;
                float x0 = 0.04f + col * 0.31f;
                float yMax = gridTop - row * rowH;
                var cardRt = UiKit.CreateRect($"Card_{skins[i].Id}", rt, new Vector2(x0, yMax - rowH + gap), new Vector2(x0 + 0.30f, yMax - gap));

                var cardBg = cardRt.gameObject.AddComponent<Image>();
                var btn = cardRt.gameObject.AddComponent<Button>();
                UiKit.ApplyButtonStyle(btn, cardBg, UiKit.CardColor);
                var outline = cardRt.gameObject.AddComponent<Outline>();
                outline.effectColor = ApogeeTheme.Gold;
                outline.effectDistance = new Vector2(6f, -6f);
                outline.enabled = false;

                int captured = i;
                btn.onClick.AddListener(() => OnCharacterCardClicked(captured));

                var portraitSprite = SkinCatalog.LoadPortrait(skins[i], out bool custom);
                var portrait = UiKit.CreateImage("Portrait", cardRt, new Vector2(0.1f, 0.3f), new Vector2(0.9f, 0.95f),
                    custom ? portraitSprite : PlayerSprite, custom ? Color.white : skins[i].Tint);

                var nameText = UiKit.Outlined(UiKit.CreateText("Name", cardRt, skins[i].Name, 22, TextAnchor.MiddleCenter,
                    new Vector2(0.05f, 0.04f), new Vector2(0.95f, 0.28f), ApogeeTheme.Cream), 1.5f);
                UiKit.FitLabel(nameText, 22);

                var lockRt = UiKit.CreateRect("Lock", cardRt, Vector2.zero, Vector2.one);
                lockRt.offsetMin = new Vector2(6, 6);
                lockRt.offsetMax = new Vector2(-6, -6);
                var lockImg = lockRt.gameObject.AddComponent<Image>();
                lockImg.sprite = ApogeeTheme.FrameFill;
                lockImg.type = Image.Type.Sliced;
                lockImg.color = new Color(0.12f, 0.02f, 0.02f, 0.62f);
                lockImg.raycastTarget = false;
                UiKit.CreateImage("Padlock", lockRt, new Vector2(0.35f, 0.42f), new Vector2(0.65f, 0.88f), PlaceholderVisuals.Padlock(), Color.white);

                characterCards.Add(new CharacterCard { portrait = portrait, lockOverlay = lockRt.gameObject, equippedOutline = outline });
            }

            // Detail bar for the selected card.
            UiKit.CreateFrame("DetailBg", rt, new Vector2(0.04f, 0.125f), new Vector2(0.96f, 0.26f));
            characterDetailText = UiKit.CreateText("Detail", rt, "", 21, TextAnchor.MiddleLeft,
                new Vector2(0.075f, 0.135f), new Vector2(0.57f, 0.25f), ApogeeTheme.Cream);
            UiKit.FitLabel(characterDetailText, 21);
            characterActionButton = UiKit.CreateButton("CharacterAction", rt, "", new Vector2(0.58f, 0.15f), new Vector2(0.93f, 0.23f), OnCharacterActionClicked, 24);

            UiKit.CreateButton("CharactersBack", rt, "RETOUR", new Vector2(0.32f, 0.03f), new Vector2(0.68f, 0.105f), ShowHub);
        }

        void OnCharacterCardClicked(int index)
        {
            selectedCharacterIndex = index;
            RefreshCharacters();
        }

        void OnCharacterActionClicked()
        {
            var skin = SkinCatalog.All[selectedCharacterIndex];
            bool unlocked = SaveSystem.IsSkinUnlocked(skin.Id);

            if (unlocked)
            {
                SaveSystem.SelectedSkinId = skin.Id;
                SkinCatalog.ApplyToPlayer(director.Player);
                RefreshCharacters();
                return;
            }

            if (skin.UnlockType == SkinUnlockType.Coins)
            {
                if (SaveSystem.TrySpendCoins(skin.CoinCost))
                {
                    SaveSystem.UnlockSkin(skin.Id);
                    SaveSystem.SelectedSkinId = skin.Id;
                    SkinCatalog.ApplyToPlayer(director.Player);
                    RefreshCharacters();
                }
            }
            else if (skin.UnlockType == SkinUnlockType.Ad)
            {
                ShowAdOverlay(true, "Publicité en cours...");
                AdService.ShowRewardedAd(() =>
                {
                    ShowAdOverlay(false);
                    SaveSystem.UnlockSkin(skin.Id);
                    SaveSystem.SelectedSkinId = skin.Id;
                    SkinCatalog.ApplyToPlayer(director.Player);
                    RefreshCharacters();
                }, () =>
                {
                    ShowAdOverlay(false);
                    characterDetailText.text = $"{skin.Name}\nPublicité indisponible ou interrompue, réessaie.";
                });
            }
        }

        void RefreshCharacters()
        {
            charactersWalletText.text = $"Pièces : {SaveSystem.Coins}";
            var skins = SkinCatalog.All;
            for (int i = 0; i < skins.Length && i < characterCards.Count; i++)
            {
                bool unlocked = SaveSystem.IsSkinUnlocked(skins[i].Id);
                characterCards[i].lockOverlay.SetActive(!unlocked);
                characterCards[i].equippedOutline.enabled = SaveSystem.SelectedSkinId == skins[i].Id;
            }

            var skin = skins[selectedCharacterIndex];
            bool selectedUnlocked = SaveSystem.IsSkinUnlocked(skin.Id);
            var label = UiKit.ButtonLabel(characterActionButton);
            if (selectedUnlocked)
            {
                bool equipped = SaveSystem.SelectedSkinId == skin.Id;
                characterDetailText.text = $"{skin.Name} — {(equipped ? "Équipé" : "Débloqué")}\n{skin.Description}";
                label.text = equipped ? "ÉQUIPÉ" : "ÉQUIPER";
                characterActionButton.interactable = !equipped;
            }
            else if (skin.UnlockType == SkinUnlockType.Coins)
            {
                characterDetailText.text = $"{skin.Name} — Verrouillé ({skin.CoinCost} pièces)\n{skin.Description}";
                label.text = $"ACHETER ({skin.CoinCost})";
                characterActionButton.interactable = SaveSystem.Coins >= skin.CoinCost;
            }
            else
            {
                characterDetailText.text = $"{skin.Name} — Verrouillé (pub)\n{skin.Description}";
                label.text = "REGARDER UNE PUB";
                characterActionButton.interactable = true;
            }
        }

        // ---- runner: endless or campaign --------------------------------------------------

        /// <summary>
        /// The two ways to play the runner, behind one hub card. They share the same world,
        /// the same character and the same controls, and differ only in what is asked: run
        /// as far as you can with the speed creeping up, or finish an authored level at a
        /// fixed speed. Putting them side by side here says that better than two cards on
        /// the home page did.
        /// </summary>
        void BuildRunnerModesPanel()
        {
            var rt = UiKit.CreatePanel("RunnerModesPanel", canvas.transform, Color.white);
            runnerModesPanel = rt.gameObject;
            ApplyOpaqueBackdrop(rt);

            CreateTitle(rt, "RUNNER", 0.875f, 0.945f);

            runnerModeBest.Clear();
            var modes = new (string title, string desc, Action onClick, Func<string> best)[]
            {
                ("RUNNER INFINI", "Cours le plus loin possible. La vitesse monte,\nle terrain se génère sans jamais s'arrêter.", StartRunner,
                    () => SaveSystem.BestDistance > 0f ? $"Record : {FormatDistance(SaveSystem.BestDistance)}" : "Aucun record"),
                ("EXPÉDITION", "Des niveaux écrits à la main, avec un début,\nune fin et un boss. Vitesse constante.", ShowExpedition,
                    () => $"Étoiles : {SaveSystem.TotalStars} / {LevelCatalog.Count * 3}"),
            };

            const float top = 0.82f, bottom = 0.17f;
            float slot = (top - bottom) / modes.Length;
            for (int i = 0; i < modes.Length; i++)
            {
                var (title, desc, onClick, best) = modes[i];
                float yMax = top - i * slot;
                float yMin = yMax - slot + 0.035f;

                var card = UiKit.CreateButton($"Mode_{title}", rt, "", new Vector2(0.06f, yMin), new Vector2(0.94f, yMax),
                    () => onClick(), 30, UiKit.CardColor);
                UiKit.Outlined(UiKit.CreateText("ModeTitle", card.transform, title, 40, TextAnchor.MiddleLeft,
                    new Vector2(0.07f, 0.66f), new Vector2(0.95f, 0.92f), ApogeeTheme.Gold), 2f);
                var descText = UiKit.CreateText("ModeDesc", card.transform, desc, 22, TextAnchor.UpperLeft,
                    new Vector2(0.07f, 0.28f), new Vector2(0.93f, 0.64f), UiKit.TextDim);
                UiKit.FitLabel(descText, 22);
                var bestText = UiKit.CreateText("ModeBest", card.transform, "", 20, TextAnchor.LowerLeft,
                    new Vector2(0.07f, 0.07f), new Vector2(0.8f, 0.28f), ApogeeTheme.Cream);
                UiKit.FitLabel(bestText, 20);
                runnerModeBest.Add(bestText);
                runnerModeBestProviders.Add(best);
                UiKit.Outlined(UiKit.CreateText("ModeArrow", card.transform, "›", 60, TextAnchor.MiddleRight,
                    new Vector2(0.78f, 0f), new Vector2(0.95f, 0.55f), ApogeeTheme.Gold), 1.5f);
            }

            UiKit.CreateButton("RunnerModesBack", rt, "RETOUR", new Vector2(0.32f, 0.04f), new Vector2(0.68f, 0.115f), ShowHub);
        }

        readonly List<Text> runnerModeBest = new();
        readonly List<Func<string>> runnerModeBestProviders = new();

        public void ShowRunnerModes()
        {
            Time.timeScale = 1f;
            if (director != null && director.InCampaign) director.LeaveCampaign();
            HideAllShellPanels();
            HideBanner();
            UiKit.SetPanel(runnerModesPanel, true);
            for (int i = 0; i < runnerModeBest.Count && i < runnerModeBestProviders.Count; i++)
                runnerModeBest[i].text = runnerModeBestProviders[i]() ?? "";
        }

        // ---- expedition (campaign level select) ------------------------------------------

        /// <summary>
        /// The campaign's front page: one card per authored level with its three stars,
        /// locked until the level before it has been cleared. Deliberately a short list -
        /// a level here is a three to five minute run with a boss at the end, not a stage
        /// in a grid of eighty.
        /// </summary>
        void BuildExpeditionPanel()
        {
            var rt = UiKit.CreatePanel("ExpeditionPanel", canvas.transform, Color.white);
            expeditionPanel = rt.gameObject;
            ApplyOpaqueBackdrop(rt);

            CreateTitle(rt, "EXPÉDITION", 0.905f, 0.965f);
            expeditionStarsText = CreateChip("Stars", rt, new Vector2(0.28f, 0.852f), new Vector2(0.72f, 0.892f), 24);

            const float top = 0.825f, bottom = 0.155f;
            int count = LevelCatalog.Count;
            float slot = (top - bottom) / count;

            levelCards.Clear();
            for (int i = 0; i < count; i++)
            {
                var def = LevelCatalog.Get(i);
                int captured = i;
                float yMax = top - i * slot;
                float yMin = yMax - slot + 0.018f;

                var card = UiKit.CreateButton($"Level_{def.Id}", rt, "", new Vector2(0.05f, yMin), new Vector2(0.95f, yMax),
                    () => OnLevelCardClicked(captured), 30, UiKit.CardColor);

                var title = UiKit.Outlined(UiKit.CreateText("LevelTitle", card.transform, def.Name, 32, TextAnchor.MiddleLeft,
                    new Vector2(0.06f, 0.62f), new Vector2(0.72f, 0.95f), ApogeeTheme.Gold), 1.5f);
                UiKit.FitLabel(title, 32);
                var subtitle = UiKit.CreateText("LevelSubtitle", card.transform, def.Subtitle, 20, TextAnchor.UpperLeft,
                    new Vector2(0.06f, 0.33f), new Vector2(0.75f, 0.62f), UiKit.TextDim);
                UiKit.FitLabel(subtitle, 20);
                var status = UiKit.CreateText("LevelStatus", card.transform, "", 19, TextAnchor.LowerLeft,
                    new Vector2(0.06f, 0.06f), new Vector2(0.95f, 0.33f), ApogeeTheme.Cream);
                UiKit.FitLabel(status, 19);

                var stars = new Image[3];
                for (int s = 0; s < 3; s++)
                {
                    float x0 = 0.74f + s * 0.082f;
                    stars[s] = UiKit.CreateImage($"Star_{s}", card.transform, new Vector2(x0, 0.6f), new Vector2(x0 + 0.075f, 0.95f),
                        PlaceholderVisuals.Star(), ApogeeTheme.GoldDark);
                }

                var lockRt = UiKit.CreateRect("Lock", card.transform, Vector2.zero, Vector2.one);
                lockRt.offsetMin = new Vector2(4, 4);
                lockRt.offsetMax = new Vector2(-4, -4);
                var lockImg = lockRt.gameObject.AddComponent<Image>();
                lockImg.sprite = ApogeeTheme.FrameFill;
                lockImg.type = Image.Type.Sliced;
                lockImg.color = new Color(0.12f, 0.02f, 0.02f, 0.72f);
                lockImg.raycastTarget = false;
                UiKit.CreateImage("Padlock", lockRt, new Vector2(0.44f, 0.22f), new Vector2(0.56f, 0.78f), PlaceholderVisuals.Padlock(), Color.white);

                levelCards.Add(new LevelCard { button = card, title = title, subtitle = subtitle, status = status, stars = stars, lockOverlay = lockRt.gameObject });
            }

            UiKit.CreateText("ExpeditionHint", rt, "Vitesse constante · 3 étoiles par niveau : terminer, tous les secrets, arriver au boss au-dessus de 60 % de vie",
                18, TextAnchor.UpperCenter, new Vector2(0.06f, 0.105f), new Vector2(0.94f, 0.15f), UiKit.TextDim);
            UiKit.CreateButton("ExpeditionBack", rt, "RETOUR", new Vector2(0.32f, 0.025f), new Vector2(0.68f, 0.098f), ShowRunnerModes);
        }

        public void ShowExpedition()
        {
            Time.timeScale = 1f;
            HideAllShellPanels();
            UiKit.SetPanel(expeditionPanel, true);
            RefreshExpedition();
        }

        void RefreshExpedition()
        {
            expeditionStarsText.text = $"{SaveSystem.TotalStars} / {LevelCatalog.Count * 3} étoiles";
            for (int i = 0; i < levelCards.Count; i++)
            {
                var def = LevelCatalog.Get(i);
                bool unlocked = LevelCatalog.IsUnlocked(i);
                int mask = SaveSystem.GetLevelStars(i);

                levelCards[i].lockOverlay.SetActive(!unlocked);
                levelCards[i].button.interactable = unlocked;
                for (int s = 0; s < 3; s++)
                    levelCards[i].stars[s].color = (mask & (1 << s)) != 0 ? ApogeeTheme.Gold : new Color(0.35f, 0.22f, 0.16f, 0.8f);

                if (!unlocked)
                    levelCards[i].status.text = $"Verrouillé — termine {LevelCatalog.Get(i - 1).Name}";
                else if ((mask & 1) != 0)
                    levelCards[i].status.text = $"Terminé · Meilleure vie au boss : {Mathf.RoundToInt(SaveSystem.GetLevelBestHealth(i) * 100f)} %";
                else
                    levelCards[i].status.text = $"{def.SecretCount} secrets · Boss : {ArenaCatalog.Bosses[def.BossIndex].Name}";
            }
        }

        void OnLevelCardClicked(int index)
        {
            if (!LevelCatalog.IsUnlocked(index)) return;
            HideAllShellPanels();
            UiKit.SetPanel(hudPanel, true);
            director.StartLevel(index);
        }

        // ---- campaign result panels --------------------------------------------------------

        void BuildCampaignPanels()
        {
            // Gate reached: the level is run, the boss is still to come.
            var clearedRt = UiKit.CreatePanel("LevelClearedPanel", canvas.transform, UiKit.Overlay);
            levelClearedPanel = clearedRt.gameObject;
            UiKit.CreateFrame("ClearedFrame", clearedRt, new Vector2(0.08f, 0.26f), new Vector2(0.92f, 0.78f));
            levelClearedTitle = UiKit.Outlined(UiKit.CreateText("ClearedTitle", clearedRt, "PORTAIL ATTEINT", 50, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.64f), new Vector2(0.9f, 0.74f), ApogeeTheme.Gold), 2.5f);
            levelClearedBody = UiKit.CreateText("ClearedBody", clearedRt, "", 26, TextAnchor.UpperCenter,
                new Vector2(0.1f, 0.44f), new Vector2(0.9f, 0.63f), ApogeeTheme.Cream);
            UiKit.CreateButton("ClearedFight", clearedRt, "AU COMBAT !", new Vector2(0.22f, 0.355f), new Vector2(0.78f, 0.43f), StartBossDuel);
            UiKit.CreateButton("ClearedQuit", clearedRt, "ABANDONNER", new Vector2(0.22f, 0.275f), new Vector2(0.78f, 0.345f), AbandonCampaign);

            // Death: back to the last flag, or back to the start.
            var failedRt = UiKit.CreatePanel("LevelFailedPanel", canvas.transform, UiKit.Overlay);
            levelFailedPanel = failedRt.gameObject;
            UiKit.CreateFrame("FailedFrame", failedRt, new Vector2(0.08f, 0.24f), new Vector2(0.92f, 0.78f));
            levelFailedTitle = UiKit.Outlined(UiKit.CreateText("FailedTitle", failedRt, "TU ES TOMBÉ", 50, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.64f), new Vector2(0.9f, 0.74f), new Color(0.88f, 0.32f, 0.22f)), 2.5f);
            levelFailedBody = UiKit.CreateText("FailedBody", failedRt, "", 26, TextAnchor.UpperCenter,
                new Vector2(0.1f, 0.5f), new Vector2(0.9f, 0.63f), ApogeeTheme.Cream);
            levelFailedResumeButton = UiKit.CreateButton("FailedResume", failedRt, "REPRENDRE AU DRAPEAU", new Vector2(0.18f, 0.41f), new Vector2(0.82f, 0.485f), ResumeFromCheckpoint);
            UiKit.CreateButton("FailedRestart", failedRt, "RECOMMENCER LE NIVEAU", new Vector2(0.18f, 0.33f), new Vector2(0.82f, 0.405f), RestartCurrentLevel);
            UiKit.CreateButton("FailedQuit", failedRt, "ABANDONNER", new Vector2(0.18f, 0.25f), new Vector2(0.82f, 0.325f), AbandonCampaign);

            // Boss beaten: the stars.
            var starsRt = UiKit.CreatePanel("LevelStarsPanel", canvas.transform, UiKit.Overlay);
            levelStarsPanel = starsRt.gameObject;
            UiKit.CreateFrame("StarsFrame", starsRt, new Vector2(0.08f, 0.27f), new Vector2(0.92f, 0.8f));
            levelStarsTitle = UiKit.Outlined(UiKit.CreateText("StarsTitle", starsRt, "NIVEAU TERMINÉ", 50, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.68f), new Vector2(0.9f, 0.77f), ApogeeTheme.Gold), 2.5f);
            for (int s = 0; s < 3; s++)
            {
                float x0 = 0.22f + s * 0.20f;
                levelStarsIcons[s] = UiKit.CreateImage($"BigStar_{s}", starsRt, new Vector2(x0, 0.53f), new Vector2(x0 + 0.17f, 0.66f),
                    PlaceholderVisuals.Star(), ApogeeTheme.GoldDark);
            }
            levelStarsBody = UiKit.CreateText("StarsBody", starsRt, "", 24, TextAnchor.UpperCenter,
                new Vector2(0.09f, 0.37f), new Vector2(0.91f, 0.52f), ApogeeTheme.Cream);
            UiKit.CreateButton("StarsContinue", starsRt, "CONTINUER", new Vector2(0.25f, 0.29f), new Vector2(0.75f, 0.36f), ShowExpedition);

            levelClearedPanel.SetActive(false);
            levelFailedPanel.SetActive(false);
            levelStarsPanel.SetActive(false);
        }

        /// <summary>The gate was reached: show the run's tally before the boss steps in.</summary>
        public void ShowLevelCleared(int index, LevelDef def, int secretsFound, float healthLeft)
        {
            pendingBossHealth = healthLeft;
            HideAllShellPanels();
            HideBanner();
            UiKit.SetPanel(levelClearedPanel, true);
            levelClearedTitle.text = "PORTAIL ATTEINT";
            string secrets = def.SecretCount > 0 ? $"Secrets : {secretsFound} / {def.SecretCount}\n" : "";
            levelClearedBody.text = $"{def.Name}\n{secrets}Vie restante : {Mathf.RoundToInt(healthLeft * 100f)} %\n\n" +
                                    $"{ArenaCatalog.Bosses[def.BossIndex].Name} t'attend.\nTu l'affrontes avec la vie qu'il te reste.";
        }

        /// <summary>Campaign death: the level restarts, it does not end.</summary>
        public void ShowLevelFailed(string levelName, bool hasCheckpoint)
        {
            HideAllShellPanels();
            HideBanner();
            UiKit.SetPanel(levelFailedPanel, true);
            levelFailedBody.text = hasCheckpoint
                ? $"{levelName}\nTu repars du dernier drapeau,\navec toute ta vie."
                : $"{levelName}\nAucun drapeau atteint :\nle niveau reprend au début.";
            levelFailedResumeButton.gameObject.SetActive(hasCheckpoint);
        }

        void StartBossDuel()
        {
            var def = director.CurrentLevel;
            int index = director.CurrentLevelIndex;
            int secrets = director.SecretsFound;
            float health = pendingBossHealth;

            HideAllShellPanels();
            if (arena == null) { ShowExpedition(); return; }

            arena.StartCampaignDuel(def.BossIndex, health, def.Name, outcome =>
            {
                switch (outcome)
                {
                    case CampaignDuelOutcome.Won:
                        int mask = 1;
                        if (def.SecretCount > 0 && secrets >= def.SecretCount) mask |= 2;
                        if (health >= 0.6f) mask |= 4;
                        SaveSystem.AddLevelStars(index, mask);
                        director.LeaveCampaign();
                        ShowLevelStars(index, def, mask, secrets, health);
                        break;

                    case CampaignDuelOutcome.RetryLevel:
                        HideAllShellPanels();
                        UiKit.SetPanel(hudPanel, true);
                        director.StartLevel(index);
                        break;

                    default:
                        director.LeaveCampaign();
                        ShowExpedition();
                        break;
                }
            });
        }

        void ShowLevelStars(int index, LevelDef def, int earnedMask, int secrets, float health)
        {
            HideAllShellPanels();
            UiKit.SetPanel(levelStarsPanel, true);

            int total = SaveSystem.GetLevelStars(index);
            for (int s = 0; s < 3; s++)
                levelStarsIcons[s].color = (total & (1 << s)) != 0 ? ApogeeTheme.Gold : new Color(0.35f, 0.22f, 0.16f, 0.8f);

            levelStarsTitle.text = def.Name;
            string secretLine = def.SecretCount > 0
                ? (secrets >= def.SecretCount ? "★ Tous les secrets trouvés" : $"☆ Secrets : {secrets} / {def.SecretCount}")
                : "";
            string healthLine = health >= 0.6f
                ? $"★ Arrivé au boss à {Mathf.RoundToInt(health * 100f)} % de vie"
                : $"☆ Arrivé au boss à {Mathf.RoundToInt(health * 100f)} % de vie (60 % requis)";
            string nextLine = index + 1 < LevelCatalog.Count ? $"\n{LevelCatalog.Get(index + 1).Name} est débloqué !" : "\nTu as terminé l'expédition !";
            levelStarsBody.text = $"★ Niveau terminé\n{secretLine}\n{healthLine}{nextLine}";
        }

        void ResumeFromCheckpoint()
        {
            HideAllShellPanels();
            UiKit.SetPanel(hudPanel, true);
            director.RetryFromCheckpoint();
        }

        void RestartCurrentLevel()
        {
            int index = director.CurrentLevelIndex;
            HideAllShellPanels();
            UiKit.SetPanel(hudPanel, true);
            director.StartLevel(index);
        }

        void AbandonCampaign()
        {
            director.LeaveCampaign();
            ShowExpedition();
        }

        /// <summary>HUD while an authored level is running: progress instead of distance.</summary>
        public void UpdateCampaignHud(Health health, float progress, int secretsFound, int secretsTotal)
        {
            if (healthSlider != null && health != null) healthSlider.value = health.NormalizedHP;
            if (hudDistanceText != null) hudDistanceText.text = $"{Mathf.RoundToInt(progress * 100f)} %";
            if (hudProgressRoot != null && !hudProgressRoot.activeSelf) hudProgressRoot.SetActive(true);
            if (hudProgressFill != null) hudProgressFill.fillAmount = progress;
            if (hudCoinsText != null) hudCoinsText.text = $"Pièces: {SaveSystem.Coins}";
            if (hudMaterialsText != null) hudMaterialsText.text = $"Matériaux: {SaveSystem.Materials}";
            if (hudSecretsText != null)
                hudSecretsText.text = secretsTotal > 0 ? $"Secrets {secretsFound}/{secretsTotal}" : "";
        }

        // ---- runner HUD ----------------------------------------------------------------

        void BuildHudPanel()
        {
            var rt = UiKit.CreatePanel("HudPanel", canvas.transform, new Color(0, 0, 0, 0));
            rt.GetComponent<Image>().raycastTarget = false;
            hudPanel = rt.gameObject;

            var barBgGo = new GameObject("HealthBarBg", typeof(RectTransform));
            barBgGo.transform.SetParent(rt, false);
            var barBgRt = barBgGo.GetComponent<RectTransform>();
            barBgRt.anchorMin = barBgRt.anchorMax = new Vector2(0f, 1f);
            barBgRt.pivot = new Vector2(0f, 1f);
            barBgRt.anchoredPosition = new Vector2(30, -34);
            barBgRt.sizeDelta = new Vector2(330, 46);
            var barBgImg = barBgGo.AddComponent<Image>();
            barBgImg.sprite = ApogeeTheme.Chip;
            barBgImg.type = Image.Type.Sliced;
            barBgImg.raycastTarget = false;

            healthSlider = barBgGo.AddComponent<Slider>();
            healthSlider.minValue = 0;
            healthSlider.maxValue = 1;
            healthSlider.value = 1;
            healthSlider.transition = Selectable.Transition.None;
            healthSlider.interactable = false;
            healthSlider.direction = Slider.Direction.LeftToRight;

            var fillAreaRt = UiKit.CreateRect("FillArea", barBgGo.transform, Vector2.zero, Vector2.one);
            fillAreaRt.offsetMin = new Vector2(8, 8);
            fillAreaRt.offsetMax = new Vector2(-8, -8);

            var fillRt = UiKit.CreateRect("Fill", fillAreaRt, Vector2.zero, new Vector2(0f, 1f));
            var fillImg = fillRt.gameObject.AddComponent<Image>();
            fillImg.color = new Color(0.86f, 0.2f, 0.14f);
            fillImg.raycastTarget = false;

            healthSlider.fillRect = fillRt;
            healthSlider.targetGraphic = fillImg;

            hudDistanceText = UiKit.Outlined(UiKit.CreateTextFixed("DistanceText", rt, "0 m", 48, TextAnchor.UpperCenter,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(320, 64), new Vector2(0, -24), ApogeeTheme.Cream), 2.5f);
            hudCoinsText = UiKit.Outlined(UiKit.CreateTextFixed("CoinsText", rt, "Pièces: 0", 28, TextAnchor.UpperRight,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(320, 45), new Vector2(-30, -28), ApogeeTheme.Gold));
            hudMaterialsText = UiKit.Outlined(UiKit.CreateTextFixed("MaterialsText", rt, "Matériaux: 0", 28, TextAnchor.UpperRight,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(320, 45), new Vector2(-30, -74), ApogeeTheme.Cream));
            hudZoneText = UiKit.Outlined(UiKit.CreateTextFixed("ZoneText", rt, "", 24, TextAnchor.UpperCenter,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(640, 40), new Vector2(0, -86), ApogeeTheme.Gold));

            // Campaign-only: how far through the authored level the player is, and how many
            // of its secrets they have turned up. Hidden during an endless run.
            var progressBg = CreateFixedRect("LevelProgressBg", rt, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(330, 16), new Vector2(30, -86));
            hudProgressRoot = progressBg.gameObject;
            var progressBgImg = progressBg.gameObject.AddComponent<Image>();
            progressBgImg.sprite = ApogeeTheme.Chip;
            progressBgImg.type = Image.Type.Sliced;
            progressBgImg.raycastTarget = false;
            var progressFillRt = UiKit.CreateRect("LevelProgressFill", progressBg, Vector2.zero, Vector2.one);
            progressFillRt.offsetMin = new Vector2(4, 4);
            progressFillRt.offsetMax = new Vector2(-4, -4);
            hudProgressFill = progressFillRt.gameObject.AddComponent<Image>();
            hudProgressFill.sprite = ApogeeTheme.FrameFill;
            hudProgressFill.type = Image.Type.Filled;
            hudProgressFill.fillMethod = Image.FillMethod.Horizontal;
            hudProgressFill.color = ApogeeTheme.Gold;
            hudProgressFill.raycastTarget = false;
            hudProgressRoot.SetActive(false);

            hudSecretsText = UiKit.Outlined(UiKit.CreateTextFixed("SecretsText", rt, "", 22, TextAnchor.UpperLeft,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(330, 34), new Vector2(30, -106), ApogeeTheme.Cream));

            BuildVirtualControls(rt);

            // --- sector banner: fades in for a couple of seconds when a new zone starts ---
            var bannerRt = UiKit.CreateRect("Banner", rt, new Vector2(0f, 0.66f), new Vector2(1f, 0.78f));
            bannerGo = bannerRt.gameObject;
            bannerRt.anchorMin = new Vector2(0.04f, 0.66f);
            bannerRt.anchorMax = new Vector2(0.96f, 0.78f);
            var bannerImg = bannerGo.AddComponent<Image>();
            bannerImg.sprite = ApogeeTheme.Panel;
            bannerImg.type = Image.Type.Sliced;
            bannerImg.color = BannerBgColor;
            bannerImg.raycastTarget = false;
            bannerTitleText = UiKit.Outlined(UiKit.CreateText("BannerTitle", bannerRt, "", 46, TextAnchor.MiddleCenter,
                new Vector2(0.05f, 0.45f), new Vector2(0.95f, 1f), BannerTitleColor), 2f);
            UiKit.FitLabel(bannerTitleText, 46);
            bannerSubtitleText = UiKit.CreateText("BannerSubtitle", bannerRt, "", 24, TextAnchor.MiddleCenter,
                new Vector2(0.05f, 0.05f), new Vector2(0.95f, 0.45f), BannerSubtitleColor);
            UiKit.FitLabel(bannerSubtitleText, 24);
            bannerGo.SetActive(false);
        }

        /// <summary>
        /// Touch controls for the run: a horizontal joystick bottom-left, FIRE and JUMP
        /// buttons bottom-right. Pixel-sized against the 1080x1920 reference so thumbs get
        /// the same targets on every phone; keyboard (arrows / space / F) works alongside.
        /// </summary>
        void BuildVirtualControls(RectTransform hud)
        {
            // Joystick pad (gold-rimmed pill) + knob. Held well clear of the bottom-left
            // corner: phones reserve the screen edges for their own back and home swipes,
            // and a pad sitting in that strip loses the touch mid-drag.
            var pad = CreateFixedRect("Joystick", hud, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(440, 170), new Vector2(104, 96));
            var padImg = pad.gameObject.AddComponent<Image>();
            padImg.sprite = ApogeeTheme.Chip;
            padImg.type = Image.Type.Sliced;
            padImg.color = new Color(1f, 1f, 1f, 0.8f);
            var knob = CreateFixedRect("Knob", pad, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(140, 140), Vector2.zero);
            var knobImg = knob.gameObject.AddComponent<Image>();
            knobImg.sprite = ApogeeTheme.Round;
            knobImg.color = new Color(1f, 1f, 1f, 0.92f);
            knobImg.raycastTarget = false;
            var joystick = pad.gameObject.AddComponent<VirtualJoystick>();
            joystick.knob = knob;
            UiKit.CreateText("JoyHint", pad, "‹                 ›", 44, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, new Color(ApogeeTheme.Gold.r, ApogeeTheme.Gold.g, ApogeeTheme.Gold.b, 0.6f));

            // Jump (big, bottom-right) and fire (left of it).
            var jump = CreateFixedRect("JumpButton", hud, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(230, 230), new Vector2(-40, 60));
            var jumpImg = jump.gameObject.AddComponent<Image>();
            jumpImg.sprite = ApogeeTheme.Round;
            jumpImg.color = new Color(1f, 1f, 1f, 0.9f);
            var jumpBtn = jump.gameObject.AddComponent<HoldButton>();
            jumpBtn.onDown = MobileInput.PressJump;
            jumpBtn.onUp = MobileInput.ReleaseJump;
            UiKit.Outlined(UiKit.CreateText("JumpLabel", jump, "SAUT", 34, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, ApogeeTheme.Cream));

            var fire = CreateFixedRect("FireButton", hud, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(180, 180), new Vector2(-300, 100));
            var fireImg = fire.gameObject.AddComponent<Image>();
            fireImg.sprite = ApogeeTheme.Round;
            fireImg.color = new Color(1.0f, 0.72f, 0.4f, 0.9f);
            var fireBtn = fire.gameObject.AddComponent<HoldButton>();
            fireBtn.onDown = () => MobileInput.FireHeld = true;
            fireBtn.onUp = () => MobileInput.FireHeld = false;
            UiKit.Outlined(UiKit.CreateText("FireLabel", fire, "TIR", 30, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, ApogeeTheme.Cream));
        }

        static RectTransform CreateFixedRect(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 size, Vector2 anchoredPos)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;
            return rt;
        }

        static readonly Color BannerTitleColor = ApogeeTheme.Gold;
        static readonly Color BannerSubtitleColor = ApogeeTheme.Cream;
        static readonly Color BannerBgColor = new Color(1f, 1f, 1f, 0.92f);

        /// <summary>Shows a large centered announcement (new sector, horde, ...) for a few seconds.</summary>
        public void ShowBanner(string title, string subtitle, float duration = 2.6f)
        {
            if (bannerGo == null) return;
            if (bannerRoutine != null) StopCoroutine(bannerRoutine);
            bannerRoutine = StartCoroutine(BannerRoutine(title, subtitle, duration));
        }

        IEnumerator BannerRoutine(string title, string subtitle, float duration)
        {
            bannerGo.SetActive(true);
            bannerTitleText.text = title;
            bannerSubtitleText.text = subtitle ?? "";
            var bannerImg = bannerGo.GetComponent<Image>();

            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float alpha = Mathf.Clamp01(Mathf.Min(t / 0.25f, (duration - t) / 0.5f));
                bannerTitleText.color = new Color(BannerTitleColor.r, BannerTitleColor.g, BannerTitleColor.b, alpha);
                bannerSubtitleText.color = new Color(BannerSubtitleColor.r, BannerSubtitleColor.g, BannerSubtitleColor.b, alpha);
                bannerImg.color = new Color(BannerBgColor.r, BannerBgColor.g, BannerBgColor.b, BannerBgColor.a * alpha);
                yield return null;
            }
            bannerGo.SetActive(false);
            bannerRoutine = null;
        }

        void HideBanner()
        {
            if (bannerRoutine != null) StopCoroutine(bannerRoutine);
            bannerRoutine = null;
            if (bannerGo != null) bannerGo.SetActive(false);
        }

        public void SetZoneLabel(string label)
        {
            if (hudZoneText != null) hudZoneText.text = label;
        }

        void BuildGameOverPanel()
        {
            var rt = UiKit.CreatePanel("GameOverPanel", canvas.transform, UiKit.Overlay);
            gameOverPanel = rt.gameObject;

            UiKit.CreateFrame("GameOverFrame", rt, new Vector2(0.08f, 0.19f), new Vector2(0.92f, 0.81f));
            UiKit.Outlined(UiKit.CreateText("GameOverTitle", rt, "FIN DE LA COURSE", 56, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.67f), new Vector2(0.9f, 0.77f), ApogeeTheme.Gold), 2.5f);
            gameOverDistanceText = UiKit.CreateText("GameOverDistance", rt, "", 34, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.59f), new Vector2(0.9f, 0.65f), ApogeeTheme.Cream);
            gameOverBestText = UiKit.CreateText("GameOverBest", rt, "", 28, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.53f), new Vector2(0.9f, 0.59f), ApogeeTheme.Gold);

            UiKit.CreateButton("RestartButton", rt, "REJOUER", new Vector2(0.25f, 0.40f), new Vector2(0.75f, 0.47f), StartRunner);
            UiKit.CreateButton("ShopButtonGameOver", rt, "AMÉLIORATIONS", new Vector2(0.25f, 0.31f), new Vector2(0.75f, 0.38f), ShowShop);
            UiKit.CreateButton("MenuButtonGameOver", rt, "MENU", new Vector2(0.25f, 0.22f), new Vector2(0.75f, 0.29f), ShowRunnerModes);
        }

        // ---- shop ----------------------------------------------------------------------

        void BuildShopPanel()
        {
            var rt = UiKit.CreatePanel("ShopPanel", canvas.transform, Color.white);
            shopPanel = rt.gameObject;
            ApplyOpaqueBackdrop(rt);

            CreateTitle(rt, "AMÉLIORATIONS", 0.885f, 0.955f);
            shopWalletText = CreateChip("Wallet", rt, new Vector2(0.22f, 0.825f), new Vector2(0.78f, 0.868f), 26);
            UiKit.CreateText("ShopHint", rt, "Elles comptent dans le Runner, l'Expédition, l'Arène, la Barricade et l'Invasion", 20, TextAnchor.MiddleCenter,
                new Vector2(0.05f, 0.78f), new Vector2(0.95f, 0.815f), UiKit.TextDim);
            UiKit.CreateFrame("ShopFrame", rt, new Vector2(0.04f, 0.115f), new Vector2(0.96f, 0.77f));

            var stats = new[] { UpgradeStat.Speed, UpgradeStat.FirePower, UpgradeStat.MaxHealth, UpgradeStat.Armor, UpgradeStat.DoubleJump, UpgradeStat.Magnet, UpgradeStat.Drone };
            // Rows tightened when the drone was added, so the seventh still clears the
            // frame's bottom edge and the RETOUR button under it.
            const float startY = 0.725f;
            const float rowH = 0.088f;

            for (int i = 0; i < stats.Length; i++)
            {
                var stat = stats[i];
                float yMax = startY - i * rowH;
                float yMin = yMax - rowH + 0.02f;

                var label = UiKit.Outlined(UiKit.CreateText($"Label_{stat}", rt, StatLabel(stat), 28, TextAnchor.MiddleLeft,
                    new Vector2(0.08f, yMin), new Vector2(0.50f, yMax), ApogeeTheme.Cream), 1.5f);
                UiKit.FitLabel(label, 28);
                shopLevelTexts[stat] = UiKit.CreateText($"Level_{stat}", rt, "Niveau 0/10", 22, TextAnchor.MiddleLeft,
                    new Vector2(0.51f, yMin), new Vector2(0.74f, yMax), ApogeeTheme.Gold);
                UiKit.FitLabel(shopLevelTexts[stat], 22);

                var capturedStat = stat;
                shopButtons[stat] = UiKit.CreateButton($"Buy_{stat}", rt, "+1", new Vector2(0.76f, yMin), new Vector2(0.94f, yMax),
                    () => OnBuyClicked(capturedStat), 22);
            }

            UiKit.CreateButton("ShopBackButton", rt, "RETOUR", new Vector2(0.32f, 0.02f), new Vector2(0.68f, 0.095f), ShowHub);
        }

        void OnBuyClicked(UpgradeStat stat)
        {
            UpgradeManager.TryPurchase(stat);
            RefreshShop();
        }

        void RefreshShop()
        {
            shopWalletText.text = $"Pièces: {SaveSystem.Coins}    Matériaux: {SaveSystem.Materials}";
            foreach (var stat in shopLevelTexts.Keys)
            {
                int level = SaveSystem.GetLevel(stat);
                int max = UpgradeManager.MaxLevelFor(stat);
                shopLevelTexts[stat].text = $"Niveau {level}/{max}";

                var btn = shopButtons[stat];
                var label = UiKit.ButtonLabel(btn);
                if (level >= max)
                {
                    label.text = "MAX";
                    btn.interactable = false;
                }
                else
                {
                    int cost = UpgradeManager.CostForNextLevel(stat);
                    string currency = stat == UpgradeStat.Armor || stat == UpgradeStat.Drone ? "mat." : "pièces";
                    label.text = $"+1\n({cost} {currency})";
                    btn.interactable = true;
                }
            }
        }

        // ---- ad overlay ----------------------------------------------------------------

        void BuildAdOverlay()
        {
            var rt = UiKit.CreatePanel("AdOverlay", canvas.transform, new Color(0f, 0f, 0f, 0.92f));
            adOverlayText = UiKit.CreateText("AdOverlayText", rt, "Publicité en cours...", 40, TextAnchor.MiddleCenter,
                Vector2.zero, Vector2.one, Color.white);
            adOverlay = rt.gameObject;
            adOverlay.SetActive(false);
        }

        public void ShowAdOverlay(bool visible, string text = null)
        {
            if (adOverlay == null) return;
            if (text != null) adOverlayText.text = text;
            adOverlay.SetActive(visible);
        }

        // ---- navigation ----------------------------------------------------------------

        void HideAllShellPanels()
        {
            UiKit.SetPanel(hubPanel, false);
            UiKit.SetPanel(charactersPanel, false);
            UiKit.SetPanel(hudPanel, false);
            UiKit.SetPanel(gameOverPanel, false);
            UiKit.SetPanel(shopPanel, false);
            UiKit.SetPanel(expeditionPanel, false);
            UiKit.SetPanel(runnerModesPanel, false);
            UiKit.SetPanel(levelClearedPanel, false);
            UiKit.SetPanel(levelFailedPanel, false);
            UiKit.SetPanel(levelStarsPanel, false);
            if (previewCamera != null) previewCamera.enabled = false;
        }

        public void ShowHub()
        {
            Time.timeScale = 1f;
            if (director != null && director.InCampaign) director.LeaveCampaign();
            foreach (var game in miniGames) game.Exit();
            HideAllShellPanels();
            HideBanner();
            UiKit.SetPanel(hubPanel, true);
            if (previewCamera != null) previewCamera.enabled = true;
            RefreshHub();
        }

        public void ShowCharacters()
        {
            HideAllShellPanels();
            UiKit.SetPanel(charactersPanel, true);
            selectedCharacterIndex = Mathf.Max(0, Array.FindIndex(SkinCatalog.All, s => s.Id == SaveSystem.SelectedSkinId));
            RefreshCharacters();
        }

        public void ShowShop()
        {
            HideAllShellPanels();
            RefreshShop();
            UiKit.SetPanel(shopPanel, true);
        }

        void StartRunner()
        {
            HideAllShellPanels();
            UiKit.SetPanel(hudPanel, true);
            director.StartRun();
        }

        void EnterMiniGame(MiniGame game)
        {
            HideAllShellPanels();
            game.Enter();
        }

        public void ShowGameOver(float distance)
        {
            HideAllShellPanels();
            HideBanner();
            UiKit.SetPanel(gameOverPanel, true);

            bool isNewRecord = distance >= SaveSystem.BestDistance;
            gameOverDistanceText.text = $"Distance parcourue : {FormatDistance(distance)}";
            gameOverBestText.text = isNewRecord ? "Nouveau record !" : $"Record : {FormatDistance(SaveSystem.BestDistance)}";
        }

        public void UpdateHud(Health health, float distance, int coins, int materials)
        {
            if (healthSlider != null && health != null) healthSlider.value = health.NormalizedHP;
            if (hudDistanceText != null) hudDistanceText.text = FormatDistance(distance);
            if (hudCoinsText != null) hudCoinsText.text = $"Pièces: {coins}";
            if (hudMaterialsText != null) hudMaterialsText.text = $"Matériaux: {materials}";
            if (hudProgressRoot != null && hudProgressRoot.activeSelf) hudProgressRoot.SetActive(false);
            if (hudSecretsText != null && hudSecretsText.text.Length > 0) hudSecretsText.text = "";
        }
    }
}
