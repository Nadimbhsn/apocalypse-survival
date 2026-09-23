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
        /// <summary>Sprite in Resources/Weapons, cut from the design sheet.</summary>
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

        /// <summary>Length of the gun in the character's hands, in world units.</summary>
        public float HeldLength = 0.55f;

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
                HeldLength = 0.5f,
            },
            new WeaponDef
            {
                Id = "revolver", Name = "Revolver", Sprite = "revolver",
                Blurb = "Trois fois plus puissant, aussi lent.",
                Damage = 3, FireInterval = 0.85f, BulletSpeed = 16f, Range = 8f, BulletSize = 0.28f,
                StartAmmo = 18, MaxAmmo = 36, AmmoPerPickup = 6,
                CostCoins = 80, HeldLength = 0.5f,
            },
            new WeaponDef
            {
                Id = "uzi", Name = "Mitraillette", Sprite = "uzi",
                Blurb = "Tire en rafales de trois balles.",
                Damage = 1, FireInterval = 0.6f, Burst = 3, BurstGap = 0.07f, BulletSpeed = 15f, Range = 6f, BulletSize = 0.18f,
                StartAmmo = 60, MaxAmmo = 120, AmmoPerPickup = 18,
                CostCoins = 150, HeldLength = 0.55f,
            },
            new WeaponDef
            {
                Id = "rifle", Name = "Mitrailleuse", Sprite = "rifle",
                Blurb = "Aussi puissante que le revolver, en semi-automatique.",
                Damage = 3, FireInterval = 0.3f, BulletSpeed = 18f, Range = 9f, BulletSize = 0.24f,
                StartAmmo = 40, MaxAmmo = 90, AmmoPerPickup = 12,
                CostCoins = 250, HeldLength = 0.95f,
            },
            new WeaponDef
            {
                Id = "launcher", Name = "Lance-grenade", Sprite = "launcher",
                Blurb = "Chaque tir explose et touche tout le groupe.",
                Damage = 4, FireInterval = 1.3f, BulletSpeed = 10f, Range = 8f, BulletSize = 0.34f,
                BulletColor = new Color(0.36f, 0.52f, 0.22f),
                ExplosionRadius = 2f, ExplosionDamage = 4,
                StartAmmo = 6, MaxAmmo = 15, AmmoPerPickup = 2,
                CostMaterials = 40, HeldLength = 1.0f,
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

        static readonly Dictionary<string, Sprite> sprites = new();

        /// <summary>
        /// The gun's picture. Loaded as a sprite when Unity imported it as one, otherwise
        /// built from the texture, so a fresh import setting can never leave a gun invisible.
        /// </summary>
        public static Sprite SpriteFor(WeaponDef w)
        {
            if (w == null) return null;
            if (sprites.TryGetValue(w.Sprite, out var s) && s != null) return s;
            s = Resources.Load<Sprite>($"Weapons/{w.Sprite}");
            if (s == null)
            {
                var tex = Resources.Load<Texture2D>($"Weapons/{w.Sprite}");
                if (tex != null) s = UnityEngine.Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            }
            sprites[w.Sprite] = s;
            return s;
        }
    }
}
