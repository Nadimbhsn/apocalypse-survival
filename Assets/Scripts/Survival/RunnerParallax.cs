using UnityEngine;

namespace Platformer.Survival
{
    /// <summary>
    /// Two painted bands of floating islands between the sky and the run: a far one,
    /// hazy and slow, and a nearer one. Each style (crimson ruins, violet moss, embers,
    /// castle stone) has its own pair; switching zone crossfades them. A band is one
    /// tiled sprite three tiles wide, re-centred on the camera every frame so it never ends.
    /// Runs after the camera has moved (late execution order) so it never lags a frame.
    /// </summary>
    [DefaultExecutionOrder(10001)]
    public class RunnerParallax : MonoBehaviour
    {
        struct Band
        {
            public SpriteRenderer sr;
            public float factorX, factorY, baseY, tileW;
        }

        const float BandHeight = 9f;   // world height of a band (768 px)
        Camera cam;
        readonly Band[,] bands = new Band[4, 2];
        int style;
        readonly float[] weight = new float[4];

        public static RunnerParallax Create(Camera camera)
        {
            if (!RunnerArt.Available || camera == null) return null;
            var p = new GameObject("RunnerParallax").AddComponent<RunnerParallax>();
            p.cam = camera;
            p.Build();
            return p;
        }

        void Build()
        {
            for (int s = 0; s < 4; s++)
            {
                string k = RunnerArt.Key((ArtStyle)s);
                bands[s, 0] = MakeBand($"par_far_{k}", 0.1f, 0.08f, 0.6f, RunnerArt.OrderFar);
                bands[s, 1] = MakeBand($"par_mid_{k}", 0.3f, 0.2f, -0.6f, RunnerArt.OrderMid);
                weight[s] = s == 0 ? 1f : 0f;
            }
        }

        Band MakeBand(string sprite, float fx, float fy, float baseY, int order)
        {
            var tex = Resources.Load<Texture2D>("Runner/" + sprite);
            var go = new GameObject(sprite);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = order;
            float ppu = tex != null ? tex.height / BandHeight : 85f;
            if (tex != null)
                sr.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), ppu, 0, SpriteMeshType.FullRect);
            float tileW = tex != null ? tex.width / ppu : 24f;
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.size = new Vector2(tileW * 3f, BandHeight);
            return new Band { sr = sr, factorX = fx, factorY = fy, baseY = baseY, tileW = tileW };
        }

        public void SetStyle(ArtStyle s) => style = (int)s;

        /// <summary>Only while a run is on: the mini-games borrow the same camera.</summary>
        public System.Func<bool> isVisible;

        public void SetVisible(bool visible) => gameObject.SetActive(visible);

        void LateUpdate()
        {
            if (cam == null) return;
            bool show = isVisible == null || isVisible();
            if (!show)
            {
                for (int s = 0; s < 4; s++)
                    for (int b = 0; b < 2; b++)
                        if (bands[s, b].sr != null && bands[s, b].sr.enabled) bands[s, b].sr.enabled = false;
                return;
            }
            var c = cam.transform.position;
            for (int s = 0; s < 4; s++)
            {
                weight[s] = Mathf.MoveTowards(weight[s], s == style ? 1f : 0f, Time.deltaTime * 0.8f);
                for (int b = 0; b < 2; b++)
                {
                    var band = bands[s, b];
                    if (band.sr == null) continue;
                    bool on = weight[s] > 0.001f;
                    if (band.sr.enabled != on) band.sr.enabled = on;
                    if (!on) continue;
                    // The band drifts by a fraction of the camera's travel; snapping it by whole
                    // tiles keeps one centred on the camera.
                    float x = c.x - Mathf.Repeat(c.x * band.factorX, band.tileW);
                    float y = c.y + band.baseY - c.y * band.factorY;
                    band.sr.transform.position = new Vector3(x, y, 60f - b * 10f);
                    var col = band.sr.color;
                    col.a = weight[s];
                    band.sr.color = col;
                }
            }
        }
    }
}
