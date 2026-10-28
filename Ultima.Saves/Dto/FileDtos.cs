using System.Text.Json.Serialization;

namespace Ultima.Saves.Dto;

// ---------- current_run.save (Core/Saves/SerializableRun.cs, schema_version 16) ----------

/// <summary>
/// Racine de current_run.save. Volontairement NON declares (decision de Mio, docs/20 §8.2) :
/// les sequences pre-tirees de l'acte, les sacs de reliques, les etats d'aleatoire et les cotes
/// (racine et joueur), ainsi que seed/map_drawings/unlock_state (inutiles ou personnels).
/// </summary>
internal sealed class RunSaveDto
{
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; }
    [JsonPropertyName("acts")] public List<ActDto>? Acts { get; set; }
    [JsonPropertyName("modifiers")] public List<ModifierDto>? Modifiers { get; set; }
    [JsonPropertyName("current_act_index")] public int CurrentActIndex { get; set; }
    [JsonPropertyName("players")] public List<RunPlayerDto>? Players { get; set; }
    [JsonPropertyName("visited_map_coords")] public List<CoordDto>? VisitedMapCoords { get; set; }
    [JsonPropertyName("map_point_history")] public List<List<FloorDto>>? MapPointHistory { get; set; }
    [JsonPropertyName("save_time")] public long SaveTime { get; set; }
    [JsonPropertyName("start_time")] public long StartTime { get; set; }
    [JsonPropertyName("run_time")] public long RunTime { get; set; }
    [JsonPropertyName("win_time")] public long WinTime { get; set; }
    [JsonPropertyName("ascension")] public int Ascension { get; set; }
    [JsonPropertyName("game_mode")] public string? GameMode { get; set; }
    [JsonPropertyName("platform_type")] public string? PlatformType { get; set; }
}

/// <summary>Core/Saves/Runs/SerializableActModel.cs.</summary>
internal sealed class ActDto
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("rooms")] public ActRoomsDto? Rooms { get; set; }
    [JsonPropertyName("saved_map")] public SavedMapDto? SavedMap { get; set; }
}

/// <summary>
/// Core/Saves/Runs/SerializableRoomSet.cs — SEULS les champs visibles en jeu sont declares :
/// boss de l'acte (affiche sur la carte), second boss, ancien de depart.
/// </summary>
internal sealed class ActRoomsDto
{
    [JsonPropertyName("boss_id")] public string? BossId { get; set; }
    [JsonPropertyName("second_boss_id")] public string? SecondBossId { get; set; }
    [JsonPropertyName("ancient_id")] public string? AncientId { get; set; }
}

/// <summary>Core/Saves/Runs/SerializableActMap.cs.</summary>
internal sealed class SavedMapDto
{
    [JsonPropertyName("width")] public int Width { get; set; }
    [JsonPropertyName("height")] public int Height { get; set; }
    [JsonPropertyName("points")] public List<MapPointDto>? Points { get; set; }
    [JsonPropertyName("start")] public MapPointDto? Start { get; set; }
    [JsonPropertyName("boss")] public MapPointDto? Boss { get; set; }
    [JsonPropertyName("second_boss")] public MapPointDto? SecondBoss { get; set; }
    [JsonPropertyName("start_coords")] public List<CoordDto>? StartCoords { get; set; }
}

/// <summary>Core/Saves/Runs/SerializableMapPoint.cs.</summary>
internal sealed class MapPointDto
{
    [JsonPropertyName("coord")] public CoordDto? Coord { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("children")] public List<CoordDto>? Children { get; set; }
}

/// <summary>Core/Saves/Runs/SerializablePlayer.cs (champs autorises uniquement).</summary>
internal sealed class RunPlayerDto
{
    [JsonPropertyName("net_id")] public ulong NetId { get; set; }
    [JsonPropertyName("character_id")] public string? CharacterId { get; set; }
    [JsonPropertyName("current_hp")] public int CurrentHp { get; set; }
    [JsonPropertyName("max_hp")] public int MaxHp { get; set; }
    [JsonPropertyName("gold")] public int Gold { get; set; }
    [JsonPropertyName("max_energy")] public int MaxEnergy { get; set; }
    [JsonPropertyName("max_potion_slot_count")] public int MaxPotionSlotCount { get; set; } = 3;
    [JsonPropertyName("base_orb_slot_count")] public int BaseOrbSlotCount { get; set; }
    [JsonPropertyName("deck")] public List<CardDto>? Deck { get; set; }
    [JsonPropertyName("relics")] public List<RelicDto>? Relics { get; set; }
    [JsonPropertyName("potions")] public List<PotionDto>? Potions { get; set; }
}

// ---------- history/*.run (Core/Runs/RunHistory.cs, schema_version 9) ----------

/// <summary>RunHistory sans <c>seed</c> (inutile, et exclu des fixtures).</summary>
internal sealed class RunHistoryDto
{
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; }
    [JsonPropertyName("platform_type")] public string? PlatformType { get; set; }
    [JsonPropertyName("game_mode")] public string? GameMode { get; set; }
    [JsonPropertyName("win")] public bool Win { get; set; }
    [JsonPropertyName("start_time")] public long StartTime { get; set; }
    [JsonPropertyName("run_time")] public long RunTime { get; set; }
    [JsonPropertyName("ascension")] public int Ascension { get; set; }
    [JsonPropertyName("build_id")] public string? BuildId { get; set; }
    [JsonPropertyName("was_abandoned")] public bool WasAbandoned { get; set; }
    [JsonPropertyName("killed_by_encounter")] public string? KilledByEncounter { get; set; }
    [JsonPropertyName("killed_by_event")] public string? KilledByEvent { get; set; }
    [JsonPropertyName("players")] public List<HistoryPlayerDto>? Players { get; set; }
    [JsonPropertyName("acts")] public List<string>? Acts { get; set; }
    [JsonPropertyName("modifiers")] public List<ModifierDto>? Modifiers { get; set; }
    [JsonPropertyName("map_point_history")] public List<List<FloorDto>>? MapPointHistory { get; set; }
}

/// <summary>Core/Runs/RunHistoryPlayer.cs.</summary>
internal sealed class HistoryPlayerDto
{
    [JsonPropertyName("id")] public ulong Id { get; set; }
    [JsonPropertyName("character")] public string? Character { get; set; }
    [JsonPropertyName("deck")] public List<CardDto>? Deck { get; set; }
    [JsonPropertyName("relics")] public List<RelicDto>? Relics { get; set; }
    [JsonPropertyName("potions")] public List<PotionDto>? Potions { get; set; }
    [JsonPropertyName("max_potion_slot_count")] public int MaxPotionSlotCount { get; set; }
}

// ---------- progress.save (Core/Saves/SerializableProgress.cs, schema_version 21) ----------

/// <summary>Stats seulement ; <c>unique_id</c> et listes de decouverte non lus.</summary>
internal sealed class ProgressDto
{
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; }
    [JsonPropertyName("total_playtime")] public long TotalPlaytime { get; set; }
    [JsonPropertyName("floors_climbed")] public long FloorsClimbed { get; set; }
    [JsonPropertyName("character_stats")] public List<CharacterStatsDto>? CharacterStats { get; set; }
    [JsonPropertyName("encounter_stats")] public List<EncounterStatsDto>? EncounterStats { get; set; }
    [JsonPropertyName("enemy_stats")] public List<EnemyStatsDto>? EnemyStats { get; set; }
    [JsonPropertyName("card_stats")] public List<CardStatsDto>? CardStats { get; set; }
    [JsonPropertyName("ancient_stats")] public List<AncientStatsDto>? AncientStats { get; set; }
}

internal sealed class CharacterStatsDto
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("total_wins")] public int TotalWins { get; set; }
    [JsonPropertyName("total_losses")] public int TotalLosses { get; set; }
    [JsonPropertyName("max_ascension")] public int MaxAscension { get; set; }
    [JsonPropertyName("preferred_ascension")] public int PreferredAscension { get; set; }
    [JsonPropertyName("best_win_streak")] public int BestWinStreak { get; set; }
    [JsonPropertyName("current_streak")] public int CurrentStreak { get; set; }
    [JsonPropertyName("fastest_win_time")] public long FastestWinTime { get; set; }
    [JsonPropertyName("playtime")] public long Playtime { get; set; }
}

internal sealed class FightStatsDto
{
    [JsonPropertyName("character")] public string? Character { get; set; }
    [JsonPropertyName("wins")] public int Wins { get; set; }
    [JsonPropertyName("losses")] public int Losses { get; set; }
}

internal sealed class EncounterStatsDto
{
    [JsonPropertyName("encounter_id")] public string? EncounterId { get; set; }
    [JsonPropertyName("fight_stats")] public List<FightStatsDto>? FightStats { get; set; }
}

internal sealed class EnemyStatsDto
{
    [JsonPropertyName("enemy_id")] public string? EnemyId { get; set; }
    [JsonPropertyName("fight_stats")] public List<FightStatsDto>? FightStats { get; set; }
}

internal sealed class CardStatsDto
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("times_picked")] public int TimesPicked { get; set; }
    [JsonPropertyName("times_skipped")] public int TimesSkipped { get; set; }
    [JsonPropertyName("times_won")] public int TimesWon { get; set; }
    [JsonPropertyName("times_lost")] public int TimesLost { get; set; }
}

internal sealed class AncientStatsDto
{
    [JsonPropertyName("ancient_id")] public string? AncientId { get; set; }
    [JsonPropertyName("character_stats")] public List<FightStatsDto>? CharacterStats { get; set; }
}

// ---------- profile.save (Core/Saves/ProfileSave.cs) ----------

internal sealed class ProfileSaveDto
{
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; }
    // Defaut 1 = valeur par defaut du jeu (ProfileSave.cs:19).
    [JsonPropertyName("last_profile_id")] public int LastProfileId { get; set; } = 1;
}
