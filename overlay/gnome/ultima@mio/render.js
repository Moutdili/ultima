// ULTIMA — mise en page de l'EngineState (module pur : aucune dépendance GNOME Shell, testable
// avec `gjs -m` et `node`).
//
// Skill sts2-design-system §11 (PRIME sur le reste) : « N'afficher que l'actionnable maintenant » ;
// « Le moment de décision prend la place (récompense, shop, événement) : lettre + une raison, rien
// d'autre visible » ; « Rien sans valeur pratique : pas de logo, pas de titre décoratif » ; « pas de
// répétition d'une info déjà visible dans le jeu (PV/or du jeu : seulement si une décision en dépend) ».
//
// Entrée : StateStore.current ({kind: 'none'} | {kind: 'future', version} | {kind: 'state', state}),
//          + options {hud (hud.js resolveHud), detail (épinglé / survol de la carte de décision)}.
// Sortie (« vue ») : {mode, decision: bool, rows: [row]} où row =
//   {key, kind, group, fixed, spans: [{text, tone, bold}], grade?, gradeTone?, lines?}
//   kind : 'alert' | 'decision' | 'line' (résumé d'un module) | 'detail' | 'more'.
//   tone : nom de ton de la vue (palette du jeu / 'muted') — theme.js le traduit en couleur du thème.
//
// Contrat (skill sts2-engine-contract) : note en LETTRE S à F ; Score null -> « — » (jamais 0, jamais
// une valeur inventée) ; health.degraded != null -> affiché en tête ; screen pilote les contextes.

import {gradeTone, MODULE_LABEL, NO_SCORE, SEP, COUNT, MORE, TONE, GRADES, DATA_TONES} from './grammar.js';

export {NO_SCORE, GRADES};

// Modules connus de cet overlay (contrat v1 + `event` v1.2). `route` et `party` (v1.2) sont ignorés
// tant qu'ils n'ont pas de rendu (consommateur tolérant).
export const MODULE_IDS = Object.freeze(['deck', 'enchanted', 'curses', 'relics', 'potions',
    'reward', 'shop', 'event', 'paths', 'deck_score']);
export const DEFAULT_MODULES = Object.freeze(MODULE_IDS.map((id, order) => ({id, enabled: true, order})));

// Préfixe des modules personnalisés / plugins (Ultima.Engine.Contracts.CustomIds.Prefix).
export const CUSTOM_PREFIX = 'x.';
const CUSTOM_MAX_TEXT = 60;      // un texte de module perso plus long est tronqué avec « … »
const CUSTOM_MAX_LABEL = 40;
const CUSTOM_HEALTH_FR = {stale: 'retard', error: 'erreur', disabled: 'désactivé'};

// Colonnes de texte estimées : (largeur utile) / (0,52 × taille de police). 360 px à 14 px -> 45.
export const TEXT_INSET = 32;    // marges gauche + droite du panneau (2 × 16 px)
export function wrapCols(width, size = 14) {
    const w = Number.isFinite(width) ? width : 360;
    const s = Number.isFinite(size) && size > 0 ? size : 14;
    return Math.max(8, Math.min(200, Math.floor((w - TEXT_INSET) / (s * 0.52))));
}
export const WRAP_COLS = wrapCols(360, 14);
export const DECISION_LINES = 3; // hauteur de la carte de décision, en lignes de corps (lettre 44 px)

const MAX_TEXT = 60;             // borne par champ texte venant de l'état
const CONFIDENCE_FR = {high: 'haute', medium: 'moyenne', low: 'basse'};
const ROOM_FR = {
    monster: 'combat', elite: 'élite', unknown: '?', event: '?', restsite: 'feu', rest: 'feu',
    shop: 'boutique', merchant: 'boutique', treasure: 'trésor', boss: 'boss', ancient: 'ancien',
};

// ---------- utilitaires ----------

function txt(v, fallback = '?') {
    if (v === null || v === undefined || v === '')
        return fallback;
    return String(v).replace(/[\r\n\t]+/g, ' ').slice(0, MAX_TEXT);
}

function int(v) {
    return Number.isFinite(v) ? Math.round(v) : null;
}

function arr(v) {
    return Array.isArray(v) ? v : [];
}

function signed(n) {
    const v = int(n);
    if (v === null)
        return NO_SCORE;
    return v > 0 ? `+${v}` : String(v);
}

const span = (text, tone = TONE.text, bold = false) => ({text, tone, bold});
const muted = text => span(text, TONE.muted);

// Notation S à F (contrat : Score.Grade = Grades.FromTotal(Total)).
const GRADE_SCALE = [['S', 85], ['A', 70], ['B', 55], ['C', 40], ['D', 25]];

export function gradeFromTotal(total) {
    const t = int(total);
    if (t === null)
        return null;
    for (const [g, min] of GRADE_SCALE) {
        if (t >= min)
            return g;
    }
    return 'F';
}

// Lettre du score : `grade` publié, sinon dérivée de `total` (états sans `grade`) ; null -> « — ».
export function fmtScore(score) {
    if (!score || typeof score !== 'object')
        return NO_SCORE;
    if (GRADES.includes(score.grade))
        return score.grade;
    return gradeFromTotal(score.total) ?? NO_SCORE;
}

function gradeSpan(score) {
    const g = fmtScore(score);
    return span(g, gradeTone(g), true);
}

function upgradeMark(card) {
    const u = int(card?.upgrade) ?? 0;
    if (u <= 0)
        return '';
    return u === 1 ? '+' : `+${u}`;
}

export function enchantText(e) {
    if (!e)
        return '';
    const amount = int(e.amount);
    return amount === null ? txt(e.name) : `${txt(e.name)} ${amount}`;
}

export function cardLabel(card) {
    return `${txt(card?.name)}${upgradeMark(card)}`;
}

function screenOf(state) {
    return String(state?.screen ?? 'Unknown').toLowerCase().replace(/[^a-z]/g, '');
}

function roomFr(t) {
    return ROOM_FR[String(t ?? '').toLowerCase().replace(/[^a-z]/g, '')] ?? txt(t);
}

function plural(n, one, many) {
    return n > 1 ? many : one;
}

// Regroupe des cartes identiques (id + amélioration + enchantement). Ordre : coût croissant
// (injouable en dernier), malédictions en dernier, puis nom, amélioration, sans enchantement d'abord.
export function groupCards(cards) {
    const groups = new Map();
    for (const c of arr(cards)) {
        if (!c || typeof c !== 'object')
            continue;
        const e = c.enchantment;
        const key = [c.id, int(c.upgrade) ?? 0, e?.id ?? '', int(e?.amount) ?? ''].join('|');
        const g = groups.get(key);
        if (g)
            g.count++;
        else
            groups.set(key, {card: c, count: 1});
    }
    const costKey = c => {
        const v = int(c.cost);
        return v === null || v < 0 ? 99 : v;
    };
    return [...groups.values()].sort((a, b) =>
        (a.card.isCurse === true) - (b.card.isCurse === true) ||
        costKey(a.card) - costKey(b.card) ||
        txt(a.card.name).localeCompare(txt(b.card.name), 'fr') ||
        (int(a.card.upgrade) ?? 0) - (int(b.card.upgrade) ?? 0) ||
        (a.card.enchantment ? 1 : 0) - (b.card.enchantment ? 1 : 0));
}

// Modules activés, dans l'ordre des réglages. Réglages absents/vides -> défaut du contrat.
// Même normalisation que le daemon (Ultima.Daemon/Settings/SettingsStore.Normalize) : ids inconnus
// ignorés, doublon -> première occurrence, module connu ABSENT de la liste -> ajouté (activé) après
// les autres (ex. `event`, v1.2, dans des réglages écrits avant v1.2).
export function orderedModules(settings) {
    const list = arr(settings?.modules);
    const src = list.length ? list : DEFAULT_MODULES;
    const seen = new Set();
    const known = src
        .map((m, i) => ({id: m?.id, enabled: m?.enabled !== false, order: int(m?.order) ?? 1000 + i, i}))
        .filter(m => MODULE_IDS.includes(m.id) && !seen.has(m.id) && seen.add(m.id))
        .sort((a, b) => a.order - b.order || a.i - b.i);
    const missing = MODULE_IDS.filter(id => !seen.has(id)).map(id => ({id, enabled: true}));
    return [...known, ...missing].filter(m => m.enabled).map(m => m.id);
}

// Raison principale d'un score : le terme qui pèse le plus (en valeur absolue), en une ligne.
export function mainReason(score) {
    const terms = arr(score?.terms).filter(t => int(t?.points) !== null);
    if (!terms.length)
        return null;
    const best = terms.reduce((a, b) => (Math.abs(int(b.points)) > Math.abs(int(a.points)) ? b : a));
    return `${txt(best.label)} ${signed(best.points)}`;
}

function termRows(group, score, gradesOnly) {
    if (gradesOnly || !score)
        return [];
    const rows = arr(score.terms).map((t, i) => detailRow(`${group}:t${i}`, group,
        [muted(`${txt(t?.label)} ${signed(t?.points)}`)]));
    const conf = CONFIDENCE_FR[score.confidence] ?? null;
    if (conf)
        rows.push(detailRow(`${group}:conf`, group, [muted(`confiance ${conf}`)]));
    return rows;
}

// ---------- lignes ----------

function row(key, kind, spans, extra = {}) {
    return {key, kind, group: extra.group ?? key, fixed: extra.fixed ?? false, spans, ...extra};
}

function alertRow(key, text, tone = TONE.warn) {
    return row(key, 'alert', [span(text, tone, true)], {fixed: true});
}

function lineRow(id, spans) {
    return row(`line:${id}`, 'line', spans, {group: id, fixed: true});
}

function detailRow(key, group, spans) {
    return row(key, 'detail', spans, {group});
}

// ---------- carte de décision (récompense, boutique, événement) ----------

// options : [{label, score, note?, available}] ; renvoie l'option recommandée : meilleur total parmi
// les options disponibles notées (égalité -> la première, ordre de l'écran) ; null si aucune n'est notée.
export function bestOption(options) {
    let best = null;
    for (const o of options) {
        if (!o.available || int(o.score?.total) === null)
            continue;
        if (!best || int(o.score.total) > int(best.score.total))
            best = o;
    }
    return best;
}

function decisionRows(id, options, {detail, gradesOnly, title, unread}) {
    const rows = [];
    const best = bestOption(options);
    const g = best ? fmtScore(best.score) : NO_SCORE;
    let headline, reason;
    if (unread) {
        headline = title;
        reason = unread;
    } else if (!best) {
        headline = title;
        reason = options.length ? 'notes non calculées' : 'rien à choisir';
    } else {
        headline = best.label;
        reason = gradesOnly ? null : mainReason(best.score) ?? 'aucune raison publiée';
    }
    const head = [span(headline, TONE.text, true)];
    if (best?.note)
        head.push(muted(`${SEP}${best.note}`));
    rows.push(row(`decision:${id}`, 'decision', head, {
        group: id, fixed: true, grade: g, gradeTone: gradeTone(g),
        reason: reason === null ? null : [muted(reason)],
    }));
    if (!detail)
        return rows;
    // Détail (épinglé ou survol de la carte) : toutes les options dans l'ordre de l'écran, puis
    // les termes du choix recommandé.
    options.forEach((o, i) => {
        const spans = [gradeSpan(o.score), span(`${SEP}${o.label}`, o.available ? TONE.text : TONE.muted)];
        if (o.note)
            spans.push(muted(`${SEP}${o.note}`));
        rows.push(detailRow(`${id}:o${i}`, id, spans));
    });
    if (best)
        rows.push(...termRows(id, best.score, gradesOnly));
    return rows;
}

function rewardDecision(state, opts) {
    const r = state.reward;
    if (!r)
        return decisionRows('reward', [], {...opts, title: MODULE_LABEL.reward, unread: 'offre non lue'});
    if (r.alreadyTaken)
        return null;
    const options = arr(r.offered).map(c => ({
        label: cardLabel(c), score: c?.score, available: true,
        note: c?.enchantment ? enchantText(c.enchantment) : null,
    }));
    options.push({label: 'Passer', score: r.skipScore, available: true, note: null});
    return decisionRows('reward', options, {...opts, title: MODULE_LABEL.reward});
}

function shopDecision(state, opts) {
    const shop = state.shop;
    if (!shop)
        return decisionRows('shop', [], {...opts, title: MODULE_LABEL.shop, unread: 'articles non lus'});
    const gold = int(state.run?.gold);
    const options = arr(shop.items).map(it => {
        const price = int(it?.price);
        const affordable = it?.affordable !== false && !(price !== null && gold !== null && price > gold);
        let note = price === null ? '? or' : `${price} or`;
        if (it?.onSale)
            note += ' soldé';
        if (!affordable)
            note += ' trop cher';
        return {label: it?.card ? cardLabel(it.card) : txt(it?.name), score: it?.score, available: affordable, note};
    });
    if (!shop.removalUsed) {
        const rp = int(shop.removalPrice);
        const affordable = !(rp !== null && gold !== null && rp > gold);
        options.push({label: 'Retrait', score: shop.removalScore, available: affordable,
            note: `${rp === null ? '?' : rp} or${affordable ? '' : ' trop cher'}`});
    }
    return decisionRows('shop', options, {...opts, title: MODULE_LABEL.shop});
}

function eventDecision(state, opts) {
    const ev = state.event;
    if (!ev)
        return null; // pas d'EventView publiée (daemon < v1.2) : rien à décider ici
    const options = arr(ev.options).map(o => ({label: txt(o?.label), score: o?.score,
        available: o?.available !== false, note: o?.available === false ? 'indisponible' : null}));
    return decisionRows('event', options, {...opts, title: txt(ev.name, MODULE_LABEL.event)});
}

// ---------- résumé d'une ligne par module (+ détail à l'épinglage) ----------

function deckModule(run, detail) {
    const deck = arr(run?.deck);
    const rows = [lineRow('deck', [span(`${MODULE_LABEL.deck} ${deck.length}`)])];
    if (detail) {
        groupCards(deck).forEach(({card, count}, i) => {
            const spans = [gradeSpan(card.score), span(`${SEP}${count}${COUNT} ${cardLabel(card)}`)];
            if (card.isCurse === true)
                spans.push(span(`${SEP}Malédiction`, TONE.curse));
            else if (card.enchantment)
                spans.push(span(`${SEP}${enchantText(card.enchantment)}`, TONE.enchant));
            rows.push(detailRow(`deck:${i}`, 'deck', spans));
        });
    }
    return rows;
}

function enchantedModule(run, detail) {
    const cards = arr(run?.deck).filter(c => c?.enchantment);
    if (!cards.length)
        return []; // rien à signaler : pas de ligne « 0 » (skill §11, rien sans valeur pratique)
    const rows = [lineRow('enchanted', [span(`${MODULE_LABEL.enchanted} ${cards.length}`, TONE.enchant)])];
    if (detail) {
        groupCards(cards).forEach(({card, count}, i) => rows.push(detailRow(`enchanted:${i}`, 'enchanted',
            [span(`${count}${COUNT} ${cardLabel(card)}`), span(`${SEP}${enchantText(card.enchantment)}`, TONE.enchant)])));
    }
    return rows;
}

function cursesModule(run, detail) {
    const cards = arr(run?.deck).filter(c => c?.isCurse === true);
    if (!cards.length)
        return [];
    // Violet + le MOT « Malédiction » (skill design §9 ; jamais la couleur seule).
    const rows = [lineRow('curses', [span(`${plural(cards.length, 'Malédiction', 'Malédictions')} ${cards.length}`, TONE.curse, true)])];
    if (detail) {
        groupCards(cards).forEach(({card, count}, i) => rows.push(detailRow(`curses:${i}`, 'curses',
            [span(`${count}${COUNT} ${cardLabel(card)}`, TONE.curse)])));
    }
    return rows;
}

function relicsModule(run, detail) {
    const relics = arr(run?.relics);
    if (!relics.length)
        return [];
    const rows = [lineRow('relics', [span(`${MODULE_LABEL.relics} ${relics.length}`)])];
    if (detail) {
        relics.forEach((r, i) => rows.push(detailRow(`relics:${i}`, 'relics',
            [gradeSpan(r?.score), span(`${SEP}${txt(r?.name)}`)])));
    }
    return rows;
}

function potionsModule(run, detail) {
    const potions = arr(run?.potions);
    if (!potions.length)
        return [];
    const rows = [lineRow('potions', [span(`${MODULE_LABEL.potions} ${potions.length}`)])];
    if (detail) {
        potions.forEach((p, i) => rows.push(detailRow(`potions:${i}`, 'potions',
            [gradeSpan(p?.score), span(`${SEP}${txt(p?.name)}`)])));
    }
    return rows;
}

function deckScoreModule(state, {detail, gradesOnly}) {
    const s = state.deck?.score ?? null;
    const rows = [lineRow('deck_score', [span(`${MODULE_LABEL.deck_score} `), gradeSpan(s)])];
    if (detail && state.deck) {
        rows.push(...termRows('deck_score', s, gradesOnly));
        const pot = arr(state.deck.potentialArchetypes).map(a => txt(a));
        if (pot.length && !gradesOnly)
            rows.push(detailRow('deck_score:pistes', 'deck_score', [muted(`pistes${SEP}${pot.join(', ')}`)]));
    }
    return rows;
}

function pathsModule(paths, detail) {
    if (!paths)
        return [lineRow('paths', [span(`${MODULE_LABEL.paths} `), muted(NO_SCORE)])];
    const total = int(paths.totalPaths);
    const choices = [...arr(paths.nextChoices)].sort((a, b) => (int(a?.col) ?? 0) - (int(b?.col) ?? 0));
    const rows = [lineRow('paths', [span(`${MODULE_LABEL.paths} ${total ?? '?'}`),
        muted(`${SEP}${choices.length} ${plural(choices.length, 'choix', 'choix')}`)])];
    if (detail) {
        choices.forEach((c, i) => {
            const n = int(c?.pathCount);
            const dmg = int(c?.expectedDamage);
            rows.push(detailRow(`paths:${i}`, 'paths', [span(`${roomFr(c?.roomType)}`),
                muted(`${SEP}${n ?? '?'} ${plural(n ?? 0, 'chemin', 'chemins')}${dmg === null ? '' : `${SEP}dégâts ~${dmg}`}`)]));
        });
    }
    return rows;
}

// Écrans autorisés : `screens` absent/vide = écrans par défaut du module. Le contrat donne les
// écrans en camelCase (« restSite ») ; screenOf() ramène l'écran courant au même alphabet.
export function screensAllow(screens, screen) {
    if (!Array.isArray(screens) || !screens.length)
        return true;
    return screens.some(s => screenOf({screen: s}) === screen);
}

// Contexte de chaque module selon l'écran. `screens` (ModuleSetting.Screens, v1.4) primes.
function moduleVisible(id, screen, hud, screens) {
    if (!screensAllow(screens, screen))
        return false;
    switch (id) {
    case 'reward': case 'shop': case 'event':
        return false; // modules de décision : rendus par la carte de décision
    case 'paths': return screen === 'map';
    case 'deck_score': return screen !== 'combat' || hud.deckScoreInCombat === true;
    default: return true;
    }
}

// ---------- modules personnalisés et plugins (EngineState.Custom, contrat v1.4) ----------
//
// Le daemon ÉVALUE (modules déclaratifs, plugins isolés) et publie `state.custom` ; l'extension ne
// fait que dessiner : aucun code tiers n'est chargé, importé ni évalué ici. Un champ manquant ou
// invalide est ignoré (libellé -> id, ton inconnu -> text, santé inconnue -> atténuée + mot).

function customText(v, max) {
    if (v === null || v === undefined || v === '')
        return '';
    const s = String(v).replace(/[\r\n\t]+/g, ' ').trim();
    const chars = [...s];
    if (chars.length <= max)
        return s;
    return `${chars.slice(0, Math.max(1, max - 1)).join('')}${MORE}`;
}

export function customModules(state) {
    const list = arr(state?.custom);
    const out = [];
    const seen = new Set();
    for (const m of list) {
        if (!m || typeof m !== 'object')
            continue;
        const id = typeof m.id === 'string' ? m.id.trim() : '';
        if (!id.startsWith(CUSTOM_PREFIX) || seen.has(id))
            continue;
        seen.add(id);
        out.push({
            id,
            label: customText(m.label, CUSTOM_MAX_LABEL) || id,
            source: m.source === 'plugin' ? 'plugin' : 'module',
            screens: Array.isArray(m.screens) ? m.screens.filter(s => typeof s === 'string') : null,
            health: typeof m.health === 'string' ? m.health : 'ok',
            rows: arr(m.rows).filter(r => r && typeof r === 'object').map(r => ({
                text: customText(r.text, CUSTOM_MAX_TEXT),
                tone: DATA_TONES.includes(r.tone) ? r.tone : TONE.text,
                grade: GRADES.includes(r.grade) ? r.grade : null,
            })).filter(r => r.text),
        });
    }
    return out;
}

// Lignes d'un module perso : une ligne d'en-tête (libellé, santé) puis ses lignes. Replié, la
// première ligne seulement (grammaire §11 : une ligne par module) ; épinglé/survol, toutes.
function customRows(m, opts) {
    const bad = m.health !== 'ok';
    const head = [span(`${m.label}${m.rows.length ? ` ${m.rows.length}` : ''}`, bad ? TONE.muted : TONE.text)];
    if (bad)
        head.push(span(`${SEP}${CUSTOM_HEALTH_FR[m.health] ?? customText(m.health, 20)}`, TONE.muted));
    const rows = [lineRow(m.id, head)];
    const shown = opts.detail ? m.rows : m.rows.slice(0, 1);
    shown.forEach((r, i) => {
        const spans = [];
        if (r.grade)
            spans.push(span(r.grade, gradeTone(r.grade), true), muted(SEP));
        // Santé ≠ ok : le texte du module est atténué, mais la note garde son ton.
        spans.push(span(r.text, bad && !r.grade ? TONE.muted : r.tone));
        rows.push(detailRow(`${m.id}:${i}`, m.id, spans));
    });
    if (!opts.detail && m.rows.length > 1)
        rows.push(row(`more:${m.id}`, 'more', [muted(`${MORE} +${m.rows.length - 1}`)], {group: m.id, fixed: true}));
    return rows;
}

// Entrées affichables, dans l'ordre des réglages : modules connus + modules perso (ids `x.`),
// réglages absents -> tout activé (même normalisation que le daemon : id inconnu ignoré, doublon ->
// première occurrence, module connu ABSENT de la liste -> ajouté après les autres).
export function orderedEntries(settings, custom = []) {
    const list = arr(settings?.modules);
    const known = new Set([...MODULE_IDS, ...custom.map(m => m.id)]);
    const src = list.length ? list : [...MODULE_IDS.map(id => ({id})), ...custom.map(m => ({id: m.id}))];
    const seen = new Set();
    const entries = src
        .map((m, i) => ({id: m?.id, enabled: m?.enabled !== false, order: int(m?.order) ?? 1000 + i, i,
            screens: Array.isArray(m?.screens) ? m.screens : null}))
        .filter(m => known.has(m.id) && !seen.has(m.id) && seen.add(m.id))
        .sort((a, b) => a.order - b.order || a.i - b.i);
    const missing = [...MODULE_IDS, ...custom.map(m => m.id)].filter(id => !seen.has(id))
        .map(id => ({id, enabled: true, screens: null}));
    return [...entries, ...missing].filter(e => e.enabled);
}


function moduleRows(id, state, opts) {
    const run = state.run;
    switch (id) {
    case 'deck': return deckModule(run, opts.detail);
    case 'enchanted': return enchantedModule(run, opts.detail);
    case 'curses': return cursesModule(run, opts.detail);
    case 'relics': return relicsModule(run, opts.detail);
    case 'potions': return potionsModule(run, opts.detail);
    case 'deck_score': return deckScoreModule(state, opts);
    case 'paths': return pathsModule(state.paths, opts.detail);
    default: return [];
    }
}

const DECISION_SCREENS = {reward: rewardDecision, shop: shopDecision, event: eventDecision};

// ---------- vue complète ----------

export function buildView(current, {hud = {}, detail = false} = {}) {
    const opts = {detail: !!detail, gradesOnly: hud.gradesOnly === true};
    if (!current || current.kind === 'none')
        return {mode: 'waiting', decision: false, rows: [alertRow('waiting', 'En attente du daemon', TONE.muted)]};
    if (current.kind === 'future') {
        return {mode: 'future', decision: false,
            rows: [alertRow('future', `Mettre à jour ULTIMA (état v${current.version})`, TONE.danger)]};
    }

    const state = current.state ?? {};
    const rows = [];
    const degraded = state.health?.degraded;
    if (degraded !== null && degraded !== undefined && degraded !== '')
        rows.push(alertRow('degraded', `Dégradé${SEP}${txt(degraded)}`));

    if (state.health?.gameRunning === false) {
        rows.push(alertRow('waiting', 'En attente du jeu', TONE.muted));
        return {mode: 'waiting', decision: false, rows};
    }
    if (!state.run) {
        rows.push(alertRow('norun', 'Pas de run en cours', TONE.muted));
        return {mode: 'norun', decision: false, rows};
    }

    const screen = screenOf(state);
    const custom = customModules(state);
    const entries = orderedEntries(state.settings, custom);
    const ids = entries.map(e => e.id);
    // Moment de décision : la carte prend la place ; le reste n'apparaît qu'à l'épinglage.
    let decision = null;
    if (DECISION_SCREENS[screen] && ids.includes(screen))
        decision = DECISION_SCREENS[screen](state, opts);
    if (decision) {
        rows.push(...decision);
        if (!opts.detail)
            return {mode: 'run', decision: true, rows};
    }
    const byId = new Map(custom.map(m => [m.id, m]));
    for (const e of entries) {
        const detail = opts.detail && !decision;
        if (byId.has(e.id)) {
            const m = byId.get(e.id);
            const screens = m.screens && m.screens.length ? m.screens : e.screens;
            if (screensAllow(screens, screen))
                rows.push(...customRows(m, {...opts, detail}));
        } else if (moduleVisible(e.id, screen, hud, e.screens)) {
            rows.push(...moduleRows(e.id, state, {...opts, detail}));
        }
    }
    return {mode: 'run', decision: !!decision, rows};
}

// ---------- ajustement à la hauteur du panneau ----------

function visualLines(text, cols) {
    return Math.max(1, Math.ceil([...text].length / cols));
}

export function rowText(r) {
    return r.spans.map(s => s.text).join('');
}

export function rowCost(r, cols = WRAP_COLS) {
    if (r.kind === 'decision')
        return DECISION_LINES;
    return visualLines(rowText(r), cols);
}

// Jamais de coupure silencieuse : tant que ça déborde, on retire la dernière ligne de détail du
// groupe qui en a le plus (égalité -> le plus bas), et on l'annonce (« … +N »). Les lignes fixes
// (alertes, carte de décision, résumés) ne sont jamais retirées.
// opts : {maxLines, cols}. Retourne une NOUVELLE vue (l'entrée n'est pas modifiée).
export function fitView(view, {maxLines, cols = WRAP_COLS}) {
    const rows = [...view.rows];
    const hidden = new Map();
    const cost = () => rows.reduce((n, r) => n + rowCost(r, cols), 0) + hidden.size;
    let guard = 10000;
    while (cost() > maxLines && guard-- > 0) {
        const counts = new Map();
        for (const r of rows) {
            if (!r.fixed)
                counts.set(r.group, (counts.get(r.group) ?? 0) + 1);
        }
        let victim = null;
        for (const [g, n] of counts) {
            if (!victim || n >= counts.get(victim))
                victim = g;
        }
        if (!victim)
            break;
        const idx = rows.map(r => !r.fixed && r.group === victim).lastIndexOf(true);
        rows.splice(idx, 1);
        hidden.set(victim, (hidden.get(victim) ?? 0) + 1);
    }
    // Marque « … +N » juste après la dernière ligne restante du groupe.
    for (const [g, n] of hidden) {
        let at = -1;
        rows.forEach((r, i) => {
            if (r.group === g)
                at = i;
        });
        rows.splice(at + 1, 0, row(`more:${g}`, 'more', [muted(`${MORE} +${n}`)], {group: g, fixed: true}));
    }
    return {...view, rows};
}

export function viewToText(view) {
    return view.rows.map(r => {
        if (r.kind === 'decision')
            return `[${r.grade}] ${rowText(r)}${r.reason ? `\n    ${r.reason.map(s => s.text).join('')}` : ''}`;
        if (r.kind === 'detail' || r.kind === 'more')
            return `  ${rowText(r)}`;
        return rowText(r);
    }).join('\n');
}

// ---------- balisage Pango (couleurs du THÈME uniquement, via theme.js) ----------

function escapeMarkup(s) {
    return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
        .replace(/'/g, '&#39;').replace(/"/g, '&quot;');
}

// spans -> balisage Pango. resolveTone(tone) -> {hex, alpha} (theme.js : tons du thème courant).
// Un ton que le thème ne connaît pas (hex null) s'affiche avec la couleur par défaut de St : aucune
// couleur n'est inventée ici.
export function spansMarkup(spans, resolveTone) {
    return spans.map(s => {
        const {hex, alpha} = resolveTone(s.tone) ?? {hex: null, alpha: 1};
        const fg = hex ? ` foreground="${hex}"` : '';
        const a = hex && alpha < 1 ? ` fgalpha="${Math.round(alpha * 100)}%"` : '';
        const w = s.bold ? ' weight="bold"' : '';
        return `<span${fg}${a}${w}>${escapeMarkup(s.text)}</span>`;
    }).join('');
}
