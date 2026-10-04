namespace Platformer.Survival
{
    /// <summary>
    /// What a player reads the first time they open each game: the goal in one line, the
    /// controls, the one thing worth knowing, and what the game pays. Kept together in one
    /// place so the wording of every game can be read and edited side by side.
    ///
    /// Rewards use the icon tokens of IconText: [c] for coins, [g] for materials.
    /// </summary>
    public struct GameGuide
    {
        public string Title;
        public string Goal;
        public string Controls;
        public string Tip;
        /// <summary>Icon tokens for what the game pays, e.g. "[c]   [g]".</summary>
        public string Rewards;

        public static GameGuide For(string id) => id switch
        {
            "runner" => new GameGuide
            {
                Title = "RUNNER INFINI",
                Goal = "Cours le plus loin possible. La vitesse monte et le terrain ne s'arrête jamais. Tombé ? Un kit de soin (fabriqué dans Fusion) ou une pub te fait reprendre là où tu étais.",
                Controls = "Joystick pour avancer ou reculer, SAUT pour sauter, TIR pour abattre les morts. Les munitions se ramassent en route.",
                Tip = "Ramasse les bonus de la piste (bouclier, pièces x2, aimant, tir illimité). Tous les 1000 m, un boss t'attend : tu l'affrontes avec la vie qu'il te reste. Les activités deviennent plus dures avec la distance, et trois missions t'attendent chaque jour.",
                Rewards = "[c]   [g]",
            },
            "expedition" => new GameGuide
            {
                Title = "EXPÉDITION",
                Goal = "Des niveaux avec un début et une fin. Au portail, un boss t'attend.",
                Controls = "Les mêmes commandes que le Runner, à vitesse constante. Chaque drapeau laisse une boîte de munitions.",
                Tip = "Tu affrontes le boss avec la vie qu'il te reste. Les drapeaux te font repartir après une chute, et chaque niveau cache des secrets.",
                Rewards = "[c]   [g]",
            },
            "fusion" => new GameGuide
            {
                Title = "FUSION",
                Goal = "Bienvenue au laboratoire ! Lâche les remèdes dans le bécher : deux identiques qui se touchent fusionnent. Pilule, gélule, comprimé, pansement, sirop, seringue, fiole, flacon, élixir...",
                Controls = "Glisse pour viser, relâche pour lâcher.",
                Tip = "Deux élixirs font un kit de soin (3 en stock maximum). Un kit te fait reprendre une course du Runner là où tu es tombé. Ne laisse rien dépasser du bord !",
                Rewards = "[c]   [k]",
            },
            "arena" => new GameGuide
            {
                Title = "ARÈNE",
                Goal = "Un duel au tour par tour contre un boss, puis le suivant, toujours plus fort.",
                Controls = "Choisis une attaque à chaque tour. Chacune a un nombre d'utilisations limité.",
                Tip = "Vise la faiblesse du boss, c'est super efficace. Les améliorations de la boutique renforcent ton combattant.",
                Rewards = "[c]   [g]",
            },
            "barricade" => new GameGuide
            {
                Title = "BARRICADE",
                Goal = "Le mode Barricade permet de récolter des matériaux. Tiens la barricade face aux vagues de morts, et garde ta base debout.",
                Controls = "Maintiens un couloir pour tirer dedans. Entre les vagues, construis et répare tes pièges avec tes débris.",
                Tip = "Les pièges s'usent : saboteurs, démolisseurs et spectres s'en prennent à eux ou les ignorent. Un couloir complet (4 pièges au niveau 5) en bon état produit des matériaux. Siège toutes les 10 vagues !",
                Rewards = "[g]",
            },
            "invasion" => new GameGuide
            {
                Title = "INVASION",
                Goal = "Repousse la formation des morts avant qu'elle n'atteigne ton île.",
                Controls = "Joystick pour te déplacer, TIR pour tirer vers le ciel.",
                Tip = "Cache-toi derrière les abris pour esquiver leurs tirs. Un boss descend tous les 5 assauts.",
                Rewards = "[c]   [g]",
            },
            "labyrinth" => new GameGuide
            {
                Title = "LABYRINTHE",
                Goal = "Ramasse toutes les rations de survie (eau, nourriture, soins) puis rapporte-les au camp pour descendre d'un étage.",
                Controls = "Glisse le doigt vers où tu veux tourner : ton perso prend le prochain passage ouvert dans cette direction.",
                Tip = "Les squelettes à lanterne ne doivent pas te voir : reste hors de leur lumière ou cache-toi dans les buissons. Les zombies, eux, te pourchassent sans relâche.",
                Rewards = "[c]   [g]",
            },
            _ => new GameGuide { Title = id.ToUpperInvariant(), Goal = "", Controls = "", Tip = "", Rewards = "" },
        };
    }
}
