using System.Text.Json;
using System.Text.Json.Nodes;
using Ultima.Engine.Contracts;

namespace Ultima.Daemon.Custom;

/// <summary>Un fichier de module : compilé (<see cref="Definition"/>) ou refusé (<see cref="Problem"/>).</summary>
public sealed record ModuleEntry(string File, string Id, string Label, ModuleDefinition? Definition, string? Problem);

/// <summary>
/// Charge <c>~/.config/ultima/modules/*.json</c>, valide chaque fichier avec <c>schemas/module.schema.json</c>
/// puis le compile (docs/44 §6). Rechargement à chaud (inotify, regroupé 200 ms). Un fichier invalide est
/// publié en <c>Health=error</c> + <c>Problem</c> — il ne fait jamais tomber le daemon et ne bloque pas les autres.
/// </summary>
public sealed class ModuleStore : IDisposable
{
    public const int MaxFiles = 100;

    private readonly string _dir;
    private readonly JsonSchema? _schema;
    private readonly Action _changed;
    private readonly object _gate = new();
    private ModuleEntry[] _entries = Array.Empty<ModuleEntry>();
    private FileSystemWatcher? _watcher;
    private FileSystemWatcher? _parentWatcher;
    private Timer? _coalesce;
    private bool _disposed;

    public ModuleStore(string dir, JsonSchema? schema, Action changed)
    {
        _dir = dir;
        _schema = schema;
        _changed = changed;
    }

    public string Dir => _dir;

    public IReadOnlyList<ModuleEntry> Entries
    {
        get { lock (_gate) return _entries; }
    }

    public void Start()
    {
        Reload();
        Watch();
    }

    // ---------------- chargement ----------------

    /// <summary>Relit le dossier (absent = aucun module). Ne lève jamais.</summary>
    public void Reload()
    {
        var list = new List<ModuleEntry>();
        string[] files;
        try
        {
            files = Directory.Exists(_dir)
                ? Directory.EnumerateFiles(_dir, "*.json").OrderBy(f => f, StringComparer.Ordinal).Take(MaxFiles).ToArray()
                : Array.Empty<string>();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Diag.Warn($"modules : dossier illisible ({_dir}) : {e.Message}");
            files = Array.Empty<string>();
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var entry = LoadOne(file);
            if (entry.Definition is not null && !ids.Add(entry.Definition.Id))
            {
                Diag.Warn($"module {entry.File} : id « {entry.Definition.Id} » déjà chargé, ignoré");
                continue;
            }
            list.Add(entry);
        }
        lock (_gate) _entries = list.ToArray();
        if (files.Length > 0) Diag.Info($"modules perso : {list.Count(e => e.Definition is not null)}/{files.Length} valide(s)");
        foreach (var e in list.Where(e => e.Problem is not null)) Diag.Warn($"module {Path.GetFileName(e.File)} : {e.Problem}");
    }

    private ModuleEntry LoadOne(string file)
    {
        var stem = Path.GetFileNameWithoutExtension(file);
        var id = stem.StartsWith(CustomIds.Prefix, StringComparison.Ordinal) ? stem : CustomIds.Prefix + stem;
        string json;
        try { json = File.ReadAllText(file); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new ModuleEntry(file, id, id, null, $"fichier illisible : {e.Message}");
        }

        JsonNode? node = null;
        if (_schema is null)
            return new ModuleEntry(file, id, id, null, "schémas du contrat introuvables (schemas/module.schema.json)");
        var errors = _schema.ValidateText(json, out node);
        if (errors.Count > 0) return new ModuleEntry(file, id, id, null, "schéma : " + string.Join(" ; ", errors));
        if (node is not JsonObject obj) return new ModuleEntry(file, id, id, null, "JSON invalide : objet attendu");
        var label = obj["label"] is JsonValue lv && lv.TryGetValue<string>(out var l) ? l : id;
        var fileId = obj["id"] is JsonValue iv && iv.TryGetValue<string>(out var i) ? i : id;
        try
        {
            return new ModuleEntry(file, fileId, label, ModuleParser.Parse(obj), null);
        }
        catch (ModuleFormatException e)
        {
            return new ModuleEntry(file, fileId, label, null, e.Message);
        }
    }

    private static string Short(string s) => s.Length <= 160 ? s : s[..160] + "…";

    // ---------------- publication ----------------

    /// <summary>Vues publiées dans <c>EngineState.Custom</c> pour l'état courant.</summary>
    public IReadOnlyList<CustomModuleView> Views(EngineState state)
    {
        var entries = Entries;
        if (entries.Count == 0) return Array.Empty<CustomModuleView>();
        var views = new List<CustomModuleView>(entries.Count);
        foreach (var e in entries)
        {
            if (e.Definition is null)
            {
                views.Add(new CustomModuleView(e.Id, e.Label, CustomIds.SourceModule, null,
                    Array.Empty<CustomRowView>(), CustomHealth.Error, e.Problem));
                continue;
            }
            try
            {
                views.Add(new CustomModuleView(e.Definition.Id, e.Definition.Label, CustomIds.SourceModule,
                    e.Definition.Screens, ModuleParser.Render(state, e.Definition), CustomHealth.Ok, null));
            }
            catch (Exception ex)
            {
                // ne peut arriver que sur un bug interne : publié, jamais propagé
                Diag.Warn($"module {e.Id} : {ex.GetType().Name}: {ex.Message}");
                views.Add(new CustomModuleView(e.Definition.Id, e.Definition.Label, CustomIds.SourceModule,
                    e.Definition.Screens, Array.Empty<CustomRowView>(), CustomHealth.Error,
                    $"évaluation : {ex.GetType().Name}"));
            }
        }
        return views;
    }

    // ---------------- rechargement à chaud ----------------

    private void Watch()
    {
        try
        {
            var target = Directory.Exists(_dir) ? _dir : Path.GetDirectoryName(_dir);
            if (target is null) return;
            if (Directory.Exists(_dir))
            {
                Attach(target, recursive: false);
                return;
            }
            if (!Directory.Exists(target)) return;
            // le dossier des modules n'existe pas encore : on surveille son apparition
            _parentWatcher = new FileSystemWatcher(target)
            {
                NotifyFilter = NotifyFilters.DirectoryName,
                IncludeSubdirectories = false,
            };
            void OnParent(string? name)
            {
                if (!string.Equals(name, Path.GetFileName(_dir), StringComparison.Ordinal)) return;
                if (!Directory.Exists(_dir) || _watcher is not null) return;
                Attach(_dir, recursive: false);
                Kick();
            }
            _parentWatcher.Created += (_, e) => OnParent(e.Name);
            _parentWatcher.Renamed += (_, e) => OnParent(e.Name);
            _parentWatcher.EnableRaisingEvents = true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Diag.Warn($"modules : surveillance impossible ({_dir}) : {e.Message}");
        }
    }

    private void Attach(string dir, bool recursive)
    {
        _watcher = new FileSystemWatcher(dir)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            IncludeSubdirectories = recursive,
            Filter = "*.json",
        };
        _watcher.Created += (_, _) => Kick();
        _watcher.Changed += (_, _) => Kick();
        _watcher.Deleted += (_, _) => Kick();
        _watcher.Renamed += (_, _) => Kick();
        _watcher.EnableRaisingEvents = true;
    }

    /// <summary>Regroupe une rafale d'écritures (tmp + rename) en un seul rechargement.</summary>
    private void Kick()
    {
        if (_disposed) return;
        _coalesce ??= new Timer(_ =>
        {
            if (_disposed) return;
            try
            {
                Reload();
                _changed();
            }
            catch (Exception e) { Diag.Warn($"modules : rechargement : {e.GetType().Name}: {e.Message}"); }
        }, null, Timeout.Infinite, Timeout.Infinite);
        _coalesce.Change(200, Timeout.Infinite);
    }

    public void Dispose()
    {
        _disposed = true;
        _watcher?.Dispose();
        _parentWatcher?.Dispose();
        _coalesce?.Dispose();
    }
}
