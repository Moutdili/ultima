// ULTIMA — réglages du HUD (contrat v1.3) et niveau de verre effectif (module pur, sans GNOME Shell).
//
// Contrat v1.3 (skill sts2-engine-contract, Ultima.Engine/Contracts/EngineContracts.cs `HudSettings`) :
// `OverlaySettings.Hud = HudSettings(Quality "eco|verre|loupe", HeightFraction, RestOpacity,
// Side "right|left", DeckScoreInCombat, GradesOnly)` ; défaut `("verre", 0.55, 0.62, "right", false, false)`.
// Écrit dans ~/.config/ultima/settings.json par le TUI `ultima` ou le compagnon (`{"modules": […],
// "hud": {…}}`, camelCase), publié par le daemon dans `state.settings.hud`.
//
// Sources, par champ, de la plus fraîche à la plus ancienne (consommateur tolérant : une valeur
// invalide est ignorée CHAMP PAR CHAMP, les autres sont gardées) :
//   1. settings.json lu directement (réaction immédiate, marche sans daemon) ;
//   2. state.settings.hud publié par le daemon (normalisé par SettingsStore.NormalizeHud) ;
//   3. GSettings de l'extension (repli : ni fichier ni daemon) ;
//   4. défaut du contrat.
// Validité = celle du daemon (SettingsStore.NormalizeHud) : qualité ∈ eco|verre|loupe (casse
// ignorée), hauteur ∈ ]0, 1], opacité ∈ [0, 1], côté ∈ right|left, booléens stricts.
// Une valeur valide hors des bornes de sûreté de l'overlay est RAMENÉE à la borne (pas rejetée).

export const QUALITIES = Object.freeze(['eco', 'verre', 'loupe']);
export const SIDES = Object.freeze(['right', 'left']);

export const HUD_DEFAULTS = Object.freeze({
    quality: 'verre',
    heightFraction: 0.55,
    restOpacity: 0.62,
    side: 'right',
    deckScoreInCombat: false,
    gradesOnly: false,
});

// Bornes de sûreté : leçon v1 (un rail à 0.30 était INVISIBLE en jeu) -> opacité au repos ≥ 0.35 ;
// surface déployée < 15 % (docs/17 §7) -> hauteur ≤ 0.85 ; en dessous de 0.2, plus rien de lisible.
export const HEIGHT_RANGE = Object.freeze([0.2, 0.85]);
export const REST_OPACITY_RANGE = Object.freeze([0.35, 1]);

function finite(v) {
    return typeof v === 'number' && Number.isFinite(v);
}

// valid(v) : domaine du contrat ; [lo, hi] : bornes de sûreté de l'overlay (ramené dedans).
function num(v, valid, [lo, hi]) {
    if (!finite(v) || !valid(v))
        return undefined;
    return Math.min(hi, Math.max(lo, v));
}

function pick(v, list) {
    if (typeof v !== 'string')
        return undefined;
    const s = v.trim().toLowerCase();
    return list.includes(s) ? s : undefined;
}

function bool(v) {
    return typeof v === 'boolean' ? v : undefined;
}

export const READERS = Object.freeze({
    quality: v => pick(v, QUALITIES),
    heightFraction: v => num(v, x => x > 0 && x <= 1, HEIGHT_RANGE),
    restOpacity: v => num(v, x => x >= 0 && x <= 1, REST_OPACITY_RANGE),
    side: v => pick(v, SIDES),
    deckScoreInCombat: bool,
    gradesOnly: bool,
});

function asObject(v) {
    return v && typeof v === 'object' && !Array.isArray(v) ? v : null;
}

// Noms de propriétés insensibles à la casse, comme le daemon (StateJson :
// PropertyNameCaseInsensitive = true) et le compagnon : « Quality » vaut « quality ».
// Première occurrence gagnante si deux casses coexistent.
function caseless(obj, names) {
    const o = asObject(obj);
    if (!o)
        return null;
    const out = {};
    const lower = new Map(names.map(n => [n.toLowerCase(), n]));
    for (const [k, v] of Object.entries(o)) {
        const name = lower.get(k.toLowerCase());
        if (name && !(name in out))
            out[name] = v;
    }
    return out;
}

// Texte de ~/.config/ultima/settings.json -> {kind: 'hud', hud} | {kind: 'none'} (pas de « hud »)
// | {kind: 'invalid', error}. Ne lève jamais. Le fichier n'est JAMAIS écrit par l'overlay.
export const MAX_SETTINGS_BYTES = 256 * 1024;
export function parseSettingsHud(text) {
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
    if (!asObject(raw))
        return {kind: 'invalid', error: 'racine non objet'};
    const hud = asObject(caseless(raw, ['hud'])?.hud);
    return hud ? {kind: 'hud', hud} : {kind: 'none'};
}

// sources : {file, state, gsettings} — objets « hud » bruts (chacun peut être absent ou partiel).
// Retour : {...réglages, source: {champ: 'fichier'|'état'|'gsettings'|'défaut'}}.
const ORDER = [['file', 'fichier'], ['state', 'état'], ['gsettings', 'gsettings']];
export function resolveHud(sources = {}) {
    const src = asObject(sources) ?? {};
    const fields = Object.keys(READERS);
    const objs = ORDER.map(([k, name]) => [caseless(src[k], fields) ?? {}, name]);
    const out = {source: {}};
    for (const [k, read] of Object.entries(READERS)) {
        out[k] = HUD_DEFAULTS[k];
        out.source[k] = 'défaut';
        for (const [o, name] of objs) {
            const v = read(o[k]);
            if (v !== undefined) {
                out[k] = v;
                out.source[k] = name;
                break;
            }
        }
    }
    return out;
}

export function hudKey(h) {
    return Object.keys(READERS).map(k => `${k}=${h[k]}`).join(' ');
}

// Capacités de l'hôte : chaque niveau n'utilise que des API présentes dans GNOME Shell 50.6 /
// Mutter 18 (vérifié dans Shell-18.typelib : BlurEffect, GLSLEffect.add_glsl_snippet,
// set_uniform_float ; Cogl-18 : SnippetHook ; Clutter-18 : Clone). Si une API manque (autre version de GNOME, typelib différente), on descend
// d'un niveau, jusqu'à `eco` qui n'utilise que des styles St.
// mods : {Shell, Clutter, Cogl} (modules gi:// réels en session, bouchons en test).
export function detectCaps({Shell, Clutter, Cogl} = {}) {
    return {
        blur: typeof Shell?.BlurEffect === 'function' && Shell?.BlurMode?.BACKGROUND !== undefined,
        glsl: typeof Shell?.GLSLEffect === 'function' && Cogl?.SnippetHook?.FRAGMENT !== undefined,
        clone: typeof Clutter?.Clone === 'function',
    };
}

// verre : flou (BlurEffect) + reflet/grain (GLSLEffect) ; loupe : verre + clone (Clutter.Clone).
const NEEDS = {eco: [], verre: ['blur', 'glsl'], loupe: ['blur', 'glsl', 'clone']};

// Niveau effectif : le plus haut niveau ≤ demandé dont toutes les API existent.
// Retour : {level, requested, missing: [api]} (missing = API absentes qui ont causé le repli).
export function effectiveQuality(requested, caps) {
    const want = QUALITIES.includes(requested) ? requested : HUD_DEFAULTS.quality;
    const missingFor = q => NEEDS[q].filter(api => !caps?.[api]);
    const missing = missingFor(want);
    for (let i = QUALITIES.indexOf(want); i >= 0; i--) {
        const q = QUALITIES[i];
        if (missingFor(q).length === 0)
            return {level: q, requested: want, missing};
    }
    return {level: 'eco', requested: want, missing};
}
