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
    /// frame on a solid, stable street segment outside the set pieces is remembered, so a
    /// fall into the void brings them back to the edge they jumped from. A death inside an
    /// activity (tower, free fall, flight, archipelago, storm, La Cadence) brings them back
    /// to its start - the firm ground just before it - to play it again from the top, the
    /// jetpack switched back on for the flight. Ground left behind is torn down after a few
    /// metres, so if the spot's ground is gone a solid slab is laid back under their feet.
    /// They return at full health, with two and a half seconds of grace, the nearby dead
    /// cleared away and the storm pushed back.
    /// </summary>
    public partial class SurvivalDirector
    {
        const float ReviveGrace = 2.5f;

        Vector2 safeSpot;
        float safeGroundY;
        bool hasSafeSpot;
        /// <summary>The firm ground just before the activity being played, and which one it is.</summary>
        Vector2 activitySpot;
        float activityGroundY;
        bool inActivity;
        ZoneKind activityKind;
        Vector2 deathSpot;
        bool adReviveUsed;

        public bool CanReviveWithAd => !adReviveUsed;

        void ResetRevive()
        {
            hasSafeSpot = false;
            inActivity = false;
            adReviveUsed = false;
        }

        /// <summary>Called as each zone begins: an activity remembers where it was entered from.</summary>
        void NoteZoneForRevive(ZoneKind kind)
        {
            inActivity = IsSetPiece(kind) && !inCampaign;
            if (!inActivity) return;
            activityKind = kind;
            if (hasSafeSpot)
            {
                activitySpot = safeSpot;
                activityGroundY = safeGroundY;
            }
            else
            {
                activitySpot = player.transform.position;
                activityGroundY = LocalGroundLevel(activitySpot.x);
            }
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
            safeGroundY = seg.topY;
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

            // Died inside an activity: back to its start. Otherwise: the last firm ground.
            Vector2 spot;
            float groundY;
            if (inActivity)
            {
                spot = activitySpot;
                groundY = activityGroundY;
            }
            else if (hasSafeSpot)
            {
                spot = safeSpot;
                groundY = safeGroundY;
            }
            else
            {
                spot = player.transform.position;
                groundY = LocalGroundLevel(spot.x);
            }
            EnsureReviveGround(spot.x, groundY);

            player.jetpackActive = false;
            player.jetpackCeilingY = float.MaxValue;
            if (inActivity && activityKind == ZoneKind.Jetpack) StartJetpack();

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

        /// <summary>
        /// Ground behind the player is recycled a few metres back, so the spot to come back to
        /// may have none left. Lay a solid slab from just behind it up to the next ground that
        /// still exists (or the next hole, such as the tower's pit), registered like any street
        /// segment - no gap is left between the spot and the activity ahead.
        /// </summary>
        void EnsureReviveGround(float x, float groundY)
        {
            if (GetSegmentAt(x, out var seg) && !seg.isGap && seg.go != null && Mathf.Abs(seg.topY - groundY) < 0.3f) return;
            // The rhythm section's floor is real but registered without an object: trust it.
            if (InCadenceSpan(x) && GetSegmentAt(x, out seg) && !seg.isGap) return;

            float from = x - 3f;
            float to = x + 8f;
            float nextStart = float.MaxValue;
            foreach (var s in segments)
                if (s.xStart > x - 0.5f && s.xStart < nextStart) nextStart = s.xStart;
            if (nextStart < float.MaxValue) to = Mathf.Max(x + 2f, Mathf.Min(nextStart, x + 80f));

            const float thickness = 1.2f;
            float width = to - from;
            var slab = CreateSolidPlatform("ReviveGround", (from + to) / 2f, groundY - thickness / 2f, width, thickness, ZoneCatalog.Get(activeZone).Ground);
            if (RunnerArt.Available) RunnerArt.DressGround(slab, width, 2.5f, RunnerArt.StyleOf(activeZone));
            props.Add(slab);
            segments.Add(new GroundSegment { xStart = from, xEnd = to, topY = groundY, isGap = false, go = slab });
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
