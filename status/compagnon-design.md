# Compagnon — refonte UX + éditeur des réglages HUD v1.3 — rendu du 2026-10-30

Branche `v2/compagnon-design`, rebasée sur `main` (`96b0f7a`) sans conflit. Auteur Mio.
Périmètre touché : `overlay/companion/**` et ce fichier. Aucune fenêtre affichée, aucune capture, aucun accès
au jeu, ni à ses données, ni à sa mémoire. Aucun fichier réel de `~/.config/ultima`, `$XDG_RUNTIME_DIR/ultima`
ou `~/.local/state/ultima` n'a été touché (le daemon de Mio, PID 663552, n'a pas été approché).

## Commits
| sha | objet |
|---|---|
| `6fe600b` | feat(companion): WIP refonte UX et éditeur des réglages HUD v1.3 (travail repris, committé tel quel) |
| `745fdf6` | feat(companion): réglages HUD v1.3 lus comme le daemon, aller-retour prouvé |
| `3707061` | feat(companion): ordre des modules des réglages partout, coût nullable v1.3, tests §9-§12 |

## Fait
- **Refonte** (repris du WIP) : 4 pages (Tableau = la décision du moment, Run, HUD, Sources), palette du jeu
  seule (`palette.py`, CSS généré par `theme.py`), polices du jeu chargées pour le processus seulement (repli
  Cantarell), lettres S-F toujours écrites, détail repliable (niveau 2), aucune animation en boucle.
- **Éditeur HUD v1.3** : qualité eco/verre/loupe, côté, hauteur, opacité au repos, note du deck en combat,
  lettres seules, modules (activer, monter/descendre, par défaut). Écriture atomique (tmp, fsync, rename, fsync
  du dossier) ; curseurs regroupés (350 ms) et écrits à la fermeture.
- **Conformité au contrat v1.3 de main** (vérifiée champ par champ sur `EngineContracts.cs` et
  `SettingsStore.NormalizeHud`) :
  `modules[{id,enabled,order}]` puis `hud{quality,heightFraction,restOpacity,side,deckScoreInCombat,gradesOnly}`,
  camelCase, ordre des clés = record C#. Défaut `("verre", 0.55, 0.62, "right", false, false)`. Valeurs
  permises : qualité ∈ eco|verre|loupe, côté ∈ right|left (lus en minuscules comme le daemon), hauteur ∈ ]0,1],
  opacité ∈ [0,1], booléens stricts ; une valeur invalide reprend le défaut de CE champ. Lecture insensible à la
  casse comme le daemon (`PropertyNameCaseInsensitive`), jamais de clé réécrite en double.
- **v1.3 `CardView.Cost` nullable** : « coût X » ou « injouable », jamais « coût 0 » ; tri en dernier.
  `RewardView.SkipScore` null → « — ».
- **Même grammaire que le HUD** : page Run et ligne de résumé dans l'ordre des réglages (deck, enchantées,
  malédictions, reliques, potions, équipe), même découpage que `render.js` (Deck = toutes les cartes,
  Enchantées et Malédictions en rappel), groupes vides masqués (§11).
- **Écritures concurrentes avec le TUI** : quand le daemon publie des réglages différents, la page HUD relit
  `settings.json` avant toute nouvelle écriture (`reload_if_changed`). Sans ça, l'écriture suivante du
  compagnon effaçait la modification du TUI.
- `--check` affiche le HUD sur une ligne : `Verre · hauteur 55 % · repos 62 % · droite`.

## Prouvé
```
$ cd overlay/companion && python3 -m pytest -q
172 passed, 1 skipped, 5 warnings in 0.52s          # skipped = test du vrai daemon, sans ULTIMA_DAEMON_DLL
$ ULTIMA_DAEMON_DLL=<scratchpad>/dn/bin/Ultima.Daemon/release/ultima-daemon.dll python3 -m pytest -q
173 passed, 6 warnings in 1.43s
```
(warnings = dépréciations internes de PyGObject sous Python 3.14.)
- Aller-retour sur l'échantillon réel : `test_hud_roundtrip_on_v13_sample_read_edit_atomic_write_reread`
  (lecture de `docs/ipc/state.v13.sample.json`, édition de chaque contrôle, écriture atomique avec un nouvel
  inode et aucun tmp laissé, relecture identique) ; `test_v13_daemon_sample_settings_read_and_rewritten_identically`
  (réglages publiés par le daemon relus puis réécrits octet pour octet).
- Vrai daemon : `test_real_daemon_dump_reads_companion_settings_unchanged` (`ultima-daemon dump --root <vide>
  --settings <tmp> --no-mirror --no-scoring --game-comm ultima-tests-none`, compilé avec `--artifacts-path`
  dans le scratchpad) : `state.settings` publié = fichier écrit par le compagnon. Essai manuel avec
  `"quality":"LOUPE"` : le daemon republie `"quality": "loupe"`, comme le compagnon.
- Sections absentes ou `null` : `test_every_section_null_or_absent_is_tolerated` (10 sections × null/absent × 5
  écrans) ; « — » pour une note absente, jamais 0.
- Palette : `test_no_hex_outside_palette_file`, `test_generated_css_uses_only_palette_colors`.
- §9-§12 : `tests/test_design_rules.py` (lettres, violet + « Malédiction », aqua + nom de l'enchantement,
  libellés = titres des blocs de `render.js`, ordre des modules, au plus un accent par ligne, crème à 60 %,
  14 px minimum, Super+Maj et jamais F8/F9, dévoilement entre 150 et 220 ms, aucun @keyframes, animation,
  transition ni minuteur d'animation).
- Isolation : `conftest.py` retire `DISPLAY`, pointe `WAYLAND_DISPLAY` vers un chemin inexistant, met
  `XDG_CONFIG_HOME` et `XDG_RUNTIME_DIR` dans des dossiers temporaires et vérifie que le vrai `settings.json`
  n'a pas bougé.

## NON prouvé
- **Le rendu n'a jamais été vu** : ni sur l'écran réel, ni hors écran. GTK4 ne peut pas créer de widget sans
  connexion à un affichage, et un serveur virtuel (broadway) reviendrait à afficher une fenêtre : je n'en ai
  pas lancé. Le CSS est seulement analysé par `Gtk.CssProvider` (test), et les classes GTK utilisées
  (`Adw.ToggleGroup`…) sont seulement vérifiées comme existantes.
- Jamais testé avec le daemon qui tourne (`run`) ni avec le socket réel : le rechargement à chaud a été vérifié
  en mode `dump` seulement, et la relecture après une écriture du TUI seulement en logique pure.
- Lisibilité à 1920×1200 @125 % et contraste ≥ 4,5:1 sur le verre : non mesurés.

## Reste à faire
- Validation visuelle par Mio pendant une fenêtre de test (pages Tableau, Run, HUD).
- La note du deck reste en tête de la page Run quel que soit son rang dans les réglages (choix : c'est la
  décision de cette page) ; à confirmer avec Mio.
- Visibilité : la page Run montre tous les modules (le compagnon est la vue complète) ; seul l'ordre des
  réglages s'applique. À confirmer.

## Besoins et écarts de contrat (rien de modifié dans `Ultima.Engine/Contracts/**` ni `overlay/gnome/**`)
- `ModuleSetting` sans `enabled` : le daemon (record C#) le lit `false`, le compagnon `true`. Sans effet tant
  que seul le compagnon ou le TUI écrit le fichier (`enabled` est toujours écrit), mais le contrat devrait
  fixer la valeur par défaut.
- Bornes de l'interface hauteur 0,30-0,90 et opacité 0,20-1,00 : le contrat n'en donne pas (le daemon accepte
  ]0,1] et [0,1]) ; une valeur lue hors de ces bornes mais valide pour le daemon est conservée telle quelle.
- `CardView.Cost == null` ne distingue pas « coût X » d'« injouable » : le compagnon le devine par
  `isCurse`/type. Un champ (`costKind` ou `isUnplayable`) lèverait l'ambiguïté.
- Côté HUD (hors périmètre, pour info) : `render.js` colore les malédictions en `red` alors que §9 dit
  `purple` + le mot ; `MODULE_IDS` n'a pas encore `event`, `route`, `party` ; l'extension ne lit pas encore
  `hud` (aucune occurrence de `heightFraction`/`quality` hors `geometry.js`).
