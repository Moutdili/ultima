using System.Text.Json;
using Ultima.Mirror;

// mirror-proof — harnais de preuve du miroir 2 CONTRE LA CIBLE DE TEST UNIQUEMENT (Sts2Shape.TestTarget).
// La CLI officielle reste `ultima-mirror` (Câbleur, lit event/piles par réflexion depuis 78e23f6) ; ce
// harnais appelle directement la bibliothèque (lisible, --repeat) avec la MÊME sérialisation camelCase.
// Refuse de s'attacher à un processus dont /proc/PID/comm n'est pas celui de la cible de test :
// il ne peut pas lire le vrai jeu.
//
//   mirror-proof guard                               refus ReadGuard (pioche, champs cachés)
//   mirror-proof read --pid N [--sections a,b] [--repeat K]

var json = new JsonSerializerOptions(JsonSerializerOptions.Web) { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
string? Opt(string n) { int i = Array.IndexOf(args, n); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
const string TestComm = "Sts2Shape.TestT"; // comm is truncated to 15 chars by the kernel

switch (args.FirstOrDefault())
{
    case "guard": return Guard();
    case "read": return Read();
    default: Console.WriteLine("mirror-proof guard | mirror-proof read --pid N [--sections event,piles,reward,shop,combat,run] [--repeat K]"); return 1;
}

int Guard()
{
    int fail = 0;
    void Refused(string what, Action a)
    {
        try { a(); Console.WriteLine($"ÉCHEC  accepté : {what}"); fail++; }
        catch (ForbiddenReadException e) { Console.WriteLine($"refusé : {what} — {e.Message}"); }
    }
    void Accepted(string what, Action a)
    {
        try { a(); Console.WriteLine($"accepté (attendu) : {what}"); }
        catch (ForbiddenReadException e) { Console.WriteLine($"ÉCHEC  refusé : {what} — {e.Message}"); fail++; }
    }
    Console.WriteLine($"liste blanche : {ObjectPaths.All.Count} champs ; contenu-sans-ordre : {ObjectPaths.UnorderedOnly.Count} ; layout : {ObjectPaths.AllForLayout.Count()}");
    var v = ReadGuard.WhitelistViolations().ToList();
    Console.WriteLine(v.Count == 0 ? "liste blanche : aucun champ interdit" : "VIOLATION : " + string.Join(", ", v));
    fail += v.Count;
    // Order of the draw pile: refused by the generic path.
    foreach (var f in new[] { "<DrawPile>k__BackingField", "_piles", "_runPiles", "<Rng>k__BackingField", "_multiplayerOptionSelectionRng",
                              "<PlayerRng>k__BackingField", "<RelicGrabBag>k__BackingField", "_playerVotes", "_mutableEncounter",
                              "_dynamicVars", "_variables", "<OnChosen>k__BackingField", "<WillKillPlayer>k__BackingField", "_generatedOptions" })
        Refused($"Check({f})", () => ReadGuard.Check(f));
    // The unordered gate accepts ONLY the draw pile.
    foreach (var f in new[] { "_cards", "<Hand>k__BackingField", "_piles", "<Rng>k__BackingField", "_events" })
        Refused($"CheckUnordered({f})", () => ReadGuard.CheckUnordered(f));
    Accepted("CheckUnordered(<DrawPile>k__BackingField)", () => ReadGuard.CheckUnordered("<DrawPile>k__BackingField"));
    foreach (var f in new[] { "<DiscardPile>k__BackingField", "<ExhaustPile>k__BackingField", "<PlayPile>k__BackingField", "<Enchantment>k__BackingField",
                              "_amount", "_events", "<CanonicalEvent>k__BackingField", "_currentOptions", "<TextKey>k__BackingField", "<locEntryKey>P" })
        Accepted($"Check({f})", () => ReadGuard.Check(f));
    Console.WriteLine(fail == 0 ? "GUARD OK" : $"GUARD ÉCHEC ({fail})");
    return fail == 0 ? 0 : 4;
}

int Read()
{
    if (Opt("--pid") is not { } p || !int.TryParse(p, out int pid)) { Console.WriteLine("--pid requis"); return 1; }
    var comm = File.ReadAllText($"/proc/{pid}/comm").Trim();
    if (comm != TestComm) { Console.WriteLine($"REFUS : /proc/{pid}/comm = « {comm} » ≠ « {TestComm} » (ce harnais ne lit que la cible de test)"); return 1; }
    var target = GameProcess.Describe(pid, Opt("--dac"));
    if (target.DacPath is null || target.CoreClrPath is null) { Console.WriteLine("DAC/libcoreclr introuvable"); return 3; }
    var core = File.Exists($"/proc/{pid}/root{target.CoreClrPath}") ? $"/proc/{pid}/root{target.CoreClrPath}" : target.CoreClrPath;
    var sw = System.Diagnostics.Stopwatch.StartNew();
    using var session = MirrorSession.Attach(pid, target.DacPath, core);
    Console.WriteLine($"pid {pid} ({comm}) attaché en {sw.ElapsedMilliseconds} ms, suspend:false, runtime {session.RuntimeBuild}");
    var reader = new GameReader(session);
    var all = new List<(string Name, Func<object> Read)>
    {
        ("run", () => reader.ReadRun()), ("reward", () => reader.ReadRewards()), ("shop", () => reader.ReadShop()),
        ("combat", () => reader.ReadCombat()), ("event", () => reader.ReadEvent()), ("piles", () => reader.ReadPiles()),
    };
    var wanted = (Opt("--sections") ?? "event,piles,reward,shop").Split(',');
    int repeat = int.Parse(Opt("--repeat") ?? "1");
    int unstable = 0;
    foreach (var (name, read) in all.Where(s => wanted.Contains(s.Name)))
    {
        var seen = new HashSet<string>();
        for (int i = 0; i < repeat; i++)
        {
            sw.Restart();
            var r = Stable.Read(session, read);
            var body = r.Stable ? JsonSerializer.Serialize(r.Value, json) : "null";
            if (!r.Stable) unstable++;
            seen.Add(body);
            if (i == 0 || repeat == 1)
            {
                Console.WriteLine($"[{name}] {(r.Stable ? "stable" : "INCONNU")} après {r.Attempts} lecture(s), {sw.ElapsedMilliseconds} ms{(r.Error is null ? "" : " — " + r.Error)}");
                Console.WriteLine(body);
            }
        }
        if (repeat > 1) Console.WriteLine($"[{name}] {repeat} sondes : {seen.Count} sortie(s) distincte(s)");
    }
    return unstable == 0 ? 0 : 7;
}
