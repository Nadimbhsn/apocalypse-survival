using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Platformer.Mechanics;

namespace Platformer.Survival
{
    /// <summary>
    /// The player's gun in the runner and the campaign, driven by the weapon equipped in the
    /// armoury (see WeaponCatalog). Holding FIRE (or F / Ctrl / gamepad West / right
    /// trigger) pulls the trigger at the weapon's own rate; a burst weapon fires several
    /// rounds per pull. Shots are aim-assisted toward the nearest living zombie within the
    /// weapon's range, otherwise straight ahead, and a bullet is gone once it has flown that
    /// range - no more clearing the screen from off-screen.
    ///
    /// Every round costs ammunition. A run starts with the weapon's starting stock; boxes
    /// lying on the ground and dropped by the dead top it up to the weapon's maximum. Out of
    /// ammo, the trigger only clicks: the player has to move on and find more, which is the
    /// point - firing is a resource, not a reflex.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class PlayerCombat : MonoBehaviour
    {
        readonly List<Projectile> pool = new();
        Transform poolParent;
        PlayerController player;
        SpriteRenderer spriteRenderer;
        HeldWeapon held;

        WeaponDef weapon;
        int ammo;
        float cooldown;
        int burstLeft;
        float burstTimer;
        bool dryClicked;

        public WeaponDef Weapon => weapon ??= WeaponCatalog.Equipped;
        public int Ammo => ammo;
        public int MaxAmmo => Weapon.MaxAmmo;

        int Damage(int baseDamage) => Mathf.Max(1, Mathf.RoundToInt(baseDamage * UpgradeManager.FirePowerMultiplier));

        void Awake()
        {
            poolParent = new GameObject("ProjectilePool").transform;
            player = GetComponent<PlayerController>();
            spriteRenderer = GetComponent<SpriteRenderer>();
            weapon = WeaponCatalog.Equipped;
            ammo = weapon.StartAmmo;
        }

        void Start()
        {
            held = HeldWeapon.Attach(player);
            held.SetWeapon(Weapon);
        }

        /// <summary>A new run or level: the equipped gun, with its starting stock.</summary>
        public void ResetForRun()
        {
            weapon = WeaponCatalog.Equipped;
            ammo = weapon.StartAmmo;
            cooldown = 0f;
            burstLeft = 0;
            dryClicked = false;
            if (held != null) held.SetWeapon(weapon);
        }

        /// <summary>Respawning at a campaign flag never leaves the player with less than a starting stock.</summary>
        public void EnsureAmmoAtLeast(int rounds) => ammo = Mathf.Max(ammo, rounds);

        /// <summary>Adds one box for the gun in hand; returns how many rounds actually fitted.</summary>
        public int AddAmmoBox()
        {
            int before = ammo;
            ammo = Mathf.Min(MaxAmmo, ammo + Weapon.AmmoPerPickup);
            return ammo - before;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            cooldown -= dt;

            var director = SurvivalDirector.Instance;
            // The rhythm section is pure platforming: no rounds wasted on nothing there.
            bool live = player.controlEnabled && !player.rhythmMode && (director == null || director.IsRunning);
            if (!live) { burstLeft = 0; return; }

            // Rounds still owed by a burst already started.
            if (burstLeft > 0)
            {
                burstTimer -= dt;
                if (burstTimer <= 0f)
                {
                    burstLeft--;
                    burstTimer = Weapon.BurstGap;
                    if (ammo > 0) FireOne(); else burstLeft = 0;
                }
                return;
            }

            if (!WantsFire()) { dryClicked = false; return; }
            if (cooldown > 0f) return;

            if (ammo <= 0)
            {
                if (!dryClicked)
                {
                    dryClicked = true;
                    Sfx.Drop();
                    Fx.Text(transform.position + Vector3.up * 0.9f, "PLUS DE MUNITIONS", new Color(1f, 0.55f, 0.45f), 0.9f);
                }
                return;
            }

            cooldown = Weapon.FireInterval;
            FireOne();
            burstLeft = Weapon.Burst - 1;
            burstTimer = Weapon.BurstGap;
        }

        static bool WantsFire()
        {
            if (MobileInput.FireHeld) return true;
            var kb = Keyboard.current;
            if (kb != null && (kb.fKey.isPressed || kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed)) return true;
            var pad = Gamepad.current;
            return pad != null && (pad.buttonWest.isPressed || pad.rightTrigger.isPressed);
        }

        Zombie FindNearestZombie(float range)
        {
            Zombie nearest = null;
            float best = range * range;
            foreach (var zombie in Zombie.Active)
            {
                if (zombie == null || !zombie.IsAlive) continue;
                float d = ((Vector2)zombie.transform.position - (Vector2)transform.position).sqrMagnitude;
                if (d <= best)
                {
                    best = d;
                    nearest = zombie;
                }
            }
            return nearest;
        }

        void FireOne()
        {
            var w = Weapon;
            Vector2 origin = held != null ? held.Muzzle : (Vector2)transform.position + Vector2.up * 0.1f;
            var target = FindNearestZombie(w.Range);
            Vector2 dir;
            if (target != null)
            {
                var col = target.GetComponent<Collider2D>();
                Vector2 aim = col != null ? (Vector2)col.bounds.center : (Vector2)target.transform.position;
                dir = aim - origin;
            }
            else
            {
                dir = spriteRenderer != null && spriteRenderer.flipX ? Vector2.left : Vector2.right;
            }

            var projectile = GetPooledProjectile();
            projectile.speed = w.BulletSpeed;
            projectile.Style(w.BulletSize, w.BulletColor);
            projectile.Launch(origin, dir, Damage(w.Damage), w.Range, w.ExplosionRadius, w.ExplosionRadius > 0f ? Damage(w.ExplosionDamage) : 0);
            ammo--;

            if (w.ExplosionRadius > 0f) Sfx.Attack(); else Sfx.Shoot();
            Fx.Burst(origin + dir.normalized * 0.1f, w.BulletColor, 3, 1.5f, 0.05f, 0f);
            if (held != null) held.Kick(dir);
        }

        Projectile GetPooledProjectile()
        {
            foreach (var p in pool)
                if (!p.gameObject.activeSelf) return p;

            var go = new GameObject("Projectile");
            go.transform.SetParent(poolParent, false);
            go.transform.localScale = Vector3.one * 0.25f;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = PlaceholderVisuals.Circle(Color.white);
            sr.sortingOrder = 5;

            var col = go.AddComponent<CircleCollider2D>();
            col.isTrigger = true;
            col.radius = 0.5f;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;

            var projectile = go.AddComponent<Projectile>();
            pool.Add(projectile);
            return projectile;
        }
    }

    /// <summary>
    /// The equipped gun, drawn in the character's hands: it faces where the character
    /// faces, swings toward whatever it is firing at, kicks back on each shot, and hides
    /// whenever the character's own sprite is hidden (the rhythm section, death).
    /// </summary>
    public class HeldWeapon : MonoBehaviour
    {
        PlayerController player;
        SpriteRenderer body;
        Transform pivot;
        SpriteRenderer art;
        WeaponDef weapon;
        float kick;
        Vector2 aimDir = Vector2.right;
        float aimTimer;

        public static HeldWeapon Attach(PlayerController target)
        {
            var go = new GameObject("HeldWeapon");
            go.transform.SetParent(target.transform, false);
            var held = go.AddComponent<HeldWeapon>();
            held.player = target;
            held.body = target.GetComponent<SpriteRenderer>();
            held.pivot = go.transform;
            var artGo = new GameObject("Art");
            artGo.transform.SetParent(go.transform, false);
            held.art = artGo.AddComponent<SpriteRenderer>();
            held.art.sortingOrder = (held.body != null ? held.body.sortingOrder : 0) + 1;
            return held;
        }

        public void SetWeapon(WeaponDef w)
        {
            weapon = w;
            art.sprite = WeaponCatalog.SpriteFor(w);
            float width = art.sprite != null ? art.sprite.bounds.size.x : 1f;
            float scale = w.HeldLength / Mathf.Max(0.01f, width);
            art.transform.localScale = Vector3.one * scale;
            // Grip a third of the way along the gun, so the barrel sticks out in front.
            art.transform.localPosition = new Vector3(w.HeldLength * 0.22f, 0f, 0f);
        }

        public void Kick(Vector2 dir)
        {
            kick = 1f;
            if (dir.sqrMagnitude > 0.0001f) aimDir = dir.normalized;
            aimTimer = 0.35f;
        }

        /// <summary>World position of the barrel's end, where bullets leave.</summary>
        public Vector2 Muzzle
        {
            get
            {
                if (art == null || art.sprite == null) return transform.position;
                return art.transform.TransformPoint(new Vector3(art.sprite.bounds.max.x, art.sprite.bounds.center.y, 0f));
            }
        }

        void LateUpdate()
        {
            if (player == null || body == null) return;
            bool visible = body.enabled && (player.health == null || player.health.IsAlive);
            art.enabled = visible && art.sprite != null;

            float dt = Time.deltaTime;
            kick = Mathf.MoveTowards(kick, 0f, dt * 7f);
            aimTimer -= dt;

            // Face the target while firing at it, otherwise the way the character faces.
            float facing = aimTimer > 0f ? Mathf.Sign(aimDir.x == 0f ? 1f : aimDir.x) : (body.flipX ? -1f : 1f);
            var offset = player.collider2d != null ? player.collider2d.offset : Vector2.zero;
            pivot.localPosition = new Vector3(offset.x + (0.1f - kick * 0.08f) * facing, offset.y + 0.02f, 0f);
            pivot.localScale = new Vector3(facing, 1f, 1f);

            // The pivot is mirrored (scale x = -1) when facing left, and a rotation applies
            // after that mirror: a barrel at local +x ends up at 180 + r degrees. So to point
            // at world angle a, rotate by a - 180 when facing left, by a when facing right.
            float angle = 0f;
            if (aimTimer > 0f)
            {
                float world = Mathf.Atan2(aimDir.y, aimDir.x) * Mathf.Rad2Deg;
                angle = facing > 0f ? world : Mathf.DeltaAngle(0f, world - 180f);
                angle = Mathf.Clamp(angle, -50f, 50f);
            }
            // Recoil lifts the barrel, whichever way it points.
            pivot.localRotation = Quaternion.Euler(0f, 0f, angle + kick * 8f * facing);
        }
    }
}
