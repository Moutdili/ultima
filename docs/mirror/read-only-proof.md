# Miroir mémoire — preuve du read-only et faisabilité sans le jeu (S2, étape 4)

> Agent B, 2026-10-28. Tout ce qui suit a été exécuté sur la machine de Mio **sans lancer le jeu**,
> contre `Sts2Shape.TestTarget` (processus .NET 9 console, invisible, qui reproduit la FORME des
> types de sts2.dll). Sorties brutes : `docs/mirror/evidence/0*.txt`.

## 1. Ce que fait réellement ClrMD en `suspend: false` (lu dans son code, pas supposé)

Décompilation (ilspycmd) de `Microsoft.Diagnostics.Runtime 4.2.751201`,
type `Microsoft.Diagnostics.Runtime.Utilities.LinuxLiveDataReader` :

| Étape | Appel système | Effet sur la cible |
|---|---|---|
| constructeur | `kill(pid, 0)` | signal 0 = test d'existence, **aucun signal délivré** |
| constructeur | lecture de `/proc/PID/maps` | aucun |
| `if (suspend) LoadThreadsAndAttach()` | `PTRACE_ATTACH` + `waitpid` | **jamais exécuté** : on passe `suspend: false` |
| `Read()` | `process_vm_readv` (repli : `/proc/PID/mem` ouvert en `FileAccess.Read`) | lecture seule |
| `GetThreadContext()` | `PTRACE_GETREGS` | n'est appelé que par les parcours de piles : **le miroir n'en fait aucun** (ni piles, ni threads, ni racines GC) |

La doc ClrMD prévient : *« ClrMD does NOT support inspecting a running process and will produce
undefined behavior »*. Ce comportement indéfini concerne **la sonde** (lectures déchirées), pas la
cible : rien n'y est écrit. Parades : `Stable.Read` (2 lectures identiques sinon « INCONNU »),
watchdog `--timeout`, et exécution de la sonde dans un **processus enfant** du daemon (cf. §4).

## 2. Mesuré (strace sur la sonde, evidence/04, 05, 07)

```
strace -f -e trace=ptrace,process_vm_writev,process_vm_readv,kill,tgkill,tkill,pidfd_open,pidfd_send_signal
      1 kill(          -> kill(95144, 0) = 0
   7996 process_vm_readv(95144, ...)
      0 ptrace
      0 process_vm_writev
```
Ouvertures sur la cible : uniquement `/proc/PID/comm`, `/proc/PID/maps` (O_RDONLY) et
`/proc/PID/root/…/libcoreclr.so|libmscordaccore.so` (O_RDONLY). Aucune donnée piégée
(`PlayerRng`, `RelicGrabBag`, `RunState.Rng`, `DrawPile`, marqueur `SECRET_MUST_NOT_LEAK`) n'apparaît
dans aucune sortie (0 occurrence), et `ReadGuard` refuse ces champs (evidence/02).

## 3. Bac à sable (Flatpak / bubblewrap) — simulé, pas encore prouvé sur Steam

| Scénario | Résultat |
|---|---|
| Cible sur l'hôte (même espaces de noms) | attache OK 93 ms ; run/reward/shop/combat == attendu (evidence/04) |
| Cible dans `bwrap --unshare-user --unshare-pid …`, `libcoreclr.so` mappé sous `/tmp/sandbox-dotnet` **inexistant sur l'hôte** | attache OK 111 ms depuis l'hôte avec le PID hôte ; DAC et libcoreclr résolus via `/proc/PID/root` (evidence/05) |
| Cible dans **deux** bwrap imbriqués (NSpid `127670 4 2`) — approxime Flatpak → pressure-vessel | attache OK 117 ms, toutes sections stables (evidence/07) |

Pourquoi ça marche (noyau) : `ptrace_scope = 0` ; même UID ; la sonde est dans l'espace de noms
utilisateur **parent** et son UID effectif est le propriétaire de l'espace de noms de la cible, donc
elle y détient `CAP_SYS_PTRACE` (user_namespaces(7)) — suffisant pour `process_vm_readv` et
`/proc/PID/root` même si la cible n'est pas « dumpable ».

Indice sur le vrai jeu : `godot.log` liste des montages `/run/parent/usr`, `/run/parent/app`,
`/run/host/os-release`, `/etc/debian_version` ⇒ le jeu tourne dans **pressure-vessel (Steam Linux
Runtime) imbriqué dans le Flatpak Steam**. `/usr/bin/bwrap` n'est pas setuid (`-rwxr-xr-x root`) :
les espaces de noms sont créés par l'utilisateur mio, comme dans la simulation. Non prouvé tant que
la fenêtre de test n'a pas eu lieu (seccomp/LSM spécifiques à Steam, `PR_SET_DUMPABLE`…).

Alternative la moins intrusive si l'attache depuis l'hôte échouait (NON exécutée) : aucune
alternative mémoire n'est acceptable (pas de `suspend: true`, pas de `createdump`, pas de gdb,
pas de `flatpak-spawn` dans le bac à sable du jeu) → repli S3 (vision).

## 4. Fragilités observées (à porter dans le daemon)

1. **`ClrRuntime.FlushCachedData()` entre deux lectures = crash de la sonde** : 11 SIGSEGV + 1 blocage
   (futex) sur 12 sondes, cible intacte. Retiré → **50/50 sondes OK** (30 hôte + 20 bwrap) sous GC
   compactant forcé toutes les 20 ms (evidence/06). Règle : une session = un cache ; pour se
   « rafraîchir », jeter la session et se ré-attacher (~100 ms).
2. **ClrMD 4.2 lit la version de CoreCLR 9 comme `0.0`** (cherche `@(#)Version` dans le 1er segment
   PT_LOAD inscriptible ; en .NET 9 la chaîne est en `.rodata`) → son contrôle DAC échoue toujours.
   Remplacé par un contrôle plus strict : chaîne `@(#)Version … @Commit: …` de libcoreclr.so (lue via
   `/proc/PID/root`, donc le fichier réellement mappé) == celle de la DAC. Jeu : les deux valent
   `9.0.725.31616 @Commit: 3c298d9f00936d651cc47d221762474e25277672` (vérifié par `strings`).
3. **`pgrep -f SlayTheSpire2` se trouve lui-même** (le shell qui l'exécute contient le mot) → le
   miroir cherche `/proc/PID/comm == "SlayTheSpire2"` exactement.
4. La DAC est un `.so` **chargé dans le processus de la sonde** (dlopen) : on n'accepte que celle livrée
   avec le jeu ou le runtime de la cible, après égalité version+commit.
5. Le daemon doit lancer le miroir en **processus enfant** avec délai max (crash ou blocage de la
   sonde ⇒ on tue l'enfant, on affiche « miroir indisponible », le reste d'ULTIMA continue).

## 5. Ce que cette étape ne prouve PAS

- Rien sur les **valeurs** du vrai jeu : la cible de test a la même forme, pas les mêmes données ni
  le même tas (Godot, GC concurrent, taille).
- L'attache sur le vrai sandbox Steam (Flatpak + pressure-vessel) : simulée seulement.
- L'absence d'à-coups dans le jeu pendant une sonde (aucune suspension, mais ~7 000–8 000
  `process_vm_readv` par sonde complète ; à mesurer en jeu).

---

## 6. Miroir 2 (2026-10-29, campagne rejouée le 2026-10-30) — mêmes garanties, nouvelles sections

Sorties brutes : `evidence/miroir2/` (rejouables : `evidence/miroir2/run-campaign.sh`). **Cible de test
uniquement** ; le harnais `mirror-proof` (Ultima.Mirror/testtarget/Sts2Shape.ProofRunner) refuse tout
processus dont `/proc/PID/comm` ≠ `Sts2Shape.TestT` : il ne peut pas lire le vrai jeu.

| Mesure | Résultat |
|---|---|
| strace de 9 sondes (événement, ancien, événement périmé, shop, CLI historique, 40 sondes de pioche, 3 × CLI `--json`) | **0 `ptrace`, 0 `process_vm_writev`** ; 1 seul `kill(pid, 0)` par sonde ; ~6 600–7 400 `process_vm_readv` par sonde complète (evidence/miroir2/23-28, 30, 32) |
| Endurance : 4 scènes × 20 sondes indépendantes, GC compactant toutes les 20 ms + pioche rebattue toutes les 5 ms | **80/80 stables** (evidence/miroir2/29) |
| Données cachées (`SECRET_MUST_NOT_LEAK` dans `RunState.Rng`, `Player.PlayerRng`, `RelicGrabBag`, `PlayerCombatState._piles`, `EventModel.Rng`, `EventSynchronizer._multiplayerOptionSelectionRng`) | **0 occurrence** dans les sorties du miroir (evidence/miroir2/30) |
| Ordre de la pioche (rebattue toutes les 5 ms par la cible, qui affiche elle-même l'ordre changeant) | 40 sondes ⇒ **1 seule sortie** (contenu compté trié) (evidence/miroir2/28) |
| `ReadGuard` | `Check` refuse toujours `<DrawPile>k__BackingField`, `_piles`, `Rng`, … ; `CheckUnordered` n'accepte que la pioche (evidence/miroir2/22) |
| Non-régression : `ultima-mirror probe --sections run,reward,shop,combat` (CLI inchangée) | exit 0, toutes sections stables, mêmes valeurs qu'en S2 + champ `Enchantment` (evidence/miroir2/27) |
| Format consommé par le daemon : `ultima-mirror probe --json --sections run,reward,shop,event,piles` (3 scènes) | exit 0, `unsupported: []`, 0 `ptrace`, 0 `process_vm_writev` ; lignes brutes analysées par `MirrorResult.Parse` du daemon dans `Ultima.Mirror.Tests` : **38/38** (evidence/miroir2/32, 33) |

Le miroir 2 n'ajoute **aucun** appel système, aucune méthode du jeu, aucun parcours de pile/tas : seulement
des champs de plus, suivis depuis les mêmes racines statiques (`RunManager.Instance`).
