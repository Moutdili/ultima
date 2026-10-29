# `data/` — ce qui est commité, ce qui ne l'est pas

**Règle (docs/28 §3)** : un clone du dépôt doit être utilisable **hors ligne**, sans rien télécharger
de lourd. Le moteur ne lit donc **que** des fichiers commités ici. Les gros corpus (export de runs,
index, codex) ne servent qu'à **produire** ces artefacts et à faire le backtest ; ils sont
gitignorés et ne sont jamais nécessaires à l'exécution.

Deux garde-fous automatisés vérifient cette règle :

```bash
bash tools/check-offline.sh     # clone propre (git archive) → build + tests, échoue si un fichier requis manque
dotnet test Ultima.Engine.Tests -c Release   # OfflinePackagingTests.cs encode le contrat (volumes, budget, chemins)
```

`check-offline.sh` exporte **uniquement** les fichiers trackés par git (`git archive HEAD | tar -x`),
puis build et teste ce clone. Si un test dépend d'un fichier non commité, il échoue là-bas — donc il
échoue avant le merge.

## 1. Commité — lu par le moteur à l'exécution (1,6 Mo au total)

Budget : **< 2 Mo** (`OfflinePackagingTests.Committed_data_stays_within_the_clone_budget`).
Ces fichiers sont générés : ne pas les éditer à la main, relancer le script indiqué.

### `data/catalog/` — catalogues du jeu (885 Ko)

| Fichier | Rôle (ce que le moteur en fait) | Volume | Généré par |
|---|---|---|---|
| `cards.json` | types, raretés, coûts, malédictions (`CardCatalog`) | 578 cartes · 205 Ko | `tools/index_source.py` |
| `cards_effects.json` | dégâts, bloc, coups, pioche, énergie, pouvoirs, deltas d'amélioration (`CardEffects`) | 577 cartes · 298 Ko | `tools/index_card_effects.py` |
| `monsters.json` | PV, moves, dégâts normal/ascension (`ThreatModel`, `BossCatalog`) | 128 monstres · 175 Ko | `tools/index_source.py` puis `tools/index_threats.py` |
| `encounters.json` | rencontre → monstres, acte, type de salle (`ThreatModel`) | 87 rencontres · 55 Ko | `tools/index_threats.py` |
| `intents_by_monster.json` | plus gros coup par monstre (`BossCatalog`) | 39 Ko | `tools/index_source.py` |
| `relics.json` | id, nom FR, description FR, tags mécaniques (`RelicCatalog`) | 304 reliques · 70 Ko | `tools/index_relics.py` |
| `potions.json` | id, nom FR, description FR, tags (`PotionCatalog`) | 64 potions · 14 Ko | `tools/index_potions.py` |
| `priority_cards.json` | cartes à payoff conditionnel (`PriorityCatalog`, embarqué aussi dans `Ultima.dll`) | 34 cartes · 13 Ko | `tools/index_source.py` |
| `powers.json` | pouvoirs modifiant les dégâts | 30 pouvoirs · 4 Ko | `tools/index_source.py` |
| `stats.json` | compteurs de l'indexation (documentation/debug) | 1 Ko | `tools/index_source.py` |

### `data/meta/` — notes communautaires (785 Ko)

| Fichier | Rôle | Volume | Généré par |
|---|---|---|---|
| `card_tiers.json` | notes par carte + consensus + écart inter-sources (`MetaTable`) | 469 Ko | `data/meta/src/build_meta.py` |
| `relic_tiers.json` | notes par relique (`MetaTable`) | 185 Ko | `data/meta/src/build_meta.py` |
| `archetypes.json` | clusters (cartes/relics clés par archétype) (`MetaTable`, `SynergyEngine`) | 77 Ko | `data/meta/src/build_meta.py` |
| `sources.json` | registre des sources + statut de redistribution (curé à la main) | 15 Ko | édition manuelle |
| `src/build_meta.py`, `src/archetypes_curated.json` | générateur + graine curée | 26 Ko | — |

Variantes **non commitées** (si présentes, elles sont préférées fichier par fichier, jamais en bloc) :
`data/meta/local/` (notes complètes sous licence, usage personnel) et `data/meta/cache/` (copies
brutes des sources, contiennent les commentaires d'auteurs).

## 2. Gitignoré — jamais lu à l'exécution (corpus de production)

| Dossier | Poids constaté | Contenu | Régénération |
|---|---|---|---|
| `data/exports/` | 2,5 Go | export public spire-codex, JSONL gzippé (785 121 runs v0.107.1) | `python3 -I tools/fetch_spire_codex.py` (réseau) |
| `data/index/` | 517 Mo | index compact des runs (19 M décisions) pour le backtest | `python3 -I tools/index_runs.py` |
| `data/runs/` | 13 Mo | échantillons/replays de runs pour les mesures | `tools/fetch_*.py` (réseau) |
| `data/codex/` | 1,3 Mo | données statiques spire-codex (cartes, monstres, rencontres, stats) | `python3 -I tools/fetch_codex_static.py` (réseau) |

Aucun de ces dossiers n'est requis pour `dotnet build` / `dotnet test` ni pour scorer en jeu :
`tools/backtest/` (Python) est le seul consommateur de `data/index/` et `data/exports/`.

## 3. Régénérer les artefacts commités

Ordre indicatif depuis la racine du dépôt. Les étapes 1–2 exigent des ressources **non livrées**
(jeu décompilé, réseau) ; les étapes 3–6 sont déterministes à partir de là.

```bash
# 1. jeu décompilé (non commité) → data/catalog/{cards,monsters,powers,priority_cards,intents_by_monster,stats}.json
#    game/ est produit par tools/decompile.sh ; sans jeu extrait, pointer --src vers une copie.
python3 -I tools/index_source.py --src game/pck/src/Core --out data/catalog --repo .
#    (ou, avec le conteneur : make index)

# 2. données statiques spire-codex → data/codex/ (réseau, gitignoré)
python3 -I tools/fetch_codex_static.py

# 3. effets numériques des cartes ← data/codex/cards.json
python3 -I tools/index_card_effects.py

# 4. modèle de menace ← data/codex/{monsters,encounters,encounter_stats}.json
python3 -I tools/index_threats.py

# 5. potions + reliques ← localisation FR du jeu
python3 -I tools/index_potions.py
python3 -I tools/index_relics.py

# 6. méta communautaire ← cache de sources (réseau) puis reconstruction locale
python3 -I data/meta/src/build_meta.py --fetch
python3 -I data/meta/src/build_meta.py
```

Après régénération : `bash tools/check-offline.sh` (poids, volumes, chemins, tests) avant de commiter.

## 4. Comportement quand un fichier manque

Aucun chargeur ne lève sur un fichier absent : il retombe sur une instance `Empty`
(`CardCatalog`, `CardEffects`, `BossCatalog`, `ThreatModel`, `PotionCatalog`, `RelicCatalog`,
`MetaTable`) et **omet** le terme de score concerné. Le score reste calculable, avec moins de
termes — jamais avec un chiffre inventé. C'est pour ça qu'un clone propre doit malgré tout
**tout** trouver : les tests de packaging vérifient les volumes réels (`> 500` cartes,
`> 100` monstres, `> 80` rencontres, `64` potions…), pas seulement l'absence d'exception.
