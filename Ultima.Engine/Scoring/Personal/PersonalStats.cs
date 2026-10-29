using Ultima.Engine.Scoring.Model;
using Ultima.Saves;
using Ultima.Saves.Model;

namespace Ultima.Engine.Scoring.Personal;

/// <summary>
/// Agrège l'historique personnel (history/*.run) en statistiques par carte et par type de salle.
/// Lecture seule, filtrée sur le build courant (mêmes règles que la méta).
/// TODO agent S2 : tests sur les fixtures + progress.save + intervalles de confiance.
/// </summary>
public sealed class PersonalStats : IPersonalStats
{
    private readonly Dictionary<string, PersonalCardStats> _cards = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(int Act, string Room), RoomRisk> _rooms = new();
    private readonly Dictionary<string, int> _perCharacter = new(StringComparer.OrdinalIgnoreCase);

    public int RunCount { get; private set; }

    public PersonalCardStats? Card(string cardId) =>
        _cards.TryGetValue(Ids.Bare(cardId), out var s) ? s : null;

    public RoomRisk RoomRisk(int actIndex, string roomType) =>
        _rooms.TryGetValue((actIndex, NormalizeRoom(roomType)), out var r)
            ? r
            : new RoomRisk(0, 0, "aucune donnée");

    public int RunsForCharacter(string characterId) =>
        _perCharacter.TryGetValue(Ids.Bare(characterId), out var n) ? n : 0;

    public static PersonalStats Empty() => new();

    /// <summary>Charge un dossier history/. Tolérant : un fichier illisible est ignoré.</summary>
    public static PersonalStats Load(string historyDir, string? buildId = "v0.107.1")
    {
        var stats = new PersonalStats();
        if (!Directory.Exists(historyDir)) return stats;

        var reader = new HistoryReader();
        foreach (var file in HistoryReader.EnumerateRunFiles(historyDir))
        {
            RunRecord run;
            try { run = reader.Read(file); }
            catch { continue; }
            if (buildId != null && !string.Equals(run.BuildId, buildId, StringComparison.OrdinalIgnoreCase))
                continue;
            stats.Add(run);
        }
        return stats;
    }

    private void Add(RunRecord run)
    {
        RunCount++;
        var ch = Ids.Bare(run.Character());
        _perCharacter[ch] = (_perCharacter.TryGetValue(ch, out var n) ? n : 0) + 1;

        foreach (var floor in run.Floors)
        {
            var ps = floor.PlayerStats.Count > 0 ? floor.PlayerStats[0] : null;
            if (ps == null) continue;

            // 1) Choix de cartes : proposée (une fois par étage) et prise ou non.
            foreach (var choice in ps.CardChoices)
            {
                var id = Ids.Bare(choice.Card.Id);
                var s = _cards.TryGetValue(id, out var cur) ? cur : new PersonalCardStats(id, 0, 0, 0, 0, 0, 0);

                int winsPick = s.WinsWhenPicked, lossPick = s.LossesWhenPicked;
                int winsSkip = s.WinsWhenSkipped, lossSkip = s.LossesWhenSkipped;
                if (choice.WasPicked)
                {
                    if (run.Win) winsPick++; else lossPick++;
                }
                else
                {
                    if (run.Win) winsSkip++; else lossSkip++;
                }

                _cards[id] = s with
                {
                    Offered = s.Offered + 1,
                    Picked = s.Picked + (choice.WasPicked ? 1 : 0),
                    WinsWhenPicked = winsPick,
                    LossesWhenPicked = lossPick,
                    WinsWhenSkipped = winsSkip,
                    LossesWhenSkipped = lossSkip
                };
            }

            // 2) Dégâts subis par type de salle de cet étage.
            var roomType = NormalizeRoom(floor.Rooms.Count > 0 ? floor.Rooms[0].RoomType : floor.RawType);
            if (roomType.Length == 0) continue;

            var key = (floor.ActIndex, roomType);
            var prev = _rooms.TryGetValue(key, out var r) ? r : new RoomRisk(0, 0, "");
            double total = prev.AverageDamage * prev.Samples + ps.DamageTaken;
            int samples = prev.Samples + 1;
            _rooms[key] = new RoomRisk(total / samples, samples, "tes runs (history)");
        }
    }

    private static string NormalizeRoom(string? room) =>
        (room ?? string.Empty).Trim().ToLowerInvariant().Replace(' ', '_');
}
