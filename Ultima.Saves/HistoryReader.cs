using Ultima.Saves.Dto;
using Ultima.Saves.IO;
using Ultima.Saves.Model;

namespace Ultima.Saves;

/// <summary>Lit les runs terminees : <c>saves/history/&lt;start_time&gt;.run</c> (Core/Runs/RunHistory.cs).</summary>
public sealed class HistoryReader
{
    private readonly RobustReadOptions _options;

    public HistoryReader(RobustReadOptions? options = null)
    {
        _options = options ?? RobustReadOptions.Default;
    }

    private static string? Validate(RunHistoryDto d) =>
        d.Players is null || d.Players.Count == 0 ? "run sans joueur" : null;

    /// <summary>Fichiers <c>*.run</c> (hors <c>.backup</c>), tries par nom (= start_time).</summary>
    public static IReadOnlyList<string> EnumerateRunFiles(string historyDir)
    {
        if (!Directory.Exists(historyDir)) return Array.Empty<string>();
        return Directory.EnumerateFiles(historyDir, "*.run", SearchOption.TopDirectoryOnly)
            .Where(f => f.EndsWith(".run", StringComparison.Ordinal))
            .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal)
            .ToArray();
    }

    /// <exception cref="SaveReadException">fichier et .backup illisibles.</exception>
    /// <exception cref="FileNotFoundException">fichier absent.</exception>
    public RunRecord Read(string path)
    {
        var r = RobustJsonReader.Read<RunHistoryDto>(path, Validate, _options)
                ?? throw new FileNotFoundException("Run introuvable", path);
        return Mapper.History(r.Value, Path.GetFileName(path));
    }

    /// <summary>Lit toutes les runs ; une run illisible est signalee a <paramref name="onError"/> et sautee.</summary>
    public IReadOnlyList<RunRecord> ReadAll(string historyDir, Action<string, Exception>? onError = null)
    {
        var list = new List<RunRecord>();
        foreach (var f in EnumerateRunFiles(historyDir))
        {
            try { list.Add(Read(f)); }
            catch (Exception e) when (onError is not null)
            {
                onError(f, e);
            }
        }
        return list;
    }

    public static RunRecord Parse(ReadOnlySpan<byte> json, string fileName = "")
        => Mapper.History(RobustJsonReader.Deserialize<RunHistoryDto>(json, Validate, fileName), fileName);
}
