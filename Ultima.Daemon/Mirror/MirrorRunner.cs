using System.Diagnostics;
using System.Runtime.InteropServices;
using Ultima.Daemon.Screens;

namespace Ultima.Daemon.Mirror;

/// <summary>Appel du miroir mémoire. Implémentation réelle : processus enfant ; tests : faux.</summary>
public interface IMirrorRunner
{
    /// <summary>Lit les sections demandées. Ne lève jamais : un échec est un <see cref="MirrorResult"/>.</summary>
    Task<MirrorResult> ProbeAsync(MirrorNeed need, CancellationToken ct = default);

    /// <summary>Vérifie la carte des champs contre sts2.dll (sans le jeu ; code 4 = patch).</summary>
    Task<MirrorResult> LayoutAsync(CancellationToken ct = default);
}

/// <summary>
/// Lance <c>ultima-mirror probe --json</c> dans un processus ENFANT (un plantage ClrMD ne touche
/// jamais le daemon), avec délai maximum : au-delà, l'arbre est tué. L'unicité « un seul en vol »
/// est garantie par <see cref="MirrorScheduler"/> ; ce lanceur la vérifie aussi (garde-fou).
/// </summary>
public sealed class ProcessMirrorRunner : IMirrorRunner
{
    private readonly string _exe;
    private readonly TimeSpan _timeout;
    private readonly string? _comm;
    private int _inFlight;

    public ProcessMirrorRunner(string exe, TimeSpan? timeout = null, string? gameComm = null)
    {
        _exe = exe;
        _timeout = timeout ?? TimeSpan.FromSeconds(3);
        _comm = gameComm;
    }

    public string Executable => _exe;

    public static string SectionsArg(MirrorNeed need)
    {
        var s = new List<string>();
        if (need.HasFlag(MirrorNeed.Shop)) s.Add("run"); // or + vérification croisée save↔mémoire
        if (need.HasFlag(MirrorNeed.Reward)) s.Add("reward");
        if (need.HasFlag(MirrorNeed.Shop)) s.Add("shop");
        if (need.HasFlag(MirrorNeed.Event)) s.Add("event");
        return string.Join(',', s);
    }

    public Task<MirrorResult> ProbeAsync(MirrorNeed need, CancellationToken ct = default)
    {
        // le probe a son propre chien de garde, un peu avant le nôtre
        var args = new List<string> { "probe", "--json", "--sections", SectionsArg(need),
            "--timeout-ms", ((int)Math.Max(500, _timeout.TotalMilliseconds - 300)).ToString() };
        if (_comm is not null) { args.Add("--comm"); args.Add(_comm); }
        return RunAsync(args, ct);
    }

    public Task<MirrorResult> LayoutAsync(CancellationToken ct = default) => RunAsync(new() { "layout", "--json" }, ct);

    private async Task<MirrorResult> RunAsync(List<string> args, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        if (!File.Exists(_exe)) return MirrorResult.Failed(ProbeExit.Missing, 0, $"miroir introuvable : {_exe}");
        if (Interlocked.Exchange(ref _inFlight, 1) == 1)
            return MirrorResult.Failed(ProbeExit.SpawnFailed, 0, "un appel au miroir est déjà en vol");
        try
        {
            var psi = new ProcessStartInfo(_exe)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = false,
                CreateNoWindow = true,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOTNET_ROOT")) && DotnetRoot() is { } root)
                psi.Environment["DOTNET_ROOT"] = root;
            // le miroir n'a besoin d'aucune optimisation dynamique : démarrage plus court, moins de CPU
            psi.Environment["DOTNET_TieredPGO"] = "0";
            psi.Environment["DOTNET_TC_QuickJitForLoops"] = "1";

            using var p = new Process { StartInfo = psi };
            try { p.Start(); }
            catch (Exception e) { return MirrorResult.Failed(ProbeExit.SpawnFailed, sw.ElapsedMilliseconds, $"lancement du miroir impossible : {e.Message}"); }

            var stdout = p.StandardOutput.ReadToEndAsync(ct);
            var stderr = p.StandardError.ReadToEndAsync(ct);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_timeout);
            try
            {
                await p.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { p.Kill(entireProcessTree: true); } catch { /* déjà mort */ }
                try { await p.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); } catch { }
                return MirrorResult.Failed(ProbeExit.KilledByDaemon, sw.ElapsedMilliseconds,
                    $"miroir tué après {(int)_timeout.TotalMilliseconds} ms");
            }
            string output = await stdout.ConfigureAwait(false);
            string err = "";
            try { err = await stderr.ConfigureAwait(false); } catch { }
            if (err.Length > 0) Diag.Debug($"mirror stderr: {err.Trim().Replace('\n', ' ')}");
            return MirrorResult.Parse(output, p.ExitCode, sw.ElapsedMilliseconds);
        }
        finally
        {
            Volatile.Write(ref _inFlight, 0);
        }
    }

    /// <summary>Racine du runtime .NET qui exécute le daemon (le miroir est « framework-dependent »).</summary>
    internal static string? DotnetRoot()
    {
        // …/shared/Microsoft.NETCore.App/9.0.x/System.Private.CoreLib.dll → racine 3 niveaux plus haut
        var core = Path.GetDirectoryName(typeof(object).Assembly.Location);
        if (core is null) return null;
        var root = Path.GetFullPath(Path.Combine(core, "..", "..", ".."));
        return File.Exists(Path.Combine(root, "dotnet")) || Directory.Exists(Path.Combine(root, "shared")) ? root : null;
    }

    /// <summary>Emplacement par défaut : $ULTIMA_MIRROR, sinon même dossier que le daemon, sinon ../mirror/, sinon build/mirror du dépôt.</summary>
    public static string DefaultPath()
    {
        var env = Environment.GetEnvironmentVariable("ULTIMA_MIRROR");
        if (!string.IsNullOrWhiteSpace(env)) return env;
        var dir = AppContext.BaseDirectory;
        var same = Path.Combine(dir, "ultima-mirror"); // installation : ~/.local/share/ultima/bin/
        if (File.Exists(same)) return same;
        var sibling = Path.GetFullPath(Path.Combine(dir, "..", "mirror", "ultima-mirror"));
        if (File.Exists(sibling)) return sibling;
        for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
        {
            var candidate = Path.Combine(dir, "build", "mirror", "ultima-mirror");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }
        return sibling;
    }

    internal static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
}
