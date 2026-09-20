# Apogée — version jouable dans le navigateur

Ce dossier est la version web du jeu, produite par `Apogée > Build WebGL` dans Unity.
Il est publié tel quel par GitHub Pages (Settings > Pages > Source : branche `main`, dossier `/docs`).

- `index.html` : la page qui lance le jeu, adaptée au format portrait des téléphones.
- `Build/` : les fichiers du moteur (compressés en Brotli, avec repli JavaScript, donc
  aucun réglage serveur n'est nécessaire).

Pour mettre à jour : relancer le build, puis recopier le contenu de `WebBuild/` ici.
