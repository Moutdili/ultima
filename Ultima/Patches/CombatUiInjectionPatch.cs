using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace Ultima.Patches;

/// <summary>
/// Injecte l'overlay ULTIMA dans l'écran de combat et démarre la session de composition.
/// Une instance par combat. Cible vérifiée : NCombatUi._Ready (même approche que sts2decktracker, v0.107.1).
/// </summary>
[HarmonyPatch(typeof(NCombatUi), nameof(NCombatUi._Ready))]
internal static class CombatUiInjectionPatch
{
    private static void Postfix(NCombatUi __instance)
    {
        try
        {
            CombatSession.Start(__instance);
        }
        catch (Exception ex)
        {
            ModEntry.Write($"overlay injection failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
