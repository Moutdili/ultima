# S23 — Cartes de soutien co-op : ΔG par allié, choix du porteur, cession

Branche `hermes/s23-coop`, worktree `~/wt-s23` (depuis `main` `712ccb6`). Ouvre le point
« Co-op » de docs/33 §2, jusqu'ici marqué ❌ « L3 non implémenté ».

Le scoring solo juge une carte **sur le deck qui la reçoit**. Une carte qui donne 11 de bloc « à un
autre joueur » n'ajoute rien au deck du porteur : `DeltaForCard` rend **0** (mesuré sur un deck
tortue : Lift → ΔG +0) et le fit la classe « inutile ». Or sa valeur dépend du deck **d'en face** :
le même Lift vaut **+7** pour un allié à 20 PV sans une seule carte de bloc (survie 12 → 31).
`CoopScorer` calcule ce ΔG allié par allié et recommande la cession quand l'écart est net.

## 1. Les 13 cartes de soutien (mesurées, `data/catalog/coop_cards.json`)

Sélection = `target` ∈ {`AnyAlly`, `AllAllies`} dans `data/catalog/cards_effects.json` : **8 + 5 = 13**
cartes sur 577. Le fichier est généré par `tools/index_coop.py` (source commitée uniquement, aucune
description reprise de `data/codex` qui n'est pas versionné).

| id | cible | type | coût | don chiffré (catalogue) | état |
|---|---|---:|---:|---|---|
| `BELIEVE_IN_YOU` | AnyAlly | Skill | 0 | énergie 2 | chiffrée |
| `COORDINATE` | AnyAlly | Skill | 1 | Force 5 (ce tour) | chiffrée |
| `DEMONIC_SHIELD` | AnyAlly | Skill | 0 | bloc = **ton** bloc ; −1 PV | calculée |
| `ENERGY_SURGE` | AllAllies | Skill | 1 | énergie 2 (tous) | chiffrée |
| `GLIMPSE_BEYOND` | AllAllies | Skill | 1 | 3 Âmes dans la pioche (tous) | chiffrée |
| `HUDDLE_UP` | AllAllies | Skill | 1 | pioche 2 (tous) | chiffrée |
| `IGNITION` | AnyAlly | Skill | 1 | canalise Plasma | **non chiffrée** |
| `INTERCEPT` | AnyAlly | Skill | 1 | bloc 9 (porteur) + redirige les attaques de l'allié | chiffrée |
| `LARGESSE` | AnyAlly | Skill | 0 | 1 carte incolore aléatoire en main | **non chiffrée** |
| `LEGION_OF_BONE` | AllAllies | Skill | 2 | Invocation 6 (tous) | chiffrée |
| `LIFT` | AnyAlly | Skill | 1 | bloc 11 | chiffrée |
| `MIMIC` | AnyAlly | Skill | 1 | bloc = bloc **d'un allié** | calculée |
| `RALLY` | AllAllies | Skill | 2 | bloc 12 (tous) | chiffrée |

9 chiffrées, 2 calculées (montant dépend de l'état du board), 2 non chiffrées (effet réel, valeur
absente de l'index des effets). `affects_carrier = true` pour les 5 `AllAllies` : le texte dit
« ALL players », le porteur en profite aussi.

## 2. Contrat et formule

```
entrée : carte + IReadOnlyList<ScoringContext> players   (players[0] = porteur ; 1 seul = solo)
ΔG_i   = G(deck_i + carte) − G(deck_i)                   PlayerStrengthCalculator.DeltaForCard
best   = argmax ΔG  (égalité ⇒ porteur, index 0)
sortie : CoopVerdict(CardId, BestAllyIndex, PerAllyDelta, Reason)
céder   si ΔG(best) ≥ ΔG(porteur) + CoopScorer.CedingThreshold (= 3)
```

- **Aucune formule parallèle** : le ΔG de chaque allié est celui du scoring solo (docs/27 §7bis,
  dimensions saturantes). `PerAllyDelta` est indexé comme l'entrée, `BestAllyIndex = -1` sur une
  liste vide.
- **Solo inchangé** : un seul contexte ⇒ `PerAllyDelta[0]` est exactement
  `PlayerStrengthCalculator.DeltaForCard(ctx, carte)` (test dédié), `ShouldTake` faux.
- `ShouldTake(carte, players)` est la règle littérale (écart ≥ 3) ; `CoopScorer.Recommends(verdict)`
  applique la même règle à un verdict déjà calculé (évite un second passage de G).
- `CoopScorer.IsSupportCard` / `IsSupportTarget` exposent la définition « cible allié » pour que
  l'appelant sache quelles cartes router vers ce scorer.

## 3. Exemples chiffrés (acte 1, Waterfall Giant — 15 de pire coup, 240 PV)

Porteur = tortue (12 × Defend+ et 1 Strike : 22,2 bloc/tour, survie 66 saturée, G 46) ;
allié = verre (10 × Strike, 20/70 PV : 0 bloc/tour, survie 12, G 31).

| carte | ΔG porteur | ΔG allié | écart | verdict |
|---|---:|---:|---:|---|
| `LIFT` (11 bloc) | 0 | **7** | +7 | **céder** |
| `RALLY` (12 bloc, tous) | 0 | **7** | +7 | **céder** |
| `INTERCEPT` (9 bloc) | 0 | **6** | +6 | **céder** |
| `BELIEVE_IN_YOU` (2 énergie) | 2 | 4 | +2 | garder (< 3) |
| `ENERGY_SURGE` (2 énergie, tous) | 1 | 2 | +1 | garder |
| `LARGESSE` (non chiffrée) | 0 | 1 | +1 | garder |
| `COORDINATE` (Force 5) | 4 | 2 | 0 | garder (porteur meilleur) |
| `HUDDLE_UP` / `GLIMPSE_BEYOND` | 0 | 0 | 0 | garder |
| `MIMIC` / `DEMONIC_SHIELD` (calculées) | 0 | 0 / 1 | 0 / +1 | garder |
| `IGNITION` / `LEGION_OF_BONE` | 0 | 0 / −1 | 0 | garder |

Détail du cas phare (`LIFT`) :

| joueur | G avant → après | survie | bloc/tour | consistance |
|---|---|---|---:|---:|
| porteur (tortue) | 46 → 46 | 66 → 66 | 22,2 → 22,9 | 69 → 69 |
| allié (verre 20 PV) | 31 → 38 | **12 → 31** | **0,0 → 3,0** | 69 → 69 |

Le don ne bouge que la dimension vide : +19 de survie chez l'allié (0,40 × 19 ≈ +7,6 → +7 de G),
+0,7 bloc/tour chez le porteur déjà saturé (ΔG 0). **3 cartes sur 13 sont recommandées** dans ce
couple ; 0/13 quand les deux joueurs ont le même deck de départ (ΔG identiques, aucun transfert).

## 4. Ce qui n'est PAS prouvé

1. **Le seuil 3 n'est pas calibré.** C'est 3 % de l'échelle 0..100, choisi pour dépasser le bruit
   d'arrondi d'une dimension. Aucun run co-op n'a servi à l'ajuster. Amplitudes observées : ΔG de
   soutien 0..7 dans ces decks, donc le seuil vaut ~40 % du maximum observé. Le passer à 2 ferait
   entrer `BELIEVE_IN_YOU` (écart +2) ; le passer à 7 ne garderait que `LIFT` et `RALLY`.
2. **Les effets réels des pouvoirs ne sont pas simulés.** Pour l'allié, la carte est ajoutée à **son
   deck** : le modèle mesure « un bloc de plus par tour », pas « 11 de bloc une fois, maintenant ».
   Le ΔG est donc un **proxy directionnel** : il classe correctement les decks qui manquent d'une
   dimension, il ne chiffre pas le gain réel du don. Un bloc ponctuel vaut probablement moins qu'un
   bloc permanent — le proxy surestime les dons de bloc, jamais l'inverse.
3. **4 cartes sur 13 ne sont pas mesurables** par ce modèle : `MIMIC` et `DEMONIC_SHIELD` (montant
   calculé depuis l'état du board), `IGNITION` et `LARGESSE` (aucun montant dans l'index). Elles
   retombent sur leur valeur de catalogue, donc ≈ 0 : elles restent sous-évaluées, comme avant.
4. **Pas de branchement dans `CardScorer`** (fichier interdit par la tâche). Le scorer est un point
   d'entrée séparé : tant que l'appelant ne lui passe pas les contextes des alliés, une carte de
   soutien garde son score solo. Le câblage (offre de cartes en co-op → `CoopScorer.Evaluate`) reste
   à faire dans la couche qui connaît les autres joueurs.
5. **Coût** : `n` contextes = 2·n calculs de G par carte évaluée (pas de cache). Négligeable à 2–4
   joueurs, à mesurer si le co-op dépasse.
6. `AnyAlly` **ne cible pas le porteur** (règle du jeu) : le modèle ne l'interdit pas explicitement,
   mais l'égalité départage vers le porteur, ce qui rend une auto-cession impossible.

## 5. Vérifications

- `tools/index_coop.py` → `data/catalog/coop_cards.json` (6,1 Ko, commité) : 13 cartes, 8 + 5,
  régénérable depuis le seul `cards_effects.json` commité.
- `CoopTests` (5 tests) : allié à dimension vide ⇒ cession ; porteur meilleur ⇒ garder ; sans allié
  ⇒ ΔG solo identique + liste vide/nulle sans exception ; ΔG identiques ⇒ aucune recommandation
  (écart 0 < 3, égalité ⇒ porteur) ; l'index committé est en phase avec `CardEffects` (13 ids,
  cibles identiques, aucune 14ᵉ carte de soutien cachée).
- `dotnet test` : **151/151** (146 + 5). `bash tools/hermes-verify.sh` : build `-warnaserror`,
  tests, champs interdits, stubs, contrats intacts.
