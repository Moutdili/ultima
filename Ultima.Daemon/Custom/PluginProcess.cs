using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Ultima.Daemon.Sources;

namespace Ultima.Daemon.Custom;

/// <summary>
/// Un plugin = un PROCESSUS SÉPARÉ (docs/44 §7) : dossier de travail = celui du plugin, <c>nice 19</c>,
/// stdin = une ligne d'état, stdout = une ligne de réponse. La lecture est BORNÉE (8 Kio) et se fait sur
/// un fil dédié : une réponse de 1 Mio ou un plugin qui dort ne bloque jamais le daemon. Un délai dépassé
/// tue le processus (le protocole n'a plus de sens après une ligne manquante) ; il sera relancé au tour
/// suivant, et désactivé après 3 échecs consécutifs (voir <see cref="PluginRuntime"/>).
/// </summary>
internal sealed class PluginProcess : IDisposable
{
    public const int MaxLineBytes = 8 * 1024;

    private readonly Process _process;
    private readonly BlockingCollection<Line> _lines = new(new ConcurrentQueue<Line>(), 8);
    private readonly Thread _reader;
    private readonly Thread _stderr;
    private volatile bool _dead;

    private readonly record struct Line(string? Text, string? Problem);

    private PluginProcess(Process process)
    {
        _process = process;
        _reader = new Thread(ReadLoop) { IsBackground = true, Name = $"plugin-{process.Id}-out" };
        _stderr = new Thread(DrainError) { IsBackground = true, Name = $"plugin-{process.Id}-err" };
    }

    public int Pid => _process.Id;

    public bool Alive
    {
        get
        {
            if (_dead) return false;
            try { return !_process.HasExited; }
            catch (InvalidOperationException) { return false; }
        }
    }

    /// <summary>Démarre le processus. null + <paramref name="problem"/> si le lancement échoue.</summary>
    public static PluginProcess? Start(PluginManifest manifest, out string? problem)
    {
        problem = null;
        var program = manifest.ResolveProgram();
        var psi = new ProcessStartInfo(program)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = manifest.Dir, // dossier de travail = celui du plugin
        };
        foreach (var a in manifest.Arguments) psi.ArgumentList.Add(a);
        var p = new Process { StartInfo = psi };
        try { p.Start(); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            p.Dispose();
            problem = $"lancement impossible ({program}) : {e.Message}";
            return null;
        }
        // nice 19 : un plugin ne vole jamais de CPU au jeu (échec = signalé, jamais fatal)
        if (!ProcessPriority.SetNice(p.Id, 19)) Diag.Warn($"plugin {manifest.Id} : nice 19 non appliqué (pid {p.Id})");
        var pp = new PluginProcess(p);
        pp._reader.Start();
        pp._stderr.Start();
        return pp;
    }

    /// <summary>Écrit UNE ligne d'état sur stdin (bornée : un état trop gros est refusé, jamais tronqué).</summary>
    public bool Send(string line, out string? problem)
    {
        problem = null;
        if (line.Length > 512 * 1024) { problem = "état trop gros pour un plugin (> 512 Kio)"; return false; }
        try
        {
            var w = _process.StandardInput;
            w.Write(line);
            w.Write('\n');
            w.Flush();
            return true;
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException)
        {
            problem = $"écriture impossible : {e.Message}";
            return false;
        }
    }

    /// <summary>Lit une ligne de réponse dans le délai imparti. false + raison si absente/invalide/trop longue.</summary>
    public bool TryRead(int timeoutMs, out string? line, out string? problem)
    {
        line = null;
        problem = null;
        if (!_lines.TryTake(out var l, Math.Max(1, timeoutMs)))
        {
            problem = $"aucune réponse en {timeoutMs} ms";
            return false;
        }
        if (l.Problem is not null) { problem = l.Problem; return false; }
        line = l.Text;
        if (line is null) { problem = "flux fermé (le plugin s'est arrêté)"; return false; }
        return true;
    }

    private void ReadLoop()
    {
        try
        {
            var stream = _process.StandardOutput.BaseStream;
            var buffer = new byte[4096];
            var current = new MemoryStream();
            bool overflow = false;
            while (true)
            {
                int n;
                try { n = stream.Read(buffer, 0, buffer.Length); }
                catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException) { break; }
                if (n <= 0) break;
                for (int i = 0; i < n; i++)
                {
                    byte b = buffer[i];
                    if (b == (byte)'\n')
                    {
                        var text = Encoding.UTF8.GetString(current.GetBuffer(), 0, (int)current.Length).TrimEnd('\r');
                        current.SetLength(0);
                        if (overflow)
                        {
                            overflow = false;
                            Push(new Line(null, $"réponse > {MaxLineBytes} octets (refusée)"));
                            continue;
                        }
                        Push(new Line(text, null));
                        continue;
                    }
                    if (overflow) continue; // on vide jusqu'à la fin de ligne sans mémoriser
                    if (current.Length >= MaxLineBytes)
                    {
                        overflow = true;
                        current.SetLength(0);
                        continue;
                    }
                    current.WriteByte(b);
                }
            }
        }
        catch (Exception e)
        {
            Diag.Debug($"plugin sortie : {e.GetType().Name}");
        }
        finally
        {
            _dead = true;
            try { _lines.CompleteAdding(); } catch (ObjectDisposedException) { }
            try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); } catch { /* déjà mort */ }
        }
    }

    private void Push(Line line)
    {
        try
        {
            // file bornée (8) : si le consommateur ne suit pas, on JETTE plutôt que de bloquer le fil de lecture
            if (!_lines.TryAdd(line, 50)) Diag.Debug("plugin : réponse ignorée (file pleine)");
        }
        catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException) { }
    }

    /// <summary>stderr : drainé (sinon le plugin se bloque) mais jamais accumulé au-delà de 512 octets.</summary>
    private void DrainError()
    {
        try
        {
            var reader = _process.StandardError;
            var buffer = new char[512];
            var kept = new StringBuilder();
            int n;
            while ((n = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (kept.Length < 512) kept.Append(buffer, 0, Math.Min(n, 512 - kept.Length));
            }
            if (kept.Length > 0) Diag.Debug($"plugin stderr : {kept.ToString().Trim().Replace('\n', ' ')}");
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException) { }
    }

    public void Dispose()
    {
        _dead = true;
        try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); } catch { /* déjà mort */ }
        try { _process.StandardInput.Close(); } catch { }
        if (_reader.IsAlive && Environment.CurrentManagedThreadId != _reader.ManagedThreadId) _reader.Join(TimeSpan.FromSeconds(1));
        if (_stderr.IsAlive && Environment.CurrentManagedThreadId != _stderr.ManagedThreadId) _stderr.Join(TimeSpan.FromSeconds(1));
        try { _process.WaitForExit(500); } catch { }
        _process.Dispose();
    }
}
