using Ultima.Daemon.Knowledge;
using Ultima.Daemon.Mirror;
using Ultima.Engine.Contracts;
using Ultima.Saves.Model;

namespace Ultima.Daemon.State;

/// <summary>Partie « run » de l'état, calculée UNE fois par save relue (et non à chaque événement de log).</summary>
public sealed record RunPart(RunState Source, PlayerState Player, RunView View, ScoringContext Context,
    DeckEvaluation? Deck, PathsView? Paths);

/// <summary>
/// Assemble un <see cref="EngineState"/> conforme au contrat : noms via <see cref="IKnowledge"/>,
/// scores via <see cref="IScorer"/>, chemins via <see cref="IPathAnalyzer"/> (Hermes). Toute exception
/// ou réponse vide du calcul → champ <c>null</c> (l'UI affiche « — »), jamais un crash ni un 0 inventé.
/// Aucune donnée cachée n'entre ici : la save est lue par des DTO sans rng/odds/sacs/ids pré-tirés.
/// </summary>
public sealed class StateAssembler
{
    private readonly IKnowledge _k;
    private readonly IScorer? _scorer;
    private readonly IPathAnalyzer? _paths;
    private readonly ulong? _steamId64;
    private readonly HashSet<string> _warned = new(StringComparer.Ordinal);

    public StateAssembler(IKnowledge knowledge, IScorer? scorer, IPathAnalyzer? paths, ulong? steamId64)
    {
        _k = knowledge;
        _scorer = scorer;
        _paths = paths;
        _steamId64 = steamId64;
    }

    // ---------- calcul protégé ----------

    private T? Guard<T>(string what, Func<T?> f) where T : class
    {
        try { return f(); }
        catch (Exception e)
        {
            if (_warned.Add(what)) Diag.Warn($"calcul indisponible ({what}) : {e.GetType().Name}: {e.Message}");
            return null;
        }
    }

    /// <summary>Score explicable, ou null si le calcul échoue ou ne renvoie rien (stub vide).</summary>
    internal Score? SafeScore(string what, Func<IScorer, Score?> f)
    {
        if (_scorer is null) return null;
        var s = Guard(what, () => f(_scorer));
        return s is null || s.Terms is null || s.Terms.Count == 0 ? null : s;
    }

    // ---------- vues ----------

    public CardView Card(CardInstance c, Score? score)
    {
        var info = Guard("knowledge.card", () => _k.Card(c.Id)) ?? Fallbacks.Card(c.Id);
        EnchantmentView? ench = null;
        if (c.Enchantment is { } e)
        {
            var ei = Guard("knowledge.enchantment", () => _k.Enchantment(e.Id));
            ench = new EnchantmentView(e.Id, ei?.Name ?? e.Id, e.Amount, ei?.Description ?? "");
        }
        bool curse = info.IsCurse || NullKnowledge.KnownCurses.Contains(c.Id);
        return new CardView(c.Id, info.Name, info.Type, info.Rarity, info.Cost, c.UpgradeLevel, ench, curse, score)
        {
            Description = string.IsNullOrEmpty(info.Description) ? null : info.Description, // v1.1
        };
    }

    public RelicView Relic(string id, Score? score)
    {
        var info = Guard("knowledge.relic", () => _k.Relic(id));
        return new RelicView(id, info?.Name ?? id, info?.Description ?? "", score);
    }

    public PotionView Potion(string id, Score? score = null)
    {
        var info = Guard("knowledge.potion", () => _k.Potion(id));
        return new PotionView(id, info?.Name ?? id, info?.Description ?? "") { Score = score }; // Score : v1.1
    }

    /// <summary>Construit la partie run depuis la save (null : pas de run, ou joueur local introuvable).</summary>
    public RunPart? BuildRun(RunState? run)
    {
        if (run is null) return null;
        var me = run.LocalPlayer(_steamId64);
        if (me is null) return null;
        var act = run.CurrentAct;
        var ctx = new ScoringContext(me.CharacterId, run.CurrentActIndex, act?.BossId, run.Ascension, run.FloorCount,
            me.Deck, me.Relics.Select(r => r.Id).ToArray(), me.Gold, me.CurrentHp, me.MaxHp);

        var deck = me.Deck.Select(c => Card(c, SafeScore("score.card", s => s.ScoreCard(c, ctx)))).ToArray();
        var relics = me.Relics.Select(r => Relic(r.Id, SafeScore("score.relic", s => s.ScoreRelic(r.Id, ctx)))).ToArray();
        var potions = me.Potions.OrderBy(p => p.SlotIndex)
            .Select(p => Potion(p.Id, SafeScore("score.potion", s => s.ScorePotion(p.Id, ctx)))).ToArray();
        var view = new RunView(
            me.CharacterId,
            Guard("knowledge.character", () => _k.CharacterName(me.CharacterId)) ?? me.CharacterId,
            run.CurrentActIndex, act?.Id, act?.BossId,
            act?.BossId is { } b ? Guard("knowledge.encounter", () => _k.EncounterName(b)) ?? b : null,
            run.Ascension, me.CurrentHp, me.MaxHp, me.Gold,
            run.FloorCount, // = RunState.TotalFloor du jeu (somme de map_point_history), Runs/RunState.cs:158
            deck, relics, potions);

        // Malédictions : TOUJOURS renseignées par le daemon (module « curses »), même si le calcul échoue.
        var curses = deck.Where(c => c.IsCurse).ToArray();
        var eval = Guard("score.deck", () => _scorer?.EvaluateDeck(ctx));
        DeckEvaluation evaluation = eval is null
            ? new DeckEvaluation(null!, null, Array.Empty<string>(), curses) // Score null = « — » (demande de contrat : Score?)
            : eval with
            {
                Score = eval.Score is { Terms.Count: > 0 } ? eval.Score : null!,
                Curses = curses,
                PotentialArchetypes = eval.PotentialArchetypes ?? Array.Empty<string>(),
            };

        PathsView? paths = null;
        if (act?.Map is { } map && _paths is not null)
        {
            var pos = run.CurrentCoord ?? map.Start.Coord;
            paths = Guard("paths", () => _paths.Analyze(map, pos, run.CurrentActIndex, me.CharacterId, me.CurrentHp));
            if (paths is { TotalPaths: <= 0 }) paths = null; // stub vide → « — »
        }
        return new RunPart(run, me, view, ctx, evaluation, paths);
    }

    /// <summary>Offre de récompense : première récompense de cartes locale non prise (sinon la dernière prise).</summary>
    public RewardView? BuildReward(MRewards? mem, RunPart? run)
    {
        if (mem is null || mem.CardRewards.Count == 0) return null;
        var pick = mem.CardRewards.FirstOrDefault(r => !r.AlreadyTaken) ?? mem.CardRewards[^1];
        var offered = pick.Offered.Where(c => c.Id is not null)
            .Select(c => new CardInstance(c.Id!, c.Upgrade, null, null)).ToArray();
        var ctx = run?.Context;
        var cards = offered.Select(c => Card(c, ctx is null ? null : SafeScore("score.card", s => s.ScoreCard(c, ctx)))).ToArray();
        var skip = ctx is null ? null : SafeScore("score.skip", s => s.ScoreSkip(offered, ctx));
        return new RewardView(cards, skip!, pick.AlreadyTaken); // skip null = « — » (demande de contrat : Score?)
    }

    /// <summary>Inventaire du marchand. Prix = prix AFFICHÉ estimé (réductions de reliques comprises).</summary>
    public ShopView? BuildShop(MShop? mem, RunPart? run, int? memGold)
    {
        if (mem is null || !mem.InShop) return null;
        int? gold = memGold ?? run?.Player.Gold;
        var ctx = run?.Context is { } c0 && gold is { } g ? c0 with { Gold = g } : run?.Context;
        bool Affordable(int price) => gold is { } gg && price <= gg;
        Score? ItemScore(string kind, string id, int price) =>
            ctx is null ? null : SafeScore("score.shop", s => s.ScoreShopItem(kind, id, price, ctx));

        var items = new List<ShopItem>();
        foreach (var e in mem.Cards)
        {
            if (e.Card.Id is not { } id) continue;
            int price = e.EstimatedDisplayedCost ?? e.BaseCost;
            var ci = new CardInstance(id, e.Card.Upgrade, null, null);
            var card = Card(ci, ctx is null ? null : SafeScore("score.card", s => s.ScoreCard(ci, ctx)));
            items.Add(new ShopItem("card", id, card.Name, price, e.OnSale, Affordable(price), card, ItemScore("card", id, price)));
        }
        foreach (var e in mem.Relics)
        {
            if (e.Id is not { } id) continue;
            int price = e.EstimatedDisplayedCost ?? e.BaseCost;
            items.Add(new ShopItem("relic", id, Relic(id, null).Name, price, false, Affordable(price), null, ItemScore("relic", id, price)));
        }
        foreach (var e in mem.Potions)
        {
            if (e.Id is not { } id) continue;
            int price = e.EstimatedDisplayedCost ?? e.BaseCost;
            items.Add(new ShopItem("potion", id, Potion(id).Name, price, false, Affordable(price), null, ItemScore("potion", id, price)));
        }
        // pas d'entrée de retrait = service indisponible : on le publie comme « utilisé » (non achetable)
        int removalPrice = mem.RemovalEstimatedCost ?? mem.RemovalBaseCost ?? 0;
        bool removalUsed = mem.RemovalUsed ?? true;
        var removalScore = removalUsed || ctx is null ? null : SafeScore("score.removal", s => s.ScoreRemoval(removalPrice, ctx));
        return new ShopView(items, removalPrice, removalUsed, removalScore);
    }

    public static EngineState Compose(DateTimeOffset at, SourceHealth health, Screen screen, RunPart? run,
        RewardView? reward, ShopView? shop, OverlaySettings settings) =>
        new(EngineState.CurrentVersion, at, health, screen, run?.View, reward, shop,
            run?.Paths, run?.Deck, settings);
}
