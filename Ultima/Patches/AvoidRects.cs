using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace Ultima.Patches;

/// <summary>
/// Zones que l'overlay ne doit jamais couvrir (SPEC F7) : intents, main, bouton de fin de tour.
/// Lecture de géométrie de nodes uniquement (read-only). Sources vérifiées v0.107.1 :
/// NCombatUi.Hand (:70), NCombatUi.EndTurnButton (:60), NCombatRoom.CreatureNodes (:90),
/// NCreature.IntentContainer (:99).
/// </summary>
internal static class AvoidRects
{
    public static IReadOnlyList<Rect2> Collect(NCombatUi ui)
    {
        var rects = new List<Rect2>(16);
        try
        {
            // Un conteneur plein écran (ex. la main ancrée en full rect) rendrait tout placement
            // impossible : on ignore les zones > 50 % du viewport, seules les zones utiles comptent.
            var vp = ui.GetViewportRect().Size;
            _maxArea = vp.X * vp.Y * 0.5f;

            AddControl(rects, ui.EndTurnButton);
            AddControl(rects, ui.Hand);

            var room = NCombatRoom.Instance;
            if (room != null && IsLive(room))
            {
                foreach (var creature in room.CreatureNodes)
                {
                    if (!IsLive(creature)) continue;
                    var intents = creature.IntentContainer;
                    AddControl(rects, intents);
                    if (intents == null || !IsLive(intents)) continue;
                    foreach (var child in intents.GetChildren())
                        if (child is Control c) AddControl(rects, c);
                }
            }
        }
        catch (Exception ex)
        {
            ModEntry.Write($"avoid: collect failed {ex.GetType().Name}: {ex.Message}");
        }
        return rects;
    }

    private static float _maxArea = float.MaxValue;

    private static void AddControl(List<Rect2> rects, Control? c)
    {
        if (c == null || !IsLive(c) || !c.IsVisibleInTree()) return;
        var r = c.GetGlobalRect();
        var area = r.Size.X * r.Size.Y;
        if (area > 0 && area <= _maxArea) rects.Add(r);
    }

    private static bool IsLive(GodotObject? o) =>
        o != null && GodotObject.IsInstanceValid(o) && !(o is Node n && n.IsQueuedForDeletion());
}
