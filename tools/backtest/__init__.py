"""Backtest hors ligne d'Ultima — politiques de choix de carte sur l'index de runs.

Paquet volontairement autonome : Python 3 standard library uniquement, aucune
dépendance, aucun accès réseau. Il lit l'index compact produit par
`tools/index_runs.py` (`data/index/runs-<build>.jsonl.gz`, gitignoré) et rend des
métriques JSON comparables entre politiques.

Modules :
    loader    — lecture en streaming + filtres (mode, ascension, solo/co-op, build)
    policies  — protocole `Policy.choose(decision, run)` + baselines
    metrics   — accord avec l'humain, skip, win rate (corrélation, pas causalité)
    run       — CLI (`python3 -I tools/backtest/run.py ...`)
    selftest  — tests sur un fixture synthétique de 3 runs
"""
