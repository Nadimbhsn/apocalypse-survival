using UnityEngine;

namespace Platformer.Survival
{
    /// <summary>
    /// The endless run's extras: the RECORD flag planted at the best distance, the bonuses
    /// that show up on the track now and then (see RunPowerUps), the shield's bubble, and
    /// the progress the run feeds into the daily missions (see DailyMissions).
    /// </summary>
    public partial class SurvivalDirector
    {
        // ---- record flag ---------------------------------------------------------------

        float recordToBeat;
        bool recordFlagPlaced, recordBroken;
        int lastMissionMeter;
        float nextPowerUpDistance;
        GameObject shieldBubble;

        void ResetRunExtras()
        {
            recordToBeat = SaveSystem.BestDistance;
            recordFlagPlaced = false;
            recordBroken = false;
            lastMissionMeter = 0;
            nextPowerUpDistance = Random.Range(90f, 140f);
            RunPowerUps.Reset(player.health);
            ResetBosses();
            if (player.health != null) player.health.onShieldBlocked = OnShieldBlocked;
        }

        void UpdateRunExtras()
        {
            if (inCampaign) return;
            UpdateRecordFlag();
            UpdateBossTrigger();
            UpdateShieldBubble();
            ui.SetPowerUpLine(RunPowerUps.HudLine(player.health));

            int meters = Mathf.FloorToInt(Distance);
            if (meters > lastMissionMeter)
            {
                DailyMissions.Report(MissionKind.TotalDistance, meters - lastMissionMeter);
                DailyMissions.ReportBest(MissionKind.RunDistance, meters);
                lastMissionMeter = meters;
            }
        }

        /// <summary>
        /// A flag on the track where the best run ended, planted once the terrain reaches it;
        /// running past it is celebrated once per run.
        /// </summary>
        void UpdateRecordFlag()
        {
            if (recordToBeat < 30f) return;
            float flagX = runStartX + recordToBeat;
            if (!recordFlagPlaced && frontierX > flagX + 2f)
            {
                recordFlagPlaced = true;
                PlantRecordFlag(flagX, GetGroundHeightAt(flagX));
            }
            if (!recordBroken && Distance > recordToBeat)
            {
                recordBroken = true;
                var at = player.transform.position;
                Sfx.Milestone();
                Fx.Shake(0.25f, 0.3f);
                for (int i = 0; i < 4; i++)
                {
                    var c = i % 2 == 0 ? ApogeeTheme.Gold : new Color(1f, 0.45f, 0.3f);
                    Fx.Burst(at + new Vector3(Random.Range(-1.5f, 3f), Random.Range(1.5f, 3.5f), 0f), c, 24, 4.5f, 0.1f, 0.4f);
                }
                ui.ShowBanner("NOUVEAU RECORD !", $"Tu bats ton record de {Mathf.FloorToInt(recordToBeat)} m", 2.2f);
            }
        }

        void PlantRecordFlag(float x, float groundY)
        {
            var root = new GameObject("RecordFlag");
            root.transform.SetParent(entityParent, false);
            root.transform.position = new Vector3(x, groundY, 0f);

            FlagPart(root.transform, PlaceholderVisuals.Square(Color.white), new Color(0.25f, 0.16f, 0.12f), new Vector2(0f, 1.6f), new Vector2(0.09f, 3.2f), 2);
            FlagPart(root.transform, PlaceholderVisuals.Square(Color.white), ApogeeTheme.Gold, new Vector2(0.62f, 2.85f), new Vector2(1.15f, 0.62f), 3);
            FlagPart(root.transform, PlaceholderVisuals.Circle(Color.white), ApogeeTheme.Gold, new Vector2(0f, 3.25f), new Vector2(0.2f, 0.2f), 3);

            var label = new GameObject("Label");
            label.transform.SetParent(root.transform, false);
            label.transform.localPosition = new Vector3(0.62f, 2.85f, 0f);
            var tm = label.AddComponent<TextMesh>();
            tm.text = $"RECORD\n{Mathf.FloorToInt(recordToBeat)} m";
            tm.font = UiKit.Font;
            tm.fontSize = 48;
            tm.characterSize = 0.035f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = new Color(0.25f, 0.08f, 0.05f);
            var mr = label.GetComponent<MeshRenderer>();
            mr.sharedMaterial = UiKit.Font.material;
            mr.sortingOrder = 4;

            props.Add(root);
        }

        static void FlagPart(Transform parent, Sprite sprite, Color color, Vector2 local, Vector2 size, int order)
        {
            var go = new GameObject("Part");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = order;
        }

        // ---- power-ups -------------------------------------------------------------------

        /// <summary>Paced by distance like the hordes: one bonus every 120 to 200 m of street.</summary>
        void UpdatePowerUpSpawns()
        {
            if (inCampaign || Distance < nextPowerUpDistance) return;
            if (cadenceActive || ascentActive || shaftActive || player.jetpackActive) return;
            if (TrySpawnAhead(SpawnPowerUp))
                nextPowerUpDistance = Distance + Random.Range(120f, 200f);
        }

        void SpawnPowerUp(float x)
        {
            var kind = (PowerUpKind)Random.Range(0, 4);
            var go = new GameObject($"PowerUp_{kind}");
            go.transform.SetParent(entityParent, false);
            go.transform.position = new Vector3(x, GetGroundHeightAt(x) + 0.95f, 0f);
            go.transform.localScale = Vector3.one * 0.75f;

            var glow = new GameObject("Glow");
            glow.transform.SetParent(go.transform, false);
            glow.transform.localScale = Vector3.one * 1.5f;
            var gsr = glow.AddComponent<SpriteRenderer>();
            gsr.sprite = PlaceholderVisuals.Circle(Color.white);
            var t = RunPowerUps.Tint(kind);
            gsr.color = new Color(t.r, t.g, t.b, 0.3f);
            gsr.sortingOrder = 3;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = GameIcons.PowerUp(kind);
            sr.sortingOrder = 4;
            var col = go.AddComponent<CircleCollider2D>();
            col.isTrigger = true;
            col.radius = 0.6f;

            var pickup = go.AddComponent<Pickup>();
            pickup.type = PickupType.PowerUp;
            pickup.powerUp = kind;
            pickups.Add(pickup);
        }

        void OnShieldBlocked()
        {
            var at = player.transform.position;
            Sfx.Bounce();
            Fx.Burst(at, RunPowerUps.Tint(PowerUpKind.Shield), 24, 4f, 0.1f, 0f);
            Fx.Text(at + Vector3.up * 1.2f, "BOUCLIER !", RunPowerUps.Tint(PowerUpKind.Shield), 1f);
            // A moment of grace so one swarm cannot take the shield and a heart in the same breath.
            if (player.health != null) player.health.invulnerableUntil = Time.time + 0.8f;
        }

        /// <summary>A pale bubble around the player while the shield holds.</summary>
        void UpdateShieldBubble()
        {
            bool on = player.health != null && player.health.shieldHits > 0;
            if (on && shieldBubble == null)
            {
                shieldBubble = new GameObject("ShieldBubble");
                shieldBubble.transform.SetParent(player.transform, false);
                var off = player.collider2d != null ? player.collider2d.offset : Vector2.zero;
                shieldBubble.transform.localPosition = off;
                var sr = shieldBubble.AddComponent<SpriteRenderer>();
                sr.sprite = PlaceholderVisuals.RimCircle(RunPowerUps.Tint(PowerUpKind.Shield));
                sr.color = new Color(1f, 1f, 1f, 0.35f);
                sr.sortingOrder = 12;
            }
            else if (!on && shieldBubble != null)
            {
                Destroy(shieldBubble);
                shieldBubble = null;
            }
            if (shieldBubble != null)
                shieldBubble.transform.localScale = Vector3.one * (1.25f + Mathf.Sin(Time.time * 5f) * 0.05f);
        }
    }
}
