# 28 — Brief pour Claude : état, trouvailles, méthode, contraintes

> Écrit par Hermes le 2026-10-29 (soir), **remplace la version de l'après-midi**. Depuis, le corpus
> a été dédupliqué, les copies de monstres mesurées, les mots-clés et effets classés, le
> planificateur de route et le classement des salles `?` implémentés.
> À lire avec `docs/27` (méthode), `docs/29` (review), `docs/30` (replays), `docs/31`
> (calibration), `docs/32` (empreinte), `docs/33` (contraintes), `docs/34` (ascension),
> `docs/35` (malédictions), `docs/36` (registre), `docs/37` (mots-clés/effets), `docs/38`
> (salles `?`), `docs/39` (copies), `docs/40` (route), `docs/41` (statuts).

## 1. État du dépôt (à vérifier avant de coder)

- Branche `main`, **238 tests** verts, `dotnet build -c Release -warnaserror` propre,
  `bash tools/hermes-verify.sh` → **PASS** (5 sections).
- Contrats : **v1.1** (`Score.Grade` S→F, `ScorePotion`, `RoomTypes`). Aucune modification de
  `Ultima.Engine/Contracts/**` par Hermes.
- Dernier commit de référence : `5b1cd6f` (docs/41). Le dépôt est autonome : un clone ne
  télécharge rien (< 2 Mo de data commitée, test `Committed_data_stays_within_the_clone_budget`).

## 2. Découvertes data (le plus important)

### 2.1 Le corpus et sa déduplication

- Export public v0.107.1 : **785 121 runs**, 2,6 Go, sha256 vérifié (gitignoré).
- Index compact : **19 039 107 décisions** de cartes, 39 % de co-op, 20,9 % de victoires.
- **26,8 % de doublons** : ce sont des sessions **co-op soumises une fois par joueur** (taux de
  retrait = `(n−1)/n` : 51,1 % à 2 joueurs, 67,0 % à 3, 75,1 % à 4, 80,4 % à 5). Hors co-op, le
  doublon résiduel est de **6,1 %**. Le sous-corpus de calibration n'était gonflé que de **6,2 %**.
- Replays combat : **8,1 %** des runs (NDJSON `play/hit/intent/shop/decision`).
- Pièges : les moyennes d'or sont empoisonnées par des runs trafiquées (max observé
  **100 000 017** d'or) → borner avant de moyenner.

### 2.2 Ce que les gagnants font (explosivité)

50 000 gagnées vs 50 000 perdues :

| Acte | Tours/combat gagnants | Perdants | Dégâts/combat gagnants | Perdants |
|---|---|---|---|---|
| 1 | 4,8 | 5,0 | **8,3** | 11,6 (−28 %) |
| 2 | 4,7 | 5,1 | **11,8** | 18,0 (−34 %) |
| 3 | 4,7 | 5,1 | **12,5** | 21,8 (−43 %) |

La vitesse sépare peu (4-8 %) ; les **dégâts évités** séparent massivement.

### 2.3 Copies de monstres (nouveau)

**42 rencontres sur 86** ont des copies (19,8 % des combats, jusqu'à **×4**). L'agrégat de menace
est sous-estimé de **×1,118** sans correction. Top : `SCROLLS_OF_BITING_NORMAL` 14→56,
`EXOSKELETONS_NORMAL` 8→32, `PHANTASMAL_GARDENERS_ELITE` 7→28. Seul boss concerné :
`THE_KIN_BOSS` (2 `KIN_FOLLOWER` + 1 `KIN_PRIEST`, ×1,36). Branché dans `ThreatModel.MaxHit`
(tests : ×4 exact, Aeonglass inchangé).

### 2.4 Salles `?` : les choix d'événement (nouveau)

80 000 runs solo → **321 076 choix**, **199 options** avec profil mesuré (PV, or, relique, carte,
retrait, malédiction). `EventScorer` classe par valeur nette, **sans** utiliser le win rate par
option (biais de sélection : `LINGER` −23 PV a un meilleur win rate que s'abstenir, parce que les
joueurs qui le prennent sont meilleurs). Exemples : `SUNKEN_TREASURY/SECOND_CHEST` +334 or → 73 ;
`ROOM_FULL_OF_CHEESE/SEARCH` relique pour −13,9 PV → 53 à pleine vie, 50 blessé.

### 2.5 Route : la valeur des élites (nouveau)

60 000 runs, conditionnés sur l'arrivée en acte 2 : **0 élite = 15,5 %** de victoires,
**3 élites = 40,8 %**, avec des PV de fin d'acte 1 identiques (~39 %). `PathPlanner` fait une DP
arrière sur la carte (nœud, PV), avec pénalité mortelle : à 70/70 PV, **deux élites enchaînées**
sont refusées au profit d'un repos, parce que 2×21 + 34,7 (boss) > 70 PV.

### 2.6 Mots-clés, malédictions, statuts (nouveau)

- **7 mots-clés** (`Exhaust`, `Ethereal`, `Innate`, `Unplayable`, `Retain`, `Sly`, `Eternal`),
  156 cartes, 0 inconnu. `Eternal` = **non retirable du deck** (`CardModel.IsRemovable`) → le
  conseiller de retrait ne recommande jamais `Curse of the Bell` ni `Ascender's Bane`.
- Modulation : `Retain`+`Unplayable` ×1,4, `Ethereal`/`Exhaust` ×0,6, `Eternal` ×1,2.
- **Malédictions classées par effet** (`curse_effects.json`) : `play_limit` 95, `draw_reduction` 85,
  `energy_loss` 80, `end_turn_damage`/`hp_loss` 75… La toxicité retenue est
  `max(mesure, effet)` : **Normality 41 → 95** (« pas plus de 3 cartes par tour »), alors que son
  delta de win rate était **+1,9 pp** (bundle événementiel).
- **Statuts 17/17 classés** : sévère −12, modéré −6, bénin −3, non classable −6 explicite.
- **Reliques 304/304 taguées** (41 tags), 4 `no_effect` justifiés.

### 2.7 Ascension : trois paliers distincts

| Palier | Effet | Constante |
|---|---|---|
| 8 `ToughEnemies` | **PV** → variante `ascension` | `ThreatRules.AscensionHpFrom = 8` |
| 9 `DeadlyEnemies` | **Dégâts** → variante `ascension` | `ThreatRules.AscensionDamageFrom = 9` |
| 10 `DoubleBoss` | **Deux boss** dans l'acte final | `ThreatRules.IsDoubleBoss(asc)` |

## 3. Ce qui est implémenté

| Brique | État |
|---|---|
| Effets des cartes (577) | `cards_effects.json` + `CardEffects` (dégâts, bloc, pioche, énergie, pouvoirs, mots-clés) |
| Menace (128 monstres, 15 boss) | `ThreatModel.MaxHit/WorstMove/AverageDamage/EffectiveHp` + contraintes + **copies** |
| Reliques (304) | catalogue, tags 304/304, `Needs`, valeur nette avec malédiction forcée |
| Potions (64) | catalogue + `PotionScorer` |
| Score global G | `PlayerStrength` : Survie/Offense/Consistance/Couverture, poids **calibrés** (acte 1 : 0,85/0,10/0/0,05) |
| ΔG (fit) | `clamp(2·ΔG, ±20)` cartes, `2,5·ΔG` ±15 reliques, mémoïsé |
| L2 besoin | PV bas + coup du boss > bloc/tour, borné 0..15 |
| Malédictions | `CurseScorer` (mesure + effet + mots-clés), `RemovalAdvisor` (75/100 or, Eternal exclu) |
| Statuts | `StatusCatalog` + `status_effects.json` (17/17) |
| Mots-clés | `KeywordCatalog` (`keyword_semantics.json`) |
| Co-op | `CoopScorer` : ΔG par allié, cession si gain ≥ +3 |
| Événements (`?`) | `EventScorer` : 199 options mesurées, score contextuel (PV) |
| Route | `PathPlanner` : DP arrière, PV cumulés, pénalité mortelle |
| Calibration | `data/calibration/weights.json` (AUC 0,688 acte 1) |
| Registre | `registry.json` (100 Ko) + 5 tests d'invariant |
| Packaging hors ligne | `tools/check-offline.sh` + test de budget |
| Backtest | `tools/backtest/` |
| Empreinte/explosivité | `tools/explosivity/{extract,analyze,attribute}.py` |
| Review (spec) | `docs/29-review-replay.md` |

## 4. Méthode de scoring (arbitrée par Mio — ne pas la redéfinir)

```
note(item, joueur) = L0 intrinsèque (rang Spire Codex, 20..78)
                   + L1 fit deck (±20, par manques, ΔG)
                   + L2 état/besoin (0..15 : PV, coup du boss)
                   + L3 équipe (co-op)
```

- **Deux notes affichées** : tier intrinsèque et « pour toi maintenant », jamais fusionnées.
- **S atteignable** : une carte B (65) + fit 20 sort S ; une carte C basse (45) a besoin de L2.
- **PV effectifs** au début du combat (Vulnérable ×1,5, Faiblesse ×0,75).
- **Co-op** : reliques personnelles, allié mort → revive 1 PV si le groupe passe, coffre 3 reliques
  à 3 joueurs, feuille-pierre-ciseaux si égalité.
- **Explication** : chaque terme porte libellé + points + source. Pas de phrase sans chiffre.

## 5. Contraintes produit

- **Un ami qui clone ne télécharge rien** : data commitée < 2 Mo ; gitignoré : exports, index,
  replays, codex.
- **Read-only** : le moteur n'appelle jamais `CardPileCmd`, `DamageCmd`, `PowerCmd.Apply`.
- **Pas de `catch {}` muet, pas de valeur par défaut silencieuse** : un effet non modélisé est
  `null` + source, ou déclaré absent. Jamais 0.
- **Review** : voie A (décisions, 100 %) + voie B (combat, 8,1 % avec replay).

## 6. Ce qu'on attend de Claude (mis à jour)

1. **Contrat v1.2** — quatre vues manquent au moteur :
   - `MapView` : nœuds + successeurs, pour `PathPlanner` (aujourd'hui la carte n'existe pas dans
     le contrat) ;
   - `PartyView` : PV/deck/reliques de **chaque** allié, pour `CoopScorer` ;
   - `EventView` : id d'événement + options proposées, pour `EventScorer` ;
   - `ReviewTimeline` + `ReviewPoint` (docs/29 §5).
2. **Scores négatifs pour les malédictions** : le contrat borne 0..100, donc une malédiction garde
   un total de **0** et seul le *terme* porte −25. Il faut soit autoriser le négatif, soit un
   drapeau « curse » que l'UI affiche.
3. **Conversion des tags de reliques** : 304/304 taguées, mais `RelicScorer` n'en lit que 7 ;
   **163 reliques restent muettes**. Priorité : `Debuff`, `Damage`, `Draw`, `Energy`, `Scaling`.
4. **Pouvoirs** : 56/260 évaluables. Les 204 autres agissent par `AfterTurnEnd`/`AfterCardPlayed`,
   pas par une expression : il faut une **couche de simulation d'événements**, pas un meilleur
   parseur. Ne pas inventer de valeurs.
5. **Ne pas casser les 238 tests** ; lancer `bash tools/hermes-verify.sh` avant tout commit ;
   ne pas commiter de data lourde.

## 7. Où vit la connaissance

- Docs : 22 → 41 (ce brief, méthode, review, replays, calibration, empreinte, contraintes,
  ascension, malédictions, registre, mots-clés/effets, salles `?`, copies, route, statuts).
- Skills : `sts2-combat-math` (pipeline, ascension, replays, pouvoirs, copies),
  `sts2-meta-scoring` (formule, ΔG, L2, calibration, co-op, explosivité).
- Tout chiffre cité ici est reproductible par un script du dépôt (`tools/`).
