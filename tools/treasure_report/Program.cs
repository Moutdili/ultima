using System.Globalization;
using Ultima.Engine.Contracts;
using Ultima.Engine.Scoring.Treasures;
using Ultima.Saves.Model;

namespace Ultima.Tools.TreasureReport;

/// <summary>
/// Liste les trésors les plus utiles pour une run donnée, par pool réel du jeu.
/// Usage : dotnet run --project tools/treasure_report -- [personnage] [acte] [pv_max] [or]
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        var character = (args.Length > 0 ? args[0] : "SILENT").ToUpperInvariant();
        var act = args.Length > 1 && int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var a) ? Math.Max(1, a) : 1;
        var maxHp = args.Length > 2 && int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var h) ? h : 70;
        var gold = args.Length > 3 && int.TryParse(args[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var g) ? g : 99;

        var index = TreasureIndex.Default;
        if (!index.IsLoaded)
        {
            Console.Error.WriteLine($"index indisponible : {index.Problem}");
            return 1;
        }

        var deck = StartingDeck(character);
        var ctx = new ScoringContext(
            character.StartsWith("CHARACTER.", StringComparison.Ordinal) ? character : $"CHARACTER.{character}",
            act - 1, null, 0, 1 + (act - 1) * 17, deck, Array.Empty<string>(), gold, maxHp, maxHp);

        Console.WriteLine($"Trésors — {character} · acte {act} · {deck.Count} cartes · {gold} or");
        Console.WriteLine($"reliques indexées : {index.RelicCount}");
        Console.WriteLine();

        foreach (var pool in TreasureIndex.Obtainable)
        {
            var ids = index.Ids(pool);
            Console.WriteLine($"{pool,-9} ({ids.Count,3} reliques)");
            foreach (var candidate in index.Rank(ctx, pool, 5))
                Console.WriteLine($"   {candidate.Score,3}  {candidate.Name} [{string.Join(",", candidate.Tags.Take(3))}]");
            Console.WriteLine();
        }

        Console.WriteLine("Meilleurs toutes provenances (hors set de départ) :");
        foreach (var candidate in index.RankAll(ctx, 10))
            Console.WriteLine($"   {candidate.Score,3}  {candidate.Name,-28} {candidate.Pool}");

        return 0;
    }

    /// <summary>Deck de départ par personnage (5 Frappes / 5 Défenses / 1 carte de base).</summary>
    private static IReadOnlyList<CardInstance> StartingDeck(string character)
    {
        var strike = character switch
        {
            "IRONCLAD" => "CARD.STRIKE_IRONCLAD",
            "DEFECT" => "CARD.STRIKE_DEFECT",
            "WATCHER" => "CARD.STRIKE_WATCHER",
            _ => "CARD.STRIKE_SILENT",
        };
        var defend = strike.Replace("STRIKE", "DEFEND");

        var deck = new List<CardInstance>(11);
        for (var i = 0; i < 5; i++) deck.Add(new CardInstance(strike, 0, null, null));
        for (var i = 0; i < 4; i++) deck.Add(new CardInstance(defend, 0, null, null));
        return deck;
    }
}
