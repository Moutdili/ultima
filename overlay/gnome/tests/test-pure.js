// Tests des modules purs de l'extension (aucune dépendance GNOME Shell).
// Lancer : gjs -m overlay/gnome/tests/test-pure.js
import System from 'system';
import * as Geo from '../ultima@mio/geometry.js';
import {PALETTE, TONES, rgba, toneHex} from '../ultima@mio/palette.js';
import {parseState} from '../ultima@mio/state.js';
import Gio from 'gi://Gio';

let failures = 0;
function check(name, cond, extra = '') {
    print(`${cond ? 'OK  ' : 'FAIL'} ${name}${extra ? ` — ${extra}` : ''}`);
    if (!cond)
        failures++;
}

// --- géométrie : critères docs/17 §7.1 sur 1080p, 1440p, 21:9 ---
// 1536x960 = écran réel de Mio (1920x1200 à l'échelle 1,25) en pixels LOGIQUES du stage.
for (const [w, h] of [[1920, 1080], [2560, 1440], [3440, 1440], [1536, 960]]) {
    const mon = {x: 0, y: 0, width: w, height: h};
    const L = Geo.computeLayout(mon);
    const c = Geo.coverage(L, mon);
    check(`${w}x${h} replié < 1 %`, c.collapsed < 0.01, `${(c.collapsed * 100).toFixed(2)} %`);
    check(`${w}x${h} déployé < 15 %`, c.expanded < 0.15, `${(c.expanded * 100).toFixed(2)} %`);
    check(`${w}x${h} ancré à droite`, L.right === w - Geo.EDGE_GAP && L.railX === w - Geo.EDGE_GAP - 14);
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

// --- palette : uniquement StsColors + teinte charte ---
const ALLOWED = ['#FFF6E2', '#EFC851', '#FF5555', '#7FFF00', '#87CEEB', '#FFA518', '#EE82EE', '#2AEBBE', '#BFBFBF', '#192930', '#000000'];
check('palette ⊂ StsColors + charte', Object.values(PALETTE).every(h => ALLOWED.includes(h)));
check('rgba #192930 @0.72', rgba('#192930', 0.72) === 'rgba(25, 41, 48, 0.72)');
check('ton inconnu -> cream', toneHex('#123456') === PALETTE.cream);
check('tons connus', TONES.every(t => toneHex(t) === PALETTE[t]));

// grep : aucun hex hors palette.js dans le code de l'extension
const dir = Gio.File.new_for_uri(import.meta.url).get_parent().get_parent().get_child('ultima@mio');
for (const f of ['extension.js', 'geometry.js', 'state.js']) {
    const [, bytes] = dir.get_child(f).load_contents(null);
    const hits = new TextDecoder().decode(bytes).match(/#[0-9a-fA-F]{6}\b/g) ?? [];
    const bad = hits.filter(h => !ALLOWED.includes(h.toUpperCase()));
    check(`${f} : aucun hex hors charte`, bad.length === 0, bad.join(' '));
}

// --- état : parsing défensif ---
const [, sampleBytes] = dir.get_child('sample-state.json').load_contents(null);
const sample = parseState(new TextDecoder().decode(sampleBytes));
check('sample-state.json valide', sample.ok, sample.error ?? '');
check('sample : 5 lignes de deck', sample.ok && sample.state.lines.length === 5);
check('sample : 3 pastilles', sample.ok && sample.state.rail.length === 3);
check('JSON cassé rejeté', !parseState('{').ok);
check('version inconnue rejetée', !parseState('{"v":2,"lines":[]}').ok);
const weird = parseState('{"v":1,"lines":[{"label":"x","value":"1","tone":"#ff00ff"}]}');
check('ton brut neutralisé', weird.ok && weird.state.lines[0].tone === 'cream');
check('pastilles complétées', weird.ok && weird.state.rail.length === 3);

print(failures ? `\n${failures} échec(s)` : '\ntous les tests passent');
System.exit(failures ? 1 : 0);
