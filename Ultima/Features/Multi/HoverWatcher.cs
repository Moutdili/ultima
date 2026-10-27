using System;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game.PeerInput;
using MegaCrit.Sts2.Core.Runs;

namespace Ultima.Features.Multi;

/// <summary>
/// F4 voie A (aucun message custom) : écoute <c>RunManager.Instance.HoveredModelTracker.HoverChanged</c>
/// (Runs/RunManager.cs:155 ; Multiplayer/Game/PeerInput/HoveredModelTracker.cs), alimenté par le jeu
/// lui-même (PeerInputSynchronizer). Lecture seule : <c>GetHoveredModel</c> uniquement.
/// </summary>
internal sealed class HoverWatcher
{
    /// <summary>(playerId, classe de la carte survolée ou null, titre de la carte ou null).</summary>
    private readonly Action<ulong, string?, string?> _onHover;
    private HoveredModelTracker? _tracker;

    public HoverWatcher(Action<ulong, string?, string?> onHover) => _onHover = onHover;

    public bool IsAttached => _tracker != null;

    public void Attach()
    {
        try
        {
            var tracker = RunManager.Instance?.HoveredModelTracker;
            if (tracker == null)
            {
                ModEntry.Write("multi: hover tracker absent, voie A inactive");
                return;
            }
            if (ReferenceEquals(tracker, _tracker)) return;

            Detach();
            tracker.HoverChanged += OnHoverChanged;
            _tracker = tracker;
            ModEntry.Write("multi: voie A active (HoverChanged)");
        }
        catch (Exception ex)
        {
            ModEntry.Write($"multi: hover attach failed {ex.GetType().Name}: {ex.Message}");
        }
    }

    public void Detach()
    {
        try
        {
            if (_tracker != null) _tracker.HoverChanged -= OnHoverChanged;
        }
        catch (Exception ex)
        {
            ModEntry.Write($"multi: hover detach failed {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _tracker = null;
        }
    }

    private void OnHoverChanged(ulong playerId)
    {
        try
        {
            var model = _tracker?.GetHoveredModel(playerId);
            if (model is CardModel card)
                _onHover(playerId, card.GetType().Name, SafeTitle(card));
            else
                _onHover(playerId, null, null);
        }
        catch (Exception ex)
        {
            ModEntry.Write($"multi: hover handler failed {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string? SafeTitle(CardModel card)
    {
        try { return card.Title; }
        catch { return null; }
    }
}
