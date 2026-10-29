// Bouchons minimaux de GNOME Shell 50 (Clutter, St, Gio, GLib, Shell, Meta, Pango, Main, global)
// pour exécuter ultima@mio/extension.js sous Node avec une HORLOGE VIRTUELLE.
// Ils n'imitent que ce que l'extension appelle ; un appel inconnu lève (attrape les fautes de frappe).
import fs from 'node:fs';
import path from 'node:path';

// ---------- signaux ----------
class Signals {
    constructor() {
        this._h = [];
    }

    connectObject(...args) {
        const owner = args.pop();
        for (let i = 0; i < args.length; i += 2)
            this._h.push({name: args[i], fn: args[i + 1], owner});
    }

    disconnectObject(owner) {
        this._h = this._h.filter(h => h.owner !== owner);
    }

    emit(name, ...a) {
        for (const h of [...this._h])
            if (h.name === name)
                h.fn(this, ...a);
    }

    get handlerCount() {
        return this._h.length;
    }
}

// ---------- horloge virtuelle (GLib main loop) ----------
export const clock = {
    now: 0, next: 1, sources: new Map(), names: new Map(), fired: new Map(),
    add(ms, fn) {
        const id = this.next++;
        this.sources.set(id, {ms, fn, due: this.now + ms});
        return id;
    },
    remove(id) {
        if (!this.sources.delete(id))
            throw new Error(`g_source_remove : source ${id} inconnue`);
        this.names.delete(id);
    },
    advance(ms) {
        const end = this.now + ms;
        for (;;) {
            let best = null;
            for (const [id, s] of this.sources)
                if (s.due <= end && (!best || s.due < best[1].due))
                    best = [id, s];
            if (!best)
                break;
            const [id, s] = best;
            this.now = s.due;
            const name = this.names.get(id) ?? '(sans nom)';
            this.fired.set(name, (this.fired.get(name) ?? 0) + 1);
            const keep = s.fn();
            if (keep && this.sources.has(id))
                s.due += s.ms;
            else
                this.sources.delete(id), this.names.delete(id);
        }
        this.now = end;
    },
    activeNamed(name) {
        return [...this.names.values()].filter(n => n === name).length;
    },
};

const GLib = {
    PRIORITY_DEFAULT: 0, PRIORITY_LOW: 300, SOURCE_CONTINUE: true, SOURCE_REMOVE: false,
    build_filenamev: parts => path.join(...parts),
    get_user_runtime_dir: () => process.env.XDG_RUNTIME_DIR,
    get_monotonic_time: () => clock.now * 1000,
    timeout_add: (_prio, ms, fn) => clock.add(ms, fn),
    source_remove: id => clock.remove(id),
    Source: {set_name_by_id: (id, name) => clock.names.set(id, name)},
};

// ---------- Gio : fichiers réels (dossier temporaire), moniteur piloté par le test ----------
const IOErrorEnum = {NOT_FOUND: 1, CANCELLED: 19, FAILED: 0};
class GioError extends Error {
    constructor(code, msg) {
        super(msg);
        this.code = code;
    }

    matches(domain, code) {
        return domain === IOErrorEnum && code === this.code;
    }
}
export const monitors = new Map(); // chemin -> moniteur
export const io = {reads: 0};
class Cancellable {
    constructor() {
        this.cancelled = false;
    }

    cancel() {
        this.cancelled = true;
    }
}
class FileMonitor extends Signals {
    constructor(p) {
        super();
        this.path = p;
        this.cancelled = false;
    }

    cancel() {
        this.cancelled = true;
    }
}
class File {
    constructor(p) {
        this._p = p;
    }

    static new_for_path(p) {
        return new File(p);
    }

    get_path() {
        return this._p;
    }

    monitor_file(_flags, _canc) {
        const m = new FileMonitor(this._p);
        monitors.set(this._p, m);
        return m;
    }

    load_contents_async(canc, cb) {
        io.reads++;
        setImmediate(() => cb(this, {canc}));
    }

    load_contents_finish(result) {
        if (result.canc?.cancelled)
            throw new GioError(IOErrorEnum.CANCELLED, 'annulé');
        try {
            return [true, new Uint8Array(fs.readFileSync(this._p))];
        } catch (e) {
            if (e.code === 'ENOENT')
                throw new GioError(IOErrorEnum.NOT_FOUND, `${this._p} : introuvable`);
            throw new GioError(IOErrorEnum.FAILED, e.message);
        }
    }
}
const Gio = {
    File, Cancellable, IOErrorEnum,
    FileMonitorFlags: {NONE: 0, WATCH_MOVES: 8},
    FileMonitorEvent: {CHANGED: 0, CHANGES_DONE_HINT: 1, DELETED: 2, CREATED: 3, ATTRIBUTE_CHANGED: 4,
        PRE_UNMOUNT: 5, UNMOUNTED: 6, MOVED: 7, RENAMED: 8, MOVED_IN: 9, MOVED_OUT: 10},
};

// ---------- acteurs ----------
const ACTOR_PROPS = new Set(['name', 'reactive', 'can_focus', 'track_hover', 'clip_to_allocation', 'orientation',
    'style', 'opacity', 'visible', 'width', 'height', 'x', 'y', 'text', 'x_expand']);
class Actor {
    constructor(props = {}) {
        this.visible = true;
        this.opacity = 255;
        this.x = this.y = this.width = this.height = 0;
        this.children = [];
        this.effects = new Map();
        this.parent = null;
        this.destroyed = false;
        for (const [k, v] of Object.entries(props)) {
            if (!ACTOR_PROPS.has(k))
                throw new Error(`propriété d'acteur inattendue : ${k}`);
            this[k] = v;
        }
    }

    add_child(c) {
        this.children.push(c);
        c.parent = this;
    }

    destroy_all_children() {
        for (const c of this.children)
            c.destroyed = true;
        this.children = [];
    }

    destroy() {
        this.destroyed = true;
        if (this.parent)
            this.parent.children = this.parent.children.filter(c => c !== this);
    }

    add_constraint() {}
    add_effect_with_name(n, e) {
        this.effects.set(n, e);
    }

    set_style(s) {
        this.style = s;
    }

    set_position(x, y) {
        this.x = x;
        this.y = y;
    }

    set_size(w, h) {
        this.width = w;
        this.height = h;
    }

    remove_all_transitions() {}
    ease(p) {
        for (const k of ['x', 'y', 'width', 'height', 'opacity'])
            if (k in p)
                this[k] = p[k];
        p.onComplete?.();
    }

    get_preferred_height() {
        return [17, 17]; // 14 px à l'échelle 1 : valeur plausible, la vraie est mesurée en session
    }
}
class Label extends Actor {
    constructor(props = {}) {
        super(props);
        this.clutter_text = {};
        if (this.text === undefined)
            this.text = '';
    }
}
const St = {Widget: Actor, BoxLayout: Actor, Label};
const Clutter = {
    BindConstraint: class {}, BindCoordinate: {SIZE: 4},
    Orientation: {VERTICAL: 1}, AnimationMode: {EASE_OUT_QUAD: 5},
};
class BlurEffect {
    constructor(p) {
        Object.assign(this, p);
        this.enabled = true;
    }
}
const Shell = {BlurEffect, BlurMode: {BACKGROUND: 1}, ActionMode: {NORMAL: 1, OVERVIEW: 2}};
const Meta = {KeyBindingFlags: {IGNORE_AUTOREPEAT: 1, NON_MASKABLE: 2}, KeyBindingAction: {NONE: 0}};
const Pango = {WrapMode: {WORD_CHAR: 2}, EllipsizeMode: {NONE: 0}};
export const gi = {Clutter, Gio, GLib, Meta, Pango, Shell, St};

// ---------- GSettings ----------
export class Settings extends Signals {
    constructor(values) {
        super();
        this.values = {...values};
    }

    _get(k) {
        if (!(k in this.values))
            throw new Error(`clé GSettings inconnue : ${k}`);
        return this.values[k];
    }

    get_double(k) {
        return this._get(k);
    }

    get_string(k) {
        return this._get(k);
    }

    get_int(k) {
        return this._get(k);
    }

    get_strv(k) {
        return this._get(k);
    }

    set(k, v) {
        this._get(k);
        this.values[k] = v;
        this.emit(`changed::${k}`, k);
    }
}

// ---------- Main, global ----------
const layoutManager = new Signals();
layoutManager.primaryMonitor = {x: 0, y: 0, width: 1536, height: 960}; // écran de Mio, pixels logiques
layoutManager.chrome = [];
layoutManager.addTopChrome = a => layoutManager.chrome.push(a);
layoutManager.removeChrome = a => {
    layoutManager.chrome = layoutManager.chrome.filter(x => x !== a);
};
const overview = new Signals();
overview.visible = false;
const wm = {
    bindings: new Map(),
    addKeybinding(name, _s, _f, _m, handler) {
        this.bindings.set(name, handler);
        return 42;
    },
    removeKeybinding(name) {
        this.bindings.delete(name);
    },
};
export const Main = {layoutManager, overview, wm};

export const compositor = {
    inhibit: 0, calls: [],
    disable_unredirect() {
        this.inhibit++;
        this.calls.push('disable');
    },
    enable_unredirect() {
        this.inhibit--;
        this.calls.push('enable');
    },
};
export const pointer = {x: 10, y: 10, reads: 0};
globalThis.global = {
    compositor,
    get_pointer() {
        pointer.reads++;
        return [pointer.x, pointer.y, 0];
    },
};

export const shared = {settings: null};
export class Extension {
    constructor(dir) {
        this.path = dir;
    }

    getSettings() {
        return shared.settings;
    }
}
