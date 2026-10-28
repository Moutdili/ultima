// ULTIMA — lecture de l'état publié par le daemon (module pur, testable avec `gjs -m`).
//
// L'overlay ne lit JAMAIS le jeu (docs/20 §5) : il affiche un JSON versionné publié par
// ultima-daemon. Spike : fichier $XDG_RUNTIME_DIR/ultima/overlay-state.json, repli sur
// sample-state.json (contenu factice : 5 lignes de deck). La socket viendra avec le daemon.

import {TONES} from './palette.js';

export const STATE_VERSION = 1;
export const MAX_LINES = 12;
export const RAIL_DOTS = 3;

// Retourne {ok: true, state} ou {ok: false, error}. Ne lève jamais.
export function parseState(text) {
    let raw;
    try {
        raw = JSON.parse(text);
    } catch (e) {
        return {ok: false, error: `JSON invalide : ${e.message}`};
    }
    if (!raw || typeof raw !== 'object')
        return {ok: false, error: 'racine non objet'};
    if (raw.v !== STATE_VERSION)
        return {ok: false, error: `version ${raw.v} non supportée (attendu ${STATE_VERSION})`};
    if (!Array.isArray(raw.lines))
        return {ok: false, error: 'champ lines manquant'};

    const tone = t => (TONES.includes(t) ? t : 'cream');
    const str = (s, max) => String(s ?? '').slice(0, max);

    const lines = raw.lines.slice(0, MAX_LINES).map(l => ({
        label: str(l?.label, 40),
        value: str(l?.value, 12),
        tone: tone(l?.tone),
    }));
    const rail = (Array.isArray(raw.rail) ? raw.rail : []).slice(0, RAIL_DOTS).map(d => ({
        tone: tone(d?.tone),
        label: str(d?.label, 16),
    }));
    while (rail.length < RAIL_DOTS)
        rail.push({tone: 'lightGray', label: ''});

    return {
        ok: true,
        state: {
            title: str(raw.title, 40) || 'ULTIMA',
            subtitle: str(raw.subtitle, 60),
            source: str(raw.source, 20),
            lines,
            rail,
        },
    };
}
