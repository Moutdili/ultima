using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace Ultima.Patches;

/// <summary>
/// Injecte l'overlay ULTIMA dans l'écran de combat. Une instance par combat.
/// Cible vérifiée : NCombatUi._Ready (même approche que sts2decktracker, v0.107.1).
/// </summary>
[HarmonyPatch(typeof(NCombatUi), nameof(NCombatUi._Ready))]
internal static class CombatUiInjectionPatch
{
    private static void Postfix(NCombatUi __instance)
    {
        try
        {
            var overlay = new Overlay.UltimaOverlay
            {
                Name = "UltimaOverlay",
                Position = new Vector2(20, 160),
            };
            __instance.AddChild(overlay);
            ModEntry.Write("overlay injected");
        }
        catch (Exception ex)
        {
            ModEntry.Write($"overlay injection failed: {ex.Message}");
        }
    }
}
