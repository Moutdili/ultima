"""Politiques de choix de carte branchables sur le harnais de backtest.

Protocole minimal (c'est le contrat du harnais, pas un contrat C#) :

    class Policy:
        name: str
        def choose(self, decision, run) -> str | None

`choose` renvoie l'identifiant de carte choisi, ou `None` quand la politique
**s'abstient** (aucune carte de l'offre n'est notée). L'abstention n'est pas un
skip : un skip est une décision explicite du joueur, l'abstention est un trou de
couverture de la politique. Les métriques les comptent séparément.

`would_skip` permet à une politique de modéliser le skip (seuil de note) ; la
baseline `HighestMeta` ne le fait que si on lui passe `skip_below`, sinon elle
n'a aucun modèle de skip — c'est une limite assumée, mesurée par le harnais.
"""

from __future__ import annotations

import json
import random
from typing import Any, Iterable

__all__ = [
    "CardMeta",
    "HighestMeta",
    "POLICIES",
    "Policy",
    "RandomPolicy",
    "make_policy",
]


# --------------------------------------------------------------------------- #
# Tables de méta
# --------------------------------------------------------------------------- #
class CardMeta:
    """Notes de consensus par carte et par personnage.

    Construit depuis `data/meta/card_tiers.json` :

        characters.<perso>.<CleDeClasse>.consensus.score

    Piège réel : la **clé** JSON est le nom de classe (`BladeOfInk`,
    `NOXIOUS_FUMES` non !) et l'identifiant employé par l'index est le champ
    `id` (`BLADE_OF_INK`). On indexe donc par `id`, jamais par la clé.
    """

    __slots__ = ("_scores", "path", "generated", "skipped_without_consensus")

    def __init__(
        self,
        scores: dict[str, dict[str, float]],
        path: str | None = None,
        generated: str | None = None,
        skipped_without_consensus: int = 0,
    ) -> None:
        self._scores = scores
        self.path = path
        self.generated = generated
        self.skipped_without_consensus = skipped_without_consensus

    # -- construction ------------------------------------------------------ #
    @classmethod
    def from_doc(cls, doc: dict, path: str | None = None) -> "CardMeta":
        """Accepte le document complet ou un mapping simplifié perso → {id: note}."""
        if isinstance(doc, dict) and isinstance(doc.get("characters"), dict):
            return cls._from_full_doc(doc, path)
        return cls._from_simple_mapping(doc, path)

    @classmethod
    def _from_full_doc(cls, doc: dict, path: str | None) -> "CardMeta":
        scores: dict[str, dict[str, float]] = {}
        skipped = 0
        for character, cards in (doc.get("characters") or {}).items():
            if not isinstance(cards, dict):
                continue
            for entry in cards.values():
                if not isinstance(entry, dict):
                    continue
                card_id = entry.get("id")
                if not isinstance(card_id, str) or not card_id:
                    continue
                consensus = entry.get("consensus")
                score: float | None = None
                if isinstance(consensus, dict):
                    score = _number(consensus.get("score"))
                elif isinstance(consensus, (int, float)):
                    score = float(consensus)
                if score is None:
                    skipped += 1
                    continue
                scores.setdefault(_card_key(card_id), {})[character] = score
        return cls(
            scores,
            path=path,
            generated=str(doc.get("generated") or "") or None,
            skipped_without_consensus=skipped,
        )

    @classmethod
    def _from_simple_mapping(cls, doc: dict, path: str | None) -> "CardMeta":
        scores: dict[str, dict[str, float]] = {}
        for character, cards in (doc or {}).items():
            if not isinstance(cards, dict):
                continue
            for card_id, value in cards.items():
                score = _number(value)
                if score is None and isinstance(value, dict):
                    score = _number((value.get("consensus") or {}).get("score"))
                if score is None:
                    continue
                scores.setdefault(_card_key(card_id), {})[character] = score
        return cls(scores, path=path)

    @classmethod
    def from_file(cls, path: str) -> "CardMeta":
        with open(path, "r", encoding="utf-8") as handle:
            doc = json.load(handle)
        return cls.from_doc(doc, path=path)

    # -- lecture ----------------------------------------------------------- #
    def score(self, card_id: str, character: str | None = None) -> float | None:
        """Note de la carte pour ce personnage ; `colorless` sert de repli.

        `character=None` renvoie la meilleure note connue (toutes listes) — utile
        pour les outils, jamais employé par `HighestMeta` qui reste strict sur le
        personnage du joueur.
        """
        by_character = self._scores.get(_card_key(card_id))
        if not by_character:
            return None
        if character is not None:
            value = by_character.get(character)
            if value is not None:
                return value
            return by_character.get("colorless")
        return max(by_character.values())

    def characters(self) -> list[str]:
        found = set()
        for by_character in self._scores.values():
            found.update(by_character)
        return sorted(found)

    def __len__(self) -> int:
        return len(self._scores)

    def __contains__(self, card_id: object) -> bool:
        return isinstance(card_id, str) and _card_key(card_id) in self._scores

    def stats(self) -> dict:
        return {
            "path": self.path,
            "generated": self.generated,
            "cards": len(self._scores),
            "characters": self.characters(),
            "skipped_without_consensus": self.skipped_without_consensus,
        }


def _card_key(card_id: str) -> str:
    return card_id.strip().upper()


def _number(value: Any) -> float | None:
    if value is None or isinstance(value, bool):
        return None
    if isinstance(value, (int, float)):
        return float(value)
    return None


# --------------------------------------------------------------------------- #
# Protocole
# --------------------------------------------------------------------------- #
class Policy:
    """Politique de choix de carte. `choose` rend un id de carte ou None."""

    name: str = "policy"
    #: Vrai si la politique sait distinguer « je passe » de « je n'ai pas d'avis ».
    has_skip_model: bool = False

    def choose(self, decision, run) -> str | None:  # pragma: no cover - abstrait
        raise NotImplementedError

    def decide(self, decision, run) -> tuple[str | None, bool]:
        """Résout `(carte choisie, skip conseillé)` en un seul appel.

        Les métriques emploient `decide` pour éviter de classer deux fois l'offre.
        """
        if self.has_skip_model and self.would_skip(decision, run):
            return None, True
        return self.choose(decision, run), False

    def would_skip(self, decision, run) -> bool:
        """Vrai si la politique conseille explicitement de passer."""
        return False

    def describe(self) -> dict:
        return {"name": self.name, "has_skip_model": self.has_skip_model}


def _character_of(decision, run) -> str | None:
    player = run.player(decision.player_id)
    if player is None and run.players:
        player = run.players[0]
    return player.character if player is not None else None


# --------------------------------------------------------------------------- #
# Baselines
# --------------------------------------------------------------------------- #
class HighestMeta(Policy):
    """Prend la carte offerte dont le consensus de méta est le plus haut.

    - Une carte absente de la méta est ignorée (pas de note inventée).
    - Offre entièrement non notée → abstention (`None`).
    - Égalité de note → départage déterministe par identifiant (ordre alphabétique),
      pour que deux exécutions rendent le même JSON.
    - `skip_below` : seuil optionnel sous lequel la politique conseille de passer.
      À `None` (défaut), la politique n'a **aucun** modèle de skip.
    """

    name = "highest_meta"

    def __init__(self, meta: CardMeta, skip_below: float | None = None) -> None:
        self.meta = meta
        self.skip_below = skip_below
        self.has_skip_model = skip_below is not None

    def rank(self, decision, run) -> list[tuple[str, float]]:
        """Cartes notées de l'offre, triées (note décroissante, id croissant)."""
        character = _character_of(decision, run)
        scored = []
        for card_id in decision.offered:
            value = self.meta.score(card_id, character)
            if value is not None:
                scored.append((card_id, value))
        scored.sort(key=lambda pair: (-pair[1], pair[0]))
        return scored

    def choose(self, decision, run) -> str | None:
        ranked = self.rank(decision, run)
        return ranked[0][0] if ranked else None

    def decide(self, decision, run) -> tuple[str | None, bool]:
        """Un seul classement de l'offre pour la carte et pour le skip."""
        ranked = self.rank(decision, run)
        if not ranked:
            return None, False
        card_id, score = ranked[0]
        if self.skip_below is not None and score < self.skip_below:
            return None, True
        return card_id, False

    def would_skip(self, decision, run) -> bool:
        if self.skip_below is None:
            return False
        ranked = self.rank(decision, run)
        return bool(ranked) and ranked[0][1] < self.skip_below

    def describe(self) -> dict:
        return {
            "name": self.name,
            "has_skip_model": self.has_skip_model,
            "skip_below": self.skip_below,
            "meta": self.meta.stats(),
        }


class RandomPolicy(Policy):
    """Contrôle : choisit uniformément dans l'offre, jamais d'abstention.

    Sert d'étalon bas — l'accord attendu avec l'humain sur des offres de 3 cartes
    est ≈ 1/3 moins le taux de skip, donc bien au-dessus du « hasard pur » quand
    les cartes ne sont pas équiprobables.
    """

    name = "random"

    def __init__(self, seed: int = 0) -> None:
        self.seed = seed
        self._rng = random.Random(seed)

    def choose(self, decision, run) -> str | None:
        if not decision.offered:
            return None
        return decision.offered[self._rng.randrange(len(decision.offered))]

    def describe(self) -> dict:
        return {"name": self.name, "has_skip_model": False, "seed": self.seed}


#: Politiques instanciables par la CLI (`--policy`).
POLICIES: dict[str, type[Policy]] = {
    "highest_meta": HighestMeta,
    "random": RandomPolicy,
}


def make_policy(name: str, meta_path: str | None = None, **kwargs) -> Policy:
    """Instancie une politique par nom ; `highest_meta` charge la méta à la demande."""
    key = (name or "").strip().lower()
    if key not in POLICIES:
        raise ValueError(
            f"politique inconnue : {name!r} (disponibles : {', '.join(sorted(POLICIES))})"
        )
    policy_class = POLICIES[key]
    if policy_class is HighestMeta:
        if not meta_path:
            raise ValueError("la politique highest_meta exige --meta")
        return HighestMeta(CardMeta.from_file(meta_path), **kwargs)
    return policy_class(**kwargs)


def resolve(policy: Policy, decision, run) -> str | None:
    """Décision effective : None si skip explicite **ou** abstention.

    Réservé aux appelants qui veulent une seule valeur ; les métriques, elles,
    distinguent les deux cas (voir `metrics.py`).
    """
    if policy.has_skip_model and policy.would_skip(decision, run):
        return None
    return policy.choose(decision, run)
