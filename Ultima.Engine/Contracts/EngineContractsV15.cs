namespace Ultima.Engine.Contracts;

// CONTRAT v1.5 — ADDITIF (chef, 2026-10-30). Décisions : docs/46-contrat-v15.md.
// Notes sous les choix (T7), coût X/injouable, combat (T6, rempli APRÈS validation FPS), feu de camp.
// Probabiliste seulement : piles COMPTÉES (jamais l'ordre de la pioche), intentions telles qu'affichées à l'écran.

public static class CostKinds
{
    public const string Number = "number", X = "x", Unplayable = "unplayable";
    public static readonly IReadOnlyList<string> All = new[] { Number, X, Unplayable };
}

/// <summary>Emplacements du marchand (merchant_inventory.tscn) : « card:0..4 », « colorless:0..1 », « relic:0..2 », « potion:0..2 ».</summary>
public static class ShopSlots
{
    public const string Card = "card", Colorless = "colorless", Relic = "relic", Potion = "potion";
    public static string Of(string kind, int index) => $"{kind}:{index}";
}

/// <summary>Avis communautaire (méta) : Consensus 0..100, nombre de sources, écart entre sources, taux connus.</summary>
public sealed record CommunityView(double? Consensus, int Sources, double? Spread, double? PickRate, double? WinRate,
    string? Source);

// ---- Combat (T6) ------------------------------------------------------------------------------

/// <summary>Une entrée de pile : la carte et son nombre d'exemplaires. Generated = créée pendant ce combat.</summary>
public sealed record PileEntry(CardView Card, int Count, bool Generated);

/// <summary>Intention visible d'un ennemi (ce que l'écran montre). Kind libre côté jeu (attack, defend, buff…).</summary>
public sealed record IntentView(string Kind, int? Damage, int? Hits);

public sealed record EnemyView(string Id, string? Name, int Hp, int MaxHp, int Block, IReadOnlyList<IntentView> Intents);

/// <summary>
/// Combat en cours. Draw/Discard/Exhaust sont des comptes TRIÉS par id (l'ordre de la pioche n'est jamais publié).
/// DrawUntilReshuffle = cartes restantes avant le remélange. Hand : main visible.
/// </summary>
public sealed record CombatView(int Turn, IReadOnlyList<PileEntry> Hand, IReadOnlyList<PileEntry> Draw,
    IReadOnlyList<PileEntry> Discard, IReadOnlyList<PileEntry> Exhaust, int DrawUntilReshuffle,
    IReadOnlyList<EnemyView> Enemies, int? IncomingDamage, bool? Lethal);

// ---- Feu de camp (T6) -------------------------------------------------------------------------

/// <summary>Option du feu de camp (« rest », « smith », … id du jeu). Target = carte conseillée pour « smith ».</summary>
public sealed record RestOptionView(string Id, string? Label, bool Available, Score? Score, CardView? Target);

public sealed record RestSiteView(IReadOnlyList<RestOptionView> Options);
