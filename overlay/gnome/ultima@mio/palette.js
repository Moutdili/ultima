// ULTIMA — palette du panneau. AUCUNE couleur hors de ce fichier (charte docs/17 §1, §2, §7.3).
//
// Source de vérité : game/pck/src/Core/Helpers/StsColors.cs (classe livrée par MegaCrit),
// + la teinte de verre #192930 imposée par docs/17 §3 et la skill sts2-design-system.
// Module pur (aucune dépendance GNOME Shell) : testable avec `gjs -m`.

export const PALETTE = Object.freeze({
    cream: '#FFF6E2',      // StsColors.cream — texte principal
    gold: '#EFC851',       // StsColors.gold — accent, liseré
    red: '#FF5555',        // StsColors.red — PV, létal
    green: '#7FFF00',      // StsColors.green — soin, buff
    blue: '#87CEEB',       // StsColors.blue — défense
    orange: '#FFA518',     // StsColors.orange — avertissement
    purple: '#EE82EE',     // StsColors.purple — debuff
    aqua: '#2AEBBE',       // StsColors.aqua — énergie, valeurs secondaires
    lightGray: '#BFBFBF',  // StsColors.lightGray — texte désactivé
    gray: '#808080',       // StsColors.gray — note « — » (non calculée), skill design §9
    glassTint: '#192930',  // docs/17 §3 — teinte du verre
    shadow: '#000000',     // docs/17 §3 — ombre (#000000 @ 0.35)
});

// Tons autorisés pour les données publiées par le daemon (jamais une couleur brute).
export const TONES = Object.freeze(['cream', 'gold', 'red', 'green', 'blue', 'orange', 'purple', 'aqua', 'lightGray', 'gray']);

// Texte secondaire : crème à 60 % (skill design §11 « texte secondaire en crème 60 % »).
export const MUTED_ALPHA = 0.6;

export function toneHex(tone) {
    return TONES.includes(tone) ? PALETTE[tone] : PALETTE.cream;
}

// '#RRGGBB' + alpha [0..1] -> 'rgba(r, g, b, a)' pour les styles St (CSS).
export function rgba(hex, alpha) {
    const m = /^#([0-9a-fA-F]{2})([0-9a-fA-F]{2})([0-9a-fA-F]{2})$/.exec(hex);
    if (!m)
        throw new Error(`couleur invalide : ${hex}`);
    const a = Math.min(1, Math.max(0, alpha));
    return `rgba(${parseInt(m[1], 16)}, ${parseInt(m[2], 16)}, ${parseInt(m[3], 16)}, ${a})`;
}
