// ULTIMA — grammaire visuelle commune (module pur, testable avec `gjs -m` et `node`).
//
// Skill sts2-design-system §11 : « Une seule grammaire visuelle partout (HUD, compagnon, TUI) :
// mêmes couleurs S-F (§9), mêmes libellés, même ordre des modules, mêmes icônes/symboles ».
// Ce fichier est la référence côté HUD (libellés, symboles, tons) ; status/verrier-design.md la
// recopie pour que le compagnon et le TUI s'alignent dessus.

// Notes S à F : couleurs de la palette du jeu uniquement (skill design §9). La lettre est TOUJOURS
// écrite, jamais la couleur seule.
export const GRADES = Object.freeze(['S', 'A', 'B', 'C', 'D', 'F']);
export const NO_SCORE = '—';
export const GRADE_TONE = Object.freeze({
    S: 'gold', A: 'green', B: 'aqua', C: 'blue', D: 'orange', F: 'red', [NO_SCORE]: 'gray',
});

export function gradeTone(letter) {
    return GRADE_TONE[letter] ?? 'gray';
}

// Symboles : un seul séparateur, un seul signe d'absence, rien d'autre (pas d'icône décorative).
export const SEP = ' · ';
export const COUNT = '×';
export const MORE = '…';

// Libellés courts (une ligne par module, skill design §11). Même ordre que le contrat, mêmes mots
// que le compagnon (overlay/companion/ultima_companion/settings.py MODULE_LABELS).
export const MODULE_LABEL = Object.freeze({
    deck: 'Deck',
    enchanted: 'Enchantées',
    curses: 'Malédictions',
    relics: 'Reliques',
    potions: 'Potions',
    reward: 'Récompense',
    shop: 'Shop',
    event: 'Événement',
    paths: 'Chemins',
    route: 'Route',
    party: 'Équipe',
    deck_score: 'Note du deck',
});

// Tons sémantiques (skill design §9) : malédiction = violet + le MOT ; enchantement = aqua + nom ;
// avertissement = orange ; secondaire = crème 60 %.
// Vocabulaire de la VUE : noms de la palette du jeu. Le thème (theme.js, TONE_ALIASES) les traduit
// en tons sémantiques du contrat v1.4 (text, accent, curse…) puis en couleurs.
export const TONE = Object.freeze({
    text: 'cream',
    curse: 'purple',
    enchant: 'aqua',
    warn: 'orange',
    danger: 'red',
    accent: 'gold',
    muted: 'muted', // pseudo-ton : texte secondaire (ton `muted` du thème)
});

// Tons du CONTRAT v1.4 (Ultima.Engine.Contracts.Tones + Tones.Grades) : c'est ce que publient les
// modules déclaratifs et les plugins dans `EngineState.Custom[].Rows[].Tone`. Acceptés tels quels
// par render.js ; un ton inconnu retombe sur le texte normal.
export const DATA_TONES = Object.freeze([
    'text', 'muted', 'accent', 'danger', 'ok', 'warn', 'curse', 'enchant', 'info',
    'grade.S', 'grade.A', 'grade.B', 'grade.C', 'grade.D', 'grade.F',
]);
