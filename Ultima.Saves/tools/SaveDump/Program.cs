using Ultima.Saves;
using Ultima.Saves.Log;
using Ultima.Saves.Model;

// ultima-saves run [<chemin current_run.save>]   -> etat de la run (profil actif si pas de chemin)
// ultima-saves history [<dossier history>]       -> resume des runs terminees
// ultima-saves progress                          -> stats par personnage
// ultima-saves log [<godot.log>]                 -> evenements du log (contenu existant)
// ultima-saves profile                           -> chemins resolus
var cmd = args.Length > 0 ? args[0] : "run";
var arg = args.Length > 1 ? args[1] : null;

switch (cmd)
{
    case "profile":
    {
        var p = ProfileLocator.Locate();
        Console.WriteLine($"steam={Mask(p.SteamId)} profile={p.ProfileId} (defaut={p.ProfileIdFromDefault})");
        Console.WriteLine($"saves={p.SavesDir.Replace(p.SteamId, Mask(p.SteamId))}");
        break;
    }
    case "run":
    {
        RunState? s;
        ulong? me = null;
        if (arg is null)
        {
            var p = ProfileLocator.Locate();
            me = p.SteamId64;
            s = new RunSaveReader().ReadCurrent(p);
        }
        else s = new RunSaveReader().TryRead(arg);
        if (s is null) { Console.WriteLine("Aucune run en cours."); return 0; }
        PrintRun(s, me);
        break;
    }
    case "history":
    {
        var dir = arg ?? ProfileLocator.Locate().HistoryDir;
        int errors = 0;
        var runs = new HistoryReader().ReadAll(dir, (f, e) => { errors++; Console.WriteLine($"ERREUR {Path.GetFileName(f)}: {e.Message}"); });
        Console.WriteLine($"runs={runs.Count} erreurs={errors}");
        foreach (var g in runs.GroupBy(r => r.BuildId).OrderBy(g => g.Key))
            Console.WriteLine($"  build {g.Key}: {g.Count()} runs, {g.Count(r => r.Win)} victoires, {g.Count(r => r.WasAbandoned)} abandons");
        foreach (var g in runs.GroupBy(r => r.Character()).OrderBy(g => g.Key))
            Console.WriteLine($"  {g.Key}: {g.Count()} runs (solo={g.Count(r => !r.IsMultiplayer)})");
        break;
    }
    case "progress":
    {
        var st = new ProgressReader().TryRead(ProfileLocator.Locate());
        if (st is null) { Console.WriteLine("progress.save absent"); return 0; }
        foreach (var c in st.Characters)
            Console.WriteLine($"{c.Id,-24} V={c.TotalWins,3} D={c.TotalLosses,3} ascMax={c.MaxAscension}");
        break;
    }
    case "log":
    {
        var path = arg ?? ProfileLocator.Locate().GodotLog;
        using var tail = new GodotLogTail(path, fromStart: true);
        tail.EventReceived += (_, e) => Console.WriteLine(Describe(e));
        tail.Poll();
        break;
    }
    default:
        Console.Error.WriteLine("usage: ultima-saves run|history|progress|log|profile [chemin]");
        return 2;
}
return 0;

static string Mask(string s) => s.Length > 4 ? new string('*', s.Length - 4) + s[^4..] : s;

static string Describe(GodotLogEvent e) => e switch
{
    RoomEntered r => $"RoomEntered {r.Kind}",
    CombatRoomCreated c => $"CombatRoomCreated {c.EncounterId} mode={c.Mode}",
    CardRewardRolled => "CardRewardRolled",
    CardRewardSelected s => $"CardRewardSelected player={(s.PlayerNetId > 1000 ? "<steam>" : s.PlayerNetId.ToString())}",
    MapVote v => $"MapVote player={(v.PlayerNetId > 1000 ? "<steam>" : v.PlayerNetId.ToString())} {v.From}->{v.To} gen={v.Generation}",
    SaveWritten w => $"SaveWritten {w.Kind} {w.Bytes}B modded={w.IsModdedProfile}",
    RunContinued c => $"RunContinued {c.CharacterId}",
    _ => e.GetType().Name,
};

static void PrintRun(RunState s, ulong? me)
{
    Console.WriteLine($"fichier      : {Path.GetFileName(s.SourcePath)} (backup={s.ReadFromBackup}, mp={s.IsMultiplayerSave})");
    Console.WriteLine($"schema       : {s.SchemaVersion}  mode={s.GameMode}  ascension={s.Ascension}");
    var act = s.CurrentAct;
    Console.WriteLine($"acte courant : {s.CurrentActIndex} {act?.Id}  boss={act?.BossId}");
    Console.WriteLine($"position     : {(s.CurrentCoord?.ToString() ?? "-")}  visites={string.Join(" ", s.VisitedMapCoords)}");
    Console.WriteLine($"etages joues : {s.FloorCount}");
    foreach (var p in s.Players)
    {
        bool local = s.LocalPlayer(me) == p;
        Console.WriteLine($"joueur {(p.NetId > 1000 ? "<steam>" : p.NetId.ToString())}{(local ? " (local)" : "")} : {p.CharacterId}");
        Console.WriteLine($"  PV {p.CurrentHp}/{p.MaxHp}  or {p.Gold}  energie max {p.MaxEnergy}  potions {p.Potions.Count}/{p.MaxPotionSlotCount}");
        Console.WriteLine($"  deck ({p.Deck.Count}) : {string.Join(", ", p.Deck.GroupBy(c => c.ToString()).Select(g => g.Count() > 1 ? $"{g.Key} x{g.Count()}" : g.Key))}");
        Console.WriteLine($"  reliques ({p.Relics.Count}) : {string.Join(", ", p.Relics.Select(r => r.Id))}");
        if (p.Potions.Count > 0) Console.WriteLine($"  potions : {string.Join(", ", p.Potions.Select(x => $"[{x.SlotIndex}] {x.Id}"))}");
    }
    foreach (var a in s.Acts)
    {
        var m = a.Map;
        string mapInfo = m is null ? "carte non generee" :
            $"carte {m.Width}x{m.Height}, {m.Points.Count} cases, depart {m.Start.Coord}, boss {m.Boss.Coord}; " +
            string.Join(" ", m.Points.GroupBy(x => x.Kind).OrderBy(g => g.Key).Select(g => $"{g.Key}={g.Count()}"));
        Console.WriteLine($"acte {a.Index} {a.Id} boss={a.BossId} ancien={a.AncientId} : {mapInfo}");
    }
    foreach (var f in s.Floors)
    {
        var ps = f.PlayerStats.FirstOrDefault();
        Console.WriteLine($"etage {f.Floor} (acte {f.ActIndex}) {f.Kind} {f.ModelId} degats={ps?.DamageTaken} " +
            $"proposees=[{string.Join(", ", ps?.CardsOffered.Select(c => c.ToString()) ?? [])}] " +
            $"prises=[{string.Join(", ", ps?.CardsPicked.Select(c => c.ToString()) ?? [])}] " +
            $"reliques=[{string.Join(", ", ps?.RelicsPicked ?? [])}]");
    }
}
