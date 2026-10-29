// ULTIMA — rendu TEXTE de l'EngineState (module pur : aucune dépendance GNOME Shell, testable
// avec `gjs -m` et `node`). Décision Mio (2026-10-29) : design EN DERNIER -> texte brut lisible.
//
// Entrée : StateStore.current ({kind: 'none'} | {kind: 'future', version} | {kind: 'state', state}).
// Sortie (« vue ») : {blocks: [{key, tone, title, body: [string], fixed, hidden}], rail: [ton × 3]}.
//   - blocks dans l'ordre d'affichage : 'degraded' (en tête), 'title', puis les modules dans
//     l'ordre de state.settings.modules (désactivés absents) ;
//   - tone : nom de ton de palette.js (jamais une couleur brute) ;
//   - fixed : bloc jamais tronqué par fitView (dégradé, titre, malédictions).
//
// Sémantique du contrat (skill sts2-engine-contract) : score affiché en LETTRE S à F (la note du
// deck détaille total et termes) ; Score null -> « — » (jamais 0, jamais F inventé) ;
// health.degraded != null -> affiché en tête ; screen pilote reward/shop/paths.

export const MODULE_IDS = Object.freeze(['deck', 'enchanted', 'curses', 'relics', 'potions',
    'reward', 'shop', 'paths', 'deck_score']);
// OverlaySettings.Default (EngineContracts.cs) : ordre voulu par Mio.
export const DEFAULT_MODULES = Object.freeze(MODULE_IDS.map((id, order) => ({id, enabled: true, order})));

export const NO_SCORE = '—';
export const WRAP_COLS = 32;     // estimation : 232 px de texte à 14 px ≈ 32 caractères par ligne

const MAX_TEXT = 60;             // borne par champ texte venant de l'état
const CONFIDENCE_FR = {high: 'haute', medium: 'moyenne', low: 'basse'};
const KIND_FR = {card: 'carte', relic: 'relique', potion: 'potion'};
const ROOM_FR = {
    monster: 'combat', elite: 'élite', unknown: '?', event: '?', restsite: 'feu', rest: 'feu',
    shop: 'shop', merchant: 'shop', treasure: 'trésor', boss: 'boss',
};
const ROOM_COUNTS = [['monster', 'combats'], ['elite', 'élites'], ['unknown', '?'], ['rest', 'feux'],
    ['shop', 'shops'], ['treasure', 'trésors']];

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

// Notation S à F (décision Mio, contrat : Score.Grade = Grades.FromTotal(Total)).
export const GRADES = Object.freeze(['S', 'A', 'B', 'C', 'D', 'F']);
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

function signed(n) {
    const v = int(n);
    if (v === null)
        return NO_SCORE;
    return v > 0 ? `+${v}` : String(v);
}

function upgradeMark(card) {
    const u = int(card?.upgrade) ?? 0;
    if (u <= 0)
        return '';
    return u === 1 ? '+' : `+${u}`;
}

function enchantText(e) {
    if (!e)
        return '';
    const amount = int(e.amount);
    return amount === null ? txt(e.name) : `${txt(e.name)} ${amount}`;
}

export function cardLabel(card) {
    return `${txt(card?.name)}${upgradeMark(card)}`;
}

function screenOf(state) {
    return String(state?.screen ?? 'Unknown').toLowerCase();
}

function normRoom(t) {
    return String(t ?? '').toLowerCase().replace(/[^a-z]/g, '');
}

function roomFr(t) {
    return ROOM_FR[normRoom(t)] ?? txt(t);
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
// Ids inconnus ignorés (consommateur tolérant) ; doublon -> première occurrence.
export function orderedModules(settings) {
    const list = arr(settings?.modules);
    const src = list.length ? list : DEFAULT_MODULES;
    const seen = new Set();
    return src
        .map((m, i) => ({id: m?.id, enabled: m?.enabled !== false, order: int(m?.order) ?? 1000 + i, i}))
        .filter(m => MODULE_IDS.includes(m.id) && !seen.has(m.id) && seen.add(m.id))
        .sort((a, b) => a.order - b.order || a.i - b.i)
        .filter(m => m.enabled)
        .map(m => m.id);
}

// ---------- modules ----------

const CONTEXTUAL = new Set(['reward', 'shop', 'paths']);

function block(key, title, body, tone = 'cream', fixed = false) {
    return {key, tone, title, body, fixed, contextual: CONTEXTUAL.has(key), hidden: 0};
}

function deckModule(run) {
    const deck = arr(run?.deck);
    const body = groupCards(deck).map(({card, count}) => {
        const e = card.enchantment ? ` [${enchantText(card.enchantment)}]` : '';
        return `${count}× ${cardLabel(card)}${e} · ${fmtScore(card.score)}`;
    });
    return block('deck', `DECK · ${deck.length} cartes`, body.length ? body : ['vide']);
}

function enchantedModule(run) {
    const cards = arr(run?.deck).filter(c => c?.enchantment);
    const body = groupCards(cards).map(({card, count}) =>
        `${count}× ${cardLabel(card)} : ${enchantText(card.enchantment)}`);
    return block('enchanted', `ENCHANTÉES · ${cards.length}`, body.length ? body : ['aucune']);
}

function cursesModule(run) {
    const cards = arr(run?.deck).filter(c => c?.isCurse === true);
    const body = groupCards(cards).map(({card, count}) => `!! ${count}× ${cardLabel(card)}`);
    return block('curses', `MALÉDICTIONS · ${cards.length}`, body.length ? body : ['aucune'],
        cards.length ? 'red' : 'cream', true);
}

function relicsModule(run) {
    const relics = arr(run?.relics);
    const body = relics.map(r => `${txt(r?.name)} · ${fmtScore(r?.score)}`);
    return block('relics', `RELIQUES · ${relics.length}`, body.length ? body : ['aucune']);
}

function potionsModule(run) {
    const potions = arr(run?.potions);
    const body = potions.map(p => txt(p?.name));
    return block('potions', `POTIONS · ${potions.length}`, body.length ? body : ['aucune']);
}

function rewardModule(reward) {
    if (!reward)
        return block('reward', 'RÉCOMPENSE · non lue', [NO_SCORE]);
    const body = arr(reward.offered).map(c => {
        const e = c?.enchantment ? ` [${enchantText(c.enchantment)}]` : '';
        return `${cardLabel(c)}${e} · ${fmtScore(c?.score)}`;
    });
    body.push(`passer · ${fmtScore(reward.skipScore)}`);
    return block('reward', reward.alreadyTaken ? 'RÉCOMPENSE (déjà prise)' : 'RÉCOMPENSE', body);
}

function shopModule(shop, run) {
    if (!shop)
        return block('shop', 'SHOP · non lu', [NO_SCORE]);
    const gold = int(run?.gold);
    const body = arr(shop.items).map(it => {
        const name = it?.card ? cardLabel(it.card) : txt(it?.name);
        const price = int(it?.price);
        let p = price === null ? '? or' : `${price} or`;
        if (it?.onSale)
            p += ' soldé';
        if (it?.affordable === false)
            p += ' TROP CHER';
        return `${KIND_FR[it?.kind] ?? txt(it?.kind)} ${name} · ${p} · ${fmtScore(it?.score)}`;
    });
    if (shop.removalUsed) {
        body.push('retrait · déjà utilisé');
    } else {
        const rp = int(shop.removalPrice);
        let p = rp === null ? '? or' : `${rp} or`;
        if (rp !== null && gold !== null && gold < rp)
            p += ' TROP CHER';
        body.push(`retrait · ${p} · ${fmtScore(shop.removalScore)}`);
    }
    return block('shop', gold === null ? 'SHOP' : `SHOP · ${gold} or`, body);
}

function range(min, max) {
    const a = int(min), b = int(max);
    if (a === null && b === null)
        return null;
    if (a === b || b === null)
        return String(a);
    if (a === null)
        return String(b);
    return `${a}-${b}`;
}

function countsLine(c) {
    const parts = [];
    for (const [k, label] of ROOM_COUNTS) {
        const max = int(c.max?.[k]);
        if (max === null || max <= 0)
            continue; // jamais rencontré sur aucun chemin : on n'encombre pas
        parts.push(`${label} ${range(c.min?.[k], c.max?.[k])}`);
    }
    return parts.length ? `   ${parts.join(' · ')}` : null;
}

function pathsModule(paths) {
    if (!paths)
        return block('paths', `CHEMINS · ${NO_SCORE}`, ['non calculés']);
    const total = int(paths.totalPaths);
    const choices = [...arr(paths.nextChoices)].sort((a, b) => (int(a?.col) ?? 0) - (int(b?.col) ?? 0));
    const body = [];
    for (const c of choices) {
        const dmg = int(c?.expectedDamage);
        const n = int(c?.pathCount);
        body.push(`col ${int(c?.col) ?? '?'} ${roomFr(c?.roomType)} · ${n ?? '?'} chemin${n === 1 ? '' : 's'}` +
            (dmg === null ? '' : ` · dégâts ~${dmg}`));
        const counts = countsLine(c ?? {});
        if (counts)
            body.push(counts);
    }
    return block('paths', `CHEMINS · ${total ?? '?'} jusqu'au boss`, body.length ? body : ['aucun choix']);
}

function deckScoreModule(deck, run) {
    const boss = run?.bossName ? txt(run.bossName) : null;
    if (!deck)
        return block('deck_score', `NOTE DU DECK · ${NO_SCORE}`, ['calcul indisponible']);
    const s = deck.score;
    const conf = CONFIDENCE_FR[s?.confidence] ?? s?.confidence;
    const body = arr(s?.terms).map(t => `${txt(t?.label)} ${signed(t?.points)}`);
    if (boss)
        body.push(`boss : ${boss}`);
    body.push(`archétype : ${deck.archetype ? txt(deck.archetype) : NO_SCORE}`);
    const pot = arr(deck.potentialArchetypes).map(a => txt(a));
    if (pot.length)
        body.push(`pistes : ${pot.join(', ')}`);
    body.unshift(`total ${int(s?.total) ?? NO_SCORE}/100${conf ? ` · confiance ${txt(conf)}` : ''}`);
    return block('deck_score', `NOTE DU DECK · ${fmtScore(s)}`, body);
}

// Modules contextuels : visibles seulement sur l'écran correspondant (skill sts2-engine-contract).
// deck_score (« note du deck ») : hors combat (points de décision : carte, récompense, shop, feu…).
function contextual(id, screen) {
    switch (id) {
    case 'reward': return screen === 'reward';
    case 'shop': return screen === 'shop';
    case 'paths': return screen === 'map';
    case 'deck_score': return screen !== 'combat';
    default: return true;
    }
}

function buildModule(id, state) {
    const run = state.run;
    switch (id) {
    case 'deck': return deckModule(run);
    case 'enchanted': return enchantedModule(run);
    case 'curses': return cursesModule(run);
    case 'relics': return relicsModule(run);
    case 'potions': return potionsModule(run);
    case 'reward': return rewardModule(state.reward);
    case 'shop': return shopModule(state.shop, run);
    case 'paths': return pathsModule(state.paths);
    case 'deck_score': return deckScoreModule(state.deck, run);
    default: return null;
    }
}

// ---------- vue complète ----------

const WAITING_RAIL = ['lightGray', 'lightGray', 'lightGray'];

export function buildView(current) {
    if (!current || current.kind === 'none') {
        return {
            blocks: [block('title', 'ULTIMA · en attente du jeu', ['aucun état publié (daemon arrêté ?)'], 'gold', true)],
            rail: WAITING_RAIL,
        };
    }
    if (current.kind === 'future') {
        return {
            blocks: [block('title', 'ULTIMA · mettre à jour ULTIMA',
                [`état v${current.version}, overlay compatible v1 seulement`], 'red', true)],
            rail: ['red', 'lightGray', 'lightGray'],
        };
    }

    const state = current.state ?? {};
    const blocks = [];
    const degraded = state.health?.degraded;
    if (degraded !== null && degraded !== undefined && degraded !== '')
        blocks.push(block('degraded', `DÉGRADÉ : ${txt(degraded)}`, [], 'orange', true));

    if (state.health?.gameRunning === false) {
        blocks.push(block('title', 'ULTIMA · en attente du jeu', [], 'gold', true));
        return {blocks, rail: [degraded ? 'orange' : 'lightGray', 'lightGray', 'lightGray']};
    }

    const screen = screenOf(state);
    const run = state.run;
    if (run) {
        const act = int(run.actIndex);
        blocks.push(block('title',
            `${txt(run.characterName)} A${int(run.ascension) ?? '?'} · acte ${act === null ? '?' : act + 1} · étage ${int(run.floor) ?? '?'}`,
            [`PV ${int(run.hp) ?? '?'}/${int(run.maxHp) ?? '?'} · ${int(run.gold) ?? '?'} or`], 'gold', true));
    } else {
        blocks.push(block('title', `ULTIMA · ${txt(state.screen)}`, ['pas de run en cours'], 'gold', true));
    }

    const ids = orderedModules(state.settings);
    if (run) {
        for (const id of ids) {
            if (!contextual(id, screen))
                continue;
            const b = buildModule(id, state);
            if (b)
                blocks.push(b);
        }
    }

    const curses = arr(run?.deck).some(c => c?.isCurse === true);
    const ctx = (screen === 'reward' && ids.includes('reward') && state.reward) ||
        (screen === 'shop' && ids.includes('shop') && state.shop) ||
        (screen === 'map' && ids.includes('paths') && state.paths);
    return {
        blocks,
        rail: [degraded ? 'orange' : 'aqua', curses ? 'red' : 'lightGray', ctx ? 'gold' : 'lightGray'],
    };
}

// Lignes affichées d'un bloc (titre, corps, marque de troncature).
// Bloc entièrement replié par fitView : une seule ligne « TITRE … +N ».
export function blockLines(b) {
    if (b.hidden > 0 && b.body.length === 0)
        return [`${b.title} … +${b.hidden}`];
    const lines = [b.title, ...b.body];
    if (b.hidden > 0)
        lines.push(`… +${b.hidden} autre${b.hidden > 1 ? 's' : ''}`);
    return lines;
}

function visualLines(line, cols) {
    return Math.max(1, Math.ceil([...line].length / cols));
}

function blockCost(b, cols) {
    return blockLines(b).reduce((n, l) => n + visualLines(l, cols), 0);
}

// Ajuste la vue à la hauteur du panneau : jamais de coupure silencieuse. Tant que ça déborde,
// on retire la dernière ligne du bloc non fixe le plus long (égalité -> le plus bas, donc le
// moins prioritaire dans l'ordre de Mio) et on l'annonce (« … +N autres »). Les modules
// contextuels (récompense, shop, chemins : la décision en cours) ne sont tronqués qu'en dernier.
// opts : {maxLines, cols}. Retourne une NOUVELLE vue (l'entrée n'est pas modifiée).
export function fitView(view, {maxLines, cols = WRAP_COLS}) {
    const blocks = view.blocks.map(b => ({...b, body: [...b.body]}));
    const total = () => blocks.reduce((n, b) => n + blockCost(b, cols), 0);
    let guard = 10000;
    while (total() > maxLines && guard-- > 0) {
        let victim = null;
        for (const pass of [false, true]) {
            for (const b of blocks) {
                if (b.fixed || b.contextual !== pass || b.body.length === 0)
                    continue;
                if (!victim || b.body.length >= victim.body.length)
                    victim = b;
            }
            if (victim)
                break;
        }
        if (!victim)
            break; // rien de tronquable : le reste est fixe (dégradé, titre, malédictions)
        victim.body.pop();
        victim.hidden++;
    }
    return {...view, blocks};
}

export function viewToText(view) {
    return view.blocks.map(b => blockLines(b).join('\n')).join('\n\n');
}
