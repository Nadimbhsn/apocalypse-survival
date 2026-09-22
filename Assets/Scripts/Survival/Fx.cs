using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Cinemachine;

namespace Platformer.Survival
{
    /// <summary>
    /// Game-feel toolbox shared by every mode: one-shot particle bursts, floating world
    /// text ("+1", "KILL"), hit-stop (a split-second slow-motion on impact), and camera
    /// shake driven through the Cinemachine composer's target offset so it composes with
    /// the follow logic instead of fighting it. Everything is generated at runtime - no
    /// prefabs or assets needed.
    /// </summary>
    public class Fx : MonoBehaviour
    {
        public static Fx Instance { get; private set; }

        CinemachinePositionComposer composer;
        Vector3 composerBaseOffset;
        Coroutine shakeRoutine, hitstopRoutine;
        Material particleMaterial;

        static Fx Ensure()
        {
            if (Instance == null)
            {
                var go = new GameObject("Fx");
                Instance = go.AddComponent<Fx>();
                Instance.particleMaterial = new Material(Shader.Find("Sprites/Default"));
            }
            return Instance;
        }

        /// <summary>Hooks the camera composer used for shakes; baseOffset is where the camera rests between shakes.</summary>
        public static void BindCamera(CinemachinePositionComposer positionComposer, Vector3 baseOffset)
        {
            var fx = Ensure();
            fx.composer = positionComposer;
            fx.composerBaseOffset = baseOffset;
            if (positionComposer != null) positionComposer.TargetOffset = baseOffset;
        }

        /// <summary>
        /// Moves where the camera rests between shakes (the rhythm section pushes the player
        /// to the left of the screen to show what is coming).
        /// </summary>
        public static void SetCameraRestOffset(Vector3 offset)
        {
            var fx = Ensure();
            fx.composerBaseOffset = offset;
            if (fx.composer != null && fx.shakeRoutine == null) fx.composer.TargetOffset = offset;
        }

        public static void Burst(Vector3 position, Color color, int count, float speed = 3f, float size = 0.12f, float gravity = 0.8f)
        {
            var fx = Ensure();
            var go = new GameObject("Burst");
            go.transform.position = position;
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size * 1.4f);
            main.startColor = color;
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 128;

            var emission = ps.emission;
            emission.enabled = false;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.12f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = gradient;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = fx.particleMaterial;
            renderer.sortingOrder = 9;

            ps.Emit(count);
            Destroy(go, 1.5f);
        }

        public static void Text(Vector3 position, string text, Color color, float scale = 1f)
        {
            Ensure();
            FloatingText.Spawn(position, text, color, scale);
        }

        /// <summary>Slows time to a crawl for a moment (real-time duration); does nothing if the game is paused.</summary>
        public static void Hitstop(float duration = 0.09f, float timeScale = 0.15f)
        {
            var fx = Ensure();
            if (Time.timeScale <= 0f) return;
            if (fx.hitstopRoutine != null) fx.StopCoroutine(fx.hitstopRoutine);
            fx.hitstopRoutine = fx.StartCoroutine(fx.HitstopRoutine(duration, timeScale));
        }

        IEnumerator HitstopRoutine(float duration, float timeScale)
        {
            Time.timeScale = timeScale;
            yield return new WaitForSecondsRealtime(duration);
            // Only restore if nobody else (game over -> 0) changed it meanwhile.
            if (Mathf.Approximately(Time.timeScale, timeScale)) Time.timeScale = 1f;
            hitstopRoutine = null;
        }

        public static void Shake(float amplitude = 0.3f, float duration = 0.25f)
        {
            var fx = Ensure();
            if (fx.composer == null) return;
            if (fx.shakeRoutine != null) fx.StopCoroutine(fx.shakeRoutine);
            fx.shakeRoutine = fx.StartCoroutine(fx.ShakeRoutine(amplitude, duration));
        }

        IEnumerator ShakeRoutine(float amplitude, float duration)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float p = 1f - t / duration;
                composer.TargetOffset = composerBaseOffset + (Vector3)(Random.insideUnitCircle * amplitude * p);
                yield return null;
            }
            composer.TargetOffset = composerBaseOffset;
            shakeRoutine = null;
        }
    }

    /// <summary>World-space text that rises and fades out, for pickups, kills and milestones.</summary>
    public class FloatingText : MonoBehaviour
    {
        TextMesh mesh;
        float life;
        Color baseColor;
        const float Duration = 0.85f;

        public static void Spawn(Vector3 position, string text, Color color, float scale)
        {
            var go = new GameObject("FloatingText");
            go.transform.position = position + new Vector3(Random.Range(-0.15f, 0.15f), 0.3f, 0f);
            var mesh = go.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.font = UiKit.Font;
            mesh.fontSize = 48;
            mesh.characterSize = 0.06f * scale;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.color = color;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = UiKit.Font.material;
            renderer.sortingOrder = 20;
            var ft = go.AddComponent<FloatingText>();
            ft.mesh = mesh;
            ft.baseColor = color;
        }

        void Update()
        {
            life += Time.deltaTime;
            float p = life / Duration;
            transform.position += Vector3.up * (1.6f * (1f - p) * Time.deltaTime);
            var c = baseColor;
            c.a = 1f - Mathf.Clamp01((p - 0.5f) * 2f);
            mesh.color = c;
            if (life >= Duration) Destroy(gameObject);
        }
    }

    /// <summary>
    /// Tiny procedural sound effects (sine sweeps and noise bursts synthesized once into
    /// AudioClips) so every action has audible feedback without any audio assets.
    /// </summary>
    public static class Sfx
    {
        const int SampleRate = 44100;
        static AudioSource source;
        static readonly Dictionary<string, AudioClip> cache = new();

        static AudioSource Source
        {
            get
            {
                if (source == null)
                {
                    var go = new GameObject("Sfx");
                    source = go.AddComponent<AudioSource>();
                    source.spatialBlend = 0f;
                    source.playOnAwake = false;
                }
                return source;
            }
        }

        public static void Coin() => Play(Sweep("coin", 0.09f, 1000f, 1500f), 0.3f);
        public static void Medkit() => Play(Sweep("medkit", 0.2f, 500f, 950f), 0.35f);
        public static void Material() => Play(Sweep("material", 0.09f, 600f, 800f), 0.3f);
        public static void Hit() => Play(Noise("hit", 0.16f), 0.45f);
        public static void Kill() => Play(Sweep("kill", 0.16f, 240f, 70f), 0.4f);
        public static void Bounce() => Play(Sweep("bounce", 0.14f, 300f, 850f), 0.35f);
        public static void Spring() => Play(Sweep("spring", 0.22f, 300f, 1300f), 0.4f);
        public static void Milestone() => Play(Sweep("milestone", 0.3f, 660f, 990f), 0.4f);
        public static void Death() => Play(Sweep("death", 0.55f, 320f, 55f), 0.55f);
        public static void Merge(int tier) => Play(Sweep($"merge{tier}", 0.13f, 380f + tier * 50f, 700f + tier * 90f), 0.35f);
        public static void Drop() => Play(Noise("drop", 0.05f), 0.15f);
        public static void Shoot() => Play(Sweep("shoot", 0.07f, 1100f, 350f), 0.25f);
        public static void Attack() => Play(Noise("attack", 0.12f), 0.35f);
        public static void Heal() => Play(Sweep("heal", 0.25f, 440f, 880f), 0.35f);

        static void Play(AudioClip clip, float volume)
        {
            if (clip != null) Source.PlayOneShot(clip, volume);
        }

        static AudioClip Sweep(string name, float duration, float fromHz, float toHz)
        {
            if (cache.TryGetValue(name, out var existing)) return existing;
            int samples = Mathf.CeilToInt(duration * SampleRate);
            var data = new float[samples];
            float phase = 0f;
            for (int i = 0; i < samples; i++)
            {
                float p = i / (float)samples;
                float freq = Mathf.Lerp(fromHz, toHz, p);
                phase += 2f * Mathf.PI * freq / SampleRate;
                float env = Mathf.Min(1f, i / (SampleRate * 0.005f)) * (1f - p) * (1f - p);
                data[i] = Mathf.Sin(phase) * env * 0.8f;
            }
            return Build(name, data);
        }

        static AudioClip Noise(string name, float duration)
        {
            if (cache.TryGetValue(name, out var existing)) return existing;
            int samples = Mathf.CeilToInt(duration * SampleRate);
            var data = new float[samples];
            var rng = new System.Random(name.GetHashCode());
            float last = 0f;
            for (int i = 0; i < samples; i++)
            {
                float p = i / (float)samples;
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                last = Mathf.Lerp(last, white, 0.35f); // crude low-pass so it thuds rather than hisses
                data[i] = last * (1f - p) * (1f - p) * 0.9f;
            }
            return Build(name, data);
        }

        static AudioClip Build(string name, float[] data)
        {
            var clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            cache[name] = clip;
            return clip;
        }
    }
}
