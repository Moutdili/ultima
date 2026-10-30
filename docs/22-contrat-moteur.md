# 22 — Contrat moteur v1 (vague 1)

Source de vérité : `Ultima.Engine/Contracts/*.cs` (chef). Figé à partir des données réelles de la
fenêtre de test n°1. **Design en dernier** (décision Mio) : l'affichage est fonctionnel, texte brut.

## Exigences de Mio (2026-10-29)
- Modèle HDT : **overlay** au-dessus du jeu + **fenêtre compagnon** (infos pratiques, réglages).
- **Personnalisable** : modules affichés, ordre, visibilité (`OverlaySettings`).
- Overlay par défaut : **deck avec score**, **reliques avec score**, **cartes à bonus spécial**
  (enchantements : Sown, Swift « Célérité »… 24 types, `Models/Enchantments/`), **malédictions très visibles**.
- Scoring au moment des **récompenses** et au **shop** (cartes + reliques + potions + retrait).

## Architecture
```
Ultima.Saves (fichiers) ─┐
ultima-mirror (enfant)  ─┼─► Ultima.Daemon ──► $XDG_RUNTIME_DIR/ultima/state.json (+ socket) ──► overlay GNOME
Ultima.Knowledge        ─┤        │                                                       └──► compagnon (vague 2)
Ultima.Engine (scoring, ─┘        └── réglages : ~/.config/ultima/settings.json (hors dossier du jeu)
  chemins, contrats)
```

## Propriété (vague 1)
| Agent | Possède |
|---|---|
| Chef | `Ultima.Engine/Contracts/**`, `docs/22*`, `Ultima.sln`, CI |
| Encyclopédiste | `Ultima.Knowledge/**`, `Ultima.Knowledge.Tests/**`, `data/kb/**`, `tools/kb/**` |
| **Hermes** (calcul, ses sous-agents — docs/23) | `Ultima.Engine/Scoring/**`, `Ultima.Engine/Paths/**`, `Ultima.Engine.Tests/Scoring/**`, `Ultima.Engine.Tests/Paths/**` |
| Câbleur | `Ultima.Daemon/**`, `Ultima.Daemon.Tests/**`, `docs/ipc/**` |
| Verrier GNOME | `overlay/gnome/**` |

## Exemple d'état (IPC)
`docs/22-state.sample.json` : état réaliste (shop Necrobinder de la fenêtre de test), sérialisation
camelCase de `EngineState`. Score `null` = calcul pas encore disponible (Hermes) → l'UI affiche « — ».
Transport v1 : fichier `$XDG_RUNTIME_DIR/ultima/state.json` réécrit atomiquement (tmp + rename) à
chaque changement, plus socket Unix `$XDG_RUNTIME_DIR/ultima/state.sock` (une ligne JSON par état).
Réglages : `~/.config/ultima/settings.json` (écrit par le compagnon, surveillé par le daemon).

## Contrat v1.2 (2026-10-29, ADDITIF — `Ultima.Engine/Contracts/EngineContractsV12.cs`)
Réponse aux demandes de Hermes (docs/28 §6, docs/29 §5.3, docs/42 §4). Version majeure inchangée
(`EngineState.CurrentVersion = 1`), mineure `ContractMinor = 2` : aucun consommateur n'est cassé.
| Demande | Réponse |
|---|---|
| `MapView` (nœuds + successeurs) | `EngineState.Map` (`MapView`, `MapNodeView`, `MapCoordView`) — seulement ce que le joueur voit |
| Route | `EngineState.Route` (`RouteView`) + interface `IRoutePlanner` (défaut : null) |
| `PartyView` | `RunView.Party` (`AllyView`) + `ScoringContext.Allies` (`AllyContext`, local exclu) |
| `EventView` | `EngineState.Event` (`EventView`, `EventOptionView`) + interface `IEventAdvisor` (défaut : null) |
| Review | types de docs/29 §5.3 repris tels quels + `IReviewBuilder` ; livraison **one-shot** (recommandation retenue) : op socket `{"op":"review.open","runId":"…"}` → fichier `$XDG_RUNTIME_DIR/ultima/review/<hash>.json` (daemon) |
| Scores négatifs (malédictions) | `Score.DeltaG` (double?, signé) ; `Total` reste 0..100 pour la lettre S-F |
| Preuve chiffrée d'un terme | `ScoreTerm.Evidence` (string?) |
| Modules | `event`, `route`, `party` ajoutés après l'ordre de Mio (deck, enchantées, malédictions, reliques, potions) |
Côté daemon (chef) : remplir `Map` depuis la save, `Party` depuis la save + miroir, `Event` depuis le miroir
(chemins d'objets d'événement à cartographier), op `review.open`.

## Décision chef — entités « localisation seule » (écart 297/304 reliques, 2026-10-29)
Les deux catalogues ont les **mêmes 304 identifiants** de reliques. 7 n'existent QUE dans la localisation
(aucune classe C# dans le code du jeu v0.107.1, donc jamais instanciables dans une vraie save) :
`CURSED_KETTLE, ENDLESS_APPETITE, GLOWING_ORB, INK_BOTTLE, MYSTERIOUS_COCOON, RED_VINE_TEA, VINE_BRACELET`
(idem 22 cartes et 2 rencontres côté KB).
**Source unique de l'existence d'une entité = une classe dans le code du jeu.** Les entrées
localisation seule restent dans les deux catalogues avec le drapeau `localizationOnly` / `localization_only`,
sont exclues des totaux, des statistiques et de la calibration, et ne reçoivent jamais de score.
Libellés affichés : `Ultima.Knowledge` fait foi. Données de calcul : registre de Hermes fait foi.

## v1.3 — compléments (2026-10-29)
- `RewardView.SkipScore` et `CardView.Cost` deviennent nullables (`null` = non évaluable / coût X ou injouable).
  `DeckEvaluation.Score` reste obligatoire : si l'évaluation échoue, le daemon publie `deck = null`.
- `AllyView.Name` = nom du personnage (la save ne contient pas les pseudos ; préférable pour la vie privée).
- Échantillon d'état v1.3 produit par le daemon sur une vraie save (anonymisé) : `docs/ipc/state.v13.sample.json`.
  `docs/22-state.sample.json` reste l'échantillon v1 des tests de rendu existants.
- Bornes communes des `HudSettings` (décision chef, 2026-10-30) : `HeightFraction` 0,30–0,85, `RestOpacity`
  0,35–1 (`HudSettings.HeightMin/HeightMax/OpacityMin/OpacityMax`). Hors bornes ⇒ ramené à la borne, partout
  (daemon, HUD, compagnon, TUI). Au-delà de 0,85 le panneau couvre trop le jeu.
