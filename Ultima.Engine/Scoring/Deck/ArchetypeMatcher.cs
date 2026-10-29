using Ultima.Engine.Scoring.Model;

namespace Ultima.Engine.Scoring.Deck;

/// <summary>
/// Correspondance entre un deck et un archétype de <c>data/meta/archetypes.json</c>.
/// <see cref="Match"/> ∈ [0,1] = part pondérée des cartes clés présentes + part des reliques clés.
/// </summary>
public sealed record ArchetypeMatch(
    string Name,
    double Match,
    int KeyCardsPresent,
    int KeyCards,
    int KeyRelicsPresent,
    int KeyRelics,
    string Kind)
{
    /// <summary>Libellé lisible pour un terme de score (« 2/3 cartes clés, 1/2 reliques »).</summary>
    public string Describe() => $"{KeyCardsPresent}/{KeyCards} cartes clés, {KeyRelicsPresent}/{KeyRelics} reliques";
}

/// <summary>
/// Calcule et classe les correspondances d'archétype d'un deck. Aucun nom inventé :
/// les noms viennent tous de <see cref="IMetaTable.Archetypes(string)"/>. Déterministe
/// (tri par score puis par nom) : mêmes entrées → mêmes sorties.
/// </summary>
public static class ArchetypeMatcher
{
    /// <summary>Poids des cartes clés dans la correspondance (le reste = reliques clés).</summary>
    public const double KeyCardWeight = 0.75;

    /// <summary>Poids des reliques clés.</summary>
    public const double KeyRelicWeight = 0.25;

    /// <summary>Bonus par copie supplémentaire d'une carte clé (plafonné à 3 copies).</summary>
    public const double DuplicateBonus = 0.05;

    /// <summary>Seuil documenté pour retenir un archétype principal (part pondérée).</summary>
    public const double ChosenThreshold = 0.50;

    /// <summary>Nombre minimum de cartes clés distinctes pour retenir un archétype.</summary>
    public const int MinKeyCardsForChosen = 2;

    /// <summary>Nombre d'archétypes potentiels exposés (top 3, noms distincts).</summary>
    public const int PotentialCount = 3;

    /// <summary>
    /// Classe les archétypes par correspondance décroissante. Les archétypes sans aucune carte ni
    /// relique clé dans le deck sont exclus (match = 0 → aucun nom à afficher).
    /// </summary>
    public static IReadOnlyList<ArchetypeMatch> Rank(
        IReadOnlyList<MetaArchetype> archetypes,
        IReadOnlyDictionary<string, int> deckCounts,
        IReadOnlyCollection<string> relicIds)
    {
        var relics = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in relicIds) relics.Add(Ids.Bare(r));

        var matches = new List<ArchetypeMatch>();
        foreach (var archetype in archetypes)
        {
            var m = Score(archetype, deckCounts, relics);
            if (m.Match > 0) matches.Add(m);
        }

        return matches
            .OrderByDescending(m => m.Match)
            .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>Correspondance d'un archétype donné.</summary>
    public static ArchetypeMatch Score(
        MetaArchetype archetype, IReadOnlyDictionary<string, int> deckCounts, IReadOnlyCollection<string> relicIds)
    {
        var keyCards = archetype.KeyCardIds.Select(Ids.Bare).Where(id => id.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var keyRelics = archetype.KeyRelicIds.Select(Ids.Bare).Where(id => id.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var relics = relicIds as ISet<string> ?? new HashSet<string>(relicIds.Select(Ids.Bare), StringComparer.OrdinalIgnoreCase);

        double cardPoints = 0;
        int cardsPresent = 0;
        foreach (var key in keyCards)
        {
            if (!deckCounts.TryGetValue(key, out var copies) || copies <= 0) continue;
            cardsPresent++;
            cardPoints += 1 + DuplicateBonus * (Math.Min(3, copies) - 1);
        }

        int relicsPresent = keyRelics.Count(relics.Contains);

        double cardShare = keyCards.Length > 0 ? Math.Clamp(cardPoints / keyCards.Length, 0, 1) : 0;
        double relicShare = keyRelics.Length > 0 ? (double)relicsPresent / keyRelics.Length : 0;

        // Si l'archétype n'a qu'un côté (guide sans relique, cluster sans carte), on renormalise
        // sur le côté disponible au lieu de pénaliser un deck qui n'a rien à fournir.
        double match = keyCards.Length > 0 && keyRelics.Length > 0
            ? KeyCardWeight * cardShare + KeyRelicWeight * relicShare
            : keyCards.Length > 0 ? cardShare : relicShare;

        return new ArchetypeMatch(
            archetype.Name, Math.Clamp(match, 0, 1), cardsPresent, keyCards.Length, relicsPresent, keyRelics.Length,
            archetype.Kind);
    }

    /// <summary>
    /// Archétype principal : le mieux classé au-dessus du seuil, avec au moins
    /// <see cref="MinKeyCardsForChosen"/> cartes clés présentes (ou toutes si l'archétype en a moins).
    /// </summary>
    public static ArchetypeMatch? Choose(IReadOnlyList<ArchetypeMatch> ranked)
    {
        foreach (var m in ranked)
        {
            if (m.Match < ChosenThreshold) break; // trié : la suite est en dessous du seuil
            int required = Math.Min(MinKeyCardsForChosen, m.KeyCards);
            if (m.KeyCards > 0 && m.KeyCardsPresent < required) continue;
            if (m.KeyCards == 0 && m.KeyRelicsPresent < Math.Min(MinKeyCardsForChosen, m.KeyRelics)) continue;
            return m;
        }
        return null;
    }

    /// <summary>Top <see cref="PotentialCount"/> noms distincts (archétypes déjà classés).</summary>
    public static IReadOnlyList<string> Potential(IReadOnlyList<ArchetypeMatch> ranked) =>
        ranked.Select(m => m.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(PotentialCount)
            .ToArray();
}
