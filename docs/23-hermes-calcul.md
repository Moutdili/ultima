# 23 — Hermes : la partie CALCUL (topo + doc + TODO)

> Décision de Mio (2026-10-29) : **Hermes s'occupe de tout le calcul** (scoring, chemins) avec ses
> propres sous-agents. Le chef (Claude) s'occupe de l'architecture : contrats, daemon, IPC, overlay,
> compagnon, intégration. Ce document est ton point d'entrée unique.

---

## 1. Topo : où en est ULTIMA (prouvé sur le vrai jeu le 2026-10-29)

ULTIMA n'est plus un mod (un mod fait passer le jeu sur le profil `modded/` → progression perdue).
C'est une **application externe façon Hearthstone Deck Tracker** :

| Brique | État | Preuve |
|---|---|---|
| Lecture des saves (`Ultima.Saves`) | ✅ | 250/250 runs ; save == écran ; lecture sans verrou |
| Lecture mémoire en direct (`Ultima.Mirror`, ClrMD lecture seule) | ✅ | récompense lue **avant le clic** 3/3 ; shop 15/15 prix ; achat suivi |
| Overlay au-dessus du jeu plein écran (extension GNOME `ultima@mio`) | ✅ | verre, survol, Super+Maj+I/O, clics traversants |
| Méta communautaire (`data/meta/`) | ✅ | 4 sources datées v0.107.1 (Untapped/slaythetierlist en `data/meta/local/`, licence) |
| **Scoring (cartes, reliques, shop, deck)** | ❌ **À TOI** | — |
| **Chemins de la carte** | ❌ **À TOI** | — |
| Daemon + IPC + compagnon | en cours (chef) | — |

Lis dans cet ordre : `docs/20-pivot-externe.md` (§1, §2, §8), `docs/22-contrat-moteur.md`,
puis les skills `sts2-meta-scoring`, `sts2-map-paths`, `sts2-save-format`, `sts2-combat-math`.

---

## 2. Ce que tu dois produire : deux implémentations du contrat

Le contrat est **figé** dans `Ultima.Engine/Contracts/` (propriété du chef — demande de changement
par écrit dans `status/hermes.md`, jamais d'édition directe) :

```csharp
public interface IScorer                         // → Ultima.Engine/Scoring/**
{
    Score ScoreCard(CardInstance card, ScoringContext ctx);           // récompense, deck
    Score ScoreSkip(IReadOnlyList<CardInstance> offered, ScoringContext ctx);
    Score ScoreRelic(string relicId, ScoringContext ctx);
    Score ScoreShopItem(string kind, string id, int price, ScoringContext ctx);  // card|relic|potion
    Score ScoreRemoval(int price, ScoringContext ctx);
    DeckEvaluation EvaluateDeck(ScoringContext ctx);                  // + archétypes + malédictions
}

public interface IPathAnalyzer                   // → Ultima.Engine/Paths/**
{
    PathsView Analyze(ActMap map, MapCoord position, int actIndex, string characterId, int hp);
}
```
Types d'entrée (`CardInstance`, `ActMap`, `MapCoord`…) : `Ultima.Saves/Model/Models.cs`.
Types de sortie (`Score`, `ScoreTerm`, `DeckEvaluation`, `PathsView`…) : `EngineContracts.cs`.
Le daemon (chef) appelle tes classes ; tant qu'elles n'existent pas, il publie des scores `null`.

---

## 3. Règles non négociables

1. **Probabiliste seulement** (Mio) : n'utilise JAMAIS les séquences pré-tirées (`*_encounter_ids`,
   `event_ids`), `rng`, `odds`, sacs de reliques, ni le boss/la variante des actes futurs. Tes
   entrées ne contiennent que ce que le joueur voit — n'en ajoute pas.
2. **Notation S à F** (décision Mio) : `Score.Grade` est dérivé de `Total` par `Grades.FromTotal`
   (S ≥ 85, A ≥ 70, B ≥ 55, C ≥ 40, D ≥ 25, F < 25) — **calibre tes Total sur cette échelle**
   (alignée sur les tiers de `data/meta` : S≈90, A≈75, B≈60, C≈45, D≈30). Une pondération qui
   comprime tout en C/D est un bug. Ajoute un test de distribution sur les cartes du catalogue.
2bis. **Score explicable** : `Total` 0-100 = somme bornée de `ScoreTerm` nommés, chacun avec sa
   `Source` (« Spire Codex v0.107.1 n=42 259 », « tes 247 runs », « synergie poison : 5 cartes »).
   Aucun nombre magique. `Confidence` = high | medium | low selon la taille d'échantillon.
3. **Les sources de méta se contredisent** (ρ 0,19-0,44) : garde le détail par source dans les termes.
4. **Malédictions** (demande explicite de Mio) : elles pèsent dans `EvaluateDeck` et sont listées
   dans `DeckEvaluation.Curses`.
5. **Enchantements** (Sown, Swift « Célérité »… 24 types, `game/pck/src/Core/Models/Enchantments/`) :
   une carte enchantée ne vaut pas la même carte nue ; la relique Wing Charm enchante les récompenses.
6. **Code pur, testable en CI** : aucune dépendance au jeu, au réseau ou à l'heure ; données passées
   en entrée ou chargées depuis `data/`. Lecture seule partout.
7. Coût : < 5 ms par écran (le daemon recalcule à chaque changement d'écran).

---

## 4. Données disponibles pour toi

| Donnée | Où | Note |
|---|---|---|
| Méta par carte/relique | `data/meta/card_tiers.json`, `relic_tiers.json`, `archetypes.json` (+ `data/meta/local/` plus complet, NON commité) | `docs/meta/README.md` |
| Option « passer » | `card_tiers.json` → `skip_reference` | Elo + taux de passage communautaire |
| Historique perso | `Ultima.Saves.HistoryReader` sur `…/profile1/saves/history/*.run` (247 runs v0.107.1) | choix proposés/pris, dégâts par salle, victoire |
| Stats perso par carte | `Ultima.Saves.ProgressReader` → `CardStats(Id, TimesPicked, TimesSkipped, TimesWon, TimesLost)` | `progress.save` |
| Catalogue | `data/catalog/cards.json`, `relics.json`, `monsters.json`, `intents_by_monster.json` | classes C#, coûts, raretés |
| Noms/descriptions FR | `game/pck/localization/fra/*.json` ; bientôt `IKnowledge` (`Ultima.Knowledge`, chef) | |
| Fixtures anonymisées | `fixtures/saves/` (`SCHEMA.md`) | pour tes tests |
| Données réelles de la fenêtre de test | `~/sts2-proofs/tw1-*/` (sorties du miroir : carte, récompense, shop) | local |

---

## 5. TODO (ordre de valeur)

### Scoring (`Ultima.Engine/Scoring/`)
- [ ] **S1** `MetaTable` : charge `data/meta` (+ `local/` si présent) → note de base par carte/relique,
      détail par source, consensus = médiane, désaccord affiché.
- [ ] **S2** `PersonalStats` : depuis l'historique + `progress.save` → écart de taux de victoire quand
      la carte est prise, intervalle de confiance, 0 si n < 10.
- [ ] **S3** `ScoreCard` / `ScoreSkip` (récompense) : base + synergies deck + reliques + boss de l'acte
      + courbe/coût + enchantement + perso. Test : les 3 cartes réelles de la fenêtre de test
      (Defect : Iteration, Hailstorm, Boot Sequence) donnent un classement explicable.
- [ ] **S4** `ScoreShopItem` / `ScoreRemoval` : valeur vs prix vs or disponible ; retrait = enlever
      une malédiction ou une Frappe/Défense de base. Test : shop réel (Necrobinder, 15 prix).
- [ ] **S5** `ScoreRelic` : méta + synergie deck/personnage.
- [ ] **S6** `EvaluateDeck` : note du deck vs reliques et boss de l'acte, archétype probable,
      archétypes potentiels (`archetypes.json`), liste des malédictions.
- [ ] **S6bis** `ScorePotion` (ajout contrat v1.1, implémentation par défaut `null`) : potions possédées,
      quand les garder/utiliser (vs boss de l'acte).
- [ ] **S7** Validation : 10 situations réelles tirées de l'historique, notées par Mio → corrélation.

### Chemins (`Ultima.Engine/Paths/`)
- [ ] **P1** Énumération exacte des chemins de la position au boss (DAG, mémoïsation).
- [ ] **P2** Par choix immédiat : min/médiane/max des types de salles (`RoomCounts`).
- [ ] **P3** `NodeFrequency` : part des chemins traversant chaque case.
- [ ] **P4** Risque : dégâts moyens par TYPE de salle et par acte depuis l'historique perso (n affiché),
      cotes publiques des `?` lues dans le source (pas `odds` de la save). `RiskBasis` en clair.
- [ ] **P5** Tests sur 3 cartes réelles (fixtures) : comptes vérifiés à la main.

---

## 6. Protocole

- Branches `hermes/scoring`, `hermes/paths` (ou main si tu préfères — Mio t'a laissé libre), mais
  **build + tous les tests verts avant chaque push**, jamais `--force`, jamais de modification de
  `Ultima.Engine/Contracts/**`, `Ultima.Saves/**`, `Ultima.Mirror/**`, `overlay/**`, `Ultima.Daemon/**`.
- Tests dans `Ultima.Engine.Tests/Scoring/**` et `Ultima.Engine.Tests/Paths/**`.
- Rendu : `status/hermes.md` — ce qui est fait, preuves (sorties de tests), **ce qui n'est pas prouvé**,
  demandes de changement de contrat.
- Le chef branche tes classes dans le daemon dès qu'elles compilent ; rien à câbler de ton côté.
