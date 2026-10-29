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
    /// <summary>Racine des données (ULTIMA_DATA) ; null = variable puis remontée depuis l'exécutable.</summary>
    public string? DataDir { get; init; }
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

/// <summary>Connaissance + calcul (Hermes). Chaque pièce absente ou en panne → null (« — »), jamais un arrêt.</summary>
    public sealed record EngineParts(IKnowledge Knowledge, IScorer? Scorer, IPathAnalyzer? Paths,
        IRoutePlanner? Route, IEventAdvisor? Events, IReviewBuilder? Review)
    {
        public StateAssembler Assembler(ulong? steamId64) => new(Knowledge, Scorer, Paths, steamId64, Route, Events);
    }

    /// <summary>Construit connaissance + calcul. Toute erreur du calcul → null (scores « — »), jamais un arrêt.</summary>
    public static EngineParts BuildEngine(ProfilePaths? paths, bool scoring, string? dataDir = null)
    {
        var knowledge = LoadKnowledge();
        if (!scoring) return new(knowledge, null, null, null, null, null);
        if (DataRoot.Apply(dataDir) is null) return new(knowledge, null, null, null, null, null);
        IPersonalStats personal = PersonalStats.Empty();
        try { if (paths is not null) personal = PersonalStats.Load(paths.HistoryDir, steamId64: paths.SteamId64); }
        catch (Exception e) { Diag.Warn($"historique illisible : {e.Message}"); }
        T? Try<T>(string what, Func<T> make) where T : class
        {
            try { return make(); }
            catch (Exception e) { Diag.Warn($"{what} indisponible : {e.GetType().Name}: {e.Message}"); return null; }
        }
        return new EngineParts(knowledge,
            Try<IScorer>("calcul des scores", () => new Scorer(MetaTable.Load(), personal)),
            Try<IPathAnalyzer>("calcul des chemins", () => new PathAnalyzer(personal)),
            Try<IRoutePlanner>("planificateur de route", () => new Ultima.Engine.Scoring.Paths.RoutePlanner()),
            Try<IEventAdvisor>("conseil d'événement", () => new Ultima.Engine.Scoring.Events.EventAdvisor()),
            Review: null); // aucune implémentation d'IReviewBuilder chez Hermes à ce jour → « review indisponible »
    }

    /// <summary>
    /// Base de connaissance de l'Encyclopédiste (<c>~/.local/share/ultima/kb/kb.json</c>, construite par
    /// <c>tools/kb/build-kb</c>). Absente ou illisible : <see cref="NullKnowledge"/> (noms = identifiants).
    /// </summary>
    public static IKnowledge LoadKnowledge()
    {
        try
        {
            var kb = Ultima.Knowledge.KnowledgeBase.TryLoadDefault(out var error);
            if (kb is not null) { Diag.Info($"connaissance : {Ultima.Knowledge.KnowledgeBase.DefaultPath}"); return kb; }
            Diag.Warn($"connaissance indisponible ({error}) : noms = identifiants");
        }
        catch (Exception e) { Diag.Warn($"connaissance indisponible ({e.GetType().Name}: {e.Message}) : noms = identifiants"); }
        return NullKnowledge.Instance;
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
        var engine = BuildEngine(paths, _o.Scoring, _o.DataDir);
        var assembler = engine.Assembler(paths?.SteamId64);

        Publisher = new StatePublisher(_o.RuntimeDir);
        var socket = new SocketServer(Path.Combine(_o.RuntimeDir, "state.sock"));
        Publisher.AddSink(socket);
        var review = new ReviewService(Path.Combine(_o.RuntimeDir, "review"), () => paths?.HistoryDir, engine.Review,
            () => Core?.GameRunning ?? false);
        socket.OnRequest = review.HandleAsync;
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
