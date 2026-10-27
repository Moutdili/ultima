using Ultima.Core.Damage;

namespace Ultima.Core.Model;

// CONTRAT FIGÉ (chef, M0) : sortie de F1/F2/F3/F4, consommée par l'UI.
// Producteurs : Features/Forecast (B1) et Features/Multi|Priority (B2). Consommateur : UI (A2).

/// <summary>F1 pour une intention d'attaque, contre le joueur local.</summary>
public sealed record AttackForecast(
    int PerHit, int Repeats,      // actuel : identique au label du jeu
    int? PerHitIfWeak,            // hypothèse ; null = non applicable (voir AlreadyWeak)
    int? PerHitIfVulnerable,      // hypothèse ; null = non applicable (voir AlreadyVulnerable)
    bool AlreadyWeak,             // → libellé « déjà affaibli »
    bool AlreadyVulnerable)       // → libellé « déjà vulnérable »
{
    public int Total => PerHit * Repeats;
}

/// <summary>F1 + F2 pour un ennemi : ses attaques et sa létalité seul, sur PV + block actuels du joueur local.</summary>
public sealed record EnemyForecast(
    uint CombatId, string EnemyName,
    IReadOnlyList<AttackForecast> Attacks,
    LethalIndex.Result Lethal);

/// <summary>Co-op : pire cas pour un joueur (toutes les attaques du tour supposées sur lui).</summary>
public sealed record PlayerThreat(
    ulong NetId, string Name, string CharacterId,
    int Incoming, int? LethalHitIndex, int HpAfter);

/// <summary>
/// F2 sur le tour complet : toutes les attaques ennemies concaténées dans l'ordre des ennemis,
/// block partagé entre les coups. Répond à « à quel coup précis je meurs ce tour-ci ».
/// </summary>
public sealed record TurnForecast(
    IReadOnlyList<EnemyForecast> Enemies,
    int TotalIncoming,            // somme des coups avant block
    int? LethalHitIndex,          // index 1-based dans la séquence complète ; null = survie
    string? LethalEnemyName,      // ennemi qui porte le coup létal
    int HpAfter, int BlockAfter, int Overkill,
    IReadOnlyList<PlayerThreat> Players);

public enum PrioritySource { LocalHand, RemoteHover, RemoteMessage }

/// <summary>Alerte de priorité de tour (F3 locale, F4 distante).</summary>
public sealed record PriorityAlert(
    string CardName, string CardClass,
    ulong PlayerNetId, string PlayerName, string CharacterId,
    bool IsRemote, PrioritySource Source, int Turn);
