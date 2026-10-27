using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;
using Ultima.Core.Model;

namespace Ultima.GameApi;

/// <summary>
/// Lecture seule de l'état de combat → <see cref="CombatSnapshot"/>. Aucune mutation, aucune commande :
/// uniquement des getters et les calculs d'affichage du jeu. Ne lève jamais vers le jeu.
/// </summary>
public static class CombatReader
{
    private static readonly object Gate = new();
    private static readonly Dictionary<ulong, string> PlatformNames = new();
    private static string? _lastStateLine;

    /// État courant via CombatManager.Instance.DebugOnlyGetState() ; null hors combat ou si erreur.
    public static CombatSnapshot? TryRead()
    {
        try
        {
            CombatManager? manager = CombatManager.Instance;
            if (manager == null || !manager.IsInProgress)
            {
                return null;
            }
            CombatState? state = manager.DebugOnlyGetState();
            return state == null ? null : Read(state);
        }
        catch (Exception ex)
        {
            ReaderLog.Error("TryRead()", ex);
            return null;
        }
    }

    /// Depuis l'argument de CombatManager.Instance.StateTracker.CombatStateChanged (Action<CombatState>).
    public static CombatSnapshot? TryRead(CombatState state)
    {
        try
        {
            if (state == null)
            {
                return null;
            }
            // Energy/MaxEnergy font un null-ref hors combat : même garde que TryRead().
            CombatManager? manager = CombatManager.Instance;
            if (manager == null || !manager.IsInProgress)
            {
                return null;
            }
            return Read(state);
        }
        catch (Exception ex)
        {
            ReaderLog.Error("TryRead(state)", ex);
            return null;
        }
    }

    private static CombatSnapshot? Read(CombatState state)
    {
        Player? me = LocalContext.GetMe(state);
        if (me == null)
        {
            ReaderLog.Once("reader: local player not found in combat state");
            return null;
        }
        Creature meCreature = me.Creature;
        ulong localNetId = me.NetId;

        int turn = 0;
        try
        {
            turn = state.RoundNumber;
        }
        catch (Exception ex)
        {
            ReaderLog.Error("Turn", ex);
        }

        IReadOnlyList<Player> players = Array.Empty<Player>();
        try
        {
            players = state.Players;
        }
        catch (Exception ex)
        {
            ReaderLog.Error("Players", ex);
        }

        PlatformType? platform = null;
        bool isMultiplayer = false;
        try
        {
            INetGameService netService = RunManager.Instance.NetService;
            platform = netService.Platform;
            isMultiplayer = netService.Type.IsMultiplayer();
        }
        catch (Exception ex)
        {
            ReaderLog.Error("NetService", ex);
        }

        var playerSnapshots = new List<PlayerSnapshot>(Math.Max(1, players.Count));
        string localName = "";
        bool localFound = false;
        for (int i = 0; i < players.Count; i++)
        {
            Player p = players[i];
            if (p == null)
            {
                continue;
            }
            PlayerSnapshot? snap = ReadPlayer(p, localNetId, platform);
            if (snap == null)
            {
                continue;
            }
            playerSnapshots.Add(snap);
            if (snap.IsLocal)
            {
                localName = snap.Name;
                localFound = true;
            }
        }
        if (!localFound)
        {
            PlayerSnapshot? localSnap = ReadPlayer(me, localNetId, platform);
            if (localSnap != null)
            {
                localName = localSnap.Name;
                playerSnapshots.Add(localSnap);
            }
        }

        int hp = 0, maxHp = 0, block = 0;
        try
        {
            hp = meCreature.CurrentHp;
            maxHp = meCreature.MaxHp;
            block = meCreature.Block;
        }
        catch (Exception ex)
        {
            ReaderLog.Error("LocalCreature", ex);
        }

        int energy = 0;
        IReadOnlyList<CardSnapshot> hand = Array.Empty<CardSnapshot>();
        IReadOnlyList<CardSnapshot> drawPile = Array.Empty<CardSnapshot>();
        IReadOnlyList<CardSnapshot> discardPile = Array.Empty<CardSnapshot>();
        PlayerCombatState? pcs = me.PlayerCombatState;
        if (pcs != null)
        {
            try
            {
                energy = pcs.Energy;
            }
            catch (Exception ex)
            {
                ReaderLog.Error("Energy", ex);
            }
            hand = ReadPile(pcs.Hand, "Hand");
            drawPile = ReadPile(pcs.DrawPile, "DrawPile");
            discardPile = ReadPile(pcs.DiscardPile, "DiscardPile");
        }
        else
        {
            ReaderLog.Once("reader: local PlayerCombatState is null");
        }

        var enemies = new List<EnemySnapshot>();
        int intentCount = 0;
        IReadOnlyList<Creature> enemyCreatures = Array.Empty<Creature>();
        try
        {
            enemyCreatures = state.Enemies;
        }
        catch (Exception ex)
        {
            ReaderLog.Error("Enemies", ex);
        }
        for (int i = 0; i < enemyCreatures.Count; i++)
        {
            Creature creature = enemyCreatures[i];
            try
            {
                if (creature == null || !creature.IsAlive)
                {
                    continue;
                }
            }
            catch (Exception ex)
            {
                ReaderLog.Error("Enemy.IsAlive", ex);
                continue;
            }
            EnemySnapshot enemy = ReadEnemy(creature, me, players);
            intentCount += enemy.Intents.Count;
            enemies.Add(enemy);
        }

        var snapshot = new CombatSnapshot(
            turn, localName, hp, maxHp, block, energy,
            enemies, hand, drawPile, discardPile)
        {
            Players = playerSnapshots,
            LocalNetId = localNetId,
            IsMultiplayer = isMultiplayer,
        };

        LogState(enemies.Count, intentCount);
        return snapshot;
    }

    private static PlayerSnapshot? ReadPlayer(Player p, ulong localNetId, PlatformType? platform)
    {
        try
        {
            ulong netId = p.NetId;
            string characterId = "";
            try
            {
                characterId = p.Character.GetType().Name;
            }
            catch (Exception ex)
            {
                ReaderLog.Error("Player.CharacterId", ex);
            }

            int hp = 0, maxHp = 0, block = 0;
            bool isVulnerable = false;
            try
            {
                Creature c = p.Creature;
                hp = c.CurrentHp;
                maxHp = c.MaxHp;
                block = c.Block;
                isVulnerable = c.GetPower<VulnerablePower>() != null;
            }
            catch (Exception ex)
            {
                ReaderLog.Error("Player.Creature", ex);
            }

            return new PlayerSnapshot(
                netId, ReadPlayerName(p, platform), characterId,
                hp, maxHp, block, netId == localNetId)
            {
                IsVulnerable = isVulnerable,
            };
        }
        catch (Exception ex)
        {
            ReaderLog.Error("Player", ex);
            return null;
        }
    }

    /// Nom plateforme (mis en cache par NetId une fois obtenu), repli sur le titre du personnage.
    private static string ReadPlayerName(Player p, PlatformType? platform)
    {
        ulong netId = p.NetId;
        lock (Gate)
        {
            if (PlatformNames.TryGetValue(netId, out string? cached))
            {
                return cached;
            }
        }

        if (platform is { } pt)
        {
            try
            {
                string name = PlatformUtil.GetPlayerName(pt, netId);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    lock (Gate)
                    {
                        PlatformNames[netId] = name;
                    }
                    return name;
                }
            }
            catch (Exception ex)
            {
                ReaderLog.Error("Player.Name", ex);
            }
        }

        try
        {
            return p.Character.Title.GetFormattedText() ?? "";
        }
        catch (Exception ex)
        {
            ReaderLog.Error("Player.CharacterTitle", ex);
            return "";
        }
    }

    private static IReadOnlyList<CardSnapshot> ReadPile(CardPile? pile, string pileName)
    {
        try
        {
            if (pile == null)
            {
                return Array.Empty<CardSnapshot>();
            }
            IReadOnlyList<CardModel> cards = pile.Cards;
            var list = new List<CardSnapshot>(cards.Count);
            for (int i = 0; i < cards.Count; i++)
            {
                CardModel card = cards[i];
                if (card != null)
                {
                    list.Add(ReadCard(card));
                }
            }
            return list;
        }
        catch (Exception ex)
        {
            ReaderLog.Error("Pile." + pileName, ex);
            return Array.Empty<CardSnapshot>();
        }
    }

    private static CardSnapshot ReadCard(CardModel card)
    {
        string modelId = "", name = "", type = "", className = "";
        int cost = 0;
        bool isUpgraded = false, glowsGold = false, playable = false;
        string? enchantment = null;

        try
        {
            modelId = card.Id.Entry;
            className = card.GetType().Name;
        }
        catch (Exception ex)
        {
            ReaderLog.Error("Card.Id", ex);
        }
        try
        {
            name = card.Title ?? "";
        }
        catch (Exception ex)
        {
            ReaderLog.Error("Card.Title", ex);
        }
        try
        {
            CardEnergyCost energyCost = card.EnergyCost;
            cost = energyCost.CostsX ? -1 : energyCost.GetAmountToSpend();
        }
        catch (Exception ex)
        {
            ReaderLog.Error("Card.Cost", ex);
        }
        try
        {
            type = card.Type.ToString();
            isUpgraded = card.IsUpgraded;
        }
        catch (Exception ex)
        {
            ReaderLog.Error("Card.Type", ex);
        }
        try
        {
            EnchantmentModel? ench = card.Enchantment;
            enchantment = ench?.Title.GetFormattedText();
        }
        catch (Exception ex)
        {
            ReaderLog.Error("Card.Enchantment", ex);
        }
        try
        {
            glowsGold = card.ShouldGlowGold;
        }
        catch (Exception ex)
        {
            ReaderLog.Error("Card.GlowsGold", ex);
        }
        try
        {
            playable = card.CanPlay(out _, out _);
        }
        catch (Exception ex)
        {
            ReaderLog.Error("Card.Playable", ex);
        }

        return new CardSnapshot(modelId, name, cost, type, isUpgraded, enchantment)
        {
            ClassName = className,
            GlowsGold = glowsGold,
            Playable = playable,
        };
    }

    private static EnemySnapshot ReadEnemy(Creature creature, Player me, IReadOnlyList<Player> players)
    {
        string name = "";
        try
        {
            name = creature.Monster?.Title.GetFormattedText() ?? creature.Name ?? "";
        }
        catch (Exception ex)
        {
            ReaderLog.Error("Enemy.Name", ex);
        }

        int hp = 0, maxHp = 0, block = 0;
        uint combatId = 0;
        bool isWeak = false;
        try
        {
            hp = creature.CurrentHp;
            maxHp = creature.MaxHp;
            block = creature.Block;
            combatId = creature.CombatId ?? 0;
            isWeak = creature.GetPower<WeakPower>() != null;
        }
        catch (Exception ex)
        {
            ReaderLog.Error("Enemy.Stats", ex);
        }

        var powers = new List<PowerSnapshot>();
        try
        {
            IReadOnlyList<PowerModel> creaturePowers = creature.Powers;
            for (int i = 0; i < creaturePowers.Count; i++)
            {
                PowerModel power = creaturePowers[i];
                try
                {
                    if (power == null || !power.IsVisible)
                    {
                        continue;
                    }
                    powers.Add(new PowerSnapshot(
                        power.Id.Entry, power.Title.GetFormattedText() ?? "",
                        power.DisplayAmount, power.Type == PowerType.Debuff));
                }
                catch (Exception ex)
                {
                    ReaderLog.Error("Enemy.Power", ex);
                }
            }
        }
        catch (Exception ex)
        {
            ReaderLog.Error("Enemy.Powers", ex);
        }

        var intents = new List<IntentSnapshot>();
        try
        {
            if (creature.Monster?.NextMove is MoveState ms && ms.Intents != null)
            {
                IEnumerable<Creature> targets =
                    (IEnumerable<Creature>?)creature.CombatState?.PlayerCreatures ?? Array.Empty<Creature>();
                IReadOnlyList<MegaCrit.Sts2.Core.MonsterMoves.Intents.AbstractIntent> moveIntents = ms.Intents;
                for (int i = 0; i < moveIntents.Count; i++)
                {
                    var intent = moveIntents[i];
                    if (intent != null)
                    {
                        intents.Add(IntentParser.Parse(intent, creature, targets, me, players));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ReaderLog.Error("Enemy.Intents", ex);
        }

        return new EnemySnapshot(name, hp, maxHp, block, intents)
        {
            CombatId = combatId,
            IsWeak = isWeak,
            Powers = powers,
        };
    }

    /// Écrit « ULTIMA state: n enemies, n intents » sans répéter deux fois de suite la même ligne.
    private static void LogState(int enemyCount, int intentCount)
    {
        try
        {
            string line = $"ULTIMA state: {enemyCount} enemies, {intentCount} intents";
            lock (Gate)
            {
                if (line == _lastStateLine)
                {
                    return;
                }
                _lastStateLine = line;
            }
            ModEntry.Write(line);
        }
        catch
        {
            // Le log ne doit jamais remonter vers le jeu.
        }
    }
}
