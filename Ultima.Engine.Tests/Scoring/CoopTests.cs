using System.Text.Json;
using Ultima.Engine.Contracts;
using Ultima.Engine.Scoring;
using Ultima.Engine.Scoring.Cards;
using Ultima.Engine.Scoring.Strength;
using Ultima.Engine.Scoring.Team;
using Ultima.Saves.Model;
using Xunit;

namespace Ultima.Engine.Tests.Scoring;

/// <summary>
/// S23 — cartes de soutien co-op (docs/33 §2, « Co-op » = non implémenté jusqu'ici).
/// <para>
/// Les 13 cartes à cible <c>AnyAlly</c> (8) ou <c>AllAllies</c> (5) sont indexées dans
/// <c>data/catalog/coop_cards.json</c>. Le trou mesuré : <see cref="PlayerStrengthCalculator.DeltaForCard"/>.
/// évalue une carte sur le deck du porteur ; un don de 11 bloc « à un autre joueur » n'y ajoute rien,
/// donc le scoring la jette. <see cref="CoopScorer"/> calcule le ΔG sur <b>le deck de chaque allié</b>
/// et ne recommande la cession que si l'écart atteint <see cref="CoopScorer.CedingThreshold"/>.
/// </para>
/// <para>
/// Chiffres de référence (acte 1, Waterfall Giant — 15 de pire coup, 240 PV), vérifiables à la main :
/// un deck « tortue » (12 Defend+ et 1 Strike) bloque 22,2/tour (survie 66, saturée) ; un deck
/// « verre » (10 Strikes) bloque 0/tour et, à 20 PV, a une survie de 12. Lift (11 bloc à un allié)
/// vaut ΔG +0 pour la tortue et +7 pour le verre — écart 7 ≥ 3, donc « céder ».
/// </para>
/// </summary>
public sealed class CoopTests
{
    private const string BossAct1 = "ENCOUNTER.WATERFALL_GIANT_BOSS";

    private static CardInstance Card(string id, int upgrade = 0) => new(id, upgrade, null, null);

    /// <summary>Lift : « Give another player 11 Block » (AnyAlly, coût 1) — le soutien le plus net.</summary>
    private static CardInstance Lift() => Card("CARD.LIFT");

    /// <summary>Deck sans aucune carte de bloc : la dimension survie n'a que les PV.</summary>
    private static List<CardInstance> Glass() =>
        Enumerable.Repeat(Card("CARD.STRIKE_IRONCLAD"), 10).ToList();

    /// <summary>Deck dont la survie est déjà saturée : 12 Defend+ (8 bloc) et un Strike.</summary>
    private static List<CardInstance> Turtles() =>
        Enumerable.Repeat(Card("CARD.DEFEND_IRONCLAD", 1), 12).Append(Card("CARD.STRIKE_IRONCLAD")).ToList();

    private static ScoringContext Ctx(string character, IEnumerable<CardInstance> deck, int hp = 70, int maxHp = 70) =>
        new(character, 0, BossAct1, 0, 10, deck.ToArray(), Array.Empty<string>(), 99, hp, maxHp);

    [Fact]
    public void Support_card_is_worth_more_to_an_ally_with_an_empty_dimension()
    {
        var carrier = Ctx("CHARACTER.IRONCLAD", Turtles());
        var ally = Ctx("CHARACTER.SILENT", Glass(), hp: 20);
        var scorer = new CoopScorer();

        // La dimension est réellement vide chez l'allié (0 bloc), saturée chez le porteur (66).
        Assert.Equal(0, PlayerStrength.G(ally).Measures["blockPerTurn"]);
        Assert.True(PlayerStrength.G(carrier).Measures["blockPerTurn"] > 20);
        Assert.True(PlayerStrength.G(carrier).Survie > PlayerStrength.G(ally).Survie + 30);

        var verdict = scorer.Evaluate(Lift(), new[] { carrier, ally });

        // Le meilleur destinataire est l'allié, et il gagne au moins le seuil de plus que le porteur.
        Assert.Equal(1, verdict.BestAllyIndex);
        Assert.Equal(0, verdict.CarrierDelta);                       // porteur saturé : le don ne vaut rien
        Assert.True(verdict.BestDelta >= CoopScorer.CedingThreshold,
            $"ΔG allié {verdict.BestDelta} < seuil {CoopScorer.CedingThreshold}");
        Assert.True(verdict.GainOverCarrier >= CoopScorer.CedingThreshold,
            $"écart {verdict.GainOverCarrier} < seuil {CoopScorer.CedingThreshold}");
        Assert.True(scorer.ShouldTake(Lift(), new[] { carrier, ally }));
        Assert.False(verdict.CarrierIsBest);

        // La phrase porte les chiffres et la décision (docs/27 §4).
        Assert.Contains("céder", verdict.Reason);
        Assert.Contains($"+{verdict.BestDelta}", verdict.Reason);
    }

    [Fact]
    public void Carrier_is_the_target_when_it_gains_the_most()
    {
        // Mêmes decks, rôles inversés : c'est le porteur qui a la dimension vide.
        var carrier = Ctx("CHARACTER.IRONCLAD", Glass(), hp: 20);
        var ally = Ctx("CHARACTER.SILENT", Turtles());
        var scorer = new CoopScorer();

        var verdict = scorer.Evaluate(Lift(), new[] { carrier, ally });

        Assert.Equal(0, verdict.BestAllyIndex);
        Assert.True(verdict.CarrierIsBest);
        Assert.Equal(verdict.CarrierDelta, verdict.BestDelta);
        Assert.Equal(0, verdict.GainOverCarrier);
        Assert.True(verdict.PerAllyDelta[0] >= verdict.PerAllyDelta[1] + CoopScorer.CedingThreshold);
        Assert.False(scorer.ShouldTake(Lift(), new[] { carrier, ally }));
        Assert.Contains("garder", verdict.Reason);
    }

    [Fact]
    public void Without_ally_the_solo_behaviour_is_unchanged()
    {
        var carrier = Ctx("CHARACTER.IRONCLAD", Glass(), hp: 50);
        var scorer = new CoopScorer();

        // Un seul contexte = le porteur seul : ΔG identique au calcul solo, aucune cession possible.
        var solo = scorer.Evaluate(Lift(), new[] { carrier });
        Assert.Single(solo.PerAllyDelta);
        Assert.Equal(new PlayerStrengthCalculator().DeltaForCard(carrier, Lift()), solo.PerAllyDelta[0]);
        Assert.Equal(0, solo.BestAllyIndex);
        Assert.Equal(0, solo.GainOverCarrier);
        Assert.False(scorer.ShouldTake(Lift(), new[] { carrier }));
        Assert.Contains("solo", solo.Reason);

        // Liste vide : pas de contexte, pas de verdict — jamais d'exception.
        var none = scorer.Evaluate(Lift(), Array.Empty<ScoringContext>());
        Assert.Empty(none.PerAllyDelta);
        Assert.Equal(-1, none.BestAllyIndex);
        Assert.Equal(0, none.BestDelta);
        Assert.False(scorer.ShouldTake(Lift(), Array.Empty<ScoringContext>()));
        Assert.Contains("aucun contexte", none.Reason);

        // Entrée nulle traitée comme vide.
        Assert.False(scorer.ShouldTake(Lift(), null));
    }

    [Fact]
    public void Identical_deltas_do_not_recommend_a_transfer()
    {
        // Deux joueurs au deck identique : aucun ne gagne plus que l'autre, l'écart est nul.
        var first = Ctx("CHARACTER.IRONCLAD", Glass(), hp: 50);
        var second = Ctx("CHARACTER.SILENT", Glass(), hp: 50);
        var scorer = new CoopScorer();

        var verdict = scorer.Evaluate(Lift(), new[] { first, second });

        Assert.All(verdict.PerAllyDelta, delta => Assert.Equal(verdict.PerAllyDelta[0], delta));
        Assert.Equal(0, verdict.BestAllyIndex);                     // égalité ⇒ le porteur, jamais un allié
        Assert.Equal(0, verdict.GainOverCarrier);
        Assert.True(verdict.GainOverCarrier < CoopScorer.CedingThreshold);
        Assert.False(scorer.ShouldTake(Lift(), new[] { first, second }));

        // Un écart de 1 ou 2 points reste sous le seuil : deux decks « presque » identiques.
        var nearly = new CoopVerdict("X", 1, new[] { 4, 6 }, "test");
        Assert.False(CoopScorer.Recommends(nearly));
        Assert.True(CoopScorer.Recommends(nearly with { PerAllyDelta = new[] { 4, 7 } }));
    }

    [Fact]
    public void Indexed_coop_cards_match_the_engine_catalog()
    {
        var path = Path.Combine(ScoringData.ResolveCatalogDir(), "coop_cards.json");
        Assert.True(File.Exists(path), $"index de soutien absent : {path} (lancer tools/index_coop.py)");

        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var cards = doc.RootElement.GetProperty("cards");
        Assert.Equal(13, cards.GetArrayLength());

        var effects = CardEffects.Default;
        var indexed = new List<string>();
        int anyAlly = 0, allAllies = 0;

        foreach (var element in cards.EnumerateArray())
        {
            var id = element.GetProperty("id").GetString();
            var target = element.GetProperty("target").GetString();
            Assert.False(string.IsNullOrWhiteSpace(id));

            var effect = effects.Get(id);
            Assert.NotNull(effect);
            Assert.Equal(target, effect!.Target);                   // pas d'index périmé
            Assert.True(CoopScorer.IsSupportTarget(target), $"{id} n'est pas une cible de soutien");

            if (target == "AnyAlly") anyAlly++;
            else allAllies++;
            indexed.Add(effect.Id);
        }

        Assert.Equal(8, anyAlly);
        Assert.Equal(5, allAllies);

        // Le catalogue ne contient pas d'autre carte de soutien que les 13 indexées.
        var fromCatalog = effects.All
            .Where(e => CoopScorer.IsSupportTarget(e.Target))
            .Select(e => e.Id)
            .OrderBy(id => id, StringComparer.Ordinal);
        Assert.Equal(fromCatalog, indexed.OrderBy(id => id, StringComparer.Ordinal));
    }
}
