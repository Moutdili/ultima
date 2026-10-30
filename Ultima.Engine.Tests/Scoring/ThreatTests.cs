using Ultima.Engine.Scoring.Deck;
using Ultima.Engine.Scoring.Threat;
using Xunit;

namespace Ultima.Engine.Tests.Scoring;

/// <summary>
/// S8 — modèle de menace : dégâts entrants par rencontre et par ascension, PV effectifs.
/// Les chiffres attendus viennent de <c>data/catalog/</c> (généré par
/// <c>tools/index_threats.py</c> depuis le codex) : ils sont vérifiables à la main.
/// Le modèle n'expose que « pire coup » et « dégâts moyens observés » — aucune rotation de
/// moves n'existe dans les données, donc aucune prédiction tour par tour n'est testée.
/// </summary>
public sealed class ThreatTests
{
    private static readonly ThreatModel Model = ThreatModel.Default;

    [Fact]
    public void Catalog_loads_monsters_and_encounters()
    {
        Assert.True(Model.MonsterCount >= 110, $"monstres chargés: {Model.MonsterCount}");
        Assert.True(Model.EncounterCount >= 80, $"rencontres chargées: {Model.EncounterCount}");

        var aeonglass = Model.Monster("AEONGLASS");
        Assert.NotNull(aeonglass);
        Assert.Equal("Boss", aeonglass!.Type);
        Assert.Equal(512, aeonglass.HpNormal);
        Assert.Equal(535, aeonglass.HpAscension);
        Assert.True(aeonglass.Moves.Count >= 3, $"moves: {aeonglass.Moves.Count}");
    }

    [Fact]
    public void Hp_scaling_starts_at_A8_and_damage_at_A9()
    {
        // Une même entrée porte les deux variantes ; le seuil des PV (ToughEnemies, A8) précède
        // celui des dégâts (DeadlyEnemies, A9) — ne jamais réutiliser la même constante.
        var monster = Model.Monster("BOWLBUG_EGG");
        Assert.NotNull(monster);
        var normal = monster!.HpNormal;
        var ascension = monster.HpAscension;
        Assert.NotNull(normal);
        Assert.NotNull(ascension);
        Assert.NotEqual(normal, ascension);

        Assert.Equal(normal, monster.HpAt(ThreatRules.AscensionHpFrom - 1));
        Assert.Equal(ascension, monster.HpAt(ThreatRules.AscensionHpFrom));
        Assert.True(ThreatRules.AscensionHpFrom < ThreatRules.AscensionDamageFrom);
        Assert.False(ThreatRules.IsDoubleBoss(ThreatRules.DoubleBossFrom - 1));
        Assert.True(ThreatRules.IsDoubleBoss(ThreatRules.DoubleBossFrom));
    }

    [Fact]
    public void Aeonglass_worst_hit_is_26_normal_and_32_ascension()
    {
        // Ebb : 26 normal / 32 ascension, un seul coup. Eye Lasers (11 × 2 = 22) reste en dessous.
        // Table des paliers (AscensionLevel.cs) : ToughEnemies = 8 (PV), DeadlyEnemies = 9
        // (dégâts), DoubleBoss = 10. Les dégâts restent donc normaux à A8 et basculent à A9.
        Assert.Equal(26, Model.MaxHit("AEONGLASS_BOSS", 0));
        Assert.Equal(26, Model.MaxHit("ENCOUNTER.AEONGLASS_BOSS", 1));
        Assert.Equal(26, Model.MaxHit("ENCOUNTER.AEONGLASS_BOSS", ThreatRules.AscensionDamageFrom - 1));
        Assert.Equal(32, Model.MaxHit("ENCOUNTER.AEONGLASS_BOSS", ThreatRules.AscensionDamageFrom));
        Assert.Equal(32, Model.MaxHit("ENCOUNTER.AEONGLASS_BOSS", 20));

        var worst = Model.WorstMove("AEONGLASS_BOSS", 0);
        Assert.NotNull(worst);
        Assert.Equal("EBB", worst!.MoveId);
        Assert.Equal("AEONGLASS", worst.MonsterId);
        Assert.Equal(26, worst.Damage);
    }

    [Fact]
    public void Hit_count_is_included_in_the_worst_move()
    {
        // Eye Lasers : 11 dégâts × 2 coups = 22 (et non 11).
        var lasers = Model.Monster("AEONGLASS")!.Moves.Single(m => m.Id == "EYE_LASERS");
        Assert.Equal(2, lasers.HitCount);
        Assert.Equal(11, lasers.DamageAt(0));
        Assert.Equal(22, lasers.HitAt(0));
        Assert.Equal(24, lasers.HitAt(10)); // 12 × 2 en ascension
    }

    [Fact]
    public void Encounter_resolves_to_its_monsters_and_sums_their_worst_hit()
    {
        var swarm = Model.MonstersOf("BOWLBUGS_NORMAL");
        Assert.Equal(
            new[] { "BOWLBUG_EGG", "BOWLBUG_NECTAR", "BOWLBUG_ROCK", "BOWLBUG_SILK" },
            swarm.Select(m => m.Id).ToArray());

        // 7 + 3 + 15 + 8 = 33 : la rencontre entière frappe au même tour.
        Assert.Equal(33, Model.MaxHit("BOWLBUGS_NORMAL", 0));
        Assert.Equal(37, Model.MaxHit("BOWLBUGS_NORMAL", 10));

        var single = Assert.Single(Model.MonstersOf("ENCOUNTER.AEONGLASS_BOSS"));
        Assert.Equal("AEONGLASS", single.Id);

        // Rencontre inconnue : liste vide, pire coup 0 — jamais une valeur par défaut inventée.
        Assert.Empty(Model.MonstersOf("NOPE_NOT_AN_ENCOUNTER"));
        Assert.Equal(0, Model.MaxHit("NOPE_NOT_AN_ENCOUNTER", 0));
        Assert.Null(Model.WorstMove("NOPE_NOT_AN_ENCOUNTER", 0));
    }

    [Fact]
    public void Every_encounter_links_to_known_monsters()
    {
        // Intégrité du catalogue : aucune rencontre ne perd un monstre à l'indexation.
        Assert.NotEmpty(Model.Encounters);
        foreach (var encounter in Model.Encounters)
        {
            Assert.NotEmpty(encounter.MonsterIds);
            Assert.Equal(encounter.MonsterIds.Count, Model.MonstersOf(encounter.Id).Count);
            Assert.True(Model.MaxHit(encounter.Id, 0) >= 0);
        }
    }

    [Fact]
    public void AverageDamage_comes_from_observed_stats_only()
    {
        // Nibbits (faible, acte 1) : 6,3 dégâts moyens observés, 9,1 pour Ironclad.
        Assert.Equal(6.3, Model.AverageDamage("NIBBITS_WEAK", null));
        Assert.Equal(6.3, Model.AverageDamage("ENCOUNTER.NIBBITS_WEAK", ""));
        Assert.Equal(9.1, Model.AverageDamage("NIBBITS_WEAK", "IRONCLAD"));
        Assert.Equal(4.3, Model.AverageDamage("NIBBITS_WEAK", "CHARACTER.SILENT"));

        // Personnage hors échantillon, rencontre sous le seuil de mesure, id inconnu : null, pas 0.
        Assert.Null(Model.AverageDamage("NIBBITS_WEAK", "WATCHER"));
        Assert.Null(Model.AverageDamage("TUNNELER_NORMAL", null));   // < 200 runs : non publiée
        Assert.Null(Model.AverageDamage("NOPE_NOT_AN_ENCOUNTER", "IRONCLAD"));

        // Depuis la mesure maison (docs/46) : un boss qui n'avait AUCUNE statistique en a une,
        // mesurée sur 40 286 runs et marquée comme telle.
        Assert.Equal(43.97, Model.AverageDamage("AEONGLASS_BOSS", null));
        Assert.Equal("measured", Model.Encounter("AEONGLASS_BOSS")!.Stats!.Source);

        Assert.Equal(3.83, Model.AverageTurns("NIBBITS_WEAK"));
        Assert.Equal(6.83, Model.AverageTurns("AEONGLASS_BOSS"));      // mesuré (docs/46)
        Assert.Null(Model.AverageTurns("NOPE_NOT_AN_ENCOUNTER"));
    }

    [Fact]
    public void EffectiveHp_applies_vulnerable_and_weak_once()
    {
        // 2 Vulnérable (+50 %) et 1 Faiblesse (−25 %) : ×1,125 → 90 / 1,125 = 80.
        Assert.Equal(80, ThreatModel.EffectiveHp(90, 90, 2, 1));

        // Les piles sont des durées : 5 Vulnérable valent toujours ×1,5 → 90 / 1,5 = 60.
        Assert.Equal(60, ThreatModel.EffectiveHp(90, 90, 5, 0));

        // Faiblesse seule : 60 PV « valent » 80 face à des dégâts réduits de 25 %.
        Assert.Equal(80, ThreatModel.EffectiveHp(60, 80, 0, 1));

        // Sans statut, et bornes : jamais au-dessus du max, jamais négatif.
        Assert.Equal(70, ThreatModel.EffectiveHp(70, 70, 0, 0));
        Assert.Equal(70, ThreatModel.EffectiveHp(200, 70, 0, 0));
        Assert.Equal(0, ThreatModel.EffectiveHp(0, 70, 0, 0));
        Assert.Equal(0, ThreatModel.EffectiveHp(-12, 70, 3, 3));
    }

    [Fact]
    public void Missing_catalog_returns_empty_without_throwing()
    {
        var empty = ThreatModel.LoadFrom("/nonexistent/ultima-catalog");

        Assert.Equal(0, empty.EncounterCount);
        Assert.Equal(0, empty.MonsterCount);
        Assert.Equal(0, empty.MaxHit("AEONGLASS_BOSS", 0));
        Assert.Null(empty.AverageDamage("NIBBITS_WEAK", null));
        Assert.Null(empty.AverageTurns("NIBBITS_WEAK"));
        Assert.Null(empty.WorstMove("AEONGLASS_BOSS", 0));
        Assert.Null(empty.Monster("AEONGLASS"));
        Assert.Null(empty.Encounter("AEONGLASS_BOSS"));
        Assert.Empty(empty.MonstersOf("AEONGLASS_BOSS"));
    }

    [Fact]
    public void Enriched_catalog_still_serves_the_legacy_boss_readers()
    {
        // data/catalog/monsters.json reste lisible par BossCatalog (champ `class`, `min_hp.values`)
        // même après enrichissement : les autres briques de scoring ne régressent pas.
        Assert.True(BossCatalog.Default.Count >= 120, $"monstres historiques: {BossCatalog.Default.Count}");
        var giant = BossCatalog.Default.Resolve("ENCOUNTER.WATERFALL_GIANT_BOSS");
        Assert.NotNull(giant);
        Assert.Equal("WaterfallGiant", giant!.ClassName);
        Assert.True(giant.Hp is > 0, $"PV: {giant.Hp}");
    }
}
