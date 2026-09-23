using UnityEngine;
using Platformer.Mechanics;

namespace Platformer.Survival
{
    /// <summary>
    /// The storm that chases the player during the Déferlante sector: a wall of glowing
    /// cloud and crimson leaves sweeping in from behind. It travels slightly slower than
    /// the player's top speed, so running keeps you ahead, but any stumble (a missed jump,
    /// a fight, the brambles) lets it catch up. It is always on screen: however far ahead
    /// the player gets, its front is held just inside the left edge of the view, so the
    /// threat is never out of sight. Once it has swallowed the player it slows right down,
    /// so running on gets them back out - being inside costs health every fraction of a
    /// second. It never moves backwards.
    /// </summary>
    public class ChaseWall : MonoBehaviour
    {
        public float speedFactor = 0.92f;
        /// <summary>Its speed while the player is inside it: slow enough to run back out.</summary>
        public float overtakenSpeedFactor = 0.5f;
        /// <summary>How far inside the left edge of the view its front is held, as a share of the view's width.</summary>
        public float screenMargin = 0.12f;
        /// <summary>The leash never drags it closer than this behind the player.</summary>
        public float minGapBehindPlayer = 1.2f;
        public float damageInterval = 0.7f;
        public int damage = 1;

        SurvivalDirector director;
        PlayerController player;
        SpriteRenderer front;
        ParticleSystem debris;
        float nextDamageTime;
        bool dissipating;

        public static ChaseWall Create(Transform parent, SurvivalDirector director, PlayerController player, float startX)
        {
            var go = new GameObject("ChaseWall");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(startX, 0f, 0.5f);

            var wall = go.AddComponent<ChaseWall>();
            wall.director = director;
            wall.player = player;

            // Body of the storm: a wide slab fading out toward its leading edge. The fade
            // sprite is tiny (0.04 x 1.28 units) and turned a quarter to the right, so its
            // local x is the slab's height and its local y the slab's depth - scaled to
            // 40 m tall and 32 m deep. (Scaled as if it were one unit square, it used to
            // shrink to a 1.3 m band: a dark line drawn across the screen at the player's height.)
            var bodyGo = new GameObject("Body");
            bodyGo.transform.SetParent(go.transform, false);
            bodyGo.transform.localPosition = new Vector3(-16f, 0f, 0f);
            var fadeSize = ApogeeTheme.VerticalFade.bounds.size;
            bodyGo.transform.localScale = new Vector3(40f / fadeSize.x, 32f / fadeSize.y, 1f);
            var body = bodyGo.AddComponent<SpriteRenderer>();
            body.sprite = ApogeeTheme.VerticalFade;
            body.color = new Color(0.30f, 0.05f, 0.10f, 0.72f);
            body.sortingOrder = 7;
            bodyGo.transform.localRotation = Quaternion.Euler(0f, 0f, -90f); // fade points right

            var frontGo = new GameObject("Front");
            frontGo.transform.SetParent(go.transform, false);
            frontGo.transform.localScale = new Vector3(1.2f, 90f, 1f);
            wall.front = frontGo.AddComponent<SpriteRenderer>();
            wall.front.sprite = PlaceholderVisuals.Square(Color.white);
            wall.front.color = new Color(0.75f, 0.18f, 0.12f, 0.5f);
            wall.front.sortingOrder = 8;

            wall.debris = wall.CreateDebris(go.transform);
            return wall;
        }

        ParticleSystem CreateDebris(Transform parent)
        {
            var go = new GameObject("StormDebris");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.startLifetime = 1.6f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.3f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.85f, 0.18f, 0.10f), new Color(1f, 0.55f, 0.2f));
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 140;

            var emission = ps.emission;
            emission.rateOverTime = 45f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(3f, 26f, 0.1f);

            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-6f, 6f);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = new Material(Shader.Find("Sprites/Default")) { mainTexture = ApogeeTheme.LeafTexture };
            renderer.sortingOrder = 9;
            return ps;
        }

        /// <summary>Stops chasing and fades away (the sector is over).</summary>
        public void Dissipate()
        {
            if (dissipating) return;
            dissipating = true;
            var emission = debris.emission;
            emission.rateOverTime = 0f;
            Destroy(gameObject, 2f);
        }

        void Update()
        {
            if (player == null || director == null || !director.IsRunning) return;

            var pos = transform.position;
            pos.y = player.transform.position.y;   // always fills the view vertically

            if (!dissipating)
            {
                float playerX = player.transform.position.x;
                bool overtaken = playerX <= pos.x;
                float speed = director.CurrentRunSpeed * (overtaken ? overtakenSpeedFactor : speedFactor);
                pos.x += speed * Time.deltaTime;

                // Never out of sight: the front is held just inside the left edge of the view.
                var cam = Camera.main;
                if (cam != null && cam.orthographic)
                {
                    float halfWidth = cam.orthographicSize * cam.aspect;
                    float leftEdge = cam.transform.position.x - halfWidth;
                    pos.x = Mathf.Max(pos.x, Mathf.Min(leftEdge + halfWidth * 2f * screenMargin, playerX - minGapBehindPlayer));
                }
                transform.position = pos;
                Engulf();
            }
            else
            {
                pos.x -= 6f * Time.deltaTime;
                transform.position = pos;
                var c = front.color;
                c.a = Mathf.MoveTowards(c.a, 0f, Time.deltaTime);
                front.color = c;
            }
        }

        void Engulf()
        {
            if (player.health == null || !player.health.IsAlive) return;
            if (player.transform.position.x > transform.position.x) return;   // still ahead of it
            if (Time.time < nextDamageTime) return;

            nextDamageTime = Time.time + damageInterval;
            player.health.Decrement(UpgradeManager.ReduceDamageToPlayer(damage));
            Fx.Shake(0.35f, 0.25f);
            Fx.Burst(player.transform.position, new Color(0.85f, 0.2f, 0.12f), 10, 3f, 0.12f);
            Sfx.Hit();
        }
    }
}
