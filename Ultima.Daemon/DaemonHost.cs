using System.Collections.Concurrent;
using Ultima.Daemon.Knowledge;
using Ultima.Daemon.Mirror;
using Ultima.Daemon.Publish;
using Ultima.Daemon.Settings;
using Ultima.Daemon.Sources;
using Ultima.Daemon.State;
using Ultima.Engine.Contracts;
using Ultima.Engine.Paths;
using Ultima.Engine.Scoring;
using Ultima.Engine.Scoring.Meta;
using Ultima.Engine.Scoring.Personal;
using Ultima.Saves;

namespace Ultima.Daemon;

public sealed record DaemonOptions
{
    public string? Root { get; init; }
    public string RuntimeDir { get; init; } = StatePublisher.DefaultDir();
    public string SettingsPath { get; init; } = SettingsStore.DefaultPath();
    public string GameComm { get; init; } = "SlayTheSpire2";
    public string? MirrorPath { get; init; } = ProcessMirrorRunner.DefaultPath();
    public bool MirrorEnabled { get; init; } = true;
    public TimeSpan MirrorTimeout { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan MirrorDebounce { get; init; } = MirrorScheduler.MinDebounce;
    public bool Scoring { get; init; } = true;
}

/// <summary>
/// Assemble les sources réelles autour de <see cref="DaemonCore"/> : une boucle sur un fil dédié
/// (attente bloquante = 0 % CPU), inotify (saves, log, réglages), pidfd (fin du jeu), miroir enfant.
/// </summary>
public sealed class DaemonHost : IDisposable
{
    private readonly DaemonOptions _o;
    private readonly BlockingCollection<Action> _queue = new();
    private readonly List<IDisposable> _owned = new();
    private Thread? _loop;
    private int _loopThreadId = -1;

    public DaemonHost(DaemonOptions options) => _o = options;

    public DaemonCore? Core { get; private set; }
    public StatePublisher? Publisher { get; private set; }

    /// <summary>Exécute l'action sur la boucle (immédiatement si l'on y est déjà).</summary>
    public void Post(Action a)
    {
        if (Environment.CurrentManagedThreadId == _loopThreadId) { Run(a); return; }
        if (!_queue.IsAddingCompleted) _queue.Add(a);
    }

    private static void Run(Action a)
    {
        try { a(); }
        catch (Exception e) { Diag.Warn($"boucle : {e.GetType().Name}: {e.Message}"); }
    }

    /// <summary>Construit connaissance + calcul. Toute erreur du calcul → null (scores « — »), jamais un arrêt.</summary>
    public static (IKnowledge, IScorer?, IPathAnalyzer?) BuildEngine(ProfilePaths? paths, bool scoring)
    {
        IKnowledge knowledge = NullKnowledge.Instance; // TODO(Encyclopédiste) : Ultima.Knowledge dès qu'elle est livrée
        if (!scoring) return (knowledge, null, null);
        IPersonalStats personal = PersonalStats.Empty();
        try { if (paths is not null) personal = PersonalStats.Load(paths.HistoryDir); }
        catch (Exception e) { Diag.Warn($"historique illisible : {e.Message}"); }
        IScorer? scorer = null;
        try { scorer = new Scorer(MetaTable.Load(), personal); }
        catch (Exception e) { Diag.Warn($"calcul des scores indisponible : {e.GetType().Name}: {e.Message}"); }
        IPathAnalyzer? analyzer = null;
        try { analyzer = new PathAnalyzer(personal); }
        catch (Exception e) { Diag.Warn($"calcul des chemins indisponible : {e.Message}"); }
        return (knowledge, scorer, analyzer);
    }

    public static ProfilePaths? Locate(string? root, out string? error)
    {
        error = null;
        try { return ProfileLocator.Locate(root); }
        catch (Exception e) { error = e.Message; return null; }
    }

    /// <summary>Démarre tout ; rend la main (la boucle tourne sur son fil).</summary>
    public void Start()
    {
        var paths = Locate(_o.Root, out var profileError);
        var (knowledge, scorer, analyzer) = BuildEngine(paths, _o.Scoring);
        var assembler = new StateAssembler(knowledge, scorer, analyzer, paths?.SteamId64);

        Publisher = new StatePublisher(_o.RuntimeDir);
        var socket = new SocketServer(Path.Combine(_o.RuntimeDir, "state.sock"));
        Publisher.AddSink(socket);
        _owned.Add(Publisher);

        IMirrorRunner? mirror = _o.MirrorEnabled && _o.MirrorPath is not null
            ? new ProcessMirrorRunner(_o.MirrorPath, _o.MirrorTimeout, _o.GameComm == "SlayTheSpire2" ? null : _o.GameComm)
            : null;
        var reader = new RunSaveReader();
        var core = new DaemonCore(assembler, mirror, new LoopTimers(Post), Post,
            s => { if (Publisher.Publish(s)) Diag.Debug($"état publié ({s.Screen})"); },
            rereadSave: paths is null ? null : () => { try { return reader.ReadCurrent(paths); } catch { return null; } },
            debounce: _o.MirrorDebounce);
        Core = core;

        _loop = new Thread(() =>
        {
            _loopThreadId = Environment.CurrentManagedThreadId;
            foreach (var a in _queue.GetConsumingEnumerable()) Run(a);
        }) { IsBackground = false, Name = "ultima-loop" };
        _loop.Start();

        // Tout le démarrage s'exécute SUR la boucle (les sources appellent le cœur en direct pendant ce temps).
        var ready = new ManualResetEventSlim();
        Post(() =>
        {
            try
            {
                var settings = SettingsStore.TryLoad(_o.SettingsPath, out var serr);
                if (serr is not null) Diag.Warn(serr);
                core.OnSettings(settings ?? OverlaySettings.Default);
                var sw = new SettingsWatcher(_o.SettingsPath, () => Post(() =>
                {
                    var s = SettingsStore.TryLoad(_o.SettingsPath, out var err);
                    if (err is not null) { Diag.Warn(err + " (réglage précédent conservé)"); return; }
                    Diag.Info("réglages rechargés");
                    core.OnSettings(s!);
                }));
                sw.Start();
                _owned.Add(sw);

                var monitor = new GameProcessMonitor(_o.GameComm);
                monitor.Changed += (running, _) => Post(() => core.OnGame(running));
                _owned.Add(monitor);
                monitor.Rescan();

                if (paths is null)
                {
                    core.OnProfileMissing(profileError ?? "?");
                    Diag.Warn($"profil introuvable : {profileError}");
                }
                else
                {
                    Diag.Info($"profil {paths.ProfileDir} (profil {paths.ProfileId}{(paths.ProfileIdFromDefault ? ", défaut" : "")})");
                    if (Directory.Exists(paths.SavesDir))
                    {
                        var saves = new SaveWatcher(paths, reader: reader);
                        saves.RunStateChanged += (_, e) => Post(() => core.OnRun(e.State));
                        saves.Error += (_, e) => Post(() => core.OnSaveError(e.Error));
                        _owned.Add(saves);
                        saves.Start(emitInitial: true);
                    }
                    else core.OnProfileMissing($"dossier absent : {paths.SavesDir}");

                    var log = new LogWatcher(paths.GodotLog, replayExisting: core.GameRunning, onActivity: () =>
                    {
                        if (!monitor.Running) monitor.Rescan();
                    });
                    log.EventReceived += (_, e) => Post(() => core.OnLog(e));
                    _owned.Add(log);
                    core.BeginReplay();
                    int n = log.Start();
                    core.EndReplay();
                    Diag.Info($"godot.log : {(core.GameRunning ? $"{n} événement(s) rejoué(s)" : "jeu fermé, suivi depuis la fin")} ; écran {core.Screen}");
                }
                core.Publish();
            }
            finally { ready.Set(); }
        });
        ready.Wait();
        int tuned = ProcessPriority.Lower();
        Diag.Info($"prêt : {Publisher.StatePath} + {socket.Path} ; miroir {(mirror is null ? "désactivé" : _o.MirrorPath)} ; nice 10 sur {tuned} fil(s)");
    }

    public void Stop()
    {
        _queue.CompleteAdding();
        if (_loop is not null && Environment.CurrentManagedThreadId != _loopThreadId) _loop.Join(TimeSpan.FromSeconds(2));
    }

    public void Dispose()
    {
        Stop();
        foreach (var d in Enumerable.Reverse(_owned)) { try { d.Dispose(); } catch { } }
    }
}
