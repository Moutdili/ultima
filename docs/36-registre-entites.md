# 36 — Registre exhaustif des entités et couverture mesurée

> Générateur : `tools/build_registry.py` → `data/catalog/registry.json` (**100 Ko**, commité,
> compact : les noms/classes restent dans `cards.json` et `relics.json`).
> Test d'invariant : `Ultima.Engine.Tests/RegistryCoverageTests.cs`.
> Principe : **aucune entité inconnue** — chaque carte, relique, potion, pouvoir, monstre et
> rencontre a une entrée avec un statut explicite quand la donnée manque.

## Comptes

| Catégorie | Total |
|---|---:|
| Cartes | **578** |
| — Attaques | 195 |
| — Compétences | 234 |
| — Pouvoirs (cartes) | 111 |
| — Malédictions | 18 |
| — Statuts | 17 (dont `DEPRECATED_CARD`) |
| — Quêtes | 3 |
| Reliques | 304 |
| Potions | 64 |
| Pouvoirs (modèles) | 260 |
| Monstres | 115 |
| Rencontres | 87 |

## Couverture

| Mesure | Couvert | Sur |
|---|---:|---:|
| Cartes avec effets | **577** | 578 (`DEPRECATED_CARD` exclu) |
| Cartes avec valeurs numériques | 348 | 578 |
| Malédictions classées (mesure ou effet) | **18** | 18 |
| Statuts classés (mesure ou effet) | **17** | 17 |
| Pouvoirs avec hook évaluable | **52** | 260 |
| Pouvoirs documentés (code + table manuelle) | **56** | 260 |
| Reliques avec au moins un tag | **304** | 304 |
| Monstres avec contrainte spéciale | 3 | 115 |
| Rencontres avec statistiques | 50 | 87 |

## Ce qui a été fermé

1. **Malédictions** : 17 mesurées + `BAD_LUCK` classée par effet (perte de PV, sévérité 75) →
   **18/18**. La toxicité retenue est `max(mesure, effet)` : Normality 41 → **95** (docs/37).
2. **Statuts** : **17/17** classés par effet (docs/41) — `damage_when_drawn`, `hp_loss`,
   `energy_loss`, `draw_block`, `play_limit`, `unplayable`, `self_removing`, `other`. Bandes :
   **−12** (sévère), **−6** (modéré), **−3** (bénin), `unclassified` → −6 explicite.
3. **Reliques** : 164 → **304/304 taguées** (300 avec mécanique, 4 `no_effect` justifiés :
   Circlet, Deprecated Relic, Fake Merchant's Rug, Wongo Badge). 41 tags distincts.
4. **Pouvoirs** : 49 → **56** couverts. Les 204 restants n'ont **pas** de formule : ils agissent par
   des hooks d'événement (`AfterTurnEnd`, `AfterCardPlayed`), pas par une expression. Aucune
   constante ne les débloque — il faudrait une couche de simulation d'événements.

## Les trous restants, nommés

- **204 pouvoirs non évaluables** : comportementaux, pas formulaïques. Liste complète dans
  `registry.json` (`gaps.powers_without_evaluable_hook`).
- **37 rencontres sans statistiques** (`avg_damage`/`avg_turns`) : le moteur dit « aucune mesure ».
- **112 monstres sans contrainte** : normal, seuls 3 ont un plafond ou un respawn (docs/33).
- **163 reliques taguées mais muettes pour le scorer** : `RelicScorer` ne lit que 7 tags
  (`block/draw/energy/heal/strength/debuff/curse`). Deux reliques changent de ΔG aujourd'hui
  (`LIZARD_TAIL` revive, `UNSETTLING_LAMP` debuff) ; l'extension est un chantier séparé.
- **Budget de données** : le registre a été compacté de 177 à 100 Ko pour tenir sous les 2 Mo du
  clone (test `Committed_data_stays_within_the_clone_budget`).

## Corrections apportées aux documents antérieurs

- docs/33 annonçait « 25 multiplicateurs de dégâts » : côté **pouvoirs** c'est **21** (19 `code` +
  2 `manual` : `Slow`, `Intercept`), plus 4 reliques (`PenNib`, `UndyingSigil`, `VitruvianMinion`,
  `PaperPhrog`) = 25 au total.
- docs/33 annonçait « 16 énergies, 17 pioches » : **non reproductible**. Mesuré côté pouvoirs :
  **4 `ModifyMaxEnergy` + 1 `ModifyEnergyGain`** et **8 `ModifyHandDraw`** (11 `code` + 1 `manual` :
  `MindRot`) ; côté reliques : 12 `ModifyMaxEnergy` et 10 `ModifyHandDraw`.
- `tools/index_source.py` écrivait `data/catalog/powers.json` (30 entrées) et aurait écrasé le
  catalogue de 260 pouvoirs : il écrit désormais `powers_damage.json`.

## Invariant garanti par les tests

- Toute carte du registre a un type connu (`attack`, `skill`, `power`, `curse`, `status`, `quest`).
- Aucune carte n'est sans effets (hors placeholder).
- **Toute malédiction et tout statut a un statut d'impact** : `measured`, `effect` ou `no_data`,
  jamais absent.
- Les identifiants sont uniques par catégorie ; les comptes ne peuvent pas dériver sans faire
  échouer la suite.

## Ce que ça change

Un score ne peut plus être « silencieusement faux » : si une entité n'est pas modélisée, elle est
listée, et le moteur applique une valeur par défaut **explicite** avec sa source. C'est la base
solide demandée : à jour, exhaustive, et vérifiable par une commande.
