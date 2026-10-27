# 17 — Charte graphique ULTIMA (dérivée des assets du jeu)

> **Statut : contrat visuel.** Aucune UI n'est mergée si elle ne respecte pas ce document.
> Toutes les couleurs ci-dessous sont **extraites des thèmes du jeu**, pas inventées :
> source dans `game/pck/themes/*.tres` (v0.107.1). Chaque valeur est reproductible.

---

## 1. Palette canonique du jeu

**Source de vérité : `game/pck/src/Core/Helpers/StsColors.cs`** (classe livrée par MegaCrit,
commentée « Color reference class for this specific game »). On ne réinvente aucune couleur :
on référence ces valeurs.

### 1.1 Sémantique de base

| Nom jeu | Hex | Usage naturel |
|---|---|---|
| `cream` | `#FFF6E2` | texte principal |
| `halfTransparentCream` | `#FFF6E280` | texte secondaire |
| `gold` | `#EFC851` | accent, valeur monétaire, focus |
| `red` | `#FF5555` | PV, danger, létalité |
| `green` | `#7FFF00` | soin, buff |
| `blue` | `#87CEEB` | défense, orbe froid |
| `darkBlue` | `#67AEEB` | bleu appuyé |
| `orange` | `#FFA518` | avertissement doux |
| `pink` | `#FF78A0` | altération |
| `purple` | `#EE82EE` | magie, debuff |
| `aqua` | `#2AEBBE` | énergie alternative |
| `gray` | `#808080` | désactivé |
| `lightGray` | `#BFBFBF` | texte désactivé |
| `disabledTextForPotionPopup` | `#5E5E5E` | texte inactif |

### 1.2 Raretés de carte (`cardTitleOutline*`)

| Rareté | Hex |
|---|---|
| Common | `#4D4B40` |
| Uncommon | `#005C75` |
| Rare | `#6B4B00` |
| Curse | `#550B9E` |
| Quest | `#7E3E15` |
| Status | `#4F522F` |
| Special | `#1B6131` |

→ ULTIMA peut colorer le liseré d'une carte du deck par sa rareté sans inventer de teinte.

### 1.3 Énergie, ciblage, map

| Élément | Hex |
|---|---|
| Énergie (coût jouable) | `#40FFFF` / outline `#20595C` |
| Coût injouable | outline `#501717` |
| Coût étoile | outline `#175561` |
| Flèche de ciblage ennemi | `#E61E1B` |
| Flèche de ciblage allié | `#36C78A` |
| Fond de barre de PV | `#00113480` |
| Nœud boss non visité | `#7D6A55D8` |
| Point de chemin parcouru | `#241F1A` |
| Texte de légende | `#2B3152` |
| Marchand | `#516ACF` |
| Fond d'écran modal | noir 80 % |

### 1.4 Couleurs par personnage (co-op)

Source : `Models/Characters/<Perso>.cs` (`NameColor`, `EnergyLabelOutlineColor`,
`DialogueColor`, `MapDrawingColor`, `RemoteTargetingLineColor`).

| Personnage | Nom (`NameColor`) | Outline énergie | Dialogue | Ligne de map |
|---|---|---|---|---|
| Ironclad | `#FF5555` | `#801212` | `#590700` | `#CB282B` |
| Silent | `#7FFF00` | `#004F04` | `#284719` | `#2F6729` |
| Defect | `#87CEEB` | `#163E64` | `#13446B` | `#0D638C` |
| Regent | `#FFA518` | `#784000` | `#52371D` | `#935206` |
| Necrobinder | `#EE82EE` | `#702D6F` | `#6B4658` | `#AC0486` |
| Deprived | `#EFC851` | — | — | `#462996` |

→ **Règle co-op** : dans l'overlay, chaque joueur est identifié par son `NameColor` et son
`EnergyLabelOutlineColor`. Le tracker parle donc exactement la langue visuelle du jeu :
un joueur Ironclad est rouge, Silent vert, Defect bleu, Regent orange, Necrobinder violet.
Jamais une couleur arbitraire pour distinguer les joueurs.

## 2. Règles d'usage

- **Aucune couleur hors `StsColors` / couleurs de personnage**, sauf blanc pur pour un pic.
- Létal = `red` **+** le mot `LÉTAL` (jamais la couleur seule).
- `gold` réservé à l'accent et au focus. Pas de grandes surfaces dorées.
- Les chiffres secondaires (probabilités, contributions) utilisent `cream` à 70 % ou
  `aqua` — jamais `red`, qui est réservé au danger.
- En co-op, la couleur d'un joueur = son personnage, point.

## 3. Typographie

| Usage | Police | Taille | Source |
|---|---|---|---|
| Titre de panneau | `kreon_bold.ttf` | 16 | `themes/kreon_*` |
| Corps / labels | `kreon_regular.ttf` | 14 | `main_menu_text_button.tres:8` |
| Chiffres (dégâts, PV) | `source_code_pro_semibold.ttf` | 14-18 | `themes/source_code_pro_*` |
| Secondaire / hints | `spectral_bold.ttf` ou Kreon 12 | 12 | `themes/spectral_bold_shared.tres` |

Les chiffres doivent être **alignés en colonne** (police monospace pour les valeurs) :
c'est ce qui permet de comparer `16x3` et `12x3` d'un coup d'œil.

## 3. Glassmorphism — pilier visuel n°1 (P0, pas une finition)

Le tracker doit donner l'impression d'être **une plaque de verre posée sur le jeu** :
on voit le décor derrière, flouté, teinté, avec un liseré métallique. Ce n'est pas
optionnel : c'est l'identité visuelle d'ULTIMA.

Trois couches, dans cet ordre :
1. **Flou** de l'arrière-plan (shader `hint_screen_texture`).
2. **Teinte** `#192930` à 72-80 % (le fond du jeu reste perceptible).
3. **Liseré** : 1 px `#EFC851` à 18 % + un highlight interne blanc à 6 % sur le bord haut.

### Shader de référence (à valider visuellement en jeu)

```glsl
shader_type canvas_item;

uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap;
uniform float blur_amount : hint_range(0.0, 6.0) = 2.0;
uniform vec4  tint : source_color = vec4(0.098, 0.161, 0.188, 0.78); // #192930 @ 0.78
uniform vec2  panel_size = vec2(260.0, 200.0);

void fragment() {
    vec2 uv = SCREEN_UV;
    vec4 acc = vec4(0.0);
    float n = 0.0;
    for (int x = -1; x <= 1; x++) {
        for (int y = -1; y <= 1; y++) {
            vec2 off = vec2(float(x), float(y)) * blur_amount / panel_size;
            acc += texture(screen_texture, uv + off);
            n += 1.0;
        }
    }
    vec4 blurred = acc / n;
    vec3 mixed = mix(blurred.rgb, tint.rgb, tint.a);
    // highlight interne en haut du panneau
    float top = smoothstep(0.0, 0.10, 1.0 - UV.y);
    mixed += vec3(0.06) * top;
    COLOR = vec4(mixed, 0.88);
}
```

Contraintes :
- `blur_amount` fixe (2.0). On ne module pas le flou en continu : c'est un coût plein écran.
- Un seul panneau flouté. Pas de blur sur les badges enfants.
- `COLOR.a` entre 0.85 et 0.90 : en dessous le texte perd en contraste, au-dessus on ne voit
  plus le jeu.
- Coût à mesurer en combat : si < 55 FPS, réduire `blur_amount` à 1.0 avant de désactiver.

### Niveau de repli (si et seulement si le FPS casse)

`StyleBoxFlat` sur le `PanelContainer` :

```
bg_color            = #192930 @ 0.72
border_width_*      = 1
border_color        = #EFC851 @ 0.18
corner_radius_*     = 10
shadow_color        = #000000 @ 0.35
shadow_size         = 6
content_margin_*    = 10
anti_aliasing       = true
```

Effet : le panneau se fond dans le décor, l'or à 18 % donne le liseré « métal ».

### Niveau 2 — flou réel (optionnel, à mesurer)

```
BackBufferCopy (copy_mode = Viewport)
└── ColorRect (material = ShaderMaterial)
      shader: sample SCREEN_TEXTURE en 9 taps (box blur 2 passes),
              mix avec #192930 à 0.72, alpha global 0.85
```

- Coût GPU réel : à mesurer en combat (30 s de combat, FPS mini).
- Si le FPS chute sous 55 : **on garde le niveau 1**. Le verre n'est pas un prérequis
  fonctionnel, c'est une finition.
- Ne pas empiler plus d'un `BackBufferCopy` (c'est un coût plein écran).

### Interdits visuels

- Pas de blur sur le texte (illisible).
- Pas de dégradé arc-en-ciel, pas d'ombre colorée saturée.
- Pas de bordure épaisse : 1 px, jamais 2.
- Pas d'animation en boucle (pulse, glow) : le regard doit rester sur le jeu.

## 4. Divulgation progressive (le cœur de l'idée)

Le tracker est **replié par défaut** : il ne prend pas d'écran.

```
ÉTAT REPLIÉ (repos)           ÉTAT SURVOL (150-200 ms, ease-out)
┌─┐                            ┌──────────────────────────────┐
│▮│  8 px de large             │ T3 · 62/80 PV · 12 block     │
│▮│  3 pastilles               │ ⚔ Jaw Worm  16x3 → 3e LÉTAL  │
└─┘                            │ ⚔ Cultist    6x2 → bloqué    │
                               │ PRIO: Backflip (P2)          │
                               └──────────────────────────────┘
                               260 px
```

- **Rail replié** : 8 px de large, 3 pastilles de statut (PV / létal / priorité). Couleurs
  de la palette uniquement. Clic droit → menu (toggle détail, quitter).
- **Survol** : expansion vers 260 px, 180 ms, `ease-out`. Pas de rebond, pas d'overshoot.
- **Inactivité** : retour au rail après 2,5 s sans survol (configurable `.cfg`).
- **F8** : masquage total. **F9** : mode détaillé (table des contributions, probabilités).
- L'expansion **ne déplace rien** dans le jeu : le panneau est en overlay, ancré à droite,
  et n'occupe jamais la zone main/intents/boutons.

## 5. Hiérarchie de l'information

| Niveau | Visible quand | Contenu |
|---|---|---|
| 0 — Rail | toujours | 3 pastilles : PV critiques / létal / priorité |
| 1 — Survol | survol | PV, block, tour, 1 ligne par ennemi (`dmg xN → létal`) |
| 2 — F9 | à la demande | variantes Weak/Vuln, contributions, probabilités, score |
| 3 — Log/CSV | hors jeu | détail complet exporté |

**Règle** : une information de niveau 2 ne doit jamais apparaître au niveau 1. Si l'écran
est inondé, c'est que la hiérarchie n'est pas respectée.

## 6. Contraste et accessibilité

- Texte sur fond du panneau : ≥ 4,5:1 (`#FFF6E2` sur `#192930` à 72 % ≈ 11:1, large marge).
- Rouge sur fond sombre : vérifier ≥ 3:1 pour le texte large, et **toujours** doubler d'un
  libellé.
- Ne jamais coder une information par la seule couleur.
- Tailles minimales : 12 px pour du secondaire, 14 px pour du corps.

## 7. Critères d'acceptation (mesurables)

1. Le panneau replié occupe **< 1 %** de l'écran ; déployé **< 15 %**.
2. Aucun chevauchement avec la main, les intents ou les boutons de fin de tour, en
   1920×1080, 2560×1440 et 21:9.
3. Toutes les couleurs du panneau appartiennent à la table §1 (vérifiable par grep des hex).
4. L'expansion dure entre 150 et 220 ms et ne provoque aucun déplacement d'élément du jeu.
5. Le rail reste lisible à 1440p sans zoom.
6. Aucune animation en boucle dans le code (`_Process` ne doit pas animer en permanence).
7. Si le flou de niveau 2 est activé : ≥ 55 FPS en combat sur la machine de test.

## 8. Mapping d'implémentation

```
Ultima/UI/Theme/UltimaPalette.cs      constantes Color (hex ci-dessus, une seule source)
Ultima/UI/Theme/UltimaStyles.cs       fabrique les StyleBoxFlat (verre, rail, badge)
Ultima/UI/UltimaOverlay.cs            rail + expansion + F8/F9
Ultima/UI/Widgets/EnemyRow.cs         ligne ennemi (chiffres monospace)
Ultima/UI/Widgets/PriorityStrip.cs    alerte de priorité
Ultima/UI/Shaders/glass_blur.gdshader (niveau 2, optionnel)
```

Toute couleur doit venir de `UltimaPalette` : **aucun `new Color(...)` dans les widgets**.
C'est vérifiable par grep et c'est la garantie que la charte ne dérive pas.

## 9. Ce qui est interdit de faire « pour faire joli »

- Changer la palette parce qu'un écran de combat est plus clair ou plus sombre.
- Ajouter une couleur « juste pour le contraste » : on joue sur l'alpha et les outlines.
- Mettre le logo/nom ULTIMA en gros : le panneau doit être discret, pas branded.
- Animer les chiffres (compteur qui défile) : le joueur lit une valeur, pas un spectacle.
