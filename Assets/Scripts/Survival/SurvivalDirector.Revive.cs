using Platformer.Core;
using UnityEngine;

namespace Platformer.Survival
{
    /// <summary>
    /// A second chance in the endless run. When the player dies, the run is frozen and they
    /// may come back where they fell - by spending a healing kit (made in Fusion, three at
    /// most) or, once per run, by watching an ad - or give up and see the usual end screen.
    ///
    /// "Where they fell" is the last spot they stood on firm ground: while running, every
    /// frame on a solid, stable street segment outside the set pieces' special rules is
    /// remembered. A fall into the void therefore brings them back to the edge they jumped
    /// from; a death in the jetpack flight puts them back in the air with the jetpack on.
    /// They return at full health, with two and a half seconds of grace, the nearby dead
    /// cleared away and the storm pushed back.
    /// </summary>
    public partial class SurvivalDirector
    {
        const float ReviveGrace = 2.5f;

        Vector2 safeSpot;
        bool hasSafeSpot;
        Vector2 deathSpot;
        bool adReviveUsed;

        public bool CanReviveWithAd => !adReviveUsed;

        void ResetRevive()
        {
            hasSafeSpot = false;
            adReviveUsed = false;
        }

        /// <summary>Remembers the last firm, ordinary ground under the player's feet.</summary>
        void TrackSafeSpot()
        {
            if (inCampaign || !player.IsGrounded || player.jetpackActive) return;
            if (cadenceActive || ascentActive || shaftActive) return;
            float x = player.transform.position.x;
            if (InCadenceSpan(x)) return;
            if (!GetSegmentAt(x, out var seg) || seg.isGap) return;
            // A slab that crumbles is not a place to come back to.
            if (seg.go != null && seg.go.GetComponent<UnstablePlatform>() != null) return;
            float feet = player.collider2d != null ? player.collider2d.bounds.min.y : player.transform.position.y;
            if (Mathf.Abs(feet - seg.topY) > 0.3f) return;   // standing on something else (a crate, an islet)
            safeSpot = player.transform.position;
            hasSafeSpot = true;
        }

        /// <summary>True when a second chance is on offer (endless run only).</summary>
        bool OfferRevive()
        {
            return !inCampaign && (SaveSystem.ReviveKits > 0 || CanReviveWithAd);
        }

        /// <summary>The player took the second chance: back into the run.</summary>
        public void Revive(bool viaAd)
        {
            if (running || player == null) return;
            if (viaAd) adReviveUsed = true;

            Time.timeScale = 1f;
            // The sample's death schedules a respawn at the old hand-made spawn point: drop it.
            Simulation.Clear();
            RestorePlayerAfterDeath();

            // Set pieces the player died in start over cleanly.
            ascentActive = false;
            shaftActive = false;
            player.maxFallSpeed = 16f;

            Vector2 spot = hasSafeSpot ? safeSpot : (Vector2)player.transform.position;
            bool inFlight = activeZone == ZoneKind.Jetpack && deathSpot.x < jetpackLandingX;
            if (inFlight)
            {
                // Back in the air where the flight was lost, jetpack on.
                spot = new Vector2(deathSpot.x, baselineY + 4.5f);
                StartJetpack();
            }
            else
            {
                player.jetpackActive = false;
                player.jetpackCeilingY = float.MaxValue;
            }

            PlacePlayerAt(spot.x, spot.y + 0.05f);
            player.velocity = Vector2.zero;
            if (player.health != null)
            {
                player.health.Increment(player.health.maxHP);
                player.health.invulnerableUntil = Time.time + ReviveGrace;
            }
            deathLineY = LocalGroundLevel(spot.x) - fallDepthBelowGround;

            // Breathing room: the dead around the spot are gone, the storm steps back.
            for (int i = zombies.Count - 1; i >= 0; i--)
            {
                var z = zombies[i];
                if (z == null) continue;
                float dx = z.transform.position.x - spot.x;
                if (dx < -6f || dx > 16f) continue;
                Fx.Burst(z.transform.position, new Color(1f, 0.9f, 0.7f), 10, 2.5f, 0.08f, 0.2f);
                Destroy(z.gameObject);
                zombies.RemoveAt(i);
            }
            if (chaseWall != null) chaseWall.PushBackTo(spot.x - 9f);

            running = true;
            player.controlEnabled = true;
            MobileInput.Reset();
            StartCoroutine(ReviveBlink());

            Sfx.Heal();
            Fx.Burst(player.transform.position, new Color(0.55f, 1f, 0.7f), 30, 4f, 0.12f, 0f);
            ui.ShowBanner("DE RETOUR !", viaAd ? "Merci d'avoir regardé la pub" : "Un kit de soin utilisé", 1.6f);
        }

        /// <summary>No second chance, or the player declined it: the usual end of the run.</summary>
        public void GiveUpRun()
        {
            if (running) return;
            Time.timeScale = 0f;
            ui.ShowGameOver(Distance);
            AdService.OnPlayerDeath();
        }

        /// <summary>The character flickers through the grace period, so it reads as untouchable.</summary>
        System.Collections.IEnumerator ReviveBlink()
        {
            var sr = player.GetComponent<SpriteRenderer>();
            if (sr == null || player.health == null) yield break;
            while (running && Time.time < player.health.invulnerableUntil)
            {
                var c = sr.color;
                sr.color = new Color(c.r, c.g, c.b, Mathf.Repeat(Time.time * 8f, 1f) < 0.5f ? 0.35f : 1f);
                yield return null;
            }
            var end = sr.color;
            sr.color = new Color(end.r, end.g, end.b, 1f);
        }
    }
}
