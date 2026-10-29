using System.Globalization;
using Ultima.Engine.Contracts;
using Ultima.Engine.Scoring.Deck;
using Ultima.Engine.Scoring.Model;

namespace Ultima.Engine.Scoring.Curses;

/// <summary>Nature d'une carte du point de vue de l'impact mesuré.</summary>
public enum CurseKind
{
    /// <summary>Ni malédiction ni statut : aucun terme, aucune pénalité.</summary>
    None,

    /// <summary>Malédiction permanente (Poor Sleep, Injury, Clumsy…).</summary>
    Curse,

    /// <summary>Statut de combat (Burn, Beckon, Sloth…) : temporaire, hors du deck final.</summary>
    Status,
}

/// <summary>
/// Verdict de pénalité pour une carte : points négatifs, libellé lisible et source vérifiable.
/// <see cref="Measured"/> est null quand la carte n'a aucune mesure exploitable (statut, n trop
/// faible, carte absente du corpus) — c'est précisément ce que dit <see cref="Source"/>.
/// </summary>
public sealed record CurseVerdict(
    string CardId,
    CurseKind Kind,
    int Penalty,
    string Label,
    string Source,
    CurseImpact? Measured)
{
    public bool IsPenalised => Penalty != 0;

    /// <summary>Terme de score prêt à poser (jamais émis pour <see cref="CurseKind.None"/>).</summary>
    public ScoreTerm? Term => Kind == CurseKind.None
        ? null
        : new ScoreTerm(Label, Penalty, Source);
}

/// <summary>
/// Pénalité d'une malédiction **tirée de la mesure**, et non d'un forfait.
///
/// Source : <c>data/catalog/curse_impact.json</c> — 63 634 runs entrées en acte 3 (étage ≥ 33),
/// taux de victoire de base 78,6 %, écart mesuré pour chaque carte acquise avant l'acte 3
/// (docs/35-curses-statuts.md). Barème :
/// <list type="bullet">
///   <item><b>Malédiction mesurée</b> (n ≥ 300) : <c>−round(toxicité × 0,26)</c>. La toxicité est
///         l'écart de victoire ramené sur 0..100 (50 = neutre, 100 = −10 pp). Le facteur 0,26
///         ancre les trois cas mesurés : POOR_SLEEP (tox 97) → −25, INJURY (65) → −17,
///         CLUMSY (53) → −14. Une malédiction garde toujours au moins 1 point de pénalité :
///         une carte morte coûte une pioche même quand le corpus ne la voit pas perdre.</item>
///   <item><b>Malédiction non classable</b> (n &lt; 300, ex. Ascender's Bane n = 6, Enthralled
///         n = 1) : <see cref="UnmeasuredPenalty"/> (−12), la pénalité médiane des malédictions
///         classées. Le corpus ne permet pas de la classer, pas de prétendre qu'elle est bénigne.</item>
///   <item><b>Carte absente du corpus</b> (ex. Bad Luck, jamais acquise en acte 3) : même −12 avec
///         un libellé explicite « aucune donnée mesurée » — jamais 0, jamais un chiffre déguisé
///         en mesure.</item>
///   <item><b>Statut</b> (Burn, Beckon, Sloth…) : <see cref="StatusPenalty"/> (−6), forfait
///         <b>faible et documenté</b>. Les statuts sont temporaires et quasi absents du corpus
///         (n = 0 à 3) : la data ne les classe pas, et leurs toxicités (100 pour Sloth sur 1 run)
///         sont du bruit. Ils coûtent une carte en main, pas un emplacement de deck.</item>
/// </list>
/// </summary>
public sealed class CurseScorer
{
    /// <summary>
    /// Échelle toxicité → points de score. 0,26 = 26 points pour une toxicité de 100 : la pire
    /// malédiction mesurée (POOR_SLEEP) vaut −25, un quart de l'échelle 0..100 du score de carte.
    /// Un forfait plus fort écraserait les cartes réellement jouables offertes à côté.
    /// </summary>
    public const double ToxicityScale = 0.26;

    /// <summary>Pénalité d'un statut : faible et forfaitaire, la data ne permettant aucun classement.</summary>
    public const int StatusPenalty = -6;

    /// <summary>
    /// Pénalité d'une malédiction sans mesure exploitable (n &lt; 300 ou absente du corpus) :
    /// −12, la pénalité médiane des malédictions classées (arrondie). Jamais 0 : un zéro
    /// affirmerait « inoffensive », ce que rien ne démontre.
    /// </summary>
    public const int UnmeasuredPenalty = -12;

    /// <summary>Borne basse de sécurité : aucune pénalité de malédiction ne dépasse −30.</summary>
    public const int MinPenalty = -30;

    private static readonly Lazy<CurseScorer> LazyDefault =
        new(() => new CurseScorer(), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Scorer adossé au catalogue du dépôt (résolu comme le reste du moteur).</summary>
    public static CurseScorer Default => LazyDefault.Value;

    private readonly CurseCatalog _catalog;
    private readonly CardCatalog _cards;

    public CurseScorer(CurseCatalog? catalog = null, CardCatalog? cards = null)
    {
        _catalog = catalog ?? CurseCatalog.Load();
        _cards = cards ?? CardCatalog.Default;
    }

    /// <summary>Catalogue utilisé (utile pour afficher provenance et seuils dans l'UI).</summary>
    public CurseCatalog Catalog => _catalog;

    /// <summary>
    /// Pénalité d'une carte, sans contexte. 0 seulement si la carte n'est ni malédiction ni
    /// statut — jamais pour une malédiction, même inconnue du corpus.
    /// </summary>
    public int Penalty(string cardId) => Classify(cardId).Penalty;

    /// <summary>Terme de score d'une carte, ou null si ce n'est ni une malédiction ni un statut.</summary>
    public ScoreTerm? Term(string cardId, IMetaTable? meta = null) => Classify(cardId, meta).Term;

    /// <summary>
    /// Verdict complet : nature, points, libellé et source. La nature vient de la donnée, jamais
    /// du nom de l'id : entrée du corpus d'impact, puis type/rareté de la méta, puis catalogue
    /// du jeu (<c>data/catalog/cards.json</c>).
    /// </summary>
    public CurseVerdict Classify(string cardId, IMetaTable? meta = null)
    {
        var impact = _catalog.Get(cardId);
        var kind = KindOf(cardId, meta, impact);

        return kind switch
        {
            CurseKind.Status => new CurseVerdict(cardId, kind, StatusPenalty, StatusLabel, StatusSource(impact), impact),
            CurseKind.Curse => Curse(cardId, impact),
            _ => new CurseVerdict(cardId, CurseKind.None, 0, string.Empty, string.Empty, null),
        };
    }

    private CurseVerdict Curse(string cardId, CurseImpact? impact)
    {
        // Corpus absent : on le dit, et on garde la pénalité prudente (jamais 0).
        if (!_catalog.HasData)
            return new CurseVerdict(cardId, CurseKind.Curse, UnmeasuredPenalty, NoDataLabel,
                $"aucune donnée mesurée — {_catalog.Problem ?? $"{CurseCatalog.FileName} vide"} ; " +
                $"pénalité prudente −{Math.Abs(UnmeasuredPenalty)} (médiane des malédictions classées)", null);

        if (impact is null)
            return new CurseVerdict(cardId, CurseKind.Curse, UnmeasuredPenalty, NoDataLabel,
                $"aucune donnée mesurée — {Ids.Bare(cardId)} est absente de {CurseCatalog.FileName} " +
                $"(jamais acquise dans les {N(_catalog.SampleRuns)} runs d'acte 3) ; " +
                $"pénalité prudente −{Math.Abs(UnmeasuredPenalty)} (médiane des malédictions classées)", null);

        if (!impact.Reliable)
            return new CurseVerdict(cardId, CurseKind.Curse, UnmeasuredPenalty, UnmeasuredLabel,
                $"n = {N(impact.N)} run(s) < {N(_catalog.MinSample)} : non classable — " +
                $"pénalité prudente −{Math.Abs(UnmeasuredPenalty)} (médiane des malédictions classées)", impact);

        // −round(toxicité × 0,26), borné à [1, 30] en magnitude : une malédiction mesurée n'est
        // jamais gratuite, et aucune ne dépasse un quart de l'échelle.
        int points = Math.Clamp(ScoreMath.Round(impact.Toxicity * ToxicityScale), 1, -MinPenalty);

        return new CurseVerdict(cardId, CurseKind.Curse, -points, MeasuredLabel,
            $"{CurseCatalog.FileName} — {N(_catalog.SampleRuns)} runs entrées en acte 3 " +
            $"(base {Fr(_catalog.BaselineWinRate)} %) : {impact.Id} " +
            $"{Signed(impact.DeltaPp)} pp, toxicité {impact.Toxicity}/100 sur {N(impact.N)} runs ; " +
            $"−round({impact.Toxicity} × {Fr(ToxicityScale)}) = −{points}",
            impact);
    }

    /// <summary>Nature de la carte : corpus, puis méta, puis catalogue du jeu.</summary>
    private CurseKind KindOf(string cardId, IMetaTable? meta, CurseImpact? impact)
    {
        if (impact is not null) return impact.IsStatus ? CurseKind.Status : CurseKind.Curse;

        var entry = meta?.Card(cardId);
        if (Contains(entry?.Type, "Curse") || Contains(entry?.Rarity, "Curse")) return CurseKind.Curse;
        if (Contains(entry?.Type, "Status")) return CurseKind.Status;

        var card = _cards.Get(cardId);
        if (card is not null)
            return card.IsCurse ? CurseKind.Curse
                : Contains(card.Type, "Status") ? CurseKind.Status
                : CurseKind.None;

        return CurseKind.None;
    }

    private static bool Contains(string? value, string needle) =>
        value != null && value.Contains(needle, StringComparison.OrdinalIgnoreCase);

    // --- Libellés et sources (tests et UI s'appuient dessus) ------------------------------------

    /// <summary>Libellé du terme pour une malédiction effectivement mesurée.</summary>
    public const string MeasuredLabel = "Malédiction (impact mesuré)";

    /// <summary>Libellé du terme pour une malédiction présente au corpus mais non classable.</summary>
    public const string UnmeasuredLabel = "Malédiction (impact non classable)";

    /// <summary>Libellé du terme pour une malédiction absente de toute mesure.</summary>
    public const string NoDataLabel = "Malédiction (aucune donnée mesurée)";

    /// <summary>Libellé du terme pour un statut de combat.</summary>
    public const string StatusLabel = "Statut (hors deck final)";

    private string StatusSource(CurseImpact? impact)
    {
        var n = impact is null ? "aucune entrée" : $"n = {N(impact.N)} run(s)";
        return $"{n} : la data ne classe pas les statuts (temporaires, absents du deck final) ; " +
               $"forfait faible −{Math.Abs(StatusPenalty)} — un statut coûte une carte en main, pas un emplacement";
    }

    private static string N(int value) => value.ToString("N0", CultureInfo.InvariantCulture).Replace(',', ' ');

    private static string Fr(double value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture).Replace('.', ',');

    private static string Signed(double value) =>
        (value > 0 ? "+" : value < 0 ? "−" : string.Empty) + Fr(Math.Abs(value));
}
