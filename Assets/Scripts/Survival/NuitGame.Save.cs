using System;
using System.Collections.Generic;
using UnityEngine;

namespace Platformer.Survival
{
    /// <summary>
    /// A night left before its end is kept. Quitting between waves keeps everything as it is;
    /// quitting in the middle of a wave keeps the station as it stood when that wave began
    /// (the wave is fought again on return). Back in the game, REPRENDRE opens the shop
    /// before that wave. Only the second fall ends a night - and clears its save.
    /// </summary>
    public partial class NuitGame
    {
        const string SaveKey = "nuit_run";

        [Serializable]
        class NuitSave
        {
            /// <summary>Waves fully held.</summary>
            public int wave, scrap, kills, bosses, gunIndex;
            public float hp, maxHp, shield, heroX, heroY;
            public bool secondChanceUsed, bonusPending;
            public List<int> gunsOwned = new(), perks = new(), gadgets = new(), zonesOpen = new();
            public List<float> doorsHp = new(), turretX = new(), turretY = new();
        }

        /// <summary>The station as the current wave found it (taken when it begins).</summary>
        NuitSave waveStart;

        static NuitSave LoadSave()
        {
            string json = PlayerPrefs.GetString(SaveKey, "");
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonUtility.FromJson<NuitSave>(json); }
            catch (Exception) { return null; }
        }

        static void ClearSave()
        {
            PlayerPrefs.DeleteKey(SaveKey);
            PlayerPrefs.Save();
        }

        NuitSave Snapshot(int held, bool bonusPending)
        {
            var s = new NuitSave
            {
                wave = held, scrap = scrap, kills = kills, bosses = bosses, gunIndex = gunIndex,
                hp = Mathf.Max(1f, hp), maxHp = maxHp, shield = shield, heroX = heroPos.x, heroY = heroPos.y,
                secondChanceUsed = secondChanceUsed, bonusPending = bonusPending,
            };
            foreach (var g in gunsOwned) s.gunsOwned.Add(g ? 1 : 0);
            foreach (var p in perkStacks) s.perks.Add(p);
            foreach (var g in gadget) s.gadgets.Add(g);
            for (int z = 0; z < zones.Length; z++) if (zones[z].Open) s.zonesOpen.Add(z);
            foreach (var d in doors) s.doorsHp.Add(d.Hp);
            foreach (var t in turrets) { s.turretX.Add(t.Pos.x); s.turretY.Add(t.Pos.y); }
            return s;
        }

        bool Running => phase == Phase.Wave || phase == Phase.Bonus || phase == Phase.Shop;

        /// <summary>Keeps the night: as it is between waves, as the wave found it in the middle of one.</summary>
        void SaveRun()
        {
            if (!Running || zones == null) return;
            var s = phase == Phase.Wave ? waveStart : Snapshot(wave, phase == Phase.Bonus);
            if (s == null) return;
            PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(s));
            PlayerPrefs.Save();
        }

        static int ScoreOf(NuitSave s) => s.wave * 100 + s.kills * 5 + s.bosses * 300;

        void ResumeNight()
        {
            var s = LoadSave();
            if (s == null) { StartNight(); return; }
            ClearRun();
            wave = s.wave; scrap = s.scrap; kills = s.kills; bosses = s.bosses; scrapCarry = 0f;
            for (int i = 0; i < perkStacks.Length; i++) perkStacks[i] = i < s.perks.Count ? s.perks[i] : 0;
            for (int i = 0; i < gadget.Length; i++) gadget[i] = i < s.gadgets.Count ? s.gadgets[i] : 0;
            for (int i = 0; i < gunsOwned.Length; i++) gunsOwned[i] = i == 0 || (i < s.gunsOwned.Count && s.gunsOwned[i] != 0);
            gunIndex = gunsOwned[Mathf.Clamp(s.gunIndex, 0, gunsOwned.Length - 1)] ? Mathf.Clamp(s.gunIndex, 0, gunsOwned.Length - 1) : 0;
            maxHp = Mathf.Max(1f, s.maxHp); hp = Mathf.Clamp(s.hp, 1f, maxHp); shield = s.shield;
            secondChanceUsed = s.secondChanceUsed;
            invuln = 0f; dashTimer = 0f; dashCooldown = 0f;
            heroPos = new Vector2(s.heroX, s.heroY); heroVel = Vector2.zero;
            zones = NewZones();
            foreach (int z in s.zonesOpen) if (z >= 0 && z < zones.Length) zones[z].Open = true;
            BuildWorld();
            for (int i = 0; i < doors.Count && i < s.doorsHp.Count; i++)
            {
                doors[i].Hp = Mathf.Clamp(s.doorsHp[i], 0f, doors[i].MaxHp);
                doors[i].State = -1;
                SetDoorLook(doors[i]);
            }
            for (int i = 0; i < s.turretX.Count && i < s.turretY.Count; i++) PlaceTurret(new Vector2(s.turretX[i], s.turretY[i]));
            hero.gameObject.SetActive(true);
            RefreshGun();
            FrameCamera(heroPos, true);
            if (s.bonusPending) { phase = Phase.Bonus; ShowBonusChoice(); }
            else ShowShop();
        }

        /// <summary>A fresh night over a saved one: the old one still counts on the leaderboard.</summary>
        void NewNight()
        {
            var old = LoadSave();
            if (old != null && old.wave > 0)
                Record(new BoardEntry { score = ScoreOf(old), wave = old.wave, kills = old.kills, date = DateTime.Now.ToString("dd/MM") });
            ClearSave();
            StartNight();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused && IsActive) SaveRun();
        }

        void OnApplicationQuit()
        {
            if (IsActive) SaveRun();
        }
    }
}
