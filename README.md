# Apogée

Jeu mobile (portrait, Unity 6) construit sur le microgame *2D Platformer* de Unity.
Un hub donne accès à quatre mini-jeux qui partagent la même monnaie, les mêmes
personnages et les mêmes améliorations.

## Les modes

| Mode | Principe |
| --- | --- |
| **Runner** | Course d'île flottante en île flottante : joystick, saut, tir. Le terrain est généré à l'infini et enchaîne des secteurs très différents (ruines, pont brisé, cimetière, terres brûlées) avec des sections spéciales : ascension d'une tour façon Doodle Jump, chute libre dans un puits, survol au jetpack. |
| **Fusion** | Deux trésors identiques qui se touchent fusionnent en un plus grand, de la feuille jusqu'à la planète, sans faire déborder le bac. |
| **Arène** | Duel au tour par tour contre une échelle de boss : quatre attaques, PP, faiblesses, brûlure et poison. |
| **Barricade** | Défense par vagues sur trois couloirs : on tire dans un couloir, et entre les vagues on achète pièges et réparations. |

## Structure du code

Tout le mode de jeu est ajouté par script au chargement de la scène, sans modifier
la scène ni les prefabs d'origine (voir `Assets/Scripts/Survival/GameBootstrap.cs`).

- `SurvivalDirector*.cs` — boucle du runner : génération du terrain, décors, apparitions.
- `RuntimeUI.cs`, `UiKit.cs`, `ApogeeTheme.cs` — toute l'interface, construite au runtime.
- `FusionGame.cs`, `ArenaGame.cs`, `BarricadeGame.cs` — les mini-jeux (`MiniGame.cs`).
- `KenneyProps.cs`, `SkyBackdrop.cs` — décors 3D et ciel peint du monde d'Apogée.
- `AdService.cs` — Unity Ads. **Les identifiants de jeu restent à renseigner** dans ce fichier.

## Crédits

- Modèles 3D : [Kenney](https://kenney.nl) — Graveyard Kit et Castle Kit, licence CC0
  (voir `Assets/ThirdParty/Kenney/`).
- Police : [Cinzel](https://github.com/NDISCOVER/Cinzel), licence SIL OFL
  (voir `Assets/ThirdParty/Fonts/`).
- Illustrations (`Apoge.png`, `runner.png`) : fournies par l'auteur du projet.
- Base : Unity *2D Platformer Microgame*.
