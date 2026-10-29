# S26 — Tags des reliques : 304/304 couvertes, 4 `no_effect` justifiés

Branche `hermes/s26-relic-tags`, worktree `~/wt-s26` (depuis `main` 75a4cff, **rebasée sur 0f27c6b**
— le registre est régénéré avec le `build_registry.py` de cette base, qui ajoute le gap
`cards_with_unknown_keyword`).
Point 4 de `docs/36-registre-entites.md` (« 140 reliques sans tags ») traité par des règles sur
les descriptions FR, comme les potions (`tools/index_potions.py`). Aucun `RelicScorer` touché,
aucun effet inventé : une relique dont le texte ne dit rien reçoit `no_effect` + sa description.

## 1. Couverture avant / après

| Mesure | Avant | Après |
|---|---:|---:|
| Reliques avec ≥ 1 tag | **164 / 304** | **304 / 304** |
| dont `no_effect` (statut explicite) | 0 | **4** |
| Reliques réellement taguées | 164 | **300** |
| Tags distincts utilisés | 12 | **41** (vocabulaire documenté) |
| Occurrences de tags | 189 | **425** |
| Tags par relique (max) | 2 | **3** (borne dure : 8) |

- 140 reliques sans tag → **136 taguées** (96 avec 1 tag, 36 avec 2, 4 avec 3) + **4 `no_effect`**.
- 164 reliques déjà taguées → 46 gagnent des tags **en plus** (118 inchangées) ; aucun tag existant
  n'est retiré ni réordonné (vérifié : `existing_tags ⊆ tags` sur les 304).
- `data/catalog/registry.json` régénéré (`tools/build_registry.py`) : `gaps.relics_without_tags`
  passe de 140 entrées à `[]`.

## 2. Les règles (`tools/index_relic_tags.py`, 40 règles)

Dérivation sur la description FR **débarrassée des balises couleur** (`[gold]…[/gold]`), insensible
à la casse. Une règle = un tag, un ou plusieurs regex, un exemple réel, et éventuellement des
**gardes** qui annulent la règle.

### 2.1 Gardes — la distinction décisive

Une formulation conditionnelle est un **déclencheur**, pas un effet de la relique. Sans garde,
`add_card` se posait sur les trois œufs (« Lorsque vous ajoutez un Pouvoir à votre Deck,
Améliorez-le »), `penalty` sur les reliques qui *réagissent* à une perte de PV, et `damage` sur
les reliques qui *réduisent* les dégâts.

| tag | garde | ce qu'elle évite |
|---|---|---|
| `add_card` | `Lorsque vous ajoutez`, `Chaque fois que vous ajoutez`, `Si vous ajoutez`, `vous ajoutez` | FROZEN_EGG, MOLTEN_EGG, TOXIC_EGG, BOOK_OF_FIVE_RINGS |
| `penalty` | `PV de moins`, `fois que vous perdez`, `vous perdez des PV` | TUNGSTEN_ROD (réduit la perte), DEMON_TONGUE, SELF_FORMING_CLAY, CENTENNIAL_PUZZLE |
| `damage` | `Effets négatifs`, `dégâts en moins` | UNSETTLING_LAMP (double un malus), UNDYING_SIGIL, PAPER_KRANE |
| `discard` | `ne défaussez pas` | RUNIC_PYRAMID |
| `cost` | `carte qui coûte` | INTIMIDATING_HELMET, IVORY_TILE (déclencheur de coût) |

### 2.2 Les 40 règles, avec un exemple de description réel

| # | tag | catégorie | exemple (description du corpus) | témoin |
|---:|---|---|---|---|
| 1 | `revive` | survie | « Lorsque vos PV sont réduits à 0, récupérez {Heal} % de vos PV max » | LIZARD_TAIL |
| 2 | `maxhp` | ressource | « Lorsque vous obtenez cette relique, augmentez vos PV Max de {MaxHp}. » | BIG_MUSHROOM |
| 3 | `heal` | ressource | « À la fin de chaque combat, récupérez {Heal} PV. » | BLACK_BLOOD |
| 4 | `block` | défense | « Démarrez chaque combat avec {Block} d'Armure. » | ANCHOR |
| 5 | `thorns` | défense | « Démarrez chaque combat avec {ThornsPower} d'Épines. » | BRONZE_SCALES |
| 6 | `mitigation` | défense | « Vous ne pouvez perdre plus de 20 PV en un seul tour. » | BEATING_REMNANT |
| 7 | `damage` | offense | « Chaque fois que vous Épuisez une carte, infligez {Damage} dégâts à un ennemi aléatoire. » | AKABEKO (`Vigueur`) |
| 8 | `aoe` | offense | « infligez {Damage} dégâts à TOUS les ennemis. » | CHARONS_ASHES |
| 9 | `poison` | offense | « appliquez {PoisonPower} de Poison à TOUS les ennemis. » | SNECKO_SKULL |
| 10 | `strength` | offense | « Démarrez chaque combat avec {StrengthPower} de Force. » | BRIMSTONE |
| 11 | `dexterity` | offense | « Démarrez chaque combat avec {DexterityPower} de Dextérité. » | BELT_BUCKLE |
| 12 | `focus` | offense | « Commencez chaque combat avec {FocusPower} de Focalisation. » | DATA_DISK |
| 13 | `energy` | ressource | « Gagnez {Energy:energyIcons()} au début de chaque tour. » | ART_OF_WAR |
| 14 | `draw` | ressource | « Au début de chaque combat, piochez {Cards} cartes supplémentaires. » | BAG_OF_PREPARATION |
| 15 | `gold` | ressource | « gagnez {Gold} Pièces d'or. » | AMETHYST_AUBERGINE |
| 16 | `potion` | ressource | « gagnez {PotionSlots} nouveaux emplacements de potion » | ALCHEMICAL_COFFER |
| 17 | `star` | ressource | « Au début de chaque combat, gagnez {Stars:starIcons()}. » | DIVINE_DESTINY |
| 18 | `orb` | ressource | « Canalisez {Lightning} Orbe kéraunique. » | CRACKED_CORE |
| 19 | `upgrade` | deck | « Améliorez une carte. » | ASTROLABE |
| 20 | `enchant` | deck | « Envoûtez une Compétence avec Imprégné. » | BEAUTIFUL_BRACELET |
| 21 | `transform` | deck | « Transformez {Cards} cartes. » | ARCHAIC_TOOTH |
| 22 | `remove` | deck | « retirez {Cards} cartes de votre Deck. » | BIIIG_HUG |
| 23 | `add_card` | deck | « ajoutez 1 carte parmi {Cards} cartes aléatoires à votre Main. » | ARCANE_SCROLL |
| 24 | `copy` | deck | « obtenez une copie supplémentaire d'une carte » / « jouée une fois de plus » | BING_BONG |
| 25 | `exhaust` | deck | « Chaque fois que vous Épuisez une carte » | BURNING_STICKS |
| 26 | `ethereal` | deck | « Les Frappes et les Défenses deviennent Éthérées. » | BIG_HAT |
| 27 | `retain` | deck | « Retenez votre Main le premier tour » / « ne défaussez pas » | BOOKMARK |
| 28 | `discard` | deck | « Pour chaque carte que vous défaussez pendant votre tour » | GAMBLING_CHIP |
| 29 | `cost` | deck | « peut être jouée sans coût » / « réduisez le coût » | BOOKMARK |
| 30 | `summon` | invocation | « Invoquez pour {Summon}. » | BOUND_PHYLACTERY |
| 31 | `forge` | invocation | « Forgez {Forge}. » | FENCING_MANUAL |
| 32 | `curse` | malus | « obtenez une Malédiction spéciale » | CALLING_BELL |
| 33 | `penalty` | malus | « perdez {HpLoss} PV » / « Démarrez chaque combat Désorienté » | DISTINGUISHED_CAPE |
| 34 | `debuff` | contrôle | « appliquez Vulnérable ({VulnerablePower}) sur TOUS les ennemis. » | BAG_OF_MARBLES |
| 35 | `shop` | méta | « Lorsque vous passez chez le Marchand » / « de remise sur tous les produits » | LORDS_PARASOL |
| 36 | `rest` | méta | « aux Sites de repos » / « Cuisiner » / « Fouiller » | DREAM_CATCHER |
| 37 | `path` | méta | « marquez {Combats} combats aléatoires sur le Plan » | FUR_COAT |
| 38 | `reward` | méta | « récompense de carte » | DINGY_RUG |
| 39 | `elite` | méta | « Les Élites donnent… » | BLACK_STAR |
| 40 | `relic` | méta | « obtenez une Relique aléatoire » | BLACK_STAR |

`no_effect` n'a pas de regex : c'est le statut par défaut quand aucun tag ne sort, plus trois
motifs de statut **forcé** (`Sans effet`, `retirée du jeu`, `C'est un diadème`) qui priment sur les
règles.

### 2.3 Sémantique des tags piégeux

- `debuff` = effet négatif infligé **aux ennemis** (seul à produire du ΔG via `PlayerStrength`).
- `penalty` = coût subi **par le joueur** (PV, statut). Volontairement distinct : sinon une relique
  qui s'auto-punit gagnerait du score de débuff.
- `mitigation` ≠ `block` : plafond/réduction de dégâts subis hors armure.
- `forge` reste un tag de mécanique nommée (« Forgez 3 ») : la description ne dit pas ce que Forge
  produit, on ne suppose rien de plus.
- Un tag par mécanisme, pas par mot : `BRONZE_SCALES` garde son `block` historique **et** gagne
  `thorns` (union, jamais remplacement).

## 3. Les 4 reliques en `no_effect`

| id | description citée | raison |
|---|---|---|
| `CIRCLET` | « C'est un diadème. » | cosmétique pur, aucun mécanisme |
| `DEPRECATED_RELIC` | « Cette relique a été retirée du jeu. » | aucun effet en jeu (le mot « relique » ne doit pas produire un tag `relic`) |
| `FAKE_MERCHANTS_RUG` | « Une piètre imitation. Sans effet. » | le texte l'affirme |
| `WONGO_CUSTOMER_APPRECIATION_BADGE` | « Sans effet. » | le texte l'affirme |

**La borne de 20 `no_effect` n'est pas atteinte, et c'est un résultat, pas un manque** : sur 304
descriptions FR, seules 4 n'énoncent aucun mécanisme. Toutes les autres nomment au moins une
mécanique évaluable (récompense, repos, chemin, transformation…). Les forcer en `no_effect` aurait
été plus destructeur que de les taguer : le tag « méta » reste la seule information disponible.

## 4. Distribution des tags (occurrences, 425 au total)

```
add_card 34 · draw 31 · block 30 · energy 30 · upgrade 22 · damage 21 · maxhp 17 · strength 17
heal 16 · reward 16 · enchant 15 · gold 13 · potion 13 · relic 12 · rest 12 · debuff 10 · aoe 9
cost 9 · elite 8 · transform 8 · exhaust 7 · orb 7 · penalty 7 · copy 6 · dexterity 6 · remove 6
mitigation 5 · shop 5 · curse 4 · no_effect 4 · path 4 · retain 4 · discard 3 · ethereal 3
star 3 · poison 2 · summon 2 · focus 1 · forge 1 · revive 1 · thorns 1
```

Par catégorie : ressource 130, deck 117, offense 56, méta 57, défense 36, malus 11, contrôle 10,
statut 4, invocation 3, survie 1.
Répartition par relique : 197 × 1 tag, 93 × 2, 14 × 3 — la queue longue est plate, aucun
empilement de tags décoratifs (la borne de 8 n'est jamais approchée).

## 5. Effet mesuré sur le scoring — et sa limite

`RelicScorer` n'est **pas** modifié (interdit). Les tags qu'il consomme réellement sont :

| tag | consommé par | effet |
|---|---|---|
| `block` / `draw` / `energy` / `heal` / `strength` / `debuff` / `curse` | `PlayerStrength` (`RelicBlockPerTurn` 2, `RelicDrawPerTurn` 0,5, `RelicEnergyPerTurn` 0,5, `RelicHealPerCombat`, `RelicScalingPoints`, +1 débuff, pénalité malédiction), plafond `RelicTagCap` 2 | ΔG du deck |
| `maxhp` / `heal` / `revive` | `RelicScorer.Needs` | terme « besoin de PV » / filet de sécurité |

Conséquence chiffrée :

- **2 reliques seulement** changent de ΔG aujourd'hui : `LIZARD_TAIL` (`revive` → terme « filet de
  sécurité » 5-15 pts quand PV ≤ 50 %) et `UNSETTLING_LAMP` (`debuff` → +1 débuff si la relique est
  dans le deck). Les 232 autres tags ajoutés ne sont **pas** convertis : `add_card`, `upgrade`,
  `damage`, `aoe`, `cost`… n'ont pas de poids dans `PlayerStrength`.
- **141 / 304 reliques** ont au moins un tag converti (contre 140 avant : `LIZARD_TAIL` était déjà
  visible via `maxhp`) ; **163 restent muettes pour le scorer**.
- Autrement dit : la couverture est complète (304/304) mais la **conversion** ne l'est pas. Étendre
  `PlayerStrength`/`RelicScorer` aux tags `damage`/`aoe`/`add_card`/`upgrade` est un autre
  chantier, volontairement laissé hors de cette passe (RelicScorer interdit).

## 6. Tests

`Ultima.Engine.Tests/RelicTagCoverageTests.cs` (5 tests, suite à **209** tests) :

1. `Every_relic_has_at_least_one_tag` — ≥ 300 reliques vues, aucune liste vide, aucun tag blanc.
2. `No_relic_is_unknown_to_the_tag_ledger` — ids catalogue ≡ ids registre (égalité stricte, aucune
   relique inconnue), statut ∈ {tagged, no_effect}, tout `no_effect` a une raison **et** cite sa
   description.
3. `Tags_are_declared_and_bounded` — chaque tag est déclaré au vocabulaire (détecte les fautes de
   frappe), pas de doublon dans une relique, ≤ `max_tags` (8).
4. `Every_rule_has_an_example_and_a_witness` — chaque règle a des regex, un exemple et un témoin
   réel qui porte le tag (une règle morte casse le test).
5. `Coverage_is_explicit_and_no_effect_stays_an_exception` — `tagged_after + no_effect = 304`,
   `no_effect ≤ 20`, `tagged_before < 304`, cohérence `no_effect_relics` ↔ tag `no_effect`.

Mutation test (fait puis annulé) : retirer les tags d'`ANCHOR`, ajouter `bogus_tag` à
`BAG_OF_MARBLES`, mettre 9 tags à `SOZU`, vider la raison d'un `no_effect` → **4 tests sur 5
échouent**, données restaurées (`--check` repasse).

Idempotence : `python3 -I tools/index_relic_tags.py --check` sort 0. `relics.json` conserve
`tags_source` (tags bruts de `index_relics.py`) : relancer la dérivation ne change ni
`existing_tags` ni `added_tags`. Si `index_relics.py` est relancé, `tags_source` disparaît et la
dérivation repart de zéro — le docstring de `index_relics.py` le signale.

**Budget de données** : la première version du registre (descriptions et preuves dupliquées,
JSON indenté, 184 Ko) faisait échouer `OfflinePackagingTests.Committed_data_stays_within_the_clone_budget`
(total 2 121 690 > 2 097 152 octets). Le registre est donc compact (67,7 Ko) : pas de
`description_fr`/`name_fr` par relique (ils sont dans `relics.json`, sauf pour les 4 `no_effect`
qui gardent leur citation), preuve (`pattern → extrait`) uniquement pour les tags **ajoutés**,
sérialisation sans indentation. Total commité après la passe : **2 004 816 octets**, marge 92 Ko.

## 7. Non prouvé / limites

- **41 tags dérivés d'un texte, pas du code du jeu.** Un tag dit « la description nomme cette
  mécanique », pas « l'effet vaut X ». Aucune valeur numérique n'est inventée.
- **Les 4 `no_effect` sont une lecture humaine des 4 descriptions** ; elles sont citées dans
  `relic_tags.json` pour être contestables.
- **Pas de pondération** : `damage`, `aoe`, `add_card`, `cost`, `upgrade`… sont posés mais aucun
  moteur ne les lit. Le gain de score immédiat est limité aux 2 reliques du §5 ; le gain réel est
  la matière première pour étendre `PlayerStrength`.
- **Faux positifs résiduels possibles** malgré les gardes : `gold` se déclenche sur `remise` (donc
  `MEMBERSHIP_CARD` est `gold` + `shop`) ; `relic` sur « une Relique » — vérifié à la main sur les
  12 porteurs, aucun cas de self-référence restant. `copy` sur « jouée une fois de plus »
  (THROWING_AXE) est un choix de modélisation, pas une copie littérale.
- **`PEN_NIB` / `MINIATURE_CANNON`** sont `damage` sans nombre : la magnitude est dans `DynamicVars`,
  non exploitée.
- **Chaîne de régénération** : `index_relics.py` puis `index_relic_tags.py` puis
  `build_registry.py`, dans cet ordre. Aucune de ces trois commandes n'est dans
  `hermes-verify.sh` ; une régénération partielle laisserait `relic_tags.json` périmé (le test 2
  échouerait, ce qui est le comportement voulu).
- **Test `Deck_de_60_cartes_reste_sous_5ms`** : échec de timing pré-existant et intermittent
  (observé 1 fois sur 2 exécutions complètes avant toute modification, passe seul 3 fois sur 3).
  Hors périmètre, non modifié.

## 8. Fichiers

Créés : `tools/index_relic_tags.py`, `data/catalog/relic_tags.json` (67,7 Ko compact, registre de
dérivation), `Ultima.Engine.Tests/RelicTagCoverageTests.cs`, ce fichier.

Modifiés : `data/catalog/relics.json` (tags complétés + `tags_source`),
`data/catalog/registry.json` (régénéré : `relics_without_tags` vide),
`docs/36-registre-entites.md` (point 4, tableau de couverture, invariant, en-tête),
`tools/index_relics.py` (docstring : avertissement de régénération).

Non touchés (interdits) : `Ultima.Engine/Scoring/Relics/RelicScorer.cs`, `Contracts/**`, `Saves/**`.
