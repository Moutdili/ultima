using Ultima.Saves.Model;

namespace Ultima.Engine.Contracts;

// Interfaces implémentées par les agents de la vague 1 (contrat chef, ne pas modifier).

/// <summary>Connaissance statique du jeu (Encyclopédiste) : noms/descriptions FR, métadonnées.</summary>
public interface IKnowledge
{
    CardInfo? Card(string id);          // id = "CARD.X"
    RelicInfo? Relic(string id);        // "RELIC.X"
    EnchantmentInfo? Enchantment(string id);
    PotionInfo? Potion(string id);
    string? EncounterName(string id);   // "ENCOUNTER.X_BOSS"
    string? CharacterName(string id);   // "CHARACTER.DEFECT"

    /// <summary>v1.2 : carte au niveau d'amélioration donné (nom « + », coût, texte). Défaut : carte de base.</summary>
    CardInfo? Card(string id, int upgradeLevel) => Card(id);
    /// <summary>v1.2 : enchantement avec le montant lu dans la save (texte rendu). Défaut : sans montant.</summary>
    EnchantmentInfo? Enchantment(string id, int amount) => Enchantment(id);
}

public sealed record CardInfo(string Id, string ClassName, string Name, string Description, string Type,
    string Rarity, int Cost, string Owner, bool IsCurse, IReadOnlyList<string> Keywords);
public sealed record RelicInfo(string Id, string ClassName, string Name, string Description, string Rarity, string Owner);
public sealed record EnchantmentInfo(string Id, string ClassName, string Name, string Description);
public sealed record PotionInfo(string Id, string ClassName, string Name, string Description, string Rarity);

/// <summary>Contexte de scoring : tout ce que le joueur voit (aucune donnée cachée).</summary>
public sealed record ScoringContext(
    string CharacterId, int ActIndex, string? BossId, int Ascension, int Floor,
    IReadOnlyList<CardInstance> Deck, IReadOnlyList<string> RelicIds, int Gold, int Hp, int MaxHp)
{
    /// <summary>v1.2 : alliés (co-op), joueur local exclu. Vide en solo.</summary>
    public IReadOnlyList<AllyContext> Allies { get; init; } = Array.Empty<AllyContext>();
}

/// <summary>Stratège : scores explicables (méta communautaire + historique perso + synergies).</summary>
public interface IScorer
{
    Score ScoreCard(CardInstance card, ScoringContext ctx);
    Score ScoreSkip(IReadOnlyList<CardInstance> offered, ScoringContext ctx);
    Score ScoreRelic(string relicId, ScoringContext ctx);
    Score ScoreShopItem(string kind, string id, int price, ScoringContext ctx);
    Score ScoreRemoval(int price, ScoringContext ctx);
    DeckEvaluation EvaluateDeck(ScoringContext ctx);

    /// <summary>Ajout v1.1 : score d'une potion possédée. Implémentation par défaut = non calculé (null),
    /// pour ne pas casser les implémentations existantes ; Hermes la surcharge quand il est prêt.</summary>
    Score? ScorePotion(string potionId, ScoringContext ctx) => null;
}

/// <summary>Cartographe : chemins de la position courante au boss (probabiliste, sans tirage caché).</summary>
public interface IPathAnalyzer
{
    PathsView Analyze(ActMap map, MapCoord position, int actIndex, string characterId, int hp);
}
