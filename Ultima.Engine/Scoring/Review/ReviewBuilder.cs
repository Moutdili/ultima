using Ultima.Engine.Contracts;
using Ultima.Engine.Scoring.Deck;
using Ultima.Engine.Scoring.Relics;
using Ultima.Saves.Model;

namespace Ultima.Engine.Scoring.Review;

/// <summary>
/// Revue d'une run passée (contrat v1.2, <see cref="IReviewBuilder"/>) : reconstruit la timeline
/// et chiffre les décisions <b>qui peuvent l'être</b>.
/// </summary>
/// <remarks>
/// <para>
/// v1 : les <b>choix de cartes</b> sont notés (ΔG de chaque option offerte, regret = meilleur ΔG
/// moins ΔG pris). Les autres décisions (relique, boutique, repos, événement) sont présentes dans
/// la timeline avec leur type et l'identifiant choisi, mais déclarées <b>non chiffrées</b> — on ne
/// fabrique pas un regret sans mesure.
/// </para>
/// <para>
/// Le deck d'un étage est reconstitué depuis le deck final en gardant les cartes dont
/// <c>FloorAdded &lt;= étage</c>. Les cartes retirées ne sont pas dans le deck final : la
/// reconstitution est donc <b>une borne</b>, écrite dans la couverture de la revue.
/// </para>
/// </remarks>
public sealed class ReviewBuilder : IReviewBuilder
{
    public const int ReviewVersion = 1;
    public const string ScorerVersion = "scoring-v1";
    public const string DataBasis =
        "RunRecord (history) : étages, choix de cartes, PV/or par étage ; deck reconstitué " +
        "depuis le deck final (cartes retirées non reconstituables) ; reliques = set final.";

    private readonly Scorer _scorer;
    private readonly CardCatalog _cards;
    private readonly RelicCatalog _relics;

    public ReviewBuilder(Scorer? scorer = null, CardCatalog? cards = null, RelicCatalog? relics = null)
    {
        _scorer = scorer ?? new Scorer();
        _cards = cards ?? CardCatalog.Default;
        _relics = relics ?? RelicCatalog.Load();
    }

    public ReviewTimeline Build(RunRecord run)
    {
        ArgumentNullException.ThrowIfNull(run);

        var points = new List<ReviewPoint>(run.Floors.Count);
        var states = new List<ReviewState>(run.Floors.Count);
        var reasons = new SortedSet<string>(StringComparer.Ordinal);
        var byAct = new Dictionary<int, (int Scored, double Total)>();
        var regrets = new List<(int Index, double Regret)>();

        var character = run.Character();
        var finals = run.Players.ToDictionary(p => p.Id, p => p);
        var relicsOfPlayer = run.Players.ToDictionary(p => p.Id, p => p.Relics.Select(r => r.Id).ToArray());

        var stateIndex = 0;
        var pointIndex = 0;
        var scored = 0;
        var notEvaluable = 0;
        var regretTotal = 0.0;

        foreach (var floor in run.Floors)
        {
            foreach (var stats in floor.PlayerStats)
            {
                var final = finals.TryGetValue(stats.PlayerId, out var player) ? player : null;
                var deckAtFloor = DeckAtFloor(final, floor.Floor);
                var relics = relicsOfPlayer.TryGetValue(stats.PlayerId, out var relicIds) ? relicIds : Array.Empty<string>();

                var ctx = new ScoringContext(
                    character, floor.ActIndex, null, run.Ascension, floor.Floor,
                    deckAtFloor, relics, stats.CurrentGold, stats.CurrentHp, stats.MaxHp);

                states.Add(new ReviewState(
                    stateIndex, floor.Floor, stats.CurrentHp, stats.MaxHp, stats.CurrentGold,
                    deckAtFloor.Select(CardViewOf).ToArray(),
                    relics.Select(RelicViewOf).ToArray(),
                    Array.Empty<PotionView>()));
                // Potions : pas d'historique par étage dans le RunRecord → liste vide explicite.

                var kind = KindOf(stats);
                var chosen = ChosenOf(stats, kind);
                var options = new List<ReviewOption>();
                ReviewRegret? regret = null;
                string? note = null;

                if (kind == ReviewKind.CardReward && stats.CardChoices.Count > 0)
                {
                    var best = double.NegativeInfinity;
                    string? bestId = null;

                    foreach (var choice in stats.CardChoices)
                    {
                        var score = _scorer.ScoreCard(choice.Card, ctx);
                        var delta = score.DeltaG;
                        if (delta is { } d && d > best)
                        {
                            best = d;
                            bestId = choice.Card.Id;
                        }

                        options.Add(new ReviewOption(
                            "card", choice.Card.Id, NameOf(choice.Card.Id), delta, choice.WasPicked,
                            true, null, score.Confidence, score.Terms));
                    }

                    if (!double.IsNegativeInfinity(best))
                    {
                        // Passer vaut 0 (on ne prend rien) : c'est la référence quand rien n'est pris.
                        var picked = options.Where(o => o.Picked && o.DeltaG is not null)
                            .Select(o => o.DeltaG!.Value)
                            .DefaultIfEmpty(0)
                            .First();

                        var r = Math.Max(0, best - picked);
                        regret = new ReviewRegret(
                            Math.Round(r, 2), RegretBands.FromRegret(r), bestId, "medium",
                            $"meilleur ΔG {best:0.##} ({(bestId is null ? "aucune" : NameOf(bestId))}), " +
                            $"pris {(options.Any(o => o.Picked) ? picked.ToString("0.##") : "rien (0)")}");

                        scored++;
                        regretTotal += r;
                        regrets.Add((pointIndex, r));
                        if (!byAct.TryGetValue(floor.ActIndex, out var act)) act = (0, 0);
                        byAct[floor.ActIndex] = (act.Scored + 1, act.Total + r);
                    }
                    else
                    {
                        notEvaluable++;
                        reasons.Add("ΔG indisponible pour les cartes offertes (données de scoring manquantes)");
                        note = "Non chiffré : ΔG indisponible pour cette offre.";
                    }
                }
                else if (kind != ReviewKind.None)
                {
                    notEvaluable++;
                    reasons.Add($"décision « {kind} » non chiffrée en v1");
                    note = $"Non chiffré en v1 : {kind}.";
                }

                points.Add(new ReviewPoint(
                    pointIndex, floor.Floor, floor.ActIndex,
                    string.IsNullOrWhiteSpace(floor.RawType) ? floor.Kind.ToString() : floor.RawType,
                    floor.ModelId,
                    floor.Rooms.SelectMany(room => room.MonsterIds).ToArray(),
                    floor.Rooms.Sum(room => room.TurnsTaken),
                    stats.PlayerId,
                    null, stateIndex,
                    new ReviewDelta(stats.DamageTaken, stats.HpHealed,
                        stats.GoldGained - stats.GoldSpent - stats.GoldLost - stats.GoldStolen,
                        stats.MaxHpGained - stats.MaxHpLost),
                    kind, chosen, options, regret, note));

                stateIndex++;
                pointIndex++;
            }
        }

        var worst = regrets.OrderByDescending(r => r.Regret).ThenBy(r => r.Index).Take(5)
            .Select(r => r.Index).ToArray();
        var top3 = regrets.OrderByDescending(r => r.Regret).Take(3).Sum(r => r.Regret);
        var concentration = regretTotal > 0 ? Math.Round(top3 / regretTotal, 3) : 0;

        var summary = new ReviewSummary(
            Points: points.Count,
            Scored: scored,
            NotEvaluable: notEvaluable,
            RegretTotal: Math.Round(regretTotal, 2),
            RegretMean: scored > 0 ? Math.Round(regretTotal / scored, 2) : 0,
            WorstPoints: worst,
            Concentration: concentration,
            ByAct: byAct.OrderBy(kv => kv.Key)
                .Select(kv => new ReviewActRegret(kv.Key, kv.Value.Scored, Math.Round(kv.Value.Total, 2)))
                .ToArray(),
            Note: scored == 0
                ? "Aucune décision chiffrable : la revue ne peut rien dire d'autre que le déroulé."
                : $"Regret moyen {Math.Round(regretTotal / Math.Max(1, scored), 2)} points de G sur {scored} décision(s).");

        var coverage = new ReviewCoverage(
            scored, notEvaluable, reasons.ToArray(), DataBasis);

        var lanes = run.Players.Select(p => new ReviewLane(
            p.Id, p.CharacterId,
            p.Deck.Select(CardViewOf).ToArray(),
            p.Relics.Select(rel => RelicViewOf(rel.Id)).ToArray())).ToArray();

        var header = new ReviewRun(
            RunId: run.FileName,
            BuildId: run.BuildId,
            GameMode: run.GameMode,
            Ascension: run.Ascension,
            CharacterId: character,
            Seed: null,
            StartedAt: DateTimeOffset.FromUnixTimeSeconds(run.StartTime),
            RunTimeSeconds: (int)Math.Max(0, run.RunTime),
            Win: run.Win,
            WasAbandoned: run.WasAbandoned,
            KilledBy: run.KilledByEncounter ?? run.KilledByEvent,
            Source: "RunRecord",
            ScorerVersion: ScorerVersion,
            WeightsHash: null,
            DataBasis: DataBasis);

        return new ReviewTimeline(
            ReviewVersion, header, lanes, states, points, summary, coverage);
    }

    /// <summary>
    /// Deck à un étage : cartes du deck final dont <c>FloorAdded &lt;= étage</c>. Si l'information
    /// manque (FloorAdded null), on garde le deck final entier plutôt que de rendre un deck vide.
    /// </summary>
    private static IReadOnlyList<CardInstance> DeckAtFloor(RunRecordPlayer? player, int floor)
    {
        if (player is null) return Array.Empty<CardInstance>();

        var known = player.Deck.Where(c => c.FloorAdded is not null).ToArray();
        if (known.Length == 0) return player.Deck.ToArray();

        var atFloor = player.Deck.Where(c => (c.FloorAdded ?? int.MaxValue) <= floor).ToArray();
        return atFloor.Length == 0 ? player.Deck.ToArray() : atFloor;
    }

    private CardView CardViewOf(CardInstance card)
    {
        var entry = _cards.Get(card.Id);
        return new CardView(
            card.Id,
            entry?.DisplayName ?? NameFormat.FromClassName(card.Id),
            entry?.Type ?? "",
            entry?.Rarity ?? "",
            entry?.Cost ?? 0,
            card.UpgradeLevel,
            card.Enchantment is null
                ? null
                : new EnchantmentView(card.Enchantment.Id, card.Enchantment.Id, card.Enchantment.Amount, ""),
            entry?.IsCurse ?? false,
            null);
    }

    private RelicView RelicViewOf(string relicId)
    {
        var entry = _relics.Get(relicId);
        return new RelicView(relicId, entry?.NameFr ?? NameFormat.FromClassName(relicId),
            entry?.DescriptionFr ?? "", null);
    }

    private string NameOf(string cardId) => _cards.Get(cardId)?.DisplayName ?? NameFormat.FromClassName(cardId);

    private static ReviewKind KindOf(PlayerFloorStats stats)
    {
        if (stats.CardChoices.Count > 0) return ReviewKind.CardReward;
        if (stats.RelicChoices.Count > 0) return ReviewKind.RelicReward;
        if (stats.AncientChoices.Count > 0) return ReviewKind.Ancient;
        if (stats.EventChoiceKeys.Count > 0) return ReviewKind.Event;
        if (stats.RestSiteChoices.Count > 0) return ReviewKind.Rest;
        if (stats.CardsRemoved.Count > 0) return ReviewKind.Removal;
        if (stats.CardsTransformed.Count > 0) return ReviewKind.Transform;
        if (stats.BoughtRelics.Count > 0 || stats.BoughtPotions.Count > 0 || stats.BoughtColorless.Count > 0)
            return ReviewKind.Shop;
        if (stats.PotionsUsed.Count > 0 || stats.PotionsDiscarded.Count > 0) return ReviewKind.Potion;
        return ReviewKind.None;
    }

    private static ReviewChoice? ChosenOf(PlayerFloorStats stats, ReviewKind kind) => kind switch
    {
        ReviewKind.CardReward => stats.CardsPicked.Select(c => new ReviewChoice("card", c.Id)).FirstOrDefault(),
        ReviewKind.RelicReward => stats.RelicsPicked.Select(id => new ReviewChoice("relic", id)).FirstOrDefault(),
        ReviewKind.Event => stats.EventChoiceKeys.Select(k => new ReviewChoice("event", k)).FirstOrDefault(),
        ReviewKind.Ancient => stats.AncientChoices.Where(a => a.WasChosen)
            .Select(a => new ReviewChoice("ancient", a.TextKey)).FirstOrDefault(),
        ReviewKind.Rest => stats.RestSiteChoices.Select(k => new ReviewChoice("rest", k)).FirstOrDefault(),
        ReviewKind.Removal => stats.CardsRemoved.Select(c => new ReviewChoice("removal", c.Id)).FirstOrDefault(),
        ReviewKind.Transform => stats.CardsTransformed.Select(t => new ReviewChoice("transform", t.Final.Id)).FirstOrDefault(),
        _ => null,
    };
}
