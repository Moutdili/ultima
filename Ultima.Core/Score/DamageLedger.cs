namespace Ultima.Core.Score;

/// <summary>
/// Une entrée de dégâts observée sur un ennemi (un DamageReceivedEntry du jeu, déjà traduit).
/// <see cref="Unblocked"/> inclut <see cref="Overkill"/> (sémantique de DamageResult du jeu).
/// <see cref="Round"/> vaut -1 si le numéro de round n'a pas pu être lu.
/// </summary>
public sealed record DamageRow(
    int Round,
    string SourceClass,
    string SourceName,
    bool Attributed,
    string Reason,
    ulong DealerNetId,
    string DealerName,
    int Unblocked,
    int Blocked,
    int Overkill,
    bool Killed);

/// <summary>Agrégat d'un combat pour une clé (joueur, source, attribution, raison).</summary>
public sealed record DamageAggregate(
    ulong DealerNetId,
    string DealerName,
    string SourceClass,
    string SourceName,
    bool Attributed,
    string Reason,
    int Hits,
    int Unblocked,
    int Blocked,
    int Overkill,
    int Kills);

/// <summary>
/// Raisons de non-attribution. On liste, on n'invente pas : l'entrée du jeu ne dit pas
/// si un coup sans carte vient d'un pouvoir, d'une relique, d'un orbe ou d'une potion.
/// </summary>
public static class UnattributedReason
{
    /// <summary>Dealer joueur mais CardSource null : pouvoir, relique, orbe, potion… (indiscernables).</summary>
    public const string NoCardSource = "no_card_source";

    /// <summary>Dealer null : dégâts sans créature source (poison, doom, …).</summary>
    public const string NoDealer = "no_dealer";

    /// <summary>Dealer = familier d'un joueur (PetOwner) : joueur connu, carte non attribuée.</summary>
    public const string PetDealer = "pet_dealer";

    /// <summary>Dealer = créature non joueur (ennemi sur ennemi, auto-dégâts, épines…).</summary>
    public const string NonPlayerDealer = "non_player_dealer";
}

/// <summary>
/// Grand livre des dégâts d'un combat. Pur (aucune dépendance au jeu), testé.
/// Les totaux observés sont cumulés à l'enregistrement, indépendamment de l'agrégation,
/// pour que <see cref="Reconciles"/> soit une vraie vérification.
/// </summary>
public sealed class DamageLedger
{
    private readonly List<DamageRow> _rows = new();

    /// <summary>Index du combat courant (1, 2, …) ; 0 avant le premier <see cref="BeginCombat"/>.</summary>
    public int CombatIndex { get; private set; }

    public IReadOnlyList<DamageRow> Rows => _rows;

    public int ObservedHits { get; private set; }
    public int ObservedUnblocked { get; private set; }
    public int ObservedBlocked { get; private set; }
    public int ObservedOverkill { get; private set; }
    public int ObservedKills { get; private set; }

    /// <summary>Continue la numérotation après un index déjà présent (ex. CSV existant).</summary>
    public void SeedCombatIndex(int lastIndex)
    {
        if (lastIndex > CombatIndex) CombatIndex = lastIndex;
    }

    /// <summary>Nouveau combat : index +1 et remise à zéro des lignes et des totaux.</summary>
    public void BeginCombat()
    {
        CombatIndex++;
        _rows.Clear();
        ObservedHits = ObservedUnblocked = ObservedBlocked = ObservedOverkill = ObservedKills = 0;
    }

    public void Record(DamageRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        _rows.Add(row);
        ObservedHits++;
        ObservedUnblocked += row.Unblocked;
        ObservedBlocked += row.Blocked;
        ObservedOverkill += row.Overkill;
        if (row.Killed) ObservedKills++;
    }

    /// <summary>
    /// Agrégats du combat : attribués d'abord, puis non attribués ; ordre de première apparition
    /// dans chaque groupe. Clé = (NetId, nom, classe, titre, attribué, raison) : pas de fusion d'homonymes.
    /// </summary>
    public IReadOnlyList<DamageAggregate> Aggregates()
    {
        var order = new List<(ulong, string, string, string, bool, string)>();
        var acc = new Dictionary<(ulong, string, string, string, bool, string), int[]>();
        foreach (var r in _rows)
        {
            var key = (r.DealerNetId, r.DealerName, r.SourceClass, r.SourceName, r.Attributed, r.Reason);
            if (!acc.TryGetValue(key, out var a))
            {
                a = new int[5];
                acc[key] = a;
                order.Add(key);
            }
            a[0]++;
            a[1] += r.Unblocked;
            a[2] += r.Blocked;
            a[3] += r.Overkill;
            if (r.Killed) a[4]++;
        }

        var result = new List<DamageAggregate>(order.Count);
        foreach (var attributed in new[] { true, false })
        {
            foreach (var k in order)
            {
                if (k.Item5 != attributed) continue;
                var a = acc[k];
                result.Add(new DamageAggregate(k.Item1, k.Item2, k.Item3, k.Item4, k.Item5, k.Item6,
                    a[0], a[1], a[2], a[3], a[4]));
            }
        }
        return result;
    }

    public IReadOnlyList<DamageAggregate> AttributedAggregates() => Aggregates().Where(a => a.Attributed).ToList();

    public IReadOnlyList<DamageAggregate> UnattributedAggregates() => Aggregates().Where(a => !a.Attributed).ToList();

    public int AttributedUnblocked => AttributedAggregates().Sum(a => a.Unblocked);

    public int UnattributedUnblocked => UnattributedAggregates().Sum(a => a.Unblocked);

    /// <summary>Somme non attribuée par raison (pour le log et l'UI).</summary>
    public IReadOnlyDictionary<string, int> UnattributedByReason()
    {
        var d = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var a in UnattributedAggregates())
            d[a.Reason] = d.TryGetValue(a.Reason, out var v) ? v + a.Unblocked : a.Unblocked;
        return d;
    }

    /// <summary>
    /// Invariant : attribués + non attribués == observé, sur coups, dégâts non bloqués, bloqués,
    /// overkill et kills.
    /// </summary>
    public bool Reconciles
    {
        get
        {
            var all = Aggregates();
            return all.Sum(a => a.Hits) == ObservedHits
                && all.Sum(a => a.Unblocked) == ObservedUnblocked
                && all.Sum(a => a.Blocked) == ObservedBlocked
                && all.Sum(a => a.Overkill) == ObservedOverkill
                && all.Sum(a => a.Kills) == ObservedKills
                && AttributedUnblocked + UnattributedUnblocked == ObservedUnblocked;
        }
    }

    /// <summary>
    /// Lit le numéro de round dans <c>CombatHistoryEntry.HumanReadableString</c>
    /// (format du jeu : "Rd {n} ({side} turn): …"). -1 si illisible.
    /// </summary>
    public static int ParseRound(string? humanReadable)
    {
        if (string.IsNullOrEmpty(humanReadable) || !humanReadable.StartsWith("Rd ", StringComparison.Ordinal))
            return -1;
        var i = 3;
        var start = i;
        while (i < humanReadable.Length && char.IsAsciiDigit(humanReadable[i])) i++;
        return i > start && int.TryParse(humanReadable.AsSpan(start, i - start), out var n) ? n : -1;
    }
}
