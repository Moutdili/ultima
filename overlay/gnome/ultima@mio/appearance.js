// ULTIMA — bloc `appearance` de ~/.config/ultima/settings.json (contrat v1.4, docs/44 §3).
// Module pur (aucune dépendance GNOME Shell), testable avec `gjs -m` et `node`.
//
// Le fichier est LU, jamais écrit. Il contient deux blocs que le HUD consomme :
//   { "hud": { quality, heightFraction, restOpacity, side, … },     <- HudSettings v1.3 (hud.js)
//     "appearance": { "hud": { theme, font, tones, position, railEdge } } }  <- v1.4 (ce module)
// `appearance` n'est PAS dans EngineState (docs/44 §8) : c'est un réglage d'affichage pur, lu par
// chaque surface. Le daemon ne le réécrit jamais.
//
// Règle de tolérance (docs/44 §1.3) : valeur absente ou invalide -> défaut, CHAMP PAR CHAMP ; une
// valeur valide hors des bornes de sûreté est RAMENÉE à la borne (jamais rejetée).

import {FONT_FAMILY, FONT_SIZE_RANGE, THEME_ID, AUTO_THEME, normalizeTone, parseColor} from './theme.js';

export const MAX_SETTINGS_BYTES = 256 * 1024;

export const ANCHORS = Object.freeze(['top', 'center', 'bottom']);
export const SIDES = Object.freeze(['left', 'right']);
export const POSITION_WIDTH_RANGE = Object.freeze([280, 520]);   // contrat docs/44 §3
export const OFFSET_LIMIT = 4000;                               // px logiques ; la géométrie borne à l'écran

export const APPEARANCE_DEFAULTS = Object.freeze({
    theme: 'sts',
    font: Object.freeze({family: null, size: null}),  // null = police du thème, puis défaut système
    tones: Object.freeze({}),
    position: Object.freeze({
        side: null,        // null = suivre HudSettings.Side (contrat v1.3)
        anchor: 'center',
        offsetX: 0,
        offsetY: 0,
        width: 360,        // dans [280, 520] ; 1536x960 : 9,9 % de surface panneau ouvert
    }),
    railEdge: true,        // liseré or du rail (décision de Mio, docs/45 T4)
});

function isObj(v) {
    return v !== null && typeof v === 'object' && !Array.isArray(v);
}

function finite(v) {
    return typeof v === 'number' && Number.isFinite(v);
}

function num(v, [lo, hi]) {
    if (!finite(v))
        return undefined;
    return Math.min(hi, Math.max(lo, v));
}

function bool(v) {
    return typeof v === 'boolean' ? v : undefined;
}

// 'auto-perso' passe le motif (minuscules + tiret) : il est réservé, jamais un id de fichier.
export function readThemeId(v) {
    if (typeof v !== 'string')
        return undefined;
    const s = v.trim().toLowerCase();
    return s === AUTO_THEME || THEME_ID.test(s) ? s : undefined;
}

// {family, size} : famille validée (pas d'injection CSS), taille entière 12-24 px.
export function readFont(v) {
    if (!isObj(v))
        return undefined;
    const f = caseless(v, ['family', 'size']);
    const out = {};
    if (typeof f.family === 'string' && FONT_FAMILY.test(f.family.trim()))
        out.family = f.family.trim();
    else if (f.family !== undefined && f.family !== null)
        out.family = undefined; // valeur invalide : champ ignoré (repli thème)
    const size = num(f.size, FONT_SIZE_RANGE);
    out.size = size === undefined ? undefined : Math.round(size);
    return out.family === undefined && out.size === undefined ? undefined : out;
}

export function readTones(v) {
    if (!isObj(v))
        return undefined;
    const out = {};
    for (const [k, raw] of Object.entries(v)) {
        const key = normalizeTone(k);
        if (key && parseColor(raw))
            out[key] = raw.trim();
    }
    return out;
}

export function readPosition(v) {
    if (!isObj(v))
        return undefined;
    const p = caseless(v, ['side', 'anchor', 'offsetX', 'offsetY', 'width']);
    const out = {};
    if (p.side !== undefined) {
        const s = typeof p.side === 'string' ? p.side.trim().toLowerCase() : '';
        out.side = SIDES.includes(s) ? s : undefined;
    }
    if (p.anchor !== undefined) {
        const a = typeof p.anchor === 'string' ? p.anchor.trim().toLowerCase() : '';
        out.anchor = ANCHORS.includes(a) ? a : undefined;
    }
    const ox = finite(p.offsetX) ? Math.round(p.offsetX) : undefined;
    const oy = finite(p.offsetY) ? Math.round(p.offsetY) : undefined;
    out.offsetX = ox === undefined ? undefined : Math.min(OFFSET_LIMIT, Math.max(-OFFSET_LIMIT, ox));
    out.offsetY = oy === undefined ? undefined : Math.min(OFFSET_LIMIT, Math.max(-OFFSET_LIMIT, oy));
    const w = num(p.width, POSITION_WIDTH_RANGE);
    out.width = w === undefined ? undefined : Math.round(w);
    return out;
}

// Noms de propriétés insensibles à la casse (comme le daemon : StateJson est insensible à la casse).
function caseless(obj, names) {
    if (!isObj(obj))
        return null;
    const out = {};
    const lower = new Map(names.map(n => [n.toLowerCase(), n]));
    for (const [k, v] of Object.entries(obj)) {
        const name = lower.get(k.toLowerCase());
        if (name && !(name in out))
            out[name] = v;
    }
    return out;
}

const FIELDS = ['theme', 'font', 'tones', 'position', 'railEdge'];

// texte de settings.json -> {kind:'ok', hud, appearance} | {kind:'invalid', error}. Ne lève jamais.
// `hud` et `appearance` sont les blocs BRUTS (hud.js/appearance.js les valident champ par champ).
export function parseSettings(text) {
    if (typeof text !== 'string')
        return {kind: 'invalid', error: 'contenu non textuel'};
    if (text.length > MAX_SETTINGS_BYTES)
        return {kind: 'invalid', error: `réglages trop gros (${text.length} octets)`};
    let raw;
    try {
        raw = JSON.parse(text);
    } catch (e) {
        return {kind: 'invalid', error: `JSON invalide : ${e.message}`};
    }
    if (!isObj(raw))
        return {kind: 'invalid', error: 'racine non objet'};
    // `appearance.hud` (contrat) ; tolérance : `appearance` à plat, et casse des clés ignorée.
    let block = null;
    const top = caseless(raw, ['appearance'])?.appearance;
    if (isObj(top)) {
        const inner = caseless(top, ['hud'])?.hud;
        block = caseless(isObj(inner) ? inner : top, FIELDS);
    }
    return {
        kind: 'ok',
        hud: caseless(raw, ['hud'])?.hud ?? null,
        appearance: block && Object.values(block).some(v => v !== undefined) ? block : null,
    };
}

// Compatibilité v1.3 : {kind:'hud', hud} | {kind:'none'} | {kind:'invalid', error}.
export function parseSettingsHud(text) {
    const r = parseSettings(text);
    if (r.kind === 'invalid')
        return r;
    return isObj(r.hud) ? {kind: 'hud', hud: r.hud} : {kind: 'none'};
}

// Bloc `appearance.hud` (brut ou null) -> réglages validés + provenance de chaque champ.
//   {theme, font:{family,size}, tones:{…}, position:{side,anchor,offsetX,offsetY,width}, railEdge,
//    source:{champ: 'fichier'|'défaut'}}
export function resolveAppearance(raw) {
    const block = isObj(raw) ? caseless(raw, FIELDS) ?? {} : {};
    const out = {
        theme: APPEARANCE_DEFAULTS.theme,
        font: {...APPEARANCE_DEFAULTS.font},
        tones: {},
        position: {...APPEARANCE_DEFAULTS.position},
        railEdge: APPEARANCE_DEFAULTS.railEdge,
        source: {},
    };
    const take = (name, value) => {
        if (value === undefined)
            return false;
        out.source[name] = 'fichier';
        return true;
    };

    const theme = readThemeId(block.theme);
    if (take('theme', theme))
        out.theme = theme;

    const font = readFont(block.font);
    if (take('font', font)) {
        if (font.family !== undefined)
            out.font.family = font.family;
        if (font.size !== undefined)
            out.font.size = font.size;
    }

    const tones = readTones(block.tones);
    if (take('tones', tones))
        out.tones = tones;

    const pos = readPosition(block.position);
    if (take('position', pos)) {
        for (const k of Object.keys(out.position))
            if (pos[k] !== undefined)
                out.position[k] = pos[k];
    }

    const railEdge = bool(block.railEdge);
    if (take('railEdge', railEdge))
        out.railEdge = railEdge;

    for (const k of ['theme', 'font', 'tones', 'position', 'railEdge'])
        out.source[k] = out.source[k] ?? 'défaut';
    return out;
}

// Clé stable (changement -> réappliquer thème/géométrie).
export function appearanceKey(a) {
    return JSON.stringify([a.theme, a.font, a.tones, a.position, a.railEdge]);
}

// Raccourci : texte de settings.json -> réglages d'apparence résolus (jamais d'exception,
// défauts si le JSON est invalide ou si le bloc est absent).
export function parseSettingsAppearance(text) {
    const r = parseSettings(text);
    return resolveAppearance(r.kind === 'invalid' ? null : r.appearance);
}
