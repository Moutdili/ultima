using System.Text.Json;
using Ultima.Mirror;

// ultima-mirror — read-only memory mirror probe (spike S2). See docs/mirror/*.md.
// Exit codes: 0 ok, 2 game not running, 3 attach failed, 4 layout drift, 1 usage.

var cmd = args.Length > 0 ? args[0] : "help";
string? Opt(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
var json = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

switch (cmd)
{
    case "layout": return Layout();
    case "probe": return Probe();
    case "self-check": return SelfCheck();
    default:
        Console.WriteLine("""
            ultima-mirror layout [--dll PATH]            vérifie ObjectPaths contre la métadonnée de sts2.dll (sans le jeu)
            ultima-mirror probe [--pid N] [--dac PATH] [--comm NAME] [--count TYPE] [--sections run,reward,shop,combat] [--json]
                                                         s'attache en LECTURE SEULE (suspend:false) et lit l'état
            ultima-mirror self-check                     liste blanche vs champs interdits
            """);
        return 1;
}

int SelfCheck()
{
    var v = ReadGuard.WhitelistViolations().ToList();
    Console.WriteLine($"champs en liste blanche : {ObjectPaths.All.Count}");
    Console.WriteLine($"fragments interdits     : {string.Join(", ", ObjectPaths.ForbiddenFieldFragments)}");
    Console.WriteLine(v.Count == 0 ? "OK : aucun champ de la liste blanche ne correspond à un fragment interdit" : "VIOLATION : " + string.Join(", ", v));
    foreach (var probe in new[] { "<PlayerRng>k__BackingField", "<RelicGrabBag>k__BackingField", "<Odds>k__BackingField", "<DrawPile>k__BackingField", "_somethingElse" })
    {
        try { ReadGuard.Check(probe); Console.WriteLine($"ÉCHEC : {probe} accepté"); return 4; }
        catch (ForbiddenReadException e) { Console.WriteLine($"refusé comme attendu : {e.Message}"); }
    }
    return v.Count == 0 ? 0 : 4;
}

int Layout()
{
    var dll = Opt("--dll") ?? GameProcess.DefaultSts2Dll;
    if (!File.Exists(dll)) { Console.WriteLine($"sts2.dll introuvable : {dll}"); return 1; }
    Console.WriteLine($"assembly : {dll}");
    Console.WriteLine($"sha256   : {Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(dll))).ToLowerInvariant()}");
    var checks = StaticLayout.Verify(dll);
    int bad = 0;
    foreach (var c in checks)
    {
        var ok = c.TypeFound && c.FieldFound && c.StaticMatches;
        if (!ok) bad++;
        var status = ok ? "OK  " : !c.TypeFound ? "TYPE?" : !c.FieldFound ? "FIELD?" : "KIND?";
        Console.WriteLine($"{status} {(c.Spec.IsStatic ? "static " : "")}{c.Spec.Type}::{c.Spec.Field}   [{c.Spec.Source}]");
    }
    Console.WriteLine($"{checks.Count - bad}/{checks.Count} chemins présents dans la métadonnée");
    return bad == 0 ? 0 : 4;
}

int Probe()
{
    int pid;
    var comm = Opt("--comm") ?? GameProcess.GameComm;
    if (Opt("--pid") is { } p) pid = int.Parse(p);
    else
    {
        var found = GameProcess.FindByComm(comm);
        if (found.Count == 0)
        {
            Console.WriteLine($"jeu non lancé : aucun processus dont /proc/PID/comm == \"{comm}\" (mode dégradé : fichiers seulement)");
            return 2;
        }
        if (found.Count > 1) Console.WriteLine($"attention : {found.Count} processus \"{comm}\" ({string.Join(",", found)}), on prend le premier");
        pid = found[0];
    }
    if (!Directory.Exists($"/proc/{pid}")) { Console.WriteLine($"jeu non lancé : PID {pid} absent"); return 2; }

    var target = GameProcess.Describe(pid, Opt("--dac"));
    Console.WriteLine($"pid            : {target.Pid} (comm={target.Comm})");
    Console.WriteLine($"libcoreclr.so  : {target.CoreClrPath ?? "(absent des maps : pas un processus .NET ?)"}");
    Console.WriteLine($"DAC            : {target.DacPath ?? "-"}  [{target.DacSource}]");
    if (target.DacPath is null) return 3;

    MirrorSession session;
    var sw = System.Diagnostics.Stopwatch.StartNew();
    try { session = MirrorSession.Attach(pid, target.DacPath); }
    catch (Exception e)
    {
        Console.WriteLine($"ÉCHEC d'attache ({e.GetType().Name}) : {e.Message}");
        return 3;
    }
    using (session)
    {
        Console.WriteLine($"attache        : OK en {sw.ElapsedMilliseconds} ms (suspend:false, lecture process_vm_readv)");
        Console.WriteLine($"runtime        : {session.Clr.Flavor} {session.Clr.Version}");
        Console.WriteLine($"modules        : {session.Runtime.EnumerateModules().Count()} ; tas : {(session.Runtime.Heap.IsServer ? "server" : "workstation")}, segments={session.Runtime.Heap.Segments.Length}");
        var reader = new GameReader(session);

        if (Opt("--count") is { } typeName)
        {
            sw.Restart();
            var (n, complete) = reader.CountObjects(typeName);
            Console.WriteLine($"objets {typeName} : {n}{(complete ? "" : " (parcours incomplet)")} en {sw.ElapsedMilliseconds} ms (inclut les objets morts non collectés)");
        }

        var sections = (Opt("--sections") ?? "run,reward,shop,combat").Split(',');
        int unstable = 0;
        void Show<T>(string name, Func<T> read)
        {
            if (!sections.Contains(name)) return;
            sw.Restart();
            StableResult<T> r;
            try { r = Stable.Read(session, read); }
            catch (ForbiddenReadException e) { Console.WriteLine($"[{name}] REFUS ReadGuard : {e.Message}"); unstable++; return; }
            Console.WriteLine($"[{name}] {(r.Stable ? "stable" : "INCONNU")} après {r.Attempts} lecture(s), {sw.ElapsedMilliseconds} ms{(r.Error is null ? "" : " — " + r.Error)}");
            if (!r.Stable) { unstable++; return; }
            Console.WriteLine(JsonSerializer.Serialize(r.Value, json));
        }
        Show("run", reader.ReadRun);
        Show("reward", reader.ReadRewards);
        Show("shop", reader.ReadShop);
        Show("combat", reader.ReadCombat);
        return 0;
    }
}
