// Substitut du jeu pour le banc headless : fenêtre X11 (GDK_BACKEND=x11 -> XWayland)
// PLEIN ÉCRAN, motif contrasté (pour voir le flou), redessinée à chaque frame comme un jeu,
// journalise clics, touches et FPS sur stdout. Ce n'est PAS l'UI d'ULTIMA (couleurs libres).
// Lancer : GDK_BACKEND=x11 gjs -m fake-game.js
import GLib from 'gi://GLib';
import Gdk from 'gi://Gdk?version=4.0';
import Gtk from 'gi://Gtk?version=4.0';

const app = new Gtk.Application({application_id: 'local.ultima.FakeGame'});
const log = s => print(`${(GLib.get_monotonic_time() / 1e6).toFixed(3)} ${s}`);

app.connect('activate', () => {
    const win = new Gtk.ApplicationWindow({application: app, title: 'ULTIMA-FAKE-GAME'});
    const area = new Gtk.DrawingArea({hexpand: true, vexpand: true});
    let frame = 0;
    area.set_draw_func((_a, cr, w, h) => {
        const n = 24;
        for (let i = 0; i < n; i++) {
            for (let j = 0; j < n; j++) {
                const v = (i + j) % 2;
                cr.setSourceRGB(v ? 0.85 : 0.1, ((i * 7) % n) / n, ((j * 5) % n) / n);
                cr.rectangle((i * w) / n, (j * h) / n, w / n + 1, h / n + 1);
                cr.fill();
            }
        }
        cr.setSourceRGB(1, 1, 1);
        cr.rectangle(((frame % 240) / 240) * w, h * 0.9, 40, 20); // « jeu » qui bouge
        cr.fill();
    });
    let count = 0, t0 = GLib.get_monotonic_time();
    area.add_tick_callback(() => {
        frame++;
        count++;
        area.queue_draw();
        const t = GLib.get_monotonic_time();
        if (t - t0 >= 1e6) {
            log(`FPS ${(count * 1e6 / (t - t0)).toFixed(1)}`);
            count = 0;
            t0 = t;
        }
        return GLib.SOURCE_CONTINUE;
    });
    const click = new Gtk.GestureClick({button: 0});
    click.connect('pressed', (_g, _n, x, y) => log(`CLICK ${Math.round(x)} ${Math.round(y)}`));
    area.add_controller(click);
    const keys = new Gtk.EventControllerKey();
    keys.connect('key-pressed', (_c, keyval) => {
        log(`KEY ${Gdk.keyval_name(keyval)}`);
        return false;
    });
    win.add_controller(keys);
    win.set_child(area);
    win.fullscreen();
    win.present();
    log('READY');
});
app.run([]);
