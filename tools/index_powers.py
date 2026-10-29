#!/usr/bin/env python3
"""Extrait les pouvoirs de Slay the Spire 2 (source C#) vers un catalogue déclaratif.

Le moteur ne code pas 260 classes : il lit `data/catalog/powers.json` et applique les
expressions des hooks. Ce script produit ce fichier en LECTURE SEULE depuis la machine de
jeu (ssh + tar sur stdout, aucun fichier écrit à distance).

    python3 tools/index_powers.py                     # ssh mio@100.123.83.40 (défaut)
    python3 tools/index_powers.py --src-dir /chemin   # copie locale (dev / hors ligne)

Ce qui est extrait, par pouvoir :
  id            nom de classe sans le suffixe `Power`, en snake_case (`DrawCardsNextTurnPower`
                -> `draw_cards_next_turn`)
  class, base   déclaration C# (`PowerModel`, `TemporaryStrengthPower`, ...)
  hooks         { hook: expression } pour les 12 hooks du moteur (voir HOOKS)
  source        "fichier:ligne" de la première déclaration de hook (ou de la classe)

Règles d'extraction (déterministes, aucune invention) :

1. Un hook n'est retenu que s'il est déclaré `override` dans le fichier.
2. Les `return` sont collectés au niveau 0 du corps de la méthode (les `return` des lambdas
   imbriquées sont ignorés). Les valeurs « neutres » sont écartées : `1` pour un
   multiplicateur, `0` pour un additif, `count`/`amount`/`playCount` pour un hook qui rend
   la valeur reçue, `decimal.MaxValue` pour un plafond (pas de plafond).
3. S'il ne reste qu'UNE valeur non neutre, c'est l'effet du pouvoir (les `if` qui précèdent
   sont des conditions d'applicabilité, pas des valeurs). S'il en reste plusieurs, la
   dernière (`return` terminal) est retenue et le hook est marqué `conditional`.
4. L'expression est normalisée vers le mini-langage de `ExpressionEval` :
       base.Amount                      -> Amount        (piles du pouvoir)
       <paramètre d'entrée du hook>     -> In            (count / amount / playCount / block)
       base.DynamicVars["X"].BaseValue  -> littéral de `CanonicalVars` (si c'est un nombre)
       (decimal) / (int)                -> supprimé
       `1.5m`                           -> `1.5`
5. Tout ce qui n'est pas réductible à ce langage donne `null` + une raison dans `unresolved`
   (`call` = appel de fonction, `state` = état de combat, `local` = variable locale non
   réductible, `dynamicvar` = DynamicVar sans littéral, `unbounded` = decimal.MaxValue).
   Aucune valeur par défaut n'est inventée : `null` veut dire « non évaluable ».
6. Une variable locale n'est réduite que si elle est initialisée une fois et que ses
   réaffectations sont des appels `X.Modify…Multiplier(...)` : la valeur de base est gardée
   et les modificateurs externes sont listés dans `dynamic_inputs` (Vulnerable -> 1.5,
   modifiable par PaperPhrog / Cruelty / Debilitate).

Sortie : `data/catalog/powers.json`, compacte (< 100 Ko) pour tenir dans le budget de
données commitées (2 Mio, tests de packaging).

Après extraction, le script affiche la mesure : combien de pouvoirs ont au moins un hook
évaluable, combien ne le sont pas et pourquoi (20 premiers).
"""
from __future__ import annotations

import argparse
import io
import json
import os
import re
import subprocess
import sys
import tarfile
from collections import Counter

DEFAULT_HOST = "mio@100.123.83.40"
DEFAULT_SRC = "~/sts2/game/pck/src/Core/Models/Powers"

HOOKS = [
    "ModifyDamageMultiplicative",
    "ModifyDamageAdditive",
    "ModifyDamageCap",
    "ModifyBlockMultiplicative",
    "ModifyBlockAdditive",
    "ModifyHandDraw",
    "ModifyMaxEnergy",
    "ModifyCardPlayCount",
    "ModifyXValue",
    "ModifyMerchantPrice",
    "ShouldDie",
    "PreventDeath",
]

BOOL_HOOKS = {"ShouldDie", "PreventDeath"}

# Valeur(s) qui signifient « ce pouvoir ne change rien » pour un hook donné.
NEUTRAL = {
    "ModifyDamageMultiplicative": {"1"},
    "ModifyDamageAdditive": {"0"},
    "ModifyDamageCap": {"MaxValue"},
    "ModifyBlockMultiplicative": {"1"},
    "ModifyBlockAdditive": {"0"},
    "ModifyHandDraw": {"In"},
    "ModifyMaxEnergy": {"In"},
    "ModifyCardPlayCount": {"In"},
    "ModifyXValue": {"In"},
    "ModifyMerchantPrice": {"In"},
    "ShouldDie": {"false"},
    "PreventDeath": {"false"},
}

NUM_TYPES = ("decimal", "int", "float", "double")


# --------------------------------------------------------------------------- source

def fetch_sources(host: str, src: str) -> dict[str, str]:
    """Lit les *.cs distants (read-only) et rend {nom_de_fichier: contenu}."""
    remote = src.replace("~", "$HOME", 1) if src.startswith("~") else src
    cmd = f"cd {remote} && tar cf - *.cs"
    out = subprocess.run(
        ["ssh", "-o", "BatchMode=yes", "-o", "ConnectTimeout=15", host, cmd],
        capture_output=True,
    )
    if out.returncode != 0:
        raise SystemExit(
            "ssh a échoué (%d) : %s" % (out.returncode, out.stderr.decode("utf-8", "replace").strip())
        )
    files: dict[str, str] = {}
    with tarfile.open(fileobj=io.BytesIO(out.stdout)) as tar:
        for member in tar.getmembers():
            if not member.isfile() or not member.name.endswith(".cs"):
                continue
            handle = tar.extractfile(member)
            if handle is None:
                continue
            files[os.path.basename(member.name)] = handle.read().decode("utf-8-sig", "replace")
    return files


def read_sources(src_dir: str) -> dict[str, str]:
    files: dict[str, str] = {}
    for name in sorted(os.listdir(src_dir)):
        if name.endswith(".cs"):
            with open(os.path.join(src_dir, name), encoding="utf-8-sig") as handle:
                files[name] = handle.read()
    return files


# --------------------------------------------------------------------------- scanning

def skip_literal(src: str, i: int) -> int:
    """i pointe sur " ou ' — rend l'index juste après la chaîne."""
    quote = src[i]
    i += 1
    while i < len(src):
        if src[i] == "\\":
            i += 2
            continue
        if src[i] == quote:
            return i + 1
        i += 1
    return i


def match_brace(src: str, open_index: int) -> int:
    """Index de la '}' qui ferme la '{' en open_index (chaînes/commentaires ignorés)."""
    depth = 0
    i = open_index
    while i < len(src):
        ch = src[i]
        if ch in "\"'":
            i = skip_literal(src, i)
            continue
        if ch == "/" and i + 1 < len(src) and src[i + 1] == "/":
            i = src.find("\n", i)
            if i < 0:
                return len(src) - 1
            continue
        if ch == "/" and i + 1 < len(src) and src[i + 1] == "*":
            end = src.find("*/", i + 2)
            i = len(src) if end < 0 else end + 2
            continue
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                return i
        i += 1
    return len(src) - 1


def split_params(sig: str) -> list[tuple[str, str]]:
    """[(type, nom)] depuis `Creature? target, decimal amount, ...` (génériques respectés)."""
    parts: list[str] = []
    depth = 0
    current = ""
    for ch in sig:
        if ch in "<([":
            depth += 1
        elif ch in ">)]":
            depth -= 1
        if ch == "," and depth == 0:
            parts.append(current)
            current = ""
        else:
            current += ch
    if current.strip():
        parts.append(current)

    out: list[tuple[str, str]] = []
    for part in parts:
        part = part.strip()
        if not part:
            continue
        # `decimal amount`, `Creature? target`, `params int[] x`
        bits = part.replace("params ", "").split()
        if len(bits) < 2:
            continue
        name = bits[-1].lstrip("@")
        typ = " ".join(bits[:-1]).replace("?", "").strip()
        out.append((typ, name))
    return out


def return_sites(body: str) -> list[tuple[int, str]]:
    """[(offset, expression)] de tous les `return` du corps (hors lambda).

    Les `return` des `if` de garde sont conservés : c'est eux qui portent les valeurs
    neutres (`1m`, `0m`, `count`) et les branches secondaires (`3m` de Gigantification).
    """
    found: list[tuple[int, str]] = []
    depth = 0
    i = 0
    while i < len(body):
        ch = body[i]
        if ch in "\"'":
            i = skip_literal(body, i)
            continue
        if ch == "/" and i + 1 < len(body) and body[i + 1] == "/":
            nl = body.find("\n", i)
            i = len(body) if nl < 0 else nl
            continue
        if ch == "{":
            depth += 1
            i += 1
            continue
        if ch == "}":
            depth -= 1
            i += 1
            continue
        if body.startswith("return", i) and not re.search(r"=>\s*$", body[:i]) and (
            i == 0 or not (body[i - 1].isalnum() or body[i - 1] == "_")
        ):
            after = i + len("return")
            if after < len(body) and (body[after].isalnum() or body[after] == "_"):
                i = after
                continue
            # capture jusqu'au ';' de niveau 0 (les parenthèses/ternaires sont dans l'expression)
            j = after
            paren = 0
            while j < len(body):
                c = body[j]
                if c in "\"'":
                    j = skip_literal(body, j)
                    continue
                if c in "([":
                    paren += 1
                elif c in ")]":
                    paren -= 1
                elif c == ";" and paren == 0:
                    break
                j += 1
            found.append((i, body[after:j].strip()))
            i = j + 1
            continue
        i += 1
    return found


def count_keyword(body: str, keyword: str) -> int:
    """Occurrences du mot-clé au niveau 0 (switch/if de la méthode, pas des lambdas)."""
    count = 0
    depth = 0
    i = 0
    while i < len(body):
        ch = body[i]
        if ch in "\"'":
            i = skip_literal(body, i)
            continue
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
        elif depth == 0 and body.startswith(keyword, i):
            before_ok = i == 0 or not (body[i - 1].isalnum() or body[i - 1] == "_")
            after = i + len(keyword)
            after_ok = after >= len(body) or not (body[after].isalnum() or body[after] == "_")
            if before_ok and after_ok:
                count += 1
        i += 1
    return count


# --------------------------------------------------------------------------- normalisation

NUM_LITERAL = re.compile(r"(?<![\w.])(\d+(?:\.\d+)?)m\b")
DYNVAR = re.compile(r'base\.DynamicVars\[\s*"([^"]+)"\s*\]\s*\.\s*(?:BaseValue|IntValue)')
LOCAL_DECL = re.compile(r"\b(?:decimal|int|float|double)\s+([A-Za-z_]\w*)\s*=\s*([^;]+);")
ASSIGN = re.compile(r"(?<![=<>!+\-*/])\b([A-Za-z_]\w*)\s*=\s*([^=;][^;]*);")

ALLOWED_TOKEN = re.compile(r"^(?:\d+(?:\.\d+)?|Amount|In|[+\-*/()])$")


def tokens(expr: str) -> list[str]:
    return re.findall(r"\d+(?:\.\d+)?|[A-Za-z_]\w*|[+\-*/()]|\S", expr)


def strip_outer_parens(expr: str) -> str:
    """`(1.5)` -> `1.5` ; `(1 + 2) * 3` reste inchangé (la parenthèse n'englobe pas tout)."""
    expr = expr.strip()
    while expr.startswith("(") and expr.endswith(")"):
        depth = 0
        for index, ch in enumerate(expr):
            if ch == "(":
                depth += 1
            elif ch == ")":
                depth -= 1
                if depth == 0 and index != len(expr) - 1:
                    return expr
        expr = expr[1:-1].strip()
    return expr


def try_parse(expr: str) -> bool:
    """Valide la grammaire du mini-langage (miroir de ExpressionEval côté C#)."""
    toks = tokens(expr)
    if not toks:
        return False
    pos = 0

    def peek() -> str | None:
        return toks[pos] if pos < len(toks) else None

    def expr_node() -> bool:
        nonlocal pos
        if not term():
            return False
        while peek() in ("+", "-"):
            pos += 1
            if not term():
                return False
        return True

    def term() -> bool:
        nonlocal pos
        if not factor():
            return False
        while peek() in ("*", "/"):
            pos += 1
            if not factor():
                return False
        return True

    def factor() -> bool:
        nonlocal pos
        tok = peek()
        if tok is None:
            return False
        if tok in ("+", "-"):
            pos += 1
            return factor()
        if tok == "(":
            pos += 1
            if not expr_node():
                return False
            if peek() != ")":
                return False
            pos += 1
            return True
        if tok in ("Amount", "In") or re.fullmatch(r"\d+(?:\.\d+)?", tok):
            pos += 1
            return True
        return False

    ok = expr_node()
    return ok and pos == len(toks)


def resolve_locals(body: str, expr: str) -> tuple[str, str | None, list[str]]:
    """Réduit les variables locales numériques. Rend (expr, raison|None, dynamic_inputs)."""
    decls = LOCAL_DECL.findall(body)
    dynamic: list[str] = []
    # type des récepteurs : `PaperPhrog paperPhrog = ...`
    receiver_types = {
        var: typ for typ, var in re.findall(r"\b([A-Z]\w*)\s+([A-Za-z_]\w*)\s*=", body)
    }

    for name, init in decls:
        if not re.search(r"\b%s\b" % re.escape(name), expr):
            continue
        value = init.strip()
        # réaffectations (hors déclaration elle-même, qui matche aussi `NAME = ...`)
        stripped = LOCAL_DECL.sub("", body)
        reassigned = [rhs.strip() for lhs, rhs in ASSIGN.findall(stripped) if lhs == name]
        reducible = True
        for rhs in reassigned:
            call = re.match(r"([A-Za-z_]\w*)\s*\.\s*(\w+)", rhs)
            if call and "Modify" in call.group(2):
                receiver = call.group(1)
                dynamic.append("%s.%s" % (receiver_types.get(receiver, receiver), call.group(2)))
            else:
                reducible = False
        if not reducible:
            return expr, "local", []
        expr = re.sub(r"\b%s\b" % re.escape(name), "(%s)" % value, expr)
    return expr, None, sorted(set(dynamic))


def normalize(
    raw: str,
    baseline: str | None,
    dynvars: dict[str, str],
    runtime_vars: set[str],
    body: str,
) -> tuple[str | None, str | None, list[str]]:
    """Rend (expression normalisée | None, raison | None, dynamic_inputs)."""
    expr = raw.strip()

    if re.fullmatch(r"decimal\.MaxValue", expr):
        # « pas de plafond » : valeur neutre pour ModifyDamageCap, pas une expression évaluable
        return "MaxValue", None, []

    expr, reason, dynamic = resolve_locals(body, expr)
    if reason:
        return None, reason, []

    # casts
    expr = re.sub(r"\((?:decimal|int|float|double)\)", "", expr)
    # littéraux décimaux : 1.5m -> 1.5
    expr = NUM_LITERAL.sub(r"\1", expr)
    # base.Amount -> Amount
    expr = re.sub(r"\bbase\.Amount\b", "Amount", expr)
    # base.DynamicVars["X"].BaseValue -> littéral déclaré
    def dyn(match: re.Match[str]) -> str:
        key = match.group(1)
        if key in runtime_vars:
            return "\x00%s\x00" % key  # valeur modifiée en combat : jamais figée au défaut
        return dynvars.get(key, "\x00%s\x00" % key)

    expr = DYNVAR.sub(dyn, expr)
    if "\x00" in expr:
        key = expr.split("\x00")[1]
        return None, ("dynamicvar_runtime" if key in runtime_vars else "dynamicvar"), dynamic
    # paramètre d'entrée du hook -> In
    if baseline:
        expr = re.sub(r"\b%s\b" % re.escape(baseline), "In", expr)

    expr = strip_outer_parens(" ".join(expr.split()))
    if not expr:
        return None, "empty", dynamic

    bad = [tok for tok in tokens(expr) if not ALLOWED_TOKEN.match(tok)]
    if bad:
        if re.search(r"\b[A-Z]\w*\s*\.\s*\w+\s*\(", expr):
            reason = "call"          # Math.Max(...), Math.Pow(...)
        elif "." in expr:
            reason = "state"         # GetInternalData<T>().champ, Creature.Modèle
        elif "(" in expr:
            reason = "call"
        else:
            reason = "unknown"
        return None, reason, dynamic
    if not try_parse(expr):
        return None, "parse", dynamic
    return expr, None, dynamic


# --------------------------------------------------------------------------- extraction

CLASS_DECL = re.compile(
    r"^\s*public\s+(?:(?:sealed|abstract|partial)\s+)*class\s+(\w+)\s*:\s*([\w<>,\.\? ]+)",
    re.M,
)
DYNVAR_DECL = re.compile(r'new\s+DynamicVar\(\s*"([^"]+)"\s*,\s*(-?\d+(?:\.\d+)?)m?\s*\)')


def snake(name: str) -> str:
    name = re.sub(r"Power$", "", name)
    name = re.sub(r"(?<=[a-z0-9])([A-Z])", r"_\1", name)
    name = re.sub(r"(?<=[A-Z])([A-Z][a-z])", r"_\1", name)
    return name.lower()


def line_of(src: str, index: int) -> int:
    return src.count("\n", 0, index) + 1


def extract(name: str, src: str, rel: str) -> dict:
    cls = CLASS_DECL.search(src)
    class_name = cls.group(1) if cls else os.path.splitext(name)[0]
    base = " ".join(cls.group(2).split()) if cls else None
    if base:
        base = base.split(",")[0].strip()

    dynvars = {key: val for key, val in DYNVAR_DECL.findall(src)}
    # DynamicVars réécrites en combat (SlowAmount++ par exemple) : leur valeur initiale
    # n'est pas l'effet du pouvoir, on refuse de la figer.
    runtime_vars = {
        key
        for key in re.findall(
            r'DynamicVars\[\s*"([^"]+)"\s*\]\s*\.\s*BaseValue\s*(?:\+\+|--|\+=|-=|=)', src
        )
    }

    hooks: dict[str, str | None] = {}
    hook_sources: dict[str, str] = {}
    unresolved: dict[str, str] = {}
    dynamic_inputs: dict[str, list[str]] = {}
    hook_shapes: dict[str, dict] = {}

    for hook in HOOKS:
        decl = re.search(
            r"^[ \t]*(?:public|protected|internal)\s+override\s+([^\n(]+?)\b%s\s*\(([^)]*)\)\s*(?:\{|=>)"
            % hook,
            src,
            re.M,
        )
        if not decl:
            continue

        hook_line = line_of(src, decl.start())
        hook_sources[hook] = "%s:%d" % (rel, hook_line)

        if decl.group(0).rstrip().endswith("=>"):
            hooks[hook] = None
            unresolved[hook] = "expression_bodied"
            continue

        open_index = src.find("{", decl.end() - 1)
        close_index = match_brace(src, open_index)
        body = src[open_index + 1 : close_index]

        param_list = split_params(decl.group(2))
        baseline = next((pname for ptype, pname in param_list if ptype in NUM_TYPES), None)

        sites = return_sites(body)
        if not sites:
            hooks[hook] = None
            unresolved[hook] = "no_return"
            continue

        neutral = NEUTRAL.get(hook, set())
        candidates: list[tuple[str, list[str]]] = []
        terminal: str | None = None
        terminal_reason: str | None = None
        any_unresolved = False

        for offset, raw in sites:
            expr, reason, dynamic = normalize(raw, baseline, dynvars, runtime_vars, body)
            if reason:
                any_unresolved = True
                terminal_reason = reason
                continue
            assert expr is not None
            if expr in neutral:
                terminal = expr
                continue
            candidates.append((expr, dynamic))
            terminal = expr

        distinct = {expr for expr, _ in candidates}
        if any_unresolved:
            # au moins un `return` non réductible : on ne fabrique pas de valeur de repli
            hooks[hook] = None
            unresolved[hook] = terminal_reason or "unknown"
        elif not candidates:
            # uniquement des valeurs neutres : le pouvoir ne modifie pas ce hook
            hooks[hook] = terminal
        elif len(distinct) == 1:
            hooks[hook] = candidates[0][0]
            if candidates[0][1]:
                dynamic_inputs[hook] = candidates[0][1]
        else:
            # plusieurs valeurs possibles selon la branche : on garde la dernière et on le dit
            hooks[hook] = candidates[-1][0]
            unresolved[hook] = "conditional"
            if candidates[-1][1]:
                dynamic_inputs[hook] = candidates[-1][1]

        hook_shapes[hook] = {
            "returns": len(sites),
            "ifs": count_keyword(body, "if"),
            "switch": count_keyword(body, "switch"),
        }

    entry = {
        "id": snake(class_name),
        "class": class_name,
        "base": base,
        "file": rel,
        "hooks": hooks,
        "source": (hook_sources and min(hook_sources.values(), key=lambda s: int(s.rsplit(":", 1)[1])))
        or "%s:%d" % (rel, line_of(src, cls.start()) if cls else 1),
    }
    if hook_sources:
        entry["hook_sources"] = hook_sources
    if unresolved:
        entry["unresolved"] = unresolved
    if dynamic_inputs:
        entry["dynamic_inputs"] = dynamic_inputs
    if hook_shapes:
        entry["shape"] = hook_shapes
    return entry


# --------------------------------------------------------------------------- report

def report(powers: list[dict]) -> dict:
    by_hook: dict[str, Counter] = {hook: Counter() for hook in HOOKS}
    evaluable_powers = 0
    non_evaluable: list[tuple[str, str, str]] = []

    for power in powers:
        hooks = power["hooks"]
        if not hooks:
            continue
        unresolved = power.get("unresolved", {})
        ok = False
        for hook, expr in hooks.items():
            by_hook[hook]["overrides"] += 1
            if expr is not None:
                by_hook[hook]["evaluable"] += 1
                ok = True
            else:
                by_hook[hook]["unresolved"] += 1
        if ok:
            evaluable_powers += 1
        else:
            for hook, reason in sorted(unresolved.items()):
                non_evaluable.append((power["id"], hook, reason))

    return {
        "powers": len(powers),
        "powers_with_hook": sum(1 for p in powers if p["hooks"]),
        "powers_evaluable": evaluable_powers,
        "hooks_overridden": sum(c["overrides"] for c in by_hook.values()),
        "hooks_evaluable": sum(c["evaluable"] for c in by_hook.values()),
        "by_hook": {
            hook: dict(counter)
            for hook, counter in by_hook.items()
            if counter["overrides"]
        },
        "non_evaluable": non_evaluable,
    }


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Extrait Models/Powers/*.cs (ssh, lecture seule) vers data/catalog/powers.json"
    )
    parser.add_argument("--host", default=DEFAULT_HOST, help="machine de jeu (ssh, lecture seule)")
    parser.add_argument("--src", default=DEFAULT_SRC, help="dossier distant des Powers/*.cs")
    parser.add_argument("--src-dir", default=None, help="copie locale des *.cs (hors ligne)")
    parser.add_argument("--out", default="data/catalog", help="dossier de sortie")
    parser.add_argument("--show", type=int, default=20, help="lignes de détail non évaluables")
    args = parser.parse_args()

    if args.src_dir:
        sources = read_sources(args.src_dir)
        origin = "local:%s" % os.path.abspath(args.src_dir)
    else:
        sources = fetch_sources(args.host, args.src)
        origin = "%s:%s" % (args.host, args.src)

    powers = [extract(name, src, name) for name, src in sorted(sources.items())]
    powers.sort(key=lambda p: p["id"])

    dupes = [i for i, c in Counter(p["id"] for p in powers).items() if c > 1]
    if dupes:
        print("IDs en double : %s" % ", ".join(sorted(dupes)), file=sys.stderr)

    stats = report(powers)
    payload = {
        "schema": 1,
        "generated_by": "tools/index_powers.py",
        "source": origin,
        "hooks": HOOKS,
        "stats": {
            "powers": stats["powers"],
            "powers_with_hook": stats["powers_with_hook"],
            "powers_evaluable": stats["powers_evaluable"],
            "hooks_overridden": stats["hooks_overridden"],
            "hooks_evaluable": stats["hooks_evaluable"],
            "by_hook": stats["by_hook"],
        },
        "powers": powers,
    }

    outdir = os.path.abspath(args.out)
    os.makedirs(outdir, exist_ok=True)
    path = os.path.join(outdir, "powers.json")
    with open(path, "w", encoding="utf-8") as handle:
        json.dump(payload, handle, ensure_ascii=False, separators=(",", ":"))
        handle.write("\n")

    size = os.path.getsize(path)
    print("pouvoirs            : %d" % stats["powers"])
    print("avec au moins 1 hook: %d" % stats["powers_with_hook"])
    print("hooks surchargés    : %d (évaluables %d)" % (stats["hooks_overridden"], stats["hooks_evaluable"]))
    print("pouvoirs évaluables : %d" % stats["powers_evaluable"])
    for hook, counter in stats["by_hook"].items():
        print(
            "  %-28s %2d  évaluables %2d  non évaluables %2d"
            % (hook, counter.get("overrides", 0), counter.get("evaluable", 0), counter.get("unresolved", 0))
        )
    print("non évaluables      : %d" % len(stats["non_evaluable"]))
    for pid, hook, reason in stats["non_evaluable"][: args.show]:
        print("  %-32s %-28s %s" % (pid, hook, reason))
    print("écrit %s (%d octets)" % (path, size))
    if size > 500 * 1024:
        print("ATTENTION : %s dépasse 500 Ko" % path, file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
