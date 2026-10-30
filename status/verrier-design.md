# Verrier — HUD verre v3 (design §8-12) — rendu du 2026-10-30

Branche `v2/verrier-design`, **rebasée sur `main` (`5e005bc`, après l'arrivée du compagnon v1.3)**, auteur Mio,
aucune mention d'outil.
Rien n'a été installé, activé ni rechargé dans la session ; aucun réglage GNOME modifié ; aucun
GNOME Shell lancé (même headless) ; vrais `~/.config/ultima/` et `$XDG_RUNTIME_DIR/ultima/` jamais
écrits (tests dans des dossiers temporaires) ; jeu, daemon (PID 663552), contrat, compagnon et `tools/`
non touchés.

## Commits
| sha | objet |
|---|---|
| `6c4f590` | feat(overlay): HUD verre v3 (eco/verre/loupe), rail en verre seul, ouverture fluide |
| `b51cbca` | feat(overlay): test window n°3 procedure, perf levers and hud-set tool |
| `165f502` | feat(overlay): same labels, grade tones and settings reading as the companion |
| (ce fichier) | docs(status): Verrier — verre v3 |

Le commit WIP du chantier interrompu (`b56dacf`, jamais poussé) a été remplacé par `6c4f590`.

## Fait
1. **Rail = bande de verre seule (§12)** : 14 px collés au bord, liseré or ; aucune pastille, aucun
   texte, aucun bouton, aucun acteur réactif. Modèle « tiroir » (R6 de `docs/perf/HYPOTHESES.md`) :
   la plaque a toujours la taille du panneau, seule `translation_x` est animée.
2. **Ouverture fluide (§12)** : verre 220 ms ease-out, puis contenu (préparé AVANT l'animation) en
   fondu ligne par ligne (délai 110 ms + 22 ms/ligne, montée 6 px, sans rebond) ; fermeture 150 ms,
   contenu effacé en 90 ms ; le flou reste pendant le glissement puis est coupé (pas de saut). Uniquement
   des transitions implicites Clutter (`actor.ease`), aucune boucle, aucun timer JS par image.
3. **Minimalisme (§11)** : shop / récompense / événement → **carte de décision seule** (lettre S-F
   en grand + choix + UNE raison) ; ailleurs **une ligne par module** (`Deck 11`, `Enchantées 1`,
   `Malédiction 1`, `Reliques 1`, `Potions 1`, `Chemins 23 · 2 choix`, `Note du deck C`) ; rien quand
   il n'y a rien (pas de ligne « 0 ») ; ni PV ni or du jeu ; ni titre ni logo ; détail seulement à
   l'épinglage (Super+Maj+I) ou au survol de la carte. Grammaire commune dans `ultima@mio/grammar.js`
   (voir « Grammaire » plus bas).
4. **HudSettings v1.3** (`ultima@mio/hud.js`) : lus **champ par champ**, du plus frais au plus
   ancien : `~/.config/ultima/settings.json` (surveillé par `Gio.FileMonitor`, lu, jamais écrit) >
   `state.settings.hud` (daemon) > GSettings (repli sans daemon) > défaut du contrat. Validité = celle
   du daemon (`SettingsStore.NormalizeHud`) ; valeur invalide → défaut de CE champ ; valeur valide hors
   bornes de sûreté → ramenée (opacité au repos ≥ 0,35 : leçon v1 ; hauteur 0,2-0,85 : surface < 15 %).
   JSON tronqué → derniers réglages valides gardés. `side=left` géré (rail au bord gauche).
5. **Niveaux de verre** : `eco` (aplat #192930 @ 0,85, **aucun effet**), `verre` (défaut :
   `Shell.BlurEffect` BACKGROUND rayon 24 + reflet `Shell.GLSLEffect` à uniformes fixes), `loupe`
   (flou rayon 10 + reflet + `Clutter.Clone` de la **fenêtre du jeu** — fenêtre active plein écran,
   sinon `global.window_group` — grossi ×1,06 autour du centre du panneau, barillet + aberration
   chromatique en GLSL). Rien de tout ça sur le rail ; la loupe n'existe que panneau ouvert, plaque
   immobile, unredirect inhibé ; détruite dès le début du repli. Repli automatique si une API manque.
6. **Aucun travail au repos** : survol piloté par le signal `position-invalidated` du curseur
   (`global.backend.get_cursor_tracker()`, comme `ui/magnifier.js` ; R4) — au premier mouvement on se
   déconnecte, on lit le pointeur 80 ms plus tard, on se reconnecte. Pointeur immobile : **zéro
   minuterie, zéro lecture**. En mouvement : ≤ 12,5 lectures/s, zéro écriture d'acteur hors du rail.
   Repli `hover-source=poll` (minuterie 12,5 Hz du spike S1, prouvée sur le vrai jeu). Un état publié
   panneau replié ne touche aucun acteur.
7. **Modules** : un module connu absent des réglages est ajouté activé à la fin, comme
   `SettingsStore.Normalize` du daemon (ex. `event` dans des réglages d'avant v1.2).
8. **Schéma** : nouvelle clé `hover-source` (`event` | `poll`) ; clés `quality`, `height-fraction`,
   `rest-opacity`, `side` = repli de HudSettings. `metadata.json` 0.3.0-verre-v3.
9. **Outil** `overlay/gnome/tools/hud-set.py` : règle le bloc `hud` de `settings.json` depuis un
   terminal (écriture atomique, autres clés gardées). Le compagnon (page HUD, arrivé sur `main`) fait la
   même chose à la souris ; le TUI `ultima` pas encore.
11. **Alignement avec le compagnon** (`165f502`) : mêmes libellés de modules (`Shop`, `Malédictions`),
   mêmes tons S-F, noms de propriétés insensibles à la casse comme le daemon (`PropertyNameCaseInsensitive`)
   — vérifié par test sur `overlay/companion/ultima_companion/{settings,palette}.py` (lus, non modifiés).
   Les écarts que `status/compagnon-design.md` relevait côté HUD sont réglés : malédictions en violet +
   le mot, module `event` rendu, `hud` lu par l'extension.
10. `PERF.md` (niveaux, `hover-source`, conditions A/B I, J, K) et `TEST-WINDOW.md` (fenêtre n°3).

### Tests mis à jour (et pourquoi)
| Test d'avant | Contredit par | Décision |
|---|---|---|
| `test-pure` « ancré à droite » (`L.right`) | modèle tiroir (§12 ouverture fluide, R6) : le champ `right` n'existe plus, le rail est désormais collé au bord (le panneau ouvert garde ses 4 px) | test réécrit sur les nouveaux champs, invariant renforcé (rail visible = 14 px au bord, ouverte = panneau), + côté gauche, + second écran |
| `test-pure` « palette ⊂ StsColors + charte » | `gray #808080` = `StsColors.gray` (0,5), exigé par §9 pour « — » ; `white` inutilisé | `gray` ajouté à la liste (source : `StsColors.cs` l. 61) ; `white` **retiré du code** (aucun usage) |
| `test-render` (blocs `DECK · 11 cartes`, en-tête `PV 66/66 · 116 or`, `aucune`, pastilles `aqua,red,gold`) | §11 (une ligne par module, carte de décision seule, pas de PV/or du jeu, rien sans valeur pratique) et §12 (aucune pastille) | réécrit sur la vue v3 ; gardé tout ce qui relève du contrat : « — » jamais 0/F, échelle S-F, dégradé en tête, version future, JSON invalide, ordre des modules, troncature annoncée |
| `test-extension` (crash `_root` null ; pastilles) | clés v1.3 absentes du harnais ; §12 | les défauts GSettings sont lus dans le VRAI schéma ; pastilles remplacées par « arbre visible = couches de verre seules » |

## Prouvé (sans session)
```
$ cd overlay/gnome && npm test
> gjs -m tests/test-pure.js && gjs -m tests/test-hud.js && node tests/test-hud.js && gjs -m tests/test-render.js && node tests/test-render.js && node --import ./tests/shell-harness/register.mjs tests/test-extension.mjs
tous les tests passent      (×6 ; 458 vérifications OK, 0 FAIL)
  test-pure 80 · test-hud 72 (gjs) + 72 (node) · test-render 77 (gjs) + 77 (node) · test-extension 80
$ glib-compile-schemas --strict --targetdir=<scratchpad>/schema2 ultima@mio/schemas ; echo $?
0          (gschemas.compiled 1260 octets, dans le scratchpad, rien d'installé)
```
Extraits qui répondent aux demandes :
- `OK  toutes valeurs invalides -> défauts` · `OK  invalide CHAMP PAR CHAMP : les champs valides sont gardés` ·
  `OK  quality : fichier > état > gsettings` · `OK  settings.json : valeurs toutes invalides -> défauts du contrat`
- `OK  loupe sans Clutter.Clone -> verre (repli annoncé)` · `OK  eco ouvert : AUCUN flou, AUCUN reflet, AUCUNE loupe (coût ~0)` ·
  `OK  loupe ouverte : flou léger (10) + reflet + clone du JEU (fenêtre active plein écran)`
- `OK  §12 rail : seuls racine + plaque + verre + teinte + liseré visibles (aucune pastille, aucun texte)` ·
  `OK  extension : aucune pastille (dot) ni bouton`
- `OK  <module>.js : aucune couleur hex (seul palette.js en définit)` (×9 modules, + rgb()/couleurs nommées) ·
  `OK  balisage : toutes les couleurs ∈ palette`
- `OK  repos 10 min, pointeur immobile : ZÉRO minuterie, ZÉRO lecture du pointeur, ZÉRO écriture d'acteur` ·
  `OK  replié : état publié sans AUCUN travail d'acteur (rendu différé)` ·
  `OK  extension : aucune boucle d'animation (repeat, Timeline, idle, later, after-paint)`

Le harnais (`tests/shell-harness/stubs.mjs`) compte toute écriture visible (propriété, style, texte,
`ease`, enfant, effet, uniforme) : c'est ce compteur qui prouve le « rien au repos ». Mutation testée :
retirer le correctif « ouverture interrompue par un relayout » fait échouer le test correspondant.

API vérifiées dans l'installation locale (lecture seule) : `Shell-18.typelib` (`BlurEffect`,
`GLSLEffect.add_glsl_snippet/get_uniform_location/set_uniform_float`), `Meta-18.typelib`
(`position-invalidated`, `get_focus_window`, `is_fullscreen`), sources de `libshell-18.so`
(`ui/magnifier.js` l. 138-317 : curseur + `Clutter.Clone` ; `ui/environment.js` l. 57-71 : `onComplete`
seulement si la transition finit ; `ui/layout.js` l. 885 `addTopChrome`).

## NON prouvé
- **Rien n'a tourné dans la vraie session ni sur le vrai jeu.** Rendu St/Clutter réel, alignement de
  la loupe, apparence du verre : non vus.
- **FPS non mesuré** (ni défaut, ni eco, ni loupe).
- Compilation des extraits GLSL (reflet, loupe) par le pilote radeonsi : aucun test hors session ne
  compile un shader.
- Que `position-invalidated` arrive quand le pointeur bouge au-dessus du jeu plein écran XWayland
  (sinon `hover-source=poll`, déjà prêt).
- Contraste ≥ 4,5:1 : calcul à la main seulement. Texte secondaire (crème 60 %) sur verre 0,72 :
  ≈ 5,9:1 sur fond sombre, mais ≈ 3,2:1 si le jeu derrière est blanc → à juger sur capture réelle.
- Hauteur de ligne réelle et estimation du retour à la ligne (32 caractères pour 232 px).

## Reste à faire
- Fenêtre de test n°3 (liste ci-dessous), puis réglages fins sur captures (opacité, rayon, reflet, loupe).
- Si la loupe dépasse le budget FPS : défaut `verre` (déjà le défaut) ; si le flou coûte : R7 (flou figé).
- Modules `route` et `party` (v1.2) : pas encore rendus (ignorés). Décider s'ils passent le test §11.
- TUI `ultima` : éditer `hud` et reprendre la grammaire ci-dessous (hors de mon périmètre).

## Fenêtre de test : quoi vérifier (procédure complète : `overlay/gnome/TEST-WINDOW.md`)
```bash
cd ~/v2-verrier-design && overlay/gnome/install-dev.sh --check   # 6× « tous les tests passent », n'installe rien
overlay/gnome/install-dev.sh                                     # puis déconnexion / reconnexion
gnome-extensions enable ultima@mio
journalctl --user -b -o cat /usr/bin/gnome-shell | grep -E '\[ULTIMA\]|JS ERROR' | tail -n 20
H=overlay/gnome/tools/hud-set.py
S="gsettings --schemadir $HOME/.local/share/gnome-shell/extensions/ultima@mio/schemas"; K=org.gnome.shell.extensions.ultima
```
1. Journal : `API flou=1 glsl=1 clone=1`, `plan : visible=1 verre=verre flou=0 reflet=0 loupe=0 unredirect-inhibé=1 survol=event/80ms`, aucun `JS ERROR`.
2. Rail : bande de verre de 14 px au bord droit, **sans pastille ni texte**, visible au-dessus du jeu plein écran.
3. Survol puis pointeur immobile : ouverture en ~0,2 s, sans à-coup, lignes en fondu. Sinon `$S set $K hover-source poll` et le noter.
4. Sortie 2,5 s : fermeture plus brève, flou présent jusqu'à la fin du glissement.
5. Shop (ou `docs/22-state.sample.json` copié, daemon arrêté) : carte seule `A` verte · `Scourge · 24 or soldé` · raison ; survol de la carte → détail.
6. `$H quality=eco` → aplat, pas de flou ; `$H quality=verre` → flou + reflet ; `$H quality=loupe` → jeu grossi/réfracté **aligné**, journal `loupe=1`.
7. `$H restOpacity=0.4`, `$H side=left`, `$H heightFraction=0.7`, puis `$H --reset`.
8. Clics et touches au jeu à travers la bande et le panneau ouvert ; Super+Maj+I / Super+Maj+O.
9. Captures composées (`tools/proof/capture-composed.py`) des gestes 2, 5, 6 (×3 niveaux) ; lisibilité du texte atténué sur fond clair.
10. FPS (Chronométreur) : conditions A, C, I, E, J, K, G de `overlay/gnome/PERF.md` §3.

## Besoins de contrat (demande au chef, rien modifié dans `Ultima.Engine/Contracts/**`)
| Champ | Type | Raison | Consommateurs |
|---|---|---|---|
| `HudSettings.TopFraction` | `double` (0..0,9, défaut 0,22) | position verticale du HUD réglable depuis le TUI/compagnon (aujourd'hui GSettings seulement) | HUD, TUI, compagnon |
| `RewardView/ShopView/EventView.Recommended` | `int?` (index de l'option, `null` = aucune) | la carte de décision choisit « meilleur total disponible » côté HUD ; publier le choix garantit la MÊME recommandation sur les trois surfaces (§11, une seule grammaire) | HUD, compagnon, TUI |
| bornes de `HudSettings` dans le contrat | constantes `HeightRange`, `RestOpacityRange` | trois avis aujourd'hui : daemon ]0, 1] / [0, 1] ; compagnon (interface) 0,30-0,90 / 0,20-1 ; HUD 0,2-0,85 / 0,35-1. Le HUD ramène à ses bornes (0,90 → 15,2 % de l'écran > 15 %, §7 ; opacité < 0,35 : rail invisible, leçon v1). Proposition : 0,30-0,85 et 0,35-1 pour tous | daemon, HUD, compagnon, TUI |
| `ModuleSetting.Enabled` absent | valeur par défaut fixée | daemon : `false` ; compagnon et HUD : `true` (relevé par le compagnon) | tous |

## Grammaire commune (à reprendre par le compagnon et le TUI)
Notes : lettre toujours écrite, S `#EFC851` · A `#7FFF00` · B `#2AEBBE` · C `#87CEEB` · D `#FFA518` ·
F `#FF5555` · `—` `#808080`. Ligne d'option : `<lettre> · <nom> · <note>`. Séparateur unique ` · `,
compte `4×`, troncature `… +N`. Malédiction : violet `#EE82EE` + le mot « Malédiction ».
Enchantement : aqua + nom et montant (« Célérité 1 »). Texte secondaire : crème `#FFF6E2` à 60 %.
Libellés : Deck, Enchantées, Malédiction(s), Reliques, Potions, Récompense, Boutique, Événement,
Chemins, Route, Équipe, Note du deck. Au plus un accent coloré par ligne.

## Questions pour Mio
1. Le liseré or (0,85) du rail au repos est-il accepté comme partie du verre, ou le rail doit-il être
   sans liseré (risque : invisible en jeu, leçon v1) ?
2. `hover-source=event` par défaut (zéro réveil au repos) : d'accord pour le garder si le test n°3
   montre qu'il marche au-dessus du jeu ? (`poll` reste disponible.)
3. Au combat, `Deck / Reliques / Potions` passe-t-il le test §11 « que fait le joueur de cette info dans
   les 10 prochaines secondes ? », ou le panneau doit-il rester vide en combat ?
