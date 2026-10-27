# 18 — Design QA de l'overlay contre la charte (docs/17)

> Relecture en lecture seule de `Ultima/UI/**` et `Ultima/Patches/**` sur `main@03132ab`
> (branche `m6/design-qa`). Le contrat est `docs/17-charte.md`. Le jeu n'a pas été lancé :
> chaque verdict repose sur une commande ou un calcul reproductible, avec sa sortie brute.
> Les scripts sont hors dépôt, dans le scratchpad de session (`qa/hexcheck.py`, `qa/metrics.py`).
> Leur texte est recopié ci-dessous.

## Récapitulatif

| # | Critère | Verdict | Fichier:ligne à corriger |
|---|---|---|---|
| 1 | Aucune couleur hors `UltimaPalette.cs` | **PASS** | — |
| 2 | Hex ⊂ docs/17 §1 (+ #192930, noir, blanc) | **PASS** | — |
| 3 | Shader verre présent, conforme, branché, embarqué | **PASS** (2 défauts dans la référence §3) | `glass_blur.gdshader:14,22` + docs/17 §3 |
| 4 | Rail 8 px, 260 px, 0.18 s, 2.5 s, ease-out, Timer one-shot | **PASS** | — |
| 5 | Aucune animation en boucle, létal statique | **PASS** | — |
| 6 | Divulgation progressive (niv. 1 / niv. 2, F8, F9) | **PASS** | — |
| 7 | Click-through | **PASS** | — |
| 8 | Surfaces : replié < 1 %, déployé < 15 % | **PASS** | — |
| 9 | Couleurs co-op via `ForCharacter` | **PASS** | — |
| 10 | Typo : chiffres en Source Code Pro, Kreon, ≥ 12 px | **FAIL** (mineur) | `UltimaOverlay.cs:156,161`, `EnemyRow.cs:36` |
| 11 | Chevauchement (main, intents, boutons) | **FAIL** (non prouvé, trous) | `AvoidRects.cs:27-41,58` |
| 12 | Contraste cream / verre ≥ 4.5:1 | **PASS** | (risques : voir §12) |
| S | Écarts vs skills (consigne chef) | 3 écarts, non tranchés | voir section « Écarts vs skills » |

**Verdict global : FAIL** : critères 10 (typo) et 11 (chevauchement). Les écarts vs skills (S1 shader,
S2 coins/ombre, S3 alpha, S4 12 px) sont signalés pour arbitrage, docs/17 restant la norme.

Build de référence : `dotnet build Ultima/Ultima.csproj -c Release`, puis `0 Warning(s) 0 Error(s)`,
`Ultima -> /home/mio/wt-qa/Ultima/bin/Release/net9.0/Ultima.dll` (horodatage 20:44:46).

---

## 1. Aucune couleur hors `UltimaPalette.cs` — PASS

```
$ grep -rn "new Color(\|Color.FromHtml\|Colors\." Ultima/ --include=*.cs
Ultima/UI/Theme/UltimaPalette.cs:6:/// SEULE source de couleurs de l'UI (docs/17 §1.1, §1.4, §3) : aucun <c>new Color(</c> ailleurs.
Ultima/UI/Theme/UltimaPalette.cs:12:    public static readonly Color Cream = Color.FromHtml("#FFF6E2");
... (lignes 13-36 et 42-47, toutes dans UltimaPalette.cs)
Ultima/UI/Theme/UltimaPalette.cs:47:            ["Deprived"] = (Color.FromHtml("#EFC851"), null),
$ ... | grep -v "^Ultima/UI/Theme/UltimaPalette.cs:" | wc -l
0
```

Aucune occurrence hors de la palette. `UltimaStyles.cs` et les widgets ne font que référencer `UltimaPalette.*`.

## 2. Hex ⊂ charte — PASS

Script `qa/hexcheck.py` (python3 -I, hors dépôt) :

```python
import re, sys, pathlib
repo = pathlib.Path(sys.argv[1])
charte = (repo / "docs/17-charte.md").read_text()
sec1 = charte[charte.index("## 1. Palette"):charte.index("## 2. R")]
HEX = re.compile(r"#([0-9A-Fa-f]{8}|[0-9A-Fa-f]{6})\b")
allowed = {h.upper()[:6] for h in HEX.findall(sec1)} | {"192930", "000000", "FFFFFF"}
used = []
for f in sorted(repo.glob("Ultima/**/*.cs")) + sorted(repo.glob("Ultima/**/*.gdshader")):
    for n, line in enumerate(f.read_text().splitlines(), 1):
        for h in HEX.findall(line):
            used.append((f"{f.relative_to(repo)}:{n}", h.upper()[:6], "#" + h.upper()))
        for m in re.finditer(r"(?:new Color|vec4|vec3)\(\s*([0-9.]+)f?\s*,\s*([0-9.]+)f?\s*,\s*([0-9.]+)f?", line):
            rgb = "".join(f"{round(float(c) * 255):02X}" for c in m.groups())
            used.append((f"{f.relative_to(repo)}:{n}", rgb, m.group(0)))
        for m in re.finditer(r"vec3\(\s*([0-9.]+)\s*\)", line):
            used.append((f"{f.relative_to(repo)}:{n}", "FFFFFF", m.group(0) + " (blanc additif)"))
bad = sum(rgb not in allowed for _, rgb, _ in used)
for where, rgb, src in used: print(("OK " if rgb in allowed else "NON"), rgb, where, "<-", src)
print(f"{len(used)} couleurs litterales, {bad} hors charte"); sys.exit(1 if bad else 0)
```

Sortie (tronquée) :

```
autorises (50): ['000000', '001134', '004F04', ..., 'FFA518', 'FFF6E2', 'FFFFFF']
OK  FFF6E2  Ultima/UI/Theme/UltimaPalette.cs:12  <- #FFF6E2
OK  FFF6E2  Ultima/UI/Theme/UltimaPalette.cs:13  <- #FFF6E280
...
OK  000000  Ultima/UI/Theme/UltimaPalette.cs:34  <- new Color(0f, 0f, 0f
OK  FFFFFF  Ultima/UI/Theme/UltimaPalette.cs:36  <- new Color(1f, 1f, 1f
OK  801212  Ultima/UI/Theme/UltimaPalette.cs:42  <- #801212
...
OK  192930  Ultima/UI/Shaders/glass_blur.gdshader:5  <- vec4(0.098, 0.161, 0.188
OK  FFFFFF  Ultima/UI/Shaders/glass_blur.gdshader:23  <- vec3(0.06) (blanc additif x0.06)

33 couleurs litterales, 0 hors charte
exit=0
```

Les dérivées `new Color(GlassTint, 0.72f)` et `new Color(Gold, 0.18f)` (`UltimaPalette.cs:30,32`)
ne changent que l'alpha de couleurs autorisées.

## 3. Shader verre — PASS (deux défauts hérités de la référence §3)

```
$ awk '/^```glsl/{f=1;next} /^```/{if(f){exit}} f' docs/17-charte.md > qa/charte_shader.glsl
$ diff qa/charte_shader.glsl Ultima/UI/Shaders/glass_blur.gdshader
diff: identique (exit 0)
$ grep -n "EmbeddedResource.*gdshader" Ultima/Ultima.csproj
42:    <EmbeddedResource Include="UI/Shaders/*.gdshader" LogicalName="Ultima.Shaders.%(Filename)%(Extension)" />
$ grep -rn "GlassShaderResource\|ShaderMaterial\|new ColorRect\|BackBufferCopy\|UseParentMaterial" Ultima/ --include=*.cs
Ultima/UI/UltimaOverlay.cs:27:    private const string GlassShaderResource = "Ultima.Shaders.glass_blur.gdshader";
Ultima/UI/UltimaOverlay.cs:34:    private ShaderMaterial? _glassMat;
Ultima/UI/UltimaOverlay.cs:204:        _glassMat = new ShaderMaterial { Shader = shader };
Ultima/UI/UltimaOverlay.cs:206:        var glass = new ColorRect { MouseFilter = MouseFilterEnum.Ignore, Material = _glassMat };
Ultima/UI/UltimaOverlay.cs:219:            using var stream = typeof(UltimaOverlay).Assembly.GetManifestResourceStream(GlassShaderResource);
$ export PATH="$HOME/.dotnet:$PATH"; dotnet build Ultima/Ultima.csproj -c Release
  Ultima -> /home/mio/wt-qa/Ultima/bin/Release/net9.0/Ultima.dll
    0 Warning(s)
    0 Error(s)
$ strings Ultima/bin/Release/net9.0/Ultima.dll | grep -c "Ultima.Shaders.glass_blur.gdshader"
1
$ strings -el Ultima/bin/Release/net9.0/Ultima.dll | grep -c "Ultima.Shaders.glass_blur.gdshader"
2
$ strings Ultima/bin/Release/net9.0/Ultima.dll | grep -c "hint_screen_texture"
1
```

- Le nom logique `Ultima.Shaders.glass_blur.gdshader` (csproj:42) est le même que celui chargé à `UltimaOverlay.cs:27`.
  La DLL contient la ressource (nom UTF-8 = 1 occurrence, littéral UTF-16 du code = 2 occurrences,
  source du shader présente).
- Il n'y a qu'un `ColorRect` et un `ShaderMaterial` (`UltimaOverlay.cs:204-206`). Le `ColorRect` n'a pas
  d'enfant : le contenu vit dans `_clip`, son frère (`:149-151`). `UseParentMaterial` n'est jamais utilisé,
  donc il n'y a pas de flou sur le texte ni sur les badges. Aucun `BackBufferCopy` : Godot 4 copie l'écran
  automatiquement quand on lit `hint_screen_texture`.
- Le repli est en place (`:192-203`) : `BlurAmount > 0f ? LoadGlassShader() : null`. Si le résultat est null
  (blur = 0 ou ressource absente), on construit un `Panel` + `UltimaStyles.Glass()` (#192930 @0.72, liseré
  1 px gold @0.18, radius 10, ombre noir @0.35 taille 6, marge 10, AA), ce qui suit §3 « Niveau de repli ».

**Défauts de la référence §3, recopiés à l'identique (à corriger dans docs/17 ET dans le shader) :**

1. Le highlight est inversé (`glass_blur.gdshader:22`). La charte veut un « highlight interne blanc à 6 %
   sur le bord haut », mais `smoothstep(0.0, 0.10, 1.0 - UV.y)` éclaire 90 % du panneau :
   ```
   UV.y -> top : 0.00->1.00  0.50->1.00  0.85->1.00  0.90->1.00  0.95->0.50  1.00->0.00
   ```
   Correctif : `float top = 1.0 - smoothstep(0.0, 0.10, UV.y);`. Ce défaut coûte du contraste (voir §12).
2. Les taps sont trop espacés (`glass_blur.gdshader:14`). L'offset vaut `blur_amount / panel_size` en
   unités SCREEN_UV, soit 2/260 × 1920 = **14.8 px** entre taps (19.8 px en 21:9 logique 2580). Un 3×3 aussi
   espacé risque de donner un effet de « fantôme » plutôt qu'un flou. Correctif proposé :
   `vec2 off = vec2(float(x), float(y)) * blur_amount * SCREEN_PIXEL_SIZE;` (ou
   `textureLod(screen_texture, uv, blur_amount)`, puisque `filter_linear_mipmap` est déjà déclaré). À valider en jeu.
3. Recommandation défensive, non bloquante : `UltimaOverlay.cs:206`. Ajouter `Color = UltimaPalette.GlassFill`
   au `ColorRect`. Le shader réécrit `COLOR`, donc rien ne change s'il compile. S'il échoue à la compilation
   (un cas que le repli ne couvre pas, puisque `Shader` n'est pas null), on ne verrait pas un rectangle blanc.

## 4. Rail et expansion — PASS

```
$ grep -rn "RailWidth\|ExpandedWidth\|ExpandDuration\|CollapseDelay\|OneShot\|SetTrans\|SetEase\|TransitionType\|EaseType" Ultima/UI Ultima/Patches Ultima/Config
Ultima/UI/UltimaOverlay.cs:17:    public const int RailWidth = 8;
Ultima/UI/UltimaOverlay.cs:18:    public const int ExpandedWidth = 260;
Ultima/UI/UltimaOverlay.cs:19:    public const double ExpandDuration = 0.18;
Ultima/UI/UltimaOverlay.cs:20:    public const double DefaultCollapseDelaySec = 2.5;
Ultima/UI/UltimaOverlay.Layout.cs:111:        float w = Mathf.Lerp(RailWidth, ExpandedWidth, t);
Ultima/UI/UltimaOverlay.Layout.cs:141:        _tween.TweenMethod(Callable.From<float>(OnTweenStep), _progress, target, ExpandDuration)
Ultima/UI/UltimaOverlay.Layout.cs:142:            .SetTrans(Tween.TransitionType.Cubic)
Ultima/UI/UltimaOverlay.Layout.cs:143:            .SetEase(Tween.EaseType.Out);
Ultima/UI/UltimaOverlay.cs:121:                _collapseTimer?.Start(Math.Max(0.05, CollapseDelaySec));
Ultima/UI/UltimaOverlay.cs:184:        _collapseTimer = new Godot.Timer { OneShot = true, WaitTime = Math.Max(0.05, CollapseDelaySec) };
Ultima/Patches/CombatSession.cs:47:            CollapseDelaySec = UltimaConfig.CollapseDelaySec,
Ultima/Config/UltimaConfig.cs:18:    public static double CollapseDelaySec { get; private set; } = 2.5;
Ultima/Config/UltimaConfig.cs:47:            CollapseDelaySec = Math.Clamp((double)cfg.GetValue("ui", "collapse_delay_sec", CollapseDelaySec), 0.5, 30.0);
```

Les constantes sont réellement utilisées. La largeur est interpolée de 8 à 260 sur 0.18 s, ce qui tient dans
la fenêtre 150-220 ms de §7.4. La transition Cubic + ease Out n'a pas de dépassement (seules Back, Elastic et
Spring en ont). Le Timer est one-shot : il démarre quand la souris sort du panneau (`:121`) et s'arrête quand
elle revient (`:116`). À l'échéance, `OnCollapseTimeout` replie le panneau, s'il n'est pas survolé, vers le rail
(`UltimaOverlay.Layout.cs:181`). Le délai est configurable via `.cfg` `[ui] collapse_delay_sec` (2.5 par défaut).
Le panneau s'étend vers la gauche avec le bord droit fixe (`:115`) et le contenu reste fixe à l'écran, révélé
par le clip (`:119`). Rien du jeu n'est déplacé : l'overlay est un enfant de `NCombatUi`.

## 5. Aucune animation en boucle — PASS

```
$ grep -rn "_Process\|SetLoops\|Loop" Ultima/UI Ultima/Patches
(exit grep=1)            <- aucune occurrence
$ grep -rn "_PhysicsProcess\|SetProcess\|CreateTween\|TweenProperty\|TweenMethod\|AnimationPlayer\|Blink\|Pulse" Ultima/UI Ultima/Patches
Ultima/UI/UltimaOverlay.Layout.cs:140:        _tween = CreateTween();
Ultima/UI/UltimaOverlay.Layout.cs:141:        _tween.TweenMethod(Callable.From<float>(OnTweenStep), _progress, target, ExpandDuration)
Ultima/UI/UltimaOverlay.cs:83:            SetProcess(false);
```

Il n'y a qu'un Tween, unique et borné (0.18 s, sans `SetLoops`), qui ne se lance que sur survol.
`SetProcess(false)` et `SetPhysicsProcess(false)` sont appelés à `:83-84`.

L'état létal est statique. Au niveau 1, `EnemyRow.cs:63-68` affiche le texte `→ {n}e coup LÉTAL`
(`OverlayText.cs:45`) en `Red`. Au niveau 2, `Turn` et `Threat` contiennent eux aussi le mot `LÉTAL`
et sont en rouge. Au repos, la 2e pastille du rail passe au rouge (`UltimaOverlay.Layout.cs:38`) et le rail
passe d'une opacité de 0.35 à 1.0 (`:163`, sans clignotement). Ce signal combine donc position, couleur et
opacité. Le rail est trop étroit pour porter le mot (8 px), ce que §4 accepte (« 3 pastilles »).

## 6. Divulgation progressive — PASS

Lecture de code (`_detail` = niveau 2, basculé par F9) :

- **Niveau 1** (survol, `_detail == false`) : en-tête `T · PV · block` (`OverlayText.cs:16`), puis une ligne
  par ennemi (nom, `NxM`, issue), l'avis « prévision indisponible », **une** ligne PRIO
  (`PriorityStrip.MaxLinesCompact = 1`) et le pied de page « F8 masquer · F9 détail ».
- **Niveau 2 seulement :**
  - variantes Weak/Vuln et label oracle : générés uniquement dans `if (detail)` (`EnemyRow.cs:72-86`),
    et `_detail.Visible = detail && _detailUsed > 0` (`:88`) ;
  - total du tour : `showTurn = !waiting && _detail && f != null` (`UltimaOverlay.Layout.cs:26`) ;
  - co-op (menace par joueur) : `_threats.Bind(showTurn && Players.Count > 1 ? ... : vide)` (`:33`) ;
  - PRIO jusqu'à 3 lignes : `_priority.Bind(..., _detail)` (`:34`).
- **F8** : `ToggleHidden` (`:168-179`) masque `_root` (rail et panneau), coupe le Timer et le Tween, et `_Input`
  ignore tout tant que le panneau est masqué (`UltimaOverlay.cs:110`).
- **F9** : `_detail = !_detail; Apply();` (`UltimaOverlay.cs:137`).

Remarque : au niveau 1, la ligne PRIO peut afficher le nom d'un joueur distant (« (Mio · distant) »)
quand aucune alerte locale n'existe, car les alertes locales passent d'abord (`CombatSession.cs:107-110`).
C'est conforme à la maquette §4 (`PRIO: Backflip (P2)`) et ce n'est pas la liste co-op de niveau 2.

## 7. Click-through — PASS

```
$ grep -rnE "new (Control|Panel|PanelContainer|ColorRect|Label|VBoxContainer|HBoxContainer|MarginContainer|TextureRect|EnemyRow|ThreatList|PriorityStrip|UltimaOverlay)\b" Ultima/UI Ultima/Patches
Ultima/UI/UltimaOverlay.cs:145:        _root = new Control { Name = "UltimaPanel", MouseFilter = MouseFilterEnum.Ignore };
Ultima/UI/UltimaOverlay.cs:149:        _clip = new Control { MouseFilter = MouseFilterEnum.Ignore, ClipContents = true, Visible = false };
... (152, 157, 169, 174+176, 198, 206, 209 : tous MouseFilter = Ignore)
Ultima/UI/Theme/UltimaFonts.cs:50:        var l = new Label            (MouseFilter = Ignore ligne 52)
Ultima/UI/Widgets/EnemyRow.cs:28/43, PriorityStrip.cs:45, ThreatList.cs:39 : MouseFilter = Ignore
Ultima/UI/UltimaOverlay.Layout.cs:58 new EnemyRow() / UltimaOverlay.cs:162-163 new ThreatList()/PriorityStrip()
    -> constructeurs : MouseFilter = MouseFilterEnum.Ignore (EnemyRow.cs:25, ThreatList.cs:16, PriorityStrip.cs:20)
Ultima/Patches/CombatSession.cs:43:        var overlay = new UltimaOverlay   -> MouseFilter = Ignore dans _Ready (UltimaOverlay.cs:81)
$ grep -rn "MouseFilter" Ultima/UI Ultima/Patches --include=*.cs | grep -v "MouseFilterEnum.Ignore"
(2 lignes, toutes deux des commentaires XML : UltimaOverlay.cs:12, UltimaFonts.cs:47)
$ grep -rn "SetInputAsHandled\|AcceptEvent\|_GuiInput" Ultima/ --include=*.cs
Ultima/UI/UltimaOverlay.cs:135:            GetViewport()?.SetInputAsHandled();
```

Le seul `SetInputAsHandled` est dans `_UnhandledInput`, après un filtre `InputEventKey` limité à F7, F8 et F9
(`:132-135`). Le survol passe par `_Input` sur `InputEventMouseMotion` avec un simple test `HasPoint`
(`:110-122`) et ne consomme jamais l'événement.

Remarque mineure : le `MouseFilter` de l'overlay plein écran n'est posé que dans `_Ready`. Le doubler dans
l'initialiseur (`CombatSession.cs:43-48`) serait une ceinture de sécurité supplémentaire.

## 8. Surfaces — PASS

Constantes : `RailWidth = 8`, `RailHeight = 2·4 + 3·14 + 2·6 = 62` (`UltimaOverlay.cs:17,25-26`),
`ExpandedWidth = 260`, hauteur max = `0.40 × H` (`Placement.cs:14`, `UltimaOverlay.Layout.cs:93`).
Côté jeu, `project.godot` fixe un viewport de 1920×1080 avec `stretch/mode="canvas_items"` et `aspect="expand"`.
Les pixels de l'overlay sont donc logiques : la hauteur logique vaut toujours 1080, et en 21:9 la largeur
logique vaut 1080 × 3440/1440 = 2580.

Sortie de `qa/metrics.py` :

```
RailHeight = 2*4 + 3*14 + 2*6 = 62
1920x1080 (logique 1920x1080, echelle x1.000) : replie 8x62=496 px2 -> 0.0239 % ; deploye max 260x432=112320 px2 -> 5.42 % (avec ombre 6 px : 5.82 %)
2560x1440 (logique 1920x1080, echelle x1.333) : replie 8x62=496 px2 -> 0.0239 % ; deploye max 260x432=112320 px2 -> 5.42 % (avec ombre 6 px : 5.82 %)
3440x1440 (logique 2580x1080, echelle x1.333) : replie 8x62=496 px2 -> 0.0178 % ; deploye max 260x432=112320 px2 -> 4.03 % (avec ombre 6 px : 4.33 %)
```

Le panneau replié occupe ≤ 0.024 % de l'écran, bien sous 1 %. Le panneau déployé, au pire (hauteur plafonnée
à 40 %, ombre comprise), occupe ≤ 5.82 %, bien sous 15 %. Le calcul est le même en pixels physiques, puisque
le rapport d'échelle s'annule.

## 9. Couleurs co-op — PASS

```
$ grep -rn "ForCharacter\|OutlineForCharacter" Ultima/ --include=*.cs
Ultima/UI/Widgets/ThreatList.cs:29:  UltimaFonts.SetColor(line.Name, UltimaPalette.ForCharacter(p.CharacterId));
Ultima/UI/Widgets/ThreatList.cs:30:  UltimaFonts.SetOutline(line.Name, UltimaPalette.OutlineForCharacter(p.CharacterId));
Ultima/UI/Widgets/PriorityStrip.cs:37:  UltimaFonts.SetColor(line.Player, UltimaPalette.ForCharacter(a.CharacterId));
Ultima/UI/Widgets/PriorityStrip.cs:38:  UltimaFonts.SetOutline(line.Player, UltimaPalette.OutlineForCharacter(a.CharacterId));
```

La table `UltimaPalette.cs:42-47` reprend les colonnes `NameColor` et `Outline énergie` de §1.4 à l'identique
(vérifié au critère 2). Pour Deprived, l'outline vaut `null`, comme le « — » de la charte. La clé est
`p.Character.GetType().Name` (`CombatReader.cs:235`), et les classes `Ironclad`, `Necrobinder`, `Deprived`, etc.
existent bien dans `game/pck/src/Core/Models/Characters/`. La comparaison ignore la casse. Un personnage inconnu
reçoit `Cream`, qui est une couleur de la palette : aucune couleur arbitraire n'est utilisée.

## 10. Typographie — FAIL (mineur)

```
$ grep -rn "NewLabel(" Ultima/UI --include=*.cs
Ultima/UI/UltimaOverlay.cs:156:        _header = UltimaFonts.NewLabel(FontRole.Title, 16, UltimaPalette.Cream);
Ultima/UI/UltimaOverlay.cs:159:        _notice = UltimaFonts.NewLabel(FontRole.Body, 12, UltimaPalette.Orange);
Ultima/UI/UltimaOverlay.cs:161:        _turn = UltimaFonts.NewLabel(FontRole.Body, 12, UltimaPalette.Cream);
Ultima/UI/UltimaOverlay.cs:164:        var footer = UltimaFonts.NewLabel(FontRole.Body, 12, UltimaPalette.HalfTransparentCream);
Ultima/UI/Widgets/EnemyRow.cs:30:        _name = UltimaFonts.NewLabel(FontRole.Body, 14, UltimaPalette.Cream);
Ultima/UI/Widgets/EnemyRow.cs:32:        _damage = UltimaFonts.NewLabel(FontRole.Mono, 14, UltimaPalette.Cream, trim: false);
Ultima/UI/Widgets/EnemyRow.cs:36:        _outcome = UltimaFonts.NewLabel(FontRole.Body, 14, UltimaPalette.Cream, trim: false);
Ultima/UI/Widgets/EnemyRow.cs:101:            l = UltimaFonts.NewLabel(role, 12, color);
Ultima/UI/Widgets/PriorityStrip.cs:47/49/51:  Title 14 / Body 14 / Body 14
Ultima/UI/Widgets/ThreatList.cs:41/43:        Body 12 / Mono 12
$ grep -rn "res://themes" Ultima/ --include=*.cs
Ultima/UI/Theme/UltimaFonts.cs:10:    public const string KreonRegular = "res://themes/kreon_regular_shared.tres";
Ultima/UI/Theme/UltimaFonts.cs:11:    public const string KreonBold = "res://themes/kreon_bold_shared.tres";
Ultima/UI/Theme/UltimaFonts.cs:12:    public const string SourceCodeProSemibold = "res://themes/source_code_pro_semibold_shared.tres";
$ (tailles des NewLabel)  6 x 12 | 6 x 14 | 1 x 16
$ ls game/pck/themes | grep kreon_regular_shared\|kreon_bold_shared\|source_code_pro_semibold_shared  -> les 3 existent
```

Ce qui est conforme :
- les chemins de police existent dans le pck, et le chargement retombe proprement sur la police par défaut
  si besoin (`UltimaFonts.cs:29-45`) ;
- le corps est en Kreon 14 et le titre en Kreon Bold 16 ;
- toutes les tailles sont ≥ 12 ;
- la colonne de dégâts `NxM` est en Source Code Pro 14, alignée à droite, sur 52 px (`EnemyRow.cs:32-34`) ;
- les variantes Weak/Vuln et la co-op sont en Mono 12, ce qui est du secondaire.

**Ce qui échoue :** §3 exige Source Code Pro pour les « Chiffres (dégâts, **PV**) », mais trois labels
affichent des PV en Kreon :
- `UltimaOverlay.cs:156` : `_header` affiche `T3 · 62/80 PV · 12 block` en Kreon Bold 16 ;
- `EnemyRow.cs:36` : `_outcome` affiche `→ 12 PV restants` en Kreon 14, hors colonne monospace ;
- `UltimaOverlay.cs:161` : `_turn` affiche `Tour : 34 → 12 PV restants` en Kreon 12.

**Correctif :** passer `FontRole.Mono` sur ces trois lignes, en gardant les tailles 16, 14 et 12 (16 et 14
restent dans la plage 14-18). Variante pour l'en-tête : le séparer en un titre Kreon Bold (`T3`) suivi d'une
valeur Mono (`62/80 · 12`). Il faudra vérifier en jeu que Source Code Pro contient bien `→`, `·` et `É`.

## 11. Chevauchement — FAIL (conformité §7.2 non démontrable, trous identifiés)

**Ce qui est évité** (`AvoidRects.cs`, rects collectés à chaque `Refresh`, `CombatSession.cs:112`) :
- `ui.EndTurnButton` (`:27`) ;
- `ui.Hand` (`:28`), **mais seulement si** son rect global fait ≤ 50 % de l'aire du viewport (`:24-25`, `:58`).
  Au-delà, la main est **ignorée en silence**, et rien ne la remplace par les rects de ses cartes ;
- pour chaque créature vivante, `NCreature.IntentContainer` et chacun de ses enfants `Control` (`:33-41`).

**Ce qui est évité par la géométrie** (`Placement.cs:12-14`) : le panneau est collé au bord droit
(x ≥ W − 260) et reste dans la bande 15 % → 75 % de la hauteur.
- La top bar (PV, or, **potions**, carte, deck, réglages) est au-dessus de 15 % : à 1080 logique, le haut du
  panneau est à y ≥ 162 px.
- Les piles du bas (pioche à gauche, défausse en bas à droite) sont sous 75 % : le bas du panneau est à
  y ≤ 810 px. Elles restent néanmoins **non listées**.

**Ce qui n'est PAS évité :**
- **le corps, la barre de PV, le bloc et les powers des ennemis**. `NCreature.Hitbox` existe (`NCreature.cs:83`)
  mais n'est pas collecté. Les ennemis se tiennent à droite et le balayage place le panneau au premier y libre
  **sous** les intents, c'est-à-dire précisément sur la barre de PV et les powers de l'ennemi le plus à droite.
  C'est le risque principal, au même niveau en 1080p et en 1440p (layout logique identique avec `canvas_items`).
  En 21:9 (logique 2580 de large), le panneau tombe dans la marge élargie et le risque est plus faible ;
- les piles `ui.DrawPile`, `ui.DiscardPile`, `ui.ExhaustPile` (`NCombatUi.cs:64-68`), même si elles sont
  probablement hors bande ;
- les cartes survolées ou agrandies et la flèche de ciblage, quand la main est ignorée ou que la carte dépasse
  le rect de la main ;
- les infobulles (hover tips) du jeu.

**Fraîcheur des rects :** `OnViewportSizeChanged` relance le placement avec les rects de l'ancienne taille
jusqu'au prochain événement de combat (`UltimaOverlay.Layout.cs` `Relayout`). Au `CombatSetUp`, les ennemis
peuvent encore être en animation d'entrée.

**Repli :** quand aucune position n'est libre, le panneau garde y = 15 % avec un alpha de 0.35 sur **tout**
le panneau, texte compris (`Placement.cs:38`, `UltimaOverlay.Layout.cs:164`). Le texte devient illisible
(2.99:1 sur décor noir, 1.18:1 sur décor blanc, voir §12).

**Correctifs proposés :**
1. Dans `AvoidRects.cs:28`, si `ui.Hand` dépasse le seuil, ajouter les rects des enfants `Control` visibles
   de la main (les cartes), comme c'est déjà fait pour les intents (`:39-40`).
2. Dans `AvoidRects.cs:36`, ajouter `AddControl(rects, creature.Hitbox);`. La barre de PV et les powers
   seront à vérifier en jeu.
3. Dans `AvoidRects.cs:27`, ajouter `AddControl(rects, ui.DrawPile); AddControl(rects, ui.DiscardPile);
   AddControl(rects, ui.ExhaustPile);`.
4. Dans `UltimaOverlay.Layout.cs:164`, appliquer `_placementAlpha` au rail seulement, pas au panneau survolé
   (`if (!_expanded) a = Math.Min(a, _placementAlpha);`), ou bien choisir le placement le moins chevauchant
   plutôt que d'assombrir.
5. Re-collecter les rects sur `size_changed` en passant par la session (`CombatSession.Schedule`).

## 12. Contraste — PASS

Le calcul suit WCAG 2.x (luminance relative sRGB) : la teinte est composée sur un décor, et le texte cream est
opaque. Script `qa/metrics.py`, sortie :

```
#FFF6E2 sur #192930@0.72 / fond noir  : fond=(18.0,29.5,34.6) ratio = 15.88:1
#FFF6E2 sur #192930@0.75 / fond noir  : fond=(18.8,30.8,36.0) ratio = 15.68:1
#FFF6E2 sur #192930@0.78 / fond noir  : fond=(19.5,32.0,37.4) ratio = 15.47:1
#FFF6E2 sur #192930@0.72 / fond BLANC : ratio =  5.59:1  (pire cas, decor clair)
#FFF6E2 sur #192930@0.78 / fond BLANC : ratio =  6.82:1  (pire cas, decor clair)
shader (tint .78, alpha .88, highlight 0.0) / decor noir : ratio cream = 16.10:1
shader (tint .78, alpha .88, highlight 0.06) / decor noir : ratio cream = 13.73:1
shader (tint .78, alpha .88, highlight 0.0) / decor blanc: ratio cream =  5.01:1
shader (tint .78, alpha .88, highlight 0.06) / decor blanc: ratio cream =  4.12:1
#FF5555 (red, LETAL) sur #192930@0.72/noir : 5.43:1 (exige >= 3:1 texte large, §6)
#FF5555 sur #192930@0.72/blanc : 1.91:1
#FFF6E280 (pied de page) sur #192930@0.72/noir : 4.88:1
#2AEBBE (aqua) 11.16:1 | #FFA518 (orange) 8.65:1 | #87CEEB (blue) 9.80:1 | #EFC851 (gold) 10.61:1
Modulate 0.35 (aucune place libre) / decor noir: texte cream 2.99:1
Modulate 0.35 (aucune place libre) / decor blanc: texte cream 1.18:1
```

Sur fond composite noir, le ratio va de 15.5 à 15.9:1, au-dessus de 4.5:1 (la charte annonçait ≈ 11:1, elle
était prudente). **PASS.**

Risques à suivre (pas des FAIL du critère tel qu'il est défini) :
- Avec le shader sur un décor très clair, la zone éclairée par le highlight descend à **4.12:1 < 4.5:1**.
  À cause du défaut du critère 3 (`:22`), cette zone couvre 90 % du panneau. Corriger le highlight ramène le
  corps du panneau à 5.01:1.
- Le rouge `LÉTAL` tombe à 1.91:1 sur décor blanc avec le repli @0.72. Le mot écrit compense, ce qui est
  conforme à §6 « toujours doubler d'un libellé ».
- Le repli de placement à alpha 0.35 rend le texte illisible (voir correctif 4 du critère 11).

---

## Écarts vs skills `sts2-design-system` et `sts2-overlay-ui` (consigne chef)

Les deux skills ont été chargées après la relecture : l'UI a été construite sur docs/17 seul. La skill
`sts2-design-system` le dit elle-même : « Source normative : `docs/17-charte.md` ». Chaque écart ci-dessous est
donc **signalé sans être tranché** : le chef décide.

| # | Point | vs skill | vs docs/17 | Preuve |
|---|---|---|---|---|
| S1 | Shader verre : réutiliser `dark_blur.gdshader` du jeu | **FAIL** | PASS (copie exacte de la référence §3) | voir ci-dessous |
| S2 | Radius 10 et ombre sur le panneau verre | **FAIL** | PASS (§3 ne les met qu'au repli) | `UltimaStyles.cs:32-42` |
| S3 | Opacité au repos 0.30 | **écart** (0.35) | pas de valeur (SPEC F7 : 0.25-0.35, donc PASS) | `UltimaOverlay.cs:21` |
| S4 | Police min 14 px | **FAIL** (6 labels à 12) | PASS (§3/§6 : 12 px pour le secondaire) | crit. 10 |
| S5 | Pièges Godot, événements, `_ExitTree` | PASS | PASS | voir ci-dessous |
| S6 | Badge C1 `HandBadges.cs` (896dc31, hors main) | PASS | PASS | voir ci-dessous |

**S1, shader.** Le shader du jeu `game/pck/shaders/dark_blur.gdshader` (utilisé par `main_menu.tscn`,
`epoch.tscn`) tient en peu de lignes :
```
3  uniform float lod: hint_range(0.0, 5.0) = 5.0;
4  uniform sampler2D SCREEN_TEXTURE : hint_screen_texture, filter_linear_mipmap;
8  vec4 color = texture(SCREEN_TEXTURE, SCREEN_UV, lod);
9  color = mix(color, vec4(0,0,0,1), mix_percentage);
```
La variante préconisée par la skill est `lod = 3.5` avec `mix_percentage = 0.72` vers `#192930`.

L'UI actuelle (`glass_blur.gdshader:12-19`) fait 9 taps `texture(screen_texture, uv + off)` **sans lod**,
donc au mip 0, espacés de 14.8 px (1080p) à 19.8 px (21:9) (calcul au critère 3). Elle risque donc un effet
de fantôme.

Évaluation : le shader du jeu fait 1 fetch au mip 3.5, ce qui moyenne des blocs d'environ 2^3.5 ≈ 11 px.
C'est un flou doux, sans fantôme, et environ 9 fois moins de fetchs. Il lui manque en revanche le highlight
haut, et son alpha vaut 1 (pas 0.85-0.90).

Divergences skill ↔ charte à arbitrer :
- §3 impose son shader de référence avec `blur_amount` fixe à 2.0 ; la skill dit « ne pas écrire de shader » ;
- la teinte vaut 0.78 dans le shader de la charte contre 0.72 dans la skill.

Option de synthèse, si le chef la retient : partir de `dark_blur` (lod 3.5, mix 0.72 vers `#192930`) et y
ajouter `COLOR.a = 0.88` ainsi que le highlight haut corrigé (critère 3).

**S2, coins et ombre.** En mode shader, le verre est un `ColorRect` rectangulaire plus un liseré
`GlassEdge()` avec `SetCornerRadiusAll(0)` (`UltimaStyles.cs:41`), sans ombre. Radius 10 et ombre noir @0.35
taille 6 n'existent qu'au repli `Glass()` (`UltimaStyles.cs:15-29`). Pour converger sans second flou :
- un `Panel` ombre-seule derrière le `ColorRect` ;
- un masque rounded-rect (SDF sur `UV × panel_size`) dans le shader, et `GlassEdge` en radius 10.

**S3, opacité.** `RestAlpha = 0.35f` (`UltimaOverlay.cs:21`) contre 0.30 dans la skill. La skill demande aussi
« 1.0 au survol (150 ms) », alors que l'UI passe à 1.0 instantanément (`Expand` appelle `UpdateAlpha` avant le
Tween, `UltimaOverlay.Layout.cs:122-127`). Il n'y a pas de fondu d'alpha.

**S4, tailles.** La skill fixe un minimum de 14 px. Six labels sont en 12 px (`UltimaOverlay.cs:159,161,164`,
`EnemyRow.cs:101`, `ThreatList.cs:41,43`), ce qui reste conforme à docs/17 §3/§6 (« 12 px pour du secondaire »).
En 1440p, ces 12 px logiques font 16 px physiques (`canvas_items`).

**S5, autres points des skills :**
- Injection par Postfix sur `NCombatUi._Ready`, une instance par combat : **PASS**, c'est le motif de la skill
  (`CombatUiInjectionPatch.cs:11-24`).
- `MouseFilter = Ignore` partout : **PASS** (crit. 7). Il n'y a aucun enfant interactif, donc aucun `Stop` à
  remettre.
- Mise à jour sur événement (`CombatSetUp`, `CombatStateChanged`, `TurnStarted`, puis `CallDeferred`, au plus
  1×/frame, `CombatSession.cs:58-86`) sans `_Process` : **PASS**.
- Nettoyage `_ExitTree` : désabonnement de `SizeChanged` et `Kill` du Tween (`UltimaOverlay.cs:95-103`), arrêt
  de la session sur `TreeExiting` et sur `CombatEnded` (`CombatSession.cs:67,138-167`) : **PASS**.
- La skill recommande un `PanelContainer`, l'UI utilise un `Control` placé à la main. L'écart est justifié par
  le Tween et le clip, sans impact.
- **Manque vs charte §4** : le « Clic droit → menu (toggle détail, quitter) » n'est **pas implémenté**.
  `grep -rn "MouseButton\|ButtonIndex\|PopupMenu\|ScrollContainer" Ultima/UI Ultima/Patches` ne renvoie rien
  (exit=1). Le menu est en tension avec le click-through ; à arbitrer (le clavier F8/F9 couvre ces fonctions).
- Pas de `ScrollContainer` : au-delà de 40 % de la hauteur (432 px logiques), le contenu est **coupé** par le
  clip, sans défilement. Cela peut arriver avec 5 ennemis ou plus au niveau 2. À valider.

**S6, badge C1 `HandBadges.cs` (commit 896dc31, branche m7/badge, pas sur main).** Audit par `git show` :
```
$ git show 896dc31 -- Ultima/Features/Priority/HandBadges.cs | grep -n "new Color\|Color.FromHtml\|Colors\.\|MouseFilter\|_Process\|SetLoops\|NewLabel\|UltimaPalette\.\|Tween\|QueueFree\|Pulse\|Modulate"
16:    Aucun _Process, aucune animation, carte jamais modifiée. ...   (message de commit)
23:    grep "new Color(" HandBadges.cs vide.                          (message de commit)
221:+            badge.QueueFree();
240:+            MouseFilter = Control.MouseFilterEnum.Ignore,
252:+        var label = UltimaFonts.NewLabel(FontRole.Title, FontSize, UltimaPalette.Cream, trim: false);
266:+        var s = UltimaStyles.Badge(UltimaPalette.GlassTint);
267:+        s.BorderColor = UltimaPalette.Gold;
     (+ s.SetBorderWidthAll(UltimaStyles.BorderWidth) ; ZIndex = 1)
```
Le badge ne crée aucune couleur hors palette : fond `#192930` opaque, liseré gold 1 px, texte cream en Kreon
Bold 20. Il est en `MouseFilter = Ignore`, sans `_Process` ni Tween, sans flou (§3 : pas de blur sur les
badges), et il est libéré par `QueueFree`. **PASS** à la lecture. À valider en jeu : le badge masque une partie
de l'art de la carte, et le gold à 100 % est un accent (§2), sur 1 px seulement.

---

## À valider en jeu (porte humaine)

1. **FPS ≥ 55** en combat avec le flou (`blur_amount = 2.0`) sur 30 s de combat, FPS mini. Sinon passer à 1.0
   (§3), puis au repli StyleBoxFlat. Vérifier aussi l'aspect du 3×3 espacé de 15 px (fantôme ou flou ?) et la
   position du highlight.
2. **Glyphes** : `⚔` (U+2694) et `→` (U+2192) dans Kreon (`EnemyRow.cs:52`, `OverlayText.cs:45`). Si Kreon ne
   les a pas, vérifier qu'il n'y a pas de tofu, ou que le repli système est acceptable. Si le correctif du
   critère 10 est appliqué, faire la même vérification pour `→`, `·` et `É` dans Source Code Pro.
3. **Lisibilité à 1440p** : rail de 8×62 px logiques (10.7×82.7 px physiques) à alpha 0.35 au repos, pastilles
   grises ou rouges sur décor chargé. Textes en 12 px logiques (16 px physiques).
4. **Chevauchement réel** en 1920×1080, 2560×1440 et 3440×1440 :
   - le panneau déployé ne doit couvrir ni la barre de PV ni les powers de l'ennemi le plus à droite, ni la
     main (cartes survolées comprises), ni les intents, ni le bouton fin de tour ;
   - logger `AvoidRects.Collect(...).Count` et le rect de `ui.Hand` pour savoir si la main dépasse le seuil
     des 50 %.
5. Rien du jeu ne bouge pendant l'expansion (overlay pur) et il n'y a aucun clic perdu sur la zone du panneau
   (click-through).
