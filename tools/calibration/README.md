# `tools/calibration` — poids de G appris sur les runs réels

Calibre les **poids des quatre dimensions du score global** (`docs/27` §7bis) sur l'index de runs
réels, au lieu de les choisir à la main. Python 3 **standard library uniquement**, hors ligne.

```bash
# 1. diagnostic d'une dimension (S17) : 24 définitions candidates d'Offense, AUC + β ajusté
python3 -I tools/calibration/diagnose_offense.py extract --stride 6 --out /tmp/cand.jsonl
python3 -I tools/calibration/diagnose_offense.py analyze --features /tmp/cand.jsonl

# 2. features : une ligne par run (état de fin de run) ET une ligne par run × acte
python3 -I tools/calibration/features.py --state both --stride 6 --out /tmp/features.jsonl

# 3. ajustement : écrit data/calibration/weights.json + tools/calibration/report.md
python3 -I tools/calibration/fit_weights.py --features /tmp/features.jsonl

# 4. vérifications
python3 -I tools/calibration/selftest.py                 # 69 vérifications, sans données
python3 -I tools/calibration/validate_csharp.py --decks 60   # parité avec le moteur C#
```

```
tools/calibration/
├── features.py           # reconstruit les 4 dimensions (mêmes formules que PlayerStrength.cs)
├── diagnose_offense.py   # S17 : compare 24 définitions d'Offense (AUC, β ajusté, contrôles)
├── validate_csharp.py    # compare champ par champ features.py ↔ PlayerStrength.G (dotnet run)
├── fit_weights.py        # régression logistique (IRLS) + modèle d'indice + rapport
├── selftest.py           # 69 vérifications hors données
├── report.md             # GÉNÉRÉ — ne pas éditer à la main
└── README.md
```

## Ce que ça produit

- `data/calibration/weights.json` (**commité**, < 20 Ko) : poids proposés par acte, coefficients,
  AUC, tailles d'échantillon, clause « corrélation, pas causalité ».
- `tools/calibration/report.md` (généré, commité) : ce que la data dit, ce qu'elle ne dit pas,
  et les contradictions avec les constantes du code.

## Ce que ça ne fait pas

- **Aucune modification des poids du C#.** Les poids de `StrengthWeights.Act1/Act2/Act3` restent la
  référence à l'exécution ; ce dossier ne fait que proposer. L'arbitrage est une autre tâche.
- **Mais la mesure d'une dimension peut changer ici.** S17 a corrigé la formule d'Offense dans
  `PlayerStrength.cs` *et* dans `features.py` : les deux doivent rester identiques, ce que
  `validate_csharp.py` vérifie champ par champ (60 decks, 1020 champs, 0 écart attendu).
- Pas de causalité : les features décrivent l'état du joueur, qui est en partie la conséquence
  de ses choix et de la chance. Voir `report.md` §5.

## Entrées

| fichier | commité ? | usage |
|---|---|---|
| `data/index/runs-v0.107.1.jsonl.gz` | non (517 Mo) | une ligne par run, décisions incluses |
| `data/catalog/cards_effects.json` | oui | dégâts/bloc/pioche/énergie par carte |
| `data/catalog/relics.json` | oui | tags des reliques (équivalents) |
| `data/catalog/monsters.json` + `encounters.json` | oui | menace par acte (médiane des boss) |

L'index est reconstruit par `tools/index_runs.py` depuis l'export public (docs/28 §2.2).

## Design d'observation (le point important)

L'index ne contient **pas** d'instantané du deck à chaque décision, seulement le deck final. Pour
un état par acte, on reconstruit **vers l'arrière** (deck final moins les cartes prises après
l'acte), comme docs/29 §2.2 l'impose. Et pour l'acte *k*, on ne garde que les runs **qui ont
survécu** à l'acte *k* : sinon les PV du dernier enregistrement sont ceux de la mort, et
« Survie » prédirait la mort par tautologie.

Conséquence assumée : **l'acte 3 n'est pas identifiable** (aucun run ne le survit sans gagner).
`weights.json` le déclare dans `not_identifiable` et le rapport explique pourquoi.
