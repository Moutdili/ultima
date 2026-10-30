// ULTIMA — extension GNOME Shell 50 : HUD en verre au-dessus du jeu plein écran (design v3).
//
// Données : EngineState publié par ultima-daemon dans $XDG_RUNTIME_DIR/ultima/state.json, et
// réglages du HUD v1.3 dans ~/.config/ultima/settings.json (LU, jamais écrit) ; relus SEULEMENT sur
// changement (Gio.FileMonitor + anti-rebond) ; JSON invalide -> dernier contenu valide gardé
// (state.js, hud.js) ; mise en page pure (render.js), réglages du HUD (hud.js), coûts (plan.js).
//
// Design (skill sts2-design-system §8, §11, §12) :
//   - repos : une simple bande de VERRE (aucune pastille, aucun texte, aucune icône) ;
//   - ouverture : la plaque glisse et s'élargit (ease-out 220 ms), puis le contenu apparaît en
//     fondu léger, ligne par ligne ; fermeture symétrique plus brève ; aucune boucle, aucun timer
//     JS par image (transitions implicites Clutter via actor.ease, environment.js) ;
//   - trois niveaux de verre (HudSettings.Quality) : eco (aplat), verre (flou + reflet GLSL),
//     loupe (verre + clone grossissant du jeu réfracté sur les bords) ; repli automatique si une
//     API manque (hud.js effectiveQuality) ; RIEN de coûteux replié ou masqué (plan.js).
//
// Couche (code GNOME Shell 50, resource:///org/gnome/shell/ui/layout.js) : addTopChrome() SANS
// trackFullscreen + global.compositor.disable_unredirect() tant que visible (réglable), comme
// osdWindow.js. Click-through : AUCUN acteur réactif ; survol = lecture de global.get_pointer()
// déclenchée par le signal `position-invalidated` du curseur (regroupé, zéro réveil pointeur
// immobile) ou, en repli (hover-source=poll), par une minuterie à 12,5 Hz (plan.js HoverWatch).
//
// Robustesse : toute erreur de enable() est attrapée et logguée ; disable() retire acteurs, effets,
// clone, raccourcis, timeouts, signaux, moniteur de fichier, et rend l'unredirect.

import Clutter from 'gi://Clutter';
import Cogl from 'gi://Cogl';
import Gio from 'gi://Gio';
import GLib from 'gi://GLib';
import Meta from 'gi://Meta';
import Pango from 'gi://Pango';
import Shell from 'gi://Shell';
import St from 'gi://St';

import * as Main from 'resource:///org/gnome/shell/ui/main.js';
import {Extension} from 'resource:///org/gnome/shell/extensions/extension.js';

import {PALETTE, MUTED_ALPHA, rgba, toneHex} from './palette.js';
import * as Geo from './geometry.js';
import {StateStore} from './state.js';
import {buildView, fitView, spansMarkup, viewToText, WRAP_COLS} from './render.js';
import {computePlan, HoverWatch} from './plan.js';
import {detectCaps, effectiveQuality, hudKey, parseSettingsHud, resolveHud} from './hud.js';
import {createLensEffect, createSheenEffect} from './effects.js';

const TAG = '[ULTIMA]';
const HOVER_DWELL_MS = 150;      // évite l'ouverture sur un simple passage du curseur
const IDLE_COLLAPSE_MS = 2500;   // docs/17 §4 : retour au rail après 2,5 s sans survol
const FILE_DEBOUNCE_MS = 100;    // rafales d'écriture (tmp + rename) -> une seule relecture

// Ouverture / fermeture (skill design §12). Durées dans la plage de docs/17 §7.4 (150-220 ms).
const OPEN_MS = 220;
const CLOSE_MS = 150;
const CONTENT_DELAY_MS = 110;    // le contenu commence quand le verre est presque ouvert
const FADE_IN_MS = 160;
const FADE_OUT_MS = 90;
const STAGGER_MS = 22;           // léger décalage par ligne
const STAGGER_MAX = 8;           // au-delà, les lignes arrivent ensemble (pas d'attente longue)
const LIFT_PX = 6;               // montée de chaque ligne pendant son fondu (pas de rebond)
const LENS_FADE_MS = 140;

const PAD_X = 16;                // marges généreuses (skill design §11)
const PAD_Y = 14;
const ROW_SPACING = 6;
const FALLBACK_LINE_PX = 19;

// Verre (docs/17 §3, skill design §8).
const TINT_ALPHA = {eco: 0.85, verre: 0.72, loupe: 0.72};
const BORDER_ALPHA = 0.18;       // liseré or du panneau (docs/17 §3)
const RAIL_EDGE_ALPHA = 0.85;    // liseré or du rail (leçon v1, commit fa00f8d : lisible au repos)
const RADIUS = 10;
const LENS_MAG = 1.06;           // grossissement de la loupe (plage demandée 1.04-1.10)

const KEY_PANEL = 'toggle-panel';
const KEY_VISIBLE = 'toggle-visible';
const HUD_KEYS = ['quality', 'height-fraction', 'rest-opacity', 'side'];
const PERF_KEYS = ['blur-mode', 'unredirect-mode', 'poll-interval-ms', 'hover-source'];

const toneOf = tone => (tone === 'muted'
    ? {hex: PALETTE.cream, alpha: MUTED_ALPHA}
    : {hex: toneHex(tone), alpha: 1});

class UltimaOverlay {
    constructor(settings, extPath) {
        this._settings = settings;
        this._extPath = extPath;
        this._timeouts = new Set();
        this._idleId = 0;
        this._dwellSince = 0;
        this._expanded = false;
        this._settled = false;       // plaque ouverte ET animation d'ouverture terminée
        this._pinned = false;
        this._hoverDetail = false;   // détail ouvert par survol de la carte de décision
        this._userHidden = false;
        this._overviewHidden = false;
        this._unredirectDisabled = false;
        this._keybindings = [];
        this._cancellable = new Gio.Cancellable();
        this._destroyed = false;
        this._layout = null;
        this._store = new StateStore();
        this._viewDirty = true;
        this._appliedKey = null;
        this._rowActors = [];
        this._cardActor = null;
        this._lineH = 0;
        this._lastKind = null;
        this._plan = null;
        this._planKey = null;
        this._perf = null;
        this._hud = null;
        this._hudKey = null;
        this._quality = null;
        this._lens = null;
        this._sheen = null;
        this._closing = false;       // verre encore ouvert pendant l'animation de fermeture
        this._fileHud = null;        // dernier « hud » valide lu dans settings.json
        this._files = [];
        this._caps = detectCaps({Shell, Clutter, Cogl});
        // PRIORITY_DEFAULT : en PRIORITY_LOW la source est affamée quand le compositeur redessine
        // en continu (jeu à 60+ FPS) — observé sur le banc headless.
        const named = (id, name) => {
            GLib.Source.set_name_by_id(id, name);
            return id;
        };
        const cursor = global.backend?.get_cursor_tracker?.() ?? null;
        this._hover = new HoverWatch({
            timers: {
                add: (ms, fn) => named(GLib.timeout_add(GLib.PRIORITY_DEFAULT, ms, () => {
                    fn();
                    return GLib.SOURCE_CONTINUE;
                }), '[ultima] pointer poll'),
                once: (ms, fn) => named(GLib.timeout_add(GLib.PRIORITY_DEFAULT, ms, () => {
                    fn();
                    return GLib.SOURCE_REMOVE;
                }), '[ultima] pointer read'),
                remove: id => GLib.source_remove(id),
            },
            tracker: cursor ? {
                connect: fn => cursor.connect('position-invalidated', fn),
                disconnect: id => cursor.disconnect(id),
            } : null,
        }, () => {
            try {
                this._tick();
            } catch (e) {
                console.error(`${TAG} tick : ${e}`);
            }
        });
    }

    start() {
        this._resolveHud('démarrage');
        this._build();
        Main.layoutManager.addTopChrome(this._root); // PAS de trackFullscreen (cf. en-tête)
        this._relayout();

        Main.layoutManager.connectObject('monitors-changed', () => {
            this._lineH = 0; // l'échelle a pu changer
            this._relayout();
        }, this);
        Main.overview.connectObject(
            'showing', () => this._setOverviewHidden(true),
            'hidden', () => this._setOverviewHidden(false), this);
        const hudChanged = () => this._resolveHud('réglage GSettings');
        const perfChanged = () => this._readPerfSettings();
        this._settings.connectObject(
            'changed::top-fraction', () => this._relayout(),
            ...HUD_KEYS.flatMap(k => [`changed::${k}`, hudChanged]),
            ...PERF_KEYS.flatMap(k => [`changed::${k}`, perfChanged]), this);

        this._overviewHidden = Main.overview.visible; // GNOME démarre dans la vue d'ensemble
        this._addKeybinding(KEY_PANEL, () => this._togglePinned());
        this._addKeybinding(KEY_VISIBLE, () => this._toggleVisible());

        this._watchFile(this._settingsPath(), text => this._acceptSettingsText(text),
            () => this._acceptSettingsText(null));
        this._watchFile(this._statePath(), text => this._acceptState(this._store.acceptText(text)),
            () => this._acceptState(this._store.acceptMissing()));
        this._readPerfSettings(); // applique aussi le plan initial
        const c = this._caps;
        console.log(`${TAG} actif : couche=addTopChrome, API flou=${+c.blur} glsl=${+c.glsl} clone=${+c.clone}, ` +
            `état=${this._statePath()} réglages=${this._settingsPath()}`);
    }

    // ---------- construction ----------

    _build() {
        // Racine : découpe la plaque (bande visible au repos), non réactive, jamais focusable.
        this._root = new St.Widget({
            name: 'ultima-root',
            reactive: false,
            can_focus: false,
            track_hover: false,
            clip_to_allocation: true,
        });

        // Plaque de verre : TOUJOURS à la taille du panneau ; seule sa translation change.
        this._plate = new St.Widget({name: 'ultima-plate', reactive: false});
        this._root.add_child(this._plate);

        // Couche 1 : flou de l'arrière-plan (acteur SANS enfant : aucun flou sur le texte).
        this._glass = new St.Widget({name: 'ultima-glass', reactive: false});
        if (this._caps.blur) {
            this._blurEffect = new Shell.BlurEffect({
                mode: Shell.BlurMode.BACKGROUND,
                radius: 24,
                brightness: 1.0,
            });
            this._blurEffect.enabled = false;
            this._glass.add_effect_with_name('ultima-blur', this._blurEffect);
        }
        // Couche 2 : teinte #192930 + liseré or 1 px + coins 10 + ombre (styles St, mis en cache).
        this._frame = new St.Widget({name: 'ultima-frame', reactive: false});
        // Couche 3 : liseré or appuyé du rail (leçon v1) ; s'efface en fondu à l'ouverture.
        this._edge = new St.Widget({name: 'ultima-edge', reactive: false});
        for (const a of [this._glass, this._frame, this._edge]) {
            a.add_constraint(new Clutter.BindConstraint({
                source: this._plate, coordinate: Clutter.BindCoordinate.SIZE,
            }));
            this._plate.add_child(a);
        }
        this._edge.set_style(`border: 1px solid ${rgba(PALETTE.gold, RAIL_EDGE_ALPHA)}; border-radius: ${RADIUS}px;`);

        // Couche 4 : contenu (une ligne par module, carte de décision). Préparé AVANT l'animation.
        this._content = new St.BoxLayout({
            orientation: Clutter.Orientation.VERTICAL,
            reactive: false,
            visible: false,
            x: PAD_X,
            y: PAD_Y,
            width: Geo.PANEL_W - 2 * PAD_X,
            style: `spacing: ${ROW_SPACING}px;`,
        });
        this._plate.add_child(this._content);
        this._applyFrameStyle();
    }

    _applyFrameStyle() {
        const q = this._quality?.level ?? 'eco';
        const style = `background-color: ${rgba(PALETTE.glassTint, TINT_ALPHA[q])};` +
            `border: 1px solid ${rgba(PALETTE.gold, BORDER_ALPHA)};` +
            `border-radius: ${RADIUS}px;` +
            `box-shadow: 0 2px 10px ${rgba(PALETTE.shadow, 0.35)};`;
        if (style !== this._frameStyle) {
            this._frame.set_style(style);
            this._frameStyle = style;
        }
    }

    _relayout() {
        const monitor = Main.layoutManager.primaryMonitor;
        if (!monitor || !this._root)
            return;
        this._layout = Geo.computeLayout(monitor, this._settings.get_double('top-fraction'),
            this._hud.heightFraction, this._hud.side);
        const L = this._layout;
        this._root.set_position(L.rootX, L.y);
        this._root.set_size(L.rootW, L.h);
        this._plate.remove_all_transitions();
        this._closing = false; // une fermeture interrompue n'appellera jamais son onComplete
        this._plate.set_position(L.plateX, 0);
        this._plate.set_size(Geo.PANEL_W, L.h);
        this._plate.translation_x = this._expanded ? 0 : L.collapsedDx;
        this._settled = this._expanded; // une ouverture interrompue est terminée d'office
        this._edge.opacity = this._expanded ? 0 : 255;
        this._root.opacity = this._restOpacity();
        this._content.height = L.h - 2 * PAD_Y;
        // Effets dépendant de la taille : recréés à la prochaine application du plan.
        this._dropSheen();
        this._dropLens();
        this._appliedKey = null;
        this._viewDirty = true;
        if (this._expanded)
            this._applyView(false);
        this._applyPlan('géométrie');
        const cov = Geo.coverage(L, monitor);
        console.log(`${TAG} géométrie : moniteur=${monitor.width}x${monitor.height} côté=${L.side} ` +
            `rail=(${L.railX},${L.y},${Geo.RAIL_W}x${L.h}) panneau=(${L.panelX},${L.y},${Geo.PANEL_W}x${L.h}) ` +
            `surface replié=${(cov.collapsed * 100).toFixed(2)}% déployé=${(cov.expanded * 100).toFixed(2)}%`);
    }

    _restOpacity() {
        return this._expanded ? 255 : Math.round(255 * this._hud.restOpacity);
    }

    // ---------- réglages du HUD (contrat v1.3, repli GSettings) ----------

    _gsettingsHud() {
        return {
            quality: this._settings.get_string('quality'),
            heightFraction: this._settings.get_double('height-fraction'),
            restOpacity: this._settings.get_double('rest-opacity'),
            side: this._settings.get_string('side'),
        };
    }

    _resolveHud(reason) {
        const cur = this._store.current;
        const stateHud = cur.kind === 'state' ? cur.state?.settings?.hud : null;
        const hud = resolveHud({file: this._fileHud, state: stateHud, gsettings: this._gsettingsHud()});
        const key = hudKey(hud);
        if (key === this._hudKey)
            return;
        const prev = this._hud;
        this._hud = hud;
        this._hudKey = key;
        this._quality = effectiveQuality(hud.quality, this._caps);
        const q = this._quality;
        const from = Object.entries(hud.source).filter(([, v]) => v !== 'défaut').map(([k, v]) => `${k}<-${v}`);
        console.log(`${TAG} HUD (${reason}) : ${key} ; verre effectif=${q.level}` +
            (from.length ? ` ; sources ${from.join(' ')}` : ' ; défauts du contrat') +
            (q.missing.length ? ` (repli : API absente ${q.missing.join(', ')})` : ''));
        if (!this._root)
            return;
        this._applyFrameStyle();
        this._dropSheen(); // le reflet dépend du niveau (bande de réfraction en loupe)
        this._viewDirty = true;
        this._appliedKey = null;
        if (!prev || prev.side !== hud.side || prev.heightFraction !== hud.heightFraction) {
            this._relayout();
        } else {
            this._root.opacity = this._restOpacity();
            if (this._expanded)
                this._applyView(false);
            this._applyPlan('HUD');
        }
    }

    // ---------- fichiers : état publié par le daemon, réglages du HUD ----------

    _statePath() {
        return GLib.build_filenamev([GLib.get_user_runtime_dir(), 'ultima', 'state.json']);
    }

    _settingsPath() {
        return GLib.build_filenamev([GLib.get_user_config_dir(), 'ultima', 'settings.json']);
    }

    // Relecture SEULEMENT sur changement : aucun minuteur périodique côté extension. Un fichier
    // absent (ou dont le dossier n'existe pas encore) est surveillé quand même (inotify sur le
    // parent, géré par GLib) ; il est lu dès qu'il apparaît.
    _watchFile(path, onText, onMissing) {
        const w = {path, timer: 0, seq: 0, monitor: null, onText, onMissing};
        this._files.push(w);
        const file = Gio.File.new_for_path(path);
        try {
            w.monitor = file.monitor_file(Gio.FileMonitorFlags.NONE, this._cancellable);
            w.monitor.connectObject('changed', (_m, _f, _o, event) => {
                if (event === Gio.FileMonitorEvent.ATTRIBUTE_CHANGED ||
                    event === Gio.FileMonitorEvent.PRE_UNMOUNT ||
                    event === Gio.FileMonitorEvent.UNMOUNTED)
                    return;
                this._queueLoad(w);
            }, this);
        } catch (e) {
            console.warn(`${TAG} pas de surveillance de ${path} : ${e.message}`);
        }
        this._load(w);
    }

    _queueLoad(w) {
        if (w.timer)
            return;
        w.timer = this._addTimeout(FILE_DEBOUNCE_MS, () => {
            w.timer = 0;
            this._load(w);
        });
    }

    _load(w) {
        const seq = ++w.seq;
        const file = Gio.File.new_for_path(w.path);
        file.load_contents_async(this._cancellable, (f, result) => {
            if (this._destroyed || seq !== w.seq)
                return; // une relecture plus récente est en cours : elle fera foi
            let text;
            try {
                const [, bytes] = f.load_contents_finish(result);
                text = new TextDecoder().decode(bytes);
            } catch (e) {
                if (e.matches?.(Gio.IOErrorEnum, Gio.IOErrorEnum.CANCELLED))
                    return;
                if (e.matches?.(Gio.IOErrorEnum, Gio.IOErrorEnum.NOT_FOUND)) {
                    w.onMissing();
                    return;
                }
                console.warn(`${TAG} lecture ${f.get_path()} : ${e.message}`);
                return; // dernier contenu valide conservé
            }
            w.onText(text);
        });
    }

    // settings.json : seul le bloc « hud » sert ici (l'ordre des modules vient de l'état publié).
    _acceptSettingsText(text) {
        if (text === null) {
            this._settingsError = null;
            this._fileHud = null;
        } else {
            const r = parseSettingsHud(text);
            if (r.kind === 'invalid') {
                if (r.error !== this._settingsError)
                    console.warn(`${TAG} réglages rejetés (derniers réglages valides conservés) : ${r.error}`);
                this._settingsError = r.error;
                return;
            }
            this._settingsError = null;
            this._fileHud = r.kind === 'hud' ? r.hud : null;
        }
        this._resolveHud('settings.json');
    }

    _acceptState(res) {
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
        this._resolveHud('état');
        this._viewDirty = true;
        if (this._expanded)
            this._applyView(false); // replié : rendu différé jusqu'à l'ouverture
    }

    // ---------- contenu ----------

    _lineHeight() {
        if (this._lineH > 0)
            return this._lineH;
        const probe = new St.Label({text: 'Ag', style: 'font-size: 14px;'});
        this._content.add_child(probe);
        const [, nat] = probe.get_preferred_height(-1);
        probe.destroy();
        this._lineH = nat > 4 ? nat + ROW_SPACING : FALLBACK_LINE_PX;
        console.log(`${TAG} hauteur de ligne mesurée : ${this._lineH.toFixed(1)} px`);
        return this._lineH;
    }

    _detail() {
        return this._pinned || this._hoverDetail;
    }

    // Construit/actualise les lignes (panneau ouvert seulement). stagger : fondu ligne par ligne
    // (ouverture) ; sinon mise à jour directe, sans animation (pas de « spectacle », docs/17 §9).
    _applyView(stagger) {
        if (!this._viewDirty && !stagger)
            return;
        this._viewDirty = false;
        const view = buildView(this._store.current, {hud: this._hud, detail: this._detail()});
        const maxLines = Math.max(6, Math.floor(this._content.height / this._lineHeight()));
        const fitted = fitView(view, {maxLines, cols: WRAP_COLS});
        const key = `${this._detail()}\u0000${viewToText(fitted)}\u0000` +
            fitted.rows.map(r => `${r.key}:${r.gradeTone ?? ''}:${r.spans.map(s => s.tone).join('/')}`).join(',');
        if (key !== this._appliedKey) {
            this._appliedKey = key;
            this._rebuildRows(fitted.rows);
        }
        if (stagger)
            this._staggerIn();
    }

    _rebuildRows(rows) {
        const sameShape = this._rowActors.length === rows.length &&
            this._rowActors.every((a, i) => a._ultimaKey === rows[i].key);
        if (!sameShape) {
            this._content.destroy_all_children();
            this._cardActor = null;
            this._rowActors = rows.map(r => this._makeRow(r));
        }
        rows.forEach((r, i) => this._fillRow(this._rowActors[i], r));
    }

    _label(px, extra = '') {
        const l = new St.Label({reactive: false, x_expand: true, style: `font-size: ${px}px;${extra}`});
        const ct = l.clutter_text;
        ct.line_wrap = true;
        ct.line_wrap_mode = Pango.WrapMode.WORD_CHAR;
        ct.ellipsize = Pango.EllipsizeMode.NONE;
        return l;
    }

    _makeRow(r) {
        let actor;
        if (r.kind === 'decision') {
            // Carte de décision : la lettre domine (44 px), puis le choix et une raison.
            actor = new St.BoxLayout({reactive: false, style: 'spacing: 12px; padding-bottom: 6px;'});
            const grade = new St.Label({reactive: false, style: 'font-size: 44px; font-weight: bold; min-width: 34px;'});
            const col = new St.BoxLayout({orientation: Clutter.Orientation.VERTICAL, reactive: false, x_expand: true,
                style: 'spacing: 2px; padding-top: 8px;'});
            const head = this._label(16);
            const reason = this._label(14);
            col.add_child(head);
            col.add_child(reason);
            actor.add_child(grade);
            actor.add_child(col);
            actor._ultima = {grade, head, reason};
            this._cardActor = actor;
        } else {
            actor = this._label(14, r.kind === 'detail' || r.kind === 'more' ? ' padding-left: 10px;' : '');
            actor._ultima = {label: actor};
        }
        actor._ultimaKey = r.key;
        this._content.add_child(actor);
        return actor;
    }

    _fillRow(actor, r) {
        const u = actor._ultima;
        if (r.kind === 'decision') {
            setText(u.grade, r.grade, `color: ${toneHex(r.gradeTone)};`);
            setMarkup(u.head, spansMarkup(r.spans, toneOf));
            u.reason.visible = !!r.reason;
            if (r.reason)
                setMarkup(u.reason, spansMarkup(r.reason, toneOf));
        } else {
            setMarkup(u.label, spansMarkup(r.spans, toneOf));
        }
    }

    _staggerIn() {
        this._rowActors.forEach((a, i) => {
            a.remove_all_transitions();
            a.opacity = 0;
            a.translation_y = LIFT_PX;
            a.ease({
                opacity: 255,
                translation_y: 0,
                delay: CONTENT_DELAY_MS + Math.min(i, STAGGER_MAX) * STAGGER_MS,
                duration: FADE_IN_MS,
                mode: Clutter.AnimationMode.EASE_OUT_QUAD,
            });
        });
    }

    // ---------- visibilité, coûts (plan.js), effets ----------

    _readPerfSettings() {
        this._perf = {
            blurMode: this._settings.get_string('blur-mode'),
            unredirectMode: this._settings.get_string('unredirect-mode'),
            pollMs: this._settings.get_int('poll-interval-ms'),
            hoverSource: this._settings.get_string('hover-source'),
        };
        console.log(`${TAG} réglages perf : blur-mode=${this._perf.blurMode} ` +
            `unredirect-mode=${this._perf.unredirectMode} poll-interval-ms=${this._perf.pollMs} ` +
            `hover-source=${this._perf.hoverSource}`);
        this._applyPlan('réglage');
    }

    _setOverviewHidden(v) {
        this._overviewHidden = v;
        this._applyPlan(v ? 'vue d\'ensemble' : 'retour');
    }

    _toggleVisible() {
        this._userHidden = !this._userHidden;
        console.log(`${TAG} raccourci ${KEY_VISIBLE} -> ${this._userHidden ? 'masqué' : 'visible'}`);
        if (this._userHidden && this._expanded && !this._pinned)
            this._setExpanded(false, {instant: true});
        this._applyPlan('raccourci');
    }

    // Seul point qui allume/éteint flou, reflet, loupe, inhibition de l'unredirect et sondage.
    _applyPlan(reason) {
        if (!this._root || !this._perf || !this._layout)
            return;
        const p = computePlan({
            userHidden: this._userHidden,
            overviewHidden: this._overviewHidden,
            expanded: this._expanded || this._closing,
            pinned: this._pinned,
            quality: this._quality.level,
            ...this._perf,
        });
        this._root.visible = p.shown;
        if (this._blurEffect) {
            if (p.blur && this._blurEffect.radius !== p.blurRadius)
                this._blurEffect.radius = p.blurRadius;
            if (this._blurEffect.enabled !== p.blur)
                this._blurEffect.enabled = p.blur;
        }
        this._setSheen(p.sheen);
        // La loupe n'apparaît qu'une fois la plaque immobile (alignement exact du clone).
        this._setLens(p.lens && this._settled);
        if (p.inhibitUnredirect && !this._unredirectDisabled) {
            global.compositor.disable_unredirect();
            this._unredirectDisabled = true;
        } else if (!p.inhibitUnredirect && this._unredirectDisabled) {
            global.compositor.enable_unredirect();
            this._unredirectDisabled = false;
        }
        this._hover.apply(p);
        if (!p.poll)
            this._dwellSince = 0;
        if (!p.shown)
            this._cancelIdle(); // masqué : plus aucun minuteur d'ULTIMA côté interface
        this._plan = p;
        // Journal horodaté (journalctl) pour corréler avec les traces MangoHud du Chronométreur.
        const key = `visible=${+p.shown} verre=${p.quality} flou=${p.blur ? p.blurRadius : 0} ` +
            `reflet=${+p.sheen} loupe=${+(p.lens && this._settled)} unredirect-inhibé=${+p.inhibitUnredirect} ` +
            `survol=${p.poll ? `${p.hoverSource}/${p.pollMs}ms` : 'non'}`;
        if (key !== this._planKey) {
            const where = this._expanded ? 'panneau' : this._closing ? 'fermeture' : 'rail';
            console.log(`${TAG} plan : ${key} (${reason}, ${where})`);
            this._planKey = key;
        }
    }

    _setSheen(on) {
        if (on && !this._sheen) {
            this._sheen = createSheenEffect({width: Geo.PANEL_W, height: this._layout.h,
                rim: this._quality.level === 'loupe' ? 1 : 0});
            this._frame.add_effect_with_name('ultima-sheen', this._sheen);
        }
        if (this._sheen && this._sheen.enabled !== on)
            this._sheen.enabled = on;
    }

    _dropSheen() {
        if (this._sheen) {
            this._frame.remove_effect(this._sheen);
            this._sheen = null;
        }
    }

    // Source de la loupe : l'acteur de la fenêtre du jeu (fenêtre active plein écran) — une seule
    // texture à repeindre ; sinon global.window_group (fenêtres + fond, jamais le chrome du Shell,
    // donc jamais ULTIMA lui-même). API : Meta.Display.get_focus_window, Meta.Window.is_fullscreen,
    // get_compositor_private (Meta-18.typelib).
    _lensSource() {
        const w = global.display?.get_focus_window?.();
        const actor = w?.is_fullscreen?.() ? w.get_compositor_private?.() : null;
        return actor ?? global.window_group;
    }

    // Loupe : Clutter.Clone de la source dans un conteneur découpé à la taille de la plaque, grossi
    // autour du centre du panneau, puis réfracté sur les bords (effects.js). Modèle :
    // ui/magnifier.js (Clone + clip_to_allocation + scale + position). Coût borné : le clone n'est
    // peint que dans un tampon de la taille du panneau, et n'existe que panneau ouvert et immobile.
    _setLens(on) {
        if (on && !this._lens) {
            const L = this._layout;
            const m = LENS_MAG;
            const source = this._lensSource();
            const lens = new Clutter.Actor({name: 'ultima-lens', reactive: false, clip_to_allocation: true,
                width: Geo.PANEL_W, height: L.h, opacity: 0});
            const clone = new Clutter.Clone({source, reactive: false});
            // Le point c (centre du panneau, coordonnées du stage) reste fixe : un point p de la
            // source (origine a sur le stage) s'affiche en c + m·(p − c) -> position du clone dans la
            // loupe = (1 − m)·c + m·a − origine de la loupe.
            const [ax, ay] = source.get_transformed_position?.() ?? [0, 0];
            const cx = L.panelX + Geo.PANEL_W / 2, cy = L.y + L.h / 2;
            clone.set_position(Math.round((1 - m) * cx + m * ax - L.panelX),
                Math.round((1 - m) * cy + m * ay - L.y));
            clone.set_scale(m, m);
            lens.add_child(clone);
            lens.add_effect_with_name('ultima-lens', createLensEffect({width: Geo.PANEL_W, height: L.h, radius: RADIUS}));
            this._plate.insert_child_below(lens, this._glass);
            lens.ease({opacity: 255, duration: LENS_FADE_MS, mode: Clutter.AnimationMode.EASE_OUT_QUAD});
            this._lens = lens;
        } else if (!on) {
            this._dropLens();
        }
    }

    _dropLens() {
        if (this._lens) {
            this._lens.remove_all_transitions();
            this._lens.destroy(); // détruit aussi le clone et son effet : plus aucune peinture du jeu
            this._lens = null;
        }
    }

    // ---------- survol ----------

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
                const waited = now - this._dwellSince;
                if (waited >= HOVER_DWELL_MS)
                    this._setExpanded(true);
                else
                    this._hover.recheck(HOVER_DWELL_MS - waited); // pointeur immobile sur le rail
            } else if (!this._hoverDetail && this._settled && this._overCard(px, py)) {
                // Survol de la carte de décision : le détail reste ouvert jusqu'au repli
                // (on peut descendre le lire sans qu'il disparaisse).
                this._hoverDetail = true;
                this._viewDirty = true;
                this._applyView(false);
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

    _overCard(px, py) {
        const a = this._cardActor;
        if (!a || a.destroyed)
            return false;
        const [x, y] = a.get_transformed_position();
        const [w, h] = a.get_transformed_size();
        return px >= x && px < x + w && py >= y && py < y + h;
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
        this._viewDirty = true; // détail affiché / retiré
        if (this._expanded === this._pinned)
            this._applyView(false);
        else
            this._setExpanded(this._pinned);
        this._applyPlan('épinglage'); // épinglé : sondage inutile -> suspendu
    }

    // Ouverture : la plaque glisse (ease-out 220 ms), le contenu, déjà préparé, apparaît en fondu
    // ligne par ligne. Fermeture : le contenu s'efface (90 ms), la plaque rentre (150 ms).
    // Transitions implicites Clutter uniquement, une seule fois, jamais en boucle (docs/17 §6).
    _setExpanded(expanded, {instant = false} = {}) {
        if (this._expanded === expanded || !this._layout)
            return;
        this._expanded = expanded;
        this._settled = false;
        this._closing = false;
        this._dwellSince = 0;
        const L = this._layout;
        for (const a of [this._plate, this._edge, this._root, this._content])
            a.remove_all_transitions();
        if (expanded) {
            this._content.opacity = 255;
            this._content.visible = true;
            this._applyView(true); // contenu préparé AVANT l'animation (pas de relayout pendant)
            this._applyPlan('ouverture');
            const mode = Clutter.AnimationMode.EASE_OUT_CUBIC;
            this._edge.ease({opacity: 0, duration: OPEN_MS, mode});
            this._root.ease({opacity: 255, duration: OPEN_MS, mode});
            this._plate.ease({
                translation_x: 0, duration: OPEN_MS, mode,
                onComplete: () => {
                    if (!this._expanded || this._destroyed)
                        return;
                    this._settled = true;
                    this._applyPlan('ouvert');
                },
            });
        } else {
            this._hoverDetail = false;
            // Le flou et le reflet restent pendant la fermeture (pas de saut visuel) ; la loupe
            // part tout de suite (elle n'est alignée que plaque immobile). Tout est coupé à la fin.
            this._closing = !instant;
            this._applyPlan('repli');
            const rest = Math.round(255 * this._hud.restOpacity);
            if (instant) {
                this._content.visible = false;
                this._plate.translation_x = L.collapsedDx;
                this._edge.opacity = 255;
                this._root.opacity = rest;
                return;
            }
            const mode = Clutter.AnimationMode.EASE_IN_CUBIC;
            this._content.ease({
                opacity: 0, duration: FADE_OUT_MS, mode,
                onComplete: () => {
                    if (!this._expanded && !this._destroyed)
                        this._content.visible = false;
                },
            });
            this._edge.ease({opacity: 255, duration: CLOSE_MS, mode});
            this._root.ease({opacity: rest, duration: CLOSE_MS, mode});
            this._plate.ease({
                translation_x: L.collapsedDx, duration: CLOSE_MS, mode,
                onComplete: () => {
                    if (this._expanded || this._destroyed || !this._closing)
                        return;
                    this._closing = false;
                    this._applyPlan('replié'); // rail = aplat : plus aucun effet
                },
            });
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
        step('suivi pointeur', () => this._hover.stop());
        step('timeouts', () => {
            for (const id of this._timeouts)
                GLib.source_remove(id);
            this._timeouts.clear();
            this._idleId = 0;
            for (const w of this._files)
                w.timer = 0;
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
        step('moniteurs de fichier', () => {
            for (const w of this._files) {
                w.monitor?.disconnectObject(this);
                w.monitor?.cancel();
                w.monitor = null;
            }
            this._files = [];
        });
        step('unredirect', () => {
            if (this._unredirectDisabled) {
                global.compositor.enable_unredirect();
                this._unredirectDisabled = false;
            }
        });
        step('loupe et reflet', () => {
            this._dropLens();
            this._dropSheen();
        });
        step('acteurs', () => {
            if (this._root) {
                for (const a of [this._plate, this._edge, this._root, this._content])
                    a?.remove_all_transitions();
                Main.layoutManager.removeChrome(this._root);
                this._root.destroy();
                this._root = null;
            }
        });
        console.log(`${TAG} désactivé, tout nettoyé`);
    }
}

function setText(label, text, style) {
    if (label.text !== text)
        label.text = text;
    if (label._ultimaStyle !== style) {
        label.set_style(`font-size: 44px; font-weight: bold; min-width: 34px; ${style}`);
        label._ultimaStyle = style;
    }
}

function setMarkup(label, markup) {
    if (label._ultimaMarkup === markup)
        return;
    label._ultimaMarkup = markup;
    label.clutter_text.set_markup(markup);
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
