# 46 — Contrat v1.5 et réponses aux 18 demandes de Hermes (chef, 2026-10-30)

Source : `docs/45-plan-hermes/demandes-de-contrat.md` (après T1→T4) et la fenêtre de test n°2 (vrai jeu).
Tout est ADDITIF (`ContractInfo.CurrentMinor = 5`). Code : `Ultima.Engine/Contracts/EngineContractsV15.cs` +
annotations dans `EngineContracts.cs` / `EngineContractsV12.cs`. Schémas : `schemas/settings.schema.json` (nouveau),
`theme.schema.json` (ton `shadow`), `module.schema.json` (`grade`).

## 1. Réponses (une décision par demande, à appliquer partout)
| # | Décision |
|---|---|
| A1 | `EventView.Name` et `EventOptionView.Label` deviennent `string?`. Texte absent ⇒ `null` ; les surfaces affichent « — ». |
| A2 | L'échantillon de référence devient `docs/ipc/state.v14.sample.json` (produit par le daemon réel, puis v1.5 quand les champs sont remplis). `ContractShapeTests` vérifie la forme EXACTE sur lui ; `docs/22-state.sample.json` reste l'échantillon historique des tests de rendu. |
| A3 | `run.character` n'existe pas : la source officielle d'`auto-perso` est **`run.characterId`** (ex. `CHARACTER.NECROBINDER` ⇒ thème `necrobinder`). Une seule fonction par surface, mêmes tests. |
| B1 | `grade` d'un module = gabarit dont le RÉSULTAT doit être S..F, sinon le daemon publie `grade: null`. |
| B2 | `schemas/settings.schema.json` couvre la racine (`modules`, `hud`, `appearance`, `pluginsEnabled`, clés inconnues permises). |
| B3 | `terminal` reste un thème **virtuel** du TUI (16 couleurs ANSI), pas un fichier ; documenté, aucun changement de schéma. |
| B4 | Écrans : `EngineState.screen` est sérialisé en PascalCase (`Map`, `RestSite`) ; les modules/réglages utilisent l'id camelCase (`map`, `restSite`) = première lettre en minuscule. Table unique : `ScreenIds.All`. |
| B5 | `CardView.CostKind` (`number` / `x` / `unplayable`, `CostKinds`). `Cost` reste `null` pour `x` et `unplayable`. |
| B6 | Ton `shadow` (réservé aux thèmes) ; absent ⇒ noir à 45 %. |
| C1 | **`appearance.hud.position.side` fait autorité.** `HudSettings.Side` est déprécié : les écrivains le tiennent synchronisé, les lecteurs ne s'en servent qu'en repli. |
| C2 | Priorité des écrans : `ModuleSetting.Screens` (choix de l'utilisateur) > `CustomModuleView.Screens` (défaut du module) > écrans par défaut. Le daemon **ne filtre pas** : chaque surface applique la règle, aux modules natifs comme perso. |
| D1 | Réglé : « `enabled` absent ⇒ `true` », appliqué par le daemon (`V14Tests.cs:225`), le compagnon et le HUD. |
| D2 | Un module/plugin `disabled` reste publié avec ses dernières lignes et `Problem` ; les surfaces l'affichent atténué « désactivé ». |
| D3 | Oui : `background` et `border` sont surchargeables par surface. |
| D4 | `glass.tintOpacity` = opacité du verre **ouvert** ; `HudSettings.RestOpacity` règle le rail au repos ; `eco` garde +0,13. |
| D5 | Bornes : on **ramène** à la borne (jamais de rejet) et `ultima doctor` signale la valeur corrigée. |
| D6 | Oui : le HUD passe entièrement aux tons du contrat et le shim `palette.py` du compagnon disparaît (tâche T8). |

## 2. Ajouts v1.5
- **Notes sous les choix (T7)** : `ShopItem.Slot` (`ShopSlots` : `card:0..4`, `colorless:0..1`, `relic:0..2`,
  `potion:0..2`), `ShopView.InventoryOpen`, `Score.Community` (`CommunityView` : consensus, sources, écart, taux de
  prise/victoire si connus, source). Article vendu ⇒ absent de `Items` ; carte choisie ⇒ `RewardView.AlreadyTaken`.
  `RewardView.Offered` est dans l'ordre de l'écran (garanti, prouvé en fenêtre n°2).
- **Combat (T6)** : `EngineState.Combat` (`CombatView` : main, pioche/défausse/épuisement **comptées et triées par
  id**, `DrawUntilReshuffle`, cartes générées, ennemis et intentions **visibles**, dégâts entrants, létal).
  **Rempli seulement après la mesure FPS** des lectures en combat (fenêtre de test suivante) ; d'ici là `null`.
- **Feu de camp (T6)** : `EngineState.RestSite` (`RestSiteView` : options, note, carte conseillée pour l'amélioration).
- Statistiques/historique (D1-D3 de docs/25) : **pas dans `EngineState`** (trop gros, pas temps réel) — prévu en
  livraison one-shot comme `review.open` (op `stats.open`), contrat dans une version suivante.

## 3. Fenêtre de test n°2 — constats qui touchent le contrat
- Le jeu rend en 1920×1080 avec bandes noires de 60 px sur un écran 1920×1200 (positions des notes : T7).
- Le jeu n'écrit rien dans `godot.log` lors d'un achat ni du choix d'une carte ; « selected card reward » = ouverture
  du choix (corrigé dans le daemon, `0328aee`). D'où la relecture ~1 Hz en Shop/Récompense (T7).
