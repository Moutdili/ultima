using Ultima.Daemon.Knowledge;
using Ultima.Daemon.Mirror;
using Ultima.Engine.Contracts;
using Ultima.Saves.Model;

namespace Ultima.Daemon.State;

/// <summary>Partie « run » de l'état, calculée UNE fois par save relue (et non à chaque événement de log).</summary>
public sealed record RunPart(RunState Source, PlayerState Player, RunView View, ScoringContext Context,
    DeckEvaluation? Deck, PathsView? Paths)
{
    /// <summary>v1.2 : carte visible de l'acte courant (save).</summary>
    public MapView? Map { get; init; }
    /// <summary>v1.2 : route conseillée (IRoutePlanner de Hermes).</summary>
    public RouteView? Route { get; init; }
}

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
    private readonly IRoutePlanner? _route;
    private readonly IEventAdvisor? _events;
    private readonly ulong? _steamId64;
    private readonly ILocTexts _texts;
    private readonly HashSet<string> _warned = new(StringComparer.Ordinal);

    public StateAssembler(IKnowledge knowledge, IScorer? scorer, IPathAnalyzer? paths, ulong? steamId64,
        IRoutePlanner? route = null, IEventAdvisor? events = null, ILocTexts? texts = null)
    {
        _texts = texts ?? NullLocTexts.Instance;
        _k = knowledge;
        _scorer = scorer;
        _paths = paths;
        _steamId64 = steamId64;
        _route = route;
        _events = events;
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
        // v1.2 : nom « + », coût et texte au niveau d'amélioration de la save
        var info = Guard("knowledge.card", () => _k.Card(c.Id, c.UpgradeLevel)) ?? Fallbacks.Card(c.Id);
        EnchantmentView? ench = null;
        if (c.Enchantment is { } e)
        {
            var ei = Guard("knowledge.enchantment", () => _k.Enchantment(e.Id, e.Amount)); // v1.2 : texte avec le montant
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
            me.Deck, me.Relics.Select(r => r.Id).ToArray(), me.Gold, me.CurrentHp, me.MaxHp)
        {
            // v1.2 : alliés co-op (joueur local exclu), identifiants seulement ; vide en solo
            Allies = run.Players.Where(p => p.NetId != me.NetId)
                .Select(p => new AllyContext(p.NetId, p.CharacterId, p.CurrentHp, p.MaxHp, p.Deck, p.Relics.Select(r => r.Id).ToArray()))
                .ToArray(),
        };

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
            deck, relics, potions)
        {
            Party = BuildParty(run, me, deck, relics), // v1.2
        };

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
        var mapView = Guard("map", () => BuildMap(run));
        var route = mapView is null || _route is null ? null : Guard("route", () => _route.Plan(mapView, ctx));
        return new RunPart(run, me, view, ctx, evaluation, paths) { Map = mapView, Route = route };
    }

    /// <summary>
    /// v1.2 : carte VISIBLE de l'acte courant (types des cases, liens, cases visitées, position, boss).
    /// Les actes futurs ne sont jamais publiés (la save les masque déjà : <c>ActInfo.IsRevealed</c>).
    /// </summary>
    public static MapView? BuildMap(RunState run)
    {
        var act = run.CurrentAct;
        if (act is not { IsRevealed: true, Map: { } map }) return null;
        var visited = run.VisitedMapCoords.ToHashSet();
        var nodes = map.ByCoord.Values
            .OrderBy(n => n.Coord.Row).ThenBy(n => n.Coord.Col)
            .Select(n => new MapNodeView(n.Coord.Col, n.Coord.Row, n.RawType,
                n.Children.Select(c => new MapCoordView(c.Col, c.Row)).ToArray(), visited.Contains(n.Coord)))
            .ToArray();
        var pos = run.CurrentCoord is { } p ? new MapCoordView(p.Col, p.Row) : null;
        return new MapView(run.CurrentActIndex, map.Width, map.Height, nodes, pos, new MapCoordView(map.Boss.Coord.Col, map.Boss.Coord.Row));
    }

    /// <summary>v1.2 : tous les joueurs de la save (local inclus, IsLocal). Solo : une seule entrée.</summary>
    private IReadOnlyList<AllyView> BuildParty(RunState run, PlayerState me, IReadOnlyList<CardView> myDeck, IReadOnlyList<RelicView> myRelics) =>
        run.Players.Select(p =>
        {
            bool local = p.NetId == me.NetId;
            var name = Guard("knowledge.character", () => _k.CharacterName(p.CharacterId)) ?? p.CharacterId;
            return new AllyView(p.NetId, name, p.CharacterId, local, p.CurrentHp, p.MaxHp,
                local ? myDeck : p.Deck.Select(c => Card(c, null)).ToArray(),
                local ? myRelics : p.Relics.Select(r => Relic(r.Id, null)).ToArray());
        }).ToArray();

    /// <summary>PV de l'équipe pris en mémoire quand la lecture mémoire est plus récente que la save.</summary>
    public static IReadOnlyList<AllyView> OverlayParty(IReadOnlyList<AllyView> party, MRun mem) =>
        party.Select(a => mem.Players.FirstOrDefault(m => m.NetId == a.NetId) is { } m ? a with { Hp = m.Hp, MaxHp = m.MaxHp } : a).ToArray();

    /// <summary>Id synthétique de l'unique bouton « Continuer » d'un événement fini (absent des options lues).</summary>
    public const string ContinueOptionId = "CONTINUE";
    public const string ContinueLabel = "Continuer";

    /// <summary>
    /// v1.2 : événement courant lu par le miroir (section « event ») + notes de l'<see cref="IEventAdvisor"/>
    /// (Hermes). Les identifiants sont passés NUS au calcul (<c>EVENT.ABYSSAL_BATHS</c> → <c>ABYSSAL_BATHS</c>,
    /// option <c>…options.ABSTAIN</c> → <c>ABSTAIN</c>) ; null = option non mesurée (« — »).
    /// Textes (v1.4, Miroir 2) : rendus en FR à partir des clés <c>nameKey</c>/<c>titleKey</c>/<c>descriptionKey</c> ;
    /// texte absent ⇒ <c>null</c> (jamais l'identifiant ni la clé). Options inconnues (<c>optionsKnown=false</c>)
    /// ⇒ pas de vue ; événement fini ⇒ une seule option « Continuer ».
    /// </summary>
    public EventView? BuildEvent(MEvent? mem, RunPart? run)
    {
        if (mem?.EventId is not { Length: > 0 } eventId) return null;
        var name = mem.Name ?? Guard("texts.event", () => _texts.Text(mem.NameKey?.Table, mem.NameKey?.Key));
        if (mem.IsFinished == true)
            return new EventView(eventId, name!, new[] { new EventOptionView(ContinueOptionId, ContinueLabel, null, true, null) });
        if (mem.OptionsKnown == false || mem.Options is null) return null;
        var options = mem.Options.Where(o => o.Id is { Length: > 0 }).ToArray();
        var ids = options.Select(o => BareOption(o.Id!)).ToArray();
        IReadOnlyList<Score?> scores = Array.Empty<Score?>();
        if (_events is not null && run?.Context is { } ctx)
            scores = Guard("event", () => _events.Rank(BareEvent(eventId), ids, ctx)) ?? Array.Empty<Score?>();
        // Label/Name null : texte inconnu (l'UI affiche son propre repli). Contrat : string non nullable → demande de contrat.
        var views = options.Select((o, i) => new EventOptionView(o.Id!, OptionLabel(o)!, OptionDescription(o), o.Available ?? true,
            i < scores.Count && scores[i] is { Terms.Count: > 0 } sc ? sc : null)).ToArray();
        return new EventView(eventId, name!, views);
    }

    private string? OptionLabel(MEventOption o)
    {
        if (o.Label is { Length: > 0 } l) return l;
        if (Guard("texts.event", () => _texts.Text(o.TitleKey?.Table, o.TitleKey?.Key)) is { } t) return t;
        // option d'ancien : titre = nom de la relique (EventOption.cs:126-131), connu de la base même sans localisation
        var relic = o.RelicId ?? (o.TitleKey is { Table: "relics", Key: { } k } && k.EndsWith(".title", StringComparison.Ordinal)
            ? "RELIC." + k[..^6] : null);
        if (relic is not null && Guard("knowledge.relic", () => _k.Relic(relic)) is { Name: { Length: > 0 } rn } && rn != relic) return rn;
        return null;
    }

    private string? OptionDescription(MEventOption o) =>
        o.Description is { Length: > 0 } d ? d : Guard("texts.event", () => _texts.Text(o.DescriptionKey?.Table, o.DescriptionKey?.Key));

    internal static Enchantment? Enchant(MEnchant? e) =>
        e?.Id is { Length: > 0 } id ? new Enchantment(id, e.Amount) : null;

    internal static string BareEvent(string id) => id.StartsWith("EVENT.", StringComparison.Ordinal) ? id[6..] : id;

    internal static string BareOption(string id)
    {
        var i = id.LastIndexOf('.');
        return i >= 0 && i < id.Length - 1 ? id[(i + 1)..] : id;
    }

    /// <summary>Offre de récompense : première récompense de cartes locale non prise (sinon la dernière prise).</summary>
    public RewardView? BuildReward(MRewards? mem, RunPart? run)
    {
        if (mem is null || mem.CardRewards.Count == 0) return null;
        var pick = mem.CardRewards.FirstOrDefault(r => !r.AlreadyTaken) ?? mem.CardRewards[^1];
        var offered = pick.Offered.Where(c => c.Id is not null)
            .Select(c => new CardInstance(c.Id!, c.Upgrade, Enchant(c.Enchantment), null)).ToArray(); // v1.4 : enchantement (Miroir 2)
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
            var ci = new CardInstance(id, e.Card.Upgrade, Enchant(e.Card.Enchantment), null); // v1.4 : enchantement (Miroir 2)
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
        RewardView? reward, ShopView? shop, OverlaySettings settings, EventView? ev = null, RunView? runView = null) =>
        new(EngineState.CurrentVersion, at, health, screen, runView ?? run?.View, reward, shop,
            run?.Paths, run?.Deck, settings)
        {
            Map = run?.Map,     // v1.2
            Route = run?.Route, // v1.2
            Event = ev,         // v1.2
        };
}
