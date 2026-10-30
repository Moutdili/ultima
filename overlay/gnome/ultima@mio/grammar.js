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
export const TONE = Object.freeze({
    text: 'cream',
    curse: 'purple',
    enchant: 'aqua',
    warn: 'orange',
    danger: 'red',
    accent: 'gold',
    muted: 'muted', // pseudo-ton : crème à MUTED_ALPHA (palette.js)
});
