using System.Collections.Generic;
using UnityEngine;

namespace Platformer.Survival
{
    /// <summary>
    /// One gun the player can carry in the runner and the campaign. Damage is per bullet,
    /// before the Fire Power upgrade multiplies it; range is how far a bullet flies before
    /// it is gone, and also how far the aim-assist looks for a target.
    /// </summary>
    public class WeaponDef
    {
        public string Id;
        public string Name;
        /// <summary>Painted gun in Resources/Arms (see WeaponCatalog.Art for its grip and muzzle).</summary>
        public string Sprite;
        public string Blurb;

        public int Damage;
        /// <summary>Seconds between trigger pulls.</summary>
        public float FireInterval;
        /// <summary>Bullets per trigger pull (the SMG's burst), each costing one round.</summary>
        public int Burst = 1;
        public float BurstGap = 0.07f;
        public float BulletSpeed = 15f;
        public float Range = 7f;
        public float BulletSize = 0.22f;
        public Color BulletColor = new Color(0.92f, 0.70f, 0.28f);

        /// <summary>Grenades: every zombie within this radius takes ExplosionDamage.</summary>
        public float ExplosionRadius;
        public int ExplosionDamage;

        public int StartAmmo;
        public int MaxAmmo;
        /// <summary>Rounds in one ammo box picked up off the ground.</summary>
        public int AmmoPerPickup;

        public int CostCoins;
        public int CostMaterials;

        /// <summary>Length of the gun in the character's hands, in world units (he is about 1.2 tall).</summary>
        public float HeldLength = 0.25f;

        public bool Free => CostCoins == 0 && CostMaterials == 0;
    }

    /// <summary>
    /// The armoury. The pistol is free and slow; everything else is bought once and then
    /// equipped from the ARMES page. Ammunition is the balancing lever: the stronger the
    /// gun, the fewer rounds it starts with and the fewer come in each box, so a run can
    /// never be won by holding the trigger down.
    /// </summary>
    public static class WeaponCatalog
    {
        public static readonly WeaponDef[] All =
        {
            new WeaponDef
            {
                Id = "pistol", Name = "Pistolet", Sprite = "pistol",
                Blurb = "L'arme de base. Lente mais économe.",
                Damage = 1, FireInterval = 0.5f, BulletSpeed = 14f, Range = 7f,
                StartAmmo = 30, MaxAmmo = 60, AmmoPerPickup = 10,
                HeldLength = 0.22f,
            },
            new WeaponDef
            {
                Id = "revolver", Name = "Revolver", Sprite = "revolver",
                Blurb = "Trois fois plus puissant, aussi lent.",
                Damage = 3, FireInterval = 0.85f, BulletSpeed = 16f, Range = 8f, BulletSize = 0.28f,
                StartAmmo = 18, MaxAmmo = 36, AmmoPerPickup = 6,
                CostCoins = 80, HeldLength = 0.27f,
            },
            new WeaponDef
            {
                Id = "uzi", Name = "Mitraillette", Sprite = "uzi",
                Blurb = "Tire en rafales de trois balles.",
                Damage = 1, FireInterval = 0.6f, Burst = 3, BurstGap = 0.07f, BulletSpeed = 15f, Range = 6f, BulletSize = 0.18f,
                StartAmmo = 60, MaxAmmo = 120, AmmoPerPickup = 18,
                CostCoins = 150, HeldLength = 0.32f,
            },
            new WeaponDef
            {
                Id = "rifle", Name = "Mitrailleuse", Sprite = "rifle",
                Blurb = "Aussi puissante que le revolver, en semi-automatique.",
                Damage = 3, FireInterval = 0.3f, BulletSpeed = 18f, Range = 9f, BulletSize = 0.24f,
                StartAmmo = 40, MaxAmmo = 90, AmmoPerPickup = 12,
                CostCoins = 250, HeldLength = 0.62f,
            },
            new WeaponDef
            {
                Id = "launcher", Name = "Lance-grenade", Sprite = "launcher",
                Blurb = "Chaque tir explose et touche tout le groupe.",
                Damage = 4, FireInterval = 1.3f, BulletSpeed = 10f, Range = 8f, BulletSize = 0.34f,
                BulletColor = new Color(0.36f, 0.52f, 0.22f),
                ExplosionRadius = 2f, ExplosionDamage = 4,
                StartAmmo = 6, MaxAmmo = 15, AmmoPerPickup = 2,
                CostMaterials = 40, HeldLength = 0.62f,
            },
        };

        public static WeaponDef Default => All[0];

        public static WeaponDef Find(string id)
        {
            foreach (var w in All) if (w.Id == id) return w;
            return Default;
        }

        public static bool IsOwned(WeaponDef w) => w.Free || SaveSystem.IsWeaponOwned(w.Id);

        /// <summary>The gun the player carries into a run.</summary>
        public static WeaponDef Equipped
        {
            get
            {
                var w = Find(SaveSystem.EquippedWeapon);
                return IsOwned(w) ? w : Default;
            }
        }

        /// <summary>
        /// Each painted gun's picture: the painted area (pixels, at the bottom left of the
        /// texture), and where the fist closes on it and where the barrel ends, as fractions
        /// of that area.
        /// </summary>
        static readonly Dictionary<string, (int w, int h, Vector2 grip, Vector2 muzzle)> Art = new()
        {
            ["pistol"] = (198, 109, new Vector2(0.1911f, 0.3059f), new Vector2(0.98f, 0.7873f)),
            ["revolver"] = (243, 118, new Vector2(0.1125f, 0.3051f), new Vector2(0.9854f, 0.7313f)),
            ["uzi"] = (288, 135, new Vector2(0.4245f, 0.4773f), new Vector2(0.9871f, 0.7644f)),
            ["rifle"] = (558, 133, new Vector2(0.3316f, 0.4221f), new Vector2(0.9932f, 0.6861f)),
            ["launcher"] = (558, 132, new Vector2(0.3065f, 0.2515f), new Vector2(0.9919f, 0.67f)),
        };

        static readonly Dictionary<string, Sprite> sprites = new();

        static Sprite Make(WeaponDef w, bool held)
        {
            if (w == null || !Art.TryGetValue(w.Sprite, out var a)) return null;
            string key = w.Sprite + (held ? "|held" : "|ui");
            if (sprites.TryGetValue(key, out var s) && s != null) return s;
            var tex = Resources.Load<Texture2D>($"Arms/{w.Sprite}");
            if (tex == null) return null;
            var rect = new Rect(0, 0, Mathf.Min(a.w, tex.width), Mathf.Min(a.h, tex.height));
            // In the hands: pivot on the grip and sized to the gun's length. On a page: centred.
            s = held
                ? Sprite.Create(tex, rect, a.grip, a.w / Mathf.Max(0.05f, w.HeldLength), 0, SpriteMeshType.FullRect)
                : Sprite.Create(tex, rect, new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            sprites[key] = s;
            return s;
        }

        /// <summary>The gun's picture for the armoury and the HUD, centred.</summary>
        public static Sprite SpriteFor(WeaponDef w) => Make(w, false);

        /// <summary>The gun as held: pivot on the grip, HeldLength units long.</summary>
        public static Sprite HeldSprite(WeaponDef w) => Make(w, true);

        /// <summary>The barrel's end relative to the grip, in world units, for a gun held facing right.</summary>
        public static Vector2 MuzzleOffset(WeaponDef w)
        {
            if (w == null || !Art.TryGetValue(w.Sprite, out var a)) return new Vector2(0.25f, 0.05f);
            float ppu = a.w / Mathf.Max(0.05f, w.HeldLength);
            return new Vector2((a.muzzle.x - a.grip.x) * a.w, (a.muzzle.y - a.grip.y) * a.h) / ppu;
        }
    }
}
