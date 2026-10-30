using Ultima.Daemon.Publish;
using Ultima.Engine.Contracts;

namespace Ultima.Daemon.Custom;

/// <summary>Emplacements XDG des modules et plugins (docs/44 §2), jamais écrits par le daemon.</summary>
public static class CustomPaths
{
    public static string ConfigRoot()
    {
        var cfg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(cfg))
            cfg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(cfg, "ultima");
    }

    public static string DataRoot()
    {
        var data = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrWhiteSpace(data))
            data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        return Path.Combine(data, "ultima");
    }

    /// <summary><c>~/.config/ultima/modules</c></summary>
    public static string ModulesDir() => Path.Combine(ConfigRoot(), "modules");

    /// <summary><c>~/.local/share/ultima/plugins</c></summary>
    public static string PluginsDir() => Path.Combine(DataRoot(), "plugins");
}

/// <summary>
/// Modules déclaratifs + plugins (contrat v1.4, docs/44 §6-7) : un seul point d'entrée pour le daemon.
/// <see cref="Views"/> rend l'état courant des modules (évalués ici, bornés) et des plugins (dernière
/// réponse valide, ou la raison de son absence) ; <see cref="Publish"/> dépose la ligne d'état aux plugins.
/// Aucun des deux ne peut retarder la publication : les plugins travaillent sur leur propre fil.
/// </summary>
public sealed class CustomHost : IStateSink, IDisposable
{
    private readonly ModuleStore _modules;
    private readonly PluginHost _plugins;

    public CustomHost(string modulesDir, string pluginsDir, SchemaSet schemas, Action changed)
    {
        _modules = new ModuleStore(modulesDir, schemas.Module, changed);
        _plugins = new PluginHost(pluginsDir, schemas.Plugin, schemas.PluginOutput, changed);
        SchemaError = schemas.Error;
    }

    /// <summary>Raison pour laquelle les schémas manquent (null = tout va bien).</summary>
    public string? SchemaError { get; }

    public ModuleStore Modules => _modules;
    public PluginHost Plugins => _plugins;

    public void Start() => _modules.Start();

    public void SetPluginsEnabled(IReadOnlyList<string> ids) => _plugins.SetEnabled(ids);

    /// <summary>Rien de configuré ⇒ <c>EngineState.Custom</c> reste null (état v1.3 inchangé).</summary>
    public bool IsEmpty => _modules.Entries.Count == 0 && _plugins.Count == 0 && SchemaError is null;

    /// <summary>Ligne d'état déposée aux plugins (appelée par le publieur, après écriture de l'état).</summary>
    public void Publish(EngineState state, string line) => _plugins.Submit(line);

    /// <summary>Vues pour <c>EngineState.Custom</c> ; null si aucun module ni plugin n'est configuré.</summary>
    public IReadOnlyList<CustomModuleView>? Views(EngineState state)
    {
        if (SchemaError is not null && _modules.Entries.Count == 0 && _plugins.Count == 0) return null;
        var modules = _modules.Views(state);
        var plugins = _plugins.Views();
        if (modules.Count == 0 && plugins.Count == 0) return null;
        var all = new List<CustomModuleView>(modules.Count + plugins.Count);
        all.AddRange(modules);
        all.AddRange(plugins);
        return all;
    }

    public void Dispose()
    {
        _modules.Dispose();
        _plugins.Dispose();
    }
}
