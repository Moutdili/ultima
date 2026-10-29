# 28 — Brief pour Claude (état, trouvailles data, méthode)

> Écrit par Hermes le 2026-10-29 avant la reprise de Claude (limite reset 17:40).
> À lire avec `docs/23` (cahier des charges), `docs/24` (archi du calcul) et `docs/27` (méthode de
> scoring contextuel). Ce document ne remplace rien : il ajoute ce qui a été appris depuis.

## 1. État du dépôt (vérifié)

- `main` à jour, contrat **v1.1** (`Score.Grade` S→F, `ScorePotion`, `CardView.Description`,
  `PotionView.Score`, `RoomTypes`).
- `Ultima.Engine` : scoring cartes/reliques/shop/deck/potions + chemins. **86 tests verts**,
  `dotnet build -c Release -warnaserror` propre, `bash tools/hermes-verify.sh` → PASS (5 sections).
- Livré par Hermes depuis ton départ :
  - **S6bis** `ScorePotion` (64 potions, catalogue généré depuis la localisation FR) ;
  - **calibrage S→F sur le rang** et non sur le consensus brut (les notes locales Untapped ont une
    autre échelle : sans ça la meilleure carte tombait en B) ;
  - **besoin PV** sur les reliques (une relique de PV max passe de B à A à 18/70 PV) ;
  - libellés de source lisibles : « Spire Codex — consensus 78/100 (1 source, écart 0) » ;
  - `docs/27` : méthode complète (G/ΔG, fit par manques, co-op, validation).
- ⚠️ Les agents de Hermes écrivent dans `status/hermes-sN.md` (gitignoré) et ne touchent **jamais**
  `Ultima.Engine/Contracts/**`, `Ultima.Saves/**`, `Ultima.Mirror/**`, `overlay/**`.

## 2. Trouvailles data (le gros morceau)

### 2.1 Export de runs public — 2 466 928 runs

`GET https://spire-codex.com/api/exports/runs/manifest` liste, par version du jeu, des exports
JSONL gzippés avec `sha256` :

| Version | runs | taille |
|---|---|---|
| **v0.107.1** (notre patch) | **785 121** | 2,63 Go |
| v0.111.0 | 328 690 | 1,29 Go |
| v0.110.1 | 66 795 | 0,25 Go |
| total | 2 466 928 | 8,54 Go |

Téléchargé et vérifié : `data/exports/runs-v0.107.1.jsonl.gz` (sha256 `315d1bb0…`, identique au
manifeste). **Une run par ligne, JSON complet du jeu** : `map_point_history`, `player_stats` **par
joueur**, `card_choices` (`was_picked`), `relic_choices`, `ancient_choice`, PV/or/dégâts par étage.

### 2.2 Index compact — 785 121 runs, 19 039 107 décisions

`tools/index_runs.py` → `data/index/runs-v0.107.1.jsonl.gz` (517 Mo, 401 s) :

- 785 121 runs indexées (lignes vérifiées), **309 276 co-op (39,4 %)**, win rate 20,9 % ;
- **19 039 107 décisions de cartes** (28,3 % de skip) + ~6,6 M décisions de reliques ;
- chaque décision porte : acte, étage, type de salle, `player_id`, PV/max PV, or, cartes proposées,
  carte prise, reliques proposées/prises ;
- une ligne par run : `h` hash, `b` build, `m` mode, `asc`, `n` nb joueurs, `w` win, `ab` abandon,
  `kb` tué par, `pl` joueurs (deck/reliques/potions), `d` décisions.

### 2.3 Données statiques — `data/codex/` (1,2 Mo, régénérables)

| Fichier | Contenu |
|---|---|
| `cards.json` | 577 cartes, **498 avec valeurs** : `damage`, `block`, `hit_count`, `cards_draw`, `energy_gain`, `powers_applied`, `vars`, `upgrade` |
| `monsters.json` | 115 monstres, **15 boss avec chaque move** : dégâts `normal` ET `ascension`, `hit_count`, `block`, `heal`, `powers` |
| `encounters.json` | 87 rencontres → monstres, acte, type de salle |
| `encounter_stats.json` | 50 combats : `avg_damage`, `avg_turns`, `fatal`, par personnage |
| `archetypes.json` | part + win rate des archétypes par version |
| `community_stats.json` | 2 237 288 runs, win 27,8 %, par ascension |
| `relics.json`, `potions.json`, `constants.json`, `charts_meta.json` | compléments |

Régénération : `python3 -I tools/fetch_codex_static.py`.

### 2.4 Replays combat — correction (vérifiée)

⚠️ Première mesure fausse (script qui s'arrêtait au premier match) : **22 runs sur 273 (8,1 %) ont
`has_replay`**, et `GET /api/runs/{hash}/replay` renvoie alors du **NDJSON** (~470-850 Ko) :
`header` (replay_version 7, seed, build, personnage, ascension) puis des événements
`play`, `hit`, `intent`, `shop` (avec prix), `decision` (avec toutes les options).

- Les runs **sans** replay → 404.
- L'export public ne contient **pas** les replays (0 occurrence sur 785 121 lignes) : il faut les
  fetcher par hash, run par run. Extrapolation : 785 121 × 8,1 % ≈ 63 000 replays ≈ 30 Go.
- Conséquence : la review a deux voies — **A** décisions/trajectoire (100 % des runs, livrable),
  **B** combat tour par tour (8 % des runs, optionnelle). Et ces replays permettent de **valider les
  dégâts calculés** (effets de cartes, menace) sur de vrais combats.
- Les moves de boss sont connus **un par un**, pas leur rotation : le modèle de menace doit exposer
  « pire coup », « coup moyen », « nb de coups », pas une prédiction tour par tour inventée.

## 3. Règles de packaging (exigence Mio)

Un ami qui clone Ultima **ne doit rien télécharger de lourd**. Donc :

- **commités** (petits, générés, < 2 Mo au total) : `data/meta/*`, `data/catalog/*`
  (cartes, effets, monstres, rencontres, potions, reliques) et `data/calibration/*` (poids appris) ;
- **jamais commités** (gitignorés) : `data/exports/`, `data/runs/`, `data/index/`, `data/codex/` ;
- le moteur ne lit **que** les fichiers commités à l'exécution ; l'export et l'index ne servent qu'à
  produire/valider ces artefacts (backtest hors ligne).

## 4. Méthode de scoring validée par Mio (docs/27)

```
note(item, joueur) = L0 intrinsèque (rang Spire Codex, 20..78)
                   + L1 fit deck (±20, par manques, anti-synergie)
                   + L2 état/besoin (0..15 : PV, or, boss, malédictions)
                   + L3 équipe (co-op)
```

- **ΔG** : on définit un score global du joueur `G = w_s·Survie + w_o·Offense + w_c·Consistance +
  w_v·Couverture`, et la valeur d'un item est `ΔG = G(état+item) − G(état)`. Naturellement idempotent
  (les dimensions saturent), cohérent entre cartes/reliques/potions/chemins.
- **S atteignable** par le fit : une relique C-tier qui s'imbrique parfaitement peut sortir S.
  L'intrinsèque n'est qu'un prior, il reste affiché à part.
- **PV au début du combat** : la survie utilise les PV **effectifs** (mitigation des statuts du boss
  au tour 1), pas les PV bruts.
- **Co-op** (décisions de Mio) : reliques **personnelles** ; allié mort **réanimé à 1 PV** si le
  groupe passe le combat ; coffre = 3 reliques pour 3 joueurs, et si plusieurs la veulent →
  **feuille-pierre-ciseaux**. Donc L3 = classement d'envie par joueur + conseil de tie-break
  (écart de ΔG < 3 → vrai match nul).
- **Explication** : chaque terme porte libellé + points + source (« Spire Codex — consensus … »).
  Pas de phrase sans chiffre derrière.

## 5. Chantier en cours (swarm Hermes)

**Livré et mergé (107 tests, porte PASS)** :

| Brique | Sortie réelle |
|---|---|
| Effets des cartes | `data/catalog/cards_effects.json` (577 cartes, 298 Ko) + `Ultima.Engine/Scoring/Cards/CardEffects.cs` — 212 cartes avec dégâts, 72 avec bloc, 18 multi-coups, 10 X-cost. Piège traité : `upgrade.damage/block/cards/energy` sont des **deltas texte** (`"+3"`), `upgrade.cost` est un **coût absolu** |
| Modèle de menace | `data/catalog/monsters.json` (128 entrées, 15 boss) + `encounters.json` (87) + `Ultima.Engine/Scoring/Threat/ThreatModel.cs` — `MaxHit`, pire move, `AverageDamage` (stats observées), `EffectiveHp` (Vulnérable ×1,5, Faiblesse ×0,75). Aeonglass 26/32, EYE_LASERS 11×2 |
| Harnais de backtest | `tools/backtest/` (loader/policies/metrics/run/selftest) — 100k runs en 23-26 s. Mesures : accord 28,8 %, +1,4 pp de win rate par décision, **−18,5 pp par run = effet de profondeur, pas causalité** (explicité dans la sortie) |
| Spec review | `docs/29-review-replay.md` (1 295 lignes) — timeline, regret (`ΔG(best) − ΔG(choix)`, seuils 5/12/25), contrôles de vitesse, voie combat optionnelle, demande de contrat v1.2 |

Puis : `G` et `ΔG` (dépendent des deux premiers), `TeamAdvisor` (L3), packaging des artefacts.

## 6. Ce qu'on attend de Claude

- Ne pas redéfinir la méthode : elle est arbitrée par Mio (docs/27 §7).
- Proposer un **contrat v1.2** pour : `PartyView` (tous les joueurs en co-op), `ScoreTerm.Evidence`
  (citer les cartes concernées), et éventuellement un `ReviewTimeline` pour la feature review.
- Ne pas commiter de data lourde ; ne pas casser les 86 tests ; lancer `tools/hermes-verify.sh`.
