namespace Ultima.Engine.Contracts;

// CONTRAT MOTEUR v1 — propriétaire : chef. Figé à partir des données RÉELLES de la fenêtre de
// test n°1 (saves + miroir, 2026-10-29). Les agents codent contre ce contrat et ne le modifient pas
// (demande → status/<agent>.md). Sérialisé en JSON (camelCase) sur l'IPC daemon → overlay/compagnon.
// Rappel « probabiliste seulement » : aucune donnée cachée (séquences pré-tirées, rng, odds) ici.

public enum Screen { Unknown, Menu, Map, Combat, Reward, Shop, Event, RestSite, Treasure }

/// <summary>État publié par le daemon (un message complet à chaque changement).</summary>
public sealed record EngineState(
    int ContractVersion,
    DateTimeOffset At,
    SourceHealth Health,
    Screen Screen,
    RunView? Run,
    RewardView? Reward,
    ShopView? Shop,
    PathsView? Paths,
    DeckEvaluation? Deck,
    OverlaySettings Settings)
{
    public const int CurrentVersion = 1;
}

/// <summary>Santé des sources. Degraded != null → l'UI l'annonce (jamais de faux chiffre).</summary>
public sealed record SourceHealth(bool GameRunning, bool SaveOk, bool MirrorOk, int? MirrorLatencyMs, string? Degraded);

public sealed record RunView(
    string CharacterId, string CharacterName, int ActIndex, string? ActId, string? BossId, string? BossName,
    int Ascension, int Hp, int MaxHp, int Gold, int Floor,
    IReadOnlyList<CardView> Deck,
    IReadOnlyList<RelicView> Relics,
    IReadOnlyList<PotionView> Potions);

/// <summary>Carte (deck, récompense, shop). Enchantment = bonus spécial (Sown, Swift…). IsCurse = malédiction.</summary>
public sealed record CardView(
    string Id, string Name, string Type, string Rarity, int Cost, int Upgrade,
    EnchantmentView? Enchantment, bool IsCurse, Score? Score);

public sealed record EnchantmentView(string Id, string Name, int Amount, string Description);

public sealed record RelicView(string Id, string Name, string Description, Score? Score);

public sealed record PotionView(string Id, string Name, string Description);

/// <summary>Score explicable : Total 0-100 = somme bornée des termes. Confidence : high|medium|low.</summary>
public sealed record Score(int Total, IReadOnlyList<ScoreTerm> Terms, string Confidence);

/// <summary>Un terme nommé du score, avec sa source (« Spire Codex v0.107.1 », « tes 247 runs »…).</summary>
public sealed record ScoreTerm(string Label, int Points, string Source);

public sealed record RewardView(IReadOnlyList<CardView> Offered, Score SkipScore, bool AlreadyTaken);

public sealed record ShopView(IReadOnlyList<ShopItem> Items, int RemovalPrice, bool RemovalUsed, Score? RemovalScore);

/// <summary>Kind : card | relic | potion. Price = prix affiché (estimé, réductions de reliques comprises).</summary>
public sealed record ShopItem(string Kind, string Id, string Name, int Price, bool OnSale, bool Affordable,
    CardView? Card, Score? Score);

public sealed record RoomCounts(int Monster, int Elite, int Unknown, int Rest, int Shop, int Treasure);

/// <summary>Un choix immédiat sur la carte (enfant de la position) et ce qu'il ouvre jusqu'au boss.</summary>
public sealed record PathChoice(int Col, int Row, string RoomType, int PathCount,
    RoomCounts Min, RoomCounts Median, RoomCounts Max, double ExpectedDamage, string RiskBasis);

/// <summary>Part des chemins restants qui traversent la case (« 17 de tes 23 chemins passent par ce feu »).</summary>
public sealed record NodeFrequency(int Col, int Row, string RoomType, int PathsThrough, double Share);

public sealed record PathsView(int TotalPaths, IReadOnlyList<PathChoice> NextChoices, IReadOnlyList<NodeFrequency> Nodes);

/// <summary>Évaluation du deck face aux reliques et au boss de l'acte + archétypes potentiels.</summary>
public sealed record DeckEvaluation(Score Score, string? Archetype, IReadOnlyList<string> PotentialArchetypes,
    IReadOnlyList<CardView> Curses);

/// <summary>Modules affichables, personnalisables (ordre + visibilité) via le compagnon.</summary>
public sealed record OverlaySettings(IReadOnlyList<ModuleSetting> Modules)
{
    public static OverlaySettings Default { get; } = new(new ModuleSetting[]
    {
        new(ModuleIds.Curses, true, 0),
        new(ModuleIds.Deck, true, 1),
        new(ModuleIds.Relics, true, 2),
        new(ModuleIds.Enchanted, true, 3),
        new(ModuleIds.Reward, true, 4),
        new(ModuleIds.Shop, true, 5),
        new(ModuleIds.Paths, true, 6),
        new(ModuleIds.DeckScore, true, 7),
    });
}

public sealed record ModuleSetting(string Id, bool Enabled, int Order);

public static class ModuleIds
{
    public const string Curses = "curses", Deck = "deck", Relics = "relics", Enchanted = "enchanted",
        Reward = "reward", Shop = "shop", Paths = "paths", DeckScore = "deck_score";
}
