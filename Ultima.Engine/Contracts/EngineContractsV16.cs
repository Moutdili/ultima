using Ultima.Saves.Model;

namespace Ultima.Engine.Contracts;

// CONTRAT v1.6 — ADDITIF (chef, 2026-11-10). Décisions : docs/51-contrat-v16.md.
// Leçons de la victoire co-op A10 (docs/45-plan-hermes/T9) : second boss, feu de camp à plusieurs actions,
// soin d'un allié, offres enchaînées ou relancées, emplacements de potion, statistiques (op « stats.open »).
// Probabiliste seulement : le second boss est publié parce que le jeu l'affiche (icône de la barre du haut).

/// <summary>Options connues du feu de camp (ids du jeu, en majuscules comme dans la save). Une option inconnue
/// est publiée telle quelle, sans note.</summary>
public static class RestOptionIds
{
    public const string Heal = "HEAL", Smith = "SMITH", Mend = "MEND", Lift = "LIFT", Cook = "COOK", Dig = "DIG",
        Hatch = "HATCH", Kindle = "KINDLE", Clone = "CLONE";
    public static readonly IReadOnlyList<string> All = new[] { Heal, Smith, Mend, Lift, Cook, Dig, Hatch, Kindle, Clone };
}

/// <summary>
/// Une étape de la combinaison conseillée au feu de camp : l'option, et sa cible si elle en a une
/// (CardId pour « SMITH », PlayerNetId pour « MEND »).
/// </summary>
public sealed record RestStepView(string OptionId, string? CardId, ulong? PlayerNetId);

/// <summary>Conseil du feu de camp : entrée de calcul et résultat (implémentation : Hermes).</summary>
public sealed record RestAdvice(IReadOnlyList<Score?> Options, IReadOnlyList<RestStepView>? Plan);

public interface IRestAdvisor
{
    /// <summary>
    /// Un score par option, dans l'ordre reçu (null = non évaluable), et la meilleure combinaison pour
    /// <paramref name="actions"/> actions (null = non calculée). Défaut : rien de calculé.
    /// </summary>
    RestAdvice Advise(IReadOnlyList<string> optionIds, int actions, ScoringContext ctx)
        => new(optionIds.Select(_ => (Score?)null).ToList(), null);
}

// ---- Statistiques et historique (docs/25 D1-D3) — livrées par l'op socket « stats.open », jamais dans EngineState ----

/// <summary>Filtre d'un agrégat. null = tout. Mode ∈ StatsModes.All.</summary>
public sealed record StatsScope(string Mode, string? CharacterId, int? AscensionMin, int? AscensionMax);

public static class StatsModes
{
    public const string All = "all", Solo = "solo", Coop = "coop";
    public static readonly IReadOnlyList<string> Values = new[] { All, Solo, Coop };
}

/// <summary>Taux de victoire d'un groupe de runs. WinRate null si Runs = 0.</summary>
public sealed record StatsBucket(string Key, string? Label, int Runs, int Wins, double? WinRate, double? MeanFloor);

/// <summary>Dégâts subis face à une rencontre. N = nombre de combats ; Deaths = runs terminées ici.</summary>
public sealed record EncounterStats(string EncounterId, string? Name, string RoomType, int N, double MeanDamage,
    double? MedianDamage, int Deaths);

/// <summary>Une run de l'historique (liste). Players = nombre de joueurs ; KilledBy null si victoire ou abandon.</summary>
public sealed record RunSummary(string RunId, DateTimeOffset StartedAt, string CharacterId, int Ascension,
    int Players, bool Win, bool WasAbandoned, int Floor, string? KilledBy, int RunTimeSeconds, string BuildId);

/// <summary>
/// Statistiques personnelles calculées hors ligne depuis history/*.run. Solo et co-op ne sont JAMAIS mélangés
/// en silence : Scope dit ce qui est compté, et les surfaces l'affichent.
/// </summary>
public sealed record StatsView(int StatsVersion, StatsScope Scope, int Runs, int Wins,
    IReadOnlyList<StatsBucket> ByCharacter, IReadOnlyList<StatsBucket> ByAscension,
    IReadOnlyList<StatsBucket> ByArchetype, IReadOnlyList<EncounterStats> Encounters,
    IReadOnlyList<RunSummary> History, string DataBasis);

public interface IStatsBuilder
{
    StatsView Build(IReadOnlyList<RunRecord> runs, StatsScope scope);
}
