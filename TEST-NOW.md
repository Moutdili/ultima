# TEST-NOW — tester ULTIMA à plusieurs, ce soir

Objectif de la session : **prouver que le broadcast custom fonctionne** entre deux joueurs,
sur la même machine (FastMP) ou sur deux machines.

> ⚠️ C'est le tout premier test en conditions réelles. Le mod logge chaque étape réseau
> (`net: attached`, `net: ping sent`, `net: ping received`). En cas d'échec, le log dit où.

---

## 1. Récupérer le repo

```bash
git clone git@github.com:Moutdili/ultima.git
cd ultima
```

## 2. Construire et installer le mod

### Option A — conteneur (recommandée, reproductible)

```bash
make image                     # podman build, ~3-5 min la première fois
make build                     # compile le mod dans le conteneur
make deploy                    # copie dans le dossier mods du jeu
```

### Option B — sans conteneur (si dotnet est déjà installé)

```bash
bash tools/build.sh
bash tools/deploy.sh
```

Vérifier :

```bash
bash tools/doctor.sh           # doit afficher 14 ok / 0 manquants
ls "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Slay the Spire 2/mods/Ultima"
# attendu : Ultima.dll  Ultima.Core.dll  Ultima.json
```

## 3. Lancer deux instances en local (FastMP)

Le jeu embarque un mode multi local, sans Steam, sur le port **33771**.

**Terminal 1 — hôte :**

```bash
cd "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Slay the Spire 2"
./SlayTheSpire2 --fastmp=host_standard
```

**Terminal 2 — client :**

```bash
cd "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Slay the Spire 2"
./SlayTheSpire2 --fastmp=join --clientId=1001
```

Notes :
- `--clientId` doit être **différent** de l'hôte (l'hôte utilise 1000 par défaut).
- Le client se connecte à `127.0.0.1:33771` (codé en dur dans `NJoinFriendScreen.FastMpJoin`).
- Si Steam refuse une 2e instance, lancer l'exécutable directement (le mode FastMP
  fonctionne sans Steam initialisé).
- Pour deux machines sur le tailnet : FastMP cible `127.0.0.1`, donc il faut soit un
  tunnel (`ssh -L 33771:127.0.0.1:33771`), soit le mode Steam classique.

## 4. Le test

1. Démarrer une run en co-op, entrer dans un **combat**.
2. L'overlay `ULTIMA [MULTI]` apparaît en haut à gauche de la zone de combat.
3. Sur une instance : **F7**.
4. Sur l'autre : la ligne doit passer à `PING from <id>: hello from <machine>` et le
   compteur `pings:` doit s'incrémenter.
5. `F8` cache/affiche l'overlay.

### Ce qui prouve que ça marche

| Observation | Signification |
|---|---|
| `pings: 1` chez l'autre joueur | le message custom a été sérialisé, envoyé, reçu, désérialisé |
| Rien, mais log `net: ping sent` | l'envoi part, la réception/registration échoue |
| Log `net: attach failed` | le service réseau n'est pas encore prêt à l'instant du hook |
| Aucune ligne `net:` | le mod n'est pas chargé ou l'overlay n'est pas injecté |

## 5. Où sont les logs

```bash
tail -f "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Slay the Spire 2/mods/Ultima/ultima.log"
```

## 6. À rapporter

Copier les 30 dernières lignes de `ultima.log` de **chaque** instance, et dire :
- le compteur `pings:` a-t-il bougé ?
- y a-t-il eu un crash ou un freeze ?
- l'overlay chevauche-t-il la main ou les intents ?

## 7. Si ça ne marche pas

Ne rien modifier à l'aveugle. Les causes probables, dans l'ordre :

1. **`Attach()` trop tôt** : le `NetService` n'est pas encore multi à `NCombatUi._Ready`.
   → déplacer l'attachement sur un hook de run (`RunManager`) ou réessayer à chaque tour.
2. **Message non découvert** : le cache de types est construit au démarrage
   (`MessageTypes.Initialize`) ; si le mod est chargé après, le type n'a pas d'ID.
   → tester en lançant le jeu avec le mod déjà installé (c'est le cas ici).
3. **Client vanilla** : un joueur sans le mod ne connaît pas l'ID du message.
   → tous les joueurs doivent avoir le mod pour ce test.
4. **Host qui ne relaie pas** : `ShouldBroadcast=true` est positionné ; vérifier que le
   client envoie bien au host (`SendMessage` côté client) et non l'inverse.

Le log est la source de vérité. On corrige à partir de lui, pas au hasard.
