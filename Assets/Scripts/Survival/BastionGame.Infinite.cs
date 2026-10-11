using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Platformer.Survival
{
    /// <summary>
    /// What keeps BASTION going once every tower is built and at its last level:
    ///
    /// · The AWAKENING: past level 5 a tower can be awakened again and again (★1, ★2, ...),
    ///   each star a quarter more damage, a little more reach and speed, dearer every time -
    ///   gold always has somewhere to go. At ★3 each kind of tower gains a gift of its own.
    /// · The ARCHIPELAGO: on the endless island, every ten waves a floating islet drifts in
    ///   and moors beside the island (five in all), each with two raised pads; a tower up
    ///   there sees further.
    /// · The SANCTUARY: every endless wave held pays Lumens, kept between runs and spent on
    ///   six rites that make every next defence stronger (damage, starting gold, lives,
    ///   loot, the hero's power, the price of towers).
    /// </summary>
    public partial class BastionGame
    {
        // ---- the Sanctuary ------------------------------------------------------------------

        enum Rite { Force, Treasure, Ramparts, Tithe, Hero, Architect }

        static readonly (string name, string text, int max, int baseCost)[] Rites =
        {
            ("Forge des tours", "+8 % de dégâts pour toutes les tours", 15, 6),
            ("Trésor", "+40 or au début de chaque partie", 8, 5),
            ("Remparts", "+3 vies au bastion", 6, 8),
            ("Dîme", "+10 % d'or par mort abattu", 8, 7),
            ("Ferveur", "Le pouvoir du héros revient 8 % plus vite", 5, 9),
            ("Architecte", "Tours et améliorations 5 % moins chères", 6, 10),
        };

        static int RiteLevel(Rite r) => PlayerPrefs.GetInt("bastion_rite_" + (int)r, 0);
        static void SetRiteLevel(Rite r, int v) { PlayerPrefs.SetInt("bastion_rite_" + (int)r, v); PlayerPrefs.Save(); }

        public static int Lumens
        {
            get => PlayerPrefs.GetInt("bastion_lumens", 0);
            set { PlayerPrefs.SetInt("bastion_lumens", Mathf.Max(0, value)); PlayerPrefs.Save(); }
        }

        static int RiteCost(Rite r)
        {
            int l = RiteLevel(r);
            return Mathf.RoundToInt(Rites[(int)r].baseCost * Mathf.Pow(l + 1, 1.55f));
        }

        static float SanctuaryDamage => 1f + 0.08f * RiteLevel(Rite.Force);
        static int StartGoldBonus => 40 * RiteLevel(Rite.Treasure);
        static int MaxLives => BastionCatalog.StartLives + 3 * RiteLevel(Rite.Ramparts);
        static float LootMult => 1f + 0.1f * RiteLevel(Rite.Tithe);
        static float PowerCooldownNow => BastionCatalog.PowerCooldown * (1f - 0.08f * RiteLevel(Rite.Hero));
        static float PriceMult => 1f - 0.05f * RiteLevel(Rite.Architect);
        static int Price(int baseCost) => Mathf.Max(1, Mathf.RoundToInt(baseCost * PriceMult));

        float lootCarry;
        int lumensThisRun;

        int Loot(int gold)
        {
            float g = gold * LootMult + lootCarry;
            int whole = Mathf.FloorToInt(g);
            lootCarry = g - whole;
            return whole;
        }

        /// <summary>Lumens for an endless wave held: a little for each, a handful every tenth.</summary>
        void AwardLumens(int n)
        {
            int l = 1 + n / 10 + (n % 10 == 0 ? 5 : 0);
            Lumens += l;
            lumensThisRun += l;
            Fx.Text(W(new Vector2(0f, 2f), -4f), $"+{l} Lumen{(l > 1 ? "s" : "")}", new Color(1f, 0.88f, 0.45f), 1.1f);
        }

        // ---- the Awakening -------------------------------------------------------------------

        int AwakenCost(Tower t) => Price(Mathf.RoundToInt(BastionCatalog.UpgradeCost(t.def, BastionCatalog.MaxTowerLevel) * 1.4f * Mathf.Pow(1.45f, t.stars)));

        float TDamage(Tower t) => BastionCatalog.DamageAt(t.def, t.level) * (1f + 0.25f * t.stars) * SanctuaryDamage;
        float TRange(Tower t) => BastionCatalog.RangeAt(t.def, t.level) + Mathf.Min(1f, 0.06f * t.stars) + (t.islet ? IsletReach : 0f)
            + (t.def.Kind == TowerKind.Brazier && t.stars >= 3 ? 0.5f : 0f);
        float TInterval(Tower t) => BastionCatalog.IntervalAt(t.def, t.level) * Mathf.Pow(0.97f, Mathf.Min(t.stars, 12));

        /// <summary>The gift each kind of tower gains at ★3.</summary>
        static string Gift(TowerKind k) => k switch
        {
            TowerKind.Archer => "les flèches transpercent un deuxième mort",
            TowerKind.Cannon => "l'explosion couvre moitié plus large",
            TowerKind.Brazier => "les flammes portent 0,5 m plus loin",
            TowerKind.Frost => "gèle net le mort touché un instant",
            _ => "l'éclair saute sur deux morts de plus",
        };

        void Awaken()
        {
            var t = selected;
            if (t == null || t.level < BastionCatalog.MaxTowerLevel) return;
            int cost = AwakenCost(t);
            if (gold < cost) return;
            gold -= cost;
            t.spent += cost;
            t.stars++;
            DrawTower(t);
            Sfx.Milestone();
            Fx.Burst(W(t.at, -3f) + Vector3.up * 0.8f, new Color(1f, 0.9f, 0.5f), 34, 4f, 0.12f, 0f);
            if (t.stars == 3) Say($"{t.def.Name}, Éveil 3 : {Gift(t.def.Kind)} !", 3f);
            rangeRing.transform.localScale = Vector3.one * TRange(t) * 2f;
            RefreshRing();
            RefreshHud();
        }

        /// <summary>The star crown of an awakened tower: a halo and its stars in a row.</summary>
        void DrawAwakening(Tower t, Transform parent, int order, float scale)
        {
            if (t.stars <= 0) return;
            Place("Awakened", Art("glow"), new Vector2(0f, 0.6f), (2.4f + 0.08f * Mathf.Min(t.stars, 10)) * scale, order - 3, 0.06f, parent,
                new Color(1f, 0.95f, 0.65f, Mathf.Min(0.65f, 0.35f + 0.04f * t.stars)));
            int shown = Mathf.Min(t.stars, 5);
            for (int k = 0; k < shown; k++)
            {
                float x = (k - (shown - 1) * 0.5f) * 0.26f;
                var star = Place($"Awake_{k}", PlaceholderVisuals.Star(), new Vector2(x, 1.75f * scale), 0.24f, order + 4, -0.12f, parent, new Color(1f, 0.95f, 0.6f));
                star.transform.localScale = Vector3.one * 0.24f;
            }
            if (t.stars > 5)
            {
                var label = new GameObject("StarCount");
                label.transform.SetParent(parent, false);
                label.transform.localPosition = new Vector3(0f, 2.02f * scale, -0.12f);
                var tm = label.AddComponent<TextMesh>();
                tm.text = $"x{t.stars}";
                tm.font = UiKit.Font;
                tm.fontSize = 48;
                tm.characterSize = 0.05f;
                tm.anchor = TextAnchor.MiddleCenter;
                tm.color = new Color(1f, 0.92f, 0.55f);
                var mr = label.GetComponent<MeshRenderer>();
                mr.sharedMaterial = UiKit.Font.material;
                mr.sortingOrder = order + 5;
            }
        }

        // ---- the Archipelago -------------------------------------------------------------------

        const float IsletReach = 0.9f;

        /// <summary>The islets in the order they arrive: where each moors, its picture, its two pads.</summary>
        static readonly (Vector2 at, int art, Vector2[] pads, Vector2 from)[] Islets =
        {
            (new Vector2(-3.6f, 6.15f), 0, new[] { new Vector2(-4.2f, 5.75f), new Vector2(-3.0f, 5.75f) }, new Vector2(-2f, 6f)),
            (new Vector2(2.1f, 6.15f), 1, new[] { new Vector2(1.5f, 5.75f), new Vector2(2.7f, 5.75f) }, new Vector2(2f, 6f)),
            (new Vector2(-10.7f, -3.0f), 2, new[] { new Vector2(-11.3f, -3.1f), new Vector2(-10.1f, -3.1f) }, new Vector2(-7f, 0f)),
            (new Vector2(10.6f, -1.5f), 0, new[] { new Vector2(10.0f, -1.6f), new Vector2(11.2f, -1.6f) }, new Vector2(7f, 0f)),
            (new Vector2(-0.8f, 6.9f), 2, new[] { new Vector2(-1.4f, 6.5f), new Vector2(-0.2f, 6.5f) }, new Vector2(0f, 7f)),
        };

        int isletsShown;
        int heldThisRun;
        readonly List<(Transform t, Vector2 target, float k)> isletMoves = new();

        int IsletsFor(int held) => map != null && map.Endless ? Mathf.Min(Islets.Length, held / 10) : 0;

        // The view grows to take the islets in.
        float FitTop => isletsShown >= 5 ? 7.9f : isletsShown >= 1 ? 7.3f : 4.95f;
        float FitBottom => -5.1f;
        float FitHalfW => isletsShown >= 3 ? 12.6f : 9.5f;
        float PanLeft => isletsShown >= 3 ? -12.8f : -9.8f;
        float PanRight => isletsShown >= 3 ? 12.8f : 9.8f;
        float PanBottom => -6.2f;
        float PanTop => isletsShown >= 5 ? 8.3f : isletsShown >= 1 ? 7.7f : 5.3f;

        /// <summary>Brings in the islets earned by the waves held (drifting in when animate).</summary>
        void SyncIslets(bool animate)
        {
            int want = IsletsFor(heldThisRun);
            while (isletsShown < want)
            {
                var def = Islets[isletsShown];
                var sr = Place($"Islet_{isletsShown}", Art($"islet_{def.art}", 0.5107f, 170.667f), def.at, 1f, -55, 6.5f);
                foreach (var p in def.pads)
                {
                    var pad = Place("IsletPad", Art("ring"), p + new Vector2(0f, -0.06f), 1.05f, -39, 5.45f, sr.transform.parent, new Color(1f, 0.9f, 0.55f, 0.75f));
                    pad.transform.position = W(p + new Vector2(0f, -0.06f), 5.45f);
                    pad.transform.SetParent(sr.transform, true);
                }
                if (animate)
                {
                    sr.transform.position = W(def.at + def.from, 6.5f);
                    isletMoves.Add((sr.transform, def.at, 0f));
                }
                isletsShown++;
            }
            if (want > 0) FrameCamera();
        }

        void AnimateIslets(float dt)
        {
            for (int i = isletMoves.Count - 1; i >= 0; i--)
            {
                var (t, target, k) = isletMoves[i];
                if (t == null) { isletMoves.RemoveAt(i); continue; }
                k = Mathf.Min(1f, k + dt * 0.5f);
                var goal = W(target, 6.5f);
                t.position = Vector3.Lerp(t.position, goal, 1f - Mathf.Pow(1f - k, 3f));
                if (k >= 1f) { t.position = goal; isletMoves.RemoveAt(i); }
                else isletMoves[i] = (t, target, k);
            }
        }

        /// <summary>The free islet pad under a tap, if any.</summary>
        bool IsletPadAt(Vector2 p, out Vector2 pad)
        {
            pad = default;
            for (int i = 0; i < isletsShown; i++)
                foreach (var q in Islets[i].pads)
                {
                    if ((q - p).sqrMagnitude > 0.55f * 0.55f) continue;
                    foreach (var t in towers) if ((t.at - q).sqrMagnitude < 0.01f) return false;
                    pad = q;
                    return true;
                }
            return false;
        }

        static bool IsIsletPad(Vector2 p)
        {
            foreach (var i in Islets) foreach (var q in i.pads) if ((q - p).sqrMagnitude < 0.01f) return true;
            return false;
        }

        // ---- the Sanctuary screen ----------------------------------------------------------------

        GameObject sanctuaryPanel;
        IconText sanctuaryLumens;
        Button sanctuaryButton;
        IconText sanctuaryButtonLabel;
        readonly List<(Text level, Button buy, IconText price, Image[] pips)> riteRows = new();

        void BuildSanctuary(RectTransform rt)
        {
            var p = UiKit.CreatePanel("BastionSanctuary", rt, ApogeeTheme.Maroon);
            sanctuaryPanel = p.gameObject;
            UiKit.CreateArtBackdrop("Backdrop", p, "menu_bg", 0.5f, 0.5f).transform.SetAsFirstSibling();
            UiKit.CreateImage("Shade", p, Vector2.zero, Vector2.one, PlaceholderVisuals.Square(Color.white), new Color(0.08f, 0.03f, 0.05f, 0.72f), false).raycastTarget = false;
            UiKit.Outlined(UiKit.CreateText("Title", p, "LE SANCTUAIRE", 66, TextAnchor.MiddleCenter, new Vector2(0.2f, 0.86f), new Vector2(0.8f, 0.97f), ApogeeTheme.Gold), 3f);
            UiKit.CreateText("Hint", p, "Chaque vague tenue sur l'île sans fin rapporte des Lumens. Ici, ils rendent chaque défense plus forte, pour toujours.", 22, TextAnchor.MiddleCenter,
                new Vector2(0.12f, 0.8f), new Vector2(0.88f, 0.86f), ApogeeTheme.Cream);
            sanctuaryLumens = IconText.Create("Lumens", p, "", 34, TextAnchor.MiddleCenter, new Vector2(0.3f, 0.73f), new Vector2(0.7f, 0.79f), ApogeeTheme.Gold, 1.5f);
            for (int i = 0; i < Rites.Length; i++)
            {
                int ri = i;
                int col = i % 2, row = i / 2;
                float x0 = 0.05f + col * 0.46f, y1 = 0.71f - row * 0.18f;
                var card = UiKit.CreateRect($"Rite_{i}", p, new Vector2(x0, y1 - 0.165f), new Vector2(x0 + 0.44f, y1));
                var bg = card.gameObject.AddComponent<Image>();
                bg.sprite = HubArt.Get("ui_tile", 32f) ?? ApogeeTheme.Panel;
                bg.type = Image.Type.Sliced;
                bg.color = new Color(0.85f, 0.78f, 0.82f, 0.95f);
                var icon = UiKit.CreateImage("Icon", card, new Vector2(0.02f, 0.15f), new Vector2(0.15f, 0.85f), GameIcons.Lumen, Color.white, true);
                icon.raycastTarget = false;
                var name = UiKit.Outlined(UiKit.CreateText("Name", card, Rites[i].name, 28, TextAnchor.MiddleLeft, new Vector2(0.17f, 0.56f), new Vector2(0.66f, 0.95f), ApogeeTheme.Gold), 1.5f);
                UiKit.FitLabel(name, 28);
                var text = UiKit.CreateText("Text", card, Rites[i].text, 19, TextAnchor.UpperLeft, new Vector2(0.17f, 0.3f), new Vector2(0.66f, 0.56f), ApogeeTheme.Cream);
                UiKit.FitLabel(text, 19);
                var level = UiKit.CreateText("Level", card, "", 18, TextAnchor.MiddleLeft, new Vector2(0.17f, 0.04f), new Vector2(0.4f, 0.28f), UiKit.TextDim);
                int max = Rites[i].max;
                var pips = new Image[max];
                for (int k = 0; k < max; k++)
                {
                    float px = 0.4f + k * (0.26f / max);
                    pips[k] = UiKit.CreateImage("Pip", card, new Vector2(px, 0.09f), new Vector2(px + 0.26f / max * 0.8f, 0.23f), PlaceholderVisuals.Circle(Color.white), Color.white, true);
                    pips[k].raycastTarget = false;
                }
                var buy = UiKit.CreateButton("Buy", card, "", new Vector2(0.68f, 0.2f), new Vector2(0.97f, 0.8f), () => BuyRite((Rite)ri), 24);
                var price = IconText.OnButton(buy, 24);
                buy.gameObject.AddComponent<ButtonPop>();
                riteRows.Add((level, buy, price, pips));
            }
            UiKit.CreateButton("Back", p, "RETOUR", new Vector2(0.4f, 0.04f), new Vector2(0.6f, 0.12f), () => { sanctuaryPanel.SetActive(false); ShowSelect(); }, 30);
            sanctuaryPanel.SetActive(false);
        }

        void ShowSanctuary()
        {
            selectPanel.SetActive(false);
            sanctuaryPanel.SetActive(true);
            sanctuaryPanel.transform.SetAsLastSibling();
            RefreshSanctuary();
        }

        void RefreshSanctuary()
        {
            sanctuaryLumens.text = $"{Lumens} [l]  Lumens";
            for (int i = 0; i < riteRows.Count; i++)
            {
                var r = (Rite)i;
                int lv = RiteLevel(r), max = Rites[i].max;
                var row = riteRows[i];
                row.level.text = $"Rang {lv}/{max}";
                for (int k = 0; k < row.pips.Length; k++) row.pips[k].color = k < lv ? ApogeeTheme.Gold : new Color(0f, 0f, 0f, 0.45f);
                if (lv >= max) { row.price.text = "MAX"; row.buy.interactable = false; continue; }
                int cost = RiteCost(r);
                row.price.text = $"{cost} [l]";
                row.buy.interactable = Lumens >= cost;
            }
            if (sanctuaryButtonLabel != null) sanctuaryButtonLabel.text = $"SANCTUAIRE   {Lumens} [l]";
        }

        void BuyRite(Rite r)
        {
            int lv = RiteLevel(r);
            if (lv >= Rites[(int)r].max) return;
            int cost = RiteCost(r);
            if (Lumens < cost) return;
            Lumens -= cost;
            SetRiteLevel(r, lv + 1);
            Sfx.Milestone();
            RefreshSanctuary();
        }
    }
}
