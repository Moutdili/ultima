# 26 — Livraison du moteur de calcul (vague 1, Hermes)

> Périmètre : `Ultima.Engine/Scoring/**`, `Ultima.Engine/Paths/**` (+ tests).
> Contrats, Saves, Mirror, Daemon et overlay non touchés. Livré le 2026-10-29.

## 1. Résultat

| Zone | État | Preuve |
|---|---|---|
| `IScorer` (cartes, skip, reliques, shop, retrait, deck) | ✅ implémenté | 73/73 tests |
| `IPathAnalyzer` (DAG, comptes, fréquences, risque) | ✅ implémenté | tests Paths + perf |
| Données méta (503 cartes, 263 reliques, 63 clusters, skip rate) | ✅ chargées | `MetaTable.Load()` |
| Historique perso (runs v0.107.1) | ✅ agrégé | `PersonalStats.Load()` |
| Calibration S→F (décision Mio) | ✅ | test `Calibration_puts_the_best_card_in_A_or_S` |
| `ScorePotion` (contrat v1.1, S6bis) | ✅ | 7 tests `PotionTests` + catalogue 64 potions |

## 2. Matrice de vérification (commandes réellement exécutées)

| # | Commande | Portée | Résultat |
|---|---|---|---|
| 1 | `dotnet build Ultima.Engine.Tests -c Release -warnaserror` | toute la solution de test | PASS, 0 warning |
| 2 | `dotnet test Ultima.Engine.Tests -c Release --no-build` | 73 tests | PASS 73/73 |
| 3 | `grep` champs interdits (`*_encounter_ids`, `event_ids`, `rng`, `odds`, `relic_grab_bag`) | `Scoring/`, `Paths/` | PASS (0 hit hors commentaires) |
| 4 | `grep` `TODO|FIXME|NotImplementedException` | `Scoring/`, `Paths/` | PASS (0) |
| 5 | `git diff origin/main -- Contracts Saves Mirror Daemon overlay` | périmètre | PASS (0) |
| 6 | CI GitHub | non déclenchée sur ces branches | **non observé** |

Tout est reproductible : `bash tools/hermes-verify.sh`.

## 3. Revue de code — findings

### P0 — aucun

### P1 — corrigés avant livraison

| id | Fichier:ligne | Défaut | Impact | Correctif |
|---|---|---|---|---|
| P1-1 | `Scoring/Deck/DeckEvaluator.cs:150` (BossFit) | la note de deck utilisait `Hp/MaxHp` : elle baissait quand le joueur prenait des dégâts | une note de deck qui bouge avec les PV n'évalue plus le deck | marge de garde seule, indépendante des PV courants |
| P1-2 | `Scoring/Personal/PersonalStats.cs:227` | runs abandonnées et journalières agrégées | `WinRateDelta` biaisé (2 abandons sur 5 runs de fixture) | `return` avant agrégation + compteurs `AbandonedRuns` / `NonStandardRuns` |
| P1-3 | `Scoring/Meta/MetaTable.cs:62-72` | `local/` choisi en bloc : un `local/` partiel masquait `archetypes.json` | archétypes vides sur la machine de Mio (bug signalé dans `STATUS.md`) | repli **fichier par fichier** + test de régression `Partial_local_dir_falls_back_file_by_file` |
| P1-4 | `Scoring/Cards/CardScorer.cs:46` | base = `0.55 × consensus` : la meilleure carte (consensus 100) sortait en B | contredit la notation S→F décidée par Mio | `base = 7 + 0.65 × consensus` (plafond 78) : médiane 65 → C, 100 → A, S réservé aux synergies |

### P2 — constatés, non corrigés (assumés)

| id | Fichier:ligne | Limite | Pourquoi non corrigé |
|---|---|---|---|
| P2-1 | `Scoring/Shop/ShopScorer.cs:109-110` | potion = valeur neutre 50 | aucune source publique ne note les potions ; ne classe pas deux potions entre elles (documenté) |
| P2-2 | `Scoring/Shop/ShopScorer.cs` (`price`) | `onSale` absent de la signature : le prix est supposé déjà remisé | changement de contrat → décision chef |
| P2-3 | `Scoring/Cards/CardTags.cs` | tags déduits du nom anglais + `key_cards` des archétypes, pas des effets (le texte des cartes n'est pas dans `data/`) | renommage de patch = tag qui bouge ; à remplacer par `IKnowledge` quand il expose la description |
| P2-4 | `Paths/PathAnalyzer.cs` | les `?` ne sont pas décomposés en cotes publiques (P4 partiel) | demande une source (source du jeu) ; risque actuel = dégâts moyens par type de salle |
| P2-5 | `.gitignore:19` (`status/`) | les rapports d'agents ne sont pas versionnés (seul S2 a forcé le sien) | comportement du dépôt, pas du moteur |
| P2-6 | `Scoring/Deck/DataPaths.cs:15` | second résolveur de chemins de données, doublon de `ScoringData.ResolveMetaDir` | à unifier quand le daemon fixera la convention |

### P3 — hygiène

- Perf mesurée en Debug (deck 1,15 ms / 60 cartes ; chemins < 5 ms) ; pas de mesure Release sur la machine du daemon.
- `characterId` et `hp` sont acceptés par `PathAnalyzer.Analyze` mais non utilisés (signature imposée).

## 4. Axe spec (`docs/23` §5) — couverture

| Tâche | État | Réserve |
|---|---|---|
| S1 `MetaTable` | ✅ | variante publique par défaut, `local/` si présent (repli par fichier) |
| S2 `PersonalStats` | ✅ | `progress.save` non agrégé (seul `history/` l'est) |
| S3 `ScoreCard`/`ScoreSkip` | ✅ | tags heuristiques (P2-3) |
| S4 shop / retrait / reliques | ✅ | pas de fixture de shop réel : tests structurels |
| S5 `EvaluateDeck` | ✅ | archétypes issus de `data/meta`, malédictions via catalogue |
| S6bis `ScorePotion` | ✅ | score dérivé des descriptions FR ; aucune méta potion publique |
| S6 validation S7 (10 situations notées par Mio) | ⏳ | dépend de Mio |
| P1-P3 chemins | ✅ | exact, cap 200 000 puis échantillonnage déterministe |
| P4 risque | ⚠️ | moyenne par type de salle ; cotes des `?` absentes |
| P5 tests sur map réelle | ✅ | 7×16, 60 points, position (4,1) |

## 5. Ce qui n'est PAS prouvé

- Aucun test avec `data/meta/local/` réel (notes Untapped) : seulement le cas partiel simulé.
- Aucune exécution dans le daemon (câblage chef) : le moteur n'a tourné qu'en tests.
- Pas de comparaison avec les 10 situations de Mio (S7) : la corrélation reste à mesurer.
- Pas de shop/récompense réels : les scores de shop sont vérifiés structurellement, pas sur un écran capturé.
- Perf non mesurée en Release sur la machine cible.
- Score des potions : heuristique sur les mots-clés des descriptions FR (pas de méta, pas de valeurs
  numériques lues) — il ordonne les potions entre elles, il ne prédit pas un gain chiffré.
- `data/catalog/potions.json` dépend de la localisation FR : une reformulation de traduction déplace un tag.

## 6. Décisions du chef (retour §7, 2026-10-29)

1. **Prix au shop** : `price` est le **prix affiché**, soldes et réductions déjà appliqués par le daemon.
   Décision prise → plus de changement de contrat attendu ; `ShopScorer` garde cette hypothèse.
2. **Tags des cartes** : restent heuristiques (nom anglais) jusqu'à la livraison de `Ultima.Knowledge`
   (Encyclopédiste) ; bascule sur les descriptions FR à ce moment-là.
3. **Chemins de données** : convention fixée par le daemon (`ULTIMA_DATA`) ; `DataPaths` et `ScoringData`
   seront unifiés ensuite sur un seul résolveur.
4. **S7** : les 10 situations réelles seront notées par Mio pendant la fenêtre de test n°2.

Reste ouvert côté moteur : rien de bloquant. Les points 2 et 3 sont des suites à donner, pas des défauts.

## 7. Repro

```bash
export PATH="$HOME/.dotnet:$PATH"
cd ~/sts2
bash tools/hermes-verify.sh          # build + 73 tests + garde-fous
dotnet test Ultima.Engine.Tests/Ultima.Engine.Tests.csproj --filter "FullyQualifiedName~Scoring" -v q
```
