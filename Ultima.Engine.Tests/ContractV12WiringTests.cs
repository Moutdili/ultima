using Ultima.Engine.Contracts;
using Ultima.Engine.Scoring;
using Ultima.Engine.Scoring.Events;
using Ultima.Engine.Scoring.Paths;
using Ultima.Engine.Scoring.Team;
using Ultima.Saves.Model;
using Xunit;

namespace Ultima.Engine.Tests;

/// <summary>
/// Branchement du contrat v1.2 (additif, 2026-10-29) sur les briques Hermes : carte → route,
/// événement → conseil, alliés → co-op, et ΔG signé des malédictions.
/// </summary>
public sealed class ContractV12WiringTests
{
    private static ScoringContext Ctx(int hp = 70, int maxHp = 70, int gold = 100, int act = 0) =>
        new("SILENT", act, null, 0, 1, Array.Empty<CardInstance>(), Array.Empty<string>(), gold, hp, maxHp);

    private static MapNodeView N(int col, int row, string type, params MapCoordView[] children) =>
        new(col, row, type, children, false);

    private static MapView Map(MapCoordView? position, params MapNodeView[] nodes) =>
        new(0, 7, 3, nodes, position, new MapCoordView(1, 2));

    [Fact]
    public void RoutePlanner_rend_le_chemin_jusqu_au_boss()
    {
        var map = Map(new MapCoordView(1, 0),
            N(1, 0, RoomTypes.Monster, new MapCoordView(1, 1)),
            N(1, 1, RoomTypes.Elite, new MapCoordView(1, 2)),
            N(1, 2, RoomTypes.Boss));

        var route = new RoutePlanner().Plan(map, Ctx());

        Assert.NotNull(route);
        Assert.Equal(3, route!.Path.Count);
        Assert.Equal(new MapCoordView(1, 0), route.Path[0]);
        Assert.Equal(new MapCoordView(1, 2), route.Path[^1]);
        Assert.NotNull(route.ExpectedHpAtBoss);
        Assert.NotEmpty(route.Terms);
        Assert.False(string.IsNullOrWhiteSpace(route.Basis));
    }

    [Fact]
    public void RoutePlanner_signale_une_route_mortelle()
    {
        var map = Map(new MapCoordView(0, 0),
            N(0, 0, RoomTypes.Elite, new MapCoordView(0, 1)),
            N(0, 1, RoomTypes.Boss));

        var route = new RoutePlanner().Plan(map, Ctx(hp: 3, maxHp: 70));

        Assert.NotNull(route);
        Assert.True(route!.MayBeLethal, "3 PV face à une élite (21 dégâts) doit être signalé mortel");
    }

    [Fact]
    public void RoutePlanner_sans_position_prend_la_meilleure_entree()
    {
        var map = Map(null,
            N(0, 0, RoomTypes.Monster, new MapCoordView(0, 1)),
            N(1, 0, RoomTypes.Treasure, new MapCoordView(1, 1)),
            N(0, 1, RoomTypes.Boss),
            N(1, 1, RoomTypes.Boss));

        var route = new RoutePlanner().Plan(map, Ctx());

        Assert.NotNull(route);
        // Trésor (10,4) > monstre (3,8) à pleine vie : la meilleure entrée est la colonne 1.
        Assert.Equal(new MapCoordView(1, 0), route!.Path[0]);
    }

    [Fact]
    public void RoutePlanner_rend_null_sans_carte()
    {
        var map = new MapView(0, 7, 3, Array.Empty<MapNodeView>(), null, new MapCoordView(1, 2));
        Assert.Null(new RoutePlanner().Plan(map, Ctx()));
    }

    [Fact]
    public void EventAdvisor_note_une_option_mesuree()
    {
        var scores = new EventAdvisor().Rank("SUNKEN_TREASURY", new[] { "SECOND_CHEST" }, Ctx(gold: 100));

        var score = Assert.Single(scores);
        Assert.NotNull(score);
        Assert.True(score!.DeltaG > 0, "le second coffre rapporte de l'or : ΔG positif");
        Assert.InRange(score.Total, 0, 100);
        Assert.NotEmpty(score.Terms);
    }

    [Fact]
    public void EventAdvisor_rend_null_sans_profil_mesure()
    {
        var scores = new EventAdvisor().Rank("SUNKEN_TREASURY", new[] { "OPTION_INCONNUE" }, Ctx());
        Assert.Null(Assert.Single(scores));
    }

    [Fact]
    public void EventAdvisor_note_une_relique_proposee_par_un_ancien()
    {
        // Un Ancien propose des reliques : l'option est un id de relique, pas un texte d'événement.
        var scores = new EventAdvisor().Rank("ANCIENT_EVENT", new[] { "RELIC.AKABEKO" }, Ctx(gold: 100));

        var score = Assert.Single(scores);
        Assert.NotNull(score);
        Assert.Contains(score!.Terms, t => t.Label == "Offre d'Ancien" && t.Points == 0);
        Assert.InRange(score.Total, 0, 100);
    }

    [Fact]
    public void EventAdvisor_rend_null_pour_une_option_qui_n_est_ni_texte_ni_relique()
    {
        var scores = new EventAdvisor().Rank("ANCIENT_EVENT", new[] { "PAS_UNE_RELIQUE" }, Ctx());
        Assert.Null(Assert.Single(scores));
    }

    [Fact]
    public void Malediction_porte_un_DeltaG_negatif()
    {
        var score = new Scorer().ScoreCard(new CardInstance("INJURY", 0, null, null), Ctx());

        Assert.NotNull(score.DeltaG);
        Assert.True(score.DeltaG < 0, "une malédiction doit peser négativement sur G");
        Assert.InRange(score.Total, 0, 100);   // la lettre reste bornée
    }

    [Fact]
    public void Carte_saine_n_a_pas_de_DeltaG_negatif()
    {
        var score = new Scorer().ScoreCard(new CardInstance("STRIKE", 0, null, null), Ctx());
        Assert.True(score.DeltaG is null || score.DeltaG >= 0);
    }

    [Fact]
    public void Coop_utilise_les_allies_du_contexte()
    {
        var ally = new AllyContext(2, "IRONCLAD", 10, 80,
            new[] { new CardInstance("DEFEND", 0, null, null) }, Array.Empty<string>());

        var ctx = Ctx() with { Allies = new[] { ally } };
        var verdict = new CoopScorer().Evaluate(new CardInstance("DEFEND", 0, null, null), ctx);

        Assert.Equal(2, verdict.PerAllyDelta.Count);   // porteur + allié
        Assert.InRange(verdict.BestAllyIndex, 0, 1);   // le meilleur destinataire est calculé
    }
}
