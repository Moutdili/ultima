using Ultima.Engine.Contracts;
using Ultima.Saves.Log;
using Ultima.Saves.Model;

namespace Ultima.Daemon.Screens;

/// <summary>Lecture mémoire demandée par une transition (jamais en boucle).</summary>
[Flags]
public enum MirrorNeed
{
    None = 0,
    Reward = 1,
    Shop = 2,
    /// <summary>v1.2 : options de l'événement (section « event » du miroir, Miroir 2).</summary>
    Event = 4,
}

/// <summary>
/// Détection d'écran par transitions (skill sts2-daemon, table « Signal → Action »). Pure et
/// déterministe : aucun E/S, aucune horloge — rejouable sur un log réel.
///
/// Règles (prouvées sur les logs de la fenêtre de test du 2026-10-29) :
/// - <c>Preloading 'X Room'</c> : Combat / Event (lecture event) / RestSite / Treasure / Merchant→Shop (lecture shop).
/// - <c>Card rarity: Rolled</c> après une salle (combat, événement, feu, trésor) : écran de récompense
///   (lecture reward). Les MÊMES lignes précèdent aussi la génération du marchand (vote → tirages →
///   <c>Preloading 'Merchant Room'</c>) : depuis la carte, un tirage ne change donc rien.
/// - <c>Player N selected card reward</c> (joueur local) : fin de la récompense.
/// - <c>Player vote changed for N</c> (joueur local) / <c>Continuing run</c> : carte.
/// - Processus lancé : menu ; arrêté : inconnu ; plus de run en cours (save absente) : menu.
/// </summary>
public sealed class ScreenTracker
{
    private Screen _beforeReward = Screen.Map;

    public Screen Screen { get; private set; } = Screen.Unknown;

    /// <summary>Joueur local (solo : 1 ; co-op : SteamID64). Null : tout joueur est accepté.</summary>
    public ulong? LocalNetId { get; set; }

    /// <summary>Nombre de tirages de rareté vus depuis l'entrée dans l'écran courant (signal seul, jamais les valeurs).</summary>
    public int RollsSinceScreen { get; private set; }

    public bool IsLocal(ulong netId) => LocalNetId is null || LocalNetId == netId;

    public MirrorNeed OnLog(GodotLogEvent e)
    {
        switch (e)
        {
            case RoomEntered r:
                return r.Kind switch
                {
                    RoomKind.Combat => Set(Screen.Combat),
                    RoomKind.Event => Set(Screen.Event) | MirrorNeed.Event,
                    RoomKind.RestSite => Set(Screen.RestSite),
                    RoomKind.Treasure => Set(Screen.Treasure),
                    RoomKind.Merchant => Set(Screen.Shop) | MirrorNeed.Shop,
                    _ => MirrorNeed.None,
                };
            case CombatRoomCreated:
                return Set(Screen.Combat);
            case CardRewardRolled:
                if (Screen is Screen.Combat or Screen.Event or Screen.RestSite or Screen.Treasure)
                {
                    _beforeReward = Screen;
                    Set(Screen.Reward);
                }
                if (Screen != Screen.Reward) return MirrorNeed.None;
                RollsSinceScreen++;
                return MirrorNeed.Reward;
            case CardRewardSelected sel when IsLocal(sel.PlayerNetId) && Screen == Screen.Reward:
                EndReward();
                return MirrorNeed.None;
            case MapVote v when IsLocal(v.PlayerNetId):
                return Set(Screen.Map);
            case RunContinued:
                return Set(Screen.Map);
            default:
                return MirrorNeed.None;
        }
    }

    /// <summary>Fin de l'écran de récompense (carte choisie, ou le miroir ne voit aucune offre locale).</summary>
    public void EndReward()
    {
        if (Screen != Screen.Reward) return;
        Set(_beforeReward == Screen.Combat ? Screen.Map : _beforeReward);
    }

    public void OnGameRunning(bool running) => Set(running ? Screen.Menu : Screen.Unknown);

    /// <summary>Save relue. Plus de run pendant que le jeu tourne : retour au menu (fin de run, abandon).</summary>
    public void OnRun(RunState? run, bool gameRunning)
    {
        if (!gameRunning) return;
        if (run is null) Set(Screen.Menu);
    }

    private MirrorNeed Set(Screen s)
    {
        if (s != Screen) RollsSinceScreen = 0;
        Screen = s;
        return MirrorNeed.None;
    }
}
