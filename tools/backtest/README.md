# `tools/backtest` — harnais de backtest hors ligne

Évalue une politique de choix de carte sur l'index compact des runs réels
(`data/index/runs-<build>.jsonl.gz`, gitignoré, 785 121 runs pour v0.107.1) et
sort des **métriques JSON comparables** entre politiques.

- Python 3 **standard library uniquement** (ni numpy, ni pandas), aucun accès réseau.
- Lecture **en streaming** : l'index fait 517 Mo compressés, la RAM utilisée reste ~25 Mo.
- Tout ce qui est écarté est **compté** (`rejected`, `malformed`, `modded_players`,
  `no_players`) : le rapport dit toujours sur quoi il porte.

```
tools/backtest/
├── loader.py     # lecture streaming + filtres (mode, ascension, solo/co-op, build)
├── policies.py   # protocole Policy.choose(decision, run) + baselines
├── metrics.py    # accord, skip, win rate, par acte / ascension / personnage / salle
├── run.py        # CLI
├── selftest.py   # 155 vérifications sur un fixture synthétique de 3 runs
└── fixtures/synthetic-runs.jsonl
```

## Démarrage

```bash
# 100 000 runs, politique « meilleur consensus de méta », JSON dans /tmp/bt.json
python3 -I tools/backtest/run.py \
    --runs 100000 --policy highest_meta --out /tmp/bt.json

# tout l'index (729 072 runs retenus, ~3 min 15)
python3 -I tools/backtest/run.py --runs 0 --policy highest_meta --out /tmp/bt-full.json

# contrôle aléatoire (étalon bas) et modèle de skip rudimentaire
python3 -I tools/backtest/run.py --runs 100000 --policy random --seed 7
python3 -I tools/backtest/run.py --runs 100000 --policy highest_meta --skip-below 50

# tests (aucun réseau, aucun index requis)
python3 -I tools/backtest/selftest.py
```

`-I` isole l'interpréteur **et retire le dossier du script de `sys.path`** :
`run.py` et `selftest.py` le réinsèrent explicitement. Sans ce bootstrap,
`import loader` échouerait — c'est volontairement testé par `selftest.py`
(sous-processus `python3 -I`).

L'index n'est pas versionné (docs/28 §3). S'il manque :

```bash
python3 -I tools/index_runs.py --help
```

## Entrée : l'index

Une ligne = un run JSON compact (produit par `tools/index_runs.py`) :

| champ | sens |
|---|---|
| `h` / `b` / `m` | hash, build, mode (`standard`, `daily`, `custom`) |
| `asc` / `n` / `w` / `ab` | ascension, nombre de joueurs, victoire, abandon |
| `pl[]` | joueurs : `id`, `c` (`CHARACTER.SILENT`), `hp`, `mhp`, `deck`, `rel`, `pot`, `pslot` |
| `d[]` | décisions : `t` (`card`/`relic`/`ancient`), `a` acte, `f` étage, `rt` salle, `p` joueur, `hp`/`mhp`, `g` or, `o` cartes proposées, `k` carte prise (`null` = skip), `ro`/`rk` reliques |

## Sélection (`--runs`, `--mode`, `--ascensions`, `--players`, `--build`)

| option | défaut | effet |
|---|---|---|
| `--runs` | `100000` | nombre de runs **retenus** (`0` = tout l'index) |
| `--mode` | `standard` | `standard`, `daily`, `custom`, `all` |
| `--ascensions` | — | liste explicite (`0,9,10`), prioritaire sur `--min-asc`/`--max-asc` |
| `--players` | `all` | `solo` (n = 1), `coop` (n > 1), `all` |
| `--build` | — | correspondance exacte sur `b` |
| `--min-decisions-run` | `10` | seuil de la tranche run-level `guarded` |
| `--exclude-abandoned` | off | retire les runs abandonnés du win rate |
| `--allow-modded-characters` | off | accepte les personnages moddés (hors tables de méta) |

Par défaut, seuls les runs dont **tous** les joueurs sont des personnages
canoniques (ironclad, silent, defect, necrobinder, regent) sont gardés : les
personnages moddés n'ont pas de notes de consensus. Sur l'index v0.107.1,
`--runs 0` retient 729 072 runs sur 785 121 lus.

## Politiques

Contrat (c'est celui du harnais Python, pas un contrat C#) :

```python
class Policy:
    name: str
    has_skip_model: bool = False
    def choose(self, decision, run) -> str | None: ...   # id de carte, None = abstention
    def would_skip(self, decision, run) -> bool: ...     # optionnel
    def decide(self, decision, run) -> tuple[str | None, bool]  # (carte, skip)
```

- `HighestMeta` : prend la carte offerte dont le **consensus** de méta est le plus
  haut (`data/meta/card_tiers.json` → `characters.<perso>.<CARD>.consensus.score`),
  repli sur `colorless`, ignore une carte absente de la méta, départage les
  égalités par identifiant (sortie déterministe). `--skip-below N` lui ajoute un
  modèle de skip (passer si la meilleure note < N), désactivé par défaut.
- `RandomPolicy` : choix uniforme dans l'offre, graine fixée — contrôle bas.

Piège encodé dans `CardMeta` : la **clé** JSON de `card_tiers.json` est le nom de
classe (`BladeOfInk`, `Abrasive`) alors que l'index emploie le champ `id`
(`BLADE_OF_INK`, `ABRASIVE`). On indexe par `id`, jamais par la clé.

Pour ajouter une politique : hériter de `Policy`, l'enregistrer dans
`policies.POLICIES`, et passer ses paramètres dans `run.make_policy`.

## Métriques (`metrics.to_dict()`)

- `decisions` : `opined` (la politique a un avis), `abstained` (aucune carte
  offerte n'est notée — **ce n'est pas un skip**), `agree`, `agreement_rate`,
  `pick` (accord quand l'humain a pris une carte), `skip` (accord quand l'humain
  a passé), `policy_skip_rate`, `human_skip_rate`.
- `by_act`, `by_ascension`, `by_character`, `by_players` (solo/coop), `by_room`,
  `by_context` (`reward` = offre de récompense à 3 cartes, `shop` = stock de
  boutique, `other`) : mêmes compteurs, plus le win rate accord/désaccord.
- `per_card` : `offered`, `policy_pick`, `human_pick`, `both`, `policy_rate`,
  `human_rate`, `precision` (= quand la politique prend cette carte, l'humain la
  prend aussi — permet de repérer les cartes sur-notées par la méta).
- `run_level` : trois tranches publiées ensemble — `all`, `excl_abandoned`,
  `guarded` (non abandonnés, ≥ `min_opined` décisions avec avis) — avec
  `mean_opined` et `abandoned_share` par tranche.
- `timing` : `seconds_scan`, `seconds_total`, `runs_per_second`,
  `within_target` (cible : 100 000 runs < 120 s).

### Ce que ces chiffres ne disent pas

**Corrélation, pas causalité.** Les runs où la politique « est d'accord » ne sont
pas un échantillon aléatoire : compétence du joueur, archétype, ascension,
profondeur du run. Aucun effet causal de la politique n'est mesuré ici, et le
JSON le dit dans `caveat` et `notes`.

Deux confondants mesurés pendant la construction, désormais exposés dans la sortie :

1. **Longueur du run.** Un run abandonné au premier étage n'a que 2-3 décisions ;
   sa fraction d'accord vaut 0 ou 1, et il pèse autant qu'un run de 40 décisions.
   D'où la tranche `guarded` : sur 100k runs, l'écart de win rate passe de
   −18,5 pp (tous) à −16,5 pp (non abandonnés, ≥10 décisions) et les runs
   « d'accord » restent plus courts (18,6 vs 34,6 décisions) — le signal négatif
   est un effet de profondeur, pas un effet de la politique.
2. **Boutique.** Le stock fait 4 à 10 cartes et l'achat est facultatif : 69 % des
   visites n'achètent rien. Une politique sans modèle d'achat y est
   mécaniquement à ~3 %. `by_context.reward` isole les récompenses à 3 cartes,
   seul terrain de comparaison honnête pour un choix de carte.

## Mesures obtenues (Fedora 44, Python 3.14.9, 12 cœurs, 1 processus)

Index v0.107.1, `--mode standard`, personnages canoniques, solo + co-op.

| exécution | runs retenus | décisions | temps | accord | accord prise | accord skip | récompense 3 cartes | boutique |
|---|---|---|---|---|---|---|---|---|
| `highest_meta` 100k | 100 000 | 2 411 261 | 23–26 s | 28,8 % | 40,2 % | 0,0 % | 34,2 % | 2,9 % |
| `random` 100k | 100 000 | 2 411 261 | 22,8 s | 22,9 % | 32,0 % | 0,0 % | 26,6 % | 7,1 % |
| `highest_meta --skip-below 50` 100k | 100 000 | 2 411 261 | 27,0 s | 28,9 % | 37,0 % | 8,5 % | 34,4 % | 2,9 % |
| `highest_meta` index entier | 729 072 | 17 327 198 | 196,5 s | 28,4 % | 39,4 % | 0,0 % | 33,8 % | 2,7 % |

Win rate (corrélation) sur les 100k, `highest_meta` : +1,4 pp au niveau décision
(44,3 % vs 42,9 %), −18,5 pp au niveau run (5,2 % vs 23,7 %, confondu par la
longueur), 20,2 % de win rate global sur le corpus filtré.

Lecture : la méta brute fait mieux que le hasard (+5,9 pp d'accord global,
+8,2 pp sur les prises, +7,6 pp sur les récompenses à 3 cartes), mais elle
plafonne à 40 % d'accord sur les prises — c'est une note de **puissance/Elo**
de carte, pas un choix contextuel. Un seuil de skip naïf ne l'améliore pas : il
échange des prises correctes contre quelques skips.

## Limites connues

- **Biais de sélection** : les décisions humaines ne sont pas randomisées ; un
  désaccord peut être une bonne décision du joueur que la méta ne voit pas.
- **Skip non modélisé** par la baseline : un skip humain (28 % des décisions)
  est un désaccord mécanique. `--skip-below` n'est qu'un seuil global, non
  contextuel (PV, or, acte, boss).
- **Pas de re-simulation** : on rejoue la décision avec l'état de l'époque
  (`hp`, `mhp`, `g`, acte, étage, salle), pas la suite du run. Aucune inférence
  contrefactuelle.
- **Co-op** : compté par décision-joueur ; un run co-op pèse autant que le
  nombre de ses décisions, pas comme un run.
- **Ascensions extrêmes** : `asc` peut valoir −15…20 dans l'index (runs custom) ;
  ces tranches existent dans `by_ascension` mais ont un `n` minuscule.
- **Notes saturées** : plusieurs cartes ont `consensus.score = 100` ; l'égalité
  est départagée par identifiant, donc le choix parmi elles est arbitraire.
- **Méta statique** : `card_tiers.json` est un artefact daté (`generated`) ; un
  run de 2025 est comparé à des notes de 2026.
