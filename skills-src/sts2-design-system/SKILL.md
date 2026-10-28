---
name: sts2-design-system
description: Use when building or reviewing the ULTIMA overlay UI — Slay the Spire 2 palette (StsColors), glassmorphism with the game's dark_blur shader, collapsed rail + hover expansion, co-op player colors, and the measurable acceptance criteria.
---

# ULTIMA design system (charte = contrat)

> **v2 (2026-10-28) — ULTIMA est une APPLICATION EXTERNE** (`docs/20`). La palette, la hiérarchie,
> le rail et les critères restent valides. Le §3 « shader du jeu » ne s'applique plus tel quel :
> une fenêtre externe ne peut pas lire l'écran du jeu. Le verre vient du **flou du compositeur**
> (extension GNOME Shell : `Shell.BlurEffect`) ou, pour une fenêtre X11, d'une teinte `#192930`
> + capture XComposite floutée. Leçon du run v1 : un rail de 8 px à 0.30 est INVISIBLE en jeu ;
> tout réglage visuel se valide sur capture réelle.

Source normative : `docs/17-charte.md`. Skill = résumé actionnable, doc = détail.

## 1. Palette : jamais une couleur inventée

Source de vérité : `game/pck/src/Core/Helpers/StsColors.cs` (livré par MegaCrit).

| Token | Hex | Usage |
|---|---|---|
| `cream` | `#FFF6E2` | texte principal |
| `gold` | `#EFC851` | accent, focus, liseré |
| `red` | `#FF5555` | PV, létal |
| `green` | `#7FFF00` | soin, buff |
| `blue` | `#87CEEB` | défense |
| `orange` | `#FFA518` | avertissement |
| `purple` | `#EE82EE` | debuff |
| `aqua` | `#2AEBBE` | énergie, valeurs secondaires |

Raretés (bordures de titre) : Common `#4D4B40`, Uncommon `#005C75`, Rare `#6B4B00`,
Curse `#550B9E`, Status `#4F522F`.

## 2. Couleurs par personnage (co-op)

`Models/Characters/<Perso>.cs` → `NameColor` / `EnergyLabelOutlineColor`.

| Perso | Nom | Outline |
|---|---|---|
| Ironclad | `#FF5555` | `#801212` |
| Silent | `#7FFF00` | `#004F04` |
| Defect | `#87CEEB` | `#163E64` |
| Regent | `#FFA518` | `#784000` |
| Necrobinder | `#EE82EE` | `#702D6F` |

Règle : un joueur = son personnage, jamais une couleur arbitraire.

## 3. Glassmorphism : réutiliser le shader du jeu

Le jeu ship `game/pck/shaders/dark_blur.gdshader` :

```glsl
shader_type canvas_item;
uniform float lod: hint_range(0.0, 5.0) = 5.0;
uniform sampler2D SCREEN_TEXTURE : hint_screen_texture, filter_linear_mipmap;
uniform float mix_percentage: hint_range(0.0, 1.0) = 0.3;
void fragment(){
    vec4 color = texture(SCREEN_TEXTURE, SCREEN_UV, lod);
    color = mix(color, vec4(0,0,0,1), mix_percentage);
    COLOR = color;
}
```

Variante ULTIMA : `lod = 3.5`, `mix_percentage = 0.72`, et remplacer le noir par `#192930`.
Pas besoin d'écrire un shader : on adapte celui du jeu (même moteur, même rendu).

StyleBoxFlat du panneau :
```
bg_color      = #192930 @ 0.72
border_width  = 1, border_color = #EFC851 @ 0.18
corner_radius = 10
shadow        = #000000 @ 0.35, size 6
```

Performance : un seul panneau flouté, `blur` non animé, mesurer ≥ 55 FPS en combat.

## 4. Divulgation progressive

- Repos : rail 8 px, 3 pastilles (PV / létal / priorité).
- Survol : expansion à 260 px en 180 ms (ease-out), pas d'overshoot.
- Inactivité : retour au rail après 2,5 s.
- F8 : masquer. F9 : détail (variantes, contributions, probabilités).
- L'expansion ne déplace **rien** dans le jeu (overlay ancré à droite).

## 5. Hiérarchie

Niveau 0 (rail) → 1 (survol : PV/block/létal par ennemi) → 2 (F9 : variantes, score,
probabilités) → 3 (CSV/log). Une info de niveau 2 n'apparaît jamais au niveau 1.

## 6. Interdits

- Couleur hors `StsColors` / personnage (sauf blanc pur).
- Létal signalé par la couleur seule (toujours + le mot `LÉTAL`).
- Animation en boucle, pulse, glow permanent.
- Blur sur les enfants/badges (coût plein écran + illisibilité).
- Chiffres animés.

## 7. Critères mesurables (à vérifier avant merge)

| Critère | Seuil |
|---|---|
| Surface repliée | < 1 % de l'écran |
| Surface déployée | < 15 % |
| Chevauchement main/intents/boutons | aucun en 1080p, 1440p, 21:9 |
| Durée d'expansion | 150-220 ms |
| FPS avec flou | ≥ 55 |
| `new Color(` hors `UltimaPalette` | 0 occurrence |
