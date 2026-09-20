using UnityEngine;

namespace Platformer.Survival
{
    /// <summary>
    /// The painted Apogée sky (Resources/Art/sky_runner) as a camera-filling quad far behind
    /// the whole scene. It is cover-fitted to whatever the camera shows (portrait phone,
    /// zooming during the tower climb), scrolls very slowly with the camera's x for a sense
    /// of distance, and mirror-tiles so it never runs out. Its tint eases toward each
    /// zone's color. Follows the main camera everywhere, so the mini-games that borrow the
    /// camera (Fusion, Barricade) get the same sky for free.
    /// </summary>
    [DefaultExecutionOrder(10001)]
    public class SkyBackdrop : MonoBehaviour
    {
        public static SkyBackdrop Instance { get; private set; }

        /// <summary>Texture widths scrolled per world unit travelled (tiny = far away).</summary>
        public float parallax = 0.0035f;
        const float Distance = 80f;

        Camera cam;
        Material material;
        Texture2D texture;
        Color tint = Color.white, targetTint = Color.white;

        public static SkyBackdrop Create(Camera camera)
        {
            if (Instance != null) return Instance;
            var texture = ApogeeTheme.Art("sky_runner");
            var shader = Shader.Find("Survival/SkyBackdrop");
            if (camera == null || texture == null || shader == null) return null;

            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "SkyBackdrop";
            var collider = go.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            texture.wrapModeU = TextureWrapMode.Mirror;
            texture.wrapModeV = TextureWrapMode.Clamp;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = new Material(shader) { mainTexture = texture, name = "SkyBackdrop" };
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;

            var sky = go.AddComponent<SkyBackdrop>();
            sky.cam = camera;
            sky.material = r.sharedMaterial;
            sky.texture = texture;
            Instance = sky;
            sky.LateUpdate();
            return sky;
        }

        /// <summary>Eases the sky toward this tint (instant = true skips the transition).</summary>
        public void SetTint(Color c, bool instant = false)
        {
            targetTint = c;
            if (instant) tint = c;
        }

        void LateUpdate()
        {
            if (cam == null) return;
            var cp = cam.transform.position;
            transform.position = new Vector3(cp.x, cp.y, cp.z + Distance);
            transform.rotation = Quaternion.identity;

            float h = 2f * cam.orthographicSize * 1.02f;
            float w = 2f * cam.orthographicSize * cam.aspect * 1.02f;
            transform.localScale = new Vector3(w, h, 1f);

            // Cover-fit: show the full texture height when the screen is narrower than the
            // painting (portrait), the full width otherwise.
            float texAspect = texture.width / (float)texture.height;
            float quadAspect = w / h;
            float uRange = 1f, vRange = 1f;
            if (quadAspect < texAspect) uRange = quadAspect / texAspect; else vRange = texAspect / quadAspect;

            float u = 0.55f - uRange * 0.5f + cp.x * parallax;
            float v = 0.5f - vRange * 0.5f;
            material.mainTextureScale = new Vector2(uRange, vRange);
            material.mainTextureOffset = new Vector2(u, v);

            tint = Color.Lerp(tint, targetTint, Time.unscaledDeltaTime * 1.2f);
            material.color = tint;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
