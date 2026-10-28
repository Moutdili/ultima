// ULTIMA — extension GNOME Shell 50 (spike S1, moteur B).
//
// Un acteur St/Clutter dessiné par le compositeur, AU-DESSUS du jeu plein écran,
// non réactif (click-through), verre réel (Shell.BlurEffect BACKGROUND + teinte #192930),
// raccourcis globaux gérés par Mutter (le jeu ne reçoit jamais la touche).
//
// Choix de couche (code GNOME Shell 50, resource:///org/gnome/shell/ui/layout.js) :
//   - addChrome() place l'acteur SOUS global.top_window_group
//     (« this.uiGroup.set_child_below_sibling(actor, global.top_window_group) ») ;
//   - trackFullscreen (défaut false) masque l'acteur si une fenêtre plein écran est sur le
//     moniteur (_updateActorVisibility : « actor.visible = !(... monitor.inFullscreen) ») ;
//   - addTopChrome() = « Like addChrome(), but adds @actor above all windows, including popups »
//     -> retenu, SANS trackFullscreen.
//   - Comme osdWindow.js / messageTray.js (affichés au-dessus du plein écran), on appelle
//     global.compositor.disable_unredirect() tant que l'overlay est visible, sinon la fenêtre
//     plein écran peut être affichée en scanout direct, sans composition de l'overlay.
//
// Robustesse (non-négociables) : toute erreur de enable() est attrapée et logguée ; disable()
// retire acteurs, raccourcis, timeouts, signaux, moniteur de fichier, et rend l'unredirect.

import Clutter from 'gi://Clutter';
import Gio from 'gi://Gio';
import GLib from 'gi://GLib';
import Meta from 'gi://Meta';
import Shell from 'gi://Shell';
import St from 'gi://St';

import * as Main from 'resource:///org/gnome/shell/ui/main.js';
import {Extension} from 'resource:///org/gnome/shell/extensions/extension.js';

import {PALETTE, rgba, toneHex} from './palette.js';
import * as Geo from './geometry.js';
import {parseState} from './state.js';

const TAG = '[ULTIMA]';
const POLL_MS = 80;              // suivi du pointeur (12,5 Hz) — seulement quand le rail est visible
const HOVER_DWELL_MS = 150;      // évite l'expansion sur un simple passage du curseur
const EXPAND_MS = 180;           // docs/17 §4 : 150-220 ms, ease-out, sans overshoot
const IDLE_COLLAPSE_MS = 2500;   // docs/17 §4 : retour au rail après 2,5 s sans survol
const STATE_DEBOUNCE_MS = 100;

const GLASS_TINT_ALPHA = 0.72;   // docs/17 §3 (≥ 0.6 : leçon v1 n°4)
const RAIL_BORDER_ALPHA = 0.85;  // leçon v1 (commit fa00f8d) : liseré or lisible au repos
const PANEL_BORDER_ALPHA = 0.18; // docs/17 §3
const BLUR_RADIUS = 24;          // px, fixe (jamais animé — docs/17 §3)
const RADIUS = 10;               // docs/17 §3 corner_radius

const KEY_PANEL = 'toggle-panel';
const KEY_VISIBLE = 'toggle-visible';

class UltimaOverlay {
    constructor(settings, extPath) {
        this._settings = settings;
        this._extPath = extPath;
        this._timeouts = new Set();
        this._pollId = 0;
        this._idleId = 0;
        this._dwellSince = 0;
        this._expanded = false;
        this._pinned = false;
        this._userHidden = false;
        this._overviewHidden = false;
        this._unredirectDisabled = false;
        this._keybindings = [];
        this._cancellable = new Gio.Cancellable();
        this._destroyed = false;
        this._layout = null;
    }

    start() {
        this._build();
        Main.layoutManager.addTopChrome(this._root); // PAS de trackFullscreen (cf. en-tête)
        this._relayout();
        this._render(null);

        Main.layoutManager.connectObject('monitors-changed', () => this._relayout(), this);
        Main.overview.connectObject(
            'showing', () => this._setOverviewHidden(true),
            'hidden', () => this._setOverviewHidden(false), this);
        this._settings.connectObject(
            'changed::top-fraction', () => this._relayout(),
            'changed::height-fraction', () => this._relayout(), this);

        this._overviewHidden = Main.overview.visible; // GNOME démarre dans la vue d'ensemble
        this._addKeybinding(KEY_PANEL, () => this._togglePinned());
        this._addKeybinding(KEY_VISIBLE, () => this._toggleVisible());

        this._watchState();
        this._updateShown();
        console.log(`${TAG} actif : couche=addTopChrome, rail=${Geo.RAIL_W}px, panneau=${Geo.PANEL_W}px`);
    }

    // ---------- construction ----------

    _build() {
        // Racine : non réactive, jamais focusable -> aucun clic ni touche avalé.
        this._root = new St.Widget({
            name: 'ultima-root',
            reactive: false,
            can_focus: false,
            track_hover: false,
            clip_to_allocation: true,
        });

        // Couche 1 : verre (flou de l'arrière-plan). Acteur SANS enfant : aucun blur sur le texte.
        // brightness 1.0 : l'assombrissement vient de la teinte (arrondie), pas du flou (rectangulaire).
        this._glass = new St.Widget({name: 'ultima-glass', reactive: false});
        this._glass.add_effect_with_name('ultima-blur', new Shell.BlurEffect({
            mode: Shell.BlurMode.BACKGROUND,
            radius: BLUR_RADIUS,
            brightness: 1.0,
        }));

        // Couche 2 : teinte #192930 + liseré or 1 px + coins 10.
        this._frame = new St.Widget({name: 'ultima-frame', reactive: false});

        for (const a of [this._glass, this._frame]) {
            a.add_constraint(new Clutter.BindConstraint({
                source: this._root, coordinate: Clutter.BindCoordinate.SIZE,
            }));
            this._root.add_child(a);
        }

        // Couche 3a : pastilles du rail (niveau 0).
        this._dots = new St.BoxLayout({
            orientation: Clutter.Orientation.VERTICAL,
            reactive: false,
            style: 'spacing: 10px;',
        });
        this._dotActors = [0, 1, 2].map(() => {
            const d = new St.Widget({reactive: false, width: 8, height: 8});
            this._dots.add_child(d);
            return d;
        });
        this._root.add_child(this._dots);

        // Couche 3b : contenu du panneau (niveau 1), largeur fixe, découpé par la racine.
        this._content = new St.BoxLayout({
            orientation: Clutter.Orientation.VERTICAL,
            reactive: false,
            opacity: 0,
            visible: false,
            width: Geo.PANEL_W - 2 * 14,
            x: 14,
            y: 12,
            style: 'spacing: 6px;',
        });
        this._root.add_child(this._content);

        this._applyFrameStyle();
    }

    _applyFrameStyle() {
        const borderAlpha = this._expanded ? PANEL_BORDER_ALPHA : RAIL_BORDER_ALPHA;
        this._frame.set_style(
            `background-color: ${rgba(PALETTE.glassTint, GLASS_TINT_ALPHA)};` +
            `border: 1px solid ${rgba(PALETTE.gold, borderAlpha)};` +
            `border-radius: ${RADIUS}px;` +
            `box-shadow: 0 0 6px ${rgba(PALETTE.shadow, 0.35)};`);
    }

    _relayout() {
        const monitor = Main.layoutManager.primaryMonitor;
        if (!monitor)
            return;
        this._layout = Geo.computeLayout(monitor,
            this._settings.get_double('top-fraction'),
            this._settings.get_double('height-fraction'));
        const L = this._layout;
        this._root.remove_all_transitions();
        const w = this._expanded ? Geo.PANEL_W : Geo.RAIL_W;
        this._root.set_position(L.right - w, L.y);
        this._root.set_size(w, L.h);
        // pastilles centrées dans le rail
        const dotsH = 3 * 8 + 2 * 10;
        this._dots.set_position(Math.round((Geo.RAIL_W - 8) / 2), Math.round((L.h - dotsH) / 2));
        this._content.height = L.h - 24;
        const cov = Geo.coverage(L, monitor);
        console.log(`${TAG} géométrie : moniteur=${monitor.width}x${monitor.height} ` +
            `rail=(${L.railX},${L.y},${Geo.RAIL_W}x${L.h}) panneau=(${L.panelX},${L.y},${Geo.PANEL_W}x${L.h}) ` +
            `surface replié=${(cov.collapsed * 100).toFixed(2)}% déployé=${(cov.expanded * 100).toFixed(2)}%`);
    }

    // ---------- état publié par le daemon ----------

    _statePath() {
        return GLib.build_filenamev([GLib.get_user_runtime_dir(), 'ultima', 'overlay-state.json']);
    }

    _watchState() {
        const file = Gio.File.new_for_path(this._statePath());
        try {
            this._monitor = file.monitor_file(Gio.FileMonitorFlags.NONE, this._cancellable);
            this._monitor.connectObject('changed', () => this._queueStateLoad(), this);
        } catch (e) {
            console.warn(`${TAG} pas de surveillance de ${file.get_path()} : ${e.message}`);
        }
        this._loadState();
    }

    _queueStateLoad() {
        if (this._stateTimer)
            return;
        this._stateTimer = this._addTimeout(STATE_DEBOUNCE_MS, () => {
            this._stateTimer = 0;
            this._loadState();
        });
    }

    _loadState() {
        const live = Gio.File.new_for_path(this._statePath());
        const sample = Gio.File.new_for_path(GLib.build_filenamev([this._extPath, 'sample-state.json']));
        this._readJson(live, (res) => {
            if (res?.ok) {
                this._render(res.state);
                return;
            }
            if (res && !res.ok)
                console.warn(`${TAG} état daemon rejeté : ${res.error}`);
            this._readJson(sample, (r2) => this._render(r2?.ok ? r2.state : null));
        });
    }

    _readJson(file, done) {
        file.load_contents_async(this._cancellable, (f, result) => {
            if (this._destroyed)
                return;
            try {
                const [, bytes] = f.load_contents_finish(result);
                done(parseState(new TextDecoder().decode(bytes)));
            } catch (e) {
                if (!e.matches?.(Gio.IOErrorEnum, Gio.IOErrorEnum.NOT_FOUND) &&
                    !e.matches?.(Gio.IOErrorEnum, Gio.IOErrorEnum.CANCELLED))
                    console.warn(`${TAG} lecture ${f.get_path()} : ${e.message}`);
                done(null);
            }
        });
    }

    _render(state) {
        const s = state ?? {
            title: 'ULTIMA', subtitle: 'aucun état', source: '', lines: [],
            rail: [{tone: 'lightGray'}, {tone: 'lightGray'}, {tone: 'lightGray'}],
        };
        s.rail.forEach((d, i) => this._dotActors[i]?.set_style(
            `background-color: ${toneHex(d.tone)}; border-radius: 4px;`));

        this._content.destroy_all_children();
        const title = new St.Label({
            text: s.source ? `${s.title}  ·  ${s.source}` : s.title,
            style: `color: ${PALETTE.gold}; font-weight: bold; font-size: 16px;`,
        });
        this._content.add_child(title);
        if (s.subtitle) {
            this._content.add_child(new St.Label({
                text: s.subtitle,
                style: `color: ${rgba(PALETTE.cream, 0.5)}; font-size: 12px;`,
            }));
        }
        for (const line of s.lines) {
            const row = new St.BoxLayout({reactive: false, x_expand: true});
            row.add_child(new St.Label({
                text: line.label,
                x_expand: true,
                style: `color: ${PALETTE.cream}; font-size: 14px;`,
            }));
            row.add_child(new St.Label({
                text: line.value,
                style: `color: ${toneHex(line.tone)}; font-family: "Source Code Pro", monospace; ` +
                       'font-weight: 600; font-size: 14px;',
            }));
            this._content.add_child(row);
        }
    }

    // ---------- visibilité, unredirect, suivi du pointeur ----------

    _isShown() {
        return !this._userHidden && !this._overviewHidden;
    }

    _setOverviewHidden(v) {
        this._overviewHidden = v;
        this._updateShown();
    }

    _toggleVisible() {
        this._userHidden = !this._userHidden;
        console.log(`${TAG} raccourci ${KEY_VISIBLE} -> ${this._userHidden ? 'masqué' : 'visible'}`);
        this._updateShown();
    }

    _updateShown() {
        const shown = this._isShown();
        this._root.visible = shown;
        if (shown && !this._unredirectDisabled) {
            global.compositor.disable_unredirect();
            this._unredirectDisabled = true;
        } else if (!shown && this._unredirectDisabled) {
            global.compositor.enable_unredirect();
            this._unredirectDisabled = false;
        }
        if (shown)
            this._startPolling();
        else
            this._stopPolling();
    }

    // Choix « aucun clic avalé » : la racine reste reactive:false en permanence. Le survol est
    // déduit de global.get_pointer() à 12,5 Hz, et seulement quand le rail est visible.
    _startPolling() {
        if (this._pollId)
            return;
        // PRIORITY_DEFAULT : en PRIORITY_LOW la source est affamée quand le compositeur
        // redessine en continu (jeu à 60+ FPS) — observé sur le banc headless.
        this._pollId = GLib.timeout_add(GLib.PRIORITY_DEFAULT, POLL_MS, () => {
            try {
                this._tick();
            } catch (e) {
                console.error(`${TAG} tick : ${e}`);
            }
            return GLib.SOURCE_CONTINUE;
        });
        GLib.Source.set_name_by_id(this._pollId, '[ultima] pointer poll');
    }

    _stopPolling() {
        if (this._pollId) {
            GLib.source_remove(this._pollId);
            this._pollId = 0;
        }
        this._dwellSince = 0;
    }

    _tick() {
        if (!this._layout)
            return;
        const [px, py] = global.get_pointer();
        const inside = Geo.contains(Geo.hoverZone(this._layout, this._expanded), px, py);
        const now = GLib.get_monotonic_time() / 1000;

        if (inside) {
            this._cancelIdle();
            if (!this._expanded) {
                if (!this._dwellSince)
                    this._dwellSince = now;
                else if (now - this._dwellSince >= HOVER_DWELL_MS)
                    this._setExpanded(true);
            }
        } else {
            this._dwellSince = 0;
            if (this._expanded && !this._pinned && !this._idleId)
                this._idleId = this._addTimeout(IDLE_COLLAPSE_MS, () => {
                    this._idleId = 0;
                    if (!this._pinned)
                        this._setExpanded(false);
                });
        }
    }

    _cancelIdle() {
        if (this._idleId) {
            this._removeTimeout(this._idleId);
            this._idleId = 0;
        }
    }

    _togglePinned() {
        this._pinned = !this._pinned;
        console.log(`${TAG} raccourci ${KEY_PANEL} -> ${this._pinned ? 'épinglé' : 'libre'}`);
        if (this._pinned && this._userHidden) {
            this._userHidden = false;
            this._updateShown();
        }
        this._cancelIdle();
        this._setExpanded(this._pinned);
    }

    // Une seule transition, jamais en boucle (docs/17 §6).
    _setExpanded(expanded) {
        if (this._expanded === expanded || !this._layout)
            return;
        this._expanded = expanded;
        this._dwellSince = 0;
        const L = this._layout;
        const w = expanded ? Geo.PANEL_W : Geo.RAIL_W;
        this._applyFrameStyle();
        this._root.remove_all_transitions();
        this._root.ease({
            x: L.right - w,
            width: w,
            duration: EXPAND_MS,
            mode: Clutter.AnimationMode.EASE_OUT_QUAD,
        });
        this._dots.visible = !expanded;
        this._content.remove_all_transitions();
        if (expanded) {
            this._content.visible = true;
            this._content.ease({opacity: 255, duration: EXPAND_MS, mode: Clutter.AnimationMode.EASE_OUT_QUAD});
        } else {
            this._content.opacity = 0;
            this._content.visible = false;
        }
    }

    // ---------- raccourcis ----------

    _addKeybinding(name, handler) {
        const action = Main.wm.addKeybinding(name, this._settings,
            Meta.KeyBindingFlags.IGNORE_AUTOREPEAT | Meta.KeyBindingFlags.NON_MASKABLE,
            Shell.ActionMode.NORMAL | Shell.ActionMode.OVERVIEW,
            () => {
                try {
                    handler();
                } catch (e) {
                    console.error(`${TAG} raccourci ${name} : ${e}`);
                }
            });
        if (action === Meta.KeyBindingAction.NONE)
            console.warn(`${TAG} raccourci ${name} (${this._settings.get_strv(name)}) non enregistré (conflit ?)`);
        else
            this._keybindings.push(name);
    }

    // ---------- timeouts suivis (tous retirés dans destroy) ----------

    _addTimeout(ms, fn) {
        const id = GLib.timeout_add(GLib.PRIORITY_DEFAULT, ms, () => {
            this._timeouts.delete(id);
            try {
                fn();
            } catch (e) {
                console.error(`${TAG} timeout : ${e}`);
            }
            return GLib.SOURCE_REMOVE;
        });
        this._timeouts.add(id);
        return id;
    }

    _removeTimeout(id) {
        if (this._timeouts.delete(id))
            GLib.source_remove(id);
    }

    destroy() {
        this._destroyed = true;
        const step = (what, fn) => {
            try {
                fn();
            } catch (e) {
                console.error(`${TAG} nettoyage ${what} : ${e}`);
            }
        };
        step('annulation E/S', () => this._cancellable.cancel());
        step('suivi pointeur', () => this._stopPolling());
        step('timeouts', () => {
            for (const id of this._timeouts)
                GLib.source_remove(id);
            this._timeouts.clear();
            this._idleId = this._stateTimer = 0;
        });
        step('raccourcis', () => {
            for (const name of this._keybindings)
                Main.wm.removeKeybinding(name);
            this._keybindings = [];
        });
        step('signaux', () => {
            Main.layoutManager.disconnectObject(this);
            Main.overview.disconnectObject(this);
            this._settings.disconnectObject(this);
        });
        step('moniteur de fichier', () => {
            this._monitor?.disconnectObject(this);
            this._monitor?.cancel();
            this._monitor = null;
        });
        step('unredirect', () => {
            if (this._unredirectDisabled) {
                global.compositor.enable_unredirect();
                this._unredirectDisabled = false;
            }
        });
        step('acteurs', () => {
            if (this._root) {
                this._root.remove_all_transitions();
                Main.layoutManager.removeChrome(this._root);
                this._root.destroy();
                this._root = null;
            }
        });
        console.log(`${TAG} désactivé, tout nettoyé`);
    }
}

export default class UltimaExtension extends Extension {
    enable() {
        try {
            this._overlay = new UltimaOverlay(this.getSettings(), this.path);
            this._overlay.start();
        } catch (e) {
            console.error(`${TAG} enable() a échoué : ${e}\n${e.stack ?? ''}`);
            this._teardown();
        }
    }

    disable() {
        this._teardown();
    }

    _teardown() {
        try {
            this._overlay?.destroy();
        } catch (e) {
            console.error(`${TAG} disable() : ${e}\n${e.stack ?? ''}`);
        }
        this._overlay = null;
    }
}
