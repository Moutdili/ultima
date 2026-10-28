// Sts2Shape.TestTarget — a SMALL .NET 9 process that reproduces the SHAPE (namespaces, type names,
// field names, auto-property backing fields, List<T> containers) of the sts2.dll objects listed
// in Ultima.Mirror/ObjectPaths.cs, so `ultima-mirror probe` can be exercised end-to-end without
// the game. It proves the probe code + ClrMD non-suspending attach; it proves NOTHING about the
// real game's values. All ids end in _TEST to make that obvious.
//
// It also holds "forbidden" data (PlayerRng, RelicGrabBag, DrawPile) containing the marker
// SECRET_MUST_NOT_LEAK: the probe output must never contain it.
//
// Live mutation + GC churn run in the background to stress non-suspended reads.

using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

var mutate = !args.Contains("--frozen");
LocalContext.NetId = 1UL;

var player = new Player(1UL, new TestCharacter("CHARACTER", "SILENT_TEST"), hp: 61, maxHp: 70, gold: 99);
for (int i = 0; i < 5; i++) player.Deck.Add(new TestCard("STRIKE_TEST", 0));
for (int i = 0; i < 4; i++) player.Deck.Add(new TestCard("DEFEND_TEST", 0));
player.Deck.Add(new TestCard("NEUTRALIZE_TEST", 1));
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
modified.Modify(new TestCard("PREPARED_TEST", 1));
cr.AddOption(modified);
set.Rewards.Add(cr);
RunManager.Instance.RewardsSetSynchronizer.BeginRewardsSet(set);

// Shop on top of the room stack.
var shop = new MerchantRoom();
var inv = new MerchantInventory(player);
inv.AddCharacterCard(new TestCard("ACROBATICS_TEST", 0), 76, onSale: false);
inv.AddCharacterCard(new TestCard("FOOTWORK_TEST", 0), 25, onSale: true);
inv.AddCharacterCard(null, 150, onSale: false);                       // sold
inv.AddColorlessCard(new TestCard("APOTHEOSIS_TEST", 0), 172);
inv.AddRelic(new TestRelic("VAJRA_TEST"), 147);
inv.AddPotion(new TestPotion("BLOCK_POTION_TEST"), 52);
inv.SetRemoval(75, used: false);
shop.Inventories.Add(inv);
run.PushRoom(shop);

// Combat.
var cs = new CombatState(round: 2);
cs.AddEnemy(new Creature(new TestMonster("JAW_WORM_TEST"), 30, 44, 6));
player.StartCombat(energy: 3, hand: new[] { new TestCard("STRIKE_TEST", 0), new TestCard("SURVIVOR_TEST", 0) });
CombatManager.Instance.Begin(cs);

Console.WriteLine($"TESTTARGET pid={Environment.ProcessId} runtime={System.Runtime.InteropServices.RuntimeEnvironment.GetSystemVersion()} mutate={mutate}");
Console.WriteLine("EXPECT run: ascension=3 act=1 hp=61/70 gold>=99 deck=10 relics=2 potions=[FIRE_POTION_TEST,null,null]");
Console.WriteLine("EXPECT reward: BACKFLIP_TEST, DAGGER_THROW_TEST, PREPARED_TEST+1");
Console.WriteLine("EXPECT shop: ACROBATICS 76(→38), FOOTWORK 25 sale(→12), APOTHEOSIS 172(→86), VAJRA 147(→73), BLOCK_POTION 52(→26), removal 75(→37)");
Console.WriteLine("EXPECT combat: round=2 energy=3 hand=[STRIKE_TEST,SURVIVOR_TEST] enemy JAW_WORM_TEST 30/44 block 6");
Console.Out.Flush();

if (mutate)
{
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
        protected CardModel(string entry, int upgrade) : base("CARD", entry) { _currentUpgradeLevel = upgrade; }
    }
    public abstract class RelicModel(string entry) : AbstractModel("RELIC", entry);
    public abstract class PotionModel(string entry) : AbstractModel("POTION", entry);
    public abstract class CharacterModel(string c, string e) : AbstractModel(c, e);
    public abstract class MonsterModel(string entry) : AbstractModel("MONSTER", entry);
    public sealed class TestCard(string entry, int upgrade) : CardModel(entry, upgrade);
    public sealed class TestRelic(string entry) : RelicModel(entry);
    public sealed class TestPotion(string entry) : PotionModel(entry);
    public sealed class TestCharacter(string c, string e) : CharacterModel(c, e);
    public sealed class TestMonster(string entry) : MonsterModel(entry);
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
        private int _energy;
        public CardPile Hand { get; } = new();
        public CardPile DrawPile { get; } = new();   // forbidden (order is hidden)
        public PlayerCombatState(int energy, IEnumerable<CardModel> hand)
        {
            _energy = energy;
            foreach (var c in hand) Hand.Add(c);
            DrawPile.Add(new TestCard("SECRET_MUST_NOT_LEAK_DRAW", 0));
        }
    }
}

namespace MegaCrit.Sts2.Core.Rooms
{
    using MegaCrit.Sts2.Core.Entities.Merchant;
    public abstract class AbstractRoom;
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
    using MegaCrit.Sts2.Core.Rewards;
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
