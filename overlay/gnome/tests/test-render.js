// Tests du rendu (vue v3 minimaliste, skill design §11-12) et de la lecture d'état (modules purs).
// Lancer :  gjs -m overlay/gnome/tests/test-render.js
//           node overlay/gnome/tests/test-render.js
// Entrées réelles : docs/22-state.sample.json (contrat v1, shop Necrobinder) et
// docs/ipc/state.v13.sample.json (produit par `ultima-daemon dump`, contrat v1.3) + variantes.
// ULTIMA_PRINT=1 : imprime aussi les sorties texte complètes (preuve brute).
import {buildView, fitView, viewToText, orderedModules, fmtScore, gradeFromTotal, groupCards,
    spansMarkup, bestOption, mainReason, rowCost, MODULE_IDS} from '../ultima@mio/render.js';
import {parseEngineState, StateStore, SUPPORTED_CONTRACT} from '../ultima@mio/state.js';
import {GRADE_TONE, GRADES, MODULE_LABEL, NO_SCORE} from '../ultima@mio/grammar.js';
import {PALETTE, TONES, MUTED_ALPHA, toneHex} from '../ultima@mio/palette.js';

// ---------- shim gjs / node ----------
const isNode = typeof globalThis.process?.versions?.node === 'string';
const out = isNode ? s => console.log(s) : s => globalThis.print(s);
let readText, getenv, exit;
if (isNode) {
    const fs = await import('node:fs');
    readText = rel => fs.readFileSync(new URL(rel, import.meta.url), 'utf8');
    getenv = k => globalThis.process.env[k];
    exit = c => globalThis.process.exit(c);
} else {
    const {default: Gio} = await import('gi://Gio');
    const {default: GLib} = await import('gi://GLib');
    const {default: System} = await import('system');
    const here = Gio.File.new_for_uri(import.meta.url).get_parent();
    readText = rel => {
        const f = here.resolve_relative_path(rel);
        const [, bytes] = f.load_contents(null);
        return new TextDecoder().decode(bytes);
    };
    getenv = k => GLib.getenv(k);
    exit = c => System.exit(c);
}
const PRINT = getenv('ULTIMA_PRINT') === '1';

let failures = 0;
function check(name, cond, extra = '') {
    out(`${cond ? 'OK  ' : 'FAIL'} ${name}${extra ? ` — ${extra}` : ''}`);
    if (!cond)
        failures++;
}
function expectText(name, actual, expected) {
    const ok = actual === expected;
    check(name, ok);
    if (!ok)
        out(`---- attendu ----\n${expected}\n---- obtenu ----\n${actual}\n-----------------`);
    else if (PRINT)
        out(`${actual.replace(/^/gm, '    | ')}`);
}

const SAMPLE_TEXT = readText('../../../docs/22-state.sample.json');
const V13_TEXT = readText('../../../docs/ipc/state.v13.sample.json');
const sample = () => JSON.parse(SAMPLE_TEXT);
const v13 = () => JSON.parse(V13_TEXT);
const view = (state, o = {}) => buildView({kind: 'state', state}, o);
const render = (state, o = {}) => viewToText(view(state, o));
const kinds = v => v.rows.map(r => r.kind).join(',');

// ---------- 1. échantillons réels du contrat ----------
const parsed = parseEngineState(SAMPLE_TEXT);
check('22-state.sample.json accepté (contrat v1)', parsed.kind === 'state' && SUPPORTED_CONTRACT === 1);
const p13 = parseEngineState(V13_TEXT);
check('state.v13.sample.json (daemon réel, contractMinor 3) accepté', p13.kind === 'state' && p13.state.contractMinor === 3);

// ---------- 2. moment de décision (shop) : la carte prend la place (§11) ----------
{
    const v = buildView(parsed);
    expectText('shop : lettre + choix + UNE raison, rien d\'autre', viewToText(v),
        '[A] Scourge · 24 or soldé\n    Méta (médiane 3 sources) +65');
    check('shop : une seule ligne visible (la carte de décision)', kinds(v) === 'decision' && v.decision === true);
    check('shop : la lettre porte son ton S-F (A = vert)', v.rows[0].grade === 'A' && v.rows[0].gradeTone === 'green');
    expectText('shop épinglé : options dans l\'ordre de l\'écran, termes, puis une ligne par module',
        viewToText(buildView(parsed, {detail: true})),
        `[A] Scourge · 24 or soldé
    Méta (médiane 3 sources) +65
  — · Graveblast · 50 or
  A · Scourge · 24 or soldé
  — · Pendule · 181 or trop cher
  — · Potion de faiblesse · 49 or
  B · Retrait · 75 or
  Méta (médiane 3 sources) +65
  Prix soldé +6
  confiance moyenne
Deck 11
Enchantées 1
Malédiction 1
Reliques 1
Potions 1
Note du deck C`);
    const s = sample();
    check('notes seules (gradesOnly) : pas de raison', render(s, {hud: {gradesOnly: true}}) === '[A] Scourge · 24 or soldé');
    check('option trop chère jamais recommandée', bestOption([{available: false, score: {total: 99}},
        {available: true, score: {total: 10}, label: 'x'}]).label === 'x');
    check('raison principale = terme le plus lourd (valeur absolue)',
        mainReason({terms: [{label: 'a', points: 5}, {label: 'b', points: -9}]}) === 'b -9');
    s.screen = 'Shop';
    s.shop = null;
    check('shop non lu : annoncé, pas de faux prix', render(s) === '[—] Boutique\n    articles non lus');
    s.shop = sample().shop;
    for (const it of s.shop.items)
        it.score = null;
    s.shop.removalScore = null;
    check('shop sans aucune note : « — » + « notes non calculées » (jamais 0 ni F)',
        render(s) === '[—] Boutique\n    notes non calculées');
}

// ---------- 3. récompense, événement ----------
{
    const s = sample();
    s.screen = 'Reward';
    s.shop = null;
    const card = (name, total, upgrade = 0, terms = []) => ({id: `CARD.${name}`, name, type: 'Skill', rarity: 'Common',
        cost: 1, upgrade, enchantment: null, isCurse: false,
        score: total === null ? null : {total, terms, confidence: 'medium'}});
    s.reward = {offered: [card('Iteration', 71, 0, [{label: 'Synergie Âmes', points: 12}]), card('Hailstorm', null, 1),
        card('Boot Sequence', 40)], skipScore: {total: 35, terms: [], confidence: 'low'}, alreadyTaken: false};
    expectText('récompense : meilleure carte + raison', render(s), '[A] Iteration\n    Synergie Âmes +12');
    check('récompense épinglée : « Passer » listé avec sa note',
        render(s, {detail: true}).includes('  D · Passer') && render(s, {detail: true}).includes('  — · Hailstorm+'));
    s.reward.skipScore = {total: 90, terms: [{label: 'Deck déjà complet', points: 20}], confidence: 'low'};
    check('passer peut être la recommandation', render(s).startsWith('[S] Passer'));
    s.reward.alreadyTaken = true;
    check('récompense déjà prise : plus de carte de décision', !view(s).decision && render(s).startsWith('Deck 11'));
    s.reward = null;
    check('récompense non lue', render(s) === '[—] Récompense\n    offre non lue');
    s.screen = 'Event';
    s.event = null;
    check('événement sans EventView (daemon < v1.2) : modules seulement', !view(s).decision);
    s.event = {name: 'Le Puits', options: [{label: 'Boire', score: {total: 30, terms: [{label: 'PV -8', points: -15}]}, available: true},
        {label: 'Partir', score: {total: 58, terms: [{label: 'sans risque', points: 5}]}, available: true},
        {label: 'Sacrifier', score: {total: 95, terms: []}, available: false}]};
    expectText('événement : option indisponible jamais recommandée', render(s), '[B] Partir\n    sans risque +5');
}

// ---------- 4. une ligne par module (§11) ----------
{
    const s = sample();
    s.screen = 'Map';
    s.shop = null;
    s.paths = {totalPaths: 23, nextChoices: [
        {col: 4, row: 5, roomType: 'elite', pathCount: 9, expectedDamage: 31.4},
        {col: 1, row: 5, roomType: 'rest_site', pathCount: 14, expectedDamage: null}], nodes: []};
    expectText('carte : un résumé d\'une ligne par module, dans l\'ordre du contrat', render(s),
        'Deck 11\nEnchantées 1\nMalédiction 1\nReliques 1\nPotions 1\nChemins 23 · 2 choix\nNote du deck C');
    const v = view(s);
    check('aucune ligne de détail sans épinglage', v.rows.every(r => r.kind === 'line'));
    const curse = v.rows.find(r => r.key === 'line:curses');
    check('malédiction : violet + le MOT (jamais la couleur seule)', curse.spans[0].tone === 'purple' &&
        /Malédiction/.test(curse.spans[0].text));
    check('enchantées : aqua', v.rows.find(r => r.key === 'line:enchanted').spans[0].tone === 'aqua');
    check('au plus 1 accent coloré par ligne (§11)', v.rows.every(r =>
        r.spans.filter(x => x.tone !== 'cream' && x.tone !== 'muted').length <= 1));
    check('aucune répétition PV / or du jeu (§11)', !/PV|\d+ or/.test(render(s)));
    check('aucun titre décoratif (ULTIMA, logo)', !/ULTIMA/.test(render(s)));
    const d = render(s, {detail: true});
    check('épinglé : détail du deck (lettre d\'abord, regroupé)', d.includes('  — · 4× Frappe') &&
        d.includes('  — · 1× Défense · Célérité 1') && d.includes("  — · 1× Fléau de l'Ascendant · Malédiction"));
    check('épinglé : chemins triés par colonne', d.includes('  feu · 14 chemins\n  élite · 9 chemins · dégâts ~31'));
    check('épinglé : note du deck détaillée', d.includes('  Méta des cartes +44') && d.includes('  pistes · Âmes (exemple)'));
    s.run.deck = s.run.deck.filter(c => !c.isCurse && !c.enchantment);
    check('rien à signaler -> pas de ligne « 0 » (malédictions, enchantées)',
        !/Malédiction|Enchantées/.test(render(s)));
    s.paths = null;
    check('chemins non calculés -> « — »', render(s).includes('Chemins —'));
    s.screen = 'Combat';
    check('combat : pas de chemins ni de note du deck', render(s) === 'Deck 9\nReliques 1\nPotions 1');
    check('combat + deckScoreInCombat : note du deck', render(s, {hud: {deckScoreInCombat: true}}).endsWith('Note du deck C'));
    const real = v13().state ?? v13();
    real.health.gameRunning = true;
    real.screen = 'Map';
    expectText('état réel du daemon (v1.3, carte)', render(real), 'Deck 14\nReliques 2\nPotions 2\nChemins 17 · 2 choix\nNote du deck F');
}

// ---------- 5. modules masqués / réordonnés ----------
{
    const s = sample();
    s.screen = 'Map';
    s.settings.modules = [
        {id: 'curses', enabled: true, order: 0},
        {id: 'deck', enabled: false, order: 1},
        {id: 'relics', enabled: true, order: 2},
        {id: 'enchanted', enabled: false, order: 4},
        {id: 'potions', enabled: false, order: 5},
        {id: 'deck_score', enabled: false, order: 6},
        {id: 'inconnu', enabled: true, order: 7},
    ];
    check('modules désactivés absents, ordre respecté, id inconnu ignoré, module absent ajouté à la fin (comme le daemon)',
        render(s) === 'Malédiction 1\nReliques 1\nChemins —', render(s).replace(/\n/g, ' / '));
    check('réglages absents -> ordre par défaut du contrat',
        orderedModules(undefined).join(',') === MODULE_IDS.join(','));
    check('ordre non trié dans le JSON -> trié par « order »',
        orderedModules({modules: [{id: 'potions', enabled: true, order: 1}, {id: 'deck', enabled: true, order: 0}]}).slice(0, 2).join(',') === 'deck,potions');
    s.screen = 'Shop';
    check('module shop absent des réglages -> ajouté activé : carte de décision', view(s).decision);
    s.settings.modules.push({id: 'shop', enabled: false, order: 8});
    check('module shop désactivé : pas de carte de décision au shop', !view(s).decision);
}

// ---------- 6. notes S-F et « — » ----------
{
    check('échelle S≥85 A≥70 B≥55 C≥40 D≥25 F<25 (bornes)',
        [[100, 'S'], [85, 'S'], [84, 'A'], [70, 'A'], [69, 'B'], [55, 'B'], [54, 'C'], [40, 'C'], [39, 'D'], [25, 'D'], [24, 'F'], [0, 'F']]
            .every(([t, g]) => gradeFromTotal(t) === g));
    check('grade publié prioritaire, grade invalide -> dérivé de total',
        fmtScore({total: 10, grade: 'A'}) === 'A' && fmtScore({total: 90, grade: 'Z'}) === 'S');
    check('fmtScore(null) = —, fmtScore({total:0}) = F, total absent = —',
        fmtScore(null) === NO_SCORE && fmtScore(undefined) === NO_SCORE && fmtScore({total: 0}) === 'F' && fmtScore({}) === NO_SCORE);
    const s = sample();
    s.screen = 'Map';
    s.deck = null;
    check('note du deck absente -> « — », jamais 0', render(s).endsWith('Note du deck —'));
    check('couleurs S-F = §9 (S or, A vert, B aqua, C bleu, D orange, F rouge, — gris)',
        JSON.stringify(GRADE_TONE) === JSON.stringify({S: 'gold', A: 'green', B: 'aqua', C: 'blue', D: 'orange', F: 'red', [NO_SCORE]: 'gray'}));
    check('chaque ton de note est un ton de palette', [...GRADES, NO_SCORE].every(g => TONES.includes(GRADE_TONE[g])));
    check('libellés : un par module connu', MODULE_IDS.every(id => typeof MODULE_LABEL[id] === 'string'));
}

// ---------- 7. dégradé, version future, attente ----------
{
    const s = sample();
    s.health.degraded = 'miroir indisponible : prix du shop non lus';
    const v = view(s);
    check('dégradé = première ligne, orange', v.rows[0].key === 'degraded' && v.rows[0].spans[0].tone === 'orange');
    check('dégradé : texte exact', viewToText(v).split('\n')[0] === 'Dégradé · miroir indisponible : prix du shop non lus');
    const f = sample();
    f.contractVersion = 2;
    const p = parseEngineState(JSON.stringify(f));
    check('contractVersion 2 -> future', p.kind === 'future' && p.version === 2);
    expectText('version future : message', viewToText(buildView(p)), 'Mettre à jour ULTIMA (état v2)');
    check('contractVersion absente -> invalide', parseEngineState('{"screen":"Map"}').kind === 'invalid');
    check('contractVersion 0 -> invalide', parseEngineState('{"contractVersion":0}').kind === 'invalid');
    const c = sample();
    c.health.gameRunning = false;
    expectText('jeu fermé -> en attente du jeu', render(c), 'En attente du jeu');
    expectText('aucun état publié', viewToText(buildView({kind: 'none'})), 'En attente du daemon');
    const m = sample();
    m.run = null;
    m.screen = 'Menu';
    expectText('menu, pas de run', render(m), 'Pas de run en cours');
}

// ---------- 8. JSON invalide -> dernier état valide conservé ----------
{
    const store = new StateStore();
    check('store initial = aucun état', store.current.kind === 'none');
    check('état valide accepté', store.acceptText(SAMPLE_TEXT).changed && store.current.kind === 'state');
    const before = store.current;
    const r = store.acceptText(SAMPLE_TEXT.slice(0, 200)); // écriture tronquée
    check('JSON tronqué rejeté, dernier état gardé', !r.changed && r.error && store.current === before);
    check('même erreur répétée -> pas re-signalée', store.acceptText(SAMPLE_TEXT.slice(0, 200)).newError === false);
    check('racine non objet rejetée', !store.acceptText('[1,2]').changed && store.current === before);
    const f = sample();
    f.contractVersion = 3;
    check('version future remplace (refus annoncé)', store.acceptText(JSON.stringify(f)).changed && store.current.kind === 'future');
    check('fichier supprimé -> aucun état', store.acceptMissing().changed && store.current.kind === 'none');
}

// ---------- 9. regroupement, améliorations, enchantements ----------
{
    const c = (id, upgrade, ench = null, cost = 1, isCurse = false) => ({id, name: id, cost, upgrade, enchantment: ench, isCurse, score: null});
    const g = groupCards([c('A', 0), c('A', 1), c('A', 0), c('A', 0, {id: 'E.S', name: 'Semé', amount: 2}),
        c('B', 2, null, 0), c('X', 0, null, -1, true), c('U', 0, null, -1)]);
    check('groupes : id+amélioration+enchantement', g.length === 6, String(g.length));
    check('tri : coût croissant, injouables puis malédictions en dernier',
        g.map(x => `${x.card.id}${x.card.upgrade}`).join(' ') === 'B2 A0 A0 A1 U0 X0', g.map(x => `${x.card.id}${x.card.upgrade}`).join(' '));
    const s = sample();
    s.screen = 'Map';
    s.run.deck = [c('Frappe', 1), c('Frappe', 1), c('Coup', 2, {id: 'E.S', name: 'Semé', amount: 3})];
    const t = render(s, {detail: true});
    check('amélioration affichée (+, +2) et enchantement + montant',
        t.includes('  — · 2× Frappe+') && t.includes('  — · 1× Coup+2 · Semé 3') && t.includes('  1× Coup+2 · Semé 3'));
}

// ---------- 10. ajustement à la hauteur : troncature annoncée, jamais silencieuse ----------
{
    const s = sample();
    s.screen = 'Map';
    const many = [];
    for (let i = 0; i < 30; i++)
        many.push({id: `C${i}`, name: `Carte ${String(i).padStart(2, '0')}`, cost: 1, upgrade: 0, enchantment: null, isCurse: false, score: null});
    many.push(...sample().run.deck.filter(c => c.isCurse));
    s.run.deck = many;
    const v = view(s, {detail: true});
    const f = fitView(v, {maxLines: 20, cols: 32});
    const t = viewToText(f);
    const used = f.rows.reduce((n, r) => n + rowCost(r, 32), 0);
    const more = f.rows.find(r => r.key === 'more:deck');
    check('deck tronqué avec « … +N »', more && /^… \+\d+$/.test(more.spans[0].text), more?.spans[0].text);
    check('budget respecté', used <= 20, `${used} lignes`);
    check('résumés jamais retirés (une ligne par module conservée)',
        ['Deck 31', 'Malédiction 1', 'Reliques 1', 'Potions 1', 'Note du deck C'].every(x => t.includes(x)));
    const hidden = Number(more.spans[0].text.replace(/\D/g, ''));
    const shownDeck = f.rows.filter(r => r.group === 'deck' && r.kind === 'detail').length;
    check('affiché + caché = 31 groupes de cartes', shownDeck + hidden === 31, `${shownDeck}+${hidden}`);
    check('entrée non modifiée', v.rows.length > f.rows.length - 2 && !v.rows.some(r => r.kind === 'more'));
    const d = fitView(buildView(parsed, {detail: true}), {maxLines: 8, cols: 32});
    check('carte de décision jamais retirée', d.rows[0].kind === 'decision');
    if (PRINT)
        out(viewToText(f).replace(/^/gm, '    | '));
}

// ---------- 11. balisage : couleurs de la palette seulement, texte échappé ----------
{
    const resolve = tone => (tone === 'muted' ? {hex: PALETTE.cream, alpha: MUTED_ALPHA} : {hex: toneHex(tone), alpha: 1});
    const s = sample();
    s.screen = 'Map';
    s.run.relics[0].name = '<b>pirate</b> & "co"';
    const all = [];
    for (const st of [s, sample()]) {
        for (const r of view(st, {detail: true}).rows) {
            all.push(spansMarkup(r.spans, resolve));
            if (r.reason)
                all.push(spansMarkup(r.reason, resolve));
        }
    }
    const markup = all.join('\n');
    const colors = new Set(markup.match(/#[0-9A-Fa-f]{6}/g));
    const allowed = new Set(Object.values(PALETTE));
    check('balisage : toutes les couleurs ∈ palette', [...colors].every(c => allowed.has(c)), [...colors].join(' '));
    check('balisage : texte de l\'état échappé', markup.includes('&lt;b&gt;pirate&lt;/b&gt; &amp; &quot;co&quot;'));
    check('balisage : texte secondaire en crème 60 %', markup.includes(`foreground="${PALETTE.cream}" fgalpha="60%"`));
    check('tons de la vue ∈ palette ou « muted »', view(s, {detail: true}).rows.every(r =>
        [...r.spans, ...(r.reason ?? [])].every(x => x.tone === 'muted' || TONES.includes(x.tone))));
}

// ---------- 12. rail (§12) : la vue ne fournit RIEN au rail ----------
{
    const v = buildView(parsed);
    check('aucune pastille, aucun indicateur de rail dans la vue', !('rail' in v) && !('dots' in v));
}

out(failures ? `\n${failures} échec(s)` : '\ntous les tests passent');
exit(failures ? 1 : 0);
