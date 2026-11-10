# T12 — Données : index des runs, biais de l'historique, calibration

**À lire d'abord** : `T9-run-coop-a10-gagnee.md` §2 et §3, `docs/31-calibration-poids-g.md`,
`docs/27-methode-scoring-contextuel.md`. **Voie R**, sauf le §3.4 (voie C). **§1 en vague 1, §2-3 en vague 2.**
**Skills** : `sts2-save-format`, `sts2-meta-scoring`.

Pourquoi : la run co-op A10 a montré que l'index lit mal les shops et les achats d'alliés, que la méta écrase les
autres termes, et que solo et co-op sont mélangés. Tant que ce n'est pas mesuré, aucun poids ne doit bouger.

## 1. Index des runs (vague 1)
1. `data/index/runs-*.jsonl.gz` est **absent de la machine du chef**. Dis où il est chez toi, ou reconstruis-le
   (`tools/index_runs.py`). Il reste hors dépôt (trop gros) : écris dans `data/README.md` la commande exacte et la
   taille attendue.
2. **Type de salle** : `tools/index_runs.py` garde `rt = map_point_type`. Utiliser `rooms[].room_type`, qui fait
   foi (une case `?` peut être un shop, T9 §1.5). Garder les deux champs dans l'index.
3. **Achats** (T9 §2) : pour le joueur local, un achat de carte se lit dans `cards_gained` ; pour un allié,
   `gold_spent` n'est pas fiable, calculer depuis `current_gold`. Une carte achetée n'est pas une carte « prise »
   à une récompense.
4. **Offres multiples** : un étage peut porter plusieurs récompenses et des relances (T9 §1.6, ordre de
   `card_choices` en §2). Une décision par offre, pas une par étage.
5. Marquer chaque run : nombre de joueurs, ascension, double boss.
6. Test : la fixture `coop3_a10_win.run` passée dans l'indexeur donne 0 récompense aux étages 22, 37 et 40
   (shops), 2 décisions aux étages 1 et 34, aucun gain net à l'étage 18.

## 2. Effet des corrections (vague 2)
Relancer le backtest et la calibration sur l'index corrigé, et rendre un tableau avant/après :
1. Accord recommandation/choix réel, global puis par mode (solo, co-op) et par tranche d'ascension (0-4, 5-9, 10).
2. Nombre de décisions qui changent de nature (récompense ⇒ achat) et leur poids dans l'ancien calcul.
3. `data/calibration/rooms.json` : aujourd'hui solo seulement. Produire la version co-op (dégâts par joueur) et la
   paire de boss du dernier acte en double boss (demandé par T10 §2.3).

## 3. Diagnostic de l'échelle (vague 2)
Faits sur la run de référence (T9 §3.3) : terme méta moyen 40,3 (de 20 à 71), autres termes 5 au plus en moyenne ;
notes offertes S 0, A 8, B 30, C 69, D 61, F 33. Fenêtre n°2 : 7 articles sur 13 en C au même shop.
1. Mesurer sur l'index (pas sur une run) : répartition des notes offertes, part de chaque terme dans le total,
   fréquence d'un S par run.
2. Dire si le tassement vient de l'échelle (méta ramenée vers le milieu) ou du manque de contexte (termes trop
   faibles). Preuve chiffrée, pas d'avis.
3. Proposer une correction **et sa mesure** : accord avec les choix des joueurs qui gagnent, avant et après.
   Une correction qui n'améliore pas la mesure n'est pas appliquée.
4. Voie C : appliquer dans `data/calibration/weights.json` seulement après accord du chef sur le tableau du §3.3.
5. `PersonalStats` (T9 §3.7) : recommandation du chef, à confirmer par ta mesure : le terme « Ton historique »
   utilise les runs du **même mode** que la run en cours, et retombe sur tout l'historique sous un seuil de volume
   que tu fixes ; la preuve du terme dit lequel des deux a servi.

## 4. Critères
- Test de la fixture du §1.6 vert ; `tools/verify-all.sh` vert ; budget data ≤ 2 Mo dans le dépôt.
- Rapport : tableaux avant/après des §2 et §3, commande de reconstruction de l'index, ce qui n'a pas pu être
  mesuré et pourquoi. Aucun poids modifié sans le tableau qui le justifie.
