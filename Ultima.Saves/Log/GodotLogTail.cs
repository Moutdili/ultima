using System.Text;
using Ultima.Saves.IO;

namespace Ultima.Saves.Log;

/// <summary>
/// Suit <c>logs/godot.log</c> (comme <c>tail -F</c>) et publie des <see cref="GodotLogEvent"/> types.
///
/// - Lecture seule, sans verrou (<see cref="NoLockReader"/>), par sondage (defaut 250 ms : cout CPU
///   negligeable, robuste au renommage).
/// - Rotation : au demarrage le jeu renomme l'ancien log en <c>godot&lt;date&gt;.log</c> et en cree un
///   nouveau (observe : logs/godot2026-10-27T21.32.33.log…). Detectee par le chemin courant du fd
///   (/proc/self/fd) ou par une taille qui diminue => reouverture depuis le debut.
/// - Les lignes incompletes (sans '\n') sont gardees en tampon jusqu'a la suite.
/// </summary>
public sealed class GodotLogTail : IDisposable
{
    private readonly string _path;
    private readonly TimeSpan _interval;
    private readonly bool _fromStart;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new();
    private readonly byte[] _buf = new byte[64 * 1024];
    private readonly StringBuilder _partial = new();
    private readonly Decoder _decoder = new UTF8Encoding(false, false).GetDecoder();
    private NoLockReader? _reader;
    private long _position;
    private Timer? _timer;
    private bool _disposed;
    private bool _firstOpen = true;
    private string? _openedRealPath;

    public event EventHandler<GodotLogEvent>? EventReceived;

    /// <summary>Erreur d'E/S non fatale (le suivi continue au prochain sondage).</summary>
    public event EventHandler<Exception>? Error;

    /// <param name="fromStart">false : ignore le contenu deja present au premier demarrage (defaut).</param>
    public GodotLogTail(string logPath, TimeSpan? pollInterval = null, bool fromStart = false,
        Func<DateTimeOffset>? clock = null)
    {
        _path = logPath;
        _interval = pollInterval ?? TimeSpan.FromMilliseconds(250);
        _fromStart = fromStart;
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _timer ??= new Timer(_ =>
        {
            try { Poll(); }
            catch (Exception e) { Error?.Invoke(this, e); }
        }, null, TimeSpan.Zero, _interval);
    }

    /// <summary>Lit ce qui est disponible et publie les evenements. Retourne le nombre publie.</summary>
    public int Poll()
    {
        var events = new List<GodotLogEvent>();
        lock (_gate)
        {
            if (_disposed) return 0;
            EnsureOpen();
            if (_reader is null) return 0;

            while (true)
            {
                int n = _reader.Read(_buf);
                if (n <= 0) break;
                _position += n;
                var chars = new char[_decoder.GetCharCount(_buf, 0, n)];
                _decoder.GetChars(_buf, 0, n, chars, 0);
                _partial.Append(chars);
            }

            var now = _clock();
            int start = 0;
            var text = _partial.ToString();
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != '\n') continue;
                var line = text.AsSpan(start, i - start).TrimEnd('\r').ToString();
                start = i + 1;
                var ev = GodotLogParser.ParseLine(line, now);
                if (ev is not null) events.Add(ev);
            }
            _partial.Clear();
            if (start < text.Length) _partial.Append(text, start, text.Length - start);
        }
        foreach (var e in events) EventReceived?.Invoke(this, e);
        return events.Count;
    }

    private void EnsureOpen()
    {
        if (_reader is not null && Rotated())
        {
            _reader.Dispose();
            _reader = null;
        }
        if (_reader is not null) return;
        if (!File.Exists(_path)) return;

        _reader = new NoLockReader(_path);
        _openedRealPath = _reader.CurrentPath();
        _partial.Clear();
        _decoder.Reset();
        if (_firstOpen && !_fromStart)
            _position = _reader.Seek(0, SeekOrigin.End);
        else
            _position = 0;
        _firstOpen = false;
    }

    private bool Rotated()
    {
        var current = _reader!.CurrentPath();
        if (current is not null)
        {
            if (current.EndsWith(" (deleted)", StringComparison.Ordinal)) return true;
            // le fichier ouvert a ete renomme (rotation) : on ne bascule que si le nouveau existe
            if (_openedRealPath is not null && !string.Equals(current, _openedRealPath, StringComparison.Ordinal))
                return File.Exists(_path);
        }
        try
        {
            var len = new FileInfo(_path).Length;
            return len < _position;
        }
        catch (IOException) { return false; }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _timer?.Dispose();
            _reader?.Dispose();
        }
    }
}
