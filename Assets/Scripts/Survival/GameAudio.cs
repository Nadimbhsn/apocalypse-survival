using System.Collections.Generic;
using UnityEngine;

namespace Platformer.Survival
{
    /// <summary>
    /// The two volume knobs of the pause menu, kept between sessions: music (every looping
    /// source - the scene's soundtrack, the rhythm section's track) and effects (everything
    /// else - shots, coins, jumps). Each source keeps the volume its own code gave it, scaled
    /// by the knob, so a quiet sound stays quieter than a loud one.
    /// </summary>
    public class GameAudio : MonoBehaviour
    {
        const string MusicKey = "vol_music", EffectsKey = "vol_effects";

        static GameAudio instance;
        static float music = -1f, effects = -1f;
        readonly Dictionary<AudioSource, float> baseVolume = new();
        float scanTimer;

        public static float Music
        {
            get { if (music < 0f) music = PlayerPrefs.GetFloat(MusicKey, 1f); return music; }
            set { music = Mathf.Clamp01(value); PlayerPrefs.SetFloat(MusicKey, music); Ensure().Apply(); }
        }

        public static float Effects
        {
            get { if (effects < 0f) effects = PlayerPrefs.GetFloat(EffectsKey, 1f); return effects; }
            set { effects = Mathf.Clamp01(value); PlayerPrefs.SetFloat(EffectsKey, effects); Ensure().Apply(); }
        }

        /// <summary>Saves the knobs once the player lets go of a slider.</summary>
        public static void Save() => PlayerPrefs.Save();

        public static GameAudio Ensure()
        {
            if (instance != null) return instance;
            var go = new GameObject("GameAudio");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<GameAudio>();
            return instance;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init() => Ensure();

        void Update()
        {
            // New sources appear all the time (one-shot clips, a section's music): look again often.
            scanTimer -= Time.unscaledDeltaTime;
            if (scanTimer > 0f) return;
            scanTimer = 0.1f;
            Apply();
        }

        void Apply()
        {
            foreach (var src in FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
            {
                if (!baseVolume.TryGetValue(src, out float v))
                {
                    v = src.volume;
                    baseVolume[src] = v;
                }
                src.volume = v * (src.loop ? Music : Effects);
            }
            // Forget destroyed sources so the table does not grow forever.
            if (baseVolume.Count > 64)
            {
                var dead = new List<AudioSource>();
                foreach (var k in baseVolume.Keys) if (k == null) dead.Add(k);
                foreach (var k in dead) baseVolume.Remove(k);
            }
        }
    }
}
