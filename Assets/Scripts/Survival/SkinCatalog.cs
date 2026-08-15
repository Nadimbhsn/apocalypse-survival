using UnityEngine;
using Platformer.Mechanics;

namespace Platformer.Survival
{
    public enum SkinUnlockType { Free, Coins, Ad }

    public struct SkinDef
    {
        public string Id;
        public string Name;
        public Color Tint;
        public SkinUnlockType UnlockType;
        public int CoinCost;
    }

    /// <summary>
    /// Static catalog of playable character skins. A skin is just a color tint applied to
    /// the player's existing sprite/animations - no new art is needed, and every skin still
    /// plays the normal idle/run/jump animations, just recolored.
    /// </summary>
    public static class SkinCatalog
    {
        public static readonly SkinDef[] All =
        {
            new SkinDef { Id = "default", Name = "Survivant",  Tint = Color.white,                            UnlockType = SkinUnlockType.Free },
            new SkinDef { Id = "veteran", Name = "Vétéran",    Tint = new Color(0.62f, 0.60f, 0.42f),         UnlockType = SkinUnlockType.Coins, CoinCost = 30 },
            new SkinDef { Id = "toxic",   Name = "Contaminé",  Tint = new Color(0.45f, 0.85f, 0.35f),         UnlockType = SkinUnlockType.Coins, CoinCost = 60 },
            new SkinDef { Id = "ember",   Name = "Braise",     Tint = new Color(0.95f, 0.45f, 0.20f),         UnlockType = SkinUnlockType.Coins, CoinCost = 100 },
            new SkinDef { Id = "spectre", Name = "Spectre",    Tint = new Color(0.65f, 0.80f, 0.95f, 0.8f),   UnlockType = SkinUnlockType.Ad },
            new SkinDef { Id = "golden",  Name = "Légendaire", Tint = new Color(1.00f, 0.84f, 0.25f),         UnlockType = SkinUnlockType.Ad },
        };

        public static SkinDef Default => All[0];

        public static SkinDef Find(string id)
        {
            foreach (var skin in All)
                if (skin.Id == id) return skin;
            return Default;
        }

        /// <summary>Tints the player's sprite to match the currently selected skin.</summary>
        public static void ApplyToPlayer(PlayerController player)
        {
            if (player == null) return;
            var sr = player.GetComponent<SpriteRenderer>();
            if (sr != null) sr.color = Find(SaveSystem.SelectedSkinId).Tint;
        }
    }
}
