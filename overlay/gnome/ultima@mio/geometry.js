// ULTIMA — géométrie du rail et du panneau (module pur, testable avec `gjs -m`).
//
// Charte docs/17 §4 + leçon v1 n°4 (docs/20 §0) : le rail fait 14 px (8 px était invisible) ;
// l'expansion ne déplace rien dans le jeu (overlay). Skill design §12 : le rail est une simple
// bande de verre ; à l'ouverture le verre s'élargit.
//
// Modèle « tiroir » (v3) : la plaque de verre a TOUJOURS la taille du panneau (aucun relayout,
// aucune ombre recalculée pendant l'animation — recommandation R6 de docs/perf/HYPOTHESES.md).
// Repliée, elle est translatée vers le bord de l'écran et découpée par la racine : seule une bande
// de RAIL_W px reste visible, collée au bord. Ouverte, translation 0.
//
// v1.4 (docs/44 §3, docs/45 T4) : côté left|right, ancrage top|center|bottom, décalages bornés à
// l'écran, largeur 280-520 px. Le panneau est TOUJOURS placé dans une ZONE SÛRE qui exclut les
// zones d'interface du jeu qu'il ne doit jamais couvrir : barre du haut (reliques/potions/étage) et
// surtout « Fin du tour », en bas à droite (skill design §7 : aucun chevauchement des boutons).

export const RAIL_W = 14;          // px — leçon v1 : 8 px à 0.30 était invisible
export const DEFAULT_WIDTH = 360;  // px — défaut v1.4 (docs/44 §3), dans [280, 520]
export const PANEL_W = DEFAULT_WIDTH; // compatibilité : largeur par défaut
export const MIN_WIDTH = 280;
export const MAX_WIDTH = 520;
export const EDGE_GAP = 4;         // px entre le bord de l'écran et le panneau ouvert
export const SHADOW_PAD = 10;      // px de marge côté intérieur pour l'ombre (non découpée)
export const HOVER_SLOP = 10;      // px de tolérance côté intérieur pour le survol
export const DEFAULT_TOP_FRACTION = 0.22;
export const DEFAULT_HEIGHT_FRACTION = 0.55;   // contrat v1.3 HudSettings.Default
export const HEIGHT_RANGE = Object.freeze([0.2, 0.85]);  // bornes de sûreté v1.3 (hud.js)
export const ANCHORS = Object.freeze(['top', 'center', 'bottom']);
export const MAX_OFFSET_FRACTION = 0.4;  // |offsetX| ≤ 40 % largeur, |offsetY| ≤ 40 % hauteur

// Zones d'interface du jeu réservées (fractions de l'écran, bornées en px).
//   top        : barre du haut (reliques à gauche, potions à droite, étage/acte)
//   bottomRight: « Fin du tour » (StS2 : en bas à droite)
//   bottomLeft : pioche / défausse (en bas à gauche)
export const SAFE = Object.freeze({
    top: 0.10,
    bottomRight: 0.17,
    bottomLeft: 0.13,
    minPx: 48,
    maxPx: 220,
});

export function clamp(v, lo, hi) {
    const n = Number(v);
    if (!Number.isFinite(n))
        return lo;
    return Math.min(hi, Math.max(lo, n));
}

function band(fraction, size) {
    return clamp(Math.round(size * fraction), SAFE.minPx, SAFE.maxPx);
}

// Zone du bouton « Fin du tour » (bas à droite) — le panneau ne doit JAMAIS l'intersecter.
export function endTurnZone(monitor) {
    const w = Math.min(Math.round(monitor.width * 0.22), 380);
    const h = Math.min(Math.round(monitor.height * 0.17), 200);
    return {x: monitor.x + monitor.width - w, y: monitor.y + monitor.height - h, width: w, height: h};
}

// Pioche / défausse (bas à gauche) : même idée, côté gauche.
export function drawPileZone(monitor) {
    const w = Math.min(Math.round(monitor.width * 0.22), 380);
    const h = Math.min(Math.round(monitor.height * 0.13), 180);
    return {x: monitor.x, y: monitor.y + monitor.height - h, width: w, height: h};
}

// Zone sûre du panneau : intérieur du moniteur moins les bandes réservées du côté choisi.
export function safeArea(monitor, side = 'right') {
    const top = monitor.y + band(SAFE.top, monitor.height);
    const bottomPad = band(side === 'left' ? SAFE.bottomLeft : SAFE.bottomRight, monitor.height);
    const bottom = monitor.y + monitor.height - bottomPad;
    return {x: monitor.x, y: top, width: monitor.width, height: Math.max(1, bottom - top)};
}

// monitor : {x, y, width, height} (Main.layoutManager.primaryMonitor, px logiques).
// options : {heightFraction, side, anchor, offsetX, offsetY, width, topFraction}
// Retour (coordonnées du stage, px logiques) :
//   root{X,W}   : racine découpante (contient la plaque + la marge d'ombre) ;
//   plateX      : position de la plaque DANS la racine ;
//   panelX      : bord gauche du panneau ouvert sur le stage ;
//   railX       : bord gauche de la bande visible repliée sur le stage ;
//   collapsedDx : translation_x de la plaque repliée (0 = ouverte) ;
//   w, h, y     : taille et haut du panneau ;
//   side, anchor, offsets (bornés).
export function computeLayout(monitor, options = {}) {
    const o = options && typeof options === 'object' ? options : {};
    const side = o.side === 'left' ? 'left' : 'right';
    const anchor = ANCHORS.includes(o.anchor) ? o.anchor : 'center';

    // Largeur : bornes du contrat, jamais plus large que l'écran.
    const maxW = Math.max(RAIL_W, monitor.width - 2 * EDGE_GAP);
    const w = Math.round(clamp(clamp(o.width ?? DEFAULT_WIDTH, MIN_WIDTH, MAX_WIDTH), MIN_WIDTH, maxW));

    // Hauteur : bornes v1.3 (0.2-0.85) puis zone sûre.
    const safe = safeArea(monitor, side);
    const frac = clamp(o.heightFraction ?? DEFAULT_HEIGHT_FRACTION, HEIGHT_RANGE[0], HEIGHT_RANGE[1]);
    const h = Math.round(Math.min(clamp(Math.round(monitor.height * frac), 40, monitor.height - 2 * EDGE_GAP),
        safe.height));

    // Décalages : bornés à une fraction de l'écran, puis la position est ramenée dans l'écran.
    const ox = clamp(o.offsetX ?? 0, -monitor.width * MAX_OFFSET_FRACTION, monitor.width * MAX_OFFSET_FRACTION);
    const oy = clamp(o.offsetY ?? 0, -monitor.height * MAX_OFFSET_FRACTION, monitor.height * MAX_OFFSET_FRACTION);

    // Vertical : ancre dans la zone sûre, décalage, puis bornage (jamais hors de la zone sûre).
    let y;
    if (anchor === 'top') {
        const topFraction = clamp(o.topFraction ?? DEFAULT_TOP_FRACTION, 0, 0.9);
        y = Math.max(safe.y, monitor.y + Math.round(monitor.height * topFraction));
    } else if (anchor === 'bottom') {
        y = safe.y + safe.height - h;
    } else {
        y = safe.y + Math.round((safe.height - h) / 2);
    }
    y = Math.round(clamp(y + oy, safe.y, safe.y + safe.height - h));

    // Horizontal : bord de l'écran, décalage vers l'intérieur, bornage.
    let panelX = side === 'left'
        ? monitor.x + EDGE_GAP + ox
        : monitor.x + monitor.width - EDGE_GAP - w - ox;
    panelX = Math.round(clamp(panelX, monitor.x + EDGE_GAP, monitor.x + monitor.width - EDGE_GAP - w));

    const monitorLeft = monitor.x;
    const monitorRight = monitor.x + monitor.width;
    let rootX, rootW, plateX, collapsedDx, railX;
    if (side === 'left') {
        rootX = monitorLeft;
        rootW = (panelX - monitorLeft) + w + SHADOW_PAD;  // marge d'ombre à l'intérieur (droite)
        plateX = panelX - monitorLeft;
        railX = monitorLeft;
        collapsedDx = monitorLeft - panelX;               // négatif : la plaque sort à gauche
    } else {
        rootX = panelX - SHADOW_PAD;
        rootW = (monitorRight - panelX) + SHADOW_PAD;
        plateX = SHADOW_PAD;
        railX = monitorRight - RAIL_W;
        collapsedDx = monitorRight - RAIL_W - panelX;     // positif : la plaque sort à droite
    }
    return {
        side, anchor, y, h, w,
        offsets: {x: ox, y: oy},
        rootX, rootW, plateX, panelX, railX, collapsedDx,
        monitorLeft, monitorRight,
    };
}

// Zone de survol : la bande visible (ou le panneau ouvert) jusqu'au bord de l'écran, plus une
// tolérance côté intérieur. La bande repliée est TOUJOURS collée au bord de l'écran (modèle tiroir),
// quel que soit le décalage horizontal du panneau ouvert.
export function hoverZone(layout, expanded) {
    if (layout.side === 'left') {
        const right = (expanded ? layout.panelX + layout.w : layout.railX + RAIL_W) + HOVER_SLOP;
        return {x: layout.monitorLeft, y: layout.y, width: right - layout.monitorLeft, height: layout.h};
    }
    const x = (expanded ? layout.panelX : layout.railX) - HOVER_SLOP;
    return {x, y: layout.y, width: layout.monitorRight - x, height: layout.h};
}

export function contains(rect, px, py) {
    return px >= rect.x && px < rect.x + rect.width && py >= rect.y && py < rect.y + rect.height;
}

export function intersects(a, b) {
    return a.x < b.x + b.width && b.x < a.x + a.width && a.y < b.y + b.height && b.y < a.y + a.height;
}

// Rectangle du panneau ouvert (stage) — pour les tests de non-chevauchement.
export function panelRect(layout) {
    return {x: layout.panelX, y: layout.y, width: layout.w, height: layout.h};
}

// Fractions d'écran occupées (critères docs/17 §7.1 : < 1 % replié, < 15 % déployé).
export function coverage(layout, monitor) {
    const area = monitor.width * monitor.height;
    return {
        collapsed: (RAIL_W * layout.h) / area,
        expanded: (layout.w * layout.h) / area,
    };
}
