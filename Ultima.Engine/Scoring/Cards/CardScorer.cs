using Ultima.Engine.Contracts;
using Ultima.Engine.Scoring.Model;
using Ultima.Engine.Scoring.Personal;
using Ultima.Saves.Model;

namespace Ultima.Engine.Scoring.Cards;

/// <summary>
/// Score d'une carte en récompense (0-100, explicable) et score de l'option « passer ».
/// Formule figée (docs/24-hermes-arch.md §2) — somme de termes nommés, bornée 0..100 :
/// <list type="bullet">
///   <item><b>Méta communautaire</b> : round(consensus × 0.55), source <see cref="SourceLabel"/> ;</item>
///   <item><b>Enchantement</b> : +3 + min(2, amount) ;</item>
///   <item><b>Malédiction</b> : −30 ;</item>
///   <item><b>Historique perso</b> : clamp(round(delta × 40), −8, +8), seulement si ≥5 runs des deux côtés ;</item>
///   <item><b>Synergie deck / reliques</b> : déléguée à <see cref="SynergyEngine"/> (+3/tag, cap 15 ; reliques 0..+5) ;</item>
///   <item><b>Adéquation boss</b> : −10..+10, lue dans <see cref="BossProfile"/> (multi-coups, débuffs, scaling) ;</item>
///   <item><b>Courbe de coût</b> : −5..+5, densité du deck au même coût.</item>
/// </list>
/// Aucun terme à 0 point n'est conservé : un score sans terme est un score sans donnée.
/// </summary>
public sealed class CardScorer
{
    /// <summary>Part du consensus communautaire retenue (≤ 55 points).</summary>
    private const double ConsensusWeight = 0.55;

    /// <summary>En dessous, la courbe de coût n'a pas de sens statistique.</summary>
    private const int MinDeckForCurve = 5;

    private readonly IMetaTable _meta;
    private readonly IPersonalStats _personal;
    private readonly SynergyEngine _synergy;

    public CardScorer(IMetaTable meta, IPersonalStats personal, SynergyEngine synergy)
    {
        _meta = meta;
        _personal = personal;
        _synergy = synergy;
    }

    public Score ScoreCard(CardInstance card, ScoringContext ctx)
    {
        var b = new ScoreBuilder();
        var meta = _meta.Card(card.Id);

        if (meta?.Consensus is double consensus)
        {
            // Calibration (décision Mio : notation S→F) : consensus 65 (médiane réelle) ≈ 50 = C,
            // 100 ≈ 72 = A ; seules synergies/reliques/boss peuvent porter à S (85+).
            int calibrated = Math.Min(78, 7 + ScoreMath.Round(consensus * 0.65));
            b.Add("Méta communautaire", calibrated, SourceLabel(meta));
        }

        if (card.Enchantment != null)
            b.Add($"Enchantement {card.Enchantment.Id}", 3 + Math.Min(2, card.Enchantment.Amount), "enchantement");

        if (IsCurse(meta))
            b.Add("Malédiction", -30, meta!.DisplayName);

        var stats = _personal.Card(card.Id);
        if (stats?.WinRateDelta is double delta)
        {
            b.Add("Ton historique", ScoreMath.Clamp(ScoreMath.Round(delta * 40), -8, 8),
                $"tes {stats.RunsWithCard + stats.RunsWithoutCard} runs");
        }

        b.AddRange(_synergy.Terms(card, ctx));

        if (BossFit(card, ctx) is { } bossFit) b.Add(bossFit);
        if (CostCurve(meta, ctx) is { } curve) b.Add(curve);

        return b.Build(Confidence.Of(meta?.SourceCount ?? 0, _personal.RunCount));
    }

    /// <summary>
    /// Score de l'option « passer » : round(skipRate × 60) − round(meilleure carte offerte × 0.35),
    /// borné 0..100. Liste vide ⇒ seule la référence communautaire compte.
    /// </summary>
    public Score ScoreSkip(IReadOnlyList<CardInstance> offered, ScoringContext ctx)
    {
        var b = new ScoreBuilder();
        double? skip = _meta.SkipRate;
        if (skip.HasValue)
            b.Add("Taux de passage communautaire", ScoreMath.Round(skip.Value * 60),
                "Spire Codex (toutes versions)");

        var best = 0;
        if (offered != null)
            foreach (var card in offered)
                best = Math.Max(best, ScoreCard(card, ctx).Total);

        b.Add("Meilleure carte offerte", -ScoreMath.Round(best * 0.35), "comparaison des cartes proposées");

        return b.Build(Confidence.Of(_meta.SkipRate.HasValue ? 1 : 0, _personal.RunCount));
    }

    /// <summary>
    /// Adéquation au boss de l'acte : seuls comptent les traits réellement présents dans les moves du
    /// monstre (data/catalog/monsters.json via <see cref="BossProfile"/>). Boss inconnu ⇒ aucun terme.
    /// </summary>
    private ScoreTerm? BossFit(CardInstance card, ScoringContext ctx)
    {
        var boss = BossProfile.For(ctx.BossId);
        if (boss == null) return null;

        var tags = new HashSet<string>(_synergy.Tags(card), StringComparer.Ordinal);
        var points = 0;
        var reasons = new List<string>();

        if (boss.MultiHits)
        {
            if (tags.Contains("block"))
            {
                points += 3;
                reasons.Add("bloc contre les multi-coups");
            }

            if (tags.Contains("weak"))
            {
                points += 2;
                reasons.Add("Faiblesse réduit chaque coup");
            }
        }

        if (boss.Scaling)
        {
            if (tags.Contains("poison"))
            {
                points += 3;
                reasons.Add("dégâts dans le temps contre un boss qui monte");
            }

            if (tags.Contains("strength"))
            {
                points += 2;
                reasons.Add("course à la force");
            }

            if (tags.Contains("power"))
            {
                points += 1;
                reasons.Add("moteur de scaling");
            }
        }

        if (boss.Sustains && tags.Contains("poison"))
        {
            points += 2;
            reasons.Add("le poison passe avant les soins");
        }

        if (!boss.HasAttack && tags.Contains("block"))
        {
            points -= 3;
            reasons.Add("le boss n'attaque pas : le bloc ne sert à rien");
        }

        if (points == 0) return null;

        points = ScoreMath.Clamp(points, -10, 10);
        return new ScoreTerm($"Adéquation boss — {boss.Name}", points,
            $"data/catalog/monsters.json : {string.Join(", ", reasons)} ({boss.Evidence})");
    }

    /// <summary>
    /// Courbe de coût : compare le coût de la carte à la densité du deck au même coût.
    /// Coût absent du deck ⇒ +3 (place libre) ; ≥50 % du deck ⇒ −4 ; ≥35 % ⇒ −2 ; ≤10 % ⇒ +2.
    /// Ignoré sous <see cref="MinDeckForCurve"/> cartes (pas de courbe sur un deck naissant).
    /// </summary>
    private ScoreTerm? CostCurve(MetaEntry? meta, ScoringContext ctx)
    {
        if (meta?.Cost is not int cost) return null;
        if (ctx.Deck.Count < MinDeckForCurve) return null;

        var same = 0;
        var known = 0;
        foreach (var card in ctx.Deck)
        {
            if (_meta.Card(card.Id)?.Cost is not int deckCost) continue;
            known++;
            if (deckCost == cost) same++;
        }

        if (known == 0) return null;

        var share = (double)same / known;
        int points;
        string verdict;

        if (same == 0)
        {
            points = 3;
            verdict = $"aucune carte à {cost} énergie sur {known} : ce coût est encore libre";
        }
        else if (share >= 0.5)
        {
            points = -4;
            verdict = $"{same}/{known} cartes à {cost} énergie : ce coût sature le deck";
        }
        else if (share >= 0.35)
        {
            points = -2;
            verdict = $"{same}/{known} cartes à {cost} énergie : ce coût est déjà bien fourni";
        }
        else if (share <= 0.1)
        {
            points = 2;
            verdict = $"{same}/{known} cartes à {cost} énergie : ce coût est rare dans le deck";
        }
        else
        {
            return null;
        }

        return new ScoreTerm($"Courbe de coût à {cost} énergie", ScoreMath.Clamp(points, -5, 5),
            $"deck courant : {verdict}");
    }

    internal static bool IsCurse(MetaEntry? meta) =>
        meta != null && ((meta.Type ?? "").Contains("Curse", StringComparison.OrdinalIgnoreCase)
                         || (meta.Rarity ?? "").Contains("Curse", StringComparison.OrdinalIgnoreCase));

    internal static string SourceLabel(MetaEntry meta)
    {
        var parts = meta.Ratings.Select(r => r.SourceId).Distinct().Take(2).ToArray();
        var who = parts.Length > 0 ? string.Join(" + ", parts) : "méta";
        return $"{who} (n={meta.SourceCount} source(s), écart {meta.Spread:0.#})";
    }
}
