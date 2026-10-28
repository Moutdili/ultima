using Ultima.Saves.Dto;
using Ultima.Saves.IO;
using Ultima.Saves.Model;

namespace Ultima.Saves;

/// <summary>
/// Lit <c>current_run.save</c> (solo) / <c>current_run_mp.save</c> (co-op, ecrit par l'HOTE seulement :
/// RunSaveManager.SaveRun ne sauvegarde que si NetGameType est Singleplayer ou Host).
/// Lecture seule, sans verrou ; JSON invalide => relecture puis repli sur <c>.backup</c>.
/// </summary>
public sealed class RunSaveReader
{
    private readonly RobustReadOptions _options;

    public RunSaveReader(RobustReadOptions? options = null)
    {
        _options = options ?? RobustReadOptions.Default;
    }

    /// <summary>Validation minimale : une save sans joueur est consideree corrompue.</summary>
    private static string? Validate(RunSaveDto d) =>
        d.Players is null || d.Players.Count == 0 ? "save sans joueur" : null;

    /// <summary>
    /// Run en cours du profil : la plus recente (save_time) entre solo et co-op.
    /// Null si aucune run en cours (fichiers et .backup absents).
    /// </summary>
    public RunState? ReadCurrent(ProfilePaths paths)
    {
        var solo = TryRead(paths.CurrentRunSave);
        var mp = TryRead(paths.CurrentRunMpSave);
        if (solo is null) return mp;
        if (mp is null) return solo;
        return mp.SaveTime > solo.SaveTime ? mp : solo;
    }

    /// <summary>Lit un fichier de run ; null s'il n'existe pas (ni son .backup).</summary>
    /// <exception cref="SaveReadException">fichier et .backup illisibles.</exception>
    public RunState? TryRead(string path)
    {
        var r = RobustJsonReader.Read<RunSaveDto>(path, Validate, _options);
        if (r is null) return null;
        var (dto, source) = r.Value;
        bool fromBackup = source == ReadSource.Backup;
        return Mapper.Run(dto, fromBackup ? path + ".backup" : path, fromBackup, IsMp(path));
    }

    /// <summary>Comme <see cref="TryRead"/> mais leve si absent.</summary>
    public RunState Read(string path) =>
        TryRead(path) ?? throw new FileNotFoundException("Save de run introuvable", path);

    /// <summary>Analyse un contenu deja en memoire (tests, copie).</summary>
    public static RunState Parse(ReadOnlySpan<byte> json, string sourcePath = "")
    {
        var dto = RobustJsonReader.Deserialize<RunSaveDto>(json, Validate, sourcePath);
        return Mapper.Run(dto, sourcePath, false, IsMp(sourcePath));
    }

    private static bool IsMp(string path) =>
        Path.GetFileName(path).StartsWith("current_run_mp.save", StringComparison.Ordinal);
}
