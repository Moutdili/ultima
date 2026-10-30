using Ultima.Engine.Contracts;
using Ultima.Engine.Scoring.Model;
using Ultima.Engine.Scoring.Meta;
using Ultima.Engine.Scoring.Personal;
using Ultima.Engine.Scoring.Relics;

namespace Ultima.Engine.Scoring.Events;

/// <summary>
/// Contrat v1.2 : branche les options d'un événement sur <see cref="EventScorer"/>.
/// Une option sans profil mesuré rend <c>null</c> — jamais un 50 déguisé en note.
/// </summary>
/// <remarks>
/// <b>Anciens (T5 §4).</b> Un Ancien n'est pas un événement à options textuelles : il propose des
/// <i>reliques</i>. Une option qui correspond à une relique du catalogue est donc notée par le
/// <see cref="RelicScorer"/> (même barème que le butin), avec un terme à 0 point qui dit d'où
/// vient l'offre. Aucune option inconnue n'est devinée : <c>null</c>.
/// </remarks>
public sealed class EventAdvisor : IEventAdvisor
{
    /// <summary>Base chiffrée commune aux termes affichés.</summary>
    public const string Basis = "EventScorer v1 · 199 options profilées sur 321 076 choix (80 000 runs solo)";

    /// <summary>Point neutre du barème d'événement (le score part de 50).</summary>
    public const int Neutral = 50;

    private static readonly Lazy<(RelicScorer Scorer, RelicCatalog Catalog)> LazyRelics = new(
        () => (new RelicScorer(MetaTable.Load(), PersonalStats.Empty()), RelicCatalog.Load()),
        LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly EventScorer _scorer;
    private readonly RelicScorer? _relics;
    private readonly RelicCatalog? _catalog;

    public EventAdvisor(EventScorer? scorer = null, RelicScorer? relics = null, RelicCatalog? catalog = null)
    {
        _scorer = scorer ?? new EventScorer();
        _relics = relics;
        _catalog = catalog;
    }

    public EventScorer Scorer => _scorer;

    public IReadOnlyList<Score?> Rank(string eventId, IReadOnlyList<string> optionIds, ScoringContext ctx)
    {
        var result = new Score?[optionIds.Count];
        if (string.IsNullOrWhiteSpace(eventId) || optionIds.Count == 0) return result;

        var eventContext = new EventContext(ctx.Hp, ctx.MaxHp, ctx.Gold, ctx.Ascension);

        for (var i = 0; i < optionIds.Count; i++)
        {
            var option = optionIds[i];
            if (string.IsNullOrWhiteSpace(option)) continue;

            // 1. Option = relique proposée (Ancien) : même barème que le butin.
            var relic = RelicScore(option, ctx);
            if (relic is not null)
            {
                result[i] = relic;
                continue;
            }

            // 2. Option d'événement profilée.
            var choice = _scorer.Score(eventId, option, eventContext);

            // Trois états seulement : mesuré → note, non mesuré → null explicite.
            if (!choice.Measured) continue;

            result[i] = new Score(ScoreMath.Clamp(choice.Score, 0, 100), choice.Terms, Confidence.Medium)
            {
                // ΔG signé : le barème d'événement part de 50, donc 50 = neutre.
                DeltaG = choice.Score - Neutral,
            };
        }

        return result;
    }

    /// <summary>
    /// Note d'une option qui est une relique (offre d'Ancien). <c>null</c> si l'identifiant n'est
    /// pas une relique du catalogue — on ne devine pas qu'une option est un objet.
    /// </summary>
    private Score? RelicScore(string option, ScoringContext ctx)
    {
        // Appelé tel quel par le daemon (`new EventAdvisor()`), l'advisor se dote d'un scorer de
        // reliques complet : une offre d'Ancien doit être notable sans câblage supplémentaire.
        var (relics, catalog) = _relics is not null && _catalog is not null
            ? (_relics, _catalog)
            : LazyRelics.Value;

        if (catalog.Get(option) is null) return null;

        var score = relics.Score(option, ctx);
        var terms = new List<ScoreTerm>(score.Terms)
        {
            new("Offre d'Ancien", 0, "relique proposée par un Ancien — notée comme le butin, pas comme un texte d'événement"),
        };

        return score with { Terms = terms };
    }
}
