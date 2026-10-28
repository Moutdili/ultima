using Ultima.Saves.Model;

namespace Ultima.Saves.Dto;

/// <summary>DTO -> modele public. Tolerant : listes absentes => vides (le jeu omet les listes vides).</summary>
internal static class Mapper
{
    internal const string None = "NONE.NONE";

    private static IReadOnlyList<TOut> Map<TIn, TOut>(List<TIn>? src, Func<TIn, TOut?> f) where TOut : class
    {
        if (src is null || src.Count == 0) return Array.Empty<TOut>();
        var r = new List<TOut>(src.Count);
        foreach (var x in src)
        {
            var y = f(x);
            if (y is not null) r.Add(y);
        }
        return r;
    }

    private static IReadOnlyList<string> Strings(List<string>? src) =>
        src is null || src.Count == 0 ? Array.Empty<string>() : src.ToArray();

    internal static string? NullIfNone(string? s) => string.IsNullOrEmpty(s) || s == None ? null : s;

    internal static CardInstance? Card(CardDto? c) =>
        c?.Id is null ? null : new CardInstance(c.Id, c.CurrentUpgradeLevel,
            c.Enchantment?.Id is null ? null : new Enchantment(c.Enchantment.Id, c.Enchantment.Amount),
            c.FloorAddedToDeck);

    internal static IReadOnlyList<CardInstance> Cards(List<CardDto>? l) => Map(l, Card);

    internal static IReadOnlyList<RelicInstance> Relics(List<RelicDto>? l) =>
        Map(l, r => r.Id is null ? null : new RelicInstance(r.Id, r.FloorAddedToDeck));

    internal static IReadOnlyList<PotionInstance> Potions(List<PotionDto>? l) =>
        Map(l, p => p.Id is null ? null : new PotionInstance(p.Id, p.SlotIndex));

    internal static MapCoord Coord(CoordDto c) => new(c.Col, c.Row);

    internal static IReadOnlyList<MapCoord> Coords(List<CoordDto>? l)
    {
        if (l is null || l.Count == 0) return Array.Empty<MapCoord>();
        return l.Select(Coord).ToArray();
    }

    private static IReadOnlyList<ModelChoice> Choices(List<ModelChoiceDto>? l) =>
        Map(l, c => c.Choice is null ? null : new ModelChoice(c.Choice, c.WasPicked));

    internal static PlayerFloorStats Stats(PlayerFloorStatsDto s) => new(
        s.PlayerId, s.CurrentHp, s.MaxHp, s.CurrentGold, s.DamageTaken, s.HpHealed,
        s.MaxHpGained, s.MaxHpLost, s.GoldGained, s.GoldSpent, s.GoldLost, s.GoldStolen,
        Map(s.CardChoices, c => Card(c.Card) is { } card ? new CardChoice(card, c.WasPicked) : null),
        Cards(s.CardsGained),
        Cards(s.CardsRemoved),
        Map(s.CardsTransformed, t => Card(t.OriginalCard) is { } o && Card(t.FinalCard) is { } f
            ? new CardTransform(o, f) : null),
        Map(s.CardsEnchanted, e => Card(e.Card)),
        Strings(s.UpgradedCards),
        Strings(s.DowngradedCards),
        Choices(s.RelicChoices),
        Strings(s.RelicsRemoved),
        Choices(s.PotionChoices),
        Strings(s.PotionUsed),
        Strings(s.PotionDiscarded),
        Strings(s.RestSiteChoices),
        Strings(s.BoughtRelics),
        Strings(s.BoughtPotions),
        Strings(s.BoughtColorless),
        Map(s.EventChoices, e => e.Title?.Key),
        Map(s.AncientChoice, a => a.TextKey is null ? null : new AncientOption(a.TextKey, a.WasChosen)),
        Strings(s.CompletedQuests));

    internal static IReadOnlyList<FloorRecord> Floors(List<List<FloorDto>>? history)
    {
        if (history is null) return Array.Empty<FloorRecord>();
        var r = new List<FloorRecord>();
        int floor = 0;
        for (int act = 0; act < history.Count; act++)
        {
            foreach (var f in history[act] ?? new List<FloorDto>())
            {
                floor++;
                r.Add(new FloorRecord(
                    floor, act, MapPointKinds.Parse(f.MapPointType), f.MapPointType ?? "",
                    Map(f.Rooms, x => new RoomVisit(x.RoomType ?? "", x.ModelId,
                        Strings(x.MonsterIds), x.TurnsTaken)),
                    Map(f.PlayerStats, s => Stats(s))));
            }
        }
        return r;
    }

    private static MapNode Node(MapPointDto p) => new(
        p.Coord is null ? default : Coord(p.Coord),
        MapPointKinds.Parse(p.Type), p.Type ?? "", Coords(p.Children));

    internal static ActMap? ActMapOf(SavedMapDto? m)
    {
        if (m?.Start is null || m.Boss is null) return null;
        return new ActMap(m.Width, m.Height, Node(m.Start), Node(m.Boss),
            m.SecondBoss is null ? null : Node(m.SecondBoss),
            Coords(m.StartCoords),
            Map(m.Points, p => p.Coord is null ? null : Node(p)));
    }

    /// <summary>
    /// Les actes FUTURS (index &gt; acte courant) ont deja boss et ancien tires dans la save, mais le
    /// joueur ne les voit pas encore en jeu : identifiant d'acte (variante), boss, ancien et carte sont masques (null).
    /// </summary>
    internal static ActInfo Act(ActDto a, int index, int currentActIndex)
    {
        bool revealed = index <= currentActIndex;
        return new ActInfo(index, revealed ? a.Id ?? "" : null,
            revealed ? NullIfNone(a.Rooms?.BossId) : null,
            revealed ? NullIfNone(a.Rooms?.SecondBossId) : null,
            revealed ? NullIfNone(a.Rooms?.AncientId) : null,
            revealed ? ActMapOf(a.SavedMap) : null,
            revealed);
    }

    internal static IReadOnlyList<string> Modifiers(List<ModifierDto>? l) => Map(l, m => m.Id);

    internal static RunState Run(RunSaveDto d, string path, bool fromBackup, bool mp) => new()
    {
        SchemaVersion = d.SchemaVersion,
        StartTime = d.StartTime,
        SaveTime = d.SaveTime,
        RunTime = d.RunTime,
        Ascension = d.Ascension,
        GameMode = d.GameMode ?? "",
        CurrentActIndex = d.CurrentActIndex,
        Modifiers = Modifiers(d.Modifiers),
        Players = Map(d.Players, p => new PlayerState(
            p.NetId, p.CharacterId ?? "", p.CurrentHp, p.MaxHp, p.Gold, p.MaxEnergy,
            p.MaxPotionSlotCount, p.BaseOrbSlotCount, Cards(p.Deck), Relics(p.Relics), Potions(p.Potions))),
        Acts = (d.Acts ?? new List<ActDto>()).Select((a, i) => Act(a, i, d.CurrentActIndex)).ToArray(),
        VisitedMapCoords = Coords(d.VisitedMapCoords),
        Floors = Floors(d.MapPointHistory),
        SourcePath = path,
        ReadFromBackup = fromBackup,
        IsMultiplayerSave = mp,
    };

    internal static RunRecord History(RunHistoryDto d, string fileName) => new()
    {
        FileName = fileName,
        SchemaVersion = d.SchemaVersion,
        BuildId = d.BuildId ?? "",
        GameMode = d.GameMode ?? "",
        Ascension = d.Ascension,
        Win = d.Win,
        WasAbandoned = d.WasAbandoned,
        KilledByEncounter = NullIfNone(d.KilledByEncounter),
        KilledByEvent = NullIfNone(d.KilledByEvent),
        StartTime = d.StartTime,
        RunTime = d.RunTime,
        Acts = Strings(d.Acts),
        Modifiers = Modifiers(d.Modifiers),
        Players = Map(d.Players, p => new RunRecordPlayer(p.Id, p.Character ?? "", p.MaxPotionSlotCount,
            Cards(p.Deck), Relics(p.Relics), Potions(p.Potions))),
        Floors = Floors(d.MapPointHistory),
    };

    private static IReadOnlyList<FightStats> Fights(List<FightStatsDto>? l) =>
        Map(l, f => f.Character is null ? null : new FightStats(f.Character, f.Wins, f.Losses));

    internal static ProgressStats Progress(ProgressDto d) => new(
        d.SchemaVersion, d.TotalPlaytime, d.FloorsClimbed,
        Map(d.CharacterStats, c => c.Id is null ? null : new CharacterStats(c.Id, c.TotalWins, c.TotalLosses,
            c.MaxAscension, c.PreferredAscension, c.BestWinStreak, c.CurrentStreak, c.FastestWinTime, c.Playtime)),
        Map(d.EncounterStats, e => e.EncounterId is null ? null : new EncounterStats(e.EncounterId, Fights(e.FightStats))),
        Map(d.EnemyStats, e => e.EnemyId is null ? null : new EncounterStats(e.EnemyId, Fights(e.FightStats))),
        Map(d.CardStats, c => c.Id is null ? null : new CardStats(c.Id, c.TimesPicked, c.TimesSkipped, c.TimesWon, c.TimesLost)),
        Map(d.AncientStats, a => a.AncientId is null ? null : new EncounterStats(a.AncientId, Fights(a.CharacterStats))));
}
