using UnityEngine;
using Platformer.Gameplay;
using Platformer.Mechanics;

namespace Platformer.Survival
{
    /// <summary>
    /// Visual feedback on the player's movement: a dust puff when landing and taking off,
    /// and a faint dust trail while sprinting on the ground. Listens to the sample's
    /// PlayerJumped / PlayerLanded events, so no changes to the movement code are needed.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class PlayerJuice : MonoBehaviour
    {
        static readonly Color DustColor = new Color(0.62f, 0.56f, 0.48f, 0.8f);

        PlayerController player;
        ParticleSystem trail;
        ParticleSystem flame;

        void Awake()
        {
            player = GetComponent<PlayerController>();
            BuildTrail();
            BuildFlame();
        }

        void OnEnable()
        {
            PlayerLanded.OnExecute += OnLanded;
            PlayerJumped.OnExecute += OnJumped;
        }

        void OnDisable()
        {
            PlayerLanded.OnExecute -= OnLanded;
            PlayerJumped.OnExecute -= OnJumped;
        }

        Vector3 Feet => new Vector3(player.Bounds.center.x, player.Bounds.min.y, 0f);

        void OnLanded(PlayerLanded ev)
        {
            if (ev.player != player) return;
            Fx.Burst(Feet, DustColor, 10, 1.8f, 0.09f, -0.05f);
        }

        void OnJumped(PlayerJumped ev)
        {
            if (ev.player != player) return;
            Fx.Burst(Feet, DustColor, 6, 1.2f, 0.07f, -0.05f);
        }

        void BuildTrail()
        {
            var go = new GameObject("DustTrail");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, -0.4f, 0f);
            trail = go.AddComponent<ParticleSystem>();
            var main = trail.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.1f);
            main.startColor = new Color(DustColor.r, DustColor.g, DustColor.b, 0.5f);
            main.gravityModifier = -0.05f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 60;

            var emission = trail.emission;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = 1.5f;

            var shape = trail.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.08f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = new Material(Shader.Find("Sprites/Default"));
            renderer.sortingOrder = 1;
        }

        void BuildFlame()
        {
            var go = new GameObject("JetFlame");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(-0.15f, -0.45f, 0f);
            flame = go.AddComponent<ParticleSystem>();
            var main = flame.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.35f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.22f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.75f, 0.2f), new Color(1f, 0.3f, 0.1f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 80;

            var emission = flame.emission;
            emission.rateOverTime = 0f;

            var shape = flame.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 12f;
            shape.radius = 0.06f;
            shape.rotation = new Vector3(90f, 0f, 0f); // point down

            var col = flame.colorOverLifetime;
            col.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.4f, 0.4f, 0.4f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = gradient;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = new Material(Shader.Find("Sprites/Default"));
            renderer.sortingOrder = 2;
        }

        void Update()
        {
            if (trail != null)
            {
                var emission = trail.emission;
                bool sprinting = player.IsGrounded && Mathf.Abs(player.velocity.x) > 2f;
                emission.rateOverDistanceMultiplier = sprinting ? 1.5f : 0f;
            }
            if (flame != null)
            {
                var emission = flame.emission;
                emission.rateOverTimeMultiplier = player.JetpackThrusting ? 90f : 0f;
            }
        }
    }
}
