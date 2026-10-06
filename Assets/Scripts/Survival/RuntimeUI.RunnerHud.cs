using UnityEngine;
using UnityEngine.UI;
using Platformer.Mechanics;

namespace Platformer.Survival
{
    /// <summary>
    /// The runner's HUD, renewed: painted hearts in place of the red bar, and the Élan
    /// gauge under the distance - a gold bar with its multiplier that blazes during the
    /// Envol (see SurvivalDirector.Elan).
    /// </summary>
    public partial class RuntimeUI
    {
        const int MaxHearts = 12;
        readonly Image[] hudHearts = new Image[MaxHearts];
        GameObject elanRoot;
        Image elanFill, elanGlow;
        Text elanLabel;
        int lastHeartsShown = -1, lastHeartsMax = -1;

        void BuildRunnerHudExtras(RectTransform rt)
        {
            var heartTex = Resources.Load<Texture2D>("Bastion/heart");
            if (heartTex != null)
            {
                var heart = Sprite.Create(heartTex, new Rect(0, 0, heartTex.width, heartTex.height), new Vector2(0.5f, 0.5f), 100f);
                var row = CreateFixedRect("Hearts", rt, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(560, 50), new Vector2(30, -30));
                for (int i = 0; i < MaxHearts; i++)
                {
                    var img = UiKit.CreateImage($"Heart_{i}", row, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), heart, Color.white);
                    img.rectTransform.pivot = new Vector2(0f, 0.5f);
                    img.rectTransform.sizeDelta = new Vector2(46f, 46f);
                    img.rectTransform.anchoredPosition = new Vector2(i * 44f, 0f);
                    img.raycastTarget = false;
                    hudHearts[i] = img;
                }
                if (healthSlider != null) healthSlider.gameObject.SetActive(false);
            }

            // The Élan gauge.
            var root = CreateFixedRect("Elan", rt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(380, 40), new Vector2(0, -92));
            elanRoot = root.gameObject;
            elanGlow = UiKit.CreateImage("Glow", root, Vector2.zero, Vector2.one, ApogeeTheme.Chip, new Color(1f, 0.75f, 0.3f, 0f), false);
            elanGlow.type = Image.Type.Sliced;
            elanGlow.rectTransform.offsetMin = new Vector2(-10, -8);
            elanGlow.rectTransform.offsetMax = new Vector2(10, 8);
            elanGlow.raycastTarget = false;
            var bg = UiKit.CreateImage("Bg", root, Vector2.zero, Vector2.one, ApogeeTheme.Chip, Color.white, false);
            bg.type = Image.Type.Sliced;
            bg.raycastTarget = false;
            var fillRt = UiKit.CreateRect("Fill", root, Vector2.zero, Vector2.one);
            fillRt.offsetMin = new Vector2(7, 7);
            fillRt.offsetMax = new Vector2(-7, -7);
            elanFill = fillRt.gameObject.AddComponent<Image>();
            elanFill.sprite = ApogeeTheme.FrameFill;
            elanFill.type = Image.Type.Filled;
            elanFill.fillMethod = Image.FillMethod.Horizontal;
            elanFill.color = ApogeeTheme.Gold;
            elanFill.raycastTarget = false;
            elanLabel = UiKit.Outlined(UiKit.CreateText("Label", root, "ÉLAN", 22, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, ApogeeTheme.Cream), 1.5f);
            elanRoot.SetActive(false);

            // The zone and bonus lines move down under the gauge.
            if (hudZoneText != null) hudZoneText.rectTransform.anchoredPosition = new Vector2(0, -138);
            if (hudPowerText != null) hudPowerText.rectTransform.anchoredPosition = new Vector2(0, -168);
        }

        void UpdateHearts(Health health)
        {
            if (health == null || hudHearts[0] == null) return;
            int max = Mathf.Clamp(health.maxHP, 1, MaxHearts);
            int hp = Mathf.Clamp(Mathf.RoundToInt(health.NormalizedHP * health.maxHP), 0, max);
            if (hp == lastHeartsShown && max == lastHeartsMax) return;
            if (hp < lastHeartsShown && lastHeartsShown > 0 && hp < MaxHearts)
                StartCoroutine(PopHeart(hudHearts[hp].rectTransform));
            lastHeartsShown = hp;
            lastHeartsMax = max;
            for (int i = 0; i < MaxHearts; i++)
            {
                hudHearts[i].gameObject.SetActive(i < max);
                hudHearts[i].color = i < hp ? Color.white : new Color(0.25f, 0.12f, 0.12f, 0.75f);
            }
        }

        System.Collections.IEnumerator PopHeart(RectTransform heart)
        {
            float t = 0f;
            while (t < 0.3f)
            {
                t += Time.unscaledDeltaTime;
                heart.localScale = Vector3.one * (1f + Mathf.Sin(t / 0.3f * Mathf.PI) * 0.5f);
                yield return null;
            }
            heart.localScale = Vector3.one;
        }

        public void UpdateElan(float fill, int multiplier, bool envol)
        {
            if (elanRoot == null) return;
            if (!elanRoot.activeSelf) elanRoot.SetActive(true);
            elanFill.fillAmount = Mathf.Lerp(elanFill.fillAmount, fill, Time.deltaTime * 10f);
            string label = envol ? "ENVOL !" : multiplier > 1 ? $"ÉLAN  x{multiplier}" : "ÉLAN";
            if (elanLabel.text != label) elanLabel.text = label;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * (envol ? 14f : 4f));
            elanGlow.color = new Color(1f, 0.75f, 0.3f, envol ? 0.5f + 0.4f * pulse : multiplier >= 4 ? 0.25f * pulse : 0f);
            elanFill.color = envol ? Color.Lerp(ApogeeTheme.Gold, Color.white, pulse * 0.6f)
                : multiplier >= 3 ? new Color(1f, 0.62f, 0.25f) : ApogeeTheme.Gold;
        }

        void HideElan()
        {
            if (elanRoot != null && elanRoot.activeSelf) elanRoot.SetActive(false);
        }
    }
}
