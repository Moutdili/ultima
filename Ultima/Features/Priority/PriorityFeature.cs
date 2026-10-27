using System;
using System.Collections.Generic;
using Ultima.Core.Model;
using Ultima.Core.Priority;

namespace Ultima.Features.Priority;

/// <summary>
/// F3 côté mod : alertes de priorité de la main locale (règles pures dans <see cref="PriorityRules"/>).
/// Read-only : ne lit que le snapshot. Ne lève jamais d'exception vers l'appelant.
/// </summary>
public static class PriorityFeature
{
    private const string ResourceName = "Ultima.Catalog.priority_cards.json";

    private static readonly object Gate = new();
    private static PriorityCatalog? _catalog;

    /// <summary>Catalogue embarqué (ressource "Ultima.Catalog.priority_cards.json", chargé une fois, paresseux).</summary>
    internal static PriorityCatalog Catalog
    {
        get
        {
            var c = _catalog;
            if (c != null) return c;
            lock (Gate)
            {
                return _catalog ??= LoadEmbedded();
            }
        }
    }

    /// <summary>
    /// Alertes F3 de la main locale (recalculées à chaque snapshot).
    /// Catalogue embarqué (ressource "Ultima.Catalog.priority_cards.json", chargé une fois, paresseux).
    /// </summary>
    public static IReadOnlyList<PriorityAlert> LocalAlerts(CombatSnapshot snapshot)
    {
        try
        {
            if (snapshot == null) return Array.Empty<PriorityAlert>();
            return PriorityRules.FromHand(snapshot, Catalog);
        }
        catch (Exception ex)
        {
            ModEntry.Write($"priority: local alerts failed {ex.GetType().Name}: {ex.Message}");
            return Array.Empty<PriorityAlert>();
        }
    }

    private static PriorityCatalog LoadEmbedded()
    {
        try
        {
            using var stream = typeof(PriorityFeature).Assembly.GetManifestResourceStream(ResourceName);
            if (stream == null)
            {
                ModEntry.Write($"priority: ressource {ResourceName} absente, catalogue vide");
                return PriorityCatalog.Empty;
            }
            var catalog = PriorityCatalog.Load(stream);
            ModEntry.Write($"priority: catalogue charge, {catalog.Count} entrees");
            return catalog;
        }
        catch (Exception ex)
        {
            ModEntry.Write($"priority: catalogue illisible {ex.GetType().Name}: {ex.Message}");
            return PriorityCatalog.Empty;
        }
    }
}
