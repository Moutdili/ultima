using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Ultima.Daemon.State;
using Ultima.Engine.Contracts;
using Ultima.Saves;

namespace Ultima.Daemon.Publish;

/// <summary>
/// Revue de run (contrat v1.2) : op socket <c>{"op":"review.open","runId":"1780696537"}</c> → le daemon lit
/// <c>history/&lt;runId&gt;.run</c> (lecture seule), appelle <see cref="IReviewBuilder"/> (Hermes) HORS de la boucle
/// principale, écrit <c>$XDG_RUNTIME_DIR/ultima/review/&lt;hash&gt;.json</c> (tmp + rename) et répond
/// <c>{"op":"review.open","runId":…,"ok":true,"path":…}</c>. Erreurs : <c>{"ok":false,"error":…}</c> ;
/// sans implémentation : <c>"review indisponible"</c>. Jamais pendant une partie (le contrat : « calculée hors ligne »).
/// </summary>
public sealed partial class ReviewService
{
    public const string Unavailable = "review indisponible";

    private readonly string _dir;
    private readonly Func<string?> _historyDir;
    private readonly IReviewBuilder? _builder;
    private readonly Func<bool> _gameRunning;
    private readonly SemaphoreSlim _one = new(1, 1);

    public ReviewService(string reviewDir, Func<string?> historyDir, IReviewBuilder? builder, Func<bool> gameRunning)
    {
        _dir = reviewDir;
        _historyDir = historyDir;
        _builder = builder;
        _gameRunning = gameRunning;
    }

    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex RunIdRx();

    private static string Reply(string op, string? runId, bool ok, string? error = null, string? path = null)
    {
        var o = new JsonObject { ["op"] = op, ["runId"] = runId, ["ok"] = ok };
        if (error is not null) o["error"] = error;
        if (path is not null) o["path"] = path;
        return o.ToJsonString(StateJson.Options);
    }

    public async Task<string?> HandleAsync(string line)
    {
        string? op, runId;
        try
        {
            var n = JsonNode.Parse(line) as JsonObject;
            op = n?["op"]?.GetValue<string>();
            runId = n?["runId"]?.GetValue<string>();
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
            return Reply("?", null, false, "requête illisible");
        }
        if (op != "review.open") return Reply(op ?? "?", runId, false, "op inconnue");
        if (_builder is null) return Reply(op, runId, false, Unavailable);
        if (runId is null) return Reply(op, null, false, "runId manquant");
        if (runId.EndsWith(".run", StringComparison.Ordinal)) runId = runId[..^4];
        if (!RunIdRx().IsMatch(runId)) return Reply(op, runId, false, "runId invalide");
        if (_gameRunning()) return Reply(op, runId, false, Unavailable + " pendant une partie");
        if (_historyDir() is not { } hist) return Reply(op, runId, false, "historique introuvable");
        var file = Path.Combine(hist, runId + ".run");
        if (!File.Exists(file)) return Reply(op, runId, false, "run introuvable");

        if (!await _one.WaitAsync(0).ConfigureAwait(false)) return Reply(op, runId, false, "une revue est déjà en cours");
        try
        {
            // Hors boucle, sur le pool (le fil hérite de nice 10) ; délai max 30 s.
            var work = Task.Run(() =>
            {
                var key = $"{runId}|{File.GetLastWriteTimeUtc(file):O}|{_builder.GetType().FullName}";
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16].ToLowerInvariant();
                var outPath = Path.Combine(_dir, hash + ".json");
                if (File.Exists(outPath)) return outPath; // déjà calculée pour cette version du fichier
                var record = new HistoryReader().Read(file);
                var timeline = _builder.Build(record);
                StatePublisher.EnsurePrivateDir(_dir);
                StatePublisher.WriteAtomic(outPath, Path.Combine(_dir, $".{hash}.tmp"),
                    JsonSerializer.Serialize(timeline, StateJson.Options) + "\n");
                return outPath;
            });
            var path = await work.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            Diag.Info($"revue {runId} → {path}");
            return Reply(op, runId, true, path: path);
        }
        catch (TimeoutException) { return Reply(op, runId, false, Unavailable + " (délai dépassé)"); }
        catch (Exception e)
        {
            Diag.Warn($"revue {runId} : {e.GetType().Name}: {e.Message}");
            return Reply(op, runId, false, Unavailable + $" ({e.GetType().Name})");
        }
        finally { _one.Release(); }
    }
}
