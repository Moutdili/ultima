using Microsoft.Diagnostics.Runtime;
using static Ultima.Mirror.MirrorSession;
using P = Ultima.Mirror.ObjectPaths;

namespace Ultima.Mirror;

/// <summary>Enchantment on a card: id "ENCHANTMENT.X" + amount (Models/EnchantmentModel.cs:29, DisplayAmount == Amount).</summary>
public sealed record EnchantView(string? Id, int Amount);
/// <summary>A card. <see cref="Enchantment"/> added by miroir 2 (null = none); positional order unchanged.</summary>
public sealed record CardView(string? Id, int Upgrade, EnchantView? Enchantment = null);
public sealed record PlayerView(ulong NetId, string? Character, int Hp, int MaxHp, int Block, int Gold, int MaxEnergy,
    IReadOnlyList<CardView> Deck, IReadOnlyList<string?> Relics, IReadOnlyList<string?> PotionSlots);
public sealed record RunView(bool InRun, ulong? LocalNetId, int AscensionLevel, int ActIndex, string? CurrentRoomType,
    IReadOnlyList<PlayerView> Players);

public sealed record CardRewardView(bool AlreadyTaken, IReadOnlyList<CardView> Offered);
public sealed record RewardScreenView(int OpenRewardSets, IReadOnlyList<CardRewardView> CardRewards);

public sealed record ShopCardView(CardView Card, int BaseCost, int? EstimatedDisplayedCost, bool OnSale, bool Colorless);
public sealed record ShopItemView(string? Id, int BaseCost, int? EstimatedDisplayedCost);
public sealed record ShopView(bool InShop, IReadOnlyList<ShopCardView> Cards, IReadOnlyList<ShopItemView> Relics,
    IReadOnlyList<ShopItemView> Potions, int? RemovalBaseCost, int? RemovalEstimatedCost, bool? RemovalUsed,
    IReadOnlyList<string> PriceModifiers);

public sealed record EnemyView(string? Id, int Hp, int MaxHp, int Block);
public sealed record CombatView(bool InProgress, int Round, int? Energy, IReadOnlyList<CardView> Hand, IReadOnlyList<EnemyView> Enemies);

// ---- miroir 2 : piles de combat (comptées, jamais ordonnées) ----
public sealed record CountedCard(CardView Card, int Count);

/// <summary>Multiset of a pile: <see cref="Cards"/> sorted by card key (id, upgrade, enchantment), never by position.</summary>
public sealed record CountedPile(int Total, IReadOnlyList<CountedCard> Cards)
{
    public static CountedPile From(IEnumerable<CardView> cards)
    {
        var groups = cards
            .GroupBy(c => c) // record equality: id + upgrade + enchantment
            .Select(g => new CountedCard(g.Key, g.Count()))
            .OrderBy(c => c.Card.Id ?? "", StringComparer.Ordinal)
            .ThenBy(c => c.Card.Upgrade)
            .ThenBy(c => c.Card.Enchantment?.Id ?? "", StringComparer.Ordinal)
            .ThenBy(c => c.Card.Enchantment?.Amount ?? 0)
            .ToList();
        return new CountedPile(groups.Sum(g => g.Count), groups);
    }
}

/// <summary>
/// Local player's combat piles. <see cref="CardsBeforeReshuffle"/> = draw pile size (the discard is
/// shuffled back only when a draw finds it empty). Null piles = not in combat.
/// </summary>
public sealed record PilesView(bool InCombat, CountedPile? Hand, CountedPile? Draw, CountedPile? Discard,
    CountedPile? Exhaust, CountedPile? Play, int? CardsBeforeReshuffle);

// ---- miroir 2 : événement courant ----
/// <summary>Localisation key (table + key). Rendering (FR text, SmartFormat variables) belongs to Ultima.Knowledge.</summary>
public sealed record LocKeyView(string? Table, string? Key);

// Shape = what Ultima.Daemon parses (Ultima.Daemon/Mirror/MirrorModels.cs : MEvent / MEventOption,
// camelCase, unknown fields ignored): eventId, name, options[{id, label, description, available}].
// The mirror never renders text (that would need a method call: LocString.GetFormattedText); so the
// display strings name/label/description are ALWAYS null and the localisation keys travel next to
// them (nameKey, titleKey, descriptionKey) for Ultima.Knowledge. Null = « — », never invented text.

/// <summary>
/// One option as displayed (<see cref="Index"/> = button order). <see cref="Id"/> = EventOption.TextKey
/// (e.g. "X.pages.INITIAL.options.ABSTAIN"; the daemon keeps the last segment). <see cref="Available"/> =
/// !Locked (a locked option ignores the click, Nodes/Rooms/NEventRoom.cs:211). Option variables (e.g. HP
/// lost) are NOT read. <see cref="Label"/>/<see cref="Description"/> are always null (see above).
/// </summary>
public sealed record EventOptionMirrorView(int Index, string? Id, string? Label, string? Description, bool Available,
    LocKeyView? TitleKey, LocKeyView? DescriptionKey, bool Locked, bool IsProceed, bool WasChosen, string? RelicId);

/// <summary>
/// Current event of the local player. <see cref="OptionsKnown"/> = false when the room is an event but
/// the local mutable event is not (yet) the room's event: <see cref="Options"/> is then null ("inconnu"),
/// never stale options. <see cref="IsFinished"/> = true → the screen shows a single synthetic « Continuer »
/// button (NEventRoom.cs:193-196) that is not in <see cref="Options"/>.
/// <see cref="NameKey"/> = (table of the page description, "ENTRY.title") per EventModel.cs:66 — table null
/// if unknown. <see cref="Name"/> is always null (see above).
/// </summary>
public sealed record EventMirrorView(bool InEvent, string? EventId, string? Name, IReadOnlyList<EventOptionMirrorView>? Options,
    bool IsAncient, bool OptionsKnown, bool IsFinished, LocKeyView? NameKey, LocKeyView? DescriptionKey);

/// <summary>
/// Follows the paths of <see cref="ObjectPaths"/> from static roots. No heap scan, no stack walk.
/// Every method may throw <see cref="MirrorMissException"/> (layout drift / torn read): callers
/// report "inconnu", never a guessed value.
/// </summary>
public sealed class GameReader(MirrorSession s)
{
    static string K(string p) => $"<{p}>k__BackingField";

    ClrObject RunManager() => s.ReadStaticObject(P.RunManager, K("Instance"));
    ClrObject RunState() => Obj(RunManager(), K("State"));

    public ulong? LocalNetId()
    {
        var v = s.ReadStaticStruct(P.LocalContext, K("NetId"));
        if (v is null) return null;
        ReadGuard.Check("hasValue"); ReadGuard.Check("value");
        return v.Value.ReadField<bool>("hasValue") ? v.Value.ReadField<ulong>("value") : null;
    }

    CardView Card(ClrObject card)
    {
        if (card.IsNull) return new(null, 0);
        var ench = Obj(card, K("Enchantment"));
        return new(ModelIdOf(card), Val<int>(card, "_currentUpgradeLevel"),
            ench.IsNull ? null : new EnchantView(ModelIdOf(ench), Val<int>(ench, "_amount")));
    }

    IReadOnlyList<CardView> Pile(ClrObject pile) => s.List(Obj(pile, "_cards")).Select(Card).ToList();

    public RunView ReadRun()
    {
        var rs = RunState();
        if (rs.IsNull) return new RunView(false, LocalNetId(), 0, 0, null, Array.Empty<PlayerView>());
        var rooms = s.List(Obj(rs, "_currentRooms"));
        var players = s.List(Obj(rs, "_players")).Select(ReadPlayer).ToList();
        return new RunView(true, LocalNetId(), Val<int>(rs, K("AscensionLevel")), Val<int>(rs, "_currentActIndex"),
            rooms.Count > 0 ? rooms[^1].Type?.Name : null, players);
    }

    PlayerView ReadPlayer(ClrObject p)
    {
        var creature = Obj(p, K("Creature"));
        return new PlayerView(
            Val<ulong>(p, K("NetId")),
            ModelIdOf(Obj(p, K("Character"))),
            Val<int>(creature, "_currentHp"), Val<int>(creature, "_maxHp"), Val<int>(creature, "_block"),
            Val<int>(p, "_gold"), Val<int>(p, K("MaxEnergy")),
            Pile(Obj(p, K("Deck"))),
            s.List(Obj(p, "_relics")).Select(ModelIdOf).ToList(),
            s.List(Obj(p, "_potionSlots")).Select(ModelIdOf).ToList());
    }

    ClrObject LocalPlayer(ClrObject rs, out int slot)
    {
        var players = s.List(Obj(rs, "_players"));
        var me = LocalNetId();
        slot = -1;
        for (int i = 0; i < players.Count; i++)
            if (me is null ? players.Count == 1 : Val<ulong>(players[i], K("NetId")) == me) { slot = i; return players[i]; }
        throw new MirrorMissException("joueur local introuvable");
    }

    /// <summary>
    /// RunManager.RewardsSetSynchronizer._rewardStates[slot local].rewardsStack[*].set.Rewards
    /// filtered on CardReward. Exists from the moment the reward screen is generated (before the
    /// player even clicks the card reward).
    /// </summary>
    public RewardScreenView ReadRewards()
    {
        var rm = RunManager();
        var rs = Obj(rm, K("State"));
        if (rs.IsNull) return new RewardScreenView(0, Array.Empty<CardRewardView>());
        var sync = Obj(rm, K("RewardsSetSynchronizer"));
        var localId = Val<ulong>(sync, "_localPlayerId");
        var states = s.List(Obj(sync, "_rewardStates"));
        LocalPlayer(rs, out int slot);
        if (slot < 0 || slot >= states.Count) throw new MirrorMissException($"slot {slot} hors de _rewardStates ({states.Count})");
        var stack = s.List(Obj(states[slot], "rewardsStack"));
        var result = new List<CardRewardView>();
        foreach (var st in stack)
        {
            var set = Obj(st, "set");
            var owner = Obj(set, K("Player"));
            if (!owner.IsNull && Val<ulong>(owner, K("NetId")) != localId)
                throw new MirrorMissException("RewardsSet d'un autre joueur dans la pile locale : incohérent");
            foreach (var r in s.List(Obj(set, K("Rewards"))))
            {
                if (r.Type?.Name != P.CardReward) continue;
                var cards = s.List(Obj(r, "_cards")).Select(cr =>
                {
                    var mod = Obj(cr, "_modifiedCard");
                    return Card(mod.IsNull ? Obj(cr, "originalCard") : mod);
                }).ToList();
                result.Add(new CardRewardView(Val<bool>(r, K("SuccessfullySelected")), cards));
            }
        }
        return new RewardScreenView(stack.Count, result);
    }

    /// <summary>RunState._currentRooms[^1] is MerchantRoom → Inventories[local].</summary>
    public ShopView ReadShop()
    {
        var empty = new ShopView(false, [], [], [], null, null, null, []);
        var rs = RunState();
        if (rs.IsNull) return empty;
        var rooms = s.List(Obj(rs, "_currentRooms"));
        if (rooms.Count == 0 || rooms[^1].Type?.Name != P.MerchantRoom) return empty;
        var me = LocalPlayer(rs, out _);
        var myId = Val<ulong>(me, K("NetId"));
        var inv = s.List(Obj(rooms[^1], K("Inventories")))
            .FirstOrDefault(i => Val<ulong>(Obj(i, K("Player")), K("NetId")) == myId);
        if (inv.IsNull) throw new MirrorMissException("inventaire du joueur local absent");

        // Displayed price = (int)(_cost × Π modifiers) — MerchantEntry.cs:26, MembershipCard.cs:18, TheCourier.cs:19.
        var relics = s.List(Obj(me, "_relics")).Select(ModelIdOf).ToList();
        var mods = new List<string>();
        decimal factor = 1m;
        if (relics.Contains("RELIC.MEMBERSHIP_CARD")) { factor *= 0.5m; mods.Add("RELIC.MEMBERSHIP_CARD ×0.5"); }
        if (relics.Contains("RELIC.THE_COURIER")) { factor *= 0.8m; mods.Add("RELIC.THE_COURIER ×0.8"); }
        int Est(int c) => (int)(c * factor);

        var cards = new List<ShopCardView>();
        foreach (var (field, colorless) in new[] { ("_characterCardEntries", false), ("_colorlessCardEntries", true) })
            foreach (var e in s.List(Obj(inv, field)))
            {
                var cr = Obj(e, K("CreationResult"));
                if (cr.IsNull) continue; // sold
                var mod = Obj(cr, "_modifiedCard");
                int cost = Val<int>(e, "_cost");
                cards.Add(new ShopCardView(Card(mod.IsNull ? Obj(cr, "originalCard") : mod), cost, Est(cost),
                    Val<bool>(e, K("IsOnSale")), colorless));
            }
        IReadOnlyList<ShopItemView> Items(string field) => s.List(Obj(inv, field))
            .Where(e => !Obj(e, K("Model")).IsNull)
            .Select(e => { int c = Val<int>(e, "_cost"); return new ShopItemView(ModelIdOf(Obj(e, K("Model"))), c, Est(c)); })
            .ToList();
        var removal = Obj(inv, K("CardRemovalEntry"));
        int? rc = removal.IsNull ? null : Val<int>(removal, "_cost");
        return new ShopView(true, cards, Items("_relicEntries"), Items("_potionEntries"),
            rc, rc is null ? null : Est(rc.Value), removal.IsNull ? null : Val<bool>(removal, K("Used")), mods);
    }

    public CombatView ReadCombat()
    {
        var cm = s.ReadStaticObject(P.CombatManager, K("Instance"));
        bool inProgress = !cm.IsNull && Val<bool>(cm, K("IsInProgress"));
        var cs = Obj(cm, "_state");
        if (cs.IsNull) return new CombatView(inProgress, 0, null, [], []);
        var enemies = s.List(Obj(cs, "_enemies")).Select(c => new EnemyView(
            ModelIdOf(Obj(c, K("Monster"))), Val<int>(c, "_currentHp"), Val<int>(c, "_maxHp"), Val<int>(c, "_block"))).ToList();
        int? energy = null;
        IReadOnlyList<CardView> hand = [];
        var rs = RunState();
        if (!rs.IsNull)
        {
            var pcs = Obj(LocalPlayer(rs, out _), K("PlayerCombatState"));
            if (!pcs.IsNull) { energy = Val<int>(pcs, "_energy"); hand = Pile(Obj(pcs, K("Hand"))); }
        }
        return new CombatView(inProgress, Val<int>(cs, K("RoundNumber")), energy, hand, enemies);
    }

    /// <summary>
    /// Piles of the local player, COUNTED. Hand/discard/exhaust/play via the whitelist; the draw pile
    /// only via <see cref="MirrorSession.ReadUnorderedPile"/> (order erased inside the library).
    /// Called by the probe CLI by reflection (`--sections piles`, Ultima.Mirror.Probe/Program.cs).
    /// </summary>
    public PilesView ReadPiles()
    {
        var none = new PilesView(false, null, null, null, null, null, null);
        var rs = RunState();
        if (rs.IsNull) return none;
        var pcs = Obj(LocalPlayer(rs, out _), K("PlayerCombatState"));
        if (pcs.IsNull) return none;
        CountedPile Counted(string field) => CountedPile.From(Pile(Obj(pcs, K(field))));
        var draw = s.ReadUnorderedPile(pcs, K("DrawPile"), Card);
        return new PilesView(true, Counted("Hand"), draw, Counted("DiscardPile"), Counted("ExhaustPile"), Counted("PlayPile"),
            draw.Total);
    }

    static LocKeyView? Loc(ClrObject locString) =>
        locString.IsNull ? null : new LocKeyView(Str(locString, "<locTable>P"), Str(locString, "<locEntryKey>P"));

    static bool DerivesFrom(ClrObject o, string typeName)
    {
        for (var t = o.Type; t is not null; t = t.BaseType) if (t.Name == typeName) return true;
        return false;
    }

    /// <summary>
    /// RunState._currentRooms[^1] is EventRoom → RunManager.EventSynchronizer._events[slot local],
    /// cross-checked: Owner.NetId == local AND event id == room's CanonicalEvent id. Mismatch (event not
    /// begun yet, or previous event still in the list) → OptionsKnown = false, Options = null.
    /// Called by the probe CLI by reflection (`--sections event`, Ultima.Mirror.Probe/Program.cs).
    /// </summary>
    public EventMirrorView ReadEvent()
    {
        var none = new EventMirrorView(false, null, null, null, false, false, false, null, null);
        var rm = RunManager();
        var rs = Obj(rm, K("State"));
        if (rs.IsNull) return none;
        var rooms = s.List(Obj(rs, "_currentRooms"));
        if (rooms.Count == 0 || rooms[^1].Type?.Name != P.EventRoom) return none;
        var roomEventId = ModelIdOf(Obj(rooms[^1], K("CanonicalEvent")));
        if (roomEventId is null) throw new MirrorMissException("EventRoom sans CanonicalEvent");
        var unknown = none with { InEvent = true, EventId = roomEventId };

        var me = LocalPlayer(rs, out int slot);
        var myId = Val<ulong>(me, K("NetId"));
        var sync = Obj(rm, K("EventSynchronizer"));
        if (sync.IsNull) return unknown;
        if (Val<ulong>(sync, "_localPlayerId") != myId) throw new MirrorMissException("EventSynchronizer : joueur local incohérent");
        var events = s.List(Obj(sync, "_events"));
        if (slot < 0 || slot >= events.Count) return unknown;
        var ev = events[slot];
        var owner = Obj(ev, K("Owner"));
        if (ev.IsNull || owner.IsNull || Val<ulong>(owner, K("NetId")) != myId || ModelIdOf(ev) != roomEventId) return unknown;

        var desc = Loc(Obj(ev, K("Description")));
        var entry = roomEventId[(roomEventId.IndexOf('.') + 1)..];
        var options = s.List(Obj(ev, "_currentOptions")).Select((o, i) =>
        {
            bool locked = Val<bool>(o, K("IsLocked"));
            return new EventOptionMirrorView(i, Str(o, K("TextKey")), null, null, !locked,
                Loc(Obj(o, K("Title"))), Loc(Obj(o, K("Description"))),
                locked, Val<bool>(o, K("IsProceed")), Val<bool>(o, K("WasChosen")), ModelIdOf(Obj(o, K("Relic"))));
        }).ToList();
        return new EventMirrorView(true, roomEventId, null, options, DerivesFrom(ev, P.AncientEventModel), true,
            Val<bool>(ev, "_isFinished"), new LocKeyView(desc?.Table, entry + ".title"), desc);
    }

    /// <summary>Count live+dead instances of a type by walking the GC heap (diagnostic only, slow).</summary>
    public (int count, bool complete) CountObjects(string typeName, int maxSeconds = 10)
    {
        var heap = s.Runtime.Heap;
        if (!heap.CanWalkHeap) return (0, false);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int n = 0;
        foreach (var o in heap.EnumerateObjects())
        {
            if (o.Type?.Name == typeName) n++;
            if (sw.Elapsed.TotalSeconds > maxSeconds) return (n, false);
        }
        return (n, true);
    }
}
