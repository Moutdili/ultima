// Tests des modules purs de l'extension (aucune dépendance GNOME Shell) + contrôles statiques du code.
// Lancer : gjs -m overlay/gnome/tests/test-pure.js
import System from 'system';
import Gio from 'gi://Gio';
import * as Geo from '../ultima@mio/geometry.js';
import {PALETTE, TONES, MUTED_ALPHA, rgba, toneHex} from '../ultima@mio/palette.js';

let failures = 0;
function check(name, cond, extra = '') {
    print(`${cond ? 'OK  ' : 'FAIL'} ${name}${extra ? ` — ${extra}` : ''}`);
    if (!cond)
        failures++;
}

// --- géométrie : critères docs/17 §7.1 sur 1080p, 1440p, 21:9 et l'écran de Mio ---
// 1536x960 = écran réel de Mio (1920x1200 à l'échelle 1,25) en pixels LOGIQUES du stage.
// Modèle « tiroir » (§12, R6) : la plaque a toujours la taille du panneau ; repliée, seule une bande
// de RAIL_W px dépasse, COLLÉE au bord ; ouverte, à EDGE_GAP px du bord.
for (const [w, h] of [[1920, 1080], [2560, 1440], [3440, 1440], [1536, 960]]) {
    const mon = {x: 0, y: 0, width: w, height: h};
    const L = Geo.computeLayout(mon);
    const c = Geo.coverage(L, mon);
    check(`${w}x${h} replié < 1 %`, c.collapsed < 0.01, `${(c.collapsed * 100).toFixed(2)} %`);
    check(`${w}x${h} déployé < 15 %`, c.expanded < 0.15, `${(c.expanded * 100).toFixed(2)} %`);
    check(`${w}x${h} ancré à droite (rail au bord, panneau à ${Geo.EDGE_GAP} px)`,
        L.side === 'right' && L.railX + Geo.RAIL_W === w && L.panelX + Geo.PANEL_W === w - Geo.EDGE_GAP);
    check(`${w}x${h} repliée : la plaque translatée laisse exactement le rail visible`,
        L.rootX + L.plateX + L.collapsedDx === L.railX && L.rootX + L.rootW === w);
    check(`${w}x${h} ouverte : translation 0 = panneau`, L.rootX + L.plateX === L.panelX);
}
{
    const mon = {x: 0, y: 0, width: 1536, height: 960};
    const L = Geo.computeLayout(mon, 0.22, 0.55, 'left');
    check('côté gauche : rail collé au bord gauche, panneau à EDGE_GAP', L.side === 'left' && L.railX === 0 &&
        L.panelX === Geo.EDGE_GAP && L.rootX === 0 && L.rootX + L.plateX + L.collapsedDx + Geo.PANEL_W === Geo.RAIL_W);
    check('côté gauche : survol au bord extrême gauche', Geo.contains(Geo.hoverZone(L, false), 0, L.y + 10));
    check('côté gauche : pas de survol à droite', !Geo.contains(Geo.hoverZone(L, false), 1530, L.y + 10));
    check('côté inconnu -> droite', Geo.computeLayout(mon, 0.22, 0.55, 'haut').side === 'right');
    check('défaut contrat 0.55 sur 1536x960 : 528 px', Geo.computeLayout(mon).h === 528);
    check('second écran (x décalé) : rail au bord du moniteur',
        Geo.computeLayout({x: 1920, y: 0, width: 1536, height: 960}).railX === 1920 + 1536 - Geo.RAIL_W);
}
check('rail ≥ 14 px (leçon v1)', Geo.RAIL_W >= 14);
check('panneau = 260 px', Geo.PANEL_W === 260);

const L = Geo.computeLayout({x: 0, y: 0, width: 1920, height: 1080});
check('survol sur le rail', Geo.contains(Geo.hoverZone(L, false), 1910, L.y + 10));
check('survol au bord extrême droit', Geo.contains(Geo.hoverZone(L, false), 1919, L.y + 10));
check('pas de survol au centre', !Geo.contains(Geo.hoverZone(L, false), 960, 540));
check('pas de survol sous le rail', !Geo.contains(Geo.hoverZone(L, false), 1910, L.y + L.h + 1));
check('panneau déployé garde le survol', Geo.contains(Geo.hoverZone(L, true), 1700, L.y + 10));
check('fractions aberrantes bornées', Geo.computeLayout({x: 0, y: 0, width: 100, height: 100}, NaN, 99).h <= 100);

// --- palette : uniquement StsColors + charte ---
// game/csharp/MegaCrit/Sts2/Core/Helpers/StsColors.cs : cream, gold, red, green, blue, orange,
// purple, aqua, lightGray (0.75), gray (0.5) ; charte docs/17 §3 : teinte #192930, ombre #000000.
const ALLOWED = ['#FFF6E2', '#EFC851', '#FF5555', '#7FFF00', '#87CEEB', '#FFA518', '#EE82EE', '#2AEBBE',
    '#BFBFBF', '#808080', '#192930', '#000000'];
check('palette ⊂ StsColors + charte', Object.values(PALETTE).every(h => ALLOWED.includes(h)),
    Object.values(PALETTE).filter(h => !ALLOWED.includes(h)).join(' '));
check('rgba #192930 @0.72', rgba('#192930', 0.72) === 'rgba(25, 41, 48, 0.72)');
check('ton inconnu -> cream', toneHex('#123456') === PALETTE.cream);
check('tons connus', TONES.every(t => toneHex(t) === PALETTE[t]));
check('texte secondaire : crème 60 % (§11)', MUTED_ALPHA === 0.6);

// --- contrôles statiques du code de l'extension ---
const dir = Gio.File.new_for_uri(import.meta.url).get_parent().get_parent().get_child('ultima@mio');
const read = f => new TextDecoder().decode(dir.get_child(f).load_contents(null)[1]);
const MODULES = [];
{
    const en = dir.enumerate_children('standard::name', Gio.FileQueryInfoFlags.NONE, null);
    for (let info; (info = en.next_file(null));) {
        if (info.get_name().endsWith('.js'))
            MODULES.push(info.get_name());
    }
    MODULES.sort();
}
check('modules trouvés', MODULES.length >= 9, MODULES.join(' '));
const CSS_NAMED = /\b(?:color|background(?:-color)?|border(?:-color)?)\s*:\s*(?!rgba\(\$\{|\$\{)([a-z]+)\b/gi;
for (const f of MODULES) {
    const src = read(f);
    const code = src.replace(/^\s*\/\/.*$/gm, ''); // les commentaires peuvent citer la charte
    const hexes = code.match(/#[0-9a-fA-F]{6}\b|#[0-9a-fA-F]{3}\b/g) ?? [];
    const badHex = f === 'palette.js' ? hexes.filter(h => !ALLOWED.includes(h.toUpperCase())) : hexes;
    check(`${f} : aucune couleur hex ${f === 'palette.js' ? 'hors charte' : '(seul palette.js en définit)'}`,
        badHex.length === 0, badHex.join(' '));
    const rgbLit = code.match(/rgba?\(\s*\d/g) ?? [];
    check(`${f} : aucun rgb()/rgba() littéral (toujours rgba(PALETTE.x, a))`, rgbLit.length === 0, rgbLit.join(' '));
    const named = [...code.matchAll(CSS_NAMED)].map(m => m[1]).filter(n => !['transparent', 'inherit'].includes(n.toLowerCase()));
    check(`${f} : aucune couleur CSS nommée`, named.length === 0, named.join(' '));
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
    // effects.js importe gi://Cogl (absent hors session) : contrôle sur le texte des déclarations GLSL.
    const uniforms = read('effects.js').match(/^uniform\s+\w+\s+(\w+);/gm) ?? [];
    check('effets : uniformes fixes, aucune liée au temps', uniforms.length >= 8 &&
        !uniforms.some(u => /time|frame|clock/i.test(u)), uniforms.join(' | '));
    check('extension : jamais de touche F', !/'F\d+'|<F\d+>|\bF\d{1,2}\b'/.test(ext));
}
{
    const schema = new TextDecoder().decode(dir.get_child('schemas').get_child('org.gnome.shell.extensions.ultima.gschema.xml')
        .load_contents(null)[1]);
    check('schéma : raccourcis Super+Maj+I / Super+Maj+O, aucune touche F',
        schema.includes("['<Super><Shift>i']") && schema.includes("['<Super><Shift>o']") && !/'[^']*F\d+'/.test(schema));
    for (const k of ['quality', 'height-fraction', 'rest-opacity', 'side', 'hover-source'])
        check(`schéma : clé ${k}`, schema.includes(`<key name="${k}"`));
    const pkg = new TextDecoder().decode(dir.get_parent().get_child('install-dev.sh').load_contents(null)[1]);
    const list = /FILES=\(([^)]*)\)/.exec(pkg)?.[1].split(/\s+/).filter(Boolean) ?? [];
    check('install-dev.sh : tous les modules sont empaquetés', MODULES.every(m => list.includes(m)),
        MODULES.filter(m => !list.includes(m)).join(' '));
}

print(failures ? `\n${failures} échec(s)` : '\ntous les tests passent');
System.exit(failures ? 1 : 0);
