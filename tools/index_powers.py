#!/usr/bin/env python3
"""Extrait les pouvoirs de Slay the Spire 2 (source C#) vers un catalogue déclaratif.

Le moteur ne code pas 260 classes : il lit `data/catalog/powers.json` et applique les
expressions des hooks. Ce script produit ce fichier en LECTURE SEULE depuis la machine de
jeu (ssh + tar sur stdout, aucun fichier écrit à distance).

    python3 tools/index_powers.py                     # ssh mio@100.123.83.40 (défaut)
    python3 tools/index_powers.py --src-dir /chemin   # copie locale (dev / hors ligne)
    python3 tools/index_powers.py --self-test         # vérifie l'extracteur sur des cas synthétiques

Ce qui est extrait, par pouvoir :
  id            nom de classe sans le suffixe `Power`, en snake_case (`DrawCardsNextTurnPower`
                -> `draw_cards_next_turn`)
  class, base   déclaration C# (`PowerModel`, `TemporaryStrengthPower`, ...)
  hooks         { hook: expression } pour les hooks du moteur (voir HOOKS_ENGINE / HOOKS_EXTENDED)
  evaluable     { hook: true|false } — l'expression est-elle applicable par ExpressionEval ?
  resolution    { hook: "code" | "manual" | "unresolved" } — d'où vient la valeur
  source        "fichier:ligne" de la première déclaration de hook (ou de la classe)

Règles d'extraction (déterministes, aucune invention) :

1. Un hook n'est retenu que s'il est déclaré au niveau de la classe (`public`/`protected`/
   `internal`, `override` optionnel : `CrueltyPower.ModifyVulnerableMultiplier` ne porte pas
   le mot-clé mais surcharge bien le hook de `PowerModel`).
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
5. Les constantes littérales de la classe sont substituées avant normalisation :
   `private const decimal damageDecrease = 30m;` puis `return (100m - damageDecrease) / 100m`
   donne `0.7`. Une constante non littérale n'est jamais substituée.
6. Les `DynamicVar` dont la valeur par défaut est un littéral sont lues dans `CanonicalVars`,
   dans un constructeur sans paramètre (`DynamicVars["X"].BaseValue = 5m;`) ou via
   `SetVar("X", 5m)`. Une `DynamicVar` réécrite en combat (hors constructeur : `BaseValue++`,
   `= 0m` dans un hook de tour) n'est PAS figée : son hook sort en `dynamicvar_runtime`.
7. Les expressions sans `Amount`/`In` sont pliées (`50 / 100` -> `0.5`,
   `(100 - 30) / 100` -> `0.7`) : même valeur, forme canonique.
8. Tout ce qui n'est pas réductible à ce langage donne `null` + une raison dans `unresolved`
   (`call` = appel de fonction, `state` = état de combat, `local` = variable locale non
   réductible, `dynamicvar` = DynamicVar sans littéral, `dynamicvar_runtime` = DynamicVar
   réécrite en combat, `unbounded` = decimal.MaxValue). Aucune valeur par défaut n'est
   inventée : `null` veut dire « non évaluable ».
9. La table manuelle `data/catalog/power_constants.json` ne comble QUE les trous : une ligne
   `{power, hook, value_expression, source}` avec une ligne de code citée donne
   `resolution: "manual"`. Si `value_expression` est dans la grammaire, elle est injectée
   comme expression (`evaluable: true`) ; sinon la valeur reste documentée mais non
   machine-évaluable (`evaluable: false`). Une ligne manuelle ne remplace jamais un `code`.

Sortie : `data/catalog/powers.json`, compacte (< 100 Ko) pour tenir dans le budget de
données commitées (2 Mio, tests de packaging), + la mesure de couverture et les 20 pouvoirs
non résolus les plus utilisés (cartes qui l'appliquent + monstres qui l'appliquent).
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
from decimal import Decimal, DivisionByZero, InvalidOperation

DEFAULT_HOST = "mio@100.123.83.40"
DEFAULT_SRC = "~/sts2/game/pck/src/Core/Models/Powers"
DEFAULT_MONSTERS = "~/sts2/game/pck/src/Core/Models/Monsters"

# Hooks du moteur (ceux que les consommateurs de PowerCatalog connaissent).
HOOKS_ENGINE = [
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

# Hooks présents dans PowerModel, de même forme (entrée décimale -> valeur décimale) et qui
# changent un classement mais n'étaient pas extraits :
#   ModifyVulnerableMultiplier / ModifyWeakMultiplier : Cruelty, Debilitate (multiplicateurs
#   de dégâts indirects) ; ModifyEnergyGain : NoEnergyGain (énergie).
HOOKS_EXTENDED = [
    "ModifyVulnerableMultiplier",
    "ModifyWeakMultiplier",
    "ModifyEnergyGain",
]

HOOKS = HOOKS_ENGINE + HOOKS_EXTENDED

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
    "ModifyVulnerableMultiplier": {"In"},
    "ModifyWeakMultiplier": {"In"},
    "ModifyEnergyGain": {"In"},
}

NUM_TYPES = ("decimal", "int", "float", "double")

RESOLUTIONS = ("code", "manual", "unresolved")


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


def fetch_monster_power_uses(host: str, src: str) -> dict[str, set[str]] | None:
    """{classe de pouvoir: {monstres}} d'après `Apply<XPower>` dans Monsters/*.cs.

    `None` si la source des monstres n'est pas joignable (mode hors ligne) : la mesure le
    dit, elle n'invente pas de zéro.
    """
    remote = src.replace("~", "$HOME", 1) if src.startswith("~") else src
    cmd = f"cd {remote} && grep -oHE 'Apply<[A-Za-z0-9_]+Power>' *.cs"
    out = subprocess.run(
        ["ssh", "-o", "BatchMode=yes", "-o", "ConnectTimeout=15", host, cmd],
        capture_output=True,
    )
    if out.returncode != 0:
        return None
    uses: dict[str, set[str]] = {}
    for line in out.stdout.decode("utf-8", "replace").splitlines():
        match = re.fullmatch(r"([A-Za-z0-9_]+\.cs):Apply<([A-Za-z0-9_]+Power)>", line.strip())
        if match:
            uses.setdefault(match.group(2), set()).add(match.group(1))
    return uses


# --------------------------------------------------------------------------- scanning

def skip_literal(src: str, i: int) -> int:
    """i pointe sur \" ou ' — rend l'index juste après la chaîne."""
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

# Constantes littérales de la classe : `private const decimal damageDecrease = 30m;`
CONST_DECL = re.compile(
    r"(?:public|private|protected|internal)\s+(?:static\s+)?const\s+"
    r"(?:decimal|int|float|double)\s+([A-Za-z_]\w*)\s*=\s*(-?\d+(?:\.\d+)?)[mMfFdD]?\s*;"
)
# `SetVar("X", 5m)` (forme utilitaire) et affectation littérale dans un constructeur.
SETVAR_DECL = re.compile(r'SetVar\(\s*"([^"]+)"\s*,\s*(-?\d+(?:\.\d+)?)[mMfFdD]?\s*\)')
CTOR_BASEVALUE = re.compile(
    r'DynamicVars\[\s*"([^"]+)"\s*\]\s*\.\s*BaseValue\s*=\s*(-?\d+(?:\.\d+)?)[mMfFdD]?\s*;'
)
CTOR_DECL = re.compile(
    r"^[ \t]*(?:public|private|protected|internal)\s+(?:static\s+)?([A-Za-z_]\w*)\s*\(\s*\)",
    re.M,
)

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


def eval_constants(expr: str) -> Decimal | None:
    """Évalue une expression SANS `Amount`/`In` (même grammaire). None si non numérique."""
    if "Amount" in expr or "In" in expr:
        return None
    toks = tokens(expr)
    if not toks:
        return None
    pos = 0

    def peek() -> str | None:
        return toks[pos] if pos < len(toks) else None

    def expr_node() -> Decimal | None:
        nonlocal pos
        left = term()
        if left is None:
            return None
        while peek() in ("+", "-"):
            op = peek()
            pos += 1
            right = term()
            if right is None:
                return None
            left = left + right if op == "+" else left - right
        return left

    def term() -> Decimal | None:
        nonlocal pos
        left = factor()
        if left is None:
            return None
        while peek() in ("*", "/"):
            op = peek()
            pos += 1
            right = factor()
            if right is None:
                return None
            if op == "*":
                left = left * right
            else:
                if right == 0:
                    return None
                left = left / right
        return left

    def factor() -> Decimal | None:
        nonlocal pos
        tok = peek()
        if tok is None:
            return None
        if tok in ("+", "-"):
            pos += 1
            inner = factor()
            if inner is None:
                return None
            return -inner if tok == "-" else inner
        if tok == "(":
            pos += 1
            inner = expr_node()
            if inner is None:
                return None
            if peek() != ")":
                return None
            pos += 1
            return inner
        if re.fullmatch(r"\d+(?:\.\d+)?", tok):
            pos += 1
            return Decimal(tok)
        return None

    try:
        value = expr_node()
    except (DivisionByZero, InvalidOperation):
        return None
    if value is None or pos != len(toks):
        return None
    return value


def format_decimal(value: Decimal) -> str:
    """`Decimal('0.5')` -> `0.5`, `Decimal('2')` -> `2` (jamais `2.0`)."""
    normalized = value.normalize()
    if normalized == normalized.to_integral_value():
        return str(normalized.quantize(Decimal(1)))
    return format(normalized, "f")


def collect_constants(src: str) -> dict[str, str]:
    """Constantes littérales substituables. Une constante non littérale n'entre pas."""
    return {name: value for name, value in CONST_DECL.findall(src)}


def ctor_bodies(src: str) -> list[tuple[int, int]]:
    """[(début, fin)] des corps de constructeurs sans paramètre."""
    spans: list[tuple[int, int]] = []
    for match in CTOR_DECL.finditer(src):
        brace = src.find("{", match.end())
        if brace < 0:
            continue
        between = src[match.end() : brace]
        if between.strip() and not re.fullmatch(r"\s*:\s*base\([^)]*\)\s*", between):
            continue
        spans.append((brace + 1, match_brace(src, brace)))
    return spans


def collect_ctor_dynvars(src: str) -> dict[str, str]:
    """DynamicVars dont la valeur par défaut littérale est posée dans un constructeur.

    On ne regarde QUE les corps de constructeurs sans paramètre (et `SetVar`, qui est une
    déclaration) : une affectation dans un hook de tour est un état de combat, pas un défaut.
    """
    out: dict[str, str] = {key: val for key, val in SETVAR_DECL.findall(src)}
    for start, end in ctor_bodies(src):
        for key, val in CTOR_BASEVALUE.findall(src[start:end]):
            out.setdefault(key, val)
    return out


def runtime_dynvars(src: str) -> set[str]:
    """DynamicVars réécrites en combat — hors constructeur (un défaut de ctor n'est pas un état)."""
    masked = src
    for start, end in sorted(ctor_bodies(src), reverse=True):
        masked = masked[:start] + " " * (end - start) + masked[end:]
    return {
        key
        for key in re.findall(
            r'DynamicVars\[\s*"([^"]+)"\s*\]\s*\.\s*BaseValue\s*(?:\+\+|--|\+=|-=|=)', masked
        )
    }


def substitute_outside_strings(expr: str, mapping: dict[str, str]) -> str:
    """Remplace les identifiants de `mapping` hors chaînes littérales."""
    if not mapping:
        return expr
    pieces: list[str] = []
    i = 0
    start = 0
    while i < len(expr):
        if expr[i] in "\"'":
            pieces.append(_sub(expr[start:i], mapping))
            end = skip_literal(expr, i)
            pieces.append(expr[i:end])
            i = end
            start = end
            continue
        i += 1
    pieces.append(_sub(expr[start:], mapping))
    return "".join(pieces)


def _sub(text: str, mapping: dict[str, str]) -> str:
    for name, value in sorted(mapping.items(), key=lambda kv: -len(kv[0])):
        text = re.sub(r"\b%s\b" % re.escape(name), value, text)
    return text


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
    constants: dict[str, str] | None = None,
) -> tuple[str | None, str | None, list[str]]:
    """Rend (expression normalisée | None, raison | None, dynamic_inputs)."""
    expr = raw.strip()

    if re.fullmatch(r"decimal\.MaxValue", expr):
        # « pas de plafond » : valeur neutre pour ModifyDamageCap, pas une expression évaluable
        return "MaxValue", None, []

    # constantes littérales de la classe (avant les variables locales : une const n'est pas
    # une variable de méthode)
    expr = substitute_outside_strings(expr, constants or {})

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

    # pliage des constantes : `50 / 100` -> `0.5` (même valeur, forme canonique)
    folded = eval_constants(expr)
    if folded is not None:
        expr = format_decimal(folded)
    return expr, None, dynamic


# --------------------------------------------------------------------------- extraction

CLASS_DECL = re.compile(
    r"^\s*public\s+(?:(?:sealed|abstract|partial)\s+)*class\s+(\w+)\s*:\s*([\w<>,\? ]+)",
    re.M,
)
DYNVAR_DECL = re.compile(
    r'new\s+DynamicVar\(\s*"([^"]+)"\s*,\s*(-?\d+(?:\.\d+)?)[mMfFdD]?\s*\)'
)


def snake(name: str) -> str:
    name = re.sub(r"Power$", "", name)
    name = re.sub(r"(?<=[a-z0-9])([A-Z])", r"_\1", name)
    name = re.sub(r"(?<=[A-Z])([A-Z][a-z])", r"_\1", name)
    return name.lower()


def line_of(src: str, index: int) -> int:
    return src.count("\n", 0, index) + 1


def hook_declaration(src: str, hook: str) -> re.Match[str] | None:
    """Déclaration du hook au niveau classe (`override` optionnel).

    `CrueltyPower` / `DebilitatePower` déclarent `public decimal ModifyVulnerableMultiplier`
    sans `override` : le hook est bien surchargé, il doit être extrait comme les autres.
    """
    pattern = (
        r"^[ \t]*(?:public|protected|internal)\s+(?:override\s+)?"
        r"([^\n(]+?)\b%s\s*\(([^)]*)\)\s*(?:\{|=>)" % hook
    )
    return re.search(pattern, src, re.M)


def extract(
    name: str,
    src: str,
    rel: str,
    manual: dict[tuple[str, str], dict] | None = None,
) -> dict:
    cls = CLASS_DECL.search(src)
    class_name = cls.group(1) if cls else os.path.splitext(name)[0]
    base = " ".join(cls.group(2).split()) if cls else None
    if base:
        base = base.split(",")[0].strip()

    dynvars = {key: val for key, val in DYNVAR_DECL.findall(src)}
    # défauts posés dans un constructeur / SetVar (littéraux seulement)
    for key, val in collect_ctor_dynvars(src).items():
        dynvars.setdefault(key, val)
    constants = collect_constants(src)
    # DynamicVars réécrites en combat (SlowAmount++ par exemple) : leur valeur initiale
    # n'est pas l'effet du pouvoir, on refuse de la figer.
    runtime_vars = runtime_dynvars(src)

    hooks: dict[str, str | None] = {}
    hook_sources: dict[str, str] = {}
    evaluable: dict[str, bool] = {}
    resolution: dict[str, str] = {}
    manual_sources: dict[str, str] = {}
    unresolved: dict[str, str] = {}
    dynamic_inputs: dict[str, list[str]] = {}
    hook_shapes: dict[str, dict] = {}

    for hook in HOOKS:
        decl = hook_declaration(src, hook)
        if not decl:
            continue

        hook_line = line_of(src, decl.start())
        hook_sources[hook] = "%s:%d" % (rel, hook_line)

        if decl.group(0).rstrip().endswith("=>"):
            hooks[hook] = None
            evaluable[hook] = False
            resolution[hook] = "unresolved"
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
            evaluable[hook] = False
            resolution[hook] = "unresolved"
            unresolved[hook] = "no_return"
            continue

        neutral = NEUTRAL.get(hook, set())
        candidates: list[tuple[str, list[str]]] = []
        terminal: str | None = None
        terminal_reason: str | None = None
        any_unresolved = False

        for offset, raw in sites:
            expr, reason, dynamic = normalize(raw, baseline, dynvars, runtime_vars, body, constants)
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
            evaluable[hook] = False
            resolution[hook] = "unresolved"
            unresolved[hook] = terminal_reason or "unknown"
        elif not candidates:
            # uniquement des valeurs neutres : le pouvoir ne modifie pas ce hook
            hooks[hook] = terminal
            evaluable[hook] = terminal is not None
            resolution[hook] = "code" if terminal is not None else "unresolved"
            if terminal is None:
                unresolved[hook] = "no_return"
        elif len(distinct) == 1:
            hooks[hook] = candidates[0][0]
            evaluable[hook] = True
            resolution[hook] = "code"
            if candidates[0][1]:
                dynamic_inputs[hook] = candidates[0][1]
        else:
            # plusieurs valeurs possibles selon la branche : on garde la dernière et on le dit
            hooks[hook] = candidates[-1][0]
            evaluable[hook] = True
            resolution[hook] = "code"
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
        "evaluable": evaluable,
        "resolution": resolution,
        "source": (hook_sources and min(hook_sources.values(), key=lambda s: int(s.rsplit(":", 1)[1])))
        or "%s:%d" % (rel, line_of(src, cls.start()) if cls else 1),
    }

    # table manuelle : comble les trous, ne remplace jamais `code`
    for (power_id, hook), row in (manual or {}).items():
        if power_id != entry["id"] or hook not in hooks:
            continue
        if resolution.get(hook) == "code":
            continue
        expression = str(row.get("value_expression", "")).strip()
        resolution[hook] = "manual"
        manual_sources[hook] = row["source"]
        if expression and try_parse(expression):
            hooks[hook] = expression
            evaluable[hook] = True
            unresolved.pop(hook, None)
        else:
            hooks[hook] = None
            evaluable[hook] = False

    if hook_sources:
        entry["hook_sources"] = hook_sources
    if manual_sources:
        entry["manual_sources"] = manual_sources
    if unresolved:
        entry["unresolved"] = unresolved
    if dynamic_inputs:
        entry["dynamic_inputs"] = dynamic_inputs
    if hook_shapes:
        entry["shape"] = hook_shapes
    return entry


def load_manual(path: str | None) -> tuple[dict[tuple[str, str], dict], dict, list[str]]:
    """Charge `power_constants.json` -> (index (power, hook), bloc sets, avertissements)."""
    warnings: list[str] = []
    if not path or not os.path.exists(path):
        if path:
            warnings.append("table manuelle absente : %s" % path)
        return {}, {}, warnings
    with open(path, encoding="utf-8") as handle:
        payload = json.load(handle)
    rows = payload.get("constants") or []
    index: dict[tuple[str, str], dict] = {}
    for row in rows:
        power = str(row.get("power", "")).strip()
        hook = str(row.get("hook", "")).strip()
        source = str(row.get("source", "")).strip()
        if not power or not hook:
            warnings.append("ligne manuelle sans power/hook ignorée")
            continue
        if not re.fullmatch(r"[A-Za-z0-9_.]+\.cs:\d+", source):
            warnings.append("source non citée (%s.%s) : ligne ignorée" % (power, hook))
            continue
        key = (power, hook)
        if key in index:
            warnings.append("ligne manuelle en double : %s.%s" % (power, hook))
            continue
        index[key] = row
    return index, payload.get("sets") or {}, warnings


# --------------------------------------------------------------------------- usage

def normalize_power_name(name: str) -> str:
    """`Biased Cognition` / `BiasedCognitionPower` -> `biased_cognition`."""
    name = re.sub(r"Power$", "", name.strip())
    name = re.sub(r"[^A-Za-z0-9]+", "_", name)
    name = re.sub(r"(?<=[a-z0-9])([A-Z])", r"_\1", name)
    name = re.sub(r"(?<=[A-Z])([A-Z][a-z])", r"_\1", name)
    return name.strip("_").lower()


def card_usage(path: str) -> dict[str, int]:
    if not os.path.exists(path):
        return {}
    with open(path, encoding="utf-8") as handle:
        payload = json.load(handle)
    counts: Counter[str] = Counter()
    for card in payload.get("cards") or []:
        for name in card.get("powers") or []:
            counts[normalize_power_name(name)] += 1
    return dict(counts)


def monster_usage(uses: dict[str, set[str]] | None) -> dict[str, int]:
    if uses is None:
        return {}
    counts: Counter[str] = Counter()
    for class_name, monsters in uses.items():
        counts[normalize_power_name(class_name)] += len(monsters)
    return dict(counts)


# --------------------------------------------------------------------------- report

def power_status(power: dict) -> str:
    """Statut du pouvoir : `code` > `manual` > `unresolved`."""
    resolutions = set((power.get("resolution") or {}).values())
    if "code" in resolutions:
        return "code"
    if "manual" in resolutions:
        return "manual"
    return "unresolved"


def report(powers: list[dict], usage: dict[str, dict[str, int]] | None = None) -> dict:
    by_hook: dict[str, Counter] = {hook: Counter() for hook in HOOKS}
    statuses = Counter(power_status(p) for p in powers)
    non_evaluable: list[tuple[str, str, str]] = []

    for power in powers:
        hooks = power["hooks"]
        if not hooks:
            continue
        resolution = power.get("resolution") or {}
        unresolved = power.get("unresolved", {})
        for hook, expr in hooks.items():
            by_hook[hook]["overrides"] += 1
            res = resolution.get(hook, "code" if expr is not None else "unresolved")
            by_hook[hook][res] += 1
            if res == "code" and expr is None:
                by_hook[hook]["inconsistent"] += 1
        if power_status(power) == "unresolved":
            if not hooks:
                non_evaluable.append((power["id"], "-", "no_hook"))
            else:
                for hook, reason in sorted(unresolved.items()):
                    non_evaluable.append((power["id"], hook, reason))

    usage = usage or {}
    ranking = []
    for power in powers:
        if power_status(power) != "unresolved":
            continue
        counts = usage.get(power["id"]) or {}
        ranking.append(
            {
                "power": power["id"],
                "cards": int(counts.get("cards", 0)),
                "monsters": int(counts.get("monsters", 0)),
            }
        )
    ranking.sort(key=lambda row: (-(row["cards"] + row["monsters"]), row["power"]))

    return {
        "powers": len(powers),
        "powers_with_hook": sum(1 for p in powers if p["hooks"]),
        "powers_code": statuses.get("code", 0),
        "powers_manual": statuses.get("manual", 0),
        "powers_unresolved": statuses.get("unresolved", 0),
        "powers_covered": statuses.get("code", 0) + statuses.get("manual", 0),
        # compat : « au moins une expression non nulle dans powers.json »
        "powers_evaluable": sum(
            1 for p in powers if any(v for v in (p.get("hooks") or {}).values())
        ),
        "hooks_overridden": sum(c["overrides"] for c in by_hook.values()),
        "hooks_code": sum(c["code"] for c in by_hook.values()),
        "hooks_manual": sum(c["manual"] for c in by_hook.values()),
        "hooks_unresolved": sum(c["unresolved"] for c in by_hook.values()),
        "by_hook": {
            hook: dict(counter)
            for hook, counter in by_hook.items()
            if counter["overrides"]
        },
        "non_evaluable": non_evaluable,
        "unresolved_ranking": ranking,
    }


# --------------------------------------------------------------------------- self-test

SELF_TEST_SOURCES = {
    "ConstPower.cs": """
public sealed class ConstPower : PowerModel
{
\tprivate const decimal damageDecrease = 30m;

\tpublic override decimal ModifyDamageMultiplicative(Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
\t{
\t\tif (target != base.Owner)
\t\t{
\t\t\treturn 1m;
\t\t}
\t\treturn (100m - damageDecrease) / 100m;
\t}
}
""",
    "CtorVarPower.cs": """
public sealed class CtorVarPower : PowerModel
{
\tpublic CtorVarPower()
\t{
\t\tbase.DynamicVars["Steps"].BaseValue = 5m;
\t}

\tpublic override decimal ModifyDamageMultiplicative(Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
\t{
\t\treturn 1m + base.DynamicVars["Steps"].BaseValue * base.Amount / 100m;
\t}
}
""",
    "SimplePower.cs": """
public sealed class SimplePower : PowerModel
{
\tpublic override decimal ModifyDamageMultiplicative(Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
\t{
\t\treturn 1m + (decimal)base.Amount / 100m;
\t}
}
""",
    "CallPower.cs": """
public sealed class CallPower : PowerModel
{
\tpublic override decimal ModifyBlockMultiplicative(Creature target, decimal block, ValueProp props, CardModel? cardSource, CardPlay? cardPlay)
\t{
\t\treturn (decimal)Math.Pow(2.0, base.Amount);
\t}
}
""",
    "IndirectPower.cs": """
public sealed class IndirectPower : PowerModel
{
\tpublic decimal ModifyVulnerableMultiplier(Creature target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
\t{
\t\treturn amount + (decimal)base.Amount / 100m;
\t}
}
""",
}


def self_test() -> int:
    """Vérifie les capacités d'extraction sur des sources synthétiques (pas des valeurs de jeu)."""
    failures: list[str] = []

    def check(file: str, hook: str, expected: str | None) -> None:
        entry = extract(file, SELF_TEST_SOURCES[file], file)
        actual = entry["hooks"].get(hook)
        if actual != expected:
            failures.append("%s.%s = %r (attendu %r)" % (file, hook, actual, expected))

    check("ConstPower.cs", "ModifyDamageMultiplicative", "0.7")
    check("CtorVarPower.cs", "ModifyDamageMultiplicative", "1 + 5 * Amount / 100")
    check("SimplePower.cs", "ModifyDamageMultiplicative", "1 + Amount / 100")
    check("CallPower.cs", "ModifyBlockMultiplicative", None)
    check("IndirectPower.cs", "ModifyVulnerableMultiplier", "In + Amount / 100")

    call = extract("CallPower.cs", SELF_TEST_SOURCES["CallPower.cs"], "CallPower.cs")
    if call["unresolved"].get("ModifyBlockMultiplicative") != "call":
        failures.append("CallPower.cs: raison attendue `call`, obtenu %r" % call["unresolved"])

    indirect = extract(
        "IndirectPower.cs", SELF_TEST_SOURCES["IndirectPower.cs"], "IndirectPower.cs"
    )
    if indirect["resolution"].get("ModifyVulnerableMultiplier") != "code":
        failures.append("IndirectPower.cs: hook sans `override` non extrait")

    if failures:
        for failure in failures:
            print("SELF-TEST FAIL  %s" % failure, file=sys.stderr)
        return 1
    print("SELF-TEST PASS  5 cas (constante, ctor, 1+Amount/100, appel, hook sans override)")
    return 0


# --------------------------------------------------------------------------- main

def main() -> int:
    parser = argparse.ArgumentParser(
        description="Extrait Models/Powers/*.cs (ssh, lecture seule) vers data/catalog/powers.json"
    )
    parser.add_argument("--host", default=DEFAULT_HOST, help="machine de jeu (ssh, lecture seule)")
    parser.add_argument("--src", default=DEFAULT_SRC, help="dossier distant des Powers/*.cs")
    parser.add_argument("--src-dir", default=None, help="copie locale des *.cs (hors ligne)")
    parser.add_argument("--out", default="data/catalog", help="dossier de sortie")
    parser.add_argument(
        "--manual", default=None, help="table manuelle (défaut : <out>/power_constants.json)"
    )
    parser.add_argument(
        "--cards",
        default=None,
        help="cards_effects.json pour la mesure d'usage (défaut : <out>/cards_effects.json)",
    )
    parser.add_argument("--show", type=int, default=20, help="lignes de détail non évaluables")
    parser.add_argument("--self-test", action="store_true", help="teste l'extracteur sur des cas synthétiques")
    parser.add_argument("--quiet", action="store_true", help="pas de rapport")
    args = parser.parse_args()

    if args.self_test:
        return self_test()

    if args.src_dir:
        sources = read_sources(args.src_dir)
        origin = "local:%s" % os.path.abspath(args.src_dir)
        monster_uses = None
    else:
        sources = fetch_sources(args.host, args.src)
        origin = "%s:%s" % (args.host, args.src)
        monster_uses = fetch_monster_power_uses(args.host, DEFAULT_MONSTERS)

    manual_path = args.manual or os.path.join(args.out, "power_constants.json")
    manual_index, manual_sets, manual_warnings = load_manual(manual_path)
    for warning in manual_warnings:
        print("ATTENTION : %s" % warning, file=sys.stderr)

    powers = [extract(name, src, name, manual_index) for name, src in sorted(sources.items())]
    powers.sort(key=lambda p: p["id"])
    by_id = {p["id"]: p for p in powers}

    for (power_id, hook), row in sorted(manual_index.items()):
        if power_id not in by_id:
            print("ATTENTION : ligne manuelle pour un pouvoir inconnu : %s" % power_id, file=sys.stderr)
        elif hook not in by_id[power_id]["hooks"]:
            print(
                "ATTENTION : ligne manuelle pour un hook non surchargé : %s.%s" % (power_id, hook),
                file=sys.stderr,
            )

    dupes = [i for i, c in Counter(p["id"] for p in powers).items() if c > 1]
    if dupes:
        print("IDs en double : %s" % ", ".join(sorted(dupes)), file=sys.stderr)

    cards_path = args.cards or os.path.join(args.out, "cards_effects.json")
    usage: dict[str, dict[str, int]] = {
        power_id: {"cards": count, "monsters": 0} for power_id, count in card_usage(cards_path).items()
    }
    for power_id, count in monster_usage(monster_uses).items():
        row = usage.setdefault(power_id, {"cards": 0, "monsters": 0})
        row["monsters"] = count

    stats = report(powers, usage)
    payload = {
        "schema": 2,
        "generated_by": "tools/index_powers.py",
        "source": origin,
        "hooks": HOOKS,
        "hooks_engine": HOOKS_ENGINE,
        "hooks_extended": HOOKS_EXTENDED,
        "manual_table": os.path.basename(manual_path) if manual_index else None,
        "monsters_source": "scanned" if monster_uses is not None else "unavailable",
        "stats": {
            "powers": stats["powers"],
            "powers_with_hook": stats["powers_with_hook"],
            "powers_code": stats["powers_code"],
            "powers_manual": stats["powers_manual"],
            "powers_unresolved": stats["powers_unresolved"],
            "powers_covered": stats["powers_covered"],
            "powers_evaluable": stats["powers_evaluable"],
            "hooks_overridden": stats["hooks_overridden"],
            "hooks_code": stats["hooks_code"],
            "hooks_manual": stats["hooks_manual"],
            "hooks_unresolved": stats["hooks_unresolved"],
            "by_hook": stats["by_hook"],
            "unresolved_ranking": stats["unresolved_ranking"],
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
    if args.quiet:
        return 0 if size <= 500 * 1024 else 1

    print("pouvoirs            : %d" % stats["powers"])
    print("avec au moins 1 hook: %d" % stats["powers_with_hook"])
    print(
        "hooks surchargés    : %d (code %d, manual %d, unresolved %d)"
        % (
            stats["hooks_overridden"],
            stats["hooks_code"],
            stats["hooks_manual"],
            stats["hooks_unresolved"],
        )
    )
    print(
        "pouvoirs            : code %d, manual %d, unresolved %d (couverts %d/%d)"
        % (
            stats["powers_code"],
            stats["powers_manual"],
            stats["powers_unresolved"],
            stats["powers_covered"],
            stats["powers"],
        )
    )
    for hook, counter in stats["by_hook"].items():
        print(
            "  %-30s %2d  code %2d  manual %2d  unresolved %2d"
            % (
                hook,
                counter.get("overrides", 0),
                counter.get("code", 0),
                counter.get("manual", 0),
                counter.get("unresolved", 0),
            )
        )
    print("non évaluables      : %d" % len(stats["non_evaluable"]))
    for pid, hook, reason in stats["non_evaluable"][: args.show]:
        print("  %-32s %-28s %s" % (pid, hook, reason))
    manual_rows = [
        (power["id"], hook, power.get("manual_sources", {}).get(hook, "?"))
        for power in powers
        for hook, res in (power.get("resolution") or {}).items()
        if res == "manual"
    ]
    print("documentés à la main : %d" % len(manual_rows))
    for pid, hook, source in manual_rows[: args.show]:
        print("  %-32s %-28s %s" % (pid, hook, source))
    print("\n20 non résolus les plus utilisés (cartes / monstres) :")
    for row in stats["unresolved_ranking"][: args.show]:
        print("  %-32s cartes %2d  monstres %2d" % (row["power"], row["cards"], row["monsters"]))
    print("écrit %s (%d octets)" % (path, size))
    if size > 500 * 1024:
        print("ATTENTION : %s dépasse 500 Ko" % path, file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
