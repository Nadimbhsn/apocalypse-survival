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
        Text menuRecord, overTitle, overStats, overRank, playLabel;
        Button playButton, newRunButton;
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

            // two fixed joysticks: move on the left, aim and fire on the right
            CreatePad(g, "MovePad", new Vector2(0.21f, 0.135f), false);
            CreateFireButton(g, new Vector2(0.79f, 0.135f));

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

            // the dodge button, above the aiming stick
            var dr = UiKit.CreateRect("Dodge", g, new Vector2(0.86f, 0.3f), new Vector2(0.86f, 0.3f));
            dr.sizeDelta = new Vector2(170f, 170f);
            dodgeButton = dr.gameObject;
            var dimg = dr.gameObject.AddComponent<Image>();
            dimg.sprite = NuitUiSprite("btn_dodge") ?? PlaceholderVisuals.Circle(new Color(0.45f, 0.75f, 1f));
            dimg.preserveAspect = true;
            var hold = dr.gameObject.AddComponent<HoldButton>();
            hold.onDown = TryDodge;
            var df = UiKit.CreateImage("Cooldown", dr, Vector2.zero, Vector2.one, PlaceholderVisuals.Circle(Color.white), new Color(0f, 0f, 0f, 0.6f), true);
            dodgeFill = df;
            dodgeFill.type = Image.Type.Filled;
            dodgeFill.fillMethod = Image.FillMethod.Radial360;
            dodgeFill.raycastTarget = false;
            var dl = UiKit.Outlined(UiKit.CreateText("Label", dr, "ESQUIVE", 20, TextAnchor.UpperCenter, new Vector2(-0.3f, -0.32f), new Vector2(1.3f, 0f), ApogeeTheme.Cream), 1.5f);
            dl.raycastTarget = false;

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

        static readonly Dictionary<string, Sprite> uiSprites = new();

        static Sprite NuitUiSprite(string name)
        {
            if (uiSprites.TryGetValue(name, out var s)) return s;
            var tex = Resources.Load<Texture2D>("Nuit/" + name);
            s = tex != null ? Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f) : null;
            uiSprites[name] = s;
            return s;
        }

        /// <summary>A fixed joystick: a gold-rimmed dial with a brass knob; the touch area around it is generous.</summary>
        void CreatePad(RectTransform parent, string name, Vector2 centre, bool aim)
        {
            var area = UiKit.CreateRect(name, parent, centre, centre);
            area.sizeDelta = new Vector2(480f, 480f);
            area.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0f);
            var pad = area.gameObject.AddComponent<NuitPad>();
            pad.aim = aim;
            var dial = UiKit.CreateImage("Dial", area, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                NuitUiSprite(aim ? "stick_base_aim" : "stick_base_move") ?? PlaceholderVisuals.RimCircle(Color.white), Color.white, true);
            dial.rectTransform.sizeDelta = new Vector2(330f, 330f);
            dial.raycastTarget = false;
            var knob = UiKit.CreateImage("Knob", area, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                NuitUiSprite(aim ? "stick_knob_aim" : "stick_knob_move") ?? PlaceholderVisuals.Circle(Color.white), Color.white, true);
            knob.rectTransform.sizeDelta = new Vector2(190f, 190f);
            knob.raycastTarget = false;
            pad.knob = knob.rectTransform;
            pad.dial = dial;
        }

        /// <summary>The big TIR button: a gold-rimmed dial with the brass gunsight knob, held to fire.</summary>
        void CreateFireButton(RectTransform parent, Vector2 centre)
        {
            var area = UiKit.CreateRect("Fire", parent, centre, centre);
            area.sizeDelta = new Vector2(400f, 400f);
            area.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0f);
            var dial = UiKit.CreateImage("Dial", area, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                NuitUiSprite("stick_base_aim") ?? PlaceholderVisuals.RimCircle(Color.white), Color.white, true);
            dial.rectTransform.sizeDelta = new Vector2(320f, 320f);
            dial.raycastTarget = false;
            var knob = UiKit.CreateImage("Knob", area, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                NuitUiSprite("stick_knob_aim") ?? PlaceholderVisuals.Circle(new Color(0.8f, 0.3f, 0.2f)), Color.white, true);
            knob.rectTransform.sizeDelta = new Vector2(240f, 240f);
            knob.raycastTarget = false;
            var label = UiKit.Outlined(UiKit.CreateText("Label", area, "TIR", 30, TextAnchor.MiddleCenter, new Vector2(0.3f, 0.02f), new Vector2(0.7f, 0.16f), ApogeeTheme.Cream), 2f);
            label.raycastTarget = false;
            var hold = area.gameObject.AddComponent<HoldButton>();
            hold.onDown = () => { NuitPad.FireHeld = true; knob.rectTransform.localScale = Vector3.one * 0.9f; dial.color = new Color(1f, 0.9f, 0.75f); };
            hold.onUp = () => { NuitPad.FireHeld = false; knob.rectTransform.localScale = Vector3.one; dial.color = Color.white; };
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
            NuitPad.Release();
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
            playButton = UiKit.CreateButton("Play", rt, "JOUER", new Vector2(0.15f, 0.245f), new Vector2(0.85f, 0.325f), () => { if (LoadSave() != null) ResumeNight(); else StartNight(); }, 42);
            playLabel = playButton.GetComponentInChildren<Text>();
            newRunButton = UiKit.CreateButton("NewRun", rt, "NOUVELLE PARTIE", new Vector2(0.26f, 0.185f), new Vector2(0.74f, 0.232f), NewNight, 24, new Color(0.35f, 0.14f, 0.1f));
            UiKit.CreateButton("Board", rt, "CLASSEMENT", new Vector2(0.26f, 0.12f), new Vector2(0.74f, 0.172f), ShowBoard, 26, new Color(0.22f, 0.10f, 0.08f));
            UiKit.CreateButton("Back", rt, "RETOUR", new Vector2(0.32f, 0.04f), new Vector2(0.68f, 0.1f), ReturnToHub, 26, new Color(0.22f, 0.10f, 0.08f));
            menuUi.SetActive(false);
        }

        void ShowMenu()
        {
            phase = Phase.Menu;
            HideScreens();
            if (hero != null) hero.gameObject.SetActive(false);
            var b = LoadBoard();
            menuRecord.text = b.entries.Count > 0 ? $"Record : {b.entries[0].score} pts  ·  vague {b.entries[0].wave}" : "Aucune nuit au classement";
            var saved = LoadSave();
            playLabel.text = saved != null ? $"REPRENDRE  ·  VAGUE {saved.wave + 1}" : "JOUER";
            UiKit.FitLabel(playLabel, 42);
            newRunButton.gameObject.SetActive(saved != null);
            menuUi.SetActive(true);
        }

        // ---- bonus ------------------------------------------------------------------------------------

        void BuildBonus()
        {
            var rt = MakeScreen("Bonus", false);
            bonusUi = rt.gameObject;
            UiKit.Outlined(UiKit.CreateText("Title", rt, "VAGUE TENUE !", 60, TextAnchor.MiddleCenter, new Vector2(0.05f, 0.8f), new Vector2(0.95f, 0.88f), ApogeeTheme.Gold), 3f);
            UiKit.CreateText("Hint", rt, "Choisis un bonus pour la suite de la nuit", 28, TextAnchor.MiddleCenter, new Vector2(0.05f, 0.75f), new Vector2(0.95f, 0.8f), ApogeeTheme.Cream);
            QuitButton(rt);
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

        // The shop is four tabs of clear cards: what it is, what it does, where it stands,
        // and one big price button that says plainly whether it can be paid.
        enum ShopTab { Defense, Guns, Gadgets, Station }
        ShopTab shopTab;
        RectTransform shopContent;
        Image shopHpFill;
        Text shopHpText, shopToast, shopNext;
        float shopToastTime;
        readonly List<(Image bg, Text label, GameObject badge)> shopTabs = new();

        static readonly string[] DoorNames =
        {
            "Porte sud-ouest", "Porte sud-est", "Porte arrière de la boutique", "Porte du garage",
            "Grand portail du garage", "Porte sud du parking", "Porte ouest du parking",
        };

        static readonly string[][] GadgetLevels =
        {
            new[] { "Une tourelle posée là où tu te tiens", "Les tourelles tirent plus fort et plus vite", "Une deuxième tourelle à tes pieds" },
            new[] { "Un bouclier de 25 qui encaisse avant ta vie", "Bouclier de 45", "Bouclier de 70" },
            new[] { "Le bouton ESQUIVE : un bond intouchable (3 s)", "Esquive toutes les 2,2 s", "Esquive toutes les 1,5 s" },
        };

        void BuildShop()
        {
            var rt = MakeScreen("Shop", false);
            shopUi = rt.gameObject;
            ((Image)rt.GetComponent<Image>()).color = new Color(0.03f, 0.03f, 0.08f, 0.92f);

            shopTitle = UiKit.Outlined(UiKit.CreateText("Title", rt, "ENTRE DEUX VAGUES", 48, TextAnchor.MiddleCenter, new Vector2(0.04f, 0.94f), new Vector2(0.96f, 0.985f), ApogeeTheme.Gold), 2.5f);
            shopNext = UiKit.CreateText("Next", rt, "", 24, TextAnchor.MiddleCenter, new Vector2(0.04f, 0.912f), new Vector2(0.96f, 0.942f), ApogeeTheme.Cream);
            QuitButton(rt);
            UiKit.FitLabel(shopNext, 24);

            // what you have: health and scrap, big
            Bar(rt, "Hp", new Vector2(0.04f, 0.862f), new Vector2(0.5f, 0.902f), new Color(0.85f, 0.24f, 0.2f), out shopHpFill);
            shopHpText = UiKit.Outlined(UiKit.CreateText("HpText", shopHpFill.rectTransform.parent, "", 22, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, ApogeeTheme.Cream), 1.2f);
            shopScrap = Pill(rt, "Scrap", new Vector2(0.53f, 0.858f), new Vector2(0.96f, 0.906f), 32);

            // the four tabs
            var tabs = new (ShopTab tab, string label, Sprite icon)[]
            {
                (ShopTab.Defense, "DÉFENSE", Art("door_h_0", 0f)),
                (ShopTab.Guns, "ARMES", WeaponCatalog.SpriteFor(WeaponCatalog.Find("pistol"))),
                (ShopTab.Gadgets, "GADGETS", Art("turret", 0.1f)),
                (ShopTab.Station, "STATION", Art("crate_loot", 0.08f)),
            };
            for (int i = 0; i < tabs.Length; i++)
            {
                var t = tabs[i];
                float x0 = 0.03f + i * 0.2375f;
                var tr = UiKit.CreateRect("Tab_" + t.label, rt, new Vector2(x0, 0.775f), new Vector2(x0 + 0.225f, 0.848f));
                var bg = tr.gameObject.AddComponent<Image>();
                bg.sprite = HubArt.Get("ui_tile", 32f) ?? ApogeeTheme.Panel;
                bg.type = Image.Type.Sliced;
                var btn = tr.gameObject.AddComponent<Button>();
                btn.targetGraphic = bg;
                var captured = t.tab;
                btn.onClick.AddListener(() => { shopTab = captured; Sfx.Drop(); RefreshShop(); });
                tr.gameObject.AddComponent<ButtonPop>();
                var icon = UiKit.CreateImage("Icon", tr, new Vector2(0.2f, 0.36f), new Vector2(0.8f, 0.94f), t.icon, Color.white, true);
                icon.raycastTarget = false;
                var label = UiKit.Outlined(UiKit.CreateText("Label", tr, t.label, 22, TextAnchor.MiddleCenter, new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.36f), ApogeeTheme.Cream), 1.2f);
                UiKit.FitLabel(label, 22);
                var badge = UiKit.CreateImage("Badge", tr, new Vector2(0.82f, 0.74f), new Vector2(0.98f, 0.98f), PlaceholderVisuals.Circle(Color.white), new Color(0.95f, 0.22f, 0.18f), true);
                badge.raycastTarget = false;
                shopTabs.Add((bg, label, badge.gameObject));
            }

            // the cards of the open tab
            shopContent = UiKit.CreateRect("Content", rt, new Vector2(0.03f, 0.15f), new Vector2(0.97f, 0.765f));

            shopToast = UiKit.Outlined(UiKit.CreateText("Toast", rt, "", 26, TextAnchor.MiddleCenter, new Vector2(0.04f, 0.105f), new Vector2(0.96f, 0.145f), ApogeeTheme.Gold), 1.5f);
            UiKit.FitLabel(shopToast, 26);
            nextWaveButton = UiKit.CreateButton("Launch", rt, "", new Vector2(0.12f, 0.02f), new Vector2(0.88f, 0.098f), () => { HideScreens(); NextWave(); }, 40);
            nextWaveLabel = nextWaveButton.GetComponentInChildren<Text>();
            shopUi.SetActive(false);
        }

        /// <summary>Leave between waves: the night is kept exactly as it is.</summary>
        void QuitButton(RectTransform rt)
        {
            var b = UiKit.CreateButton("Quit", rt, "QUITTER", new Vector2(0.02f, 0.945f), new Vector2(0.2f, 0.985f), QuitNight, 20, new Color(0.22f, 0.10f, 0.08f));
            b.transform.SetAsLastSibling();
        }

        void Toast(string text)
        {
            shopToast.text = text;
            shopToastTime = 2.5f;
        }

        /// <summary>One card: picture, name, what it does, its state, and the button that buys it.</summary>
        Button ShopCard(float y0, float y1, Sprite icon, string title, string text, string state, string price, bool canPay, int missing, UnityEngine.Events.UnityAction onClick, Color? tint = null)
        {
            var card = UiKit.CreateRect("Card_" + title, shopContent, new Vector2(0f, y0), new Vector2(1f, y1));
            var bg = card.gameObject.AddComponent<Image>();
            bg.sprite = HubArt.Get("ui_tile", 32f) ?? ApogeeTheme.Panel;
            bg.type = Image.Type.Sliced;
            bg.color = tint ?? new Color(0.85f, 0.8f, 0.85f, 0.95f);
            bg.raycastTarget = false;
            if (icon != null)
            {
                var ic = UiKit.CreateImage("Icon", card, new Vector2(0.025f, 0.12f), new Vector2(0.21f, 0.88f), icon, Color.white, true);
                ic.raycastTarget = false;
            }
            var name = UiKit.Outlined(UiKit.CreateText("Name", card, title, 32, TextAnchor.LowerLeft, new Vector2(0.23f, 0.55f), new Vector2(0.66f, 0.92f), ApogeeTheme.Gold), 1.6f);
            UiKit.FitLabel(name, 32);
            var desc = UiKit.CreateText("Text", card, text, 22, TextAnchor.UpperLeft, new Vector2(0.23f, 0.08f), new Vector2(0.66f, 0.55f), ApogeeTheme.Cream);
            UiKit.FitLabel(desc, 22);
            Button btn = null;
            if (price != null)
            {
                btn = UiKit.CreateButton("Buy", card, "", new Vector2(0.68f, 0.3f), new Vector2(0.97f, 0.86f), onClick, 26);
                var lbl = IconText.OnButton(btn, 26);
                lbl.text = price;
                btn.interactable = canPay;
                btn.gameObject.AddComponent<ButtonPop>();
            }
            if (missing > 0)
            {
                // what is lacking, in red, with the scrap icon
                IconText.Create("Missing", card, $"il manque {missing} [d]", 20, TextAnchor.MiddleCenter, new Vector2(0.66f, 0.04f), new Vector2(0.99f, 0.3f), new Color(1f, 0.55f, 0.45f), 1f);
            }
            else if (state != null)
            {
                bool alone = price == null;
                var st = UiKit.Outlined(UiKit.CreateText("State", card, state, alone ? 28 : 20, TextAnchor.MiddleCenter,
                    alone ? new Vector2(0.66f, 0.2f) : new Vector2(0.66f, 0.04f), alone ? new Vector2(0.99f, 0.8f) : new Vector2(0.99f, 0.3f),
                    alone ? ApogeeTheme.Gold : ApogeeTheme.Cream), 1.2f);
                UiKit.FitLabel(st, alone ? 28 : 20);
            }
            return btn;
        }

        static void StatBar(RectTransform card, string label, float value, float y)
        {
            UiKit.CreateText("S_" + label, card, label, 16, TextAnchor.MiddleLeft, new Vector2(0.23f, y), new Vector2(0.36f, y + 0.14f), UiKit.TextDim);
            var bg = UiKit.CreateImage("SB_" + label, card, new Vector2(0.36f, y + 0.035f), new Vector2(0.64f, y + 0.105f), PlaceholderVisuals.Square(Color.white), new Color(0f, 0f, 0f, 0.45f), false);
            bg.raycastTarget = false;
            var fill = UiKit.CreateImage("SF_" + label, card, new Vector2(0.36f, y + 0.035f), new Vector2(0.36f + 0.28f * Mathf.Clamp01(value), y + 0.105f), PlaceholderVisuals.Square(Color.white), ApogeeTheme.Gold, false);
            fill.raycastTarget = false;
        }

        void BuildShopTab()
        {
            for (int i = shopContent.childCount - 1; i >= 0; i--) Destroy(shopContent.GetChild(i).gameObject);
            switch (shopTab)
            {
                case ShopTab.Defense: BuildDefenseTab(); break;
                case ShopTab.Guns: BuildGunsTab(); break;
                case ShopTab.Gadgets: BuildGadgetsTab(); break;
                default: BuildStationTab(); break;
            }
        }

        void BuildDefenseTab()
        {
            int cost = RepairCost;
            int open = 0, intact = 0;
            foreach (var d in doors) if (zones[d.Zone].Open) { open++; if (d.Hp >= d.MaxHp - 0.5f) intact++; }
            ShopCard(0.6f, 1f, Art("door_h_0", 0f), "Réparer les portes",
                cost > 0 ? "Les morts cassent les portes pour entrer. Remets-les toutes à neuf avant la vague." : "Toutes tes portes tiennent. Rien à réparer.",
                cost > 0 ? null : "INTACTES", cost > 0 ? $"TOUT RÉPARER\n{cost} [d]" : null, scrap >= cost, cost > 0 && scrap < cost ? cost - scrap : 0,
                () => { int c = RepairCost; if (c <= 0 || scrap < c) return; scrap -= c; foreach (var d in doors) if (zones[d.Zone].Open) { d.Hp = d.MaxHp; SetDoorLook(d); } Sfx.Material(); Toast("Portes réparées !"); RefreshShop(); });
            // each door's state
            var list = UiKit.CreateRect("Doors", shopContent, new Vector2(0f, 0.34f), new Vector2(1f, 0.585f));
            int row = 0;
            for (int i = 0; i < doors.Count; i++)
            {
                var d = doors[i];
                if (!zones[d.Zone].Open) continue;
                int col = row % 2, line = row / 2;
                float x0 = col * 0.5f, y1 = 1f - line * 0.25f;
                var t = UiKit.CreateText("D" + i, list, DoorNames[i], 18, TextAnchor.MiddleLeft, new Vector2(x0 + 0.02f, y1 - 0.24f), new Vector2(x0 + 0.3f, y1), ApogeeTheme.Cream);
                UiKit.FitLabel(t, 18);
                float k = Mathf.Clamp01(d.Hp / d.MaxHp);
                var bg = UiKit.CreateImage("DB" + i, list, new Vector2(x0 + 0.31f, y1 - 0.17f), new Vector2(x0 + 0.48f, y1 - 0.07f), PlaceholderVisuals.Square(Color.white), new Color(0f, 0f, 0f, 0.5f), false);
                bg.raycastTarget = false;
                var fill = UiKit.CreateImage("DF" + i, list, new Vector2(x0 + 0.31f, y1 - 0.17f), new Vector2(x0 + 0.31f + 0.17f * k, y1 - 0.07f), PlaceholderVisuals.Square(Color.white),
                    k > 0.66f ? new Color(0.45f, 0.85f, 0.4f) : k > 0.01f ? new Color(1f, 0.65f, 0.25f) : new Color(0.9f, 0.2f, 0.18f), false);
                fill.raycastTarget = false;
                row++;
            }
            bool full = hp >= maxHp - 0.5f;
            ShopCard(0f, 0.32f, GameIcons.Kit, "Trousse de soin", $"Rend 40 points de vie. Tu as {Mathf.CeilToInt(hp)} sur {Mathf.CeilToInt(maxHp)}.",
                full ? "VIE PLEINE" : null, full ? null : "+40 PV\n30 [d]", scrap >= 30, !full && scrap < 30 ? 30 - scrap : 0,
                () => { if (scrap < 30 || hp >= maxHp) return; scrap -= 30; hp = Mathf.Min(maxHp, hp + 40f); Sfx.Heal(); Toast("+40 PV"); RefreshShop(); });
        }

        void BuildGunsTab()
        {
            float h = 1f / Guns.Length;
            for (int i = 0; i < Guns.Length; i++)
            {
                int gi = i;
                var g = Guns[i];
                float y1 = 1f - i * h, y0 = y1 - h + 0.012f;
                bool owned = gunsOwned[i], equipped = gunIndex == i;
                string price = equipped ? null : owned ? "PRENDRE" : $"{g.Cost} [d]";
                var btn = ShopCard(y0, y1, WeaponCatalog.SpriteFor(ArtOf(g)), g.Name, "",
                    equipped ? "EN MAIN" : owned ? "à toi" : null, price, owned || scrap >= g.Cost, !owned && scrap < g.Cost ? g.Cost - scrap : 0,
                    () =>
                    {
                        if (!gunsOwned[gi]) { if (scrap < g.Cost) return; scrap -= g.Cost; gunsOwned[gi] = true; Sfx.Coin(); Toast($"{g.Name} acheté et pris en main !"); }
                        else Toast($"{g.Name} en main");
                        gunIndex = gi;
                        RefreshGun();
                        RefreshShop();
                    }, equipped ? new Color(1f, 0.85f, 0.55f, 0.95f) : (Color?)null);
                var card = (RectTransform)shopContent.GetChild(shopContent.childCount - 1);
                // compare at a glance
                StatBar(card, "DÉGÂTS", g.Damage * (g.Splash > 0f ? 1.3f : 1f) / 45f, 0.38f);
                StatBar(card, "CADENCE", g.Rate / 9f, 0.22f);
                StatBar(card, "PORTÉE", g.Range / 8.5f, 0.06f);
                var name = card.Find("Name") as RectTransform;
                if (name != null) { name.anchorMin = new Vector2(0.23f, 0.56f); name.anchorMax = new Vector2(0.66f, 0.96f); }
            }
        }

        void BuildGadgetsTab()
        {
            var icons = new[] { Art("turret", 0.1f), PlaceholderVisuals.RimCircle(new Color(0.55f, 0.85f, 1f)), NuitUiSprite("btn_dodge") };
            float h = 1f / 3f;
            for (int i = 0; i < 3; i++)
            {
                int gi = i;
                int lv = gadget[i];
                float y1 = 1f - i * h, y0 = y1 - h + 0.015f;
                string text = lv >= 3 ? "Au maximum. " + GadgetLevels[i][2] : (lv == 0 ? "" : "Niveau suivant : ") + GadgetLevels[i][lv];
                int cost = lv < 3 ? GadgetCosts[i][lv] : 0;
                ShopCard(y0, y1, icons[i], GadgetNames[i], text, lv >= 3 ? "MAX" : null, lv >= 3 ? null : (lv == 0 ? $"ACHETER\n{cost} [d]" : $"NIVEAU {lv + 1}\n{cost} [d]"),
                    scrap >= cost, lv < 3 && scrap < cost ? cost - scrap : 0,
                    () =>
                    {
                        int before = gadget[gi];
                        BuyGadget(gi);
                        if (gadget[gi] > before) Toast($"{GadgetNames[gi]} niveau {gadget[gi]} !" + (gi == 0 && (gadget[gi] == 1 || gadget[gi] == 3) ? " Elle est posée à tes pieds." : ""));
                    });
                var card = (RectTransform)shopContent.GetChild(shopContent.childCount - 1);
                for (int k = 0; k < 3; k++)
                {
                    var pip = UiKit.CreateImage("Pip" + k, card, new Vector2(0.23f + k * 0.045f, 0.62f), new Vector2(0.26f + k * 0.045f, 0.62f + 0.17f), PlaceholderVisuals.Circle(Color.white),
                        k < lv ? ApogeeTheme.Gold : new Color(0f, 0f, 0f, 0.45f), true);
                    pip.raycastTarget = false;
                }
                var name = card.Find("Name") as RectTransform;
                if (name != null) { name.anchorMin = new Vector2(0.38f, 0.55f); }
            }
        }

        void BuildStationTab()
        {
            // a plan of the station: rooms open or closed, the doors and their state, where you stand
            var map = UiKit.CreateRect("Map", shopContent, new Vector2(0.02f, 0.47f), new Vector2(0.98f, 1f));
            var mbg = map.gameObject.AddComponent<Image>();
            mbg.color = new Color(0.06f, 0.07f, 0.12f, 0.95f);
            mbg.raycastTarget = false;
            var area = new Rect(-20.5f, -9.5f, 39f, 26f);
            Vector2 A(Vector2 p) => new Vector2((p.x - area.xMin) / area.width, (p.y - area.yMin) / area.height);
            for (int z = 0; z < zones.Length; z++)
            {
                var zr = zones[z].Area;
                var img = UiKit.CreateImage("Z" + z, map, A(zr.min), A(zr.max), PlaceholderVisuals.Square(Color.white),
                    zones[z].Open ? new Color(0.62f, 0.44f, 0.26f, 0.95f) : new Color(0.22f, 0.22f, 0.28f, 0.95f), false);
                img.raycastTarget = false;
                var o = img.gameObject.AddComponent<Outline>();
                o.effectColor = new Color(0f, 0f, 0f, 0.8f);
                o.effectDistance = new Vector2(3f, -3f);
                var lbl = UiKit.Outlined(UiKit.CreateText("L", img.rectTransform, zones[z].Name.ToUpperInvariant() + (zones[z].Open ? "" : "\nfermé"), 20, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one,
                    zones[z].Open ? ApogeeTheme.Cream : new Color(0.75f, 0.75f, 0.8f)), 1.2f);
                UiKit.FitLabel(lbl, 20);
            }
            for (int i = 0; i < doors.Count; i++)
            {
                var d = doors[i];
                bool on = zones[d.Zone].Open;
                var half = d.Horizontal ? new Vector2(1.3f, 0.45f) : new Vector2(0.45f, 1.3f);
                float k = d.Hp / d.MaxHp;
                var col = !on ? new Color(0.4f, 0.4f, 0.45f) : k > 0.66f ? new Color(0.45f, 0.9f, 0.4f) : k > 0.01f ? new Color(1f, 0.65f, 0.25f) : new Color(1f, 0.2f, 0.18f);
                var di = UiKit.CreateImage("D" + i, map, A(d.Pos - half), A(d.Pos + half), PlaceholderVisuals.Square(Color.white), col, false);
                di.raycastTarget = false;
            }
            foreach (var t in turrets)
            {
                var ti = UiKit.CreateImage("T", map, A(t.Pos - Vector2.one * 0.5f), A(t.Pos + Vector2.one * 0.5f), PlaceholderVisuals.Circle(Color.white), new Color(0.5f, 0.9f, 1f), false);
                ti.raycastTarget = false;
            }
            var me = UiKit.CreateImage("Me", map, A(heroPos - Vector2.one * 0.6f), A(heroPos + Vector2.one * 0.6f), PlaceholderVisuals.Circle(Color.white), Color.white, false);
            me.raycastTarget = false;
            UiKit.CreateText("Legend", map, "portes :  vert = solide   orange = abîmée   rouge = cassée     blanc = toi", 16, TextAnchor.LowerCenter, new Vector2(0f, 0f), new Vector2(1f, 0.08f), UiKit.TextDim);

            float h = 0.45f / 3f;
            for (int z = 1; z < 4; z++)
            {
                int zi = z;
                var zone = zones[z];
                int extra = 0;
                foreach (var d in doors) if (d.Zone == z) extra++;
                float y1 = 0.455f - (z - 1) * h, y0 = y1 - h + 0.01f;
                string text = zone.Open ? zone.Blurb : $"{zone.Blurb}. Une caisse : +{25 + z * 15} ferraille et +25 PV. Attention : {extra} porte{(extra > 1 ? "s" : "")} de plus à tenir.";
                ShopCard(y0, y1, Art(zone.Open ? "crate" : "crate_loot", 0.08f), zone.Name, text, zone.Open ? "OUVERTE" : null, zone.Open ? null : $"OUVRIR\n{zone.Cost} [d]",
                    scrap >= zone.Cost, !zone.Open && scrap < zone.Cost ? zone.Cost - scrap : 0,
                    () => { if (zones[zi].Open || scrap < zones[zi].Cost) return; scrap -= zones[zi].Cost; OpenZone(zi); Toast($"{zones[zi].Name} ouverte ! Ses portes sont barricadées."); RefreshShop(); });
            }
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
            bool bossNext = (wave + 1) % 5 == 0;
            var ev = EventFor(wave + 1);
            shopNext.text = ev != NightEvent.None ? $"Prochaine vague : {EventTitle(ev)} - {EventText(ev)}" : bossNext ? "Prochaine vague : un BOSS arrive" : $"Prochaine vague : {wave + 1}";
            nextWaveLabel.text = bossNext ? $"VAGUE {wave + 1} : BOSS" : $"LANCER LA VAGUE {wave + 1}";
            UiKit.FitLabel(nextWaveLabel, 40);
            // open where something needs doing
            shopTab = RepairCost > 0 || hp < maxHp * 0.5f ? ShopTab.Defense : ShopTab.Guns;
            shopToast.text = "";
            RefreshShop();
            shopUi.SetActive(true);
        }

        void RefreshShop()
        {
            shopScrap.text = $"{scrap} [d]  ferraille";
            shopHpFill.fillAmount = maxHp > 0f ? hp / maxHp : 0f;
            shopHpText.text = $"Vie {Mathf.CeilToInt(hp)} / {Mathf.CeilToInt(maxHp)}" + (shield > 0.5f ? $"  +{Mathf.CeilToInt(shield)} bouclier" : "");
            bool doorsHurt = RepairCost > 0, low = hp < maxHp * 0.5f;
            for (int i = 0; i < shopTabs.Count; i++)
            {
                bool on = (int)shopTab == i;
                shopTabs[i].bg.color = on ? ApogeeTheme.Gold : new Color(0.55f, 0.5f, 0.6f, 0.9f);
                shopTabs[i].label.color = on ? new Color(0.25f, 0.08f, 0.03f) : ApogeeTheme.Cream;
                shopTabs[i].badge.SetActive(i == 0 && (doorsHurt || low));
            }
            BuildShopTab();
        }

        void UpdateShopToast()
        {
            if (shopToastTime <= 0f || shopToast == null) return;
            shopToastTime -= Time.unscaledDeltaTime;
            var c = shopToast.color; c.a = Mathf.Clamp01(shopToastTime / 0.5f); shopToast.color = c;
            if (shopToastTime <= 0f) { shopToast.text = ""; c.a = 1f; shopToast.color = c; }
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
    /// A fixed joystick of DERNIÈRE NUIT. The left one moves the hero, the right one aims and
    /// fires while it is held. Each follows its own finger (several touches at once); the
    /// knob slides within the dial and springs back to the middle when let go.
    /// </summary>
    public class NuitPad : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public static Vector2 Move, Aim;
        /// <summary>The TIR button is held.</summary>
        public static bool FireHeld;
        public bool aim;
        public RectTransform knob;
        public Image dial;
        const float Travel = 115f;
        static readonly List<NuitPad> all = new();
        RectTransform rect;
        int pointer = int.MinValue;

        void Awake() { rect = (RectTransform)transform; all.Add(this); }
        void OnDestroy() => all.Remove(this);

        public static void Release()
        {
            Move = Aim = Vector2.zero;
            FireHeld = false;
            foreach (var p in all) p.Recentre();
        }

        void Recentre()
        {
            pointer = int.MinValue;
            if (knob != null) knob.anchoredPosition = Vector2.zero;
            if (dial != null) dial.color = Color.white;
            if (aim) Aim = Vector2.zero; else Move = Vector2.zero;
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (pointer != int.MinValue) return;
            pointer = e.pointerId;
            if (dial != null) dial.color = new Color(1f, 0.95f, 0.85f);
            Apply(e);
        }

        public void OnDrag(PointerEventData e)
        {
            if (e.pointerId == pointer) Apply(e);
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (e.pointerId == pointer) Recentre();
        }

        void OnDisable() => Recentre();

        void Apply(PointerEventData e)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, e.position, e.pressEventCamera, out var p)) return;
            var d = Vector2.ClampMagnitude(p - rect.rect.center, Travel);
            if (knob != null) knob.anchoredPosition = d;
            var v = d / Travel;
            if (v.magnitude < 0.15f) v = Vector2.zero;
            if (aim) Aim = v; else Move = v;
        }
    }
}
