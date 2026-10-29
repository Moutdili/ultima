import copy

from ultima_companion import ipc, state, textview
from ultima_companion.settings import OverlaySettings


def _flat(sections):
    return "\n".join(f"{s.title}\n" + "\n".join(f"{r.title} | {r.subtitle}" for r in s.rows)
                     for s in sections)


def test_score_absent_is_dash_never_zero():
    assert textview.fmt_score(None) == "—"
    assert textview.grade_prefix(None) == "— · "


def test_score_grade_first():
    sc = state.Score(62, [state.ScoreTerm("Méta", 60, "Spire Codex"), state.ScoreTerm("Malus", -3, "")],
                     "medium")
    assert textview.fmt_score(sc) == "B (62/100, confiance medium)"
    assert textview.fmt_terms(sc) == "Méta +60 [Spire Codex] ; Malus -3"
    assert textview.grade_prefix(sc) == "B · "


def test_run_sections_sample(sample):
    secs = textview.run_sections(state.parse_state(sample))
    titles = [s.title for s in secs]
    assert titles == ["Résumé", "Note du deck", "Malédictions (1)", "Deck (11 cartes)",
                      "Reliques (1)", "Potions (1)"]
    text = _flat(secs)
    assert "— · Frappe ×4" in text
    assert "— · Défense ×4" in text           # 4 Défense normales
    assert "— · Défense |" in text            # + la Défense enchantée, séparée
    assert "Enchantement : Célérité 1 — Piochez 1 carte" in text
    assert "[MALÉDICTION] Fléau de l'Ascendant" in text
    assert "injouable" in text
    assert "B · (exemple)" in text and "score B (62/100, confiance medium)" in text
    assert "Méta +60 [Spire Codex v0.107.1]" in text
    assert "— · Potion d'attaque" in text and "score —" in text
    assert "C (48/100, confiance low)" in text
    assert "score 0" not in text


def test_run_sections_no_run(sample):
    data = copy.deepcopy(sample)
    data["run"] = None
    secs = textview.run_sections(state.parse_state(data))
    assert secs[0].rows[0].title == "Aucune run en cours."


def test_run_sections_missing_daemon():
    secs = textview.run_sections(state.StateResult(state.MISSING, message="x"))
    assert secs[0].rows[0].title == "Daemon non démarré."


def test_status_sections_sample(sample):
    secs = textview.status_sections(state.parse_state(sample), ipc.ALIVE, OverlaySettings.default())
    text = _flat(secs)
    assert "Daemon | en marche" in text
    assert "Version du contrat | v1 (supportée : v1)" in text
    assert "Mode dégradé | aucun" in text
    assert "Jeu lancé | oui" in text and "Save | OK" in text and "Miroir mémoire | OK" in text
    assert "Latence du miroir | 108 ms" in text
    assert "Écran courant | Boutique (Shop)" in text
    assert "identiques à settings.json" in text
    assert "29/10/2026" in text


def test_status_sections_settings_differ(sample):
    local = OverlaySettings.default()
    local.move_up("relics")
    text = _flat(textview.status_sections(state.parse_state(sample), ipc.ALIVE, local))
    assert "différents de settings.json" in text


def test_status_sections_variants(sample):
    text = _flat(textview.status_sections(state.StateResult(state.MISSING), ipc.NO_SOCKET, None))
    assert "Daemon non démarré" in text
    data = copy.deepcopy(sample)
    data["contractVersion"] = 7
    text = _flat(textview.status_sections(state.parse_state(data), ipc.ALIVE, None))
    assert "v7 — non supportée" in text and "Mettre à jour ULTIMA" in text


def test_banner_priorities(sample):
    ok = state.parse_state(sample)
    assert textview.banner_message(ok, ipc.ALIVE) is None
    assert "Daemon non démarré" in textview.banner_message(state.StateResult(state.MISSING), ipc.NO_SOCKET)
    assert "périmé" in textview.banner_message(ok, ipc.REFUSED)
    data = copy.deepcopy(sample)
    data["health"]["degraded"] = "save illisible"
    assert textview.banner_message(state.parse_state(data), ipc.ALIVE) == "Mode dégradé : save illisible"
    data["contractVersion"] = 2
    assert "mettre à jour ULTIMA" in textview.banner_message(state.parse_state(data), ipc.ALIVE)
