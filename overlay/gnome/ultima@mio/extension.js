// ULTIMA — extension GNOME Shell 50 (moteur B retenu en S1), branchée sur le contrat moteur v1.
//
// Données : EngineState publié par ultima-daemon dans $XDG_RUNTIME_DIR/ultima/state.json, relu
// SEULEMENT sur changement (Gio.FileMonitor + anti-rebond) ; JSON invalide -> dernier état valide
// gardé (state.js) ; rendu texte brut (render.js, décision Mio « design en dernier »).
// Coûts : un planificateur pur (plan.js) décide flou / inhibition de l'unredirect / sondage du
// pointeur à partir des réglages A/B (blur-mode, unredirect-mode, poll-interval-ms ; voir PERF.md).
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
//     global.compositor.disable_unredirect() (par défaut tant que l'overlay est visible, réglage
//     unredirect-mode), sinon la fenêtre plein écran peut être affichée en scanout direct, sans
//     composition de l'overlay.
//
// Robustesse (non-négociables) : toute erreur de enable() est attrapée et logguée ; disable()
// retire acteurs, raccourcis, timeouts, signaux, moniteur de fichier, et rend l'unredirect.

import Clutter from 'gi://Clutter';
import Gio from 'gi://Gio';
import GLib from 'gi://GLib';
import Meta from 'gi://Meta';
import Pango from 'gi://Pango';
import Shell from 'gi://Shell';
import St from 'gi://St';

import * as Main from 'resource:///org/gnome/shell/ui/main.js';
import {Extension} from 'resource:///org/gnome/shell/extensions/extension.js';

import {PALETTE, rgba, toneHex} from './palette.js';
import * as Geo from './geometry.js';
import {StateStore} from './state.js';
import {blockLines, buildView, fitView, viewToText, WRAP_COLS} from './render.js';
import {computePlan, Poller} from './plan.js';

const TAG = '[ULTIMA]';
const HOVER_DWELL_MS = 150;      // évite l'expansion sur un simple passage du curseur
const EXPAND_MS = 180;           // docs/17 §4 : 150-220 ms, ease-out, sans overshoot
const IDLE_COLLAPSE_MS = 2500;   // docs/17 §4 : retour au rail après 2,5 s sans survol
const STATE_DEBOUNCE_MS = 100;   // rafales d'écriture du daemon -> une seule relecture
const BLOCK_SPACING = 6;         // px entre blocs (valeur du spike)
const FALLBACK_LINE_PX = 18;     // si la mesure de hauteur de ligne échoue

const GLASS_TINT_ALPHA = 0.72;   // docs/17 §3 (≥ 0.6 : leçon v1 n°4)
const RAIL_BORDER_ALPHA = 0.85;  // leçon v1 (commit fa00f8d) : liseré or lisible au repos
const PANEL_BORDER_ALPHA = 0.18; // docs/17 §3
const BLUR_RADIUS = 24;          // px, fixe (jamais animé — docs/17 §3)
const RADIUS = 10;               // docs/17 §3 corner_radius

// Styles du spike conservés tels quels (design en dernier) : titre or gras 16 px, texte 14 px.
const titleStyle = tone => `color: ${toneHex(tone)}; font-weight: bold; font-size: 16px;`;
const bodyStyle = tone => `color: ${toneHex(tone)}; font-size: 14px;`;

const KEY_PANEL = 'toggle-panel';
const KEY_VISIBLE = 'toggle-visible';

class UltimaOverlay {
    constructor(settings, extPath) {
        this._settings = settings;
        this._extPath = extPath;
        this._timeouts = new Set();
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
        this._store = new StateStore();
        this._view = null;
        this._viewDirty = true;
        this._appliedKey = null;
        this._labels = [];
        this._railKey = null;
        this._lineH = 0;
        this._loadSeq = 0;
        this._lastKind = null;
        this._plan = null;
        this._planKey = null;
        this._perf = null;
        this._poller = new Poller({
            add: (ms, fn) => {
                // PRIORITY_DEFAULT : en PRIORITY_LOW la source est affamée quand le compositeur
                // redessine en continu (jeu à 60+ FPS) — observé sur le banc headless.
                const id = GLib.timeout_add(GLib.PRIORITY_DEFAULT, ms, () => {
                    fn();
                    return GLib.SOURCE_CONTINUE;
                });
                GLib.Source.set_name_by_id(id, '[ultima] pointer poll');
                return id;
            },
            remove: id => GLib.source_remove(id),
        }, () => {
            try {
                this._tick();
            } catch (e) {
                console.error(`${TAG} tick : ${e}`);
            }
        });
    }

    start() {
        this._build();
        Main.layoutManager.addTopChrome(this._root); // PAS de trackFullscreen (cf. en-tête)
        this._relayout();
        this._setView(buildView(this._store.current));

        Main.layoutManager.connectObject('monitors-changed', () => {
            this._lineH = 0; // l'échelle a pu changer
            this._relayout();
        }, this);
        Main.overview.connectObject(
            'showing', () => this._setOverviewHidden(true),
            'hidden', () => this._setOverviewHidden(false), this);
        this._settings.connectObject(
            'changed::top-fraction', () => this._relayout(),
            'changed::height-fraction', () => this._relayout(),
            'changed::blur-mode', () => this._readPerfSettings(),
            'changed::unredirect-mode', () => this._readPerfSettings(),
            'changed::poll-interval-ms', () => this._readPerfSettings(), this);

        this._overviewHidden = Main.overview.visible; // GNOME démarre dans la vue d'ensemble
        this._addKeybinding(KEY_PANEL, () => this._togglePinned());
        this._addKeybinding(KEY_VISIBLE, () => this._toggleVisible());

        this._watchState();
        this._readPerfSettings(); // applique aussi le plan initial
        console.log(`${TAG} actif : couche=addTopChrome, rail=${Geo.RAIL_W}px, panneau=${Geo.PANEL_W}px, ` +
            `état=${this._statePath()}`);
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
        // Activé/désactivé par le plan (blur-mode, défaut : panneau ouvert seulement ; rail = aplat).
        this._glass = new St.Widget({name: 'ultima-glass', reactive: false});
        this._blurEffect = new Shell.BlurEffect({
            mode: Shell.BlurMode.BACKGROUND,
            radius: BLUR_RADIUS,
            brightness: 1.0,
        });
        this._blurEffect.enabled = false;
        this._glass.add_effect_with_name('ultima-blur', this._blurEffect);

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
        // Un St.Label par bloc (titre, dégradé, module) : peu d'acteurs, mis à jour seulement si
        // le texte change, et seulement panneau ouvert (rendu différé quand replié).
        this._content = new St.BoxLayout({
            orientation: Clutter.Orientation.VERTICAL,
            reactive: false,
            opacity: 0,
            visible: false,
            width: Geo.PANEL_W - 2 * 14,
            x: 14,
            y: 12,
            style: `spacing: ${BLOCK_SPACING}px;`,
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
        this._appliedKey = null; // hauteur changée -> nouvel ajustement du texte
        this._viewDirty = true;
        if (this._expanded)
            this._applyView();
        const cov = Geo.coverage(L, monitor);
        console.log(`${TAG} géométrie : moniteur=${monitor.width}x${monitor.height} ` +
            `rail=(${L.railX},${L.y},${Geo.RAIL_W}x${L.h}) panneau=(${L.panelX},${L.y},${Geo.PANEL_W}x${L.h}) ` +
            `surface replié=${(cov.collapsed * 100).toFixed(2)}% déployé=${(cov.expanded * 100).toFixed(2)}%`);
    }

    // ---------- état publié par le daemon ----------

    _statePath() {
        return GLib.build_filenamev([GLib.get_user_runtime_dir(), 'ultima', 'state.json']);
    }

    // Relecture SEULEMENT sur changement : aucun minuteur périodique côté extension.
    // Le daemon écrit par tmp + rename : l'événement arrive comme CREATED (ou CHANGES_DONE_HINT
    // pour une écriture en place, DELETED si le daemon retire le fichier). Si le dossier
    // $XDG_RUNTIME_DIR/ultima n'existe pas encore, GLib surveille son apparition (voir PERF.md).
    _watchState() {
        const file = Gio.File.new_for_path(this._statePath());
        try {
            this._monitor = file.monitor_file(Gio.FileMonitorFlags.NONE, this._cancellable);
            this._monitor.connectObject('changed', (_m, _f, _o, event) => {
                if (event === Gio.FileMonitorEvent.ATTRIBUTE_CHANGED ||
                    event === Gio.FileMonitorEvent.PRE_UNMOUNT ||
                    event === Gio.FileMonitorEvent.UNMOUNTED)
                    return;
                this._queueStateLoad();
            }, this);
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
        const seq = ++this._loadSeq;
        const file = Gio.File.new_for_path(this._statePath());
        file.load_contents_async(this._cancellable, (f, result) => {
            if (this._destroyed || seq !== this._loadSeq)
                return; // une relecture plus récente est en cours : elle fera foi
            let res;
            try {
                const [, bytes] = f.load_contents_finish(result);
                res = this._store.acceptText(new TextDecoder().decode(bytes));
            } catch (e) {
                if (e.matches?.(Gio.IOErrorEnum, Gio.IOErrorEnum.CANCELLED))
                    return;
                if (e.matches?.(Gio.IOErrorEnum, Gio.IOErrorEnum.NOT_FOUND)) {
                    res = this._store.acceptMissing();
                } else {
                    console.warn(`${TAG} lecture ${f.get_path()} : ${e.message}`);
                    return; // dernier état valide conservé
                }
            }
            if (res.newError)
                console.warn(`${TAG} état rejeté (dernier état valide conservé) : ${res.error}`);
            if (!res.changed)
                return;
            const cur = this._store.current;
            const kind = cur.kind === 'future' ? `future v${cur.version}` : cur.kind;
            if (kind !== this._lastKind) {
                console.log(`${TAG} état : ${kind}`);
                this._lastKind = kind;
            }
            this._setView(buildView(cur));
        });
    }

    // ---------- rendu texte ----------

    _setView(view) {
        this._view = view;
        this._applyRail(view.rail);
        this._viewDirty = true;
        if (this._expanded)
            this._applyView();
    }

    _applyRail(tones) {
        const key = tones.join(',');
        if (key === this._railKey)
            return;
        this._railKey = key;
        tones.forEach((t, i) => this._dotActors[i]?.set_style(
            `background-color: ${toneHex(t)}; border-radius: 4px;`));
    }

    _lineHeight() {
        if (this._lineH > 0)
            return this._lineH;
        const probe = new St.Label({text: 'Ag', style: bodyStyle('cream')});
        this._content.add_child(probe);
        const [, nat] = probe.get_preferred_height(-1);
        probe.destroy();
        this._lineH = nat > 4 ? nat : FALLBACK_LINE_PX;
        console.log(`${TAG} hauteur de ligne mesurée : ${this._lineH.toFixed(1)} px`);
        return this._lineH;
    }

    // Applique la vue au panneau (seulement panneau ouvert ; replié, on note juste « à refaire »).
    _applyView() {
        if (!this._view || !this._viewDirty)
            return;
        this._viewDirty = false;
        const n = this._view.blocks.length;
        const avail = this._content.height - Math.max(0, n - 1) * BLOCK_SPACING;
        // -1 : marge pour le titre (16 px gras) et l'arrondi de l'estimation de retour à la ligne.
        const maxLines = Math.max(4, Math.floor(avail / this._lineHeight()) - 1);
        const fitted = fitView(this._view, {maxLines, cols: WRAP_COLS});
        const texts = fitted.blocks.map(b => blockLines(b).join('\n'));
        const key = `${viewToText(fitted)}\u0000${fitted.blocks.map(b => `${b.key}:${b.tone}`).join(',')}`;
        if (key === this._appliedKey)
            return;
        this._appliedKey = key;

        const sameShape = this._labels.length === fitted.blocks.length &&
            this._labels.every((l, i) => l._ultimaKey === fitted.blocks[i].key);
        if (!sameShape) {
            this._content.destroy_all_children();
            this._labels = fitted.blocks.map(b => {
                const l = new St.Label({reactive: false, x_expand: true});
                l._ultimaKey = b.key;
                const ct = l.clutter_text;
                ct.line_wrap = true;
                ct.line_wrap_mode = Pango.WrapMode.WORD_CHAR;
                ct.ellipsize = Pango.EllipsizeMode.NONE;
                this._content.add_child(l);
                return l;
            });
        }
        fitted.blocks.forEach((b, i) => {
            const l = this._labels[i];
            const style = b.key === 'title' ? titleStyle(b.tone) : bodyStyle(b.tone);
            if (l._ultimaStyle !== style) {
                l.set_style(style);
                l._ultimaStyle = style;
            }
            if (l.text !== texts[i])
                l.text = texts[i];
        });
    }

    // ---------- visibilité, coûts (plan.js), suivi du pointeur ----------

    _readPerfSettings() {
        this._perf = {
            blurMode: this._settings.get_string('blur-mode'),
            unredirectMode: this._settings.get_string('unredirect-mode'),
            pollMs: this._settings.get_int('poll-interval-ms'),
        };
        console.log(`${TAG} réglages perf : blur-mode=${this._perf.blurMode} ` +
            `unredirect-mode=${this._perf.unredirectMode} poll-interval-ms=${this._perf.pollMs}`);
        this._applyPlan('réglage');
    }

    _setOverviewHidden(v) {
        this._overviewHidden = v;
        this._applyPlan(v ? 'vue d\'ensemble' : 'retour');
    }

    _toggleVisible() {
        this._userHidden = !this._userHidden;
        console.log(`${TAG} raccourci ${KEY_VISIBLE} -> ${this._userHidden ? 'masqué' : 'visible'}`);
        this._applyPlan('raccourci');
    }

    // Seul point qui allume/éteint flou, inhibition de l'unredirect et sondage du pointeur.
    _applyPlan(reason) {
        if (!this._root || !this._perf)
            return;
        const p = computePlan({
            userHidden: this._userHidden,
            overviewHidden: this._overviewHidden,
            expanded: this._expanded,
            pinned: this._pinned,
            ...this._perf,
        });
        this._root.visible = p.shown;
        if (this._blurEffect.enabled !== p.blur)
            this._blurEffect.enabled = p.blur;
        if (p.inhibitUnredirect && !this._unredirectDisabled) {
            global.compositor.disable_unredirect();
            this._unredirectDisabled = true;
        } else if (!p.inhibitUnredirect && this._unredirectDisabled) {
            global.compositor.enable_unredirect();
            this._unredirectDisabled = false;
        }
        this._poller.apply(p);
        if (!p.poll)
            this._dwellSince = 0;
        if (!p.shown)
            this._cancelIdle(); // masqué : plus aucun minuteur d'ULTIMA côté interface
        this._plan = p;
        // Journal horodaté (journalctl) pour corréler avec les traces MangoHud du Chronométreur.
        const key = `visible=${+p.shown} flou=${+p.blur} unredirect-inhibé=${+p.inhibitUnredirect} ` +
            `sondage=${p.poll ? `${p.pollMs}ms` : 'non'}`;
        if (key !== this._planKey) {
            console.log(`${TAG} plan : ${key} (${reason}, ${this._expanded ? 'panneau' : 'rail'})`);
            this._planKey = key;
        }
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
        if (this._pinned && this._userHidden)
            this._userHidden = false;
        this._cancelIdle();
        this._setExpanded(this._pinned);
        this._applyPlan('épinglage'); // épinglé : sondage inutile -> suspendu
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
            this._applyView(); // rendu différé pendant que le panneau était replié
            this._content.ease({opacity: 255, duration: EXPAND_MS, mode: Clutter.AnimationMode.EASE_OUT_QUAD});
        } else {
            this._content.opacity = 0;
            this._content.visible = false;
        }
        this._applyPlan(expanded ? 'ouverture' : 'repli');
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
        step('suivi pointeur', () => this._poller.stop());
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
