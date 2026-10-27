# 05 — Spécification des features

Les algorithmes ci-dessous s'appuient sur des API **vérifiées** dans le source du jeu
(v0.107.1). Les chemins de fichiers sont relatifs à `~/sts2/game/pck/src/Core`.

---

## F1 — DamageForecast (dégâts hypothétiques par ennemi)

### API disponibles

| Élément | Fichier | Signature |
|---|---|---|
| Intent d'attaque | `MonsterMoves/Intents/AttackIntent.cs` | `Func<decimal>? DamageCalc`, `int Repeats` |
| Dégâts d'un coup | `AttackIntent.cs` (fin de fichier) | `int GetSingleDamage(IEnumerable<Creature> targets, Creature owner)` |
| Dégâts totaux | `Single/MultiAttackIntent.cs` | `GetTotalDamage(...)` |
| Pipeline réel | `Hooks/Hook.cs:1486` | `ModifyDamage(runState, combatState, target, dealer, damage, props, cardSource, hookType, previewMode, out modifiers)` |
| Weak | `Models/Powers/WeakPower.cs` | `ModifyDamageMultiplicative` → `0.75m` (modifiable par `PaperKrane`, `DebilitatePower`) |

Point crucial : `GetSingleDamage` appelle déjà le pipeline complet **du point de vue du
joueur local** (`Hook.ModifyDamage(..., ModifyDamageHookType.All, CardPreviewMode.None)`),
donc les dégâts affichés incluent force/vulnérable/faiblesse déjà appliqués.

### Calcul des variantes

```
base        = intent.DamageCalc()                       // brut déclaré par le monstre
current     = intent.GetSingleDamage(targets, owner)   // pipeline réel (ce que le jeu affiche)
```

Pour les variantes hypothétiques, on rejoue la partie multiplicative :

- **Weak ajouté** (le monstre n'est pas encore Weak) :
  `weak = current * weakMultiplier`, où `weakMultiplier` =
  - `0.75m` par défaut (`WeakPower.DynamicVars["DamageDecrease"].BaseValue`),
  - remplacé par `PaperKrane.ModifyWeakMultiplier(...)` si le joueur local porte
    `PaperKrane` (relique),
  - remplacé par `DebilitatePower.ModifyWeakMultiplier(...)` si le monstre a `Debilitate`.
  ⚠️ Ne pas appliquer si `owner.GetPower<WeakPower>() != null` : ce serait un double comptage.
- **Vulnerable ajouté** (le joueur n'est pas encore Vulnerable) :
  lire `VulnerablePower.ModifyDamageMultiplicative` pour le multiplicateur exact
  (ne pas supposer 1.5 : vérifier la valeur dans le modèle).
- **Vulnerable + Weak** : produit des deux multiplicateurs (ordre sans importance sur un
  produit, mais le cap `IntangiblePower` s'applique en dernier).

Formule de présentation :
```
16 x3   |  Weak: 12 x3 (36)   |  Vuln: 24 x3 (72)
```
Toujours afficher l'arrondi utilisé par le jeu : `Math.Max(0, (int)decimal)`.

### Où l'afficher

Ancré à droite de l'intent existant (`NIntent`), pas dans un panneau séparé : on veut
zéro déplacement du regard. Voir `docs/07-ui.md`.

---

## F2 — LethalIndex (« le 3e coup te tue »)

### Entrées

- `hits` = `intent.Repeats` (1 pour `SingleAttackIntent`, N pour `MultiAttackIntent`)
- `perHit` = `intent.GetSingleDamage(...)`
- `block` = block actuel de la cible (joueur local)
- `hp` = HP actuel (pas max)
- `blockLossPerHit` : le block encaisse, puis disparaît (sauf effets type Barricade —
  vérifier `BlockRetention`/pouvoirs avant d'affirmer)

### Algorithme

```
remainingBlock = block
remainingHp    = hp
lethalAt       = null
for i in 1..hits:
    dmg = perHit
    absorbed = min(remainingBlock, dmg)
    remainingBlock -= absorbed
    dmg -= absorbed
    remainingHp -= dmg
    if remainingHp <= 0 and lethalAt == null:
        lethalAt = i
```

### Sortie

| Cas | Affichage |
|---|---|
| `lethalAt == null` | `16x3 → 14 PV restants (block 20)` |
| `lethalAt` trouvé | `16x3 → 3e coup LÉTAL (−2 PV)` (rouge) |
| `perHit == 0` après block | `16x3 → tout bloqué` |
| mort au 1er | `LÉTAL au 1er coup` |

Extensions :
- Afficher l'overkill (`remainingHp` négatif → `-2`).
- Si la cible est un **allié** en multi : calculer pour chaque joueur, afficher le pire cas.
- Prendre en compte `IntangiblePower` (cap à 1) et les pouvoirs de réduction : en pratique
  passer par `Hook.ModifyDamage` plutôt que la boucle naïve si un pouvoir modifie par coup.

⚠️ Le calcul doit se refaire à chaque changement d'état : block gagné, power appliqué,
carte jouée. Source d'événements : hooks de combat + `CardPlay`.

---

## F3 — TurnPriority (carte « à jouer en premier »)

### Problème

Certaines cartes/reliques ont un payoff conditionné au fait d'être la **première carte
jouée dans le tour**. En multi, si deux joueurs jouent en même temps, l'ordre est décidé
par le jeu, pas par l'intention. Il faut donc **communiquer** : « X veut jouer en premier ».

### Détection (deux niveaux)

1. **Statique (catalogue)** — `tools/index_source.py` scanne les cartes dont le code
   dépend de « aucune carte jouée ce tour » (helpers `AnyCardsPlayedThisTurn`,
   `CardsPlayedThisTurn`, compteurs de tour). Sortie : `data/catalog/first_card_cards.json`.
   C'est une liste à valider manuellement (les faux positifs existent).
2. **Dynamique (main du joueur)** — à la main, on lit les cartes du joueur local et on
   croise avec le catalogue → si une carte « first card » est en main **et** jouable
   (coût vs énergie), on émet l'alerte.

### Alerte

- Locale : badge sur la carte + ligne dans l'overlay (`PRIO: <carte>`).
- Distante : `INetMessage` custom `UltimaPriorityMessage { playerId, cardId, turnNumber }`
  broadcasté. Voir `docs/08-multiplayer.md`.
- Fin d'alerte : la carte est jouée (`AfterCardPlayedLate`), défaussée, ou le tour change.

Ne **jamais** dire au joueur quoi jouer : seulement signaler l'intention. L'outil informe,
le joueur décide (et ça évite tout débat « triche »).

---

## F4 — RunScore (scoring de run)

### Par entité (inspiré de ronishae, en plus simple pour V1)

| Métrique | Définition |
|---|---|
| `runDamage` | dégâts attribués à la carte/relique/potion sur le run |
| `combatDamage` | idem sur le combat courant |
| `timesPlayed` / `timesDrawn` | ratio d'utilisation |
| `addedFloor` / `removedFloor` | cycle de vie dans le deck |
| `efficiency` | `runDamage / (coût énergétique cumulé)` |

Règles d'attribution V1 (assumées et documentées) :
- dégâts sur block comptés comme dégâts,
- overkill non compté,
- Vulnerable attribué FIFO à la carte qui l'a appliqué,
- poison réparti au prorata des stacks,
- les cas non attribuables sont listés dans l'UI (pas de faux chiffre).

Export : `<jeu>/mods/Ultima/exports/card_fights.csv` (une ligne par carte par combat).

### Score de deck (heuristique, explicable)

```
score = 0.45 * damageShare      (contribution normalisée)
      + 0.25 * consistency      (ratio cartes jouées / piochées)
      + 0.20 * curveFit         (adéquation coûts/énergie disponible)
      + 0.10 * synergyTags      (mots-clés partagés — table statique)
```
Chaque composante est affichable : un score non explicable est un score inutile.

---

## F5 — PathRisk (risque des chemins)

### Données

- Graphe : `Map/` (nodes, edges, types : combat, elite, shop, rest, event, boss).
- Pools de rencontres par acte et par type (`Models/Monsters`, `Models/Acts`).
- HP/énergie actuels, deck score, potions.

### Calcul

Pour chaque embranchement accessible depuis la position courante :
```
risk = Σ (p_encounter * expectedHpLoss) / hpRestant
     + malus(elite) si deckScore < seuil
     + bonus(rest) si hp < 50%
```
`expectedHpLoss` est estimé par les dégâts d'intent des monstres du pool × nombre de tours
moyen (constante par acte, explicite dans le code). **On affiche l'hypothèse**, pas un
chiffre magique.

Sortie : `Chemin A: Risque 0.62 (élite) · Chemin B: 0.18 (repos)`.

V2 : apprendre les moyennes depuis les runs réelles (CSV) plutôt que des constantes.

---

## Priorité d'implémentation

1. F1 + F2 (le cœur différenciant, testable en solo immédiatement)
2. F7 (UI) en parallèle minimal
3. F3 + F4-broadcast (multi)
4. F5 (scoring)
5. F6 (path)
