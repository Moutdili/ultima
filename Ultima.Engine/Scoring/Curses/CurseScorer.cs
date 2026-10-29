using System.Globalization;
using Ultima.Engine.Contracts;
using Ultima.Engine.Scoring.Cards;
using Ultima.Engine.Scoring.Deck;
using Ultima.Engine.Scoring.Keywords;
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
///   <item><b>Statut</b> (Burn, Sloth, Void…) : pénalité **tirée de l'effet décrit** par
///         <c>data/catalog/status_effects.json</c> (docs/35, section statuts). Les statuts sont
///         temporaires et quasi absents du corpus (n = 0 à 3) : la mesure ne les classe pas,
///         mais leur texte si — « subissez 5 dégâts » n'est pas « injouable, rien d'autre ».
///         Barème : sévérité ≥ 70 → <see cref="StatusSeverePenalty"/> (−12), 35..69 →
///         <see cref="StatusPenalty"/> (−6), 1..34 → <see cref="StatusBenignPenalty"/> (−3),
///         famille <c>unclassified</c> → −6 <b>explicite</b> (jamais 0).</item>
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

    /// <summary>
    /// Pénalité d'un statut modéré (famille de sévérité 35..69) — et forfait <b>explicite</b>
    /// des statuts non classables. La bande est fixée par l'effet décrit, pas par une mesure
    /// absente.
    /// </summary>
    public const int StatusPenalty = -6;

    /// <summary>Pénalité d'un statut sévère (sévérité ≥ 70 : limite de cartes, dégâts, énergie…).</summary>
    public const int StatusSeverePenalty = -12;

    /// <summary>Pénalité d'un statut bénin (sévérité 1..34 : carte qui s'épuise d'elle-même…).</summary>
    public const int StatusBenignPenalty = -3;

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
    private readonly CardEffects _effects;
    private readonly KeywordCatalog _keywords;
    private readonly StatusCatalog _statuses;

    public CurseScorer(CurseCatalog? catalog = null, CardCatalog? cards = null,
        CardEffects? effects = null, KeywordCatalog? keywords = null,
        StatusCatalog? statuses = null)
    {
        _catalog = catalog ?? CurseCatalog.Load();
        _cards = cards ?? CardCatalog.Default;
        _effects = effects ?? CardEffects.Default;
        _keywords = keywords ?? KeywordCatalog.Default;
        _statuses = statuses ?? StatusCatalog.Default;
    }

    /// <summary>Catalogue des mots-clés utilisé (Exhaust, Ethereal, Retain, Eternal…).</summary>
    public KeywordCatalog Keywords => _keywords;

    /// <summary>Catalogue des statuts classés par effet décrit (17 entrées, docs/35).</summary>
    public StatusCatalog Statuses => _statuses;

    /// <summary>
    /// Modulation par les mots-clés : une carte <c>Retain</c> + <c>Unplayable</c> reste en main
    /// chaque tour (pire qu'une carte qui disparaît), <c>Ethereal</c>/<c>Exhaust</c> est
    /// transitoire (moins grave), <c>Eternal</c> ne peut pas être retirée du deck (plus grave).
    /// Les facteurs sont documentés ici, pas cachés dans un coefficient anonyme.
    /// </summary>
    public const double RetainUnplayableFactor = 1.4;
    public const double TransientFactor = 0.6;
    public const double EternalFactor = 1.2;

    private CurseVerdict Modulate(CurseVerdict verdict, string cardId)
    {
        if (verdict.Kind == CurseKind.None) return verdict;

        var flags = _keywords.Flags(_effects.Get(cardId)?.Keywords);
        if (!flags.Any) return verdict;

        var factor = 1.0;
        var notes = new List<string>();
        if (flags.Retained && flags.Unplayable)
        {
            factor *= RetainUnplayableFactor;
            notes.Add($"Retain+Unplayable : carte morte qui reste en main (×{Fr(RetainUnplayableFactor)})");
        }
        else if (flags.Transient)
        {
            factor *= TransientFactor;
            notes.Add($"Ethereal/Exhaust : transitoire (×{Fr(TransientFactor)})");
        }

        if (flags.Unremovable)
        {
            factor *= EternalFactor;
            notes.Add($"Eternal : non retirable du deck (×{Fr(EternalFactor)})");
        }

        if (notes.Count == 0) return verdict;

        var points = Math.Clamp(ScoreMath.Round(verdict.Penalty * factor), MinPenalty, -1);
        return verdict with
        {
            Penalty = points,
            Source = $"{verdict.Source} · {string.Join(" ; ", notes)}",
        };
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

        var verdict = kind switch
        {
            CurseKind.Status => Status(cardId, impact),
            CurseKind.Curse => Curse(cardId, impact),
            _ => new CurseVerdict(cardId, CurseKind.None, 0, string.Empty, string.Empty, null),
        };

        return Modulate(verdict, cardId);
    }

    /// <summary>
    /// Statut : la pénalité vient de la <b>famille d'effet décrite</b> dans
    /// <c>status_effects.json</c>, jamais de la mesure (les statuts sont temporaires, n = 0 à 3).
    /// Une entrée absente ou <c>unclassified</c> garde le forfait modéré <see cref="StatusPenalty"/>,
    /// avec un libellé qui dit explicitement « non classé » — jamais 0.
    /// </summary>
    private CurseVerdict Status(string cardId, CurseImpact? impact)
    {
        var effect = _statuses.Get(cardId);
        var name = Ids.Bare(cardId);

        if (effect is null || !effect.IsClassified || effect.Severity is null)
        {
            var why = effect is null
                ? $"{StatusCatalog.FileName} sans entrée pour {name}"
                : $"famille « {effect.Family}/{effect.EffectFamily} » sans sévérité décrite";
            return new CurseVerdict(cardId, CurseKind.Status, StatusPenalty, StatusUnclassifiedLabel,
                $"{why} ; forfait modéré −{Math.Abs(StatusPenalty)} explicite (statut temporaire, " +
                "aucune mesure de corpus) — jamais 0", impact);
        }

        int severity = effect.Severity.Value;
        int points = severity >= 70 ? StatusSeverePenalty
            : severity >= 35 ? StatusPenalty
            : StatusBenignPenalty;
        var label = severity >= 70 ? StatusSevereLabel
            : severity >= 35 ? StatusLabel
            : StatusBenignLabel;

        var text = string.IsNullOrWhiteSpace(effect.DescriptionFr)
            ? "texte FR absent — classé par les données de la carte"
            : $"« {Shorten(effect.DescriptionFr!, 140)} »";
        var value = effect.Value is int v ? $", valeur {v}" : string.Empty;

        return new CurseVerdict(cardId, CurseKind.Status, points, label,
            $"{StatusCatalog.FileName} — {text} ; famille {effect.Family} " +
            $"({effect.EffectFamily}){value}, sévérité {severity}/100 → {BandFr(effect.Band)} " +
            $"{points} ; {effect.Source}", impact);
    }

    private static string Shorten(string text, int max) =>
        text.Length <= max ? text : text[..(max - 1)].TrimEnd() + "…";

    private static string BandFr(string band) => band switch
    {
        StatusCatalog.SevereBand => "sévère",
        StatusCatalog.ModerateBand => "modéré",
        StatusCatalog.BenignBand => "bénin",
        _ => "non classé",
    };

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

        // Effet décrit (limite de cartes, perte d'énergie, pioche réduite…) : il prime sur
        // l'absence de mesure. Normality mesure +1,9 pp parce que l'événement offre une
        // contrepartie, mais « pas plus de 3 cartes par tour » reste dévastateur.
        if (!impact.Reliable && impact.EffectSeverity > 0)
        {
            int effectPoints = Math.Clamp(ScoreMath.Round(impact.EffectSeverity * ToxicityScale), 1, -MinPenalty);
            return new CurseVerdict(cardId, CurseKind.Curse, -effectPoints, EffectLabel,
                $"{Ids.Bare(cardId)} : effet « {impact.EffectFamily} » (sévérité {impact.EffectSeverity}/100, " +
                $"texte FR) — mesure {(impact.N == 0 ? "absente" : $"n = {N(impact.N)} insuffisant")} ; " +
                $"−round({impact.EffectSeverity} × {Fr(ToxicityScale)}) = −{effectPoints}", impact);
        }

        if (!impact.Reliable)
            return new CurseVerdict(cardId, CurseKind.Curse, UnmeasuredPenalty, UnmeasuredLabel,
                $"{Ids.Bare(cardId)} : n = {N(impact.N)} run(s) < {N(_catalog.MinSample)} : non classable — " +
                $"pénalité prudente −{Math.Abs(UnmeasuredPenalty)} (médiane des malédictions classées)", impact);

        // −round(toxicité × 0,26), borné à [1, 30] en magnitude : une malédiction mesurée n'est
        // jamais gratuite, et aucune ne dépasse un quart de l'échelle.
        int points = Math.Clamp(ScoreMath.Round(impact.Toxicity * ToxicityScale), 1, -MinPenalty);

        var effectNote = impact.EffectSeverity > (impact.MeasuredToxicity ?? 0)
            ? $" ; effet « {impact.EffectFamily} » {impact.EffectSeverity}/100 (texte FR) prime sur la mesure"
            : string.Empty;
        return new CurseVerdict(cardId, CurseKind.Curse, -points, MeasuredLabel,
            $"{CurseCatalog.FileName} — {N(_catalog.SampleRuns)} runs entrées en acte 3 " +
            $"(base {Fr(_catalog.BaselineWinRate)} %) : {impact.Id} " +
            $"{Signed(impact.DeltaPp)} pp, toxicité {impact.Toxicity}/100 sur {N(impact.N)} runs" +
            $"{effectNote} ; −round({impact.Toxicity} × {Fr(ToxicityScale)}) = −{points}",
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

    /// <summary>Pénalité fondée sur l'effet décrit, quand la mesure manque ou est masquée par un bundle.</summary>
    public const string EffectLabel = "Malédiction (effet décrit)";

    /// <summary>Libellé du terme pour une malédiction présente au corpus mais non classable.</summary>
    public const string UnmeasuredLabel = "Malédiction (impact non classable)";

    /// <summary>Libellé du terme pour une malédiction absente de toute mesure.</summary>
    public const string NoDataLabel = "Malédiction (aucune donnée mesurée)";

    /// <summary>Libellé du terme pour un statut modéré (sévérité 35..69).</summary>
    public const string StatusLabel = "Statut (effet modéré)";

    /// <summary>Libellé du terme pour un statut sévère (sévérité ≥ 70).</summary>
    public const string StatusSevereLabel = "Statut (effet sévère)";

    /// <summary>Libellé du terme pour un statut bénin (sévérité 1..34).</summary>
    public const string StatusBenignLabel = "Statut (effet bénin)";

    /// <summary>Libellé du terme pour un statut non classable : forfait modéré explicite.</summary>
    public const string StatusUnclassifiedLabel = "Statut (effet non classé)";

    private static string N(int value) => value.ToString("N0", CultureInfo.InvariantCulture).Replace(',', ' ');

    private static string Fr(double value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture).Replace('.', ',');

    private static string Signed(double value) =>
        (value > 0 ? "+" : value < 0 ? "−" : string.Empty) + Fr(Math.Abs(value));
}
