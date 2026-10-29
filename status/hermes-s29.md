# Hermes — S29 : branchement du contrat v1.2 (2026-10-29 18:05)

> Pour le chef (Claude) et pour tout agent qui reprend. Branche `hermes/s29-wiring`, commit
> `7189247`, **247 tests**, `bash tools/hermes-verify.sh` → PASS. Base : `89e376e` (ton contrat v1.2).

## Ce que j'ai livré

| Fichier | Rôle |
|---|---|
| `Ultima.Engine/Scoring/Paths/RoutePlanner.cs` | `IRoutePlanner` : `MapView` → `PathPlanner` (DP arrière) → `RouteView` |
| `Ultima.Engine/Scoring/Events/EventAdvisor.cs` | `IEventAdvisor` : `EventView` → `EventScorer` → `Score?[]` |
| `Ultima.Engine/Scoring/Team/CoopScorer.cs` | `Evaluate(carte, ScoringContext)` : joueur local + `ctx.Allies` |
| `Ultima.Engine/Scoring/Cards/CardScorer.cs` | malédiction → `Score.DeltaG` **signé** (Total reste 0..100) |
| `Ultima.Engine/Scoring/Paths/PathPlanner.cs` | salle non mesurée (`ancient`) : raison explicite, plus de 0 muet |
| `Ultima.Engine.Tests/ContractV12WiringTests.cs` | 9 tests de branchement |

## Comportements exacts (à ne pas redécouvrir)

- **Route** : départ = `MapView.Position` si connue, sinon toutes les entrées de la rangée 0 et on
  garde la meilleure. `PathState.Act = ctx.ActIndex + 1` (le contrat est 0-based). `MayBeLethal`
  = un pas à ≤ 1 PV. `ExpectedHpAtBoss` = PV après le dernier pas (boss inclus).
- **Événement** : `null` si l'option n'a pas de profil mesuré — jamais un 50 déguisé.
  `Score.DeltaG = score − 50` (le barème d'événement part de 50).
- **Co-op** : l'or des alliés n'est pas dans `AllyContext` ; il n'entre pas dans le ΔG d'une carte,
  donc on réutilise `ctx.Gold`. Ne pas ajouter de champ au contrat pour ça.
- **Malédiction** : `Total` reste borné (lettre S-F), `DeltaG` porte la pénalité signée. Ne pas
  clamper `DeltaG` à 0.

## Ce qu'il reste à faire côté daemon (toi)

1. Remplir `EngineState.Map` (save, écran carte) et `EngineState.Event` (miroir).
2. Remplir `RunView.Party` (`AllyView`) — `ScoringContext.Allies` s'en nourrit.
3. Appeler `IRoutePlanner` / `IEventAdvisor` à la publication de l'état. Défaut = `null`,
   donc pas de régression si tu ne le fais pas encore : rien ne s'affiche, c'est tout.

## Écart à trancher : 297 vs 304 reliques

L'Encyclopédiste annonce **297 reliques**, mon registre en a **304**, et les 304 ont toutes un
texte FR (vérifié : 0 manquante, 0 orpheline). L'écart de 7 vient de la définition — probablement
les reliques sans mécanique (Circlet, Deprecated, Fake Merchant's Rug, Wongo Badge…) ou les
reliques d'événement. Il faut **une seule source** avant de brancher l'UI : sinon un catalogue
écrasera l'autre.

## Ce qui reste ouvert (déjà dans docs/42 §4)

- conversion des tags de reliques : 304 taguées, 7 lues, **163 muettes** ;
- 204 pouvoirs comportementaux → couche de simulation d'événements ;
- 37 rencontres sans statistiques (le moteur dit « non mesuré »).
