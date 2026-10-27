# 18 — Design QA de l'overlay contre la charte (docs/17) et les skills (v2)

> **v2** : relecture en lecture seule de `Ultima/UI/**` et `Ultima/Patches/**` sur `main@b2bd4cd`
> (branche `m9/design-qa2`), après la refonte du verre par A2 (6b1f8b0 → f4b0ff2) et le correctif
> AvoidRects du chef (4a5f996). Les normes sont `docs/17-charte.md`, puis les skills `sts2-design-system`
> et `sts2-overlay-ui`, rechargées pour cette passe. La v1 (`d572da9`, main@03132ab) reste dans
> l'historique git.
>
> Le jeu n'a pas été lancé et **le shader n'a jamais été compilé** : il a été relu ligne à ligne (§3).
> Les scripts sont hors dépôt, dans le scratchpad de session : `qa/hexcheck.py` et `qa/metrics.py`,
> les mêmes qu'en v1, dont le texte figure dans `d572da9`.

## Récapitulatif

| # | Critère | v1 | **v2** | Fichier:ligne |
|---|---|---|---|---|
| 1 | Aucune couleur hors `UltimaPalette.cs` | PASS | **PASS** | — |
| 2 | Hex ⊂ docs/17 §1 (+ #192930, noir, blanc) | PASS | **PASS** | — |
| 3 | Shader verre : syntaxe Godot 4.5, conforme, branché, embarqué | PASS* | **PASS** (non compilé) | `glass_blur.gdshader:1-39` |
| 4 | 8 px / 260 px / 180 ms / 2.5 s, ease-out, Timer one-shot | PASS | **PASS** | — |
| 5 | Aucune boucle (Tweens 180 ms et 150 ms), létal statique | PASS | **PASS** | — |
| 6 | Divulgation progressive, F8, F9, menu | PASS | **PASS** | — |
| 7 | Click-through ; clic droit consommé seulement dans la zone | PASS | **PASS** | `UltimaOverlay.cs:120-126` |
| 8 | Surfaces (police 14 px) : < 1 % / < 15 % | PASS | **PASS** | — |
| 9 | Co-op via `ForCharacter` | PASS | **PASS** | — |
| 10 | Typo : PV en Source Code Pro, ≥ 14 px | FAIL | **PASS** | — |
| 11 | Chevauchement main / intents / boutons | FAIL | **FAIL** (mineur, repli) | `Placement.cs:38`, `UltimaOverlay.Layout.cs:166`, `UltimaOverlay.Layout.cs:217-221` |
| 12 | Contraste cream / verre ≥ 4.5:1 | PASS | **PASS** | — |
| S | Skills : chaque règle appliquée ou écart justifié | 4 écarts | **appliquées**, 4 écarts justifiés et 1 écart à arbitrer (pas de scroll) | voir section Skills |

**Verdict global v2 : FAIL, sur un seul point mineur (critère 11, chemin de repli).**

Build : `export PATH="$HOME/.dotnet:$PATH"; dotnet build Ultima/Ultima.csproj -c Release`, qui donne
`0 Error(s)` et `EXIT=0`.

---

## 1. Couleurs hors palette — PASS

```
$ grep -rn "new Color(\|Color.FromHtml\|Colors\." Ultima/ --include=*.cs | grep -vc "^Ultima/UI/Theme/UltimaPalette.cs:"
0
```

`UltimaPalette.cs` est inchangé depuis la v1. Le `ColorRect` utilise `Color = UltimaPalette.GlassFill`
(`UltimaOverlay.cs:240`), une couleur de la palette.

## 2. Hex ⊂ charte — PASS

```
$ python3 -I qa/hexcheck.py /home/mio/wt-qa2 | tail -6
OK  192930  Ultima/UI/Theme/UltimaStyles.cs:14  <- #192930
OK  192930  Ultima/UI/Shaders/glass_blur.gdshader:10  <- #192930
OK  192930  Ultima/UI/Shaders/glass_blur.gdshader:10  <- vec3(0.098, 0.161, 0.188
OK  FFFFFF  Ultima/UI/Shaders/glass_blur.gdshader:32  <- vec3(0.06) (blanc additif x0.06)

34 couleurs litterales, 0 hors charte
exit=0
```

## 3. Shader verre — PASS (relu ligne à ligne, jamais compilé)

```
$ grep -n "EmbeddedResource.*gdshader" Ultima/Ultima.csproj
42:    <EmbeddedResource Include="UI/Shaders/*.gdshader" LogicalName="Ultima.Shaders.%(Filename)%(Extension)" />
$ strings Ultima/bin/Release/net9.0/Ultima.dll | grep -c "Ultima.Shaders.glass_blur.gdshader"
1
$ strings Ultima/bin/Release/net9.0/Ultima.dll | grep -c "rounded_rect_sdf"     # la NOUVELLE source est embarquée
2
$ grep -rn "new ColorRect\|new ShaderMaterial\|UseParentMaterial\|BackBufferCopy" Ultima/ --include=*.cs
Ultima/UI/UltimaOverlay.cs:236:        _glassMat = new ShaderMaterial { Shader = shader };
Ultima/UI/UltimaOverlay.cs:240:        var glass = new ColorRect { MouseFilter = MouseFilterEnum.Ignore, Material = _glassMat, Color = UltimaPalette.GlassFill };
```

**Relecture Godot 4.5 (canvas_item, fragment) :**

| Ligne | Code | Verdict |
|---|---|---|
| 1 | `shader_type canvas_item;` | OK |
| 7 | `uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap;` | OK. Mêmes hints que `dark_blur.gdshader:4` |
| 8 | `uniform float lod : hint_range(0.0, 5.0) = 3.5;` | OK. Le nom `lod` est déjà utilisé par `dark_blur.gdshader:3`, qui compile dans le jeu |
| 9, 11 | `blur_amount` [0,6] = 2.0, `mix_percentage` [0,1] = 0.78 | OK |
| 10 | `uniform vec3 tint : source_color = vec3(0.098, 0.161, 0.188);` | OK. `source_color` est valide sur un vec3. #192930 |
| 12-13 | `uniform vec2 rect_size`, `uniform float corner_radius` | OK |
| 16-19 | `float rounded_rect_sdf(vec2 p, vec2 half_size, float r)` : `abs`, `max(vec2, vec2)`, `length`, `min(max(q.x, q.y), 0.0)` | OK. SDF classique, types cohérents |
| 22 | `vec2 step_uv = blur_amount * 0.5 * SCREEN_PIXEL_SIZE * exp2(lod);` | OK. `SCREEN_PIXEL_SIZE` (vec2) est un built-in valide du fragment canvas_item, et `exp2(float)` existe |
| 24-25 | `for (int x = -1; x <= 1; x++)` imbriqués | OK. Bornes constantes |
| 26 | `texture(screen_texture, SCREEN_UV + vec2(float(x), float(y)) * step_uv, lod)` | OK. Surcharge `texture(sampler2D, vec2, float bias)`, identique à `dark_blur.gdshader:8`. `SCREEN_UV` est valide |
| 29 | `mix(acc.rgb / 9.0, tint, mix_percentage)` | OK. Signature (vec3, vec3, float) |
| 31 | `float top = 1.0 - smoothstep(0.0, 0.10, UV.y);` | OK. **Le défaut v1 est corrigé** : le highlight ne couvre plus que les 10 % du haut |
| 36 | `rounded_rect_sdf(UV * rect_size - half_size, half_size, r)` | OK. `UV` couvre 0..1 sur le rect du `ColorRect` |
| 37-38 | `mask = clamp(0.5 - d, 0.0, 1.0)`, `COLOR = vec4(mixed, 0.88 * mask)` | OK. Alpha 0.88, dans la plage 0.85-0.90 de §3. Anticrénelage sur 1 px |

Aucune erreur de syntaxe ni de type trouvée.

Le flou n'a plus de fantôme. L'écart entre taps vaut `2 × 0.5 × 2^3.5 = 11.3 px` écran, soit un texel du mip
3.5 : c'est un filtre « tente » sur un mip déjà moyenné. L'écart ne dépend plus de la taille du panneau.

Le branchement est correct :
- un seul `ColorRect` et un seul `ShaderMaterial`, sans enfant et sans `UseParentMaterial` ;
- l'ombre est un `Panel` `ShadowOnly()` placé **derrière** (`:231-234`), le liseré `GlassEdge()` radius 10
  est placé **devant** (`:245-248`) ;
- le repli `Glass()` est utilisé si `BlurAmount == 0` ou si la ressource manque (`:218-229`) ;
- `Color = GlassFill` évite un rectangle blanc si le shader ne compile pas (`:240`, recommandation v1 appliquée).

Les docs Godot ne précisent pas si une erreur de compilation retombe sur `Color` : c'est un point de la porte
humaine.

**(b) `rect_size` sur `Resized` uniquement :**

```
$ grep -rn "rect_size\|Resized\|_Process\|_Draw\|ProcessFrame" Ultima/UI Ultima/Patches --include=*.cs
Ultima/UI/UltimaOverlay.cs:242:        glass.Resized += OnGlassResized;   // rect_size du masque SDF : sur événement, pas par frame
Ultima/UI/UltimaOverlay.cs:287:    private void OnGlassResized()
Ultima/UI/UltimaOverlay.cs:291:            if (_glass != null) _glassMat?.SetShaderParameter("rect_size", _glass.Size);
```

`rect_size` n'a qu'un seul écrivain, le gestionnaire de `Resized`. Il n'y a ni `_Process` ni `_Draw`.
Pendant l'expansion, `Resized` se déclenche à chaque pas du Tween de 180 ms, environ 11 frames, ce qui suit
bien la taille réelle sans traitement permanent.

## 4. Rail et expansion — PASS

```
$ grep -n "RailWidth = \|ExpandedWidth = \|ExpandDuration = \|DefaultCollapseDelaySec = \|FadeDuration = \|RestAlpha = " Ultima/UI/UltimaOverlay.cs
17:    public const int RailWidth = 8;
18:    public const int ExpandedWidth = 260;
19:    public const double ExpandDuration = 0.18;
20:    public const double DefaultCollapseDelaySec = 2.5;
21:    public const float RestAlpha = 0.30f;
22:    public const double FadeDuration = 0.15;
```

Utilisations :
- `Mathf.Lerp(RailWidth, ExpandedWidth, t)` (`UltimaOverlay.Layout.cs:113`) ;
- `TweenMethod(..., ExpandDuration).SetTrans(Cubic).SetEase(Out)` (`:144-146`) : ease-out cubique, sans
  dépassement ;
- `Timer { OneShot = true }` (`UltimaOverlay.cs:210`), démarré quand la souris sort (`:138`) et arrêté
  quand elle revient (`:133`) ;
- `OnCollapseTimeout` qui appelle `Collapse` (`Layout.cs:211-215`).

## 5. Aucune boucle — PASS

**(c)**

```
$ grep -rn "SetLoops\|Loop\|_Process" Ultima/UI Ultima/Patches
(grep exit=1)
$ grep -rn "CreateTween\|TweenMethod\|TweenProperty" Ultima/UI --include=*.cs
Ultima/UI/UltimaOverlay.Layout.cs:143:        _tween = CreateTween();
Ultima/UI/UltimaOverlay.Layout.cs:144:        _tween.TweenMethod(Callable.From<float>(OnTweenStep), _progress, target, ExpandDuration)
Ultima/UI/UltimaOverlay.Layout.cs:183:            _fadeTween = CreateTween();
Ultima/UI/UltimaOverlay.Layout.cs:184:            _fadeTween.TweenProperty(_root, "modulate:a", a, FadeDuration)
```

Il n'y a que deux Tweens, tous deux one-shot (pas de `SetLoops`, et le défaut Godot est d'une seule passe) :
- l'expansion de 180 ms ;
- le fondu `modulate:a` de 150 ms, lancé seulement par `Expand` ou `Collapse` (`Layout.cs:128,136`).

Chaque Tween est tué avant d'être recréé (`:142,181`) et dans `_ExitTree` (`UltimaOverlay.cs:105-106`).

Le létal reste statique. Le texte `→ Ne coup LÉTAL` est en rouge, et la pastille rouge accompagne une alpha
du rail à 1.0 (`TargetAlpha`, `Layout.cs:167`), sans clignotement.

## 6. Divulgation progressive — PASS

Rien n'a changé en v2 dans la logique des niveaux :
- `showTurn = !waiting && _detail && f != null` (`Layout.cs:28`) ;
- `_turnBy` (« par {ennemi} ») suit `showTurn` (`:30-31`), donc reste au niveau 2 ;
- la menace co-op et les 3 lignes PRIO ne s'affichent qu'au niveau 2 (`:38-39`) ;
- les variantes Weak/Vuln et l'oracle sont dans `if (detail)` (`EnemyRow.cs:72-88`).

F8 masque (`ToggleHidden`, `Layout.cs:197-209`), F9 bascule `_detail` (`UltimaOverlay.cs:154`). Le menu clic
droit, ajouté pour la charte §4, mène aux mêmes actions (`OnMenuId`, `:277-285`).

## 7. Click-through et clic droit — PASS

**(d)**

```
$ grep -rn "SetInputAsHandled\|AcceptEvent\|_GuiInput" Ultima/ --include=*.cs
Ultima/UI/UltimaOverlay.cs:123:                GetViewport()?.SetInputAsHandled();
Ultima/UI/UltimaOverlay.cs:152:            GetViewport()?.SetInputAsHandled();
$ grep -rn "MouseFilter" Ultima/UI Ultima/Patches --include=*.cs | grep -v "MouseFilterEnum.Ignore"
(2 lignes, toutes deux des commentaires XML : UltimaFonts.cs:50, UltimaOverlay.cs:12)
```

`:123` sert le menu. Le code est exécuté seulement si toutes ces conditions tiennent :
1. l'overlay n'est pas masqué (`:119`) ;
2. l'événement est un `InputEventMouseButton` avec `ButtonIndex == Right` (`:120`) ;
3. la souris est **dans `_root.GetGlobalRect()`** (`:122`, sinon `return` sans consommer).

Hors de la zone, et pour tous les clics gauches, rien n'est consommé. `:152` ne concerne que les touches F7,
F8 et F9. Le survol (`:127-139`) ne consomme rien. Les nodes de l'overlay sont tous en `Ignore`. Le
`PopupMenu` est une fenêtre et ne fait pas partie du click-through.

## 8. Surfaces avec la police à 14 px — PASS

**(e)** La géométrie ne dépend pas de la police : la largeur vaut 260, la hauteur est plafonnée à 0.40 × H
(`Layout.cs:98-99`) et le contenu au-delà est coupé par `_clip`.

```
$ python3 -I qa/metrics.py | head -4
RailHeight = 2*4 + 3*14 + 2*6 = 62
1920x1080 (logique 1920x1080, echelle x1.000) : replie 8x62=496 px2 -> 0.0239 % ; deploye max 260x432=112320 px2 -> 5.42 % (avec ombre 6 px : 5.82 %)
2560x1440 (logique 1920x1080, echelle x1.333) : replie 8x62=496 px2 -> 0.0239 % ; deploye max 260x432=112320 px2 -> 5.42 % (avec ombre 6 px : 5.82 %)
3440x1440 (logique 2580x1080, echelle x1.333) : replie 8x62=496 px2 -> 0.0178 % ; deploye max 260x432=112320 px2 -> 4.03 % (avec ombre 6 px : 4.33 %)
$ (estimation du contenu à 14 px : interligne 1.25-1.45 × taille, Pad 10, séparations 4 et 2)
interligne 1.25: niveau 1 (3 ennemis) = 144 px -> 260x144 = 1.80 % a 1080p ; lignes 14 px visibles sous le plafond 432 px ~ 21
interligne 1.45: niveau 1 (3 ennemis) = 161 px -> 260x161 = 2.01 % a 1080p ; lignes 14 px visibles sous le plafond 432 px ~ 18
```

Le panneau replié occupe 0.024 % de l'écran (< 1 %). Le panneau déployé occupe environ 2 % en usage typique et
5.82 % au maximum, ombre comprise (< 15 %).

Conséquence du passage à 14 px : au niveau 2, avec 3 ennemis à 2 attaques et la co-op, le contenu peut
dépasser 18 à 21 lignes. Il est alors **coupé sans défilement** (écart S-OV5 ci-dessous).

## 9. Co-op — PASS

```
$ grep -rn "ForCharacter\|OutlineForCharacter" Ultima/ --include=*.cs | grep -v "public static"
Ultima/UI/Widgets/PriorityStrip.cs:37/38 et ThreatList.cs:29/30 (inchangés depuis la v1)
```

## 10. Typographie — PASS (corrigé depuis la v1)

```
$ grep -rn "NewLabel(" Ultima/UI --include=*.cs | grep -v "public static"
Ultima/UI/UltimaOverlay.cs:175:  _header = NewLabel(FontRole.Title, 16, ...)      -> "T3 ·" (titre)
Ultima/UI/UltimaOverlay.cs:176:  _vitals = NewLabel(FontRole.Mono, 16, ...)       -> "62/80 PV · 12 block" (SCP)
Ultima/UI/UltimaOverlay.cs:184:  _turn   = NewLabel(FontRole.Mono, 14, ...)       -> total du tour (SCP)
Ultima/UI/Widgets/EnemyRow.cs:32: _damage  Mono 14 ; EnemyRow.cs:36: _outcome Mono 14 (PV restants, SCP)
Ultima/UI/Widgets/ThreatList.cs:43: info Mono 14 ; détails EnemyRow.cs:101 : 14 (Mono pour Weak/Vuln)
$ grep -rhoE "NewLabel\((FontRole\.[A-Za-z]+|role), *[0-9]+" Ultima/UI --include=*.cs | grep -oE "[0-9]+$" | sort -n | uniq -c
     13 14
      2 16
$ grep -rn "font_size" Ultima/UI --include=*.cs
Ultima/UI/Theme/UltimaFonts.cs:63:        l.AddThemeFontSizeOverride("font_size", Math.Max(size, MinFontSize));
Ultima/UI/UltimaOverlay.cs:260:        _menu.AddThemeFontSizeOverride("font_size", UltimaFonts.MinFontSize);
```

Les PV et les dégâts sont en Source Code Pro 14-16. Le corps est en Kreon 14 et le titre en Kreon Bold 16.
Le minimum de 14 px est imposé par `MinFontSize = 14` (`UltimaFonts.cs:15,63`).

Une seule exception, acceptable : le numéro de tour `T3` reste dans le titre en Kreon Bold. Ce n'est ni un
dégât ni un PV.

## 11. Chevauchement — FAIL (mineur, chemin de repli)

**(f)** Ce que `AvoidRects.Collect` (`AvoidRects.cs:24-60`, appelé à chaque `Refresh`) évite **maintenant** :

| Zone | Code |
|---|---|
| Bouton fin de tour | `:34` |
| Pioche, défausse, épuisement | `:35-37` (`NCombatUi.cs:64-68`) |
| Main | `:38`. Si son rect fait plus de 50 % du viewport, chaque carte de `ActiveHolders` est prise une par une (`NCard.defaultSize` × échelle globale, centrée, `:79-90`) |
| Corps des créatures, barre de PV et powers | `Hitbox` étendue vers le bas de 35 % de sa hauteur (`:92-99`) |
| Intents | `IntentContainer` et ses enfants (`:47-51`) |

S'y ajoutent la top bar et les potions, au-dessus de la bande des 15 %, et les piles, sous la bande des 75 %
(`Placement.cs:12-13`).

**Ce qui reste à risque :**
1. **Repli sans place libre** (`Placement.cs:38`). Le panneau est placé à `yMin` **par-dessus** les zones,
   et depuis G5 il est **opaque une fois déployé** (`UltimaOverlay.Layout.cs:166`). Avec les hitbox de tous
   les ennemis désormais évitées, une grosse créature à droite (boss) peut ne laisser libre qu'une bande plus
   courte que le panneau. Le chevauchement est alors certain, ce qui contredit §7.2. Il ne se produit que
   pendant un survol voulu.
   - **Correctif :** dans `Placement.Compute`, au lieu de `yMin`, retenir le y qui **minimise l'aire
     d'intersection**. Ou bien réduire `h` à la plus grande bande libre : le clip coupe le reste, et le niveau 1
     tient en environ 150 px.
2. **Rects périmés après un redimensionnement.** `OnViewportSizeChanged` relance `Relayout()` avec l'ancien
   `_avoid` (`UltimaOverlay.Layout.cs:217-221`) jusqu'au prochain événement de combat.
   - **Correctif :** exposer un événement `SizeChanged` sur l'overlay, auquel la session s'abonne pour appeler
     `Schedule()`.
3. **Éléments transitoires non suivis :** carte survolée qui grandit, carte glissée, flèche de ciblage,
   infobulles du jeu. Les rects ne sont relus qu'à chaque événement de combat. Le risque se limite aux 2.5 s
   pendant lesquelles le panneau reste déployé après la sortie de la souris.
4. **Marge PV/powers heuristique.** Les 35 % ne viennent pas du jeu (`:20-22`), et la zone ne s'étend que vers
   le bas : une barre de PV plus large que la hitbox (petite créature) déborde sur les côtés. À mesurer en jeu.

**Par résolution :** 1080p et 1440p ont le même layout logique (`canvas_items`, ×1.333), donc le même risque.
En 21:9 (logique 2580 de large), le panneau tombe dans la marge élargie et le risque est plus faible.

## 12. Contraste — PASS

```
$ python3 -I qa/metrics.py | sed -n '6,9p;11,14p'
#FFF6E2 sur #192930@0.72 / fond noir  : fond=(18.0,29.5,34.6) ratio = 15.88:1
#FFF6E2 sur #192930@0.75 / fond noir  : fond=(18.8,30.8,36.0) ratio = 15.68:1
#FFF6E2 sur #192930@0.78 / fond noir  : fond=(19.5,32.0,37.4) ratio = 15.47:1
#FFF6E2 sur #192930@0.72 / fond BLANC : ratio =  5.59:1  (pire cas, decor clair)
shader (tint .78, alpha .88, highlight 0.0) / decor noir : ratio cream = 16.10:1
shader (tint .78, alpha .88, highlight 0.06) / decor noir : ratio cream = 13.73:1
shader (tint .78, alpha .88, highlight 0.0) / decor blanc: ratio cream =  5.01:1
shader (tint .78, alpha .88, highlight 0.06) / decor blanc: ratio cream =  4.12:1
```

Le contraste dépasse 4.5:1 partout dans le corps du panneau, même sur un décor blanc (5.01:1). Le correctif du
highlight limite la valeur de 4.12:1 aux 10 % du haut. Le repli de placement n'abaisse plus l'alpha du panneau
déployé (`Layout.cs:166`) : le cas v1 à 2.99:1 a disparu.

---

## Skills `sts2-design-system` et `sts2-overlay-ui` : règle par règle

`docs/17` reste normative (« Source normative : docs/17 » dans la skill).

| Réf. | Règle de skill | Statut | Preuve |
|---|---|---|---|
| S-DS1 | Palette StsColors uniquement | Appliqué | crit. 1-2 |
| S-DS2 | Joueur = couleur du personnage | Appliqué | crit. 9 |
| S-DS3a | Partir de `dark_blur.gdshader` (hint_screen_texture, mipmap, `texture(..., lod)`, mix) | Appliqué | `glass_blur.gdshader:3-8,26,29` |
| S-DS3b | `lod = 3.5` | Appliqué | `:8` |
| S-DS3c | `mix_percentage = 0.72` | **Écart justifié** : 0.78, plage 72-80 % de docs/17 §3, valeur du shader de référence | `:11` |
| S-DS3d | 1 seul tap (`dark_blur`) | **Écart justifié** : 3×3 taps au lod (décision chef D11/G1), plus lisse, 11.3 px = 1 texel du mip | `:22-28` |
| S-DS3e | StyleBoxFlat : bg 0.72, liseré 1 px gold 0.18, radius 10, ombre 0.35 taille 6 | Appliqué. Masque SDF radius 10 + `GlassEdge` radius 10 + `ShadowOnly` en mode shader, `Glass()` en repli | `UltimaStyles.cs:15-60` |
| S-DS3f | Un seul panneau flouté, flou non animé | Appliqué (`blur_amount` fixé une fois, `:237`). **Remarque perf** : le `ColorRect` reste visible sur le rail au repos, donc la copie d'écran et ses mips sont recalculées à chaque frame même replié. Option : masquer `_glass` quand `t == 0` (`Layout.cs:119`) | FPS = porte humaine |
| S-DS4 | Rail 8 px, 260 px en 180 ms ease-out, 2.5 s, F8/F9, rien ne bouge dans le jeu | Appliqué | crit. 4, 6 |
| S-DS5 | Niveau 2 jamais au niveau 1 | Appliqué | crit. 6 |
| S-DS6 | Interdits : couleur seule, boucle, blur enfants, chiffres animés | Appliqué | crit. 5 ; aucun Tween sur un texte |
| S-DS7 | Critères mesurables | Appliqués, sauf chevauchement (crit. 11) et FPS (en jeu) | crit. 8, 11 |
| S-OV1 | Injection par Postfix sur `NCombatUi._Ready`, une instance par combat | Appliqué | `CombatUiInjectionPatch.cs:11-24` |
| S-OV2 | Opacité au repos 0.30, puis 1.0 au survol en 150 ms | Appliqué (v1 : 0.35 instantané) | `UltimaOverlay.cs:21-22`, `Layout.cs:175-195` |
| S-OV3 | Ne jamais couvrir main, intents, boutons | Partiel | crit. 11 |
| S-OV4 | `MouseFilter = Ignore` sur la racine ; `Stop` sur les enfants interactifs | Appliqué. Aucun enfant `Control` interactif ; le menu est un `PopupMenu` (fenêtre) | crit. 7 |
| S-OV5 | Scroll via `_UnhandledInput` quand `Ignore` | **Écart à arbitrer** : pas de `ScrollContainer`, le contenu est coupé au-delà de 0.40 × H (environ 18 à 21 lignes à 14 px). Seul le niveau 2 chargé est concerné | crit. 8 |
| S-OV6 | Nettoyage dans `_ExitTree` | Appliqué : 2 Tweens tués, `SizeChanged` désabonné | `UltimaOverlay.cs:100-109` |
| S-OV7 | Police et tailles du thème du jeu, min 14 px | Appliqué | crit. 10 |
| S-OV8 | `PanelContainer` en racine | **Écart justifié** : `Control` placé à la main (Tween et clip) | `UltimaOverlay.cs:162` |
| S-OV9 | Mise à jour sur événement, pas dans `_Process` | Appliqué (`CallDeferred`, au plus 1×/frame, durée loggée) | `CombatSession.cs` |

Le badge `HandBadges.cs`, maintenant sur main, est **identique à 896dc31**
(`git diff 896dc31 HEAD -- Ultima/Features/Priority/HandBadges.cs | wc -l` donne `0`). Le verdict v1 reste
valable : PASS.

**Menu clic droit, points à valider en jeu.** `PopupMenu` stylé `Glass()`, Kreon 14, cream avec survol gold
(`UltimaOverlay.cs:252-265`). Trois points restent ouverts :
- les styleboxes `hover` et les icônes de case à cocher viennent du thème du jeu ou de Godot, sans garantie
  qu'elles respectent la palette ;
- `Position` vient de `GetScreenTransform()`, et sa justesse en 1440p avec `canvas_items` est à vérifier ;
- le clic qui ferme le popup hors de la zone pourrait être absorbé par Godot, uniquement quand le menu est
  ouvert.

---

## À valider en jeu (porte humaine)

1. **Compilation du shader** : chercher l'absence d'erreur `SHADER ERROR` dans le log Godot, et le verre visible
   avec ses coins arrondis à 10 px. Si la compilation échoue, vérifier le repli `Color = GlassFill`.
2. **FPS ≥ 55** en combat. Le flou est actif **même replié**, car le rail utilise aussi le shader.
3. Glyphes `⚔` et `→` (Kreon), `→`, `·` et `É` (Source Code Pro).
4. Lisibilité du rail à 1440p, alpha 0.30.
5. **Chevauchement réel** en 1080p, 1440p et 21:9, avec un combat de boss pour provoquer le repli du crit. 11.
   Vérifier aussi la marge PV/powers de 35 % et les petites créatures.
6. Menu clic droit : position, couleurs de survol, et clic de fermeture.
