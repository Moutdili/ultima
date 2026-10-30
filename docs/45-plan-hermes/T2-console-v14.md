# T2 — Console `ultima` v1.4 (thèmes, apparence, plugins, polices, README)

**Skills** : `sts2-console-tui`, `sts2-design-system` (§1, §2, §8-12), `sts2-engine-contract`, `sts2-companion-app`.
**Périmètre** : `tools/console/**`, `tools/install.sh`, `tools/uninstall.sh`, `themes/**` (sauf `themes/sts.json`,
référence du chef), `fonts/**`, `docs/personnalisation/**`.
**Point de départ** : branche `origin/v2/console2` (un WIP : `ultima_console/schema.py`) ; état actuel décrit dans
`status/console.md` (124 tests, install/uninstall à manifeste).

## Ce que veut Mio (ses mots)
« Si la personne décide que son TUI respecte les couleurs basiques de son terminal tu respectes ; si elle veut tel
code couleur tu la laisses faire ; une partie config pour choisir les polices, les couleurs, les emplacements sur
l'écran, si tel bloc doit apparaître ou non. » Thèmes par personnage, neutres sobres, thèmes perso. Modules
déclaratifs et vrais plugins avec une méthode pour ne rien casser. Polices de base, système et du jeu, chacune dans
son dossier. « Le README doit permettre d'expliquer au user ou à son agent comment configurer proprement. »
Défaut actuel à corriger : les thèmes Textual ne changent que le cadre ; le contenu reste codé en dur dans `palette.py`.

## Travail
1. **Thèmes intégrés** dans `themes/` : `ironclad`, `silent`, `defect`, `regent`, `necrobinder` (skill design §2),
   `nuit`, `ardoise`, `contraste` (texte ≥ 7:1) ; `auto-perso` = résolu selon `run.character`, repli `sts`.
   Test : chaque thème valide le schéma ; contraste texte/fond mesuré.
2. **TUI entièrement thématisé** : résolveur (thème → `extends` → `appearance.tui.tones`) qui alimente le cadre
   ET le contenu. Plus aucun hex hors `themes/` (test). `terminal` = 16 couleurs ANSI, fond du terminal respecté,
   aucune couleur 24 bits (test). Couleurs du journal depuis le thème et `appearance.tui.logColors`. Bandeau inclus.
3. **`appearance` dans `settings.json`** : migration unique de `theme`/`logColors` de `console.json` vers
   `appearance.tui` (ancien fichier gardé) ; `console.json` ne garde que le lancement du daemon ; écrivain qui
   relit avant d'écrire, ne touche que ses clés, **conserve les clés inconnues**, écrit atomiquement.
4. **Onglet Apparence** (un sous-onglet par surface : TUI, HUD, compagnon) : thème + aperçu, tons, police/taille
   (HUD, compagnon), position du HUD (côté, ancrage, décalages, largeur) et liseré, blocs (activé, ordre, écrans),
   modules `x.*`, activation des plugins (`pluginsEnabled`) seulement après un test réussi.
5. **Commandes** : `ultima theme list|show|check|set <surface> <id>`, `ultima font list|refresh`,
   `ultima module list|validate|new`, `ultima plugin list|new|test|enable|disable` (appellent les commandes de test
   du daemon de T1 ; si absentes, validation par schéma et point « à brancher »). `--json`, `NO_COLOR`, `--color`.
6. **Polices** (docs/44 §5) : `install.sh` crée `~/.local/share/ultima/fonts/{base,systeme,jeu,perso}/` :
   `base/` = polices OFL/Apache seulement, licence à côté, ≤ 2 Mo, versionnées dans `fonts/base/` (sha256 vérifié) ;
   `systeme/` = liens symboliques depuis `fc-list` ; `jeu/` = extraction LOCALE, jamais commitée ; `perso/` vide.
   `~/.config/fontconfig/conf.d/60-ultima.conf` installé/retiré via le manifeste ; `ultima font refresh`.
7. **`docs/personnalisation/README.md`** pour l'utilisateur ET son agent (emplacements, schémas, exemples complets :
   un thème, un module, un plugin Python ; validation ; dépannage ; section « Consignes pour un agent »). Test :
   chaque exemple du README passe `theme check` / `module validate` / `plugin test`.

## Critères
- `tools/verify-all.sh` vert ; pytest console stable sur 2 passages.
- install → uninstall dans un HOME temporaire : arbre identique (polices et fontconfig compris).
- Un réglage écrit par le TUI conserve les clés inconnues et reste lisible par le compagnon (test).
