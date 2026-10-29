// Tests du rendu texte, de la lecture d'état et du planificateur (modules purs, sans session).
// Lancer :  gjs -m overlay/gnome/tests/test-render.js
//           node overlay/gnome/tests/test-render.js
// Entrée réelle : docs/22-state.sample.json (contrat v1) + variantes construites ici.
// ULTIMA_PRINT=1 : imprime aussi les sorties texte complètes (preuve brute).
import {blockLines, buildView, fitView, viewToText, orderedModules, fmtScore, gradeFromTotal, groupCards} from '../ultima@mio/render.js';
import {parseEngineState, StateStore, SUPPORTED_CONTRACT} from '../ultima@mio/state.js';
import {computePlan, Poller} from '../ultima@mio/plan.js';

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
const sample = () => JSON.parse(SAMPLE_TEXT);
const render = state => viewToText(buildView({kind: 'state', state}));

// ---------- 1. échantillon réel du contrat (shop Necrobinder) ----------
const parsed = parseEngineState(SAMPLE_TEXT);
check('22-state.sample.json accepté (contrat v1)', parsed.kind === 'state' && SUPPORTED_CONTRACT === 1);

const SHOP_TEXT = `Nécrolieuse A9 · acte 1 · étage 4
PV 66/66 · 116 or

DECK · 11 cartes
4× Défense · —
1× Défense [Célérité 1] · —
1× Fléau · —
4× Frappe · —
1× Fléau de l'Ascendant · —

ENCHANTÉES · 1
1× Défense : Célérité 1

MALÉDICTIONS · 1
!! 1× Fléau de l'Ascendant

RELIQUES · 1
(exemple) · B

POTIONS · 1
Potion d'attaque

SHOP · 116 or
carte Graveblast · 50 or · —
carte Scourge · 24 or soldé · A
relique Pendule · 181 or TROP CHER · —
potion Potion de faiblesse · 49 or · —
retrait · 75 or · B

NOTE DU DECK · C
total 48/100 · confiance basse
Méta des cartes +44
Malédiction -6
Vs boss acte 1 +10
boss : Bête cérémonielle
archétype : —
pistes : Âmes (exemple)`;
expectText('échantillon shop : sortie texte exacte', render(parsed.state), SHOP_TEXT);

const v0 = buildView(parsed);
check('ordre des blocs = réglages (shop)', v0.blocks.map(b => b.key).join(',') ===
    'title,deck,enchanted,curses,relics,potions,shop,deck_score');
check('malédictions en rouge', v0.blocks.find(b => b.key === 'curses').tone === 'red');
check('rail : sain, malédiction, contexte shop', v0.rail.join(',') === 'aqua,red,gold');
check('aucun "0" ni "F" inventé pour un score null', !/· (0|F)$/m.test(render(parsed.state)));
{
    const noGrade = JSON.parse(SAMPLE_TEXT.replace(/,\s*"grade": "[A-Z]"/g, ''));
    check('état sans "grade" (compat) : lettre dérivée de total, même sortie', !SAMPLE_TEXT.includes('"grade"') ||
        (!JSON.stringify(noGrade).includes('"grade"') && render(noGrade) === render(parsed.state)));
    check('échelle S≥85 A≥70 B≥55 C≥40 D≥25 F<25 (bornes)',
        [[100, 'S'], [85, 'S'], [84, 'A'], [70, 'A'], [69, 'B'], [55, 'B'], [54, 'C'], [40, 'C'], [39, 'D'], [25, 'D'], [24, 'F'], [0, 'F']]
            .every(([t, g]) => gradeFromTotal(t) === g));
    check('grade publié prioritaire, grade invalide -> dérivé de total',
        fmtScore({total: 10, grade: 'A'}) === 'A' && fmtScore({total: 90, grade: 'Z'}) === 'S');
}

// ---------- 2. modules masqués / réordonnés ----------
{
    const s = sample();
    s.settings.modules = [
        {id: 'curses', enabled: true, order: 0},
        {id: 'deck', enabled: false, order: 1},
        {id: 'relics', enabled: true, order: 2},
        {id: 'shop', enabled: true, order: 3},
        {id: 'enchanted', enabled: false, order: 4},
        {id: 'potions', enabled: false, order: 5},
        {id: 'deck_score', enabled: false, order: 6},
        {id: 'inconnu', enabled: true, order: 7},
    ];
    const keys = buildView({kind: 'state', state: s}).blocks.map(b => b.key).join(',');
    check('modules désactivés absents, ordre respecté, id inconnu ignoré', keys === 'title,curses,relics,shop', keys);
    check('réglages absents -> ordre par défaut du contrat',
        orderedModules(undefined).join(',') === 'deck,enchanted,curses,relics,potions,reward,shop,paths,deck_score');
    check('ordre non trié dans le JSON -> trié par "order"',
        orderedModules({modules: [{id: 'potions', enabled: true, order: 1}, {id: 'deck', enabled: true, order: 0}]}).join(',') === 'deck,potions');
}

// ---------- 3. scores null partout ----------
{
    const s = sample();
    for (const r of s.run.relics)
        r.score = null;
    for (const it of s.shop.items)
        it.score = null;
    s.shop.removalScore = null;
    s.deck = null;
    const t = render(s);
    check('relique score null -> —', t.includes('(exemple) · —'));
    check('article score null -> —', t.includes('carte Scourge · 24 or soldé · —'));
    check('retrait score null -> —', t.includes('retrait · 75 or · —'));
    check('note du deck absente -> —', t.includes('NOTE DU DECK · —\ncalcul indisponible'));
    check('aucun score 0/F affiché', !/· (0|F)(\n|$)/.test(t));
    check('fmtScore(null) = —, fmtScore({total:0}) = F', fmtScore(null) === '—' && fmtScore(undefined) === '—' && fmtScore({total: 0}) === 'F');
}

// ---------- 4. mode dégradé (en tête) ----------
{
    const s = sample();
    s.health.degraded = 'miroir indisponible : prix du shop non lus';
    s.health.mirrorOk = false;
    s.shop = null;
    const v = buildView({kind: 'state', state: s});
    check('dégradé = premier bloc', v.blocks[0].key === 'degraded' && v.blocks[0].tone === 'orange');
    expectText('dégradé : en-tête exact', viewToText({blocks: v.blocks.slice(0, 2)}),
        'DÉGRADÉ : miroir indisponible : prix du shop non lus\n\nNécrolieuse A9 · acte 1 · étage 4\nPV 66/66 · 116 or');
    check('shop non lu annoncé (pas de faux prix)', viewToText(v).includes('SHOP · non lu\n—'));
    check('rail dégradé orange', v.rail[0] === 'orange');
}

// ---------- 5. version future ----------
{
    const s = sample();
    s.contractVersion = 2;
    const p = parseEngineState(JSON.stringify(s));
    check('contractVersion 2 -> future', p.kind === 'future' && p.version === 2);
    expectText('version future : message', viewToText(buildView(p)),
        'ULTIMA · mettre à jour ULTIMA\nétat v2, overlay compatible v1 seulement');
    check('contractVersion absente -> invalide', parseEngineState('{"screen":"Map"}').kind === 'invalid');
    check('contractVersion 0 -> invalide', parseEngineState('{"contractVersion":0}').kind === 'invalid');
}

// ---------- 6. jeu fermé / aucun état ----------
{
    const s = sample();
    s.health.gameRunning = false;
    expectText('jeu fermé -> en attente du jeu', render(s), 'ULTIMA · en attente du jeu');
    expectText('aucun état publié', viewToText(buildView({kind: 'none'})),
        'ULTIMA · en attente du jeu\naucun état publié (daemon arrêté ?)');
    const m = sample();
    m.run = null;
    m.screen = 'Menu';
    expectText('jeu au menu, pas de run', render(m), 'ULTIMA · Menu\npas de run en cours');
}

// ---------- 7. JSON invalide -> dernier état valide conservé ----------
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

// ---------- 8. récompense ----------
{
    const s = sample();
    s.screen = 'Reward';
    s.shop = null;
    const card = (name, total, upgrade = 0) => ({id: `CARD.${name}`, name, type: 'Skill', rarity: 'Common',
        cost: 1, upgrade, enchantment: null, isCurse: false,
        score: total === null ? null : {total, terms: [], confidence: 'medium'}});
    s.reward = {offered: [card('Iteration', 71), card('Hailstorm', null, 1), card('Boot Sequence', 40)],
        skipScore: {total: 35, terms: [], confidence: 'low'}, alreadyTaken: false};
    const t = render(s);
    check('récompense : cartes + scores + passer', t.includes('RÉCOMPENSE\nIteration · A\nHailstorm+ · —\nBoot Sequence · C\npasser · D'));
    check('récompense : pas de bloc shop', !t.includes('SHOP'));
    s.reward.alreadyTaken = true;
    check('récompense déjà prise annoncée', render(s).includes('RÉCOMPENSE (déjà prise)'));
    s.reward = null;
    check('récompense non lue', render(s).includes('RÉCOMPENSE · non lue\n—'));
    s.screen = 'Combat';
    const c = buildView({kind: 'state', state: s}).blocks.map(b => b.key).join(',');
    check('combat : aucun module contextuel, pas de note du deck', c === 'title,deck,enchanted,curses,relics,potions', c);
}

// ---------- 9. chemins ----------
{
    const s = sample();
    s.screen = 'Map';
    s.shop = null;
    const rc = (monster, elite, unknown, rest, shop, treasure) => ({monster, elite, unknown, rest, shop, treasure});
    s.paths = {
        totalPaths: 23,
        nextChoices: [
            {col: 4, row: 5, roomType: 'elite', pathCount: 9, min: rc(3, 1, 1, 1, 0, 1), median: rc(4, 2, 2, 2, 1, 1),
                max: rc(6, 3, 3, 2, 1, 1), expectedDamage: 31.4, riskBasis: 'x'},
            {col: 1, row: 5, roomType: 'RestSite', pathCount: 14, min: rc(4, 0, 2, 2, 0, 1), median: rc(5, 1, 3, 2, 1, 1),
                max: rc(6, 1, 4, 3, 1, 1), expectedDamage: null, riskBasis: 'x'},
        ],
        nodes: [],
    };
    expectText('chemins : choix triés par colonne + comptes min-max', viewToText({blocks: buildView({kind: 'state', state: s}).blocks.filter(b => b.key === 'paths')}),
        `CHEMINS · 23 jusqu'au boss
col 1 feu · 14 chemins
   combats 4-6 · élites 0-1 · ? 2-4 · feux 2-3 · shops 0-1 · trésors 1
col 4 élite · 9 chemins · dégâts ~31
   combats 3-6 · élites 1-3 · ? 1-3 · feux 1-2 · shops 0-1 · trésors 1`);
    s.paths = null;
    check('chemins non calculés -> —', render(s).includes('CHEMINS · —\nnon calculés'));
}

// ---------- 10. regroupement, améliorations, enchantements ----------
{
    const c = (id, upgrade, ench = null, cost = 1, isCurse = false) => ({id, name: id, cost, upgrade, enchantment: ench, isCurse, score: null});
    const g = groupCards([c('A', 0), c('A', 1), c('A', 0), c('A', 0, {id: 'E.S', name: 'Semé', amount: 2}),
        c('B', 2, null, 0), c('X', 0, null, -1, true), c('U', 0, null, -1)]);
    check('groupes : id+amélioration+enchantement', g.length === 6, String(g.length));
    check('tri : coût croissant, injouables puis malédictions en dernier',
        g.map(x => `${x.card.id}${x.card.upgrade}`).join(' ') === 'B2 A0 A0 A1 U0 X0', g.map(x => `${x.card.id}${x.card.upgrade}`).join(' '));
    const s = sample();
    s.run.deck = [c('Frappe', 1), c('Frappe', 1), c('Coup', 2, {id: 'E.S', name: 'Semé', amount: 3})];
    const t = render(s);
    check('amélioration affichée (+, +2) et enchantement + montant',
        t.includes('2× Frappe+ · —') && t.includes('1× Coup+2 [Semé 3] · —') && t.includes('1× Coup+2 : Semé 3'));
    check('aucune malédiction -> « aucune »', t.includes('MALÉDICTIONS · 0\naucune'));
}

// ---------- 11. troncature annoncée (jamais de coupure silencieuse) ----------
{
    const s = sample();
    const many = [];
    for (let i = 0; i < 30; i++)
        many.push({id: `C${i}`, name: `Carte ${String(i).padStart(2, '0')}`, cost: 1, upgrade: 0, enchantment: null, isCurse: false, score: null});
    many.push(...sample().run.deck.filter(c => c.isCurse));
    s.run.deck = many;
    const v = buildView({kind: 'state', state: s});
    const f = fitView(v, {maxLines: 30, cols: 32});
    const t = viewToText(f);
    const used = f.blocks.reduce((n, b) => n + blockLines(b).length, 0); // lignes < 32 car.
    const deck = f.blocks.find(b => b.key === 'deck');
    check('deck tronqué avec « … +N autres »', deck.hidden > 0 && t.includes(`… +${deck.hidden} autres`), `hidden=${deck.hidden}`);
    check('malédictions jamais tronquées', t.includes("!! 1× Fléau de l'Ascendant"));
    check('budget respecté', used <= 30, `${used} lignes`);
    check('entrée non modifiée', v.blocks.find(b => b.key === 'deck').hidden === 0);
    check('total cartes conservé (affiché + caché = 31 groupes)', deck.body.length + deck.hidden === 31);
    const shopB = f.blocks.find(b => b.key === 'shop');
    check('shop (contexte en cours) intact tant que le reste peut céder', shopB.hidden === 0 && shopB.body.length === 5);
    const tiny = fitView(buildView({kind: 'state', state: sample()}), {maxLines: 18, cols: 32});
    const tShop = tiny.blocks.find(b => b.key === 'shop');
    const tinyLines = tiny.blocks.reduce((n, b) => n + blockLines(b).length, 0);
    check('panneau 18 lignes (défaut 1536x960) : shop complet, le reste tronqué et annoncé',
        tShop.hidden === 0 && tiny.blocks.some(b => b.hidden > 0) && tinyLines <= 18, `${tinyLines} lignes`);
    check('bloc entièrement replié = une ligne « TITRE … +N »',
        blockLines({title: 'DECK · 11 cartes', body: [], hidden: 6}).join() === 'DECK · 11 cartes … +6');
    if (PRINT)
        out(viewToText(tiny).replace(/^/gm, '    | '));
}

// ---------- 12. planificateur : zéro sondage quand masqué ----------
{
    const base = {userHidden: false, overviewHidden: false, expanded: false, pinned: false,
        blurMode: 'panel-only', unredirectMode: 'while-shown', pollMs: 80};
    const P = o => computePlan({...base, ...o});
    for (const hidden of [{userHidden: true}, {overviewHidden: true}]) {
        for (const expanded of [false, true]) {
            for (const blurMode of ['panel-only', 'always', 'never']) {
                for (const unredirectMode of ['while-shown', 'while-expanded', 'never']) {
                    const p = P({...hidden, expanded, blurMode, unredirectMode});
                    if (p.shown || p.poll || p.blur || p.inhibitUnredirect || p.pollMs !== 0) {
                        check(`masqué ${JSON.stringify({...hidden, expanded, blurMode, unredirectMode})} -> rien d'actif`, false);
                    }
                }
            }
        }
    }
    check('masqué (36 combinaisons) : ni sondage, ni flou, ni inhibition unredirect', true);
    check('rail (replié) panel-only : pas de flou, sondage 80 ms', !P({}).blur && P({}).poll && P({}).pollMs === 80);
    check('panneau ouvert panel-only : flou', P({expanded: true}).blur);
    check('blur always : flou sur le rail', P({blurMode: 'always'}).blur);
    check('blur never : jamais', !P({blurMode: 'never', expanded: true}).blur);
    check('unredirect while-shown : inhibé rail visible', P({}).inhibitUnredirect);
    check('unredirect while-expanded : pas sur le rail, oui panneau ouvert',
        !P({unredirectMode: 'while-expanded'}).inhibitUnredirect && P({unredirectMode: 'while-expanded', expanded: true}).inhibitUnredirect);
    check('unredirect never : jamais', !P({unredirectMode: 'never', expanded: true}).inhibitUnredirect);
    check('épinglé : pas de sondage', !P({pinned: true, expanded: true}).poll);
    check('poll-interval-ms 0 : pas de sondage', !P({pollMs: 0}).poll);
    check('intervalle borné [20, 1000]', P({pollMs: 5}).pollMs === 20 && P({pollMs: 99999}).pollMs === 1000);
    check('mode inconnu -> défauts sûrs', P({blurMode: 'x', unredirectMode: 'y'}).inhibitUnredirect && !P({blurMode: 'x'}).blur);

    // Poller sur horloge factice : compte les ticks réellement exécutés.
    const clock = {now: 0, timers: new Map(), next: 1, added: 0,
        add(ms, fn) {
            const id = this.next++;
            this.timers.set(id, {ms, fn, due: this.now + ms});
            this.added++;
            return id;
        },
        remove(id) {
            this.timers.delete(id);
        },
        advance(ms) {
            const end = this.now + ms;
            for (;;) {
                let best = null;
                for (const [id, t] of this.timers)
                    if (t.due <= end && (!best || t.due < best[1].due))
                        best = [id, t];
                if (!best)
                    break;
                this.now = best[1].due;
                best[1].due += best[1].ms;
                best[1].fn();
            }
            this.now = end;
        }};
    let ticks = 0;
    const poller = new Poller(clock, () => ticks++);
    poller.apply(P({}));
    clock.advance(1000);
    check('visible : 12 ou 13 ticks par seconde à 80 ms', ticks === 12, String(ticks));
    poller.apply(P({userHidden: true}));
    const t0 = ticks;
    clock.advance(60000);
    check('masqué 60 s : ZÉRO tick, aucune minuterie', ticks === t0 && clock.timers.size === 0 && !poller.active,
        `ticks=${ticks - t0} timers=${clock.timers.size}`);
    poller.apply(P({overviewHidden: true}));
    clock.advance(10000);
    check('vue d\'ensemble : zéro tick', ticks === t0);
    poller.apply(P({}));
    poller.apply(P({})); // ré-application idempotente : pas de seconde minuterie
    check('réaffiché : une seule minuterie', clock.timers.size === 1 && clock.added === 2);
    poller.apply(P({pollMs: 200}));
    check('intervalle changé à chaud : minuterie remplacée', clock.timers.size === 1 && poller.intervalMs === 200);
    poller.apply(P({pinned: true, expanded: true}));
    const t1 = ticks;
    clock.advance(5000);
    check('épinglé : zéro tick', ticks === t1 && clock.timers.size === 0);
    poller.stop();
}

out(failures ? `\n${failures} échec(s)` : '\ntous les tests passent');
exit(failures ? 1 : 0);
