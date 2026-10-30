using Ultima.Engine.Contracts;

namespace Ultima.Daemon.Custom;

/// <summary>
/// Ensemble des plugins ACTIFS : seulement ceux listés dans <c>settings.json</c> → <c>pluginsEnabled</c>
/// (docs/44 §7 et section « pluginsEnabled »). Un dossier manquant, un manifeste invalide ou une API
/// inconnue donnent une vue <c>Health=error</c> avec sa raison — jamais un arrêt, jamais un plugin deviné.
/// </summary>
public sealed class PluginHost : IDisposable
{
    private readonly string _dir;
    private readonly JsonSchema? _manifestSchema;
    private readonly JsonSchema? _outputSchema;
    private readonly Action _changed;
    private readonly object _gate = new();
    private List<Slot> _slots = new();
    private bool _disposed;

    private sealed record Slot(string Id, PluginManifest? Manifest, string? Problem, PluginRuntime? Runtime);

    public PluginHost(string dir, JsonSchema? manifestSchema, JsonSchema? outputSchema, Action changed)
    {
        _dir = dir;
        _manifestSchema = manifestSchema;
        _outputSchema = outputSchema;
        _changed = changed;
    }

    public string Dir => _dir;

    /// <summary>Plugins actifs (manifeste chargé) — mesure et tests.</summary>
    public int ActiveCount
    {
        get { lock (_gate) return _slots.Count(s => s.Runtime is not null); }
    }

    public int Count
    {
        get { lock (_gate) return _slots.Count; }
    }

    /// <summary>
    /// Applique la liste <c>pluginsEnabled</c> : arrête les plugins retirés, démarre les nouveaux,
    /// garde les autres tels quels. Appelé au démarrage et à chaque rechargement des réglages.
    /// </summary>
    public void SetEnabled(IReadOnlyList<string> ids)
    {
        List<Slot> removed = new();
        lock (_gate)
        {
            if (_disposed) return;
            var wanted = new List<string>();
            foreach (var id in ids)
            {
                if (string.IsNullOrWhiteSpace(id)) continue;
                if (!id.StartsWith(CustomIds.Prefix, StringComparison.Ordinal))
                {
                    Diag.Warn($"pluginsEnabled : « {id} » ignoré (un id commence par « {CustomIds.Prefix} »)");
                    continue;
                }
                // jamais de chemin : l'id ne doit pas sortir du dossier des plugins
                if (id.Contains('/') || id.Contains('\\') || id.Contains("..", StringComparison.Ordinal))
                {
                    Diag.Warn($"pluginsEnabled : « {id} » ignoré (id invalide)");
                    continue;
                }
                if (wanted.Contains(id)) continue;
                wanted.Add(id);
            }

            var next = new List<Slot>(wanted.Count);
            foreach (var id in wanted)
            {
                var existing = _slots.FirstOrDefault(s => s.Id == id);
                if (existing is not null) { next.Add(existing); continue; }
                next.Add(LoadSlot(id));
            }
            foreach (var old in _slots)
                if (!next.Any(s => ReferenceEquals(s, old))) removed.Add(old);
            _slots = next;
        }
        // arrêt HORS du verrou : tuer un enfant peut prendre quelques dizaines de ms
        foreach (var r in removed) Stop(r);
        if (ids.Count > 0) Diag.Info($"plugins activés : {string.Join(", ", ids)}");
    }

    private Slot LoadSlot(string id)
    {
        var dir = Path.Combine(_dir, id);
        if (!Directory.Exists(dir))
        {
            var why = $"dossier introuvable : {dir}";
            Diag.Warn($"plugin {id} : {why}");
            return new Slot(id, null, why, null);
        }
        var manifest = PluginManifest.TryLoad(dir, _manifestSchema, out var problem);
        if (manifest is null)
        {
            Diag.Warn($"plugin {id} : {problem}");
            return new Slot(id, null, problem, null);
        }
        PluginRuntime? runtime = null;
        try { runtime = new PluginRuntime(manifest, _outputSchema, _changed); }
        catch (Exception e)
        {
            Diag.Warn($"plugin {id} : démarrage impossible : {e.GetType().Name}: {e.Message}");
        }
        return new Slot(id, manifest, runtime is null ? "démarrage impossible" : null, runtime);
    }

    private static void Stop(Slot slot)
    {
        try { slot.Runtime?.Dispose(); }
        catch (Exception e) { Diag.Warn($"plugin {slot.Id} : arrêt : {e.GetType().Name}"); }
    }

    /// <summary>Dépose la ligne d'état auprès de chaque plugin (retour immédiat).</summary>
    public void Submit(string line)
    {
        Slot[] slots;
        lock (_gate) slots = _slots.ToArray();
        foreach (var s in slots) s.Runtime?.Submit(line);
    }

    /// <summary>Vues publiées dans <c>EngineState.Custom</c> (une par plugin activé).</summary>
    public IReadOnlyList<CustomModuleView> Views()
    {
        Slot[] slots;
        lock (_gate) slots = _slots.ToArray();
        if (slots.Length == 0) return Array.Empty<CustomModuleView>();
        var views = new List<CustomModuleView>(slots.Length);
        foreach (var s in slots)
        {
            if (s.Runtime is not null) { views.Add(s.Runtime.View()); continue; }
            views.Add(new CustomModuleView(s.Id, s.Id, CustomIds.SourcePlugin, null,
                Array.Empty<CustomRowView>(), CustomHealth.Error, s.Problem ?? "plugin indisponible"));
        }
        return views;
    }

    public void Dispose()
    {
        Slot[] slots;
        lock (_gate)
        {
            _disposed = true;
            slots = _slots.ToArray();
            _slots.Clear();
        }
        foreach (var s in slots) Stop(s);
    }
}
