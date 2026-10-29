using Ultima.Daemon.Screens;

namespace Ultima.Daemon.Mirror;

/// <summary>Minuteur ponctuel (armé seulement par un événement ; jamais périodique).</summary>
public interface IOneShotTimers
{
    IDisposable Once(TimeSpan due, Action callback);
}

/// <summary>
/// Appels au miroir : UNIQUEMENT sur demande d'une transition, anti-rebond (≥ 250 ms de calme : une
/// rafale de 3 tirages de rareté = 1 lecture), jamais plus d'un en vol, et une garde au moment du
/// départ (écran ≠ combat, jeu lancé, miroir non désactivé). Toutes les méthodes s'exécutent sur la
/// boucle du daemon (pas de verrou) ; le rappel du minuteur est rapatrié sur la boucle par la fabrique.
/// </summary>
public sealed class MirrorScheduler
{
    public static readonly TimeSpan MinDebounce = TimeSpan.FromMilliseconds(250);

    private readonly IOneShotTimers _timers;
    private readonly TimeSpan _debounce;
    private readonly Func<MirrorNeed, string?> _guard;
    private readonly Action<MirrorNeed> _start;
    private IDisposable? _armed;
    private int _generation;
    private MirrorNeed _pending;

    /// <param name="guard">raison du refus, ou null si l'appel est permis.</param>
    /// <param name="start">lance l'appel (asynchrone) ; l'appelant DOIT rappeler <see cref="Completed"/>.</param>
    public MirrorScheduler(IOneShotTimers timers, Func<MirrorNeed, string?> guard, Action<MirrorNeed> start, TimeSpan? debounce = null)
    {
        _timers = timers;
        _guard = guard;
        _start = start;
        _debounce = debounce is { } d && d > MinDebounce ? d : MinDebounce;
    }

    public bool InFlight { get; private set; }
    public MirrorNeed Pending => _pending;
    public int Started { get; private set; }
    public int Refused { get; private set; }
    public string? LastRefusal { get; private set; }

    public void Request(MirrorNeed need)
    {
        if (need == MirrorNeed.None) return;
        _pending |= need;
        if (!InFlight) Arm();
    }

    /// <summary>Annule les demandes en attente (ex. entrée en combat).</summary>
    public void CancelPending()
    {
        _pending = MirrorNeed.None;
        _armed?.Dispose();
        _armed = null;
        _generation++;
    }

    private void Arm()
    {
        _armed?.Dispose();
        int gen = ++_generation;
        _armed = _timers.Once(_debounce, () => { if (gen == _generation) Fire(); });
    }

    internal void Fire()
    {
        _generation++;
        _armed?.Dispose();
        _armed = null;
        if (InFlight || _pending == MirrorNeed.None) return;
        var need = _pending;
        _pending = MirrorNeed.None;
        if (_guard(need) is { } why)
        {
            Refused++;
            LastRefusal = why;
            Diag.Debug($"mirror refusé ({need}) : {why}");
            return;
        }
        InFlight = true;
        Started++;
        _start(need);
    }

    public void Completed()
    {
        InFlight = false;
        if (_pending != MirrorNeed.None) Arm();
    }
}

/// <summary>Minuteurs réels : un <see cref="Timer"/> ponctuel dont le rappel est posté sur la boucle.</summary>
public sealed class LoopTimers(Action<Action> post) : IOneShotTimers
{
    public IDisposable Once(TimeSpan due, Action callback)
    {
        var t = new Timer(_ => post(callback), null, Timeout.Infinite, Timeout.Infinite);
        t.Change(due, Timeout.InfiniteTimeSpan);
        return t;
    }
}
