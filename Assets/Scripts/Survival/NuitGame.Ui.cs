using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Platformer.Survival
{
    /// <summary>
    /// DERNIÈRE NUIT's screens: the title, the in-game HUD (joystick, health and shield,
    /// scrap, the boss's bar, banners), the bonus picked after each wave, the shop between
    /// waves, the end of the night and the leaderboard.
    /// </summary>
    public partial class NuitGame
    {
        RectTransform uiRoot;
        GameObject gameUi, menuUi, bonusUi, shopUi, overUi, boardUi;

        // HUD
        Text waveText, subText, hpText, messageText, bannerTitle, bannerSub, bossText, chanceText;
        IconText scrapText;
        Image hpFill, shieldFill, bossFill, dodgeFill;
        GameObject bossBar, dodgeButton, banner;
        CanvasGroup bannerGroup;
        float bannerTime, messageTime;

        // menu / over / board
        Text menuRecord, overTitle, overStats, overRank;
        IconText overCoins;
        readonly List<Text> boardRows = new();

        // bonus
        readonly List<(Button button, Text name, Text text)> bonusCards = new();
        readonly List<int> offered = new();

        // shop
        IconText shopScrap;
        Text shopTitle;
        Button nextWaveButton;
        Text nextWaveLabel;
        readonly List<System.Action> shopRefresh = new();

        protected override void BuildUi()
        {
            uiRoot = UiKit.CreateRect("NuitPanel", ui.Canvas.transform, Vector2.zero, Vector2.one);
            panel = uiRoot.gameObject;
            BuildHud();
            BuildMenu();
            BuildBonus();
            BuildShop();
            BuildOver();
            BuildBoard();
        }

        static IconText Pill(RectTransform parent, string name, Vector2 min, Vector2 max, int size)
        {
            var chip = UiKit.CreateRect(name, parent, min, max);
            var img = chip.gameObject.AddComponent<Image>();
            img.sprite = HubArt.Get("ui_pill", 30f) ?? ApogeeTheme.Chip;
            img.type = Image.Type.Sliced;
            img.raycastTarget = false;
            var label = IconText.Create(name + "_Text", chip, "", size, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, ApogeeTheme.Cream, 1.2f);
            var lrt = (RectTransform)label.transform;
            lrt.offsetMin = new Vector2(12, 4);
            lrt.offsetMax = new Vector2(-12, -4);
            return label;
        }

        static Image Bar(RectTransform parent, string name, Vector2 min, Vector2 max, Color col, out Image fill)
        {
            var bar = UiKit.CreateRect(name, parent, min, max);
            var bg = bar.gameObject.AddComponent<Image>();
            bg.sprite = HubArt.Get("ui_pill", 30f) ?? ApogeeTheme.Chip;
            bg.type = Image.Type.Sliced;
            bg.color = new Color(0.18f, 0.08f, 0.08f, 0.9f);
            bg.raycastTarget = false;
            var f = UiKit.CreateRect("Fill", bar, Vector2.zero, Vector2.one);
            f.offsetMin = new Vector2(6, 5);
            f.offsetMax = new Vector2(-6, -5);
            fill = f.gameObject.AddComponent<Image>();
            fill.sprite = ApogeeTheme.FrameFill;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.color = col;
            fill.raycastTarget = false;
            return bg;
        }

        RectTransform MakeScreen(string name, bool art)
        {
            var rt = UiKit.CreateRect(name, uiRoot, Vector2.zero, Vector2.one);
            var bg = rt.gameObject.AddComponent<Image>();
            bg.color = new Color(0.03f, 0.03f, 0.08f, art ? 1f : 0.82f);
            if (art)
            {
                var tex = HubArt.Texture("card_nuit");
                if (tex != null)
                {
                    var a = UiKit.CreateRect("Art", rt, Vector2.zero, Vector2.one);
                    var raw = a.gameObject.AddComponent<RawImage>();
                    raw.texture = tex;
                    raw.raycastTarget = false;
                    a.gameObject.AddComponent<CoverImage>().focus = new Vector2(0.5f, 0.6f);
                }
                var fade = UiKit.CreateImage("Fade", rt, Vector2.zero, new Vector2(1f, 0.62f), ApogeeTheme.VerticalFade, new Color(0.03f, 0.02f, 0.06f, 0.97f), false);
                fade.raycastTarget = false;
            }
            return rt;
        }

        // ---- HUD -------------------------------------------------------------------------------------

        void BuildHud()
        {
            var g = UiKit.CreateRect("GameUi", uiRoot, Vector2.zero, Vector2.one);
            gameUi = g.gameObject;

            // the joystick: anywhere on the lower part of the screen
            var stickZone = UiKit.CreateRect("StickZone", g, new Vector2(0f, 0f), new Vector2(1f, 0.7f));
            stickZone.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0f);
            var stick = stickZone.gameObject.AddComponent<NuitStick>();
            var ring = UiKit.CreateImage("Ring", stickZone, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), PlaceholderVisuals.RimCircle(Color.white), new Color(1f, 1f, 1f, 0.35f), false);
            ring.rectTransform.sizeDelta = new Vector2(260f, 260f);
            ring.raycastTarget = false;
            var knob = UiKit.CreateImage("Knob", stickZone, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), PlaceholderVisuals.Circle(Color.white), new Color(1f, 0.9f, 0.7f, 0.6f), false);
            knob.rectTransform.sizeDelta = new Vector2(110f, 110f);
            knob.raycastTarget = false;
            stick.ring = ring.rectTransform;
            stick.knob = knob.rectTransform;
            ring.gameObject.SetActive(false);
            knob.gameObject.SetActive(false);

            // top: the wave, the threat, pause
            var top = UiKit.CreateRect("Top", g, new Vector2(0f, 0.92f), new Vector2(1f, 1f));
            var shade = UiKit.CreateImage("Shade", top, Vector2.zero, Vector2.one, ApogeeTheme.VerticalFade, new Color(0.02f, 0.02f, 0.06f, 0.85f), false);
            shade.rectTransform.localScale = new Vector3(1f, -1f, 1f);
            shade.raycastTarget = false;
            waveText = UiKit.Outlined(UiKit.CreateText("Wave", top, "", 44, TextAnchor.MiddleLeft, new Vector2(0.04f, 0.38f), new Vector2(0.7f, 1f), ApogeeTheme.Gold), 2.5f);
            UiKit.FitLabel(waveText, 44);
            subText = UiKit.Outlined(UiKit.CreateText("Sub", top, "", 22, TextAnchor.MiddleLeft, new Vector2(0.04f, 0.02f), new Vector2(0.72f, 0.4f), ApogeeTheme.Cream), 1.5f);
            UiKit.FitLabel(subText, 22);
            ui.CreatePauseButton(top, new Vector2(0.74f, 0.2f), new Vector2(0.96f, 0.85f), () => { if (phase == Phase.Wave) ui.Pause(QuitNight, null); });

            // health with the shield over it, the second chance, scrap
            Bar(g, "Hp", new Vector2(0.03f, 0.87f), new Vector2(0.62f, 0.905f), new Color(0.85f, 0.24f, 0.2f), out hpFill);
            var sh = UiKit.CreateRect("ShieldFill", hpFill.rectTransform.parent as RectTransform, Vector2.zero, Vector2.one);
            sh.offsetMin = new Vector2(6, 5); sh.offsetMax = new Vector2(-6, -5);
            shieldFill = sh.gameObject.AddComponent<Image>();
            shieldFill.sprite = ApogeeTheme.FrameFill;
            shieldFill.type = Image.Type.Filled;
            shieldFill.fillMethod = Image.FillMethod.Horizontal;
            shieldFill.color = new Color(0.5f, 0.85f, 1f, 0.55f);
            shieldFill.raycastTarget = false;
            hpText = UiKit.Outlined(UiKit.CreateText("HpText", hpFill.rectTransform.parent, "", 20, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, ApogeeTheme.Cream), 1.2f);
            scrapText = Pill(g, "Scrap", new Vector2(0.64f, 0.865f), new Vector2(0.97f, 0.91f), 26);
            chanceText = UiKit.Outlined(UiKit.CreateText("Chance", g, "", 18, TextAnchor.MiddleLeft, new Vector2(0.04f, 0.84f), new Vector2(0.62f, 0.868f), ApogeeTheme.Gold), 1.2f);

            // the boss's bar
            var bb = UiKit.CreateRect("Boss", g, new Vector2(0.08f, 0.8f), new Vector2(0.92f, 0.835f));
            bossBar = bb.gameObject;
            Bar(bb, "BossBar", Vector2.zero, Vector2.one, new Color(0.75f, 0.12f, 0.12f), out bossFill);
            bossText = UiKit.Outlined(UiKit.CreateText("Name", bb, "", 20, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, ApogeeTheme.Cream), 1.2f);

            messageText = UiKit.Outlined(UiKit.CreateText("Message", g, "", 28, TextAnchor.MiddleCenter, new Vector2(0.04f, 0.74f), new Vector2(0.96f, 0.79f), ApogeeTheme.Cream), 2f);
            UiKit.FitLabel(messageText, 28);

            // the dodge button
            var dr = UiKit.CreateRect("Dodge", g, new Vector2(0.72f, 0.06f), new Vector2(0.96f, 0.19f));
            dodgeButton = dr.gameObject;
            var dimg = dr.gameObject.AddComponent<Image>();
            dimg.sprite = HubArt.Get("ui_disc") ?? PlaceholderVisuals.Circle(Color.white);
            dimg.color = new Color(0.45f, 0.75f, 1f, 0.9f);
            dimg.preserveAspect = true;
            var hold = dr.gameObject.AddComponent<HoldButton>();
            hold.onDown = TryDodge;
            var df = UiKit.CreateImage("Cooldown", dr, Vector2.zero, Vector2.one, PlaceholderVisuals.Circle(Color.white), new Color(0f, 0f, 0f, 0.55f), true);
            dodgeFill = df;
            dodgeFill.type = Image.Type.Filled;
            dodgeFill.fillMethod = Image.FillMethod.Radial360;
            dodgeFill.raycastTarget = false;
            UiKit.Outlined(UiKit.CreateText("Label", dr, "ESQUIVE", 22, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, ApogeeTheme.Cream), 1.5f);

            // the big banner for waves, bosses and the planet's events
            var bn = UiKit.CreateRect("Banner", g, new Vector2(0f, 0.55f), new Vector2(1f, 0.7f));
            banner = bn.gameObject;
            bannerGroup = bn.gameObject.AddComponent<CanvasGroup>();
            bannerGroup.blocksRaycasts = false;
            var bimg = UiKit.CreateImage("Shade", bn, Vector2.zero, Vector2.one, PlaceholderVisuals.Square(Color.white), new Color(0.02f, 0.01f, 0.05f, 0.65f), false);
            bimg.raycastTarget = false;
            bannerTitle = UiKit.Outlined(UiKit.CreateText("Title", bn, "", 64, TextAnchor.MiddleCenter, new Vector2(0.04f, 0.42f), new Vector2(0.96f, 0.95f), ApogeeTheme.Gold), 3f);
            UiKit.FitLabel(bannerTitle, 64);
            bannerSub = UiKit.Outlined(UiKit.CreateText("Sub", bn, "", 26, TextAnchor.MiddleCenter, new Vector2(0.06f, 0.05f), new Vector2(0.94f, 0.42f), ApogeeTheme.Cream), 1.5f);
            UiKit.FitLabel(bannerSub, 26);
            banner.SetActive(false);
            gameUi.SetActive(false);
        }

        void ShowGameUi()
        {
            HideScreens();
            gameUi.SetActive(true);
        }

        void HideScreens()
        {
            gameUi.SetActive(false); menuUi.SetActive(false); bonusUi.SetActive(false);
            shopUi.SetActive(false); overUi.SetActive(false); boardUi.SetActive(false);
            NuitStick.Release();
        }

        void ShowBanner(string title, string sub)
        {
            bannerTitle.text = title;
            bannerSub.text = sub ?? "";
            banner.SetActive(true);
            bannerTime = 2.8f;
        }

        void ShowMessage(string text)
        {
            messageText.text = text;
            messageTime = 2.2f;
        }

        void UpdateHud()
        {
            if (gameUi == null || !gameUi.activeSelf) return;
            float dt = Time.unscaledDeltaTime;
            waveText.text = $"VAGUE {wave}";
            int left = toSpawn + foes.Count;
            int standing = 0, total = 0;
            foreach (var d in doors) if (zones[d.Zone].Open) { total++; if (d.Intact) standing++; }
            subText.text = (nightEvent != NightEvent.None ? EventTitle(nightEvent) + "  ·  " : "") + $"{left} morts  ·  portes {standing}/{total}";
            hpFill.fillAmount = maxHp > 0f ? hp / maxHp : 0f;
            float cap = ShieldCap;
            shieldFill.fillAmount = cap > 0f ? shield / maxHp : 0f;
            hpText.text = $"{Mathf.CeilToInt(hp)} / {Mathf.CeilToInt(maxHp)}" + (shield > 0.5f ? $"  +{Mathf.CeilToInt(shield)}" : "");
            scrapText.text = $"{scrap} [d]";
            chanceText.text = secondChanceUsed ? "Seconde chance utilisée" : "Seconde chance : prête";
            chanceText.color = secondChanceUsed ? new Color(0.7f, 0.5f, 0.5f) : ApogeeTheme.Gold;
            bossBar.SetActive(boss != null && !boss.Dead && phase == Phase.Wave);
            if (boss != null) { bossFill.fillAmount = boss.Hp / boss.MaxHp; bossText.text = bossName; }
            dodgeButton.SetActive(gadget[(int)Gadget.Dodge] > 0);
            dodgeFill.fillAmount = Mathf.Clamp01(dashCooldown / DodgeCooldown);
            if (bannerTime > 0f)
            {
                bannerTime -= dt;
                bannerGroup.alpha = Mathf.Clamp01(bannerTime / 0.5f) * Mathf.Clamp01((2.8f - bannerTime) / 0.25f);
                if (bannerTime <= 0f) banner.SetActive(false);
            }
            if (messageTime > 0f)
            {
                messageTime -= dt;
                var c = messageText.color; c.a = Mathf.Clamp01(messageTime / 0.4f); messageText.color = c;
                if (messageTime <= 0f) messageText.text = "";
            }
        }

        // ---- title -------------------------------------------------------------------------------------

        void BuildMenu()
        {
            var rt = MakeScreen("Menu", true);
            menuUi = rt.gameObject;
            UiKit.Outlined(UiKit.CreateText("Title", rt, "DERNIÈRE NUIT", 84, TextAnchor.MiddleCenter, new Vector2(0.05f, 0.5f), new Vector2(0.95f, 0.6f), ApogeeTheme.Gold), 3.5f);
            UiKit.CreateText("Pitch", rt, "Piégé dans une station-service abandonnée. Les morts arrivent par vagues, toujours plus nombreux. Tiens le plus longtemps possible.", 26, TextAnchor.UpperCenter,
                new Vector2(0.08f, 0.4f), new Vector2(0.92f, 0.49f), ApogeeTheme.Cream);
            menuRecord = UiKit.Outlined(UiKit.CreateText("Record", rt, "", 28, TextAnchor.MiddleCenter, new Vector2(0.08f, 0.35f), new Vector2(0.92f, 0.39f), ApogeeTheme.Gold), 1.5f);
            UiKit.CreateButton("Play", rt, "JOUER", new Vector2(0.18f, 0.24f), new Vector2(0.82f, 0.32f), StartNight, 44);
            UiKit.CreateButton("Board", rt, "CLASSEMENT", new Vector2(0.24f, 0.155f), new Vector2(0.76f, 0.215f), ShowBoard, 28, new Color(0.22f, 0.10f, 0.08f));
            UiKit.CreateButton("Back", rt, "RETOUR", new Vector2(0.32f, 0.06f), new Vector2(0.68f, 0.12f), ReturnToHub, 26, new Color(0.22f, 0.10f, 0.08f));
            menuUi.SetActive(false);
        }

        void ShowMenu()
        {
            phase = Phase.Menu;
            HideScreens();
            if (hero != null) hero.gameObject.SetActive(false);
            var b = LoadBoard();
            menuRecord.text = b.entries.Count > 0 ? $"Record : {b.entries[0].score} pts  ·  vague {b.entries[0].wave}" : "Aucune nuit au classement";
            menuUi.SetActive(true);
        }

        // ---- bonus ------------------------------------------------------------------------------------

        void BuildBonus()
        {
            var rt = MakeScreen("Bonus", false);
            bonusUi = rt.gameObject;
            UiKit.Outlined(UiKit.CreateText("Title", rt, "VAGUE TENUE !", 60, TextAnchor.MiddleCenter, new Vector2(0.05f, 0.8f), new Vector2(0.95f, 0.88f), ApogeeTheme.Gold), 3f);
            UiKit.CreateText("Hint", rt, "Choisis un bonus pour la suite de la nuit", 28, TextAnchor.MiddleCenter, new Vector2(0.05f, 0.75f), new Vector2(0.95f, 0.8f), ApogeeTheme.Cream);
            for (int i = 0; i < 3; i++)
            {
                float y1 = 0.72f - i * 0.2f, y0 = y1 - 0.18f;
                var card = UiKit.CreateRect($"Card_{i}", rt, new Vector2(0.08f, y0), new Vector2(0.92f, y1));
                var img = card.gameObject.AddComponent<Image>();
                var btn = card.gameObject.AddComponent<Button>();
                UiKit.ApplyButtonStyle(btn, img, UiKit.CardColor);
                card.gameObject.AddComponent<ButtonPop>();
                var outline = card.gameObject.AddComponent<Outline>();
                outline.effectColor = ApogeeTheme.Gold;
                outline.effectDistance = new Vector2(4f, -4f);
                var name = UiKit.Outlined(UiKit.CreateText("Name", card, "", 40, TextAnchor.MiddleCenter, new Vector2(0.05f, 0.5f), new Vector2(0.95f, 0.92f), ApogeeTheme.Gold), 2f);
                UiKit.FitLabel(name, 40);
                var text = UiKit.CreateText("Text", card, "", 28, TextAnchor.MiddleCenter, new Vector2(0.05f, 0.1f), new Vector2(0.95f, 0.5f), ApogeeTheme.Cream);
                UiKit.FitLabel(text, 28);
                int captured = i;
                btn.onClick.AddListener(() => PickBonus(captured));
                bonusCards.Add((btn, name, text));
            }
            bonusUi.SetActive(false);
        }

        void ShowBonusChoice()
        {
            HideScreens();
            offered.Clear();
            var pool = new List<int>();
            for (int i = 0; i < Perks.Length; i++)
            {
                if (perkStacks[(int)Perks[i].perk] >= Perks[i].max) continue;
                if (Perks[i].perk == Perk.Heal && hp > maxHp * 0.8f) continue;
                pool.Add(i);
            }
            for (int k = 0; k < 3 && pool.Count > 0; k++)
            {
                int j = Random.Range(0, pool.Count);
                offered.Add(pool[j]);
                pool.RemoveAt(j);
            }
            for (int k = 0; k < bonusCards.Count; k++)
            {
                bool has = k < offered.Count;
                bonusCards[k].button.gameObject.SetActive(has);
                if (!has) continue;
                var p = Perks[offered[k]];
                int stacks = perkStacks[(int)p.perk];
                bonusCards[k].name.text = p.name + (stacks > 0 && p.max < 99 ? $"  ({stacks + 1})" : "");
                bonusCards[k].text.text = p.text;
            }
            bonusUi.SetActive(true);
            if (offered.Count == 0) ShowShop();
        }

        void PickBonus(int card)
        {
            if (card >= offered.Count) return;
            var p = Perks[offered[card]].perk;
            switch (p)
            {
                case Perk.Vitality: perkStacks[(int)p]++; maxHp += 20f; hp = Mathf.Min(maxHp, hp + 20f); break;
                case Perk.Heal: hp = Mathf.Min(maxHp, hp + maxHp * 0.6f); break;
                case Perk.Doors:
                    perkStacks[(int)p]++;
                    foreach (var d in doors) { d.MaxHp = DoorMaxHp; d.Hp = d.MaxHp; d.State = -1; SetDoorLook(d); }
                    break;
                default: perkStacks[(int)p]++; break;
            }
            Sfx.Milestone();
            ShowShop();
        }

        // ---- the shop between waves -----------------------------------------------------------------------

        void BuildShop()
        {
            var rt = MakeScreen("Shop", false);
            shopUi = rt.gameObject;
            shopTitle = UiKit.Outlined(UiKit.CreateText("Title", rt, "", 46, TextAnchor.MiddleCenter, new Vector2(0.05f, 0.925f), new Vector2(0.95f, 0.975f), ApogeeTheme.Gold), 2.5f);
            shopScrap = Pill(rt, "Scrap", new Vector2(0.3f, 0.885f), new Vector2(0.7f, 0.92f), 28);

            float y = 0.87f;
            const float rowH = 0.047f;
            void Header(string text)
            {
                UiKit.Outlined(UiKit.CreateText("H_" + text, rt, text, 24, TextAnchor.LowerLeft, new Vector2(0.05f, y - 0.03f), new Vector2(0.95f, y), ApogeeTheme.Gold), 1.4f);
                y -= 0.032f;
            }
            void Row(string label, System.Func<string> detail, System.Func<(string text, bool enabled)> button, UnityEngine.Events.UnityAction onClick)
            {
                float y0 = y - rowH + 0.006f;
                var name = UiKit.Outlined(UiKit.CreateText("L_" + label, rt, label, 26, TextAnchor.MiddleLeft, new Vector2(0.05f, y0 + rowH * 0.42f), new Vector2(0.66f, y), ApogeeTheme.Cream), 1.2f);
                UiKit.FitLabel(name, 26);
                var det = UiKit.CreateText("D_" + label, rt, "", 18, TextAnchor.UpperLeft, new Vector2(0.05f, y0), new Vector2(0.66f, y0 + rowH * 0.45f), UiKit.TextDim);
                UiKit.FitLabel(det, 18);
                var btn = UiKit.CreateButton("B_" + label, rt, "", new Vector2(0.68f, y0 + 0.004f), new Vector2(0.95f, y - 0.004f), onClick, 22);
                var lbl = IconText.OnButton(btn, 22);
                shopRefresh.Add(() =>
                {
                    det.text = detail();
                    var (t, en) = button();
                    lbl.text = t;
                    btn.interactable = en;
                });
                y -= rowH;
            }

            Header("PORTES ET SOINS");
            Row("Réparer les portes", () => { int s = 0, n = 0; foreach (var d in doors) if (zones[d.Zone].Open) { n++; if (d.Hp >= d.MaxHp - 0.5f) s++; } return $"{s}/{n} intactes"; },
                () => { int c = RepairCost; return c > 0 ? ($"{c} [d]", scrap >= c) : ("INTACTES", false); },
                () => { int c = RepairCost; if (c <= 0 || scrap < c) return; scrap -= c; foreach (var d in doors) if (zones[d.Zone].Open) { d.Hp = d.MaxHp; SetDoorLook(d); } Sfx.Material(); RefreshShop(); });
            Row("Trousse de soin", () => $"+40 PV ({Mathf.CeilToInt(hp)}/{Mathf.CeilToInt(maxHp)})",
                () => hp >= maxHp - 0.5f ? ("PLEIN", false) : ("30 [d]", scrap >= 30),
                () => { if (scrap < 30 || hp >= maxHp) return; scrap -= 30; hp = Mathf.Min(maxHp, hp + 40f); Sfx.Heal(); RefreshShop(); });

            Header("ARMES");
            for (int i = 0; i < Guns.Length; i++)
            {
                int gi = i;
                var gun = Guns[i];
                Row(gun.Name, () => gun.Blurb,
                    () => gunIndex == gi ? ("ÉQUIPÉE", false) : gunsOwned[gi] ? ("PRENDRE", true) : ($"{gun.Cost} [d]", scrap >= gun.Cost),
                    () =>
                    {
                        if (!gunsOwned[gi]) { if (scrap < gun.Cost) return; scrap -= gun.Cost; gunsOwned[gi] = true; Sfx.Coin(); }
                        gunIndex = gi;
                        RefreshGun();
                        RefreshShop();
                    });
            }

            Header("GADGETS");
            for (int i = 0; i < 3; i++)
            {
                int gi = i;
                Row(GadgetNames[i], () => GadgetBlurbs[gi] + (gadget[gi] > 0 ? $"  ·  niveau {gadget[gi]}/3" : ""),
                    () => gadget[gi] >= 3 ? ("MAX", false) : ($"{GadgetCosts[gi][gadget[gi]]} [d]", scrap >= GadgetCosts[gi][gadget[gi]]),
                    () => BuyGadget(gi));
            }

            Header("LA STATION");
            for (int z = 1; z < 4; z++)
            {
                int zi = z;
                Row(NewZones()[z].Name, () => zones != null ? zones[zi].Blurb + (zones[zi].Open ? "" : "  ·  plus de portes à tenir") : "",
                    () => zones == null ? ("", false) : zones[zi].Open ? ("OUVERTE", false) : ($"{zones[zi].Cost} [d]", scrap >= zones[zi].Cost),
                    () => { if (zones[zi].Open || scrap < zones[zi].Cost) return; scrap -= zones[zi].Cost; OpenZone(zi); RefreshShop(); });
            }

            nextWaveButton = UiKit.CreateButton("Next", rt, "", new Vector2(0.15f, 0.025f), new Vector2(0.85f, 0.095f), () => { HideScreens(); NextWave(); }, 38);
            nextWaveLabel = nextWaveButton.GetComponentInChildren<Text>();
            shopUi.SetActive(false);
        }

        int RepairCost
        {
            get
            {
                float missing = 0f;
                foreach (var d in doors) if (zones[d.Zone].Open) missing += d.MaxHp - Mathf.Max(0f, d.Hp);
                return missing < 0.5f ? 0 : Mathf.Max(1, Mathf.CeilToInt(missing * 0.2f));
            }
        }

        void BuyGadget(int g)
        {
            if (gadget[g] >= 3) return;
            int cost = GadgetCosts[g][gadget[g]];
            if (scrap < cost) return;
            scrap -= cost;
            gadget[g]++;
            switch ((Gadget)g)
            {
                case Gadget.Turret:
                    // a turret at his feet (the first, then a second one at level 3); level 2 makes them hit harder
                    if (gadget[g] == 1 || gadget[g] == 3) PlaceTurret(heroPos + new Vector2(facingLeft ? -0.9f : 0.9f, 0f));
                    break;
                case Gadget.Shield: shield = ShieldCap; break;
            }
            Sfx.Coin();
            RefreshShop();
        }

        void ShowShop()
        {
            HideScreens();
            phase = Phase.Shop;
            shopTitle.text = $"AVANT LA VAGUE {wave + 1}";
            bool bossNext = (wave + 1) % 5 == 0;
            var ev = EventFor(wave + 1);
            nextWaveLabel.text = ev != NightEvent.None ? $"VAGUE {wave + 1} : {EventTitle(ev)}" : bossNext ? $"VAGUE {wave + 1} : BOSS" : $"VAGUE {wave + 1}";
            UiKit.FitLabel(nextWaveLabel, 38);
            RefreshShop();
            shopUi.SetActive(true);
        }

        void RefreshShop()
        {
            shopScrap.text = $"{scrap} [d] ferraille";
            foreach (var r in shopRefresh) r();
        }

        // ---- end of the night and the leaderboard ---------------------------------------------------------

        void BuildOver()
        {
            var rt = MakeScreen("Over", true);
            overUi = rt.gameObject;
            overTitle = UiKit.Outlined(UiKit.CreateText("Title", rt, "LA NUIT T'A EU", 70, TextAnchor.MiddleCenter, new Vector2(0.05f, 0.52f), new Vector2(0.95f, 0.61f), ApogeeTheme.Gold), 3f);
            UiKit.FitLabel(overTitle, 70);
            overStats = UiKit.CreateText("Stats", rt, "", 30, TextAnchor.UpperCenter, new Vector2(0.08f, 0.38f), new Vector2(0.92f, 0.51f), ApogeeTheme.Cream);
            overRank = UiKit.Outlined(UiKit.CreateText("Rank", rt, "", 32, TextAnchor.MiddleCenter, new Vector2(0.08f, 0.33f), new Vector2(0.92f, 0.38f), ApogeeTheme.Gold), 1.5f);
            overCoins = IconText.Create("Coins", rt, "", 32, TextAnchor.MiddleCenter, new Vector2(0.08f, 0.285f), new Vector2(0.92f, 0.33f), ApogeeTheme.Cream, 1.2f);
            UiKit.CreateButton("Again", rt, "REJOUER", new Vector2(0.18f, 0.18f), new Vector2(0.82f, 0.26f), StartNight, 42);
            UiKit.CreateButton("Board", rt, "CLASSEMENT", new Vector2(0.24f, 0.1f), new Vector2(0.76f, 0.16f), ShowBoard, 28, new Color(0.22f, 0.10f, 0.08f));
            UiKit.CreateButton("Menu", rt, "MENU", new Vector2(0.32f, 0.03f), new Vector2(0.68f, 0.085f), ShowMenu, 26, new Color(0.22f, 0.10f, 0.08f));
            overUi.SetActive(false);
        }

        void ShowOver(BoardEntry e, int rank, int coins)
        {
            HideScreens();
            overStats.text = $"Vague {e.wave} tenue\n{e.kills} morts abattus  ·  {bosses} boss\nScore : {e.score}";
            overRank.text = rank == 1 ? "NOUVEAU RECORD !" : rank > 0 ? $"{rank}e au classement" : "Hors du classement";
            overCoins.text = coins > 0 ? $"+{coins} [c]" : "";
            overUi.SetActive(true);
        }

        void BuildBoard()
        {
            var rt = MakeScreen("Board", false);
            boardUi = rt.gameObject;
            UiKit.Outlined(UiKit.CreateText("Title", rt, "CLASSEMENT", 60, TextAnchor.MiddleCenter, new Vector2(0.05f, 0.84f), new Vector2(0.95f, 0.92f), ApogeeTheme.Gold), 3f);
            UiKit.CreateText("Head", rt, "RANG       SCORE        VAGUE      MORTS       DATE", 22, TextAnchor.MiddleCenter, new Vector2(0.05f, 0.8f), new Vector2(0.95f, 0.835f), UiKit.TextDim);
            for (int i = 0; i < 10; i++)
            {
                float y1 = 0.79f - i * 0.055f;
                var row = UiKit.CreateText($"Row_{i}", rt, "", 30, TextAnchor.MiddleCenter, new Vector2(0.05f, y1 - 0.05f), new Vector2(0.95f, y1), ApogeeTheme.Cream);
                boardRows.Add(UiKit.Outlined(row, 1.2f));
            }
            UiKit.CreateButton("Back", rt, "RETOUR", new Vector2(0.3f, 0.06f), new Vector2(0.7f, 0.13f), () => { if (phase == Phase.Over) { boardUi.SetActive(false); overUi.SetActive(true); } else ShowMenu(); }, 30);
            boardUi.SetActive(false);
        }

        void ShowBoard()
        {
            menuUi.SetActive(false);
            overUi.SetActive(false);
            var b = LoadBoard();
            for (int i = 0; i < boardRows.Count; i++)
            {
                if (i >= b.entries.Count) { boardRows[i].text = i == 0 ? "Aucune nuit encore. À toi de jouer !" : ""; continue; }
                var e = b.entries[i];
                boardRows[i].text = $"{i + 1,2}.        {e.score,6}         {e.wave,3}          {e.kills,4}         {e.date}";
                boardRows[i].color = i == 0 ? ApogeeTheme.Gold : ApogeeTheme.Cream;
            }
            boardUi.SetActive(true);
        }
    }

    /// <summary>
    /// The two-way joystick of DERNIÈRE NUIT: the ring appears where the thumb lands and the
    /// knob follows it; Move is the direction (0..1 strength), shared with the game.
    /// </summary>
    public class NuitStick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public static Vector2 Move;
        public RectTransform ring, knob;
        const float Travel = 120f;
        static NuitStick current;
        RectTransform rect;
        int pointer = int.MinValue;
        Vector2 origin;

        void Awake()
        {
            rect = (RectTransform)transform;
            Show(false);
        }

        void Show(bool on)
        {
            if (ring != null) ring.gameObject.SetActive(on);
            if (knob != null) knob.gameObject.SetActive(on);
        }

        public static void Release()
        {
            Move = Vector2.zero;
            if (current != null) { current.pointer = int.MinValue; current.Show(false); }
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (pointer != int.MinValue) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, e.position, e.pressEventCamera, out origin)) return;
            pointer = e.pointerId;
            current = this;
            ring.localPosition = origin;
            knob.localPosition = origin;
            Show(true);
            Move = Vector2.zero;
        }

        public void OnDrag(PointerEventData e)
        {
            if (e.pointerId != pointer) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, e.position, e.pressEventCamera, out var p)) return;
            var d = Vector2.ClampMagnitude(p - origin, Travel);
            knob.localPosition = origin + d;
            var v = d / Travel;
            Move = v.magnitude < 0.12f ? Vector2.zero : v;
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (e.pointerId != pointer) return;
            pointer = int.MinValue;
            Move = Vector2.zero;
            Show(false);
        }

        void OnDisable()
        {
            pointer = int.MinValue;
            Move = Vector2.zero;
            Show(false);
        }
    }
}
