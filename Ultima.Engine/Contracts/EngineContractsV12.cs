using Ultima.Saves.Model;

namespace Ultima.Engine.Contracts;

// CONTRAT v1.2 — ADDITIF (chef, 2026-10-29). Demandes de Hermes : docs/28 §6, docs/29 §5.3, docs/42 §4.
// Règle : la version MAJEURE (EngineState.CurrentVersion = 1) ne change que sur une rupture ;
// les ajouts compatibles incrémentent CurrentMinor. Les consommateurs ignorent les champs inconnus.
// « Probabiliste seulement » : la carte ne contient QUE ce que le joueur voit (types des cases).

public static class ContractInfo
{
    public const int CurrentMinor = 4;   // v1.3 : HudSettings ; v1.4 : Custom, ModuleSetting.Screens
}

// ---- Carte et route (MapView → PathPlanner) ---------------------------------------------------

public sealed record MapCoordView(int Col, int Row);

/// <summary>Case visible de la carte de l'acte courant. RoomType ∈ RoomTypes.All.</summary>
public sealed record MapNodeView(int Col, int Row, string RoomType, IReadOnlyList<MapCoordView> Children, bool Visited);

/// <summary>Carte complète de l'acte courant (ce que le joueur voit), position et boss.</summary>
public sealed record MapView(int ActIndex, int Width, int Height, IReadOnlyList<MapNodeView> Nodes,
    MapCoordView? Position, MapCoordView Boss);

/// <summary>Route conseillée de la position au boss (calcul Hermes : PathPlanner, PV cumulés).</summary>
public sealed record RouteView(IReadOnlyList<MapCoordView> Path, int? ExpectedHpAtBoss, bool MayBeLethal,
    IReadOnlyList<ScoreTerm> Terms, string Basis);

// ---- Équipe co-op (PartyView → CoopScorer) ----------------------------------------------------

/// <summary>Un joueur de la partie (le local inclus, IsLocal). Solo : une seule entrée.</summary>
public sealed record AllyView(ulong NetId, string Name, string CharacterId, bool IsLocal, int Hp, int MaxHp,
    IReadOnlyList<CardView> Deck, IReadOnlyList<RelicView> Relics);

/// <summary>Entrée de calcul pour un allié (identifiants seulement, aucun texte).</summary>
public sealed record AllyContext(ulong NetId, string CharacterId, int Hp, int MaxHp,
    IReadOnlyList<CardInstance> Deck, IReadOnlyList<string> RelicIds);

// ---- Événements (EventView → EventScorer) -----------------------------------------------------

public sealed record EventOptionView(string Id, string Label, string? Description, bool Available, Score? Score);

/// <summary>Événement courant (salle « ? » ou ancien) et ses options proposées à l'écran.</summary>
public sealed record EventView(string EventId, string Name, IReadOnlyList<EventOptionView> Options);

// ---- Revue de run (docs/29 §5.3, repris tel que proposé par Hermes) ----------------------------

public sealed record ReviewTimeline(
    int ReviewVersion, ReviewRun Run, IReadOnlyList<ReviewLane> Lanes, IReadOnlyList<ReviewState> States,
    IReadOnlyList<ReviewPoint> Points, ReviewSummary Summary, ReviewCoverage Coverage);

public sealed record ReviewRun(
    string RunId, string BuildId, string GameMode, int Ascension, string CharacterId,
    string? Seed, DateTimeOffset StartedAt, int RunTimeSeconds,
    bool Win, bool WasAbandoned, string? KilledBy,
    string Source, string ScorerVersion, string? WeightsHash, string DataBasis);

public sealed record ReviewLane(ulong PlayerId, string CharacterId,
    IReadOnlyList<CardView> FinalDeck, IReadOnlyList<RelicView> FinalRelics);

public sealed record ReviewState(
    int Index, int Floor, int Hp, int MaxHp, int Gold,
    IReadOnlyList<CardView> Deck, IReadOnlyList<RelicView> Relics, IReadOnlyList<PotionView> Potions);

public sealed record ReviewPoint(
    int Index, int Floor, int ActIndex, string RoomType, string? ModelId,
    IReadOnlyList<string> MonsterIds, int TurnsTaken, ulong PlayerId,
    int? StateBefore, int StateAfter, ReviewDelta Delta, ReviewKind Kind, ReviewChoice? Chosen,
    IReadOnlyList<ReviewOption> Options, ReviewRegret? Regret, string? Note);

public sealed record ReviewDelta(int DamageTaken, int HpHealed, int GoldDelta, int MaxHpDelta);

public sealed record ReviewChoice(string Kind, string? Id);

public enum ReviewKind { None, CardReward, RelicReward, Ancient, Shop, Rest, Event, Removal, Transform, Potion }

public sealed record ReviewOption(string Kind, string? Id, string Label,
    double? DeltaG, bool Picked, bool Available, string? UnavailableReason,
    string Confidence, IReadOnlyList<ScoreTerm> Terms);

public sealed record ReviewRegret(double DeltaG, string Band, string? BestId, string Confidence, string Basis);

public sealed record ReviewSummary(int Points, int Scored, int NotEvaluable,
    double RegretTotal, double RegretMean, IReadOnlyList<int> WorstPoints, double Concentration,
    IReadOnlyList<ReviewActRegret> ByAct, string? Note);

public sealed record ReviewActRegret(int ActIndex, int Scored, double RegretTotal);

public sealed record ReviewCoverage(int Scored, int NotEvaluable, IReadOnlyList<string> Reasons, string DataBasis);

public static class RegretBands
{
    public const string Best = "best", Info = "info", Inaccuracy = "inaccuracy",
        Error = "error", Blunder = "blunder", NotEvaluable = "not_evaluable";
    public static string FromRegret(double r) => r <= 0 ? Best : r < 5 ? Info : r < 12 ? Inaccuracy : r < 25 ? Error : Blunder;
}

// ---- Interfaces v1.2 (implémentations : Hermes ; défaut = non calculé) ------------------------

public interface IRoutePlanner
{
    RouteView? Plan(MapView map, ScoringContext ctx) => null;
}

public interface IEventAdvisor
{
    /// <summary>Un score par option, dans l'ordre reçu ; null = non évaluable.</summary>
    IReadOnlyList<Score?> Rank(string eventId, IReadOnlyList<string> optionIds, ScoringContext ctx)
        => optionIds.Select(_ => (Score?)null).ToList();
}

/// <summary>Revue d'une run passée, calculée hors ligne (jamais pendant le jeu).</summary>
public interface IReviewBuilder
{
    ReviewTimeline Build(RunRecord run);
}
