using UnityEngine;

namespace Platformer.Survival
{
    /// <summary>
    /// Renders one Kenney 3D character into a RenderTexture for the UI (Arena bosses):
    /// the model sits far away from the play area on its own layer, filmed by a dedicated
    /// orthographic camera with a transparent background, like the hub's character preview.
    /// </summary>
    public class ModelStage : MonoBehaviour
    {
        const int Layer = 30;

        Camera cam;
        RenderTexture texture;
        Transform current;
        Renderer[] renderers;

        public RenderTexture Texture => texture;
        public Renderer[] Renderers => renderers;

        public static ModelStage Create(string name, Vector3 position, int resolution = 512)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            var stage = go.AddComponent<ModelStage>();

            var camGo = new GameObject(name + "Camera");
            camGo.transform.SetParent(go.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 0.6f, -10f);
            stage.cam = camGo.AddComponent<Camera>();
            stage.cam.orthographic = true;
            stage.cam.orthographicSize = 1f;
            stage.cam.cullingMask = 1 << Layer;
            stage.cam.clearFlags = CameraClearFlags.SolidColor;
            stage.cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            stage.cam.enabled = false;

            stage.texture = new RenderTexture(resolution, resolution, 16) { name = name + "RT" };
            stage.cam.targetTexture = stage.texture;
            return stage;
        }

        /// <summary>Shows a character of the given height, turned toward the viewer's left (facing the player).</summary>
        public void Show(PropKit kit, string model, float height, float yaw = 40f)
        {
            if (current != null) Destroy(current.gameObject);
            current = null;
            renderers = null;
            if (!KenneyProps.Available) return;

            var size = KenneyProps.Size(kit, model);
            current = KenneyProps.Spawn(kit, model, transform, Vector3.zero, height / Mathf.Max(0.01f, size.y), PropLayer.Character, yaw, -8f);
            if (current == null) return;
            SetLayer(current, Layer);
            renderers = current.GetComponentsInChildren<Renderer>();
            var motion = current.gameObject.AddComponent<ModelMotion>();
            motion.height = height;
            motion.pitch = -8f;
            motion.floating = model == "character-ghost";

            cam.orthographicSize = height * 0.62f;
            cam.transform.localPosition = new Vector3(0f, height * 0.5f, -10f);
        }

        public void SetActive(bool on)
        {
            if (cam != null) cam.enabled = on;
        }

        static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
        }
    }
}
