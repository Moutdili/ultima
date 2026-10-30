// Tests du résolveur de thème (modules purs, sans session GNOME).
// Lancer :  gjs -m overlay/gnome/tests/test-theme.js
//           node overlay/gnome/tests/test-theme.js
//
// Couvre docs/45 T4 point 1 : appearance.hud.theme -> extends -> appearance.hud.tones, teinte du
// verre depuis `glass`, auto-perso, thème cassé -> repli `sts` + problème journalisable, JAMAIS
// d'exception ; aucun hexadécimal hors des fichiers de thème.
import * as T from '../ultima@mio/theme.js';

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

const STS_TEXT = readText('../../../themes/sts.json');
const sts = T.parseTheme(STS_TEXT, {fileId: 'sts'});
const lib = T.buildLibrary([{path: 'livrés', files: [{name: 'sts.json', text: STS_TEXT}]}]);

// ---------- 1. couleurs : lecture, alpha, conversion ----------
{
    check('couleur #RRGGBB -> alpha 1', JSON.stringify(T.parseColor('#FFF6E2')) ===
        JSON.stringify({hex: '#FFF6E2', alpha: 1}));
    const c = T.parseColor('#FFF6E299');
    check('#RRGGBBAA -> alpha = AA/255 (0,6 pour 99)', c && c.hex === '#FFF6E2' && Math.abs(c.alpha - 0.6) < 0.01,
        `${c?.alpha}`);
    check('couleurs invalides -> null (jamais d\'exception)',
        T.parseColor('cream') === null && T.parseColor('#12345') === null && T.parseColor(null) === null &&
        T.parseColor('#GGGGGG') === null && T.parseColor(42) === null);
    check('rgba() pour St', T.rgba('#192930', 0.68) === 'rgba(25, 41, 48, 0.68)');
    check('rgba() alpha borné [0,1]', T.rgba('#000000', 2) === 'rgba(0, 0, 0, 1)' &&
        T.rgba('#000000', -1) === 'rgba(0, 0, 0, 0)');
    check('rgba() d\'une couleur absente -> null (l\'appelant n\'écrit pas la propriété)',
        T.rgba(null, 1) === null && T.rgba(undefined, 1) === null);
}

// ---------- 2. tons : contrat v1.4 + pont v1.3 ----------
{
    check('tons du contrat reconnus', T.TONE_KEYS.every(t => T.normalizeTone(t) === t) && T.TONE_KEYS.length === 17);
    check('alias v1.3 -> tons sémantiques', T.normalizeTone('cream') === 'text' && T.normalizeTone('gold') === 'accent' &&
        T.normalizeTone('red') === 'danger' && T.normalizeTone('green') === 'ok' && T.normalizeTone('blue') === 'info' &&
        T.normalizeTone('orange') === 'warn' && T.normalizeTone('purple') === 'curse' &&
        T.normalizeTone('aqua') === 'enchant' && T.normalizeTone('gray') === 'muted' &&
        T.normalizeTone('lightGray') === 'muted');
    check('ton inconnu -> null', T.normalizeTone('#FF0000') === null && T.normalizeTone('rose') === null &&
        T.normalizeTone(3) === null);
    const theme = sts.theme;
    check('tous les tons du thème sts sont résolus', T.TONE_KEYS.every(t => T.toneColor(theme, t).hex),
        T.TONE_KEYS.filter(t => !T.toneColor(theme, t).hex).join(' '));
    check('muted porte son alpha (#FFF6E299 -> 60 %)',
        Math.abs(T.toneColor(theme, 'muted').alpha - 0.6) < 0.01);
    check('grade.A = vert du jeu, via l\'alias « green »',
        T.toneColor(theme, 'grade.A').hex === T.toneColor(theme, 'green').hex);
    check('thème absent -> aucune couleur (null), aucune exception',
        T.toneColor(null, 'text').hex === null && T.toneColor(undefined, 'accent').hex === null);
    check('toneCss : rgba du thème', T.toneCss(theme, 'accent', 0.5) === 'rgba(239, 200, 81, 0.5)');
}

// ---------- 3. lecture d'un fichier de thème ----------
{
    check('thèmes/sts.json : lu, api 1, 17 tons', sts.ok && sts.theme.api === 1 &&
        Object.keys(sts.theme.tones).length === 17 && sts.problems.length === 0, sts.problems.join(' | '));
    check('sts.json identique à celui livré avec l\'extension (aucune dérive)',
        readText('../ultima@mio/themes/sts.json') === STS_TEXT);
    check('JSON cassé -> thème inutilisable, problème expliqué', !T.parseTheme('{"id": "x"').ok &&
        T.parseTheme('{"id": "x"').problems[0].includes('JSON invalide'));
    check('api inconnue -> refusé en entier (jamais deviné)',
        !T.parseTheme(JSON.stringify({id: 'x', label: 'X', api: 2, tones: {text: '#FFFFFF'}})).ok);
    check('racine non objet -> refusé', !T.parseTheme('[]').ok && !T.parseTheme('null').ok && !T.parseTheme(12).ok);
    check('id invalide -> id du fichier utilisé',
        T.parseTheme(JSON.stringify({label: 'X', api: 1}), {fileId: 'rubis'}).theme?.id === 'rubis');
    const partial = T.parseTheme(JSON.stringify({id: 'x', label: 'X', api: 1,
        tones: {text: '#FFFFFF', rose: '#FF0000', accent: 'rouge'},
        glass: {tint: '#801212', tintOpacity: 3},
        font: {family: 'Fira Sans; color: red', size: 40}}));
    check('champ invalide ignoré champ par champ (ton inconnu, couleur, tintOpacity, police)',
        partial.ok && partial.theme.tones.text?.hex === '#FFFFFF' && !partial.theme.tones.rose &&
        !partial.theme.tones.accent && partial.theme.glass.tint?.hex === '#801212' &&
        partial.theme.glass.tintOpacity === undefined && partial.theme.font.family === undefined &&
        partial.theme.font.size === undefined, partial.problems.join(' | '));
    check('problèmes lisibles (fichier + champ)', partial.problems.length >= 4 &&
        partial.problems.every(p => typeof p === 'string' && p.length > 10));
    check('fichier énorme refusé', !T.parseTheme(' '.repeat(T.MAX_THEME_BYTES + 1)).ok);
    check('family de police saine acceptée', T.parseTheme(JSON.stringify({id: 'x', api: 1,
        font: {family: 'Fira Sans', size: 16}})).theme.font.family === 'Fira Sans');
}

// ---------- 4. bibliothèque : priorité des dossiers ----------
{
    const user = JSON.stringify({id: 'sts', label: 'Perso', api: 1, tones: {text: '#000001'}});
    const data = JSON.stringify({id: 'arda', label: 'Ardoise', api: 1, tones: {text: '#101010'}});
    const l = T.buildLibrary([
        {path: 'config', files: [{name: 'sts.json', text: user}, {name: 'note.txt', text: 'x'}]},
        {path: 'data', files: [{name: 'arda.json', text: data}, {name: 'casse.json', text: '{'}]},
    ]);
    check('premier dossier prioritaire (utilisateur > installés)', l.themes.get('sts')?.label === 'Perso');
    check('thèmes fusionnés, fichiers non .json ignorés', l.themes.size === 2 && l.themes.has('arda'));
    check('fichier cassé signalé, les autres chargés', l.problems.some(p => p.includes('casse.json')) &&
        l.problems.some(p => p.includes('JSON invalide')));
    check('aucune source -> bibliothèque vide, aucun problème',
        T.buildLibrary().themes.size === 0 && T.buildLibrary([{path: 'x'}]).problems.length === 0);
}

// ---------- 5. extends : chaîne, fusion, cycle ----------
{
    const l = T.buildLibrary([{path: 'livrés', files: [
        {name: 'sts.json', text: STS_TEXT},
        {name: 'ironclad.json', text: JSON.stringify({id: 'ironclad', label: 'Ironclad', api: 1, extends: 'sts',
            tones: {accent: '#FF5555'}, glass: {tint: '#801212', tintOpacity: 0.35}, font: {family: 'Fira Sans', size: 15}})},
        {name: 'fils.json', text: JSON.stringify({id: 'fils', label: 'Fils', api: 1, extends: 'ironclad',
            tones: {text: '#FFFFFF'}})},
        {name: 'orphelin.json', text: JSON.stringify({id: 'orphelin', label: 'Orphelin', api: 1,
            extends: 'inconnu', tones: {warn: '#FFAA00'}})},
        {name: 'a.json', text: JSON.stringify({id: 'a', label: 'A', api: 1, extends: 'b'})},
        {name: 'b.json', text: JSON.stringify({id: 'b', label: 'B', api: 1, extends: 'a'})},
    ]}]);
    check('chaîne [sts, ironclad, fils]', T.chainFor('fils', l.themes).names.join(',') === 'sts,ironclad,fils');
    check('chaîne d\'un thème sans extends : sts en base', T.chainFor('sts', l.themes).names.join(',') === 'sts');
    const merged = T.mergeChain(T.chainFor('fils', l.themes).names, l.themes);
    check('fusion : le fils surcharge le parent, le parent surcharge sts',
        merged.tones.text.hex === '#FFFFFF' && merged.tones.accent.hex === '#FF5555' &&
        merged.tones.curse.hex === T.parseColor(JSON.parse(STS_TEXT).tones.curse).hex);
    check('fusion : verre et police héritées du parent', merged.glass.tint.hex === '#801212' &&
        merged.glass.tintOpacity === 0.35 && merged.font.family === 'Fira Sans' && merged.font.size === 15);
    check('parent introuvable : signalé, le thème s\'applique quand même sur sts',
        T.chainFor('orphelin', l.themes).names.join(',') === 'sts,orphelin' &&
        T.chainFor('orphelin', l.themes).problems.some(p => p.includes('inconnu')));
    check('cycle d\'extends refusé (chaîne vide + problème)',
        T.chainFor('a', l.themes).names.length === 0 && T.chainFor('a', l.themes).problems[0].includes('cycle'));
}

// ---------- 6. résolution complète : repli sts, auto-perso, surcharges ----------
{
    const l = T.buildLibrary([{path: 'livrés', files: [
        {name: 'sts.json', text: STS_TEXT},
        {name: 'ironclad.json', text: JSON.stringify({id: 'ironclad', label: 'Ironclad', api: 1, extends: 'sts',
            tones: {accent: '#FF5555'}})},
    ]}]);
    const def = T.resolveTheme({library: l.themes});
    check('défaut = sts, sans problème', def.id === 'sts' && def.problems.length === 0 && def.theme.tones.text.hex === '#FFF6E2');
    const missing = T.resolveTheme({requested: 'rubis', library: l.themes});
    check('thème introuvable -> repli sts + problème', missing.id === 'sts' &&
        missing.problems.some(p => p.includes('introuvable')) && missing.theme.tones.accent.hex === '#EFC851');
    const broken = T.resolveTheme({requested: 'a', library: T.buildLibrary([{path: 'x', files: [
        {name: 'a.json', text: '{'}, {name: 'sts.json', text: STS_TEXT}]}]).themes});
    check('thème cassé (JSON) -> repli sts + problème', broken.id === 'sts' && broken.problems.length > 0);
    check('aucun thème disponible -> aucun exception, thème null',
        T.resolveTheme({library: new Map()}).theme.tones === undefined ||
        T.resolveTheme({library: new Map()}).id === null);
    check('entrées aberrantes -> jamais d\'exception',
        T.resolveTheme() && T.resolveTheme({library: null}) && T.resolveTheme({requested: 42}) &&
        T.resolveTheme({requested: 'sts', library: [], overrides: 'x'}) &&
        T.resolveTheme({requested: 'sts', library: l.themes, overrides: [1, 2]}));
    const auto = T.resolveTheme({requested: 'auto-perso', library: l.themes, autoId: 'ironclad'});
    check('auto-perso : thème du personnage de la run', auto.id === 'ironclad' && auto.auto &&
        auto.theme.tones.accent.hex === '#FF5555');
    const autoOff = T.resolveTheme({requested: 'auto-perso', library: l.themes, autoId: null});
    check('auto-perso hors run -> sts + problème', autoOff.id === 'sts' &&
        autoOff.problems.some(p => p.includes('auto-perso')));
    const autoUnknown = T.resolveTheme({requested: 'auto-perso', library: l.themes, autoId: 'defect'});
    check('auto-perso sans thème du personnage -> sts + problème', autoUnknown.id === 'sts' &&
        autoUnknown.problems.some(p => p.includes('defect')));
    const ov = T.resolveTheme({requested: 'sts', library: l.themes, overrides: {accent: '#EFC85199', rose: '#FF0000',
        text: 'pas une couleur'}});
    check('surcharges appearance.hud.tones appliquées par-dessus le thème',
        ov.theme.tones.accent.hex === '#EFC851' && Math.abs(ov.theme.tones.accent.alpha - 0.6) < 0.01 &&
        ov.problems.some(p => p.includes('rose')) && ov.problems.some(p => p.includes('text')));
    check('surcarte par ton sémantique ET par alias v1.3',
        T.resolveTheme({requested: 'sts', library: l.themes, overrides: {gold: '#000000'}}).theme.tones.accent.hex === '#000000');
}

// ---------- 7. verre, liseré, ombre ----------
{
    const theme = sts.theme;
    const g = T.glassTint(theme, 'verre');
    check('teinte du verre : tint + tintOpacity du thème (0,55)', g.hex === '#192930' && Math.abs(g.alpha - 0.55) < 0.001,
        `${g.alpha}`);
    check('eco : aplat plus opaque (opacité de surface, même couleur)',
        Math.abs(T.glassTint(theme, 'eco').alpha - 0.68) < 0.001 && T.glassTint(theme, 'eco').hex === g.hex);
    const custom = T.parseTheme(JSON.stringify({id: 'x', api: 1, glass: {tint: '#801212', tintOpacity: 0.35}})).theme;
    check('teinte du verre depuis glass (thème personnalisé)', T.glassTint(custom, 'verre').hex === '#801212' &&
        Math.abs(T.glassTint(custom, 'verre').alpha - 0.35) < 0.001);
    check('verre sans `glass` : repli sur le ton background, opacité 0,55',
        T.glassTint(T.parseTheme(JSON.stringify({id: 'x', api: 1, tones: {background: '#123456'}})).theme).hex === '#123456');
    check('liseré : ton border du thème (alpha #66)', Math.abs(T.borderColor(theme).alpha - 0.4) < 0.01,
        `${T.borderColor(theme).alpha.toFixed(2)}`);
    check('liseré sans ton border : accent @ 0,4 (charte)', T.borderColor({tones: {}})?.hex === null &&
        T.borderColor({tones: {accent: {hex: '#EFC851', alpha: 1}}}).alpha === 0.4);
    check('ombre : background @ 0,45', T.shadowColor(theme).alpha === 0.45);
    check('thème nul : aucune couleur, aucune exception',
        T.glassTint(null).hex === null && T.borderColor(null).hex === null && T.shadowColor(null).hex === null);
}

// ---------- 8. auto-perso, clé de changement, description ----------
{
    check('characterId du contrat -> id de thème',
        T.autoThemeId('CHARACTER.NECROBINDER') === 'necrobinder' && T.autoThemeId('CHARACTER.IRONCLAD') === 'ironclad' &&
        T.autoThemeId('character.the_silent') === 'silent' && T.autoThemeId('CHARACTER.DEFECT') === 'defect');
    check('personnage inconnu -> null', T.autoThemeId('CHARACTER.THE_GUARDIAN') === null &&
        T.autoThemeId('CHARACTER.WATCHER') === 'watcher' && T.autoThemeId(null) === null);
    const a = T.resolveTheme({library: lib.themes});
    const b = T.resolveTheme({library: lib.themes});
    check('clé stable pour un même thème', T.themeKey(a) === T.themeKey(b));
    const c = T.resolveTheme({library: lib.themes, overrides: {accent: '#000000'}});
    check('clé différente dès qu\'un ton change', T.themeKey(a) !== T.themeKey(c));
    check('description journalisable (chaîne d\'extends)', T.describeTheme(a) === 'sts' &&
        T.describeTheme({id: 'fils', chain: ['sts', 'ironclad', 'fils']}) === 'fils (sts → ironclad → fils)');
}

out(failures ? `\n${failures} échec(s)` : '\ntous les tests passent');
exit(failures ? 1 : 0);
