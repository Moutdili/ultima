# Miroir mémoire — protocole de la fenêtre de test S2

> Écrit à l'avance (docs/21 §6) par l'agent B. **Le jeu est lancé par Mio, pas par un agent.**
> Pendant la fenêtre : aucune capture/entrée simulée par un agent sans accord explicite ;
> l'agent ne fait que lancer `ultima-mirror` (processus console, lecture seule) depuis l'hôte.
> Durée visée : ~15 min. Chaque étape = 1 commande, un résultat attendu, un critère PASS/FAIL.

## 0. Préparation (AVANT que Mio lance le jeu — sans le jeu)

```bash
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"
cd /home/mio/v2-miroir/Ultima.Mirror && dotnet build Ultima.Mirror.slnx      # 0 erreur
M=/home/mio/v2-miroir/Ultima.Mirror/src/Ultima.Mirror.Probe/bin/Debug/net9.0/ultima-mirror
OUT=/home/mio/v2-miroir/docs/mirror/evidence/window-$(date +%Y%m%d-%H%M); mkdir -p "$OUT"
$M layout     | tee "$OUT/00-layout.txt"     # ATTENDU : 56/56, sha256 d13f3f15…bd03 (sinon : patch EA → STOP, re-cartographier)
$M self-check | tee "$OUT/00-self-check.txt" # ATTENDU : exit 0
$M probe      | tee "$OUT/00-no-game.txt"    # ATTENDU : « jeu non lancé », exit 2
ls "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Slay the Spire 2/mods"   # ATTENDU : vide (sinon profil modded/)
```

Audit des non-négociables (G-nonneg) pendant toute la fenêtre, dans un 2e terminal :

```bash
inotifywait -m -r -e modify,create,delete,move,attrib \
  "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Slay the Spire 2" \
  > "$OUT/inotify-gamedir.txt" 2>&1 &
```
ATTENDU : aucune ligne causée par ULTIMA (le jeu lui-même n'écrit pas dans son dossier d'install).

## 1. Mio lance le jeu, menu principal (pas de run)

| Commande | Attendu | PASS si |
|---|---|---|
| `pgrep -x SlayTheSpire2; cat /proc/$(pgrep -x SlayTheSpire2)/status \| grep NSpid` | 1 PID hôte ; NSpid à 2 colonnes (bac à sable Flatpak/bwrap) | PID trouvé |
| `strace -f -qq -o "$OUT/strace-1.log" -e trace=ptrace,process_vm_writev,process_vm_readv,kill $M probe --sections run --count MegaCrit.Sts2.Core.Models.CardModel \| tee "$OUT/01-menu.txt"` | `attache : OK`, `runtime : … 9.0.725.31616 @Commit: 3c298d9f…`, `[run] stable` avec `"InRun": false` | attache OK **et** `grep -c 'ptrace(' strace-1.log` = 0 **et** aucun `process_vm_writev` |

Note : `--count` sur `CardModel` (type abstrait) renverra 0 — c'est voulu comme témoin ; le compte
utile est `--count MegaCrit.Sts2.Core.Models.ModelId` (> 1000 attendu : les modèles canoniques).

Si l'attache échoue : relever le message exact, puis tester **une seule** variante (aucune autre
action) : `$M probe --dac "/proc/<PID>/root/<chemin de libmscordaccore.so vu dans /proc/<PID>/maps>"`.
Ne jamais passer `suspend: true`, ne jamais utiliser gdb/createdump. Si toujours KO → S2 échoue,
repli S3 (vision).

## 2. Sur la carte (run en cours, après une salle)

Mio : continuer/démarrer une run, finir une salle, revenir sur la **carte** (la save vient d'être écrite :
`grep "Wrote" ~/.var/app/com.valvesoftware.Steam/.local/share/SlayTheSpire2/logs/godot.log | tail -1`).

```bash
$M probe --sections run --compare-save | tee "$OUT/02-map.txt"
```
ATTENDU : `[run] stable`, `CurrentRoomType` = type de la salle courante ; bloc `[save]` avec
**hp, max_hp, gold, deck(multiset), relics(ordre), ascension, act_index tous ÉGAL** (exit 0).
Puis comparaison à l'ÉCRAN, faite par Mio à voix haute : PV x/y et or en haut de l'écran == `Hp/MaxHp`,
`Gold` ; nombre de cartes du deck (icône deck) == longueur de `Deck`.
PASS : mémoire == save == écran. Toute DIFF → noter le champ ; ne PAS corriger pendant la fenêtre.

## 3. Écran de récompense (PRIORITÉ)

Mio : gagner un combat et **s'arrêter sur l'écran des récompenses, sans rien cliquer**.

```bash
$M probe --sections reward | tee "$OUT/03a-reward-before-click.txt"
```
ATTENDU : `OpenRewardSets: 1`, un `CardRewards[0]` avec `AlreadyTaken: false` et **3 ids** `CARD.*`.
Mio clique alors sur la récompense de cartes (les 3 cartes s'affichent) :
```bash
$M probe --sections reward | tee "$OUT/03b-reward-open.txt"
```
PASS : les 3 ids == les 3 cartes à l'écran (Mio lit les noms ; correspondance id→nom FR via
`~/sts2/game/pck/localization/fra/cards.json`), dans le même ordre gauche→droite, `Upgrade` = 1 si
la carte affiche « + ». Le fait que 03a == 03b prouve que l'offre est lisible AVANT le clic.
Après choix : `$M probe --sections reward` → `AlreadyTaken: true` ou pile vide.
Témoin log : `grep -E "Card rarity: Rolled|selected card reward" godot.log | tail -4`.

## 4. Shop (PRIORITÉ)

Mio : entrer dans un marchand, **ne rien acheter**.

```bash
$M probe --sections shop,run | tee "$OUT/04a-shop.txt"
```
ATTENDU : `InShop: true`, 5 `Cards` du personnage (2 attaques, 2 compétences, 1 pouvoir) + 2 incolores (Entities/Merchant/MerchantInventory.cs:15-28),
reliques, potions, `RemovalBaseCost`. PASS : ids == objets affichés ; `EstimatedDisplayedCost` ==
prix affiché pour CHAQUE objet (si `PriceModifiers` est vide, `BaseCost` == prix affiché) ; la carte
soldée (`OnSale: true`) est celle avec l'étiquette de solde.
Mio achète **une** carte (de son choix) puis :
```bash
$M probe --sections shop,run | tee "$OUT/04b-shop-after-buy.txt"
```
PASS : la carte achetée a disparu des `Cards`, `Gold` a baissé exactement du prix affiché, `Deck` +1.

## 5. Combat (bonus, si le temps le permet)

```bash
$M probe --sections combat | tee "$OUT/05-combat.txt"
```
PASS : `Round`, `Energy`, ids de `Hand` (ordre gauche→droite), PV/armure de chaque ennemi == écran.

## 6. Fin de fenêtre

```bash
kill %1   # inotifywait
for f in "$OUT"/strace-*.log; do echo "$f ptrace=$(grep -c 'ptrace(' "$f") writev=$(grep -c process_vm_writev "$f")"; done
grep -c SECRET "$OUT"/*.txt   # 0 attendu (aucune donnée cachée)
ls -la ~/.var/app/com.valvesoftware.Steam/.local/share/SlayTheSpire2/steam/*/modded/   # dates inchangées
```
Le jeu doit être resté fluide (Mio : aucun à-coup perçu pendant les sondes ; chaque sonde dure < 1 s
sur la cible de test).

## Tableau de verdict (à remplir pendant la fenêtre)

| Étape | Mémoire | Save | Écran | Verdict |
|---|---|---|---|---|
| 1 attache, 0 ptrace | | — | — | |
| 2 PV/or/deck/reliques | | | | |
| 3 récompense avant clic / ouverte | | — | | |
| 4 shop ids + prix / après achat | | — | | |
| 5 combat | | — | | |

## Ce que la fenêtre doit trancher (inconnues actuelles)

1. L'attache marche-t-elle sur le VRAI bac à sable Flatpak Steam (+ éventuel conteneur
   pressure-vessel/Steam Linux Runtime) ? Simulé avec succès sous bwrap (evidence/05), pas prouvé sur Steam.
2. Le runtime 9.0.7 du jeu (build `9.0.725.31616`) + sa DAC se comportent-ils comme 9.0.22 sous ClrMD 4.2 ?
3. `LocalContext.NetId` est-il renseigné en solo ? (sinon repli codé : joueur unique.)
4. Le tas Godot/jeu (taille, GC concurrent) rend-il des lectures INCONNU fréquentes ?
5. Prix affichés == `_cost` (ou estimation avec reliques) ?
