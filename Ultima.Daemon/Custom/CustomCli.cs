using System.Text.Json.Nodes;
using Ultima.Daemon.State;
using Ultima.Engine.Contracts;

namespace Ultima.Daemon.Custom;

/// <summary>
/// Commandes de test hors jeu (docs/ipc/README.md §5) : <c>module-test &lt;fichier&gt;</c> et
/// <c>plugin-test &lt;dossier&gt; [--state f]</c>. Elles rejouent les états d'exemple du dépôt
/// (<c>docs/ipc/*.sample.json</c>) avec les MÊMES validateurs que le daemon (schémas du contrat) et rendent
/// un code de sortie clair : 0 valide, 1 usage, 2 fichier/dossier refusé, 3 échec d'exécution (délai, sortie).
/// Aucune écriture : ni les vrais dossiers XDG, ni le jeu.
/// </summary>
public static class CustomCli
{
    public const int Ok = 0, BadUsage = 1, Invalid = 2, RunFailed = 3;

    // ---------------- module-test ----------------

    public static int ModuleTest(string file, string? statePath, TextWriter output)
    {
        var set = SchemaSet.Load();
        if (set.Module is null)
        {
            output.WriteLine($"schémas : {set.Error}");
            return Invalid;
        }
        if (!File.Exists(file))
        {
            output.WriteLine($"module  : {file} introuvable");
            return Invalid;
        }
        string json;
        try { json = File.ReadAllText(file); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            output.WriteLine($"module  : illisible ({e.Message})");
            return Invalid;
        }

        var errors = set.Module.ValidateText(json, out var node);
        if (errors.Count > 0)
        {
            output.WriteLine($"module  : {Path.GetFileName(file)}");
            output.WriteLine($"schéma  : REFUSÉ ({errors.Count} erreur(s), schemas/module.schema.json)");
            foreach (var e in errors) output.WriteLine("          " + e);
            return Invalid;
        }
        if (node is not JsonObject o)
        {
            output.WriteLine("module  : JSON invalide (objet attendu)");
            return Invalid;
        }

        ModuleDefinition def;
        try { def = ModuleParser.Parse(o); }
        catch (ModuleFormatException e)
        {
            output.WriteLine($"module  : {Path.GetFileName(file)}");
            output.WriteLine($"format  : REFUSÉ — {e.Message}");
            return Invalid;
        }

        var states = LoadStates(statePath, out var loadError);
        if (states.Length == 0)
        {
            output.WriteLine($"état    : {loadError}");
            return Invalid;
        }

        output.WriteLine($"module  : {def.Id} · « {def.Label} » · écrans {(def.Screens is null ? "(défaut)" : string.Join(",", def.Screens))}");
        output.WriteLine("schéma  : ok (schemas/module.schema.json)");
        int total = 0;
        foreach (var (name, sample) in states)
        {
            IReadOnlyList<CustomRowView> rows;
            try { rows = ModuleParser.Render(sample, def); }
            catch (Exception e)
            {
                output.WriteLine($"évaluation : ÉCHEC — {e.GetType().Name}: {e.Message}");
                return Invalid;
            }
            output.WriteLine($"état    : {name} (écran {sample.Screen})");
            foreach (var r in rows)
                output.WriteLine($"  [{r.Tone ?? "text"}{(r.Grade is null ? "" : " " + r.Grade)}] {r.Text}");
            if (rows.Count == 0) output.WriteLine("  (aucune ligne : tous les « when » sont faux pour cet état)");
            total += rows.Count;
        }
        output.WriteLine($"{total} ligne(s) sur {states.Length} état(s) — OK");
        return Ok;
    }

    // ---------------- plugin-test ----------------

    public static int PluginTest(string dir, string? statePath, TextWriter output)
    {
        var set = SchemaSet.Load();
        if (set.Plugin is null || set.PluginOutput is null)
        {
            output.WriteLine($"schémas : {set.Error}");
            return Invalid;
        }
        if (!Directory.Exists(dir))
        {
            output.WriteLine($"plugin  : dossier introuvable ({dir})");
            return Invalid;
        }
        var manifest = PluginManifest.TryLoad(dir, set.Plugin, out var problem);
        if (manifest is null)
        {
            output.WriteLine($"plugin  : {Path.GetFileName(dir)}");
            output.WriteLine($"manifeste : REFUSÉ — {problem}");
            return Invalid;
        }

        var states = LoadStates(statePath, out var stateError);
        if (states.Length == 0)
        {
            output.WriteLine($"état    : {stateError}");
            return Invalid;
        }

        output.WriteLine($"plugin  : {manifest.Id} · « {manifest.Label} » · api 1 · timeout {manifest.TimeoutMs} ms · minInterval {manifest.MinIntervalMs} ms");
        output.WriteLine($"états   : {string.Join(", ", states.Select(s => s.Name))}");

        var proc = PluginProcess.Start(manifest, out var startProblem);
        if (proc is null)
        {
            output.WriteLine($"processus : ÉCHEC — {startProblem}");
            return RunFailed;
        }
        output.WriteLine($"processus : pid {proc.Pid} · nice {NiceOf(proc.Pid)?.ToString() ?? "?"} (le daemon le lance en nice 19)");
        try
        {
            int round = 0;
            foreach (var (name, state) in states)
            {
                round++;
                var line = "{\"api\":1,\"state\":" + StateJson.Serialize(state) + "}";
                if (!proc.Send(line, out var sendProblem))
                {
                    output.WriteLine($"tour {round} ({name}) : ÉCHEC — {sendProblem}");
                    return RunFailed;
                }
                if (!proc.TryRead(manifest.TimeoutMs, out var reply, out var readProblem))
                {
                    output.WriteLine($"tour {round} ({name}) : ÉCHEC — {readProblem}");
                    return RunFailed;
                }
                var errors = set.PluginOutput.ValidateText(reply!, out var node);
                if (errors.Count > 0)
                {
                    output.WriteLine($"tour {round} ({name}) : SORTIE INVALIDE");
                    foreach (var e in errors) output.WriteLine("          " + e);
                    return RunFailed;
                }
                var rows = (JsonArray)node!["rows"]!;
                output.WriteLine($"tour {round} ({name}) : {rows.Count} ligne(s)");
                foreach (var r in rows)
                {
                    var text = r!["text"]!.GetValue<string>();
                    var tone = r["tone"] is JsonValue tv && tv.TryGetValue<string>(out var t) ? t : "text";
                    var grade = r["grade"] is JsonValue gv && gv.TryGetValue<string>(out var g) ? " " + g : "";
                    output.WriteLine($"  [{tone}{grade}] {text}");
                }
            }
        }
        finally
        {
            proc.Dispose();
        }
        output.WriteLine($"{states.Length} état(s) — OK");
        return Ok;
    }

    // ---------------- états d'exemple ----------------

    private static (string Name, EngineState State)[] LoadStates(string? statePath, out string? error)
    {
        error = null;
        var list = new List<(string, EngineState)>();
        if (statePath is not null)
        {
            var (name, state, err) = LoadState(statePath);
            if (state is null) { error = err; return Array.Empty<(string, EngineState)>(); }
            return new[] { (name, state) };
        }
        var dir = FindIpcDir();
        if (dir is not null)
            foreach (var file in Directory.EnumerateFiles(dir, "state.*.sample.json").OrderBy(f => f, StringComparer.Ordinal).Take(8))
            {
                var (name, state, err) = LoadState(file);
                if (state is null) { error = $"{file} : {err}"; return Array.Empty<(string, EngineState)>(); }
                list.Add((name, state));
            }
        if (list.Count == 0) list.Add(("état minimal", Minimal()));
        return list.ToArray();
    }

    private static (string Name, EngineState? State, string? Error) LoadState(string? statePath)
    {
        if (statePath is null) return ("aucun état fourni", null, "indiquer --state <fichier> (docs/ipc/state.v14.sample.json)");
        if (!File.Exists(statePath)) return (statePath, null, $"fichier introuvable : {statePath}");
        string text;
        try { text = File.ReadAllText(statePath); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return (statePath, null, $"illisible : {e.Message}");
        }
        var state = StateJson.Deserialize(text);
        return state is null
            ? (statePath, null, "n'est pas un EngineState (contrat illisible)")
            : (Path.GetFileName(statePath), state, null);
    }

    /// <summary>État minimal (aucune run) : dernier recours quand aucun échantillon n'est trouvé.</summary>
    public static EngineState Minimal() => new(EngineState.CurrentVersion, DateTimeOffset.Now,
        new SourceHealth(false, true, false, null, null), Screen.Unknown, null, null, null, null, null,
        OverlaySettings.Default);

    /// <summary>Nice réel d'un processus (Linux) : preuve que le plugin tourne bien en 19.</summary>
    private static int? NiceOf(int pid)
    {
        try
        {
            var stat = File.ReadAllText($"/proc/{pid}/stat");
            var rest = stat[(stat.LastIndexOf(')') + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return rest.Length > 16 && int.TryParse(rest[16], out var nice) ? nice : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Dossier <c>docs/ipc</c> du dépôt (remontée depuis l'exécutable), ou null.</summary>
    public static string? FindIpcDir()
    {
        var d = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && !string.IsNullOrEmpty(d); i++)
        {
            var candidate = Path.Combine(d, "docs", "ipc");
            if (Directory.Exists(candidate) && Directory.EnumerateFiles(candidate, "*.sample.json").Any()) return candidate;
            d = Path.GetDirectoryName(d.TrimEnd(Path.DirectorySeparatorChar));
        }
        return null;
    }
}
