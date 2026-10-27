using Ultima.Core.Damage;
using Ultima.Core.Model;

namespace Ultima.Core.Forecast;

/// <summary>
/// F1 + F2 en logique pure : <see cref="CombatSnapshot"/> → <see cref="TurnForecast"/>.
///
/// Règles (aucune valeur de jeu codée en dur) :
/// - « actuel » = <see cref="IntentSnapshot.PerHit"/> / <see cref="IntentSnapshot.Repeats"/> tels que
///   lus par le reader (pipeline du jeu) : jamais recalculés ici.
/// - Variantes = base décimale (<see cref="IntentSnapshot.RawPerHit"/>, repli PerHit si le reader ne
///   l'a pas rempli) × multiplicateur FOURNI par le snapshot, puis troncature du jeu
///   (<see cref="DamageMath.Truncate"/>), bornée à 0.
/// - Pas de double comptage : dealer déjà Weak → PerHitIfWeak null + AlreadyWeak ;
///   joueur local déjà Vulnerable → PerHitIfVulnerable null + AlreadyVulnerable.
/// - Le Label du jeu n'est jamais lu ici (oracle seulement, voir <see cref="IntentOracle"/>).
/// </summary>
public static class ForecastEngine
{
    /// <summary>Valeur de <see cref="IntentSnapshot.Kind"/> d'une intention d'attaque (contrat DTO).</summary>
    public const string AttackKind = "Attack";

    public static bool IsAttack(IntentSnapshot? intent)
        => intent is not null && string.Equals(intent.Kind, AttackKind, StringComparison.OrdinalIgnoreCase);

    public static TurnForecast Build(CombatSnapshot s)
    {
        ArgumentNullException.ThrowIfNull(s);

        var players = s.Players ?? Array.Empty<PlayerSnapshot>();
        var local = FindLocal(s, players);
        var localVulnerable = local?.IsVulnerable ?? false;

        var enemies = s.Enemies ?? Array.Empty<EnemySnapshot>();
        var enemyForecasts = new List<EnemyForecast>(enemies.Count);
        var turnHits = new List<int>();
        var turnOwners = new List<string>();
        var turnIntents = new List<IntentSnapshot>();   // un intent par attaque, pour le co-op

        foreach (var enemy in enemies)
        {
            if (enemy is null) continue;

            var attacks = new List<AttackForecast>();
            var enemyHits = new List<int>();

            foreach (var intent in enemy.Intents ?? Array.Empty<IntentSnapshot>())
            {
                if (!IsAttack(intent) || intent.Repeats <= 0) continue;

                var perHit = Math.Max(0, intent.PerHit);
                var repeats = intent.Repeats;
                var baseDamage = VariantBase(intent);

                int? ifWeak = enemy.IsWeak || intent.WeakMultiplier is null
                    ? null
                    : Math.Max(0, DamageMath.Truncate(baseDamage * intent.WeakMultiplier.Value));

                int? ifVulnerable = localVulnerable || intent.VulnerableMultiplier is null
                    ? null
                    : Math.Max(0, DamageMath.Truncate(baseDamage * intent.VulnerableMultiplier.Value));

                attacks.Add(new AttackForecast(perHit, repeats, ifWeak, ifVulnerable,
                    AlreadyWeak: enemy.IsWeak, AlreadyVulnerable: localVulnerable));

                for (var k = 0; k < repeats; k++)
                {
                    enemyHits.Add(perHit);
                    turnHits.Add(perHit);
                    turnOwners.Add(enemy.Name);
                }
                turnIntents.Add(intent);
            }

            enemyForecasts.Add(new EnemyForecast(enemy.CombatId, enemy.Name, attacks,
                SequenceLethal.Compute(enemyHits, s.Block, s.Hp)));
        }

        var turn = SequenceLethal.Compute(turnHits, s.Block, s.Hp);
        string? lethalEnemy = turn.LethalAt is int at ? turnOwners[at - 1] : null;

        return new TurnForecast(
            enemyForecasts,
            TotalIncoming: Sum(turnHits),
            LethalHitIndex: turn.LethalAt,
            LethalEnemyName: lethalEnemy,
            HpAfter: turn.HpAfter,
            BlockAfter: turn.BlockAfter,
            Overkill: turn.Overkill,
            Players: BuildThreats(s, players, turnIntents));
    }

    /// <summary>Base décimale des variantes : RawPerHit ; repli PerHit si le reader ne l'a pas rempli.</summary>
    private static decimal VariantBase(IntentSnapshot intent)
        => intent.RawPerHit == 0m && intent.PerHit > 0 ? intent.PerHit : intent.RawPerHit;

    private static PlayerSnapshot? FindLocal(CombatSnapshot s, IReadOnlyList<PlayerSnapshot> players)
    {
        foreach (var p in players)
            if (p is not null && p.IsLocal) return p;
        if (s.LocalNetId != 0)
            foreach (var p in players)
                if (p is not null && p.NetId == s.LocalNetId) return p;
        return null;
    }

    /// <summary>
    /// Pire cas par joueur : TOUTES les attaques du tour supposées sur lui. Coups = PerTarget de ce
    /// joueur (repli PerHit si absent) × Repeats, contre ses PV/block. Solo sans Players : 1 entrée
    /// synthétisée depuis les champs du snapshot (joueur local).
    /// </summary>
    private static IReadOnlyList<PlayerThreat> BuildThreats(
        CombatSnapshot s, IReadOnlyList<PlayerSnapshot> players, IReadOnlyList<IntentSnapshot> attacks)
    {
        var targets = players.Where(p => p is not null).ToList();
        if (targets.Count == 0)
            targets.Add(new PlayerSnapshot(s.LocalNetId, s.PlayerName ?? "", "", s.Hp, s.MaxHp, s.Block, IsLocal: true));

        var threats = new List<PlayerThreat>(targets.Count);
        foreach (var p in targets)
        {
            var hits = new List<int>();
            foreach (var intent in attacks)
            {
                var perHit = Math.Max(0, PerHitAgainst(intent, p.NetId));
                for (var k = 0; k < intent.Repeats; k++) hits.Add(perHit);
            }

            var r = SequenceLethal.Compute(hits, p.Block, p.Hp);
            threats.Add(new PlayerThreat(p.NetId, p.Name, p.CharacterId, Sum(hits), r.LethalAt, r.HpAfter));
        }
        return threats;
    }

    private static int PerHitAgainst(IntentSnapshot intent, ulong netId)
    {
        foreach (var t in intent.PerTarget ?? Array.Empty<TargetHit>())
            if (t is not null && t.PlayerNetId == netId) return t.PerHit;
        return intent.PerHit;
    }

    private static int Sum(IReadOnlyList<int> hits)
    {
        var total = 0;
        foreach (var h in hits) total += h;
        return total;
    }
}
