using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Platformer.Survival
{
    /// <summary>
    /// The "COMPAGNON" section next to the characters: Étienne, the robot Elron built, in
    /// each finish he can be given - shown hovering, animated, with the same buy / equip
    /// flow as the characters. Two tabs switch between the sections.
    /// </summary>
    public partial class RuntimeUI
    {
        GameObject companionPanel;
        IconText companionWalletText;
        Text companionDetailText;
        Button companionActionButton;
        IconText companionActionLabel;
        Image companionHero;
        int selectedCompanionIndex;
        readonly List<CharacterCard> companionCards = new();

        /// <summary>The two tabs at the top of the characters and companion pages.</summary>
        void CreateSectionTabs(RectTransform panel, bool companion)
        {
            var a = UiKit.CreateButton("TabCharacters", panel, "PERSONNAGES", new Vector2(0.06f, 0.795f), new Vector2(0.49f, 0.845f),
                ShowCharacters, 26, companion ? new Color(0.22f, 0.10f, 0.08f) : (Color?)null);
            var b = UiKit.CreateButton("TabCompanion", panel, "COMPAGNON", new Vector2(0.51f, 0.795f), new Vector2(0.94f, 0.845f),
                ShowCompanions, 26, companion ? (Color?)null : new Color(0.22f, 0.10f, 0.08f));
        }

        void BuildCompanionPanel()
        {
            var rt = UiKit.CreatePanel("CompanionPanel", canvas.transform, Color.white);
            companionPanel = rt.gameObject;
            ApplyOpaqueBackdrop(rt);
            CreateTitle(rt, "COMPAGNON", 0.905f, 0.965f);
            companionWalletText = CreateIconChip("Wallet", rt, new Vector2(0.30f, 0.855f), new Vector2(0.70f, 0.893f), 26);
            CreateSectionTabs(rt, true);

            // Étienne in the selected finish, large and alive.
            var stage = UiKit.CreateFrame("Stage", rt, new Vector2(0.06f, 0.56f), new Vector2(0.94f, 0.78f));
            companionHero = UiKit.CreateImage("Etienne", rt, new Vector2(0.3f, 0.565f), new Vector2(0.7f, 0.775f), null, Color.white);
            companionHero.raycastTarget = false;
            companionHero.gameObject.AddComponent<UiFlipbook>();
            UiKit.CreateText("Lore", rt, "« Étienne, le robot qu'Elron a construit pour survivre à la fin du monde. »", 22, TextAnchor.MiddleCenter,
                new Vector2(0.08f, 0.525f), new Vector2(0.92f, 0.56f), UiKit.TextDim);

            var all = CompanionCatalog.All;
            const int columns = 3;
            const float gridTop = 0.515f, gridBottom = 0.275f;
            int rows = Mathf.CeilToInt(all.Length / (float)columns);
            float rowH = (gridTop - gridBottom) / rows;
            companionCards.Clear();
            for (int i = 0; i < all.Length; i++)
            {
                int col = i % columns, row = i / columns;
                float x0 = 0.04f + col * 0.31f;
                float yMax = gridTop - row * rowH;
                var cardRt = UiKit.CreateRect($"Card_{all[i].Id}", rt, new Vector2(x0, yMax - rowH + 0.008f), new Vector2(x0 + 0.30f, yMax - 0.008f));
                var cardBg = cardRt.gameObject.AddComponent<Image>();
                var btn = cardRt.gameObject.AddComponent<Button>();
                UiKit.ApplyButtonStyle(btn, cardBg, UiKit.CardColor);
                var outline = cardRt.gameObject.AddComponent<Outline>();
                outline.effectColor = ApogeeTheme.Gold;
                outline.effectDistance = new Vector2(6f, -6f);
                outline.enabled = false;
                int captured = i;
                btn.onClick.AddListener(() => { selectedCompanionIndex = captured; RefreshCompanions(); });
                var frames = CompanionCatalog.Frames(all[i].Id);
                var portrait = UiKit.CreateImage("Portrait", cardRt, new Vector2(0.08f, 0.28f), new Vector2(0.92f, 0.97f), frames != null ? frames[0] : null, Color.white);
                portrait.raycastTarget = false;
                var nameText = UiKit.Outlined(UiKit.CreateText("Name", cardRt, all[i].Name, 22, TextAnchor.MiddleCenter, new Vector2(0.05f, 0.03f), new Vector2(0.95f, 0.28f), ApogeeTheme.Cream), 1.5f);
                UiKit.FitLabel(nameText, 22);
                var lockRt = UiKit.CreateRect("Lock", cardRt, Vector2.zero, Vector2.one);
                lockRt.offsetMin = new Vector2(6, 6);
                lockRt.offsetMax = new Vector2(-6, -6);
                var lockImg = lockRt.gameObject.AddComponent<Image>();
                lockImg.sprite = ApogeeTheme.FrameFill;
                lockImg.type = Image.Type.Sliced;
                lockImg.color = new Color(0.12f, 0.02f, 0.02f, 0.55f);
                lockImg.raycastTarget = false;
                UiKit.CreateImage("Padlock", lockRt, new Vector2(0.38f, 0.45f), new Vector2(0.62f, 0.85f), PlaceholderVisuals.Padlock(), Color.white).raycastTarget = false;
                companionCards.Add(new CharacterCard { portrait = portrait, lockOverlay = lockRt.gameObject, equippedOutline = outline });
            }

            UiKit.CreateFrame("DetailBg", rt, new Vector2(0.04f, 0.125f), new Vector2(0.96f, 0.26f));
            companionDetailText = UiKit.CreateText("Detail", rt, "", 21, TextAnchor.MiddleLeft, new Vector2(0.075f, 0.135f), new Vector2(0.57f, 0.25f), ApogeeTheme.Cream);
            UiKit.FitLabel(companionDetailText, 21);
            companionActionButton = UiKit.CreateButton("CompanionAction", rt, "", new Vector2(0.58f, 0.15f), new Vector2(0.93f, 0.23f), OnCompanionAction, 24);
            companionActionLabel = IconText.OnButton(companionActionButton, 24);
            UiKit.CreateButton("CompanionBack", rt, "RETOUR", new Vector2(0.32f, 0.03f), new Vector2(0.68f, 0.105f), ShowHub);
        }

        public void ShowCompanions()
        {
            HideAllShellPanels();
            UiKit.SetPanel(companionPanel, true);
            selectedCompanionIndex = Mathf.Max(0, System.Array.FindIndex(CompanionCatalog.All, c => c.Id == CompanionCatalog.SelectedId));
            RefreshCompanions();
        }

        void RefreshCompanions()
        {
            companionWalletText.text = $"{SaveSystem.Coins} [c]";
            var all = CompanionCatalog.All;
            for (int i = 0; i < all.Length && i < companionCards.Count; i++)
            {
                companionCards[i].lockOverlay.SetActive(!CompanionCatalog.IsUnlocked(all[i].Id));
                companionCards[i].equippedOutline.enabled = CompanionCatalog.SelectedId == all[i].Id;
            }
            var c = all[selectedCompanionIndex];
            var flip = companionHero.GetComponent<UiFlipbook>();
            if (flip != null) flip.frames = CompanionCatalog.Frames(c.Id);
            bool unlocked = CompanionCatalog.IsUnlocked(c.Id);
            if (unlocked)
            {
                bool equipped = CompanionCatalog.SelectedId == c.Id;
                companionDetailText.text = $"{c.Name} — {(equipped ? "À tes côtés" : "Débloqué")}\n{c.Description}";
                companionActionLabel.text = equipped ? "À TES CÔTÉS" : "CHOISIR";
                companionActionButton.interactable = !equipped;
            }
            else if (c.UnlockType == SkinUnlockType.Coins)
            {
                companionDetailText.text = $"{c.Name} — Verrouillé\n{c.Description}";
                companionActionLabel.text = $"ACHETER   {c.CoinCost} [c]";
                companionActionButton.interactable = SaveSystem.Coins >= c.CoinCost;
            }
            else
            {
                companionDetailText.text = $"{c.Name} — Verrouillé (pub)\n{c.Description}";
                companionActionLabel.text = "REGARDER UNE PUB";
                companionActionButton.interactable = true;
            }
        }

        void OnCompanionAction()
        {
            var c = CompanionCatalog.All[selectedCompanionIndex];
            if (CompanionCatalog.IsUnlocked(c.Id)) { EquipCompanion(c.Id); return; }
            if (c.UnlockType == SkinUnlockType.Coins)
            {
                if (SaveSystem.TrySpendCoins(c.CoinCost)) { CompanionCatalog.Unlock(c.Id); EquipCompanion(c.Id); }
                return;
            }
            ShowAdOverlay(true, "Publicité en cours...");
            AdService.ShowRewardedAd(() =>
            {
                ShowAdOverlay(false);
                CompanionCatalog.Unlock(c.Id);
                EquipCompanion(c.Id);
            }, () =>
            {
                ShowAdOverlay(false);
                companionDetailText.text = $"{c.Name}\nPublicité indisponible ou interrompue, réessaie.";
            });
        }

        void EquipCompanion(string id)
        {
            CompanionCatalog.SelectedId = id;
            foreach (var d in FindObjectsByType<Drone>(FindObjectsSortMode.None)) d.Repaint();
            Sfx.Milestone();
            RefreshCompanions();
        }
    }

    /// <summary>Plays sprite frames on a UI image (Étienne hovering on the companion page).</summary>
    public class UiFlipbook : MonoBehaviour
    {
        public Sprite[] frames;
        public float fps = 12f;
        Image image;
        float t;

        void Awake()
        {
            image = GetComponent<Image>();
            if (image != null) image.preserveAspect = true;
        }

        void Update()
        {
            if (image == null || frames == null || frames.Length == 0) return;
            t += Time.unscaledDeltaTime * fps;
            image.sprite = frames[(int)t % Mathf.Min(8, frames.Length)];
        }
    }
}
