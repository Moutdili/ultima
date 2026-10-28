using Ultima.Saves.Dto;
using Ultima.Saves.IO;

namespace Ultima.Saves;

/// <summary>Chemins resolus du profil VANILLA actif. Aucun chemin ne passe par <c>modded/</c>.</summary>
public sealed record ProfilePaths(
    string Root,
    string SteamId,
    int ProfileId,
    bool ProfileIdFromDefault,
    string AccountDir,
    string ProfileDir,
    string SavesDir,
    string CurrentRunSave,
    string CurrentRunMpSave,
    string ProgressSave,
    string HistoryDir,
    string GodotLog)
{
    /// <summary>SteamID64 numerique (identifie le joueur local en co-op), null si non numerique.</summary>
    public ulong? SteamId64 => ulong.TryParse(SteamId, out var v) ? v : null;

    /// <summary>
    /// Construit les chemins d'un profil. Disposition prouvee sur disque et par le source
    /// (Core/Saves/UserDataPathProvider.cs GetProfileDir ; RunSaveManager.cs ; RunHistorySaveManager.cs).
    /// </summary>
    public static ProfilePaths For(string root, string steamId, int profileId, bool fromDefault = false)
    {
        string account = Path.Combine(root, "steam", steamId);
        string profile = Path.Combine(account, $"profile{profileId}");
        string saves = Path.Combine(profile, "saves");
        var p = new ProfilePaths(root, steamId, profileId, fromDefault, account, profile, saves,
            Path.Combine(saves, "current_run.save"),
            Path.Combine(saves, "current_run_mp.save"),
            Path.Combine(saves, "progress.save"),
            Path.Combine(saves, "history"),
            Path.Combine(root, "logs", "godot.log"));
        ProfileLocator.AssertNotModded(p.ProfileDir);
        return p;
    }
}

/// <summary>
/// Trouve le profil actif du jeu en lisant <c>steam/&lt;id&gt;/profile.save</c> (<c>last_profile_id</c>).
/// Ne devine jamais et ne resout JAMAIS vers <c>modded/</c> : un mod charge fait basculer le jeu sur
/// <c>modded/profileN</c> (UserDataPathProvider.GetProfileDir), ULTIMA lit toujours <c>profileN</c>.
/// </summary>
public static class ProfileLocator
{
    /// <summary>Racine observee chez Mio (Steam Flatpak, Fedora), relative a $HOME.</summary>
    public const string FlatpakRelativeRoot = ".var/app/com.valvesoftware.Steam/.local/share/SlayTheSpire2";

    /// <summary>Variable d'environnement pour forcer la racine (tests, autre installation).</summary>
    public const string RootEnvVar = "ULTIMA_STS2_ROOT";

    public static string DefaultRoot()
    {
        var env = Environment.GetEnvironmentVariable(RootEnvVar);
        if (!string.IsNullOrWhiteSpace(env)) return env;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, FlatpakRelativeRoot);
    }

    /// <summary>Comptes Steam presents sous <c>root/steam/</c> (noms de dossiers).</summary>
    public static IReadOnlyList<string> ListSteamAccounts(string root)
    {
        var steam = Path.Combine(root, "steam");
        if (!Directory.Exists(steam)) return Array.Empty<string>();
        return Directory.GetDirectories(steam).Select(Path.GetFileName).OfType<string>()
            .OrderBy(s => s, StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// Lit <c>last_profile_id</c> dans <c>accountDir/profile.save</c> (repli .backup).
    /// Fichier absent : 1, valeur par defaut du jeu (ProfileSave.cs:19), signalee par <paramref name="fromDefault"/>.
    /// </summary>
    public static int ReadActiveProfileId(string accountDir, out bool fromDefault)
    {
        var r = RobustJsonReader.Read<ProfileSaveDto>(Path.Combine(accountDir, "profile.save"),
            d => d.LastProfileId < 1 ? $"last_profile_id invalide ({d.LastProfileId})" : null);
        fromDefault = r is null;
        return r?.Value.LastProfileId ?? 1;
    }

    /// <summary>
    /// Resout le profil actif. <paramref name="steamId"/> null : unique compte, sinon celui dont
    /// profile.save est le plus recent.
    /// </summary>
    /// <exception cref="DirectoryNotFoundException">aucun compte Steam sous la racine.</exception>
    public static ProfilePaths Locate(string? root = null, string? steamId = null)
    {
        root ??= DefaultRoot();
        if (steamId is null)
        {
            var accounts = ListSteamAccounts(root);
            if (accounts.Count == 0)
                throw new DirectoryNotFoundException($"Aucun compte sous {Path.Combine(root, "steam")}");
            steamId = accounts.Count == 1
                ? accounts[0]
                : accounts.OrderByDescending(a => SafeMTime(Path.Combine(root, "steam", a, "profile.save"))).First();
        }
        var account = Path.Combine(root, "steam", steamId);
        int id = ReadActiveProfileId(account, out bool fromDefault);
        return ProfilePaths.For(root, steamId, id, fromDefault);
    }

    private static DateTime SafeMTime(string path) =>
        File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;

    /// <summary>Garde-fou : leve si un chemin passe par un dossier <c>modded</c>.</summary>
    public static void AssertNotModded(string path)
    {
        var parts = Path.GetFullPath(path).Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Any(p => p.Equals("modded", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Chemin de profil modde refuse : {path}");
    }
}
