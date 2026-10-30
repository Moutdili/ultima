// Tests des modules purs de l'extension (aucune dépendance GNOME Shell) + contrôles statiques du code.
// Lancer : gjs -m overlay/gnome/tests/test-pure.js
import System from 'system';
import Gio from 'gi://Gio';
import * as Geo from '../ultima@mio/geometry.js';
import {parseTheme, toneColor, TONE_KEYS, parseColor} from '../ultima@mio/theme.js';

let failures = 0;
function check(name, cond, extra = '') {
    print(`${cond ? 'OK  ' : 'FAIL'} ${name}${extra ? ` — ${extra}` : ''}`);
    if (!cond)
        failures++;
}

// --- géométrie (docs/45 T4 point 3) : 5 résolutions, 3 ancrages, décalages extrêmes ---
// 1920x1200 à l'échelle 125 % = 1536x960 en pixels LOGIQUES du stage (écran réel de Mio) ;
// 1920x1080, 2560x1440, 3440x1440 (21:9) et 1536x960 complètent la série.
const SCREENS = [
    ['1920x1200 @125 % -> 1536x960', {x: 0, y: 0, width: 1536, height: 960, scale: 1.25}],
    ['1920x1080', {x: 0, y: 0, width: 1920, height: 1080, scale: 1}],
    ['2560x1440', {x: 0, y: 0, width: 2560, height: 1440, scale: 1}],
    ['3440x1440', {x: 0, y: 0, width: 3440, height: 1440, scale: 1}],
    ['1536x960', {x: 0, y: 0, width: 1536, height: 960, scale: 1}],
];
const ANCHORS = ['top', 'center', 'bottom'];
const OFFSETS = [[0, 0], [-9999, -9999], [9999, 9999], [200, -300], [-150, 250]];

for (const [name, mon] of SCREENS) {
    const L = Geo.computeLayout(mon);
    const c = Geo.coverage(L, mon);
    const area = mon.width * mon.height;
    check(`${name} : replié < 1 %`, c.collapsed < 0.01, `${(c.collapsed * 100).toFixed(2)} %`);
    check(`${name} : déployé < 15 %`, c.expanded < 0.15, `${(c.expanded * 100).toFixed(2)} %`);
    check(`${name} : rail collé au bord droit, panneau à ${Geo.EDGE_GAP} px du bord`,
        L.side === 'right' && L.railX + Geo.RAIL_W === mon.width &&
        L.panelX + L.w === mon.width - Geo.EDGE_GAP);
    check(`${name} : replié, la plaque translatée laisse exactement le rail visible`,
        L.rootX + L.plateX + L.collapsedDx === L.railX && L.rootX + L.rootW === mon.width);
    check(`${name} : ouvert, translation 0 = panneau`, L.rootX + L.plateX === L.panelX);
    check(`${name} : largeur par défaut 360 px (contrat 280-520)`,
        L.w === Geo.DEFAULT_WIDTH && L.w >= Geo.MIN_WIDTH && L.w <= Geo.MAX_WIDTH, `${L.w}`);
    check(`${name} : hauteur dans les bornes du contrat v1.3 (0.2-0.85 de l'écran)`,
        L.h >= Math.floor(mon.height * 0.2) - 1 && L.h <= Math.ceil(mon.height * 0.85) + 1, `${L.h}`);
    check(`${name} : panneau entièrement dans l'écran`,
        L.panelX >= mon.x && L.panelX + L.w <= mon.x + mon.width &&
        L.y >= mon.y && L.y + L.h <= mon.y + mon.height);
    check(`${name} : ne couvre jamais « Fin du tour » (bas droite)`,
        !Geo.intersects(Geo.panelRect(L), Geo.endTurnZone(mon)));
}

// Toutes les combinaisons ancrage × décalage : jamais hors écran, jamais sur « Fin du tour ».
{
    let out = 0, endTurn = 0, total = 0;
    for (const [, mon] of SCREENS) {
        for (const anchor of ANCHORS) {
            for (const [offsetX, offsetY] of OFFSETS) {
                for (const width of [280, 360, 520]) {
                    for (const heightFraction of [0.2, 0.55, 0.85]) {
                        total++;
                        const L = Geo.computeLayout(mon, {anchor, offsetX, offsetY, width, heightFraction});
                        const r = Geo.panelRect(L);
                        if (r.x < mon.x || r.x + r.width > mon.x + mon.width ||
                            r.y < mon.y || r.y + r.height > mon.y + mon.height)
                            out++;
                        if (Geo.intersects(r, Geo.endTurnZone(mon)))
                            endTurn++;
                        if (L.rootX + L.plateX + L.collapsedDx !== L.railX)
                            out++;
                    }
                }
            }
        }
    }
    check(`géométrie ${total} combinaisons (ancrage, décalage, largeur, hauteur) : jamais hors écran`,
        out === 0, `${out}`);
    check(`géométrie ${total} combinaisons : jamais sur « Fin du tour »`, endTurn === 0, `${endTurn}`);
}

// Ancrages : l'ancrage haut garde la position v1.3 (top-fraction), centre/bas restent dans la zone sûre.
{
    const mon = {x: 0, y: 0, width: 1536, height: 960};
    const safe = Geo.safeArea(mon, 'right');
    const top = Geo.computeLayout(mon, {anchor: 'top', topFraction: 0.22});
    check('ancrage haut : 22 % du haut (position v1.3 conservée)', top.y === Math.round(960 * 0.22), `${top.y}`);
    const center = Geo.computeLayout(mon, {anchor: 'center', heightFraction: 0.4});
    const bottom = Geo.computeLayout(mon, {anchor: 'bottom', heightFraction: 0.4});
    check('ancrage bas : le bas du panneau est au bas de la zone sûre',
        bottom.y + bottom.h === safe.y + safe.height, `${bottom.y + bottom.h}`);
    check('ancrage centre : panneau centré dans la zone sûre',
        Math.abs((center.y - safe.y) - (safe.y + safe.height - center.y - center.h)) <= 1);
    check('ordre des ancrages : haut < centre < bas', top.y < center.y && center.y < bottom.y);
    check('décalage vertical borné : le panneau reste dans la zone sûre',
        Geo.computeLayout(mon, {anchor: 'top', offsetY: 9999}).y + Geo.computeLayout(mon, {anchor: 'top', offsetY: 9999}).h <=
        safe.y + safe.height && Geo.computeLayout(mon, {anchor: 'top', offsetY: -9999}).y === safe.y);
    check('ancrage inconnu -> centre',
        Geo.computeLayout(mon, {anchor: 'haut', heightFraction: 0.4}).y === center.y);
}

// Largeurs et décalages hors bornes : ramenés, jamais rejetés.
{
    const mon = {x: 0, y: 0, width: 1920, height: 1080};
    check('largeur < 280 -> 280', Geo.computeLayout(mon, {width: 100}).w === 280);
    check('largeur > 520 -> 520', Geo.computeLayout(mon, {width: 9999}).w === 520);
    check('largeur absente -> 360', Geo.computeLayout(mon).w === 360);
    const huge = Geo.computeLayout(mon, {offsetX: 99999, offsetY: -99999});
    check('décalages absurdes bornés : panneau encore dans l\'écran et rail au bord',
        huge.panelX >= 0 && huge.panelX + huge.w <= mon.width && huge.rootX + huge.plateX + huge.collapsedDx === huge.railX);
    const inner = Geo.computeLayout(mon, {offsetX: 120});
    check('décalage horizontal : le panneau s\'éloigne du bord, le rail reste collé au bord',
        inner.panelX === Geo.computeLayout(mon).panelX - 120 && inner.railX === mon.width - Geo.RAIL_W);
    check('décalage : la plaque repliée est translatée plus loin (tiroir intact)',
        inner.collapsedDx > Geo.computeLayout(mon).collapsedDx);
    const left = Geo.computeLayout(mon, {side: 'left'});
    check('côté gauche : rail collé au bord gauche, panneau à EDGE_GAP',
        left.railX === 0 && left.panelX === Geo.EDGE_GAP && left.rootX === 0 &&
        left.rootX + left.plateX + left.collapsedDx === 0);
    check('côté gauche : la marge d\'ombre est à l\'intérieur (à droite de la plaque)',
        left.rootW === left.panelX - left.monitorLeft + left.w + Geo.SHADOW_PAD);
    check('côté gauche : jamais sur la pioche (bas gauche)',
        !Geo.intersects(Geo.panelRect(left), Geo.drawPileZone(mon)));
    check('côté inconnu -> droite', Geo.computeLayout(mon, {side: 'haut'}).side === 'right');
    check('second écran (x décalé) : rail au bord du moniteur',
        Geo.computeLayout({x: 1920, y: 0, width: 1536, height: 960}).railX === 1920 + 1536 - Geo.RAIL_W);
    check('moniteur minuscule : aucune exception, rien hors écran',
        Geo.computeLayout({x: 0, y: 0, width: 100, height: 100}, {heightFraction: 99}).h <= 100);
    check('moniteur absent : aucune exception', !!Geo.computeLayout({x: 0, y: 0, width: 800, height: 600}, undefined));
    check('rail ≥ 14 px (leçon v1)', Geo.RAIL_W >= 14);
    check('zone « Fin du tour » : coin bas droite du moniteur',
        Geo.endTurnZone(mon).x + Geo.endTurnZone(mon).width === mon.width &&
        Geo.endTurnZone(mon).y + Geo.endTurnZone(mon).height === mon.height);
}

// Survol : la bande visible jusqu'au bord, plus une tolérance côté intérieur.
{
    const mon = {x: 0, y: 0, width: 1920, height: 1080};
    const L = Geo.computeLayout(mon);
    check('survol sur le rail', Geo.contains(Geo.hoverZone(L, false), 1910, L.y + 10));
    check('survol au bord extrême droit', Geo.contains(Geo.hoverZone(L, false), 1919, L.y + 10));
    check('pas de survol au centre', !Geo.contains(Geo.hoverZone(L, false), 960, 540));
    check('pas de survol sous le rail', !Geo.contains(Geo.hoverZone(L, false), 1910, L.y + L.h + 1));
    check('panneau déployé garde le survol', Geo.contains(Geo.hoverZone(L, true), 1700, L.y + 10));
    const shifted = Geo.computeLayout(mon, {offsetX: 120});
    check('décalé : le survol suit le panneau ouvert ET le rail collé au bord',
        Geo.contains(Geo.hoverZone(shifted, true), shifted.panelX + 10, shifted.y + 10) &&
        Geo.contains(Geo.hoverZone(shifted, false), mon.width - 2, shifted.y + 10));
    const left = Geo.computeLayout(mon, {side: 'left'});
    check('côté gauche : survol au bord extrême gauche', Geo.contains(Geo.hoverZone(left, false), 0, left.y + 10));
    check('côté gauche : pas de survol à droite', !Geo.contains(Geo.hoverZone(left, false), 1910, left.y + 10));
}

// --- thèmes : AUCUNE couleur hors des fichiers de thème ---
// Le HUD ne définit plus de palette en JS : la seule source est `themes/*.json` (dont
// ultima@mio/themes/sts.json, livré avec l'extension et identique à themes/sts.json du dépôt).
const dir = Gio.File.new_for_uri(import.meta.url).get_parent().get_parent().get_child('ultima@mio');
const read = f => new TextDecoder().decode(dir.get_child(f).load_contents(null)[1]);
const repoThemes = Gio.File.new_for_uri(import.meta.url).get_parent().get_parent().get_parent().get_parent().get_child('themes');
const readTheme = name => new TextDecoder().decode(repoThemes.get_child(name).load_contents(null)[1]);
{
    const shipped = read('themes/sts.json');
    const repo = readTheme('sts.json');
    check('thème sts livré avec l\'extension = themes/sts.json du dépôt (aucune dérive)', shipped === repo);
    const parsed = parseTheme(shipped, {fileId: 'sts'});
    check('thème sts livré : valide, api 1, 17 tons, aucun problème',
        parsed.ok && parsed.theme.api === 1 && Object.keys(parsed.theme.tones).length === 17 &&
        parsed.problems.length === 0, parsed.problems.join(' | '));
    check('les 17 tons du contrat sont tous définis par sts', TONE_KEYS.every(t => toneColor(parsed.theme, t).hex));
    check('chaque couleur du thème est un hex valide',
        Object.values(JSON.parse(shipped).tones).every(v => parseColor(v)) &&
        parseColor(JSON.parse(shipped).glass.tint) !== null);
    let themes = [];
    const en = repoThemes.enumerate_children('standard::name', Gio.FileQueryInfoFlags.NONE, null);
    for (let info; (info = en.next_file(null));) {
        if (info.get_name().endsWith('.json'))
            themes.push(info.get_name());
    }
    en.close(null);
    check('themes/ : au moins sts.json, tous relisibles',
        themes.includes('sts.json') && themes.every(n => parseTheme(readTheme(n), {fileId: n.slice(0, -5)}).ok),
        themes.join(' '));
}

// --- contrôles statiques du code de l'extension ---
const MODULES = [];
{
    const en = dir.enumerate_children('standard::name', Gio.FileQueryInfoFlags.NONE, null);
    for (let info; (info = en.next_file(null));) {
        if (info.get_name().endsWith('.js'))
            MODULES.push(info.get_name());
    }
    en.close(null);
    MODULES.sort();
}
check('modules trouvés', MODULES.length >= 10, MODULES.join(' '));
const CSS_NAMED = /\b(?:color|background(?:-color)?|border(?:-color)?)\s*:\s*(?!rgba\(|\$)([a-z]+)\b/gi;
for (const f of MODULES) {
    const src = read(f);
    const code = src.replace(/^\s*\/\/.*$/gm, ''); // les commentaires peuvent citer la charte
    const hexes = code.match(/#[0-9a-fA-F]{6}\b|#[0-9a-fA-F]{3}\b/g) ?? [];
    check(`${f} : aucun hexadécimal (les couleurs viennent des fichiers de thème)`, hexes.length === 0, hexes.join(' '));
    const rgbLit = code.match(/rgba?\(\s*\d/g) ?? [];
    check(`${f} : aucun rgb()/rgba() littéral (toujours depuis le thème)`, rgbLit.length === 0, rgbLit.join(' '));
    const named = [...code.matchAll(CSS_NAMED)].map(m => m[1]).filter(n => !['transparent', 'inherit', 'none'].includes(n.toLowerCase()));
    check(`${f} : aucune couleur CSS nommée`, named.length === 0, named.join(' '));
    // Aucun code tiers exécuté dans l'extension (modules perso et plugins : dessin seulement).
    check(`${f} : aucun code tiers évalué (eval, Function, import dynamique, sous-processus)`,
        !/\beval\s*\(|new\s+Function\s*\(|\bimport\s*\(|GLib\.spawn|Gio\.Subprocess|util\.spawn/.test(code));
}

// --- §12 : le rail est une simple bande de verre (aucune pastille, aucun bouton, aucun texte) ---
{
    const ext = read('extension.js');
    check('extension : aucune pastille (dot) ni bouton', !/_dot|dotActors|St\.Button|St\.Icon|Gio\.ThemedIcon/.test(ext));
    check('extension : aucun acteur réactif (click-through)', !/reactive:\s*true/.test(ext) && !/track_hover:\s*true/.test(ext));
    check('extension : aucun texte hors du conteneur de contenu (rail sans texte)',
        (ext.match(/new St\.Label\(/g) ?? []).length === 3 && /_content\.add_child\(probe\)/.test(ext));
    // §6, §12 : aucune boucle, aucun minuteur par image, aucune animation répétée.
    check('extension : aucune boucle d\'animation (repeat, Timeline, idle, later, after-paint)',
        !/repeat_count|repeatCount|autoReverse|Clutter\.Timeline|idle_add|get_laters|after-paint|BEFORE_REDRAW|queue_redraw/.test(ext));
    check('extension : jamais de touche F', !/'F\d+'|<F\d+>|\bF\d{1,2}\b/.test(ext));
    check('extension : le contenu de modules perso est dessiné, jamais chargé',
        !/Gio\.File\.new_for_path\([^)]*plugin|plugin\.json|\.py\b/.test(ext));
    const uniforms = read('effects.js').match(/^uniform\s+\w+\s+(\w+);/gm) ?? [];
    check('effets : uniformes fixes, aucune liée au temps', uniforms.length >= 8 &&
        !uniforms.some(u => /time|frame|clock/i.test(u)), uniforms.join(' | '));
    const pkg = new TextDecoder().decode(dir.get_parent().get_child('install-dev.sh').load_contents(null)[1]);
    const list = /FILES=\(([^)]*)\)/.exec(pkg)?.[1].split(/\s+/).filter(Boolean) ?? [];
    check('install-dev.sh : tous les modules sont empaquetés', MODULES.every(m => list.includes(m)),
        MODULES.filter(m => !list.includes(m)).join(' '));
    check('install-dev.sh : le thème de repli est empaqueté', list.includes('themes/sts.json'));
}

// --- schéma GSettings : clés et raccourcis inchangés (aucune clé de thème : appearance est un fichier) ---
{
    const schema = new TextDecoder().decode(dir.get_child('schemas').get_child('org.gnome.shell.extensions.ultima.gschema.xml')
        .load_contents(null)[1]);
    check('schéma : raccourcis Super+Maj+I / Super+Maj+O, aucune touche F',
        schema.includes("['<Super><Shift>i']") && schema.includes("['<Super><Shift>o']") && !/'[^']*F\d+'/.test(schema));
    for (const k of ['quality', 'height-fraction', 'rest-opacity', 'side', 'hover-source'])
        check(`schéma : clé ${k}`, schema.includes(`<key name="${k}"`));
}

print(failures ? `\n${failures} échec(s)` : '\ntous les tests passent');
System.exit(failures ? 1 : 0);
