using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Platformer.Survival
{
    /// <summary>How the duel that closes a campaign level ended.</summary>
    public enum CampaignDuelOutcome
    {
        /// <summary>The boss went down: the level is cleared and its stars are awarded.</summary>
        Won,
        /// <summary>The player was beaten and wants to run the level again for more health.</summary>
        RetryLevel,
        /// <summary>The player backed out to the level list.</summary>
        Quit,
    }

    /// <summary>
    /// "ARÈNE": a Pokémon-style turn-based duel between the equipped character and the
    /// next boss on the ladder (see ArenaCatalog). Four moves with PP, accuracy rolls,
    /// weaknesses/resistances, burns and poison ticking each turn, shields, crits, and a
    /// message box narrating every step. Upgrades bought in the shop feed the fighter's
    /// HP / attack / defense, and beating a boss pays coins and materials into the shared
    /// wallet. Once the ladder is cleared it loops with stronger bosses.
    /// </summary>
    public class ArenaGame : MiniGame
    {
        public override string Id => "arena";
        public override string Title => "ARÈNE";
        public override string Description => "Affronte les boss en duel au tour par tour";
        public override string BestLine => SaveSystem.ArenaBossesBeaten > 0 ? $"Boss vaincus : {SaveSystem.ArenaBossesBeaten}" : "Aucun boss vaincu";

        class Fighter
        {
            public string Name;
            public int MaxHP, HP, Attack, Defense;
            public BattleMove[] Moves;
            public int[] PP;
            public MoveKind Weakness, Resist;
            public bool HasTypes;
            public int DotDamage, DotTurns;
            public MoveKind DotKind;
            public int ShieldTurns;
            public Graphic Sprite;
            public Image HpFill;
            public Text HpText;
            public Text StatusText;
            public bool Alive => HP > 0;
        }

        Fighter player, boss;
        Image playerImage;
        RawImage bossImage;
        ModelStage bossStage;
        static readonly string[] BossModels = { "character-zombie", "character-ghost", "character-keeper", "character-skeleton", "character-vampire" };
        BossDef bossDef;
        int bossIndex;
        float levelMult;
        bool busy;

        // ---- campaign duel ---------------------------------------------------------------
        // Set by SurvivalDirector when a level's gate is reached: the same arena, but the
        // boss is the level's own and the fighter walks in with the health they had left,
        // so how well the level was played decides how hard its boss is.
        bool campaignDuel;
        int campaignBossIndex;
        float campaignHealth = 1f;
        string campaignTitle;
        System.Action<CampaignDuelOutcome> campaignOnDone;

        Text messageText, bossNameText, playerNameText, bossLevelText;
        readonly Button[] moveButtons = new Button[4];
        readonly Text[] moveLabels = new Text[4];
        GameObject resultPanel;
        Text resultTitle, resultBody;
        Button resultContinue;

        // ---- UI ------------------------------------------------------------------------

        protected override void BuildUi()
        {
            var rt = UiKit.CreateRect("ArenaPanel", ui.Canvas.transform, Vector2.zero, Vector2.one);
            panel = rt.gameObject;
            var bg = panel.AddComponent<Image>();
            bg.color = ApogeeTheme.Maroon;
            UiKit.CreateArtBackdrop("ArenaSky", rt, "sky_runner", 0.72f, 0.5f);

            // Arena floor lines for depth.
            // Each fighter stands on a small floating island.
            var bossRock = UiKit.CreateImage("BossIsland", rt, new Vector2(0.55f, 0.46f), new Vector2(0.97f, 0.60f), ApogeeTheme.Island(1), Color.white, false);
            bossRock.rectTransform.pivot = new Vector2(0.5f, 1f);
            var playerRock = UiKit.CreateImage("PlayerIsland", rt, new Vector2(0.02f, 0.27f), new Vector2(0.46f, 0.395f), ApogeeTheme.Island(2), Color.white, false);
            UiKit.CreateImage("BossGrass", rt, new Vector2(0.55f, 0.588f), new Vector2(0.97f, 0.6f), PlaceholderVisuals.Square(Color.white), new Color(0.64f, 0.15f, 0.10f), false);
            UiKit.CreateImage("PlayerGrass", rt, new Vector2(0.02f, 0.383f), new Vector2(0.46f, 0.395f), PlaceholderVisuals.Square(Color.white), new Color(0.64f, 0.15f, 0.10f), false);

            UiKit.CreateButton("Quit", rt, "QUITTER", new Vector2(0.74f, 0.935f), new Vector2(0.96f, 0.98f), ReturnToHub, 22);

            // Boss: info box top-left, sprite top-right.
            boss = new Fighter();
            var bossBox = UiKit.CreateRect("BossBox", rt, new Vector2(0.04f, 0.80f), new Vector2(0.58f, 0.92f));
            var bossBoxImg = bossBox.gameObject.AddComponent<Image>();
            bossBoxImg.sprite = ApogeeTheme.Panel;
            bossBoxImg.type = Image.Type.Sliced;
            bossNameText = UiKit.CreateText("BossName", bossBox, "", 30, TextAnchor.MiddleLeft, new Vector2(0.05f, 0.6f), new Vector2(0.7f, 0.98f), UiKit.Parchment);
            bossLevelText = UiKit.CreateText("BossLevel", bossBox, "", 20, TextAnchor.MiddleRight, new Vector2(0.5f, 0.6f), new Vector2(0.95f, 0.98f), UiKit.Gold);
            boss.HpFill = UiKit.CreateBar("BossHp", bossBox, new Vector2(0.05f, 0.3f), new Vector2(0.95f, 0.55f), new Color(0.8f, 0.15f, 0.15f));
            boss.HpText = UiKit.CreateText("BossHpText", bossBox, "", 20, TextAnchor.MiddleRight, new Vector2(0.05f, 0.02f), new Vector2(0.95f, 0.3f), Color.white);
            boss.StatusText = UiKit.CreateText("BossStatus", bossBox, "", 20, TextAnchor.MiddleLeft, new Vector2(0.05f, 0.02f), new Vector2(0.7f, 0.3f), ArenaCatalog.KindColor(MoveKind.Toxique));
            var bossRt = UiKit.CreateRect("BossSprite", rt, new Vector2(0.55f, 0.585f), new Vector2(0.97f, 0.88f));
            bossRt.pivot = new Vector2(0.5f, 0f);
            var bossFitRt = UiKit.CreateRect("BossModel", bossRt, Vector2.zero, Vector2.one);
            var bossFit = bossFitRt.gameObject.AddComponent<AspectRatioFitter>();
            bossFit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            bossFit.aspectRatio = 1f;
            bossImage = bossFitRt.gameObject.AddComponent<RawImage>();
            bossImage.raycastTarget = false;
            boss.Sprite = bossImage;
            bossStage = ModelStage.Create("ArenaBossStage", new Vector3(-9500f, 9500f, 0f));
            bossImage.texture = bossStage.Texture;

            // Player: sprite bottom-left, info box bottom-right.
            player = new Fighter();
            playerImage = UiKit.CreateImage("PlayerSprite", rt, new Vector2(0.06f, 0.39f), new Vector2(0.42f, 0.60f), null, Color.white);
            player.Sprite = playerImage;
            var playerBox = UiKit.CreateRect("PlayerBox", rt, new Vector2(0.48f, 0.40f), new Vector2(0.96f, 0.52f));
            var playerBoxImg = playerBox.gameObject.AddComponent<Image>();
            playerBoxImg.sprite = ApogeeTheme.Panel;
            playerBoxImg.type = Image.Type.Sliced;
            playerNameText = UiKit.CreateText("PlayerName", playerBox, "", 30, TextAnchor.MiddleLeft, new Vector2(0.05f, 0.6f), new Vector2(0.95f, 0.98f), UiKit.Parchment);
            player.HpFill = UiKit.CreateBar("PlayerHp", playerBox, new Vector2(0.05f, 0.3f), new Vector2(0.95f, 0.55f), new Color(0.25f, 0.75f, 0.3f));
            player.HpText = UiKit.CreateText("PlayerHpText", playerBox, "", 20, TextAnchor.MiddleRight, new Vector2(0.05f, 0.02f), new Vector2(0.95f, 0.3f), Color.white);
            player.StatusText = UiKit.CreateText("PlayerStatus", playerBox, "", 20, TextAnchor.MiddleLeft, new Vector2(0.05f, 0.02f), new Vector2(0.7f, 0.3f), ArenaCatalog.KindColor(MoveKind.Toxique));

            // Message box.
            var msgBox = UiKit.CreateRect("MessageBox", rt, new Vector2(0.04f, 0.265f), new Vector2(0.96f, 0.35f));
            var msgImg = msgBox.gameObject.AddComponent<Image>();
            msgImg.sprite = ApogeeTheme.Panel;
            msgImg.type = Image.Type.Sliced;
            messageText = UiKit.CreateText("Message", msgBox, "", 26, TextAnchor.MiddleLeft, new Vector2(0.04f, 0.05f), new Vector2(0.96f, 0.95f), ApogeeTheme.Cream);
            UiKit.FitLabel(messageText, 26);

            // 2x2 move grid.
            for (int i = 0; i < 4; i++)
            {
                int col = i % 2, row = i / 2;
                float x0 = 0.04f + col * 0.47f;
                float yMax = 0.25f - row * 0.105f;
                int captured = i;
                moveButtons[i] = UiKit.CreateButton($"Move_{i}", rt, "", new Vector2(x0, yMax - 0.095f), new Vector2(x0 + 0.45f, yMax),
                    () => OnMoveClicked(captured), 24, UiKit.CardColor);
                moveLabels[i] = UiKit.ButtonLabel(moveButtons[i]);
                moveLabels[i].alignment = TextAnchor.MiddleCenter;
            }

            // Result overlay.
            var resultRt = UiKit.CreatePanel("ArenaResult", rt, UiKit.Overlay);
            resultPanel = resultRt.gameObject;
            UiKit.CreateFrame("ResultFrame", resultRt, new Vector2(0.08f, 0.24f), new Vector2(0.92f, 0.8f));
            resultTitle = UiKit.Outlined(UiKit.CreateText("ResultTitle", resultRt, "", 56, TextAnchor.MiddleCenter, new Vector2(0.05f, 0.64f), new Vector2(0.95f, 0.76f), ApogeeTheme.Gold), 2.5f);
            resultBody = UiKit.CreateText("ResultBody", resultRt, "", 28, TextAnchor.MiddleCenter, new Vector2(0.1f, 0.50f), new Vector2(0.9f, 0.63f), ApogeeTheme.Cream);
            resultContinue = UiKit.CreateButton("ResultContinue", resultRt, "CONTINUER", new Vector2(0.25f, 0.38f), new Vector2(0.75f, 0.45f), OnResultContinue);
            UiKit.CreateButton("ResultMenu", resultRt, "MENU", new Vector2(0.25f, 0.29f), new Vector2(0.75f, 0.36f), OnResultMenu);
            resultPanel.SetActive(false);
        }

        // ---- lifecycle -----------------------------------------------------------------

        protected override void OnEnter() => StartBattle();

        protected override void OnExit()
        {
            StopAllCoroutines();
            busy = false;
            campaignDuel = false;
            campaignOnDone = null;
            if (bossStage != null) bossStage.SetActive(false);
        }

        /// <summary>
        /// Opens the duel that closes a campaign level. healthLeft (0..1) is the health bar
        /// the player finished the level with and becomes the health they start the fight
        /// with, so reaching the gate in good shape is the real reward for playing well.
        /// onDone is called with whether the boss went down.
        /// </summary>
        public void StartCampaignDuel(int levelBossIndex, float healthLeft, string levelName, System.Action<CampaignDuelOutcome> onDone)
        {
            campaignDuel = true;
            campaignBossIndex = levelBossIndex;
            campaignHealth = Mathf.Clamp(healthLeft, 0.15f, 1f); // never walk in already dead
            campaignTitle = levelName;
            campaignOnDone = onDone;
            Enter();
        }

        void FinishCampaignDuel(CampaignDuelOutcome outcome)
        {
            var done = campaignOnDone;
            campaignDuel = false;
            campaignOnDone = null;
            Exit();
            done?.Invoke(outcome);
        }

        void OnResultMenu()
        {
            if (campaignDuel) FinishCampaignDuel(CampaignDuelOutcome.Quit);
            else ReturnToHub();
        }

        void OnResultContinue()
        {
            if (!campaignDuel) { StartBattle(); return; }
            FinishCampaignDuel(boss.Alive ? CampaignDuelOutcome.RetryLevel : CampaignDuelOutcome.Won);
        }

        void StartBattle()
        {
            StopAllCoroutines();
            resultPanel.SetActive(false);

            int beaten = SaveSystem.ArenaBossesBeaten;
            int cycle;
            if (campaignDuel)
            {
                // A level's boss is fixed and always fought at its base strength: the
                // difficulty of a campaign fight comes from the health left, not from how
                // far up the endless ladder the player happens to be.
                bossIndex = Mathf.Clamp(campaignBossIndex, 0, ArenaCatalog.Bosses.Length - 1);
                cycle = 0;
                levelMult = 1f;
            }
            else
            {
                bossIndex = beaten % ArenaCatalog.Bosses.Length;
                cycle = beaten / ArenaCatalog.Bosses.Length;
                levelMult = 1f + cycle * 0.35f;
            }
            bossDef = ArenaCatalog.Bosses[bossIndex];

            // Player from the equipped character + shop upgrades.
            var skin = SkinCatalog.Find(SaveSystem.SelectedSkinId);
            player.Name = skin.Name;
            player.MaxHP = 100 + SaveSystem.GetLevel(UpgradeStat.MaxHealth) * 12;
            player.HP = campaignDuel ? Mathf.Max(1, Mathf.RoundToInt(player.MaxHP * campaignHealth)) : player.MaxHP;
            player.Attack = 20 + SaveSystem.GetLevel(UpgradeStat.FirePower) * 2;
            player.Defense = 12 + SaveSystem.GetLevel(UpgradeStat.Armor) * 2;
            player.Moves = ArenaCatalog.MovesFor(skin.Id);
            player.PP = new int[4];
            for (int i = 0; i < 4; i++) player.PP[i] = player.Moves[i].MaxPP;
            player.HasTypes = false;
            ResetStatus(player);
            var portrait = SkinCatalog.LoadPortrait(skin, out bool custom);
            playerImage.sprite = custom ? portrait : ui.PlayerSprite;
            playerImage.color = custom ? Color.white : skin.Tint;
            playerNameText.text = player.Name;

            boss.Name = bossDef.Name;
            boss.MaxHP = Mathf.RoundToInt(bossDef.MaxHP * levelMult);
            boss.HP = boss.MaxHP;
            boss.Attack = Mathf.RoundToInt(bossDef.Attack * levelMult);
            boss.Defense = Mathf.RoundToInt(bossDef.Defense * levelMult);
            boss.Moves = bossDef.Moves;
            boss.PP = new int[boss.Moves.Length];
            for (int i = 0; i < boss.PP.Length; i++) boss.PP[i] = boss.Moves[i].MaxPP;
            boss.Weakness = bossDef.Weakness;
            boss.Resist = bossDef.Resist;
            boss.HasTypes = true;
            ResetStatus(boss);
            // Custom art in Resources/Bosses/bossN wins; otherwise a Kenney character on the stage.
            var bossSprite = Resources.Load<Sprite>($"Bosses/boss{bossIndex}");
            if (bossSprite != null)
            {
                bossStage.SetActive(false);
                bossImage.texture = bossSprite.texture;
                var r = bossSprite.textureRect;
                bossImage.uvRect = new Rect(r.x / bossSprite.texture.width, r.y / bossSprite.texture.height, r.width / bossSprite.texture.width, r.height / bossSprite.texture.height);
            }
            else
            {
                bossImage.texture = bossStage.Texture;
                bossImage.uvRect = new Rect(0f, 0f, 1f, 1f);
                bossStage.Show(PropKit.Graveyard, BossModels[bossIndex % BossModels.Length], 1f + (bossDef.Scale - 1f) * 0.6f);
                bossStage.SetActive(true);
            }
            bossImage.color = Color.white;
            boss.Sprite.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.85f, 1.1f, (bossDef.Scale - 1f) / 0.45f);
            bossNameText.text = boss.Name;
            bossLevelText.text = campaignDuel ? campaignTitle
                : cycle > 0 ? $"Boss {bossIndex + 1}  •  Niv. {cycle + 1}" : $"Boss {bossIndex + 1}";

            RefreshFighter(player, true);
            RefreshFighter(boss, true);
            RefreshMoveButtons();
            StartCoroutine(IntroRoutine());
        }

        static void ResetStatus(Fighter f)
        {
            f.DotDamage = 0;
            f.DotTurns = 0;
            f.ShieldTurns = 0;
        }

        IEnumerator IntroRoutine()
        {
            busy = true;
            SetMovesInteractable(false);
            yield return Say($"{boss.Name} apparaît !", 1.2f);
            yield return Say(bossDef.Intro, 1.4f);
            if (campaignDuel)
                yield return Say($"{player.Name} entre avec {Mathf.RoundToInt(campaignHealth * 100f)} % de vie.", 1.3f);
            yield return Say($"Que doit faire {player.Name} ?", 0f);
            busy = false;
            SetMovesInteractable(true);
        }

        // ---- turn flow -----------------------------------------------------------------

        void OnMoveClicked(int index)
        {
            if (busy || !IsActive) return;
            if (player.PP[index] <= 0) return;
            StartCoroutine(TurnRoutine(index));
        }

        IEnumerator TurnRoutine(int playerMoveIndex)
        {
            busy = true;
            SetMovesInteractable(false);

            player.PP[playerMoveIndex]--;
            RefreshMoveButtons();
            yield return UseMove(player, boss, player.Moves[playerMoveIndex]);
            yield return EndOfTurnEffects(player);
            if (!boss.Alive) { yield return Victory(); yield break; }
            if (!player.Alive) { yield return Defeat(); yield break; }

            yield return new WaitForSeconds(0.3f);
            var bossMove = ChooseBossMove();
            yield return UseMove(boss, player, bossMove);
            yield return EndOfTurnEffects(boss);
            if (!player.Alive) { yield return Defeat(); yield break; }
            if (!boss.Alive) { yield return Victory(); yield break; }

            yield return Say($"Que doit faire {player.Name} ?", 0f);
            RefreshMoveButtons();
            busy = false;
            SetMovesInteractable(true);
        }

        BattleMove ChooseBossMove()
        {
            var moves = boss.Moves;
            if (boss.HP < boss.MaxHP * 0.35f && Random.value < 0.5f)
                foreach (var m in moves) if (m.Kind == MoveKind.Soin) return m;
            if (boss.ShieldTurns == 0 && Random.value < 0.25f)
                foreach (var m in moves) if (m.Kind == MoveKind.Bouclier) return m;

            // Weighted pick among attacks: heavier moves a bit less often.
            float total = 0f;
            foreach (var m in moves) if (m.IsAttack) total += 100f / (m.Power + 10f);
            float roll = Random.value * total;
            foreach (var m in moves)
            {
                if (!m.IsAttack) continue;
                roll -= 100f / (m.Power + 10f);
                if (roll <= 0f) return m;
            }
            return moves[0];
        }

        IEnumerator UseMove(Fighter user, Fighter target, BattleMove move)
        {
            yield return Say($"{user.Name} utilise {move.Name} !", 0.9f);

            if (move.IsAttack)
            {
                if (Random.Range(0, 100) >= move.Accuracy)
                {
                    yield return Say("L'attaque échoue !", 1f);
                    yield break;
                }

                yield return Lunge(user.Sprite, user == player ? new Vector2(60f, 30f) : new Vector2(-60f, -30f));

                float typeMult = 1f;
                if (target.HasTypes)
                {
                    if (move.Kind == target.Weakness) typeMult = 1.5f;
                    else if (move.Kind == target.Resist) typeMult = 0.6f;
                }
                bool crit = Random.value < 0.08f;
                float shield = target.ShieldTurns > 0 ? 0.5f : 1f;
                int damage = Mathf.Max(1, Mathf.RoundToInt(move.Power * (user.Attack / (float)(target.Defense + 10)) * 1.6f
                    * Random.Range(0.9f, 1.1f) * typeMult * (crit ? 1.5f : 1f) * shield));

                target.HP = Mathf.Max(0, target.HP - damage);
                Sfx.Attack();
                StartCoroutine(HitFlash(target.Sprite));
                yield return AnimateHp(target);

                if (crit) yield return Say("Coup critique !", 0.8f);
                if (typeMult > 1f) yield return Say("C'est super efficace !", 0.9f);
                else if (typeMult < 1f) yield return Say("Ce n'est pas très efficace...", 0.9f);
                if (shield < 1f) yield return Say($"Le bouclier de {target.Name} absorbe le choc.", 0.8f);

                if (move.DotTurns > 0 && target.Alive && target.DotTurns == 0)
                {
                    target.DotDamage = Mathf.RoundToInt(move.DotDamage * (user == boss ? levelMult : 1f));
                    target.DotTurns = move.DotTurns;
                    target.DotKind = move.Kind;
                    RefreshFighter(target, false);
                    yield return Say(move.Kind == MoveKind.Feu ? $"{target.Name} brûle !" : $"{target.Name} est empoisonné !", 0.9f);
                }
            }

            if (move.Heal > 0 && user.Alive)
            {
                int heal = Mathf.RoundToInt(move.Heal * (user == boss ? levelMult : 1f));
                int before = user.HP;
                user.HP = Mathf.Min(user.MaxHP, user.HP + heal);
                Sfx.Heal();
                yield return AnimateHp(user);
                yield return Say($"{user.Name} récupère {user.HP - before} PV.", 0.9f);
            }

            if (move.ShieldTurns > 0)
            {
                user.ShieldTurns = move.ShieldTurns + 1; // +1 because it ticks down at the end of this same turn
                RefreshFighter(user, false);
                yield return Say($"{user.Name} se protège.", 0.9f);
            }
        }

        IEnumerator EndOfTurnEffects(Fighter fighter)
        {
            if (fighter.ShieldTurns > 0)
            {
                fighter.ShieldTurns--;
                RefreshFighter(fighter, false);
            }
            if (fighter.DotTurns > 0 && fighter.Alive)
            {
                fighter.DotTurns--;
                fighter.HP = Mathf.Max(0, fighter.HP - fighter.DotDamage);
                StartCoroutine(HitFlash(fighter.Sprite));
                yield return AnimateHp(fighter);
                yield return Say(fighter.DotKind == MoveKind.Feu
                    ? $"{fighter.Name} souffre de sa brûlure ! (-{fighter.DotDamage})"
                    : $"{fighter.Name} souffre du poison ! (-{fighter.DotDamage})", 0.9f);
                if (fighter.DotTurns == 0) fighter.DotDamage = 0;
                RefreshFighter(fighter, false);
            }
        }

        IEnumerator Victory()
        {
            yield return Say($"{boss.Name} est vaincu !", 1.2f);
            int coins = Mathf.RoundToInt(bossDef.RewardCoins * levelMult);
            int materials = Mathf.RoundToInt(bossDef.RewardMaterials * levelMult);
            SaveSystem.AddCoins(coins);
            SaveSystem.AddMaterials(materials);
            // The endless ladder only advances when the ladder itself is being played.
            if (!campaignDuel) SaveSystem.ArenaBossesBeaten = SaveSystem.ArenaBossesBeaten + 1;

            Sfx.Milestone();
            resultTitle.text = "VICTOIRE !";
            resultTitle.color = UiKit.Gold;
            resultBody.text = campaignDuel
                ? $"+{coins} pièces   +{materials} matériaux\nNiveau terminé !"
                : $"+{coins} pièces   +{materials} matériaux\nProchain boss débloqué";
            UiKit.ButtonLabel(resultContinue).text = campaignDuel ? "VOIR LES ÉTOILES" : "BOSS SUIVANT";
            resultPanel.SetActive(true);
            busy = false;
        }

        IEnumerator Defeat()
        {
            yield return Say($"{player.Name} est K.O. ...", 1.2f);
            Sfx.Death();
            AdService.OnPlayerDeath();
            resultTitle.text = "DÉFAITE";
            resultTitle.color = new Color(0.75f, 0.15f, 0.1f);
            resultBody.text = campaignDuel
                ? "Refais le niveau et arrive au portail\navec plus de vie."
                : "Améliore ton personnage dans la boutique\nou change de combattant.";
            UiKit.ButtonLabel(resultContinue).text = campaignDuel ? "REFAIRE LE NIVEAU" : "RÉESSAYER";
            resultPanel.SetActive(true);
            busy = false;
        }

        // ---- presentation --------------------------------------------------------------

        IEnumerator Say(string text, float hold)
        {
            messageText.text = text;
            if (hold > 0f) yield return new WaitForSeconds(hold);
        }

        IEnumerator Lunge(Graphic sprite, Vector2 offset)
        {
            var rt = sprite.rectTransform;
            var start = rt.anchoredPosition;
            const float duration = 0.12f;
            float t = 0f;
            while (t < duration) { t += Time.deltaTime; rt.anchoredPosition = Vector2.Lerp(start, start + offset, t / duration); yield return null; }
            t = 0f;
            while (t < duration) { t += Time.deltaTime; rt.anchoredPosition = Vector2.Lerp(start + offset, start, t / duration); yield return null; }
            rt.anchoredPosition = start;
        }

        IEnumerator HitFlash(Graphic sprite)
        {
            var baseColor = sprite.color;
            var rt = sprite.rectTransform;
            var start = rt.anchoredPosition;
            const float duration = 0.3f;
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float p = t / duration;
                sprite.color = Color.Lerp(new Color(1f, 0.3f, 0.3f), baseColor, p);
                rt.anchoredPosition = start + new Vector2(Mathf.Sin(t * 70f) * 12f * (1f - p), 0f);
                yield return null;
            }
            sprite.color = baseColor;
            rt.anchoredPosition = start;
        }

        IEnumerator AnimateHp(Fighter f)
        {
            float from = f.HpFill.fillAmount;
            float to = f.MaxHP > 0 ? f.HP / (float)f.MaxHP : 0f;
            const float duration = 0.45f;
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                f.HpFill.fillAmount = Mathf.Lerp(from, to, t / duration);
                f.HpText.text = $"{Mathf.RoundToInt(Mathf.Lerp(from, to, t / duration) * f.MaxHP)} / {f.MaxHP} PV";
                yield return null;
            }
            RefreshFighter(f, true);
        }

        void RefreshFighter(Fighter f, bool hp)
        {
            if (hp)
            {
                f.HpFill.fillAmount = f.MaxHP > 0 ? f.HP / (float)f.MaxHP : 0f;
                f.HpText.text = $"{f.HP} / {f.MaxHP} PV";
                f.HpFill.color = f.HP > f.MaxHP * 0.5f ? new Color(0.25f, 0.75f, 0.3f) : f.HP > f.MaxHP * 0.2f ? new Color(0.9f, 0.7f, 0.2f) : new Color(0.8f, 0.15f, 0.15f);
            }
            string status = "";
            if (f.DotTurns > 0) status += f.DotKind == MoveKind.Feu ? "BRÛLÉ " : "POISON ";
            if (f.ShieldTurns > 0) status += "BOUCLIER";
            f.StatusText.text = status;
        }

        void RefreshMoveButtons()
        {
            for (int i = 0; i < 4; i++)
            {
                var move = player.Moves[i];
                moveLabels[i].text = $"{move.Name}\n<size=18>{ArenaCatalog.KindLabel(move.Kind)}  •  PP {player.PP[i]}/{move.MaxPP}</size>";
                moveLabels[i].supportRichText = true;
                moveLabels[i].color = ArenaCatalog.KindColor(move.Kind);
                moveButtons[i].interactable = !busy && player.PP[i] > 0;
            }
        }

        void SetMovesInteractable(bool value)
        {
            for (int i = 0; i < 4; i++) moveButtons[i].interactable = value && player.PP[i] > 0;
        }
    }
}
