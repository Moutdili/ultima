using System.Text.Json.Nodes;
using Ultima.Engine.Scoring.Model;
using Ultima.Engine.Scoring.Personal;
using Ultima.Saves;
using Xunit;

namespace Ultima.Engine.Tests.Scoring;

/// <summary>
/// Statistiques personnelles : filtrage build, comptage par étage, co-op, risque par salle,
/// intervalles de confiance et — exigence d'audit — aucune erreur de lecture avalée en silence.
/// Fixtures : fixtures/saves/root/steam/70000000000000001/profile1/saves/history (7 .run).
/// </summary>
public sealed class PersonalStatsTests
{
    private const string SteamId = "70000000000000001";
    private const ulong SteamId64 = 70000000000000001UL;

    private static readonly string HistoryDir = Path.Combine(
        RepoRoot(), "fixtures", "saves", "root", "steam", SteamId, "profile1", "saves", "history");

    // ------------------------------------------------------------ filtrage build

    [Fact]
    public void Load_filters_runs_on_build_id()
    {
        Assert.Equal("v0.107.1", PersonalStats.DefaultBuildId);

        var filtered = PersonalStats.Load(HistoryDir);
        Assert.Equal(7, filtered.SourcesScanned);
        // 5 runs v0.107.1, dont 2 abandonnées (abandon_solo + daily) : seules 3 sont agrégées.
        Assert.Equal(3, filtered.RunCount);
        Assert.Equal(2, filtered.AbandonedRuns);
        Assert.Equal(2, filtered.SkippedOtherBuild);
        Assert.Equal(0, filtered.UnreadableRuns);
        Assert.Equal("v0.107.1", filtered.BuildFilter);

        var all = PersonalStats.Load(HistoryDir, buildId: null);
        // 7 runs, dont 3 abandonnées : 4 agrégées (coop4, death_solo, killed_by_event, win_solo).
        Assert.Equal(4, all.RunCount);
        Assert.Equal(3, all.AbandonedRuns);
        Assert.Equal(0, all.SkippedOtherBuild);
        Assert.Null(all.BuildFilter);

        // Réalité des fixtures : 5 runs v0.107.1, 2 runs v0.103.3 (dont old_build.run).
        var reader = new HistoryReader();
        var builds = HistoryReader.EnumerateRunFiles(HistoryDir).Select(f => reader.Read(f).BuildId).ToList();
        Assert.Equal(5, builds.Count(b => b == "v0.107.1"));
        Assert.Equal(2, builds.Count(b => b == "v0.103.3"));

        Assert.Contains("2 d'un autre build", filtered.Summary);
        Assert.Contains("3 run(s) retenue(s) sur 7", filtered.Summary);
    }

    // ------------------------------------------------------------- cartes

    [Fact]
    public void Known_card_reports_offers_and_picks()
    {
        var stats = PersonalStats.Load(HistoryDir);

        var card = stats.Card("CARD.DEADLY_POISON");
        Assert.NotNull(card);
        Assert.Equal("DEADLY_POISON", card!.CardId);
        Assert.True(card.Offered > 0, $"offres: {card.Offered}");
        Assert.InRange(card.Picked, 0, card.Offered);
        Assert.True(card.RunsWithCard > 0, "carte prise dans au moins une run des fixtures");

        // Le préfixe CARD. est optionnel et la casse indifférente.
        Assert.Equal(card.Offered, stats.Card("deadly_poison")!.Offered);

        // IC cohérents avec les compteurs.
        Assert.True(card.PickRateCi!.Value.Contains(card.PickRate));
        Assert.True(card.WinRateWithCi!.Value.Contains(card.WinRateWith));

        // Carte proposée mais jamais prise : pas d'IC de winrate « avec ».
        var untouched = stats.Card("CARD.UNTOUCHABLE");
        Assert.NotNull(untouched);
        Assert.True(untouched!.Offered > 0);
        Assert.Equal(0, untouched.Picked);
        Assert.Equal(0, untouched.RunsWithCard);
        Assert.Null(untouched.WinRateWithCi);

        Assert.Null(stats.Card("CARD.N_EXISTE_PAS"));
        Assert.Null(stats.Card(""));
    }

    // -------------------------------------------------------------- salles

    [Fact]
    public void Room_risk_by_act_and_room_has_samples_and_basis()
    {
        var stats = PersonalStats.Load(HistoryDir);

        var risk = stats.RoomRisk(0, "monster");
        Assert.True(risk.Samples > 0, $"n={risk.Samples}");
        Assert.True(risk.AverageDamage >= 0);
        Assert.False(string.IsNullOrWhiteSpace(risk.Basis));
        Assert.Contains("n=", risk.Basis);
        Assert.Contains("acte 0", risk.Basis);
        Assert.Contains("history perso", risk.Basis);

        // Normalisation : casse et espaces indifférents.
        Assert.Equal(risk.Samples, stats.RoomRisk(0, " MONSTER ").Samples);
        Assert.Equal(risk.Samples, stats.RoomRisk(0, "monster").Samples);

        // Absence explicite, jamais silencieuse.
        var missing = stats.RoomRisk(99, "monster");
        Assert.Equal(0, missing.Samples);
        Assert.Equal(0, missing.AverageDamage);
        Assert.Contains("aucune donnée", missing.Basis);

        // Moyenne = dégâts cumulés / n (vérifié en brute force sur les fixtures).
        Assert.Equal(ExpectedMonsterDamage(0) / risk.Samples, risk.AverageDamage, 6);
    }

    // --------------------------------------------------------------- vide

    [Fact]
    public void Empty_exposes_explicit_absences()
    {
        var empty = PersonalStats.Empty();

        Assert.Equal(0, empty.RunCount);
        Assert.Null(empty.Card("X"));
        Assert.Null(empty.Card(""));
        Assert.Equal(0, empty.RunsForCharacter("CHARACTER.SILENT"));
        Assert.Empty(empty.LoadErrors);
        Assert.Empty(empty.Cards);

        var risk = empty.RoomRisk(0, "monster");
        Assert.Equal(0, risk.Samples);
        Assert.Equal(0, risk.AverageDamage);
        Assert.False(string.IsNullOrWhiteSpace(risk.Basis));
        Assert.Contains("aucune run chargée", risk.Basis);
    }

    // --------------------------------------------------- autre build ignoré

    [Fact]
    public void Old_build_run_does_not_contribute()
    {
        var filtered = PersonalStats.Load(HistoryDir);
        var all = PersonalStats.Load(HistoryDir, buildId: null);

        // CARD.DISMANTLE n'apparaît que dans old_build.run : autre build ET run abandonnée.
        // Elle ne doit donc contribuer à aucune des deux agrégations.
        Assert.Null(filtered.Card("CARD.DISMANTLE"));
        Assert.Null(all.Card("CARD.DISMANTLE"));
        Assert.Equal(3, all.AbandonedRuns);
        Assert.True(all.RunCount >= filtered.RunCount);

        // Aucun compteur filtré ne dépasse son équivalent tous-builds.
        foreach (var card in filtered.Cards)
        {
            var wide = all.Card(card.CardId);
            Assert.NotNull(wide);
            Assert.True(wide!.Offered >= card.Offered, $"offres {card.CardId}");
            Assert.True(wide.Picked >= card.Picked, $"prises {card.CardId}");
            Assert.True(wide.RunsWithCard + wide.RunsWithoutCard >= card.RunsWithCard + card.RunsWithoutCard,
                $"runs {card.CardId}");
        }

        // Personnages : IRONCLAD n'existe que dans les runs écartées (old_build + coop4 v0.103.3).
        Assert.Equal(0, filtered.RunsForCharacter("CHARACTER.IRONCLAD"));
        Assert.True(all.RunsForCharacter("CHARACTER.IRONCLAD") >= 1);
        // Aucun double comptage : la somme par personnage égale les runs agrégées.
        int perCharacter = filtered.RunsForCharacter("CHARACTER.SILENT")
                         + filtered.RunsForCharacter("CHARACTER.DEFECT")
                         + filtered.RunsForCharacter("CHARACTER.IRONCLAD");
        Assert.Equal(filtered.RunCount, perCharacter);
        Assert.True(filtered.RunsForCharacter("CHARACTER.SILENT") >= 1);

        // Retirer le filtre ajoute du volume (coop4 + old_build) et des cartes inconnues du build courant.
        Assert.True(all.Cards.Sum(c => c.Offered) > filtered.Cards.Sum(c => c.Offered));
        Assert.True(all.Cards.Count(c => filtered.Card(c.CardId) is null) > 0);
    }

    // ------------------------------------------------------- co-op sans double comptage

    [Fact]
    public void Coop_run_counts_only_the_local_player()
    {
        using var tmp = new TempDir();
        File.Copy(Path.Combine(HistoryDir, "coop4.run"), tmp.File("coop4.run"));

        var reader = new HistoryReader();
        var run = reader.Read(tmp.File("coop4.run"));
        Assert.Equal(4, run.Players.Count);

        var stats = PersonalStats.Load(tmp.Path, "v0.103.3", SteamId64);

        Assert.Equal(1, stats.RunCount);
        Assert.Equal(1, stats.RunsForCharacter("CHARACTER.SILENT"));   // joueur local (SteamID64)
        Assert.Equal(0, stats.RunsForCharacter("CHARACTER.IRONCLAD")); // coéquipier, jamais agrégé

        var expected = run.Floors
            .Where(f => f.ActIndex == 0 && IsMonster(f) && f.ForPlayer(SteamId64) is not null)
            .ToArray();
        Assert.True(expected.Length > 0, "fixture coop4 sans salle monster à l'acte 0");

        var risk = stats.RoomRisk(0, "monster");
        Assert.Equal(expected.Length, risk.Samples); // 1 étage = 1 échantillon (pas 1 par joueur)
        Assert.Equal(expected.Sum(f => f.ForPlayer(SteamId64)!.DamageTaken) / (double)expected.Length,
            risk.AverageDamage, 6); // dégâts du joueur local seulement

        // Sans SteamID : repli déterministe sur Players[0] (hôte), toujours un seul joueur.
        var host = PersonalStats.Load(tmp.Path, "v0.103.3");
        Assert.Equal(1, host.RunsForCharacter("CHARACTER.IRONCLAD"));
        Assert.Equal(0, host.RunsForCharacter("CHARACTER.SILENT"));
        Assert.Equal(risk.Samples, host.RoomRisk(0, "monster").Samples);
    }

    // ------------------------------------------- une offre par carte et par étage

    [Fact]
    public void Duplicate_offer_on_same_floor_counts_once()
    {
        using var plainDir = new TempDir();
        using var dupDir = new TempDir();
        var source = Path.Combine(HistoryDir, "win_solo.run");
        File.Copy(source, plainDir.File("run.run"));
        File.Copy(source, dupDir.File("run.run"));

        // Duplique la première carte proposée d'un étage : elle doit rester comptée une seule fois.
        var node = JsonNode.Parse(File.ReadAllText(source))!;
        string? cardId = null;
        foreach (var act in node["map_point_history"]!.AsArray())
        {
            foreach (var floor in act!.AsArray())
            {
                var choices = floor!["player_stats"]?[0]?["card_choices"]?.AsArray();
                if (choices is null || choices.Count == 0) continue;
                cardId = choices[0]!["card"]!["id"]!.GetValue<string>();
                choices.Add(choices[0]!.DeepClone());
                break;
            }
            if (cardId is not null) break;
        }
        Assert.NotNull(cardId);
        File.WriteAllText(dupDir.File("run.run"), node.ToJsonString());

        var plain = PersonalStats.Load(plainDir.Path);
        var dup = PersonalStats.Load(dupDir.Path);

        Assert.Equal(1, plain.RunCount);
        Assert.Equal(1, dup.RunCount);
        Assert.Equal(0, dup.DuplicateRuns);

        var key = Ids.Bare(cardId);
        var run = new HistoryReader().Read(source);
        int expectedOffers = run.Floors.Count(f => f.PlayerStats
            .SelectMany(p => p.CardChoices)
            .Any(c => Ids.Bare(c.Card.Id) == key));

        Assert.True(expectedOffers >= 1);
        Assert.Equal(expectedOffers, plain.Card(key)!.Offered);
        Assert.Equal(expectedOffers, dup.Card(key)!.Offered); // le doublon n'ajoute pas d'offre
        Assert.Equal(plain.Card(key)!.Picked, dup.Card(key)!.Picked);
        Assert.Equal(plain.Cards.Count, dup.Cards.Count);

        foreach (var card in plain.Cards)
            Assert.Equal(card.Offered, dup.Card(card.CardId)!.Offered);
    }

    // --------------------------------------- erreur de lecture comptée, jamais avalée

    [Fact]
    public void Unreadable_run_is_counted_not_silently_skipped()
    {
        using var tmp = new TempDir();
        File.Copy(Path.Combine(HistoryDir, "win_solo.run"), tmp.File("win_solo.run"));
        File.WriteAllText(tmp.File("broken.run"), "{ \"build_id\": \"v0.107.1\", ceci n'est pas du json");

        var stats = PersonalStats.Load(tmp.Path);

        Assert.Equal(2, stats.SourcesScanned);
        Assert.Equal(1, stats.RunCount);
        Assert.Equal(1, stats.UnreadableRuns);
        Assert.Single(stats.LoadErrors);
        Assert.Contains("broken.run", stats.LoadErrors[0]);
        Assert.Contains("illisible", stats.Summary);
        Assert.Contains("1 illisible", stats.Summary);
        Assert.Contains("1 illisible", stats.RoomRisk(0, "monster").Basis);

        // Dossier absent : absence explicite, pas une exception ni un silence.
        var missing = PersonalStats.Load(Path.Combine(tmp.Path, "inexistant"));
        Assert.Equal(0, missing.RunCount);
        Assert.Single(missing.LoadErrors);
        Assert.Contains("introuvable", missing.LoadErrors[0]);
    }

    // ---------------------------------------- doublon de fichier (même start_time)

    [Fact]
    public void Duplicate_start_time_is_counted_once_and_traced()
    {
        using var tmp = new TempDir();
        File.Copy(Path.Combine(HistoryDir, "win_solo.run"), tmp.File("a.run"));
        File.Copy(Path.Combine(HistoryDir, "win_solo.run"), tmp.File("b.run"));

        var stats = PersonalStats.Load(tmp.Path);

        Assert.Equal(2, stats.SourcesScanned);
        Assert.Equal(1, stats.RunCount);
        Assert.Equal(1, stats.DuplicateRuns);
        Assert.Single(stats.LoadErrors);
        Assert.Contains("doublon", stats.LoadErrors[0]);
        Assert.Contains("1 doublon", stats.Summary);
    }

    // ------------------------------------------- invariants globaux des compteurs

    [Fact]
    public void All_card_counters_are_internally_consistent()
    {
        var stats = PersonalStats.Load(HistoryDir);
        Assert.NotEmpty(stats.Cards);

        foreach (var card in stats.Cards)
        {
            Assert.True(card.Offered > 0, card.CardId);
            Assert.InRange(card.Picked, 0, card.Offered);
            Assert.True(card.RunsWithCard + card.RunsWithoutCard <= stats.RunCount,
                $"{card.CardId} : runs avec+sans > {stats.RunCount}");
            Assert.InRange(card.PickRate, 0, 1);
            Assert.InRange(card.WinRateWith, 0, 1);
            Assert.InRange(card.WinRateWithout, 0, 1);
            if (card.RunsWithCard > 0)
            {
                var ci = card.WinRateWithCi!.Value;
                Assert.True(ci.Contains(card.WinRateWith), $"{card.CardId} : {card.WinRateWith} hors de {ci}");
                Assert.InRange(ci.Low, 0.0, 1.0);
                Assert.InRange(ci.High, 0.0, 1.0);
            }
            else
            {
                Assert.Null(card.WinRateWithCi); // jamais prise : pas de taux « avec »
            }
        }
    }

    // ------------------------------------------------------------ IC Wilson

    [Fact]
    public void Wilson_intervals_are_sane_and_thresholded()
    {
        var half = PersonalCardStats.Wilson(5, 10);
        Assert.True(half.Contains(0.5));
        Assert.InRange(half.Low, 0.2, 0.5);
        Assert.InRange(half.High, 0.5, 0.8);

        Assert.Equal(new Interval(0, 1), PersonalCardStats.Wilson(0, 0));
        Assert.Equal(1, PersonalCardStats.Wilson(10, 10).High, 6);
        Assert.True(PersonalCardStats.Wilson(10, 10).Low < 1);
        Assert.Equal(0, PersonalCardStats.Wilson(0, 3).Low, 6);

        // Sous 5 runs d'un côté : pas d'écart, donc pas d'IC d'écart.
        var thin = new PersonalCardStats("X", 4, 2, 2, 0, 1, 1);
        Assert.Null(thin.WinRateDelta);
        Assert.Null(thin.WinRateDeltaCi);
        Assert.Equal(0.5, thin.PickRate, 6);

        // 4 victoires/5 avec la carte, 1 victoire/5 sans : écart 0,6 et IC qui le contient.
        var thick = new PersonalCardStats("X", 20, 10, 4, 1, 1, 4);
        Assert.Equal(5, thick.RunsWithCard);
        Assert.Equal(5, thick.RunsWithoutCard);
        Assert.Equal(0.6, thick.WinRateDelta!.Value, 6);
        var ci = thick.WinRateDeltaCi;
        Assert.NotNull(ci);
        Assert.True(ci!.Value.Contains(0.6));
        Assert.True(ci.Value.Low < ci.Value.High);
        Assert.InRange(ci.Value.Low, -1, 1);
        Assert.InRange(ci.Value.High, -1, 1);
    }

    // ------------------------------------------------------------- helpers

    private static bool IsMonster(Ultima.Saves.Model.FloorRecord floor) =>
        floor.Rooms.Count > 0 && string.Equals(floor.Rooms[0].RoomType, "monster", StringComparison.OrdinalIgnoreCase);

    /// <summary>Somme des DamageTaken des salles monster d'un acte (toutes runs v0.107.1, joueur local).</summary>
    private static double ExpectedMonsterDamage(int actIndex)
    {
        var reader = new HistoryReader();
        double sum = 0;
        foreach (var file in HistoryReader.EnumerateRunFiles(HistoryDir))
        {
            var run = reader.Read(file);
            if (run.BuildId != PersonalStats.DefaultBuildId) continue;
            if (run.WasAbandoned) continue;                       // même règle que la production
            if (!string.IsNullOrEmpty(run.GameMode) &&
                !run.GameMode.Equals("standard", StringComparison.OrdinalIgnoreCase)) continue;
            ulong local = run.Players[0].Id;
            foreach (var floor in run.Floors)
            {
                if (floor.ActIndex != actIndex || !IsMonster(floor)) continue;
                var ps = floor.ForPlayer(local);
                if (ps is not null) sum += ps.DamageTaken;
            }
        }
        return sum;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "fixtures", "saves")) &&
                Directory.Exists(Path.Combine(dir.FullName, "Ultima.Engine")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("fixtures/saves introuvable depuis " + AppContext.BaseDirectory);
    }

    /// <summary>Dossier temporaire jetable (jamais dans le dossier du jeu).</summary>
    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "ultima-engine-personal-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string File(string name) => System.IO.Path.Combine(Path, name);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException e)
            {
                Console.WriteLine($"nettoyage partiel de {Path} : {e.Message}");
            }
        }
    }
}
