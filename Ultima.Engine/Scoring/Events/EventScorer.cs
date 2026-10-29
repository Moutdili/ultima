using Ultima.Engine.Contracts;
using Ultima.Engine.Scoring.Model;

namespace Ultima.Engine.Scoring.Events;

/// <summary>État du joueur au moment du choix (les PV changent le prix d'un choix risqué).</summary>
public sealed record EventContext(int Hp, int MaxHp, int Gold, int Ascension = 0)
{
    public double HpPercent => MaxHp > 0 ? 100.0 * Hp / MaxHp : 50.0;
}

/// <summary>Une option classée, avec ses termes chiffrés et une phrase lisible.</summary>
public sealed record EventChoice(
    string Option,
    string Title,
    int Score,
    bool Measured,
    IReadOnlyList<ScoreTerm> Terms,
    string Explanation);

/// <summary>
/// Classe les options d'une salle « ? » (événement) par valeur nette mesurée :
/// ce que l'option rapporte (or, relique, carte, retrait) moins ce qu'elle coûte
/// (PV, malédiction). Les profils viennent de 80 000 runs solo (321 076 choix).
///
/// <para>
/// Le win rate observé n'entre <b>pas</b> dans le score : les joueurs qui prennent les options
/// risquées sont déjà plus forts (biais de sélection). On ne garde que les deltas mesurés dans la
/// salle (PV, or, reliques, cartes) et on valorise les PV selon l'état du joueur.
/// </para>
/// </summary>
public sealed class EventScorer
{
    /// <summary>Valeur d'un point d'or (12 points de G pour 100 or ≈ 0,12 ; on reste prudent à 0,06).</summary>
    public const double GoldValue = 0.06;

    /// <summary>Valeur d'une relique obtenue (moyenne observée ~8 points de G).</summary>
    public const double RelicValue = 8.0;

    /// <summary>Valeur d'une carte ajoutée (une pioche moyenne vaut ~3 points).</summary>
    public const double CardValue = 3.0;

    /// <summary>Valeur d'un retrait de carte (75 or ≈ 6 points de G).</summary>
    public const double RemovalValue = 6.0;

    /// <summary>Valeur d'un PV : 0,35 point, doublée quand on est bas en vie.</summary>
    public const double HpValue = 0.35;

    /// <summary>Valeur d'un PV soigné (moitié d'un PV perdu : on ne stocke pas la vie).</summary>
    public const double HealValue = 0.2;

    /// <summary>Coût d'une malédiction ajoutée (ordre de grandeur mesuré : −12 à −30 points).</summary>
    public const double CurseCost = 20.0;

    /// <summary>Plafond de cartes valorisées (certaines options en donnent des dizaines).</summary>
    public const double MaxCardsValued = 3.0;

    private readonly EventCatalog _catalog;

    public EventScorer(EventCatalog? catalog = null) => _catalog = catalog ?? EventCatalog.Default;

    public EventCatalog Catalog => _catalog;

    /// <summary>Classe les options disponibles, de la meilleure à la pire.</summary>
    public IReadOnlyList<EventChoice> Rank(string eventId, IEnumerable<string> options, EventContext ctx) =>
        options.Select(o => Score(eventId, o, ctx))
            .OrderByDescending(c => c.Score)
            .ThenByDescending(c => c.Measured)
            .ThenBy(c => c.Title, StringComparer.Ordinal)
            .ToArray();

    /// <summary>Score d'une option ; une option sans profil vaut 50 et le dit.</summary>
    public EventChoice Score(string eventId, string option, EventContext ctx)
    {
        var profile = _catalog.Get(eventId, option);
        var title = _catalog.Title(eventId, option) ?? option;

        if (profile is null)
        {
            return new EventChoice(option, title, 50, false,
                new[] { new ScoreTerm("Option inconnue", 0, $"{EventCatalog.ProfilesFile} : aucune mesure pour {eventId}|{option} — score neutre 50, non recommandé") },
                $"{title} : aucune donnée mesurée, score neutre 50 (à toi de juger)");
        }

        var terms = new List<ScoreTerm>(5);
        var details = new List<string>(5);
        double gains = 0, costs = 0;

        if (Math.Abs(profile.GoldDelta) >= 1)
        {
            var points = profile.GoldDelta * GoldValue;
            gains += Math.Max(0, points);
            costs += Math.Max(0, -points);
            terms.Add(new ScoreTerm("Or", ScoreMath.Round(points),
                $"{Signed(profile.GoldDelta)} or en moyenne sur {N(profile.N)} choix × {Fr(GoldValue)}"));
            details.Add($"{Signed(profile.GoldDelta)} or");
        }

        if (profile.RelicRate > 0.01)
        {
            var rate = Math.Min(1.0, profile.RelicRate);
            var points = rate * RelicValue;
            gains += points;
            terms.Add(new ScoreTerm("Relique", ScoreMath.Round(points),
                $"{Fr(100 * rate)} % des choix donnent une relique ({N(profile.N)} choix) × {Fr(RelicValue)}"));
            details.Add($"relique {Fr(100 * rate)} %");
        }

        if (profile.CardGainRate > 0.01)
        {
            var rate = Math.Min(MaxCardsValued, profile.CardGainRate);
            var points = rate * CardValue;
            gains += points;
            terms.Add(new ScoreTerm("Cartes", ScoreMath.Round(points),
                $"{Fr(profile.CardGainRate)} carte(s) par choix (plafonné à {Fr(MaxCardsValued)}) × {Fr(CardValue)}"));
            details.Add($"{Fr(profile.CardGainRate)} carte(s)");
        }

        if (profile.CardRemoveRate > 0.01)
        {
            var rate = Math.Min(1.0, profile.CardRemoveRate);
            var points = rate * RemovalValue;
            gains += points;
            terms.Add(new ScoreTerm("Retrait", ScoreMath.Round(points),
                $"{Fr(100 * rate)} % des choix retirent une carte × {Fr(RemovalValue)}"));
            details.Add($"retrait {Fr(100 * rate)} %");
        }

        // Les PV : le coût double quand on est sous 50 % de vie.
        var hpWeight = HpValue * (1 + Math.Max(0, (50 - ctx.HpPercent) / 50));
        if (profile.HpDelta < -0.5)
        {
            var points = -profile.HpDelta * hpWeight;
            costs += points;
            terms.Add(new ScoreTerm("Dégâts", -ScoreMath.Round(points),
                $"{Fr(-profile.HpDelta)} PV perdus en moyenne × {Fr(hpWeight)} (à {Fr(ctx.HpPercent)} % de vie)"));
            details.Add($"−{Fr(-profile.HpDelta)} PV");
        }
        else if (profile.HpDelta > 0.5)
        {
            var points = profile.HpDelta * HealValue;
            gains += points;
            terms.Add(new ScoreTerm("Soin", ScoreMath.Round(points),
                $"{Fr(profile.HpDelta)} PV gagnés en moyenne × {Fr(HealValue)}"));
            details.Add($"+{Fr(profile.HpDelta)} PV");
        }

        if (profile.CurseRate > 0.005)
        {
            var points = profile.CurseRate * CurseCost;
            costs += points;
            terms.Add(new ScoreTerm("Malédiction", -ScoreMath.Round(points),
                $"{Fr(100 * profile.CurseRate)} % des choix ajoutent une malédiction × {Fr(CurseCost)}"));
            details.Add($"malédiction {Fr(100 * profile.CurseRate)} %");
        }

        var score = (int)Math.Clamp(ScoreMath.Round(50 + gains - costs), 0, 100);
        var explanation = $"{title} : {string.Join(", ", details)} → {score}/100";
        return new EventChoice(option, title, score, true, terms, explanation);
    }

    private static string Fr(double value) => value.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));

    private static string Signed(double value) => (value >= 0 ? "+" : "") + Fr(value);

    private static string N(int value) => value.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
}
