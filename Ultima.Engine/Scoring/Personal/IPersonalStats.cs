namespace Ultima.Engine.Scoring.Personal;

/// <summary>
/// Intervalle de confiance (bornes incluses, dans l'unité de la statistique : 0..1 pour un taux).
/// <see cref="Low"/> peut être négatif pour une différence de taux.
/// </summary>
public readonly record struct Interval(double Low, double High)
{
    public double Width => High - Low;

    public bool Contains(double value) => value >= Low && value <= High;

    public override string ToString() => $"[{Low:0.###} ; {High:0.###}]";
}

/// <summary>
/// Statistiques personnelles d'une carte (history + progress.save).
/// <para>
/// <b>Unités</b> : <see cref="Offered"/> et <see cref="Picked"/> comptent des <i>occasions</i>
/// (un étage = au plus une offre par carte), tandis que <see cref="WinsWhenPicked"/>,
/// <see cref="LossesWhenPicked"/>, <see cref="WinsWhenSkipped"/> et <see cref="LossesWhenSkipped"/>
/// comptent des <i>runs</i> (une run = au plus une unité, même si la carte y a été prise plusieurs fois).
/// C'est ce qui rend <see cref="RunsWithCard"/>, <see cref="RunsWithoutCard"/> et
/// <see cref="WinRateDelta"/> interprétables.
/// </para>
/// </summary>
public sealed record PersonalCardStats(
    string CardId,
    int Offered,
    int Picked,
    int WinsWhenPicked,
    int LossesWhenPicked,
    int WinsWhenSkipped,
    int LossesWhenSkipped)
{
    /// <summary>Runs où la carte a été prise au moins une fois.</summary>
    public int RunsWithCard => WinsWhenPicked + LossesWhenPicked;

    /// <summary>Runs où la carte a été proposée mais jamais prise (passée ou non choisie).</summary>
    public int RunsWithoutCard => WinsWhenSkipped + LossesWhenSkipped;

    /// <summary>Part des occasions où la carte a été prise (0..1).</summary>
    public double PickRate => Offered > 0 ? (double)Picked / Offered : 0;

    public double WinRateWith => RunsWithCard > 0 ? (double)WinsWhenPicked / RunsWithCard : 0;
    public double WinRateWithout => RunsWithoutCard > 0 ? (double)WinsWhenSkipped / RunsWithoutCard : 0;

    /// <summary>Écart de taux de victoire (avec − sans). Null si un côté a moins de 5 runs.</summary>
    public double? WinRateDelta =>
        RunsWithCard >= 5 && RunsWithoutCard >= 5 ? WinRateWith - WinRateWithout : null;

    /// <summary>Intervalle de Wilson à 95 % sur <see cref="WinRateWith"/>. Null si aucun run.</summary>
    public Interval? WinRateWithCi => RunsWithCard > 0 ? Wilson(WinsWhenPicked, RunsWithCard) : null;

    /// <summary>Intervalle de Wilson à 95 % sur <see cref="WinRateWithout"/>. Null si aucun run.</summary>
    public Interval? WinRateWithoutCi => RunsWithoutCard > 0 ? Wilson(WinsWhenSkipped, RunsWithoutCard) : null;

    /// <summary>Intervalle de Wilson à 95 % sur <see cref="PickRate"/>. Null si jamais proposée.</summary>
    public Interval? PickRateCi => Offered > 0 ? Wilson(Picked, Offered) : null;

    /// <summary>
    /// Intervalle de confiance à 95 % sur <see cref="WinRateDelta"/> (méthode de Newcombe :
    /// combinaison des deux intervalles de Wilson). Null sous le seuil de 5 runs par côté,
    /// comme <see cref="WinRateDelta"/>.
    /// </summary>
    public Interval? WinRateDeltaCi
    {
        get
        {
            if (RunsWithCard < 5 || RunsWithoutCard < 5) return null;

            var with = Wilson(WinsWhenPicked, RunsWithCard);
            var without = Wilson(WinsWhenSkipped, RunsWithoutCard);
            double delta = WinRateWith - WinRateWithout;

            double low = delta - Math.Sqrt(
                Math.Pow(WinRateWith - with.Low, 2) + Math.Pow(without.High - WinRateWithout, 2));
            double high = delta + Math.Sqrt(
                Math.Pow(with.High - WinRateWith, 2) + Math.Pow(WinRateWithout - without.Low, 2));

            return new Interval(Math.Max(-1, low), Math.Min(1, high));
        }
    }

    /// <summary>
    /// Intervalle de Wilson à <paramref name="z"/> écarts-types (1.96 par défaut, soit 95 %).
    /// <paramref name="n"/> ≤ 0 renvoie l'intervalle non informatif [0 ; 1] : un échantillon vide
    /// est un cas normal, pas une erreur.
    /// </summary>
    public static Interval Wilson(int successes, int n, double z = 1.96)
    {
        if (n <= 0) return new Interval(0, 1);

        if (successes < 0) successes = 0;
        if (successes > n) successes = n;

        double p = (double)successes / n;
        double z2 = z * z;
        double denom = 1 + z2 / n;
        double centre = (p + z2 / (2.0 * n)) / denom;
        double demi = z * Math.Sqrt(p * (1 - p) / n + z2 / (4.0 * n * n)) / denom;
        return new Interval(Math.Max(0, centre - demi), Math.Min(1, centre + demi));
    }
}

/// <summary>
/// Dégâts subis par type de salle (moyenne observée sur les runs perso).
/// <see cref="Samples"/> = nombre d'étages observés ; <see cref="Basis"/> dit toujours d'où vient
/// la moyenne et combien de runs ont été ignorées (et pourquoi). Jamais vide.
/// </summary>
public sealed record RoomRisk(double AverageDamage, int Samples, string Basis);

/// <summary>
/// Historique personnel, agrégé. Aucune I/O ici : l'implémentation charge les runs une fois
/// (voir <see cref="PersonalStats.Load"/>). Toutes les lectures sont sans effet de bord.
/// </summary>
public interface IPersonalStats
{
    PersonalCardStats? Card(string cardId);
    RoomRisk RoomRisk(int actIndex, string roomType);
    int RunCount { get; }
    int RunsForCharacter(string characterId);
}
