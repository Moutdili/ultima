using Ultima.Daemon.Mirror;
using Ultima.Daemon.Screens;
using Ultima.Daemon.State;
using Ultima.Engine.Contracts;
using Ultima.Saves.Log;
using Ultima.Saves.Model;

namespace Ultima.Daemon;

/// <summary>
/// Cœur du daemon : reçoit des événements (log, save, processus du jeu, miroir, réglages), tient
/// l'écran courant, décide des lectures mémoire (UNIQUEMENT sur transition) et publie l'état.
/// Toutes les méthodes s'exécutent sur UN seul fil (la boucle) : aucun verrou, ordre déterministe.
/// Aucun minuteur périodique : seuls l'anti-rebond du miroir (ponctuel) et les sources inotify existent.
/// </summary>
public sealed class DaemonCore
{
    public const string DegradedPatch = "lecture mémoire indisponible (patch du jeu)";

    private readonly IMirrorRunner? _mirror;
    private readonly Action<Action> _post;
    private readonly StateAssembler _assembler;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Action<EngineState> _publish;
    private readonly Func<RunState?>? _rereadSave;
    private readonly Func<int?> _ptraceScope;

    private bool _gameRunning;
    private RunState? _run;
    private RunPart? _runPart;
    private bool _saveOk = true;
    private string? _saveError;
    private MRewards? _reward;
    private MShop? _shop;
    private int? _memGold;
    private int? _memDeckCount;
    private bool _mirrorOk;
    private int? _mirrorMs;
    private string? _mirrorDegraded;
    private bool _patchDrift;
    private string? _divergence;
    private OverlaySettings _settings = OverlaySettings.Default;
    private MEvent? _event;
    private bool _eventUnsupported;
    private bool _eventRetried; // une seule relecture par salle quand les options ne sont pas encore connues
    private MRun? _memRun;
    private DateTimeOffset _memRunAt, _runAt;
    private (MEvent? Mem, RunPart? Run, EventView? View) _eventKey;
    private bool _replaying;
    // vues contextuelles mises en cache : les scores ne sont recalculés que si leurs entrées changent
    private (MRewards? Mem, RunPart? Run, RewardView? View) _rewardKey;
    private (MShop? Mem, RunPart? Run, int? Gold, ShopView? View) _shopKey;

    public DaemonCore(StateAssembler assembler, IMirrorRunner? mirror, IOneShotTimers timers, Action<Action> post,
        Action<EngineState> publish, Func<DateTimeOffset>? clock = null, Func<RunState?>? rereadSave = null,
        Func<int?>? ptraceScope = null, TimeSpan? debounce = null)
    {
        _assembler = assembler;
        _mirror = mirror;
        _post = post;
        _publish = publish;
        _clock = clock ?? (() => DateTimeOffset.Now);
        _rereadSave = rereadSave;
        _ptraceScope = ptraceScope ?? ReadPtraceScope;
        Scheduler = new MirrorScheduler(timers, Guard, StartMirror, debounce);
    }

    public ScreenTracker Tracker { get; } = new();
    public MirrorScheduler Scheduler { get; }
    public Screen Screen => Tracker.Screen;
    public bool GameRunning => _gameRunning;

    /// <summary>
    /// v1.4 : modules perso et plugins (docs/44 §6-7). Appelé à chaque composition d'état avec l'état déjà
    /// assemblé ; rend la liste publiée dans <c>EngineState.Custom</c> (null = pas de section). Toute exception
    /// est journalisée et laisse <c>Custom</c> vide : jamais un arrêt, jamais un état partiel.
    /// </summary>
    public Func<EngineState, IReadOnlyList<CustomModuleView>?>? CustomProvider { get; set; }

    /// <summary>Appels au miroir effectivement lancés (probe), et lectures de structure (layout).</summary>
    public int MirrorProbes { get; private set; }
    public int MirrorLayouts { get; private set; }

    /// <summary>Écran au moment de chaque lancement de probe (preuve « jamais en combat »).</summary>
    public List<(Screen Screen, MirrorNeed Need)> ProbeLog { get; } = new();

    public EngineState? LastState { get; private set; }

    // ---------------- événements ----------------

    public void OnSettings(OverlaySettings settings)
    {
        _settings = settings;
        Publish();
    }

    public void OnGame(bool running)
    {
        if (running == _gameRunning) return;
        _gameRunning = running;
        Tracker.OnGameRunning(running);
        Scheduler.CancelPending();
        _reward = null; _shop = null; _memGold = null; _memDeckCount = null; _event = null; _memRun = null;
        _mirrorOk = false; _mirrorDegraded = null; _divergence = null; _patchDrift = false;
        _eventUnsupported = false; // le miroir a pu être mis à jour entre deux sessions
        if (running && _mirror is not null) CheckLayout();
        Tracker.OnRun(_run, _gameRunning);
        Publish();
    }

    public void OnRun(RunState? run)
    {
        ApplyRun(run);
        // Relire le marchand après un achat : la save a changé APRÈS notre lecture (or/deck différents).
        if (!_replaying && Screen == Screen.Shop && _shop is not null && _runPart is { } rp
            && (rp.Player.Gold != _memGold || rp.Player.Deck.Count != _memDeckCount))
            Scheduler.Request(MirrorNeed.Shop);
        Publish();
    }

    private void ApplyRun(RunState? run)
    {
        _run = run;
        _runAt = _clock();
        _saveOk = true;
        _saveError = null;
        try { _runPart = _assembler.BuildRun(run); }
        catch (Exception e) { Diag.Warn($"assemblage run : {e.Message}"); _runPart = null; }
        Tracker.LocalNetId = _runPart?.Player.NetId;
        Tracker.OnRun(run, _gameRunning);
    }

    public void OnSaveError(Exception e)
    {
        _saveOk = false;
        _saveError = "save illisible : dernier état valide conservé";
        Diag.Warn($"save : {e.GetType().Name}: {e.Message}");
        Publish();
    }

    /// <summary>Profil du jeu introuvable (jamais lancé, autre installation).</summary>
    public void OnProfileMissing(string why)
    {
        _saveOk = false;
        _saveError = $"profil du jeu introuvable ({why})";
        Publish();
    }

    public void OnLog(GodotLogEvent e)
    {
        var before = Tracker.Screen;
        var need = Tracker.OnLog(e);
        AfterScreenChange(before);
        if (!_replaying && need != MirrorNeed.None) Scheduler.Request(need);
        Publish();
    }

    /// <summary>Rejoue le log existant (démarrage, jeu déjà lancé) sans aucune lecture mémoire ni publication.</summary>
    public void BeginReplay() => _replaying = true;

    public void EndReplay()
    {
        _replaying = false;
        // Transition « démarrage du daemon » : si l'on tombe sur un écran de récompense/marchand, une lecture.
        if (Screen == Screen.Reward) Scheduler.Request(MirrorNeed.Reward);
        else if (Screen == Screen.Shop) Scheduler.Request(MirrorNeed.Shop);
        else if (Screen == Screen.Event) Scheduler.Request(MirrorNeed.Event);
        Publish();
    }

    private void AfterScreenChange(Screen before)
    {
        var now = Tracker.Screen;
        if (now == before) return;
        if (now == Screen.Combat) Scheduler.CancelPending(); // aucune lecture mémoire en combat (v1)
        if (before == Screen.Reward) _reward = null;
        if (before == Screen.Event) _event = null;
        if (now == Screen.Event || before == Screen.Event) _eventRetried = false;
        if (before == Screen.Shop) { _shop = null; _memGold = null; _memDeckCount = null; _divergence = null; }
        Diag.Debug($"écran {before} → {now}");
    }

    // ---------------- miroir ----------------

    /// <summary>Garde au DÉPART de la lecture (l'écran a pu changer pendant l'anti-rebond).</summary>
    private string? Guard(MirrorNeed need)
    {
        if (_mirror is null) return "miroir désactivé";
        if (!_gameRunning) return "jeu non lancé";
        if (_patchDrift) return "patch du jeu";
        if (Screen == Screen.Combat) return "combat en cours";
        if (Screen == Screen.Event && need.HasFlag(MirrorNeed.Event) && _eventUnsupported)
            return "section « event » non prise en charge par ce miroir";
        bool relevant = (need.HasFlag(MirrorNeed.Reward) && Screen == Screen.Reward)
                        || (need.HasFlag(MirrorNeed.Shop) && Screen == Screen.Shop)
                        || (need.HasFlag(MirrorNeed.Event) && Screen == Screen.Event);
        return relevant ? null : $"transition périmée (écran {Screen})";
    }

    private void StartMirror(MirrorNeed need)
    {
        // ne lire que ce que l'écran courant affiche
        need = Screen switch { Screen.Shop => MirrorNeed.Shop, Screen.Event => MirrorNeed.Event, _ => MirrorNeed.Reward };
        MirrorProbes++;
        ProbeLog.Add((Screen, need));
        Task<MirrorResult> task;
        try { task = _mirror!.ProbeAsync(need); }
        catch (Exception e) { task = Task.FromResult(MirrorResult.Failed(ProbeExit.SpawnFailed, 0, e.Message)); }
        task.ContinueWith(t =>
        {
            var r = t.IsCompletedSuccessfully ? t.Result
                : MirrorResult.Failed(ProbeExit.SpawnFailed, 0, t.Exception?.GetBaseException().Message ?? "annulé");
            _post(() => OnMirrorResult(need, r));
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private void CheckLayout()
    {
        MirrorLayouts++;
        Task<MirrorResult> task;
        try { task = _mirror!.LayoutAsync(); }
        catch (Exception e) { task = Task.FromResult(MirrorResult.Failed(ProbeExit.SpawnFailed, 0, e.Message)); }
        task.ContinueWith(t =>
        {
            var r = t.IsCompletedSuccessfully ? t.Result : MirrorResult.Failed(ProbeExit.SpawnFailed, 0, "layout");
            _post(() => OnLayoutResult(r));
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    internal void OnLayoutResult(MirrorResult r)
    {
        Diag.Info($"mirror layout exit={r.Exit} {r.ElapsedMs} ms");
        if (!_gameRunning) return;
        if (r.Exit == ProbeExit.LayoutDrift)
        {
            _patchDrift = true;
            _mirrorOk = false;
            _mirrorDegraded = DegradedPatch;
            Scheduler.CancelPending();
        }
        else if (r.Exit is ProbeExit.Missing) _mirrorDegraded = "lecture mémoire indisponible (ultima-mirror introuvable)";
        Publish();
    }

    internal void OnMirrorResult(MirrorNeed need, MirrorResult r)
    {
        try
        {
            Diag.Info($"mirror {need} exit={r.Exit} mirror ms={r.ElapsedMs}{(r.Error is null ? "" : " — " + r.Error)}");
            _mirrorMs = (int)Math.Min(int.MaxValue, r.ElapsedMs);
            if (!_gameRunning) return;
            _mirrorOk = r.Ok;
            _mirrorDegraded = Degradation(r);

            if (need.HasFlag(MirrorNeed.Reward) && Screen == Screen.Reward)
            {
                if (r.Reward is { } rw)
                {
                    if (rw.CardRewards.Count == 0) { Tracker.EndReward(); _reward = null; } // pas d'offre locale
                    else _reward = rw;
                }
            }
            if (need.HasFlag(MirrorNeed.Shop) && Screen == Screen.Shop)
            {
                // Vérification croisée à CHAQUE lecture du marchand (entrée ou relecture après achat — cette
                // dernière n'a lieu que sur save réécrite, donc fraîche), AVANT de retenir l'inventaire.
                var local = r.Run?.Local;
                var shop = r.Shop is { InShop: true } ? r.Shop : null;
                _memGold = local?.Gold;
                _memDeckCount = local?.Deck.Count;
                if (shop is not null && _runPart is not null)
                {
                    if (local is null)
                    {
                        _divergence = "vérification croisée impossible (joueur local illisible en mémoire)";
                        shop = null;
                    }
                    else if (!CrossCheckAgrees(local)) shop = null;
                }
                _shop = shop;
                if (_divergence is not null) _mirrorOk = false;
                else if (shop is not null && r.Run is { } mr) { _memRun = mr; _memRunAt = _clock(); }
            }
            if (need.HasFlag(MirrorNeed.Event) && Screen == Screen.Event)
            {
                if (r.Unsupported.Contains("event"))
                {
                    if (!_eventUnsupported) Diag.Info("miroir : section « event » non prise en charge (Miroir 2 à venir) ; événements non lus jusqu'au prochain lancement du jeu");
                    _eventUnsupported = true;
                    _event = null;
                }
                else
                {
                    _event = r.Event;
                    // options pas encore connues (préchargement de la salle) : UNE relecture après l'anti-rebond,
                    // jamais de boucle (le drapeau n'est remis à zéro qu'au changement d'écran)
                    if (r.Event is { OptionsKnown: false } && !_eventRetried)
                    {
                        _eventRetried = true;
                        Diag.Debug("événement : options inconnues, une relecture programmée");
                        Scheduler.Request(MirrorNeed.Event);
                    }
                }
            }
        }
        finally
        {
            Scheduler.Completed();
            Publish();
        }
    }

    /// <returns>true si la mémoire et la save concordent (sinon <c>_divergence</c> est renseigné).</returns>
    private bool CrossCheckAgrees(MPlayer mem)
    {
        var diffs = CrossCheck.Compare(_runPart!.Player, mem);
        if (diffs.Count > 0 && _rereadSave?.Invoke() is { } fresh)
        {
            // la save a pu être réécrite pendant la lecture : relire avant de conclure (sans relancer le miroir)
            ApplyRun(fresh);
            if (_runPart?.Player is { } p2) diffs = CrossCheck.Compare(p2, mem);
        }
        if (diffs.Count == 0)
        {
            if (_divergence is not null) Diag.Info("vérification croisée save↔mémoire : accord rétabli");
            _divergence = null;
            return true;
        }
        _divergence = "divergence save↔mémoire : " + string.Join(", ", diffs);
        Diag.Warn(_divergence);
        return false; // jamais de faux chiffre : l'inventaire lu dans un état incohérent n'est pas publié
    }

    private string? Degradation(MirrorResult r) => r.Exit switch
    {
        ProbeExit.Ok => null,
        ProbeExit.NotRunning => null, // le moniteur de processus fait foi
        ProbeExit.AttachFailed when (r.Health?.PtraceScope ?? _ptraceScope()) is int s and > 0 =>
            $"lecture mémoire refusée : ptrace_scope={s} (doit valoir 0)",
        ProbeExit.AttachFailed => "lecture mémoire indisponible (attache impossible)",
        ProbeExit.LayoutDrift => DegradedPatch,
        ProbeExit.Watchdog or ProbeExit.KilledByDaemon => "lecture mémoire trop lente (abandonnée)",
        ProbeExit.Unstable => "lecture mémoire instable (réessai à la prochaine transition)",
        ProbeExit.Missing => "lecture mémoire indisponible (ultima-mirror introuvable)",
        _ => $"lecture mémoire indisponible (code {r.Exit})",
    };

    internal static int? ReadPtraceScope()
    {
        try { return int.Parse(File.ReadAllText("/proc/sys/kernel/yama/ptrace_scope").Trim()); }
        catch { return null; }
    }

    // ---------------- publication ----------------

    public EngineState Compose()
    {
        var degraded = string.Join(" ; ", new[] { _saveError, _mirrorDegraded, _divergence }.Where(s => s is not null));
        var health = new SourceHealth(_gameRunning, _saveOk, _gameRunning && _mirrorOk, _mirrorMs,
            degraded.Length == 0 ? null : degraded);
        RewardView? reward = null;
        ShopView? shop = null;
        if (Screen == Screen.Reward && _reward is not null)
        {
            if (!ReferenceEquals(_rewardKey.Mem, _reward) || !ReferenceEquals(_rewardKey.Run, _runPart))
                _rewardKey = (_reward, _runPart, _assembler.BuildReward(_reward, _runPart));
            reward = _rewardKey.View;
        }
        if (Screen == Screen.Shop && _shop is not null)
        {
            if (!ReferenceEquals(_shopKey.Mem, _shop) || !ReferenceEquals(_shopKey.Run, _runPart) || _shopKey.Gold != _memGold)
                _shopKey = (_shop, _runPart, _memGold, _assembler.BuildShop(_shop, _runPart, _memGold));
            shop = _shopKey.View;
        }
        EventView? ev = null;
        if (Screen == Screen.Event && _event is not null)
        {
            if (!ReferenceEquals(_eventKey.Mem, _event) || !ReferenceEquals(_eventKey.Run, _runPart))
                _eventKey = (_event, _runPart, _assembler.BuildEvent(_event, _runPart));
            ev = _eventKey.View;
        }
        // v1.2 : PV de l'équipe pris en mémoire si la dernière lecture (vérifiée) est plus récente que la save
        var runView = _runPart?.View;
        if (runView is not null && _memRun is not null && _memRunAt > _runAt)
            runView = runView with { Party = StateAssembler.OverlayParty(runView.Party, _memRun) };
        var state = StateAssembler.Compose(_clock(), health, Screen, _runPart, reward, shop, _settings, ev, runView);
        if (CustomProvider is { } provider)
        {
            try
            {
                if (provider(state) is { } custom) state = state with { Custom = custom };
            }
            catch (Exception e)
            {
                Diag.Warn($"modules perso : {e.GetType().Name}: {e.Message}");
            }
        }
        return state;
    }

    public void Publish()
    {
        if (_replaying) return;
        var s = Compose();
        LastState = s;
        _publish(s);
    }
}
