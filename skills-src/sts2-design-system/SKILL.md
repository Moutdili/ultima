---
name: sts2-design-system
description: Use when building or reviewing the ULTIMA overlay UI — Slay the Spire 2 palette (StsColors), glassmorphism with the game's dark_blur shader, collapsed rail + hover expansion, co-op player colors, and the measurable acceptance criteria.
---

# ULTIMA design system (charte = contrat)

> **PHASE DESIGN OUVERTE (Mio, 2026-10-29)** : « retravailler le HUD et l'UI/UX de tout » ; le verre doit être
> **étourdissant**, avec un **effet loupe** sur le jeu derrière le panneau. Voir §8 ci-dessous (prime sur §3).

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


## 8. Verre v3 « étourdissant » + effet loupe (extension GNOME Shell 50)
Trois niveaux, réglables (`HudSettings.Quality` du contrat v1.3) :
| Niveau | Rendu | Technique (à vérifier dans le code de GNOME Shell 50 / Mutter, citer fichier) |
|---|---|---|
| `eco` | aplat #192930 ~0.85 + liseré or | St.Widget + style ; coût ~0 |
| `verre` | flou du décor + teinte + reflet spéculaire + grain fin + liseré or 1 px + ombre | `Shell.BlurEffect` (BACKGROUND), dégradé/specular par `Shell.GLSLEffect` (snippets GLSL) |
| `loupe` | `verre` + le jeu derrière le panneau **grossi** (×1.04-1.10) et **réfracté** sur les bords (lentille), légère aberration chromatique | `Clutter.Clone` de `global.window_group` (ou de l'acteur de la fenêtre du jeu) dans un conteneur découpé, décalé pour s'aligner sous le panneau et mis à l'échelle autour de son centre ; distorsion en barillet + aberration par `Shell.GLSLEffect` sur le clone |
Règles : rien de tout ça quand le panneau est replié (rail = aplat) ; aucune animation en boucle ;
coût mesuré (protocole `docs/perf/MESURE.md`) : si `loupe` dépasse le budget FPS, défaut = `verre`.

## 9. Couleurs des notes S à F (palette du jeu uniquement)
S = gold `#EFC851` · A = green `#7FFF00` · B = aqua `#2AEBBE` · C = blue `#87CEEB` · D = orange `#FFA518` ·
F = red `#FF5555` · « — » (non calculé) = gray `#808080`. Malédictions : purple `#EE82EE` + mot « Malédiction ».
Enchantements : aqua + nom (« Célérité 1 »). La lettre est TOUJOURS écrite (jamais la couleur seule).

## 10. UX
- Une seule chose importante à la fois : en récompense/shop, la carte de décision occupe le panneau
  (lettre en grand, raison principale en une ligne, détail F9/survol) ; deck & co passent au second plan.
- Lisible à 1920×1200 @125 % (taille min 14 px logiques), contraste ≥ 4,5:1 sur le verre.
- Survol / épinglage / masquage : Super+Maj+I / Super+Maj+O (jamais de touches F).
- Validation visuelle sur captures RÉELLES pendant une fenêtre de test ; banc GNOME isolé autorisé
  seulement sur bus de session PRIVÉ et sans aucun appel au bus système (logind/GDM).

## 11. Minimalisme et cohérence (exigence Mio, 2026-10-29) — PRIME sur tout le reste
« Une UI/UX aussi minimaliste qu'épurée, pour que l'utilisateur ne sente pas son écran pris par des infos non pratiques. »
- **N'afficher que l'actionnable maintenant.** Au repos : le rail seul (3 pastilles). Déployé : un résumé d'UNE
  ligne par module (« Deck 14 · 2 enchantées · 1 malédiction · reliques 4 »), le détail seulement à l'épinglage/survol.
- **Le moment de décision prend la place** (récompense, shop, événement) : lettre + une raison, rien d'autre visible.
- **Une seule grammaire visuelle** partout (HUD, compagnon, TUI) : mêmes couleurs S-F (§9), mêmes libellés,
  même ordre des modules, mêmes icônes/symboles, mêmes espacements. Si un élément n'a pas d'équivalent dans
  les trois surfaces, se demander s'il est utile.
- **Rien sans valeur pratique** : pas de logo, pas de titre décoratif, pas de chiffre sans décision associée,
  pas de répétition d'une info déjà visible dans le jeu (PV/or du jeu : seulement si une décision en dépend).
- Densité : marges généreuses, au plus 1 accent coloré par ligne, texte secondaire en crème 60 %.
- Test : demander « que fait le joueur de cette info dans les 10 prochaines secondes ? » — sinon, la masquer.
