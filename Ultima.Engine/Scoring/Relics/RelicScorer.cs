using Ultima.Engine.Contracts;
using Ultima.Engine.Scoring.Cards;
using Ultima.Engine.Scoring.Model;
using Ultima.Engine.Scoring.Personal;
using Ultima.Saves.Model;

namespace Ultima.Engine.Scoring.Relics;

/// <summary>
/// Score d'une relique (récompense, shop). Formule figée (docs/24-hermes-arch.md §2) :
/// <c>consensus × 0.75 + synergie deck/personnage (0..+5)</c>, total borné 0..100.
///
/// Une relique sans note chiffrée — catégorie d'Ancien (« Always Amazing », « Needs Synergy »…
/// non converties en score, cf. <c>score_conventions.ancient_category</c> de
/// data/meta/relic_tiers.json) ou entrée absente de la méta — ne reçoit <b>jamais</b> un faux
/// chiffre : le score se limite à la synergie et un terme explicite dit pourquoi.
/// </summary>
public sealed class RelicScorer
{
    /// <summary>Plafond de la synergie deck/personnage (formule figée : 0..+5).</summary>
    public const int MaxSynergy = 5;

    private readonly IMetaTable _meta;
    private readonly IPersonalStats _personal;

    public RelicScorer(IMetaTable meta, IPersonalStats personal)
    {
        _meta = meta;
        _personal = personal;
    }

    public Score Score(string relicId, ScoringContext ctx)
    {
        var meta = _meta.Relic(relicId);

        // Relique inconnue : score borné + terme explicite, jamais une exception.
        if (meta == null)
            return NoData($"relique « {Ids.Bare(relicId)} » absente de data/meta/relic_tiers.json");

        var b = new ScoreBuilder();
        string? note = null, noteSource = null;

        if (meta.Consensus is double consensus)
        {
            b.Add("Méta communautaire", ScoreMath.Round(ScoreMath.Clamp(consensus, 0, 100) * 0.75),
                CardScorer.SourceLabel(meta));
        }
        else
        {
            note = "Pas de note chiffrée";
            noteSource = NoScoreBasis(meta);
        }

        foreach (var term in Synergy(meta, ctx)) b.Add(term);

        var confidence = meta.Consensus.HasValue
            ? Confidence.Of(meta.SourceCount, _personal.RunCount)
            : Confidence.Unknown;

        return AddNote(b.Build(confidence), note, noteSource);
    }

    /// <summary>
    /// Synergie explicite, plafonnée à <see cref="MaxSynergy"/> :
    /// — relique du pool du personnage joué : +1 ;
    /// — relique clé d'un cluster d'archétype dont le deck contient des cartes clés : +1..+3
    ///   (une carte clé = +1, plafond 3), le cluster le mieux aligné seulement.
    /// </summary>
    private IEnumerable<ScoreTerm> Synergy(MetaEntry relic, ScoringContext ctx)
    {
        int total = 0;

        var owner = Ids.CharacterKey(relic.Owner);
        var character = Ids.CharacterKey(ctx.CharacterId);
        if (owner.Length > 0 && owner == character && total < MaxSynergy)
        {
            total += 1;
            yield return new ScoreTerm("Relique de ton personnage", 1, $"{owner} (pool du personnage)");
        }

        var deck = ctx.Deck ?? Array.Empty<CardInstance>();
        if (deck.Count == 0) yield break;

        var deckIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var card in deck) deckIds.Add(Ids.Bare(card.Id));

        ScoreTerm? best = null;
        int bestPoints = 0;

        foreach (var archetype in _meta.Archetypes(ctx.CharacterId))
        {
            if (!Contains(archetype.KeyRelicIds, relic.Id)) continue;

            int present = archetype.KeyCardIds.Count(k => deckIds.Contains(Ids.Bare(k)));
            if (present < 2) continue; // une seule carte clé ne fait pas un cluster

            int points = Math.Min(3, present);
            if (points <= bestPoints) continue;

            bestPoints = points;
            best = new ScoreTerm($"Cluster « {archetype.Name} »", points,
                $"{present}/{archetype.KeyCardIds.Count} cartes clés du cluster dans ton deck");
        }

        if (best == null) yield break;

        if (total + bestPoints > MaxSynergy) bestPoints = MaxSynergy - total;
        if (bestPoints > 0) yield return best with { Points = bestPoints };
    }

    private static bool Contains(IReadOnlyList<string> ids, string relicId)
    {
        foreach (var id in ids)
            if (string.Equals(Ids.Bare(id), relicId, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>Pourquoi aucune note : catégorie d'Ancien (non convertie) ou source muette.</summary>
    private static string NoScoreBasis(MetaEntry meta)
    {
        bool ancient = string.Equals(meta.Owner, "ancient_event", StringComparison.OrdinalIgnoreCase);
        return ancient
            ? "catégorie d'Ancien non convertie en score (relic_tiers.json → score_conventions.ancient_category)"
            : $"aucune source chiffrée pour {Ids.Bare(meta.Id)} (0 source)";
    }

    /// <summary>Score 0 borné, confiance inconnue, une seule ligne : « aucune donnée ».</summary>
    private static Score NoData(string reason) =>
        new(0, new[] { new ScoreTerm("Aucune donnée", 0, reason) }, Confidence.Unknown);

    /// <summary>
    /// Ajoute un terme informatif à 0 point (ScoreBuilder ignore les 0) pour que l'absence de
    /// note soit lisible dans l'UI au lieu d'un total muet.
    /// </summary>
    private static Score AddNote(Score score, string? label, string? source)
    {
        if (string.IsNullOrWhiteSpace(label)) return score;
        var terms = new List<ScoreTerm>(score.Terms) { new(label!, 0, source ?? "data/meta") };
        return score with { Terms = terms };
    }
}
