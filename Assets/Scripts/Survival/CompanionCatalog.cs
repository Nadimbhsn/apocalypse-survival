using System.Collections.Generic;
using UnityEngine;

namespace Platformer.Survival
{
    public struct CompanionDef
    {
        public string Id, Name, Description;
        public SkinUnlockType UnlockType;
        public int CoinCost;
    }

    /// <summary>
    /// Étienne, the robot Elron built in Apogée (Gabin Zanetti), and the finishes he can be
    /// given. He flies at the hero's shoulder in every mode; the shop's companion upgrade
    /// arms him (see Drone). Painted sprites in Resources/Companion/etienne_{id}.png: eight
    /// hover frames and a firing frame, side view facing right.
    /// </summary>
    public static class CompanionCatalog
    {
        public static readonly CompanionDef[] All =
        {
            new CompanionDef { Id = "cuivre", Name = "Étienne", Description = "Le robot qu'Elron a bricolé de ses mains : cuivre, rivets et ruban adhésif. Il ne le lâche jamais.",
                UnlockType = SkinUnlockType.Free },
            new CompanionDef { Id = "nuit", Name = "Étienne de nuit", Description = "Repeint en acier bleu pour les traversées nocturnes. Son œil voit dans le noir.",
                UnlockType = SkinUnlockType.Coins, CoinCost = 150 },
            new CompanionDef { Id = "braise", Name = "Étienne braise", Description = "Une coque rouge, la couleur des ciels de la fin du monde.",
                UnlockType = SkinUnlockType.Coins, CoinCost = 300 },
            new CompanionDef { Id = "prototype", Name = "Le Prototype", Description = "La toute première version d'Étienne, blanche comme au sortir de l'atelier.",
                UnlockType = SkinUnlockType.Ad },
            new CompanionDef { Id = "dore", Name = "Étienne doré", Description = "Plaqué or de la tête au rotor. Le plus fier des robots.",
                UnlockType = SkinUnlockType.Coins, CoinCost = 800 },
        };

        public static string SelectedId
        {
            get => PlayerPrefs.GetString("companion_selected", "cuivre");
            set { PlayerPrefs.SetString("companion_selected", value); PlayerPrefs.Save(); }
        }

        public static bool IsUnlocked(string id)
        {
            var def = Find(id);
            return def.UnlockType == SkinUnlockType.Free || PlayerPrefs.GetInt("companion_owned_" + id, 0) == 1;
        }

        public static void Unlock(string id)
        {
            PlayerPrefs.SetInt("companion_owned_" + id, 1);
            PlayerPrefs.Save();
        }

        public static CompanionDef Find(string id)
        {
            foreach (var c in All) if (c.Id == id) return c;
            return All[0];
        }

        public static CompanionDef Selected => Find(SelectedId);

        static readonly Dictionary<string, Sprite[]> frames = new();

        /// <summary>The nine frames (eight hovering, one firing) of a finish, centred; null without the art.</summary>
        public static Sprite[] Frames(string id, float pixelsPerUnit = 330f)
        {
            string key = id + "|" + pixelsPerUnit;
            if (frames.TryGetValue(key, out var f)) return f;
            var tex = Resources.Load<Texture2D>("Companion/etienne_" + id);
            if (tex == null) { frames[key] = null; return null; }
            int w = tex.width / 9;
            f = new Sprite[9];
            for (int i = 0; i < 9; i++)
                f[i] = Sprite.Create(tex, new Rect(i * w, 0, w, tex.height), new Vector2(0.48f, 0.5f), pixelsPerUnit, 0, SpriteMeshType.Tight);
            frames[key] = f;
            return f;
        }
    }
}
