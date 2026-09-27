using UnityEngine;
using UnityEngine.UI;

namespace Platformer.Survival
{
    /// <summary>
    /// The runner's "second chance" screen, shown when the player dies in an endless run:
    /// use a healing kit, watch an ad (once per run), or give up. See SurvivalDirector.Revive.
    /// </summary>
    public partial class RuntimeUI
    {
        GameObject revivePanel;
        Text reviveDistanceText, reviveInfoText;
        Button reviveKitButton, reviveAdButton;
        IconText reviveKitLabel;

        void BuildRevivePanel()
        {
            var rt = UiKit.CreatePanel("RevivePanel", canvas.transform, UiKit.Overlay);
            revivePanel = rt.gameObject;
            UiKit.CreateFrame("ReviveFrame", rt, new Vector2(0.08f, 0.22f), new Vector2(0.92f, 0.80f));

            UiKit.Outlined(UiKit.CreateText("ReviveTitle", rt, "SECONDE CHANCE", 54, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.68f), new Vector2(0.9f, 0.77f), ApogeeTheme.Gold), 2.5f);
            reviveDistanceText = UiKit.CreateText("ReviveDistance", rt, "", 30, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.62f), new Vector2(0.9f, 0.675f), ApogeeTheme.Cream);
            reviveInfoText = UiKit.CreateText("ReviveInfo", rt, "Reprends la course là où tu es tombé", 22, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.575f), new Vector2(0.9f, 0.62f), UiKit.TextDim);
            UiKit.FitLabel(reviveInfoText, 22);

            reviveKitButton = UiKit.CreateButton("ReviveKit", rt, "", new Vector2(0.16f, 0.47f), new Vector2(0.84f, 0.555f), OnReviveWithKit, 28);
            reviveKitLabel = IconText.OnButton(reviveKitButton, 28);
            reviveAdButton = UiKit.CreateButton("ReviveAd", rt, "REGARDER UNE PUB", new Vector2(0.16f, 0.37f), new Vector2(0.84f, 0.455f), OnReviveWithAd, 28);
            UiKit.CreateButton("ReviveGiveUp", rt, "ABANDONNER", new Vector2(0.3f, 0.26f), new Vector2(0.7f, 0.33f), OnReviveGiveUp, 24,
                new Color(0.22f, 0.10f, 0.08f));
            revivePanel.SetActive(false);
        }

        public void ShowReviveOffer(float distance, bool adAvailable)
        {
            HideAllShellPanels();
            HideBanner();
            UiKit.SetPanel(revivePanel, true);
            revivePanel.transform.SetAsLastSibling();
            reviveDistanceText.text = $"Distance : {FormatDistance(distance)}";
            reviveInfoText.text = "Reprends la course là où tu es tombé";

            int kits = SaveSystem.ReviveKits;
            reviveKitButton.interactable = kits > 0;
            reviveKitLabel.text = kits > 0
                ? $"UTILISER UN KIT   {kits}/{SaveSystem.MaxReviveKits} [k]"
                : "PAS DE KIT   0 [k]";
            reviveAdButton.interactable = adAvailable;
            UiKit.ButtonLabel(reviveAdButton).text = adAvailable ? "REGARDER UNE PUB" : "PUB DÉJÀ UTILISÉE";
        }

        void OnReviveWithKit()
        {
            if (!SaveSystem.TryUseReviveKit()) return;
            ResumeAfterRevive(viaAd: false);
        }

        void OnReviveWithAd()
        {
            ShowAdOverlay(true, "Publicité en cours...");
            AdService.ShowRewardedAd(() =>
            {
                ShowAdOverlay(false);
                ResumeAfterRevive(viaAd: true);
            }, () =>
            {
                ShowAdOverlay(false);
                reviveInfoText.text = "Publicité indisponible ou interrompue, réessaie.";
            });
        }

        void OnReviveGiveUp()
        {
            UiKit.SetPanel(revivePanel, false);
            director.GiveUpRun();
        }

        void ResumeAfterRevive(bool viaAd)
        {
            UiKit.SetPanel(revivePanel, false);
            UiKit.SetPanel(hudPanel, true);
            director.Revive(viaAd);
        }
    }
}
