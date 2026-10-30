#!/bin/bash
# Campagne de preuves miroir 2 — CIBLE DE TEST UNIQUEMENT (jamais le vrai jeu).
set -u
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"
R=/home/mio/v2-miroir2/Ultima.Mirror
T=$R/testtarget/Sts2Shape.TestTarget/bin/Debug/net9.0/Sts2Shape.TestTarget
P=$R/testtarget/Sts2Shape.ProofRunner/bin/Debug/net9.0/mirror-proof
M=$R/src/Ultima.Mirror.Probe/bin/Debug/net9.0/ultima-mirror
E=/home/mio/v2-miroir2/docs/mirror/evidence/miroir2
TR="trace=ptrace,process_vm_writev,process_vm_readv,kill,tgkill,tkill,pidfd_open,pidfd_send_signal"
mkdir -p $E
rm -f $E/32-probe-json.txt
if pgrep -x SlayTheSpire2 >/dev/null; then echo "le jeu tourne : la campagne ne touche qu'à la cible de test (comm Sts2Shape.TestT)"; fi

summ() { # $1 strace log
  echo "# strace $(basename $1) : ptrace=$(grep -c 'ptrace(' $1) process_vm_writev=$(grep -c 'process_vm_writev(' $1) process_vm_readv=$(grep -c 'process_vm_readv(' $1) kill=$(grep -cE '^[0-9]+ +kill\(' $1)"
  grep -E '^[0-9]+ +kill\(' $1 | sed 's/^/#   /' | sort -u | head -3
}

start() { # $1 scene, extra args
  local out=$E/.target-$1.out; shift_scene=$1; shift
  $T --scene $shift_scene "$@" > $out 2>&1 & TP=$!; sleep 1.5
}

{ echo "# $(date -Is) build (0 avertissement attendu)"; cd $R && dotnet build Ultima.Mirror.slnx -nologo --no-incremental 2>&1 | tail -4
  cd $R/testtarget/Sts2Shape.ProofRunner && dotnet build -nologo --no-incremental 2>&1 | tail -4; } > $E/20-build.txt

{ echo "# $(date -Is) layout : métadonnée de sts2.dll (fichier, la DLL n'est ni chargée ni exécutée ; aucune lecture mémoire)"; echo "\$ ultima-mirror layout"; $M layout; echo "exit=$?"; } > $E/21-layout.txt 2>&1

{ echo "# $(date -Is) garde-fous"; echo "\$ mirror-proof guard"; $P guard; echo "exit=$?"; echo; echo "\$ ultima-mirror self-check"; $M self-check; echo "exit=$?"; } > $E/22-guard.txt 2>&1

for scene in event ancient event-stale shop; do
  n=$(case $scene in event) echo 23;; ancient) echo 24;; event-stale) echo 25;; shop) echo 26;; esac)
  $T --scene $scene > $E/.target.out 2>&1 & TP=$!; sleep 1.5
  { echo "# $(date -Is) cible de test --scene $scene ; sortie de la CIBLE :"; cat $E/.target.out; echo
    echo "\$ strace -f -e $TR mirror-proof read --pid $TP --sections event,piles,reward,shop"
    strace -f -qq -o $E/.strace-$scene.log -e $TR $P read --pid $TP --sections event,piles,reward,shop; echo "exit=$?"
    summ $E/.strace-$scene.log; } > $E/$n-$scene.txt 2>&1
  kill $TP; wait $TP 2>/dev/null
done

# Non-régression : CLI existante (Câbleur) sur la cible, sections historiques.
$T --scene shop > $E/.target.out 2>&1 & TP=$!; sleep 1.5
{ echo "# $(date -Is) non-régression : ultima-mirror (CLI inchangée) sur la cible --scene shop"; cat $E/.target.out; echo
  echo "\$ strace -f -e $TR ultima-mirror probe --pid $TP --sections run,reward,shop,combat --compact"
  strace -f -qq -o $E/.strace-regress.log -e $TR $M probe --pid $TP --sections run,reward,shop,combat --compact; echo "exit=$?"
  summ $E/.strace-regress.log; } > $E/27-regression-cli.txt 2>&1
kill $TP; wait $TP 2>/dev/null

# Ordre de la pioche : la cible le rebat toutes les 5 ms et l'affiche elle-même chaque seconde ;
# 40 sondes du miroir doivent donner UNE seule sortie (contenu compté, trié).
$T --scene shop --show-order > $E/.target.out 2>&1 & TP=$!; sleep 1.5
{ echo "# $(date -Is) ordre de pioche : 40 sondes pendant que la cible rebat sa pioche toutes les 5 ms"
  echo "\$ strace -f -e $TR mirror-proof read --pid $TP --sections piles --repeat 40"
  strace -f -qq -o $E/.strace-order.log -e $TR $P read --pid $TP --sections piles --repeat 40; echo "exit=$?"
  summ $E/.strace-order.log
  echo; echo "# témoin : ordre secret affiché PAR LA CIBLE pendant les sondes (doit changer à chaque ligne) :"
  grep "TARGET draw order" $E/.target.out | head -12; } > $E/28-draw-order.txt 2>&1
kill $TP; wait $TP 2>/dev/null

# Endurance : 20 sondes indépendantes (attache/détache) par scène, sous GC compactant + rebattage.
{ echo "# $(date -Is) endurance : 20 sondes indépendantes par scène (nouvelle attache à chaque fois)"
  for scene in event ancient event-stale shop; do
    $T --scene $scene > /dev/null 2>&1 & TP=$!; sleep 1.5
    ok=0; ko=0
    for i in $(seq 20); do
      if timeout 30 $P read --pid $TP --sections event,piles,reward,shop > $E/.run.out 2>&1; then ok=$((ok+1)); else ko=$((ko+1)); tail -3 $E/.run.out; fi
    done
    echo "scene=$scene ok=$ok inconnu_ou_echec=$ko"
    kill $TP; wait $TP 2>/dev/null
  done; } > $E/29-endurance.txt 2>&1

# Format consommé par le daemon : la CLI officielle `ultima-mirror probe --json` (une ligne) sur la cible.
# Les lignes brutes servent de fixtures à Ultima.Mirror.Tests (analysées par MirrorResult.Parse du daemon).
for scene in event ancient event-stale; do
  $T --scene $scene > $E/.target.out 2>&1 & TP=$!; sleep 1.5
  strace -f -qq -o $E/.strace-json-$scene.log -e $TR $M probe --pid $TP --json --sections run,reward,shop,event,piles > $E/32-probe-json-$scene.jsonl; rc=$?
  { echo "# $(date -Is) cible --scene $scene : \$ strace -f -e $TR ultima-mirror probe --pid $TP --json --sections run,reward,shop,event,piles"
    echo "exit=$rc ; ligne JSON : $E/32-probe-json-$scene.jsonl ($(wc -c < $E/32-probe-json-$scene.jsonl) octets)"
    summ $E/.strace-json-$scene.log; } >> $E/32-probe-json.txt
  kill $TP; wait $TP 2>/dev/null
done

{ echo "# $(date -Is) fuite de données cachées : occurrences de SECRET dans les sorties du MIROIR (hors sortie de la cible)"
  for f in $E/2*.txt $E/32-*; do echo "$(basename $f) : $(grep -v '^TARGET\|^EXPECT\|^TESTTARGET' $f | grep -c SECRET)"; done
  echo "# total strace : ptrace=$(cat $E/.strace-*.log | grep -c 'ptrace(') process_vm_writev=$(cat $E/.strace-*.log | grep -c 'process_vm_writev(')"; } > $E/30-leaks-and-syscalls.txt
{ echo "# $(date -Is) chaque fichier:ligne de ObjectPaths déclare bien le membre (source ~/sts2/game/pck/src/Core)"; python3 -I $E/check-source-lines.py; } > $E/31-source-lines.txt 2>&1
{ echo "# $(date -Is) tests automatisés du miroir (fixtures = lignes 32-probe-json-*.jsonl ci-dessus)"
  cd $R && dotnet test tests/Ultima.Mirror.Tests -nologo 2>&1 | grep -E "Passed!|Failed!|error|warning|Réussi|Échec" ; } > $E/33-tests.txt 2>&1
rm -f $E/.target*.out $E/.run.out
# journaux strace bruts (~1 Mo chacun) : non versionnés
mkdir -p "${STRACE_KEEP:-/tmp/ultima-miroir2-strace}" && mv $E/.strace-*.log "${STRACE_KEEP:-/tmp/ultima-miroir2-strace}/"
