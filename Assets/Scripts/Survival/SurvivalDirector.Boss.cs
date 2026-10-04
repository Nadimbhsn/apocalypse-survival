using System.Collections;
using UnityEngine;

namespace Platformer.Survival
{
    /// <summary>
    /// A mini-boss every kilometre of the endless run. Past each 1000 m mark, as soon as the
    /// player is back on ordinary ground (never in the middle of an activity), the run
    /// stops and the Expedition's turn-based duel begins, the player walking in with the
    /// health they had on the track. Each boss of the run is a quarter stronger than the
    /// one before, up to two and a half times the first.
    ///
    /// Won: the boss pays out (the arena's own reward), the run goes on with the health
    /// left at the end of the fight. Lost: it is a death - the second-chance screen comes
    /// up, and a revived player meets the same boss again, at full health.
    /// </summary>
    public partial class SurvivalDirector
    {
        const float BossEvery = 1000f;
        const float BossWarning = 80f;

        float nextBossDistance;
        bool bossWarned, inBossDuel;

        void ResetBosses()
        {
            nextBossDistance = BossEvery;
            bossWarned = false;
            inBossDuel = false;
        }

        int BossNumber => Mathf.RoundToInt(nextBossDistance / BossEvery);

        void UpdateBossTrigger()
        {
            if (inBossDuel || inCampaign) return;
            if (!bossWarned && Distance >= nextBossDistance - BossWarning)
            {
                bossWarned = true;
                ui.ShowBanner("UN BOSS APPROCHE", $"Il t'attend à {Mathf.RoundToInt(nextBossDistance)} m : garde ta vie !", 2.2f);
            }
            if (Distance < nextBossDistance) return;
            // Only on plain ground: never mid-activity, mid-air or on a crumbling slab.
            if (inActivity || cadenceActive || ascentActive || shaftActive || player.jetpackActive) return;
            if (!player.IsGrounded || !IsWalkable(player.transform.position.x)) return;
            StartCoroutine(RunnerBossRoutine());
        }

        IEnumerator RunnerBossRoutine()
        {
            inBossDuel = true;
            running = false;
            player.controlEnabled = false;
            player.velocity = Vector2.zero;
            MobileInput.Reset();
            ClearZombiesAround(player.transform.position.x, -8f, 25f);
            // Nothing on the track may hurt the player while the duel is on.
            if (player.health != null) player.health.invulnerableUntil = float.MaxValue;

            Fx.Shake(0.5f, 0.6f);
            Sfx.Death();
            ui.ShowBanner("UN BOSS BLOQUE LE PASSAGE !", $"Kilomètre {BossNumber}", 1.4f);
            yield return new WaitForSecondsRealtime(1.3f);

            int number = BossNumber;
            float health = player.health != null ? player.health.NormalizedHP : 1f;
            float strength = Mathf.Min(2.5f, 1f + 0.25f * (number - 1));
            ui.StartRunnerBossDuel((number - 1) % ArenaCatalog.Bosses.Length, health, $"BOSS DU KILOMÈTRE {number}", strength, OnRunnerBossDone);
        }

        void OnRunnerBossDone(bool won, float healthLeft)
        {
            inBossDuel = false;
            if (player.health != null) player.health.invulnerableUntil = 0f;

            if (!won)
            {
                // A lost duel is a death: the second chance is offered as for any other.
                deathSpot = player.transform.position;
                SaveSystem.BestDistance = Mathf.Max(SaveSystem.BestDistance, Distance);
                Time.timeScale = 0f;
                if (OfferRevive()) ui.ShowReviveOffer(Distance, CanReviveWithAd);
                else GiveUpRun();
                return;
            }

            nextBossDistance += BossEvery;
            bossWarned = false;
            DailyMissions.Report(MissionKind.Boss, 1);
            SetHealthFraction(healthLeft);
            if (player.health != null) player.health.invulnerableUntil = Time.time + 1.5f;
            ClearZombiesAround(player.transform.position.x, -8f, 14f);
            running = true;
            player.controlEnabled = true;
            Sfx.Milestone();
            Fx.Burst(player.transform.position, ApogeeTheme.Gold, 36, 5f, 0.12f, 0.3f);
            ui.ShowBanner("BOSS VAINCU !", $"Prochain boss à {Mathf.RoundToInt(nextBossDistance)} m", 2f);
        }

        /// <summary>Sets the player's hearts to a share of their maximum (at least one).</summary>
        void SetHealthFraction(float fraction)
        {
            var h = player.health;
            if (h == null) return;
            int target = Mathf.Clamp(Mathf.RoundToInt(fraction * h.maxHP), 1, h.maxHP);
            int shield = h.shieldHits;
            h.shieldHits = 0;
            h.invulnerableUntil = 0f;
            h.Increment(h.maxHP);
            h.Decrement(h.maxHP - target);
            h.shieldHits = shield;
        }

        void ClearZombiesAround(float x, float behind, float ahead)
        {
            for (int i = zombies.Count - 1; i >= 0; i--)
            {
                var z = zombies[i];
                if (z == null) continue;
                float dx = z.transform.position.x - x;
                if (dx < behind || dx > ahead) continue;
                Destroy(z.gameObject);
                zombies.RemoveAt(i);
            }
        }
    }
}
