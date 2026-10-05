using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Platformer.Survival
{
    /// <summary>
    /// The home screen: the Apogée key art drifting slowly behind falling leaves, a top bar
    /// with the hero's portrait and the currencies, the logo, a swipeable carousel of large
    /// painted game cards (one per game shown on the hub) with a big JOUER button under it,
    /// and a bottom bar of tiles for the characters, weapons, missions and upgrades.
    /// Every piece is placed in reference units by HubLayout, so the same screen works
    /// upright on a phone and sideways in a browser.
    /// </summary>
    public partial class RuntimeUI
    {
        readonly List<IconText> hubPills = new();
        List<Func<string>> hubCardBestProviders = new();
        Image hubAvatar;
        Text hubMissionsBadge;
        bool missionsFromHub;

        void BuildHubPanel()
        {
            var rt = UiKit.CreateRect("HubPanel", canvas.transform, Vector2.zero, Vector2.one);
            hubPanel = rt.gameObject;
            rt.gameObject.AddComponent<Image>().color = ApogeeTheme.Maroon;

            // Backdrop: the key art, breathing in and out very slowly, under soft shades.
            var art = UiKit.CreateArtBackdrop("KeyArt", rt, "home_art", 0.29f, 0.5f);
            art.gameObject.AddComponent<HubDrift>();
            var topShade = UiKit.CreateImage("TopShade", rt, new Vector2(0f, 0.72f), Vector2.one, ApogeeTheme.VerticalFade, new Color(0.14f, 0.03f, 0.03f, 0.75f), false);
            topShade.rectTransform.localScale = new Vector3(1f, -1f, 1f);
            topShade.raycastTarget = false;
            var bottomShade = UiKit.CreateImage("BottomShade", rt, Vector2.zero, new Vector2(1f, 0.5f), ApogeeTheme.VerticalFade, new Color(0.12f, 0.02f, 0.02f, 0.92f), false);
            bottomShade.raycastTarget = false;
            var leaves = UiKit.CreateRect("Leaves", rt, Vector2.zero, Vector2.one);
            leaves.gameObject.AddComponent<HubLeaves>();

            var layout = rt.gameObject.AddComponent<HubLayout>();

            // ---- top bar: portrait and name on the left, currencies on the right --------
            var top = UiKit.CreateRect("TopBar", rt, new Vector2(0f, 1f), new Vector2(1f, 1f));
            layout.topBar = top;

            var avatarBtnRt = UiKit.CreateRect("Avatar", top, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
            avatarBtnRt.sizeDelta = new Vector2(124f, 124f);
            avatarBtnRt.anchoredPosition = new Vector2(30f + 62f, 0f);
            var disc = avatarBtnRt.gameObject.AddComponent<Image>();
            disc.sprite = HubArt.Get("ui_disc");
            disc.color = new Color(0.16f, 0.05f, 0.04f);
            avatarBtnRt.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            var avatarBtn = avatarBtnRt.gameObject.AddComponent<Button>();
            avatarBtn.transition = Selectable.Transition.None;
            avatarBtn.onClick.AddListener(ShowCharacters);
            avatarBtnRt.gameObject.AddComponent<ButtonPop>();
            hubAvatar = UiKit.CreateImage("Portrait", avatarBtnRt, Vector2.zero, Vector2.one, HubArt.Get("avatar") ?? PlayerSprite, Color.white);
            hubAvatar.raycastTarget = false;
            var ring = UiKit.CreateImage("Ring", top, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), HubArt.Get("ui_ring"), Color.white);
            ring.rectTransform.sizeDelta = new Vector2(136f, 136f);
            ring.rectTransform.anchoredPosition = avatarBtnRt.anchoredPosition;
            ring.raycastTarget = false;

            var nameRt = UiKit.CreateRect("Name", top, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
            nameRt.pivot = new Vector2(0f, 0.5f);
            nameRt.sizeDelta = new Vector2(300f, 100f);
            nameRt.anchoredPosition = new Vector2(170f, 0f);
            hubCharacterText = UiKit.Outlined(UiKit.CreateText("HubCharacter", nameRt, "", 30, TextAnchor.LowerLeft,
                new Vector2(0f, 0.5f), Vector2.one, ApogeeTheme.Cream), 1.5f);
            UiKit.FitLabel(hubCharacterText, 30);
            UiKit.Outlined(UiKit.CreateText("HubCharacterHint", nameRt, "PERSONNAGE  ›", 19, TextAnchor.UpperLeft,
                Vector2.zero, new Vector2(1f, 0.5f), ApogeeTheme.Gold), 1.2f);
            layout.nameBlock = nameRt;

            var pills = UiKit.CreateRect("Currencies", top, new Vector2(1f, 0f), new Vector2(1f, 1f));
            pills.pivot = new Vector2(1f, 0.5f);
            pills.sizeDelta = new Vector2(620f, 0f);
            pills.anchoredPosition = new Vector2(-26f, 0f);
            var row = pills.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.childAlignment = TextAnchor.MiddleRight;
            row.spacing = 12f;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            hubPills.Clear();
            for (int i = 0; i < 3; i++)
            {
                var pill = UiKit.CreateRect("Pill" + i, pills, Vector2.zero, Vector2.one);
                var img = pill.gameObject.AddComponent<Image>();
                img.sprite = HubArt.Get("ui_pill", 30f);
                img.type = Image.Type.Sliced;
                img.raycastTarget = false;
                var le = pill.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = i == 2 ? 130f : 190f;
                le.preferredHeight = 68f;
                var label = IconText.Create("Value", pill, "", 28, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, ApogeeTheme.Cream, 1.2f);
                ((RectTransform)label.transform).offsetMin = new Vector2(14f, 6f);
                ((RectTransform)label.transform).offsetMax = new Vector2(-14f, -6f);
                hubPills.Add(label);
            }
            hubWalletText = hubPills[0];
            layout.pills = pills;

            // ---- logo ---------------------------------------------------------------------
            var logo = UiKit.CreateImage("Logo", rt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), ApogeeTheme.ArtSprite("logo_light"), Color.white);
            logo.raycastTarget = false;
            var logoShadow = logo.gameObject.AddComponent<Shadow>();
            logoShadow.effectColor = new Color(0.12f, 0.02f, 0.02f, 0.7f);
            logoShadow.effectDistance = new Vector2(0f, -5f);
            layout.logo = logo.rectTransform;

            // ---- carousel of game cards -----------------------------------------------------
            var cards = new List<(string title, string desc, Action onClick, Func<string> best, string id)>
            {
                ("RUNNER", "Cours, saute et survis le plus loin possible", ShowRunnerModes,
                    () => (SaveSystem.BestDistance > 0f
                        ? $"Record : {FormatDistance(SaveSystem.BestDistance)}   ·   "
                        : "") + $"Missions {DailyMissions.DoneCount}/{DailyMissions.Count}", "runner"),
            };
            foreach (var game in miniGames)
            {
                if (!game.ShowOnHub) continue;
                var captured = game;
                cards.Add((game.Title, game.Description, () => LaunchWithGuide(captured.Id, () => EnterMiniGame(captured)), () => captured.BestLine, game.Id));
            }

            var carouselRt = UiKit.CreateRect("Carousel", rt, Vector2.zero, Vector2.one);
            var catcher = carouselRt.gameObject.AddComponent<Image>();
            catcher.color = Color.clear; // swipes anywhere across the band, not only on a card
            var carousel = carouselRt.gameObject.AddComponent<HubCarousel>();
            layout.carousel = carouselRt;

            hubCardBestTexts.Clear();
            var actions = new List<Action>();
            for (int i = 0; i < cards.Count; i++)
            {
                var (title, desc, onClick, best, id) = cards[i];
                int index = i;
                var card = BuildHubCard(carouselRt, title, desc, id, out var body, out var dim, out var bestText);
                hubCardBestTexts.Add(bestText);
                body.onClick.AddListener(() =>
                {
                    if (carousel.Centered == index) onClick();
                    else carousel.GoTo(index);
                });
                carousel.Add(card, dim);
                actions.Add(onClick);
            }
            hubCardBestProviders = cards.ConvertAll(c => c.best);

            // Page dots and the big play button under the cards.
            var dots = UiKit.CreateRect("Dots", rt, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            dots.sizeDelta = new Vector2(400f, 26f);
            var dotRow = dots.gameObject.AddComponent<HorizontalLayoutGroup>();
            dotRow.childAlignment = TextAnchor.MiddleCenter;
            dotRow.spacing = 14f;
            dotRow.childControlWidth = dotRow.childControlHeight = true;
            dotRow.childForceExpandWidth = dotRow.childForceExpandHeight = false;
            for (int i = 0; i < cards.Count; i++)
            {
                var dot = UiKit.CreateImage("Dot" + i, dots, Vector2.zero, Vector2.one, HubArt.Get("ui_dot"), Color.white);
                var le = dot.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = le.preferredHeight = 18f;
                dot.raycastTarget = false;
                carousel.dots.Add(dot);
            }
            layout.dots = dots;

            var playRt = UiKit.CreateRect("Play", rt, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            playRt.sizeDelta = new Vector2(470f, 124f);
            var playImg = playRt.gameObject.AddComponent<Image>();
            playImg.sprite = HubArt.Get("ui_play", 50f);
            playImg.type = Image.Type.Sliced;
            var play = playRt.gameObject.AddComponent<Button>();
            play.targetGraphic = playImg;
            play.onClick.AddListener(() => actions[Mathf.Clamp(carousel.Centered, 0, actions.Count - 1)]());
            var playInner = UiKit.CreateRect("Inner", playRt, Vector2.zero, Vector2.one);
            var playIcon = UiKit.CreateImage("Icon", playInner, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), HubArt.Get("icon_play"), new Color(0.36f, 0.12f, 0.03f));
            playIcon.rectTransform.sizeDelta = new Vector2(54f, 54f);
            playIcon.rectTransform.anchoredPosition = new Vector2(-104f, 2f);
            playIcon.raycastTarget = false;
            var playLabel = UiKit.CreateText("Label", playInner, "JOUER", 50, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, new Color(0.30f, 0.09f, 0.02f));
            playLabel.rectTransform.offsetMin = new Vector2(60f, 0f);
            playLabel.fontStyle = FontStyle.Bold;
            var playShadow = playLabel.gameObject.AddComponent<Shadow>();
            playShadow.effectColor = new Color(1f, 0.95f, 0.75f, 0.6f);
            playShadow.effectDistance = new Vector2(0f, -2f);
            playRt.gameObject.AddComponent<ButtonPop>();
            playInner.gameObject.AddComponent<HubPulse>();
            layout.play = playRt;

            // ---- bottom bar ---------------------------------------------------------------------
            var nav = UiKit.CreateRect("Nav", rt, new Vector2(0f, 0f), new Vector2(1f, 0f));
            nav.pivot = new Vector2(0.5f, 0f);
            var navRow = nav.gameObject.AddComponent<HorizontalLayoutGroup>();
            navRow.childAlignment = TextAnchor.MiddleCenter;
            navRow.spacing = 18f;
            navRow.childControlWidth = navRow.childControlHeight = true;
            navRow.childForceExpandWidth = navRow.childForceExpandHeight = false;
            layout.nav = nav;

            CreateNavTile(nav, "PERSONNAGES", HubArt.Get("icon_hero"), ShowCharacters, out _);
            var arms = CreateNavTile(nav, "ARMES", null, ShowArmory, out hubWeaponIcon);
            hubWeaponIcon.color = Color.white;
            var missions = CreateNavTile(nav, "MISSIONS", HubArt.Get("icon_missions"), () => { ShowMissions(); missionsFromHub = true; }, out _);
            var badge = UiKit.CreateImage("Badge", missions, new Vector2(1f, 1f), new Vector2(1f, 1f), PlaceholderVisuals.Circle(Color.white), ApogeeTheme.Crimson);
            badge.rectTransform.sizeDelta = new Vector2(64f, 44f);
            badge.rectTransform.anchoredPosition = new Vector2(-24f, -14f);
            badge.preserveAspect = false;
            badge.raycastTarget = false;
            hubMissionsBadge = UiKit.Outlined(UiKit.CreateText("Count", badge.transform, "", 22, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, ApogeeTheme.Cream), 1f);
            CreateNavTile(nav, "AMÉLIORER", HubArt.Get("icon_upgrade"), ShowShop, out _);
        }

        /// <summary>
        /// One game card, built at a fixed design size (the carousel scales it): the painted
        /// illustration in a rounded mask, a dark gradient at the bottom carrying the title,
        /// the pitch and the record, the rewards chip and the TUTO chip on top, a gold rim
        /// and a soft shadow, and a dimming veil the carousel fades in on the side cards.
        /// </summary>
        RectTransform BuildHubCard(RectTransform parent, string title, string desc, string id,
            out Button body, out Image dim, out IconText bestText)
        {
            var card = UiKit.CreateRect("Card_" + id, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            card.sizeDelta = HubCarousel.CardSize;

            var shadow = UiKit.CreateImage("Shadow", card, Vector2.zero, Vector2.one, HubArt.Get("ui_shadow", 48f), Color.white, false);
            shadow.type = Image.Type.Sliced;
            shadow.rectTransform.offsetMin = new Vector2(-40f, -58f);
            shadow.rectTransform.offsetMax = new Vector2(40f, 22f);
            shadow.raycastTarget = false;

            var bodyRt = UiKit.CreateRect("Body", card, Vector2.zero, Vector2.one);
            var maskImg = bodyRt.gameObject.AddComponent<Image>();
            maskImg.sprite = HubArt.Get("ui_card_mask", 32f);
            maskImg.type = Image.Type.Sliced;
            maskImg.color = ApogeeTheme.Maroon;
            bodyRt.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            body = bodyRt.gameObject.AddComponent<Button>();
            body.transition = Selectable.Transition.None;
            bodyRt.gameObject.AddComponent<ButtonPop>();

            var artRt = UiKit.CreateRect("Art", bodyRt, Vector2.zero, Vector2.one);
            var art = artRt.gameObject.AddComponent<RawImage>();
            var tex = HubArt.Texture("card_" + id);
            if (tex != null)
            {
                art.texture = tex;
                artRt.gameObject.AddComponent<CoverImage>().focus = new Vector2(0.5f, 0.55f);
            }
            else art.texture = ApogeeTheme.Art("menu_bg");

            var fade = UiKit.CreateImage("Fade", bodyRt, Vector2.zero, new Vector2(1f, 0.52f), ApogeeTheme.VerticalFade, new Color(0.10f, 0.02f, 0.02f, 0.95f), false);
            fade.raycastTarget = false;

            var titleText = UiKit.Outlined(UiKit.CreateText("Title", bodyRt, title, 82, TextAnchor.LowerLeft,
                new Vector2(0.07f, 0.17f), new Vector2(0.93f, 0.31f), ApogeeTheme.Gold), 3f);
            UiKit.FitLabel(titleText, 82);
            var titleShadow = titleText.gameObject.AddComponent<Shadow>();
            titleShadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
            titleShadow.effectDistance = new Vector2(0f, -6f);
            var rule = UiKit.CreateImage("Rule", bodyRt, new Vector2(0.07f, 0.162f), new Vector2(0.38f, 0.166f), PlaceholderVisuals.Square(Color.white), ApogeeTheme.Gold, false);
            rule.raycastTarget = false;
            var descText = UiKit.CreateText("Desc", bodyRt, desc, 30, TextAnchor.UpperLeft,
                new Vector2(0.07f, 0.075f), new Vector2(0.93f, 0.15f), ApogeeTheme.Cream);
            UiKit.FitLabel(descText, 30);
            bestText = IconText.Create("Best", bodyRt, "", 26, TextAnchor.LowerLeft,
                new Vector2(0.07f, 0.02f), new Vector2(0.93f, 0.075f), ApogeeTheme.Gold, 1.2f);

            // What the game pays, in a glass chip at the top right.
            var rewards = UiKit.CreateRect("Rewards", bodyRt, new Vector2(1f, 1f), new Vector2(1f, 1f));
            rewards.pivot = new Vector2(1f, 1f);
            rewards.sizeDelta = new Vector2(250f, 72f);
            rewards.anchoredPosition = new Vector2(-26f, -26f);
            var rewardsImg = rewards.gameObject.AddComponent<Image>();
            rewardsImg.sprite = HubArt.Get("ui_pill", 30f);
            rewardsImg.type = Image.Type.Sliced;
            rewardsImg.raycastTarget = false;
            var rewardsText = IconText.Create("Text", rewards, GameGuide.For(id).Rewards, 30, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, ApogeeTheme.Cream, 1.2f);
            ((RectTransform)rewardsText.transform).offsetMin = new Vector2(16f, 6f);
            ((RectTransform)rewardsText.transform).offsetMax = new Vector2(-16f, -6f);

            // The runner opens its own page, where each mode has its own help.
            if (id != "runner")
            {
                var help = UiKit.CreateRect("Help", bodyRt, new Vector2(0f, 1f), new Vector2(0f, 1f));
                help.pivot = new Vector2(0f, 1f);
                help.sizeDelta = new Vector2(128f, 72f);
                help.anchoredPosition = new Vector2(26f, -26f);
                var helpImg = help.gameObject.AddComponent<Image>();
                helpImg.sprite = HubArt.Get("ui_pill", 30f);
                helpImg.type = Image.Type.Sliced;
                var helpBtn = help.gameObject.AddComponent<Button>();
                helpBtn.targetGraphic = helpImg;
                helpBtn.onClick.AddListener(() => ShowGuide(id, null));
                help.gameObject.AddComponent<ButtonPop>();
                UiKit.Outlined(UiKit.CreateText("Label", help, "TUTO", 26, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, ApogeeTheme.Gold), 1.2f);
            }

            var frame = UiKit.CreateImage("Frame", card, Vector2.zero, Vector2.one, HubArt.Get("ui_card_frame", 32f), Color.white, false);
            frame.type = Image.Type.Sliced;
            frame.raycastTarget = false;

            dim = UiKit.CreateImage("Dim", card, Vector2.zero, Vector2.one, HubArt.Get("ui_card_mask", 32f), new Color(0.08f, 0.02f, 0.02f, 0f), false);
            dim.type = Image.Type.Sliced;
            dim.raycastTarget = false;
            return card;
        }

        /// <summary>A square glass tile of the bottom bar: an icon over a small label.</summary>
        RectTransform CreateNavTile(RectTransform parent, string label, Sprite icon, UnityEngine.Events.UnityAction onClick, out Image iconImage)
        {
            var tile = UiKit.CreateRect("Tile_" + label, parent, Vector2.zero, Vector2.one);
            var le = tile.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 230f;
            le.preferredHeight = 170f;
            var img = tile.gameObject.AddComponent<Image>();
            img.sprite = HubArt.Get("ui_tile", 32f);
            img.type = Image.Type.Sliced;
            var btn = tile.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.pressedColor = new Color(0.8f, 0.75f, 0.7f);
            btn.colors = colors;
            btn.onClick.AddListener(onClick);
            tile.gameObject.AddComponent<ButtonPop>();
            iconImage = UiKit.CreateImage("Icon", tile, new Vector2(0.2f, 0.36f), new Vector2(0.8f, 0.9f), icon, Color.white);
            iconImage.raycastTarget = false;
            var text = UiKit.Outlined(UiKit.CreateText("Label", tile, label, 24, TextAnchor.MiddleCenter,
                new Vector2(0.04f, 0.06f), new Vector2(0.96f, 0.34f), ApogeeTheme.Gold), 1.2f);
            UiKit.FitLabel(text, 24);
            return tile;
        }

        /// <summary>Opens the missions page from the hub (its back button then returns here).</summary>
        void CloseMissions()
        {
            bool fromHub = missionsFromHub;
            missionsFromHub = false;
            if (fromHub) ShowHub(); else ShowRunnerModes();
        }

        void RefreshHub()
        {
            if (hubPills.Count == 3)
            {
                hubPills[0].text = $"{SaveSystem.Coins} [c]";
                hubPills[1].text = $"{SaveSystem.Materials} [g]";
                hubPills[2].text = $"{SaveSystem.ReviveKits} [k]";
            }
            if (hubWeaponIcon != null) hubWeaponIcon.sprite = WeaponCatalog.SpriteFor(WeaponCatalog.Equipped);
            var skin = SkinCatalog.Find(SaveSystem.SelectedSkinId);
            hubCharacterText.text = skin.Name;
            if (hubAvatar != null) hubAvatar.color = Color.Lerp(Color.white, skin.Tint, 0.5f);
            if (hubMissionsBadge != null)
            {
                int left = DailyMissions.Count - DailyMissions.DoneCount;
                hubMissionsBadge.transform.parent.gameObject.SetActive(left > 0);
                hubMissionsBadge.text = left.ToString();
            }
            for (int i = 0; i < hubCardBestTexts.Count && i < hubCardBestProviders.Count; i++)
                hubCardBestTexts[i].text = hubCardBestProviders[i]() ?? "";
        }
    }

    /// <summary>Hub art from Resources/Hub, as textures or (9-sliced) sprites, cached.</summary>
    static class HubArt
    {
        static readonly Dictionary<string, Sprite> sprites = new();

        public static Texture2D Texture(string name) => Resources.Load<Texture2D>("Hub/" + name);

        public static Sprite Get(string name, float border = 0f)
        {
            string key = name + "|" + border;
            if (sprites.TryGetValue(key, out var s)) return s;
            var tex = Texture(name);
            if (tex != null)
                s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0,
                    SpriteMeshType.FullRect, new Vector4(border, border, border, border));
            sprites[key] = s;
            return s;
        }
    }

    /// <summary>
    /// Places the hub's blocks in reference units for the current orientation: upright, the
    /// logo sits under the top bar and the cards fill the middle; sideways, the logo moves
    /// into the top bar and the cards get the room. Fades the screen in when it opens.
    /// </summary>
    public class HubLayout : MonoBehaviour
    {
        public RectTransform topBar, nameBlock, pills, logo, carousel, dots, play, nav;

        RectTransform self;
        CanvasGroup group;
        Vector2 lastSize;
        float shownAt;

        void Awake()
        {
            self = (RectTransform)transform;
            group = gameObject.AddComponent<CanvasGroup>();
        }

        void OnEnable()
        {
            shownAt = Time.unscaledTime;
            lastSize = Vector2.zero;
        }

        void Update()
        {
            if (group != null) group.alpha = Mathf.SmoothStep(0f, 1f, (Time.unscaledTime - shownAt) / 0.35f);
            var size = self.rect.size;
            if (size == lastSize || size.x <= 0f) return;
            lastSize = size;
            bool landscape = size.x > size.y;

            float topH = landscape ? 140f : 170f;
            topBar.sizeDelta = new Vector2(0f, topH);
            topBar.pivot = new Vector2(0.5f, 1f);
            topBar.anchoredPosition = Vector2.zero;
            // Narrow phones: let the currencies have the room, the name gets what is left.
            nameBlock.sizeDelta = new Vector2(Mathf.Clamp(size.x - 170f - 560f, 120f, 340f), 100f);

            float logoH = landscape ? 0f : Mathf.Clamp(size.y * 0.11f, 150f, 230f);
            if (landscape)
            {
                logo.anchorMin = logo.anchorMax = new Vector2(0.5f, 1f);
                logo.sizeDelta = new Vector2(380f, 120f);
                logo.anchoredPosition = new Vector2(-40f, -topH * 0.5f);
                logo.pivot = new Vector2(0.5f, 0.5f);
            }
            else
            {
                logo.anchorMin = logo.anchorMax = new Vector2(0.5f, 1f);
                logo.pivot = new Vector2(0.5f, 1f);
                logo.sizeDelta = new Vector2(logoH * 3.2f, logoH);
                logo.anchoredPosition = new Vector2(0f, -topH + 6f);
            }

            float navH = landscape ? 150f : 190f;
            nav.sizeDelta = new Vector2(0f, navH);
            nav.anchoredPosition = new Vector2(0f, landscape ? 14f : 30f);
            foreach (Transform t in nav)
            {
                var le = t.GetComponent<LayoutElement>();
                if (le == null) continue;
                le.preferredHeight = navH - 20f;
                le.preferredWidth = landscape ? 210f : Mathf.Min(230f, (size.x - 40f - 3 * 18f) / 4f);
            }

            float navTop = (landscape ? 14f : 30f) + navH;
            float playH = landscape ? 104f : 124f;
            play.sizeDelta = new Vector2(landscape ? 400f : 470f, playH);
            play.anchoredPosition = new Vector2(0f, navTop + 22f + playH * 0.5f);
            float dotsY = navTop + 22f + playH + 30f;
            dots.anchoredPosition = new Vector2(0f, dotsY);

            float carouselTop = topH + (landscape ? 0f : logoH) + 10f;
            float carouselBottom = dotsY + 24f;
            carousel.anchorMin = Vector2.zero;
            carousel.anchorMax = Vector2.one;
            carousel.offsetMin = new Vector2(0f, carouselBottom);
            carousel.offsetMax = new Vector2(0f, -carouselTop);
        }
    }

    /// <summary>
    /// Swipeable row of cards: the centered card full size and bright, its neighbors
    /// smaller, slightly tilted and dimmed. Follows the finger while dragging, then snaps
    /// to the nearest card (a quick flick moves one card). Arrow keys work too.
    /// </summary>
    public class HubCarousel : MonoBehaviour, IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public static readonly Vector2 CardSize = new Vector2(720f, 960f);
        const float SideScale = 0.84f;
        static int remembered;

        public readonly List<Image> dots = new();
        readonly List<RectTransform> cards = new();
        readonly List<Image> dims = new();

        RectTransform self;
        float pos, target, velocity, dragStartPos, dragVelocity, spacing = 1f;
        bool dragging;
        int lastFront = -1;

        public int Centered => Mathf.Clamp(Mathf.RoundToInt(target), 0, Mathf.Max(0, cards.Count - 1));

        void Awake() => self = (RectTransform)transform;

        public void Add(RectTransform card, Image dim)
        {
            cards.Add(card);
            dims.Add(dim);
        }

        public void GoTo(int index)
        {
            target = Mathf.Clamp(index, 0, cards.Count - 1);
            remembered = (int)target;
        }

        void OnEnable()
        {
            target = Mathf.Clamp(remembered, 0, Mathf.Max(0, cards.Count - 1));
            pos = target + 0.45f; // a small slide in when the screen opens
            velocity = 0f;
            dragging = false;
        }

        public void OnInitializePotentialDrag(PointerEventData e) => e.useDragThreshold = true;

        public void OnBeginDrag(PointerEventData e)
        {
            dragging = true;
            dragStartPos = pos;
            dragVelocity = 0f;
        }

        public void OnDrag(PointerEventData e)
        {
            var canvas = GetComponentInParent<Canvas>();
            float scale = canvas != null ? canvas.scaleFactor : 1f;
            float delta = -e.delta.x / scale / Mathf.Max(1f, spacing);
            pos = Mathf.Clamp(pos + delta, -0.35f, cards.Count - 0.65f);
            float dt = Mathf.Max(Time.unscaledDeltaTime, 0.001f);
            dragVelocity = Mathf.Lerp(dragVelocity, delta / dt, 0.4f);
        }

        public void OnEndDrag(PointerEventData e)
        {
            dragging = false;
            float aim = pos + Mathf.Clamp(dragVelocity * 0.12f, -0.6f, 0.6f);
            int start = Mathf.RoundToInt(dragStartPos);
            GoTo(Mathf.Clamp(Mathf.RoundToInt(aim), start - 1, start + 1));
        }

        void Update()
        {
            if (cards.Count == 0) return;
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.leftArrowKey.wasPressedThisFrame) GoTo(Centered - 1);
                if (kb.rightArrowKey.wasPressedThisFrame) GoTo(Centered + 1);
            }

            if (!dragging) pos = Mathf.SmoothDamp(pos, target, ref velocity, 0.16f, Mathf.Infinity, Time.unscaledDeltaTime);

            var area = self.rect.size;
            float scale = Mathf.Min(area.y * 0.96f / CardSize.y, area.x * 0.72f / CardSize.x);
            scale = Mathf.Max(scale, 0.05f);
            spacing = CardSize.x * scale * 1.0f;

            for (int i = 0; i < cards.Count; i++)
            {
                float d = i - pos;
                float ad = Mathf.Abs(d);
                float s = Mathf.Lerp(1f, SideScale, Mathf.Clamp01(ad));
                var c = cards[i];
                c.anchoredPosition = new Vector2(d * spacing, -CardSize.y * scale * (1f - s) * 0.12f);
                c.localScale = Vector3.one * (scale * s);
                c.localRotation = Quaternion.Euler(0f, 0f, -Mathf.Clamp(d, -1.5f, 1.5f) * 2.5f);
                var col = dims[i].color;
                col.a = 0.55f * Mathf.Clamp01(ad);
                dims[i].color = col;
            }

            int front = Mathf.Clamp(Mathf.RoundToInt(pos), 0, cards.Count - 1);
            if (front != lastFront)
            {
                lastFront = front;
                // Nearest cards drawn last, so the centered one overlaps its neighbors.
                var order = new List<int>();
                for (int i = 0; i < cards.Count; i++) order.Add(i);
                order.Sort((a, b) => Mathf.Abs(b - front).CompareTo(Mathf.Abs(a - front)));
                foreach (int i in order) cards[i].SetAsLastSibling();
            }

            for (int i = 0; i < dots.Count; i++)
            {
                float on = Mathf.Clamp01(1f - Mathf.Abs(i - pos));
                dots[i].color = Color.Lerp(new Color(1f, 0.93f, 0.8f, 0.4f), ApogeeTheme.Gold, on);
                dots[i].rectTransform.localScale = Vector3.one * (1f + 0.45f * on);
            }
        }
    }

    /// <summary>Very slow zoom and pan of the backdrop, so the home screen feels alive.</summary>
    public class HubDrift : MonoBehaviour
    {
        void Update()
        {
            float t = Time.unscaledTime;
            transform.localScale = Vector3.one * (1.04f + 0.035f * Mathf.Sin(t * 0.11f));
            ((RectTransform)transform).anchoredPosition = new Vector2(Mathf.Sin(t * 0.07f) * 14f, Mathf.Cos(t * 0.09f) * 8f);
        }
    }

    /// <summary>Gentle breathing scale (the play button's label).</summary>
    public class HubPulse : MonoBehaviour
    {
        void Update() => transform.localScale = Vector3.one * (1f + 0.035f * Mathf.Sin(Time.unscaledTime * 3.2f));
    }

    /// <summary>Red and amber maple leaves drifting down across the home screen.</summary>
    public class HubLeaves : MonoBehaviour
    {
        const int Count = 16;
        static readonly Color[] Tints =
        {
            new Color(0.86f, 0.22f, 0.12f), new Color(0.95f, 0.45f, 0.16f), new Color(0.72f, 0.12f, 0.08f), new Color(0.98f, 0.64f, 0.25f),
        };

        struct Leaf { public RectTransform rt; public float speed, sway, phase, spin, size; }
        readonly Leaf[] leaves = new Leaf[Count];
        RectTransform self;

        void Awake()
        {
            self = (RectTransform)transform;
            for (int i = 0; i < Count; i++)
            {
                var rt = UiKit.CreateRect("Leaf" + i, transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
                var raw = rt.gameObject.AddComponent<RawImage>();
                raw.texture = ApogeeTheme.LeafTexture;
                raw.raycastTarget = false;
                raw.color = Tints[i % Tints.Length] * new Color(1f, 1f, 1f, UnityEngine.Random.Range(0.55f, 0.95f));
                leaves[i] = new Leaf { rt = rt };
                Respawn(ref leaves[i], true);
            }
        }

        void Respawn(ref Leaf l, bool anywhere)
        {
            var size = self.rect.size;
            if (size.y <= 0f) size = new Vector2(1080f, 1920f);
            l.size = UnityEngine.Random.Range(22f, 46f);
            l.speed = UnityEngine.Random.Range(50f, 110f) * (l.size / 34f);
            l.sway = UnityEngine.Random.Range(20f, 60f);
            l.phase = UnityEngine.Random.Range(0f, 10f);
            l.spin = UnityEngine.Random.Range(-90f, 90f);
            l.rt.sizeDelta = new Vector2(l.size, l.size);
            float y = anywhere ? UnityEngine.Random.Range(-size.y * 0.5f, size.y * 0.5f) : size.y * 0.5f + 40f;
            l.rt.anchoredPosition = new Vector2(UnityEngine.Random.Range(-size.x * 0.5f, size.x * 0.5f), y);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime, t = Time.unscaledTime;
            float bottom = -self.rect.height * 0.5f - 50f;
            for (int i = 0; i < Count; i++)
            {
                ref var l = ref leaves[i];
                var p = l.rt.anchoredPosition;
                p.y -= l.speed * dt;
                p.x += Mathf.Sin(t * 0.9f + l.phase) * l.sway * dt + 12f * dt;
                l.rt.anchoredPosition = p;
                l.rt.localRotation = Quaternion.Euler(0f, 0f, l.phase * 40f + t * l.spin);
                if (p.y < bottom) Respawn(ref l, false);
            }
        }
    }
}
