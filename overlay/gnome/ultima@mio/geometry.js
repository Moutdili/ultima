// ULTIMA — géométrie du rail et du panneau (module pur, testable avec `gjs -m`).
//
// Charte docs/17 §4 + leçon v1 n°4 (docs/20 §0) : le rail fait 14 px (8 px était invisible),
// le panneau 260 px, ancré à droite ; l'expansion ne déplace rien dans le jeu (overlay).

export const RAIL_W = 14;          // px — leçon v1 : 8 px à 0.30 était invisible
export const PANEL_W = 260;        // px — docs/17 §4
export const EDGE_GAP = 4;         // px entre le bord droit de l'écran et le verre
export const HOVER_SLOP = 10;      // px de tolérance à gauche du rail pour le survol
export const DEFAULT_TOP_FRACTION = 0.22;
export const DEFAULT_HEIGHT_FRACTION = 0.36;

// monitor : {x, y, width, height} (Main.layoutManager.primaryMonitor)
export function computeLayout(monitor, topFraction = DEFAULT_TOP_FRACTION, heightFraction = DEFAULT_HEIGHT_FRACTION) {
    const top = clamp(topFraction, 0, 0.9);
    const frac = clamp(heightFraction, 0.1, 1 - top);
    const right = monitor.x + monitor.width - EDGE_GAP;
    const y = Math.round(monitor.y + monitor.height * top);
    const h = Math.round(monitor.height * frac);
    return {
        right,
        y,
        h,
        railX: right - RAIL_W,
        panelX: right - PANEL_W,
        monitorRight: monitor.x + monitor.width,
    };
}

// Zone de survol : le rail (ou le panneau déployé) + la bande jusqu'au bord de l'écran.
export function hoverZone(layout, expanded) {
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
