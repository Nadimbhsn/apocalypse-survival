using UnityEngine;
using UnityEngine.UI;

namespace Platformer.Survival
{
    /// <summary>
    /// The daily missions page (reached from the Runner page), the banner that cheers a
    /// mission done mid-run, and the HUD line listing the bonuses in effect.
    /// </summary>
    public partial class RuntimeUI
    {
        GameObject missionsPanel;
        readonly Text[] missionTexts = new Text[DailyMissions.Count];
        readonly Image[] missionBars = new Image[DailyMissions.Count];
        readonly Text[] missionProgress = new Text[DailyMissions.Count];
        readonly IconText[] missionRewards = new IconText[DailyMissions.Count];
        readonly Image[] missionCards = new Image[DailyMissions.Count];
        IconText missionsBonusText;
        IconText missionsButtonLabel;
        Text hudPowerText;

        void BuildMissionsPanel()
        {
            var rt = UiKit.CreatePanel("MissionsPanel", canvas.transform, Color.white);
            missionsPanel = rt.gameObject;
            ApplyOpaqueBackdrop(rt);

            CreateTitle(rt, "MISSIONS DU JOUR", 0.875f, 0.945f);
            UiKit.CreateText("MissionsHint", rt, "Trois défis du Runner, renouvelés chaque jour", 22, TextAnchor.MiddleCenter,
                new Vector2(0.05f, 0.82f), new Vector2(0.95f, 0.86f), UiKit.TextDim);

            const float top = 0.79f, rowH = 0.165f;
            for (int i = 0; i < DailyMissions.Count; i++)
            {
                float yMax = top - i * rowH, yMin = yMax - rowH + 0.02f;
                var card = UiKit.CreateImage($"Mission_{i}", rt, new Vector2(0.06f, yMin), new Vector2(0.94f, yMax), ApogeeTheme.Panel, UiKit.CardColor, false);
                card.type = Image.Type.Sliced;
                missionCards[i] = card;
                missionTexts[i] = UiKit.CreateText("Text", card.transform, "", 26, TextAnchor.MiddleLeft,
                    new Vector2(0.05f, 0.52f), new Vector2(0.95f, 0.92f), ApogeeTheme.Cream);
                UiKit.FitLabel(missionTexts[i], 26);
                missionBars[i] = UiKit.CreateBar("Bar", card.transform, new Vector2(0.05f, 0.14f), new Vector2(0.55f, 0.42f), ApogeeTheme.Gold);
                missionProgress[i] = UiKit.CreateText("Progress", card.transform, "", 22, TextAnchor.MiddleLeft,
                    new Vector2(0.57f, 0.12f), new Vector2(0.72f, 0.44f), ApogeeTheme.Gold);
                missionRewards[i] = IconText.Create("Reward", card.transform, "", 22, TextAnchor.MiddleRight,
                    new Vector2(0.72f, 0.12f), new Vector2(0.95f, 0.44f), ApogeeTheme.Cream);
            }

            missionsBonusText = IconText.Create("Bonus", rt, "", 24, TextAnchor.MiddleCenter,
                new Vector2(0.06f, 0.21f), new Vector2(0.94f, 0.27f), ApogeeTheme.Gold);
            UiKit.CreateButton("MissionsPlay", rt, "COURIR", new Vector2(0.2f, 0.125f), new Vector2(0.8f, 0.195f),
                () => LaunchWithGuide("runner", StartRunner), 30);
            UiKit.CreateButton("MissionsBack", rt, "RETOUR", new Vector2(0.32f, 0.03f), new Vector2(0.68f, 0.1f), ShowRunnerModes);
            missionsPanel.SetActive(false);

            DailyMissions.Completed += m =>
                ShowBanner("MISSION ACCOMPLIE", $"{m.Text}   {m.RewardText}", 2.4f);
            DailyMissions.AllCompleted += kit =>
                ShowBanner("TOUTES LES MISSIONS !", kit ? "Cadeau : un kit de soin  1 [k]" : $"Stock de kits plein : {DailyMissions.BonusCoinsWhenFull} [c]", 2.8f);
        }

        public void ShowMissions()
        {
            HideAllShellPanels();
            UiKit.SetPanel(missionsPanel, true);
            var list = DailyMissions.Current;
            for (int i = 0; i < list.Length; i++)
            {
                var m = list[i];
                missionTexts[i].text = m.Text;
                missionBars[i].fillAmount = m.Target > 0 ? Mathf.Clamp01(m.Progress / (float)m.Target) : 0f;
                missionProgress[i].text = m.Done ? "FAIT" : $"{m.Progress} / {m.Target}";
                missionRewards[i].text = m.RewardText;
                missionCards[i].color = m.Done ? new Color(0.32f, 0.42f, 0.24f) : UiKit.CardColor;
            }
            missionsBonusText.text = DailyMissions.BonusPaid
                ? "Bonus du jour reçu !"
                : "Les trois finies : un kit de soin  1 [k]";
        }

        string MissionsButtonText() => $"MISSIONS DU JOUR   {DailyMissions.DoneCount} / {DailyMissions.Count}";

        /// <summary>Opens the arena for a runner mini-boss; onDone gets the result and the health left.</summary>
        public void StartRunnerBossDuel(int bossIndex, float health, string title, float strength, System.Action<bool, float> onDone)
        {
            HideAllShellPanels();
            HideBanner();
            if (arena == null)
            {
                UiKit.SetPanel(hudPanel, true);
                onDone(true, health);
                return;
            }
            arena.StartCampaignDuel(bossIndex, health, title, outcome =>
            {
                float left = arena.LastPlayerHealthFraction;
                HideAllShellPanels();
                UiKit.SetPanel(hudPanel, true);
                onDone(outcome == CampaignDuelOutcome.Won, left);
            }, strength, fromRunner: true);
        }

        /// <summary>The bonuses in effect, under the zone name.</summary>
        public void SetPowerUpLine(string text)
        {
            if (hudPowerText != null && hudPowerText.text != text) hudPowerText.text = text;
        }
    }
}
