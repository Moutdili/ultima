using System.Text.Json;
using System.Text.Json.Nodes;
using Ultima.Mirror;

// ultima-mirror — read-only memory mirror probe (spike S2). See docs/mirror/*.md.
// Exit codes: 0 ok, 2 game not running, 3 attach failed, 4 layout drift, 5 watchdog, 6 save≠mémoire, 7 section INCONNUE, 1 usage.
//
// --json (Câbleur, docs/ipc/README.md) : exactement UNE ligne JSON sur stdout par appel, aucun autre texte ;
// le code de sortie est inchangé. Schéma : { v, cmd, exit, ms, error, health{…}, sections{name:{stable,attempts,ms,error,value}},
// unsupported[], compare }. « event » et « piles » (Miroir 2) : lues si GameReader.ReadEvent()/ReadPiles() existent.

var cmd = args.Length > 0 ? args[0] : "help";
string? Opt(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
bool jsonMode = args.Contains("--json");
var json = new JsonSerializerOptions { WriteIndented = !args.Contains("--compact"), Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
var jsonLine = new JsonSerializerOptions(JsonSerializerOptions.Web) { WriteIndented = false, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
var started = System.Diagnostics.Stopwatch.StartNew();

// Sections : lues par la bibliothèque actuelle, et sections annoncées par Miroir 2 (event, piles).
string[] KnownSections = { "run", "reward", "shop", "combat" };
string[] FutureSections = { "event", "piles" };

// Sortie JSON : un seul objet, émis une seule fois (le chien de garde et le chemin normal se le disputent).
var outObj = new JsonObject { ["v"] = 1, ["cmd"] = cmd };
var emitGate = new object();
bool emitted = false;
void Say(string line) { if (!jsonMode) Console.WriteLine(line); }
int Finish(int code, string? error = null)
{
    if (!jsonMode) return code;
    lock (emitGate)
    {
        if (emitted) return code;
        emitted = true;
        outObj["exit"] = code;
        outObj["ms"] = started.ElapsedMilliseconds;
        outObj["error"] = error;
        Console.Out.Write(outObj.ToJsonString(jsonLine) + "\n");
        Console.Out.Flush();
    }
    return code;
}

try
{
    switch (cmd)
    {
        case "layout": return Layout();
        case "probe": return Probe();
        case "self-check": return SelfCheck();
        default:
            if (jsonMode) return Finish(1, "commande inconnue");
            Console.WriteLine("""
                ultima-mirror layout [--dll PATH] [--json]   vérifie ObjectPaths contre la métadonnée de sts2.dll (sans le jeu)
                ultima-mirror probe [--pid N] [--dac PATH] [--comm NAME] [--count TYPE] [--sections run,reward,shop,combat,event,piles]
                                    [--timeout S | --timeout-ms MS] [--compact] [--compare-save [--save PATH]] [--json]
                                                             s'attache en LECTURE SEULE (suspend:false) et lit l'état
                ultima-mirror self-check                     liste blanche vs champs interdits
                --json : une seule ligne JSON sur stdout (aucun texte), code de sortie inchangé.
                """);
            return 1;
    }
}
catch (Exception e) when (jsonMode)
{
    // Ne jamais laisser une trace texte polluer stdout en mode --json.
    return Finish(3, $"{e.GetType().Name}: {e.Message}");
}

int SelfCheck()
{
    var v = ReadGuard.WhitelistViolations().ToList();
    Say($"champs en liste blanche : {ObjectPaths.All.Count}");
    Say($"fragments interdits     : {string.Join(", ", ObjectPaths.ForbiddenFieldFragments)}");
    Say(v.Count == 0 ? "OK : aucun champ de la liste blanche ne correspond à un fragment interdit" : "VIOLATION : " + string.Join(", ", v));
    outObj["whitelist"] = ObjectPaths.All.Count;
    outObj["violations"] = new JsonArray(v.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
    foreach (var probe in new[] { "<PlayerRng>k__BackingField", "<RelicGrabBag>k__BackingField", "<Odds>k__BackingField", "<DrawPile>k__BackingField", "_somethingElse" })
    {
        try { ReadGuard.Check(probe); Say($"ÉCHEC : {probe} accepté"); return Finish(4, $"{probe} accepté"); }
        catch (ForbiddenReadException e) { Say($"refusé comme attendu : {e.Message}"); }
    }
    return Finish(v.Count == 0 ? 0 : 4);
}

int Layout()
{
    var dll = Opt("--dll") ?? GameProcess.DefaultSts2Dll;
    if (!File.Exists(dll)) { Say($"sts2.dll introuvable : {dll}"); return Finish(1, $"sts2.dll introuvable : {dll}"); }
    var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(dll))).ToLowerInvariant();
    Say($"assembly : {dll}");
    Say($"sha256   : {sha}");
    var checks = StaticLayout.Verify(dll);
    int bad = 0;
    var missing = new JsonArray();
    foreach (var c in checks)
    {
        var ok = c.TypeFound && c.FieldFound && c.StaticMatches;
        if (!ok) { bad++; missing.Add($"{c.Spec.Type}::{c.Spec.Field}"); }
        var status = ok ? "OK  " : !c.TypeFound ? "TYPE?" : !c.FieldFound ? "FIELD?" : "KIND?";
        Say($"{status} {(c.Spec.IsStatic ? "static " : "")}{c.Spec.Type}::{c.Spec.Field}   [{c.Spec.Source}]");
    }
    Say($"{checks.Count - bad}/{checks.Count} chemins présents dans la métadonnée");
    outObj["sha256"] = sha;
    outObj["ok"] = checks.Count - bad;
    outObj["total"] = checks.Count;
    outObj["missing"] = missing;
    return Finish(bad == 0 ? 0 : 4, bad == 0 ? null : $"{bad} chemin(s) absent(s) : patch du jeu");
}

int Probe()
{
    // Watchdog: a non-suspended read can hang inside the DAC (observed: futex wait, 2026-10-28).
    // The probe must never linger; the daemon must run it as a child process (crash/hang isolation).
    int timeoutMs = Opt("--timeout-ms") is { } tms ? int.Parse(tms) : int.Parse(Opt("--timeout") ?? "15") * 1000;
    var health = new JsonObject { ["gameRunning"] = false, ["attached"] = false };
    outObj["health"] = health;
    var sectionsOut = new JsonObject();
    outObj["sections"] = sectionsOut;
    outObj["compare"] = null;
    try { health["ptraceScope"] = int.Parse(File.ReadAllText("/proc/sys/kernel/yama/ptrace_scope").Trim()); }
    catch { health["ptraceScope"] = null; }
    new Thread(() =>
    {
        Thread.Sleep(timeoutMs);
        lock (emitGate) if (emitted) return; // le chemin normal a déjà répondu : ne pas écraser son code
        Say($"WATCHDOG : abandon après {timeoutMs} ms (lecture bloquée)");
        Console.Out.Flush();
        Finish(5, $"watchdog : abandon après {timeoutMs} ms (lecture bloquée)");
        Environment.Exit(5);
    }) { IsBackground = true }.Start();

    int pid;
    var comm = Opt("--comm") ?? GameProcess.GameComm;
    if (Opt("--pid") is { } p) pid = int.Parse(p);
    else
    {
        var found = GameProcess.FindByComm(comm);
        if (found.Count == 0)
        {
            Say($"jeu non lancé : aucun processus dont /proc/PID/comm == \"{comm}\" (mode dégradé : fichiers seulement)");
            return Finish(2, "jeu non lancé");
        }
        if (found.Count > 1) Say($"attention : {found.Count} processus \"{comm}\" ({string.Join(",", found)}), on prend le premier");
        pid = found[0];
    }
    if (!Directory.Exists($"/proc/{pid}")) { Say($"jeu non lancé : PID {pid} absent"); return Finish(2, $"PID {pid} absent"); }
    health["gameRunning"] = true;
    health["pid"] = pid;

    var target = GameProcess.Describe(pid, Opt("--dac"));
    Say($"pid            : {target.Pid} (comm={target.Comm})");
    Say($"libcoreclr.so  : {target.CoreClrPath ?? "(absent des maps : pas un processus .NET ?)"}");
    Say($"DAC            : {target.DacPath ?? "-"}  [{target.DacSource}]");
    if (target.DacPath is null) return Finish(3, "DAC introuvable");

    MirrorSession session;
    var sw = System.Diagnostics.Stopwatch.StartNew();
    // Prefer /proc/PID/root: it is the file ACTUALLY mapped, even when the target lives in another mount namespace.
    var coreFile = target.CoreClrPath is null ? null : File.Exists($"/proc/{pid}/root{target.CoreClrPath}") ? $"/proc/{pid}/root{target.CoreClrPath}" : target.CoreClrPath;
    if (coreFile is null || !File.Exists(coreFile)) { Say("libcoreclr.so illisible depuis l'hôte"); return Finish(3, "libcoreclr.so illisible depuis l'hôte"); }
    try { session = MirrorSession.Attach(pid, target.DacPath, coreFile); }
    catch (Exception e)
    {
        Say($"ÉCHEC d'attache ({e.GetType().Name}) : {e.Message}");
        return Finish(3, $"échec d'attache ({e.GetType().Name}) : {e.Message}");
    }
    using (session)
    {
        health["attached"] = true;
        health["attachMs"] = sw.ElapsedMilliseconds;
        health["runtime"] = session.RuntimeBuild?.ToString();
        Say($"attache        : OK en {sw.ElapsedMilliseconds} ms (suspend:false, lecture process_vm_readv)");
        Say($"runtime        : {session.Clr.Flavor} {session.RuntimeBuild} (== DAC, vérifié)");
        if (!jsonMode)
            Say($"modules        : {session.Runtime.EnumerateModules().Count()} ; tas : {(session.Runtime.Heap.IsServer ? "server" : "workstation")}, segments={session.Runtime.Heap.Segments.Length}");
        var reader = new GameReader(session);

        if (Opt("--count") is { } typeName)
        {
            sw.Restart();
            var (n, complete) = reader.CountObjects(typeName);
            Say($"objets {typeName} : {n}{(complete ? "" : " (parcours incomplet)")} en {sw.ElapsedMilliseconds} ms (inclut les objets morts non collectés)");
            outObj["count"] = new JsonObject { ["type"] = typeName, ["n"] = n, ["complete"] = complete };
        }

        var sections = (Opt("--sections") ?? "run,reward,shop,combat").Split(',');
        int unstable = 0;
        void Show<T>(string name, Func<T> read)
        {
            if (!sections.Contains(name)) return;
            sw.Restart();
            StableResult<T> r;
            try { r = Stable.Read(session, read); }
            catch (ForbiddenReadException e)
            {
                Say($"[{name}] REFUS ReadGuard : {e.Message}");
                sectionsOut[name] = new JsonObject { ["stable"] = false, ["attempts"] = 0, ["ms"] = sw.ElapsedMilliseconds, ["error"] = "ReadGuard : " + e.Message, ["value"] = null };
                unstable++;
                return;
            }
            Say($"[{name}] {(r.Stable ? "stable" : "INCONNU")} après {r.Attempts} lecture(s), {sw.ElapsedMilliseconds} ms{(r.Error is null ? "" : " — " + r.Error)}");
            sectionsOut[name] = new JsonObject
            {
                ["stable"] = r.Stable, ["attempts"] = r.Attempts, ["ms"] = sw.ElapsedMilliseconds, ["error"] = r.Error,
                ["value"] = r.Stable ? JsonSerializer.SerializeToNode(r.Value, jsonLine) : null,
            };
            if (!r.Stable) { unstable++; return; }
            Say(JsonSerializer.Serialize(r.Value, json));
        }
        Show("run", reader.ReadRun);
        Show("reward", reader.ReadRewards);
        Show("shop", reader.ReadShop);
        Show("combat", reader.ReadCombat);
        // Sections à venir (agent Miroir 2) : appelées si la bibliothèque expose GameReader.Read<Nom>() ; sinon
        // listées dans « unsupported » (ce n'est PAS une lecture instable : code de sortie inchangé).
        var unsupported = new JsonArray();
        foreach (var name in sections.Where(n => !KnownSections.Contains(n)))
        {
            var method = FutureSections.Contains(name)
                ? typeof(GameReader).GetMethod("Read" + char.ToUpperInvariant(name[0]) + name[1..], Type.EmptyTypes)
                : null;
            if (method is null) { unsupported.Add(name); Say($"[{name}] non prise en charge par ce miroir"); continue; }
            Show<object>(name, () =>
            {
                try { return method.Invoke(reader, null)!; }
                catch (System.Reflection.TargetInvocationException e) when (e.InnerException is not null)
                {
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                    throw;
                }
            });
        }
        outObj["unsupported"] = unsupported;

        if (args.Contains("--compare-save"))
        {
            var savePath = Opt("--save") ?? SaveCompare.FindCurrentRunSave();
            if (savePath is null) { Say("[save] pas de current_run.save (profil vanilla actif) : comparaison impossible"); return Finish(6, "pas de current_run.save"); }
            var mem = Stable.Read(session, reader.ReadRun);
            if (!mem.Stable) { Say($"[save] mémoire INCONNUE ({mem.Error}) : comparaison impossible"); return Finish(6, $"mémoire inconnue ({mem.Error})"); }
            Say($"[save] {savePath} (modifié {File.GetLastWriteTime(savePath):HH:mm:ss})");
            var lines = SaveCompare.Compare(mem.Value!, SaveCompare.Load(savePath));
            foreach (var l in lines)
                Say($"  {(l.Match ? "ÉGAL " : "DIFF ")} {l.Field,-28} mémoire={l.Memory}  save={l.Save}");
            var diff = lines.Count(l => !l.Match);
            Say($"[save] {lines.Count - diff}/{lines.Count} égaux");
            outObj["compare"] = JsonSerializer.SerializeToNode(lines, jsonLine);
            return Finish(diff == 0 ? 0 : 6, diff == 0 ? null : $"{diff} écart(s) save≠mémoire");
        }
        return Finish(unstable == 0 ? 0 : 7, unstable == 0 ? null : $"{unstable} section(s) INCONNUE(s)");
    }
}
