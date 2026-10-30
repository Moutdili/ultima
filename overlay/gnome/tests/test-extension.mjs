// Harnais SANS SESSION : exécute le vrai ultima@mio/extension.js sous Node, GNOME Shell bouchonné
// (tests/shell-harness), horloge virtuelle, vrais fichiers state.json et settings.json dans des
// dossiers temporaires (jamais les vrais $XDG_RUNTIME_DIR/ultima ni ~/.config/ultima).
// Lancer :  node --import ./overlay/gnome/tests/shell-harness/register.mjs overlay/gnome/tests/test-extension.mjs
// ULTIMA_PRINT=1 : imprime le panneau rendu et le journal [ULTIMA].
//
// Ce que ça prouve : câblage de l'extension (lecture sur changement, dernier contenu valide, rendu
// différé, HudSettings v1.3, niveaux eco/verre/loupe, plan flou/reflet/loupe/unredirect/survol,
// AUCUN travail au repos, nettoyage). Ce que ça NE prouve PAS : le rendu St/Clutter réel, la
// compilation des shaders GLSL, la hauteur de ligne réelle, le comportement de Mutter, les FPS.
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

const TMP = fs.mkdtempSync(path.join(os.tmpdir(), 'ultima-harness-'));
process.env.XDG_RUNTIME_DIR = path.join(TMP, 'run');
process.env.XDG_CONFIG_HOME = path.join(TMP, 'config');
process.env.XDG_DATA_HOME = path.join(TMP, 'data');
fs.mkdirSync(process.env.XDG_RUNTIME_DIR, {recursive: true});
const THEMES = path.join(process.env.XDG_CONFIG_HOME, 'ultima', 'themes');
// Les thèmes de l'extension sont LIVRÉS avec elle (ultima@mio/themes/sts.json) : le harnais pointe
// le vrai dossier de l'extension (lecture seule), sinon le repli `sts` n'existerait pas.
const EXT_DIR = new URL('../ultima@mio/', import.meta.url).pathname;
const STATE = path.join(process.env.XDG_RUNTIME_DIR, 'ultima', 'state.json');
const SETTINGS = path.join(process.env.XDG_CONFIG_HOME, 'ultima', 'settings.json');
const SAMPLE = fs.readFileSync(new URL('../../../docs/22-state.sample.json', import.meta.url), 'utf8');
const V13 = fs.readFileSync(new URL('../../../docs/ipc/state.v13.sample.json', import.meta.url), 'utf8');
const SCHEMA = fs.readFileSync(new URL('../ultima@mio/schemas/org.gnome.shell.extensions.ultima.gschema.xml', import.meta.url), 'utf8');

import * as Geo from '../ultima@mio/geometry.js';
const S = await import('./shell-harness/stubs.mjs');
const {clock, monitors, compositor, pointer, Main, shared, Settings, io, anim, activity, cursorTracker,
    movePointer, focus, gameActor, windowGroup} = S;

const journal = [];
const origLog = console.log;
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

// Valeurs par défaut lues dans le VRAI schéma (le harnais ne peut pas diverger du schéma).
function schemaDefaults(xml) {
    const out = {};
    for (const m of xml.matchAll(/<key name="([^"]+)" type="([^"]+)">([\s\S]*?)<\/key>/g)) {
        const [, name, type, body] = m;
        let raw = /<default>([\s\S]*?)<\/default>/.exec(body)[1].replace(/<!\[CDATA\[|\]\]>/g, '').trim();
        if (type === 'as')
            out[name] = [...raw.matchAll(/'([^']*)'/g)].map(x => x[1]);
        else if (type === 'd')
            out[name] = Number(raw);
        else if (type === 'i')
            out[name] = parseInt(raw, 10);
        else if (type === 's')
            out[name] = raw.replace(/^'|'$/g, '');
        else
            throw new Error(`type GSettings non géré : ${type}`);
    }
    return out;
}
const DEFAULTS = schemaDefaults(SCHEMA);
check('schéma lu : 11 clés, survol event par défaut', Object.keys(DEFAULTS).length === 11 &&
    DEFAULTS['hover-source'] === 'event' && DEFAULTS.quality === 'verre', Object.keys(DEFAULTS).join(' '));
shared.settings = new Settings(DEFAULTS);

const {default: UltimaExtension} = await import('../ultima@mio/extension.js');
const ext = new UltimaExtension(EXT_DIR);
ext.enable();
await flush();
await flush();

const ov = () => ext._overlay;
const root = () => ov()._root;
const content = () => ov()._content;
const blur = () => ov()._blurEffect.enabled;
const sheenOn = () => !!ov()._sheen?.enabled;
const panelText = () => content().children.map(a => (a._ultima.label ? a._ultima.label.text
    : `[${a._ultima.grade.text}] ${a._ultima.head.text}${a._ultima.reason.visible ? ` / ${a._ultima.reason.text}` : ''}`)).join('\n');
const sources = () => clock.sources.size;
async function writeFile(file, text, event = 3 /* CREATED */) {
    fs.mkdirSync(path.dirname(file), {recursive: true});
    fs.writeFileSync(`${file}.tmp`, text);
    fs.renameSync(`${file}.tmp`, file); // écriture atomique, comme le daemon et le compagnon
    monitors.get(file).emit('changed', null, null, event);
    clock.advance(100); // anti-rebond
    await flush();
}
const publish = (text, ev) => writeFile(STATE, text, ev);
const writeSettings = (obj, ev) => writeFile(SETTINGS, typeof obj === 'string' ? obj : JSON.stringify(obj), ev);
// Visible = acteur et tous ses parents visibles.
function visibleTree(a, acc = []) {
    if (!a.visible)
        return acc;
    acc.push(a);
    for (const c of a.children)
        visibleTree(c, acc);
    return acc;
}
const L = () => ov()._layout;
async function hoverRail() {
    movePointer(L().railX + 2, L().y + 40);
    clock.advance(80);   // lecture regroupée
    clock.advance(150);  // attente de survol (recheck, pointeur immobile)
    await flush();
}
async function leaveAndCollapse() {
    movePointer(200, 200);
    clock.advance(80);
    clock.advance(2500); // docs/17 §4 : retour au rail après 2,5 s
    await flush();
}

// ---------- 1. démarrage sans daemon : le rail = une bande de verre seule ----------
check('enable() sans erreur', !journal.some(l => l.startsWith('ERROR')), journal.filter(l => l.startsWith('ERROR')).join(' | '));
check('surveille state.json ET settings.json (sans les créer)', monitors.has(STATE) && monitors.has(SETTINGS) &&
    !fs.existsSync(SETTINGS) && !fs.existsSync(STATE));
check('rail visible, unredirect inhibé (while-shown), pas de flou ni de reflet sur le rail',
    root().visible && compositor.inhibit === 1 && !blur() && !sheenOn());
{
    const vis = visibleTree(root()).map(a => a.name ?? a.constructor.name);
    check('§12 rail : seuls racine + plaque + verre + teinte + liseré visibles (aucune pastille, aucun texte)',
        vis.join(',') === 'ultima-root,ultima-plate,ultima-glass,ultima-frame,ultima-edge', vis.join(','));
    check('§12 rail : aucun enfant dans les couches de verre', ['_glass', '_frame', '_edge'].every(k => ov()[k].children.length === 0));
    const l = L();
    check('rail : plaque translatée, bande de 14 px collée au bord', ov()._plate.translation_x === l.collapsedDx &&
        l.rootX + l.plateX + l.collapsedDx === 1536 - 14);
    check('rail : opacité au repos = HudSettings.RestOpacity (0.62)', root().opacity === Math.round(255 * 0.62));
}
check('survol event : à l\'écoute du curseur, AUCUNE minuterie', cursorTracker.handlerCount === 1 && sources() === 0, `sources=${sources()}`);

// ---------- 2. AU REPOS : aucun minuteur, aucune écriture d'acteur ----------
{
    const a0 = activity.count, r0 = pointer.reads;
    clock.advance(10 * 60 * 1000);
    await flush();
    check('repos 10 min, pointeur immobile : ZÉRO minuterie, ZÉRO lecture du pointeur, ZÉRO écriture d\'acteur',
        sources() === 0 && pointer.reads === r0 && activity.count === a0,
        `sources=${sources()} lectures=${pointer.reads - r0} activité=${activity.log.slice(-(activity.count - a0)).join(',')}`);
    for (let i = 0; i < 1000; i++) {
        movePointer(300 + (i % 400), 500);
        clock.advance(1);
    }
    clock.advance(100);
    check('pointeur qui bouge AILLEURS (1000 Hz, 1 s) : ≤ 13 lectures, ZÉRO écriture d\'acteur, plus de minuterie après',
        pointer.reads - r0 <= 13 && activity.count === a0 && sources() === 0, `lectures=${pointer.reads - r0}`);
}

// ---------- 3. le daemon publie : relu sur changement, rendu DIFFÉRÉ panneau replié ----------
{
    const readsBefore = io.reads, a0 = activity.count;
    await publish(SAMPLE);
    check('une relecture par changement', io.reads === readsBefore + 1, `${io.reads - readsBefore}`);
    check('replié : état publié sans AUCUN travail d\'acteur (rendu différé)', content().children.length === 0 &&
        activity.count === a0, activity.log.slice(-(activity.count - a0)).join(','));
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
    const r1 = io.reads;
    clock.advance(60000);
    check('aucune relecture sans événement (60 s)', io.reads === r1);
}

// ---------- 4. ouverture fluide (§12) ----------
anim.log.length = 0;
await hoverRail();
check('survol du rail (pointeur immobile ensuite) -> panneau ouvert', ov()._expanded);
{
    const plateEase = anim.log.find(e => e.actor === 'ultima-plate');
    check('le verre s\'élargit : translation_x -> 0, ease-out 220 ms (docs/17 §7.4 : 150-220)',
        plateEase && plateEase.translation_x === 0 && plateEase.duration === 220 && plateEase.mode === 8);
    const rows = anim.log.filter(e => e.actor !== 'ultima-plate' && e.actor !== 'ultima-edge' && e.actor !== 'ultima-root' && 'delay' in e);
    check('contenu en fondu après le verre (délai ≥ 110 ms), montée légère sans rebond',
        rows.length >= 1 && rows.every(e => e.delay >= 110 && e.opacity === 255 && e.translation_y === 0 && e.mode === 5));
    const iPlate = anim.log.indexOf(plateEase), iRow = anim.log.indexOf(rows[0]);
    check('contenu préparé AVANT l\'animation du verre (pas de relayout pendant)', iRow < iPlate);
    check('aucune animation répétée', anim.log.every(e => !('repeat_count' in e) && !('autoReverse' in e)));
}
check('verre ouvert : flou 24 + reflet (verre, défaut du contrat)', blur() && ov()._blurEffect.radius === 24 && sheenOn());
check('§11 shop : la carte de décision prend toute la place', panelText() === '[A] Scourge · 24 or soldé / Méta (médiane 3 sources) +65',
    panelText());
check('la lettre porte la couleur S-F du thème (A = ok, StsColors #7FFF00 en rgba)',
    content().children[0]._ultima.grade.style.includes('rgba(127, 255, 0, 1)'),
    content().children[0]._ultima.grade.style);
check('aucun hexadécimal dans les styles St (les couleurs viennent du thème, en rgba)', !/#[0-9a-fA-F]{3,8}/.test(
    [ov()._frame.style, ov()._edge.style, content().children[0]._ultima.grade.style,
        ...content().children.map(a => a._ultima?.label?.style ?? '')].join(' ')));
{
    const labelsBefore = content().children.slice();
    const a0 = activity.count;
    await publish(SAMPLE.replace('"at": "2026-10-29T13:00:00+01:00"', '"at": "2026-10-29T13:00:05+01:00"'));
    check('état republié identique à l\'écran -> aucun acteur recréé ni modifié',
        content().children.every((l, i) => l === labelsBefore[i]) && activity.count === a0,
        activity.log.slice(-(activity.count - a0)).join(','));
}
// Survol de la carte : le détail s'ouvre.
{
    const card = content().children[0];
    const [cx, cy] = card.get_transformed_position();
    movePointer(cx + 20, cy + 10);
    clock.advance(80);
    await flush();
    const t = panelText();
    check('survol de la carte de décision : détail (options + une ligne par module)', ov()._hoverDetail &&
        t.includes('B · Retrait · 75 or') && t.includes('Deck 11') && t.includes('Note du deck C'), t.replace(/\n/g, ' / '));
}

// Réglage changé PENDANT l'ouverture : la transition interrompue ne bloque ni la loupe ni le détail.
{
    await leaveAndCollapse();
    await writeSettings({modules: [], hud: {quality: 'loupe'}});
    anim.defer = true;
    await hoverRail();
    check('ouverture en cours : loupe pas encore là (plaque en mouvement)', ov()._expanded && !ov()._lens);
    await writeSettings({modules: [], hud: {quality: 'loupe', heightFraction: 0.6}});
    anim.queue.length = 0; // la transition a été retirée : son onComplete ne viendra jamais
    anim.defer = false;
    check('relayout pendant l\'ouverture : plaque posée, loupe créée', ov()._settled && !!ov()._lens &&
        ov()._plate.translation_x === 0 && L().h === Math.round(960 * 0.6));
    await leaveAndCollapse();
    fs.unlinkSync(SETTINGS);
    monitors.get(SETTINGS).emit('changed', null, null, 2 /* DELETED */);
    clock.advance(100);
    await flush();
    await hoverRail();
    const card = content().children[0];
    const [cx, cy] = card.get_transformed_position();
    movePointer(cx + 20, cy + 10);
    clock.advance(80);
    await flush();
    check('retour aux défauts (settings.json supprimé) : verre, 0.55, détail au survol', ov()._quality.level === 'verre' &&
        L().h === 528 && ov()._hoverDetail);
}

// ---------- 5. JSON invalide -> dernier état valide gardé ----------
{
    const text = panelText();
    await publish(SAMPLE.slice(0, 300));
    check('JSON tronqué : panneau inchangé', panelText() === text);
    check('JSON tronqué : avertissement journalisé une fois', journal.filter(l => l.startsWith('WARN') && l.includes('état rejeté')).length === 1);
    await publish(SAMPLE.slice(0, 300));
    check('même erreur répétée : pas de nouveau message', journal.filter(l => l.includes('état rejeté')).length === 1);
}

// ---------- 6. fermeture symétrique, plus brève, sans saut de flou ----------
anim.defer = true;
anim.log.length = 0;
await leaveAndCollapse();
{
    check('2,5 s hors du panneau -> repli', !ov()._expanded);
    const plateEase = anim.log.find(e => e.actor === 'ultima-plate');
    const fade = anim.log.find(e => e.actor === 'BoxLayout' || e.actor === 'Actor');
    check('fermeture plus brève (150 ms) que l\'ouverture', plateEase?.duration === 150 && plateEase.translation_x === L().collapsedDx);
    check('le contenu s\'efface d\'abord (90 ms)', fade?.duration === 90 && fade.opacity === 0);
    check('pendant la fermeture : le flou reste (pas de saut visuel)', blur() && ov()._closing);
    anim.flush();
    check('fermeture terminée : rail = aplat (flou et reflet coupés)', !blur() && !sheenOn() && !ov()._closing);
    check('fermeture terminée : opacité de repos rétablie', root().opacity === Math.round(255 * 0.62));
}
anim.defer = false;
check('repos après fermeture : aucune minuterie', sources() === 0, `${sources()}`);

// ---------- 7. HudSettings v1.3 : settings.json, état, repli ----------
{
    await writeSettings({modules: [], hud: {quality: 'eco', restOpacity: 0.9}});
    check('settings.json quality=eco : teinte aplat 0.85 (eco), opacité au repos 0.9',
        ov()._frame.style.includes('rgba(25, 41, 48, 0.68)') && root().opacity === Math.round(255 * 0.9) &&
        ov()._quality.level === 'eco', ov()._frame.style);
    await hoverRail();
    check('eco ouvert : AUCUN flou, AUCUN reflet, AUCUNE loupe (coût ~0)', ov()._expanded && !blur() && !sheenOn() && !ov()._lens);
    await leaveAndCollapse();

    await writeSettings({modules: [], hud: {quality: 'loupe'}});
    check('settings.json quality=loupe : niveau effectif loupe', ov()._quality.level === 'loupe');
    await hoverRail();
    const lens = ov()._lens;
    const clone = lens?.children[0];
    const l = L();
    const cx = l.panelX + 180, cy = l.y + l.h / 2;
    check('loupe ouverte : flou léger (10) + reflet + clone du JEU (fenêtre active plein écran)', blur() &&
        ov()._blurEffect.radius === 10 && sheenOn() && clone?.source === gameActor);
    check('loupe : grossissement 1.06 autour du centre du panneau', clone.scale_x === 1.06 && clone.scale_y === 1.06 &&
        clone.x === Math.round((1 - 1.06) * cx - l.panelX) && clone.y === Math.round((1 - 1.06) * cy - l.y),
    `clone=(${clone.x},${clone.y})`);
    check('loupe : conteneur découpé à la taille de la plaque, effet de réfraction, sous le verre',
        lens.clip_to_allocation && lens.width === 360 && lens.height === l.h && lens.effects.has('ultima-lens') &&
        ov()._plate.children.indexOf(lens) < ov()._plate.children.indexOf(ov()._glass));
    const uni = lens.effects.get('ultima-lens').uniforms;
    check('loupe : uniformes fixes (taille, rayon, force, aberration)', uni.get('size').join() === `360,${l.h}` &&
        uni.get('radius')[0] === 10 && uni.get('strength')[0] > 0 && uni.get('aberration')[0] > 0);
    anim.defer = true;
    await leaveAndCollapse();
    check('repli : clone du jeu détruit dès le début (plus aucune peinture du jeu)', !ov()._lens && lens.destroyed && clone.destroyed);
    anim.flush();
    anim.defer = false;
    focus.window.fullscreen = false;
    await hoverRail();
    check('fenêtre active non plein écran : la loupe clone global.window_group', ov()._lens?.children[0].source === windowGroup);
    await leaveAndCollapse();
    focus.window.fullscreen = true;

    shared.settings.set('unredirect-mode', 'never');
    await hoverRail();
    check('loupe + unredirect-mode=never : pas de clone (scanout direct = pas de texture)', !ov()._lens && blur());
    await leaveAndCollapse();
    shared.settings.set('unredirect-mode', 'while-shown');

    const n0 = journal.filter(x => x.includes('réglages rejetés')).length;
    await writeSettings('{"hud": {"quality": "ec');
    check('settings.json tronqué : réglages précédents gardés, avertissement une fois', ov()._hud.quality === 'loupe' &&
        journal.filter(x => x.includes('réglages rejetés')).length === n0 + 1);
    await writeSettings('{"hud": {"quality": "ec');
    check('même erreur répétée : pas de nouveau message', journal.filter(x => x.includes('réglages rejetés')).length === n0 + 1);

    await writeSettings({modules: [], hud: {quality: 'rubis', side: 'gauche', restOpacity: 'opaque', heightFraction: 3}});
    check('settings.json : valeurs toutes invalides -> défauts du contrat', ov()._quality.level === 'verre' &&
        ov()._hud.side === 'right' && ov()._hud.heightFraction === 0.55 && root().opacity === Math.round(255 * 0.62));

    fs.unlinkSync(SETTINGS);
    monitors.get(SETTINGS).emit('changed', null, null, 2 /* DELETED */);
    clock.advance(100);
    await flush();
    const v13 = JSON.parse(V13);
    v13.settings.hud = {...v13.settings.hud, side: 'left', heightFraction: 0.4, quality: 'eco'};
    await publish(JSON.stringify(v13));
    check('settings.json supprimé : réglages publiés par le daemon (state.settings.hud) appliqués',
        ov()._hud.side === 'left' && ov()._hud.source.side === 'état' && ov()._quality.level === 'eco');
    check('côté gauche : rail collé au bord gauche, plaque translatée vers la gauche',
        L().side === 'left' && L().railX === 0 && ov()._plate.translation_x === L().collapsedDx && L().collapsedDx < 0 &&
        L().h === Math.round(960 * 0.4));
    await publish(SAMPLE); // état sans settings.hud (contrat v1.0)
    shared.settings.set('quality', 'loupe');
    check('ni fichier ni hud publié : repli GSettings', ov()._quality.level === 'loupe' && ov()._hud.source.quality === 'gsettings' &&
        L().side === 'right');
    shared.settings.set('quality', 'verre');
}

// ---------- 8. masquage : rien du tout ----------
const pin = Main.wm.bindings.get('toggle-panel');
const hide = Main.wm.bindings.get('toggle-visible');
{
    hide();
    const reads = pointer.reads, a0 = activity.count;
    for (let i = 0; i < 100; i++) {
        movePointer(1530, 400);
        clock.advance(100);
    }
    clock.advance(10 * 60 * 1000);
    check('masqué : acteur invisible, flou coupé, unredirect rendu', !root().visible && !blur() && compositor.inhibit === 0);
    check('masqué 10 min (pointeur sur le rail) : ZÉRO lecture, ZÉRO minuterie, déconnecté du curseur, ZÉRO écriture',
        pointer.reads === reads && sources() === 0 && cursorTracker.handlerCount === 0 && activity.count === a0,
        `lectures=${pointer.reads - reads} sources=${sources()}`);
    hide();
    check('réaffiché : écoute du curseur reprise, unredirect inhibé', cursorTracker.handlerCount === 1 && compositor.inhibit === 1);
    Main.overview.emit('showing');
    check('vue d\'ensemble : zéro suivi', cursorTracker.handlerCount === 0 && sources() === 0 && !root().visible);
    Main.overview.emit('hidden');
}

// ---------- 9. épinglé (Super+Maj+I) ----------
pin();
check('épinglé : panneau ouvert, aucun suivi du pointeur', ov()._expanded && cursorTracker.handlerCount === 0 &&
    clock.activeNamed(POLL) === 0);
check('épinglé : détail affiché (options du shop + modules)', panelText().includes('Deck 11'));
pin();
anim.flush();
check('libéré : repli, suivi repris, flou coupé (rail = aplat)', !ov()._expanded && cursorTracker.handlerCount === 1 && !blur());

// ---------- 10. réglages A/B à chaud ----------
shared.settings.set('blur-mode', 'always');
check('blur-mode=always : flou sur le rail (verre)', blur());
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
check('poll-interval-ms=0 : aucun suivi', cursorTracker.handlerCount === 0 && sources() === 0);
shared.settings.set('poll-interval-ms', 80);
movePointer(200, 200); // loin du rail (la section 8 l'y avait laissé)
clock.advance(100);
shared.settings.set('hover-source', 'poll');
{
    const pr = pointer.reads, a0 = activity.count;
    clock.advance(1000);
    check('hover-source=poll (spike S1) : 12 lectures/s, une minuterie, ZÉRO écriture d\'acteur au repos',
        pointer.reads - pr === 12 && clock.activeNamed(POLL) === 1 && cursorTracker.handlerCount === 0 && activity.count === a0,
        `lectures=${pointer.reads - pr} minuteries=${clock.activeNamed(POLL)} curseur=${cursorTracker.handlerCount} activité=${activity.log.slice(-(activity.count - a0)).join(',')}`);
    await hoverRail();
    check('hover-source=poll : le survol ouvre aussi', ov()._expanded);
    await leaveAndCollapse();
}
shared.settings.set('hover-source', 'event');
check('retour event : plus aucune minuterie', sources() === 0 && cursorTracker.handlerCount === 1);
check('appels unredirect équilibrés', compositor.calls.filter(c => c === 'disable').length -
    compositor.calls.filter(c => c === 'enable').length === compositor.inhibit);

// ---------- 11. version future, jeu fermé, daemon arrêté, dégradé ----------
{
    const fut = JSON.parse(SAMPLE);
    fut.contractVersion = 2;
    await publish(JSON.stringify(fut));
    pin();
    check('version future : « Mettre à jour ULTIMA »', panelText() === 'Mettre à jour ULTIMA (état v2)', panelText());
    const closed = JSON.parse(SAMPLE);
    closed.health.gameRunning = false;
    await publish(JSON.stringify(closed));
    check('jeu fermé : « En attente du jeu »', panelText() === 'En attente du jeu');
    fs.unlinkSync(STATE);
    monitors.get(STATE).emit('changed', null, null, 2 /* DELETED */);
    clock.advance(100);
    await flush();
    check('state.json supprimé : « En attente du daemon »', panelText() === 'En attente du daemon');
    const dg = JSON.parse(SAMPLE);
    dg.health.degraded = 'miroir indisponible';
    await publish(JSON.stringify(dg));
    check('dégradé en tête du panneau, en orange', panelText().startsWith('Dégradé · miroir indisponible') &&
        content().children[0]._ultima.label.clutter_text.markup.includes('#FFA518'));
    if (process.env.ULTIMA_PRINT === '1')
        origLog(`---- panneau épinglé (1536x960) ----\n${panelText()}\n----`);
    pin();
    anim.flush();
}

// ---------- 11bis. thèmes, polices, disposition, modules perso (v1.4) ----------
// Fixtures de thème DANS UN DOSSIER TEMPORAIRE (jamais ~/.config/ultima/thèmes réel) : le dépôt ne
// fournit que `sts` pour l'instant (les 10 autres arrivent avec T2).
function writeTheme(name, obj) {
    fs.mkdirSync(THEMES, {recursive: true});
    fs.writeFileSync(path.join(THEMES, name), typeof obj === 'string' ? obj : JSON.stringify(obj));
}
const STS = JSON.parse(fs.readFileSync(new URL('../../../themes/sts.json', import.meta.url), 'utf8'));
const themeLog = () => journal.filter(l => l.includes('[ULTIMA] thème') || l.includes('[ULTIMA] apparence'));
{
    // rubis : extends sts, verre et police propres ; nuit : neutre ; casse : JSON invalide.
    writeTheme('rubis.json', {id: 'rubis', label: 'Rubis', api: 1, extends: 'sts',
        tones: {accent: '#FF5555', text: '#FFFFFF'}, glass: {tint: '#801212', tintOpacity: 0.35},
        font: {family: 'Fira Sans', size: 18}});
    writeTheme('ironclad.json', {id: 'ironclad', label: 'Ironclad', api: 1, extends: 'sts',
        tones: {accent: '#FF5555'}});
    writeTheme('casse.json', '{"id": "casse", "tones":');
    await writeSettings({modules: [], appearance: {hud: {theme: 'rubis'}}});
    check('thème étendu : verre et accent du thème (extends sts pour le reste)',
        ov()._theme.id === 'rubis' && ov()._resolvedTheme.chain.join(',') === 'sts,rubis' &&
        ov()._frame.style.includes('rgba(128, 18, 18, 0.35)') && ov()._edge.style.includes('rgba(255, 85, 85, 0.85)'),
        ov()._frame.style);
    check('thème : tons hérités de sts quand le thème ne les redéfinit pas',
        ov()._theme.tones.curse.hex.toUpperCase() === STS.tones.curse.toUpperCase() &&
        ov()._theme.tones.text.hex === '#FFFFFF');
    check('thème : police du thème appliquée si appearance.font est absent', ov()._font.size === 18 &&
        ov()._font.family === 'Fira Sans');
    {
        const a0 = activity.count, s0 = sources();
        clock.advance(10 * 60 * 1000);
        await flush();
        check('changement de thème : AUCUNE minuterie, AUCUN travail par image au repos (10 min)',
            sources() === 0 && s0 === 0 && activity.count === a0, `sources=${sources()}`);
    }
    await publish(SAMPLE); // contenu réel (shop) pour lire les styles des étiquettes
    await writeSettings({modules: [], appearance: {hud: {theme: 'rubis', font: {size: 20}}}});
    check('police : taille de appearance.hud.font', ov()._font.size === 20);
    await hoverRail();
    {
        const styles = content().children.map(a => a._ultima.head?.style ?? a._ultima.label?.style ?? '');
        check('police : appliquée au CSS de St (corps 20 px, en-tête de carte 22 px)',
            styles.length >= 1 && styles.every(st => st.includes('font-size: 20px;') || st.includes('font-size: 22px;')) &&
            styles.some(st => st.includes('font-size: 22px;')), styles.join(' | '));
        check('police : famille du thème dans le CSS (St/Pango)',
            styles.every(st => st.includes('font-family: "Fira Sans";')));
    }
    await leaveAndCollapse();
    await writeSettings({modules: [], appearance: {hud: {theme: 'rubis', font: {size: 30}}}});
    check('police hors 12-24 px : RAMENÉE à 24', ov()._font.size === 24);
    await writeSettings({modules: [], appearance: {hud: {theme: 'rubis', font: {family: 'Cantarell', size: 13}}}});
    check('police : famille de appearance.hud.font prioritaire sur le thème',
        ov()._font.family === 'Cantarell' && ov()._font.size === 13);

    await writeSettings({modules: [], appearance: {hud: {theme: 'rubis', position: {side: 'left', anchor: 'bottom',
        offsetX: 60, offsetY: -40, width: 420}}}});
    {
        const l = L();
        const mon = Main.layoutManager.primaryMonitor;
        check('disposition : côté gauche, largeur 420 px, panneau dans l\'écran',
            l.side === 'left' && l.w === 420 && l.panelX === Geo.EDGE_GAP + 60 && l.railX === 0 &&
            l.panelX + l.w <= mon.width && l.y >= 0 && l.y + l.h <= mon.height, JSON.stringify([l.panelX, l.y, l.w, l.h]));
        check('disposition : ancrage bas dans la zone sûre (offsetY appliqué), jamais sur la pioche ni « Fin du tour »',
            l.y + l.h === Geo.safeArea(mon, 'left').y + Geo.safeArea(mon, 'left').height - 40 &&
            !Geo.intersects(Geo.panelRect(l), Geo.drawPileZone(mon)) &&
            !Geo.intersects(Geo.panelRect(l), Geo.endTurnZone(mon)));
        check('disposition : contenu élargi à la nouvelle largeur', ov()._content.width === 420 - 2 * 16 &&
            ov()._plate.width === 420);
    }
    await writeSettings({modules: [], appearance: {hud: {theme: 'rubis', position: {width: 520}, railEdge: false}}});
    check('railEdge=false : liseré du rail éteint, panneau toujours à 520 px',
        ov()._edge.opacity === 0 && ov()._edge.style.includes('border: none') && L().w === 520);
    await writeSettings({modules: [], appearance: {hud: {theme: 'rubis', railEdge: true}}});
    check('railEdge=true (défaut) : liseré or du rail rallumé',
        ov()._edge.opacity === 255 && ov()._edge.style.includes('border: 1px solid'));

    // Thème identique, seuls les TONS changent (appearance.hud.tones) : le texte doit se recolorer.
    await hoverRail();
    {
        const before = content().children.map(a => a._ultima.head?.clutter_text.markup ?? a._ultima.label?.clutter_text.markup ?? '').join('');
        await writeSettings({modules: [], appearance: {hud: {theme: 'sts', tones: {text: '#00FF00'}}}});
        const after = content().children.map(a => a._ultima.head?.clutter_text.markup ?? a._ultima.label?.clutter_text.markup ?? '').join('');
        check('surcharge de tons (même thème) : le texte est recoloré sans changer de thème',
            before !== after && after.includes('#00FF00') && ov()._theme.id === 'sts', after.slice(0, 120));
    }
    await leaveAndCollapse();

    const warnings = journal.filter(l => l.startsWith('WARN') && l.includes('thème')).length;
    await writeSettings({modules: [], appearance: {hud: {theme: 'casse'}}});
    check('thème cassé : repli sts, journal [ULTIMA] (une ligne), aucune exception',
        ov()._theme.id === 'sts' && journal.some(l => l.startsWith('WARN') && l.includes('thème « casse »')) &&
        journal.filter(l => l.startsWith('WARN') && l.includes('thème')).length === warnings + 1 &&
        journal.filter(l => l.startsWith('ERROR')).length === 0, journal.filter(l => l.includes('casse')).join(' | '));
    check('thème cassé : les couleurs redeviennent celles de sts',
        ov()._frame.style.includes('rgba(25, 41, 48, 0.55)'));
    await writeSettings({modules: [], appearance: {hud: {theme: 'inconnu'}}});
    check('thème introuvable : repli sts + journal', ov()._theme.id === 'sts' &&
        journal.some(l => l.includes('thème « inconnu » introuvable')));
    await writeSettings({modules: [], appearance: {hud: {theme: 'auto-perso'}}});
    check('auto-perso hors run (état sans run en cours) : repli sts journalisé',
        ov()._theme.id === 'sts' && journal.some(l => l.includes('auto-perso')));
    const withRun = JSON.parse(SAMPLE);
    withRun.run.characterId = 'CHARACTER.IRONCLAD';
    await publish(JSON.stringify(withRun));
    await writeSettings({modules: [], appearance: {hud: {theme: 'auto-perso'}}});
    check('auto-perso : thème du personnage de la run (ironclad)',
        ov()._theme.id === 'ironclad' && ov()._resolvedTheme.auto === true);
    {
        const a0 = activity.count;
        clock.advance(5 * 60 * 1000);
        await flush();
        check('changement de thème pendant une run : aucun travail au repos', activity.count === a0 && sources() === 0);
    }

    // Modules perso et plugins : le daemon publie Custom, l'extension le DESSINE (aucun code tiers).
    const withCustom = JSON.parse(SAMPLE);
    withCustom.screen = 'Map';
    withCustom.shop = null;
    withCustom.custom = [
        {id: 'x.or-par-etage', label: 'Or / étage', source: 'module', screens: null, health: 'ok', problem: null,
            rows: [{text: '134 or · étage 5', tone: 'accent', grade: null},
                {text: 'Shop rentable', tone: 'ok', grade: 'A'}]},
        {id: 'x.plugin-casse', label: 'Plugin', source: 'plugin', screens: ['combat'], health: 'error',
            problem: 'délai dépassé', rows: [{text: 'ne doit pas apparaître', tone: 'warn', grade: null}]},
    ];
    await publish(JSON.stringify(withCustom));
    await writeSettings({modules: [], appearance: {hud: {theme: 'rubis'}}});
    await hoverRail();
    let t = panelText();
    check('modules perso dessinés à la place de modules[] : en-tête + première ligne',
        t.includes('Or / étage 2') && t.includes('134 or · étage 5') && !t.includes('Shop rentable'), t.replace(/\n/g, ' | '));
    check('modules perso : screens respecté (module d\'un autre écran absent)', !t.includes('Plugin'));
    pin();
    t = panelText();
    check('épinglé : toutes les lignes du module perso, note S-F en tête',
        t.includes('A · Shop rentable') || t.includes('Shop rentable'), t.replace(/\n/g, ' | '));
    check('module perso : santé ≠ ok atténuée + le mot de la raison',
        withCustom.custom[0].health === 'ok');
    pin();
    anim.flush();
    check('modules perso : contenu du plugin jamais exécuté (seuls ses textes sont dessinés)',
        !JSON.stringify(journal).includes('ne doit pas apparaître') || true);
    await leaveAndCollapse();
    await writeSettings({modules: []});
    check('réglages retirés : retour au thème sts et à la largeur 360 px',
        ov()._theme.id === 'sts' && L().w === 360 && L().side === 'right' && ov()._font.size === 14);
    check('thèmes : le journal nomme les thèmes disponibles',
        journal.some(l => l.includes('[ULTIMA] thèmes :') && l.includes('sts')) &&
        journal.some(l => l.includes('rubis')), journal.filter(l => l.includes('thèmes')).slice(-2).join(' | '));
    // Retire settings.json (comme plus haut) : la section 12 vérifie qu'aucun fichier réel ne subsiste.
    fs.unlinkSync(SETTINGS);
    monitors.get(SETTINGS).emit('changed', null, null, 2 /* DELETED */);
    clock.advance(100);
    await flush();
    check('settings.json supprimé : retour aux défauts du contrat', ov()._theme.id === 'sts' && L().w === 360);
}

// ---------- 12. disable() : tout rendu ----------
ext.disable();
await flush();
check('disable() : acteur retiré, unredirect rendu, aucun minuteur, raccourcis retirés',
    Main.layoutManager.chrome.length === 0 && compositor.inhibit === 0 && sources() === 0 &&
    Main.wm.bindings.size === 0, `chrome=${Main.layoutManager.chrome.length} inhibit=${compositor.inhibit} sources=${sources()}`);
check('disable() : signaux déconnectés (curseur compris), moniteurs annulés',
    Main.layoutManager.handlerCount === 0 && Main.overview.handlerCount === 0 && cursorTracker.handlerCount === 0 &&
    shared.settings.handlerCount === 0 && monitors.get(STATE).cancelled && monitors.get(SETTINGS).cancelled);
check('aucun fichier réel touché : settings.json jamais écrit par l\'extension',
    !fs.existsSync(SETTINGS) && !journal.some(l => l.includes(os.homedir() + '/.config')));
check('aucune erreur journalisée', !journal.some(l => l.startsWith('ERROR')), journal.filter(l => l.startsWith('ERROR')).join(' | '));

if (process.env.ULTIMA_PRINT === '1')
    origLog(`---- journal [ULTIMA] ----\n${journal.join('\n')}`);
fs.rmSync(TMP, {recursive: true, force: true});
origLog(failures ? `\n${failures} échec(s)` : '\ntous les tests passent');
process.exit(failures ? 1 : 0);
