using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Platformer.Mechanics;

namespace Platformer.Survival
{
    /// <summary>
    /// Entire survival-mode UI (character home screen, HUD, game over, upgrade shop) built
    /// at runtime with legacy uGUI (Text/Image/Button/Slider + the built-in LegacyRuntime
    /// font) so no hand-authored Canvas prefab or external font/asset reference is needed.
    /// </summary>
    public class RuntimeUI : MonoBehaviour
    {
        SurvivalDirector director;
        Font font;

        Canvas canvas;
        GameObject homePanel, hudPanel, gameOverPanel, shopPanel;

        const int PreviewLayer = 31;
        Camera previewCamera;
        RenderTexture previewTexture;
        GameObject previewCharacter;

        Text titleBestText;
        Text homeWalletText;
        Text skinNameText, skinStatusText;
        Button skinActionButton;
        int currentSkinIndex;

        Slider healthSlider;
        Text hudDistanceText, hudCoinsText, hudMaterialsText;
        Text gameOverDistanceText, gameOverBestText;
        Text shopWalletText;
        GameObject adOverlay;
        Text adOverlayText;

        readonly Dictionary<UpgradeStat, Text> shopLevelTexts = new();
        readonly Dictionary<UpgradeStat, Button> shopButtons = new();

        public void Init(SurvivalDirector survivalDirector)
        {
            director = survivalDirector;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            EnsureEventSystem();
            SetupCharacterPreview();
            BuildCanvas();
            BuildVignette();
            BuildHomePanel();
            BuildHudPanel();
            BuildGameOverPanel();
            BuildShopPanel();
            BuildAdOverlay();

            ShowHome();
        }

        /// <summary>
        /// Builds an isolated character preview: a lightweight clone of the player's sprite
        /// and animator (same idle/run animations, no physics/gameplay components) parked
        /// far outside the play area, rendered by its own camera into a RenderTexture on a
        /// dedicated layer. The home screen shows this texture instead of compositing over
        /// the live game camera, so it reads as a genuinely separate page - no ground,
        /// zombies or world geometry can ever show through.
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
            previewCamera.orthographicSize = 1.5f;
            previewCamera.cullingMask = 1 << PreviewLayer;
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            previewCamera.enabled = false;

            previewTexture = new RenderTexture(512, 512, 16) { name = "SkinPreviewRT" };
            previewCamera.targetTexture = previewTexture;
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

            canvasGo.AddComponent<GraphicRaycaster>();
        }

        void BuildVignette()
        {
            var go = new GameObject("Vignette", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var img = go.AddComponent<Image>();
            img.sprite = PlaceholderVisuals.Vignette();
            img.raycastTarget = false;
        }

        // ---- generic UI builders -------------------------------------------------

        RectTransform CreatePanel(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            go.AddComponent<Image>().color = color;
            return rt;
        }

        Text CreateText(string name, Transform parent, string content, int size, TextAnchor alignment,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 sizeDelta, Vector2 anchoredPos, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.sizeDelta = sizeDelta;
            rt.anchoredPosition = anchoredPos;

            var text = go.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.alignment = alignment;
            text.text = content;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            return text;
        }

        static void ApplyButtonStyle(Button btn, Image img)
        {
            img.color = new Color(0.36f, 0.14f, 0.08f);
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = new Color(0.5f, 0.2f, 0.1f);
            colors.pressedColor = new Color(0.25f, 0.09f, 0.05f);
            colors.disabledColor = new Color(0.2f, 0.18f, 0.16f);
            btn.colors = colors;
        }

        /// <summary>Fixed-size button centered via a pixel offset from screen center - only
        /// safe for offsets small enough to stay on-screen across aspect ratios (use
        /// CreateButtonFraction instead for anything placed far from center).</summary>
        Button CreateButton(string name, Transform parent, string label, Vector2 anchoredPos, Vector2 sizeDelta, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = sizeDelta;
            rt.anchoredPosition = anchoredPos;

            var img = go.AddComponent<Image>();
            var btn = go.AddComponent<Button>();
            ApplyButtonStyle(btn, img);
            if (onClick != null) btn.onClick.AddListener(onClick);

            CreateText(name + "_Label", go.transform, label, 32, TextAnchor.MiddleCenter,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, Color.white);

            return btn;
        }

        /// <summary>Button anchored by screen fraction (always on-screen regardless of aspect
        /// ratio) - use this for anything not near dead-center.</summary>
        Button CreateButtonFraction(string name, Transform parent, string label, Vector2 anchorMin, Vector2 anchorMax, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var img = go.AddComponent<Image>();
            var btn = go.AddComponent<Button>();
            ApplyButtonStyle(btn, img);
            if (onClick != null) btn.onClick.AddListener(onClick);

            CreateText(name + "_Label", go.transform, label, 32, TextAnchor.MiddleCenter,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, Color.white);

            return btn;
        }

        /// <summary>Fraction-anchored button (unlike CreateButton's fixed-pixel centering) used for the skin carousel arrows.</summary>
        Button CreateArrowButton(string name, Transform parent, string label, Vector2 anchorMin, Vector2 anchorMax, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var img = go.AddComponent<Image>();
            img.color = new Color(0.36f, 0.14f, 0.08f, 0.85f);

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            if (onClick != null) btn.onClick.AddListener(onClick);

            CreateText(name + "_Label", go.transform, label, 44, TextAnchor.MiddleCenter,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, Color.white);

            return btn;
        }

        static void SetPanel(GameObject panel, bool active)
        {
            if (panel != null) panel.SetActive(active);
        }

        static string StatLabel(UpgradeStat stat) => stat switch
        {
            UpgradeStat.Speed => "Vitesse",
            UpgradeStat.FirePower => "Puissance de tir",
            UpgradeStat.MaxHealth => "Vie max",
            UpgradeStat.Armor => "Armure",
            _ => stat.ToString()
        };

        static string FormatDistance(float meters) => $"{Mathf.FloorToInt(meters)} m";

        // ---- panels ----------------------------------------------------------------

        /// <summary>
        /// The character home screen (Brawl Stars style): a fully opaque backdrop (not the
        /// live game world) with the isolated character preview (see SetupCharacterPreview)
        /// composited via a RawImage, a skin carousel just below it, and JOUER/AMÉLIORATIONS
        /// at the bottom. Every element is anchored by screen fraction, never a fixed pixel
        /// offset far from center, so nothing can end up positioned off-screen on an aspect
        /// ratio other than the reference one.
        /// </summary>
        void BuildHomePanel()
        {
            var go = new GameObject("HomePanel", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            homePanel = go;

            var bgImg = go.AddComponent<Image>();
            bgImg.sprite = PlaceholderVisuals.VerticalGradient(PlaceholderVisuals.SkyColor, new Color(0.07f, 0.06f, 0.05f));

            var bannerBg = new GameObject("TitleBannerBg", typeof(RectTransform));
            bannerBg.transform.SetParent(rt, false);
            var bannerRt = bannerBg.GetComponent<RectTransform>();
            bannerRt.anchorMin = new Vector2(0f, 0.90f);
            bannerRt.anchorMax = new Vector2(1f, 1f);
            bannerRt.offsetMin = Vector2.zero;
            bannerRt.offsetMax = Vector2.zero;
            bannerBg.AddComponent<Image>().color = new Color(0.03f, 0.02f, 0.02f, 0.55f);

            CreateText("Title", rt, "SURVIVRE À LA FIN DU MONDE", 38, TextAnchor.MiddleCenter,
                new Vector2(0.05f, 0.90f), new Vector2(0.95f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0.82f, 0.76f, 0.66f));

            titleBestText = CreateText("TitleBest", rt, "", 26, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.855f), new Vector2(0.9f, 0.895f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0.75f, 0.65f, 0.35f));

            homeWalletText = CreateText("HomeWallet", rt, "", 24, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.815f), new Vector2(0.9f, 0.855f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, PlaceholderVisuals.CoinColor);

            // --- isolated character preview (see SetupCharacterPreview) ---
            var previewGo = new GameObject("CharacterPreview", typeof(RectTransform));
            previewGo.transform.SetParent(rt, false);
            var previewRt = previewGo.GetComponent<RectTransform>();
            previewRt.anchorMin = new Vector2(0.20f, 0.40f);
            previewRt.anchorMax = new Vector2(0.80f, 0.80f);
            previewRt.offsetMin = Vector2.zero;
            previewRt.offsetMax = Vector2.zero;
            var previewImg = previewGo.AddComponent<RawImage>();
            previewImg.texture = previewTexture;
            previewImg.raycastTarget = false;

            // --- skin carousel, sitting just below the character preview ---
            CreateArrowButton("SkinPrev", rt, "<", new Vector2(0.04f, 0.30f), new Vector2(0.15f, 0.40f), OnSkinPrevClicked);
            CreateArrowButton("SkinNext", rt, ">", new Vector2(0.85f, 0.30f), new Vector2(0.96f, 0.40f), OnSkinNextClicked);

            skinNameText = CreateText("SkinName", rt, "", 32, TextAnchor.MiddleCenter,
                new Vector2(0.18f, 0.335f), new Vector2(0.82f, 0.395f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, Color.white);

            skinStatusText = CreateText("SkinStatus", rt, "", 22, TextAnchor.MiddleCenter,
                new Vector2(0.18f, 0.29f), new Vector2(0.82f, 0.33f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0.75f, 0.65f, 0.35f));

            var actionGo = new GameObject("SkinActionButton", typeof(RectTransform));
            actionGo.transform.SetParent(rt, false);
            var actionRt = actionGo.GetComponent<RectTransform>();
            actionRt.anchorMin = new Vector2(0.30f, 0.20f);
            actionRt.anchorMax = new Vector2(0.70f, 0.275f);
            actionRt.offsetMin = Vector2.zero;
            actionRt.offsetMax = Vector2.zero;

            var actionImg = actionGo.AddComponent<Image>();
            skinActionButton = actionGo.AddComponent<Button>();
            ApplyButtonStyle(skinActionButton, actionImg);
            skinActionButton.onClick.AddListener(OnSkinActionClicked);

            CreateText("SkinActionLabel", actionGo.transform, "", 24, TextAnchor.MiddleCenter,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, Color.white);

            // --- bottom buttons (fraction-anchored so they can never end up off-screen) ---
            CreateButtonFraction("ShopButtonHome", rt, "AMÉLIORATIONS", new Vector2(0.30f, 0.105f), new Vector2(0.70f, 0.175f), ShowShop);
            CreateButtonFraction("PlayButton", rt, "JOUER", new Vector2(0.25f, 0.02f), new Vector2(0.75f, 0.095f), OnPlayClicked);
        }

        void BuildHudPanel()
        {
            var rt = CreatePanel("HudPanel", canvas.transform, new Color(0, 0, 0, 0));
            hudPanel = rt.gameObject;

            var barBgGo = new GameObject("HealthBarBg", typeof(RectTransform));
            barBgGo.transform.SetParent(rt, false);
            var barBgRt = barBgGo.GetComponent<RectTransform>();
            barBgRt.anchorMin = barBgRt.anchorMax = new Vector2(0f, 1f);
            barBgRt.pivot = new Vector2(0f, 1f);
            barBgRt.anchoredPosition = new Vector2(30, -30);
            barBgRt.sizeDelta = new Vector2(320, 40);
            barBgGo.AddComponent<Image>().color = new Color(0, 0, 0, 0.5f);

            healthSlider = barBgGo.AddComponent<Slider>();
            healthSlider.minValue = 0;
            healthSlider.maxValue = 1;
            healthSlider.value = 1;
            healthSlider.transition = Selectable.Transition.None;
            healthSlider.interactable = false;
            healthSlider.direction = Slider.Direction.LeftToRight;

            var fillAreaGo = new GameObject("FillArea", typeof(RectTransform));
            fillAreaGo.transform.SetParent(barBgGo.transform, false);
            var fillAreaRt = fillAreaGo.GetComponent<RectTransform>();
            fillAreaRt.anchorMin = Vector2.zero;
            fillAreaRt.anchorMax = Vector2.one;
            fillAreaRt.offsetMin = new Vector2(4, 4);
            fillAreaRt.offsetMax = new Vector2(-4, -4);

            var fillGo = new GameObject("Fill", typeof(RectTransform));
            fillGo.transform.SetParent(fillAreaGo.transform, false);
            var fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = new Vector2(0f, 1f);
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;
            var fillImg = fillGo.AddComponent<Image>();
            fillImg.color = new Color(0.8f, 0.15f, 0.15f);

            healthSlider.fillRect = fillRt;
            healthSlider.targetGraphic = fillImg;

            hudDistanceText = CreateText("DistanceText", rt, "0 m", 44, TextAnchor.UpperCenter,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(320, 60), new Vector2(0, -25), Color.white);

            hudCoinsText = CreateText("CoinsText", rt, "Pièces: 0", 30, TextAnchor.UpperRight,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(300, 45), new Vector2(-30, -25), PlaceholderVisuals.CoinColor);

            hudMaterialsText = CreateText("MaterialsText", rt, "Matériaux: 0", 30, TextAnchor.UpperRight,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(300, 45), new Vector2(-30, -75), PlaceholderVisuals.MaterialColor);
        }

        void BuildGameOverPanel()
        {
            var rt = CreatePanel("GameOverPanel", canvas.transform, new Color(0.04f, 0.03f, 0.02f, 0.88f));
            gameOverPanel = rt.gameObject;

            CreateText("GameOverTitle", rt, "GAME OVER", 64, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.62f), new Vector2(0.9f, 0.75f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0.75f, 0.15f, 0.1f));

            gameOverDistanceText = CreateText("GameOverDistance", rt, "", 36, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.53f), new Vector2(0.9f, 0.6f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0.82f, 0.76f, 0.66f));

            gameOverBestText = CreateText("GameOverBest", rt, "", 30, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.46f), new Vector2(0.9f, 0.53f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0.85f, 0.85f, 0.4f));

            CreateButton("RestartButton", rt, "REJOUER", new Vector2(0, -40), new Vector2(420, 110), OnPlayClicked);
            CreateButton("ShopButtonGameOver", rt, "AMÉLIORATIONS", new Vector2(0, -190), new Vector2(420, 110), ShowShop);
        }

        void BuildShopPanel()
        {
            var rt = CreatePanel("ShopPanel", canvas.transform, new Color(0.05f, 0.04f, 0.03f, 0.9f));
            shopPanel = rt.gameObject;

            CreateText("ShopTitle", rt, "AMÉLIORATIONS", 46, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.85f), new Vector2(0.9f, 0.95f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0.82f, 0.76f, 0.66f));

            shopWalletText = CreateText("Wallet", rt, "", 30, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.78f), new Vector2(0.9f, 0.85f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, Color.yellow);

            var stats = new[] { UpgradeStat.Speed, UpgradeStat.FirePower, UpgradeStat.MaxHealth, UpgradeStat.Armor };
            const float startY = 0.68f;
            const float rowH = 0.13f;

            for (int i = 0; i < stats.Length; i++)
            {
                var stat = stats[i];
                float yMax = startY - i * rowH;
                float yMin = yMax - rowH + 0.02f;

                CreateText($"Label_{stat}", rt, StatLabel(stat), 30, TextAnchor.MiddleLeft,
                    new Vector2(0.08f, yMin), new Vector2(0.52f, yMax), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, Color.white);

                var levelText = CreateText($"Level_{stat}", rt, "Niveau 0/10", 24, TextAnchor.MiddleLeft,
                    new Vector2(0.52f, yMin), new Vector2(0.74f, yMax), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0.8f, 0.8f, 0.8f));
                shopLevelTexts[stat] = levelText;

                var btnGo = new GameObject($"Buy_{stat}", typeof(RectTransform));
                btnGo.transform.SetParent(rt, false);
                var btnRt = btnGo.GetComponent<RectTransform>();
                btnRt.anchorMin = new Vector2(0.76f, yMin);
                btnRt.anchorMax = new Vector2(0.94f, yMax);
                btnRt.offsetMin = Vector2.zero;
                btnRt.offsetMax = Vector2.zero;

                var btnImg = btnGo.AddComponent<Image>();
                btnImg.color = new Color(0.36f, 0.14f, 0.08f);
                var btn = btnGo.AddComponent<Button>();
                btn.targetGraphic = btnImg;
                var capturedStat = stat;
                btn.onClick.AddListener(() => OnBuyClicked(capturedStat));

                CreateText($"BuyLabel_{stat}", btnGo.transform, "+1", 22, TextAnchor.MiddleCenter,
                    Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, Color.white);

                shopButtons[stat] = btn;
            }

            CreateButtonFraction("ShopBackButton", rt, "RETOUR", new Vector2(0.32f, 0.02f), new Vector2(0.68f, 0.095f), ShowHome);
        }

        void BuildAdOverlay()
        {
            var go = new GameObject("AdOverlay", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            go.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.92f);

            adOverlayText = CreateText("AdOverlayText", go.transform, "Publicité en cours...", 40, TextAnchor.MiddleCenter,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, Color.white);

            adOverlay = go;
            adOverlay.SetActive(false);
        }

        // ---- state transitions -------------------------------------------------

        void OnPlayClicked()
        {
            SetPanel(homePanel, false);
            SetPanel(gameOverPanel, false);
            SetPanel(shopPanel, false);
            SetPanel(hudPanel, true);
            if (previewCamera != null) previewCamera.enabled = false;
            director.StartRun();
        }

        void OnBuyClicked(UpgradeStat stat)
        {
            UpgradeManager.TryPurchase(stat);
            RefreshShop();
        }

        void OnSkinPrevClicked()
        {
            currentSkinIndex = (currentSkinIndex - 1 + SkinCatalog.All.Length) % SkinCatalog.All.Length;
            RefreshSkinCarousel();
        }

        void OnSkinNextClicked()
        {
            currentSkinIndex = (currentSkinIndex + 1) % SkinCatalog.All.Length;
            RefreshSkinCarousel();
        }

        void OnSkinActionClicked()
        {
            var skin = SkinCatalog.All[currentSkinIndex];
            bool unlocked = SaveSystem.IsSkinUnlocked(skin.Id);

            if (unlocked)
            {
                SaveSystem.SelectedSkinId = skin.Id;
                SkinCatalog.ApplyToPlayer(director.Player);
                RefreshSkinCarousel();
                return;
            }

            if (skin.UnlockType == SkinUnlockType.Coins)
            {
                if (SaveSystem.TrySpendCoins(skin.CoinCost))
                {
                    SaveSystem.UnlockSkin(skin.Id);
                    SaveSystem.SelectedSkinId = skin.Id;
                    SkinCatalog.ApplyToPlayer(director.Player);
                    RefreshSkinCarousel();
                }
            }
            else if (skin.UnlockType == SkinUnlockType.Ad)
            {
                adOverlay.SetActive(true);
                adOverlayText.text = "Publicité en cours...";
                AdService.ShowRewardedAd(this, () =>
                {
                    adOverlay.SetActive(false);
                    SaveSystem.UnlockSkin(skin.Id);
                    SaveSystem.SelectedSkinId = skin.Id;
                    SkinCatalog.ApplyToPlayer(director.Player);
                    RefreshSkinCarousel();
                });
            }
        }

        public void ShowHome()
        {
            Time.timeScale = 1f;
            SetPanel(homePanel, true);
            SetPanel(hudPanel, false);
            SetPanel(gameOverPanel, false);
            SetPanel(shopPanel, false);

            if (previewCamera != null) previewCamera.enabled = true;

            currentSkinIndex = Array.FindIndex(SkinCatalog.All, s => s.Id == SaveSystem.SelectedSkinId);
            if (currentSkinIndex < 0) currentSkinIndex = 0;
            RefreshSkinCarousel();

            titleBestText.text = SaveSystem.BestDistance > 0f
                ? $"Record : {FormatDistance(SaveSystem.BestDistance)}"
                : "Aucun record pour l'instant";
        }

        public void ShowShop()
        {
            RefreshShop();
            SetPanel(homePanel, false);
            SetPanel(hudPanel, false);
            SetPanel(gameOverPanel, false);
            SetPanel(shopPanel, true);
            if (previewCamera != null) previewCamera.enabled = false;
        }

        public void ShowGameOver(float distance)
        {
            SetPanel(homePanel, false);
            SetPanel(hudPanel, false);
            SetPanel(shopPanel, false);
            SetPanel(gameOverPanel, true);

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
        }

        void RefreshShop()
        {
            shopWalletText.text = $"Pièces: {SaveSystem.Coins}    Matériaux: {SaveSystem.Materials}";
            foreach (var stat in shopLevelTexts.Keys)
            {
                int level = SaveSystem.GetLevel(stat);
                shopLevelTexts[stat].text = $"Niveau {level}/{UpgradeManager.MaxLevel}";

                var btn = shopButtons[stat];
                var label = btn.GetComponentInChildren<Text>();
                if (level >= UpgradeManager.MaxLevel)
                {
                    label.text = "MAX";
                    btn.interactable = false;
                }
                else
                {
                    int cost = UpgradeManager.CostForNextLevel(stat);
                    string currency = stat == UpgradeStat.Armor ? "mat." : "pièces";
                    label.text = $"+1\n({cost} {currency})";
                    btn.interactable = true;
                }
            }
        }

        /// <summary>
        /// Refreshes the carousel for SkinCatalog.All[currentSkinIndex] and live-previews
        /// its tint on the isolated preview character (never the real player, which only
        /// ever wears the actually-equipped skin). The preview always follows whatever is
        /// being browsed, and ShowHome resets the carousel back to the equipped skin on
        /// every visit.
        /// </summary>
        void RefreshSkinCarousel()
        {
            var skin = SkinCatalog.All[currentSkinIndex];
            bool unlocked = SaveSystem.IsSkinUnlocked(skin.Id);

            skinNameText.text = skin.Name;
            homeWalletText.text = $"Pièces: {SaveSystem.Coins}";

            if (previewCharacter != null)
            {
                var sr = previewCharacter.GetComponent<SpriteRenderer>();
                if (sr != null) sr.color = skin.Tint;
            }

            var label = skinActionButton.GetComponentInChildren<Text>();
            if (unlocked)
            {
                bool selected = SaveSystem.SelectedSkinId == skin.Id;
                skinStatusText.text = selected ? "Équipé" : "Débloqué";
                label.text = selected ? "ÉQUIPÉ" : "ÉQUIPER";
                skinActionButton.interactable = !selected;
            }
            else if (skin.UnlockType == SkinUnlockType.Coins)
            {
                skinStatusText.text = $"Verrouillé — {skin.CoinCost} pièces";
                label.text = $"ACHETER ({skin.CoinCost})";
                skinActionButton.interactable = true;
            }
            else
            {
                skinStatusText.text = "Verrouillé — débloquer avec une pub";
                label.text = "REGARDER UNE PUB";
                skinActionButton.interactable = true;
            }
        }
    }
}
