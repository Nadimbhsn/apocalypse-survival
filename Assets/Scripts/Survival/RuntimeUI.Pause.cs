using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Platformer.Survival
{
    /// <summary>
    /// The pause menu shared by every game, and the upgrade rows it has in common with the
    /// AMÉLIORATIONS page. Each game puts a PAUSE button in its own HUD (CreatePauseButton)
    /// and hands Pause() what "quit" means for it; the menu freezes time and sound, lets the
    /// player buy upgrades on the spot, and resumes or quits.
    /// </summary>
    public partial class RuntimeUI
    {
        /// <summary>One upgrade list on screen: per stat, its level text and buy button.</summary>
        class UpgradeRowSet
        {
            public readonly Dictionary<UpgradeStat, Text> levels = new();
            public readonly Dictionary<UpgradeStat, Button> buttons = new();
            public readonly Dictionary<UpgradeStat, IconText> labels = new();
        }

        static readonly UpgradeStat[] UpgradeStats =
        {
            UpgradeStat.Speed, UpgradeStat.FirePower, UpgradeStat.MaxHealth, UpgradeStat.Armor,
            UpgradeStat.DoubleJump, UpgradeStat.Magnet, UpgradeStat.Drone,
        };

        GameObject pausePanel;
        IconText pauseWalletText;
        readonly UpgradeRowSet pauseRows = new();
        readonly List<Button> pauseButtons = new();
        Action pauseQuit;
        Action<UpgradeStat> pauseUpgraded;

        public bool IsPaused => pausePanel != null && pausePanel.activeSelf;

        /// <summary>
        /// One row per upgrade: name, level, and a buy button showing the price. Columns are
        /// laid out between xMin and xMax; onBought runs after a successful purchase.
        /// </summary>
        void BuildUpgradeRows(RectTransform rt, UpgradeRowSet set, float startY, float rowH, int labelSize,
            float xMin, float xMax, Action<UpgradeStat> onBought)
        {
            float X(float f) => xMin + (f - 0.08f) / 0.86f * (xMax - xMin);
            for (int i = 0; i < UpgradeStats.Length; i++)
            {
                var stat = UpgradeStats[i];
                float yMax = startY - i * rowH;
                float yMin = yMax - rowH * 0.78f;

                var label = UiKit.Outlined(UiKit.CreateText($"Label_{stat}", rt, StatLabel(stat), labelSize, TextAnchor.MiddleLeft,
                    new Vector2(X(0.08f), yMin), new Vector2(X(0.50f), yMax), ApogeeTheme.Cream), 1.5f);
                UiKit.FitLabel(label, labelSize);
                var level = UiKit.CreateText($"Level_{stat}", rt, "Niveau 0/10", labelSize - 6, TextAnchor.MiddleLeft,
                    new Vector2(X(0.51f), yMin), new Vector2(X(0.74f), yMax), ApogeeTheme.Gold);
                UiKit.FitLabel(level, labelSize - 6);
                set.levels[stat] = level;

                var captured = stat;
                var btn = UiKit.CreateButton($"Buy_{stat}", rt, "+1", new Vector2(X(0.76f), yMin), new Vector2(X(0.94f), yMax), () =>
                {
                    if (UpgradeManager.TryPurchase(captured))
                    {
                        Sfx.Coin();
                        onBought?.Invoke(captured);
                    }
                    RefreshUpgradeRows(set);
                }, 22);
                set.buttons[stat] = btn;
                set.labels[stat] = IconText.OnButton(btn, labelSize - 4);
            }
        }

        static void RefreshUpgradeRows(UpgradeRowSet set)
        {
            foreach (var stat in set.levels.Keys)
            {
                int level = SaveSystem.GetLevel(stat);
                int max = UpgradeManager.MaxLevelFor(stat);
                set.levels[stat].text = $"Niveau {level}/{max}";

                var btn = set.buttons[stat];
                var label = set.labels[stat];
                if (level >= max)
                {
                    label.text = "MAX";
                    btn.interactable = false;
                    continue;
                }
                int cost = UpgradeManager.CostForNextLevel(stat);
                bool materials = stat == UpgradeStat.Armor || stat == UpgradeStat.Drone;
                label.text = $"{cost} {(materials ? "[g]" : "[c]")}";
                // Greyed out when the wallet cannot cover it, so the eye goes to what is buyable.
                btn.interactable = (materials ? SaveSystem.Materials : SaveSystem.Coins) >= cost;
            }
        }

        void BuildPausePanel()
        {
            var rt = UiKit.CreatePanel("PausePanel", canvas.transform, UiKit.Overlay);
            pausePanel = rt.gameObject;
            UiKit.CreateFrame("PauseFrame", rt, new Vector2(0.05f, 0.1f), new Vector2(0.95f, 0.9f));

            UiKit.Outlined(UiKit.CreateText("PauseTitle", rt, "PAUSE", 56, TextAnchor.MiddleCenter,
                new Vector2(0.1f, 0.80f), new Vector2(0.9f, 0.875f), ApogeeTheme.Gold), 2.5f);
            pauseWalletText = CreateIconChip("Wallet", rt, new Vector2(0.25f, 0.748f), new Vector2(0.75f, 0.79f), 26);
            UiKit.CreateText("PauseHint", rt, "Améliore ton personnage sans quitter la partie", 20, TextAnchor.MiddleCenter,
                new Vector2(0.08f, 0.705f), new Vector2(0.92f, 0.742f), UiKit.TextDim);

            BuildUpgradeRows(rt, pauseRows, 0.695f, 0.058f, 24, 0.09f, 0.91f, stat =>
            {
                pauseUpgraded?.Invoke(stat);
                pauseWalletText.text = WalletLine();
            });

            UiKit.CreateButton("Resume", rt, "REPRENDRE", new Vector2(0.2f, 0.2f), new Vector2(0.8f, 0.27f), Resume, 34);
            UiKit.CreateButton("QuitGame", rt, "QUITTER LA PARTIE", new Vector2(0.25f, 0.125f), new Vector2(0.75f, 0.185f),
                QuitFromPause, 22, new Color(0.22f, 0.10f, 0.08f));
            pausePanel.SetActive(false);
        }

        /// <summary>A PAUSE button for a game's HUD. It is also what Escape and leaving the app press.</summary>
        public Button CreatePauseButton(Transform parent, Vector2 anchorMin, Vector2 anchorMax, UnityAction onClick)
        {
            var btn = UiKit.CreateButton("Pause", parent, "PAUSE", anchorMin, anchorMax, onClick, 22, new Color(0.22f, 0.10f, 0.08f));
            pauseButtons.Add(btn);
            return btn;
        }

        /// <summary>
        /// Freezes the game under the pause menu. onQuit says how this game is left;
        /// onUpgraded lets it apply an upgrade bought here at once.
        /// </summary>
        public void Pause(Action onQuit, Action<UpgradeStat> onUpgraded)
        {
            // Already frozen (a death screen, a level result): nothing to pause.
            if (pausePanel == null || IsPaused || Time.timeScale <= 0f) return;
            pauseQuit = onQuit;
            pauseUpgraded = onUpgraded;
            Time.timeScale = 0f;
            AudioListener.pause = true;
            MobileInput.Reset();
            pauseWalletText.text = WalletLine();
            RefreshUpgradeRows(pauseRows);
            pausePanel.transform.SetAsLastSibling();
            pausePanel.SetActive(true);
        }

        public void Resume()
        {
            if (!IsPaused) return;
            pausePanel.SetActive(false);
            AudioListener.pause = false;
            // Always back to normal speed: a hit-stop caught by the pause has lapsed meanwhile.
            Time.timeScale = 1f;
            pauseQuit = null;
            pauseUpgraded = null;
        }

        /// <summary>Closes the menu without asking, when a game is left some other way.</summary>
        public void ClosePause() => Resume();

        void QuitFromPause()
        {
            var quit = pauseQuit;
            Resume();
            quit?.Invoke();
        }

        void PressVisiblePauseButton()
        {
            foreach (var b in pauseButtons)
            {
                if (b == null || !b.isActiveAndEnabled || !b.interactable) continue;
                b.onClick.Invoke();
                return;
            }
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || !kb.escapeKey.wasPressedThisFrame) return;
            if (IsPaused) Resume();
            else PressVisiblePauseButton();
        }

        /// <summary>A call or a swipe home mid-game: come back to a paused game, not a lost one.</summary>
        void OnApplicationPause(bool paused)
        {
            if (paused && !IsPaused) PressVisiblePauseButton();
        }

        void PauseRunner()
        {
            if (director == null || !director.IsRunning) return;
            Pause(() => { director.QuitRun(); ShowHub(); }, director.ApplyUpgradeMidRun);
        }
    }
}
