using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Random = UnityEngine.Random;

namespace Platformer.Survival
{
    /// <summary>
    /// The Barricade's screens around the fight: its own home (the land to defend, the
    /// Éclats, the Forge, the Mill), the Forge's list of permanent upgrades, the choice of a
    /// Blessing every third wave, and the end of a run with what it earned.
    /// </summary>
    public partial class BarricadeGame
    {
        GameObject menuPanel, forgePanel, perkPanel, overPanel;
        Text landName, landSub, landStats, landBest, landLock;
        IconText menuShards, millLabel, forgeShards;
        Button playButton, prevLand, nextLand, millButton;
        RectTransform forgeContent;
        readonly List<(Forge id, Text level, IconText buy, Button button, Text effect)> forgeRows = new();
        readonly (Button button, Text name, Text effect, Text stack)[] perkCards = new (Button, Text, Text, Text)[3];
        readonly Perk[] perkOffer = new Perk[3];
        Text overTitle, overStats;
        IconText overRewards;
        bool forgeFromOver;

        void BuildScreens(RectTransform rt)
        {
            BuildMenu(rt);
            BuildForge(rt);
            BuildPerks(rt);
            BuildOverScreen(rt);
        }

        static Image Glass(RectTransform parent, string name, Vector2 min, Vector2 max, float alpha = 0.96f)
        {
            var r = UiKit.CreateRect(name, parent, min, max);
            var img = r.gameObject.AddComponent<Image>();
            img.sprite = HubArt.Get("ui_tile", 32f) ?? ApogeeTheme.Panel;
            img.type = Image.Type.Sliced;
            img.color = new Color(1f, 1f, 1f, alpha);
            return img;
        }

        static Button GoldButton(RectTransform parent, string name, string label, Vector2 min, Vector2 max, UnityEngine.Events.UnityAction onClick, int size = 40)
        {
            var r = UiKit.CreateRect(name, parent, min, max);
            var img = r.gameObject.AddComponent<Image>();
            img.sprite = HubArt.Get("ui_play", 50f) ?? ApogeeTheme.Button;
            img.type = Image.Type.Sliced;
            var b = r.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            b.onClick.AddListener(onClick);
            r.gameObject.AddComponent<ButtonPop>();
            var t = UiKit.CreateText("Label", r, label, size, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, new Color(0.3f, 0.09f, 0.02f));
            t.fontStyle = FontStyle.Bold;
            UiKit.FitLabel(t, size);
            return b;
        }

        // ---- the Barricade's home -----------------------------------------------------------

        void BuildMenu(RectTransform rt)
        {
            var m = UiKit.CreateRect("BarricadeMenu", rt, Vector2.zero, Vector2.one);
            menuPanel = m.gameObject;
            var veil = m.gameObject.AddComponent<Image>();
            veil.sprite = ApogeeTheme.VerticalFade;
            veil.color = new Color(0.12f, 0.03f, 0.06f, 0.82f);

            UiKit.Outlined(UiKit.CreateText("Title", m, "BARRICADE", 96, TextAnchor.MiddleCenter, new Vector2(0.05f, 0.85f), new Vector2(0.95f, 0.95f), ApogeeTheme.Gold), 4f);
            UiKit.CreateText("Tag", m, "Tiens la palissade. Forge ta puissance. Conquiers les cinq terres.", 26, TextAnchor.MiddleCenter, new Vector2(0.06f, 0.81f), new Vector2(0.94f, 0.85f), ApogeeTheme.Cream);
            menuShards = Pill(m, "Shards", new Vector2(0.26f, 0.745f), new Vector2(0.74f, 0.795f), 34);

            var card = Glass(m, "LandCard", new Vector2(0.06f, 0.43f), new Vector2(0.94f, 0.72f)).rectTransform;
            var thumb = UiKit.CreateImage("Thumb", card, new Vector2(0.05f, 0.42f), new Vector2(0.95f, 0.95f), null, Color.white, false);
            thumb.raycastTarget = false;
            var tex = Resources.Load<Texture2D>("Hub/card_barricade");
            if (tex != null)
            {
                thumb.sprite = Sprite.Create(tex, new Rect(0, tex.height * 0.3f, tex.width, tex.height * 0.5f), new Vector2(0.5f, 0.5f), 100f);
                thumb.preserveAspect = false;
            }
            landName = UiKit.Outlined(UiKit.CreateText("Name", card, "", 40, TextAnchor.MiddleCenter, new Vector2(0.05f, 0.3f), new Vector2(0.95f, 0.42f), ApogeeTheme.Gold), 2f);
            UiKit.FitLabel(landName, 40);
            landSub = UiKit.CreateText("Sub", card, "", 24, TextAnchor.MiddleCenter, new Vector2(0.05f, 0.21f), new Vector2(0.95f, 0.3f), ApogeeTheme.Cream);
            UiKit.FitLabel(landSub, 24);
            landStats = UiKit.CreateText("Stats", card, "", 22, TextAnchor.MiddleCenter, new Vector2(0.05f, 0.12f), new Vector2(0.95f, 0.21f), UiKit.TextDim);
            UiKit.FitLabel(landStats, 22);
            landBest = UiKit.Outlined(UiKit.CreateText("Best", card, "", 26, TextAnchor.MiddleCenter, new Vector2(0.05f, 0.03f), new Vector2(0.95f, 0.12f), ApogeeTheme.Gold), 1.5f);
            landLock = UiKit.Outlined(UiKit.CreateText("Lock", card, "", 30, TextAnchor.MiddleCenter, new Vector2(0.08f, 0.5f), new Vector2(0.92f, 0.9f), ApogeeTheme.Cream), 2.5f);
            UiKit.FitLabel(landLock, 30);
            prevLand = UiKit.CreateButton("Prev", m, "‹", new Vector2(0.0f, 0.53f), new Vector2(0.1f, 0.62f), () => ChangeLand(-1), 60, new Color(0.22f, 0.10f, 0.08f));
            nextLand = UiKit.CreateButton("Next", m, "›", new Vector2(0.9f, 0.53f), new Vector2(1f, 0.62f), () => ChangeLand(1), 60, new Color(0.22f, 0.10f, 0.08f));

            playButton = GoldButton(m, "Play", "DÉFENDRE", new Vector2(0.12f, 0.3f), new Vector2(0.88f, 0.39f), () => { if (land <= BarrSave.Unlocked) StartRun(); }, 48);
            UiKit.CreateButton("Forge", m, "LA FORGE", new Vector2(0.08f, 0.19f), new Vector2(0.49f, 0.27f), () => ShowForge(false), 30, new Color(0.45f, 0.22f, 0.1f));
            millButton = UiKit.CreateButton("Mill", m, "", new Vector2(0.51f, 0.19f), new Vector2(0.92f, 0.27f), CollectMill, 24, new Color(0.22f, 0.40f, 0.24f));
            millLabel = IconText.OnButton(millButton, 24);
            UiKit.CreateButton("Back", m, "RETOUR", new Vector2(0.3f, 0.07f), new Vector2(0.7f, 0.14f), ReturnToHub, 28, new Color(0.22f, 0.10f, 0.08f));
            menuPanel.SetActive(false);
        }

        void ShowMenu()
        {
            phase = Phase.Menu;
            ClearUnits();
            HideScreens();
            gameUi.SetActive(false);
            menuPanel.SetActive(true);
            menuPanel.transform.SetAsLastSibling();
            RefreshMenu();
        }

        void ChangeLand(int dir)
        {
            int next = Mathf.Clamp(land + dir, 0, Lands.Length - 1);
            if (next == land) return;
            land = next;
            if (land <= BarrSave.Unlocked) BarrSave.Region = land;
            ApplyLayout();
            Sfx.Drop();
            RefreshMenu();
        }

        void RefreshMenu()
        {
            var L = Lands[land];
            bool open = land <= BarrSave.Unlocked;
            landName.text = $"{land + 1}. {L.name}";
            landSub.text = L.subtitle;
            landStats.text = $"{L.lanes} couloirs   ·   morts x{L.hp:0.#}   ·   éclats x{L.reward:0.#}";
            int best = BarrSave.Best(land);
            landBest.text = open ? (best > 0 ? $"Record : vague {best}" : "Aucune vague tenue ici") : "";
            landLock.text = open ? "" : $"VERROUILLÉ\nTiens la vague {UnlockWave} dans « {Lands[land - 1].name} »";
            playButton.interactable = open;
            prevLand.interactable = land > 0;
            nextLand.interactable = land < Lands.Length - 1;
            menuShards.text = $"{BarrSave.Shards} [e]";
            RefreshMillLabel();
        }

        void RefreshMillLabel()
        {
            if (millLabel == null) return;
            int stock = Mathf.FloorToInt(BarrSave.MillStock);
            millLabel.text = ForgeLevel(Forge.Mill) <= 0 ? "MOULIN (Forge)" : stock > 0 ? $"MOULIN  +{stock} [g]" : $"MOULIN  {MillPerHour:0}/h";
            millButton.interactable = stock > 0;
        }

        // ---- the Forge ----------------------------------------------------------------------

        void BuildForge(RectTransform rt)
        {
            var f = UiKit.CreateRect("Forge", rt, Vector2.zero, Vector2.one);
            forgePanel = f.gameObject;
            var veil = f.gameObject.AddComponent<Image>();
            veil.color = new Color(0.08f, 0.02f, 0.03f, 0.94f);
            UiKit.Outlined(UiKit.CreateText("Title", f, "LA FORGE", 64, TextAnchor.MiddleCenter, new Vector2(0.05f, 0.9f), new Vector2(0.95f, 0.97f), ApogeeTheme.Gold), 3f);
            UiKit.CreateText("Sub", f, "Des améliorations pour toujours, payées en éclats", 24, TextAnchor.MiddleCenter, new Vector2(0.05f, 0.865f), new Vector2(0.95f, 0.9f), ApogeeTheme.Cream);
            forgeShards = Pill(f, "Shards", new Vector2(0.26f, 0.815f), new Vector2(0.74f, 0.86f), 32);

            var viewport = UiKit.CreateRect("Viewport", f, new Vector2(0.04f, 0.13f), new Vector2(0.96f, 0.8f));
            viewport.gameObject.AddComponent<RectMask2D>();
            var vimg = viewport.gameObject.AddComponent<Image>();
            vimg.color = new Color(1f, 1f, 1f, 0.01f);
            forgeContent = UiKit.CreateRect("Content", viewport, new Vector2(0f, 1f), new Vector2(1f, 1f));
            forgeContent.pivot = new Vector2(0.5f, 1f);
            const float rowH = 190f;
            forgeContent.sizeDelta = new Vector2(0f, rowH * ForgeDefs.Length + 20f);
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = forgeContent;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;

            forgeRows.Clear();
            for (int i = 0; i < ForgeDefs.Length; i++)
            {
                var id = (Forge)i;
                var row = UiKit.CreateRect($"Row_{i}", forgeContent, new Vector2(0f, 1f), new Vector2(1f, 1f));
                row.pivot = new Vector2(0.5f, 1f);
                row.sizeDelta = new Vector2(0f, rowH - 14f);
                row.anchoredPosition = new Vector2(0f, -10f - i * rowH);
                var bg = row.gameObject.AddComponent<Image>();
                bg.sprite = HubArt.Get("ui_tile", 32f) ?? ApogeeTheme.Panel;
                bg.type = Image.Type.Sliced;
                var name = UiKit.Outlined(UiKit.CreateText("Name", row, ForgeDefs[i].name, 32, TextAnchor.MiddleLeft, new Vector2(0.04f, 0.55f), new Vector2(0.62f, 0.92f), ApogeeTheme.Gold), 1.5f);
                UiKit.FitLabel(name, 32);
                var effect = UiKit.CreateText("Effect", row, ForgeDefs[i].effect, 22, TextAnchor.UpperLeft, new Vector2(0.04f, 0.08f), new Vector2(0.62f, 0.55f), ApogeeTheme.Cream);
                UiKit.FitLabel(effect, 22);
                var level = UiKit.Outlined(UiKit.CreateText("Level", row, "", 26, TextAnchor.MiddleCenter, new Vector2(0.62f, 0.62f), new Vector2(0.97f, 0.94f), ApogeeTheme.Cream), 1.2f);
                var buy = UiKit.CreateButton("Buy", row, "", new Vector2(0.64f, 0.1f), new Vector2(0.96f, 0.58f), () => BuyForge(id), 22);
                var buyLabel = IconText.OnButton(buy, 26);
                forgeRows.Add((id, level, buyLabel, buy, effect));
            }
            UiKit.CreateButton("Close", f, "RETOUR", new Vector2(0.3f, 0.035f), new Vector2(0.7f, 0.105f), CloseForge, 30, new Color(0.22f, 0.10f, 0.08f));
            forgePanel.SetActive(false);
        }

        void ShowForge(bool fromOver)
        {
            forgeFromOver = fromOver;
            forgePanel.SetActive(true);
            forgePanel.transform.SetAsLastSibling();
            RefreshForge();
        }

        void CloseForge()
        {
            forgePanel.SetActive(false);
            if (forgeFromOver) { overPanel.SetActive(true); overPanel.transform.SetAsLastSibling(); }
            else RefreshMenu();
        }

        void BuyForge(Forge id)
        {
            var def = ForgeDefs[(int)id];
            int level = ForgeLevel(id);
            if (level >= def.max) return;
            int cost = ForgeCost(id);
            if (BarrSave.Shards < cost) { Sfx.Hit(); return; }
            BarrSave.Shards -= cost;
            if (id == Forge.Mill) { MillCatchUp(); }
            BarrSave.SetForge((int)id, level + 1);
            if (id == Forge.Mill) BarrSave.MillLast = DateTime.UtcNow.Ticks;
            SaveSystem.Flush();
            Sfx.Milestone();
            Fx.Shake(0.08f, 0.1f);
            RefreshForge();
        }

        void RefreshForge()
        {
            forgeShards.text = $"{BarrSave.Shards} [e]";
            foreach (var (id, level, buy, button, effect) in forgeRows)
            {
                var def = ForgeDefs[(int)id];
                int l = ForgeLevel(id);
                level.text = $"{l} / {def.max}";
                bool maxed = l >= def.max;
                int cost = maxed ? 0 : ForgeCost(id);
                buy.text = maxed ? "MAX" : $"{cost} [e]";
                button.interactable = !maxed && BarrSave.Shards >= cost;
            }
        }

        // ---- the Blessings ----------------------------------------------------------------------

        void BuildPerks(RectTransform rt)
        {
            var p = UiKit.CreateRect("Blessing", rt, Vector2.zero, Vector2.one);
            perkPanel = p.gameObject;
            var veil = p.gameObject.AddComponent<Image>();
            veil.color = new Color(0.08f, 0.02f, 0.05f, 0.88f);
            UiKit.Outlined(UiKit.CreateText("Title", p, "BÉNÉDICTION", 70, TextAnchor.MiddleCenter, new Vector2(0.05f, 0.8f), new Vector2(0.95f, 0.88f), ApogeeTheme.Gold), 3f);
            UiKit.CreateText("Sub", p, "Choisis un don pour cette partie", 28, TextAnchor.MiddleCenter, new Vector2(0.05f, 0.76f), new Vector2(0.95f, 0.8f), ApogeeTheme.Cream);
            for (int i = 0; i < 3; i++)
            {
                int captured = i;
                float y1 = 0.72f - i * 0.2f, y0 = y1 - 0.18f;
                var b = UiKit.CreateButton($"Gift_{i}", p, "", new Vector2(0.08f, y0), new Vector2(0.92f, y1), () => TakePerk(captured), 20, UiKit.CardColor);
                var name = UiKit.Outlined(UiKit.CreateText("Name", b.transform, "", 38, TextAnchor.MiddleLeft, new Vector2(0.06f, 0.52f), new Vector2(0.8f, 0.92f), ApogeeTheme.Gold), 2f);
                UiKit.FitLabel(name, 38);
                var effect = UiKit.CreateText("Effect", b.transform, "", 26, TextAnchor.UpperLeft, new Vector2(0.06f, 0.08f), new Vector2(0.94f, 0.52f), ApogeeTheme.Cream);
                UiKit.FitLabel(effect, 26);
                var stack = UiKit.Outlined(UiKit.CreateText("Stack", b.transform, "", 26, TextAnchor.MiddleRight, new Vector2(0.75f, 0.55f), new Vector2(0.95f, 0.92f), ApogeeTheme.Cream), 1.2f);
                perkCards[i] = (b, name, effect, stack);
            }
            perkPanel.SetActive(false);
        }

        /// <summary>Three different gifts not yet at their limit; false if there are none left.</summary>
        bool OfferPerks()
        {
            var pool = new List<Perk>();
            for (int i = 0; i < PerkDefs.Length; i++) if (perks[i] < PerkDefs[i].max) pool.Add((Perk)i);
            if (pool.Count == 0) return false;
            for (int i = 0; i < pool.Count; i++) { int j = Random.Range(i, pool.Count); (pool[i], pool[j]) = (pool[j], pool[i]); }
            phase = Phase.Perk;
            sheet.SetActive(false);
            launchButton.gameObject.SetActive(false);
            for (int i = 0; i < 3; i++)
            {
                bool has = i < pool.Count;
                perkCards[i].button.gameObject.SetActive(has);
                if (!has) continue;
                var pk = pool[i];
                perkOffer[i] = pk;
                var def = PerkDefs[(int)pk];
                perkCards[i].name.text = def.name;
                perkCards[i].effect.text = def.effect;
                perkCards[i].stack.text = def.max > 1 ? $"{perks[(int)pk]} / {def.max}" : "";
            }
            perkPanel.SetActive(true);
            perkPanel.transform.SetAsLastSibling();
            Sfx.Milestone();
            return true;
        }

        void TakePerk(int index)
        {
            if (phase != Phase.Perk) return;
            var pk = perkOffer[index];
            perks[(int)pk]++;
            if (pk == Perk.Wall)
            {
                barricadeMax *= 1.35f;
                barricadeHp = barricadeMax;
                RefreshPalisade();
            }
            perkPanel.SetActive(false);
            ui.ShowBanner(PerkDefs[(int)pk].name.ToUpperInvariant(), PerkDefs[(int)pk].effect, 1.6f);
            EnterBuildPhase(first: false);
        }

        // ---- the end of a run -----------------------------------------------------------------

        void BuildOverScreen(RectTransform rt)
        {
            var o = UiKit.CreateRect("Over", rt, Vector2.zero, Vector2.one);
            overPanel = o.gameObject;
            var veil = o.gameObject.AddComponent<Image>();
            veil.color = new Color(0.08f, 0.02f, 0.03f, 0.88f);
            overTitle = UiKit.Outlined(UiKit.CreateText("Title", o, "", 58, TextAnchor.MiddleCenter, new Vector2(0.05f, 0.74f), new Vector2(0.95f, 0.84f), ApogeeTheme.Gold), 3f);
            UiKit.FitLabel(overTitle, 58);
            overStats = UiKit.CreateText("Stats", o, "", 30, TextAnchor.MiddleCenter, new Vector2(0.08f, 0.56f), new Vector2(0.92f, 0.73f), ApogeeTheme.Cream);
            overRewards = IconText.Create("Rewards", o, "", 44, TextAnchor.MiddleCenter, new Vector2(0.08f, 0.44f), new Vector2(0.92f, 0.55f), ApogeeTheme.Gold, 2f);
            GoldButton(o, "Again", "REJOUER", new Vector2(0.12f, 0.31f), new Vector2(0.88f, 0.4f), StartRun, 46);
            UiKit.CreateButton("Forge", o, "LA FORGE", new Vector2(0.08f, 0.2f), new Vector2(0.49f, 0.28f), () => { overPanel.SetActive(false); ShowForge(true); }, 30, new Color(0.45f, 0.22f, 0.1f));
            UiKit.CreateButton("Menu", o, "MENU", new Vector2(0.51f, 0.2f), new Vector2(0.92f, 0.28f), ShowMenu, 30, new Color(0.22f, 0.10f, 0.08f));
            overPanel.SetActive(false);
        }

        void ShowOver(int held, int shards, int materials)
        {
            bool record = held > 0 && held >= BarrSave.Best(land);
            overTitle.text = record ? "NOUVEAU RECORD !" : "LA PALISSADE EST TOMBÉE";
            overStats.text = $"{Lands[land].name}\nVagues tenues : {held}   ·   Abattus : {kills}\nMeilleure série : {bestStreak}   ·   Colosses : {bossesKilled}";
            overRewards.text = materials > 0 ? $"+{shards} [e]      +{materials} [g]" : $"+{shards} [e]";
            gameUi.SetActive(false);
            overPanel.SetActive(true);
            overPanel.transform.SetAsLastSibling();
        }

        void HideScreens()
        {
            menuPanel.SetActive(false);
            forgePanel.SetActive(false);
            perkPanel.SetActive(false);
            overPanel.SetActive(false);
        }

        void UpdateScreens()
        {
            if (menuPanel.activeSelf)
            {
                hudTimer -= Time.deltaTime;
                if (hudTimer <= 0f) { hudTimer = 1f; RefreshMillLabel(); }
            }
        }

        // ---- the Mill -------------------------------------------------------------------------

        float MillPerHour => 3f * ForgeLevel(Forge.Mill);
        const float MillCapHours = 8f;

        /// <summary>Counts what the Mill made since it was last counted (game closed included).</summary>
        void MillCatchUp()
        {
            long now = DateTime.UtcNow.Ticks;
            long last = BarrSave.MillLast;
            if (last > 0 && now > last && MillPerHour > 0f)
            {
                double hours = (now - last) / (double)TimeSpan.TicksPerHour;
                BarrSave.MillStock = Mathf.Min(MillPerHour * MillCapHours, BarrSave.MillStock + (float)(hours * MillPerHour));
            }
            BarrSave.MillLast = now;
        }

        float millSaveTimer;

        void MillTick(float dt)
        {
            millSaveTimer -= dt;
            if (millSaveTimer > 0f) return;
            millSaveTimer = 15f;
            MillCatchUp();
        }

        void MillSave() => MillCatchUp();

        void CollectMill()
        {
            MillCatchUp();
            int n = Mathf.FloorToInt(BarrSave.MillStock);
            if (n <= 0) return;
            BarrSave.MillStock -= n;
            SaveSystem.AddMaterials(n);
            SaveSystem.Flush();
            ui.ShowBanner("RÉCOLTE !", $"+{n} [g]", 1.4f);
            Sfx.Material();
            RefreshMillLabel();
        }
    }
}
