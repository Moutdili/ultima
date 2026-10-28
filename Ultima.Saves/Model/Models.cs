namespace Ultima.Saves.Model;

/// <summary>Coordonnee de carte (col 0..width-1, row 0 = depart, row = height pour le boss).</summary>
public readonly record struct MapCoord(int Col, int Row)
{
    public override string ToString() => $"({Col},{Row})";
}

/// <summary>Type de case (Core/Map/MapPointType.cs, serialise en snake_case).</summary>
public enum MapPointKind
{
    /// <summary>Valeur non reconnue (nouveau type apres un patch) : voir le champ brut.</summary>
    Unrecognized = -1,
    Unassigned = 0,
    /// <summary>Case « ? ».</summary>
    Unknown,
    Shop,
    Treasure,
    RestSite,
    Monster,
    Elite,
    Boss,
    Ancient,
}

public static class MapPointKinds
{
    public static MapPointKind Parse(string? raw) => raw switch
    {
        "unassigned" => MapPointKind.Unassigned,
        "unknown" => MapPointKind.Unknown,
        "shop" => MapPointKind.Shop,
        "treasure" => MapPointKind.Treasure,
        "rest_site" => MapPointKind.RestSite,
        "monster" => MapPointKind.Monster,
        "elite" => MapPointKind.Elite,
        "boss" => MapPointKind.Boss,
        "ancient" => MapPointKind.Ancient,
        _ => MapPointKind.Unrecognized,
    };
}

public sealed record Enchantment(string Id, int Amount);

/// <summary>Carte (deck, choix, gains). <c>UpgradeLevel</c> 0 = non amelioree.</summary>
public sealed record CardInstance(string Id, int UpgradeLevel, Enchantment? Enchantment, int? FloorAdded)
{
    public bool IsUpgraded => UpgradeLevel > 0;
    public override string ToString() => UpgradeLevel > 0 ? $"{Id}+{UpgradeLevel}" : Id;
}

public sealed record RelicInstance(string Id, int? FloorAdded);

public sealed record PotionInstance(string Id, int SlotIndex);

/// <summary>Etat d'un joueur dans current_run.save.</summary>
public sealed record PlayerState(
    ulong NetId,
    string CharacterId,
    int CurrentHp,
    int MaxHp,
    int Gold,
    int MaxEnergy,
    int MaxPotionSlotCount,
    int BaseOrbSlotCount,
    IReadOnlyList<CardInstance> Deck,
    IReadOnlyList<RelicInstance> Relics,
    IReadOnlyList<PotionInstance> Potions);

public sealed record MapNode(MapCoord Coord, MapPointKind Kind, string RawType, IReadOnlyList<MapCoord> Children);

/// <summary>Carte d'un acte (acts[].saved_map) : DAG des cases, depart, boss.</summary>
public sealed record ActMap(
    int Width,
    int Height,
    MapNode Start,
    MapNode Boss,
    MapNode? SecondBoss,
    IReadOnlyList<MapCoord> StartCoords,
    IReadOnlyList<MapNode> Points)
{
    private Dictionary<MapCoord, MapNode>? _index;

    /// <summary>Toutes les cases (points + depart + boss[es]) indexees par coordonnee.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyDictionary<MapCoord, MapNode> ByCoord => _index ??= BuildIndex();

    private Dictionary<MapCoord, MapNode> BuildIndex()
    {
        var d = new Dictionary<MapCoord, MapNode>();
        foreach (var p in Points) d[p.Coord] = p;
        d.TryAdd(Start.Coord, Start);
        d.TryAdd(Boss.Coord, Boss);
        if (SecondBoss is not null) d.TryAdd(SecondBoss.Coord, SecondBoss);
        return d;
    }
}

/// <summary>
/// Acte de la run. <c>Map</c> est null tant que le jeu ne l'a pas generee. Pour un acte futur
/// (<c>IsRevealed</c> = false) variante d'acte, boss, ancien et carte sont masques : le joueur ne les voit pas en jeu.
/// </summary>
public sealed record ActInfo(int Index, string? Id, string? BossId, string? SecondBossId, string? AncientId,
    ActMap? Map, bool IsRevealed);

public sealed record RoomVisit(string RoomType, string? ModelId, IReadOnlyList<string> MonsterIds, int TurnsTaken);

public sealed record ModelChoice(string Id, bool WasPicked);

public sealed record CardChoice(CardInstance Card, bool WasPicked);

public sealed record CardTransform(CardInstance Original, CardInstance Final);

public sealed record AncientOption(string TextKey, bool WasChosen);

/// <summary>Bilan d'un joueur sur un etage (map_point_history[][].player_stats[]).</summary>
public sealed record PlayerFloorStats(
    ulong PlayerId,
    int CurrentHp,
    int MaxHp,
    int CurrentGold,
    int DamageTaken,
    int HpHealed,
    int MaxHpGained,
    int MaxHpLost,
    int GoldGained,
    int GoldSpent,
    int GoldLost,
    int GoldStolen,
    IReadOnlyList<CardChoice> CardChoices,
    IReadOnlyList<CardInstance> CardsGained,
    IReadOnlyList<CardInstance> CardsRemoved,
    IReadOnlyList<CardTransform> CardsTransformed,
    IReadOnlyList<CardInstance> CardsEnchanted,
    IReadOnlyList<string> UpgradedCards,
    IReadOnlyList<string> DowngradedCards,
    IReadOnlyList<ModelChoice> RelicChoices,
    IReadOnlyList<string> RelicsRemoved,
    IReadOnlyList<ModelChoice> PotionChoices,
    IReadOnlyList<string> PotionsUsed,
    IReadOnlyList<string> PotionsDiscarded,
    IReadOnlyList<string> RestSiteChoices,
    IReadOnlyList<string> BoughtRelics,
    IReadOnlyList<string> BoughtPotions,
    IReadOnlyList<string> BoughtColorless,
    IReadOnlyList<string> EventChoiceKeys,
    IReadOnlyList<AncientOption> AncientChoices,
    IReadOnlyList<string> CompletedQuests)
{
    /// <summary>Cartes proposees (toutes, prises ou non).</summary>
    public IEnumerable<CardInstance> CardsOffered => CardChoices.Select(c => c.Card);

    /// <summary>Cartes choisies parmi l'offre (vide = « passer »).</summary>
    public IEnumerable<CardInstance> CardsPicked => CardChoices.Where(c => c.WasPicked).Select(c => c.Card);

    public IEnumerable<string> RelicsPicked => RelicChoices.Where(c => c.WasPicked).Select(c => c.Id);
}

/// <summary>
/// Un etage deja joue. <c>Floor</c> = index global 1-based a travers les actes (verifie sur les
/// 250 runs : aucune carte gagnee n'a floor_added_to_deck &gt; Floor ; 258 egalites strictes).
/// </summary>
public sealed record FloorRecord(
    int Floor,
    int ActIndex,
    MapPointKind Kind,
    string RawType,
    IReadOnlyList<RoomVisit> Rooms,
    IReadOnlyList<PlayerFloorStats> PlayerStats)
{
    /// <summary>Identifiant de la rencontre/evenement principal (rooms[0].model_id).</summary>
    public string? ModelId => Rooms.Count > 0 ? Rooms[0].ModelId : null;

    public PlayerFloorStats? ForPlayer(ulong playerId) => PlayerStats.FirstOrDefault(p => p.PlayerId == playerId);
}

/// <summary>Etat de la run en cours, tel qu'ecrit par le jeu dans current_run.save.</summary>
public sealed record RunState
{
    public required int SchemaVersion { get; init; }
    public required long StartTime { get; init; }
    public required long SaveTime { get; init; }
    public required long RunTime { get; init; }
    public required int Ascension { get; init; }
    public required string GameMode { get; init; }
    public required int CurrentActIndex { get; init; }
    public required IReadOnlyList<string> Modifiers { get; init; }
    public required IReadOnlyList<PlayerState> Players { get; init; }
    public required IReadOnlyList<ActInfo> Acts { get; init; }
    public required IReadOnlyList<MapCoord> VisitedMapCoords { get; init; }
    public required IReadOnlyList<FloorRecord> Floors { get; init; }

    /// <summary>Fichier lu (principal ou .backup).</summary>
    public string SourcePath { get; init; } = "";
    public bool ReadFromBackup { get; init; }
    /// <summary>Vrai si lu depuis current_run_mp.save (co-op, ecrit par l'hote seulement).</summary>
    public bool IsMultiplayerSave { get; init; }

    public ActInfo? CurrentAct => CurrentActIndex >= 0 && CurrentActIndex < Acts.Count ? Acts[CurrentActIndex] : null;

    /// <summary>Derniere case visitee (= position actuelle), null avant le premier pas.</summary>
    public MapCoord? CurrentCoord => VisitedMapCoords.Count > 0 ? VisitedMapCoords[^1] : null;

    /// <summary>Nombre d'etages deja enregistres.</summary>
    public int FloorCount => Floors.Count;

    /// <summary>
    /// Joueur local : seul joueur en solo ; en co-op, celui dont net_id == SteamID64 du compte
    /// (observe : solo net_id=1, co-op net_id=SteamID64). Null si introuvable.
    /// </summary>
    public PlayerState? LocalPlayer(ulong? steamId64)
    {
        if (Players.Count == 1) return Players[0];
        return steamId64 is null ? null : Players.FirstOrDefault(p => p.NetId == steamId64);
    }
}

public sealed record RunRecordPlayer(
    ulong Id,
    string CharacterId,
    int MaxPotionSlotCount,
    IReadOnlyList<CardInstance> Deck,
    IReadOnlyList<RelicInstance> Relics,
    IReadOnlyList<PotionInstance> Potions);

/// <summary>Run terminee (history/&lt;start_time&gt;.run).</summary>
public sealed record RunRecord
{
    public required string FileName { get; init; }
    public required int SchemaVersion { get; init; }
    public required string BuildId { get; init; }
    public required string GameMode { get; init; }
    public required int Ascension { get; init; }
    public required bool Win { get; init; }
    public required bool WasAbandoned { get; init; }
    /// <summary>Rencontre fatale ; null si « NONE.NONE ».</summary>
    public required string? KilledByEncounter { get; init; }
    /// <summary>Evenement fatal ; null si « NONE.NONE ».</summary>
    public required string? KilledByEvent { get; init; }
    public required long StartTime { get; init; }
    public required long RunTime { get; init; }
    public required IReadOnlyList<string> Acts { get; init; }
    public required IReadOnlyList<string> Modifiers { get; init; }
    public required IReadOnlyList<RunRecordPlayer> Players { get; init; }
    public required IReadOnlyList<FloorRecord> Floors { get; init; }

    public bool IsMultiplayer => Players.Count > 1;

    /// <summary>Personnage du joueur local (voir <see cref="LocalPlayer"/>), sinon du premier joueur.</summary>
    public string Character(ulong? steamId64 = null) => (LocalPlayer(steamId64) ?? Players[0]).CharacterId;

    /// <summary>Solo : unique joueur (id=1). Co-op : id == SteamID64 du compte.</summary>
    public RunRecordPlayer? LocalPlayer(ulong? steamId64)
    {
        if (Players.Count == 1) return Players[0];
        return steamId64 is null ? null : Players.FirstOrDefault(p => p.Id == steamId64);
    }
}

public sealed record FightStats(string Character, int Wins, int Losses);

public sealed record CharacterStats(
    string Id,
    int TotalWins,
    int TotalLosses,
    int MaxAscension,
    int PreferredAscension,
    int BestWinStreak,
    int CurrentStreak,
    long FastestWinTime,
    long Playtime);

public sealed record EncounterStats(string EncounterId, IReadOnlyList<FightStats> ByCharacter);

public sealed record CardStats(string Id, int TimesPicked, int TimesSkipped, int TimesWon, int TimesLost);

/// <summary>Statistiques de progress.save.</summary>
public sealed record ProgressStats(
    int SchemaVersion,
    long TotalPlaytime,
    long FloorsClimbed,
    IReadOnlyList<CharacterStats> Characters,
    IReadOnlyList<EncounterStats> Encounters,
    IReadOnlyList<EncounterStats> Enemies,
    IReadOnlyList<CardStats> Cards,
    IReadOnlyList<EncounterStats> Ancients);
