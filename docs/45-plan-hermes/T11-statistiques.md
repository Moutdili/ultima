# T11 — Statistiques et historique (op `stats.open`)

**À lire d'abord** : `docs/51-contrat-v16.md` §3, `docs/25-idees-hdt.md` §D (D1 à D3), `T9` §2 et §3.7.
**Vague 3**. **Dépend de** : T9 §1-2 (revue corrigée) et T12 §1 (lecture juste des shops et des achats).
**Skills** : `sts2-save-format`, `sts2-daemon`, `sts2-console-tui`, `sts2-companion-app`, `sts2-engine-contract`.

But : l'onglet « Stats » de Hearthstone Deck Tracker, pour tes runs. Calcul hors ligne depuis `history/*.run`,
jamais pendant un combat, jamais dans `EngineState`.

## 1. Voie R — `IStatsBuilder`
Implémentation : `Ultima.Engine/Scoring/Stats/StatsBuilder.cs` (`StatsView Build(runs, scope)`).
1. **Filtre** (`StatsScope`) : `solo` = 1 joueur, `coop` = 2 joueurs ou plus, `all` = tout ; personnage du joueur
   **local** ; bornes d'ascension incluses.
2. **Agrégats** : `ByCharacter`, `ByAscension`, `ByArchetype` (archétype du deck final, celui de `EvaluateDeck`),
   chacun avec runs, victoires, taux, étage moyen. Runs abandonnées : comptées dans `Runs`, jamais dans `Wins`,
   et le rapport dit si tu les exclus du taux.
3. **Rencontres** (`EncounterStats`) : dégâts moyens et médians subis par le joueur local, nombre de combats,
   nombre de runs terminées là. Rappel T9 §2 : `damage_taken` ne couvre pas toute perte de PV ; une mort relevée
   (`hp 1, hp_healed 1`) n'est pas une fin de run.
4. **Historique** (`RunSummary`) : une ligne par run, la plus récente d'abord.
5. `DataBasis` : nombre de runs lues, écartées et pourquoi, version du jeu.
6. Vie privée : aucun pseudo, identifiant Steam ni seed dans la sortie (test).
7. Budget : 500 runs en moins d'une seconde, mesuré en octets alloués comme tes tests de `status/hermes-s36.md` §7.

## 2. Voie D — op `stats.open`
1. Même mécanique que `ReviewService` : une requête à la fois, refus pendant un combat, écriture atomique dans
   `$XDG_RUNTIME_DIR/ultima/stats/`, réponse avec le chemin. Nom de fichier dérivé du filtre, sans caractère libre.
2. `scope` absent ou invalide ⇒ `all` sans filtre ; valeur inconnue de `mode` ⇒ erreur lisible, pas d'exception.
3. `IStatsBuilder` absent ⇒ `{"ok":false,"error":"stats indisponibles"}`.

## 3. Voie S — affichage
1. **Console** : `ultima stats [--mode solo|coop|all] [--character X] [--ascension N[-M]] [--json]` et un onglet
   Stats dans le TUI (taux par personnage, par ascension, pires rencontres, liste des runs).
2. **Compagnon** : page Stats avec les mêmes quatre blocs et le sélecteur de filtre. Fonctionnel seulement, pas
   de design (décision de Mio : design en dernier).
3. Le filtre appliqué est **toujours affiché** en tête. Un agrégat à `Runs = 0` affiche « — », jamais 0 %.
4. Depuis la liste des runs, ouvrir la revue de la run (`review.open`, déjà livré).

## 4. Critères (un test par ligne)
- Sur `fixtures/saves/golden/coop3_a10_win.run` seule : `coop` ⇒ 1 run, 1 victoire ; `solo` ⇒ 0 run et taux `null`.
- Morts relevées aux étages 12, 31 et 46 : aucune comptée comme fin de run.
- Sortie sans identifiant : le fichier ne contient ni l'identifiant du joueur local de la fixture, ni seed.
- `stats.open` pendant un combat ⇒ refus ; deux requêtes en même temps ⇒ une seule calculée à la fois.
- `ultima stats --json` rend exactement la forme de `StatsView` ; `ultima stats` affiche le filtre.
- `tools/verify-all.sh` vert. Rapport habituel.
