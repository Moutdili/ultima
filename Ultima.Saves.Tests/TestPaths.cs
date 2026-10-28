using Ultima.Saves;

namespace Ultima.Saves.Tests;

internal static class TestPaths
{
    public const string FakeSteamId = "70000000000000001";

    /// <summary>Racine du depot (contient fixtures/saves).</summary>
    public static string RepoRoot { get; } = FindRepoRoot();

    public static string Fixtures => Path.Combine(RepoRoot, "fixtures", "saves");
    public static string FixtureRoot => Path.Combine(Fixtures, "root");
    public static string FixtureAccount => Path.Combine(FixtureRoot, "steam", FakeSteamId);
    public static string FixtureSaves => Path.Combine(FixtureAccount, "profile1", "saves");
    public static string FixtureHistory => Path.Combine(FixtureSaves, "history");
    public static string LogSample => Path.Combine(Fixtures, "godot.sample.txt");

    public static string LibrarySourceDir => Path.Combine(RepoRoot, "Ultima.Saves");

    private static string FindRepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null)
        {
            if (Directory.Exists(Path.Combine(d.FullName, "fixtures", "saves")) &&
                Directory.Exists(Path.Combine(d.FullName, "Ultima.Saves")))
                return d.FullName;
            d = d.Parent;
        }
        throw new DirectoryNotFoundException("fixtures/saves introuvable depuis " + AppContext.BaseDirectory);
    }

    /// <summary>Racine reelle des donnees du jeu (lecture seule) : ULTIMA_STS2_ROOT ou chemin Flatpak.</summary>
    public static string RealRoot => ProfileLocator.DefaultRoot();

    /// <summary>Profil reel resolu, ou null si les donnees du jeu sont absentes (CI).</summary>
    public static ProfilePaths? RealProfile()
    {
        try
        {
            if (ProfileLocator.ListSteamAccounts(RealRoot).Count == 0) return null;
            var p = ProfileLocator.Locate(RealRoot);
            return Directory.Exists(p.HistoryDir) ? p : null;
        }
        catch (DirectoryNotFoundException) { return null; }
    }
}

/// <summary>Test « de terrain » : ignore automatiquement si les donnees reelles du jeu sont absentes.</summary>
public sealed class RealDataFactAttribute : FactAttribute
{
    public RealDataFactAttribute()
    {
        if (TestPaths.RealProfile() is null)
            Skip = $"Donnees reelles absentes ({TestPaths.RealRoot}) : test de terrain ignore.";
    }
}

/// <summary>Dossier temporaire jetable (jamais dans le dossier du jeu).</summary>
internal sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ultima-saves-" + Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    public string File(string name) => System.IO.Path.Combine(Path, name);

    /// <summary>Ecrit comme le jeu : X.tmp puis renommage atomique vers X.</summary>
    public void WriteLikeGame(string name, string content)
    {
        var target = File(name);
        var tmp = target + ".tmp";
        System.IO.File.WriteAllText(tmp, content);
        System.IO.File.Move(tmp, target, overwrite: true);
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
    }
}
