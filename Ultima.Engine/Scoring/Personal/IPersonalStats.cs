namespace Ultima.Engine.Scoring.Personal;

/// <summary>Statistiques personnelles d'une carte (history + progress.save).</summary>
public sealed record PersonalCardStats(
    string CardId,
    int Offered,
    int Picked,
    int WinsWhenPicked,
    int LossesWhenPicked,
    int WinsWhenSkipped,
    int LossesWhenSkipped)
{
    public int RunsWithCard => WinsWhenPicked + LossesWhenPicked;
    public int RunsWithoutCard => WinsWhenSkipped + LossesWhenSkipped;

    public double PickRate => Offered > 0 ? (double)Picked / Offered : 0;

    public double WinRateWith => RunsWithCard > 0 ? (double)WinsWhenPicked / RunsWithCard : 0;
    public double WinRateWithout => RunsWithoutCard > 0 ? (double)WinsWhenSkipped / RunsWithoutCard : 0;

    /// <summary>Écart de taux de victoire (avec − sans). Null si un côté a moins de 5 runs.</summary>
    public double? WinRateDelta =>
        RunsWithCard >= 5 && RunsWithoutCard >= 5 ? WinRateWith - WinRateWithout : null;
}

/// <summary>Dégâts subis par type de salle (moyenne observée sur les runs perso).</summary>
public sealed record RoomRisk(double AverageDamage, int Samples, string Basis);

/// <summary>
/// Historique personnel, agrégé. Aucune I/O ici : l'implémentation charge les runs une fois.
/// </summary>
public interface IPersonalStats
{
    PersonalCardStats? Card(string cardId);
    RoomRisk RoomRisk(int actIndex, string roomType);
    int RunCount { get; }
    int RunsForCharacter(string characterId);
}
