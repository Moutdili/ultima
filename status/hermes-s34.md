# Compagnon v1.4 (T3) — rendu du 2026-10-30

Branche `hermes/t3-compagnon-v14`, partie de `origin/main` (`60bb99a`) ; `git merge origin/main` : déjà à jour.
Périmètre : `overlay/companion/**` et ce fichier. Aucune fenêtre affichée, aucune capture, aucun accès au
jeu, à ses données ou à sa mémoire. Aucun fichier réel de `~/.config/ultima`, `~/.local/share/ultima`,
`~/.local/state/ultima`, `$XDG_RUNTIME_DIR/ultima` n'a été touché ; l'isolation de `tests/conftest.py` est
conservée telle quelle (DISPLAY retiré, XDG temporaires, contrôle du vrai `settings.json` en fin de session).

## Fait

1. **Résolveur de thèmes** — `ultima_companion/themes.py` (nouveau) et `tones.py` (nouveau) :
   chargement de **tous** les `themes/*.json` présents (dépôt, `~/.local/share/ultima/themes/`,
   `~/.config/ultima/themes/`, `ULTIMA_THEMES_DIR` pour les tests), résolution
   `appearance.companion.theme` → `extends` (niveau multiple, cycles et profondeur refusés) →
   `appearance.companion.tones` par-dessus ; `auto-perso` résolu depuis `run.characterId`
   (`IRONCLAD`→`ironclad`…), repli `sts` hors run ou personnage inconnu ; thème cassé (JSON, id, label,
   API ≠ 1, `extends` introuvable/circulaire, tons manquants) ⇒ **repli `sts` + message** dans le bandeau de
   la fenêtre, la page Apparence et `--check`. Surcharges de tons invalides ignorées champ par champ, signalées.
2. **Tout le CSS par les tons** — `theme.py` génère la feuille depuis les tons du thème résolu (17 tons,
   `background`/`border` compris) ; **aucun hexadécimal dans le code** (`tones.py`, `theme.py` et tout
   `ultima_companion/*.py` n'en contiennent aucun, test). `palette.py` n'est plus qu'un **shim sans couleur**
   (72 octets de données, voir « Reste ») gardé pour le test croisé du HUD, qui lit encore ce nom de fichier.
3. **Polices** — `fonts.py` réécrit : recherche dans `~/.local/share/ultima/fonts/{perso,jeu,base,systeme}`
   (nom interne de la table `name`, sinon nom de fichier), puis fontconfig (`fc-list`), puis police du thème,
   puis police système ; taille `appearance.companion.font.size` bornée 12–24 px (arrondie) ; famille
   assainie pour le CSS. **La lecture de `game/pck/fonts` (et `$ULTIMA_GAME_FONTS`) est supprimée.**
   Seuls les fichiers retenus sont ajoutés à la FontMap Pango du processus (rien n'est installé).
4. **Modules perso et plugins** — `state.py` lit `EngineState.Custom` (`CustomModuleView`/`CustomRowView` :
   id `x.`, libellé, source, `screens`, lignes ton/note, `health`, `problem`) ; santé inconnue → `error`,
   entrées invalides ignorées, doublons écartés. `views.custom_group` les dessine comme les autres blocs
   (note S-F + ton), avec une note discrète « en retard », « erreur · … », « désactivé » (jamais la couleur
   seule) ; `screens` respecté sur le Tableau (écran courant) et non filtré sur la page Run. Les modules
   `x.*` des réglages qui ne sont pas publiés affichent « Non publié par le daemon ».
5. **Réglages** — `settings.py` : bornes du contrat **hauteur 0,30–0,85** et **opacité 0,35–1,00** (lecture
   ET écriture), `enabled` absent ⇒ `true`, `screens` lu (casse tolérée, écrans du contrat, doublons
   retirés) et écrit **seulement s'il est présent** ; clés inconnues d'un module conservées. Écriture
   atomique (tmp, fsync, rename, fsync du dossier) qui **relit le fichier juste avant d'écrire**, ne remplace
   que `modules` et `hud`, **conserve `appearance`, `pluginsEnabled` et toute clé inconnue** (jamais de
   doublon de casse), et **refuse** d'écrire un fichier illisible (`SettingsWriteError`).
6. **Éditeur d'apparence** — `ui.AppearancePage` + `appearance.py` (modèle pur) : thème (liste des thèmes
   présents + `auto-perso`) avec **aperçu** (bloc verre, notes S-F, mots des tons), 17 tons surchargeables
   (entrée `#RRGGBB[AA]`, réinitialisation), police (famille libre + taille 12–24), « police retenue » et
   « thème appliqué » (repli/avertissements). Le HUD et la console sont en **lecture seule** avec la
   commande `ultima` équivalente (`ultima theme set hud|tui|compagnon …`, `ultima font set …`).
   Écriture ciblée : `appearance.companion` seulement, relecture avant écriture, `appearance.hud`/`.tui`,
   `modules`, `hud`, `pluginsEnabled` et les clés inconnues conservés, atomique.
7. **Décisions de Mio** — la note du deck **suit l'ordre des réglages** (`views.run_sequence` /
   `run_blocks`, `RunPage` la dessine à sa place, plus en tête) ; la **page Run montre tous les modules des
   réglages, même désactivés pour le HUD** (`enabled: false` ⇒ bloc présent + « désactivé dans le HUD »),
   modules vides compris (une phrase calme), modules perso dans l'ordre des réglages.
8. **`--check`** affiche en plus l'apparence : thème demandé/effectif, police retenue et source, surfaces
   HUD/TUI avec leur commande, avertissements, alerte `!!` si repli ; toujours en lecture seule.
9. `EngineState.Custom` apparaît aussi dans la page Sources (`== Modules perso`).

## Prouvé

```
$ cd overlay/companion && /usr/bin/python3 -m pytest -q -p no:cacheprovider
284 passed, 1 skipped, 5 warnings in 0.93s
```
(avant modification, sur `origin/main` : `172 passed, 1 skipped` ; le skip est le test du vrai daemon,
sans `ULTIMA_DAEMON_DLL`.)

```
$ bash tools/verify-all.sh          # passage 1 (XDG_RUNTIME_DIR isolé, TMPDIR retiré)
.NET       vert
compagnon  vert
hud        vert
console    vert
PORTE : VERTE
$ echo $?
0
$ bash tools/verify-all.sh          # passage 2, immédiatement après
.NET       vert
compagnon  vert
hud        vert
console    vert
PORTE : VERTE
$ echo $?
0
```
(Un premier essai lancé pendant la porte d'un autre agent a donné « console ROUGE » : relancé seul, deux
fois de suite, vert. La suite console passe seule : `124 passed in 26.00s`.)

Repli `sts` sur thème cassé, visible dans l'interface (`--check`, même source que le bandeau et la page
Apparence) :
```
$ ULTIMA_THEMES_DIR=<tmp>/themes XDG_CONFIG_HOME=<tmp>/cfg ./ultima-companion --check
!! Thème « casse » : tons manquants (text, muted, …). Repli sur « sts ».
== Apparence
  Thème demandé : casse
  Thème effectif : sts — Slay the Spire (repli)
  Police : police système 15 px (système)
  HUD : sts · police du thème — commande : ultima theme set hud sts
```

Tests dédiés (noms) : `test_broken_theme_falls_back_with_a_visible_message` (7 variantes : JSON, id, API,
tons manquants, label, racine), `test_check_shows_a_broken_theme_alert_and_falls_back`,
`test_the_interface_shows_the_theme_message`, `test_cycle_is_refused_and_falls_back`,
`test_missing_sts_means_no_theme_and_a_message`, `test_no_hex_in_companion_sources`,
`test_css_uses_only_theme_colors_and_no_hex`, `test_save_preserves_appearance_plugins_enabled_and_unknown_keys`,
`test_save_rereads_before_writing_so_external_keys_are_never_clobbered`,
`test_save_companion_preserves_everything_else_and_is_atomic`,
`test_save_companion_reuses_existing_key_case`, `test_contract_bounds_are_the_v1_4_ones`,
`test_enabled_absent_means_true`, `test_screens_parsed_case_insensitively_and_serialized_only_when_present`,
`test_run_blocks_include_deck_score_at_its_settings_place`, `test_run_blocks_show_disabled_modules_with_a_note`,
`test_custom_health_is_discreet_and_never_colour_only`, `test_custom_groups_follow_settings_order_and_screens`,
`test_font_files_and_families_by_folder`, `test_resolve_from_folders_then_fontconfig_then_theme_then_system`,
`test_no_game_pck_read_anymore`.

## NON prouvé

- **Rien n'a tourné sur le vrai jeu ni dans une vraie session** : aucun état réel lu en direct (socket),
  aucun `auto-perso` observé sur une run en cours, aucune écriture vue par le daemon de Mio.
- **Aucune fenêtre GTK n'a été créée** : GTK 4 sans affichage tue le processus (`Gtk.Box()` sans display →
  SIGSEGV, vérifié). L'éditeur d'apparence, la page Run, le Tableau et le bandeau ne sont donc prouvés que
  par le modèle pur (thèmes, apparence, vues, écritures), l'import du module et la vérification statique des
  classes/propriétés GTK utilisées. Rendu, lisibilité, contrastes ≥ 4,5:1, tailles réelles non mesurés.
- **Polices réelles non exercées** : les tests utilisent des fichiers sfnt synthétiques (table `name`) et un
  jeu de familles fontconfig injecté ; `fc-list` réel n'est pas appelé dans les tests, et aucune vraie
  police `jeu/`/`perso/` n'a été chargée.
- **Commandes `ultima theme set|font set`** : affichées en lecture, non vérifiées contre la console (T2 pas
  encore fusionnée) ; les noms de surface `hud|tui|compagnon` restent à confirmer.
- **Thèmes intégrés autres que `sts`** : absents de `main` (T2). Le résolveur charge tout `themes/*.json`
  présent et les tests utilisent leurs propres thèmes en dossier temporaire ; les 10 thèmes de T2 n'ont pas
  été vus.

## Reste

- Validation visuelle par Mio : page Apparence (aperçu, tons, police), bandeau de repli, blocs perso.
- Après fusion de T2 : vérifier les thèmes intégrés et les polices `fonts/base/`, puis supprimer le shim
  `palette.py` quand le HUD parlera les tons du contrat (voir demandes).
- Éditer les `screens` par module depuis le compagnon (le modèle lit/écrit déjà `screens` ; l'édition est
  laissée au TUI, docs/44 §9).
- Proposer les familles connues (`fonts.families_by_folder`) en suggestion dans l'éditeur (aujourd'hui
  champ libre + message de repli).

## Demandes de contrat

1. **`ModuleSetting.Enabled` sans valeur par défaut** : le daemon (record C# non nullable) lit `false` quand
   `enabled` est absent, le compagnon lit `true` (docs/44 : « valeur absente ⇒ défaut »). Fixer `true` dans
   le contrat, ou rendre le champ `bool?` avec normalisation explicite côté daemon.
2. **Tons du HUD** : `overlay/gnome/ultima@mio/{palette,grammar}.js` parlent encore `gold/green/aqua…` et
   `overlay/gnome/tests/test-render.js` lit `palette.py` du compagnon pour comparer. Pour supprimer ce
   dernier vestige de palette codée en dur, migrer le HUD vers les tons du contrat (`grade.S`…) et mettre à
   jour ce test croisé (T4).
3. **`CardView.Cost == null`** ne distingue toujours pas « coût X » d'« injouable » (déjà signalé en v1.3) :
   un champ `isUnplayable` ou `costKind` lèverait l'ambiguïté.
4. **`CustomHealth.Disabled`** : préciser si un module désactivé reste publié dans `custom[]` avec ses
   dernières lignes (le compagnon l'affiche atténué + « désactivé ») ou s'il en disparaît.
5. **`appearance.companion.tones`** : confirmer que `background` et `border` y sont permis (le schéma les
   autorise, le compagnon les applique ; une surcharge de `background` change la teinte de la fenêtre).
