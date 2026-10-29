# S19 — contraintes d'ennemis extraites du source : plafond de dégâts et respawns

Branche `hermes/s19-constraints`, worktree `~/wt-s19` (depuis `main` 0118298).

Le problème : une stratégie « empiler du mana puis frapper N fois » ne marche pas contre un
monstre qui plafonne les dégâts subis. Ces contraintes existaient dans le code du jeu
(docs/32 §4) mais n'étaient exploitables ni par le scoring ni par une quelconque analyse :
il fallait les sortir du C# décompilé et les rendre interrogeables par rencontre.

## 1. Ce qui est extrait automatiquement

`tools/index_constraints.py` lit en **lecture seule** (`ssh mio@100.123.83.40`,
`~/sts2/game/pck/src/Core`) uniquement les fichiers utiles, puis écrit
`data/catalog/constraints.json` (5,4 Ko, commité) :

| Fait | Méthode d'extraction | Exemple vérifié |
|---|---|---|
| sémantique du plafond d'un pouvoir | surcharge de `ModifyDamageCap` : littéral `return 1m` ou `return base.Amount` | `Models/Powers/IntangiblePower.cs:46-53` → 1 ; `HardToKillPower.cs:14-21` → `Amount` |
| pouvoir appliqué par un monstre, avec sa valeur | `PowerCmd.Apply<XPower>(…, Nm, …)` **filtré sur `base.Creature`** (auto-application uniquement ; un Vulnerable posé sur le joueur n'est pas une contrainte) et **restreint aux pouvoirs plafonneurs** | `Models/Monsters/SoulFysh.cs:203` → Intangible 2 |
| move qui porte le pouvoir | table `new MoveState("FADE_MOVE", FadeMove)` du même fichier, suffixe `_MOVE` retiré | `FADE` |
| portée du plafond | `AfterAddedToRoom` → `always`, méthode de move → `periodic` | Exoskeleton = `always`, SoulFysh = `periodic` |
| respawns | `ShouldDisappearFromDoom => Respawns >= N` | `Models/Monsters/TestSubject.cs:164` → 2 |

Trois monstres ressortent (SoulFysh, Exoskeleton, TestSubject) et quatre rencontres
(`SOUL_FYSH_BOSS`, `EXOSKELETONS_NORMAL`, `EXOSKELETONS_WEAK`, `TEST_SUBJECT_BOSS`).
Les mocks (`Models/Monsters/Mocks/*`) sont explicitement écartés et listés dans le JSON.

## 2. Ce qui est saisi à la main (et pourquoi)

1. **La chaîne `NEMESIS → INTANGIBLE`.** TestSubject applique `NemesisPower` sur son
   `RESPAWN_MOVE` (3ᵉ forme, `TestSubject.cs:274`) ; c'est `NemesisPower.AfterSideTurnEnd`
   (`NemesisPower.cs:20-33`) qui applique ensuite `Intangible 1` un tour sur deux. Le
   catalogue codex ne liste que les pouvoirs appliqués par les moves, pas les pouvoirs qui
   en accordent d'autres : cette chaîne est une lecture manuelle, sourcée, dans la table
   `CHAINS`. Le champ `extraction.damage_cap` de TestSubject vaut donc `auto+manual`
   (respawn et pouvoir : auto ; plafond : déduit de la chaîne manuelle).
2. **La table `MANUAL`** (mêmes trois monstres, mêmes valeurs, mêmes références). Elle sert
   de repli quand la source est injoignable (`--offline`, poste sans accès à fedora) et de
   **garde-fou** en ligne : l'extraction est croisée avec elle, toute divergence est
   signalée et `--strict` échoue. Aujourd'hui : 7 faits extraits, 0 divergence.

`constraints.json` porte `extraction` par entrée et par fait (`auto` / `auto+manual` /
`manual`), plus `source` (`fichier:ligne`) — rien n'est publié sans référence.

## 3. Côté moteur

`Ultima.Engine/Scoring/Threat/ThreatModel.cs` :

- `ThreatConstraint` (`DamageCap`, `HardToKill`, `Respawns`, `CapScope`, `CapPower`,
  `Powers`, `Source`) + `Cap` (minimum des deux mécaniques) et `IsPermanent` ;
- `Constraint(encounterId)` / `MonsterConstraint(monsterId)` ;
- `DamageCap(encounterId, ascension)` → `int?`, **null** si absent ; le plafond d'une
  rencontre est le plus contraignant de ses monstres (un plafond ne vaut que pour la
  créature qui le porte, `ModifyDamageCap` teste `target == Owner`) ;
- `Respawns(encounterId)` → `int`, **0** si absent ;
- `IsBurstIneffective(encounterId, ascension)` et sa surcharge
  `IsBurstIneffective(encounterId, ascension, expectedHit)` : vrai si **tous** les monstres
  de la rencontre plafonnent et que le plafond ≤ 25 % du coup attendu
  (`ThreatRules.BurstIneffectiveCapRatio`). Par défaut le coup attendu est `MaxHit` de la
  rencontre (faute de profil de deck dans la signature) ; un appelant qui connaît la
  puissance du joueur passe la surcharge.
- Chargement tolérant : fichier absent, corrompu ou de forme inattendue ⇒ dictionnaire
  vide, aucune exception, le reste du modèle continue de fonctionner.

Résultats sur le catalogue réel : `SOUL_FYSH_BOSS` cap 1, `TEST_SUBJECT_BOSS` cap 1 +
respawn 2, `EXOSKELETONS_*` cap 9 (HardToKill), `AEONGLASS_BOSS` aucun cap.
`IsBurstIneffective` : vrai pour SoulFysh et TestSubject, faux pour Aeonglass et pour
Exoskeleton (9 n'est pas ≤ 25 % de son propre pire coup). `CardScorer` n'est pas modifié —
le signal est disponible, il n'est pas encore branché dans le score (tâche suivante).

## 4. Tests

`Ultima.Engine.Tests/Scoring/ThreatConstraintTests.cs` — 12 tests :

- `TestSubject_revient_deux_fois` (2, via id codex, `ENCOUNTER.` et minuscules ; 0 ailleurs) ;
- `Un_monstre_intangible_plafonne_les_degats_a_un` (SoulFysh, cap 1, `periodic`, source C#) ;
- `Exoskeleton_plafonne_a_neuf_par_hard_to_kill` (9, `always`, permanent) ;
- `Le_plafond_ne_depend_pas_de_l_ascension` (0 → 20) ;
- `IsBurstIneffective_vrai_quand_le_plafond_est_un`, dont la borne exacte du seuil
  (coup attendu 4 → vrai, 3 → faux) et `expectedHit = 0` → faux ;
- `IsBurstIneffective_faux_pour_un_boss_sans_plafond` (Aeonglass, Exoskeleton) ;
- `Un_plafonneur_isole_ne_suffit_pas_dans_une_rencontre_mixte` (catalogue synthétique
  SoulFysh + Aeonglass : cap exposé, burst toujours valide) ;
- `Catalogue_absent_ne_leve_pas` (dossier vide) ;
- `Constraints_absent_ou_corrompu_ne_casse_pas_le_modele` (modèle de menace intact) ;
- `Le_json_est_petit_et_chaque_entree_est_sourcee` (< 100 Ko, `source` en `fichier:ligne`) ;
- `Les_contraintes_du_json_et_du_modele_concordent` (chargeur ↔ fichier, pas d'entrée orpheline).

`dotnet test` : **158/158** (146 avant + 12). `bash tools/hermes-verify.sh` : **PASS**
(build `-warnaserror`, tests, champs interdits, stubs, contrats intacts).
`tools/index_constraints.py` est idempotent (relance → fichier identique à l'octet) et
passe `--strict` (0 divergence avec la table manuelle).

## 5. Non prouvé / limites

- **Aucune validation par replay.** On n'a pas mesuré dans les replays qu'un coup > 1 est
  bien ramené à 1 : la chaîne est prouvée par le code (`ModifyDamageCap`), pas par
  l'observation. Un replay où l'on tape SoulFysh pendant `FADE` trancherait.
- **La portée temporelle est une lecture, pas une mesure.** `periodic` signifie « un move
  de la rotation porte le pouvoir » (Fade = 1 move sur 5, Intangible 2 tours) ; la rotation
  réelle et le nombre de tours d'Intangible ne sont pas simulés, et `late_phase` ne dit pas
  *quand* Nemesis bascule par rapport au tour du joueur. `IsBurstIneffective` ignore
  volontairement cette nuance : il ne répond que oui/non, pas « burst possible 3 tours sur 5 ».
- **Le coup attendu par défaut est un proxy.** `MaxHit` est le pire coup **de la rencontre**,
  pas celui du deck : le booléen à deux arguments est un majorant prudent, la surcharge
  `expectedHit` est la forme correcte pour le scoring.
- **Pas d'exhaustivité.** Seuls les pouvoirs qui surchargent `ModifyDamageCap` sont cherchés.
  Un plafond appliqué par une relique, un modificateur de rencontre ou un effet de salle
  serait invisible. De même, les monstres qui appliquent un pouvoir plafonneur à un **autre**
  monstre (buff d'allié) ne sont pas modélisés : le filtre est `base.Creature`.
- **Ascension.** Vérifié dans le source : ni `IntangiblePower` ni `HardToKillPower`
  n'utilisent `AscensionHelper`, et la valeur d'Exoskeleton est le littéral `9m`. On en
  déduit qu'il n'y a pas de variante ascensionnelle, mais on n'a pas relu tous les
  modificateurs d'ascension du jeu.
- **Représailles non traitées** (riposte, dégâts au tueur) : hors périmètre de cette tâche.
