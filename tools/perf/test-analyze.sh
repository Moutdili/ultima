#!/usr/bin/env bash
# test-analyze.sh — teste analyze-frametimes.py sur les journaux SYNTHÉTIQUES de fixtures/.
# 1. régénère les fixtures dans un dossier temporaire et vérifie qu'elles sont identiques aux
#    fichiers commités (générateur déterministe) ;
# 2. lance l'analyse (sortie brute affichée) ;
# 3. vérifie les verdicts attendus (EXPECTED de make-fixtures.py), les exclusions et le code retour.
# N'écrit que dans un dossier temporaire supprimé à la fin.
set -euo pipefail
HERE=$(cd "$(dirname "$0")" && pwd)
FIX="$HERE/fixtures/session-demo"
TMP=$(mktemp -d); trap 'rm -rf "$TMP"' EXIT
fail=0; ok() { echo "PASS $*"; }; ko() { echo "FAIL $*"; fail=1; }

python3 "$HERE/fixtures/make-fixtures.py" "$TMP/regen" >/dev/null
if diff -r "$FIX" "$TMP/regen" >/dev/null; then ok "fixtures commitées == générateur"; else ko "fixtures commitées != générateur (relancer make-fixtures.py)"; fi

ARGS=(--marks "$FIX/marks.tsv" --events "$FIX/godot-events.tsv"
      --order sans-ultima,rail-seul,panneau-ouvert,sans-flou,sans-unredirect,x11 "$FIX/csv")
echo "----- sortie brute -----"
set +e; python3 "$HERE/analyze-frametimes.py" "${ARGS[@]}"; rc=$?; set -e
echo "------------------------"
[[ $rc -eq 1 ]] && ok "code retour 1 (un DEPASSE attendu)" || ko "code retour $rc, attendu 1"

python3 "$HERE/analyze-frametimes.py" --json "${ARGS[@]}" >"$TMP/out.json" || true
python3 - "$TMP/out.json" "$HERE/fixtures" <<'EOF'
import json, sys
j = json.load(open(sys.argv[1]))
# mêmes valeurs que EXPECTED dans fixtures/make-fixtures.py
exp = {"sans-ultima": "REFERENCE", "rail-seul": "OK", "panneau-ouvert": "DEPASSE",
       "sans-flou": "OK", "sans-unredirect": "OK", "x11": "INDECIS"}
bad = 0
for c, v in exp.items():
    got = j["conditions"].get(c, {}).get("verdict")
    print(("PASS" if got == v else "FAIL"), f"verdict {c} = {got} (attendu {v})"); bad += got != v
segs = {s["file"]: s for s in j["segments"]}
checks = [
    ("masque exclu (focus perdu + plafond 30)", "masque" not in j["conditions"]
     and any("focus perdu" in r for r in segs["SlayTheSpire2_2026-10-29_14-07-10.csv"]["invalid"])
     and any("plafond 30" in r for r in segs["SlayTheSpire2_2026-10-29_14-07-10.csv"]["invalid"])),
    ("ligne tronquée rejetée (rail-seul)", segs["SlayTheSpire2_2026-10-29_14-01-10.csv"]["bad_rows"] == 1),
    ("en-tête versionné lu", segs["SlayTheSpire2_2026-10-29_14-06-10.csv"]["header"] == "versionné"),
    ("attribution par dossier", segs["repetition-2.csv"]["condition"] == "x11" and segs["repetition-2.csv"]["source"] == "dossier"),
    ("2 répétitions de référence", j["conditions"]["sans-ultima"]["reps"] == 2),
    ("colonne gpu_load lue (71 % dans les fixtures)", abs(j["conditions"]["sans-ultima"]["gpu_load_pct"] - 71.0) < 1e-6),
    ("panneau-ouvert : perte 1 % low > 20 %", j["conditions"]["panneau-ouvert"]["loss_low1_pct"] > 20),
    ("rail-seul : perte 1 % low < 3 %", j["conditions"]["rail-seul"]["loss_low1_pct"] < 3),
    ("panneau-ouvert : à-coups > 0 (rafales 32 ms)", j["conditions"]["panneau-ouvert"]["stutters_per_min"] > 0),
    ("référence : 0 à-coup (rafales 24 ms < 1,5 x médiane)", j["conditions"]["sans-ultima"]["stutters_per_min"] == 0),
]
for name, cond in checks:
    print(("PASS" if cond else "FAIL"), name); bad += not cond
sys.exit(1 if bad else 0)
EOF
[[ $? -eq 0 ]] || fail=1

# --assign prime sur les marques
out=$(python3 "$HERE/analyze-frametimes.py" --json --marks "$FIX/marks.tsv" \
      --assign SlayTheSpire2_2026-10-29_14-03-10.csv=essai "$FIX/csv" || true)
python3 -c 'import json,sys; j=json.loads(sys.argv[1]); s=[x for x in j["segments"] if x["file"].endswith("14-03-10.csv")][0]; sys.exit(0 if s["condition"]=="essai" and s["source"]=="--assign" else 1)' "$out" \
  && ok "--assign prime sur --marks" || ko "--assign"

# sans référence -> code 3
set +e; python3 "$HERE/analyze-frametimes.py" --baseline absente "$FIX/csv" >/dev/null; rc=$?; set -e
[[ $rc -eq 3 ]] && ok "référence absente -> code 3" || ko "référence absente -> code $rc"

[[ $fail -eq 0 ]] && echo "RÉSULTAT : PASS" || echo "RÉSULTAT : FAIL"
exit $fail
