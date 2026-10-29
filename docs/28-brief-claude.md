# 28 — Brief pour Claude : état, trouvailles, méthode, contraintes

> Écrit par Hermes le 2026-10-29 (soir). Remplace la version précédente : depuis, le corpus a été
> téléchargé, l'explosivité mesurée, les pouvoirs inventoriés et le scoring branché sur G.
> À lire avec `docs/27` (méthode), `docs/29` (review), `docs/30` (replays), `docs/31`
> (calibration), `docs/32` (empreinte/explosivité), `docs/33` (registre des contraintes).

## 1. État du dépôt (à vérifier avant de coder)

- Branche `main`, **146 tests** verts, `dotnet build -c Release -warnaserror` propre,
  `bash tools/hermes-verify.sh` → PASS (5 sections).
- Contrats : **v1.1** (`Score.Grade` S→F, `ScorePotion`, `RoomTypes`). Aucune modification de
  `Ultima.Engine/Contracts/**` par Hermes.
- Livré : S1→S20 (voir §3). Agents en cours au moment d'écrire : dédup du corpus, contraintes
  d'ennemis, branchement des poids, catalogue de pouvoirs, co-op, copies de monstres.

## 2. Découvertes data (le plus important)

### 2.1 `rooms[]` : la donnée qui change tout

Chaque étage de `map_point_history` porte :

```json
{"model_id": "ENCOUNTER.EXOSKELETONS_WEAK",
 "monster_ids": ["MONSTER.EXOSKELETON","MONSTER.EXOSKELETON","MONSTER.EXOSKELETON"],
 "room_type": "monster",
 "turns_taken": 5}
```

- **`turns_taken`** = durée réelle du combat, pour **toutes** les runs (pas seulement les 8 % de
  replays) ;
- **`model_id`** = rencontre exacte (jointure avec `encounter_stats`) ;
- **`monster_ids` avec doublons** = effectifs réels.

Deux fois, l'audit initial a conclu « pas de tour par tour » : c'était faux. Toujours inspecter les
tableaux imbriqués avant de déclarer une donnée absente.

### 2.2 Corpus public

- `GET /api/exports/runs/manifest` : 2 466 928 runs / 8,54 Go, dont **785 121 sur v0.107.1**.
  Téléchargé, sha256 vérifié (`data/exports/`, gitignoré).
- `tools/index_runs.py` → `data/index/runs-v0.107.1.jsonl.gz` (517 Mo) : **19 039 107 décisions de
  cartes**, 39 % de co-op, 20,9 % de victoires.
- **38 % de doublons exacts** (même deck, reliques, PV, tours par acte ; hash différent) : la
  déduplication précède toute statistique.
- Replays combat : ~8 % des runs, NDJSON avec `play/hit/intent/shop/decision` (docs/30).

### 2.3 Explosivité : ce que les gagnants font vraiment

50 000 gagnées vs 50 000 perdues, 1,06 M + 0,56 M combats :

| Acte | Tours/combat gagnants | Perdants | Dégâts/combat gagnants | Perdants |
|---|---|---|---|---|
| 1 | 4,8 | 5,0 | **8,3** | 11,6 (−28 %) |
| 2 | 4,7 | 5,1 | **11,8** | 18,0 (−34 %) |
| 3 | 4,7 | 5,1 | **12,5** | 21,8 (−43 %) |

La vitesse sépare peu (4-8 %) ; les **dégâts évités** séparent massivement. La puissance finale du
deck ne corrèle ni à la vitesse (0,03) ni aux dégâts (−0,08).

### 2.4 Attribution d'un saut à un choix

Chaque combat est noté
`0,5·(tours_moyens−tours)/tours_moyens + 0,5·(dégâts_moyens−dégâts)/dégâts_moyens`, puis on compare
les 3 combats après une carte aux 3 avant (même acte), en retirant la dérive de l'acte.

- Tête : FLASH_OF_STEEL +0,133 (n=966), DISMANTLE +0,120, POMMEL_STRIKE +0,109 (n=4 213),
  BATTLE_TRANCE +0,105.
- Queue : VAULT −0,327, WISH −0,229, DEFEND_REGENT −0,120, BASH −0,118.
- Déclencheurs des plus gros sauts : SHRUG_IT_OFF (472), POMMEL_STRIKE (467), MAD_SCIENCE (443).

Lecture : ce sont des cartes de **pioche / énergie / attaques efficaces** — le débit de cartes, pas
les dégâts bruts. Le bloc pur ressort négatif parce qu'il ralentit.

### 2.5 Contraintes d'ennemis (vérifiées dans le source)

| Mécanique | Preuve | Effet |
|---|---|---|
| `IntangiblePower.ModifyDamageCap` | `Models/Powers/IntangiblePower.cs` | dégâts subis plafonnés à **1** |
| `HardToKillPower.ModifyDamageCap` | idem | plafond = `Amount` |
| `TestSubject` | `Models/Monsters/TestSubject.cs` | **respawn 2 fois** |
| `SoulFysh`, `Exoskeleton` | `Models/Monsters/` | appliquent Intangible/HardToKill |

Surface mesurée : **260 pouvoirs**, 25 `ModifyDamageMultiplicative`, 17 `ModifyHandDraw`,
16 `ModifyMaxEnergy`, 11 `ModifyDamageAdditive`, 8 `ModifyBlockMultiplicative`,
7 `ModifyCardPlayCount`, 2 `ModifyDamageCap`, 10 paliers d'ascension.
Registre complet et omissions classées : `docs/33`.

### 2.6 Seuils d'ascension : deux barèmes différents

La table est dans `Entities/Ascension/AscensionLevel.cs` (ordre de l'enum) :

| Palier | Effet | Constante |
|---|---|---|
| 8 `ToughEnemies` | **PV** des ennemis → variante `ascension` | `ThreatRules.AscensionHpFrom = 8` |
| 9 `DeadlyEnemies` | **Dégâts** des ennemis → variante `ascension` | `ThreatRules.AscensionDamageFrom = 9` |
| 10 `DoubleBoss` | **Deux boss** dans l'acte final | `ThreatRules.IsDoubleBoss(asc)` |

Mesures concordantes : à A8 les PV observés valent déjà la variante `ascension` (Bygone Effigy 132,
Ceremonial Beast 262, Crusher 219) ; les dégâts restent `normal` à A8 et basculent à A9 (389 moves de
replays : 76 % de concordance, contre 53 % avec « ascension dès A1 »). **Trois constantes distinctes,
jamais une seule.**

## 3. Ce qui est implémenté

| Brique | État |
|---|---|
| Effets des cartes (577, 298 Ko) | `data/catalog/cards_effects.json` + `CardEffects` |
| Menace (128 monstres, 15 boss) | `ThreatModel.MaxHit/WorstMove/AverageDamage/EffectiveHp` |
| Potions (64) / reliques (304) | catalogues + scoring contextuel |
| Score global G | `PlayerStrength` : Survie/Offense/Consistance/Couverture, saturantes |
| ΔG (fit) | `clamp(2·ΔG, ±20)` cartes, `2,5·ΔG` ±15 reliques, mémoïsé |
| L2 besoin | PV bas (bloc/soin, 0..10) + coup du boss > bloc/tour (+5), borné 0..15 |
| Calibration | `data/calibration/weights.json` (AUC 0,688 acte 1 ; Survie 85-92 %) |
| Packaging hors ligne | `tools/check-offline.sh` : clone → build + tests, réseau coupé |
| Backtest | `tools/backtest/` : 100k runs en 23 s, accord 28,8 % |
| Empreinte/explosivité | `tools/explosivity/{extract,analyze,attribute}.py` |
| Review (spec) | `docs/29-review-replay.md` (timeline, regret, vitesses, voie combat) |

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
- **Co-op** : reliques personnelles, allié mort → **revive 1 PV** si le groupe passe, coffre 3 reliques
  à 3 joueurs, **feuille-pierre-ciseaux** si plusieurs la veulent (écart de ΔG < 3 = vrai match nul).
- **13 cartes de soutien** (8 `AnyAlly`, 5 `AllAllies`) : ΔG calculé sur le deck de **chaque allié**.
- **Explication** : chaque terme porte libellé + points + source. Pas de phrase sans chiffre.

## 5. Contraintes produit

- **Un ami qui clone ne télécharge rien** : commité < 2 Mo (`data/meta`, `data/catalog`,
  `data/calibration`) ; gitignoré : exports, index, replays, codex. Prouvé par `check-offline.sh`.
- **Read-only** : le moteur n'appelle jamais `CardPileCmd`, `DamageCmd`, `PowerCmd.Apply`.
- **Pas de `catch {}` muet, pas de valeur par défaut silencieuse** : un effet non modélisé est
  `null` + source, ou déclaré absent. Jamais 0.
- **Review** : voie A (décisions/trajectoire, 100 % des runs) + voie B (combat, 8 % avec replay).

## 6. Ce qu'on attend de Claude

1. **Contrat v1.2** : `PartyView` (tous les joueurs), `ScoreTerm.Evidence` (cartes concernées),
   `ReviewTimeline` + `ReviewPoint` (docs/29 §5).
2. Brancher les **pouvoirs** dans le pipeline de dégâts (catalogue S21 + évaluateur S22) : c'est la
   brique qui empêche de sous-évaluer les decks de scaling.
3. **Copies de monstres** dans `ThreatModel` (catalogue S24) et **phases/invulnérabilité**.
4. Trancher le **seuil d'ascension des PV** (≤ A8, à mesurer sur A1-A7).
5. Ne pas commiter de data lourde ; ne pas casser les 146 tests ; lancer `tools/hermes-verify.sh`.

## 7. Où vit la connaissance

- Skills : `sts2-combat-math` (pipeline, ascension, replays, pouvoirs, copies),
  `sts2-meta-scoring` (formule, ΔG, L2, calibration, co-op, explosivité) +
  `references/contraintes-et-pouvoirs.md`, `references/public-run-corpus.md`, `references/formule-v1.md`.
- Docs : 22-33. Tout chiffre cité ici est reproductible par un script du dépôt.
