// Sts2Shape.TestTarget — a SMALL .NET 9 process that reproduces the SHAPE (namespaces, type names,
// field names, auto-property backing fields, List<T> containers) of the sts2.dll objects listed
// in Ultima.Mirror/ObjectPaths.cs, so `ultima-mirror probe` can be exercised end-to-end without
// the game. It proves the probe code + ClrMD non-suspending attach; it proves NOTHING about the
// real game's values. All ids end in _TEST to make that obvious.
//
// It also holds "forbidden" data (PlayerRng, RelicGrabBag, RunState.Rng, PlayerCombatState._piles,
// EventModel.Rng, EventSynchronizer._multiplayerOptionSelectionRng) containing the marker
// SECRET_MUST_NOT_LEAK: the probe output must never contain it.
// The draw pile CONTENT is readable (counted, like the game's own sorted pile view) but its ORDER is
// secret: a background thread reshuffles it every 5 ms, so any reader that leaked the order would
// never get two identical reads (Stable.Read → INCONNU).
//
// Scenes (miroir 2): --scene shop (default, top room = MerchantRoom), event, ancient, event-stale.
// Live mutation + GC churn run in the background to stress non-suspended reads.

using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

var mutate = !args.Contains("--frozen");
var scene = args.SkipWhile(a => a != "--scene").Skip(1).FirstOrDefault() ?? "shop";
LocalContext.NetId = 1UL;

var player = new Player(1UL, new TestCharacter("CHARACTER", "SILENT_TEST"), hp: 61, maxHp: 70, gold: 99);
for (int i = 0; i < 5; i++) player.Deck.Add(new TestCard("STRIKE_TEST", 0));
for (int i = 0; i < 4; i++) player.Deck.Add(new TestCard("DEFEND_TEST", 0));
player.Deck.Add(new TestCard("NEUTRALIZE_TEST", 1));
player.Deck.Add(new TestCard("DEFEND_TEST", 0, new TestEnchantment("SOWN_TEST", 1)));
player.AddRelic(new TestRelic("RING_OF_THE_SNAKE_TEST"));
player.AddRelic(new TestRelic("MEMBERSHIP_CARD"));      // real id on purpose: exercises the price estimate
player.AddPotionSlot(new TestPotion("FIRE_POTION_TEST"));
player.AddPotionSlot(null);
player.AddPotionSlot(null);

var run = new RunState(ascension: 3, actIndex: 1);
run.AddPlayer(player);
RunManager.Instance.Start(run);

// Card reward screen: one gold reward + one card reward (3 cards, the 3rd modified by a relic).
var set = new RewardsSet(player);
set.Rewards.Add(new GoldReward(player));
var cr = new CardReward(player);
cr.AddOption(new CardCreationResult(new TestCard("BACKFLIP_TEST", 0)));
cr.AddOption(new CardCreationResult(new TestCard("DAGGER_THROW_TEST", 0)));
var modified = new CardCreationResult(new TestCard("PREPARED_TEST", 0));
modified.Modify(new TestCard("PREPARED_TEST", 1, new TestEnchantment("SWIFT_TEST", 2)));
cr.AddOption(modified);
set.Rewards.Add(cr);
RunManager.Instance.RewardsSetSynchronizer.BeginRewardsSet(set);

// Shop on top of the room stack.
var shop = new MerchantRoom();
var inv = new MerchantInventory(player);
inv.AddCharacterCard(new TestCard("ACROBATICS_TEST", 0), 76, onSale: false);
inv.AddCharacterCard(new TestCard("FOOTWORK_TEST", 0, new TestEnchantment("SHARP_TEST", 3)), 25, onSale: true);
inv.AddCharacterCard(null, 150, onSale: false);                       // sold
inv.AddColorlessCard(new TestCard("APOTHEOSIS_TEST", 0), 172);
inv.AddRelic(new TestRelic("VAJRA_TEST"), 147);
inv.AddPotion(new TestPotion("BLOCK_POTION_TEST"), 52);
inv.SetRemoval(75, used: false);
shop.Inventories.Add(inv);
run.PushRoom(shop);

// Event scenes (miroir 2): an EventRoom on top of the stack + the synchronizer's per-player events.
EventModel? MakeEvent(EventModel e, params EventOption[] options)
{
    e.Begin(player, new LocString(e.LocTable, e.Id.Entry + ".pages.INITIAL.description"), options);
    return e;
}
switch (scene)
{
    case "event":
    {
        var ev = new TestEvent("BATHS_TEST");
        MakeEvent(ev,
            new EventOption(ev, "BATHS_TEST.pages.INITIAL.options.IMMERSE", locked: false),
            new EventOption(ev, "BATHS_TEST.pages.INITIAL.options.ABSTAIN", locked: false),
            new EventOption(ev, "BATHS_TEST.pages.INITIAL.options.LOCKED_CHOICE", locked: true));
        run.PushRoom(new EventRoom(new TestEvent("BATHS_TEST")));
        RunManager.Instance.EventSynchronizer.BeginEvent(ev);
        break;
    }
    case "ancient":
    {
        var ev = new TestAncientEvent("NEOW_TEST");
        MakeEvent(ev,
            new EventOption(ev, "NEOW_TEST.pages.INITIAL.options.RELIC_A", locked: false, relic: new TestRelic("LANTERN_TEST")),
            new EventOption(ev, "NEOW_TEST.pages.INITIAL.options.RELIC_B", locked: false, relic: new TestRelic("BAG_TEST")));
        run.PushRoom(new EventRoom(new TestAncientEvent("NEOW_TEST")));
        RunManager.Instance.EventSynchronizer.BeginEvent(ev);
        break;
    }
    case "event-stale":
    {
        // The room is BATHS_TEST but the synchronizer still holds the PREVIOUS event (not begun yet):
        // the mirror must answer OptionsKnown=false, never the stale options.
        var old = new TestEvent("OLD_EVENT_TEST");
        MakeEvent(old, new EventOption(old, "OLD_EVENT_TEST.pages.INITIAL.options.STALE", locked: false));
        run.PushRoom(new EventRoom(new TestEvent("BATHS_TEST")));
        RunManager.Instance.EventSynchronizer.BeginEvent(old);
        break;
    }
}

// Combat.
var cs = new CombatState(round: 2);
cs.AddEnemy(new Creature(new TestMonster("JAW_WORM_TEST"), 30, 44, 6));
player.StartCombat(energy: 3, hand: new[] { new TestCard("STRIKE_TEST", 0), new TestCard("SURVIVOR_TEST", 0) });
var pcs = player.PlayerCombatState!;
foreach (var c in new CardModel[] { new TestCard("DEFEND_TEST", 0), new TestCard("STRIKE_TEST", 0), new TestCard("NEUTRALIZE_TEST", 1),
             new TestCard("STRIKE_TEST", 0), new TestCard("DEFEND_TEST", 0, new TestEnchantment("SOWN_TEST", 1)), new TestCard("ZAP_TEST", 0) })
    pcs.DrawPile.Add(c);
pcs.DiscardPile.Add(new TestCard("STRIKE_TEST", 0));
pcs.DiscardPile.Add(new TestCard("DEFEND_TEST", 0));
pcs.ExhaustPile.Add(new TestCard("SLIMED_TEST", 0));
CombatManager.Instance.Begin(cs);

Console.WriteLine($"TESTTARGET pid={Environment.ProcessId} runtime={System.Runtime.InteropServices.RuntimeEnvironment.GetSystemVersion()} mutate={mutate} scene={scene}");
Console.WriteLine("EXPECT run: ascension=3 act=1 hp=61/70 gold>=99 deck=11 (1 enchantée SOWN_TEST 1) relics=2 potions=[FIRE_POTION_TEST,null,null]");
Console.WriteLine("EXPECT reward: BACKFLIP_TEST, DAGGER_THROW_TEST, PREPARED_TEST+1 [ENCHANTMENT.SWIFT_TEST 2]");
Console.WriteLine(scene == "shop"
    ? "EXPECT shop: ACROBATICS 76(→38), FOOTWORK [ENCHANTMENT.SHARP_TEST 3] 25 sale(→12), APOTHEOSIS 172(→86), VAJRA 147(→73), BLOCK_POTION 52(→26), removal 75(→37)"
    : "EXPECT shop: InShop=false (top room is an EventRoom)");
Console.WriteLine(scene switch
{
    "event" => "EXPECT event: EVENT.BATHS_TEST, OptionsKnown=true, options IMMERSE(avail) ABSTAIN(avail) LOCKED_CHOICE(locked)",
    "ancient" => "EXPECT event: EVENT.NEOW_TEST IsAncient=true, Name table=ancients, options RELIC_A→RELIC.LANTERN_TEST RELIC_B→RELIC.BAG_TEST",
    "event-stale" => "EXPECT event: EVENT.BATHS_TEST, OptionsKnown=false, Options=null (stale OLD_EVENT_TEST never reported)",
    _ => "EXPECT event: InEvent=false",
});
Console.WriteLine("EXPECT piles: hand 2, draw 6 = DEFEND×1 + DEFEND[SOWN_TEST 1]×1 + NEUTRALIZE+1×1 + STRIKE×2 + ZAP×1 (sorted, order reshuffled every 5 ms), discard 2, exhaust 1 (SLIMED), play 0, before reshuffle 6");
Console.WriteLine("EXPECT combat: round=2 energy=3 hand=[STRIKE_TEST,SURVIVOR_TEST] enemy JAW_WORM_TEST 30/44 block 6");
Console.Out.Flush();

if (mutate)
{
    // Draw ORDER is the secret: reshuffle it continuously (content unchanged).
    new Thread(() => { var rnd = new Random(7); while (true) { pcs.DrawPile.ShuffleInPlace(rnd); Thread.Sleep(5); } }) { IsBackground = true }.Start();
    // --show-order: the TARGET prints its own (secret) draw order, as a witness that it keeps changing.
    if (args.Contains("--show-order"))
        new Thread(() => { while (true) { Console.WriteLine("TARGET draw order: " + pcs.DrawPile.Describe()); Console.Out.Flush(); Thread.Sleep(1000); } }) { IsBackground = true }.Start();
    // Gold ticks up every 250 ms (live value); churn allocates + forces compacting GCs so objects MOVE.
    new Thread(() => { while (true) { player.Gold++; Thread.Sleep(250); } }) { IsBackground = true }.Start();
    new Thread(() =>
    {
        var keep = new List<byte[]>();
        var rnd = new Random(1);
        while (true)
        {
            for (int i = 0; i < 2000; i++) keep.Add(new byte[rnd.Next(16, 512)]);
            if (keep.Count > 50_000) keep.RemoveRange(0, 25_000);
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            Thread.Sleep(20);
        }
    }) { IsBackground = true }.Start();
}
Thread.Sleep(Timeout.Infinite);

// ---------------------------------------------------------------------------------------------
namespace MegaCrit.Sts2.Core.Models
{
    public record ModelId
    {
        public string Category { get; }
        public string Entry { get; }
        public ModelId(string category, string entry) { Category = category; Entry = entry; }
    }
    public abstract class AbstractModel
    {
        public ModelId Id { get; }
        protected AbstractModel(string category, string entry) { Id = new ModelId(category, entry); }
    }
    public abstract class CardModel : AbstractModel
    {
        private int _currentUpgradeLevel;
        public EnchantmentModel? Enchantment { get; private set; }
        protected CardModel(string entry, int upgrade, EnchantmentModel? ench) : base("CARD", entry)
        { _currentUpgradeLevel = upgrade; Enchantment = ench; }
    }
    public abstract class EnchantmentModel : AbstractModel
    {
        private int _amount;
        protected EnchantmentModel(string entry, int amount) : base("ENCHANTMENT", entry) { _amount = amount; }
    }
    public sealed class TestEnchantment(string entry, int amount) : EnchantmentModel(entry, amount);
    public abstract class RelicModel(string entry) : AbstractModel("RELIC", entry);
    public abstract class PotionModel(string entry) : AbstractModel("POTION", entry);
    public abstract class CharacterModel(string c, string e) : AbstractModel(c, e);
    public abstract class MonsterModel(string entry) : AbstractModel("MONSTER", entry);
    public sealed class TestCard(string entry, int upgrade, EnchantmentModel? ench = null) : CardModel(entry, upgrade, ench);
    public sealed class TestRelic(string entry) : RelicModel(entry);
    public sealed class TestPotion(string entry) : PotionModel(entry);
    public sealed class TestCharacter(string c, string e) : CharacterModel(c, e);
    public sealed class TestMonster(string entry) : MonsterModel(entry);

    public abstract class EventModel : AbstractModel
    {
        private List<MegaCrit.Sts2.Core.Events.EventOption>? _currentOptions;
        private bool _isFinished;
        public MegaCrit.Sts2.Core.Entities.Players.Player? Owner { get; private set; }
        public MegaCrit.Sts2.Core.Localization.LocString? Description { get; private set; }
        public string Rng { get; private set; } = "SECRET_MUST_NOT_LEAK event rng";   // forbidden
        public virtual string LocTable => "events";
        protected EventModel(string entry) : base("EVENT", entry) { }
        public void Begin(MegaCrit.Sts2.Core.Entities.Players.Player owner, MegaCrit.Sts2.Core.Localization.LocString description,
            IEnumerable<MegaCrit.Sts2.Core.Events.EventOption> options)
        {
            Owner = owner; Description = description; _currentOptions = options.ToList(); _isFinished = _currentOptions.Count == 0;
        }
    }
    public abstract class AncientEventModel(string entry) : EventModel(entry)
    {
        public override string LocTable => "ancients";
    }
    public sealed class TestEvent(string entry) : EventModel(entry);
    public sealed class TestAncientEvent(string entry) : AncientEventModel(entry);
}

namespace MegaCrit.Sts2.Core.Localization
{
    // Same primary-constructor shape as the game: the compiler emits <locTable>P / <locEntryKey>P.
    public class LocString(string locTable, string locEntryKey)
    {
        private readonly Dictionary<string, object> _variables = new();
        public string LocTable => locTable;
        public string LocEntryKey => locEntryKey;
        public int VariableCount => _variables.Count;
    }
}

namespace MegaCrit.Sts2.Core.Events
{
    using MegaCrit.Sts2.Core.Localization;
    using MegaCrit.Sts2.Core.Models;
    public class EventOption
    {
        public string TextKey { get; private set; }
        public LocString Title { get; private set; }
        public LocString Description { get; private set; }
        public bool IsLocked { get; }
        public bool IsProceed { get; private set; }
        public bool WasChosen { get; private set; }
        public RelicModel? Relic { get; private set; }
        public EventOption(EventModel ev, string textKey, bool locked, RelicModel? relic = null)
        {
            TextKey = textKey;
            Title = new LocString(relic is null ? ev.LocTable : "relics", relic is null ? textKey + ".title" : relic.Id.Entry + ".title");
            Description = new LocString(ev.LocTable, textKey + ".description");
            IsLocked = locked;
            Relic = relic;
        }
    }
}

namespace MegaCrit.Sts2.Core.Context
{
    public static class LocalContext { public static ulong? NetId { get; set; } }
}

namespace MegaCrit.Sts2.Core.Entities.Cards
{
    using MegaCrit.Sts2.Core.Models;
    public class CardPile
    {
        private readonly List<CardModel> _cards = new();
        public void Add(CardModel c) => _cards.Add(c);
        public string Describe() { lock (_cards) return string.Join(",", _cards.Select(c => c.Id.Entry)); }
        /// In-place Fisher-Yates on the backing list (content unchanged, order changes).
        public void ShuffleInPlace(Random rnd)
        {
            lock (_cards) // target-internal lock (the mirror never takes it: it only reads memory)
                for (int i = _cards.Count - 1; i > 0; i--)
                {
                    int j = rnd.Next(i + 1);
                    (_cards[i], _cards[j]) = (_cards[j], _cards[i]);
                }
        }
    }
    public class CardCreationResult(CardModel originalCard)
    {
        public readonly CardModel originalCard = originalCard;
        private CardModel? _modifiedCard;
        public void Modify(CardModel c) => _modifiedCard = c;
    }
}

namespace MegaCrit.Sts2.Core.Entities.Creatures
{
    using MegaCrit.Sts2.Core.Models;
    public class Creature
    {
        private int _block;
        private int _currentHp;
        private int _maxHp;
        public MonsterModel? Monster { get; }
        public Creature(MonsterModel? m, int hp, int maxHp, int block) { Monster = m; _currentHp = hp; _maxHp = maxHp; _block = block; }
    }
}

namespace MegaCrit.Sts2.Core.Entities.Players
{
    using MegaCrit.Sts2.Core.Entities.Cards;
    using MegaCrit.Sts2.Core.Entities.Creatures;
    using MegaCrit.Sts2.Core.Models;
    public class Player
    {
        private readonly List<RelicModel> _relics = new();
        private readonly List<PotionModel?> _potionSlots = new();
        private int _gold;
        public CharacterModel Character { get; }
        public Creature Creature { get; }
        public ulong NetId { get; }
        public CardPile Deck { get; } = new();
        public int MaxEnergy { get; set; } = 3;
        public PlayerCombatState? PlayerCombatState { get; private set; }
        // forbidden data: must never be read by the probe
        public string PlayerRng { get; } = "SECRET_MUST_NOT_LEAK rng";
        public List<string> RelicGrabBag { get; } = new() { "SECRET_MUST_NOT_LEAK grabbag" };
        public Player(ulong netId, CharacterModel ch, int hp, int maxHp, int gold)
        { NetId = netId; Character = ch; Creature = new Creature(null, hp, maxHp, 0); _gold = gold; }
        public int Gold { get => _gold; set => _gold = value; }
        public void AddRelic(RelicModel r) => _relics.Add(r);
        public void AddPotionSlot(PotionModel? p) => _potionSlots.Add(p);
        public void StartCombat(int energy, IEnumerable<CardModel> hand) => PlayerCombatState = new PlayerCombatState(energy, hand);
    }
    public class PlayerCombatState
    {
        private CardPile[]? _piles;                    // forbidden (game: lazily cached AllPiles)
        private int _energy;
        public CardPile Hand { get; } = new();
        public CardPile DrawPile { get; } = new();     // content readable COUNTED only; ORDER is hidden
        public CardPile DiscardPile { get; } = new();
        public CardPile ExhaustPile { get; } = new();
        public CardPile PlayPile { get; } = new();
        public PlayerCombatState(int energy, IEnumerable<CardModel> hand)
        {
            _energy = energy;
            foreach (var c in hand) Hand.Add(c);
            var secret = new CardPile();
            secret.Add(new TestCard("SECRET_MUST_NOT_LEAK_PILES", 0));
            _piles = new[] { secret };
        }
    }
}

namespace MegaCrit.Sts2.Core.Rooms
{
    using MegaCrit.Sts2.Core.Entities.Merchant;
    public abstract class AbstractRoom;
    public class EventRoom(MegaCrit.Sts2.Core.Models.EventModel canonical) : AbstractRoom
    {
        public MegaCrit.Sts2.Core.Models.EventModel CanonicalEvent { get; } = canonical;
    }
    public class MerchantRoom : AbstractRoom
    {
        public List<MerchantInventory> Inventories { get; private set; } = new();
    }
}

namespace MegaCrit.Sts2.Core.Entities.Merchant
{
    using MegaCrit.Sts2.Core.Entities.Cards;
    using MegaCrit.Sts2.Core.Entities.Players;
    using MegaCrit.Sts2.Core.Models;
    public abstract class MerchantEntry(Player player, int cost)
    {
        protected readonly Player _player = player;
        protected int _cost = cost;
    }
    public sealed class MerchantCardEntry(Player p, CardModel? card, int cost, bool sale) : MerchantEntry(p, cost)
    {
        public CardCreationResult? CreationResult { get; private set; } = card is null ? null : new CardCreationResult(card);
        public bool IsOnSale { get; private set; } = sale;
    }
    public sealed class MerchantRelicEntry(Player p, RelicModel r, int cost) : MerchantEntry(p, cost)
    { public RelicModel? Model { get; private set; } = r; }
    public sealed class MerchantPotionEntry(Player p, PotionModel m, int cost) : MerchantEntry(p, cost)
    { public PotionModel? Model { get; private set; } = m; }
    public sealed class MerchantCardRemovalEntry(Player p, int cost, bool used) : MerchantEntry(p, cost)
    { public bool Used { get; private set; } = used; }
    public class MerchantInventory(Player player)
    {
        private readonly List<MerchantCardEntry> _characterCardEntries = new();
        private readonly List<MerchantCardEntry> _colorlessCardEntries = new();
        private readonly List<MerchantRelicEntry> _relicEntries = new();
        private readonly List<MerchantPotionEntry> _potionEntries = new();
        public MerchantCardRemovalEntry? CardRemovalEntry { get; private set; }
        public Player Player { get; } = player;
        public void AddCharacterCard(CardModel? c, int cost, bool onSale) => _characterCardEntries.Add(new(Player, c, cost, onSale));
        public void AddColorlessCard(CardModel c, int cost) => _colorlessCardEntries.Add(new(Player, c, cost, false));
        public void AddRelic(RelicModel r, int cost) => _relicEntries.Add(new(Player, r, cost));
        public void AddPotion(PotionModel p, int cost) => _potionEntries.Add(new(Player, p, cost));
        public void SetRemoval(int cost, bool used) => CardRemovalEntry = new(Player, cost, used);
    }
}

namespace MegaCrit.Sts2.Core.Rewards
{
    using MegaCrit.Sts2.Core.Entities.Cards;
    using MegaCrit.Sts2.Core.Entities.Players;
    public abstract class Reward(Player player)
    {
        public Player Player { get; } = player;
        public bool SuccessfullySelected { get; private set; }
    }
    public class GoldReward(Player p) : Reward(p);
    public class CardReward(Player p) : Reward(p)
    {
        private readonly List<CardCreationResult> _cards = new();
        public void AddOption(CardCreationResult r) => _cards.Add(r);
    }
    public class RewardsSet(Player player)
    {
        public Player Player { get; } = player;
        public List<Reward> Rewards { get; } = new();
    }
}

namespace MegaCrit.Sts2.Core.Multiplayer.Game
{
    using MegaCrit.Sts2.Core.Models;
    using MegaCrit.Sts2.Core.Rewards;
    public class EventSynchronizer(ulong localPlayerId)
    {
        private readonly ulong _localPlayerId = localPlayerId;
        private readonly List<EventModel> _events = new();
        private readonly string _multiplayerOptionSelectionRng = "SECRET_MUST_NOT_LEAK option rng";  // forbidden
        public void BeginEvent(EventModel mutableForLocal) { _events.Clear(); _events.Add(mutableForLocal); }
        public int SelectionRngLength => _multiplayerOptionSelectionRng.Length;
    }
    public class RewardsSetSynchronizer(ulong localPlayerId, int playerCount)
    {
        private class RewardsSetState { public required RewardsSet set; }
        private class PlayerRewardState { public required List<RewardsSetState> rewardsStack; }
        private readonly ulong _localPlayerId = localPlayerId;
        private readonly List<PlayerRewardState> _rewardStates =
            Enumerable.Range(0, playerCount).Select(_ => new PlayerRewardState { rewardsStack = new() }).ToList();
        public void BeginRewardsSet(RewardsSet set) => _rewardStates[0].rewardsStack.Add(new RewardsSetState { set = set });
    }
}

namespace MegaCrit.Sts2.Core.Runs
{
    using MegaCrit.Sts2.Core.Entities.Players;
    using MegaCrit.Sts2.Core.Multiplayer.Game;
    using MegaCrit.Sts2.Core.Rooms;
    public class RunState(int ascension, int actIndex)
    {
        private readonly List<Player> _players = new();
        private int _currentActIndex = actIndex;
        private readonly List<AbstractRoom> _currentRooms = new();
        public int AscensionLevel { get; init; } = ascension;
        public string Rng { get; } = "SECRET_MUST_NOT_LEAK seed";
        public void AddPlayer(Player p) => _players.Add(p);
        public void PushRoom(AbstractRoom r) => _currentRooms.Add(r);
    }
    public class RunManager
    {
        public static RunManager Instance { get; } = new();
        private RunState? State { get; set; }
        public RewardsSetSynchronizer RewardsSetSynchronizer { get; private set; } = new(1UL, 1);
        public EventSynchronizer EventSynchronizer { get; private set; } = new(1UL);
        public void Start(RunState s) => State = s;
    }
}

namespace MegaCrit.Sts2.Core.Combat
{
    using MegaCrit.Sts2.Core.Entities.Creatures;
    public class CombatState(int round)
    {
        private readonly List<Creature> _enemies = new();
        public int RoundNumber { get; set; } = round;
        public void AddEnemy(Creature c) => _enemies.Add(c);
    }
    public class CombatManager
    {
        public static CombatManager Instance { get; } = new();
        private CombatState? _state;
        public bool IsInProgress { get; private set; }
        public void Begin(CombatState s) { _state = s; IsInProgress = true; }
    }
}
