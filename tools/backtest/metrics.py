"""Métriques du backtest : accord avec l'humain, skip, win rate.

Ce que ces chiffres disent — et ne disent pas :

- l'**accord** mesure si la politique aurait fait le même choix que le joueur
  humain à l'instant T, avec l'état de l'époque (§2 « Backtest » de docs/27) ;
- le **win rate comparé** (runs où la politique est d'accord vs pas d'accord) est
  une **corrélation**, jamais une causalité : les runs où l'humain suit une
  politique donnée ne sont pas un échantillon aléatoire (biais de sélection,
  joueurs meilleurs, archétypes plus faciles, ascensions différentes). Toute
  lecture causale est explicitement refusée dans la sortie (`notes`).
- l'**abstention** (politique sans avis : aucune carte offerte n'est notée) est
  comptée à part du **skip** (le joueur a passé). Les mélanger ferait croire à un
  modèle de skip qui n'existe pas.
"""

from __future__ import annotations

from typing import Any

__all__ = ["CardTally", "Metrics", "Tally", "rate"]

RATE_DIGITS = 4
#: Seuil d'accord run-level : un run est « d'accord » si la politique a suivi le
#: joueur sur au moins cette fraction de ses décisions cartes où elle avait un avis.
RUN_AGREEMENT_THRESHOLD = 0.5

#: Salles dont l'offre de cartes est une récompense (3 cartes, on en prend une).
REWARD_ROOMS = frozenset({"monster", "elite", "boss", "unknown", "treasure"})


def context_of(decision) -> str:
    """Classe une décision carte par contexte d'offre.

    - `reward` : offre de récompense à 3 cartes (monstre, élite, boss, événement) ;
    - `shop`   : stock de boutique (4 à 10 cartes, achat facultatif) ;
    - `other`  : le reste (anciens, offres moddées, tailles inhabituelles).

    Mélanger boutique et récompense dans un seul taux d'accord n'a pas de sens :
    en boutique l'humain n'achète rien 69 % du temps, donc une politique sans
    modèle d'achat y est mécaniquement à ~0 %.
    """
    if decision.room_type == "shop":
        return "shop"
    if decision.room_type in REWARD_ROOMS and len(decision.offered) == 3:
        return "reward"
    return "other"


NOTES = (
    "corrélation, pas causalité : le win rate comparé ne prouve pas l'effet de la politique",
    "skip non modélisé par la baseline : un skip humain est un désaccord mécanique",
    "l'abstention (aucune carte offerte n'est notée) est hors du dénominateur d'accord",
    "les runs abandonnés comptent comme des défaites (filtre --exclude-abandoned disponible)",
    "les runs co-op sont comptés par décision-joueur, pas par run",
    "boutique : stock de 4 à 10 cartes et achat facultatif (69 % des visites "
    "n'achètent rien) — l'accord y est structurellement bas pour une politique "
    "sans modèle d'achat ; voir by_context.reward pour la comparaison honnête",
)


def rate(numerator: float, denominator: float) -> float | None:
    """Ratio arrondi, None si le dénominateur est nul (jamais 0 par convention)."""
    if not denominator:
        return None
    return round(numerator / denominator, RATE_DIGITS)


class Tally:
    """Compteur d'accord pour un sous-ensemble (total, acte, ascension, perso…)."""

    __slots__ = (
        "decisions",
        "opined",
        "abstained",
        "human_pick",
        "human_skip",
        "agree",
        "pick_opined",
        "pick_agree",
        "skip_opined",
        "skip_agree",
        "policy_pick",
        "policy_skip",
        "n_agree",
        "wins_agree",
        "n_disagree",
        "wins_disagree",
    )

    def __init__(self) -> None:
        for slot in self.__slots__:
            setattr(self, slot, 0)

    def add(
        self,
        *,
        agree: bool,
        branch: str,
        win: bool | None,
        policy_skip: bool,
    ) -> None:
        self.decisions += 1
        self.opined += 1
        if policy_skip:
            self.policy_skip += 1
        else:
            self.policy_pick += 1
        if branch == "pick":
            self.human_pick += 1
            self.pick_opined += 1
            if agree:
                self.pick_agree += 1
        else:
            self.human_skip += 1
            self.skip_opined += 1
            if agree:
                self.skip_agree += 1
        if agree:
            self.agree += 1
        # `win=None` : décision hors du périmètre win rate (run abandonné exclu).
        if win is None:
            return
        if agree:
            self.n_agree += 1
            if win:
                self.wins_agree += 1
        else:
            self.n_disagree += 1
            if win:
                self.wins_disagree += 1

    def add_abstention(self, *, human_pick: bool) -> None:
        self.decisions += 1
        self.abstained += 1
        if human_pick:
            self.human_pick += 1
        else:
            self.human_skip += 1

    def to_dict(self) -> dict:
        win_agree = rate(self.wins_agree, self.n_agree)
        win_disagree = rate(self.wins_disagree, self.n_disagree)
        delta = None
        if win_agree is not None and win_disagree is not None:
            delta = round(100 * (win_agree - win_disagree), 2)
        return {
            "decisions": self.decisions,
            "opined": self.opined,
            "abstained": self.abstained,
            "abstain_rate": rate(self.abstained, self.decisions),
            "human_pick": self.human_pick,
            "human_skip": self.human_skip,
            "human_skip_rate": rate(self.human_skip, self.opined),
            "policy_skip": self.policy_skip,
            "policy_skip_rate": rate(self.policy_skip, self.opined),
            "agree": self.agree,
            "agreement_rate": rate(self.agree, self.opined),
            "pick": {
                "opined": self.pick_opined,
                "agree": self.pick_agree,
                "rate": rate(self.pick_agree, self.pick_opined),
            },
            "skip": {
                "opined": self.skip_opined,
                "agree": self.skip_agree,
                "rate": rate(self.skip_agree, self.skip_opined),
            },
            "win_rate": {
                "agree": {
                    "decisions": self.n_agree,
                    "wins": self.wins_agree,
                    "rate": win_agree,
                },
                "disagree": {
                    "decisions": self.n_disagree,
                    "wins": self.wins_disagree,
                    "rate": win_disagree,
                },
                "delta_pp": delta,
            },
        }


class RunSplit:
    """Compteur run-level : accord majoritaire, avec la longueur du run exposée.

    La longueur est le confondant principal : un run abandonné tôt n'a que
    quelques décisions, donc une fraction d'accord très bruitée (0 ou 1). Publier
    `mean_opined` et `abandoned_share` par tranche rend ce biais visible au lieu
    de le laisser derrière un delta de win rate flatteur.
    """

    __slots__ = ("runs", "wins", "opined", "abandoned", "coop")

    def __init__(self) -> None:
        for slot in self.__slots__:
            setattr(self, slot, 0)

    def add(self, *, win: bool, opined: int, abandoned: bool, coop: bool) -> None:
        self.runs += 1
        self.opined += opined
        self.abandoned += int(abandoned)
        self.coop += int(coop)
        if win:
            self.wins += 1

    def to_dict(self) -> dict:
        return {
            "runs": self.runs,
            "wins": self.wins,
            "win_rate": rate(self.wins, self.runs),
            "mean_opined": rate(self.opined, self.runs),
            "abandoned_runs": self.abandoned,
            "abandoned_share": rate(self.abandoned, self.runs),
            "coop_runs": self.coop,
            "coop_share": rate(self.coop, self.runs),
        }


class CardTally:
    """Compteur par carte : offres, picks de la politique, picks humains, communs."""

    __slots__ = ("offered", "policy_pick", "human_pick", "both")

    def __init__(self) -> None:
        for slot in self.__slots__:
            setattr(self, slot, 0)

    def to_dict(self) -> dict:
        return {
            "offered": self.offered,
            "policy_pick": self.policy_pick,
            "human_pick": self.human_pick,
            "both": self.both,
            "policy_rate": rate(self.policy_pick, self.offered),
            "human_rate": rate(self.human_pick, self.offered),
            # Précision : quand la politique prend cette carte, l'humain la prend aussi.
            "precision": rate(self.both, self.policy_pick),
        }


class Metrics:
    """Accumulateur ; `score_run` traite un run complet, `to_dict` le sérialise."""

    def __init__(
        self,
        policy_name: str,
        policy_description: dict | None = None,
        filters: dict | None = None,
        run_threshold: float = RUN_AGREEMENT_THRESHOLD,
        exclude_abandoned: bool = False,
        min_opined_for_run_split: int = 10,
    ) -> None:
        self.policy_name = policy_name
        self.policy_description = policy_description or {"name": policy_name}
        self.filters = filters or {}
        self.run_threshold = run_threshold
        self.exclude_abandoned = exclude_abandoned
        self.min_opined_for_run_split = max(0, int(min_opined_for_run_split))

        self.totals = Tally()
        self.by_act: dict[str, Tally] = {}
        self.by_ascension: dict[str, Tally] = {}
        self.by_character: dict[str, Tally] = {}
        self.by_players: dict[str, Tally] = {}
        self.by_room: dict[str, Tally] = {}
        self.by_context: dict[str, Tally] = {}
        self.per_card: dict[str, CardTally] = {}

        self.runs_scored = 0
        self.runs_without_card_decisions = 0
        self.runs_without_opinion = 0
        self.wins = 0
        self.abandoned = 0
        self.wins_excl_abandoned = 0
        self.runs_excl_abandoned = 0
        # Trois périmètres run-level, publiés ensemble : aucun n'est « le bon ».
        self.run_all = {"agree": RunSplit(), "disagree": RunSplit()}
        self.run_excl = {"agree": RunSplit(), "disagree": RunSplit()}
        self.run_guarded = {"agree": RunSplit(), "disagree": RunSplit()}
        self.runs_below_min_opined = 0

    # -- traitement -------------------------------------------------------- #
    def score_run(self, run, policy) -> dict:
        """Évalue toutes les décisions cartes du run. Renvoie un résumé du run."""
        self.runs_scored += 1
        if run.win:
            self.wins += 1
        if run.abandoned:
            self.abandoned += 1
        else:
            self.runs_excl_abandoned += 1
            if run.win:
                self.wins_excl_abandoned += 1

        counts = {"opined": 0, "agree": 0, "abstained": 0, "policy_skip": 0}
        # `win=None` exclut la décision du périmètre win rate (runs abandonnés).
        win: bool | None = None if (self.exclude_abandoned and run.abandoned) else bool(run.win)
        solo_key = "coop" if run.coop else "solo"
        card_decisions = 0

        for decision in run.card_decisions():
            card_decisions += 1
            choice, policy_skip = policy.decide(decision, run)
            human_pick = decision.picked is not None
            player = run.player(decision.player_id)
            character = player.character if player is not None else None

            buckets = (
                self.totals,
                self._bucket(self.by_act, decision.act),
                self._bucket(self.by_ascension, run.ascension),
                self._bucket(self.by_character, character),
                self._bucket(self.by_players, solo_key),
                self._bucket(self.by_room, decision.room_type),
                self._bucket(self.by_context, context_of(decision)),
            )

            # Carte par carte : les offres sont comptées même quand la politique
            # n'a pas d'avis (c'est la couverture réelle des tables de méta).
            if decision.offered:
                per_card = self.per_card
                if human_pick and decision.picked_in_offer():
                    self._card(per_card, decision.picked).human_pick += 1
                policy_choice = None if (policy_skip or choice is None) else choice
                for card_id in decision.offered:
                    entry = self._card(per_card, card_id)
                    entry.offered += 1
                    if policy_choice is not None and card_id == policy_choice:
                        entry.policy_pick += 1
                        if human_pick and decision.picked == card_id:
                            entry.both += 1

            if choice is None and not policy_skip:
                counts["abstained"] += 1
                for tally in buckets:
                    tally.add_abstention(human_pick=human_pick)
                continue

            branch = "pick" if human_pick else "skip"
            agree = (human_pick and choice == decision.picked) or (
                not human_pick and policy_skip
            )
            counts["opined"] += 1
            counts["agree"] += int(agree)
            counts["policy_skip"] += int(policy_skip)

            for tally in buckets:
                tally.add(
                    agree=agree,
                    branch=branch,
                    win=win,
                    policy_skip=policy_skip,
                )

        if card_decisions == 0:
            self.runs_without_card_decisions += 1
        if counts["opined"] == 0:
            self.runs_without_opinion += 1
            return counts

        ratio = counts["agree"] / counts["opined"]
        side = "agree" if ratio >= self.run_threshold else "disagree"
        opined = counts["opined"]
        payload = {"win": bool(run.win), "opined": opined,
                   "abandoned": run.abandoned, "coop": run.coop}
        self.run_all[side].add(**payload)
        if not run.abandoned:
            self.run_excl[side].add(**payload)
            if opined >= self.min_opined_for_run_split:
                self.run_guarded[side].add(**payload)
            else:
                self.runs_below_min_opined += 1
        return counts

    # -- helpers ----------------------------------------------------------- #
    @staticmethod
    def _key(value: Any) -> str:
        return "unknown" if value is None else str(value)

    def _bucket(self, mapping: dict[str, Tally], key: Any) -> Tally:
        name = self._key(key)
        tally = mapping.get(name)
        if tally is None:
            tally = mapping[name] = Tally()
        return tally

    def _card(self, mapping: dict[str, CardTally], card_id: str) -> CardTally:
        tally = mapping.get(card_id)
        if tally is None:
            tally = mapping[card_id] = CardTally()
        return tally

    # -- sérialisation ----------------------------------------------------- #
    def to_dict(self) -> dict:
        win_rate = rate(self.wins, self.runs_scored)
        win_rate_excl = rate(self.wins_excl_abandoned, self.runs_excl_abandoned)
        return {
            "policy": self.policy_description,
            "filters": self.filters,
            "runs": {
                "scored": self.runs_scored,
                "wins": self.wins,
                "win_rate": win_rate,
                "abandoned": self.abandoned,
                "excl_abandoned": {
                    "runs": self.runs_excl_abandoned,
                    "wins": self.wins_excl_abandoned,
                    "win_rate": win_rate_excl,
                },
                "runs_without_opinion": self.runs_without_opinion,
            },
            "decisions": self.totals.to_dict(),
            "by_act": {k: v.to_dict() for k, v in sorted(self.by_act.items())},
            "by_ascension": {
                k: v.to_dict()
                for k, v in sorted(
                    self.by_ascension.items(), key=lambda kv: _asc_sort_key(kv[0])
                )
            },
            "by_character": {k: v.to_dict() for k, v in sorted(self.by_character.items())},
            "by_players": {k: v.to_dict() for k, v in sorted(self.by_players.items())},
            "by_room": {k: v.to_dict() for k, v in sorted(self.by_room.items())},
            "by_context": {k: v.to_dict() for k, v in sorted(self.by_context.items())},
            "run_level": {
                "threshold": self.run_threshold,
                "min_opined": self.min_opined_for_run_split,
                "runs_below_min_opined": self.runs_below_min_opined,
                "all": _split_to_dict(self.run_all, "tous les runs avec avis"),
                "excl_abandoned": _split_to_dict(
                    self.run_excl, "runs non abandonnés"
                ),
                "guarded": _split_to_dict(
                    self.run_guarded,
                    "runs non abandonnés avec au moins min_opined décisions avec avis",
                ),
                "note": (
                    "les tranches `all` et `excl_abandoned` comptent des runs courts : "
                    "une fraction d'accord sur 3 décisions est très bruitée, et les runs "
                    "abandonnés tôt y dominent. `guarded` neutralise ce confondant."
                ),
            },
            "per_card": {
                card: tally.to_dict()
                for card, tally in sorted(
                    self.per_card.items(), key=lambda kv: (-kv[1].offered, kv[0])
                )
            },
            "notes": list(NOTES),
        }


def _split_to_dict(split: dict, label: str) -> dict:
    agree = split["agree"].to_dict()
    disagree = split["disagree"].to_dict()
    return {
        "label": label,
        "agree": agree,
        "disagree": disagree,
        "delta_pp": _delta(agree["win_rate"], disagree["win_rate"]),
    }


def _delta(first: float | None, second: float | None) -> float | None:
    if first is None or second is None:
        return None
    return round(100 * (first - second), 2)


def _asc_sort_key(key: str) -> tuple[int, Any]:
    try:
        return (int(key), int(key))
    except (TypeError, ValueError):
        return (10_000, key)
