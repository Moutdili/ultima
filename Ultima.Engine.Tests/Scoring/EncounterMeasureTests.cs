using Ultima.Engine.Scoring.Threat;
using Xunit;

namespace Ultima.Engine.Tests.Scoring;

/// <summary>
/// Rencontres sans statistiques communautaires (point 4 du chef) : elles sont complétées par la
/// mesure maison, sans jamais écraser une stat du catalogue ni inventer une moyenne.
/// </summary>
public sealed class EncounterMeasureTests : IDisposable
{
    private readonly string _root;

    public EncounterMeasureTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ultima-enc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "catalog"));
        Directory.CreateDirectory(Path.Combine(_root, "calibration"));

        File.WriteAllText(Path.Combine(_root, "catalog", "encounters.json"), """
        {"encounters":[
          {"id":"CULTISTS_NORMAL","name":"Cultists","act":1,"room_type":"Monster","is_weak":false,
           "monsters":["CULTIST"],"stats":null},
          {"id":"BYGONE_EFFIGY_ELITE","name":"Bygone Effigy","act":1,"room_type":"Elite","is_weak":false,
           "monsters":["BYGONE_EFFIGY"],"stats":{"total":1000,"fatal":10,"abandoned":0,
             "avg_damage":22.6,"avg_turns":4.7,"characters":{}}}
        ]}
        """);

        File.WriteAllText(Path.Combine(_root, "catalog", "monsters.json"), """{"monsters":[]}""");

        File.WriteAllText(Path.Combine(_root, "calibration", "encounter_measure.json"), """
        {"schema":"ultima.encounter-measure/1",
         "source":{"runs":400000,"solo":400000,"min_n":200,"method":"test","caveat":"test"},
         "count":2,
         "encounters":[
           {"id":"CULTISTS_NORMAL","act":1,"n":5000,"avg_damage":11.4,"avg_turns":3.2,
            "fatal":120,"fatal_rate":0.024,"characters":{"SILENT":2500},
            "by_character":{"SILENT":{"n":2500,"avg_damage":9.8,"avg_turns":3.0}}},
           {"id":"BYGONE_EFFIGY_ELITE","act":1,"n":4000,"avg_damage":99.9,"avg_turns":9.9,
            "fatal":1,"fatal_rate":0.0,"characters":{},"by_character":{}}
         ]}
        """);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void Missing_catalog_stats_are_filled_by_the_measure()
    {
        var threat = ThreatModel.LoadFrom(Path.Combine(_root, "catalog"), Path.Combine(_root, "calibration"));

        Assert.Equal(11.4, threat.AverageDamage("CULTISTS_NORMAL", null));
        Assert.Equal(3.2, threat.AverageTurns("CULTISTS_NORMAL"));
        Assert.Equal("measured", threat.Encounter("CULTISTS_NORMAL")!.Stats!.Source);
    }

    [Fact]
    public void Per_character_measure_is_used_when_present()
    {
        var threat = ThreatModel.LoadFrom(Path.Combine(_root, "catalog"), Path.Combine(_root, "calibration"));

        Assert.Equal(9.8, threat.AverageDamage("CULTISTS_NORMAL", "CHARACTER.SILENT"));
        Assert.Null(threat.AverageDamage("CULTISTS_NORMAL", "CHARACTER.IRONCLAD"));   // absent de l'échantillon
    }

    [Fact]
    public void Community_stats_are_never_overwritten()
    {
        var threat = ThreatModel.LoadFrom(Path.Combine(_root, "catalog"), Path.Combine(_root, "calibration"));

        Assert.Equal(22.6, threat.AverageDamage("BYGONE_EFFIGY_ELITE", null));
        Assert.Null(threat.Encounter("BYGONE_EFFIGY_ELITE")!.Stats!.Source);   // stat du catalogue, pas la mesure
    }
}
