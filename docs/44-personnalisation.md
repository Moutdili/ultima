# 44 — Personnalisation : thèmes, polices, disposition, modules et plugins (contrat v1.4)

> Demande de Mio (2026-10-30) : « le thème je l'aime pas trop, je veux plus de thèmes possibles, avec la
> possibilité d'ajouter des modules ou polices ». Ce document est le contrat (propriété du chef). Il est
> ADDITIF : rien de v1.3 ne casse. Il remplace la règle « palette du jeu uniquement » du skill design §1
> **pour les thèmes autres que le thème par défaut** ; le thème par défaut reste la palette du jeu.

## 1. Principes
1. **Une configuration par surface** : HUD, compagnon et TUI ont chacun leur thème, leur police, leurs couleurs.
   Par défaut les trois utilisent le même thème (`sts`), donc les mêmes couleurs.
2. **Tout se règle sans code** : depuis le TUI (onglet Apparence), depuis le compagnon, ou à la main dans des
   fichiers JSON documentés et validés par un schéma (`schemas/*.schema.json`).
3. **Rien ne casse** : un thème, un module ou un plugin invalide est ignoré **champ par champ ou en entier**,
   avec un message clair (`ultima doctor`, journal du daemon) ; la surface retombe sur le thème `sts`.
4. **Tons sémantiques** : surfaces, modules et plugins ne parlent jamais en hex mais en **tons**
   (`text`, `muted`, `accent`, `danger`, `ok`, `warn`, `curse`, `enchant`, `info`, `grade.S` … `grade.F`).
   Le thème associe une couleur à chaque ton. Un plugin s'affiche donc bien dans tous les thèmes.
5. Règles inchangées : lecture seule, probabiliste seulement (les modules et plugins ne voient que
   `EngineState`, qui ne contient aucun tirage caché), notes S à F avec les mêmes seuils, raccourcis Super+Maj.

## 2. Fichiers (XDG)
| Quoi | Où | Écrit par |
|---|---|---|
| Réglages (dont `appearance`) | `~/.config/ultima/settings.json` | TUI, compagnon, à la main |
| Thèmes utilisateur | `~/.config/ultima/themes/<id>.json` | utilisateur / son agent |
| Modules déclaratifs | `~/.config/ultima/modules/<id>.json` | utilisateur / son agent |
| Plugins | `~/.local/share/ultima/plugins/<id>/plugin.json` (+ code) | utilisateur / son agent |
| Polices | `~/.local/share/ultima/fonts/{base,systeme,jeu,perso}/` | voir §5 |
| Thèmes intégrés | `themes/*.json` dans le dépôt, installés dans `~/.local/share/ultima/themes/` | dépôt |
| Schémas | `schemas/{theme,module,plugin,plugin-output,appearance}.schema.json` | dépôt (chef) |

**Règle d'écriture de `settings.json`** : tout écrivain (TUI, compagnon) relit le fichier juste avant
d'écrire, ne modifie que ses clés, **conserve les clés inconnues** et écrit atomiquement (tmp + rename).

## 3. `settings.json` → `appearance` (nouveau, v1.4)
```json
"appearance": {
  "hud": {
    "theme": "sts",
    "font": { "family": "Fira Sans", "size": 14 },
    "tones": { "accent": "#EFC851" },
    "position": { "side": "right", "anchor": "center", "offsetX": 0, "offsetY": 0, "width": 360 },
    "railEdge": true
  },
  "companion": { "theme": "sts", "font": { "family": "Fira Sans", "size": 14 }, "tones": {} },
  "tui": { "theme": "terminal", "tones": {} }
}
```
- `theme` : id d'un thème intégré ou utilisateur. `"auto-perso"` = thème du personnage de la run en cours
  (repli `sts` hors run). Pour le TUI seulement, `"terminal"` = **les 16 couleurs ANSI du terminal de
  l'utilisateur** (aucune couleur imposée, fond du terminal respecté).
- `tones` : surcharges ponctuelles (`#RRGGBB` ou `#RRGGBBAA`), appliquées par-dessus le thème.
- `font` : nom de famille (voir §5) et taille en px (12–24). Sans objet pour le TUI (police du terminal).
- `position` (HUD) : `side` left|right, `anchor` top|center|bottom, décalages en px logiques (bornés à
  l'écran), `width` 280–520 px. `HudSettings.HeightFraction` / `RestOpacity` (bornes v1.3) restent valables.
- Visibilité des blocs : inchangée (`modules[]` : `id`, `enabled`, `order`) ; les modules perso et plugins y
  figurent avec leur id préfixé `x.` (ex. `x.compteur-or`). Nouveau, optionnel par module :
  `"screens": ["map","shop",…]` (écrans où le bloc apparaît ; absent = écrans par défaut du module).
- Valeur absente ou invalide ⇒ défaut, champ par champ (comme v1.3).

## 4. Thèmes (`schemas/theme.schema.json`)
```json
{ "id": "ironclad", "label": "Ironclad", "api": 1, "extends": "sts",
  "tones": { "accent": "#FF5555", "text": "#FFF6E2", "muted": "#FFF6E299" },
  "glass": { "tint": "#801212", "tintOpacity": 0.35 },
  "font": { "family": "Fira Sans" } }
```
- `extends` : hérite d'un autre thème (un seul niveau de profondeur conseillé, cycles refusés).
- Intégrés, livrés dans `themes/` :
  - `sts` (défaut, palette `StsColors`, skill design §1) ;
  - par personnage : `ironclad`, `silent`, `defect`, `regent`, `necrobinder` (couleurs `NameColor` /
    `EnergyLabelOutlineColor` du skill design §2), plus `auto-perso` ;
  - neutres sobres : `nuit` (noir minimal), `ardoise`, `contraste` (haut contraste, contraste ≥ 7:1 du texte).
- Contraste : un thème dont `text`/fond tombe sous 4,5:1 est accepté mais signalé (`ultima theme check`).

## 5. Polices
`~/.local/share/ultima/fonts/` contient quatre dossiers séparés :
| Dossier | Contenu | Origine |
|---|---|---|
| `base/` | polices libres livrées par ULTIMA (licence OFL/Apache, fichier de licence à côté) | dépôt `fonts/base/` (≤ 2 Mo au total) |
| `systeme/` | liens symboliques vers les polices installées sur le système (index généré, jamais de copie) | `fc-list` à l'installation / `ultima font refresh` |
| `jeu/` | polices du jeu extraites **localement** de l'installation de l'utilisateur | `install.sh` ; **jamais commitées** (droit d'auteur, dépôt public) |
| `perso/` | polices déposées par l'utilisateur (.ttf/.otf) | utilisateur |
Le HUD (St/Pango) et le compagnon (GTK/Pango) les voient via un fichier fontconfig dédié installé par
`install.sh` (`~/.config/fontconfig/conf.d/60-ultima.conf`, retiré par `uninstall.sh`). `ultima font list`
liste les familles par dossier. Famille introuvable ⇒ police du thème, puis police système par défaut.

## 6. Modules déclaratifs (`schemas/module.schema.json`) — aucun code
```json
{ "id": "x.or-par-etage", "label": "Or / étage", "api": 1, "screens": ["map","shop"],
  "rows": [ { "text": "{run.gold} or · étage {run.floor}", "tone": "accent" },
            { "when": "run.gold >= 150", "text": "Shop rentable", "tone": "ok" } ] }
```
- `text` : gabarit `{chemin.dans.EngineState}` (camelCase du contrat), filtres limités (`|upper`, `|pct`,
  `|grade`, `|count`). `when` : comparaison simple (`== != < <= > >=`, `and`, `or`), rien d'autre.
- **Évalués par le daemon** (un seul évaluateur) et publiés dans `EngineState.Custom` (§8) ; les surfaces ne font
  que les dessiner. Un chemin absent donne « — », jamais 0.

## 7. Plugins (`schemas/plugin.schema.json`, `schemas/plugin-output.schema.json`) — du vrai code, isolé
- Un plugin est un **processus séparé** lancé par le daemon : `plugin.json` = `id` (`x.…`), `label`, `api: 1`,
  `exec` (ex. `["python3","main.py"]`), `screens`, `timeoutMs` (≤ 500), `minIntervalMs` (≥ 250).
- Protocole : à chaque publication d'état (au plus une fois par `minIntervalMs`), le daemon écrit sur stdin
  **une ligne JSON** `{"api":1,"state":EngineState}` et attend **une ligne JSON** sur stdout conforme à
  `plugin-output.schema.json` : `{"rows":[{"text":"…","tone":"…","grade":"A"?}]}` (≤ 20 lignes, ≤ 160 car./ligne).
- **Méthode pour ne rien casser** :
  1. Le plugin ne tourne JAMAIS dans GNOME Shell, le TUI ou le compagnon : seulement dans son processus, en
     `nice 19`, sans stdin autre que l'état, dossier de travail = son dossier.
  2. Sortie validée par schéma ; invalide, trop longue ou hors délai ⇒ ignorée pour ce tour ; 3 échecs de
     suite ⇒ plugin désactivé et signalé (`EngineState.Custom[].Health`, `ultima doctor`, journal).
  3. Un plugin n'est activé qu'après `ultima plugin test <id>` : rejoue les états d'exemple
     (`docs/ipc/*.sample.json`, fixtures) et valide chaque sortie. `ultima plugin new <id>` crée un squelette
     qui passe ce test.
  4. Aucune donnée cachée n'existe dans `EngineState` : un plugin ne peut pas en obtenir par ce canal. Un plugin
     qui lit lui-même la mémoire ou les saves du jeu enfreint les règles d'ULTIMA (documenté, non supporté).
  5. L'API est versionnée (`api: 1`) ; un plugin d'une API inconnue est refusé, pas deviné.

## 8. Contrat C# v1.4 (additif, `ContractMinor = 4`)
- `EngineState.Custom : IReadOnlyList<CustomModuleView>?` —
  `CustomModuleView(string Id, string Label, string Source /* "module" | "plugin" */, IReadOnlyList<string>? Screens,
  IReadOnlyList<CustomRowView> Rows, string Health /* "ok" | "stale" | "error" | "disabled" */, string? Problem)` ;
  `CustomRowView(string Text, string? Tone, string? Grade)`.
- `ModuleSetting` : champ optionnel `Screens` (`IReadOnlyList<string>?`).
- `appearance` n'est **pas** dans `EngineState` (réglage d'affichage pur, lu par chaque surface dans
  `settings.json`) ; le daemon ne le réécrit jamais.

## 9. Commandes (`ultima`)
`ultima theme list|show <id>|check <id>|set <surface> <id>` · `ultima font list|refresh` ·
`ultima module list|validate <fichier>|new <id>` · `ultima plugin list|new <id>|test <id>|enable|disable <id>` ·
onglet **Apparence** du TUI (par surface : thème, police, tons, position, blocs et écrans) avec aperçu.

## 10. Documentation
`docs/personnalisation/README.md` : écrit pour l'utilisateur **et pour son agent** (« lis ce fichier, puis
modifie tel fichier, puis lance telle commande de validation ») : emplacements, schémas, exemples complets
(un thème, un module, un plugin Python minimal), commandes de validation, dépannage. Chaque exemple du README
est testé automatiquement (il doit passer `check` / `validate` / `plugin test`).
