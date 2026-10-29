using Ultima.Engine.Contracts;

namespace Ultima.Daemon.Knowledge;

/// <summary>
/// Connaissance de repli tant que <c>Ultima.Knowledge</c> (Encyclopédiste) n'est pas branchée :
/// les noms sont les identifiants, type/rareté « Unknown », coût -1 (inconnu). Seule information
/// statique fournie : la liste des MALÉDICTIONS (pour que le module « curses » reste juste), tirée
/// des classes <c>CardType.Curse</c> du source v0.107.1 (<c>Models/Cards/*.cs</c>, hors MockCurseCard).
/// </summary>
public sealed class NullKnowledge : IKnowledge
{
    public static NullKnowledge Instance { get; } = new();

    /// <summary>Classes Curse du source v0.107.1, converties en id de save (AscendersBane → CARD.ASCENDERS_BANE).</summary>
    public static readonly IReadOnlySet<string> KnownCurses = new HashSet<string>(StringComparer.Ordinal)
    {
        "CARD.ASCENDERS_BANE", "CARD.BAD_LUCK", "CARD.CLUMSY", "CARD.CURSE_OF_THE_BELL", "CARD.DEBT",
        "CARD.DECAY", "CARD.DOUBT", "CARD.ENTHRALLED", "CARD.FOLLY", "CARD.GREED", "CARD.GUILTY",
        "CARD.INJURY", "CARD.NORMALITY", "CARD.POOR_SLEEP", "CARD.REGRET", "CARD.SHAME",
        "CARD.SPORE_MIND", "CARD.WRITHE",
    };

    public CardInfo? Card(string id) => Fallbacks.Card(id);
    public RelicInfo? Relic(string id) => null;
    public EnchantmentInfo? Enchantment(string id) => null;
    public PotionInfo? Potion(string id) => null;
    public string? EncounterName(string id) => null;
    public string? CharacterName(string id) => null;
}

/// <summary>Valeurs de repli quand la connaissance ne connaît pas un id (nouvelle carte après un patch…).</summary>
public static class Fallbacks
{
    public const string UnknownType = "Unknown";
    public const int UnknownCost = -1;

    public static CardInfo Card(string id)
    {
        bool curse = NullKnowledge.KnownCurses.Contains(id);
        return new CardInfo(id, "", id, "", curse ? "Curse" : UnknownType, curse ? "Curse" : UnknownType,
            UnknownCost, "", curse, Array.Empty<string>());
    }
}
