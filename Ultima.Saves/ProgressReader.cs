using Ultima.Saves.Dto;
using Ultima.Saves.IO;
using Ultima.Saves.Model;

namespace Ultima.Saves;

/// <summary>Lit <c>progress.save</c> : statistiques par personnage, rencontre, carte, ancien.</summary>
public sealed class ProgressReader
{
    private readonly RobustReadOptions _options;

    public ProgressReader(RobustReadOptions? options = null)
    {
        _options = options ?? RobustReadOptions.Default;
    }

    /// <returns>null si progress.save et son .backup sont absents.</returns>
    public ProgressStats? TryRead(string path)
    {
        var r = RobustJsonReader.Read<ProgressDto>(path, null, _options);
        return r is null ? null : Mapper.Progress(r.Value.Value);
    }

    public ProgressStats? TryRead(ProfilePaths paths) => TryRead(paths.ProgressSave);

    public static ProgressStats Parse(ReadOnlySpan<byte> json)
        => Mapper.Progress(RobustJsonReader.Deserialize<ProgressDto>(json, null, "progress.save"));
}
