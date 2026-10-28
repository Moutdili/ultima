using System.Text.Json;

namespace Ultima.Saves.IO;

/// <summary>D'ou vient la donnee effectivement lue.</summary>
public enum ReadSource
{
    Primary,
    Backup,
}

/// <summary>Echec definitif de lecture (fichier principal ET .backup illisibles).</summary>
public sealed class SaveReadException : Exception
{
    public string Path { get; }

    public SaveReadException(string path, string message, Exception? inner = null)
        : base($"{message} ({path})", inner)
    {
        Path = path;
    }
}

/// <summary>Reglages de la lecture robuste.</summary>
public sealed record RobustReadOptions
{
    /// <summary>Nombre de lectures du fichier principal avant repli sur .backup.</summary>
    public int Attempts { get; init; } = 3;

    /// <summary>Pause entre deux relectures.</summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromMilliseconds(60);

    public static RobustReadOptions Default { get; } = new();
}

/// <summary>
/// Lecture robuste d'un fichier JSON du jeu :
/// 1. lecture sans verrou ; 2. JSON invalide ou rejete par <paramref name="validate"/> => relecture
/// (jusqu'a <see cref="RobustReadOptions.Attempts"/>) ; 3. repli sur <c>&lt;fichier&gt;.backup</c>.
/// Le jeu ecrit <c>X.tmp</c> puis renomme en <c>X</c> (Core/Saves/GodotFileIo.cs:110-121), et tient
/// une copie <c>X.backup</c> (GodotFileIo.cs:277-313).
/// </summary>
internal static class RobustJsonReader
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <returns>null si ni le fichier ni son .backup n'existent.</returns>
    internal static (T Value, ReadSource Source)? Read<T>(string path, Func<T, string?>? validate,
        RobustReadOptions? options = null) where T : class
    {
        options ??= RobustReadOptions.Default;
        Exception? last = null;
        bool primaryExists = false;

        for (int i = 0; i < Math.Max(1, options.Attempts); i++)
        {
            if (i > 0) Thread.Sleep(options.RetryDelay);
            try
            {
                var bytes = NoLockFile.ReadAllBytes(path);
                primaryExists = true;
                var v = Deserialize(bytes, validate, path);
                return (v, ReadSource.Primary);
            }
            catch (FileNotFoundException) { break; }
            catch (Exception e) when (e is JsonException or InvalidDataException or IOException)
            {
                primaryExists = true;
                last = e;
            }
        }

        string backup = path + ".backup";
        try
        {
            var bytes = NoLockFile.ReadAllBytes(backup);
            return (Deserialize(bytes, validate, backup), ReadSource.Backup);
        }
        catch (FileNotFoundException)
        {
            if (!primaryExists) return null;
            throw new SaveReadException(path, "Fichier illisible et aucun .backup", last);
        }
        catch (Exception e) when (e is JsonException or InvalidDataException or IOException)
        {
            throw new SaveReadException(path, "Fichier et .backup illisibles", last ?? e);
        }
    }

    internal static T Deserialize<T>(ReadOnlySpan<byte> bytes, Func<T, string?>? validate, string path)
        where T : class
    {
        var v = JsonSerializer.Deserialize<T>(bytes, Options)
                ?? throw new InvalidDataException($"JSON nul : {path}");
        var err = validate?.Invoke(v);
        if (err is not null) throw new InvalidDataException($"{err} : {path}");
        return v;
    }
}
