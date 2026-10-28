using System.Text.Json.Serialization;

namespace Ultima.Saves.Dto;

// DTO = miroir STRICT des champs AUTORISES et PROUVES par un exemple reel
// (voir fixtures/saves/SCHEMA.md). Tout champ absent d'ici n'est PAS deserialise :
// System.Text.Json saute les proprietes inconnues sans les materialiser.
// Interdit : [JsonExtensionData] (capturerait les champs caches) — verifie par un test.

internal sealed class CoordDto
{
    [JsonPropertyName("col")] public int Col { get; set; }
    [JsonPropertyName("row")] public int Row { get; set; }
}

internal sealed class EnchantmentDto
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("amount")] public int Amount { get; set; }
}

/// <summary>Core/Saves/Runs/SerializableCard.cs (props ignores).</summary>
internal sealed class CardDto
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("current_upgrade_level")] public int CurrentUpgradeLevel { get; set; }
    [JsonPropertyName("enchantment")] public EnchantmentDto? Enchantment { get; set; }
    [JsonPropertyName("floor_added_to_deck")] public int? FloorAddedToDeck { get; set; }
}

/// <summary>Core/Saves/Runs/SerializableRelic.cs (props ignores).</summary>
internal sealed class RelicDto
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("floor_added_to_deck")] public int? FloorAddedToDeck { get; set; }
}

/// <summary>Core/Saves/Runs/SerializablePotion.cs.</summary>
internal sealed class PotionDto
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("slot_index")] public int SlotIndex { get; set; }
}

internal sealed class CardChoiceDto
{
    [JsonPropertyName("card")] public CardDto? Card { get; set; }
    [JsonPropertyName("was_picked")] public bool WasPicked { get; set; }
}

internal sealed class ModelChoiceDto
{
    [JsonPropertyName("choice")] public string? Choice { get; set; }
    [JsonPropertyName("was_picked")] public bool WasPicked { get; set; }
}

internal sealed class LocStringDto
{
    [JsonPropertyName("key")] public string? Key { get; set; }
    [JsonPropertyName("table")] public string? Table { get; set; }
}

internal sealed class AncientChoiceDto
{
    [JsonPropertyName("TextKey")] public string? TextKey { get; set; }
    [JsonPropertyName("title")] public LocStringDto? Title { get; set; }
    [JsonPropertyName("was_chosen")] public bool WasChosen { get; set; }
}

internal sealed class EventChoiceDto
{
    [JsonPropertyName("title")] public LocStringDto? Title { get; set; }
}

internal sealed class CardTransformDto
{
    [JsonPropertyName("original_card")] public CardDto? OriginalCard { get; set; }
    [JsonPropertyName("final_card")] public CardDto? FinalCard { get; set; }
}

internal sealed class CardEnchantDto
{
    [JsonPropertyName("card")] public CardDto? Card { get; set; }
    [JsonPropertyName("enchantment")] public string? Enchantment { get; set; }
}

/// <summary>Core/Runs/PlayerMapPointHistoryEntry.cs.</summary>
internal sealed class PlayerFloorStatsDto
{
    [JsonPropertyName("player_id")] public ulong PlayerId { get; set; }
    [JsonPropertyName("current_hp")] public int CurrentHp { get; set; }
    [JsonPropertyName("max_hp")] public int MaxHp { get; set; }
    [JsonPropertyName("current_gold")] public int CurrentGold { get; set; }
    [JsonPropertyName("damage_taken")] public int DamageTaken { get; set; }
    [JsonPropertyName("hp_healed")] public int HpHealed { get; set; }
    [JsonPropertyName("max_hp_gained")] public int MaxHpGained { get; set; }
    [JsonPropertyName("max_hp_lost")] public int MaxHpLost { get; set; }
    [JsonPropertyName("gold_gained")] public int GoldGained { get; set; }
    [JsonPropertyName("gold_spent")] public int GoldSpent { get; set; }
    [JsonPropertyName("gold_lost")] public int GoldLost { get; set; }
    [JsonPropertyName("gold_stolen")] public int GoldStolen { get; set; }
    [JsonPropertyName("card_choices")] public List<CardChoiceDto>? CardChoices { get; set; }
    [JsonPropertyName("cards_gained")] public List<CardDto>? CardsGained { get; set; }
    [JsonPropertyName("cards_removed")] public List<CardDto>? CardsRemoved { get; set; }
    [JsonPropertyName("cards_transformed")] public List<CardTransformDto>? CardsTransformed { get; set; }
    [JsonPropertyName("cards_enchanted")] public List<CardEnchantDto>? CardsEnchanted { get; set; }
    [JsonPropertyName("upgraded_cards")] public List<string>? UpgradedCards { get; set; }
    [JsonPropertyName("downgraded_cards")] public List<string>? DowngradedCards { get; set; }
    [JsonPropertyName("relic_choices")] public List<ModelChoiceDto>? RelicChoices { get; set; }
    [JsonPropertyName("relics_removed")] public List<string>? RelicsRemoved { get; set; }
    [JsonPropertyName("potion_choices")] public List<ModelChoiceDto>? PotionChoices { get; set; }
    [JsonPropertyName("potion_used")] public List<string>? PotionUsed { get; set; }
    [JsonPropertyName("potion_discarded")] public List<string>? PotionDiscarded { get; set; }
    [JsonPropertyName("rest_site_choices")] public List<string>? RestSiteChoices { get; set; }
    [JsonPropertyName("bought_relics")] public List<string>? BoughtRelics { get; set; }
    [JsonPropertyName("bought_potions")] public List<string>? BoughtPotions { get; set; }
    [JsonPropertyName("bought_colorless")] public List<string>? BoughtColorless { get; set; }
    [JsonPropertyName("event_choices")] public List<EventChoiceDto>? EventChoices { get; set; }
    [JsonPropertyName("ancient_choice")] public List<AncientChoiceDto>? AncientChoice { get; set; }
    [JsonPropertyName("completed_quests")] public List<string>? CompletedQuests { get; set; }
}

/// <summary>Core/Runs/History/MapPointRoomHistoryEntry.cs.</summary>
internal sealed class FloorRoomDto
{
    [JsonPropertyName("room_type")] public string? RoomType { get; set; }
    [JsonPropertyName("model_id")] public string? ModelId { get; set; }
    [JsonPropertyName("monster_ids")] public List<string>? MonsterIds { get; set; }
    [JsonPropertyName("turns_taken")] public int TurnsTaken { get; set; }
}

/// <summary>Core/Runs/History/MapPointHistoryEntry.cs (identique save et .run).</summary>
internal sealed class FloorDto
{
    [JsonPropertyName("map_point_type")] public string? MapPointType { get; set; }
    [JsonPropertyName("rooms")] public List<FloorRoomDto>? Rooms { get; set; }
    [JsonPropertyName("player_stats")] public List<PlayerFloorStatsDto>? PlayerStats { get; set; }
}

internal sealed class ModifierDto
{
    [JsonPropertyName("id")] public string? Id { get; set; }
}
