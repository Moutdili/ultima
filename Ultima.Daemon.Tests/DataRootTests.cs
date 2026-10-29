namespace Ultima.Daemon.Tests;

/// <summary>Convention ULTIMA_DATA (racine des données) fixée par le daemon pour le moteur.</summary>
public class DataRootTests
{
    [Fact]
    public void Data_root_accepts_the_data_dir_and_the_repo_root()
    {
        var repo = Fixtures.RepoRoot();
        var data = Path.Combine(repo, "data");
        Assert.Equal(data, DataRoot.Normalize(data));
        Assert.Equal(data, DataRoot.Normalize(repo));          // compatibilité : <repo> → <repo>/data
        Assert.Null(DataRoot.Normalize(Path.Combine(repo, "Ultima.Daemon"))); // aucun meta/
        Assert.Null(DataRoot.Normalize(null));
        Assert.Equal(data, DataRoot.Resolve(repo, out var src));
        Assert.Equal("--data", src);
    }
}
