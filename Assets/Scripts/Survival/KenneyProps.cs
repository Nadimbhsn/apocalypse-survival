using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Platformer.Survival
{
    public enum PropKit { Graveyard, Castle }

    /// <summary>
    /// Depth layer a prop is rendered in. Each has its own material: more fog (blend
    /// toward the sky) and desaturation the further back it is, so decor never competes
    /// with the gameplay plane.
    /// </summary>
    public enum PropLayer { Near, Rampart, Mid, Far, Tower, Character }

    /// <summary>
    /// Runtime access to the Kenney Graveyard / Castle kits (CC0, www.kenney.nl) copied as
    /// OBJ into Assets/Resources/Kenney. Spawns a model as a small rig:
    ///   rig (position, world pitch) -> yaw pivot (facing, scale) -> imported model
    /// with the model's bottom-center sitting exactly on the rig origin, so callers place
    /// props by their base. All renderers get the Survival/KenneyProp material of the
    /// requested layer (one shared material per kit and layer, SRP-batcher friendly).
    ///
    /// Import differences (unit scale, axis handedness) are detected at startup from two
    /// reference models with known asymmetric dimensions, so "yaw 0" always means "front
    /// facing the camera" and scale 1 always means "1 kit unit = 1 world unit".
    /// If the assets or the shader are missing, Available is false and callers fall back
    /// to the old flat placeholder decor.
    /// </summary>
    public static class KenneyProps
    {
        const string Root = "Kenney/";
        static readonly string[] KitFolder = { "Graveyard", "Castle" };

        struct LayerStyle
        {
            public float fog, desat, tint;
            public LayerStyle(float fog, float desat, float tint) { this.fog = fog; this.desat = desat; this.tint = tint; }
        }

        static readonly LayerStyle[] Styles =
        {
            new LayerStyle(0.12f, 0.18f, 0.97f), // Near
            new LayerStyle(0.22f, 0.18f, 0.88f), // Rampart
            new LayerStyle(0.40f, 0.22f, 0.88f), // Mid
            new LayerStyle(0.68f, 0.28f, 0.85f), // Far (melts into the painted sky)
            new LayerStyle(0.45f, 0.20f, 0.72f), // Tower (behind the bounce platforms: dark so they pop)
            new LayerStyle(0.00f, 0.10f, 1.05f), // Character
        };

        class Entry
        {
            public GameObject prefab;
            /// <summary>Bottom-center of the model in imported units, relative to its parent.</summary>
            public Vector3 anchor;
            /// <summary>Bounding size in kit units.</summary>
            public Vector3 size;
        }

        static bool initialized;
        static bool available;
        static Shader shader;
        static readonly Texture2D[] colormaps = new Texture2D[2];
        /// <summary>Apogée recolor of each kit's palette (foliage crimson, roofs red, stone warm) for scenery; characters keep the original.</summary>
        static readonly Texture2D[] apogeeColormaps = new Texture2D[2];
        static readonly float[] kitScale = { 1f, 1f };
        static readonly float[] frontYaw = { 180f, 180f };
        static readonly Dictionary<string, Entry> entries = new();
        static readonly Dictionary<int, Material> materials = new();
        static MaterialPropertyBlock block;

        public static readonly int FogColorId = Shader.PropertyToID("_SurvivalFogColor");
        static readonly int FlashId = Shader.PropertyToID("_Flash");

        public static bool Available
        {
            get
            {
                if (!initialized) Init();
                return available;
            }
        }

        static void Init()
        {
            initialized = true;
            shader = Shader.Find("Survival/KenneyProp");
            for (int k = 0; k < 2; k++)
            {
                colormaps[k] = Resources.Load<Texture2D>(Root + KitFolder[k] + "/Textures/colormap");
                apogeeColormaps[k] = Resources.Load<Texture2D>(Root + KitFolder[k] + "/Textures/colormap_apogee");
            }

            available = shader != null && colormaps[0] != null && colormaps[1] != null
                && Resources.Load<GameObject>(Root + "Graveyard/iron-fence") != null
                && Resources.Load<GameObject>(Root + "Castle/flag-pennant") != null;
            if (!available)
            {
                Debug.LogWarning("[KenneyProps] Kenney assets or Survival/KenneyProp shader missing - using flat placeholder decor.");
                return;
            }

            // Reference models: native height and native depth-center (from the OBJ files).
            // iron-fence sits on the -Z edge of its tile, flag-pennant waves toward -Z.
            Calibrate(PropKit.Graveyard, "iron-fence", 0.88f, -0.325f);
            Calibrate(PropKit.Castle, "flag-pennant", 0.87f, -0.235f);
        }

        static void Calibrate(PropKit kit, string name, float nativeHeight, float nativeCenterZ)
        {
            var prefab = Resources.Load<GameObject>(Root + KitFolder[(int)kit] + "/" + name);
            var probe = Object.Instantiate(prefab);
            var b = MeasureWorldBounds(probe);
            Object.Destroy(probe);
            if (b.size.y <= 0.0001f) return;

            kitScale[(int)kit] = nativeHeight / b.size.y;
            float centerZ = (b.center.z - prefab.transform.localPosition.z) * kitScale[(int)kit];
            // Kenney models face +Z in their source files. If the importer kept Z, the
            // front points away from the camera (which looks down +Z) and needs a half turn.
            frontYaw[(int)kit] = Mathf.Sign(centerZ) == Mathf.Sign(nativeCenterZ) ? 180f : 0f;
        }

        static Bounds MeasureWorldBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b;
        }

        static Entry Get(PropKit kit, string name)
        {
            string key = KitFolder[(int)kit] + "/" + name;
            if (entries.TryGetValue(key, out var cached)) return cached;

            var prefab = Resources.Load<GameObject>(Root + key);
            if (prefab == null)
            {
                Debug.LogWarning($"[KenneyProps] Missing model {key}");
                entries[key] = null;
                return null;
            }

            var probe = Object.Instantiate(prefab);
            var b = MeasureWorldBounds(probe);
            Object.Destroy(probe);

            var entry = new Entry
            {
                prefab = prefab,
                anchor = new Vector3(b.center.x, b.min.y, b.center.z),
                size = b.size * kitScale[(int)kit],
            };
            entries[key] = entry;
            return entry;
        }

        /// <summary>Loads and measures every model up front (at the hub) so the run never hitches on a first use.</summary>
        public static void Prewarm()
        {
            if (!Available) return;
            for (int k = 0; k < KitFolder.Length; k++)
                foreach (var prefab in Resources.LoadAll<GameObject>(Root + KitFolder[k]))
                    Get((PropKit)k, prefab.name);
        }

        /// <summary>Size of a model in kit units (width, height, depth as authored, before yaw).</summary>
        public static Vector3 Size(PropKit kit, string name)
        {
            if (!Available) return Vector3.one;
            var e = Get(kit, name);
            return e != null ? e.size : Vector3.one;
        }

        /// <summary>
        /// Spawns a model with its bottom-center at localPosition under parent. yaw is
        /// relative to "front facing the camera"; pitch tilts the whole rig about the world
        /// X axis (negative = top leans toward the camera, showing more of the top faces).
        /// stretch scales along the model's own axes on top of the uniform scale.
        /// </summary>
        public static Transform Spawn(PropKit kit, string name, Transform parent, Vector3 localPosition, float scale,
            PropLayer layer, float yaw = 0f, float pitch = 0f, Vector3? stretch = null)
        {
            if (!Available) return null;
            var e = Get(kit, name);
            if (e == null) return null;

            var rig = new GameObject(name).transform;
            rig.SetParent(parent, false);
            rig.localPosition = localPosition;
            rig.localRotation = Quaternion.Euler(pitch, 0f, 0f);

            var pivot = new GameObject("pivot").transform;
            pivot.SetParent(rig, false);
            pivot.localRotation = Quaternion.Euler(0f, frontYaw[(int)kit] + yaw, 0f);
            pivot.localScale = (stretch ?? Vector3.one) * (scale * kitScale[(int)kit]);

            var model = Object.Instantiate(e.prefab, pivot, false);
            model.transform.localPosition = e.prefab.transform.localPosition - e.anchor;
            ApplyMaterial(model, kit, layer);
            return rig;
        }

        static void ApplyMaterial(GameObject model, PropKit kit, PropLayer layer)
        {
            var material = MaterialFor(kit, layer);
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                var mats = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
                for (int i = 0; i < mats.Length; i++) mats[i] = material;
                r.sharedMaterials = mats;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
        }

        static Material MaterialFor(PropKit kit, PropLayer layer)
        {
            int key = (int)kit * 16 + (int)layer;
            if (materials.TryGetValue(key, out var m) && m != null) return m;

            var style = Styles[(int)layer];
            var palette = layer != PropLayer.Character && apogeeColormaps[(int)kit] != null ? apogeeColormaps[(int)kit] : colormaps[(int)kit];
            m = new Material(shader) { name = $"Kenney_{kit}_{layer}", mainTexture = palette };
            m.SetFloat("_Tint", style.tint);
            m.SetFloat("_Fog", style.fog);
            m.SetFloat("_Desat", style.desat);
            materials[key] = m;
            return m;
        }

        /// <summary>Whitens renderers (hit feedback); 0 clears it.</summary>
        public static void SetFlash(Renderer[] renderers, float amount)
        {
            if (renderers == null) return;
            block ??= new MaterialPropertyBlock();
            foreach (var r in renderers)
            {
                if (r == null) continue;
                if (amount <= 0f)
                {
                    r.SetPropertyBlock(null);
                    continue;
                }
                r.GetPropertyBlock(block);
                block.SetFloat(FlashId, amount);
                r.SetPropertyBlock(block);
            }
        }

        /// <summary>Sky color the props fog toward; call every frame while the sky changes.</summary>
        public static void SetFogColor(Color sky) => Shader.SetGlobalColor(FogColorId, sky);
    }

    /// <summary>
    /// Procedural motion for a 3D character rig (the Kenney characters have no animation):
    /// a waddle whose strength follows the parent's horizontal speed, or a gentle float for
    /// the ghost, plus a pop-up "rise from the grave" on spawn.
    /// </summary>
    public class ModelMotion : MonoBehaviour
    {
        public bool floating;
        public float height = 1.2f;
        public float pitch = -8f;

        Vector3 basePosition;
        float phase;
        Vector2 lastPos;
        float riseTime, riseDuration;

        public bool IsRising => riseTime < riseDuration;

        void Awake()
        {
            basePosition = transform.localPosition;
            phase = Random.Range(0f, 10f);
        }

        void Start() => lastPos = transform.parent != null ? (Vector2)transform.parent.position : Vector2.zero;

        public void Rise(float duration)
        {
            riseDuration = duration;
            riseTime = 0f;
            transform.localScale = new Vector3(1f, 0.05f, 1f);
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (riseTime < riseDuration)
            {
                riseTime += dt;
                float p = Mathf.Clamp01(riseTime / riseDuration);
                // Ease-out-back: overshoots a little then settles, like bursting out of the dirt.
                float s = 1f + 2.2f * Mathf.Pow(p - 1f, 3f) + 1.2f * Mathf.Pow(p - 1f, 2f);
                transform.localScale = new Vector3(1f, Mathf.Max(0.05f, s), 1f);
            }

            Vector2 pos = transform.parent != null ? (Vector2)transform.parent.position : Vector2.zero;
            float speed = Mathf.Min(8f, (pos - lastPos).magnitude / dt);
            lastPos = pos;

            if (floating)
            {
                phase += dt;
                transform.localPosition = basePosition + new Vector3(0f, 0.25f * height * 0.3f + Mathf.Sin(phase * 2.6f) * 0.12f, 0f);
                transform.localRotation = Quaternion.Euler(pitch, 0f, Mathf.Sin(phase * 1.7f) * 6f);
                return;
            }

            float strength = Mathf.Clamp01(speed / 2.5f);
            phase += dt * (4f + speed * 2.2f);
            float bob = Mathf.Abs(Mathf.Sin(phase)) * 0.07f * height * strength;
            transform.localPosition = basePosition + new Vector3(0f, bob, 0f);
            transform.localRotation = Quaternion.Euler(pitch, 0f, Mathf.Sin(phase) * 9f * strength);
        }
    }
}
