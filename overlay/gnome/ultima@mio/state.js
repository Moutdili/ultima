// ULTIMA — lecture de l'état publié par le daemon (module pur : aucune dépendance GNOME Shell,
// testable avec `gjs -m` et `node`).
//
// L'overlay ne lit JAMAIS le jeu : il affiche l'EngineState (contrat moteur v1,
// Ultima.Engine/Contracts/EngineContracts.cs, docs/22) publié par ultima-daemon dans
// $XDG_RUNTIME_DIR/ultima/state.json (écriture atomique tmp + rename).
//
// Règles du contrat appliquées ici :
//   - champs inconnus ignorés ; champs absents traités comme null (rendu défensif dans render.js) ;
//   - contractVersion supérieure à SUPPORTED_CONTRACT -> refus annoncé (« mettre à jour ULTIMA ») ;
//   - JSON invalide (écriture partielle, fichier tronqué…) -> on GARDE le dernier état valide.

export const SUPPORTED_CONTRACT = 1;
export const MAX_STATE_BYTES = 4 * 1024 * 1024; // garde-fou : un état réel fait ~10 Ko

// Résultat : {kind: 'state', state} | {kind: 'future', version} | {kind: 'invalid', error}.
// Ne lève jamais.
export function parseEngineState(text) {
    if (typeof text !== 'string')
        return {kind: 'invalid', error: 'contenu non textuel'};
    if (text.length > MAX_STATE_BYTES)
        return {kind: 'invalid', error: `état trop gros (${text.length} octets)`};
    let raw;
    try {
        raw = JSON.parse(text);
    } catch (e) {
        return {kind: 'invalid', error: `JSON invalide : ${e.message}`};
    }
    if (!raw || typeof raw !== 'object' || Array.isArray(raw))
        return {kind: 'invalid', error: 'racine non objet'};
    const v = raw.contractVersion;
    if (!Number.isInteger(v) || v < 1)
        return {kind: 'invalid', error: `contractVersion absente ou invalide (${JSON.stringify(v)})`};
    if (v > SUPPORTED_CONTRACT)
        return {kind: 'future', version: v};
    return {kind: 'state', state: raw};
}

// Mémoire de l'overlay : dernier état VALIDE + dernière erreur (pour ne logguer qu'au changement).
// current : {kind: 'none'} (aucun état publié) | {kind: 'state', state} | {kind: 'future', version}.
export class StateStore {
    constructor() {
        this.current = {kind: 'none'};
        this.lastError = null;
    }

    // Retourne {changed, error}. Un contenu invalide ne remplace jamais le dernier état valide.
    acceptText(text) {
        const r = parseEngineState(text);
        if (r.kind === 'invalid') {
            const fresh = r.error !== this.lastError;
            this.lastError = r.error;
            return {changed: false, error: r.error, newError: fresh};
        }
        this.lastError = null;
        this.current = r;
        return {changed: true, error: null, newError: false};
    }

    // Fichier absent (daemon arrêté / jamais démarré) : plus rien de publié.
    acceptMissing() {
        const changed = this.current.kind !== 'none';
        this.current = {kind: 'none'};
        this.lastError = null;
        return {changed, error: null, newError: false};
    }
}
