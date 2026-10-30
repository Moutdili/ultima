// Tests des réglages du HUD (contrat v1.3), du choix du niveau de verre et du planificateur des
// coûts (modules purs, sans session).
// Lancer :  gjs -m overlay/gnome/tests/test-hud.js
//           node overlay/gnome/tests/test-hud.js
import {HUD_DEFAULTS, QUALITIES, resolveHud, parseSettingsHud, effectiveQuality, detectCaps, hudKey}
    from '../ultima@mio/hud.js';
import {computePlan, HoverWatch, BLUR_RADIUS} from '../ultima@mio/plan.js';
import {parseSettings, parseSettingsAppearance, resolveAppearance, appearanceKey, APPEARANCE_DEFAULTS,
    ANCHORS, POSITION_WIDTH_RANGE} from '../ultima@mio/appearance.js';

const isNode = typeof globalThis.process?.versions?.node === 'string';
const out = isNode ? s => console.log(s) : s => globalThis.print(s);
let readText, exit;
if (isNode) {
    const fs = await import('node:fs');
    readText = rel => fs.readFileSync(new URL(rel, import.meta.url), 'utf8');
    exit = c => globalThis.process.exit(c);
} else {
    const {default: Gio} = await import('gi://Gio');
    const {default: System} = await import('system');
    const here = Gio.File.new_for_uri(import.meta.url).get_parent();
    readText = rel => new TextDecoder().decode(here.resolve_relative_path(rel).load_contents(null)[1]);
    exit = c => System.exit(c);
}

let failures = 0;
function check(name, cond, extra = '') {
    out(`${cond ? 'OK  ' : 'FAIL'} ${name}${extra ? ` — ${extra}` : ''}`);
    if (!cond)
        failures++;
}
const strip = h => Object.fromEntries(Object.keys(HUD_DEFAULTS).map(k => [k, h[k]]));
const same = (a, b) => JSON.stringify(strip(a)) === JSON.stringify(strip(b));

// ---------- 1. HudSettings v1.3 : absent / invalide -> défauts du contrat ----------
{
    check('défaut = contrat v1.3 ("verre", 0.55, 0.62, "right", false, false)',
        JSON.stringify(HUD_DEFAULTS) === JSON.stringify({quality: 'verre', heightFraction: 0.55, restOpacity: 0.62,
            side: 'right', deckScoreInCombat: false, gradesOnly: false}));
    for (const [name, src] of [['aucune source', {}], ['sources nulles', {file: null, state: null, gsettings: null}],
        ['hud tableau', {state: [1, 2]}], ['hud chaîne', {file: 'loupe'}], ['appel sans argument', undefined]]) {
        const h = resolveHud(src);
        check(`${name} -> défauts, source « défaut »`, same(h, HUD_DEFAULTS) &&
            Object.values(h.source).every(v => v === 'défaut'));
    }
    const bad = {quality: 'LOUPE+', heightFraction: 'grand', restOpacity: NaN, side: 'haut',
        deckScoreInCombat: 'oui', gradesOnly: 1};
    check('toutes valeurs invalides -> défauts', same(resolveHud({state: bad}), HUD_DEFAULTS));
    check('valeurs hors domaine du contrat -> défaut (hauteur 0 ou 1.5, opacité -0.1 ou 2, Infinity)',
        same(resolveHud({state: {heightFraction: 0, restOpacity: -0.1}}), HUD_DEFAULTS) &&
        same(resolveHud({state: {heightFraction: 1.5, restOpacity: 2}}), HUD_DEFAULTS) &&
        same(resolveHud({state: {heightFraction: Infinity, restOpacity: Infinity}}), HUD_DEFAULTS));
    const one = resolveHud({state: {quality: 'loupe', side: 42, heightFraction: 0.7}});
    check('invalide CHAMP PAR CHAMP : les champs valides sont gardés',
        one.quality === 'loupe' && one.heightFraction === 0.7 && one.side === 'right' && one.source.side === 'défaut');
    check('casse et espaces ignorés (« Eco », « LEFT »)',
        resolveHud({state: {quality: ' Eco ', side: 'LEFT'}}).quality === 'eco' && resolveHud({state: {side: 'LEFT'}}).side === 'left');
    const lo = resolveHud({state: {restOpacity: 0.1, heightFraction: 0.05}});
    const hi = resolveHud({state: {heightFraction: 1}});
    check('valeurs valides hors bornes de sûreté RAMENÉES (opacité ≥ 0.35 : leçon v1 ; hauteur 0.2-0.85)',
        lo.restOpacity === 0.35 && lo.heightFraction === 0.2 && hi.heightFraction === 0.85);
    check('noms de champs insensibles à la casse (comme le daemon)',
        resolveHud({file: {Quality: 'eco', SIDE: 'left', restopacity: 0.5}}).quality === 'eco' &&
        resolveHud({file: {SIDE: 'left'}}).side === 'left' && resolveHud({file: {restopacity: 0.5}}).restOpacity === 0.5);
    check('booléens stricts', resolveHud({state: {deckScoreInCombat: true, gradesOnly: true}}).gradesOnly === true &&
        resolveHud({state: {gradesOnly: 'true'}}).gradesOnly === false);
}

// ---------- 2. priorité des sources : settings.json > état > GSettings > défaut ----------
{
    const h = resolveHud({
        file: {quality: 'eco'},
        state: {quality: 'loupe', side: 'left', heightFraction: 'x'},
        gsettings: {quality: 'verre', heightFraction: 0.4, restOpacity: 0.8},
    });
    check('quality : fichier > état > gsettings', h.quality === 'eco' && h.source.quality === 'fichier');
    check('side : état (absent du fichier)', h.side === 'left' && h.source.side === 'état');
    check('hauteur : invalide dans l\'état -> gsettings', h.heightFraction === 0.4 && h.source.heightFraction === 'gsettings');
    check('opacité : gsettings', h.restOpacity === 0.8 && h.source.restOpacity === 'gsettings');
    check('gradesOnly : défaut', h.gradesOnly === false && h.source.gradesOnly === 'défaut');
    check('clé de réglages stable et complète', hudKey(h) ===
        'quality=eco heightFraction=0.4 restOpacity=0.8 side=left deckScoreInCombat=false gradesOnly=false');
}

// ---------- 3. lecture de ~/.config/ultima/settings.json ----------
{
    const real = JSON.parse(readText('../../../docs/ipc/state.v13.sample.json')).settings;
    const p = parseSettingsHud(JSON.stringify(real));
    check('settings.json (forme OverlaySettings v1.3) -> hud lu', p.kind === 'hud' && same(resolveHud({file: p.hud}), HUD_DEFAULTS));
    check('settings.json sans « hud » (écrit avant v1.3) -> none', parseSettingsHud('{"modules": []}').kind === 'none');
    check('settings.json « Hud » (casse du record C#) -> lu', parseSettingsHud('{"Modules": [], "Hud": {"Quality": "eco"}}').kind === 'hud' &&
        resolveHud({file: parseSettingsHud('{"Hud": {"Quality": "eco"}}').hud}).quality === 'eco');
    check('settings.json « hud »: null -> none', parseSettingsHud('{"modules": [], "hud": null}').kind === 'none');
    check('JSON tronqué -> invalide (dernier réglage valide gardé par l\'extension)', parseSettingsHud('{"hud": {"qua').kind === 'invalid');
    check('racine non objet -> invalide', parseSettingsHud('[]').kind === 'invalid' && parseSettingsHud('null').kind === 'invalid');
    check('contenu non textuel -> invalide', parseSettingsHud(undefined).kind === 'invalid');
    check('fichier énorme -> invalide', parseSettingsHud(' '.repeat(300 * 1024)).kind === 'invalid');
    const q = parseSettingsHud('{"modules":[],"hud":{"quality":"loupe","restOpacity":0.9,"side":"left","inconnu":1}}');
    const h = resolveHud({file: q.hud});
    check('champ inconnu ignoré, champs connus lus', h.quality === 'loupe' && h.restOpacity === 0.9 && h.side === 'left');
}

// ---------- 4. niveau de verre eco / verre / loupe ----------
{
    const all = {blur: true, glsl: true, clone: true};
    for (const q of QUALITIES)
        check(`demandé ${q}, toutes API -> ${q}`, effectiveQuality(q, all).level === q && effectiveQuality(q, all).missing.length === 0);
    check('inconnu -> défaut (verre)', effectiveQuality('ultra', all).level === 'verre');
    check('loupe sans Clutter.Clone -> verre (repli annoncé)',
        effectiveQuality('loupe', {blur: true, glsl: true, clone: false}).level === 'verre' &&
        effectiveQuality('loupe', {blur: true, glsl: true, clone: false}).missing.join() === 'clone');
    check('verre sans GLSLEffect -> eco', effectiveQuality('verre', {blur: true, glsl: false, clone: true}).level === 'eco');
    check('loupe sans flou -> eco', effectiveQuality('loupe', {blur: false, glsl: true, clone: true}).level === 'eco');
    check('eco : n\'exige aucune API', effectiveQuality('eco', {}).level === 'eco');
    const fn = function () {};
    check('détection des API (bouchons)', JSON.stringify(detectCaps({
        Shell: {BlurEffect: fn, BlurMode: {BACKGROUND: 1}, GLSLEffect: fn}, Clutter: {Clone: fn},
        Cogl: {SnippetHook: {FRAGMENT: 2048}}})) === JSON.stringify(all));
    check('détection : rien -> aucune API', JSON.stringify(detectCaps({})) === JSON.stringify({blur: false, glsl: false, clone: false}));
    check('chaîne complète : settings.json « loupe » -> plan loupe panneau ouvert',
        computePlan({expanded: true, quality: effectiveQuality(resolveHud({file: {quality: 'loupe'}}).quality, all).level,
            pollMs: 80}).lens);
}

// ---------- 5. plan des coûts par niveau ----------
const base = {userHidden: false, overviewHidden: false, expanded: false, pinned: false,
    blurMode: 'panel-only', unredirectMode: 'while-shown', pollMs: 80, hoverSource: 'event', quality: 'verre'};
const P = o => computePlan({...base, ...o});
{
    for (const quality of QUALITIES) {
        for (const blurMode of ['panel-only', 'always', 'never']) {
            const rest = P({quality, blurMode});
            const rail = blurMode === 'always' && quality !== 'eco';
            if (rest.sheen || rest.lens || rest.blur !== rail)
                check(`rail ${quality}/${blurMode} : ni reflet ni loupe, flou seulement si always`, false);
        }
    }
    check('rail (9 combinaisons) : ni reflet ni loupe ; flou seulement blur-mode=always', true);
    const eco = P({quality: 'eco', expanded: true, blurMode: 'always'});
    check('eco ouvert : aucun flou, aucun reflet, aucune loupe (coût ~0)', !eco.blur && !eco.sheen && !eco.lens && eco.blurRadius === 0);
    const verre = P({expanded: true});
    check('verre ouvert : flou 24 + reflet, pas de loupe', verre.blur && verre.blurRadius === BLUR_RADIUS.verre && verre.sheen && !verre.lens);
    const loupe = P({quality: 'loupe', expanded: true});
    check('loupe ouverte : flou léger 10 + reflet + loupe', loupe.blur && loupe.blurRadius === BLUR_RADIUS.loupe && loupe.sheen && loupe.lens);
    check('loupe sans inhibition de l\'unredirect : pas de clone (pas de texture à cloner)',
        !P({quality: 'loupe', expanded: true, unredirectMode: 'never'}).lens &&
        P({quality: 'loupe', expanded: true, unredirectMode: 'while-expanded'}).lens);
    check('qualité inconnue -> verre', P({quality: 'x', expanded: true}).quality === 'verre');
}

// ---------- 6. masqué : rien d'actif ----------
{
    let bad = 0;
    for (const hidden of [{userHidden: true}, {overviewHidden: true}]) {
        for (const expanded of [false, true]) {
            for (const blurMode of ['panel-only', 'always', 'never']) {
                for (const unredirectMode of ['while-shown', 'while-expanded', 'never']) {
                    for (const quality of QUALITIES) {
                        const p = P({...hidden, expanded, blurMode, unredirectMode, quality});
                        if (p.shown || p.poll || p.blur || p.sheen || p.lens || p.inhibitUnredirect || p.pollMs !== 0 || p.hoverSource)
                            bad++;
                    }
                }
            }
        }
    }
    check('masqué (108 combinaisons) : ni survol, ni flou, ni reflet, ni loupe, ni inhibition', bad === 0, `${bad}`);
    check('unredirect while-shown : inhibé rail visible', P({}).inhibitUnredirect);
    check('unredirect while-expanded : pas sur le rail, oui panneau ouvert',
        !P({unredirectMode: 'while-expanded'}).inhibitUnredirect && P({unredirectMode: 'while-expanded', expanded: true}).inhibitUnredirect);
    check('unredirect never : jamais', !P({unredirectMode: 'never', expanded: true}).inhibitUnredirect);
    check('épinglé : pas de survol', !P({pinned: true, expanded: true}).poll);
    check('poll-interval-ms 0 : pas de survol', !P({pollMs: 0}).poll);
    check('intervalle borné [20, 1000]', P({pollMs: 5}).pollMs === 20 && P({pollMs: 99999}).pollMs === 1000);
    check('hover-source inconnu -> event', P({hoverSource: 'x'}).hoverSource === 'event');
    check('mode inconnu -> défauts sûrs', P({blurMode: 'x', unredirectMode: 'y'}).inhibitUnredirect && !P({blurMode: 'x'}).blur);
}

// ---------- 7. HoverWatch : AUCUN minuteur au repos en mode event ----------
function makeClock() {
    return {now: 0, timers: new Map(), next: 1, added: 0,
        _add(ms, fn, repeat) {
            const id = this.next++;
            this.timers.set(id, {ms, fn, due: this.now + ms, repeat});
            this.added++;
            return id;
        },
        add(ms, fn) {
            return this._add(ms, fn, true);
        },
        once(ms, fn) {
            return this._add(ms, fn, false);
        },
        remove(id) {
            if (!this.timers.delete(id))
                throw new Error(`source ${id} inconnue`);
        },
        advance(ms) {
            const end = this.now + ms;
            for (;;) {
                let best = null;
                for (const [id, t] of this.timers) {
                    if (t.due <= end && (!best || t.due < best[1].due))
                        best = [id, t];
                }
                if (!best)
                    break;
                const [id, t] = best;
                this.now = t.due;
                if (t.repeat)
                    t.due += t.ms;
                else
                    this.timers.delete(id);
                t.fn();
            }
            this.now = end;
        }};
}
function makeTracker() {
    return {handlers: new Map(), next: 1, connects: 0,
        connect(fn) {
            this.connects++;
            const id = this.next++;
            this.handlers.set(id, fn);
            return id;
        },
        disconnect(id) {
            if (!this.handlers.delete(id))
                throw new Error(`connexion ${id} inconnue`);
        },
        move() {
            for (const fn of [...this.handlers.values()])
                fn();
        }};
}
{
    const clock = makeClock();
    const tracker = makeTracker();
    let ticks = 0;
    const w = new HoverWatch({timers: clock, tracker}, () => ticks++);
    w.apply(P({}));
    check('event : écoute du curseur, aucune minuterie', w.mode === 'event' && w.connected && clock.timers.size === 0);
    clock.advance(60000);
    check('event, pointeur immobile 60 s : ZÉRO tick, ZÉRO minuterie', ticks === 0 && clock.timers.size === 0 && clock.added === 0);
    // Mouvement continu à 1000 Hz pendant 1 s : lectures regroupées (≤ 1000/80).
    for (let i = 0; i < 1000; i++) {
        tracker.move();
        clock.advance(1);
    }
    clock.advance(100);
    check('event, mouvement à 1000 Hz pendant 1 s : ≤ 13 lectures', ticks >= 11 && ticks <= 13, String(ticks));
    check('event, après le mouvement : plus aucune minuterie, de nouveau à l\'écoute', clock.timers.size === 0 && w.connected);
    check('event : au plus une minuterie à la fois (signal coupé pendant l\'attente)', tracker.handlers.size === 1);
    const t0 = ticks;
    w.recheck(150);
    check('recheck : relecture ponctuelle (attente de survol sans mouvement)', clock.timers.size === 1 && !w.connected);
    w.recheck(150);
    check('recheck idempotent', clock.timers.size === 1);
    clock.advance(150);
    check('recheck : un tick, puis plus rien', ticks === t0 + 1 && clock.timers.size === 0 && w.connected);
    w.apply(P({userHidden: true}));
    tracker.move();
    clock.advance(10000);
    check('masqué : déconnecté, zéro tick', !w.active && !w.connected && tracker.handlers.size === 0 && ticks === t0 + 1);
    w.apply(P({}));
    w.apply(P({})); // ré-application idempotente
    check('réaffiché : une seule connexion', tracker.handlers.size === 1);
    w.apply(P({pinned: true, expanded: true}));
    check('épinglé : déconnecté', tracker.handlers.size === 0 && clock.timers.size === 0);
    w.apply(P({}));
    tracker.move();
    w.apply(P({hoverSource: 'poll'}));
    check('bascule event -> poll à chaud : minuterie de mouvement retirée, une minuterie périodique',
        clock.timers.size === 1 && tracker.handlers.size === 0 && w.mode === 'poll');
    w.stop();
    check('stop : rien ne reste', clock.timers.size === 0 && tracker.handlers.size === 0);
}

// ---------- 8. HoverWatch mode poll (spike S1, repli) ----------
{
    const clock = makeClock();
    let ticks = 0;
    const w = new HoverWatch({timers: clock, tracker: makeTracker()}, () => ticks++);
    w.apply(P({hoverSource: 'poll'}));
    clock.advance(1000);
    check('poll : 12 ticks par seconde à 80 ms', ticks === 12, String(ticks));
    w.recheck(10);
    check('poll : recheck sans effet (la période suffit)', clock.timers.size === 1);
    w.apply(P({hoverSource: 'poll', userHidden: true}));
    const t0 = ticks;
    clock.advance(60000);
    check('poll masqué 60 s : ZÉRO tick, aucune minuterie', ticks === t0 && clock.timers.size === 0 && !w.active);
    w.apply(P({hoverSource: 'poll'}));
    w.apply(P({hoverSource: 'poll', pollMs: 200}));
    check('poll : intervalle changé à chaud, minuterie remplacée', clock.timers.size === 1 && w.intervalMs === 200);
    const nw = new HoverWatch({timers: clock, tracker: null}, () => {});
    nw.apply(P({}));
    check('event sans curseur disponible -> repli poll', nw.mode === 'poll');
    nw.stop();
    w.stop();
}

// ---------- 9. appearance.hud (contrat v1.4) : thème, police, tons, position, railEdge ----------
{
    check('défauts du contrat docs/44 §3', APPEARANCE_DEFAULTS.theme === 'sts' &&
        APPEARANCE_DEFAULTS.font.family === null && APPEARANCE_DEFAULTS.font.size === null &&
        APPEARANCE_DEFAULTS.position.side === null && APPEARANCE_DEFAULTS.position.anchor === 'center' &&
        APPEARANCE_DEFAULTS.position.offsetX === 0 && APPEARANCE_DEFAULTS.position.offsetY === 0 &&
        APPEARANCE_DEFAULTS.position.width === 360 && APPEARANCE_DEFAULTS.railEdge === true);
    const none = resolveAppearance(null);
    check('appearance absent -> défauts, sources « défaut »',
        appearanceKey(none) === appearanceKey(resolveAppearance(undefined)) &&
        Object.values(none.source).every(v => v === 'défaut') && none.theme === 'sts');
    check('appearance non objet -> défauts', resolveAppearance('auto-perso').theme === 'sts');
    const full = resolveAppearance({
        theme: ' Auto-Perso ', tones: {accent: '#FF0000', rose: '#00FF00', text: 'rouge'},
        font: {family: ' Fira Sans ', size: 16.4},
        position: {side: 'LEFT', anchor: 'bottom', offsetX: 40.6, offsetY: -20, width: 480},
        railEdge: false,
    });
    check('thème « auto-perso » accepté (casse et espaces ignorés)', full.theme === 'auto-perso');
    check('police : famille validée, taille arrondie', full.font.family === 'Fira Sans' && full.font.size === 16);
    check('tons : seuls les tons connus et les couleurs valides sont gardés',
        full.tones.accent === '#FF0000' && !full.tones.rose && !full.tones.text);
    check('position : côté/ancrage normalisés, décalages entiers, largeur dans 280-520',
        full.position.side === 'left' && full.position.anchor === 'bottom' && full.position.offsetX === 41 &&
        full.position.offsetY === -20 && full.position.width === 480);
    check('railEdge false respecté', full.railEdge === false &&
        Object.values(full.source).every(v => v === 'fichier'));
    const bad = resolveAppearance({theme: 'Ironclad!', font: {family: 'Arial; background: red', size: 40},
        position: {side: 'haut', anchor: 'milieu', offsetX: 'loin', offsetY: NaN, width: 100}, railEdge: 'oui'});
    check('valeurs invalides -> défauts champ par champ, bornes RAMENÉES (aucune injection CSS possible)',
        bad.theme === 'sts' && bad.font.family === null && bad.font.size === 24 && bad.position.side === null &&
        bad.position.anchor === 'center' && bad.position.offsetX === 0 && bad.position.offsetY === 0 &&
        bad.position.width === 280 && bad.railEdge === true);
    const wide = resolveAppearance({position: {width: 9999}});
    check('largeur hors bornes RAMENÉE à 520', wide.position.width === 520);
    const offsets = resolveAppearance({position: {offsetX: 1e9, offsetY: -1e9}});
    check('décalages absurdes ramenés (bornage final par la géométrie)',
        Math.abs(offsets.position.offsetX) <= 4000 && Math.abs(offsets.position.offsetY) <= 4000);
    check('ancrages du contrat', ANCHORS.join(',') === 'top,center,bottom' &&
        POSITION_WIDTH_RANGE.join(',') === '280,520');
    check('noms de champs insensibles à la casse (comme le daemon)',
        resolveAppearance({THEME: 'nuit', Position: {ANCHOR: 'top'}}).theme === 'nuit' &&
        resolveAppearance({Position: {ANCHOR: 'top'}}).position.anchor === 'top');
    const text = JSON.stringify({modules: [], hud: {quality: 'eco'},
        appearance: {hud: {theme: 'ironclad', font: {family: 'Fira Sans', size: 15},
            tones: {accent: '#FF5555'}, position: {side: 'left', width: 420}, railEdge: false}}});
    const parsed = parseSettings(text);
    check('parseSettings sépare les deux blocs du même fichier',
        parsed.kind === 'ok' && parsed.hud.quality === 'eco' && parsed.appearance.theme === 'ironclad' &&
        parseSettingsAppearance(text).position.width === 420);
    check('sans bloc appearance : null (défauts)', parseSettings('{"modules": []}').appearance === null &&
        parseSettings('{"hud": {"quality": "eco"}}').appearance === null);
    check('JSON invalide -> invalide (l\'extension garde les derniers réglages valides)',
        parseSettings('{"appearance": {').kind === 'invalid' && parseSettings('[]').kind === 'invalid');
    check('appearance: null -> défauts', parseSettings('{"appearance": null}').appearance === null);
    const k1 = appearanceKey(resolveAppearance({theme: 'sts'})), k2 = appearanceKey(resolveAppearance({theme: 'nuit'}));
    check('clé de changement stable et sensible au thème', k1 === appearanceKey(resolveAppearance({theme: 'sts'})) && k1 !== k2);
}

out(failures ? `\n${failures} échec(s)` : '\ntous les tests passent');
exit(failures ? 1 : 0);
