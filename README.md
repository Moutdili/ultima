# ULTIMA — Slay the Spire 2 Combat Intelligence

Tracker de deck **augmenté** pour Slay the Spire 2 (Early Access), construit sur une base
de reverse engineering reproductible (REA + ILSpy + GDRE Tools).

Etat : dossier de préconnaissance + squelette de repo. Le code du mod est à écrire
(voir `docs/06-architecture.md`), mais **toute la connaissance du jeu est déjà extraite
et indexée** dans `docs/10-source-map.md` + `docs/11-api-catalog.md`.

## Ce que fait ULTIMA (différentiel vs trackers existants)

1. **Forecast de dégâts ennemis** : dégâts actuels, + variante *hypothétique avec Weak*,
   + variante Vulnerable, par ennemi et par cible.
2. **Létalité exacte** : sur `16x3` avec 35 PV, dit *« le 3e coup te tue »* (block inclus,
   overkill affiché).
3. **Alertes multi** : si un joueur tient une carte « payoff si jouée en premier ce tour »,
   le tracker le signale aux autres AVANT qu'ils jouent.
4. **Broadcast** : chaque client partage ses infos (forecast, létalité, prio de tour) via
   des `INetMessage` custom — les mods sont auto-découverts par le jeu.
5. **Scoring de run** : contribution réelle des cartes/reliques/potions aux dégâts,
   qualité du deck, et **risque des chemins** sur la map.
6. **UI compacte** : overlay Godot léger, déplaçable, opacité au repos, hotkeys.
7. **Linux Fedora natif** (le jeu tourne en build Linux, pas Proton).

## Machines

- **fedora** (`100.123.83.40`) : la machine de JEU. Le jeu, la décompilation et le
  développement du mod vivent ici. Claude Code travaille ici.
- **fedora-hp** (`100.76.54.29`) : la machine de rédaction/coordination (ce repo y est né).

## Démarrage rapide (sur fedora)

```bash
cd ~/sts2
cat CLAUDE.md                 # manuel d'opération (à lire en premier)
cat docs/10-source-map.md     # où vit chaque système dans le code du jeu
cat docs/11-api-catalog.md    # classes/hooks/API vérifiés
bash tools/doctor.sh          # vérifie toolchain + jeu + mods
```

## Arborescence

```
sts2/
  CLAUDE.md              manuel pour l'agent
  .mcp.json              MCP REA (portée projet)
  docs/                  dossier de préconnaissance
  research/repos/        clones des trackers/mods existants (gitignored)
  tools/                 install / decompile / index / doctor / build / deploy
  skills-src/            skills au format Claude Code (source)
  game/                  sortie de décompilation (gitignored)
```

## Avertissement

Recherche et interopérabilité sur un jeu possédé. Aucun asset du jeu n'est redistribué.
Le mod est **read-only** côté gameplay (il observe, il ne modifie pas l'état).
