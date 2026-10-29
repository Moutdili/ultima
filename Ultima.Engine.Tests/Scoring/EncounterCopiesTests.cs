using Ultima.Engine.Scoring;
using Ultima.Engine.Scoring.Threat;
using Xunit;

namespace Ultima.Engine.Tests.Scoring;

/// <summary>
/// Le catalogue des copies (docs/39) doit changer la menace : 42 rencontres ont plusieurs
/// instances du même monstre, jusqu'à ×4. Sans correction, le pire tour est sous-estimé de
/// 11,8 % en agrégat (×4 sur Scrolls of Biting, Exoskeletons, Phantasmal Gardeners).
/// </summary>
public sealed class EncounterCopiesTests
{
    private static string DirWithoutCopies()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ultima-copies-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var source = ScoringData.ResolveCatalogDir();
        foreach (var file in new[] { "monsters.json", "encounters.json", "constraints.json" })
        {
            var from = Path.Combine(source, file);
            if (File.Exists(from)) File.Copy(from, Path.Combine(dir, file));
        }
        return dir;
    }

    [Fact]
    public void Copies_multiply_the_worst_turn()
    {
        var dir = DirWithoutCopies();
        try
        {
            var baseline = ThreatModel.LoadFrom(dir);
            var model = ThreatModel.LoadFrom();

            var single = baseline.MaxHit("SCROLLS_OF_BITING_NORMAL", 0);
            var copied = model.MaxHit("SCROLLS_OF_BITING_NORMAL", 0);

            Assert.True(single > 0, "la rencontre doit avoir un coup");
            Assert.Equal(4, model.Instances("SCROLLS_OF_BITING_NORMAL", "SCROLL_OF_BITING"));
            Assert.Equal(4 * single, copied);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Encounter_without_copies_is_unchanged()
    {
        var dir = DirWithoutCopies();
        try
        {
            var baseline = ThreatModel.LoadFrom(dir);
            var model = ThreatModel.LoadFrom();

            Assert.Equal(1, model.Instances("AEONGLASS_BOSS", "AEONGLASS"));
            Assert.Equal(baseline.MaxHit("AEONGLASS_BOSS", 0), model.MaxHit("AEONGLASS_BOSS", 0));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Boss_with_two_followers_counts_both()
    {
        var dir = DirWithoutCopies();
        try
        {
            var baseline = ThreatModel.LoadFrom(dir);
            var model = ThreatModel.LoadFrom();

            Assert.Equal(2, model.Instances("THE_KIN_BOSS", "KIN_FOLLOWER"));
            Assert.Equal(1, model.Instances("THE_KIN_BOSS", "KIN_PRIEST"));
            Assert.True(model.MaxHit("THE_KIN_BOSS", 0) > baseline.MaxHit("THE_KIN_BOSS", 0),
                "deux followers doivent peser plus qu'un");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
