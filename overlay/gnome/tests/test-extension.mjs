// Harnais SANS SESSION : exécute le vrai ultima@mio/extension.js sous Node, GNOME Shell bouchonné
// (tests/shell-harness), horloge virtuelle, vrai fichier state.json dans un dossier temporaire.
// Lancer :  node --import ./overlay/gnome/tests/shell-harness/register.mjs overlay/gnome/tests/test-extension.mjs
// ULTIMA_PRINT=1 : imprime le panneau rendu et le journal [ULTIMA].
//
// Ce que ça prouve : câblage de l'extension (lecture sur changement, dernier état valide, rendu
// différé, plan flou/unredirect/sondage, nettoyage). Ce que ça NE prouve PAS : le rendu St réel,
// la hauteur de ligne réelle, le comportement de Mutter (voir PERF.md, « non prouvé »).
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

const RUNTIME = fs.mkdtempSync(path.join(os.tmpdir(), 'ultima-harness-'));
process.env.XDG_RUNTIME_DIR = RUNTIME;
const STATE = path.join(RUNTIME, 'ultima', 'state.json');
const SAMPLE = fs.readFileSync(new URL('../../../docs/22-state.sample.json', import.meta.url), 'utf8');

const S = await import('./shell-harness/stubs.mjs');
const {clock, monitors, compositor, pointer, Main, shared, Settings, io} = S;

const journal = [];
const origLog = console.log, origWarn = console.warn, origErr = console.error;
const capture = kind => (...a) => journal.push(`${kind}${a.join(' ')}`);
console.log = capture('');
console.warn = capture('WARN ');
console.error = capture('ERROR ');

let failures = 0;
function check(name, cond, extra = '') {
    origLog(`${cond ? 'OK  ' : 'FAIL'} ${name}${extra ? ` — ${extra}` : ''}`);
    if (!cond)
        failures++;
}
const flush = () => new Promise(r => setImmediate(r));
const POLL = '[ultima] pointer poll';

shared.settings = new Settings({
    'top-fraction': 0.22, 'height-fraction': 0.36,
    'toggle-panel': ['<Super><Shift>i'], 'toggle-visible': ['<Super><Shift>o'],
    'blur-mode': 'panel-only', 'unredirect-mode': 'while-shown', 'poll-interval-ms': 80,
});

const {default: UltimaExtension} = await import('../ultima@mio/extension.js');
const ext = new UltimaExtension('/nonexistent');
ext.enable();
await flush();

const ov = () => ext._overlay;
const root = () => ov()._root;
const content = () => ov()._content;
const panelText = () => content().children.map(l => l.text).join('\n\n');
const blur = () => ov()._blurEffect.enabled;
async function publish(text, event = 3 /* CREATED */) {
    fs.mkdirSync(path.dirname(STATE), {recursive: true});
    fs.writeFileSync(`${STATE}.tmp`, text);
    fs.renameSync(`${STATE}.tmp`, STATE); // écriture atomique, comme le daemon
    monitors.get(STATE).emit('changed', null, null, event);
    clock.advance(100); // anti-rebond
    await flush();
}
const rail = () => ov()._dotActors.map(d => /background-color: (#[0-9A-F]+)/i.exec(d.style)?.[1]).join(',');

// 1. démarrage sans daemon
check('enable() sans erreur', !journal.some(l => l.startsWith('ERROR')), journal.filter(l => l.startsWith('ERROR')).join(' | '));
check('surveille $XDG_RUNTIME_DIR/ultima/state.json', monitors.has(STATE));
check('rail visible, unredirect inhibé (while-shown), pas de flou sur le rail', root().visible && compositor.inhibit === 1 && !blur());
check('une seule minuterie de sondage', clock.activeNamed(POLL) === 1);
check('aucun état -> rail gris', rail() === '#BFBFBF,#BFBFBF,#BFBFBF', rail());

// 2. le daemon publie : relu sur changement, rendu DIFFÉRÉ panneau replié
const readsBefore = io.reads;
await publish(SAMPLE);
check('une relecture par changement', io.reads === readsBefore + 1, `${io.reads - readsBefore}`);
check('replié : aucun label construit (rendu différé)', content().children.length === 0);
check('rail : sain / malédiction / shop', rail() === '#2AEBBE,#FF5555,#EFC851', rail());
// rafale de 5 événements en 100 ms -> 1 seule relecture
const r0 = io.reads;
for (let i = 0; i < 5; i++) {
    monitors.get(STATE).emit('changed', null, null, 1);
    clock.advance(10);
}
clock.advance(100);
await flush();
check('rafale de 5 événements -> 1 relecture', io.reads === r0 + 1, `${io.reads - r0}`);
monitors.get(STATE).emit('changed', null, null, 4 /* ATTRIBUTE_CHANGED */);
clock.advance(200);
await flush();
check('ATTRIBUTE_CHANGED ignoré', io.reads === r0 + 1);
// aucun minuteur de relecture périodique
check('aucune relecture sans événement (60 s)', (() => {
    const r = io.reads;
    clock.advance(60000);
    return io.reads === r;
})());

// 3. survol -> ouverture, flou, rendu
const L = ov()._layout;
pointer.x = L.railX + 2;
pointer.y = L.y + 20;
clock.advance(80 * 4);
await flush();
check('survol 150 ms -> panneau ouvert', ov()._expanded);
check('panneau ouvert : flou actif (panel-only)', blur());
const text = panelText();
check('panneau : titre du run', text.startsWith('Nécrolieuse A9 · acte 1 · étage 4\nPV 66/66 · 116 or'));
check('panneau : malédiction visible', text.includes("MALÉDICTIONS · 1\n!! 1× Fléau de l'Ascendant"));
check('panneau : notes en lettres', text.includes('carte Scourge · 24 or soldé · A'));
check('panneau 0.36 (≈15 lignes) : troncature ANNONCÉE, le shop garde le plus de lignes',
    /DECK · 11 cartes … \+\d+/.test(text) && /SHOP · 116 or\n.+\n.+\n… \+\d+ autres/.test(text));
shared.settings.set('height-fraction', 0.7);
const full = panelText();
check('panneau 0.70 : tout affiché, rien de tronqué', !full.includes('… +') &&
    full.includes('relique Pendule · 181 or TROP CHER · —') && full.includes('retrait · 75 or · B') &&
    full.includes('4× Frappe · —') && full.includes('pistes : Âmes (exemple)'));
shared.settings.set('height-fraction', 0.36);
check('retour 0.36 : même rendu qu\'avant', panelText() === text);
const curseLabel = content().children.find(l => l.text.startsWith('MALÉDICTIONS'));
check('malédictions en rouge', curseLabel?.style.includes('#FF5555'));
const labelsBefore = content().children.slice();
await publish(SAMPLE.replace('"at": "2026-10-29T13:00:00+01:00"', '"at": "2026-10-29T13:00:05+01:00"'));
check('état republié identique à l\'écran -> aucun label recréé ni modifié',
    content().children.length === labelsBefore.length && content().children.every((l, i) => l === labelsBefore[i]));

// 4. JSON invalide -> dernier état valide gardé
await publish(SAMPLE.slice(0, 300));
check('JSON tronqué : panneau inchangé', panelText() === text);
check('JSON tronqué : avertissement journalisé une fois', journal.filter(l => l.startsWith('WARN') && l.includes('état rejeté')).length === 1);
await publish(SAMPLE.slice(0, 300));
check('même erreur répétée : pas de nouveau message', journal.filter(l => l.includes('état rejeté')).length === 1);

// 5. masquage : zéro sondage
const pin = Main.wm.bindings.get('toggle-panel');
const hide = Main.wm.bindings.get('toggle-visible');
hide();
const reads = pointer.reads;
const pollFired = clock.fired.get(POLL) ?? 0;
clock.advance(10 * 60 * 1000);
check('masqué : acteur invisible, flou coupé, unredirect rendu', !root().visible && !blur() && compositor.inhibit === 0);
check('masqué 10 min : ZÉRO lecture du pointeur, ZÉRO tick, aucune minuterie de sondage',
    pointer.reads === reads && (clock.fired.get(POLL) ?? 0) === pollFired && clock.activeNamed(POLL) === 0,
    `lectures=${pointer.reads - reads} ticks=${(clock.fired.get(POLL) ?? 0) - pollFired} sources=${clock.activeNamed(POLL)}`);
check('masqué : aucune source GLib de l\'extension restante', clock.sources.size === 0, `${clock.sources.size}`);
hide();
check('réaffiché : sondage repris (1 minuterie), unredirect inhibé', clock.activeNamed(POLL) === 1 && compositor.inhibit === 1);

// vue d'ensemble = masqué aussi
Main.overview.emit('showing');
check('vue d\'ensemble : zéro sondage', clock.activeNamed(POLL) === 0 && !root().visible);
Main.overview.emit('hidden');

// 6. épinglé : pas de sondage
pin();
check('épinglé : panneau ouvert, aucun sondage', ov()._expanded && clock.activeNamed(POLL) === 0);
pin();
check('libéré : repli, sondage repris, flou coupé (rail = aplat)', !ov()._expanded && clock.activeNamed(POLL) === 1 && !blur());

// 7. réglages A/B à chaud
shared.settings.set('blur-mode', 'always');
check('blur-mode=always : flou sur le rail', blur());
shared.settings.set('blur-mode', 'never');
pin();
check('blur-mode=never : pas de flou panneau ouvert', !blur());
pin();
shared.settings.set('blur-mode', 'panel-only');
shared.settings.set('unredirect-mode', 'while-expanded');
check('unredirect-mode=while-expanded : rail -> unredirect permis', compositor.inhibit === 0);
pin();
check('unredirect-mode=while-expanded : panneau -> inhibé', compositor.inhibit === 1);
pin();
shared.settings.set('unredirect-mode', 'never');
pin();
check('unredirect-mode=never : jamais inhibé', compositor.inhibit === 0);
pin();
shared.settings.set('unredirect-mode', 'while-shown');
shared.settings.set('poll-interval-ms', 0);
check('poll-interval-ms=0 : aucun sondage', clock.activeNamed(POLL) === 0);
shared.settings.set('poll-interval-ms', 200);
const pr = pointer.reads;
clock.advance(1000);
check('poll-interval-ms=200 : 5 lectures/s', pointer.reads - pr === 5, `${pointer.reads - pr}`);
check('appels unredirect équilibrés', compositor.calls.filter(c => c === 'disable').length -
    compositor.calls.filter(c => c === 'enable').length === compositor.inhibit);

// 8. version future, jeu fermé, daemon arrêté
const fut = JSON.parse(SAMPLE);
fut.contractVersion = 2;
await publish(JSON.stringify(fut));
pin();
check('version future : « mettre à jour ULTIMA »', panelText() === 'ULTIMA · mettre à jour ULTIMA\nétat v2, overlay compatible v1 seulement');
const closed = JSON.parse(SAMPLE);
closed.health.gameRunning = false;
await publish(JSON.stringify(closed));
check('jeu fermé : « en attente du jeu »', panelText() === 'ULTIMA · en attente du jeu');
fs.unlinkSync(STATE);
monitors.get(STATE).emit('changed', null, null, 2 /* DELETED */);
clock.advance(100);
await flush();
check('state.json supprimé : en attente + daemon arrêté ?', panelText() === 'ULTIMA · en attente du jeu\naucun état publié (daemon arrêté ?)');
const dg = JSON.parse(SAMPLE);
dg.health.degraded = 'miroir indisponible';
await publish(JSON.stringify(dg));
check('dégradé en tête du panneau', panelText().startsWith('DÉGRADÉ : miroir indisponible\n\n'));
check('dégradé en orange', content().children[0].style.includes('#FFA518'));
if (process.env.ULTIMA_PRINT === '1') {
    await publish(SAMPLE);
    origLog(`---- panneau (1536x960, height-fraction 0.36, ligne 17 px simulée) ----\n${panelText()}\n----`);
    shared.settings.set('height-fraction', 0.7);
    origLog(`---- panneau (height-fraction 0.70) ----\n${panelText()}\n----`);
    shared.settings.set('height-fraction', 0.36);
}
pin();

// 9. disable() : tout rendu
ext.disable();
await flush();
check('disable() : acteur retiré, unredirect rendu, aucun minuteur, raccourcis retirés',
    Main.layoutManager.chrome.length === 0 && compositor.inhibit === 0 && clock.sources.size === 0 &&
    Main.wm.bindings.size === 0, `chrome=${Main.layoutManager.chrome.length} inhibit=${compositor.inhibit} sources=${clock.sources.size}`);
check('disable() : signaux déconnectés, moniteur annulé',
    Main.layoutManager.handlerCount === 0 && Main.overview.handlerCount === 0 &&
    shared.settings.handlerCount === 0 && monitors.get(STATE).cancelled);
check('aucune erreur journalisée', !journal.some(l => l.startsWith('ERROR')), journal.filter(l => l.startsWith('ERROR')).join(' | '));

if (process.env.ULTIMA_PRINT === '1')
    origLog(`---- journal [ULTIMA] ----\n${journal.join('\n')}`);
fs.rmSync(RUNTIME, {recursive: true, force: true});
origLog(failures ? `\n${failures} échec(s)` : '\ntous les tests passent');
process.exit(failures ? 1 : 0);
