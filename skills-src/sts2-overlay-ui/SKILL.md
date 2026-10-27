---
name: sts2-overlay-ui
description: Use when building the in-game overlay for a Slay the Spire 2 mod with Godot Control nodes (injection, click-through, scrolling, opacity, hotkeys, layout pitfalls).
---

# STS2 overlay UI (Godot 4 + C#)

## Injection

```csharp
[HarmonyPatch(typeof(NCombatUi), nameof(NCombatUi._Ready))]
internal static class CombatUiInjectionPatch
{
    private static void Postfix(NCombatUi __instance)
    {
        try { __instance.AddChild(new UltimaOverlay()); }
        catch (Exception ex) { ModEntry.Write(ex.ToString()); }
    }
}
```

`NCombatUi` n'est **pas** un singleton : une instance par combat, détruite à la fin.
Recréer l'overlay dans `_Ready` est donc correct (pattern vérifié chez 999dulgi).

## Glassmorphism (obligatoire)

Le jeu ship `game/pck/shaders/dark_blur.gdshader` (`hint_screen_texture` + `lod` + mix).
ULTIMA l'adapte : `lod=3.5`, `mix_percentage=0.72` vers `#192930`, + StyleBoxFlat
(`bg #192930 @0.72`, border `#EFC851` @0.18, radius 10, shadow 0.35).
Ne pas écrire un shader from scratch, ne pas blurer les enfants (coût plein écran).

Détail et palette : skill `sts2-design-system` + `docs/17-charte.md`.

## Règles d'UX

- Ne jamais couvrir la main, les intents, ni les boutons de fin de tour.
- Panneau vertical ~260 px, ancré à droite ; opacité repos 0.30, 1.0 au survol (150 ms).
- `F8` toggle, `F9` détail. Aucun pulse permanent.
- Létal : texte + couleur (pas seulement rouge).

## Pièges Godot (vécus)

| Piège | Solution |
|---|---|
| Overlay bloque les clics | `MouseFilter = Ignore` sur le root, puis remettre `Stop` sur les enfants interactifs |
| Scroll mort | `_UnhandledInput` + wheel manuel quand `MouseFilter = Ignore` |
| Scrollbar visible | `GetVScrollBar().Modulate = Colors.Transparent` en `CallDeferred` |
| Layout effondré | `SizeFlagsVertical = ExpandFill` sur les parents du `ScrollContainer` |
| Fuite de node | cleanup dans `_ExitTree` (plus fiable que `_Notification`) |
| Texte illisible | reprendre la police/tailles du thème du jeu, min 14 px |

## Structure recommandée

```
UltimaOverlay (PanelContainer, MouseFilter=Ignore)
├── VBox
│   ├── PlayerStrip   (HP, block, tour)
│   ├── EnemyList     (1 bloc par ennemi : dégâts, weak, létal)
│   ├── PriorityStrip (F3)
│   └── ScoreFooter   (F4)
```

Mettre à jour **sur événement** (hook de combat / card play), pas dans `_Process` à 60 fps.
