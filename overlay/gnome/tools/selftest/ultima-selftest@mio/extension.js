// Banc d'essai headless d'ultima@mio — NE JAMAIS installer dans la vraie session.
// Inerte si ULTIMA_SELFTEST_OUT n'est pas défini (seul tools/headless-proof.sh le définit).
//
// Scénario (dans un vrai GNOME Shell 50, instance --headless isolée) :
//  1. attend la fenêtre X11 plein écran « ULTIMA-FAKE-GAME » (substitut du jeu) ;
//  2. relève la pile d'acteurs (ultima-root au-dessus de window_group / top_window_group) ;
//  3. captures : rail au repos, survol (pointeur virtuel), retour au rail après 2,5 s,
//     raccourci Super+F9 (clavier virtuel), Super+F8 (masqué) ;
//  4. clic virtuel DANS le panneau déployé : le pick Clutter et le faux jeu doivent le recevoir ;
//  5. disable()/enable() d'ultima@mio : plus aucun acteur ni raccourci après disable().
// Résultats : $ULTIMA_SELFTEST_OUT/selftest.json + shots/*.png, puis fichier « selftest-done ».

import Clutter from 'gi://Clutter';
import Gio from 'gi://Gio';
import GLib from 'gi://GLib';
import Meta from 'gi://Meta';
import Shell from 'gi://Shell';

import * as Main from 'resource:///org/gnome/shell/ui/main.js';
import {Extension} from 'resource:///org/gnome/shell/extensions/extension.js';

const TAG = '[ULTIMA-SELFTEST]';
const sleep = ms => new Promise(r => GLib.timeout_add(GLib.PRIORITY_DEFAULT, ms, () => {
    r();
    return GLib.SOURCE_REMOVE;
}));
const now = () => GLib.get_monotonic_time();

// ExtensionManager ré-active les extensions activées APRÈS ultima@mio quand on la désactive :
// le scénario ne doit tourner qu'une fois par processus.
let started = false;

export default class SelftestExtension extends Extension {
    enable() {
        this._out = GLib.getenv('ULTIMA_SELFTEST_OUT');
        if (!this._out) {
            console.warn(`${TAG} ULTIMA_SELFTEST_OUT absent : inerte`);
            return;
        }
        if (started)
            return;
        started = true;
        this._results = {checks: [], notes: []};
        this._run().catch(e => {
            this._results.notes.push(`EXCEPTION ${e}\n${e.stack}`);
            console.error(`${TAG} ${e}\n${e.stack}`);
        }).finally(() => this._finish());
    }

    disable() {}

    _check(name, ok, detail = '') {
        this._results.checks.push({name, ok: !!ok, detail});
        console.log(`${TAG} ${ok ? 'OK  ' : 'FAIL'} ${name} ${detail}`);
    }

    _finish() {
        const path = GLib.build_filenamev([this._out, 'selftest.json']);
        GLib.file_set_contents(path, JSON.stringify(this._results, null, 2));
        GLib.file_set_contents(GLib.build_filenamev([this._out, 'selftest-done']), 'done\n');
    }

    _root() {
        return Main.layoutManager.uiGroup.get_children().find(a => a.name === 'ultima-root') ?? null;
    }

    async _shot(name) {
        await sleep(400); // laisse finir l'éventuelle transition et un redraw
        const file = Gio.File.new_for_path(GLib.build_filenamev([this._out, 'shots', `${name}.png`]));
        const stream = file.replace(null, false, Gio.FileCreateFlags.NONE, null);
        const shooter = new Shell.Screenshot();
        await new Promise((resolve, reject) => {
            shooter.screenshot(false, stream, (s, res) => {
                try {
                    s.screenshot_finish(res);
                    resolve();
                } catch (e) {
                    reject(e);
                }
            });
        });
        stream.close(null);
        this._results.notes.push(`capture ${name}.png`);
    }

    _pointer(x, y) {
        this._ptr.notify_absolute_motion(now(), x, y);
    }

    async _click(x, y) {
        this._pointer(x, y);
        await sleep(100);
        this._ptr.notify_button(now(), Clutter.BUTTON_PRIMARY, Clutter.ButtonState.PRESSED);
        await sleep(50);
        this._ptr.notify_button(now(), Clutter.BUTTON_PRIMARY, Clutter.ButtonState.RELEASED);
        await sleep(300);
    }

    async _combo(keyvals) {
        for (const k of keyvals) {
            this._kbd.notify_keyval(now(), k, Clutter.KeyState.PRESSED);
            await sleep(30);
        }
        for (const k of [...keyvals].reverse()) {
            this._kbd.notify_keyval(now(), k, Clutter.KeyState.RELEASED);
            await sleep(30);
        }
        await sleep(300);
    }

    _gameLog() {
        try {
            const [, b] = GLib.file_get_contents(GLib.build_filenamev([this._out, 'game.log']));
            return new TextDecoder().decode(b);
        } catch {
            return '';
        }
    }

    async _run() {
        const seat = Clutter.get_default_backend().get_default_seat();
        this._ptr = seat.create_virtual_device(Clutter.InputDeviceType.POINTER_DEVICE);
        this._kbd = seat.create_virtual_device(Clutter.InputDeviceType.KEYBOARD_DEVICE);
        // Banc : le 1er mouvement absolu d'un périphérique virtuel neuf n'applique qu'un axe
        // (observé : 1500,432 -> [1500,0]) ; on « chauffe » le pointeur au centre.
        this._pointer(960, 540);
        await sleep(200);
        this._pointer(960, 540);

        // 1. faux jeu X11 plein écran
        let game = null;
        for (let i = 0; i < 300 && !game; i++) {
            game = global.display.list_all_windows().find(w => w.get_title() === 'ULTIMA-FAKE-GAME' && w.is_fullscreen());
            if (!game)
                await sleep(100);
        }
        this._check('faux jeu présent et plein écran', game);
        if (!game)
            return;
        this._check('faux jeu = client X11 (XWayland)', game.get_client_type() === Meta.WindowClientType.X11,
            `client_type=${game.get_client_type()}`);
        const mon = Main.layoutManager.primaryMonitor;
        // GNOME démarre dans la vue d'ensemble ; le jeu lancé depuis Steam la ferme.
        Main.overview.hide();
        for (let i = 0; i < 50 && Main.overview.visible; i++)
            await sleep(100);
        game.activate(global.get_current_time());
        await sleep(1500);
        this._check('vue d\'ensemble fermée', !Main.overview.visible);
        this._check('moniteur en plein écran (monitor.inFullscreen) hors vue d\'ensemble',
            mon.inFullscreen, `${mon.width}x${mon.height}`);

        // 2. pile d'acteurs
        const root = this._root();
        this._check('ultima-root présent', root);
        if (!root)
            return;
        const kids = Main.layoutManager.uiGroup.get_children();
        const iRoot = kids.indexOf(root);
        const iWin = kids.indexOf(global.window_group);
        const iTop = kids.indexOf(global.top_window_group);
        this._check('ultima-root au-dessus de window_group et top_window_group', iRoot > iWin && iRoot > iTop,
            `index root=${iRoot} window_group=${iWin} top_window_group=${iTop} / ${kids.length}`);
        this._check('ultima-root visible malgré le plein écran', root.visible && root.mapped);
        this._check('ultima-root non réactif', !root.reactive);
        const blur = root.get_children().find(c => c.name === 'ultima-glass')?.get_effect('ultima-blur');
        this._check('Shell.BlurEffect mode BACKGROUND', blur && blur.mode === Shell.BlurMode.BACKGROUND,
            `radius=${blur?.radius} brightness=${blur?.brightness}`);
        const [rx, ry] = root.get_transformed_position();
        const [rw, rh] = root.get_transformed_size();
        this._check('rail ≥ 14 px de large', rw >= 14, `rail=(${rx},${ry},${rw}x${rh})`);
        this._check('panneau GNOME masqué par le plein écran (témoin trackFullscreen)', !Main.layoutManager.panelBox.visible);
        this._check('focus resté au faux jeu', global.display.focus_window === game,
            `focus=${global.display.focus_window?.get_title()}`);
        await this._shot('01-rail-repos');

        // 3. survol (pointeur virtuel) -> déploiement
        this._pointer(rx + rw - 4, ry + rh / 2);
        await sleep(900);
        const ov = Main.extensionManager.lookup('ultima@mio')?.stateObj?._overlay;
        this._results.notes.push(`diag survol : get_pointer=${JSON.stringify(global.get_pointer().slice(0, 2))} ` +
            `pollId=${ov?._pollId} expanded=${ov?._expanded} dwell=${ov?._dwellSince} layout=${JSON.stringify(ov?._layout)}`);
        this._check('survol -> panneau déployé à 260 px', Math.round(root.width) === 260, `width=${root.width}`);
        await this._shot('02-survol-deploye');

        // 4. clic à travers le panneau déployé
        const cx = Math.round(rx + rw - 150), cy = Math.round(ry + 80);
        const picked = global.stage.get_actor_at_pos(Clutter.PickMode.REACTIVE, cx, cy);
        let a = picked, insideUltima = false;
        while (a) {
            if (a === root)
                insideUltima = true;
            a = a.get_parent();
        }
        this._check('pick Clutter au point du panneau ≠ ULTIMA', !insideUltima,
            `acteur=${picked?.constructor?.name}:${picked?.name ?? ''}`);
        const before = this._gameLog();
        await this._click(cx, cy);
        const after = this._gameLog().slice(before.length);
        this._check('le faux jeu reçoit le clic fait SUR le panneau', /CLICK/.test(after),
            `point=(${cx},${cy}) log=${JSON.stringify(after.trim())}`);
        this._check('panneau toujours déployé pendant le survol', Math.round(root.width) === 260);

        // 5. sortie -> retour au rail après 2,5 s
        this._pointer(mon.width / 2, mon.height / 2);
        await sleep(1500);
        this._check('encore déployé à 1,5 s (pas de repli prématuré)', Math.round(root.width) === 260, `width=${root.width}`);
        await sleep(1700);
        this._check('replié en rail après 2,5 s', Math.round(root.width) === 14, `width=${root.width}`);
        await this._shot('03-repli-apres-inactivite');

        // 6. raccourcis (clavier virtuel) : Super+F9, Super+F8
        const logBeforeKeys = this._gameLog().length;
        await this._combo([Clutter.KEY_Super_L, Clutter.KEY_F9]);
        await sleep(300);
        this._check('Super+F9 -> panneau épinglé déployé', Math.round(root.width) === 260, `width=${root.width}`);
        await this._shot('04-raccourci-super-f9');
        await sleep(3000);
        this._check('épinglé : pas de repli après 3 s hors survol', Math.round(root.width) === 260);
        await this._combo([Clutter.KEY_Super_L, Clutter.KEY_F8]);
        this._check('Super+F8 -> masqué', !root.visible);
        await this._shot('05-masque-super-f8');
        await this._combo([Clutter.KEY_Super_L, Clutter.KEY_F8]);
        this._check('Super+F8 -> réaffiché', root.visible);
        const keyLog = this._gameLog().slice(logBeforeKeys);
        this._check('le faux jeu ne reçoit PAS F9/F8', !/KEY F[89]/.test(keyLog), `log=${JSON.stringify(keyLog.trim())}`);
        this._check('focus toujours au faux jeu', global.display.focus_window === game);

        // 7. FPS indicatif du faux jeu (overlay visible vs masqué) — lu dans game.log
        await this._combo([Clutter.KEY_Super_L, Clutter.KEY_F9]); // désépingle
        const fps = log => (log.match(/FPS [0-9.]+/g) ?? []).map(x => Number(x.slice(4)));
        let mark = this._gameLog().length;
        await sleep(4200); // overlay visible (rail)
        const fpsShown = fps(this._gameLog().slice(mark));
        await this._combo([Clutter.KEY_Super_L, Clutter.KEY_F8]);
        mark = this._gameLog().length;
        await sleep(4200); // overlay masqué
        const fpsHidden = fps(this._gameLog().slice(mark));
        await this._combo([Clutter.KEY_Super_L, Clutter.KEY_F8]);
        this._results.notes.push(`FPS faux jeu (indicatif, headless) overlay visible=${JSON.stringify(fpsShown)} masqué=${JSON.stringify(fpsHidden)}`);
        this._check('le faux jeu tourne (FPS mesurés)', fpsShown.length > 0 && fpsHidden.length > 0);

        // 8. disable() / enable()
        Main.extensionManager.disableExtension('ultima@mio');
        await sleep(500);
        const leftovers = Main.layoutManager.uiGroup.get_children().filter(c => c.name === 'ultima-root');
        this._check('disable() : plus aucun acteur ultima-root', leftovers.length === 0);
        this._check('disable() : raccourcis retirés (allowKeybinding NONE)',
            Main.wm._allowedKeybindings['toggle-panel'] === Shell.ActionMode.NONE &&
            Main.wm._allowedKeybindings['toggle-visible'] === Shell.ActionMode.NONE);
        Main.extensionManager.enableExtension('ultima@mio');
        await sleep(800);
        this._check('enable() à nouveau : ultima-root revient', this._root()?.visible);
        await this._shot('06-reactive-apres-cycle');
    }
}
