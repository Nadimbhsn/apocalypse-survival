using System.Collections.Generic;
using UnityEngine;

namespace Platformer.Survival
{
    /// <summary>
    /// Scenery half of SurvivalDirector: dresses the generated terrain with the Kenney
    /// Graveyard / Castle 3D kits (see KenneyProps). Nothing here has a collider or affects
    /// gameplay, except the cemetery graves that make zombies burst out of the ground.
    ///
    /// Depth plan (camera looks down +Z; the player sprite sits at z = 1):
    ///   z = 0       gameplay sprites (ground, platforms, pickups, zombies' feet)
    ///   z ~ 1.8-2.6 Near layer: small props standing on the walkway, behind the player
    ///   z ~ 2-4     Rampart layer: castle masonry under rooftop / staircase segments
    ///   z ~ 4-6     Mid layer: trees, crypts, fences, lamp posts, on the segment they belong to
    ///   z ~ 7-9     the Ascent tower / shaft masonry
    ///   z ~ 14-24   Far layer: skyline (castles, viaducts, dead forests) under a parallax
    ///               root that follows the camera at half speed; their bases sit below
    ///               street level so the walkway hides them
    /// Further layers get more fog toward the current sky color, so decor reads as depth
    /// and never as something to stand on.
    ///
    /// The Apogée world floats: every walkway is the top of a floating island (a rocky
    /// underside hangs below it, crimson grass lines its edge), castle masonry and the
    /// Ascent tower sit on rock too, and the far skyline is made of small fogged islands
    /// carrying castles and trees over the painted sky (SkyBackdrop).
    /// </summary>
    public partial class SurvivalDirector
    {
        const float NearZ = 1.8f;
        const float MidZMin = 4.2f, MidZMax = 6f;
        const float FarParallax = 0.5f;
        static readonly Color GrassColor = new Color(0.64f, 0.15f, 0.10f);

        struct PropDef
        {
            public PropKit kit;
            public string name;
            public float hMin, hMax, weight;
            /// <summary>Special composites: 0 = single model, 1 = grave (zombie spawner), 2 = crypt with roof, 3 = fence row, 4 = wall row.</summary>
            public int special;

            public PropDef(PropKit kit, string name, float hMin, float hMax, float weight = 1f, int special = 0)
            {
                this.kit = kit; this.name = name; this.hMin = hMin; this.hMax = hMax; this.weight = weight; this.special = special;
            }
        }

        struct GraveTrigger
        {
            public float x, y;
            public bool used;
        }

        const PropKit G = PropKit.Graveyard;
        const PropKit C = PropKit.Castle;

        static readonly PropDef[] NearCity =
        {
            new(G, "rocks", 0.35f, 0.5f), new(G, "debris", 0.25f, 0.35f), new(G, "debris-wood", 0.2f, 0.28f),
            new(G, "trunk", 0.4f, 0.55f, 0.7f), new(G, "gravestone-debris", 0.3f, 0.4f, 0.5f), new(C, "rocks-small", 0.45f, 0.6f),
            new(C, "tree-trunk", 0.35f, 0.5f, 0.6f), new(G, "bench-damaged", 0.55f, 0.65f, 0.5f), new(G, "fence-damaged", 0.6f, 0.7f, 0.6f),
            new(G, "lightpost-single", 1.6f, 1.9f, 0.4f),
        };
        static readonly PropDef[] NearHighway =
        {
            new(G, "debris", 0.25f, 0.35f), new(G, "debris-wood", 0.2f, 0.28f), new(G, "rocks", 0.35f, 0.5f),
            new(G, "lightpost-double", 1.7f, 2.0f, 0.9f), new(G, "fence-damaged", 0.6f, 0.7f, 0.7f), new(C, "siege-ram-demolished", 0.7f, 0.85f, 0.4f),
            new(C, "rocks-small", 0.45f, 0.6f, 0.6f),
        };
        static readonly PropDef[] NearCemetery =
        {
            new(G, "gravestone-round", 0.6f, 0.75f), new(G, "gravestone-cross", 0.9f, 1.1f), new(G, "gravestone-cross-large", 1.0f, 1.2f, 0.6f),
            new(G, "gravestone-roof", 0.65f, 0.8f), new(G, "gravestone-decorative", 0.65f, 0.8f), new(G, "gravestone-wide", 0.55f, 0.65f, 0.7f),
            new(G, "gravestone-broken", 0.35f, 0.45f, 0.8f), new(G, "gravestone-bevel", 0.55f, 0.7f, 0.7f), new(G, "candle-multiple", 0.25f, 0.3f, 0.6f),
            new(G, "lantern-candle", 0.45f, 0.55f, 0.5f), new(G, "pumpkin-carved", 0.35f, 0.45f, 0.5f), new(G, "pumpkin-tall-carved", 0.4f, 0.5f, 0.4f),
            new(G, "urn-round", 0.35f, 0.45f, 0.4f), new(G, "cross-wood", 0.9f, 1.1f, 0.5f), new(G, "shovel-dirt", 0.6f, 0.7f, 0.3f),
            new(G, "grave", 0f, 0f, 0.6f, special: 1),
        };
        static readonly PropDef[] NearWasteland =
        {
            new(G, "rocks", 0.35f, 0.5f), new(G, "rocks-tall", 0.5f, 0.7f), new(G, "trunk", 0.4f, 0.55f), new(G, "trunk-long", 0.8f, 1.1f, 0.7f),
            new(G, "debris-wood", 0.2f, 0.28f), new(G, "hay-bale", 0.35f, 0.45f, 0.6f), new(G, "coffin-old", 0.2f, 0.25f, 0.4f),
            new(G, "shovel-dirt", 0.6f, 0.7f, 0.4f), new(G, "pine-fall-crooked", 1.3f, 1.6f, 0.6f), new(G, "fire-basket", 0.22f, 0.26f, 0.4f),
        };
        static readonly PropDef[] NearRampart =
        {
            new(C, "flag-banner-long", 1.6f, 2.0f, 0.7f), new(G, "fire-basket", 0.22f, 0.26f), new(G, "lantern-candle", 0.45f, 0.55f, 0.6f),
            new(C, "rocks-small", 0.4f, 0.5f, 0.4f), new(G, "urn-round", 0.35f, 0.45f, 0.5f), new(G, "debris", 0.25f, 0.35f, 0.7f),
        };

        static readonly PropDef[] MidCity =
        {
            new(G, "pine-fall", 2.2f, 3.0f, 0.7f), new(C, "tree-large", 2.2f, 3.0f), new(C, "siege-tower-demolished", 2.4f, 3.0f, 0.5f),
            new(C, "siege-catapult-demolished", 0.8f, 1.1f, 0.5f), new(G, "stone-wall-damaged", 1.0f, 1.3f, 0.8f, special: 4),
            new(G, "lightpost-double", 2.0f, 2.3f, 0.6f), new(C, "wall-half", 1.8f, 2.4f, 0.5f),
        };
        static readonly PropDef[] MidHighway =
        {
            new(G, "lightpost-double", 2.0f, 2.4f, 1.2f), new(C, "siege-ram-demolished", 1.0f, 1.3f), new(C, "siege-ballista-demolished", 0.7f, 0.9f, 0.7f),
            new(G, "stone-wall-damaged", 1.0f, 1.3f, 0.8f, special: 4), new(C, "tree-small", 1.8f, 2.4f, 0.5f),
        };
        static readonly PropDef[] MidCemetery =
        {
            new(G, "crypt-small", 2.0f, 2.5f, 1.0f, special: 2), new(G, "crypt", 1.6f, 2.0f, 0.7f), new(G, "iron-fence", 1.1f, 1.3f, 1.5f, special: 3),
            new(G, "pine", 2.4f, 3.2f), new(G, "pine-crooked", 2.4f, 3.2f), new(G, "cross-column", 1.8f, 2.3f, 0.6f),
            new(G, "pillar-obelisk", 1.8f, 2.3f, 0.6f), new(G, "lightpost-single", 2.0f, 2.4f, 0.6f),
        };
        static readonly PropDef[] MidWasteland =
        {
            new(G, "pine-fall-crooked", 2.4f, 3.2f, 1.5f), new(G, "pine-crooked", 2.4f, 3.2f, 0.7f), new(G, "trunk-long", 1.4f, 2.0f),
            new(C, "rocks-large", 1.0f, 1.5f), new(C, "siege-trebuchet-demolished", 0.5f, 0.7f, 0.6f), new(C, "siege-trebuchet", 2.4f, 3.0f, 0.5f),
        };

        readonly List<GameObject> scenery = new();
        readonly List<GameObject> farScenery = new();
        readonly List<GraveTrigger> graves = new();
        Transform farRoot;
        float nearFrontierX, midFrontierX, farFrontierLocal;
        /// <summary>Graves that spawn the dead are kept at least this far apart.</summary>
        const float GraveSpacing = 11f;
        float lastGraveX = float.MinValue;

        bool UseKenney => KenneyProps.Available;

        // ---- setup / lifecycle -----------------------------------------------------------

        void SetupScenery()
        {
            if (!UseKenney) return;
            farRoot = new GameObject("FarScenery").transform;
            farRoot.SetParent(entityParent, false);
            farRoot.gameObject.AddComponent<FarLayerFollower>().director = this;
            KenneyProps.Prewarm();
        }

        void ResetScenery()
        {
            foreach (var go in scenery) if (go != null) Destroy(go);
            scenery.Clear();
            foreach (var go in farScenery) if (go != null) Destroy(go);
            farScenery.Clear();
            graves.Clear();
            lastGraveX = float.MinValue;
            nearFrontierX = midFrontierX = 0f;
            farFrontierLocal = -20f;
            if (farRoot != null) farRoot.localPosition = Vector3.zero;
        }

        /// <summary>Per-frame scenery work: fog color, far skyline, graves.</summary>
        void UpdateScenery()
        {
            if (mainCamera != null) KenneyProps.SetFogColor(mainCamera.backgroundColor);
            if (!UseKenney) return;
            GenerateFarSkyline();
            UpdateGraves();
        }

        void RecycleScenery(float cutoff)
        {
            RecycleList(scenery, cutoff);
            if (mainCamera == null) return;
            float farCutoff = mainCamera.transform.position.x - 45f;
            RecycleList(farScenery, farCutoff);
            for (int i = graves.Count - 1; i >= 0; i--)
                if (graves[i].x < cutoff) graves.RemoveAt(i);
        }

        /// <summary>Background decor entry point (replaces the flat ruins when the kits are present).</summary>
        void GenerateBackgroundDecorOrScenery()
        {
            if (UseKenney) return; // segments decorate themselves as they are generated
            GenerateBackgroundDecor();
        }

        // ---- segment dressing --------------------------------------------------------------

        /// <summary>
        /// Called for every solid, stable segment right after it is generated. High segments
        /// (rooftops, staircase, the roof after the tower) get castle masonry under them;
        /// everything else gets a deep earth block. Then props per the zone's palettes.
        /// </summary>
        void DecorateSegment(GameObject segmentGo, float xStart, float width, float topY)
        {
            // Decoration is cosmetic: never let it break the terrain the player runs on.
            try { DecorateSegmentInternal(segmentGo, xStart, width, topY); }
            catch (System.Exception e) { Debug.LogError($"[Survival] Scenery failed on segment at x={xStart:0}: {e}"); }
        }

        void DecorateSegmentInternal(GameObject segmentGo, float xStart, float width, float topY)
        {
            bool castleZone = genZone.Kind == ZoneKind.Rooftops || genZone.Kind == ZoneKind.Descent || genZone.Kind == ZoneKind.Ascent;
            bool high = castleZone && topY > 1.5f;

            if (UseKenney && high) BuildRampart(xStart, width, topY);
            else AddEarth(segmentGo, xStart, width, topY);

            if (segmentGo != null) AddLip(segmentGo, high ? (Color?)null : GrassColor);
            if (!UseKenney) return;

            PlaceNearProps(xStart, width, topY, high ? NearRampart : NearPalette(genZone.Kind));
            if (!high) PlaceMidProps(xStart, width, topY, MidPalette(genZone.Kind));
        }

        static PropDef[] NearPalette(ZoneKind kind) => kind switch
        {
            ZoneKind.Highway => NearHighway,
            ZoneKind.Infested => NearCemetery,
            ZoneKind.Wasteland => NearWasteland,
            ZoneKind.Rooftops or ZoneKind.Descent or ZoneKind.Ascent => NearRampart,
            _ => NearCity,
        };

        static PropDef[] MidPalette(ZoneKind kind) => kind switch
        {
            ZoneKind.Highway => MidHighway,
            ZoneKind.Infested => MidCemetery,
            ZoneKind.Wasteland => MidWasteland,
            _ => MidCity,
        };

        static (float min, float max) NearSpacing(ZoneKind kind) => kind switch
        {
            ZoneKind.Infested => (1.2f, 2.2f),
            ZoneKind.Highway => (2.5f, 5f),
            ZoneKind.Rooftops or ZoneKind.Descent or ZoneKind.Ascent => (3f, 6f),
            _ => (2f, 3.8f),
        };

        /// <summary>Rocky underside of the floating island a walkway belongs to.</summary>
        void AddEarth(GameObject segmentGo, float xStart, float width, float topY)
        {
            float depth = Mathf.Clamp(width * 0.45f, 1.6f, 6f) * Random.Range(0.85f, 1.15f);
            var island = CreateIslandSprite(width * 1.03f, depth, IslandTint(genZone.Ground), -2);

            if (segmentGo != null)
            {
                // Child of the ground so it is recycled with it; undo the parent's scale.
                var parentScale = segmentGo.transform.localScale;
                var t = island.transform;
                var world = t.localScale;
                t.SetParent(segmentGo.transform, false);
                t.localScale = new Vector3(world.x / parentScale.x, world.y / parentScale.y, 1f);
                t.localPosition = new Vector3(0f, -0.5f + 0.04f / parentScale.y, 0f);
            }
            else
            {
                // Hand-placed intro ground: the scene object is 1 unit thick under topY.
                island.transform.SetParent(entityParent, false);
                island.transform.position = new Vector3(xStart + width / 2f, topY - 1f + 0.04f, 0f);
                scenery.Add(island);
            }
        }

        /// <summary>
        /// One islet of the Archipel: a small solid platform with crimson grass on top, a
        /// rocky underside, drifting sideways (see MovingIsland).
        /// </summary>
        GameObject CreateDriftingIsland(float centerX, float topY, float width, float amplitude, float speed)
        {
            const float thickness = 0.5f;
            var go = CreateSolidPlatform($"Islet_{centerX:0}", centerX, topY - thickness / 2f, width, thickness, genZone.Ground);
            AddLip(go, GrassColor);

            var rock = CreateIslandSprite(width * 1.05f, Mathf.Clamp(width * 0.9f, 1.2f, 3.2f), IslandTint(genZone.Ground), -2);
            var world = rock.transform.localScale;
            rock.transform.SetParent(go.transform, false);
            rock.transform.localScale = new Vector3(world.x / width, world.y / thickness, 1f);
            rock.transform.localPosition = new Vector3(0f, -0.5f + 0.04f / thickness, 0f);

            var drift = go.AddComponent<MovingIsland>();
            drift.amplitude = amplitude;
            drift.speed = speed;
            drift.phase = Random.Range(0f, Mathf.PI * 2f);
            return go;
        }

        /// <summary>An island underside sprite of the given world size (pivot at its top center).</summary>
        static GameObject CreateIslandSprite(float width, float depth, Color tint, int sortingOrder)
        {
            var go = new GameObject("IslandRock");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = ApogeeTheme.Island(Random.Range(0, 3));
            sr.color = tint;
            sr.sortingOrder = sortingOrder;
            sr.flipX = Random.value < 0.5f;
            go.transform.localScale = new Vector3(width, depth / ApogeeTheme.IslandAspect, 1f);
            return go;
        }

        /// <summary>Nudges the island rock toward the zone's ground hue.</summary>
        static Color IslandTint(Color ground)
        {
            var g = new Color(Mathf.Min(1.2f, ground.r * 2.4f), Mathf.Min(1.2f, ground.g * 2.4f), Mathf.Min(1.2f, ground.b * 2.4f), 1f);
            var c = Color.Lerp(Color.white, g, 0.5f);
            c.a = 1f;
            return c;
        }

        /// <summary>A strip along the top of a walkway (crimson grass, or lighter stone on ramparts) so its edge always reads.</summary>
        void AddLip(GameObject segmentGo, Color? color = null)
        {
            var parentScale = segmentGo.transform.localScale;
            const float lip = 0.12f;
            var go = new GameObject("Lip");
            go.transform.SetParent(segmentGo.transform, false);
            go.transform.localScale = new Vector3(1f, lip / parentScale.y, 1f);
            go.transform.localPosition = new Vector3(0f, 0.5f - lip / parentScale.y / 2f, 0f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = PlaceholderVisuals.Square(Color.white);
            var g = genZone.Ground;
            sr.color = color ?? new Color(Mathf.Min(1f, g.r * 1.45f + 0.04f), Mathf.Min(1f, g.g * 1.45f + 0.04f), Mathf.Min(1f, g.b * 1.45f + 0.04f), 1f);
            sr.sortingOrder = 0;
        }

        /// <summary>
        /// Castle masonry under a high walkway: columns of tower blocks, face-on so their
        /// outline matches the walkway exactly (no fake ledge sticking out past a gap).
        /// </summary>
        void BuildRampart(float xStart, float width, float topY)
        {
            int columns = Mathf.Max(1, Mathf.RoundToInt(width / 3.2f));
            float columnWidth = width / columns;
            var size = KenneyProps.Size(C, "tower-square-mid");
            float scale = columnWidth / size.x;
            float pieceHeight = size.y * scale;
            float z = 2.6f + size.z * scale * 0.5f;
            // A castle block a few storeys tall standing on a floating rock (camera rarely
            // looks further down than that while running on the ramparts).
            int rows = Mathf.Clamp(Mathf.CeilToInt(9f / pieceHeight), 2, 5);
            float bottom = topY - 0.05f - rows * pieceHeight;

            for (int c = 0; c < columns; c++)
            {
                float cx = xStart + columnWidth * (c + 0.5f);
                for (int row = 0; row < rows; row++)
                {
                    float pieceTop = topY - 0.05f - row * pieceHeight;
                    string piece = row > 0 && Random.value < 0.45f ? "tower-square-mid-windows" : "tower-square-mid";
                    AddScenery(KenneyProps.Spawn(C, piece, entityParent, new Vector3(cx, pieceTop - pieceHeight, z), scale, PropLayer.Rampart));
                }
            }

            var rock = CreateIslandSprite(width * 1.08f, Mathf.Clamp(width * 0.6f, 2.5f, 7f), IslandTint(genZone.Ground), -2);
            rock.transform.SetParent(entityParent, false);
            rock.transform.position = new Vector3(xStart + width / 2f, bottom + 0.3f, 0f);
            scenery.Add(rock);
        }

        void PlaceNearProps(float xStart, float width, float topY, PropDef[] palette)
        {
            if (palette == null) return;
            var (spaceMin, spaceMax) = NearSpacing(genZone.Kind);
            float x = Mathf.Max(xStart + 0.5f, nearFrontierX);
            float end = xStart + width - 0.5f;
            while (true)
            {
                x += Random.Range(spaceMin, spaceMax) * 0.5f;
                if (x > end) break;
                var def = Pick(palette);
                if (def.special == 1 && x - lastGraveX >= GraveSpacing) PlaceGrave(x, topY);
                else if (def.special == 1) SpawnNear(palette[0], x, topY);
                else SpawnNear(def, x, topY);
                x += Random.Range(spaceMin, spaceMax) * 0.5f;
            }
            nearFrontierX = x;
        }

        void SpawnNear(PropDef def, float x, float topY)
        {
            var size = KenneyProps.Size(def.kit, def.name);
            float h = Random.Range(def.hMin, def.hMax);
            float scale = h / Mathf.Max(0.01f, size.y);
            float depth = Mathf.Max(size.x, size.z) * scale;
            float z = NearZ + depth * 0.5f + h * 0.18f;
            float yaw = Random.Range(-35f, 35f);
            AddScenery(KenneyProps.Spawn(def.kit, def.name, entityParent, new Vector3(x, topY - 0.04f, z), scale, PropLayer.Near, yaw, -10f));
        }

        /// <summary>A fresh grave (mound + headstone). Zombies burst out of it as the player approaches.</summary>
        void PlaceGrave(float x, float topY)
        {
            var mound = KenneyProps.Size(G, "grave");
            float moundScale = 1.2f / Mathf.Max(0.01f, mound.z); // the mound's long side faces the camera
            AddScenery(KenneyProps.Spawn(G, "grave", entityParent, new Vector3(x, topY - 0.02f, NearZ + 0.45f), moundScale, PropLayer.Near, 90f, -12f));

            string stone = Random.value < 0.5f ? "gravestone-round" : "gravestone-cross";
            var s = KenneyProps.Size(G, stone);
            float h = Random.Range(0.6f, 0.8f);
            AddScenery(KenneyProps.Spawn(G, stone, entityParent, new Vector3(x - 0.55f, topY - 0.04f, NearZ + 0.9f), h / s.y, PropLayer.Near, Random.Range(-15f, 15f), -10f));

            graves.Add(new GraveTrigger { x = x, y = topY });
            lastGraveX = x;
        }

        void PlaceMidProps(float xStart, float width, float topY, PropDef[] palette)
        {
            if (palette == null) return;
            float x = Mathf.Max(xStart + 0.6f, midFrontierX);
            float end = xStart + width - 0.6f;
            while (x < end)
            {
                var def = Pick(palette);
                float footprint = MidFootprint(def);
                if (x + footprint > end) break;
                SpawnMid(def, x + footprint / 2f, topY, footprint);
                x += footprint + Random.Range(2.5f, 7f);
            }
            midFrontierX = x;
        }

        float MidFootprint(PropDef def)
        {
            var size = KenneyProps.Size(def.kit, def.name);
            float h = (def.hMin + def.hMax) * 0.5f;
            float scale = h / Mathf.Max(0.01f, size.y);
            return def.special switch
            {
                3 => 3 * size.x * scale,      // fence row
                4 => 2 * size.x * scale,      // wall row
                _ => Mathf.Max(size.x, size.z) * scale * 1.1f,
            };
        }

        void SpawnMid(PropDef def, float centerX, float topY, float footprint)
        {
            var size = KenneyProps.Size(def.kit, def.name);
            float h = Random.Range(def.hMin, def.hMax);
            float scale = h / Mathf.Max(0.01f, size.y);
            float z = Random.Range(MidZMin, MidZMax);
            float baseY = topY - 0.25f;

            switch (def.special)
            {
                case 2: // crypt with its roof: same rig pose, roof pivot raised by the wall height
                {
                    float yaw = Random.Range(-20f, 20f);
                    AddScenery(KenneyProps.Spawn(G, def.name, entityParent, new Vector3(centerX, baseY, z), scale, PropLayer.Mid, yaw, -6f));
                    var roofRig = KenneyProps.Spawn(G, def.name + "-roof", entityParent, new Vector3(centerX, baseY, z), scale, PropLayer.Mid, yaw, -6f);
                    if (roofRig != null)
                    {
                        roofRig.GetChild(0).localPosition = new Vector3(0f, size.y * scale, 0f);
                        AddScenery(roofRig);
                    }
                    break;
                }

                case 3: // iron fence row
                case 4: // stone wall row
                    int count = def.special == 3 ? 3 : 2;
                    float pieceWidth = size.x * scale;
                    float start = centerX - pieceWidth * (count - 1) / 2f;
                    for (int i = 0; i < count; i++)
                    {
                        string name = def.special == 3 && Random.value < 0.25f ? "iron-fence-damaged" : def.name;
                        AddScenery(KenneyProps.Spawn(G, name, entityParent, new Vector3(start + i * pieceWidth, baseY, z), scale, PropLayer.Mid, 0f, -6f));
                    }
                    break;

                default:
                    AddScenery(KenneyProps.Spawn(def.kit, def.name, entityParent, new Vector3(centerX, baseY, z), scale, PropLayer.Mid, Random.Range(-30f, 30f), -6f));
                    break;
            }
        }

        // ---- ascent tower, shaft, lake -------------------------------------------------------

        /// <summary>
        /// A castle tower behind the Doodle-Jump column, rising from the pit to exactly the
        /// roof height, crenellated on top. Returns false if the kits are missing (the caller
        /// keeps the flat silhouette).
        /// </summary>
        bool BuildCastleTower(float xStart, float baseY, float towerTop)
        {
            if (!UseKenney) return false;
            var mid = KenneyProps.Size(C, "tower-square-mid");
            var baseSize = KenneyProps.Size(C, "tower-square-base");
            var topSize = KenneyProps.Size(C, "tower-square-top");
            // Seen at a 20 degree angle a square tower shows cos+sin of its width.
            float scale = (TowerWidth + 0.8f) / (mid.x * 1.28f);
            float midH = mid.y * scale, baseH = baseSize.y * scale, topH = topSize.y * scale;
            float centerX = xStart + TowerWidth / 2f;
            float z = 2.2f + mid.x * scale * 0.7f;

            int mids = Mathf.Max(1, Mathf.CeilToInt((towerTop - topH - (baseY - 14f) - baseH) / midH));
            float y = towerTop - topH - mids * midH - baseH;

            AddScenery(KenneyProps.Spawn(C, "tower-square-base", entityParent, new Vector3(centerX, y, z), scale, PropLayer.Tower, 20f));
            var rock = CreateIslandSprite(TowerWidth * 1.4f, 9f, IslandTint(genZone.Ground), -2);
            rock.transform.SetParent(entityParent, false);
            rock.transform.position = new Vector3(centerX, y + 0.5f, 0f);
            scenery.Add(rock);
            y += baseH;
            for (int i = 0; i < mids; i++)
            {
                string piece = i % 2 == 1 ? "tower-square-mid-windows" : "tower-square-mid";
                AddScenery(KenneyProps.Spawn(C, piece, entityParent, new Vector3(centerX, y, z), scale, PropLayer.Tower, 20f));
                y += midH;
            }
            AddScenery(KenneyProps.Spawn(C, "tower-square-top", entityParent, new Vector3(centerX, y, z), scale, PropLayer.Tower, 20f));
            // A banner on the battlements, visible when arriving on the roof.
            var flag = KenneyProps.Size(C, "flag-pennant");
            AddScenery(KenneyProps.Spawn(C, "flag-pennant", entityParent, new Vector3(centerX - TowerWidth * 0.3f, towerTop, z - 1f), 2.6f / flag.y, PropLayer.Mid, 70f));
            return true;
        }

        /// <summary>Masonry inside the free-fall shaft (face-on so its edges match the shaft).</summary>
        bool BuildShaftMasonry(float xStart, float width, float top)
        {
            if (!UseKenney) return false;
            var mid = KenneyProps.Size(C, "tower-square-mid");
            float scale = (width + 1.2f) / mid.x;
            float h = mid.y * scale;
            float z = 2.4f + mid.z * scale * 0.5f;
            int row = 0;
            for (float y = -1f; y < top + 1f; y += h, row++)
            {
                string piece = row % 2 == 1 ? "tower-square-mid-windows" : "tower-square-mid";
                AddScenery(KenneyProps.Spawn(C, piece, entityParent, new Vector3(xStart + width / 2f, y, z), scale, PropLayer.Tower));
            }
            return true;
        }

        /// <summary>Drowned towers and dead trees sticking out of the Survol lake.</summary>
        void DecorateLakeStretch(float xStart, float step, float lakeSurface)
        {
            if (!UseKenney || Random.value > 0.45f) return;
            float x = xStart + Random.Range(0.5f, step - 0.5f);
            float z = Random.Range(MidZMin, MidZMax);
            if (Random.value < 0.5f)
            {
                var b = KenneyProps.Size(C, "tower-hexagon-base");
                float scale = Random.Range(1.6f, 2.4f);
                float y = lakeSurface - 1.2f;
                AddScenery(KenneyProps.Spawn(C, "tower-hexagon-base", entityParent, new Vector3(x, y, z), scale, PropLayer.Mid, Random.Range(-20f, 20f)));
                AddScenery(KenneyProps.Spawn(C, "tower-hexagon-roof", entityParent, new Vector3(x, y + b.y * scale, z), scale, PropLayer.Mid, Random.Range(-20f, 20f)));
            }
            else
            {
                var s = KenneyProps.Size(G, "pine-fall-crooked");
                AddScenery(KenneyProps.Spawn(G, "pine-fall-crooked", entityParent, new Vector3(x, lakeSurface - 0.8f, z), Random.Range(2.4f, 3.4f) / s.y, PropLayer.Mid, Random.Range(-30f, 30f)));
            }
        }

        // ---- far skyline (parallax) ------------------------------------------------------------

        enum FarKind { CastleTower, HexTower, WallRow, Viaduct, Trees, DeadTrees, Crypt, Siege }

        void GenerateFarSkyline()
        {
            if (farRoot == null || mainCamera == null) return;
            float camX = mainCamera.transform.position.x;
            while (farRoot.position.x + farFrontierLocal < camX + 35f)
            {
                float x = farFrontierLocal + Random.Range(3f, 9f);
                float width = SpawnFarGroup(PickFarKind(genZone.Kind), x);
                farFrontierLocal = x + width;
            }
        }

        static FarKind PickFarKind(ZoneKind zone)
        {
            float r = Random.value;
            switch (zone)
            {
                case ZoneKind.Highway: return r < 0.5f ? FarKind.Viaduct : r < 0.7f ? FarKind.CastleTower : r < 0.85f ? FarKind.Trees : FarKind.Siege;
                case ZoneKind.Infested: return r < 0.3f ? FarKind.Crypt : r < 0.6f ? FarKind.CastleTower : FarKind.Trees;
                case ZoneKind.Wasteland: return r < 0.5f ? FarKind.DeadTrees : r < 0.75f ? FarKind.Siege : FarKind.HexTower;
                case ZoneKind.Jetpack: return r < 0.4f ? FarKind.HexTower : r < 0.7f ? FarKind.DeadTrees : FarKind.CastleTower;
                case ZoneKind.Ascent:
                case ZoneKind.Rooftops:
                case ZoneKind.Descent:
                case ZoneKind.Shaft: return r < 0.45f ? FarKind.CastleTower : r < 0.7f ? FarKind.HexTower : FarKind.WallRow;
                default: return r < 0.35f ? FarKind.CastleTower : r < 0.6f ? FarKind.WallRow : r < 0.75f ? FarKind.HexTower : FarKind.Trees;
            }
        }

        /// <summary>Spawns one floating skyline island at local x (parallax space) and returns its width.</summary>
        float SpawnFarGroup(FarKind kind, float x)
        {
            float scale = Random.Range(1.0f, 1.6f);
            float z = Random.Range(14f, 24f);
            float y = Random.Range(2f, 6.5f);
            float yaw = Random.Range(-25f, 25f);
            int first = farScenery.Count;
            float width = SpawnFarContent(kind, x, y, z, scale, yaw);

            // Horizontal extent of what was just placed, in the parallax root's space.
            float minX = float.MaxValue, maxX = float.MinValue;
            for (int i = first; i < farScenery.Count; i++)
            {
                if (farScenery[i] == null) continue;
                foreach (var r in farScenery[i].GetComponentsInChildren<Renderer>())
                {
                    minX = Mathf.Min(minX, r.bounds.min.x);
                    maxX = Mathf.Max(maxX, r.bounds.max.x);
                }
            }
            if (minX > maxX) return width;
            float rootX = farRoot.position.x;
            float span = maxX - minX;

            // The rock it floats on, fogged like the models so it melts into the painted sky.
            var fog = ZoneCatalog.Get(genZone.Kind).Sky;
            var rockTint = Color.Lerp(IslandTint(genZone.Ground), fog, 0.62f);
            var rock = CreateIslandSprite(span * 1.15f + 0.4f, Mathf.Clamp(span * 0.7f, 1.2f, 5f), rockTint, -6);
            rock.transform.SetParent(farRoot, false);
            rock.transform.localPosition = new Vector3((minX + maxX) * 0.5f - rootX, y + 0.15f, z);
            farScenery.Add(rock);
            return Mathf.Max(width, span);
        }

        float SpawnFarContent(FarKind kind, float x, float y, float z, float scale, float yaw)
        {

            switch (kind)
            {
                case FarKind.CastleTower:
                {
                    var b = KenneyProps.Size(C, "tower-square-base");
                    var m = KenneyProps.Size(C, "tower-square-mid");
                    float cy = y;
                    Far(C, "tower-square-base", x, cy, z, scale, yaw); cy += b.y * scale;
                    int mids = Random.Range(0, 3);
                    for (int i = 0; i < mids; i++) { Far(C, i == mids - 1 ? "tower-square-mid-windows" : "tower-square-mid", x, cy, z, scale, yaw); cy += m.y * scale; }
                    string roof = Random.value < 0.4f ? "tower-square-top-roof" : Random.value < 0.5f ? "tower-square-top-roof-high-windows" : "tower-square-roof";
                    Far(C, roof, x, cy, z, scale, yaw);
                    return b.x * scale * 1.3f;
                }
                case FarKind.HexTower:
                {
                    var b = KenneyProps.Size(C, "tower-hexagon-base");
                    var m = KenneyProps.Size(C, "tower-hexagon-mid");
                    float cy = y;
                    Far(C, "tower-hexagon-base", x, cy, z, scale, yaw); cy += b.y * scale;
                    if (Random.value < 0.6f) { Far(C, "tower-hexagon-mid", x, cy, z, scale, yaw); cy += m.y * scale; }
                    Far(C, "tower-hexagon-roof", x, cy, z, scale, yaw);
                    return b.x * scale * 1.2f;
                }
                case FarKind.WallRow:
                {
                    // Low pieces need extra size to clear the street line they stand behind.
                    var w = KenneyProps.Size(C, "wall");
                    float s = scale * 1.1f;
                    int n = Random.Range(2, 5);
                    for (int i = 0; i < n; i++) Far(C, "wall", x + i * w.x * s, y, z, s, 0f);
                    return n * w.x * s;
                }
                case FarKind.Viaduct:
                {
                    var w = KenneyProps.Size(C, "bridge-straight");
                    float s = scale * 1.2f;
                    int n = Random.Range(3, 6);
                    for (int i = 0; i < n; i++) Far(C, i % 3 == 2 ? "bridge-straight-pillar" : "bridge-straight", x + i * w.x * s, y, z, s, 0f);
                    return n * w.x * s;
                }
                case FarKind.Crypt:
                {
                    var b = KenneyProps.Size(G, "crypt-large");
                    float s = scale * 1.4f;
                    Far(G, "crypt-large", x, y, z, s, yaw); // roof below shares the pose
                    var roof = Far(G, "crypt-large-roof", x, y, z, s, yaw);
                    if (roof != null) roof.GetChild(0).localPosition = new Vector3(0f, b.y * s, 0f);
                    return b.x * s * 1.3f;
                }
                case FarKind.Siege:
                {
                    string name = Random.value < 0.5f ? "siege-trebuchet" : "siege-tower";
                    var s = KenneyProps.Size(C, name);
                    Far(C, name, x, y, z, scale * 1.3f, yaw);
                    return s.x * scale * 1.3f;
                }
                case FarKind.DeadTrees:
                case FarKind.Trees:
                default:
                {
                    int n = Random.Range(2, 4);
                    float cx = x;
                    for (int i = 0; i < n; i++)
                    {
                        string name = kind == FarKind.DeadTrees
                            ? (Random.value < 0.5f ? "pine-fall-crooked" : "pine-crooked")
                            : (Random.value < 0.5f ? "pine" : "tree-large");
                        var kit = name == "tree-large" ? C : G;
                        var s = KenneyProps.Size(kit, name);
                        float ts = scale * Random.Range(1.1f, 1.6f);
                        Far(kit, name, cx, y, z + Random.Range(-1f, 1f), ts, Random.Range(0f, 360f));
                        cx += s.x * ts * 0.8f;
                    }
                    return cx - x;
                }
            }
        }

        Transform Far(PropKit kit, string name, float x, float y, float z, float scale, float yaw)
        {
            var t = KenneyProps.Spawn(kit, name, farRoot, new Vector3(x, y, z), scale, PropLayer.Far, yaw);
            if (t != null) farScenery.Add(t.gameObject);
            return t;
        }

        /// <summary>Moves the skyline at half the camera's speed (parallax).</summary>
        internal void UpdateFarLayer()
        {
            if (!running || farRoot == null || mainCamera == null) return;
            farRoot.position = new Vector3(mainCamera.transform.position.x * FarParallax, 0f, 0f);
        }

        // ---- graves ------------------------------------------------------------------------

        /// <summary>Zombies burst out of cemetery graves just ahead of the player.</summary>
        void UpdateGraves()
        {
            if (ascentActive || player.jetpackActive) return;
            float px = player.transform.position.x;
            for (int i = 0; i < graves.Count; i++)
            {
                var g = graves[i];
                if (g.used) continue;
                float ahead = g.x - px;
                if (ahead > 6.5f || ahead < 1.8f) continue;
                g.used = true;
                graves[i] = g;
                if (!IsWalkable(g.x)) continue;
                // Skeletons claw their way out; now and then a ghost rises instead.
                var kind = Random.value < 0.35f ? ZombieKind.Spitter
                    : Difficulty > 1.6f && Random.value < 0.3f ? ZombieKind.Runner : ZombieKind.Walker;
                SpawnZombie(g.x, kind, rising: true);
                Fx.Burst(new Vector3(g.x, g.y + 0.1f, 0f), new Color(0.35f, 0.26f, 0.18f), 18, 3f, 0.11f);
                Fx.Shake(0.12f, 0.15f);
                Sfx.Drop();
            }
        }

        // ---- helpers -------------------------------------------------------------------------

        void AddScenery(Transform t)
        {
            if (t != null) scenery.Add(t.gameObject);
        }

        static PropDef Pick(PropDef[] palette)
        {
            float total = 0f;
            foreach (var d in palette) total += d.weight;
            float r = Random.value * total;
            foreach (var d in palette)
            {
                r -= d.weight;
                if (r <= 0f) return d;
            }
            return palette[palette.Length - 1];
        }
    }

    /// <summary>
    /// Runs after Cinemachine has moved the camera (late execution order) so the parallax
    /// skyline never lags a frame behind it.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public class FarLayerFollower : MonoBehaviour
    {
        public SurvivalDirector director;

        void LateUpdate()
        {
            if (director != null) director.UpdateFarLayer();
        }
    }
}
