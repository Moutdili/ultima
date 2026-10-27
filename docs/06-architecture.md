# 06 — Architecture cible

## Vue d'ensemble

```
┌──────────────────────────── ULTIMA.dll (C#, net9.0, Harmony) ────────────────────────────┐
│                                                                                          │
│  GameApi/            couche d'isolation version (seul endroit qui touche sts2.dll)        │
│   ├ VersionProbe     lit release_info.json, log la version                                │
│   ├ CombatReader     état : joueurs, ennemis, intents, piles, pouvoirs, block/HP          │
│   └ DamageBridge     wrapper sur Hook.ModifyDamage + multiplicateurs Weak/Vuln            │
│                                                                                          │
│  Core/               logique pure, testable sans Godot                                    │
│   ├ DamageForecast   F1                                                                   │
│   ├ LethalIndex      F2                                                                   │
│   ├ TurnPriority     F3 (catalogue + main)                                                │
│   ├ RunScore         F4 (attribution + agrégats)                                          │
│   └ PathRisk         F5                                                                   │
│                                                                                          │
│  Net/                multi                                                               │
│   ├ UltimaPriorityMessage : INetMessage  (auto-découvert par le loader)                   │
│   ├ UltimaForecastMessage : INetMessage                                                  │
│   └ NetBridge        RunManager.Instance.NetService.SendMessage<T>(msg)                   │
│                                                                                          │
│  UI/                 Godot Control nodes                                                  │
│   ├ UltimaOverlay    root, drag, hotkey F8, opacité repos                                 │
│   ├ IntentBadges     accolades F1/F2 ancrées aux intents                                  │
│   ├ PriorityStrip    bandeau F3 (cartes à jouer en premier)                               │
│   └ ScorePanel       F4 (table compacte, toggle)                                          │
│                                                                                          │
│  Patches/            Harmony (le minimum)                                                 │
│   ├ CombatUiInjectionPatch  NCombatUi._Ready → crée l'overlay                             │
│   ├ CardPlayObserver        Hook.AfterCardPlayedLate → refresh + attribution              │
│   └ ReturnToMenuPatch       NGame.ReturnToMainMenu → cleanup                              │
└──────────────────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    │ (optionnel, V2)
                                    ▼
                        Bridge HTTP/SSE localhost:7373  → outils externes
```

## Décisions

| Sujet | Décision | Raison |
|---|---|---|
| Injection UI | Harmony sur `NCombatUi._Ready` | éprouvé par 999dulgi, 1 instance/combat |
| Lecture d'état | hooks + lecture directe des modèles | pas de polling par frame coûteux |
| Calcul dégâts | appeler le vrai pipeline | zéro divergence avec le jeu |
| Multi | `INetMessage` custom | natif, auto-découvert, pas de hack |
| Persistance | fichiers dans le dossier du mod | pas de `%APPDATA%` sous Linux |
| Packaging | DLL + PCK minimal (icône, scène optionnelle) | build sans Godot éditeur |
| Tests | logique Core en console (`tests/`) | la logique pure ne doit pas exiger le jeu |

## Structure de fichiers attendue

```
Ultima/
  Ultima.csproj
  ModEntry.cs
  GameApi/VersionProbe.cs, CombatReader.cs, DamageBridge.cs
  Core/DamageForecast.cs, LethalIndex.cs, TurnPriority.cs, RunScore.cs, PathRisk.cs
  Net/UltimaMessages.cs, NetBridge.cs
  UI/UltimaOverlay.cs, IntentBadges.cs, PriorityStrip.cs, ScorePanel.cs, Theme.cs
  Patches/*.cs
  assets/ (icône, thème .tres si besoin)
  tests/Ultima.Tests/ (xunit, aucune dépendance Godot)
```

## Règles de code

- `Core/` : **aucun** `using Godot` ni `using MegaCrit`. Entrées = structs/DTO.
  Sortie = DTO. C'est ce qui rend F1/F2 testables.
- `GameApi/` : seul endroit autorisé à référencer les types du jeu.
- Try/catch autour de **chaque** patch Harmony : un mod ne doit jamais crasher le jeu.
  Log via `Log.Warn` + fichier local.
- Aucun `async void`. Les hooks renvoient `Task` : propager, ne pas bloquer.

## Ordre de build conseillé pour Claude

1. `GameApi/VersionProbe` + `ModEntry` + log → prouver que le mod charge.
2. `UI/UltimaOverlay` vide + hotkey → prouver l'injection.
3. `CombatReader` → dump de l'état dans le log (preuve de lecture).
4. `DamageForecast` + `LethalIndex` (Core pur + tests) → badges in-game.
5. `TurnPriority` + catalogue → alerte locale.
6. `Net/` → broadcast 2 instances.
7. `RunScore`, `PathRisk`.

À chaque étape : `bash tools/dev.sh` et vérifier le log. Pas de big bang.
