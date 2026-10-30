# Demandes de contrat — consolidées après T1→T4

Les rapports individuels (`status/hermes-s32.md` à `s35.md`) vivent dans `status/`, **ignoré par git** :
ce fichier est le seul endroit où le chef voit les demandes d'un coup d'œil. Dix-huit demandes, dont
**une contradiction entre deux composants déjà livrés** (D1) et **deux doublons v1.3/v1.4** (C1, C2).

Rien n'a été modifié dans `Ultima.Engine/Contracts/**`, `schemas/**`, `docs/22`, `docs/44`.

---

## A — Nullabilité et fraîcheur des échantillons

**A1. `EventView.Name` et `EventOptionView.Label` sont non nullables** — portée par T1.
Le miroir peut ne rendre aucun texte (base de connaissance incomplète) ; la règle du projet est
« texte absent ⇒ null, jamais la clé ni l'id ». Le daemon publie donc `name: null` dans un champ typé
`string` (`name!` côté C#), et le JSON contient un null que le contrat interdit.
→ Demande : `string?` sur les deux champs, **ou** décision explicite « chaîne vide ».
→ Coût si non tranché : tout consommateur qui fait `name.Length` casse sur une récompense sans texte.

**A2. `docs/22-state.sample.json` n'a ni `custom` ni `screens`** — portée par T1.
`ContractShapeTests` tolère ces champs additifs v1.4 au lieu de vérifier exactement la forme.
→ Demande : rafraîchir l'échantillon du contrat en v1.4 (custom, screens, appearance).

**A3. `run.character` est souvent `null` alors que `run.characterId` est rempli** — portée par T2.
`auto-perso` (thème par personnage) doit alors se replier sur `characterId`, ce que T2, T3 et T4 ont
fait **chacun de leur côté**.
→ Demande : garantir `character` quand une run existe, **ou** documenter `characterId` comme source
officielle d'`auto-perso` (une seule ligne dans docs/44 évite trois implémentations divergentes).

---

## B — Schémas à aligner

**B1. `CustomRowView.Grade` : contrat et `plugin-output.schema.json` imposent `S..F`, mais
`module.schema.json` accepte toute chaîne ≤ 80** — portée par T1.
→ Demande : enum `S..F` côté module, **ou** documenter `grade` de module comme texte libre.
→ Coût si non tranché : un module peut publier `grade: "top"`, que le HUD affichera comme une note.

**B2. Aucun schéma ne couvre la racine de `settings.json`** — portée par T2.
`pluginsEnabled` et `modules[]` sont hors contrat (`EngineState` ne les contient pas) mais trois
composants les lisent et deux les écrivent.
→ Demande : `schemas/settings.schema.json`, **ou** une note normative dans docs/44.

**B3. `theme.schema.json` n'autorise que des hex** — portée par T2.
Le thème `terminal` (16 couleurs ANSI, aucun 24 bits, fond du terminal respecté) est donc un thème
**virtuel** validé en interne, pas un fichier.
→ Demande : marqueur `ansi16` dans le schéma, **ou** valeurs `ansi:<nom>`.

**B4. Écrans en minuscules côté module (`map`, `shop`, `restSite`) vs `PascalCase` côté
`EngineState.screen`** — portée par T2.
La table de correspondance est implicite ; le HUD et le compagnon l'ont réécrite chacun.
→ Demande : la documenter dans docs/44 §3/§6.

**B5. `CardView.Cost == null` ne distingue pas « coût X » de « injouable »** — portée par T3.
→ Demande : `isUnplayable` ou `costKind`.

**B6. Pas de ton `shadow` dans `theme.schema.json`** — portée par T4.
L'ombre du panneau prend `background` à 0,45 : sur un thème clair, l'ombre est claire.
→ Demande : ton `shadow` (ou `shadowOpacity`).

---

## C — Doublons v1.3 / v1.4 à arbitrer

**C1. `HudSettings.Side` (v1.3) et `appearance.hud.position.side` (v1.4)** — portée par T2 **et** T4,
qui ont dû trancher seuls. T2 synchronise les deux ; T4 fait primer `position.side`, `HudSettings.Side`
restant le repli fichier/état/GSettings.
→ Demande : dire lequel fait autorité (ou déprécier `HudSettings.Side` en v1.5). Aujourd'hui la
réponse dépend du composant.

**C2. `ModuleSetting.Screens` et `CustomModuleView.Screens`** — portée par T4.
Pour un module perso, les deux existent ; T4 a donné la priorité au second (ce que le daemon publie).
→ Demande : documenter la priorité, et si le HUD doit appliquer `ModuleSetting.Screens` aux modules
**natifs** ou si le daemon filtre déjà (sinon : deux vérités).

---

## D — Sémantique à préciser

**D1. `ModuleSetting.Enabled` absent : `true` ou `false` ? — CONTRADICTION ENTRE DEUX COMPOSANTS LIVRÉS.**
T1 (daemon) lit **false** quand `enabled` est absent ; T3 (compagnon) applique **true**, conformément
à docs/44 (« absent ⇒ true »). Un module sans `enabled` est donc **affiché actif par le compagnon et
publié inactif par le daemon**.
→ Demande : `bool?` (le défaut métier vit à un seul endroit), **ou** `enabled` obligatoire.
→ C'est la seule demande de cette liste qui produit déjà un comportement incohérent, pas seulement
un risque.

**D2. `CustomHealth.Disabled`** — portée par T3 : un module désactivé reste-t-il publié dans `custom[]`
avec ses dernières lignes ? Le compagnon l'affiche atténué avec la mention « désactivé ». À confirmer.

**D3. `appearance.companion.tones` : `background` et `border` sont-ils surchargeables ?** — T3 les
applique (le fond de fenêtre change de teinte) ; le schéma les autorise. À confirmer.

**D4. `glass.tintOpacity`** — portée par T4 : opacité du verre **ouvert** (lecture de T4 : 0,55 dans
`themes/sts.json`) ou base modulée par la surface (v1.3 : 0,72) ? Et `eco` doit-il rester +0,13
d'opacité ?

**D5. Bornes : ramener ou rejeter ?** — portée par T4. `width` 280–520 et `font.size` 12–24 (et les
bornes v1.3 : hauteur 0,2–0,85, opacité 0,35–1) : T4 **ramène** la valeur utilisateur dans la borne.
→ Demande : confirmer, ou dire que ces valeurs doivent être rejetées avec un message.

**D6. Migration du HUD vers les tons du contrat** — portée par T3 : `overlay/gnome/**/{palette,grammar}.js`
exposent encore leur propre vocabulaire de couleurs, d'où un shim `palette.py` côté compagnon.
→ Demande : inscrire cette migration (et la mise à jour de `test-render.js`) dans une tâche, pour
supprimer le dernier endroit où deux vocabulaires de couleur coexistent.

---

## Ce que Hermes n'a pas tranché tout seul

Aucune de ces demandes n'a été résolue en modifiant le contrat. Quand un composant avait besoin d'une
réponse, il a choisi **localement**, l'a écrit dans son rapport, et le comportement est couvert par un
test — donc réversible en changeant une seule ligne dès que le chef tranche.
