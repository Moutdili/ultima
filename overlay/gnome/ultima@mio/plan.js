// ULTIMA — planificateur des coûts de l'overlay (module pur, testable avec `gjs -m` et `node`).
//
// Une seule fonction décide, à partir de l'état de l'overlay et des réglages A/B (GSettings),
// ce qui est actif : flou, inhibition de l'unredirect, sondage du pointeur. L'extension applique
// le plan à chaque changement (masquage, survol, épinglage, réglage modifié à chaud).
// Skill sts2-performance : « Flou seulement panneau ouvert (rail = aplat) ; … `disable_unredirect`
// seulement si nécessaire ; sondage suspendu quand masqué ».

export const BLUR_MODES = Object.freeze(['panel-only', 'always', 'never']);
export const UNREDIRECT_MODES = Object.freeze(['while-shown', 'while-expanded', 'never']);
export const DEFAULT_POLL_MS = 80;     // 12,5 Hz (spike S1)
export const MIN_POLL_MS = 20;
export const MAX_POLL_MS = 1000;

// input : {userHidden, overviewHidden, expanded, pinned, blurMode, unredirectMode, pollMs}
// sortie : {shown, blur, inhibitUnredirect, poll, pollMs}
export function computePlan(input) {
    const shown = !input.userHidden && !input.overviewHidden;
    const expanded = shown && !!input.expanded;
    const blurMode = BLUR_MODES.includes(input.blurMode) ? input.blurMode : 'panel-only';
    const unredirectMode = UNREDIRECT_MODES.includes(input.unredirectMode) ? input.unredirectMode : 'while-shown';
    const ms = Number.isFinite(input.pollMs) ? Math.round(input.pollMs) : DEFAULT_POLL_MS;

    const blur = shown && (blurMode === 'always' || (blurMode === 'panel-only' && expanded));
    const inhibitUnredirect = shown &&
        (unredirectMode === 'while-shown' || (unredirectMode === 'while-expanded' && expanded));
    // Épinglé : le panneau ne se replie jamais, le survol ne sert à rien -> pas de sondage.
    // pollMs <= 0 : survol désactivé (panneau uniquement par Super+Maj+I).
    const poll = shown && !input.pinned && ms > 0;
    return {
        shown,
        blur,
        inhibitUnredirect,
        poll,
        pollMs: poll ? Math.min(MAX_POLL_MS, Math.max(MIN_POLL_MS, ms)) : 0,
    };
}

// Sondage du pointeur : une seule source de minuterie au plus, créée/retirée selon le plan.
// timers : {add(ms, fn) -> id, remove(id)} — GLib en production, horloge factice en test.
export class Poller {
    constructor(timers, onTick) {
        this._timers = timers;
        this._onTick = onTick;
        this._id = 0;
        this._ms = 0;
    }

    get active() {
        return this._id !== 0;
    }

    get intervalMs() {
        return this._ms;
    }

    apply(plan) {
        if (plan.poll) {
            if (this._id && this._ms === plan.pollMs)
                return;
            this.stop();
            this._ms = plan.pollMs;
            this._id = this._timers.add(this._ms, () => this._onTick());
        } else {
            this.stop();
        }
    }

    stop() {
        if (this._id) {
            this._timers.remove(this._id);
            this._id = 0;
        }
        this._ms = 0;
    }
}
