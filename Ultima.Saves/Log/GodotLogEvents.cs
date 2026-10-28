using System.Globalization;
using System.Text.RegularExpressions;
using Ultima.Saves.Model;

namespace Ultima.Saves.Log;

/// <summary>
/// Evenement type extrait de <c>logs/godot.log</c>. Le log n'a PAS d'horodatage par ligne :
/// <c>ReceivedAt</c> est l'heure de lecture par ULTIMA.
/// </summary>
public abstract record GodotLogEvent(DateTimeOffset ReceivedAt, string RawLine);

/// <summary>Type de salle annonce par le prechargement (Core/Assets/PreloadManager.cs:101-127).</summary>
public enum RoomKind
{
    Other,
    Combat,
    Event,
    Treasure,
    Merchant,
    RestSite,
}

/// <summary><c>Preloading '&lt;X&gt; Room' assets...</c> — entree dans une salle.</summary>
public sealed record RoomEntered(DateTimeOffset ReceivedAt, string RawLine, RoomKind Kind, string RoomName)
    : GodotLogEvent(ReceivedAt, RawLine);

/// <summary>
/// <c>Creating NCombatRoom with mode=&lt;M&gt; encounter=&lt;E&gt;.</c> (NCombatRoom.cs:174). L'identifiant
/// est normalise au format des saves (<c>ENCOUNTER.&lt;E&gt;</c>).
/// </summary>
public sealed record CombatRoomCreated(DateTimeOffset ReceivedAt, string RawLine, string EncounterId, string Mode)
    : GodotLogEvent(ReceivedAt, RawLine)
{
    public bool IsActiveCombat => Mode == "ActiveCombat";
}

/// <summary>
/// <c>Card rarity: Rolled …</c> (classe de rarete de carte du jeu, ligne 100) : un tirage de rarete de carte vient d'avoir lieu
/// (un par carte proposee). Les valeurs tirees et l'offset de rarete sont VOLONTAIREMENT non exposes :
/// l'offset est un etat cache du jeu (decision « probabiliste seulement », docs/20 §8.2).
/// </summary>
public sealed record CardRewardRolled(DateTimeOffset ReceivedAt, string RawLine) : GodotLogEvent(ReceivedAt, RawLine);

/// <summary><c>Player &lt;netId&gt; selected card reward</c> (CardReward.cs:185).</summary>
public sealed record CardRewardSelected(DateTimeOffset ReceivedAt, string RawLine, ulong PlayerNetId)
    : GodotLogEvent(ReceivedAt, RawLine);

/// <summary>
/// <c>Player vote changed for &lt;netId&gt;: &lt;old&gt;-&gt;&lt;new&gt;</c> (NMapScreen.cs:461) ; chaque cote vaut
/// <c>MapVote (gen: G coord: (c, r))</c> (MapVote.cs:38) ou vide.
/// </summary>
public sealed record MapVote(DateTimeOffset ReceivedAt, string RawLine, ulong PlayerNetId,
    MapCoord? From, MapCoord? To, int? Generation) : GodotLogEvent(ReceivedAt, RawLine);

public enum SaveFileKind
{
    Other,
    CurrentRun,
    CurrentRunMultiplayer,
    Progress,
    History,
    Profile,
    Settings,
    Prefs,
}

/// <summary>
/// <c>Wrote N bytes to path=user://… save_dir=…</c> (GodotFileIo.cs:121/144) : fichier local termine
/// (apres renommage). La ligne doublon « in steam remote store » (Cloud) est ignoree.
/// </summary>
public sealed record SaveWritten(DateTimeOffset ReceivedAt, string RawLine, long Bytes, string GamePath,
    SaveFileKind Kind, bool IsModdedProfile) : GodotLogEvent(ReceivedAt, RawLine);

/// <summary><c>Continuing run with character: CHARACTER.X</c> (NMainMenu.cs:405).</summary>
public sealed record RunContinued(DateTimeOffset ReceivedAt, string RawLine, string CharacterId)
    : GodotLogEvent(ReceivedAt, RawLine);

/// <summary>Analyse ligne a ligne. Formats prouves par les logs reels et le source (voir SCHEMA.md).</summary>
public static partial class GodotLogParser
{
    [GeneratedRegex(@"^Preloading '(?<name>[A-Za-z]+) Room' assets\.\.\.")]
    private static partial Regex PreloadRx();

    [GeneratedRegex(@"^Creating NCombatRoom with mode=(?<mode>\w+) encounter=(?<enc>[A-Z0-9_]+)\.?$")]
    private static partial Regex CombatRx();

    [GeneratedRegex(@"^Card rarity: Rolled ")]
    private static partial Regex RarityRx();

    [GeneratedRegex(@"^Player (?<id>\d+) selected card reward$")]
    private static partial Regex RewardRx();

    [GeneratedRegex(@"^Player vote changed for (?<id>\d+): (?<old>.*)->(?<new>.*)$")]
    private static partial Regex VoteRx();

    [GeneratedRegex(@"^MapVote \(gen: (?<gen>-?\d+) coord: \((?<c>-?\d+), (?<r>-?\d+)\)\)$")]
    private static partial Regex VoteValueRx();

    [GeneratedRegex(@"^Wrote (?<n>\d+) bytes to path=(?<path>\S+) save_dir=")]
    private static partial Regex WroteRx();

    [GeneratedRegex(@"^Continuing run with character: (?<c>\S+)$")]
    private static partial Regex ContinueRx();

    [GeneratedRegex(@"^\[(?:INFO|WARN|ERROR|DEBUG|VERY_DEBUG|LOAD)\] ")]
    private static partial Regex LevelRx();

    /// <summary>Evenement reconnu, ou null.</summary>
    public static GodotLogEvent? ParseLine(string line, DateTimeOffset receivedAt)
    {
        if (string.IsNullOrEmpty(line)) return null;
        string raw = line.TrimEnd('\r');
        var lvl = LevelRx().Match(raw);
        string msg = lvl.Success ? raw[lvl.Length..] : raw;

        Match m;
        if ((m = PreloadRx().Match(msg)).Success)
        {
            var name = m.Groups["name"].Value;
            return new RoomEntered(receivedAt, raw, RoomKindOf(name), name + " Room");
        }
        if ((m = CombatRx().Match(msg)).Success)
            return new CombatRoomCreated(receivedAt, raw, "ENCOUNTER." + m.Groups["enc"].Value, m.Groups["mode"].Value);
        if (RarityRx().IsMatch(msg))
            return new CardRewardRolled(receivedAt, raw);
        if ((m = RewardRx().Match(msg)).Success && ulong.TryParse(m.Groups["id"].Value, out var rid))
            return new CardRewardSelected(receivedAt, raw, rid);
        if ((m = VoteRx().Match(msg)).Success && ulong.TryParse(m.Groups["id"].Value, out var vid))
        {
            var (from, _) = Vote(m.Groups["old"].Value);
            var (to, gen) = Vote(m.Groups["new"].Value);
            return new MapVote(receivedAt, raw, vid, from, to, gen);
        }
        if ((m = WroteRx().Match(msg)).Success && long.TryParse(m.Groups["n"].Value, out var n))
        {
            var path = m.Groups["path"].Value;
            return new SaveWritten(receivedAt, raw, n, path, KindOf(path), path.Contains("/modded/", StringComparison.Ordinal));
        }
        if ((m = ContinueRx().Match(msg)).Success)
            return new RunContinued(receivedAt, raw, m.Groups["c"].Value);
        return null;
    }

    private static (MapCoord? Coord, int? Gen) Vote(string s)
    {
        var m = VoteValueRx().Match(s.Trim());
        if (!m.Success) return (null, null);
        return (new MapCoord(Int(m.Groups["c"].Value), Int(m.Groups["r"].Value)), Int(m.Groups["gen"].Value));
    }

    private static int Int(string s) => int.Parse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    internal static RoomKind RoomKindOf(string name) => name switch
    {
        "Combat" => RoomKind.Combat,
        "Event" => RoomKind.Event,
        "Treasure" => RoomKind.Treasure,
        "Merchant" => RoomKind.Merchant,
        "RestSite" => RoomKind.RestSite,
        _ => RoomKind.Other,
    };

    internal static SaveFileKind KindOf(string path)
    {
        var file = path[(path.LastIndexOf('/') + 1)..];
        if (file == "current_run.save") return SaveFileKind.CurrentRun;
        if (file == "current_run_mp.save") return SaveFileKind.CurrentRunMultiplayer;
        if (file == "progress.save") return SaveFileKind.Progress;
        if (file == "profile.save") return SaveFileKind.Profile;
        if (file == "settings.save") return SaveFileKind.Settings;
        if (file == "prefs.save") return SaveFileKind.Prefs;
        if (file.EndsWith(".run", StringComparison.Ordinal) && path.Contains("/history/", StringComparison.Ordinal))
            return SaveFileKind.History;
        return SaveFileKind.Other;
    }
}
