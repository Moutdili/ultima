// ULTIMA — planificateur des coûts de l'overlay (module pur, testable avec `gjs -m` et `node`).
//
// Une seule fonction décide, à partir de l'état de l'overlay, du niveau de verre effectif (hud.js)
// et des réglages A/B (GSettings), ce qui est actif : flou, reflet GLSL, loupe (clone du jeu),
// inhibition de l'unredirect, suivi du pointeur. L'extension applique le plan à chaque changement.
// Skill sts2-performance : « Flou seulement panneau ouvert (rail = aplat) ; … `disable_unredirect`
// seulement si nécessaire ; sondage suspendu quand masqué ».
// Skill sts2-design-system §8 : « rien de tout ça quand le panneau est replié (rail = aplat) ».
//
// AU REPOS (rail replié, aucune animation) : aucun effet actif, aucune transition, et avec
// hover-source=event (défaut) AUCUNE minuterie tant que le pointeur ne bouge pas (docs/perf
// HYPOTHESES.md, recommandation R4).

export const BLUR_MODES = Object.freeze(['panel-only', 'always', 'never']);
export const UNREDIRECT_MODES = Object.freeze(['while-shown', 'while-expanded', 'never']);
export const HOVER_SOURCES = Object.freeze(['event', 'poll']);
export const DEFAULT_POLL_MS = 80;     // 12,5 Hz (spike S1) ; en mode event : délai de regroupement
export const MIN_POLL_MS = 20;
export const MAX_POLL_MS = 1000;

// Rayon du flou par niveau (px logiques, fixe : jamais animé — docs/17 §3). En loupe, flou plus
// léger pour laisser voir le grossissement et la réfraction du jeu derrière le panneau.
export const BLUR_RADIUS = Object.freeze({verre: 24, loupe: 10});

// input : {userHidden, overviewHidden, expanded, pinned, blurMode, unredirectMode, pollMs,
//          hoverSource, quality ('eco'|'verre'|'loupe' = niveau EFFECTIF, après repli selon les API)}
//   expanded = verre ouvert (y compris pendant l'animation de fermeture : pas de saut de flou).
// sortie : {shown, quality, blur, blurRadius, sheen, lens, inhibitUnredirect, poll, pollMs, hoverSource}
//   - sheen : reflet spéculaire + grain + liseré interne (Shell.GLSLEffect) — verre ouvert seulement ;
//   - lens : clone grossissant du jeu + réfraction (loupe) — verre ouvert ET unredirect inhibé
//     (un jeu plein écran en scanout direct n'a pas de texture à cloner) ;
//   - poll : suivi du pointeur actif (survol) ; hoverSource : 'event' (signal du curseur, regroupé)
//     ou 'poll' (minuterie périodique, comportement du spike S1).
export function computePlan(input) {
    const shown = !input.userHidden && !input.overviewHidden;
    const expanded = shown && !!input.expanded;
    const blurMode = BLUR_MODES.includes(input.blurMode) ? input.blurMode : 'panel-only';
    const unredirectMode = UNREDIRECT_MODES.includes(input.unredirectMode) ? input.unredirectMode : 'while-shown';
    const hoverSource = HOVER_SOURCES.includes(input.hoverSource) ? input.hoverSource : 'event';
    const ms = Number.isFinite(input.pollMs) ? Math.round(input.pollMs) : DEFAULT_POLL_MS;

    const quality = ['eco', 'verre', 'loupe'].includes(input.quality) ? input.quality : 'verre';
    const glassy = quality !== 'eco';

    const inhibitUnredirect = shown &&
        (unredirectMode === 'while-shown' || (unredirectMode === 'while-expanded' && expanded));
    // eco : jamais de flou, quel que soit blur-mode (coût ~0 demandé par la skill §8).
    const blur = shown && glassy && (blurMode === 'always' || (blurMode === 'panel-only' && expanded));
    const sheen = expanded && glassy;
    const lens = expanded && quality === 'loupe' && inhibitUnredirect;
    // Épinglé : le panneau ne se replie jamais, le survol ne sert à rien -> pas de suivi.
    // pollMs <= 0 : survol désactivé (panneau uniquement par Super+Maj+I).
    const poll = shown && !input.pinned && ms > 0;
    return {
        shown,
        quality,
        blur,
        blurRadius: blur ? BLUR_RADIUS[quality === 'loupe' && expanded ? 'loupe' : 'verre'] : 0,
        sheen,
        lens,
        inhibitUnredirect,
        poll,
        pollMs: poll ? Math.min(MAX_POLL_MS, Math.max(MIN_POLL_MS, ms)) : 0,
        hoverSource: poll ? hoverSource : null,
    };
}

// Suivi du pointeur : au plus UNE minuterie à la fois, créée/retirée selon le plan.
//   timers  : {add(ms, fn) -> id, once(ms, fn) -> id, remove(id)} — GLib en production
//             (add : périodique, once : un seul coup), horloge factice en test ;
//   tracker : {connect(fn) -> id, disconnect(id)} — signal `position-invalidated` du curseur
//             (Meta.CursorTracker, global.backend.get_cursor_tracker(), comme ui/magnifier.js).
// Mode 'poll' : minuterie périodique (pollMs). Mode 'event' : écoute du signal ; au premier
// mouvement, on se DÉCONNECTE et on lit le pointeur une fois pollMs plus tard, puis on se
// reconnecte : au plus 1000/pollMs lectures par seconde pendant un mouvement, ZÉRO réveil quand le
// pointeur ne bouge pas. recheck(ms) : relecture ponctuelle (fin d'attente de survol sans mouvement).
export class HoverWatch {
    constructor({timers, tracker}, onTick) {
        this._timers = timers;
        this._tracker = tracker;
        this._onTick = onTick;
        this._mode = null;
        this._ms = 0;
        this._timer = 0;      // minuterie périodique (poll) ou ponctuelle (event / recheck)
        this._conn = 0;       // connexion au signal du curseur (event)
    }

    get active() {
        return this._mode !== null;
    }

    get mode() {
        return this._mode;
    }

    get intervalMs() {
        return this._ms;
    }

    // Sources tenues en ce moment (pour les tests : 0 au repos en mode event).
    get pendingTimers() {
        return this._timer ? 1 : 0;
    }

    get connected() {
        return this._conn !== 0;
    }

    apply(plan) {
        if (!plan.poll) {
            this.stop();
            return;
        }
        const mode = plan.hoverSource === 'poll' || !this._tracker ? 'poll' : 'event';
        if (this._mode === mode && this._ms === plan.pollMs)
            return;
        this.stop();
        this._mode = mode;
        this._ms = plan.pollMs;
        if (mode === 'poll')
            this._timer = this._timers.add(this._ms, () => this._onTick());
        else
            this._listen();
    }

    recheck(ms) {
        if (this._mode !== 'event' || this._timer)
            return; // poll : la prochaine période suffit ; event : une lecture est déjà prévue
        this._unlisten();
        this._timer = this._timers.once(Math.max(0, Math.round(ms)), () => this._settle());
    }

    stop() {
        if (this._timer) {
            this._timers.remove(this._timer);
            this._timer = 0;
        }
        this._unlisten();
        this._mode = null;
        this._ms = 0;
    }

    _listen() {
        if (!this._conn)
            this._conn = this._tracker.connect(() => this._moved());
    }

    _unlisten() {
        if (this._conn) {
            this._tracker.disconnect(this._conn);
            this._conn = 0;
        }
    }

    _moved() {
        if (this._timer)
            return;
        this._unlisten();
        this._timer = this._timers.once(this._ms, () => this._settle());
    }

    // Fin d'une minuterie ponctuelle (event) : la source GLib est retirée par son retour.
    _settle() {
        this._timer = 0;
        if (this._mode !== 'event')
            return;
        this._listen();
        this._onTick();
    }
}
