# 14 — Charte Graphique & Design System UI/UX (ULTIMA)

> **Document de référence pour Claude Code et les agents d'implémentation.**  
> Ce document définit l'ensemble des règles visuelles, tokens de design, shaders, typographies, hiérarchies et comportements d'interaction de l'overlay **ULTIMA** pour *Slay the Spire 2* (Godot 4.5.1 / C# .NET 9).

---

## 1. Philosophie Visuelle : « Le Grimoire d'Obsidienne »

ULTIMA ne doit jamais ressembler à une application tierce plaquée au burin sur l'écran.  
Il est conçu comme une **lentille runique intégrée au jeu** :
1. **Morphisme natif** : S'intègre directement dans l'arbre de scènes Godot (`NCombatUi`), empruntant les shaders, les polices et la palette de MegaCrit.
2. **Présence fantôme au repos** : Pendant la réflexion et le jeu, l'overlay est quasi-invisible (opacité 0.25-0.30), laissant le champ de vision libre sur les cartes, les monstres et les animations.
3. **Divulgation progressive (Progressive Disclosure)** : Dès que le curseur approche ou survole la zone, l'overlay se réveille en 150 ms (flou de verre dépoli, bordures ciselées en or antique, données statistiques et probabilités détaillées).
4. **Zéro pollution visuelle** : Pas de clignotements agressifs, pas de jauges multicolores inutiles. La tension du jeu reste au premier plan.

---

## 2. Palette Chromatique Officielle STS2 (Vérifiée dans le code)

*Source : `MegaCrit.Sts2.Core.Helpers.StsColors` & `MegaCrit.Sts2.Core.Models.Characters.*`*

### 2.1 Couleurs Fondamentales du Jeu

| Token | Hex | Usage dans le Tracker |
|---|---|---|
| **`StsColors.cream`** | `#FFF6E2` | Typographie principale, labels standards, titres |
| **`StsColors.gold`** | `#EFC851` | Accents de bordure, reliques, cartes rares, scores |
| **`StsColors.red`** | `#FF5555` | Dégâts entrants, menace critique, HP |
| **`StsColors.redGlow`** | `#FF0000` | Alerte coup LÉTAL |
| **`StsColors.aqua`** | `#2AEBBE` | Valeurs de Block / Armure, bouclier |
| **`StsColors.blue`** | `#87CEEB` | Statistiques secondaires, cartes compétences |
| **`StsColors.darkBlue`** | `#67AEEB` | Fond de pill, jauges de probabilité |
| **`StsColors.green`** | `#7FFF00` | Soins, bonus positifs, probabilités favorables |
| **`StsColors.orange`** | `#FFA518` | Alertes intermédiaires, affaiblissement (Weak) |
| **`StsColors.purple`** | `#EE82EE` | Malédictions, pouvoirs altérés, statut Necrobinder |
| **`StsColors.screenBackdrop`** | `rgba(0,0,0,0.80)` | Fond du panneau en mode focus complet |
| **`StsColors.hpBarBackground`** | `#00113480` | Fond des barres de comparaison de dégâts |

### 2.2 Couleurs d'Identification des Classes (Multiplayer & Sync)

| Personnage | Couleur Nom | Ligne Ciblage / Outline | Accent UI |
|---|---|---|---|
| **Ironclad** | `#FF5555` | `#E15847` / `#801212` | Crimson / Sang antique |
| **Silent** | `#7FFF00` | `#2EBD5E` / `#004F04` | Jade poison / Ombre |
| **Defect** | `#87CEEB` | `#70B6ED` / `#163E64` | Cyan plasma / Électrique |
| **Necrobinder** | `#EE82EE` | `#FD98C9` / `#702D6F` | Violet spectral / Os |
| **Regent** | `#FFA518` | `#BFA270` / `#784000` | Orbe ambre / Cuivre royal |

### 2.3 Raretés des Cartes (Bordures & Piles)

- **Commune** : `#4D4B40` (Gris fer parchemin)
- **Peu commune** : `#005C75` (Bleu profond)
- **Rare** : `#6B4B00` (Or lustré)
- **Malédiction** : `#550B9E` (Violet abîme)

---

## 3. Typographie Officielle

*Source : `/fonts/` dans `SlayTheSpire2.pck`*

```
Typography Hierarchy:
├── Header / Titres     : kreon_bold.ttf           (16 - 18 px) -> Titres des sections, noms d'ennemis
├── Chiffres / Calculs  : source_code_pro_semibold.ttf (13 - 15 px) -> Menaces (16x3), % probabilités, ratios
├── Sous-titres / Mots  : spectral_bold.ttf        (13 - 14 px) -> Mots-clés (Weak, Vulnerable, Prio)
└── Descriptions / Info : bitter_medium_italic.ttf (11 - 12 px) -> Tooltips et explications contextuelles
```

---

## 4. Shaders & Effet Verre (« Frosted Obsidian Glass »)

STS2 inclut déjà en natif le shader `dark_blur.gdshader` dans `/shaders/dark_blur.gdshader`.  
ULTIMA utilise une variante adaptée pour créer le fond de panneau en verre :

```glsl
// UltimaGlassPanel.gdshader
shader_type canvas_item;

uniform float lod : hint_range(0.0, 5.0) = 3.5;
uniform sampler2D SCREEN_TEXTURE : hint_screen_texture, filter_linear_mipmap;
uniform vec4 glass_tint : source_color = vec4(0.043, 0.055, 0.078, 0.72); // Teinte obsidienne sombre
uniform vec4 border_gold : source_color = vec4(0.937, 0.784, 0.318, 0.85); // Or antique STS2
uniform float border_thickness : hint_range(0.0, 4.0) = 1.0;

void fragment() {
    // 1. Échantillonnage avec flou Gaussien d'arrière-plan
    vec4 bg = texture(SCREEN_TEXTURE, SCREEN_UV, lod);
    
    // 2. Teinte obsidienne transparente
    vec4 final_color = mix(bg, glass_tint, glass_tint.a);
    
    // 3. Bordure runique discrète
    vec2 size = vec2(textureSize(TEXTURE, 0));
    vec2 pixel_coord = UV * size;
    bool is_border = (pixel_coord.x <= border_thickness || pixel_coord.x >= size.x - border_thickness ||
                      pixel_coord.y <= border_thickness || pixel_coord.y >= size.y - border_thickness);
    
    if (is_border) {
        final_color = mix(final_color, border_gold, 0.65);
    }
    
    COLOR = final_color;
}
```

---

## 5. Zones Écran & Anatomie du Layout

### 5.1 Carte de l'Écran de Combat (Résolution cible : 1920x1080 & 2560x1440)

```
┌──────────────────────────────────────────────────────────────────────────────┐
│ [RELIQUES JOUEUR]             [FLOOR / ACT]             [POTIONS]  [SETTINGS]│
│                                                                              │
│    (Zone Joueur)                         (Zone Monstres & Intent)            │
│       [JOUEUR]                                  [MONSTRE A]    [MONSTRE B]   │
│                                                   [INTENT]       [INTENT]    │
│                                                                              │
│                                                    ┌────────────────────────┐│
│                                                    │ ❖ ULTIMA DOCK          ││
│                                                    │   (Zone morte droite)  ││
│                                                    │   Largeur: 240-280px   ││
│ [ORBE ÉNERGIE]                                     │   Ancrage: X=1640-1900 ││
│ [DRAW PILE]            [MAIN DE CARTES]            │ [FIN DE TOUR]          ││
│ (Bas gauche)             (Bas centre)              │ [DISCARD / EXHAUST]    ││
└──────────────────────────────────────────────────────────────────────────────┘
```

### 5.2 Les Deux États Visuels

```
1. ÉTAT SILENCIEUX (Repos / Combat actif)
┌────────────────────────┐
│ ⚔ 38 Menace · 3e LÉTAL │  <- Pill compacte translucide (Alpha 0.28)
│ 🂡 14/14 Pioche (82% D) │  <- Indicateur minimaliste
└────────────────────────┘

2. ÉTAT DÉVELOPPÉ (Survol de la souris - 150ms Tween)
┌───────────────────────────────────────────────┐
│ ❖ ULTIMA — TACTICAL MATRIX            [F8][F9]│
├───────────────────────────────────────────────┤
│ ⚔ MENACE EN COMBAT                            │
│   ▸ Jaw Worm                                  │
│     Attaque : 16 x 3 = 48 dmg                 │
│     Votre Block : 12                          │
│     Impact net  : 36 dmg sur vos 35 PV        │
│     ➔ ⚠ 3e COUP LÉTAL (PV restants: 0)       │
│     Avec Weak (-25%) : 12 x 3 = 36 dmg        │
│     ➔ Survie assurée à 11 PV restants !       │
├───────────────────────────────────────────────┤
│ 🂡 DECK & PIOCHES                              │
│   Pioche (Draw) : 12 cartes                   │
│   • Proba Block prochain tour : 78.4%         │
│   • Cartes Attaque restantes  : 5             │
│   • Cartes Défense restantes  : 6             │
│   • Cartes Clés non piochées  : Bash, Uppercut│
├───────────────────────────────────────────────┤
│ 🗲 MULTI SYNC & PRIORITÉ DE TOUR             │
│   Joueur 2 (Silent) :                         │
│   ★ PRIO : Backflip à jouer en premier !      │
│     Raison : Bonus pioche avant votre tour    │
└───────────────────────────────────────────────┘
```

---

## 6. Spécification des 4 Modules Fonctionnels

### Module 1 : Combat Threat Matrix (Forecast & Létal NxM)
- **Objectif** : Transformer les intentions brutes en calculs clairs et prédictifs.
- **Règles d'affichage** :
  - Si l'attaque est entièrement couverte par le Block : chiffre affiché en `#2AEBBE` (Aqua).
  - Si des dégâts passent sans être létaux : affiché en `#FFA518` (Orange).
  - Si un coup est **LÉTAL** :
    - Texte explicite : `3e COUP LÉTAL` (ne jamais utiliser la couleur seule).
    - Couleur : `#FF5555` avec bordure lumineuse `#FF0000`.
  - **Ligne hypothétique Weak** :
    - Affichée sous la ligne principale en gris clair `#BFB6A4` avec préfixe `Hypo Weak (-25%)`.
    - Montre immédiatement si appliquer un affaiblissement sauve le tour.

### Module 2 : Deck Probability Engine
- **Objectif** : Anticiper le tirage du prochain tour sans compter manuellement.
- **Formule de tirage sans remise** (loi hypergéométrique) :
  $$\text{Proba}(k \ge 1) = 1 - \frac{\binom{N-K}{n}}{\binom{N}{n}}$$
  où $N$ = cartes dans la Draw Pile, $K$ = cartes de block/attaque ciblées, $n$ = nombre de cartes piochées au prochain tour (généralement 5).
- **Rendu visuel** :
  - Jauge fine (hauteur 3px) en dégradé `#67AEEB` vers `#2AEBBE`.
  - Liste condensée des cartes restantes dans la Draw pile (groupées par nom avec badge quantité `x2`, `x3`).

### Module 3 : Co-op Tactical Telepathy (Broadcast Multi)
- **Objectif** : Synchroniser les équipes sans obliger à parler en vocal en permanence.
- **Déclencheur** : Lorsqu'un allié possède une carte marquée `ShouldGlowGold` ou à synergie séquentielle (ex. `Backflip`, `Spot Weakness`, cartes vulnérabilité/weak avant les grosses attaques).
- **Composant UI** :
  - Une pill flottante lumineuse avec l'icône du personnage allié (vert Silent, bleu Defect, etc.).
  - Notification discrète : `[P2] Jouer Backflip en 1er`.
  - Clic sur la notification ou raccourci envoie un ping audio/visuel natif via `NetMessageBus`.

### Module 4 : Route & Relic Valuation
- **Objectif** : Aider au choix de chemin sur la carte et évaluer l'efficacité de la run.
- **Rendu** : Accessible via `F9` (mode étendu) pour ne pas encombrer le combat de base.

---

## 7. Règles d'Or pour l'Agent d'Implémentation (Claude Code)

1. **`MouseFilter` Strict** :
   - Le conteneur racine `UltimaOverlay` doit avoir `MouseFilter = Control.MouseFilterEnum.Ignore`.
   - Seuls les panneaux interactifs au survol passent en `Control.MouseFilterEnum.Stop`.
   - Ne jamais intercepter les clics destinés aux cartes en main ou au bouton *End Turn*.
2. **Animation par Tweens Godot 4** :
   - Transition d'ouverture : `CreateTween().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic)`.
   - Durée : `0.15s` (assez rapide pour ne jamais donner l'impression de lag).
3. **Mise à jour Réactive (Zéro polling dans `_Process`)** :
   - Mise à jour uniquement sur les signaux du jeu : `CombatStateChanged`, `CardPlayAction`, `HpChanged`, `TurnChanged`.
4. **Localisation & Textes** :
   - Prise en charge UTF-8 complète pour les caractères français (accents, guillemets, flèches `▸`, `➔`).
   - Utiliser les helpers de localisation du jeu si possible (`MegaCrit.Sts2.Core.Localization`).
