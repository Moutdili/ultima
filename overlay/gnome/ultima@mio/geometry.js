// ULTIMA — géométrie du rail et du panneau (module pur, testable avec `gjs -m`).
//
// Charte docs/17 §4 + leçon v1 n°4 (docs/20 §0) : le rail fait 14 px (8 px était invisible),
// le panneau 260 px ; l'expansion ne déplace rien dans le jeu (overlay).
// Skill design §12 : le rail est une simple bande de verre ; à l'ouverture le verre s'élargit.
//
// Modèle « tiroir » (v3) : la plaque de verre a TOUJOURS la taille du panneau (aucun relayout,
// aucune ombre recalculée pendant l'animation — recommandation R6 de docs/perf/HYPOTHESES.md).
// Repliée, elle est translatée vers le bord de l'écran et découpée par la racine : seule une bande
// de RAIL_W px reste visible, collée au bord. Ouverte, translation 0, à EDGE_GAP px du bord.

export const RAIL_W = 14;          // px — leçon v1 : 8 px à 0.30 était invisible
export const PANEL_W = 260;        // px — docs/17 §4
export const EDGE_GAP = 4;         // px entre le bord de l'écran et le panneau ouvert
export const SHADOW_PAD = 10;      // px de marge côté intérieur pour l'ombre (non découpée)
export const HOVER_SLOP = 10;      // px de tolérance côté intérieur pour le survol
export const DEFAULT_TOP_FRACTION = 0.22;
export const DEFAULT_HEIGHT_FRACTION = 0.55;   // contrat v1.3 HudSettings.Default

// monitor : {x, y, width, height} (Main.layoutManager.primaryMonitor) ; side : 'right' | 'left'.
// Retour (coordonnées du stage, px logiques) :
//   root{X,W}   : racine découpante (contient la plaque + la marge d'ombre) ;
//   plateX      : position de la plaque DANS la racine ;
//   panelX      : bord gauche du panneau ouvert sur le stage ;
//   railX       : bord gauche de la bande visible repliée sur le stage ;
//   collapsedDx : translation_x de la plaque repliée (0 = ouverte).
export function computeLayout(monitor, topFraction = DEFAULT_TOP_FRACTION,
    heightFraction = DEFAULT_HEIGHT_FRACTION, side = 'right') {
    const top = clamp(topFraction, 0, 0.9);
    const frac = clamp(heightFraction, 0.1, 1 - top);
    const y = Math.round(monitor.y + monitor.height * top);
    const h = Math.round(monitor.height * frac);
    const left = side === 'left';
    const monitorLeft = monitor.x;
    const monitorRight = monitor.x + monitor.width;
    const rootW = SHADOW_PAD + PANEL_W + EDGE_GAP;
    if (left) {
        const panelX = monitorLeft + EDGE_GAP;
        return {
            side: 'left', y, h, rootX: monitorLeft, rootW, plateX: EDGE_GAP,
            panelX, railX: monitorLeft, collapsedDx: -(PANEL_W + EDGE_GAP - RAIL_W),
            monitorLeft, monitorRight,
        };
    }
    const panelX = monitorRight - EDGE_GAP - PANEL_W;
    return {
        side: 'right', y, h, rootX: panelX - SHADOW_PAD, rootW, plateX: SHADOW_PAD,
        panelX, railX: monitorRight - RAIL_W, collapsedDx: PANEL_W + EDGE_GAP - RAIL_W,
        monitorLeft, monitorRight,
    };
}

// Zone de survol : la bande visible (ou le panneau ouvert) jusqu'au bord de l'écran, plus une
// tolérance côté intérieur.
export function hoverZone(layout, expanded) {
    if (layout.side === 'left') {
        const right = (expanded ? layout.panelX + PANEL_W : layout.railX + RAIL_W) + HOVER_SLOP;
        return {x: layout.monitorLeft, y: layout.y, width: right - layout.monitorLeft, height: layout.h};
    }
    const x = (expanded ? layout.panelX : layout.railX) - HOVER_SLOP;
    return {x, y: layout.y, width: layout.monitorRight - x, height: layout.h};
}

export function contains(rect, px, py) {
    return px >= rect.x && px < rect.x + rect.width && py >= rect.y && py < rect.y + rect.height;
}

// Fractions d'écran occupées (critères docs/17 §7.1 : < 1 % replié, < 15 % déployé).
export function coverage(layout, monitor) {
    const area = monitor.width * monitor.height;
    return {
        collapsed: (RAIL_W * layout.h) / area,
        expanded: (PANEL_W * layout.h) / area,
    };
}

function clamp(v, lo, hi) {
    const n = Number(v);
    if (!Number.isFinite(n))
        return lo;
    return Math.min(hi, Math.max(lo, n));
}
