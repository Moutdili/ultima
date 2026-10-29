using System.Net.Sockets;
using System.Runtime.InteropServices;
using Ultima.Daemon;
using Ultima.Daemon.Mirror;
using Ultima.Daemon.Publish;
using Ultima.Daemon.Settings;
using Ultima.Daemon.Sources;
using Ultima.Daemon.State;
using Ultima.Engine.Contracts;
using Ultima.Saves;
using Ultima.Saves.Log;

// ultima-daemon — orchestration temps réel d'ULTIMA (docs/ipc/README.md).
// Codes : 0 ok ; 1 usage ; 2 déjà lancé (run) ; 3 daemon injoignable (status).

var cmd = args.Length > 0 ? args[0] : "help";
string? Opt(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
bool Flag(string name) => args.Contains(name);
Diag.Verbose = Flag("--verbose") || Flag("-v");

var options = new DaemonOptions
{
    Root = Opt("--root"),
    RuntimeDir = Opt("--runtime-dir") ?? StatePublisher.DefaultDir(),
    SettingsPath = Opt("--settings") ?? SettingsStore.DefaultPath(),
    GameComm = Opt("--game-comm") ?? "SlayTheSpire2",
    MirrorPath = Opt("--mirror") ?? ProcessMirrorRunner.DefaultPath(),
    MirrorEnabled = !Flag("--no-mirror"),
    Scoring = !Flag("--no-scoring"),
    DataDir = Opt("--data"),
};

return cmd switch
{
    "run" => Run(),
    "status" => Status(),
    "dump" => Dump(),
    _ => Usage(),
};

int Usage()
{
    Console.WriteLine("""
        ultima-daemon run    [options]   démarre le daemon (premier plan ; systemd : docs/ipc/ultima-daemon.service)
        ultima-daemon status [options]   état publié + daemon joignable ? (code 3 si injoignable)
        ultima-daemon dump   [options]   calcule UNE fois l'état depuis les fichiers et l'écrit sur stdout (rien n'est publié)

        options :
          --root DIR          racine des données du jeu (défaut : Flatpak Steam ; env ULTIMA_STS2_ROOT)
          --runtime-dir DIR   dossier IPC (défaut : $XDG_RUNTIME_DIR/ultima)
          --settings FILE     réglages (défaut : ~/.config/ultima/settings.json)
          --mirror FILE       exécutable ultima-mirror (défaut : $ULTIMA_MIRROR, ../mirror/, build/mirror/)
          --no-mirror         jamais de lecture mémoire (fichiers seulement)
          --no-scoring        pas de calcul (scores et chemins null)
          --data DIR          racine des données (meta/, catalog/…) ; défaut $ULTIMA_DATA puis data/ du dépôt
          --game-comm NAME    nom du processus du jeu (/proc/PID/comm, défaut SlayTheSpire2)
          -v, --verbose       journal détaillé (stderr)
        """);
    return cmd == "help" || cmd == "--help" ? 0 : 1;
}

int Run()
{
    StatePublisher.EnsurePrivateDir(options.RuntimeDir);
    using var lk = DaemonLock.TryAcquire(options.RuntimeDir, out var holder);
    if (lk is null)
    {
        Console.Error.WriteLine($"ultima-daemon tourne déjà{(holder is null ? "" : $" (pid {holder})")} : {options.RuntimeDir}/daemon.lock");
        return 2;
    }
    using var host = new DaemonHost(options);
    var stop = new ManualResetEventSlim();
    using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, c => { c.Cancel = true; stop.Set(); });
    using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, c => { c.Cancel = true; stop.Set(); });
    host.Start();
    stop.Wait(); // bloquant : aucun réveil
    Diag.Info("arrêt demandé");
    return 0;
}

int Status()
{
    var dir = options.RuntimeDir;
    var statePath = Path.Combine(dir, "state.json");
    var sockPath = Path.Combine(dir, "state.sock");
    bool alive = false;
    string? line = null;
    try
    {
        using var s = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        s.Connect(new UnixDomainSocketEndPoint(sockPath));
        alive = true;
        s.ReceiveTimeout = 1000;
        using var ns = new NetworkStream(s);
        using var rd = new StreamReader(ns);
        try { line = rd.ReadLine(); } catch (IOException) { }
    }
    catch (SocketException) { }
    if (line is null && File.Exists(statePath)) line = File.ReadAllText(statePath);
    Console.WriteLine($"daemon     : {(alive ? "joignable" : "INJOIGNABLE")} ({sockPath})");
    if (line is null) { Console.WriteLine("état       : aucun"); return alive ? 0 : 3; }
    var st = StateJson.Deserialize(line);
    if (st is null) { Console.WriteLine("état       : illisible"); return 3; }
    Console.WriteLine($"état       : {statePath} (contrat v{st.ContractVersion}, {st.At:yyyy-MM-dd HH:mm:ss}, il y a {(DateTimeOffset.Now - st.At).TotalSeconds:0} s)");
    Console.WriteLine($"santé      : jeu={(st.Health.GameRunning ? "lancé" : "fermé")} save={(st.Health.SaveOk ? "ok" : "KO")} miroir={(st.Health.MirrorOk ? "ok" : "—")} latence={st.Health.MirrorLatencyMs?.ToString() ?? "—"} ms");
    if (st.Health.Degraded is not null) Console.WriteLine($"DÉGRADÉ    : {st.Health.Degraded}");
    Console.WriteLine($"écran      : {st.Screen}");
    if (st.Run is { } r)
        Console.WriteLine($"run        : {r.CharacterName} A{r.Ascension} acte {r.ActIndex + 1} étage {r.Floor} PV {r.Hp}/{r.MaxHp} or {r.Gold} deck {r.Deck.Count} reliques {r.Relics.Count}");
    else Console.WriteLine("run        : aucune");
    if (st.Deck is { } d && d.Curses.Count > 0) Console.WriteLine($"malédictions: {string.Join(", ", d.Curses.Select(c => c.Name))}");
    Console.WriteLine($"modules    : {string.Join(" ", st.Settings.Modules.OrderBy(m => m.Order).Select(m => m.Enabled ? m.Id : $"({m.Id})"))}");
    return alive ? 0 : 3;
}

int Dump()
{
    // Calcul ponctuel, entièrement sur ce fil, SANS lecture mémoire et sans rien publier.
    var paths = DaemonHost.Locate(options.Root, out var perr);
    var (k, scorer, analyzer) = DaemonHost.BuildEngine(paths, options.Scoring, options.DataDir);
    EngineState? last = null;
    var core = new DaemonCore(new StateAssembler(k, scorer, analyzer, paths?.SteamId64), mirror: null,
        timers: new LoopTimers(a => a()), post: a => a(), publish: s => last = s);
    var settings = SettingsStore.TryLoad(options.SettingsPath, out var serr);
    if (serr is not null) Diag.Warn(serr);
    core.OnSettings(settings ?? OverlaySettings.Default);
    core.OnGame(GameProcessMonitor.FindByComm(options.GameComm) is not null);
    if (paths is null) core.OnProfileMissing(perr ?? "?");
    else
    {
        try { core.OnRun(new RunSaveReader().ReadCurrent(paths)); }
        catch (Exception e) { core.OnSaveError(e); }
        if (core.GameRunning)
        {
            core.BeginReplay();
            using var tail = new GodotLogTail(paths.GodotLog, Timeout.InfiniteTimeSpan, fromStart: true);
            tail.EventReceived += (_, e) => core.OnLog(e);
            tail.Poll();
            core.EndReplay();
        }
    }
    core.Publish();
    Console.WriteLine(StateJson.Serialize(last!, indented: !Flag("--compact")));
    return 0;
}
