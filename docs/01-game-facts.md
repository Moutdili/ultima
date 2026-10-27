# 01 — Faits vérifiés sur le jeu

Tout ce qui suit a été constaté sur l'installation de Mio (fedora), pas recopié d'un blog.

## Version

```json
// <racine jeu>/release_info.json
{"commit":"59260271","version":"v0.107.1","date":"2026-06-18T15:43:56-07:00","branch":"v0.107.1","main_assembly_hash":1692500715}
```

## Moteur et packaging

| Élément | Valeur |
|---|---|
| Moteur | Godot **4.5.1** (format PCK 4.5.1 confirmé par GDRE) |
| Langage gameplay | C# (`net9.0`, runtime .NET 9.0.7 embarqué) |
| Exécutable | `SlayTheSpire2` — **build Linux natif** (`data_sts2_linuxbsd_x86_64`) |
| Code compilé | `data_sts2_linuxbsd_x86_64/sts2.dll` |
| Assets + source | `SlayTheSpire2.pck` (1.9 Go) |
| Audio | FMOD (`libfmod*`, `libGodotFmod*`) |
| Anti-tamper | Sentry (`libsentry*`, `Sentry.dll`) — télémétrie crash, pas d'obfuscation |
| Distribution | Steam appid **2868840**, install **Flatpak** |

## Le .pck contient le code source original

C'est le point crucial : MegaCrit **shippe les sources C#** dans le pck
(`src/Core/...`, `project.godot`, `sts2.csproj`, `sts2.sln`), en plus des scènes et assets.
GDRE les extrait telles quelles → on lit le **vrai** code, pas du pseudo-code ILSpy.

- Source original : `~/sts2/game/pck/src/Core` (3 424 fichiers `.cs` au total dans le pck)
- Décompilé ILSpy : `~/sts2/game/csharp` (utile pour les types sans source, ex. GodotPlugins)
- Localisation : `~/sts2/game/pck/localization/{eng,fra,deu,esp,ita,jpn,kor,pol,ptb,...}`
- Scènes : `~/sts2/game/pck/scenes` · images : `images/` · anims : `animations/`

## Volumétrie du contenu (comptée dans le source, v0.107.1)

| Modèle | Dossiers/fichiers |
|---|---|
| Powers | **521** |
| Monsters | **243** |
| Cartes | 24 répertoires (par personnage/type) sous `Models/Cards` |
| Reliques | 8 répertoires sous `Models/Relics` |

→ Un index complet est généré par `tools/index_source.py` (sortie `docs/11-api-catalog.md`
et `data/catalog/*.json`).

## Arborescence des namespaces utiles

```
MegaCrit.Sts2.Core
├── Combat          état de combat, tours, sides
├── Commands        CreatureCmd, CardPileCmd, PlayerCmd, RelicCmd, PowerCmd (API d'action)
├── Entities        Cards, Creatures, Players, Powers (données runtime)
├── GameActions     CardPlay, PlayerChoiceContext, Multiplayer
├── Hooks           Hook (tous les hooks sémantiques) + ModifyDamageHookType
├── Models          Cards, Monsters, Powers, Relics, Potions, Events, Acts
├── MonsterMoves    Intents (Single/Multi/Attack), state machine
├── Multiplayer     Messages, Serialization (INetMessage), Transport (Steam/ENet), NetMessageBus
├── Nodes           UI Godot (NCombatUi, NCombatRoom, NCardPlay, NIntent...)
├── Odds            probabilités (récompenses, rareté)
├── Map             graphe de map, génération, nodes
├── Runs            RunManager, RunState
├── Saves           sauvegardes
└── Modding         ModInitializer, ModHelper, manifest
```

## Ce qui n'est PAS dans le jeu

- Pas de CLI/headless officielle pour simuler un combat.
- Pas de mode spectateur réseau.
- Pas d'API de plugins tierce : le contrat, c'est le loader de mods + Harmony + hooks.
