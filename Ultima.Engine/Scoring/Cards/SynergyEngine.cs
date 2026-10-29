using Ultima.Engine.Contracts;
using Ultima.Engine.Scoring.Model;
using Ultima.Saves.Model;

namespace Ultima.Engine.Scoring.Cards;

/// <summary>
/// Synergies explicables : tags mécaniques de la carte candidate confrontés au deck et aux reliques.
/// <para>
/// Règles (aucune boîte noire, chaque terme nomme sa règle) :
/// <list type="bullet">
///   <item><b>Deck</b> : pour chaque tag de la carte, si le deck contient au moins le seuil de cartes
///   portant ce tag (3, ou 5 pour <c>power</c>), on ajoute <b>+3</b>. Plafond total <b>+15</b>.
///   Libellé : « Poison : 5 cartes au deck » ; source : le tag, sa justification et la règle.</item>
///   <item><b>Reliques</b> : affinité de cluster — si la carte et une relique possédée appartiennent
///   au même archétype de <c>data/meta/archetypes.json</c> (clé <c>key_relics</c>), <b>+2</b> par
///   relique, plafond <b>+5</b>. Aucune relique n'est taguée à la main.</item>
/// </list>
/// </para>
/// Les tags viennent de <see cref="CardTags"/> (nom anglais + type + archétypes), jamais d'une liste
/// d'ids codée en dur. Ordre de sortie déterministe : tags triés, puis reliques dans l'ordre du deck.
/// </summary>
public sealed class SynergyEngine
{
    private const int PointsPerTag = 3;
    private const int DeckCap = 15;
    private const int PointsPerRelic = 2;
    private const int RelicCap = 5;

    private readonly IMetaTable _meta;
    private readonly Dictionary<string, IReadOnlyList<CardTags.TagHit>> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public SynergyEngine(IMetaTable meta) => _meta = meta;

    /// <summary>Tags mécaniques d'une carte (triés, dédupliqués). Vide si la carte est inconnue.</summary>
    public IReadOnlyList<string> Tags(CardInstance card) => Hits(card).Select(h => h.Tag).ToArray();

    /// <summary>Termes de synergie deck/reliques pour une carte candidate.</summary>
    public IReadOnlyList<ScoreTerm> Terms(CardInstance card, ScoringContext ctx)
    {
        var hits = Hits(card);
        var meta = _meta.Card(card.Id);
        var terms = new List<ScoreTerm>();

        // 1) Synergie deck : +3 par tag présent au moins « seuil » fois, plafond +15.
        var awarded = 0;
        foreach (var hit in hits)
        {
            if (awarded + PointsPerTag > DeckCap) break;

            var threshold = CardTags.ThresholdOf(hit.Tag);
            var count = CountInDeck(hit.Tag, ctx.Deck);
            if (count < threshold) continue;

            awarded += PointsPerTag;
            terms.Add(new ScoreTerm(
                $"{hit.Label} : {count} cartes au deck",
                PointsPerTag,
                $"tag « {hit.Tag} » ({hit.Basis}) ; règle : ≥{threshold} cartes au deck → +{PointsPerTag}, plafond {DeckCap}"));
        }

        // 2) Synergie reliques : affinité de cluster carte ↔ relique (archetypes.json), plafond +5.
        if (meta != null && ctx.RelicIds.Count > 0)
        {
            var relicPoints = 0;
            foreach (var archetype in _meta.Archetypes(Ids.CharacterKey(meta.Owner)))
            {
                if (relicPoints >= RelicCap) break;
                if (!archetype.KeyCardIds.Contains(meta.Id, StringComparer.OrdinalIgnoreCase)) continue;

                foreach (var relicId in ctx.RelicIds)
                {
                    if (relicPoints >= RelicCap) break;
                    var bare = Ids.Bare(relicId);
                    if (!archetype.KeyRelicIds.Contains(bare, StringComparer.OrdinalIgnoreCase)) continue;

                    var name = _meta.Relic(bare)?.DisplayName ?? bare;
                    relicPoints += PointsPerRelic;
                    terms.Add(new ScoreTerm(
                        $"Cluster « {archetype.Name} » : {name}",
                        PointsPerRelic,
                        $"data/meta/archetypes.json — relique {bare} et carte {meta.Id} dans le même cluster (co-occurrence observée)"));
                }
            }
        }

        return terms;
    }

    private IReadOnlyList<CardTags.TagHit> Hits(CardInstance card)
    {
        var key = Ids.Bare(card.Id);
        if (string.IsNullOrEmpty(key)) return Array.Empty<CardTags.TagHit>();
        if (_cache.TryGetValue(key, out var cached)) return cached;

        var hits = CardTags.For(_meta.Card(key), _meta);
        _cache[key] = hits;
        return hits;
    }

    private int CountInDeck(string tag, IReadOnlyList<CardInstance> deck)
    {
        var count = 0;
        foreach (var card in deck)
            if (Hits(card).Any(h => h.Tag == tag)) count++;
        return count;
    }
}
