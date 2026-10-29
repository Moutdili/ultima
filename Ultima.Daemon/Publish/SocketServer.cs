using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using Ultima.Engine.Contracts;

namespace Ultima.Daemon.Publish;

/// <summary>
/// Socket Unix <c>state.sock</c> (flux) : chaque client reçoit à la connexion le DERNIER état, puis une
/// ligne JSON (terminée par <c>\n</c>) par nouvel état. Plusieurs clients. Un client lent ne bloque jamais
/// le daemon : file bornée par client (on garde les états les plus récents) ; un client mort est retiré.
/// Rien n'est interprété de ce que les clients envoient (le canal est descendant) ; la fin de connexion est détectée (EOF). Aucune activité sans état nouveau.
/// </summary>
public sealed class SocketServer : IStateSink, IDisposable
{
    private readonly string _path;
    private readonly Socket _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _gate = new();
    private readonly List<Client> _clients = new();
    private string? _last;

    private sealed record Client(Socket Socket, Channel<string> Queue);

    public SocketServer(string path)
    {
        _path = path;
        if (File.Exists(path)) File.Delete(path); // socket orpheline d'une exécution précédente (verrou détenu : voir DaemonLock)
        _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        _listener.Bind(new UnixDomainSocketEndPoint(path));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        _listener.Listen(64);
        _ = AcceptLoop();
    }

    public string Path => _path;

    /// <summary>
    /// Requêtes montantes (v1.2) : une ligne JSON par requête, ≤ 4 Kio (au-delà : client déconnecté).
    /// La réponse (une ligne JSON) est envoyée à CE client seulement, entre deux lignes d'état ; elle se
    /// reconnaît à son champ <c>op</c> (les états ont <c>contractVersion</c>).
    /// </summary>
    public Func<string, Task<string?>>? OnRequest { get; set; }

    public const int MaxRequestBytes = 4096;

    public int ClientCount { get { lock (_gate) return _clients.Count; } }

    private async Task AcceptLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            Socket s;
            try { s = await _listener.AcceptAsync(_cts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException e) { Diag.Warn($"socket accept : {e.Message}"); continue; }

            var q = Channel.CreateBounded<string>(new BoundedChannelOptions(8)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
            });
            var c = new Client(s, q);
            lock (_gate)
            {
                _clients.Add(c);
                if (_last is not null) q.Writer.TryWrite(_last);
            }
            Diag.Debug($"socket : client connecté ({ClientCount})");
            _ = Pump(c);
            _ = WatchEof(c);
        }
    }

    private async Task Pump(Client c)
    {
        try
        {
            await foreach (var line in c.Queue.Reader.ReadAllAsync(_cts.Token).ConfigureAwait(false))
            {
                var bytes = Encoding.UTF8.GetBytes(line + "\n");
                int sent = 0;
                while (sent < bytes.Length)
                    sent += await c.Socket.SendAsync(bytes.AsMemory(sent), SocketFlags.None, _cts.Token).ConfigureAwait(false);
            }
        }
        catch (Exception e) when (e is SocketException or ObjectDisposedException or OperationCanceledException or IOException) { }
        finally
        {
            lock (_gate) _clients.Remove(c);
            try { c.Socket.Dispose(); } catch { }
            Diag.Debug($"socket : client parti ({ClientCount})");
        }
    }

    /// <summary>
    /// Détecte la fermeture côté client (EOF) même sans état à envoyer : le compagnon se connecte puis
    /// ferme aussitôt toutes les ~4 s pour tester que le daemon vit ; sans cela, chaque sonde resterait
    /// en mémoire jusqu'au prochain état. Attente bloquante (0 % CPU) ; les octets reçus sont ignorés.
    /// </summary>
    private async Task WatchEof(Client c)
    {
        var buf = new byte[1024];
        var pending = new List<byte>();
        try
        {
            while (true)
            {
                int n = await c.Socket.ReceiveAsync(buf, SocketFlags.None, _cts.Token).ConfigureAwait(false);
                if (n == 0) break;
                for (int i = 0; i < n; i++)
                {
                    if (buf[i] != (byte)'\n') { pending.Add(buf[i]); continue; }
                    var line = Encoding.UTF8.GetString(pending.ToArray()).Trim();
                    pending.Clear();
                    if (line.Length == 0 || OnRequest is not { } handler) continue;
                    string? resp;
                    try { resp = await handler(line).ConfigureAwait(false); }
                    catch (Exception e) { resp = null; Diag.Warn($"socket : requête en échec : {e.Message}"); }
                    if (resp is not null) c.Queue.Writer.TryWrite(resp);
                }
                if (pending.Count > MaxRequestBytes) { Diag.Warn("socket : requête trop longue, client déconnecté"); break; }
            }
        }
        catch (Exception e) when (e is SocketException or ObjectDisposedException or OperationCanceledException) { }
        c.Queue.Writer.TryComplete(); // Pump sort et retire le client
    }

    public void Publish(EngineState state, string line)
    {
        lock (_gate)
        {
            _last = line;
            foreach (var c in _clients) c.Queue.Writer.TryWrite(line);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        lock (_gate)
        {
            foreach (var c in _clients) { c.Queue.Writer.TryComplete(); try { c.Socket.Dispose(); } catch { } }
            _clients.Clear();
        }
        _listener.Dispose();
        try { File.Delete(_path); } catch { }
    }
}
