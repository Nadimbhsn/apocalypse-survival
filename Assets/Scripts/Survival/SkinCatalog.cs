using UnityEngine;
using Platformer.Mechanics;

namespace Platformer.Survival
{
    public enum SkinUnlockType { Free, Coins, Ad }

    public struct SkinDef
    {
        public string Id;
        public string Name;
        public string Description;
        public Color Tint;
        public SkinUnlockType UnlockType;
        public int CoinCost;
    }

    /// <summary>
    /// Static catalog of playable characters. Visually a character is a color tint applied
    /// to the player's existing sprite/animations (until custom portraits are dropped into
    /// Resources/Portraits, see LoadPortrait); in the Arena each one also has its own move
    /// set (see ArenaCatalog.MovesFor). All characters are original to this game.
    /// </summary>
    public static class SkinCatalog
    {
        public static readonly SkinDef[] All =
        {
            new SkinDef { Id = "default", Name = "Survivant", Description = "Un inconnu sans passé qui refuse de mourir. Polyvalent.",
                Tint = Color.white, UnlockType = SkinUnlockType.Free },
            new SkinDef { Id = "veteran", Name = "Vétéran", Description = "Ancien soldat, il a vu la ville tomber. Solide et méthodique.",
                Tint = new Color(0.62f, 0.60f, 0.42f), UnlockType = SkinUnlockType.Coins, CoinCost = 30 },
            new SkinDef { Id = "toxic", Name = "Contaminé", Description = "Mordu, mais toujours humain. Pour l'instant. Empoisonne ses ennemis.",
                Tint = new Color(0.45f, 0.85f, 0.35f), UnlockType = SkinUnlockType.Coins, CoinCost = 60 },
            new SkinDef { Id = "sentinel", Name = "Sentinelle", Description = "Ancienne gardienne du bunker 7. Elle ne recule jamais et encaisse tout.",
                Tint = new Color(0.55f, 0.65f, 0.82f), UnlockType = SkinUnlockType.Coins, CoinCost = 80 },
            new SkinDef { Id = "ember", Name = "Braise", Description = "Elle a brûlé son propre quartier pour stopper la horde. Le feu est son allié.",
                Tint = new Color(0.95f, 0.45f, 0.20f), UnlockType = SkinUnlockType.Coins, CoinCost = 100 },
            new SkinDef { Id = "scavenger", Name = "Charognard", Description = "Il survit en pillant les ruines. Chez lui, tout se recycle... même les armes.",
                Tint = new Color(0.78f, 0.56f, 0.30f), UnlockType = SkinUnlockType.Coins, CoinCost = 120 },
            new SkinDef { Id = "monk", Name = "Moine", Description = "Dernier disciple d'un ordre disparu. Calme au milieu du chaos, il soigne et protège.",
                Tint = new Color(0.88f, 0.82f, 0.60f), UnlockType = SkinUnlockType.Coins, CoinCost = 160 },
            new SkinDef { Id = "spectre", Name = "Spectre", Description = "On ne sait pas s'il est vraiment là. Ses coups drainent la vie.",
                Tint = new Color(0.65f, 0.80f, 0.95f, 0.8f), UnlockType = SkinUnlockType.Ad },
            new SkinDef { Id = "prowler", Name = "Rôdeuse", Description = "Elle se déplace la nuit. Personne ne l'a jamais vue venir. Venin et embuscades.",
                Tint = new Color(0.50f, 0.32f, 0.60f), UnlockType = SkinUnlockType.Ad },
            new SkinDef { Id = "golden", Name = "Légendaire", Description = "Le héros dont parlent les survivants autour du feu. Frappe fort, partout.",
                Tint = new Color(1.00f, 0.84f, 0.25f), UnlockType = SkinUnlockType.Ad },
        };

        public static SkinDef Default => All[0];

        /// <summary>
        /// Portrait for the characters page and the arena. Drop a Sprite named after the
        /// character id into Assets/Resources/Portraits/ (e.g. Portraits/veteran.png, Texture
        /// Type = Sprite) and it is used as-is; otherwise the player's own sprite tinted
        /// with the character color stands in.
        /// </summary>
        public static Sprite LoadPortrait(SkinDef skin, out bool isCustom)
        {
            var custom = Resources.Load<Sprite>("Portraits/" + skin.Id);
            isCustom = custom != null;
            return custom;
        }

        public static SkinDef Find(string id)
        {
            foreach (var skin in All)
                if (skin.Id == id) return skin;
            return Default;
        }

        /// <summary>Tints the player's sprite to match the currently selected character.</summary>
        public static void ApplyToPlayer(PlayerController player)
        {
            if (player == null) return;
            var sr = player.GetComponent<SpriteRenderer>();
            if (sr != null) sr.color = Find(SaveSystem.SelectedSkinId).Tint;
        }
    }
}
