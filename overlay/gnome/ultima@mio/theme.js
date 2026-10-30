// ULTIMA — résolveur de thème (module pur : aucune dépendance GNOME Shell, testable avec `gjs -m`
// et `node`). Contrat : docs/44-personnalisation.md §3, §4 ; schéma schemas/theme.schema.json.
//
// Chaîne de résolution (extension) :
//   appearance.hud.theme  →  extends (récursif)  →  appearance.hud.tones (surcharges)  →  rendu
// Le thème de référence `sts` est la base de TOUTE chaîne : un thème incomplet n'efface donc jamais
// les tons qu'il ne redéfinit pas. Un thème cassé (JSON invalide, api ≠ 1, cycle d'`extends`,
// parent introuvable) ne lève JAMAIS : on retombe sur `sts` et on rapporte le problème dans
// `problems` pour que l'appelant écrive une ligne `[ULTIMA]` dans le journal.
//
// AUCUNE couleur n'est écrite ici : les seules valeurs hexadécimales du HUD vivent dans les
// fichiers de thème (`themes/*.json`, dont `ultima@mio/themes/sts.json` livré avec l'extension).
// Ce module ne fait que lire, valider, fusionner et convertir.

// Tons du contrat (Ultima.Engine.Contracts.Tones) + tons de notes + réservés au thème.
export const TONE_KEYS = Object.freeze([
    'text', 'muted', 'accent', 'danger', 'ok', 'warn', 'curse', 'enchant', 'info',
    'grade.S', 'grade.A', 'grade.B', 'grade.C', 'grade.D', 'grade.F',
    'background', 'border',
]);

// Tons des notes S-F (mêmes lettres que grammar.js GRADES).
export const GRADE_TONE_KEYS = Object.freeze(['grade.S', 'grade.A', 'grade.B', 'grade.C', 'grade.D', 'grade.F']);

// Vocabulaire v1.3 des surfaces (palette du jeu) → tons sémantiques v1.4. Pont explicite : la vue
// (render.js/grammar.js) et le compagnon continuent de nommer les tons de la palette, le thème
// décide de la couleur. « gray » (note non calculée) suit « muted » : le contrat v1.4 n'a pas de
// ton « gris » et le gris du jeu est un texte secondaire.
export const TONE_ALIASES = Object.freeze({
    cream: 'text',
    gold: 'accent',
    red: 'danger',
    green: 'ok',
    blue: 'info',
    orange: 'warn',
    purple: 'curse',
    aqua: 'enchant',
    lightGray: 'muted',
    gray: 'muted',
});

export const THEME_ID = /^[a-z0-9][a-z0-9-]{0,39}$/;
export const AUTO_THEME = 'auto-perso';
export const DEFAULT_THEME = 'sts';
export const MAX_THEME_BYTES = 64 * 1024;
export const MAX_EXTENDS_DEPTH = 8;
export const FONT_SIZE_RANGE = Object.freeze([12, 24]);
// Nom de famille de police : lettres/chiffres/espaces/._- seulement. Une valeur qui contient un
// guillemet, un point-virgule ou un antislash est REJETÉE (on l'injecte dans du CSS St).
export const FONT_FAMILY = /^[\p{L}\p{N}][\p{L}\p{N} ._-]{0,79}$/u;

const HEX6 = /^#([0-9A-Fa-f]{2})([0-9A-Fa-f]{2})([0-9A-Fa-f]{2})$/;
const HEX8 = /^#([0-9A-Fa-f]{2})([0-9A-Fa-f]{2})([0-9A-Fa-f]{2})([0-9A-Fa-f]{2})$/;

function isObj(v) {
    return v !== null && typeof v === 'object' && !Array.isArray(v);
}

function finite(v) {
    return typeof v === 'number' && Number.isFinite(v);
}

// Ton canonique pour un nom quelconque (alias v1.3 compris) ; null si inconnu.
export function normalizeTone(tone) {
    if (typeof tone !== 'string')
        return null;
    const t = tone.trim();
    if (TONE_KEYS.includes(t))
        return t;
    const alias = TONE_ALIASES[t];
    return alias ?? null;
}

// '#RRGGBB' | '#RRGGBBAA' -> {hex: '#RRGGBB', alpha: 0..1} ; null si invalide (jamais d'exception).
export function parseColor(value) {
    if (typeof value !== 'string')
        return null;
    const s = value.trim();
    let m = HEX6.exec(s);
    if (m)
        return {hex: `#${m[1]}${m[2]}${m[3]}`.toUpperCase(), alpha: 1};
    m = HEX8.exec(s);
    if (m)
        return {hex: `#${m[1]}${m[2]}${m[3]}`.toUpperCase(), alpha: parseInt(m[4], 16) / 255};
    return null;
}

// '#RRGGBB' + alpha [0..1] -> 'rgba(r, g, b, a)' pour les styles St ; null si la couleur est
// absente (thème introuvable) pour que l'appelant n'écrive simplement pas la propriété.
export function rgba(hex, alpha = 1) {
    const c = parseColor(hex);
    if (!c)
        return null;
    const a = Math.min(1, Math.max(0, finite(alpha) ? alpha : 1));
    const r = parseInt(c.hex.slice(1, 3), 16);
    const g = parseInt(c.hex.slice(3, 5), 16);
    const b = parseInt(c.hex.slice(5, 7), 16);
    return `rgba(${r}, ${g}, ${b}, ${a})`;
}

// Couleur d'un ton : {hex, alpha} (hex null si le thème ne le donne pas). `alpha` explicite
// remplace celui du thème (liseré, ombre).
export function toneColor(theme, tone, alpha = null) {
    const key = normalizeTone(tone);
    const c = key ? theme?.tones?.[key] : null;
    if (!c)
        return {hex: null, alpha: 1};
    return {hex: c.hex, alpha: finite(alpha) ? Math.min(1, Math.max(0, alpha)) : c.alpha};
}

// 'rgba(...)' d'un ton, ou null si le thème ne le connaît pas.
export function toneCss(theme, tone, alpha = null) {
    const c = toneColor(theme, tone, alpha);
    return c.hex ? rgba(c.hex, c.alpha) : null;
}

// Teinte du verre : `glass.tint` (repli : ton `background`), opacité `glass.tintOpacity`.
// Le niveau `eco` (aplat, sans flou) ajoute un voile pour rester lisible sans flou : c'est un
// réglage de SURFACE (opacité), pas une couleur.
export const ECO_EXTRA_ALPHA = 0.13;
export function glassTint(theme, quality = 'verre') {
    const tint = theme?.glass?.tint ?? theme?.tones?.background ?? null;
    if (!tint)
        return {hex: null, alpha: 1};
    const base = finite(theme?.glass?.tintOpacity) ? theme.glass.tintOpacity : 0.55;
    const alpha = quality === 'eco' ? Math.min(1, base + ECO_EXTRA_ALPHA) : base;
    return {hex: tint.hex, alpha: tint.alpha * alpha};
}

// Liseré du panneau : ton `border` ; repli accent @ 0.4 (valeur de charte docs/17 §3).
export function borderColor(theme) {
    const border = toneColor(theme, 'border');
    if (border.hex)
        return border;
    return toneColor(theme, 'accent', 0.4);
}

// Ombre : ton `background` très transparent (aucun ton dédié au contrat v1.4).
export function shadowColor(theme) {
    return toneColor(theme, 'background', 0.45);
}

// Personnage -> id de thème (« auto-perso »). Null si inconnu (l'appelant retombe sur `sts`).
const AUTO_IDS = Object.freeze({
    ironclad: 'ironclad', theironclad: 'ironclad',
    silent: 'silent', thesilent: 'silent',
    defect: 'defect', thedefect: 'defect',
    regent: 'regent', theregent: 'regent',
    necrobinder: 'necrobinder', thenecrobinder: 'necrobinder',
    watcher: 'watcher', thewatcher: 'watcher',
});
export function autoThemeId(characterId) {
    const s = String(characterId ?? '').toLowerCase().replace(/^character\./, '').replace(/[^a-z]/g, '');
    return AUTO_IDS[s] ?? null;
}

// ---------- lecture d'un fichier de thème ----------

function readTones(raw, problems, where) {
    const out = {};
    if (raw === undefined)
        return out;
    if (!isObj(raw)) {
        problems.push(`${where} : « tones » n'est pas un objet`);
        return out;
    }
    for (const [name, value] of Object.entries(raw)) {
        if (!TONE_KEYS.includes(name)) {
            problems.push(`${where} : ton inconnu « ${name} » ignoré`);
            continue;
        }
        const c = parseColor(value);
        if (!c)
            problems.push(`${where} : couleur invalide pour le ton « ${name} »`);
        else
            out[name] = c;
    }
    return out;
}

function readGlass(raw, problems, where) {
    const out = {};
    if (raw === undefined)
        return out;
    if (!isObj(raw)) {
        problems.push(`${where} : « glass » n'est pas un objet`);
        return out;
    }
    if (raw.tint !== undefined) {
        const c = parseColor(raw.tint);
        if (c)
            out.tint = c;
        else
            problems.push(`${where} : teinte de verre invalide`);
    }
    if (raw.tintOpacity !== undefined) {
        if (finite(raw.tintOpacity) && raw.tintOpacity >= 0 && raw.tintOpacity <= 1)
            out.tintOpacity = raw.tintOpacity;
        else
            problems.push(`${where} : tintOpacity doit être un nombre entre 0 et 1`);
    }
    return out;
}

function readFont(raw, problems, where) {
    const out = {};
    if (raw === undefined)
        return out;
    if (!isObj(raw)) {
        problems.push(`${where} : « font » n'est pas un objet`);
        return out;
    }
    if (raw.family !== undefined) {
        if (typeof raw.family === 'string' && FONT_FAMILY.test(raw.family.trim()))
            out.family = raw.family.trim();
        else
            problems.push(`${where} : famille de police invalide`);
    }
    if (raw.size !== undefined) {
        if (Number.isInteger(raw.size) && raw.size >= FONT_SIZE_RANGE[0] && raw.size <= FONT_SIZE_RANGE[1])
            out.size = raw.size;
        else
            problems.push(`${where} : taille de police hors de ${FONT_SIZE_RANGE[0]}-${FONT_SIZE_RANGE[1]} px`);
    }
    return out;
}

// Texte d'un fichier de thème -> {ok, theme, problems}. `fileId` : id attendu (nom du fichier).
// ok = false : le thème est inutilisable en entier (api inconnue, racine non objet, JSON cassé).
export function parseTheme(text, {fileId = null} = {}) {
    const problems = [];
    if (typeof text !== 'string')
        return {ok: false, theme: null, problems: ['contenu non textuel']};
    if (text.length > MAX_THEME_BYTES)
        return {ok: false, theme: null, problems: [`fichier trop gros (${text.length} octets)`]};
    let raw;
    try {
        raw = JSON.parse(text);
    } catch (e) {
        return {ok: false, theme: null, problems: [`JSON invalide : ${e.message}`]};
    }
    if (!isObj(raw))
        return {ok: false, theme: null, problems: ['racine non objet']};
    const where = `thème « ${fileId ?? raw.id ?? '?' } »`;
    let id = typeof raw.id === 'string' && THEME_ID.test(raw.id) ? raw.id : null;
    if (!id && fileId && THEME_ID.test(fileId)) {
        id = fileId;
        problems.push(`${where} : champ « id » absent ou invalide, id du fichier utilisé`);
    }
    if (!id)
        return {ok: false, theme: null, problems: [...problems, `${where} : id absent ou invalide`]};
    if (fileId && fileId !== id)
        problems.push(`${where} : id « ${id} » différent du nom de fichier « ${fileId} »`);
    if (raw.api !== 1)
        return {ok: false, theme: null, problems: [...problems, `${where} : api ${JSON.stringify(raw.api)} inconnue (1 attendue)`]};
    let extendsId = null;
    if (raw.extends !== undefined) {
        if (typeof raw.extends === 'string' && THEME_ID.test(raw.extends))
            extendsId = raw.extends;
        else
            problems.push(`${where} : « extends » invalide, ignoré`);
    }
    const label = typeof raw.label === 'string' && raw.label.trim() ? raw.label.trim().slice(0, 40) : id;
    if (label !== raw.label && raw.label !== undefined)
        problems.push(`${where} : libellé invalide, id utilisé`);
    return {
        ok: true,
        problems,
        theme: {
            id,
            label,
            api: 1,
            extends: extendsId,
            tones: readTones(raw.tones, problems, where),
            glass: readGlass(raw.glass, problems, where),
            font: readFont(raw.font, problems, where),
        },
    };
}

// ---------- bibliothèque et chaîne d'héritage ----------

// dirs : du plus PRIORITAIRE au moins prioritaire ({path, files:[{name, text}]}) — l'appelant fait
// l'E/S (Gio en session, fs en test) ; ce module ne touche pas au disque (donc testable partout).
// Retour : {themes: Map<id, theme>, problems: [string]}.
export function buildLibrary(sources = []) {
    const themes = new Map();
    const problems = [];
    const seen = new Set();
    for (const src of Array.isArray(sources) ? sources : []) {
        for (const file of Array.isArray(src?.files) ? src.files : []) {
            const name = String(file?.name ?? '');
            if (!name.endsWith('.json'))
                continue;
            const fileId = name.slice(0, -'.json'.length);
            const r = parseTheme(file.text, {fileId});
            for (const p of r.problems)
                problems.push(`${src.path ? `${src.path}/` : ''}${name} : ${p}`);
            if (!r.ok || seen.has(r.theme.id))
                continue; // le premier fichier d'un id gagne (dirs du plus prioritaire au moins)
            seen.add(r.theme.id);
            themes.set(r.theme.id, r.theme);
        }
    }
    return {themes, problems};
}

// Chaîne [base … thème demandé] avec `sts` en tête ; cycles et parents manquants signalés.
export function chainFor(id, library) {
    const lib = library instanceof Map ? library : new Map();
    const problems = [];
    const up = [];
    const seen = new Set();
    let cur = id;
    for (let depth = 0; cur && depth < MAX_EXTENDS_DEPTH; depth++) {
        if (seen.has(cur)) {
            problems.push(`cycle d'« extends » sur « ${cur} »`);
            return {names: [], problems};
        }
        seen.add(cur);
        const t = lib.get(cur);
        if (!t) {
            problems.push(`thème « ${cur} » introuvable`);
            break;
        }
        up.push(cur);
        cur = t.extends;
    }
    if (cur && up.length >= MAX_EXTENDS_DEPTH)
        problems.push(`« extends » trop profond (${MAX_EXTENDS_DEPTH} niveaux maximum)`);
    const names = up.reverse();
    if (names[0] !== DEFAULT_THEME && lib.has(DEFAULT_THEME))
        names.unshift(DEFAULT_THEME);
    return {names, problems};
}

// Fusion d'une chaîne (base d'abord, thème demandé en dernier).
export function mergeChain(names, library) {
    const lib = library instanceof Map ? library : new Map();
    const out = {id: null, label: '', api: 1, tones: {}, glass: {}, font: {}, chain: []};
    for (const name of Array.isArray(names) ? names : []) {
        const t = lib.get(name);
        if (!t)
            continue;
        Object.assign(out.tones, t.tones);
        Object.assign(out.glass, t.glass);
        Object.assign(out.font, t.font);
        out.chain.push(name);
    }
    out.id = out.chain.length ? out.chain[out.chain.length - 1] : null;
    out.label = lib.get(out.id)?.label ?? (out.id ?? '');
    return out;
}

// Surcharges de `appearance.hud.tones` : {ton: '#RRGGBB[AA]'} validées une à une.
export function applyOverrides(theme, overrides, problems = []) {
    if (overrides === undefined || overrides === null)
        return theme;
    if (!isObj(overrides)) {
        problems.push('appearance.hud.tones : objet attendu, ignoré');
        return theme;
    }
    for (const [name, value] of Object.entries(overrides)) {
        const key = normalizeTone(name);
        if (!key)
            problems.push(`appearance.hud.tones : ton inconnu « ${name} » ignoré`);
        else if (!parseColor(value))
            problems.push(`appearance.hud.tones : couleur invalide pour « ${name} »`);
        else
            theme.tones[key] = parseColor(value);
    }
    return theme;
}

// Résolution complète. Ne lève jamais.
//   requested : appearance.hud.theme ('sts' | id | 'auto-perso')
//   library   : Map<id, theme> (buildLibrary().themes)
//   autoId    : id du thème du personnage (theme.autoThemeId(state.run.characterId)), ou null
//   overrides : appearance.hud.tones
// Retour : {requested, id, theme, chain, problems, auto}
export function resolveTheme({requested = DEFAULT_THEME, library = null, autoId = null, overrides = null} = {}) {
    const lib = library instanceof Map ? library : new Map();
    const problems = [];
    let want = typeof requested === 'string' ? requested.trim().toLowerCase() : '';
    let auto = false;
    if (want === AUTO_THEME) {
        auto = true;
        if (autoId && lib.has(autoId)) {
            want = autoId;
        } else {
            problems.push(autoId
                ? `auto-perso : thème « ${autoId} » absent, repli « ${DEFAULT_THEME} »`
                : `auto-perso hors run : repli « ${DEFAULT_THEME} »`);
            want = DEFAULT_THEME;
        }
    }
    if (!want || !THEME_ID.test(want)) {
        if (want)
            problems.push(`thème « ${want} » invalide, repli « ${DEFAULT_THEME} »`);
        want = DEFAULT_THEME;
    }
    let chain;
    if (!lib.has(want)) {
        problems.push(`thème « ${want} » introuvable, repli « ${DEFAULT_THEME} »`);
        chain = lib.has(DEFAULT_THEME) ? [DEFAULT_THEME] : [];
    } else {
        const c = chainFor(want, lib);
        if (!c.names.length) {
            problems.push(...c.problems, `repli « ${DEFAULT_THEME} »`);
            chain = lib.has(DEFAULT_THEME) ? [DEFAULT_THEME] : [];
        } else {
            problems.push(...c.problems);
            chain = c.names;
        }
    }
    const theme = applyOverrides(mergeChain(chain, lib), overrides, problems);
    if (!theme.id && lib.has(DEFAULT_THEME))
        theme.id = DEFAULT_THEME;
    return {requested: requested ?? DEFAULT_THEME, id: theme.id, theme, chain, problems, auto};
}

// Clé stable pour ne réappliquer un thème que s'il change vraiment.
export function themeKey(resolved) {
    const t = resolved?.theme ?? null;
    return JSON.stringify([resolved?.id ?? null, t?.tones ?? null, t?.glass ?? null, t?.font ?? null,
        resolved?.chain ?? []]);
}

// Libellé journalisable : « id (extends a → b) ».
export function describeTheme(resolved) {
    const chain = resolved?.chain ?? [];
    return chain.length > 1 ? `${resolved.id} (${chain.join(' → ')})` : String(resolved?.id ?? '—');
}
