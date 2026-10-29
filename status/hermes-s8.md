# S8 — Modèle de menace (S8 « threat »)

Branche `hermes/s8-threat`, worktree `~/wt-s8`. Livré : dégâts entrants par rencontre et par
ascension, PV effectifs, catalogues générés depuis `data/codex/`.

## Fait

| Livrable | Contenu |
|---|---|
| `tools/index_threats.py` | Générateur reproductible (`--codex`, `--catalog`, `--base`). Lit `monsters.json` / `encounters.json` / `encounter_stats.json`, écrit les deux catalogues. **Idempotent** (deux exécutions → `cmp` identique). |
| `data/catalog/monsters.json` | Catalogue monstre **enrichi sans casser la forme historique** : `class`, `file`, `min_hp.values`, `moves[].state/method/values` conservés à l'octet près (0 diff sémantique sur les 123 entrées), plus `id`, `type`, `hp_normal`, `hp_ascension`, `moves[].damage{normal,ascension,hit_count}`, `block`, `heal`, `powers`, `intent_codex`. |
| `data/catalog/encounters.json` | 87 rencontres → ids de monstres, acte (`act` numérique + `act_name`), `room_type`, `is_weak`, `stats` observées (globales + par personnage) quand elles existent. |
| `Ultima.Engine/Scoring/Threat/ThreatModel.cs` | `Default` (lazy, `data/catalog` résolu comme les autres catalogues), `Empty`, `LoadFrom(dir)`, `MaxHit(enc, asc)`, `WorstMove(enc, asc)`, `AverageDamage(enc, personnage)`, `AverageTurns`, `MonstersOf`, `Encounter`, `Monster`, `Encounters`, `EffectiveHp(hp, maxHp, vuln, weak)`. |
| `Ultima.Engine.Tests/Scoring/ThreatTests.cs` | 9 tests (voir ci-dessous). |

`EffectiveHp` : `PV effectifs = PV / (1,5 si Vulnérable × 0,75 si Faiblesse)`, bornés `0..maxHp`.
Les piles sont des **durées**, pas des intensités : 2 Vulnérable = ×1,5 (pas ×3). Exemple testé :
90 PV, 2 Vulnérable + 1 Faiblesse → 90 / 1,125 = **80**.

Aucune rotation de moves n'est inventée : `MaxHit` = somme, sur les monstres de la rencontre, du
move le plus fort de chacun (`dégâts × hit_count`) — un maximum **simultané**, pas une prédiction
tour par tour ; `AverageDamage` = `avg_damage` réellement observé, `null` hors échantillon.

## Tests

`bash tools/hermes-verify.sh` → **PASS** (build `-warnaserror` propre, suite complète 95/95,
aucun champ interdit, aucun stub, contrats intacts).

Les 9 tests de `ThreatTests` :

1. catalogue chargé (≥ 110 monstres, ≥ 80 rencontres ; Aeonglass 512/535 PV) ;
2. Aeonglass **26 normal / 32 ascension** (ids `AEONGLASS_BOSS` et `ENCOUNTER.AEONGLASS_BOSS`) ;
3. `hit_count` inclus (Eye Lasers 11 × 2 = 22, 12 × 2 = 24 en ascension) ;
4. rencontre → ses monstres (Bowlbugs ×4) et somme 33 / 37 ; rencontre inconnue → liste vide,
   `MaxHit` 0, `WorstMove` null ;
5. `AverageDamage` : 6,3 global, 9,1 Ironclad, 4,3 Silent, `null` pour personnage hors
   échantillon / rencontre sans stats / id inconnu ; `AverageTurns` 3,83 ;
6. intégrité : chaque rencontre garde tous ses monstres après indexation ;
7. `EffectiveHp` 2 Vulnérable + 1 Faiblesse = 80, 5 Vulnérable = 60, Faiblesse seule = 80,
   bornes 0..max ;
8. dossier absent → `Empty` **sans exception** (0 / null / liste vide partout) ;
9. compatibilité : `BossCatalog` lit toujours le catalogue enrichi (Waterfall Giant résolu, PV > 0).

## Chiffres

- **115 monstres** codex (82 Normal / 18 Elite / **15 Boss**) ; sortie : **127 entrées** =
  111 enrichies + 4 codex seuls + 12 historiques (mocks/`DeprecatedMonster`) conservés tels quels.
- **Boss avec dégâts par ascension : 11 / 15** (`ascension > normal` sur le pire coup). Les 4
  identiques : `KIN_FOLLOWER` 5, `KIN_PRIEST` 9, `TEST_SUBJECT` 45, `TORCH_HEAD_AMALGAM` 24
  (PV ascensionnels possibles, dégâts non scalés dans le codex).
- **345 moves** codex : 224 avec `damage`, **55 avec `hit_count` explicite**, 169 sans (×1).
- **87 rencontres** (63 Monster / 12 Boss / 12 Elite ; 28 multi-monstres), **50 avec stats**,
  **37 sans** (l'échantillon public s'arrête à 50 des 91 combats annoncés).
- Tailles : `monsters.json` **175,3 Ko** + `encounters.json` **55,3 Ko** = **230,7 Ko** (< 400 Ko).

## Non prouvé / limites

- **2 moves « Attack » sans dégâts** dans le codex (`THE_FORGOTTEN/DREAD`,
  `WATERFALL_GIANT/PRESSURE_GUN`) : comptés **0**, pas extrapolés (dégâts calculés en jeu).
- **169 moves avec `damage` mais `hit_count: null`** : lus comme **1 coup**. C'est une lecture
  (un move mono-coup n'a pas de `hit_count`), pas une donnée du codex.
- **Ascension** : le codex ne distingue que `normal` et `ascension` (A1+). Aucun palier A2..A20
  n'est modélisé ; `MaxHit(enc, n>0)` renvoie la même valeur pour tout `n ≥ 1`.
- **Rotation inconnue** : `MaxHit` suppose que tous les monstres portent leur pire coup le même
  tour. Les dégâts réels d'un tour dépendent de la rotation, absente des données.
- **37 rencontres sans stats** (dont les 12 boss) : `AverageDamage` → `null`, jamais une moyenne
  inventée. Les stats par personnage ne couvrent que 5 ids (`IRONCLAD`, `SILENT`, `NECROBINDER`,
  `DEFECT`, `REGENT`) ; tout autre id → `null`.
- **4 monstres codex absents de la décompilation locale** (`DECIMILLIPEDE_SEGMENT_FRONT/MIDDLE/BACK`,
  `MYSTERIOUS_KNIGHT`) : entrées minimales (pas de `file`/`values` historiques), donc invisibles
  pour `BossProfile` (non-boss, sans impact constaté).
- `data/catalog/monsters.json` garde un élément `_meta` en tête de tableau : ignoré par
  `BossProfile` et `BossCatalog`, mais un futur lecteur qui suppose « tout élément = monstre »
  doit le sauter.
- `data/codex/` reste **non commité** (gitignoré) : le générateur exige donc `--codex` ou un
  `data/codex/` local pour être rejoué ; les catalogues commités, eux, suffisent au moteur.
