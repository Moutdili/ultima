using Ultima.Engine.Contracts;
using Ultima.Engine.Scoring.Model;

namespace Ultima.Engine.Scoring.Events;

/// <summary>
/// Contrat v1.2 : branche les options d'un événement sur <see cref="EventScorer"/>.
/// Une option sans profil mesuré rend <c>null</c> — jamais un 50 déguisé en note.
/// </summary>
public sealed class EventAdvisor : IEventAdvisor
{
    /// <summary>Base chiffrée commune aux termes affichés.</summary>
    public const string Basis = "EventScorer v1 · 199 options profilées sur 321 076 choix (80 000 runs solo)";

    /// <summary>Point neutre du barème d'événement (le score part de 50).</summary>
    public const int Neutral = 50;

    private readonly EventScorer _scorer;

    public EventAdvisor(EventScorer? scorer = null) => _scorer = scorer ?? new EventScorer();

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
}
