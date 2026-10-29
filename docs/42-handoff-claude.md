# 42 — Passation à Claude : partie scoring/algo (reprise après coupure)

> Ce document ne décrit **que la partie livrée par Hermes** (scoring + algo) et ce qu'elle attend
> de toi. Tes propres tâches ne sont pas ici. À lire en 5 minutes avant de reprendre.
> État au 2026-10-29 17:34 : `main` = **`9eb36c9`**, **238 tests** verts, `bash tools/hermes-verify.sh`
> → PASS (build `-warnaserror`, tests, champs interdits, stubs, contrats).

## 0. Reprise propre

1. `git fetch origin && git rebase origin/main` sur ta branche.
2. Si un conflit porte sur `data/catalog/*.json` ou `data/calibration/*.json` : **ne pas fusionner
   à la main**. Prendre `main`, puis régénérer (`python3 -I tools/build_registry.py`,
   `python3 -I tools/index_status_effects.py`, etc.).
3. Ne pas modifier `Ultima.Engine/Contracts/**` (v1.1) : les évolutions passent par une demande
   explicite (§4), pas par un patch local.
4. `git config user.name/email` avant de commiter.

## 1. Ce qui a été livré (utilisable tel quel)

| Brique | Point d'entrée | Fichier |
|---|---|---|
| Score global G | `PlayerStrength.Compute(ctx)` → `.Total` + dimensions | `Scoring/Strength/PlayerStrength.cs` |
| Score d'une carte | `CardScorer.ScoreCard(card, ctx)` → `Total` + `Terms` | `Scoring/Cards/CardScorer.cs` |
| Effets des cartes | `CardEffects.Get(id)` (dégâts, bloc, pioche, énergie, pouvoirs, mots-clés) | `Scoring/Cards/CardEffects.cs` |
| Malédictions/statuts | `CurseScorer.Classify(id, meta)` → `Kind, Penalty, Label, Source` | `Scoring/Curses/CurseScorer.cs` |
| Mots-clés | `KeywordCatalog.Flags(keywords)` → `Transient, Unplayable, Retained, OpeningHand, ReturnsOnDiscard, Unremovable` | `Scoring/Keywords/KeywordCatalog.cs` |
| Retrait | `RemovalAdvisor.Recommend(ctx, price, gold)` → `Score, WorthIt, Reason` | `Scoring/Deck/RemovalAdvisor.cs` |
| Reliques | `RelicScorer.NetValue(id, ctx)` (malédiction forcée comprise) | `Scoring/Relics/RelicScorer.cs` |
| Potions | `PotionScorer` | `Scoring/Potions/PotionScorer.cs` |
| Menace | `ThreatModel.MaxHit(encounterId, asc)` (copies ×N), `Instances(enc, monster)` | `Scoring/Threat/ThreatModel.cs` |
| Co-op | `CoopScorer.ShouldTake(card, allies)` (seuil +3) | `Scoring/Team/CoopScorer.cs` |
| Événements (`?`) | `EventScorer.Rank(eventId, options, EventContext)` | `Scoring/Events/EventScorer.cs` |
| Route | `PathPlanner.Plan(nodes, startId, PathState)` | `Scoring/Paths/PathPlanner.cs` |
| Registre | `data/catalog/registry.json` (100 Ko) + `tools/build_registry.py` | — |

## 2. Ce qui a changé depuis ta coupure (à connaître, pas à refaire)

- **Dédup du corpus** : 26,8 % de doublons = co-op soumis une fois par joueur ; 6,1 % en solo.
  Toute stat doit partir de là (5 296 129 combats utiles).
- **Copies de monstres** : 42/86 rencontres, jusqu'à ×4 (`docs/39`). Branchées dans
  `ThreatModel.MaxHit` — un deck burst doit gérer 4 exosquelettes, pas 1.
- **Mots-clés** : 7 (`Exhaust, Ethereal, Innate, Unplayable, Retain, Sly, Eternal`), 0 inconnu
  (`docs/37`). `Eternal` = non retirable du deck.
- **Malédictions par effet** : la toxicité retenue est `max(mesure, effet)`. Normality passe de
  41 à **95** (« pas plus de 3 cartes/tour »), malgré un delta mesuré de +1,9 pp (bundle).
- **Statuts** : 17/17 classés, bandes **−12 / −6 / −3** (`docs/41`).
- **Reliques** : 304/304 taguées, 4 `no_effect` justifiés (`docs/36`).
- **Salles `?`** : 321 076 choix, 199 options profilées ; le win rate par option n'entre **pas**
  dans le score (biais de sélection) (`docs/38`).
- **Route** : DP arrière sur la carte, PV cumulés, pénalité mortelle (`docs/40`).
- **Pouvoirs** : 56/260 évaluables ; les 204 autres sont comportementaux (`AfterTurnEnd`,
  `AfterCardPlayed`), pas formulaïques (`docs/36`).

## 3. Règles dures (elles font échouer la porte si tu les casses)

1. **Pas de valeur par défaut silencieuse** : un effet non modélisé est `null` + source, ou
   déclaré absent. Jamais 0.
2. **Read-only** : jamais `CardPileCmd`, `DamageCmd`, `PowerCmd.Apply`.
3. **Contrat borné 0..100** : ne pas contourner localement pour afficher −25. Les scores négatifs
   sont une demande de contrat (§4), pas un hack.
4. **Data commitée < 2 Mo** : pas d'exports/index/replays.
5. **`bash tools/hermes-verify.sh` avant chaque commit**. `hermes-verify` fait foi sur l'arbre
   courant, pas un run antérieur.
6. **Ne pas redéfinir la formule** `L0 + L1 + L2 + L3` : elle est arbitrée par Mio (`docs/27`).
7. **Chaque terme porte libellé + points + source.** Pas de phrase sans chiffre.

## 4. Ce qui bloque et attend une décision (contrat v1.2)

1. **`MapView`** — nœuds + successeurs. Sans elle, `PathPlanner` ne peut pas être branché sur la
   vraie carte ; il ne tourne aujourd'hui que sur des graphes de test.
2. **`PartyView`** — PV/deck/reliques de chaque allié. `CoopScorer` existe, mais ne reçoit pas les
   alliés du jeu.
3. **`EventView`** — id d'événement + options proposées. `EventScorer` est prêt (199 options),
   il attend l'événement.
4. **`ReviewTimeline` + `ReviewPoint`** (`docs/29` §5).
5. **Scores négatifs** pour les malédictions (ou drapeau `curse`) : aujourd'hui une malédiction
   affiche 0, seul le terme porte −25.
6. **Conversion des tags de reliques** : 304 taguées, 7 lues → **163 reliques muettes**. Priorité :
   `Debuff`, `Damage`, `Draw`, `Energy`, `Scaling`.
7. **Pouvoirs** : 204 comportementaux → couche de simulation d'événements. Ne pas inventer de
   valeurs.

## 5. Lecture minimale (5 fichiers, pas plus)

1. `docs/28-brief-claude.md` — le brief complet, à jour.
2. `docs/36-registre-entites.md` — couverture et trous.
3. `docs/37-mots-cles-et-effets.md` — mots-clés + effet des malédictions.
4. `docs/40-calcul-route.md` — route et PV cumulés.
5. `Ultima.Engine/Scoring/Model/ScoreBuilder.cs` + `Strength/PlayerStrength.cs` — la structure
   des termes.

Interdit : relire tout le code source avant d'avoir lu ces cinq fichiers.

## 6. Commandes

```bash
bash tools/hermes-verify.sh                 # la porte (fait foi)
dotnet test Ultima.Engine.Tests             # 238 tests
python3 -I tools/build_registry.py          # régénère registry.json
python3 -I tools/index_status_effects.py    # régénère status_effects.json
python3 -I tools/index_curse_effects.py     # régénère curse_effects.json
python3 -I tools/index_curse_impact.py      # régénère curse_impact.json
```

## 7. Ce qui n'est pas fait (ne pas croire que c'est cassé)

- 204 pouvoirs non évaluables (comportementaux, pas un bug).
- 37 rencontres sans statistiques (pas de `avg_damage`).
- 163 reliques taguées mais non converties en ΔG.
- `PathPlanner`/`EventScorer`/`CoopScorer` non branchés au jeu (contrat v1.2).
- L'UI (overlay) n'est pas dans ce dépôt.

---
## Réponse du chef (2026-10-29 17:55)
- **Contrat v1.2 livré** (additif) : voir docs/22 §v1.2 — MapView/RouteView + `IRoutePlanner`, `RunView.Party` +
  `ScoringContext.Allies`, `EventView` + `IEventAdvisor`, ReviewTimeline (tes types, tels quels) + `IReviewBuilder`
  (livraison one-shot `review.open` retenue), `Score.DeltaG` signé, `ScoreTerm.Evidence`. 238/238 sur clone propre.
- **À corriger (Hermes)** : sur la machine de Mio, `data/meta/local/` (notes Untapped) fait échouer 4 tests :
  `ContextTests.Explanation_names_the_meta_source`, `FitTests.Same_card_scores_higher_when_the_need_is_not_covered`,
  `NeedTests.A_45_point_card_with_a_big_fit_plus_need_reaches_A`, `NeedTests.Low_meta_block_card_reaches_A_with_max_fit_and_low_hp`.
  Les tests doivent fixer leur méta (fixture) au lieu de lire la variante présente sur la machine — comme pour
  `ULTIMA_DATA`, ils doivent être insensibles à `local/`.
- Tags de reliques (163 muettes) et pouvoirs comportementaux : restent dans ton périmètre (calcul).

---
## Revue du chef sur cas RÉELS (2026-10-29 18:10)
Situations de la fenêtre de test n°1 notées par `new Scorer()` (sonde jetable hors dépôt) :
récompense Defect A9 (Iteration / Hailstorm / Boot Sequence) et shop Necrobinder (116 or, 13 articles).
**Ce qui est bon** : classement de la récompense cohérent (Boot Sequence B 67 > Iteration D 37 >
Hailstorm F 20 = passer), termes chiffrés et sourcés, perf 0,01 ms/appel, méthode de calibration
honnête (survivants d'acte, acte 3 déclaré non identifiable, 660 champs Python = C#).

**À corriger, par priorité :**
1. **[P0] Échec silencieux sans données** : lancé hors du dépôt sans `ULTIMA_DATA`, `MetaTable.Load()`
   ne trouve rien et TOUT sort en **F 0** (aucun terme méta, aucune erreur). Viole ta règle n°1.
   Attendu : `MetaTable`/`Scorer` exposent un état (`IsLoaded`, chemin, nb d'entrées) ou lèvent ; le
   daemon le traduira en `SourceHealth.Degraded`. Test : répertoire vide → erreur explicite, pas des F.
2. **[P1] Shop incohérent avec la récompense** : au shop une carte n'a que « Valeur + Économie » (pas de
   Fit/ΔG ni de besoin immédiat) et une autre échelle (Putrefy consensus 75 → +37 au shop, alors qu'à la
   récompense 60 → +40). Les lettres du shop sont tassées (cartes C/D/F) et incomparables à celles des
   récompenses. Attendu : `ScoreShopItem(card)` = `ScoreCard` complet + terme économie séparé.
3. **[P1] « Trop cher » ≠ « mauvais »** : Bread (consensus 78,5) sort F 21 parce que non abordable.
   La lettre doit noter la valeur ; l'abordabilité est déjà un booléen du contrat (`ShopItem.Affordable`).
   Garde le terme, mais hors de la note (ou terme informatif à 0 point).
4. **[P1] Potions à valeur neutre au-dessus des cartes** : les 3 potions sortent C 44 grâce à une
   « valeur neutre documentée » (+35), au-dessus de presque toutes les cartes mesurées. Un placeholder
   ne doit pas battre une mesure : score `null` ou note plafonnée/marquée « non comparable ».
5. **[P2] Note du deck peu informative** : un deck de départ à l'étage 2 sort F 17 (moyenne des notes
   de Frappe/Défense). Note relative attendue : percentile de G parmi les runs au même acte/étage
   (ton corpus le permet).
6. **[P2] `Score.DeltaG` (v1.2) jamais rempli** alors que le terme Fit calcule déjà « ΔG = +4 pts ».
7. **[P2] Retrait** : `ScoreRemoval` (+25 fixe − coût) ne passe pas par `RemovalAdvisor` (qui connaît
   malédictions et Frappes) ; deux vérités pour la même décision.
8. **[info] Pouvoir prédictif modeste** (AUC 0,66-0,69 d'après docs/31) : garde les termes ΔG bornés
   (±20) comme aujourd'hui ; ne pas les laisser dominer la méta.
9. **[info] Corpus Spire Codex** : conditions d'utilisation introuvables (docs/meta) — garder le brut hors
   dépôt (c'est le cas), ne publier que des agrégats, et ménager l'API (cache, débit limité).
