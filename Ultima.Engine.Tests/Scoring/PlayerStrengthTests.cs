using Ultima.Engine.Contracts;
using Ultima.Engine.Scoring.Strength;
using Ultima.Engine.Scoring.Threat;
using Ultima.Saves.Model;
using Xunit;

namespace Ultima.Engine.Tests.Scoring;

/// <summary>
/// Collection isolée : ce fichier ajoute une classe de tests gourmande en CPU (chargement des
/// catalogues) et <c>DeckTests</c> mesure une performance à 5 ms. La désactiver en parallèle évite
/// de fausser cette mesure sans toucher aux autres classes.
/// </summary>
/// <summary>
/// S11 — score global du joueur G (docs/27 §7bis) : Survie, Offense, Consistance, Couverture.
/// Les chiffres attendus viennent de <c>data/catalog/cards_effects.json</c> et
/// <c>data/catalog/monsters.json</c> : ils sont vérifiables à la main (Strike 6, Defend 5, Bash 8,
/// Waterfall Giant 15 de pire coup et 240 PV).
/// </summary>
public sealed class PlayerStrengthTests
{
    private const string BossAct1 = "ENCOUNTER.WATERFALL_GIANT_BOSS";

    private static CardInstance Card(string id, int upgrade = 0) => new(id, upgrade, null, null);

    private static CardInstance Strike(int upgrade = 0) => Card("CARD.STRIKE_IRONCLAD", upgrade);

    private static CardInstance Defend() => Card("CARD.DEFEND_IRONCLAD");

    private static CardInstance Bash(int upgrade = 0) => Card("CARD.BASH", upgrade);

    /// <summary>Deck de départ Ironclad : Strike ×5, Defend ×4, Bash.</summary>
    private static List<CardInstance> Starter() => new()
    {
        Strike(), Strike(), Strike(), Strike(), Strike(),
        Defend(), Defend(), Defend(), Defend(),
        Bash(),
    };

    private static ScoringContext Ctx(
        IEnumerable<CardInstance> deck,
        int hp = 70,
        int maxHp = 70,
        string? boss = BossAct1,
        int act = 0,
        IReadOnlyList<string>? relics = null) =>
        new("CHARACTER.IRONCLAD", act, boss, 0, 10, deck.ToArray(),
            relics ?? Array.Empty<string>(), 99, hp, maxHp);

    [Fact]
    public void Starter_deck_gives_bounded_reproducible_dimensions()
    {
        var deck = Starter();

        var first = PlayerStrength.G(Ctx(deck));
        var second = PlayerStrength.G(Ctx(deck));

        // Reproductible : mêmes entrées ⇒ mêmes sorties (aucun aléa, aucun état caché).
        Assert.Equal(first.Survie, second.Survie);
        Assert.Equal(first.Offense, second.Offense);
        Assert.Equal(first.Consistance, second.Consistance);
        Assert.Equal(first.Couverture, second.Couverture);
        Assert.Equal(first.Total, second.Total);

        // Chaque dimension et le total restent sur l'échelle 0..100.
        foreach (var value in new[] { first.Survie, first.Offense, first.Consistance, first.Couverture, first.Total })
        {
            Assert.InRange(value, 0, 100);
        }

        // Le deck de départ est faible mais pas nul : l'offense doit rester basse (6 dégâts par
        // Strike, 3 énergie/tour), la consistance haute (petit deck, coûts 1).
        Assert.InRange(first.Survie, 20, 80);
        Assert.InRange(first.Offense, 5, 60);
        Assert.True(first.Consistance > first.Offense,
            $"consistance {first.Consistance} devrait dépasser l'offense {first.Offense} sur un deck de départ");

        // Une entrée par dimension, avec des points et une source non vides.
        foreach (var label in new[] { "Survie", "Offense", "Consistance", "Couverture" })
        {
            var matches = first.Terms.Where(t => t.Label == label).ToArray();
            Assert.Single(matches);
            var term = matches[0];
            Assert.InRange(term.Points, 0, 100);
            Assert.False(string.IsNullOrWhiteSpace(term.Source), $"{label} sans source");
        }

        // Les mesures brutes sont exposées (nécessaires au ΔG et à l'explication).
        Assert.True(first.Measures["blockPerTurn"] > 0);
        Assert.True(first.Measures["damagePerTurn"] > 0);
        Assert.Equal(15, first.Measures["threat"]);   // pire coup de Waterfall Giant
        Assert.Equal(240, first.Measures["bossHp"]);
    }

    [Fact]
    public void Adding_defends_raises_survival_without_raising_offense()
    {
        var before = PlayerStrength.G(Ctx(Starter()));
        var after = PlayerStrength.G(Ctx(Starter().Concat(Enumerable.Repeat(Defend(), 5))));

        Assert.True(after.Survie > before.Survie,
            $"Survie {before.Survie} → {after.Survie} : 5 Defend doivent augmenter la survie");
        Assert.True(after.Offense <= before.Offense,
            $"Offense {before.Offense} → {after.Offense} : des Defend ne doivent pas augmenter l'offense");
        Assert.True(after.Measures["blockPerTurn"] > before.Measures["blockPerTurn"]);
    }

    [Fact]
    public void Adding_a_stronger_damage_card_raises_offense()
    {
        var before = PlayerStrength.G(Ctx(Starter()));
        // Un 2e Bash (8 dégâts) entre dans les 3 cartes jouées par tour : le plafond passe de
        // 8+6+6 à 8+8+6. C'est la grandeur que la dimension mesure depuis S17.
        var after = PlayerStrength.G(Ctx(Starter().Append(Bash())));

        Assert.True(after.Offense > before.Offense,
            $"Offense {before.Offense} → {after.Offense} : un 2e Bash doit augmenter l'offense");
        Assert.True(after.Measures["damagePerTurn"] > before.Measures["damagePerTurn"]);
    }

    [Fact]
    public void Redundant_strikes_do_not_raise_offense()
    {
        // S17 : l'offense est la somme des k meilleures cartes (k = cartes jouées par tour), pas
        // une moyenne du deck. 5 Strike de plus dans un deck de 11 cartes ne changent pas les 3
        // cartes jouées : le plafond est identique, donc ΔG d'une carte redondante ≈ 0 — c'est
        // l'idempotence de docs/27 §7bis. La densité moyenne, elle, bougeait (ancienne mesure).
        var before = PlayerStrength.G(Ctx(Starter()));
        var after = PlayerStrength.G(Ctx(Starter().Concat(Enumerable.Repeat(Strike(), 5))));

        Assert.Equal(before.Measures["damagePerTurn"], after.Measures["damagePerTurn"], 2);
        Assert.InRange(Math.Abs(after.Offense - before.Offense), 0, 1);
        Assert.True(after.Measures["damageDensityPerTurn"] > before.Measures["damageDensityPerTurn"],
            "la densité moyenne reste exposée comme mesure de diagnostic, et elle bouge");
    }

    [Fact]
    public void Power_with_scaling_raises_offense_without_dealing_damage()
    {
        // Inflame : pouvoir, 0 dégât, +2 Force. L'ancienne densité moyenne le pénalisait (une
        // carte à 0 dégât dilue Σdégâts/N) ; le burst top-k ne le compte pas comme dégât, mais le
        // scaling s'ajoute aux dégâts/tour — un deck de pouvoirs n'est donc plus puni.
        var before = PlayerStrength.G(Ctx(Starter()));
        var after = PlayerStrength.G(Ctx(Starter().Append(Card("CARD.INFLAME"))));

        Assert.Equal(before.Measures["damagePerTurn"], after.Measures["damagePerTurn"], 2);
        Assert.True(after.Measures["scaling"] > before.Measures["scaling"]);
        Assert.True(after.Offense > before.Offense,
            $"Offense {before.Offense} → {after.Offense} : +2 Force doit augmenter l'offense");
    }

    [Fact]
    public void Saturated_offense_plateaus_instead_of_growing()
    {
        // Le plafond de dégâts par tour est borné par les cartes réellement jouables : 40 Strike
        // ne sortent pas plus de dégâts par tour que 20, seule la moyenne du deck monterait.
        var twenty = PlayerStrength.G(Ctx(Enumerable.Repeat(Strike(), 20)));
        var forty = PlayerStrength.G(Ctx(Enumerable.Repeat(Strike(), 40)));

        Assert.True(twenty.Offense < 100, $"l'offense doit saturer sous 100, obtenu {twenty.Offense}");
        Assert.Equal(twenty.Measures["damagePerTurn"], forty.Measures["damagePerTurn"], 2);
        Assert.InRange(Math.Abs(forty.Offense - twenty.Offense), 0, 1);
    }

    [Fact]
    public void Saturated_defense_plateaus_instead_of_growing()
    {
        // 20 Defend couvrent déjà tout le tour : le débit par tour est plafonné par la pioche et
        // l'énergie, donc doubler le nombre de Defend n'ajoute plus de bloc/tour.
        var twenty = PlayerStrength.G(Ctx(Enumerable.Repeat(Defend(), 20)));
        var forty = PlayerStrength.G(Ctx(Enumerable.Repeat(Defend(), 40)));

        Assert.True(twenty.Survie < 100, $"la survie doit saturer sous 100, obtenu {twenty.Survie}");
        Assert.Equal(twenty.Measures["blockPerTurn"], forty.Measures["blockPerTurn"], 2);
        Assert.InRange(Math.Abs(forty.Survie - twenty.Survie), 0, 1);

        // Et un deck mono-Defend ne produit aucun dégât : le plafond n'est pas un score parfait.
        Assert.Equal(0, twenty.Offense);
    }

    [Fact]
    public void Empty_deck_is_zero_without_throwing()
    {
        var strength = PlayerStrength.G(Ctx(Array.Empty<CardInstance>()));

        Assert.Equal(0, strength.Survie);
        Assert.Equal(0, strength.Offense);
        Assert.Equal(0, strength.Consistance);
        Assert.Equal(0, strength.Couverture);
        Assert.Equal(0, strength.Total);
        Assert.NotEmpty(strength.Terms);
        Assert.Contains(strength.Terms, t => t.Label.Contains("insuffisantes", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Total_is_the_weighted_sum_of_the_dimensions()
    {
        var deck = Starter();
        foreach (var act in new[] { 0, 1, 2, 5 })
        {
            var strength = PlayerStrength.G(Ctx(deck, act: act));
            var weights = strength.Weights;

            Assert.Equal(1.0, weights.Sum, 6);
            Assert.Equal(
                weights.Apply(strength.Survie, strength.Offense, strength.Consistance, strength.Couverture),
                strength.Total);
        }

        // Les poids changent avec l'acte (acte 1 = survie d'abord, acte 3 = offense d'abord).
        Assert.True(StrengthWeights.Act1.Survie > StrengthWeights.Act3.Survie);
        Assert.True(StrengthWeights.Act3.Offense > StrengthWeights.Act1.Offense);
        Assert.Equal(StrengthWeights.Act3, StrengthWeights.ForActIndex(7));
    }

    [Fact]
    public void Lower_hp_lowers_survival_through_effective_hp()
    {
        var full = PlayerStrength.G(Ctx(Starter(), hp: 70));
        var hurt = PlayerStrength.G(Ctx(Starter(), hp: 20));

        Assert.True(hurt.Survie < full.Survie, $"Survie {full.Survie} à 70 PV, {hurt.Survie} à 20 PV");
        Assert.Equal(70, full.Measures["effectiveHp"]);
        Assert.Equal(20, hurt.Measures["effectiveHp"]);
    }

    [Fact]
    public void Unknown_cards_and_boss_never_throw()
    {
        // Carte absente du catalogue d'effets : comptée au deck, sans dégâts ni bloc, pénalité de 1.
        var withUnknown = PlayerStrength.G(Ctx(Starter().Append(Card("CARD.NOT_A_REAL_CARD"))));
        Assert.False(withUnknown.Confidence == "high", "une carte inconnue interdit la confiance haute");
        Assert.Contains(withUnknown.Terms, t => t.Label.Contains("insuffisantes", StringComparison.OrdinalIgnoreCase));
        Assert.InRange(withUnknown.Total, 0, 100);

        // Boss inconnu : repli documenté sur la médiane des boss de l'acte, jamais une exception.
        var unknownBoss = PlayerStrength.G(Ctx(Starter(), boss: "ENCOUNTER.NOPE"));
        Assert.InRange(unknownBoss.Total, 0, 100);
        Assert.True(unknownBoss.Measures["threat"] > 0);
        Assert.Contains(unknownBoss.Terms,
            t => t.Source.Contains("médiane", StringComparison.OrdinalIgnoreCase));

        // Boss absent (null) : même repli.
        var noBoss = PlayerStrength.G(Ctx(Starter(), boss: null));
        Assert.InRange(noBoss.Total, 0, 100);

        // Catalogue de menace vide : les dimensions qui en dépendent tombent à 0, avec le terme.
        var empty = new PlayerStrengthCalculator(threat: ThreatModel.Empty).Compute(Ctx(Starter()));
        Assert.Equal(0, empty.Survie);
        Assert.Equal(0, empty.Offense);
        Assert.True(empty.Consistance > 0);
        Assert.Contains(empty.Terms, t => t.Source.Contains("données insuffisantes", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Second_defend_is_worth_less_than_the_first()
    {
        // docs/27 §7bis : la valeur d'un item est ΔG = G(état + item) − G(état), et les dimensions
        // saturent — un Defend qui comble un trou vaut plus qu'un Defend de plus dans un deck couvert.
        var calc = new PlayerStrengthCalculator();
        var bare = Ctx(Enumerable.Repeat(Strike(), 10));
        var covered = Ctx(Enumerable.Repeat(Strike(), 10).Concat(Enumerable.Repeat(Defend(), 20)));

        var first = calc.DeltaForCard(bare, Defend());
        var second = calc.DeltaForCard(covered, Defend());

        Assert.True(first > 0, $"ΔG du 1er Defend = {first}, attendu > 0");
        Assert.True(second < first, $"ΔG du Defend en deck couvert = {second}, attendu < {first}");
        Assert.InRange(second, 0, 1);
    }

    [Fact]
    public void Boss_statuses_lower_effective_hp_and_survival()
    {
        var ctx = Ctx(Starter());
        var none = PlayerStrength.G(ctx);
        var vulnerable = PlayerStrength.G(ctx, new ThreatStatuses(Vulnerable: 2));

        // Vulnérable ×1,5 : 70 PV « valent » 47 face au pire coup (ThreatModel.EffectiveHp).
        Assert.Equal(47, vulnerable.Measures["effectiveHp"]);
        Assert.True(vulnerable.Survie < none.Survie,
            $"Survie {none.Survie} sans statut, {vulnerable.Survie} avec 2 Vulnérable");
    }

    [Fact]
    public void Relic_tags_feed_the_dimensions_they_serve()
    {
        // Ancre (tag block) et Sacoche de préparatifs (tag draw), passées avec le préfixe RELIC.
        var bare = PlayerStrength.G(Ctx(Starter()));
        var withRelics = PlayerStrength.G(Ctx(Starter(), relics: new[] { "RELIC.ANCHOR", "RELIC.BAG_OF_PREPARATION" }));

        Assert.True(withRelics.Measures["blockPerTurn"] > bare.Measures["blockPerTurn"]);
        Assert.True(withRelics.Measures["drawPerTurn"] > bare.Measures["drawPerTurn"]);
        Assert.Contains(withRelics.Terms, t => t.Label.StartsWith("Reliques", StringComparison.Ordinal));
    }

    [Fact]
    public void Upgraded_cards_improve_the_dimensions_they_serve()
    {
        var plain = PlayerStrength.G(Ctx(Enumerable.Repeat(Strike(), 10)));
        var upgraded = PlayerStrength.G(Ctx(Enumerable.Repeat(Strike(1), 10)));

        // Strike+ : 9 dégâts au lieu de 6 (cards_effects.json, damage_up).
        Assert.True(upgraded.Offense > plain.Offense,
            $"Offense {plain.Offense} → {upgraded.Offense} avec des Strike+");
    }
}
