using System.Diagnostics;
using System.Text.Json.Nodes;
using Ultima.Engine.Contracts;

namespace Ultima.Daemon.Custom;

/// <summary>
/// Un plugin vivant (docs/44 §7) : un fil dédié par plugin, un processus enfant <c>nice 19</c>, le protocole
/// <c>{"api":1,"state":EngineState}</c> sur stdin → une ligne JSON validée par
/// <c>schemas/plugin-output.schema.json</c> sur stdout. <see cref="Submit"/> ne fait que déposer une ligne
/// (quelques microsecondes, jamais d'attente) : la publication de l'état n'est JAMAIS retardée par un plugin,
/// même endormi, planté, bavard ou invalide. 3 échecs consécutifs ⇒ <c>disabled</c> + journal.
/// </summary>
public sealed class PluginRuntime : IDisposable
{
    public const int MaxConsecutiveFailures = 3;

    private readonly PluginManifest _m;
    private readonly JsonSchema? _outputSchema;
    private readonly Action _changed;
    private readonly object _gate = new();
    private readonly Thread _worker;

    private string? _pending;
    private string? _lastSubmitted;
    private bool _stop;
    private bool _disabled;
    private PluginProcess? _proc;
    private long _lastSentTicks;
    private int _failures;
    private IReadOnlyList<CustomRowView> _rows = Array.Empty<CustomRowView>();
    private string _health = CustomHealth.Stale;
    private string? _problem = "en attente de la première réponse";

    public PluginRuntime(PluginManifest manifest, JsonSchema? outputSchema, Action changed)
    {
        _m = manifest;
        _outputSchema = outputSchema;
        _changed = changed;
        _worker = new Thread(Loop) { IsBackground = true, Name = "plugin-" + manifest.Id };
        _worker.Start();
    }

    public string Id => _m.Id;
    public IReadOnlyList<CustomRowView> Rows { get { lock (_gate) return _rows; } }
    public string Health { get { lock (_gate) return _health; } }
    public string? Problem { get { lock (_gate) return _problem; } }
    public int Failures { get { lock (_gate) return _failures; } }
    public bool Disabled { get { lock (_gate) return _disabled; } }

    /// <summary>Nombre de réponses valides publiées (mesure, tests).</summary>
    public int Answers { get; private set; }

    /// <summary>Nombre d'états effectivement envoyés (respecte <c>minIntervalMs</c>).</summary>
    public int Sends { get; private set; }

    /// <summary>Dépose une ligne d'état (JSON d'EngineState). Retour immédiat (jamais d'attente).</summary>
    public void Submit(string stateJson)
    {
        var line = "{\"api\":1,\"state\":" + stateJson + "}";
        lock (_gate)
        {
            if (_stop || _disabled) return;
            if (_lastSubmitted is not null && _lastSubmitted == stateJson) return; // « seulement si l'état change »
            _lastSubmitted = stateJson;
            _pending = line;
            Monitor.Pulse(_gate);
        }
    }

    public CustomModuleView View() => new(_m.Id, _m.Label, CustomIds.SourcePlugin, _m.Screens, Rows, Health, Problem);

    private void Loop()
    {
        while (true)
        {
            string line;
            lock (_gate)
            {
                while (!_stop && _pending is null) Monitor.Wait(_gate);
                if (_stop || _pending is null) return;
                line = _pending;
                _pending = null;
            }

            // au plus une fois par minIntervalMs (l'état le plus récent gagne)
            var wait = _m.MinIntervalMs - ElapsedMs(_lastSentTicks);
            if (wait > 0)
            {
                lock (_gate)
                {
                    if (_stop) return;
                    Monitor.Wait(_gate, TimeSpan.FromMilliseconds(wait));
                    if (_stop) return;
                    if (_pending is not null) { line = _pending; _pending = null; }
                }
            }

            if (_proc is null)
            {
                var started = PluginProcess.Start(_m, out var startProblem);
                if (started is null) { Fail(startProblem ?? "lancement impossible"); continue; }
                _proc = started;
            }

            _lastSentTicks = Stopwatch.GetTimestamp();
            Sends++;
            if (!_proc.Send(line, out var sendProblem)) { Fail(sendProblem ?? "écriture impossible"); continue; }
            if (!_proc.TryRead(_m.TimeoutMs, out var reply, out var readProblem)) { Fail(readProblem ?? "réponse absente"); continue; }
            var rows = ParseRows(reply!, out var problem);
            if (rows is null) { Fail(problem ?? "réponse invalide"); continue; }
            Succeed(rows);
        }
    }

    private static double ElapsedMs(long sinceTicks) =>
        sinceTicks == 0 ? double.MaxValue : (Stopwatch.GetTimestamp() - sinceTicks) * 1000.0 / Stopwatch.Frequency;

    private IReadOnlyList<CustomRowView>? ParseRows(string reply, out string? problem)
    {
        problem = null;
        if (_outputSchema is null) { problem = "schémas du contrat indisponibles (plugin-output)"; return null; }
        var errors = _outputSchema.ValidateText(reply, out var node);
        if (errors.Count > 0) { problem = "sortie invalide : " + string.Join(" ; ", errors); return null; }
        if (node is not JsonObject o || o["rows"] is not JsonArray array)
        {
            problem = "sortie invalide : « rows » attendu";
            return null;
        }
        var rows = new List<CustomRowView>(array.Count);
        foreach (var r in array)
        {
            var text = r!["text"]!.GetValue<string>();
            var tone = r["tone"] is JsonValue tv && tv.TryGetValue<string>(out var t) ? t : null;
            var grade = r["grade"] is JsonValue gv && gv.TryGetValue<string>(out var g) ? g : null;
            rows.Add(new CustomRowView(text, tone, grade));
        }
        return rows;
    }

    private void Succeed(IReadOnlyList<CustomRowView> rows)
    {
        lock (_gate)
        {
            _rows = rows;
            _health = CustomHealth.Ok;
            _problem = null;
            _failures = 0;
        }
        Answers++;
        try { _changed(); }
        catch (Exception e) { Diag.Warn($"plugin {_m.Id} : republication : {e.GetType().Name}: {e.Message}"); }
    }

    private void Fail(string problem)
    {
        bool disable;
        int failures;
        lock (_gate)
        {
            _failures++;
            failures = _failures;
            _health = failures >= MaxConsecutiveFailures ? CustomHealth.Disabled : CustomHealth.Stale;
            _problem = problem;
            disable = failures >= MaxConsecutiveFailures;
            if (disable) _disabled = true;
        }
        Diag.Warn($"plugin {_m.Id} : échec {failures}/{MaxConsecutiveFailures} — {problem}" +
                  (disable ? " ; plugin DÉSACTIVÉ" : ""));
        var proc = _proc;
        _proc = null;
        try { proc?.Dispose(); } catch { }
        if (disable)
        {
            lock (_gate) { _rows = Array.Empty<CustomRowView>(); }
            try { _changed(); } // l'état publié doit refléter « disabled » immédiatement
            catch (Exception e) { Diag.Warn($"plugin {_m.Id} : republication : {e.GetType().Name}"); }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _stop = true;
            _pending = null;
            Monitor.PulseAll(_gate);
        }
        var proc = _proc;
        _proc = null;
        try { proc?.Dispose(); } catch { }
        if (_worker.IsAlive && Environment.CurrentManagedThreadId != _worker.ManagedThreadId)
            _worker.Join(TimeSpan.FromSeconds(2));
    }
}
