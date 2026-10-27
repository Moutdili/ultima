using Ultima.Core.Damage;
using Ultima.Core.Model;

namespace Ultima.UI;

/// <summary>Textes de l'overlay, purs (sans Godot). Aucun chiffre inventé : tout vient des DTO.</summary>
public static class OverlayText
{
    public const string Waiting = "ULTIMA — en attente du combat";
    public const string ForecastUnavailable = "prévision indisponible";
    public const string Footer = "F8 masquer · F9 détail";
    public const string Lethal = "LÉTAL";

    public static string Ordinal(int n) => n == 1 ? "1er" : $"{n}e";

    public static string Header(CombatSnapshot s) => $"T{s.Turn} · {s.Hp}/{s.MaxHp} PV · {s.Block} block";

    public static string Hits(int perHit, int repeats) => $"{perHit}x{repeats}";

    public static bool IsAttack(IntentSnapshot i) =>
        i.Repeats > 0 && i.Kind.Contains("Attack", StringComparison.OrdinalIgnoreCase);

    public static bool HasAttack(IReadOnlyList<IntentSnapshot> intents)
    {
        for (int i = 0; i < intents.Count; i++) if (IsAttack(intents[i])) return true;
        return false;
    }

    public static string Damage(IReadOnlyList<AttackForecast> attacks)
    {
        if (attacks.Count == 0) return "—";
        if (attacks.Count == 1) return Hits(attacks[0].PerHit, attacks[0].Repeats);
        return string.Join("+", attacks.Select(a => Hits(a.PerHit, a.Repeats)));
    }

    /// <summary>Repli sans prévision : PerHit x Repeats lus tels quels dans le snapshot.</summary>
    public static string DamageFromIntents(IReadOnlyList<IntentSnapshot> intents)
    {
        var parts = intents.Where(IsAttack).Select(i => Hits(i.PerHit, i.Repeats)).ToList();
        return parts.Count == 0 ? "—" : string.Join("+", parts);
    }

    public static string Outcome(LethalIndex.Result r, bool hasAttacks)
    {
        if (r.LethalAt is int n) return $"→ {Ordinal(n)} coup {Lethal}";
        if (!hasAttacks) return "→ pas d'attaque";
        if (r.FullyBlocked) return "→ tout bloqué";
        return $"→ {r.HpAfter} PV restants";
    }

    /// <summary>Niveau 2 : hypothèse « si l'ennemi est affaibli ». null = rien à dire.</summary>
    public static string? WeakVariant(AttackForecast a) =>
        a.PerHitIfWeak is int w ? $"si Weak {Hits(w, a.Repeats)} ({w * a.Repeats})"
        : a.AlreadyWeak ? "déjà affaibli" : null;

    /// <summary>Niveau 2 : hypothèse « si je suis vulnérable ». null = rien à dire.</summary>
    public static string? VulnVariant(AttackForecast a) =>
        a.PerHitIfVulnerable is int v ? $"si Vuln {Hits(v, a.Repeats)} ({v * a.Repeats})"
        : a.AlreadyVulnerable ? "déjà vulnérable" : null;

    /// <summary>Niveau 2 : label du jeu (oracle), jamais une donnée de calcul.</summary>
    public static string? Oracle(IReadOnlyList<IntentSnapshot> intents)
    {
        var labels = intents.Select(i => i.Label).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        return labels.Count == 0 ? null : "jeu : " + string.Join(" · ", labels);
    }

    public static string Turn(TurnForecast f) =>
        f.LethalHitIndex is int n
            ? $"Tour : {f.TotalIncoming} → {Lethal} au {Ordinal(n)} coup"
              + (string.IsNullOrEmpty(f.LethalEnemyName) ? "" : $" ({f.LethalEnemyName})")
            : $"Tour : {f.TotalIncoming} → {f.HpAfter} PV restants";

    public static string Threat(PlayerThreat p) =>
        p.LethalHitIndex is int n
            ? $"{p.Incoming} → {Lethal} au {Ordinal(n)} coup"
            : $"{p.Incoming} → {p.HpAfter} PV restants";

    public static string PriorityPlayer(PriorityAlert a) =>
        a.IsRemote ? $"({a.PlayerName} · distant)" : $"({a.PlayerName})";

    public static bool IsHpCritical(CombatSnapshot s, int percent) =>
        s.MaxHp > 0 && s.Hp * 100 <= s.MaxHp * percent;
}
