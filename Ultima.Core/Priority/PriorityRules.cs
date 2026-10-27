using Ultima.Core.Model;

namespace Ultima.Core.Priority;

/// <summary>
/// F3 (alerte locale) et utilitaires F4, purs et testables.
///
/// Règle F3 : une carte de la main déclenche une alerte si
///   1. sa classe est au catalogue (<c>priority_candidate == true</c>),
///   2. elle est jouable (<c>CardSnapshot.Playable</c> = <c>CanPlay</c>, seul test fiable),
///   3. si l'entrée a <c>glow_gold == true</c> : <c>CardSnapshot.GlowsGold</c> (oracle du jeu, bonus actif MAINTENANT).
/// Entrée sans glow_gold : la condition de tour n'est pas observable sans réimplémenter la carte
/// (historique de combat, etc.) → alerte dès que la carte est jouable (faux positifs possibles, assumés).
/// Recalcul complet à chaque snapshot : une carte jouée/défaussée quitte la main, son alerte disparaît.
/// </summary>
public static class PriorityRules
{
    public static IReadOnlyList<PriorityAlert> FromHand(CombatSnapshot s, PriorityCatalog catalog)
    {
        if (s?.Hand is not { Count: > 0 } hand || catalog is null || catalog.Count == 0)
            return Array.Empty<PriorityAlert>();

        var (netId, name, characterId) = ResolveLocal(s);
        List<PriorityAlert>? alerts = null;
        foreach (var card in hand)
        {
            if (card is null || !catalog.TryGet(card.ClassName, out var entry)) continue;
            if (!IsActive(card, entry)) continue;

            (alerts ??= new List<PriorityAlert>()).Add(new PriorityAlert(
                CardName: string.IsNullOrEmpty(card.Name) ? card.ClassName : card.Name,
                CardClass: card.ClassName,
                PlayerNetId: netId, PlayerName: name, CharacterId: characterId,
                IsRemote: false, Source: PrioritySource.LocalHand, Turn: s.Turn));
        }
        return alerts is null ? Array.Empty<PriorityAlert>() : alerts;
    }

    /// <summary>Jouable, et glow gold si l'entrée le permet (sinon condition de tour non observable).</summary>
    public static bool IsActive(CardSnapshot card, PriorityEntry entry) =>
        card.Playable && (!entry.GlowGold || card.GlowsGold);

    /// <summary>Joueur local : entrée <c>IsLocal</c> de <c>Players</c>, repli <c>LocalNetId</c>/<c>PlayerName</c>.</summary>
    public static (ulong NetId, string Name, string CharacterId) ResolveLocal(CombatSnapshot s)
    {
        var local = s.Players?.FirstOrDefault(p => p is not null && p.IsLocal);
        if (local is null) return (s.LocalNetId, s.PlayerName ?? "", "");
        return (local.NetId, string.IsNullOrEmpty(local.Name) ? s.PlayerName ?? "" : local.Name, local.CharacterId ?? "");
    }

    /// <summary>Nom et personnage d'un joueur via <c>Players</c> ; repli « #netId » / "" (snapshot absent ou joueur inconnu).</summary>
    public static (string Name, string CharacterId) ResolvePlayer(CombatSnapshot? s, ulong netId)
    {
        var p = s?.Players?.FirstOrDefault(x => x is not null && x.NetId == netId);
        if (p is null) return (FallbackName(netId), "");
        return (string.IsNullOrEmpty(p.Name) ? FallbackName(netId) : p.Name, p.CharacterId ?? "");
    }

    public static string FallbackName(ulong netId) => $"#{netId}";

    /// <summary>Alerte distante (survol voie A ou message voie B), nom/personnage résolus via le snapshot.</summary>
    public static PriorityAlert Remote(
        CombatSnapshot? s, ulong playerNetId, string cardClass, string cardName, PrioritySource source, int turn)
    {
        var (name, characterId) = ResolvePlayer(s, playerNetId);
        return new PriorityAlert(
            CardName: string.IsNullOrEmpty(cardName) ? cardClass : cardName,
            CardClass: cardClass,
            PlayerNetId: playerNetId, PlayerName: name, CharacterId: characterId,
            IsRemote: true, Source: source, Turn: turn);
    }

    /// <summary>
    /// Clé stable d'un ensemble d'alertes (ordre indifférent) pour n'émettre qu'au changement.
    /// Ensemble vide → "" quel que soit le tour (pas de ré-émission « rien » à chaque tour) ;
    /// sinon le tour est inclus (un message d'un tour passé est jeté comme périmé par le pair).
    /// </summary>
    public static string Signature(IReadOnlyList<PriorityAlert>? alerts, int turn)
    {
        if (alerts is null || alerts.Count == 0) return "";
        var keys = alerts.Select(a => a.CardClass + "|" + a.CardName).OrderBy(k => k, StringComparer.Ordinal);
        return $"T{turn}:" + string.Join(";", keys);
    }
}

/// <summary>Règles de la voie B (messages custom), pures.</summary>
public static class PriorityProtocol
{
    /// <summary>Version majeure du protocole ULTIMA (hello + priorité). Incrémenter = incompatibilité.</summary>
    public const int Version = 1;

    public static bool IsCompatible(int remoteVersion) => remoteVersion == Version;

    /// <summary>Message périmé : émis pour un tour antérieur au tour courant.</summary>
    public static bool IsStale(int messageTurn, int currentTurn) => messageTurn < currentTurn;

    /// <summary>
    /// Émission d'une priorité autorisée seulement si ≥ 1 pair distant connu ET tous les pairs distants
    /// connus ont répondu au hello (le message est diffusé par l'hôte à tout le monde : un seul pair
    /// non capable suffit à interdire l'envoi).
    /// </summary>
    public static bool CanSendPriority(IEnumerable<ulong> remotePeers, IReadOnlySet<ulong> capablePeers)
    {
        var any = false;
        foreach (var peer in remotePeers)
        {
            if (!capablePeers.Contains(peer)) return false;
            any = true;
        }
        return any;
    }
}
