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

    /// <summary>En-tête, partie titre (Kreon Bold) : « T3 · ».</summary>
    public static string HeaderTurn(CombatSnapshot s) => $"T{s.Turn} ·";

    /// <summary>En-tête, chiffres PV/block (Source Code Pro) : « 62/80 PV · 12 block ».</summary>
    public static string HeaderVitals(CombatSnapshot s) => $"{s.Hp}/{s.MaxHp} PV · {s.Block} block";

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

    /// <summary>Issue niveau 1, forme courte du schéma docs/17 §4 (« 16x3 → 3e LÉTAL », « → bloqué ») :
    /// en Source Code Pro 14 la colonne tient sur 94 px. Pas d'attaque → vide (la colonne NxM dit « — »).</summary>
    public static string Outcome(LethalIndex.Result r, bool hasAttacks)
    {
        if (r.LethalAt is int n) return $"→ {Ordinal(n)} {Lethal}";
        if (!hasAttacks) return "";
        if (r.FullyBlocked) return "→ bloqué";
        return $"→ {r.HpAfter} PV";
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

    /// <summary>Total du tour (Source Code Pro) : « Tour : 54 → 5e coup LÉTAL » ou « Tour : 54 → 8 PV restants ».</summary>
    public static string Turn(TurnForecast f) =>
        f.LethalHitIndex is int n
            ? $"Tour : {f.TotalIncoming} → {Ordinal(n)} coup {Lethal}"
            : $"Tour : {f.TotalIncoming} → {f.HpAfter} PV restants";

    /// <summary>Ennemi qui porte le coup létal du tour (ligne Kreon sous le total) ; null si survie.</summary>
    public static string? TurnBy(TurnForecast f) =>
        f.LethalHitIndex != null && !string.IsNullOrEmpty(f.LethalEnemyName) ? $"par {f.LethalEnemyName}" : null;

    public static string Threat(PlayerThreat p) =>
        p.LethalHitIndex is int n
            ? $"{p.Incoming} → {Ordinal(n)} {Lethal}"
            : $"{p.Incoming} → {p.HpAfter} PV";

    public static string PriorityPlayer(PriorityAlert a) =>
        a.IsRemote ? $"({a.PlayerName} · distant)" : $"({a.PlayerName})";

    public static bool IsHpCritical(CombatSnapshot s, int percent) =>
        s.MaxHp > 0 && s.Hp * 100 <= s.MaxHp * percent;
}
