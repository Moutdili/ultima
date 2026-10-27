# 00 — Brief produit

## Mission

Construire le **meilleur tracker de deck pour Slay the Spire 2**, pas un clone : un outil
qui répond à « qu'est-ce qui me tue ce tour, et qu'est-ce que je dois jouer d'abord ? »,
en solo comme en multi.

## Utilisateur

- Joueur avancé (Ascension), joue en solo et en co-op.
- Veut des chiffres exacts, pas des estimations : dégâts réels, létalité réelle.
- Joue sur **Linux Fedora**, jeu en Flatpak, build natif Linux.
- UI : discrète, jamais au-dessus de la main.

## Périmètre V1 (must)

| # | Feature | Critère d'acceptation |
|---|---|---|
| F1 | Forecast dégâts | Pour chaque ennemi en intent d'attaque : dégâts affichés, variante Weak, variante Vulnerable, par cible |
| F2 | Létalité | Pour une séquence `dmg x N`, indique l'index du coup létal ; gère block, HP, overkill |
| F3 | Priorité de tour (multi) | Si un joueur a une carte « first card » en main, tous les clients voient l'alerte |
| F4 | Broadcast | Les infos F1-F3 d'un joueur sont visibles par les autres |
| F5 | Score de run | Contribution dégâts par carte/relique/potion + export CSV |
| F6 | Risque de chemin | Sur la map, note chaque embranchement (combat/élite/repos/boss) |
| F7 | UI compacte | Overlay déplaçable, hotkey, opacité au repos, < 15% de l'écran |

## Hors périmètre V1

- Automatisation de jeu (bot) : interdit par le design read-only.
- Upload cloud de stats (V2, optionnel, opt-in).
- Support Windows/macOS : non prioritaire (mais pas d'API Windows-only).

## Contraintes

- Read-only : aucune mutation d'état → pas de désync, pas d'incompat multi.
- Early Access : le jeu bouge (v0.103 → v0.107 en ~2 mois). Toute API doit être isolée
  dans une couche `GameApi` avec un fichier de version vérifiée.
- Pas de dépendance à un installateur système (pas de sudo).

## Définition de « fini »

1. `dotnet build` propre, 0 warning critique.
2. Mod chargé dans le jeu, log `ULTIMA ready` + version jeu détectée.
3. En combat : overlay correct sur 3 rencontres différentes, 2 personnages.
4. Multi : 2 instances (host + client) sur la même machine, alerte F3 reçue côté client.
5. CSV de run généré et relisible.
6. Aucun crash sur 30 min de jeu, aucun warning Harmony au chargement.
