using Ultima.Saves.Model;

namespace Ultima.Saves;

/// <summary>Nouvel etat publie par <see cref="SaveWatcher"/>. <c>State</c> null = plus de run en cours.</summary>
public sealed class RunStateChangedEventArgs(RunState? state, string trigger) : EventArgs
{
    public RunState? State { get; } = state;

    /// <summary>Evenement fichier a l'origine (« initial », « renamed:current_run.save »…).</summary>
    public string Trigger { get; } = trigger;
}

public sealed class SaveWatcherErrorEventArgs(Exception error) : EventArgs
{
    public Exception Error { get; } = error;
}

/// <summary>
/// Surveille le dossier <c>saves/</c> du profil (inotify via FileSystemWatcher) et publie un nouveau
/// <see cref="RunState"/> quand la save de run change.
///
/// « Ecriture terminee » : le jeu n'ecrit JAMAIS la save en place. Il ecrit <c>current_run.save.tmp</c>
/// puis le RENOMME en <c>current_run.save</c> (GodotFileIo.cs:110-121). Le signal de fin d'ecriture est
/// donc le renommage (inotify IN_MOVED_TO => <see cref="FileSystemWatcher.Renamed"/>), pas
/// IN_CLOSE_WRITE sur la cible. Un debounce regroupe la rafale (tmp, rename, .backup) puis on relit.
/// Le watcher n'ouvre aucun fichier en ecriture ; la lecture est sans verrou.
/// </summary>
public sealed class SaveWatcher : IDisposable
{
    private static readonly string[] Watched = ["current_run.save", "current_run_mp.save"];

    private readonly ProfilePaths _paths;
    private readonly RunSaveReader _reader;
    private readonly TimeSpan _debounce;
    private readonly object _gate = new();
    private FileSystemWatcher? _fsw;
    private Timer? _timer;
    private string _pendingTrigger = "";
    private string? _lastKey;
    private bool _disposed;

    public event EventHandler<RunStateChangedEventArgs>? RunStateChanged;
    public event EventHandler<SaveWatcherErrorEventArgs>? Error;

    /// <summary>Dernier etat publie.</summary>
    public RunState? Current { get; private set; }

    public SaveWatcher(ProfilePaths paths, TimeSpan? debounce = null, RunSaveReader? reader = null)
    {
        _paths = paths;
        _reader = reader ?? new RunSaveReader();
        _debounce = debounce ?? TimeSpan.FromMilliseconds(300);
    }

    /// <param name="emitInitial">publie tout de suite l'etat present sur disque.</param>
    public void Start(bool emitInitial = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_fsw is not null) return;
        _timer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
        _fsw = new FileSystemWatcher(_paths.SavesDir)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        _fsw.Created += (_, e) => OnFs("created", e.Name);
        _fsw.Changed += (_, e) => OnFs("changed", e.Name);
        _fsw.Deleted += (_, e) => OnFs("deleted", e.Name);
        _fsw.Renamed += (_, e) => OnFs("renamed", e.Name);
        _fsw.Error += (_, e) => Error?.Invoke(this, new SaveWatcherErrorEventArgs(e.GetException()));
        _fsw.EnableRaisingEvents = true;
        if (emitInitial)
        {
            lock (_gate) _pendingTrigger = "initial";
            Flush(force: true);
        }
    }

    /// <summary>Vrai si le nom concerne la save de run (y compris .backup, mais pas .tmp).</summary>
    internal static bool IsRelevant(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        foreach (var w in Watched)
            if (name == w || name == w + ".backup") return true;
        return false;
    }

    private void OnFs(string kind, string? name)
    {
        if (!IsRelevant(name)) return;
        lock (_gate)
        {
            if (_disposed) return;
            _pendingTrigger = $"{kind}:{name}";
            _timer?.Change(_debounce, Timeout.InfiniteTimeSpan);
        }
    }

    private void Flush(bool force = false)
    {
        string trigger;
        lock (_gate)
        {
            if (_disposed) return;
            trigger = _pendingTrigger;
        }
        try
        {
            var state = _reader.ReadCurrent(_paths);
            string? key = state is null ? null : Fingerprint(state);
            lock (_gate)
            {
                if (!force && key == _lastKey) return;
                _lastKey = key;
                Current = state;
            }
            RunStateChanged?.Invoke(this, new RunStateChangedEventArgs(state, trigger));
        }
        catch (Exception e)
        {
            Error?.Invoke(this, new SaveWatcherErrorEventArgs(e));
        }
    }

    /// <summary>Empreinte de deduplication (une rafale d'evenements => une seule publication).</summary>
    internal static string Fingerprint(RunState s) =>
        $"{s.SourcePath}|{s.StartTime}|{s.SaveTime}|{s.FloorCount}|{s.VisitedMapCoords.Count}|" +
        string.Join(";", s.Players.Select(p =>
            $"{p.NetId}:{p.CurrentHp}/{p.MaxHp}:{p.Gold}:{p.Deck.Count}:{p.Relics.Count}:{p.Potions.Count}"));

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _fsw?.Dispose();
        _timer?.Dispose();
    }
}
