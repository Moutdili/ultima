using Ultima.Saves.Log;

namespace Ultima.Daemon.Sources;

/// <summary>
/// Suit <c>logs/godot.log</c> en PILOTANT <see cref="GodotLogTail.Poll"/> par inotify au lieu de son
/// minuteur périodique (<see cref="GodotLogTail.Start"/> n'est jamais appelé : 0 réveil au repos).
/// Une rafale d'écritures est regroupée par un minuteur PONCTUEL (100 ms) armé par l'événement.
/// Lecture seule et sans verrou (NoLockReader d'Ultima.Saves).
/// </summary>
public sealed class LogWatcher : IDisposable
{
    private readonly string _logPath;
    private readonly GodotLogTail _tail;
    private readonly Action _onActivity;
    private readonly TimeSpan _coalesce;
    private readonly object _gate = new();
    private FileSystemWatcher? _fsw;
    private Timer? _timer;
    private bool _armed;
    private bool _disposed;

    /// <param name="replayExisting">relire le log courant depuis le début au premier sondage (jeu déjà lancé).</param>
    /// <param name="onActivity">appelé à chaque activité du dossier des logs (sert à détecter le lancement du jeu).</param>
    public LogWatcher(string logPath, bool replayExisting, Action onActivity, TimeSpan? coalesce = null)
    {
        _logPath = logPath;
        _onActivity = onActivity;
        _coalesce = coalesce ?? TimeSpan.FromMilliseconds(100);
        _tail = new GodotLogTail(logPath, pollInterval: Timeout.InfiniteTimeSpan, fromStart: replayExisting);
    }

    public event EventHandler<GodotLogEvent>? EventReceived
    {
        add => _tail.EventReceived += value;
        remove => _tail.EventReceived -= value;
    }

    public event EventHandler<Exception>? Error
    {
        add => _tail.Error += value;
        remove => _tail.Error -= value;
    }

    public int Polls { get; private set; }

    /// <summary>Premier sondage synchrone (rejoue l'existant si demandé), puis surveillance inotify.</summary>
    public int Start()
    {
        int n = PollNow();
        var dir = Path.GetDirectoryName(_logPath)!;
        if (!Directory.Exists(dir)) { Diag.Warn($"dossier des logs absent : {dir}"); return n; }
        var name = Path.GetFileName(_logPath);
        _fsw = new FileSystemWatcher(dir)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            IncludeSubdirectories = false,
        };
        void On(string? n1, string? n2 = null)
        {
            if (n1 != name && n2 != name) return;
            _onActivity();
            Kick();
        }
        _fsw.Changed += (_, e) => On(e.Name);
        _fsw.Created += (_, e) => On(e.Name);
        _fsw.Renamed += (_, e) => On(e.Name, e.OldName);
        _fsw.Deleted += (_, e) => On(e.Name);
        _fsw.EnableRaisingEvents = true;
        return n;
    }

    private void Kick()
    {
        lock (_gate)
        {
            if (_disposed || _armed) return;
            _armed = true;
            _timer ??= new Timer(_ =>
            {
                lock (_gate) _armed = false;
                PollNow();
            }, null, Timeout.Infinite, Timeout.Infinite);
            _timer.Change(_coalesce, Timeout.InfiniteTimeSpan);
        }
    }

    private int PollNow()
    {
        try { Polls++; return _tail.Poll(); }
        catch (Exception e) { Diag.Warn($"godot.log : {e.Message}"); return 0; }
    }

    public void Dispose()
    {
        lock (_gate) _disposed = true;
        _fsw?.Dispose();
        _timer?.Dispose();
        _tail.Dispose();
    }
}
