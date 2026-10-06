using UnityEngine;

namespace Platformer.Survival
{
    /// <summary>
    /// L'ÉLAN: the runner's flow meter. Playing well fills it - coins, kills, gaps cleared
    /// in one bound, every hundred metres - and it multiplies the coins picked up (x2 from
    /// a quarter, up to x4). Idling lets it drain; a single hit breaks it. Full, it sets
    /// off L'ENVOL: five seconds untouchable and faster, coins flying in, the dead
    /// shattering on contact, a golden wake behind the hero. Endless runs only.
    /// </summary>
    public partial class SurvivalDirector
    {
        public const float ElanMax = 100f;
        const float EnvolDuration = 5f, EnvolSpeedBoost = 1.25f;

        float elan, elanIdleSince, envolUntil = -1f;
        int lastHp = -1;
        bool wasAirborne;
        float takeoffX, takeoffTime;
        ParticleSystem envolWake;

        public float Elan01 => elan / ElanMax;
        public bool EnvolActive => Time.time < envolUntil;
        /// <summary>Coins are worth this many times their face value right now.</summary>
        public int ElanMultiplier => EnvolActive ? 5 : 1 + Mathf.Min(3, Mathf.FloorToInt(elan / 25f));

        void ResetElan()
        {
            elan = 0f;
            elanIdleSince = Time.time;
            envolUntil = -1f;
            lastHp = -1;
            wasAirborne = false;
            if (envolWake != null) { var e = envolWake.emission; e.rateOverDistanceMultiplier = 0f; }
        }

        /// <summary>Feeds the meter (nothing during the Envol itself, which empties it as it burns).</summary>
        void AddElan(float amount, string cheer = null)
        {
            if (inCampaign || !running || EnvolActive) return;
            int before = ElanMultiplier;
            elan = Mathf.Min(ElanMax, elan + amount);
            elanIdleSince = Time.time;
            if (cheer != null) Fx.Text(player.transform.position + Vector3.up * 1.3f, cheer, ApogeeTheme.Gold, 0.8f);
            if (ElanMultiplier > before && elan < ElanMax)
            {
                Fx.Text(player.transform.position + Vector3.up * 1.7f, $"ÉLAN x{ElanMultiplier}", new Color(1f, 0.85f, 0.4f), 1.1f);
                Sfx.Milestone();
            }
            if (elan >= ElanMax) StartEnvol();
        }

        void StartEnvol()
        {
            envolUntil = Time.time + EnvolDuration;
            if (player.health != null) player.health.invulnerableUntil = Mathf.Max(player.health.invulnerableUntil, envolUntil);
            ui.ShowBanner("ENVOL !", "Intouchable : fonce !", 1.4f);
            Fx.Burst(player.transform.position, ApogeeTheme.Gold, 40, 6f, 0.14f, 0f);
            Fx.Shake(0.35f, 0.3f);
            Sfx.Spring();
            EnsureEnvolWake();
        }

        void UpdateElan()
        {
            if (player == null || player.health == null) return;
            UpdateDashKills();
            if (inCampaign) return;

            // A hit breaks the meter.
            int hp = Mathf.RoundToInt(player.health.NormalizedHP * player.health.maxHP);
            if (lastHp >= 0 && hp < lastHp && !EnvolActive && elan > 0f)
            {
                elan = 0f;
                Fx.Text(player.transform.position + Vector3.up * 1.4f, "ÉLAN BRISÉ", new Color(0.95f, 0.4f, 0.3f), 0.9f);
            }
            lastHp = hp;

            // A gap cleared in a single bound.
            bool airborne = !player.IsGrounded;
            if (airborne && !wasAirborne) { takeoffX = player.transform.position.x; takeoffTime = Time.time; }
            if (!airborne && wasAirborne && Time.time - takeoffTime > 0.35f && CrossedGap(takeoffX, player.transform.position.x))
                AddElan(9f, "BOND !");
            wasAirborne = airborne;

            if (EnvolActive)
            {
                // The meter burns down over the Envol.
                elan = ElanMax * Mathf.Clamp01((envolUntil - Time.time) / EnvolDuration);
                foreach (var z in Zombie.Active)
                {
                    if (z == null || !z.IsAlive) continue;
                    if (((Vector2)z.transform.position - (Vector2)player.transform.position).sqrMagnitude < 1.3f * 1.3f)
                    {
                        z.TakeDamage(999);
                        Fx.Shake(0.15f, 0.12f);
                        break; // the set changes as it dies
                    }
                }
            }
            else
            {
                if (envolUntil > 0f) { envolUntil = -1f; elan = 0f; }
                // Idling drains it.
                if (Time.time - elanIdleSince > 2.5f) elan = Mathf.Max(0f, elan - 7f * Time.deltaTime);
            }

            if (envolWake != null)
            {
                var e = envolWake.emission;
                e.rateOverDistanceMultiplier = EnvolActive ? 10f : 0f;
            }
            ui.UpdateElan(Elan01, ElanMultiplier, EnvolActive);
        }

        /// <summary>La Ruée: a moment untouchable, and whatever it goes through dies.</summary>
        void OnPlayerDash(Platformer.Mechanics.PlayerController p)
        {
            if (!running) return;
            if (p.health != null) p.health.invulnerableUntil = Mathf.Max(p.health.invulnerableUntil, Time.time + p.dashDuration + 0.1f);
            Fx.Burst(p.transform.position, new Color(1f, 0.85f, 0.55f), 12, 3f, 0.08f, 0f);
            Fx.Shake(0.08f, 0.1f);
            Sfx.Attack();
        }

        void UpdateDashKills()
        {
            if (!player.IsDashing) return;
            foreach (var z in Zombie.Active)
            {
                if (z == null || !z.IsAlive) continue;
                if (((Vector2)z.transform.position - (Vector2)player.transform.position).sqrMagnitude > 1.25f * 1.25f) continue;
                Fx.Text(z.transform.position + Vector3.up * 1.0f, "RUÉE !", ApogeeTheme.Gold, 0.8f);
                Fx.Hitstop(0.05f, 0.2f);
                z.TakeDamage(z.kind == ZombieKind.Brute ? 3 : 999);
                break;
            }
        }

        bool CrossedGap(float fromX, float toX)
        {
            if (toX - fromX < 0.8f) return false;
            foreach (var s in segments)
                if (s.isGap && s.xStart >= fromX - 0.2f && s.xEnd <= toX + 0.2f && s.xEnd - s.xStart > 0.8f) return true;
            return false;
        }

        /// <summary>Coins are worth more with Élan: the extra is banked here, on top of the pickup's own.</summary>
        void ElanOnPickup(Pickup pickup)
        {
            if (inCampaign || pickup == null) return;
            if (pickup.type == PickupType.Coin)
            {
                int extra = ElanMultiplier - 1;
                if (extra > 0)
                {
                    SaveSystem.AddCoins(extra);
                    Fx.Text(pickup.transform.position + Vector3.up * 0.4f, $"x{ElanMultiplier}", new Color(1f, 0.85f, 0.35f), 0.7f);
                }
                AddElan(3.5f);
            }
            else if (pickup.type != PickupType.Ammo) AddElan(6f);
        }

        void EnsureEnvolWake()
        {
            if (envolWake != null) return;
            var go = new GameObject("EnvolWake");
            go.transform.SetParent(player.transform, false);
            go.transform.localPosition = new Vector3(0f, 0.2f, 0f);
            envolWake = go.AddComponent<ParticleSystem>();
            var main = envolWake.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.35f), new Color(1f, 0.55f, 0.2f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 200;
            var emission = envolWake.emission;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = 1f;
            emission.rateOverDistanceMultiplier = 0f;
            var shape = envolWake.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.35f;
            var col = envolWake.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.material = new Material(Shader.Find("Sprites/Default"));
            r.sortingOrder = 2;
        }
    }
}
