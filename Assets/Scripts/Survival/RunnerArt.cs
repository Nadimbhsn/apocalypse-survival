using System.Collections.Generic;
using UnityEngine;

namespace Platformer.Survival
{
    /// <summary>The painted look a stretch of the runner wears.</summary>
    public enum ArtStyle { Grass, Moss, Ember, Stone }

    /// <summary>
    /// The runner's painted world (Resources/Runner, made by the offline painter at
    /// 170.67 px per metre): crimson-grass walkways over hanging rock, violet moss for the
    /// cemetery and the storm, charred earth with embers, castle stone for the ramparts,
    /// stone slabs, thorns, toxic pools, painted props and parallax bands.
    ///
    /// Gameplay objects keep their colliders and their flat placeholder sprite, which is
    /// only hidden: the art hangs under an "Art" child that undoes the parent's scale, so
    /// it is laid out in world units and recycled with its object. Depth: the art sits a
    /// little behind z = 0 (the plane the dead and the pickups stand in) so 3D bodies are
    /// never hidden by it; draw order between sprites comes from the sorting order.
    /// </summary>
    public static class RunnerArt
    {
        public const float Ppu = 1024f / 6f;
        /// <summary>Height of the grass strip, and where its surface line sits (from the bottom).</summary>
        const float TopHeight = 224f / Ppu, TopPivot = (224f - 96f) / 224f;
        const float UnderHeight = 640f / Ppu, UnderWidth = 1024f / Ppu;
        const float WallTile = 512f / Ppu;

        // Sorting orders of the painted layers (the player and gameplay sprites sit at 0 and above).
        public const int OrderFar = -60, OrderMid = -55, OrderBackProp = -12, OrderWall = -9, OrderUnder = -7,
            OrderNearProp = -4, OrderTop = -1;
        // Depths behind the gameplay plane.
        public const float ZGround = 0.6f, ZNearProp = 1.4f, ZBackProp = 3f, ZWall = 4f;

        static readonly Dictionary<string, Sprite> sprites = new();
        static int available = -1;

        public static bool Available
        {
            get
            {
                if (available < 0) available = Resources.Load<Texture2D>("Runner/top_grass") != null ? 1 : 0;
                return available == 1;
            }
        }

        public static string Key(ArtStyle s) => s switch
        {
            ArtStyle.Moss => "moss",
            ArtStyle.Ember => "ember",
            ArtStyle.Stone => "stone",
            _ => "grass",
        };

        public static ArtStyle StyleOf(ZoneKind k) => k switch
        {
            ZoneKind.Infested or ZoneKind.Storm => ArtStyle.Moss,
            ZoneKind.Wasteland => ArtStyle.Ember,
            ZoneKind.Highway or ZoneKind.Rooftops or ZoneKind.Descent or ZoneKind.Ascent or ZoneKind.Shaft => ArtStyle.Stone,
            _ => ArtStyle.Grass,
        };

        public static Sprite Get(string name, Vector2 pivot, Vector4 border = default)
        {
            string key = $"{name}|{pivot.x:0.###}|{pivot.y:0.###}|{border}";
            if (sprites.TryGetValue(key, out var s)) return s;
            var tex = Resources.Load<Texture2D>("Runner/" + name);
            if (tex != null)
                s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), pivot, Ppu, 0, SpriteMeshType.FullRect, border);
            sprites[key] = s;
            return s;
        }

        /// <summary>A painted prop, its pivot at its feet.</summary>
        public static Sprite Prop(string name)
        {
            var pivot = RunnerPropPivots.All.TryGetValue("prop_" + name, out var p) ? p : new Vector2(0.5f, 0.05f);
            return Get("prop_" + name, pivot);
        }

        static SpriteRenderer Child(Transform parent, string name, Sprite sprite, Vector3 localPos, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            return sr;
        }

        /// <summary>
        /// An unscaled child at the top centre of a box scaled to (width, thickness), whose
        /// children are laid out in world units; the box's own flat sprite is hidden.
        /// </summary>
        static Transform ArtRoot(GameObject box, float z = ZGround)
        {
            var sr = box.GetComponent<SpriteRenderer>();
            if (sr != null) sr.enabled = false;
            var old = box.transform.Find("Art");
            if (old != null) Object.Destroy(old.gameObject);
            var art = new GameObject("Art").transform;
            art.SetParent(box.transform, false);
            var s = box.transform.localScale;
            art.localScale = new Vector3(1f / Mathf.Max(0.0001f, s.x), 1f / Mathf.Max(0.0001f, s.y), 1f);
            art.localPosition = new Vector3(0f, 0.5f, z);
            return art;
        }

        /// <summary>
        /// A walkway: grass (or paving) strip along the top with rounded ends, and the rock
        /// it belongs to hanging below - or, for the ramparts, a castle wall going down.
        /// </summary>
        public static void DressGround(GameObject seg, float width, float depth, ArtStyle style, bool castle = false)
        {
            var art = ArtRoot(seg);
            string k = Key(style);
            float rockTop = -0.2f;
            if (castle)
            {
                var wall = Child(art, "Wall", Get("wall_castle", new Vector2(0.5f, 1f)), new Vector3(0f, -0.15f, 0.2f), OrderWall);
                wall.drawMode = SpriteDrawMode.Tiled;
                wall.size = new Vector2(width, Mathf.Max(WallTile, depth));
                rockTop = -0.15f - depth + 0.3f;
                depth = Mathf.Clamp(width * 0.5f, 2f, 5f);
            }
            var under = Child(art, "Under", Get($"under_{k}_{Random.Range(0, 3)}", new Vector2(0.5f, 1f)), new Vector3(0f, rockTop, 0.1f), OrderUnder);
            under.flipX = Random.value < 0.5f;
            under.transform.localScale = new Vector3(width * 1.04f / UnderWidth, depth / UnderHeight, 1f);

            var top = Child(art, "Top", Get($"top_{k}", new Vector2(0.5f, TopPivot)), Vector3.zero, OrderTop);
            top.drawMode = SpriteDrawMode.Tiled;
            top.tileMode = SpriteTileMode.Continuous;
            top.size = new Vector2(Mathf.Max(0.3f, width - 0.7f), TopHeight);

            var corner = Get($"corner_{k}", new Vector2(0f, TopPivot));
            if (width > 1.2f)
            {
                Child(art, "EndR", corner, new Vector3(width / 2f - 0.4f, 0f, 0f), OrderTop);
                Child(art, "EndL", corner, new Vector3(-width / 2f + 0.4f, 0f, 0f), OrderTop).flipX = true;
            }
            else top.size = new Vector2(width, TopHeight);
        }

        /// <summary>A thin floating slab (stepping stone, bonus ledge, debris, bounce pad).</summary>
        public static void DressSlab(GameObject box, float width, ArtStyle style, Color? tint = null)
        {
            var art = ArtRoot(box);
            var slab = Child(art, "Slab", Get($"slab_{Key(style)}", new Vector2(0.5f, 1f - 34f / 128f), new Vector4(70f, 0f, 70f, 0f)), Vector3.zero, OrderTop);
            slab.drawMode = SpriteDrawMode.Sliced;
            slab.size = new Vector2(width + 0.15f, 128f / Ppu);
            if (tint.HasValue) slab.color = tint.Value;
        }

        /// <summary>A row of stone thorns with glowing tips, as tall as the hazard.</summary>
        public static void DressSpikes(GameObject box, float width, float height)
        {
            var art = ArtRoot(box, 0f);
            art.localPosition = new Vector3(0f, -0.5f, 0f);  // bottom of the box
            var tile = Get("spikes", new Vector2(0.5f, 0f));
            var sr = Child(art, "Thorns", tile, Vector3.zero, 1);
            sr.drawMode = SpriteDrawMode.Tiled;
            float tileH = 104f / Ppu;
            sr.transform.localScale = new Vector3(1f, height / tileH, 1f);
            sr.size = new Vector2(width, tileH);
        }

        /// <summary>
        /// A body of toxic liquid: a glowing surface over violet depths. surfaceOnTop false
        /// turns it into the Survol's toxic cloud ceiling (the surface faces down).
        /// </summary>
        public static void DressToxic(GameObject box, float width, float depth, bool surfaceOnTop = true)
        {
            var art = ArtRoot(box, 0.2f);
            art.localPosition = new Vector3(0f, surfaceOnTop ? 0.5f : -0.5f, 0.2f);
            var surf = Child(art, "Surface", Get("toxic", new Vector2(0.5f, 1f - 18f / 96f)), Vector3.zero, surfaceOnTop ? 1 : 2);
            surf.drawMode = SpriteDrawMode.Tiled;
            surf.size = new Vector2(width, 96f / Ppu);
            if (!surfaceOnTop) surf.flipY = true;
            if (depth > 0.6f)
            {
                var body = Child(art, "Depths", PlaceholderVisuals.Square(Color.white), new Vector3(0f, surfaceOnTop ? -depth / 2f - 0.3f : depth / 2f + 0.3f, 0.05f), 0);
                body.color = new Color(0.16f, 0.05f, 0.2f, 0.96f);
                body.transform.localScale = new Vector3(width, depth, 1f);
            }
        }

        /// <summary>A crackling arcane chain (the Survol's live cables).</summary>
        public static void DressCable(GameObject box, float width)
        {
            var art = ArtRoot(box, 0f);
            art.localPosition = Vector3.zero;
            var sr = Child(art, "Chain", Get("chain", new Vector2(0.5f, 0.5f)), Vector3.zero, 2);
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.size = new Vector2(width, 40f / Ppu);
        }

        /// <summary>A stone wall surface (the tower behind the climb, the shaft, its side walls).</summary>
        public static SpriteRenderer Wall(Transform parent, Vector3 centre, Vector2 size, Color tint, int order = OrderWall)
        {
            var sr = Child(parent, "Wall", Get("wall_castle", new Vector2(0.5f, 0.5f)), centre, order);
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.size = size;
            sr.color = tint;
            return sr;
        }

        public static SpriteRenderer Window(Transform parent, Vector3 pos, int order = OrderWall + 1)
        {
            return Child(parent, "Window", Get("window", new Vector2(0.5f, 0f)), pos, order);
        }

        /// <summary>A painted prop standing at (x, groundY), possibly mirrored, with a depth haze.</summary>
        public static GameObject PlaceProp(Transform parent, string name, float x, float groundY, bool back, float scale = 1f, Color? tint = null)
        {
            var sprite = Prop(name);
            if (sprite == null) return null;
            var sr = Child(parent, "Prop_" + name, sprite, new Vector3(x, groundY, back ? ZBackProp : ZNearProp), back ? OrderBackProp : OrderNearProp);
            sr.transform.localScale = Vector3.one * scale;
            sr.flipX = Random.value < 0.5f && !name.StartsWith("banner");
            sr.color = tint ?? Color.white;
            return sr.gameObject;
        }

        /// <summary>Props that stand behind the walkway (trees, ruins, banners) and on it (bushes, rocks, lanterns), per style.</summary>
        public static string[] BackProps(ArtStyle s, bool castle) => castle
            ? new[] { "banner_0", "banner_0", "banner_1", "maple_2_0", "pine_0" }
            : s switch
            {
                ArtStyle.Moss => new[] { "deadtree_0", "deadtree_1", "pine_1", "ironfence", "banner_1", "deadtree_0" },
                ArtStyle.Ember => new[] { "burnt_0", "burnt_1", "ruin_1", "burnt_0" },
                ArtStyle.Stone => new[] { "banner_0", "maple_1_0", "pine_0", "ruin_0" },
                _ => new[] { "maple_0_0", "maple_0_1", "maple_1_0", "maple_1_1", "maple_2_0", "maple_3_0", "maple_3_1", "pine_0", "pine_1", "ruin_0", "ruin_1", "banner_0" },
            };

        public static string[] NearProps(ArtStyle s, bool castle) => castle
            ? new[] { "brazier", "crate_0", "crate_1", "lantern_0" }
            : s switch
            {
                ArtStyle.Moss => new[] { "grave_0", "grave_1", "grave_2", "bush_3", "rock_0", "lantern_1", "grave_0" },
                ArtStyle.Ember => new[] { "rock_0", "rock_1", "rock_2", "brazier", "crate_1" },
                ArtStyle.Stone => new[] { "crate_0", "crate_1", "lantern_0", "rock_1", "fence" },
                _ => new[] { "bush_0", "bush_1", "bush_2", "rock_0", "rock_1", "lantern_0", "fence", "stump", "crate_0" },
            };
    }
}
