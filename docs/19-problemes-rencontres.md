# 19 — Journal des problèmes rencontrés

Tout ce qui a bloqué, avec le symptôme exact, la cause réelle et le correctif appliqué.
Version jeu : **v0.107.1** · Godot **4.5.1** · Fedora (Flatpak) · mod dans `mods/Ultima/`.

Règle de lecture : le log `mods/Ultima/ultima.log` est la **source de vérité**. À chaque
itération, on lisait le log avant de coder. Trois des cinq bugs ci-dessous étaient
invisibles depuis le jeu et évidents dans le log.

---

## 1. Le mod ne démarre pas du tout — Harmony + libgcc (godot#81032)

**Symptôme**

```
FATAL init: HarmonyLib.HarmonyException: Patching exception in method
  virtual System.Void MegaCrit.Sts2.Core.Nodes.Combat.NCombatUi::_Ready()
 ---> System.DllNotFoundException: Unable to load shared library '/tmp/mm-exhelper.so.dGg6Jx'
      ... undefined symbol: _Unwind_RaiseException
```

**Conséquence** : aucun patch appliqué → l'overlay n'existe pas → **aucune touche ne peut
répondre**. Ce n'était pas un bug de touches.

**Cause** : Godot ne charge pas `libgcc_s` avec `RTLD_GLOBAL`
([godot#81032](https://github.com/godotengine/godot/issues/81032)). Harmony compile son
helper natif `mm-exhelper.so` à l'exécution puis le `dlopen` : le symbole
`_Unwind_RaiseException` n'est pas résolvable dans l'espace de noms global du process.

**Correctif** (`Ultima/ModEntry.cs`, commit `7d4f59f`) : charger explicitement la lib en
global **avant** `new Harmony(id).PatchAll()` :

```csharp
[DllImport("libdl.so.2", EntryPoint = "dlopen")]
private static extern IntPtr DlOpen(string fileName, int flags);
DlOpen("libgcc_s.so.1", RtldNow | RtldGlobal);   // 0x2 | 0x100
```

Candidats essayés : `libgcc_s.so.1`, `/usr/lib64/…`, `/usr/lib/x86_64-linux-gnu/…`,
`libunwind.so.8`.

**Piège** : `NativeLibrary.TryLoad` **ne garantit pas** `RTLD_GLOBAL` — il faut passer par
`dlopen` directement. Un test reproduisant le cas sur Fedora a donné `True` avant même le
préchargement (le runtime .NET avait déjà chargé libgcc), donc le test était non concluant :
seul le log du jeu tranche.

**Repli utilisateur** si le correctif ne suffit pas : option de lancement Steam
`LD_PRELOAD=/usr/lib64/libgcc_s.so.1 %command%`.

**Preuve après correctif** :
```
dlopen: libgcc_s.so.1 (RTLD_GLOBAL) — workaround godot#81032
harmony applied
```

---

## 2. `FileNotFoundException: Ultima.Core` — le loader ne résout pas les dépendances

**Symptôme**
```
overlay injection failed: FileNotFoundException: Could not load file or assembly
  'Ultima.Core, Version=1.0.0.0'. The system cannot find the file specified.
```

**Cause** : `ModManager.HandleAssemblyResolveFailure` (`src/Core/Modding/ModManager.cs:892`)
ne gère **que deux noms** : `sts2,` et `0Harmony,`. Tout le reste renvoie `null`. Et chaque
mod est chargé dans son propre `AssemblyLoadContext` (`ModManager.cs:718`) — le dossier du
mod n'est pas une sonde de chargement.

**Correctif** (`Ultima/ModEntry.cs`, commits `77282f3` + `1efbd2b`) : précharger les DLL
sœurs dans **l'ALC du mod**, avec un `AppDomain.AssemblyResolve` en filet :

```csharp
var alc = AssemblyLoadContext.GetLoadContext(typeof(ModEntry).Assembly)
          ?? AssemblyLoadContext.Default;
alc.LoadFromAssemblyPath(Path.Combine(dir, "Ultima.Core.dll"));
```

**Preuve** :
```
assembly: Ultima.Core precharge (contexte IsolatedComponentLoadContext(.../sts2.dll))
```

---

## 3. Les touches ne répondent pas

**Symptôme** : le jeu tourne, l'overlay logue `overlay injected`, mais F7/F8/F9 ne font rien
et **aucune ligne `ui key` n'apparaît** dans le log.

**Deux causes cumulées**

1. `UltimaOverlay._Ready()` appelait `SetProcess(false)` → le node ne traite aucune frame.
2. `_UnhandledInput` arrive **après** `_Input` et la GUI. Or le jeu lit ses raccourcis dans
   `NInputManager._UnhandledKeyInput` et marque les événements comme traités
   (`SetInputAsHandled`) — nos touches n'atteignaient donc jamais notre handler.

**Correctif** (commit `e2e8304`) : abandon du routage d'événements, passage en **polling
d'état avec détection de front** (une seule action par appui), et `SetProcess(true)` :

```csharp
bool k1 = shift && Input.IsPhysicalKeyPressed(Key.Key1);
if (k1 && !_k1Down) { /* action */ }
_k1Down = k1;
```

**Preuve** :
```
ui key Maj+1 -> detail=True
ui key Maj+2 -> hidden=True
ui key Maj+3 -> ping
```

---

## 4. L'overlay est là mais invisible

**Symptôme** : les toggles fonctionnent dans le log (`hidden=True`), mais **rien n'apparaît**
à l'écran — donc aucune cible à survoler, donc aucune expansion.

**Cause** (charte appliquée trop littéralement) : rail de **8 px** de large à **30 %
d'opacité**, avec des pastilles de **14 px de haut dans 8 px de large** → rognées. Sur un
décor chargé, invisible.

**Correctif** (commit `fa00f8d`) :
- rail 8 → **14 px**
- opacité au repos 0.30 → **0.62**
- **liseré or** de 2 px sur le bord droit (repère permanent, indépendant du décor)
- log de géométrie unique par combat :

```
ui layout: viewport=(1920,1080) pos=(1906,509) size=(14,62) alpha=0.62 parent=NCombatUi
```

**Leçon** : « discret » ne veut pas dire « invisible ». Un panneau replié doit rester
localisable du coin de l'œil, sinon l'utilisateur conclut que le mod est mort.

---

## 5. « Le mod est considéré comme un mod » → progression disparue

**Symptôme** : au lancement avec ULTIMA, les profils semblent vides, progression perdue.

**Cause** : `UserDataPathProvider.GetProfileDir` (`Core/Saves/UserDataPathProvider.cs:43`) :

```csharp
return Path.Combine(IsRunningModded ? "modded/" : "", $"profile{profileId}");
```

et `ModManager.IsRunningModded()` = **au moins un mod chargé**. Donc tout mod, même purement
cosmétique, déplace les sauvegardes dans `modded/`.

**CE N'EST PAS `affectsGameplay`** : ce champ ne sert qu'aux listes de mods en multijoueur
(`JoinFlow`, `InitialGameInfoMessage`). Il **ne change pas** le chemin de sauvegarde.

**État réel des données** (rien n'est perdu) :
```
…/SlayTheSpire2/steam/76561199515637309/profile1|2|3/      ← vanilla, intact
…/SlayTheSpire2/steam/76561199515637309/modded/profile1/   ← créé par ULTIMA
…/Steam/userdata/1555371581/2868840/remote/                ← miroir Steam Cloud
```

**Remèdes possibles**
1. Désactiver le mod (ce qui a été fait) → les profils vanilla réapparaissent.
2. Ou copier `profileN/` vers `modded/profileN/` (avec backup) pour continuer en moddé.
3. Attention au Steam Cloud qui synchronise les deux arborescences.

**Désactivation propre** : le loader scanne `mods/` **récursivement** — renommer le dossier
en `Ultima.disabled` ne suffit pas, son `Ultima.json` serait encore lu. Il faut **sortir le
dossier de `mods/`** :
```bash
mv "<jeu>/mods/Ultima" ~/ultima-disabled/Ultima
```
Fait sur la machine de test, avec aussi `Ultima.bak-2026` (un backup oublié dans `mods/`,
qui aurait été chargé comme un second mod).

---

## 6. `verify-deploy.sh` mentait (dates vs contenu)

**Symptôme** : le script affichait « ✅ déploiement à jour » alors que le DLL déployé était
l'ancien.

**Cause** : un build **échoué** laisse le staging intact ; `deploy.sh` recopie alors l'ancien
binaire, dont la date de copie devient **plus récente** que celle du build. Comparer les
dates ne prouve rien.

**Correctif** (commit `1efbd2b`) : comparaison de **sha256** entre le build et le déployé,
plus vérification des callbacks Godot dans le binaire déployé.

---

## 7. Conteneur : `ilspycmd: command not found` après installation réussie

**Symptôme**
```
Tool 'ilspycmd' (version '11.2.0.10050') was successfully installed.
/bin/sh: line 1: ilspycmd: command not found
Containerfile:25  → exit code 127
```

**Cause** : `dotnet tool install --global` installe dans `$HOME/.dotnet/tools`
(`/root/.dotnet/tools`), qui n'est pas dans le `PATH` de l'image.

**Correctif** (commit `074e06e`) :
- `dotnet tool install --tool-path /usr/local/dotnet/tools ilspycmd`
- GDRE résolu par `find` (le zip peut extraire dans un sous-dossier)
- `Makefile` compatible **podman et docker** (`--userns=keep-id` n'existe que sous podman,
  ce qui cassait les builds lancés via buildx)

**Preuve** : image construite puis testée —
`SDK 9.0.320 + 10.0.403 · ilspycmd 11.2 · GDRE 2.7.4 · Python 3.14 · Node 22`.

---

## 8. MCP `rea` : configuration invalide

**Symptôme** : le serveur MCP ne répond pas / `doctor` rouge.

**Cause** : le `.mcp.json` versionné contenait `npx -y rea-agents@latest mcp serve` — la
sous-commande `serve` n'existe pas. Il avait de plus écrasé la configuration générée par
`rea mcp add`.

**Correctif** (commit `f9a2ca3`) : `npx -y rea-agents@6.8.0 mcp`.
**Preuve** : 5/5 checks verts, 229 outils, 6 prompts.

---

## 9. `gh` absent sur la machine de jeu

`gh` n'est pas installé : toute instruction du type `gh pr create` échoue. Le livrable est
donc **la branche poussée**, pas une PR. Documenté dans `docs/15-swarm-playbook.md`.
Le push depuis un worktree a été **prouvé** (branche créée puis supprimée sur GitHub).

---

## 10. Chiffres 1-9 et console de cheat : ce n'est pas ULTIMA

- Les chiffres sont mappés par le jeu via `NInputManager._debugInputMap` vers des
  `DebugHotkey` (cacher la top bar, les intents, la main…), actifs uniquement en
  `NGame.IsTrailerMode`.
- La console de triche qui s'ouvre est `NDevConsole`, côté MegaCrit.
- Les touches d'ULTIMA sont sur **Maj+1/2/3** (le modificateur évite de marcher sur un
  raccourci existant). Alias F7/F8/F9 conservés.
- Sur AZERTY, `é`/`à`/`$` sont les **mêmes touches physiques** que 2/0/4 : changer de label
  ne change rien à ce que le jeu reçoit. Si conflit avéré, les touches réellement libres
  sont **F2, F8, F12**.

---

## 11. Ce qui n'a pas encore été validé

| Point | État |
|---|---|
| Rendu du verre (flou, teinte, liseré) | ⏳ jamais vu à l'écran par un humain |
| FPS avec flou | ⏳ non mesuré en jeu |
| Exactitude `NxM` vs intents du jeu (G5) | ⏳ 3 rencontres à comparer |
| Chevauchements main/intents en 1080p/1440p/21:9 | ⏳ |
| Multi 2 instances (`tools/multi-lab.sh`) | ⏳ |
| F6 PathRisk | ❌ non implémenté |
| F5 dans l'UI (CSV + log seulement) | ⚠️ partiel |
| Voie B réseau (messages custom) | ⚠️ codée, OFF par défaut |

---

## 12. Leçons générales

1. **Le log du mod avant tout** : trois bugs sur cinq étaient déjà écrits noir sur blanc.
2. **Ne jamais se fier au mtime** d'un artefact : comparer le contenu (hash).
3. **Vérifier dans le binaire déployé**, pas dans les sources : `strings -el` pour les
   chaînes UTF-16 des assemblies .NET.
4. **Un mod n'est jamais « read-only » du point de vue du jeu** : sa simple présence change
   le dossier de sauvegarde. À annoncer à l'utilisateur avant installation.
5. **Discret ≠ invisible** : une UI repliée doit rester repérable.
6. **Le polling d'input est plus fiable que le routage** dans un jeu qui consomme ses
   événements.
