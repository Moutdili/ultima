# 02 — Modding : loader, manifest, hooks

## Installation d'un mod

```
<racine jeu>/mods/<ModId>/
  <ModId>.json     manifest (obligatoire)
  <ModId>.dll      code managé (si has_dll)
  <ModId>.pck      ressources Godot (si has_pck)
```

Le loader scanne récursivement `mods/` à la recherche des manifests JSON.
Sous Flatpak, la racine jeu est dans `~/.var/app/...` → **accessible en écriture** sans sudo.

## Manifest (v0.103+)

```json
{
  "id": "Ultima",
  "name": "ULTIMA",
  "author": "Mio",
  "description": "Combat intelligence overlay",
  "version": "0.1.0",
  "has_pck": true,
  "has_dll": true,
  "dependencies": [],
  "affects_gameplay": false
}
```

`affects_gameplay=false` : le mod n'altère pas le gameplay → utilisable en multi même
si les autres joueurs ne l'ont pas. `id` = nom de base des payloads.

## Point d'entrée

```csharp
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
    public static void Initialize()
    {
        Log.Warn("[ULTIMA] boot");
        new Harmony("mio.ultima").PatchAll(typeof(ModEntry).Assembly);
    }
}
```

Un initializer explicite est **obligatoire** dans notre cas : il garantit l'ordre
(log, lecture de version, enregistrement des handlers réseau).

## Trois niveaux d'intégration (du moins invasif au plus)

1. **Override de modèle / command API** — pour du contenu (cartes, reliques). Read-only :
   pas concerné, sauf pour un modèle de "règles" custom.
2. **Hooks sémantiques** — `Hook.*` (lifecycle, damage, cards, rewards, map...).
   Pour un objet de règles persistant : `ModHelper.SubscribeForRunStateHooks` /
   `ModHelper.SubscribeForCombatStateHooks` (ID unique, trié ordinalement).
3. **Harmony** — seulement quand aucun hook ne couvre le besoin (injection UI, observation
   d'une frontière privée). Toujours avec signature exacte :

```csharp
[HarmonyPatch(typeof(NCombatUi), nameof(NCombatUi._Ready))]
internal static class CombatUiInjectionPatch
{
    private static void Postfix(NCombatUi __instance) { /* ajoute l'overlay */ }
}
```

## Hooks utiles pour ULTIMA

| Besoin | Hook/API |
|---|---|
| Début de combat / création UI | `NCombatUi._Ready` (Harmony Postfix) — vérifié dans 999dulgi |
| Carte ajoutée à une pile | `CardPileCmd.Add` (Prefix) — vérifié |
| Carte résolue | `Hook.AfterCardPlayedLate(PlayerChoiceContext, CardPlay)` |
| Dégâts (calculés) | `Hook.ModifyDamage(...)` + `AbstractModel.ModifyDamageAdditive/Multiplicative/Cap` |
| Fin de combat | `NCombatUi.OnCombatWon` (Harmony Postfix) |
| Retour menu | `NGame.ReturnToMainMenu` (Prefix) |

## Build

Le csproj référence les assemblies du jeu (jamais copiées : `Private=false`) :

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="sts2"><HintPath>$(Sts2DataDir)/sts2.dll</HintPath><Private>false</Private></Reference>
    <Reference Include="0Harmony"><HintPath>$(Sts2DataDir)/0Harmony.dll</HintPath><Private>false</Private></Reference>
    <Reference Include="GodotSharp"><HintPath>$(Sts2DataDir)/GodotSharp.dll</HintPath><Private>false</Private></Reference>
  </ItemGroup>
</Project>
```

`dotnet build` suffit (SDK Microsoft.NET.Sdk, pas besoin de Godot .NET pour du code pur).
Le `.pck` peut être fabriqué sans Godot éditeur : `gdre_tools --headless --pck-create=...`
(voir `tools/pack.sh`).

## Pièges connus

- `Godot.NET.Sdk` seulement si tu veux exporter via l'éditeur ; sinon `Microsoft.NET.Sdk` +
  références manuelles = build plus simple et reproductible.
- `%APPDATA%` n'existe pas sous Linux → config dans le dossier du mod ou `user://`.
- `Private=false` sur les références, sinon tu shippes `sts2.dll` (illicite + casse).
- Les noms de types changent entre versions EA : tout patch doit être vérifié par version.
