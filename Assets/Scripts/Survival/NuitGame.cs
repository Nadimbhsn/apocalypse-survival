using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Platformer.Survival
{
    /// <summary>
    /// "DERNIÈRE NUIT": the hero is trapped in an abandoned petrol station on the road trip,
    /// and the dead come in waves - more of them, and worse, every time. Seen from above:
    /// the joystick moves him, he fires on his own at the nearest of the dead in sight.
    ///
    /// The dead come up the road and break down the barricaded doors before they pour in.
    /// Each kill pays scrap; between waves it buys door repairs, guns, gadgets (a turret, a
    /// shield, a dodge) and the station's other rooms - more space, more loot, but more doors
    /// to hold. After each wave a bonus is chosen among three, so no two nights are alike.
    /// Every fifth wave a boss comes; every tenth, the approaching planet shakes the night
    /// (gravity fails, debris rains, the power goes out). One second chance - the second fall
    /// ends the night and its score joins the leaderboard.
    ///
    /// The station lives far from the other games (around x = -6000, y = 6000), painted
    /// (Resources/Nuit), seen in three-quarter view: whatever stands lower on screen is
    /// drawn in front.
    /// </summary>
    public partial class NuitGame : MiniGame
    {
        public override string Id => "nuit";
        public override string Title => "DERNIÈRE NUIT";
        public override string Description => "Piégé dans la station-service : tiens jusqu'à l'aube";
        public override string BestLine => BestWave > 0 ? $"Record : vague {BestWave}" : "Aucune nuit tenue";

        static readonly Vector2 Origin = new Vector2(-6000f, 6000f);
        const float PlayerRadius = 0.32f;

        enum Phase { Menu, Wave, Bonus, Shop, Over }
        Phase phase = Phase.Menu;

        Transform root;
        Zone[] zones;
        readonly List<Door> doors = new();
        readonly List<Rect> walls = new();
        /// <summary>The walls that stop bullets and sight: brick and metal, not the chain-link fences.</summary>
        readonly List<Rect> sightWalls = new();
        readonly List<Rect> obstacles = new();
        readonly List<(SpriteRenderer sr, int zone)> lights = new();
        readonly List<(SpriteRenderer sr, int zone)> loots = new();

        // ---- the hero ------------------------------------------------------------------------
        Transform hero;
        SpriteRenderer heroSr, gunSr, shieldSr, shadowSr;
        Transform gunPivot;
        Vector2 heroPos, heroVel, aimDir = Vector2.right;
        float hp, maxHp, shield, invuln, hurtFlash, anim, kick;
        float fireTimer, regenAcc;
        float dashTimer, dashCooldown, shieldIdle;
        Vector2 dashDir;
        bool secondChanceUsed, facingLeft;
        int gunIndex;
        readonly bool[] gunsOwned = new bool[5];
        readonly int[] gadget = new int[3];

        // ---- run state ---------------------------------------------------------------------------
        int wave, scrap, kills, bosses, toSpawn, spawned;
        float spawnTimer, waveClock;
        NightEvent nightEvent;
        readonly int[] perkStacks = new int[16];
        float scrapCarry;

        // ---- the dead ------------------------------------------------------------------------------
        class Foe
        {
            public FoeKind Kind;
            public Transform T;
            public SpriteRenderer Sr;
            public Sprite[] Frames;
            public Vector2 Pos, Vel;
            public float Radius, Speed, Hp, MaxHp, Dmg, DoorDmg, Attack, Anim, Flash, Scale;
            public int Scrap, Door = -1;
            public bool Inside, Dead;
            public float Charge, ChargeWindup, Summon;
            public Vector2 ChargeDir;
            public float Bob;
        }
        readonly List<Foe> foes = new();
        Foe boss;

        class Shot
        {
            public Transform T;
            public SpriteRenderer Sr;
            public Vector2 Pos, Vel;
            public float Dmg, Life, Splash;
            public int Pierce;
            public readonly List<Foe> Hit = new();
            public bool Live;
        }
        readonly List<Shot> shots = new();

        class Turret
        {
            public Vector2 Pos;
            public Transform Gun;
            public SpriteRenderer Base;
            public float Timer;
        }
        readonly List<Turret> turrets = new();

        class Meteor
        {
            public Vector2 Pos;
            public float T, Fall;
            public SpriteRenderer Shadow, Rock;
        }
        readonly List<Meteor> meteors = new();
        float meteorTimer;
        SpriteRenderer darkness, nightTint;
        readonly List<(Transform t, float speed, float phase)> motes = new();

        // ---- view -------------------------------------------------------------------------------
        float shake;
        Vector3 camPos;

        static Vector3 W(Vector2 local, float z = 0f) => new Vector3(Origin.x + local.x, Origin.y + local.y, z);
        static int Depth(float y) => 1000 - Mathf.RoundToInt(y * 20f);

        // ======================================================================================
        // lifecycle
        // ======================================================================================

        protected override void OnEnter()
        {
            Fx.OrderBoost = 3100;
            BuildWorld();
            FrameCamera(new Vector2(0f, -1f), true);
            ShowMenu();
        }

        protected override void OnExit()
        {
            // leaving keeps the night (see NuitGame.Save)
            SaveRun();
            phase = Phase.Menu;
            Fx.OrderBoost = 0;
            NuitPad.Release();
            if (root != null) Destroy(root.gameObject);
            root = null;
            foes.Clear(); shots.Clear(); turrets.Clear(); meteors.Clear(); doors.Clear(); motes.Clear();
            lights.Clear(); loots.Clear();
            boss = null;
        }

        /// <summary>Quitting keeps the night: REPRENDRE picks it up before the wave that was running.</summary>
        void QuitNight()
        {
            SaveRun();
            phase = Phase.Menu;
            ReturnToHub();
        }

        void StartNight()
        {
            ClearSave();
            ClearRun();
            wave = 0; scrap = 0; kills = 0; bosses = 0; scrapCarry = 0f;
            for (int i = 0; i < perkStacks.Length; i++) perkStacks[i] = 0;
            for (int i = 0; i < gadget.Length; i++) gadget[i] = 0;
            for (int i = 0; i < gunsOwned.Length; i++) gunsOwned[i] = i == 0;
            gunIndex = 0;
            maxHp = 100f; hp = maxHp; shield = 0f;
            secondChanceUsed = false;
            invuln = 0f; dashTimer = 0f; dashCooldown = 0f;
            heroPos = new Vector2(0f, -1.5f); heroVel = Vector2.zero;
            zones = NewZones();
            BuildWorld();
            hero.gameObject.SetActive(true);
            RefreshGun();
            NextWave();
        }

        void ClearRun()
        {
            foreach (var f in foes) if (f.T != null) Destroy(f.T.gameObject);
            foes.Clear();
            foreach (var s in shots) if (s.T != null) Destroy(s.T.gameObject);
            shots.Clear();
            foreach (var t in turrets) if (t.Base != null) Destroy(t.Base.gameObject);
            turrets.Clear();
            foreach (var m in meteors) { if (m.Shadow != null) Destroy(m.Shadow.gameObject); if (m.Rock != null) Destroy(m.Rock.gameObject); }
            meteors.Clear();
            boss = null;
        }

        // ======================================================================================
        // the station
        // ======================================================================================

        static readonly Dictionary<string, Sprite> art = new();

        static Sprite Art(string name, float pivotY = 0f, float ppu = 128f)
        {
            string key = name + "|" + pivotY + "|" + ppu;
            if (art.TryGetValue(key, out var s)) return s;
            var tex = Resources.Load<Texture2D>("Nuit/" + name);
            if (tex == null) { art[key] = null; return null; }
            s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, pivotY), ppu, 0, SpriteMeshType.FullRect);
            art[key] = s;
            return s;
        }

        /// <summary>The painted prop sheet: feet pivots measured by the painter.</summary>
        static readonly Dictionary<string, float> Feet = new()
        {
            ["pump"] = 0.05f, ["pillar"] = 0.037f, ["lamp"] = 0.027f, ["shelf"] = 0.05f, ["counter"] = 0.0556f, ["fridge"] = 0.0417f,
            ["car_red"] = 0.0233f, ["car_blue"] = 0.0233f, ["car_white"] = 0.04f, ["car_green"] = 0.04f, ["workbench"] = 0.05f,
            ["lift"] = 0.0213f, ["tyres"] = 0.0714f, ["barrel_red"] = 0.0741f, ["barrel_blue"] = 0.0741f, ["crate"] = 0.08f,
            ["crate_loot"] = 0.08f, ["dumpster"] = 0.05f, ["trash"] = 0.1111f, ["sign"] = 0.0196f, ["turret"] = 0.1f,
        };

        SpriteRenderer Put(string name, Sprite sprite, Vector2 pos, int order, Transform parent = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent != null ? parent : root, false);
            go.transform.position = W(pos);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            return sr;
        }

        SpriteRenderer Floor(string tile, Rect r, int order)
        {
            var sr = Put("Floor_" + tile, Art(tile, 0.5f), r.center, order);
            if (sr.sprite == null) return sr;
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.size = r.size;
            return sr;
        }

        void BuildWorld()
        {
            if (root != null) Destroy(root.gameObject);
            root = new GameObject("NuitWorld").transform;
            zones ??= NewZones();
            walls.Clear(); sightWalls.Clear(); obstacles.Clear(); doors.Clear(); lights.Clear(); loots.Clear(); motes.Clear();

            // ground, road, floors
            Floor("floor_ground", WorldBounds, -2000);
            Floor("floor_road", new Rect(WorldBounds.xMin, -14f, WorldBounds.width, 4f), -1990);
            Floor("floor_asphalt", zones[0].Area, -1980);
            Floor("floor_tiles", zones[1].Area, -1980);
            Floor("floor_concrete", zones[2].Area, -1980);
            Floor("floor_asphalt", zones[3].Area, -1980);
            var line = PlaceholderVisuals.Square(Color.white);
            for (float x = -18f; x < -9f; x += 2f)
                foreach (var (y0, y1) in new[] { (-7.5f, -4.5f), (2.5f, 5.5f) })
                {
                    var l = Put("ParkLine", line, new Vector2(x, (y0 + y1) / 2f), -1970);
                    l.transform.localScale = new Vector3(0.08f, y1 - y0, 1f);
                    l.color = new Color(0.86f, 0.78f, 0.47f, 0.45f);
                }
            var rnd = new System.Random(7);
            for (int i = 0; i < 14; i++)
            {
                var p = new Vector2((float)(rnd.NextDouble() * 34 - 18), (float)(rnd.NextDouble() * 20 - 8));
                var st = Put("Stain", Art("stain", 0.5f), p, -1960);
                st.transform.localScale = Vector3.one * (0.8f + (float)rnd.NextDouble());
            }

            // walls
            foreach (var w in WallPlan) BuildWall(w.kind, w.horizontal, w.at, w.from, w.to, w.gaps);

            // doors
            for (int i = 0; i < DoorPlan.Length; i++)
            {
                var d = DoorPlan[i];
                var door = new Door { Pos = d.pos, Horizontal = d.horizontal, Out = d.outward, Zone = d.zone };
                door.MaxHp = DoorMaxHp; door.Hp = door.MaxHp;
                door.Sr = d.horizontal
                    ? Put("Door", Art("door_h_0", 0f), d.pos + new Vector2(0f, -0.2f), Depth(d.pos.y - 0.2f) + 1)
                    : Put("Door", Art("door_v_0", 0.5f), d.pos, -1490);
                doors.Add(door);
                SetDoorLook(door);
            }

            // zone shutters and veils
            for (int z = 1; z < zones.Length; z++)
            {
                var zone = zones[z];
                zone.Shutter = zone.GateHorizontal
                    ? Put("Shutter", Art("shutter_h", 0f), zone.Gate + new Vector2(0f, -0.2f), Depth(zone.Gate.y - 0.2f) + 1)
                    : Put("Shutter", Art("shutter_v", 0.5f), zone.Gate, -1490);
                zone.Veil = Put("Veil", line, zone.Area.center, 2950);
                zone.Veil.transform.localScale = new Vector3(zone.Area.width, zone.Area.height + 1.2f, 1f);
                zone.Veil.color = new Color(0.02f, 0.02f, 0.06f, 0.72f);
                zone.Shutter.gameObject.SetActive(!zone.Open);
                zone.Veil.gameObject.SetActive(!zone.Open);
            }

            // props
            foreach (var p in PropPlan)
            {
                var sr = Put(p.sprite, Art(p.sprite, Feet.TryGetValue(p.sprite, out var f) ? f : 0f), p.pos, Depth(p.pos.y));
                if (p.foot.width > 0f) obstacles.Add(new Rect(p.pos.x + p.foot.x, p.pos.y + p.foot.y, p.foot.width, p.foot.height));
            }
            for (int z = 1; z < LootPlan.Length; z++)
            {
                var c = Put("Loot", Art("crate_loot", Feet["crate_loot"]), LootPlan[z], Depth(LootPlan[z].y));
                loots.Add((c, z));
                c.gameObject.SetActive(!zones[z].Open);
            }

            BuildGrid();

            // night: a cold tint over everything, warm pools from the lamps
            nightTint = Put("Night", line, WorldBounds.center, 2900);
            nightTint.transform.localScale = new Vector3(WorldBounds.width + 20f, WorldBounds.height + 20f, 1f);
            nightTint.color = new Color(0.03f, 0.04f, 0.16f, 0.38f);
            var glowTex = Resources.Load<Texture2D>("Bastion/glow");
            var glow = glowTex != null ? Sprite.Create(glowTex, new Rect(0, 0, glowTex.width, glowTex.height), new Vector2(0.5f, 0.5f), glowTex.width) : PlaceholderVisuals.Circle(Color.white);
            foreach (var l in LightPlan)
            {
                var g = Put("Light", glow, l.pos, 3000);
                g.transform.localScale = new Vector3(l.size, l.size * 0.8f, 1f);
                g.color = new Color(1f, 0.68f, 0.32f, 0.32f);
                lights.Add((g, l.zone));
            }
            RefreshLights();

            // the hero
            var heroGo = new GameObject("Hero");
            heroGo.transform.SetParent(root, false);
            hero = heroGo.transform;
            shadowSr = Put("Shadow", PlaceholderVisuals.Circle(Color.white), Vector2.zero, -1500, hero);
            shadowSr.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            shadowSr.transform.localScale = new Vector3(0.7f, 0.25f, 1f);
            shadowSr.color = new Color(0f, 0f, 0.05f, 0.45f);
            var body = new GameObject("Body");
            body.transform.SetParent(hero, false);
            heroSr = body.AddComponent<SpriteRenderer>();
            heroSr.sprite = HeroSprites.Get("idle", 0);
            var gp = new GameObject("GunPivot");
            gp.transform.SetParent(hero, false);
            gunPivot = gp.transform;
            var gun = new GameObject("Gun");
            gun.transform.SetParent(gunPivot, false);
            gunSr = gun.AddComponent<SpriteRenderer>();
            shieldSr = Put("Shield", PlaceholderVisuals.RimCircle(new Color(0.55f, 0.85f, 1f)), Vector2.zero, 0, hero);
            shieldSr.transform.localPosition = new Vector3(0f, 0.55f, 0f);
            shieldSr.transform.localScale = Vector3.one * 1.5f;
            hero.position = W(heroPos);
            hero.gameObject.SetActive(phase != Phase.Menu);
            RefreshGun();
        }

        void BuildWall(string kind, bool horizontal, float at, float from, float to, float[] gaps)
        {
            var segs = new List<(float a, float b)> { (from, to) };
            for (int g = 0; g + 1 < gaps.Length; g += 2)
            {
                float g0 = gaps[g], g1 = gaps[g + 1];
                var next = new List<(float a, float b)>();
                foreach (var (a, b) in segs)
                {
                    if (g1 <= a || g0 >= b) { next.Add((a, b)); continue; }
                    if (g0 > a) next.Add((a, g0));
                    if (g1 < b) next.Add((g1, b));
                }
                segs = next;
            }
            foreach (var (a, b) in segs)
            {
                if (b - a < 0.05f) continue;
                if (horizontal)
                {
                    var sr = Put("Wall", Art("wall_h_" + kind, 0f), new Vector2((a + b) / 2f, at - 0.2f), Depth(at - 0.2f));
                    if (sr.sprite != null) { sr.drawMode = SpriteDrawMode.Tiled; sr.size = new Vector2(b - a, sr.sprite.bounds.size.y); }
                    walls.Add(new Rect(a, at - 0.2f, b - a, 0.4f));
                    if (kind != "fence") sightWalls.Add(walls[walls.Count - 1]);
                }
                else
                {
                    var sr = Put("Wall", Art("wall_v_" + kind, 0f), new Vector2(at, a - 0.2f), -1495);
                    if (sr.sprite != null) { sr.drawMode = SpriteDrawMode.Tiled; sr.size = new Vector2(0.4f, b - a + 1.2f); }
                    walls.Add(new Rect(at - 0.2f, a, 0.4f, b - a));
                    if (kind != "fence") sightWalls.Add(walls[walls.Count - 1]);
                }
            }
        }

        float DoorMaxHp => 140f * (1f + 0.4f * perkStacks[(int)Perk.Doors]);

        void SetDoorLook(Door d)
        {
            bool active = zones[d.Zone].Open;
            d.Sr.gameObject.SetActive(active && d.Intact);
            int state = d.Hp > d.MaxHp * 0.66f ? 0 : d.Hp > d.MaxHp * 0.33f ? 1 : 2;
            if (state == d.State) return;
            d.State = state;
            d.Sr.sprite = d.Horizontal ? Art($"door_h_{state}", 0f) : Art($"door_v_{state}", 0.5f);
        }

        void RefreshLights()
        {
            bool blackout = phase == Phase.Wave && nightEvent == NightEvent.Blackout;
            foreach (var (sr, z) in lights) sr.gameObject.SetActive(!blackout && (z < 0 || zones[z].Open));
        }

        Rect DoorRect(Door d) => d.Horizontal ? new Rect(d.Pos.x - 1.2f, d.Pos.y - 0.2f, 2.4f, 0.4f) : new Rect(d.Pos.x - 0.2f, d.Pos.y - 1.2f, 0.4f, 2.4f);

        Rect GateRect(Zone z) => z.GateHorizontal ? new Rect(z.Gate.x - 1.2f, z.Gate.y - 0.2f, 2.4f, 0.4f) : new Rect(z.Gate.x - 0.2f, z.Gate.y - 1.5f, 0.4f, 3f);

        /// <summary>Which room a point is in: 0..3, or -1 outside.</summary>
        int RoomOf(Vector2 p)
        {
            for (int z = 0; z < zones.Length; z++) if (zones[z].Area.Contains(p)) return z;
            return -1;
        }

        // ======================================================================================
        // movement and collisions
        // ======================================================================================

        /// <summary>Pushes a circle out of every wall, closed door or shutter, and (unless it floats) the props.</summary>
        Vector2 Collide(Vector2 p, float r, bool props, bool ghost)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                if (!ghost)
                {
                    foreach (var w in walls) p = PushOut(p, r, w);
                    foreach (var d in doors) if (d.Intact || !zones[d.Zone].Open) p = PushOut(p, r, DoorRect(d));
                    for (int z = 1; z < zones.Length; z++) if (!zones[z].Open) p = PushOut(p, r, GateRect(zones[z]));
                }
                if (props) foreach (var o in obstacles) p = PushOut(p, r, o);
            }
            p.x = Mathf.Clamp(p.x, WorldBounds.xMin + 1f, WorldBounds.xMax - 1f);
            p.y = Mathf.Clamp(p.y, WorldBounds.yMin + 1f, WorldBounds.yMax - 1f);
            return p;
        }

        static Vector2 PushOut(Vector2 p, float r, Rect box)
        {
            float cx = Mathf.Clamp(p.x, box.xMin, box.xMax), cy = Mathf.Clamp(p.y, box.yMin, box.yMax);
            var d = p - new Vector2(cx, cy);
            float sq = d.sqrMagnitude;
            if (sq >= r * r) return p;
            if (sq > 1e-6f) return new Vector2(cx, cy) + d / Mathf.Sqrt(sq) * r;
            // centre inside the box: leave by the nearest side
            float l = p.x - box.xMin, rr = box.xMax - p.x, b = p.y - box.yMin, t = box.yMax - p.y;
            float m = Mathf.Min(Mathf.Min(l, rr), Mathf.Min(b, t));
            if (m == l) return new Vector2(box.xMin - r, p.y);
            if (m == rr) return new Vector2(box.xMax + r, p.y);
            if (m == b) return new Vector2(p.x, box.yMin - r);
            return new Vector2(p.x, box.yMax + r);
        }

        /// <summary>True when nothing solid stands between two points (bullets and sight go through the fences' mesh).</summary>
        bool Clear(Vector2 a, Vector2 b)
        {
            foreach (var w in sightWalls) if (SegHits(a, b, w)) return false;
            foreach (var d in doors) if ((d.Intact || !zones[d.Zone].Open) && SegHits(a, b, DoorRect(d))) return false;
            for (int z = 1; z < zones.Length; z++) if (!zones[z].Open && SegHits(a, b, GateRect(zones[z]))) return false;
            return true;
        }

        static bool SegHits(Vector2 a, Vector2 b, Rect r)
        {
            // slab test
            float t0 = 0f, t1 = 1f;
            var d = b - a;
            for (int axis = 0; axis < 2; axis++)
            {
                float o = axis == 0 ? a.x : a.y, dd = axis == 0 ? d.x : d.y;
                float mn = axis == 0 ? r.xMin : r.yMin, mx = axis == 0 ? r.xMax : r.yMax;
                if (Mathf.Abs(dd) < 1e-6f) { if (o < mn || o > mx) return false; continue; }
                float u0 = (mn - o) / dd, u1 = (mx - o) / dd;
                if (u0 > u1) (u0, u1) = (u1, u0);
                t0 = Mathf.Max(t0, u0); t1 = Mathf.Min(t1, u1);
                if (t0 > t1) return false;
            }
            return true;
        }

        // ======================================================================================
        // the frame
        // ======================================================================================

        void Update()
        {
            if (!IsActive || root == null) return;
            float dt = Time.deltaTime;
            if (phase == Phase.Wave && dt > 0f)
            {
                UpdateHero(dt);
                UpdateSpawning(dt);
                UpdateField(dt);
                UpdateFoes(dt);
                UpdateShots(dt);
                UpdateTurrets(dt);
                UpdateEvent(dt);
                UpdateDoors(dt);
                if (toSpawn <= 0 && foes.Count == 0) WaveCleared();
            }
            UpdateZoneShutters(Time.unscaledDeltaTime);
            DrawHero(dt);
            UpdateHud();
            UpdateShopToast();
            FrameCamera(heroPos, false);
        }

        // ---- the hero ------------------------------------------------------------------------

        Vector2 MoveInput()
        {
            var v = NuitPad.Move;
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed || kb.qKey.isPressed) v.x -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) v.x += 1f;
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed || kb.zKey.isPressed) v.y += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) v.y -= 1f;
                if (kb.spaceKey.wasPressedThisFrame || kb.leftShiftKey.wasPressedThisFrame) TryDodge();
            }
            var pad = Gamepad.current;
            if (pad != null)
            {
                v += pad.leftStick.ReadValue();
                if (pad.buttonSouth.wasPressedThisFrame) TryDodge();
            }
            return Vector2.ClampMagnitude(v, 1f);
        }

        float MoveSpeed => 4.1f * (1f + 0.1f * perkStacks[(int)Perk.Speed]);

        void UpdateHero(float dt)
        {
            var input = MoveInput();
            float traction = nightEvent == NightEvent.LowGravity ? 2.2f : 18f;
            if (dashTimer > 0f)
            {
                dashTimer -= dt;
                heroVel = dashDir * 13f;
            }
            else heroVel = Vector2.Lerp(heroVel, input * MoveSpeed, 1f - Mathf.Exp(-traction * dt));
            heroPos = Collide(heroPos + heroVel * dt, PlayerRadius, true, false);
            if (input.sqrMagnitude > 0.01f) facingLeft = input.x < -0.05f || (facingLeft && input.x <= 0.05f);

            invuln -= dt; hurtFlash -= dt; dashCooldown -= dt;
            kick = Mathf.MoveTowards(kick, 0f, dt * 7f);

            // shield recharges after a calm moment
            int sl = gadget[(int)Gadget.Shield];
            if (sl > 0)
            {
                shieldIdle += dt;
                float cap = ShieldCap;
                if (shieldIdle > 4f) shield = Mathf.Min(cap, shield + dt * 12f);
            }
            if (perkStacks[(int)Perk.Regen] > 0)
            {
                regenAcc += dt * perkStacks[(int)Perk.Regen];
                if (regenAcc >= 1f) { hp = Mathf.Min(maxHp, hp + Mathf.Floor(regenAcc)); regenAcc -= Mathf.Floor(regenAcc); }
            }

            // holding TIR (or the mouse button, F, Enter, a pad's trigger) fires; the gun finds
            // the nearest of the dead in sight by itself, or shoots straight ahead
            var g = Guns[gunIndex];
            fireTimer -= dt;
            bool fire = NuitPad.FireHeld;
            var kb = Keyboard.current;
            if (kb != null && (kb.fKey.isPressed || kb.enterKey.isPressed || kb.jKey.isPressed || kb.leftCtrlKey.isPressed)) fire = true;
            var gp = Gamepad.current;
            if (gp != null && (gp.rightTrigger.isPressed || gp.buttonWest.isPressed)) fire = true;
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.isPressed
                && (UnityEngine.EventSystems.EventSystem.current == null || !UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())) fire = true;
            var target = NearestFoe(heroPos + Vector2.up * 0.5f, g.Range * RangeMult, true);
            if (target != null) aimDir = (target.Pos + Vector2.up * 0.45f * target.Scale - (heroPos + Vector2.up * 0.5f)).normalized;
            else if (heroVel.sqrMagnitude > 0.1f) aimDir = heroVel.normalized;
            else if (aimDir.sqrMagnitude < 0.01f) aimDir = facingLeft ? Vector2.left : Vector2.right;
            aimTarget = target != null;
            aiming = fire;
            if (fire && fireTimer <= 0f)
            {
                fireTimer = 1f / (g.Rate * (1f + 0.15f * perkStacks[(int)Perk.Rate]));
                FireGun(g);
            }
        }

        float RangeMult => 1f + 0.15f * perkStacks[(int)Perk.Range];
        float ShieldCap => gadget[(int)Gadget.Shield] switch { 1 => 25f, 2 => 45f, 3 => 70f, _ => 0f };

        Foe NearestFoe(Vector2 from, float range, bool needSight)
        {
            Foe best = null;
            float bestD = range * range;
            foreach (var f in foes)
            {
                if (f.Dead) continue;
                float d = (f.Pos - from).sqrMagnitude;
                if (d >= bestD) continue;
                if (needSight && !Clear(from, f.Pos + Vector2.up * 0.4f)) continue;
                bestD = d; best = f;
            }
            return best;
        }

        void FireGun(Gun g)
        {
            var muzzle = MuzzleWorld();
            int count = g.Count + perkStacks[(int)Perk.Multi];
            float dmgMult = 1f + 0.2f * perkStacks[(int)Perk.Damage];
            float baseAngle = Mathf.Atan2(aimDir.y, aimDir.x) * Mathf.Rad2Deg;
            for (int i = 0; i < count; i++)
            {
                float spread = (count > 1 ? (i - (count - 1) / 2f) * 7f : 0f) + Random.Range(-g.Spread, g.Spread);
                float a = (baseAngle + spread) * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                float dmg = g.Damage * dmgMult;
                bool crit = Random.value < 0.1f * perkStacks[(int)Perk.Crit];
                if (crit) dmg *= 2.5f;
                SpawnShot(muzzle, dir, g.Speed * (nightEvent == NightEvent.LowGravity ? 0.6f : 1f), dmg, g.Range * RangeMult, g.Pierce + perkStacks[(int)Perk.Pierce], g.Splash,
                    crit ? new Color(1f, 0.4f, 0.3f) : g.Splash > 0f ? new Color(0.6f, 0.8f, 0.35f) : new Color(1f, 0.86f, 0.45f), g.Splash > 0f ? 0.28f : 0.16f);
            }
            kick = 1f;
            if (g.Splash > 0f) Sfx.Attack(); else Sfx.Shoot();
            Fx.Burst(W(muzzle), new Color(1f, 0.8f, 0.4f), 3, 1.4f, 0.05f, 0f);
        }

        Vector2 MuzzleWorld()
        {
            if (gunSr == null || gunSr.sprite == null) return heroPos + Vector2.up * 0.5f;
            var p = gunSr.transform.TransformPoint(WeaponCatalog.MuzzleOffset(ArtOf(Guns[gunIndex])));
            return new Vector2(p.x - Origin.x, p.y - Origin.y);
        }

        void SpawnShot(Vector2 from, Vector2 dir, float speed, float dmg, float range, int pierce, float splash, Color col, float size)
        {
            Shot s = null;
            foreach (var x in shots) if (!x.Live) { s = x; break; }
            if (s == null)
            {
                s = new Shot();
                var go = new GameObject("Shot");
                go.transform.SetParent(root, false);
                s.T = go.transform;
                s.Sr = go.AddComponent<SpriteRenderer>();
                s.Sr.sprite = PlaceholderVisuals.Circle(Color.white);
                s.Sr.sortingOrder = 3100;
                shots.Add(s);
            }
            s.Live = true;
            s.T.gameObject.SetActive(true);
            s.Pos = from; s.Vel = dir * speed; s.Dmg = dmg; s.Life = range / speed; s.Pierce = pierce; s.Splash = splash;
            s.Hit.Clear();
            s.Sr.color = col;
            s.T.localScale = new Vector3(size * 1.6f, size, 1f);
            s.T.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
            s.T.position = W(s.Pos, 0f);
        }

        void UpdateShots(float dt)
        {
            foreach (var s in shots)
            {
                if (!s.Live) continue;
                var next = s.Pos + s.Vel * dt;
                s.Life -= dt;
                bool dead = s.Life <= 0f || !Clear(s.Pos, next);
                if (!dead)
                {
                    foreach (var f in foes)
                    {
                        if (f.Dead || s.Hit.Contains(f)) continue;
                        var c = f.Pos + Vector2.up * 0.4f * f.Scale;
                        float r = f.Radius + 0.15f + (f.Kind == FoeKind.Boss ? 0.5f : 0f);
                        if (DistToSeg(c, s.Pos, next) > r) continue;
                        s.Hit.Add(f);
                        if (s.Splash > 0f) { Explode(next, s.Splash, s.Dmg); dead = true; break; }
                        HurtFoe(f, s.Dmg, s.Vel.normalized * 0.12f);
                        if (s.Pierce-- <= 0) { dead = true; break; }
                    }
                }
                else if (s.Splash > 0f) Explode(s.Pos, s.Splash, s.Dmg);
                if (dead) { s.Live = false; s.T.gameObject.SetActive(false); continue; }
                s.Pos = next;
                s.T.position = W(s.Pos);
            }
        }

        static float DistToSeg(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return (a + ab * t - p).magnitude;
        }

        void Explode(Vector2 at, float radius, float dmg)
        {
            Fx.Burst(W(at), new Color(1f, 0.6f, 0.25f), 16, 3.2f, 0.12f, 0f);
            shake = Mathf.Max(shake, 0.18f);
            foreach (var f in foes.ToArray())
            {
                if (f.Dead) continue;
                float d = (f.Pos - at).magnitude;
                if (d <= radius + f.Radius) HurtFoe(f, dmg * (1f - 0.4f * d / (radius + f.Radius)), (f.Pos - at).normalized * 0.3f);
            }
        }

        // ---- gadgets --------------------------------------------------------------------------

        void TryDodge()
        {
            if (phase != Phase.Wave || gadget[(int)Gadget.Dodge] == 0 || dashCooldown > 0f || dashTimer > 0f) return;
            var dir = heroVel.sqrMagnitude > 0.05f ? heroVel.normalized : (facingLeft ? Vector2.left : Vector2.right);
            dashDir = dir;
            dashTimer = 0.2f;
            invuln = Mathf.Max(invuln, 0.35f);
            dashCooldown = DodgeCooldown;
            Sfx.Bounce();
        }

        float DodgeCooldown => gadget[(int)Gadget.Dodge] switch { 1 => 3f, 2 => 2.2f, _ => 1.5f };

        void PlaceTurret(Vector2 at)
        {
            var go = new GameObject("Turret");
            go.transform.SetParent(root, false);
            go.transform.position = W(at);
            var t = new Turret { Pos = at };
            t.Base = go.AddComponent<SpriteRenderer>();
            t.Base.sprite = Art("turret", Feet["turret"]);
            t.Base.sortingOrder = Depth(at.y);
            var gun = new GameObject("Gun");
            gun.transform.SetParent(go.transform, false);
            gun.transform.localPosition = new Vector3(0f, 0.62f, 0f);
            var gsr = gun.AddComponent<SpriteRenderer>();
            gsr.sprite = Art("turret_gun", 0.5f);
            gsr.sortingOrder = Depth(at.y) + 1;
            t.Gun = gun.transform;
            turrets.Add(t);
            Fx.Burst(W(at), new Color(0.9f, 0.8f, 0.5f), 10, 2f, 0.08f, 0f);
        }

        void UpdateTurrets(float dt)
        {
            int lv = gadget[(int)Gadget.Turret];
            float rate = lv >= 2 ? 4.2f : 3f, dmg = (lv >= 2 ? 11f : 7f) * (1f + 0.1f * perkStacks[(int)Perk.Damage]);
            foreach (var t in turrets)
            {
                t.Timer -= dt;
                var from = t.Pos + Vector2.up * 0.62f;
                var target = NearestFoe(from, 6.5f, true);
                if (target == null) continue;
                var dir = (target.Pos + Vector2.up * 0.4f * target.Scale - from).normalized;
                float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                t.Gun.localScale = new Vector3(1f, dir.x < 0f ? -1f : 1f, 1f);
                t.Gun.localRotation = Quaternion.Euler(0f, 0f, ang);
                if (t.Timer > 0f) continue;
                t.Timer = 1f / rate;
                SpawnShot(from + dir * 0.4f, dir, 17f, dmg, 6.5f, 0, 0f, new Color(0.7f, 0.95f, 1f), 0.13f);
            }
        }

        // ---- the dead ---------------------------------------------------------------------------

        int ActiveDoorCount { get { int n = 0; foreach (var d in doors) if (zones[d.Zone].Open) n++; return n; } }

        void UpdateSpawning(float dt)
        {
            if (toSpawn <= 0) return;
            spawnTimer -= dt;
            if (spawnTimer > 0f) return;
            spawnTimer = Mathf.Max(0.22f, 1.5f - wave * 0.05f) * (3f / (2f + ActiveDoorCount));
            toSpawn--;
            spawned++;
            SpawnFoe(PickKind(), -1);
        }

        FoeKind PickKind()
        {
            float r = Random.value;
            float brute = wave >= 8 ? Mathf.Min(0.14f, 0.04f + (wave - 8) * 0.008f) : 0f;
            float wisp = wave >= 6 ? 0.1f : 0f;
            float hound = wave >= 3 ? 0.16f : 0f;
            float heavy = wave >= 2 ? Mathf.Min(0.3f, 0.1f + wave * 0.015f) : 0f;
            if ((r -= brute) < 0f) return FoeKind.Brute;
            if ((r -= wisp) < 0f) return FoeKind.Wisp;
            if ((r -= hound) < 0f) return FoeKind.Hound;
            if ((r -= heavy) < 0f) return FoeKind.Heavy;
            return FoeKind.Walker;
        }

        float HpMult => 1f + 0.15f * (wave - 1) + 0.012f * (wave - 1) * (wave - 1);

        Foe SpawnFoe(FoeKind kind, int doorIndex, Vector2? at = null)
        {
            var def = FoeDefs[(int)kind];
            if (doorIndex < 0)
            {
                var open = new List<int>();
                for (int i = 0; i < doors.Count; i++) if (zones[doors[i].Zone].Open) open.Add(i);
                doorIndex = open[Random.Range(0, open.Count)];
            }
            var door = doors[doorIndex];
            var side = door.Horizontal ? Vector2.right : Vector2.up;
            var pos = at ?? door.Pos + door.Out * Random.Range(4.5f, 7f) + side * Random.Range(-3f, 3f);

            var go = new GameObject("Foe_" + kind);
            go.transform.SetParent(root, false);
            var f = new Foe
            {
                Kind = kind, T = go.transform, Pos = pos, Radius = def.radius,
                Speed = def.speed * Random.Range(0.9f, 1.1f) * Mathf.Min(1.35f, 1f + wave * 0.012f),
                Hp = def.hp * (kind == FoeKind.Boss ? 1f + 0.5f * bosses : HpMult), Dmg = def.dmg * (1f + 0.07f * (wave - 1)),
                DoorDmg = def.doorDmg * (1f + 0.05f * (wave - 1)), Scrap = def.scrap, Door = doorIndex,
                Scale = def.height / 1.2f, Bob = Random.value * 6f, Summon = 6f, Charge = 4f,
            };
            if (kind == FoeKind.Boss) f.Hp *= HpMult * 0.5f;
            f.MaxHp = f.Hp;
            var shadow = Put("Shadow", PlaceholderVisuals.Circle(Color.white), Vector2.zero, -1500, go.transform);
            shadow.transform.localPosition = Vector3.zero;
            shadow.transform.localScale = new Vector3(def.radius * 2.4f, def.radius * 0.9f, 1f);
            shadow.color = new Color(0f, 0f, 0.05f, 0.4f);
            var frames = EnemySprite.Frames(def.sheet, 0f);
            var bodyGo = new GameObject("Body");
            bodyGo.transform.SetParent(go.transform, false);
            // the painted bodies' real heights in their sheets (measured), so each one ends up def.height tall
            float natural = def.sheet switch { "hound" => 1.04f, "brute" => 2.16f, "wisp" => 1.64f, _ => 1.77f };
            bodyGo.transform.localScale = Vector3.one * (def.height / natural);
            f.Sr = bodyGo.AddComponent<SpriteRenderer>();
            f.Frames = frames;
            f.Sr.sprite = frames != null ? frames[0] : PlaceholderVisuals.Square(Color.gray);
            f.Sr.color = def.tint;
            f.Anim = Random.Range(0f, 8f);
            go.transform.position = W(pos);
            foes.Add(f);
            return f;
        }

        void UpdateFoes(float dt)
        {
            float traction = nightEvent == NightEvent.LowGravity ? 1.6f : 10f;
            for (int i = 0; i < foes.Count; i++)
            {
                var f = foes[i];
                if (f.Dead) continue;
                f.Flash -= dt;
                f.Attack -= dt;
                bool ghost = f.Kind == FoeKind.Wisp;
                Vector2 goal;
                bool atDoor = false;
                Door door = null;
                if (ghost) goal = heroPos;
                else
                {
                    // downhill on the distance field: round the cars, through the openings, to a door to break
                    int attack = PathStep(f, out goal);
                    if (attack >= 0) { door = doors[attack]; atDoor = true; f.Door = attack; }
                }
                f.Inside = RoomOf(f.Pos) >= 0;

                // the boss charges and calls for help
                if (f.Kind == FoeKind.Boss && f.Inside) UpdateBoss(f, dt, ref goal);

                Vector2 want = Vector2.zero;
                if (!atDoor)
                {
                    var to = goal - f.Pos;
                    if (to.sqrMagnitude > 0.0004f) want = to.normalized * f.Speed;
                }
                if (f.ChargeWindup > 0f) want = Vector2.zero;
                if (f.Charge < 0f) want = f.ChargeDir * f.Speed * 4f;
                // keep apart from each other
                for (int j = 0; j < foes.Count; j++)
                {
                    if (j == i) continue;
                    var o = foes[j];
                    var d = f.Pos - o.Pos;
                    float min = f.Radius + o.Radius;
                    float sq = d.sqrMagnitude;
                    if (sq < min * min && sq > 1e-5f) want += d / Mathf.Sqrt(sq) * (min - Mathf.Sqrt(sq)) * 6f;
                }
                f.Vel = Vector2.Lerp(f.Vel, want, 1f - Mathf.Exp(-traction * dt));
                f.Pos = Collide(f.Pos + f.Vel * dt, f.Radius, !ghost, ghost);

                if (atDoor && door.Intact && f.Attack <= 0f)
                {
                    f.Attack = 0.9f;
                    door.Hp -= f.DoorDmg;
                    door.Shake = 0.25f;
                    if (Random.value < 0.4f) Sfx.Drop();
                    if (!door.Intact) { Sfx.Kill(); shake = Mathf.Max(shake, 0.15f); Fx.Burst(W(door.Pos), new Color(0.7f, 0.5f, 0.3f), 14, 3f, 0.1f); ShowMessage("Une porte a cédé !"); }
                    SetDoorLook(door);
                }

                // biting the hero
                float reach = f.Radius + PlayerRadius + 0.12f;
                if ((f.Pos - heroPos).sqrMagnitude < reach * reach && f.Attack <= 0f)
                {
                    f.Attack = 0.85f;
                    HurtHero(f.Dmg);
                    if (perkStacks[(int)Perk.Thorns] > 0) HurtFoe(f, 15f * perkStacks[(int)Perk.Thorns], (f.Pos - heroPos).normalized * 0.2f);
                }

                // the painted body: walk cycle with the distance covered, facing its way
                if (f.Frames != null)
                {
                    float speed = f.Vel.magnitude;
                    f.Anim += dt * 10f * (ghost ? 1f : Mathf.Clamp(speed / 1.6f, 0.3f, 2.4f));
                    f.Sr.sprite = f.Frames[(int)f.Anim % f.Frames.Length];
                }
                float face = (heroPos.x - f.Pos.x) < 0f ? -1f : 1f;
                if (Mathf.Abs(f.Vel.x) > 0.2f) face = Mathf.Sign(f.Vel.x);
                float bob = ghost ? 0.25f + Mathf.Sin(Time.time * 2.4f + f.Bob) * 0.1f : nightEvent == NightEvent.LowGravity ? 0.2f + Mathf.Sin(Time.time * 1.6f + f.Bob) * 0.15f : 0f;
                f.Sr.transform.localPosition = new Vector3(0f, bob, 0f);
                var s = f.Sr.transform.localScale;
                f.Sr.transform.localScale = new Vector3(Mathf.Abs(s.x) * face, s.y, 1f);
                bool hot = f.Flash > 0f || f.ChargeWindup > 0f && Mathf.Repeat(Time.time * 10f, 1f) < 0.5f;
                f.Sr.color = hot ? new Color(1f, 0.45f, 0.35f) : FoeDefs[(int)f.Kind].tint;
                f.T.position = W(f.Pos);
                f.Sr.sortingOrder = Depth(f.Pos.y);
            }
            foes.RemoveAll(f => f.Dead);
        }

        void UpdateBoss(Foe f, float dt, ref Vector2 goal)
        {
            f.Summon -= dt;
            if (f.Summon <= 0f)
            {
                f.Summon = 9f;
                for (int k = 0; k < 3; k++) { var m = SpawnFoe(FoeKind.Walker, f.Door, f.Pos + Random.insideUnitCircle * 1.5f); m.Inside = f.Inside; }
                ShowMessage("Le boss appelle la horde !");
            }
            if (f.ChargeWindup > 0f)
            {
                f.ChargeWindup -= dt;
                if (f.ChargeWindup <= 0f) { f.Charge = -0.7f; f.ChargeDir = (heroPos - f.Pos).normalized; Sfx.Kill(); }
                return;
            }
            if (f.Charge < 0f)
            {
                f.Charge += dt;
                if (f.Charge >= 0f) f.Charge = Random.Range(4f, 6f);
                return;
            }
            f.Charge -= dt;
            if (f.Charge <= 0f && (heroPos - f.Pos).magnitude < 8f) { f.ChargeWindup = 0.75f; f.Charge = 0f; }
        }

        void HurtFoe(Foe f, float dmg, Vector2 push)
        {
            if (f.Dead) return;
            f.Hp -= dmg;
            f.Flash = 0.1f;
            f.Pos = Collide(f.Pos + push * (f.Kind == FoeKind.Boss ? 0.2f : 1f), f.Radius, f.Kind != FoeKind.Wisp, f.Kind == FoeKind.Wisp);
            if (f.Hp > 0f) return;
            f.Dead = true;
            kills++;
            if (f.Kind == FoeKind.Boss) { bosses++; boss = null; ShowMessage("Le boss est tombé !"); Sfx.Milestone(); }
            float gain = f.Scrap * (1f + 0.25f * perkStacks[(int)Perk.Scrap]) + scrapCarry;
            int whole = Mathf.FloorToInt(gain);
            scrapCarry = gain - whole;
            scrap += whole;
            if (whole > 0) Fx.Text(W(f.Pos + Vector2.up * 1.2f * f.Scale), $"+{whole}", new Color(0.9f, 0.82f, 0.6f), 0.6f);
            if (perkStacks[(int)Perk.Vampire] > 0) hp = Mathf.Min(maxHp, hp + perkStacks[(int)Perk.Vampire]);
            Sfx.Kill();
            Fx.Burst(W(f.Pos + Vector2.up * 0.5f * f.Scale), new Color(0.45f, 0.2f, 0.35f), f.Kind == FoeKind.Boss ? 30 : 8, 2.4f, 0.09f);
            if (perkStacks[(int)Perk.Blast] > 0)
            {
                float r = 1.3f, d2 = 12f * perkStacks[(int)Perk.Blast];
                foreach (var o in foes.ToArray()) if (!o.Dead && o != f && (o.Pos - f.Pos).sqrMagnitude < r * r) HurtFoe(o, d2, (o.Pos - f.Pos).normalized * 0.2f);
            }
            Destroy(f.T.gameObject);
        }

        void HurtHero(float dmg)
        {
            if (invuln > 0f || hp <= 0f) return;
            shieldIdle = 0f;
            if (shield > 0f)
            {
                float a = Mathf.Min(shield, dmg);
                shield -= a; dmg -= a;
                if (dmg <= 0f) { Sfx.Bounce(); return; }
            }
            hp -= dmg;
            invuln = 0.45f;
            hurtFlash = 0.2f;
            shake = Mathf.Max(shake, 0.12f);
            Sfx.Hit();
            if (hp > 0f) return;
            if (!secondChanceUsed)
            {
                // the one second chance: back on his feet, everything near him thrown back
                secondChanceUsed = true;
                hp = maxHp * 0.5f;
                invuln = 2.2f;
                foreach (var f in foes.ToArray())
                {
                    var d = f.Pos - heroPos;
                    if (d.magnitude < 3.5f) HurtFoe(f, 120f, d.normalized * 1.6f);
                }
                Fx.Burst(W(heroPos + Vector2.up * 0.5f), new Color(1f, 0.85f, 0.5f), 30, 5f, 0.14f, 0f);
                Sfx.Milestone();
                ShowBanner("SECONDE CHANCE", "Tu te relèves. La prochaine chute sera la dernière.");
                return;
            }
            hp = 0f;
            Sfx.Death();
            EndNight(true);
        }

        // ---- doors, zones, events ---------------------------------------------------------------

        void UpdateDoors(float dt)
        {
            foreach (var d in doors)
            {
                if (d.Shake <= 0f || !d.Sr.gameObject.activeSelf) continue;
                d.Shake -= dt;
                var basePos = d.Horizontal ? d.Pos + new Vector2(0f, -0.2f) : d.Pos;
                d.Sr.transform.position = W(basePos + Random.insideUnitCircle * 0.04f * (d.Shake > 0f ? 1f : 0f));
            }
        }

        void UpdateZoneShutters(float dt)
        {
            if (zones == null) return;
            for (int z = 1; z < zones.Length; z++)
            {
                var zone = zones[z];
                if (zone.ShutterAnim < 0f || zone.Shutter == null) continue;
                zone.ShutterAnim += dt * 1.6f;
                float k = Mathf.Clamp01(zone.ShutterAnim);
                zone.Shutter.transform.localScale = zone.GateHorizontal ? new Vector3(1f, 1f - k, 1f) : new Vector3(1f, 1f - k, 1f);
                if (zone.Veil != null) zone.Veil.color = new Color(0.02f, 0.02f, 0.06f, 0.72f * (1f - k));
                if (k >= 1f)
                {
                    zone.ShutterAnim = -1f;
                    zone.Shutter.gameObject.SetActive(false);
                    zone.Veil.gameObject.SetActive(false);
                }
            }
        }

        void OpenZone(int z)
        {
            var zone = zones[z];
            if (zone.Open) return;
            zone.Open = true;
            zone.ShutterAnim = 0f;
            foreach (var d in doors) if (d.Zone == z) { d.MaxHp = DoorMaxHp; d.Hp = d.MaxHp; d.State = -1; SetDoorLook(d); }
            // its crate: a handful of scrap and a breath
            foreach (var (sr, lz) in loots)
                if (lz == z && sr.gameObject.activeSelf)
                {
                    sr.gameObject.SetActive(false);
                    int bonus = 25 + z * 15;
                    scrap += bonus;
                    hp = Mathf.Min(maxHp, hp + 25f);
                    Fx.Burst(sr.transform.position + Vector3.up * 0.4f, new Color(1f, 0.85f, 0.4f), 16, 3f, 0.1f, 0f);
                    Fx.Text(sr.transform.position + Vector3.up * 1.2f, $"Caisse : +{bonus} ferraille, +25 PV", new Color(1f, 0.85f, 0.45f), 1.4f);
                }
            RefreshLights();
            BuildGrid();
            Sfx.Milestone();
        }

        void BeginEvent()
        {
            nightEvent = EventFor(wave);
            meteorTimer = 2f;
            if (darkness != null) darkness.gameObject.SetActive(nightEvent == NightEvent.Blackout);
            if (nightEvent == NightEvent.Blackout && darkness == null)
            {
                darkness = Put("Blackout", BlackoutSprite(), heroPos, 3400);
                darkness.color = new Color(0f, 0f, 0.02f, 0.96f);
            }
            if (nightEvent == NightEvent.LowGravity)
                for (int i = 0; i < 40; i++)
                {
                    var m = Put("Mote", PlaceholderVisuals.Circle(Color.white), heroPos + Random.insideUnitCircle * 9f, 3050);
                    m.transform.localScale = Vector3.one * Random.Range(0.05f, 0.12f);
                    m.color = new Color(1f, 0.85f, 0.6f, 0.6f);
                    motes.Add((m.transform, Random.Range(0.3f, 0.9f), Random.value * 6f));
                }
            RefreshLights();
        }

        void EndEvent()
        {
            if (darkness != null) darkness.gameObject.SetActive(false);
            foreach (var (t, _, _) in motes) if (t != null) Destroy(t.gameObject);
            motes.Clear();
            foreach (var m in meteors) { if (m.Shadow != null) Destroy(m.Shadow.gameObject); if (m.Rock != null) Destroy(m.Rock.gameObject); }
            meteors.Clear();
            nightEvent = NightEvent.None;
            RefreshLights();
        }

        static Sprite blackout;

        /// <summary>A big black sheet with a soft round hole: the light of the hero's torch.</summary>
        static Sprite BlackoutSprite()
        {
            if (blackout != null) return blackout;
            const int N = 256;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x - N / 2f) / (N / 2f), dy = (y - N / 2f) / (N / 2f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy) * 8f;    // hole radius ~ 1/8 of the sheet
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1.15f, d));
                    px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px);
            tex.Apply();
            blackout = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N / 50f);   // 50 units wide, hole ~3.2 units
            return blackout;
        }

        void UpdateEvent(float dt)
        {
            if (nightEvent == NightEvent.Blackout && darkness != null)
                darkness.transform.position = W(heroPos + Vector2.up * 0.5f);
            if (nightEvent == NightEvent.LowGravity)
                foreach (var (t, speed, ph) in motes)
                {
                    var p = t.position;
                    p.y += speed * dt;
                    p.x += Mathf.Sin(Time.time + ph) * dt * 0.3f;
                    if (p.y > Origin.y + heroPos.y + 9f) p = W(heroPos + new Vector2(Random.Range(-9f, 9f), -9f));
                    t.position = p;
                }
            if (nightEvent != NightEvent.DebrisRain) return;
            meteorTimer -= dt;
            if (meteorTimer <= 0f)
            {
                meteorTimer = Random.Range(0.5f, 1.1f);
                var at = Random.value < 0.4f ? heroPos + Random.insideUnitCircle * 2f : new Vector2(Random.Range(-18f, 16f), Random.Range(-8f, 14f));
                var m = new Meteor { Pos = at, Fall = 1.4f };
                m.Shadow = Put("MeteorShadow", PlaceholderVisuals.Circle(Color.white), at, -1400);
                m.Shadow.color = new Color(0.1f, 0f, 0f, 0.5f);
                m.Rock = Put("Meteor", Art("meteor", 0.5f), at + Vector2.up * 12f, 3200);
                meteors.Add(m);
            }
            for (int i = meteors.Count - 1; i >= 0; i--)
            {
                var m = meteors[i];
                m.T += dt;
                float k = Mathf.Clamp01(m.T / m.Fall);
                m.Shadow.transform.localScale = Vector3.one * Mathf.Lerp(0.3f, 2.4f, k) * new Vector3(1f, 0.55f, 1f).magnitude;
                m.Shadow.transform.localScale = new Vector3(Mathf.Lerp(0.3f, 2.4f, k), Mathf.Lerp(0.15f, 1.3f, k), 1f);
                m.Rock.transform.position = W(m.Pos + Vector2.up * (1f - k) * 12f);
                m.Rock.transform.Rotate(0f, 0f, dt * 300f);
                if (k < 1f) continue;
                // impact
                shake = Mathf.Max(shake, 0.25f);
                Sfx.Attack();
                Fx.Burst(W(m.Pos), new Color(1f, 0.55f, 0.2f), 18, 4f, 0.12f, 0f);
                var crater = Put("Crater", Art("crater", 0.5f), m.Pos, -1950);
                Destroy(crater.gameObject, 12f);
                if ((heroPos - m.Pos).magnitude < 1.3f) HurtHero(28f);
                foreach (var f in foes.ToArray()) if (!f.Dead && (f.Pos - m.Pos).magnitude < 1.3f + f.Radius) HurtFoe(f, 90f, (f.Pos - m.Pos).normalized * 0.5f);
                Destroy(m.Shadow.gameObject); Destroy(m.Rock.gameObject);
                meteors.RemoveAt(i);
            }
        }

        // ---- waves ------------------------------------------------------------------------------

        void NextWave()
        {
            wave++;
            bool bossWave = wave % 5 == 0;
            toSpawn = 8 + wave * 4 + (wave > 15 ? (wave - 15) * 3 : 0);
            if (bossWave) toSpawn /= 2;
            spawned = 0;
            spawnTimer = 1.5f;
            waveClock = 0f;
            phase = Phase.Wave;
            // what to come back to if the night is left during this wave
            waveStart = Snapshot(wave - 1, false);
            BeginEvent();
            if (bossWave)
            {
                var open = new List<int>();
                for (int i = 0; i < doors.Count; i++) if (zones[doors[i].Zone].Open) open.Add(i);
                boss = SpawnFoe(FoeKind.Boss, open[Random.Range(0, open.Count)]);
                bossName = BossNames[(wave / 5 - 1) % BossNames.Length];
                ShowBanner($"VAGUE {wave}", bossName.ToUpperInvariant() + " ARRIVE");
            }
            else if (nightEvent != NightEvent.None) ShowBanner(EventTitle(nightEvent), EventText(nightEvent));
            else ShowBanner($"VAGUE {wave}", wave == 1 ? "Ils arrivent par la route. Tiens les portes !" : null);
            ShowGameUi();
        }

        string bossName = "";

        void WaveCleared()
        {
            EndEvent();
            if (wave > BestWave) BestWave = wave;
            scrap += 10 + wave * 2;
            phase = Phase.Bonus;
            foreach (var s in shots) { s.Live = false; s.T.gameObject.SetActive(false); }
            Sfx.Milestone();
            ShowBonusChoice();
        }

        void EndNight(bool died)
        {
            if (phase == Phase.Over || phase == Phase.Menu) return;
            // waves fully held: the one being fought does not count, one already cleared does
            int held = Mathf.Max(0, phase == Phase.Wave ? wave - 1 : wave);
            phase = Phase.Over;
            anim = 0f;
            aiming = false;
            EndEvent();
            int score = held * 100 + kills * 5 + bosses * 300;
            int coins = held * 4 + bosses * 25;
            if (coins > 0) SaveSystem.AddCoins(coins);
            if (held > BestWave) BestWave = held;
            var entry = new BoardEntry { score = score, wave = held, kills = kills, date = System.DateTime.Now.ToString("dd/MM") };
            ClearSave();
            int rank = Record(entry);
            if (died) ShowOver(entry, rank, coins);
        }

        // ---- drawing the hero and the camera -------------------------------------------------------

        void DrawHero(float dt)
        {
            if (hero == null || !hero.gameObject.activeSelf) return;
            hero.position = W(heroPos);
            int order = Depth(heroPos.y);
            bool moving = heroVel.sqrMagnitude > 0.2f;
            bool dead = phase == Phase.Over && hp <= 0f;
            string state = dead ? "death" : hurtFlash > 0f ? "hurt" : moving ? "run" : "idle";
            anim += dt * (state == "run" ? 13f * Mathf.Clamp(heroVel.magnitude / 3.6f, 0.6f, 1.6f) : 7f);
            int count = HeroSprites.Count(state);
            int index = state == "death" ? Mathf.Min((int)anim, count - 1) : (int)anim % count;
            heroSr.sprite = HeroSprites.Get(state, index) ?? heroSr.sprite;
            bool firing = (aiming || aimTarget) && phase == Phase.Wave;
            bool left = firing ? aimDir.x < 0f : facingLeft;
            heroSr.flipX = left;
            heroSr.sortingOrder = order;
            heroSr.color = invuln > 0f && Mathf.Repeat(Time.time * 12f, 1f) < 0.5f ? new Color(1f, 1f, 1f, 0.55f) : Color.white;
            float bob = nightEvent == NightEvent.LowGravity ? 0.2f + Mathf.Sin(Time.time * 1.6f) * 0.15f : 0f;
            heroSr.transform.localPosition = new Vector3(0f, bob, 0f);

            // the gun in his fist, swung toward what he shoots
            int frame = HeroSprites.Index(state, index);
            bool holding = HeroSprites.HandAt(frame, out var hand, out var lean) && !dead;
            gunSr.enabled = holding && gunSr.sprite != null;
            if (holding)
            {
                float facing = left ? -1f : 1f;
                gunPivot.localPosition = new Vector3(left ? -hand.x : hand.x, hand.y + bob, 0f);
                gunPivot.localScale = new Vector3(facing, 1f, 1f);
                // elevation seen from the hero (up positive, forward = his facing), mirrored by the pivot's scale
                float elevation = firing ? Mathf.Clamp(Mathf.Atan2(aimDir.y, aimDir.x * facing) * Mathf.Rad2Deg, -70f, 70f) : lean;
                gunPivot.localRotation = Quaternion.Euler(0f, 0f, facing * (elevation + kick * 12f));
                gunSr.transform.localPosition = new Vector3(-kick * 0.035f, 0f, 0f);
                gunSr.sortingOrder = order - 1;
            }
            // a faint sight line where the shots will go
            if (sight == null)
            {
                sight = Put("Sight", PlaceholderVisuals.Square(Color.white), heroPos, 3090);
                sight.color = new Color(1f, 0.35f, 0.25f, 0.28f);
            }
            bool showSight = aimTarget && holding && phase == Phase.Wave;
            sight.gameObject.SetActive(showSight);
            if (showSight)
            {
                var from = MuzzleWorld();
                float len = Guns[gunIndex].Range * RangeMult;
                sight.transform.position = W(from + aimDir * len * 0.5f);
                sight.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(aimDir.y, aimDir.x) * Mathf.Rad2Deg);
                sight.transform.localScale = new Vector3(len, 0.035f, 1f);
            }

            // shield bubble
            float cap = ShieldCap;
            shieldSr.enabled = cap > 0f && shield > 0.5f;
            shieldSr.color = new Color(0.55f, 0.85f, 1f, 0.15f + 0.45f * (cap > 0f ? shield / cap : 0f));
            shieldSr.sortingOrder = order + 2;
            shadowSr.sortingOrder = -1500;
        }

        /// <summary>Whether the player is aiming (and so firing) this frame.</summary>
        bool aiming;
        /// <summary>Whether someone is in the gun's sights (the aim line shows it).</summary>
        bool aimTarget;
        SpriteRenderer sight;

        void RefreshGun()
        {
            if (gunSr == null) return;
            gunSr.sprite = WeaponCatalog.HeldSprite(ArtOf(Guns[gunIndex]));
        }

        void FrameCamera(Vector2 focus, bool snap)
        {
            if (cam == null) return;
            float ortho = cam.aspect < 1f ? 6.6f : 5.6f;
            if (!IsActive) return;
            TakeOverCamera(cam.transform.position, ortho, new Color(0.04f, 0.05f, 0.1f));
            float halfW = ortho * cam.aspect, halfH = ortho;
            var b = WorldBounds;
            // the joysticks cover the bottom quarter: centre the hero in what stays visible above them
            float lift = cam.aspect < 1f ? -0.65f : -0.1f;
            var want = new Vector2(Mathf.Clamp(focus.x, b.xMin + halfW, b.xMax - halfW), Mathf.Clamp(focus.y + lift, b.yMin + halfH, b.yMax - halfH));
            var cur = snap ? (Vector3)W(want, -10f) : Vector3.Lerp(cam.transform.position, W(want, -10f), 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime));
            cur.z = -10f;
            shake = Mathf.MoveTowards(shake, 0f, Time.unscaledDeltaTime);
            cam.transform.position = cur + (Vector3)(Random.insideUnitCircle * shake * 0.6f);
        }
    }
}
