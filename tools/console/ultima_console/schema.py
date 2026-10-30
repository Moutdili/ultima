"""Validation JSON Schema (sous-ensemble draft 2020-12 utilisé par `schemas/*.schema.json`), bibliothèque standard.

La CLI n'a pas de dépendance hors stdlib : ce validateur couvre exactement les mots-clés des schémas du contrat
(type, required, properties, additionalProperties, propertyNames, enum, const, pattern, minLength, maxLength,
minimum, maximum, items, minItems, maxItems, uniqueItems, oneOf). Un mot-clé inconnu fait échouer le test
`test_schema_keywords_are_supported` : un schéma futur ne peut pas être validé à moitié en silence.
Les tests comparent ce validateur à la bibliothèque `jsonschema` (dépendance de test seulement).

Où sont les schémas : `$ULTIMA_SCHEMAS`, sinon `<racine>/schemas` (dépôt ou installation, voir paths.share_root).
"""

from __future__ import annotations

import json
import re
from functools import lru_cache
from pathlib import Path
from typing import Any

from . import paths

SUPPORTED = {"$schema", "$id", "title", "description", "default", "type", "required", "properties",
             "additionalProperties", "propertyNames", "enum", "const", "pattern", "minLength", "maxLength",
             "minimum", "maximum", "items", "minItems", "maxItems", "uniqueItems", "oneOf"}
NAMES = ("theme", "module", "plugin", "plugin-output", "appearance")


class SchemaMissing(Exception):
    pass


def schema_path(name: str) -> Path:
    return paths.schemas_dir() / f"{name}.schema.json"


@lru_cache(maxsize=16)
def _load_cached(path: str, mtime: float) -> dict:
    return json.loads(Path(path).read_text(encoding="utf-8"))


def load(name: str) -> dict:
    p = schema_path(name)
    try:
        return _load_cached(str(p), p.stat().st_mtime)
    except (OSError, ValueError) as e:
        raise SchemaMissing(f"schéma {name} introuvable ou illisible ({p}) : {e}") from e


def _type_ok(value: Any, t: str) -> bool:
    if t == "object":
        return isinstance(value, dict)
    if t == "array":
        return isinstance(value, list)
    if t == "string":
        return isinstance(value, str)
    if t == "integer":
        return isinstance(value, int) and not isinstance(value, bool) or (
            isinstance(value, float) and value.is_integer())
    if t == "number":
        return isinstance(value, (int, float)) and not isinstance(value, bool)
    if t == "boolean":
        return isinstance(value, bool)
    if t == "null":
        return value is None
    return False


def _eq(a: Any, b: Any) -> bool:
    if isinstance(a, bool) or isinstance(b, bool):
        return type(a) is type(b) and a == b
    return a == b


def _fmt(path: str) -> str:
    return path or "(racine)"


def validate(value: Any, schema: dict, path: str = "") -> list[str]:
    """Liste des erreurs lisibles (vide = valide). `path` = chemin JSON (« rows[0].tone »)."""
    errs: list[str] = []
    if not isinstance(schema, dict):
        return errs
    t = schema.get("type")
    if t is not None:
        types = t if isinstance(t, list) else [t]
        if not any(_type_ok(value, x) for x in types):
            return [f"{_fmt(path)} : type {'/'.join(types)} attendu"]
    if "const" in schema and not _eq(value, schema["const"]):
        errs.append(f"{_fmt(path)} : doit valoir {json.dumps(schema['const'], ensure_ascii=False)}")
    if "enum" in schema and not any(_eq(value, e) for e in schema["enum"]):
        allowed = ", ".join(str(e) for e in schema["enum"])
        errs.append(f"{_fmt(path)} : « {value} » hors des valeurs permises ({allowed})")
    if isinstance(value, str):
        if "minLength" in schema and len(value) < schema["minLength"]:
            errs.append(f"{_fmt(path)} : au moins {schema['minLength']} caractère(s)")
        if "maxLength" in schema and len(value) > schema["maxLength"]:
            errs.append(f"{_fmt(path)} : au plus {schema['maxLength']} caractères ({len(value)})")
        if "pattern" in schema and not re.search(schema["pattern"], value):
            errs.append(f"{_fmt(path)} : « {value} » ne respecte pas le format {schema['pattern']}")
    if isinstance(value, (int, float)) and not isinstance(value, bool):
        if "minimum" in schema and value < schema["minimum"]:
            errs.append(f"{_fmt(path)} : minimum {schema['minimum']} ({value})")
        if "maximum" in schema and value > schema["maximum"]:
            errs.append(f"{_fmt(path)} : maximum {schema['maximum']} ({value})")
    if isinstance(value, list):
        if "minItems" in schema and len(value) < schema["minItems"]:
            errs.append(f"{_fmt(path)} : au moins {schema['minItems']} élément(s)")
        if "maxItems" in schema and len(value) > schema["maxItems"]:
            errs.append(f"{_fmt(path)} : au plus {schema['maxItems']} éléments ({len(value)})")
        if schema.get("uniqueItems"):
            seen: list[str] = []
            for v in value:
                k = json.dumps(v, sort_keys=True)
                if k in seen:
                    errs.append(f"{_fmt(path)} : élément en double ({v})")
                    break
                seen.append(k)
        if isinstance(schema.get("items"), dict):
            for i, v in enumerate(value):
                errs.extend(validate(v, schema["items"], f"{path}[{i}]"))
    if isinstance(value, dict):
        for r in schema.get("required", []):
            if r not in value:
                errs.append(f"{_fmt(path)} : champ « {r} » obligatoire")
        props = schema.get("properties", {})
        pn = schema.get("propertyNames")
        addl = schema.get("additionalProperties", True)
        for k, v in value.items():
            sub = f"{path}.{k}" if path else str(k)
            if isinstance(pn, dict):
                pe = validate(k, pn, sub)
                if pe:
                    errs.append(f"{sub} : clé inconnue")
                    continue
            if k in props:
                errs.extend(validate(v, props[k], sub))
            elif addl is False:
                errs.append(f"{sub} : clé inconnue (non permise ici)")
            elif isinstance(addl, dict):
                errs.extend(validate(v, addl, sub))
    if "oneOf" in schema:
        ok = [s for s in schema["oneOf"] if not validate(value, s, path)]
        if len(ok) != 1:
            errs.append(f"{_fmt(path)} : valeur invalide (« {value} »)")
    return errs


def check(value: Any, name: str) -> list[str]:
    return validate(value, load(name))


def keywords(schema: Any, out: set[str] | None = None) -> set[str]:
    """Tous les mots-clés employés par un schéma (test : tous supportés)."""
    out = set() if out is None else out
    if isinstance(schema, dict):
        for k, v in schema.items():
            out.add(k)
            if k == "properties" and isinstance(v, dict):
                for sub in v.values():
                    keywords(sub, out)
            elif k in ("items", "additionalProperties", "propertyNames") and isinstance(v, dict):
                keywords(v, out)
            elif k == "oneOf" and isinstance(v, list):
                for sub in v:
                    keywords(sub, out)
    return out
