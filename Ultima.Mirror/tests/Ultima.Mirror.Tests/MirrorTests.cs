using System.Text.Json;
using System.Text.Json.Nodes;
using Ultima.Daemon.Mirror;
using Ultima.Mirror;

namespace Ultima.Mirror.Tests;

/// <summary>Piles rendues comptées : l'ordre de la pioche ne doit JAMAIS influencer la sortie.</summary>
public class CountedPileTests
{
    static readonly CardView Strike = new("CARD.STRIKE", 0);
    static readonly CardView StrikePlus = new("CARD.STRIKE", 1);
    static readonly CardView Defend = new("CARD.DEFEND", 0);
    static readonly CardView DefendSown = new("CARD.DEFEND", 0, new EnchantView("ENCHANTMENT.SOWN", 1));
    static readonly CardView Zap = new("CARD.ZAP", 0);

    static string Json(CountedPile p) => JsonSerializer.Serialize(p, JsonSerializerOptions.Web);

    [Fact]
    public void Toutes_les_permutations_donnent_la_meme_sortie()
    {
        var cards = new[] { Zap, Strike, DefendSown, Strike, StrikePlus, Defend };
        var reference = Json(CountedPile.From(cards));
        var rng = new Random(1234);
        for (int i = 0; i < 200; i++)
            Assert.Equal(reference, Json(CountedPile.From(cards.OrderBy(_ => rng.Next()).ToArray())));
    }

    [Fact]
    public void Regroupe_par_id_amelioration_et_enchantement_et_trie_par_cle()
    {
        var p = CountedPile.From(new[] { Zap, Strike, DefendSown, Strike, StrikePlus, Defend });
        Assert.Equal(6, p.Total);
        Assert.Equal(new[]
        {
            new CountedCard(Defend, 1), new CountedCard(DefendSown, 1),
            new CountedCard(Strike, 2), new CountedCard(StrikePlus, 1), new CountedCard(Zap, 1),
        }, p.Cards);
    }

    [Fact]
    public void Pile_vide()
    {
        var p = CountedPile.From(Array.Empty<CardView>());
        Assert.Equal(0, p.Total);
        Assert.Empty(p.Cards);
    }
}

/// <summary>Garde-fous : champs cachés refusés, la pioche seulement par le chemin « contenu sans ordre ».</summary>
public class ReadGuardTests
{
    [Theory]
    [InlineData("<DrawPile>k__BackingField")]
    [InlineData("_piles")]
    [InlineData("_runPiles")]
    [InlineData("<Rng>k__BackingField")]
    [InlineData("<PlayerRng>k__BackingField")]
    [InlineData("<Odds>k__BackingField")]
    [InlineData("<RelicGrabBag>k__BackingField")]
    [InlineData("_multiplayerOptionSelectionRng")]
    [InlineData("_playerVotes")]
    [InlineData("_variables")]
    [InlineData("_dynamicVars")]
    [InlineData("<OnChosen>k__BackingField")]
    [InlineData("<WillKillPlayer>k__BackingField")]
    [InlineData("_generatedOptions")]
    [InlineData("_mutableEncounter")]
    public void Check_refuse_les_champs_caches_ou_hors_liste(string field) =>
        Assert.Throws<ForbiddenReadException>(() => ReadGuard.Check(field));

    [Theory]
    [InlineData("_cards")]
    [InlineData("<Hand>k__BackingField")]
    [InlineData("_piles")]
    [InlineData("<Rng>k__BackingField")]
    [InlineData("_events")]
    public void CheckUnordered_n_accepte_que_la_pioche(string field) =>
        Assert.Throws<ForbiddenReadException>(() => ReadGuard.CheckUnordered(field));

    [Fact]
    public void La_pioche_passe_seulement_par_CheckUnordered()
    {
        ReadGuard.CheckUnordered("<DrawPile>k__BackingField");
        Assert.Throws<ForbiddenReadException>(() => ReadGuard.Check("<DrawPile>k__BackingField"));
        Assert.DoesNotContain(ObjectPaths.All, s => ObjectPaths.UnorderedOnly.Any(u => u.Field == s.Field));
        Assert.Equal(ObjectPaths.All.Count + ObjectPaths.UnorderedOnly.Count, ObjectPaths.AllForLayout.Count());
    }

    [Fact]
    public void Liste_blanche_sans_champ_interdit() => Assert.Empty(ReadGuard.WhitelistViolations());

    [Theory]
    [InlineData("<DiscardPile>k__BackingField")]
    [InlineData("<ExhaustPile>k__BackingField")]
    [InlineData("<PlayPile>k__BackingField")]
    [InlineData("<Enchantment>k__BackingField")]
    [InlineData("_currentOptions")]
    [InlineData("<TextKey>k__BackingField")]
    [InlineData("<CanonicalEvent>k__BackingField")]
    public void Champs_du_miroir_2_acceptes(string field) => ReadGuard.Check(field);
}

/// <summary>
/// La sortie du miroir doit être lisible par Ultima.Daemon (Mirror/MirrorModels.cs) TELLE QUELLE.
/// On sérialise comme la CLI (`JsonSerializerOptions.Web`, Ultima.Mirror.Probe/Program.cs) et on passe
/// la ligne au VRAI analyseur du daemon (<see cref="MirrorResult.Parse"/>).
/// </summary>
public class DaemonCompatTests
{
    static string ProbeLine(params (string Name, object Value)[] sections)
    {
        var s = new JsonObject();
        foreach (var (name, value) in sections)
            s[name] = new JsonObject
            {
                ["stable"] = true, ["attempts"] = 2, ["ms"] = 5, ["error"] = null,
                ["value"] = JsonSerializer.SerializeToNode(value, value.GetType(), JsonSerializerOptions.Web),
            };
        return new JsonObject
        {
            ["v"] = 1, ["cmd"] = "probe", ["exit"] = 0, ["ms"] = 40, ["error"] = null,
            ["health"] = new JsonObject { ["gameRunning"] = true, ["attached"] = true },
            ["sections"] = s, ["unsupported"] = new JsonArray(),
        }.ToJsonString();
    }

    static EventOptionMirrorView Opt(int i, string id, bool locked, string? relic = null) =>
        new(i, id, null, null, !locked, new LocKeyView("events", id + ".title"), new LocKeyView("events", id + ".description"),
            locked, false, false, relic);

    [Fact]
    public void Evenement_connu_lu_par_le_daemon()
    {
        var ev = new EventMirrorView(true, "EVENT.ABYSSAL_BATHS", null,
            [Opt(0, "ABYSSAL_BATHS.pages.INITIAL.options.IMMERSE", false), Opt(1, "ABYSSAL_BATHS.pages.INITIAL.options.ABSTAIN", true)],
            false, true, false, new LocKeyView("events", "ABYSSAL_BATHS.title"), new LocKeyView("events", "ABYSSAL_BATHS.pages.INITIAL.description"));
        var r = MirrorResult.Parse(ProbeLine(("event", ev)), 0, 40);
        Assert.NotNull(r.Event);
        Assert.Equal("EVENT.ABYSSAL_BATHS", r.Event!.EventId);
        Assert.Null(r.Event.Name); // jamais de texte inventé : le daemon retombe sur l'id
        Assert.Equal(new[] { "ABYSSAL_BATHS.pages.INITIAL.options.IMMERSE", "ABYSSAL_BATHS.pages.INITIAL.options.ABSTAIN" },
            r.Event.Options!.Select(o => o.Id));
        Assert.Equal(new bool?[] { true, false }, r.Event.Options!.Select(o => o.Available));
        Assert.All(r.Event.Options!, o => { Assert.Null(o.Label); Assert.Null(o.Description); });
    }

    [Fact]
    public void Evenement_inconnu_donne_des_options_null()
    {
        var ev = new EventMirrorView(true, "EVENT.ABYSSAL_BATHS", null, null, false, false, false, null, null);
        var r = MirrorResult.Parse(ProbeLine(("event", ev)), 0, 40);
        Assert.NotNull(r.Event);
        Assert.Null(r.Event!.Options); // StateAssembler.BuildEvent → null : rien d'affiché plutôt que des options périmées
    }

    [Fact]
    public void Carte_enchantee_reste_lisible_par_le_daemon()
    {
        var rewards = new RewardScreenView(1, [new CardRewardView(false, [new CardView("CARD.PREPARED", 1, new EnchantView("ENCHANTMENT.SWIFT", 2))])]);
        var r = MirrorResult.Parse(ProbeLine(("reward", rewards)), 0, 40);
        Assert.NotNull(r.Reward);
        Assert.Equal("CARD.PREPARED", r.Reward!.CardRewards[0].Offered[0].Id);
        Assert.Equal(1, r.Reward.CardRewards[0].Offered[0].Upgrade);
    }

    /// <summary>Sorties BRUTES de `ultima-mirror probe --json` sur la cible de test (campagne, evidence/miroir2/32-*).</summary>
    public static TheoryData<string> Fixtures()
    {
        var d = new TheoryData<string>();
        foreach (var f in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures"), "32-probe-json-*.jsonl").OrderBy(f => f))
            d.Add(Path.GetFileName(f));
        return d;
    }

    static (MirrorResult R, JsonNode Line) LoadFixture(string name)
    {
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
        var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Last(l => l.StartsWith('{'));
        return (MirrorResult.Parse(text, 0, 0), JsonNode.Parse(line)!);
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Sortie_reelle_de_la_CLI_lue_par_le_daemon(string fixture)
    {
        var (r, line) = LoadFixture(fixture);
        Assert.Equal(0, r.Exit);
        Assert.Empty(r.Unsupported);
        Assert.Empty(r.UnstableSections);
        Assert.NotNull(r.Run);
        Assert.NotNull(r.Reward);
        Assert.NotNull(r.Event);
        Assert.DoesNotContain("SECRET", line.ToJsonString());
        var scene = fixture["32-probe-json-".Length..^".jsonl".Length];
        switch (scene)
        {
            case "event":
                Assert.Equal("EVENT.BATHS_TEST", r.Event!.EventId);
                Assert.Equal(3, r.Event.Options!.Count);
                Assert.Equal(new bool?[] { true, true, false }, r.Event.Options.Select(o => o.Available));
                Assert.EndsWith(".ABSTAIN", r.Event.Options[1].Id);
                break;
            case "ancient":
                Assert.Equal("EVENT.NEOW_TEST", r.Event!.EventId);
                Assert.Equal(2, r.Event.Options!.Count);
                Assert.True(line["sections"]!["event"]!["value"]!["isAncient"]!.GetValue<bool>());
                break;
            case "event-stale":
                Assert.Equal("EVENT.BATHS_TEST", r.Event!.EventId);
                Assert.Null(r.Event.Options);
                Assert.DoesNotContain("OLD_EVENT_TEST", line.ToJsonString());
                break;
            default:
                Assert.Fail($"scène inattendue : {scene}");
                break;
        }
        // Piles : présentes dans la ligne (le daemon ne les lit pas encore), comptées et triées.
        var draw = line["sections"]!["piles"]!["value"]!["draw"]!;
        Assert.Equal(6, draw["total"]!.GetValue<int>());
        var ids = draw["cards"]!.AsArray().Select(c => c!["card"]!["id"]!.GetValue<string>()).ToList();
        Assert.Equal(ids.OrderBy(x => x, StringComparer.Ordinal), ids);
    }
}
