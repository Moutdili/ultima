using Ultima.Engine.Scoring.Model;
using Ultima.Saves;
using Ultima.Saves.IO;
using Ultima.Saves.Model;

namespace Ultima.Engine.Scoring.Personal;

/// <summary>
/// Agrège l'historique personnel (<c>history/*.run</c>) en statistiques par carte et par type de salle.
/// <para>
/// Règles de comptage :
/// <list type="bullet">
/// <item>filtrage sur <c>build_id</c> (<see cref="DefaultBuildId"/> par défaut) ;</item>
/// <item>une carte proposée ne compte qu'<b>une fois par étage</b> (doublons d'id dédupliqués) ;</item>
/// <item>co-op : seul le joueur local est agrégé (<c>Players[0]</c> en solo, SteamID64 sinon) ;</item>
/// <item>les compteurs victoire/défaite d'une carte comptent des <b>runs</b>, pas des étages ;</item>
/// <item>dégâts moyens par (acte, type de salle) avec <c>n</c> explicite dans <see cref="RoomRisk.Basis"/>.</item>
/// </list>
/// Rien n'est ignoré en silence : chaque fichier/étage écarté incrémente un compteur public
/// (<see cref="SkippedOtherBuild"/>, <see cref="UnreadableRuns"/>, <see cref="DuplicateRuns"/>,
/// <see cref="RunsWithoutPlayers"/>, <see cref="FloorsWithoutPlayerStats"/>,
/// <see cref="FloorsWithoutRoomType"/>) et, pour les fichiers, laisse une ligne dans
/// <see cref="LoadErrors"/>. <see cref="Summary"/> et <see cref="RoomRisk.Basis"/> disent
/// combien de runs ont été ignorées et pourquoi.
/// </para>
/// </summary>
public sealed class PersonalStats : IPersonalStats
{
    /// <summary>Build ciblé par défaut (patch courant des données méta).</summary>
    public const string DefaultBuildId = "v0.107.1";

    private readonly Dictionary<string, PersonalCardStats> _cards = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(int Act, string Room), RoomAcc> _rooms = new();
    private readonly Dictionary<string, int> _perCharacter = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<long> _seenStartTimes = new();
    private readonly List<string> _loadErrors = new();
    private readonly ulong? _steamId64;

    private PersonalStats(string? buildId, ulong? steamId64)
    {
        BuildFilter = buildId;
        _steamId64 = steamId64;
    }

    // ---------------------------------------------------------------- lecture

    /// <summary>Build retenu au chargement (<c>null</c> = tous les builds).</summary>
    public string? BuildFilter { get; }

    /// <summary>Runs retenues (après filtrage build + doublons).</summary>
    public int RunCount { get; private set; }

    public int Wins { get; private set; }
    public int Losses { get; private set; }
    public int AbandonedRuns { get; private set; }

    /// <summary>Fichiers/runs examinés, filtrage inclus (retenues + ignorées).</summary>
    public int SourcesScanned { get; private set; }

    /// <summary>Runs valides mais d'un autre build : ignorées volontairement.</summary>
    public int SkippedOtherBuild { get; private set; }

    /// <summary>Fichiers illisibles (JSON cassé, ni fichier ni .backup lisible, IO) : comptés, jamais avalés.</summary>
    public int UnreadableRuns { get; private set; }

    /// <summary>Runs dont le <c>start_time</c> a déjà été vu (copie d'un même run).</summary>
    public int DuplicateRuns { get; private set; }

    /// <summary>Runs ignorées faute de joueur (fichier structurellement inexploitable).</summary>
    public int RunsWithoutPlayers { get; private set; }

    /// <summary>Runs retenues mais sans personnage identifiable.</summary>
    public int UnknownCharacterRuns { get; private set; }

    /// <summary>Étages retenus mais sans <c>player_stats</c> pour le joueur local.</summary>
    public int FloorsWithoutPlayerStats { get; private set; }

    /// <summary>Étages sans type de salle exploitable (ni <c>rooms[0].room_type</c> ni <c>map_point_type</c>).</summary>
    public int FloorsWithoutRoomType { get; private set; }

    /// <summary>Entrées de <c>card_choices</c> sans identifiant de carte.</summary>
    public int CardChoicesWithoutId { get; private set; }

    /// <summary>Diagnostics de chargement (fichier : cause). Vide = tout a été lu et retenu.</summary>
    public IReadOnlyList<string> LoadErrors => _loadErrors;

    /// <summary>Toutes les cartes vues, triées par nombre d'offres décroissant puis par id.</summary>
    public IReadOnlyCollection<PersonalCardStats> Cards => _cards.Values
        .OrderByDescending(c => c.Offered)
        .ThenBy(c => c.CardId, StringComparer.Ordinal)
        .ToArray();

    /// <summary>Résumé lisible du périmètre et de tout ce qui a été écarté (jamais vide).</summary>
    public string Summary =>
        $"{RunCount} run(s) retenue(s) sur {SourcesScanned} source(s), build {BuildFilter ?? "tous"}"
        + $" ; {Wins} victoire(s) / {Losses} défaite(s) dont {AbandonedRuns} abandon(s)"
        + IgnoredNote()
        + (FloorsWithoutPlayerStats > 0 ? $" ; {FloorsWithoutPlayerStats} étage(s) sans stats joueur" : "")
        + (FloorsWithoutRoomType > 0 ? $" ; {FloorsWithoutRoomType} étage(s) sans type de salle" : "")
        + (CardChoicesWithoutId > 0 ? $" ; {CardChoicesWithoutId} choix de carte sans id" : "");

    // ------------------------------------------------------------ accesseurs

    public PersonalCardStats? Card(string cardId)
    {
        var id = Ids.Bare(cardId);
        return id.Length > 0 && _cards.TryGetValue(id, out var s) ? s : null;
    }

    /// <summary>Dégâts moyens par acte et type de salle ; <c>Basis</c> porte toujours le n et le périmètre.</summary>
    public RoomRisk RoomRisk(int actIndex, string roomType)
    {
        var room = NormalizeRoom(roomType);
        var label = room.Length > 0 ? room : "?";
        if (_rooms.TryGetValue((actIndex, room), out var acc) && acc.N > 0)
        {
            return new RoomRisk(acc.Sum / acc.N, acc.N,
                $"history perso : n={acc.N} étage(s) « {label} » acte {actIndex}{ScopeNote()}{IgnoredNote()}");
        }

        return new RoomRisk(0, 0,
            $"aucune donnée pour l'acte {actIndex} / « {label} »{ScopeNote()}{IgnoredNote()}");
    }

    public int RunsForCharacter(string characterId)
    {
        var key = Ids.Bare(characterId);
        return key.Length > 0 && _perCharacter.TryGetValue(key, out var n) ? n : 0;
    }

    /// <summary>Statistiques vides : toutes les lectures renvoient une absence explicite.</summary>
    public static PersonalStats Empty() => new(DefaultBuildId, null);

    // -------------------------------------------------------------- chargement

    /// <summary>
    /// Charge un dossier <c>history/</c>. Un fichier illisible n'interrompt pas le chargement mais il est
    /// <b>compté</b> (<see cref="UnreadableRuns"/>) et décrit dans <see cref="LoadErrors"/>.
    /// </summary>
    /// <param name="historyDir">Dossier contenant les <c>*.run</c>.</param>
    /// <param name="buildId">Build à retenir ; <c>null</c> = tous les builds.</param>
    /// <param name="steamId64">Compte Steam, pour choisir le joueur local en co-op (facultatif).</param>
    public static PersonalStats Load(string historyDir, string? buildId = DefaultBuildId, ulong? steamId64 = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(historyDir);

        var stats = new PersonalStats(buildId, steamId64);
        if (!Directory.Exists(historyDir))
        {
            stats._loadErrors.Add($"{historyDir} : dossier d'historique introuvable");
            return stats;
        }

        IReadOnlyList<string> files;
        try
        {
            files = HistoryReader.EnumerateRunFiles(historyDir);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            stats._loadErrors.Add($"{historyDir} : énumération impossible ({Describe(e)})");
            return stats;
        }

        var reader = new HistoryReader();
        foreach (var file in files)
        {
            stats.SourcesScanned++;
            try
            {
                stats.Accept(reader.Read(file));
            }
            catch (Exception e) when (e is SaveReadException or IOException or InvalidDataException
                                          or System.Text.Json.JsonException or UnauthorizedAccessException)
            {
                stats.UnreadableRuns++;
                stats._loadErrors.Add($"{Path.GetFileName(file)} : {Describe(e)}");
            }
        }
        return stats;
    }

    /// <summary>Variante sans I/O (tests, runs déjà en mémoire). Aucune erreur de lecture possible.</summary>
    public static PersonalStats FromRuns(IEnumerable<RunRecord> runs, string? buildId = DefaultBuildId,
        ulong? steamId64 = null)
    {
        ArgumentNullException.ThrowIfNull(runs);

        var stats = new PersonalStats(buildId, steamId64);
        foreach (var run in runs.OrderBy(r => r.StartTime))
        {
            stats.SourcesScanned++;
            stats.Accept(run);
        }
        return stats;
    }

    private void Accept(RunRecord run)
    {
        var name = string.IsNullOrWhiteSpace(run.FileName) ? "(run mémoire)" : run.FileName;

        if (run.Players.Count == 0)
        {
            RunsWithoutPlayers++;
            _loadErrors.Add($"{name} : run ignorée (aucun joueur)");
            return;
        }

        if (BuildFilter is not null && !string.Equals(run.BuildId, BuildFilter, StringComparison.OrdinalIgnoreCase))
        {
            SkippedOtherBuild++;
            return;
        }

        if (run.StartTime > 0 && !_seenStartTimes.Add(run.StartTime))
        {
            DuplicateRuns++;
            _loadErrors.Add($"{name} : run ignorée (doublon start_time={run.StartTime})");
            return;
        }

        RunCount++;
        if (run.Win) Wins++; else Losses++;
        if (run.WasAbandoned) AbandonedRuns++;

        var characterKey = Ids.Bare(run.Character(_steamId64));
        if (characterKey.Length == 0) UnknownCharacterRuns++;
        else _perCharacter[characterKey] = (_perCharacter.TryGetValue(characterKey, out var n) ? n : 0) + 1;

        ulong localPlayerId = LocalPlayerId(run);

        // Agrégation par run : les compteurs victoire/défaite ne comptent qu'une fois par run.
        var offeredFloors = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var pickedFloors = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var pickedAtLeastOnce = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var floor in run.Floors)
        {
            var ps = StatsFor(floor, localPlayerId);
            if (ps is null)
            {
                FloorsWithoutPlayerStats++;
                continue;
            }

            // 1) Cartes proposées : au plus une fois par étage et par carte.
            var seenOnFloor = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var choice in ps.CardChoices)
            {
                var id = Ids.Bare(choice.Card.Id);
                if (id.Length == 0)
                {
                    CardChoicesWithoutId++;
                    continue;
                }
                if (!seenOnFloor.Add(id)) continue;

                offeredFloors[id] = (offeredFloors.TryGetValue(id, out var o) ? o : 0) + 1;
                if (!choice.WasPicked) continue;

                pickedFloors[id] = (pickedFloors.TryGetValue(id, out var p) ? p : 0) + 1;
                pickedAtLeastOnce.Add(id);
            }

            // 2) Dégâts subis dans la salle de cet étage.
            var roomType = NormalizeRoom(floor.Rooms.Count > 0 ? floor.Rooms[0].RoomType : floor.RawType);
            if (roomType.Length == 0)
            {
                FloorsWithoutRoomType++;
                continue;
            }

            var key = (floor.ActIndex, roomType);
            _rooms[key] = _rooms.TryGetValue(key, out var acc)
                ? acc.Add(ps.DamageTaken)
                : new RoomAcc(ps.DamageTaken, 1);
        }

        // Fusion dans les totaux : Offres/prises = occasions, victoires/défaites = runs.
        foreach (var (id, offered) in offeredFloors)
        {
            var s = _cards.TryGetValue(id, out var cur) ? cur : new PersonalCardStats(id, 0, 0, 0, 0, 0, 0);
            bool picked = pickedAtLeastOnce.Contains(id);
            int pickedHere = pickedFloors.TryGetValue(id, out var pf) ? pf : 0;

            _cards[id] = s with
            {
                Offered = s.Offered + offered,
                Picked = s.Picked + pickedHere,
                WinsWhenPicked = s.WinsWhenPicked + (picked && run.Win ? 1 : 0),
                LossesWhenPicked = s.LossesWhenPicked + (picked && !run.Win ? 1 : 0),
                WinsWhenSkipped = s.WinsWhenSkipped + (!picked && run.Win ? 1 : 0),
                LossesWhenSkipped = s.LossesWhenSkipped + (!picked && !run.Win ? 1 : 0),
            };
        }
    }

    /// <summary>Solo : l'unique joueur. Co-op : le joueur dont l'id == SteamID64, sinon le premier (hôte).</summary>
    private ulong LocalPlayerId(RunRecord run)
    {
        if (run.Players.Count == 1) return run.Players[0].Id;
        if (_steamId64 is ulong steamId && run.LocalPlayer(steamId) is { } local) return local.Id;
        return run.Players[0].Id;
    }

    /// <summary>
    /// Stats du joueur local pour cet étage. Pas de repli sur un autre joueur en co-op :
    /// mieux vaut compter un étage manquant que d'attribuer les dégâts/cartes d'un coéquipier au joueur local.
    /// </summary>
    private static PlayerFloorStats? StatsFor(FloorRecord floor, ulong localPlayerId)
    {
        var ps = floor.ForPlayer(localPlayerId);
        if (ps is not null) return ps;
        return floor.PlayerStats.Count == 1 ? floor.PlayerStats[0] : null;
    }

    private string ScopeNote() =>
        RunCount > 0
            ? $" ({RunCount} run(s), build {BuildFilter ?? "tous"})"
            : " (aucune run chargée)";

    /// <summary>Dit explicitement ce qui a été écarté et pourquoi (vide si rien).</summary>
    private string IgnoredNote()
    {
        var parts = new List<string>(5);
        if (SkippedOtherBuild > 0) parts.Add($"{SkippedOtherBuild} d'un autre build");
        if (UnreadableRuns > 0) parts.Add($"{UnreadableRuns} illisible(s)");
        if (DuplicateRuns > 0) parts.Add($"{DuplicateRuns} doublon(s)");
        if (RunsWithoutPlayers > 0) parts.Add($"{RunsWithoutPlayers} sans joueur");
        if (UnknownCharacterRuns > 0) parts.Add($"{UnknownCharacterRuns} sans personnage");
        return parts.Count == 0 ? " ; aucun fichier ignoré" : " ; ignorés : " + string.Join(", ", parts);
    }

    private static string Describe(Exception e) => $"{e.GetType().Name}: {e.Message}";

    /// <summary>Normalise un type de salle (casse, espaces, tirets + synonymes du jeu).</summary>
    internal static string NormalizeRoom(string? room)
    {
        var s = (room ?? string.Empty).Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        while (s.Contains("__", StringComparison.Ordinal)) s = s.Replace("__", "_", StringComparison.Ordinal);
        return s switch
        {
            "rest" or "restsite" or "rest_sites" => "rest_site",
            "elite_monster" or "elitemonster" => "elite",
            "monster_fight" or "monsters" => "monster",
            "merchant" => "shop",
            _ => s,
        };
    }

    /// <summary>Accumulateur exact (somme + n) : la moyenne n'est calculée qu'à la lecture.</summary>
    private sealed class RoomAcc
    {
        public RoomAcc(double sum, int n)
        {
            Sum = sum;
            N = n;
        }

        public double Sum { get; }
        public int N { get; }

        public RoomAcc Add(int damage) => new(Sum + damage, N + 1);
    }
}
