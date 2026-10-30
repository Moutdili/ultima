# S33 — T2 Console v1.4 (thèmes, apparence, commandes, polices, README)

Branche `hermes/t2-console-v14` (partie de `origin/v2/console2`, fusion de `origin/main`).
Périmètre respecté : `tools/console/**`, `tools/install.sh`, `tools/uninstall.sh`, `themes/**` (sauf
`themes/sts.json`), `fonts/**`, `docs/personnalisation/**`, `status/hermes-s33.md`.
Aucune modification de `Ultima.Engine/**`, `Ultima.Daemon/**`, `schemas/**`, `docs/22`, `docs/44`,
`overlay/**`, `themes/sts.json`.

## Fait

1. **Thèmes intégrés** — `themes/ironclad|silent|defect|regent|necrobinder|nuit|ardoise|contraste|
   auto-perso.json` (10 fichiers + `sts.json` du chef inchangé). Chacun valide
   `schemas/theme.schema.json` ; héritage `extends: sts` ; tons du personnage = `NameColor` (accent) et
   `EnergyLabelOutlineColor` (teinte du verre). `auto-perso` est résolu depuis `run.character` (repli
   `run.characterId`, puis `sts` hors run). `terminal` est un thème **virtuel** (16 noms ANSI, aucun hex,
   fond laissé au terminal) : le schéma du chef exige des hex, donc il ne peut pas être un fichier JSON.
2. **TUI entièrement thématisée** — `ultima_console/theme.py` : résolveur `thème → extends → tons →
   surcharges appearance.<surface>.tones`, avec composition des couleurs `#RRGGBBAA` sur le fond (Rich
   ne sait pas les rendre). `textview` ne parle plus qu'en tons sémantiques (`text`, `muted`, `accent`,
   `danger`, `ok`, `warn`, `curse`, `enchant`, `info`, `grade.S…F`, `perso:<personnage>`) ; le cadre, le
   contenu, le journal et le bandeau passent par `Resolution.style()`. `palette.py` n'a plus AUCUNE
   couleur (symboles + ANSI CLI seulement). Thème `terminal` : 16 noms ANSI, `Screen.ansi16` transparent,
   aucune couleur 24 bits.
3. **`appearance` dans settings.json** — `ultima_console/appearance.py` : TUI/HUD/compagnon (thème, tons,
   `logColors` TUI, police/taille HUD+compagnon, position HUD, liseré). Écrivain `settings.write_merge` :
   relit le fichier, n'écrit que `appearance` (ou `modules`/`hud`, `pluginsEnabled`), conserve les clés
   inconnues à tous les niveaux, refuse d'écraser un JSON invalide, écrit atomiquement (tmp + fsync +
   rename). Migration unique `console.json → appearance.tui` (thème et `logColors`, ancien fichier
   conservé) ; `consolecfg` ne garde que le lancement du daemon et ne lit `theme`/`logColors` que pour
   migrer.
4. **Onglet Apparence** — onglet `7 Apparence` à quatre sous-onglets (TUI, HUD, Compagnon, Modules), avec
   aperçu vivant par surface : thème (+ `auto-perso → perso`), tons, couleurs du journal, police et
   taille (HUD/compagnon), côté/ancrage/décalages/largeur et liseré, blocs (affiché, ordre, écrans via
   `e`), modules `x.*`, plugins (activation après test réussi, `pluginsEnabled`). `c` dans le journal
   ouvre l'onglet Apparence.
5. **Commandes** — `ultima theme list|show|check|set <surface> <id>`, `ultima font list|refresh`,
   `ultima module list|validate|new`, `ultima plugin list|new|test|enable|disable`, toutes en `--json`,
   `NO_COLOR` et `--color`. `module validate` / `plugin test` appellent `ultima-daemon module-test` /
   `plugin-test` (T1) quand le binaire répond ; sinon : validation par schéma + essai LOCAL du plugin
   (un état d'exemple, un processus, une ligne validée par `plugin-output.schema.json`) et mention
   « à brancher ». `plugin enable` refuse un plugin dont le test échoue.
6. **Polices** — `fonts/base/` (Liberation Sans + Mono, SIL OFL 1.1, licence, `SHA256SUMS`, 714 Kio,
   ≤ 2 Mo), `fonts/.gitignore` (`jeu/`, `systeme/` jamais versionnés). `ultima_console/fonts.py` :
   vérification sha256, `systeme/` = liens `fc-list` + `index.json`, `jeu/` = extraction LOCALE
   (`install.sh --game-fonts DIR`, `ULTIMA_GAME_FONTS`), `perso/` vide, fontconfig
   `~/.config/fontconfig/conf.d/60-ultima.conf`. `installer.py` installe `fonts/`, `themes/`,
   `schemas/`, `samples/` et le fontconfig dans le manifeste ; `uninstall` retire tout (dossiers vides
   compris). `ultima font refresh` met le manifeste à jour.
7. **`docs/personnalisation/README.md`** — guide utilisateur ET agent : emplacements, trois surfaces,
   thèmes livrés, un thème complet (`brume`), `appearance` complet, modules (`x.or-par-etage`), plugin
   Python (`x.exemple`), polices, onglet Apparence, dépannage, « Consignes pour un agent ». Les blocs
   sont marqués `<!-- exemple: … -->` et un test les extrait pour les faire passer par les commandes.

## Prouvé

Porte complète, deux passages consécutifs (dernier état, avant push) :

```
$ bash tools/verify-all.sh
.NET       vert
compagnon  vert
hud        vert
console    vert
PORTE : VERTE          (code 0)
```

Suite console (seul changement de compteur : 124 → 186 tests) :

```
$ cd tools/console && python -m pytest -q
186 passed in ~36 s
```

Détail des preuves ciblées (tests du dépôt) :

- `test_theme.py::test_neutral_themes_have_seven_to_one_contrast` — contraste WCAG mesuré :
  `nuit 18.06:1`, `ardoise 12.92:1`, `contraste 21.0:1`.
- `test_theme.py::test_every_builtin_theme_is_a_file_and_validates` — 10 thèmes = 10 fichiers, schéma OK.
- `test_theme.py::test_terminal_theme_uses_only_the_16_ansi_names` +
  `test_terminal_rendering_emits_no_24_bit_escape` — tons ⊆ 16 noms ANSI, rendu Rich sans `38;2;`.
- `test_theme.py::test_no_hexadecimal_outside_themes` — 0 hex dans `ultima_console/*.py`,
  `tools/install.sh`, `tools/uninstall.sh` (install.sh passé en SGR ANSI).
- `test_theme.py::test_auto_perso_resolves_from_run_character_with_sts_fallback` — 5 personnages + repli.
- `test_appearance.py::test_migration_console_json_once_and_file_kept` — migration `nord → ardoise`,
  `gray → muted`, `#123456` conservé, `console.json` identique octet pour octet, 2e appel = False.
- `test_appearance.py::test_tui_write_keeps_unknown_key_and_companion_reads_it` — réglage écrit par la
  TUI, `cleInconnue` conservée, fichier relu par `overlay/companion/.../settings.py` (sans GTK).
- `test_appearance.py::test_writer_rereads_and_keeps_unknown_keys_added_meanwhile` — clé ajoutée par un
  autre écrivain entre chargement et écriture conservée.
- `test_fonts.py::test_install_uninstall_fonts_identity` — install → uninstall dans un HOME temporaire :
  arbre identique (`snapshot()` complet), quatre dossiers + fontconfig présents puis retirés.
- `test_fonts.py::test_shipped_fonts_are_free_small_and_checksummed` — 714 Kio, licence OFL, sha256.
- `test_readme.py` — les 5 exemples du README passent `theme check`, `module validate`, `plugin test`
  (mode local) et le schéma `appearance` ; blocs bash vérifiés.
- `test_custom_cli.py` — `--json`, codes 0/1/2, `NO_COLOR`, daemon factice `module-test`/`plugin-test`
  (rc 0 / 2 / 3) et repli « à brancher ».

Vérification hors tests, installation réelle dans un HOME temporaire (dépôt et build factices) :

```
$ bin/ultima theme list            → 11 thèmes (dont terminal), 0 erreur
$ bin/ultima theme show ironclad   → accent #FF5555
$ bin/ultima font list             → base 2, systeme 1 lien, jeu 0, perso 0
$ bin/ultima module validate …     → {"ok": true, "mode": "schema"}
$ bin/ultima plugin test x.essai   → {"ok": true, "mode": "local"}
$ bin/ultima plugin enable x.essai → {"ok": true}   (pluginsEnabled écrit)
$ bin/ultima doctor --quick        → polices ok · apparence ok
```

## NON prouvé

- **Rien n'a tourné sur le vrai jeu** : aucune lecture de save/mémoire, aucun lancement de Slay the Spire 2.
- **Aucune vraie session** : la TUI n'a été vue que dans `App.run_test` (headless). Le rendu réel des
  couleurs (thème `terminal` dans un vrai terminal, contraste perçu), la taille 1920×1200 et le confort
  de l'onglet Apparence n'ont pas été vus.
- **Daemon de T1 non fusionné** : `module-test`/`plugin-test` n'ont été exercés que par un **faux
  daemon** (scripts à code 0/2/3) et par le repli local. Le vrai évaluateur du daemon n'a pas été appelé.
- **HUD et compagnon** : aucun rendu GTK/GNOME ; `appearance.hud`/`compagnon` ne sont prouvés que par les
  fichiers écrits et la lecture du compagnon en Python (pas d'affichage).
- **fontconfig réel** : `60-ultima.conf` est écrit et relu, mais `fc-cache`/`fc-list` réels ne l'ont pas
  rechargé (les tests utilisent un faux `fc-list` pour rester déterministes).
- **Polices du jeu** : l'extraction locale est testée sur un faux dossier ; aucune police du jeu n'a été
  lue ni copiée (interdit).
- **Installation réelle** (`~/.local`, venv Textual, build dotnet) : non lancée ; seuls les tests
  d'installateur avec dépôt factice et le scénario de `status/console.md` (v1.3) la couvrent.
- **Écriture concurrente réelle** TUI + compagnon en même temps : prouvée par relecture/merge, pas par
  une course de processus.

## Reste

- Fusionner T1 : `plugin test`/`module validate` passeront alors en `mode: daemon` (le code le détecte
  seul) ; vérifier `custom._run_daemon` sur le vrai binaire et les vrais `docs/ipc/state.v14.sample.json`.
- Intégration au chef : `themes/`, `fonts/`, `schemas/` sont maintenant copiés dans
  `~/.local/share/ultima/` par `install.sh` ; à revalider sur une vraie installation.
- Fenêtre de test n°2 (chef + Mio) : regarder les 11 thèmes dans un vrai terminal, l'onglet Apparence,
  les polices (dont `jeu/`), et le HUD avec `appearance.hud.position`.
- T3/T4 consomment `themes/*.json` et `appearance` : rien à refaire, mais leurs surfaces doivent lire
  `appearance.companion`/`appearance.hud` (contrat commun).
- `ultima doctor` ne vérifie pas encore que `60-ultima.conf` est pris en compte par fontconfig
  (`fc-match`), seulement sa présence.

## Demandes de contrat

1. **`schemas/theme.schema.json`** : impossible d'exprimer le thème `terminal` (16 couleurs ANSI) puisque
   `tones` n'accepte que `#RRGGBB(AA)`. Je l'ai implémenté en **thème virtuel** (documenté, testé,
   listé, `theme check` le valide en interne). Si le chef préfère un fichier, il faudrait autoriser un
   marqueur (`"ansi16": true`) ou des valeurs de la forme `ansi:red`.
2. **`appearance.schema.json`** : `pluginsEnabled` (et `modules[]`) vivent à la racine de
   `settings.json`, pas dans `appearance` ; aucun schéma ne couvre la racine. Demande : un
   `schemas/settings.schema.json` (ou une note dans docs/44) pour que les écrivains valident la racine.
3. **`EngineState.run.character`** : souvent `null` alors que `run.characterId` est rempli (échantillon
   v1.3). `auto-perso` lit `character` puis `characterId`. Demande : garantir `character` rempli quand un
   run existe, ou documenter que `characterId` est la source.
4. **`schemas/module.schema.json`** : `screens` est en minuscules (`map`, `shop`, `restSite`) alors que
   `EngineState.screen` est en PascalCase (`Map`, `Shop`, `RestSite`). Le daemon (T1) attend les
   minuscules ; demande : documenter la table de correspondance dans docs/44 §3/§6 pour que HUD,
   compagnon et TUI parlent le même vocabulaire.
5. **`HudSettings` vs `appearance.hud.position`** : `side` existe aux deux endroits (contrat v1.3 et
   appearance). Le TUI écrit `side` dans les deux pour rester cohérent ; demande : dire lequel fait
   autorité (ou déprécier `HudSettings.Side` en v1.5).
