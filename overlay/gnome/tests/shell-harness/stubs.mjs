// Bouchons minimaux de GNOME Shell 50 (Clutter, St, Gio, GLib, Shell, Meta, Pango, Main, global)
// pour exécuter ultima@mio/extension.js sous Node avec une HORLOGE VIRTUELLE.
// Ils n'imitent que ce que l'extension appelle ; un appel inconnu lève (attrape les fautes de frappe).
import fs from 'node:fs';
import path from 'node:path';

// ---------- activité : toute écriture visible sur un acteur ou un effet (preuve « rien au repos ») ----------
// Chaque changement de propriété affichable, de style, de texte, chaque ease(), ajout/retrait
// d'enfant ou d'effet, chaque bascule d'effet incrémente activity.count (et note quoi dans log).
export const activity = {count: 0, log: [],
    touch(what) {
        this.count++;
        this.log.push(what);
        if (this.log.length > 200)
            this.log.shift();
    }};

// ---------- signaux ----------
class Signals {
    constructor() {
        this._h = [];
        this._nextId = 1;
    }

    // GObject : connect(name, fn) -> id ; disconnect(id).
    connect(name, fn) {
        const id = this._nextId++;
        this._h.push({name, fn, owner: null, id});
        return id;
    }

    disconnect(id) {
        const n = this._h.length;
        this._h = this._h.filter(h => h.id !== id);
        if (this._h.length === n)
            throw new Error(`disconnect : connexion ${id} inconnue`);
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
    get_user_config_dir: () => process.env.XDG_CONFIG_HOME,
    get_user_data_dir: () => process.env.XDG_DATA_HOME ?? path.join(process.env.HOME ?? '/tmp', '.local', 'share'),
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

    get_child(name) {
        return new File(path.join(this._p, name));
    }

    query_exists(_canc) {
        return fs.existsSync(this._p);
    }

    // Énumération synchrone (comme g_file_enumerate_children) : dossier absent -> lève, comme Gio.
    enumerate_children(_attrs, _flags, _canc) {
        if (!fs.existsSync(this._p) || !fs.statSync(this._p).isDirectory())
            throw new GioError(IOErrorEnum.NOT_FOUND, `${this._p} : dossier absent`);
        const names = fs.readdirSync(this._p);
        let i = 0;
        return {
            next_file: () => (i < names.length ? {get_name: () => names[i++]} : null),
            close: () => {},
        };
    }

    load_contents(_canc) {
        try {
            return [true, new Uint8Array(fs.readFileSync(this._p))];
        } catch (e) {
            throw new GioError(e.code === 'ENOENT' ? IOErrorEnum.NOT_FOUND : IOErrorEnum.FAILED, e.message);
        }
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
    FileQueryInfoFlags: {NONE: 0, NOFOLLOW_SYMLINKS: 4},
    FileMonitorFlags: {NONE: 0, WATCH_MOVES: 8},
    FileMonitorEvent: {CHANGED: 0, CHANGES_DONE_HINT: 1, DELETED: 2, CREATED: 3, ATTRIBUTE_CHANGED: 4,
        PRE_UNMOUNT: 5, UNMOUNTED: 6, MOVED: 7, RENAMED: 8, MOVED_IN: 9, MOVED_OUT: 10},
};

// ---------- acteurs ----------
// Animations : ease() applique la valeur finale et ENREGISTRE l'appel (durée, délai, mode) dans
// actor.eased ; onComplete est appelé tout de suite, ou mis en file si anim.defer = true
// (le test vide la file avec anim.flush() pour simuler la fin de l'animation).
export const anim = {defer: false, queue: [], log: [],
    flush() {
        const q = this.queue;
        this.queue = [];
        for (const f of q)
            f();
    }};
const ACTOR_PROPS = new Set(['name', 'reactive', 'can_focus', 'track_hover', 'clip_to_allocation', 'orientation',
    'style', 'opacity', 'visible', 'width', 'height', 'x', 'y', 'text', 'x_expand', 'source']);
// Propriétés affichables suivies par `activity` (accesseurs sur le prototype).
const TRACKED = ['opacity', 'visible', 'x', 'y', 'width', 'height', 'translation_x', 'translation_y',
    'scale_x', 'scale_y', 'text', 'style'];
class Actor {
    constructor(props = {}) {
        this._v = {};
        this.visible = true;
        this.opacity = 255;
        this.x = this.y = this.width = this.height = 0;
        this.translation_x = this.translation_y = 0;
        this.scale_x = this.scale_y = 1;
        this.children = [];
        this.effects = new Map();
        this.parent = null;
        this.destroyed = false;
        this.eased = [];
        for (const [k, v] of Object.entries(props)) {
            if (!ACTOR_PROPS.has(k))
                throw new Error(`propriété d'acteur inattendue : ${k}`);
            this[k] = v;
        }
        this._live = true;
        activity.touch(`new ${this.name ?? this.constructor.name}`);
    }

    add_child(c) {
        this.children.push(c);
        c.parent = this;
        activity.touch(`add_child ${c.name ?? c.constructor.name}`);
    }

    insert_child_below(c, sibling) {
        const i = this.children.indexOf(sibling);
        if (i < 0)
            throw new Error('insert_child_below : voisin absent');
        this.children.splice(i, 0, c);
        c.parent = this;
    }

    destroy_all_children() {
        for (const c of this.children)
            c.destroy({fromParent: true});
        this.children = [];
    }

    destroy({fromParent = false} = {}) {
        this.destroyed = true;
        activity.touch(`destroy ${this.name ?? this.constructor.name}`);
        for (const c of this.children)
            c.destroy({fromParent: true});
        this.children = [];
        if (this.parent && !fromParent)
            this.parent.children = this.parent.children.filter(c => c !== this);
        this.parent = null;
    }

    add_constraint() {}
    add_effect_with_name(n, e) {
        if (this.effects.has(n))
            throw new Error(`effet déjà présent : ${n}`);
        this.effects.set(n, e);
        e.actor = this;
        activity.touch(`add_effect ${n}`);
    }

    remove_effect(e) {
        for (const [n, x] of this.effects) {
            if (x === e) {
                activity.touch(`remove_effect ${n}`);
                this.effects.delete(n);
                e.actor = null;
                return;
            }
        }
        throw new Error('remove_effect : effet absent');
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

    set_scale(sx, sy) {
        this.scale_x = sx;
        this.scale_y = sy;
    }

    get_transformed_position() {
        let x = 0, y = 0;
        for (let a = this; a; a = a.parent) {
            x += a.x + a.translation_x;
            y += a.y + a.translation_y;
        }
        return [x, y];
    }

    get_transformed_size() {
        return [this.width || 228, this.height || 52];
    }

    remove_all_transitions() {}
    ease(p) {
        const rec = {...p};
        delete rec.onComplete;
        this.eased.push(rec);
        activity.touch(`ease ${this.name ?? this.constructor.name}`);
        anim.log.push({actor: this.name ?? this.constructor.name, ...rec});
        for (const k of ['x', 'y', 'width', 'height', 'opacity', 'translation_x', 'translation_y'])
            if (k in p)
                this[k] = p[k];
        if (p.onComplete) {
            if (anim.defer)
                anim.queue.push(p.onComplete);
            else
                p.onComplete();
        }
    }

    get_preferred_height() {
        return [17, 17]; // 14 px à l'échelle 1 : valeur plausible, la vraie est mesurée en session
    }
}
for (const k of TRACKED) {
    Object.defineProperty(Actor.prototype, k, {
        get() {
            return this._v[k];
        },
        set(v) {
            if (this._live && this._v[k] !== v)
                activity.touch(`${this.name ?? this.constructor.name}.${k}`);
            this._v[k] = v;
        },
    });
}
class Label extends Actor {
    constructor(props = {}) {
        super(props);
        const self = this;
        this.clutter_text = {
            markup: null,
            set_markup(m) {
                if (typeof m !== 'string')
                    throw new Error('set_markup : chaîne attendue');
                if (this.markup !== m)
                    activity.touch('set_markup');
                this.markup = m;
                self.text = m.replace(/<[^>]+>/g, '').replace(/&amp;/g, '&').replace(/&lt;/g, '<')
                    .replace(/&gt;/g, '>').replace(/&#39;/g, "'").replace(/&quot;/g, '"');
            },
        };
        if (this.text === undefined)
            this.text = '';
    }
}
class Clone extends Actor {}
const St = {Widget: Actor, BoxLayout: Actor, Label};
const Clutter = {
    Actor, Clone,
    BindConstraint: class {}, BindCoordinate: {SIZE: 4},
    Orientation: {VERTICAL: 1}, AnimationMode: {EASE_OUT_QUAD: 5, EASE_OUT_CUBIC: 8, EASE_IN_CUBIC: 7},
};
// Effets : `enabled` et `radius` suivis par `activity`.
class TrackedEffect {
    get enabled() {
        return this._enabled;
    }

    set enabled(v) {
        if (this._live && this._enabled !== v)
            activity.touch(`${this.constructor.name}.enabled=${v}`);
        this._enabled = v;
    }
}
class BlurEffect extends TrackedEffect {
    constructor(p) {
        super();
        Object.assign(this, p);
        this.enabled = true;
        this._live = true;
    }

    get radius() {
        return this._radius;
    }

    set radius(v) {
        if (this._live && this._radius !== v)
            activity.touch(`BlurEffect.radius=${v}`);
        this._radius = v;
    }
}
// Shell.GLSLEffect : vfunc_build_pipeline appelé à la construction (comme shell-glsl-effect.c,
// une fois par classe) ; les extraits GLSL et les uniformes sont enregistrés pour les tests.
export const glsl = {built: 0, snippets: []};
class GLSLEffect extends TrackedEffect {
    constructor() {
        super();
        this.enabled = true;
        this.uniforms = new Map();
        this._locs = [];
        if (typeof this.vfunc_build_pipeline === 'function')
            this.vfunc_build_pipeline();
        this._live = true;
        activity.touch(`new ${this.constructor.name}`);
    }

    add_glsl_snippet(hook, decl, code, replace) {
        glsl.built++;
        glsl.snippets.push({hook, decl, code, replace});
    }

    get_uniform_location(name) {
        this._locs.push(name);
        return this._locs.length - 1;
    }

    set_uniform_float(loc, n, values) {
        if (!Array.isArray(values) || values.length !== n)
            throw new Error(`set_uniform_float : ${n} valeurs attendues`);
        this.uniforms.set(this._locs[loc], values);
        activity.touch(`uniform ${this._locs[loc]}`);
    }
}
const Shell = {BlurEffect, BlurMode: {BACKGROUND: 1}, GLSLEffect, ActionMode: {NORMAL: 1, OVERVIEW: 2}};
const Cogl = {SnippetHook: {FRAGMENT: 2048}};
const GObject = {registerClass: (a, b) => b ?? a};
const Meta = {KeyBindingFlags: {IGNORE_AUTOREPEAT: 1, NON_MASKABLE: 2}, KeyBindingAction: {NONE: 0}};
const Pango = {WrapMode: {WORD_CHAR: 2}, EllipsizeMode: {NONE: 0}};
export const gi = {Clutter, Cogl, Gio, GLib, GObject, Meta, Pango, Shell, St};

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
export const windowGroup = new Actor({name: 'window_group', width: 1536, height: 960});
// Fenêtre du jeu : acteur de fenêtre (MetaWindowActor) plein écran, actif.
export const gameActor = new Actor({name: 'game_window_actor', width: 1536, height: 960});
export const focus = {window: {
    fullscreen: true,
    is_fullscreen() {
        return this.fullscreen;
    },
    get_compositor_private: () => gameActor,
}};
// Meta.CursorTracker : signal 'position-invalidated' à chaque mouvement du pointeur.
export const cursorTracker = new Signals();
export function movePointer(x, y) {
    pointer.x = x;
    pointer.y = y;
    cursorTracker.emit('position-invalidated');
}
globalThis.global = {
    compositor,
    window_group: windowGroup,
    display: {get_focus_window: () => focus.window},
    backend: {get_cursor_tracker: () => cursorTracker},
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
