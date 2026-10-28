using System.Net.Sockets;
using System.Text;
using Ultima.Overlay.X11;

// ultima-x11 run [--expanded]   : lance l'overlay (demon, une seule instance)
// ultima-x11 toggle|expand|collapse|hide|show|status|quit|snapshot <png>
//                               : commande envoyee au demon par socket Unix (pour un raccourci GNOME)
// ultima-x11 stack              : ordre d'empilement X reel (XQueryTree sur la racine, bas -> haut)

var socketPath = Path.Combine(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? "/tmp", "ultima-x11.sock");
var cmd = args.Length > 0 ? args[0] : "run";

switch (cmd)
{
    case "run":
        return RunDaemon(socketPath, args.Contains("--expanded"));
    case "stack":
        return Stack.Print();
    case "-h" or "--help" or "help":
        Console.WriteLine("usage: ultima-x11 [run [--expanded] | toggle | expand | collapse | hide | show | status | snapshot <png> | quit | stack]");
        return 0;
    default:
        return SendCommand(socketPath, string.Join(' ', args));
}

static int SendCommand(string socketPath, string line)
{
    try
    {
        using var s = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        s.Connect(new UnixDomainSocketEndPoint(socketPath));
        s.Send(Encoding.UTF8.GetBytes(line + "\n"));
        s.Shutdown(SocketShutdown.Send);
        var buf = new byte[4096];
        var sb = new StringBuilder();
        int n;
        while ((n = s.Receive(buf)) > 0) sb.Append(Encoding.UTF8.GetString(buf, 0, n));
        Console.Write(sb.ToString());
        return sb.ToString().StartsWith("ok") ? 0 : 1;
    }
    catch (SocketException e)
    {
        Console.Error.WriteLine($"ultima-x11: demon injoignable sur {socketPath} ({e.SocketErrorCode}). Lancer : ultima-x11 run");
        return 2;
    }
}

static unsafe int RunDaemon(string socketPath, bool expanded)
{
    var logDir = Path.Combine(Environment.GetEnvironmentVariable("XDG_STATE_HOME")
                              ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/state"), "ultima");
    Directory.CreateDirectory(logDir);
    var logFile = new StreamWriter(Path.Combine(logDir, "ultima-x11.log"), append: true) { AutoFlush = true };
    void Log(string m)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} [ultima-x11] {m}";
        Console.WriteLine(line);
        logFile.WriteLine(line);
    }

    // une seule instance
    if (File.Exists(socketPath))
    {
        try
        {
            using var probe = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            probe.Connect(new UnixDomainSocketEndPoint(socketPath));
            Log("deja lance (socket actif) -> abandon");
            return 1;
        }
        catch (SocketException) { File.Delete(socketPath); }
    }

    using var overlay = new Overlay(Log, expanded);
    using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
    listener.Bind(new UnixDomainSocketEndPoint(socketPath));
    listener.Listen(4);
    listener.Blocking = false;
    Log($"pid={Environment.ProcessId} socket={socketPath}");

    var running = true;
    // SIGINT/SIGTERM : on nettoie le socket puis on laisse le runtime terminer (pas de reveil periodique).
    void OnSignal(System.Runtime.InteropServices.PosixSignalContext _) { try { File.Delete(socketPath); } catch { } Log("signal -> arret"); }
    using var sigInt = System.Runtime.InteropServices.PosixSignalRegistration.Create(System.Runtime.InteropServices.PosixSignal.SIGINT, OnSignal);
    using var sigTerm = System.Runtime.InteropServices.PosixSignalRegistration.Create(System.Runtime.InteropServices.PosixSignal.SIGTERM, OnSignal);

    overlay.Paint();
    var fds = stackalloc PollFd[2];
    while (running)
    {
        fds[0] = new PollFd { Fd = overlay.Fd, Events = LibC.POLLIN };
        fds[1] = new PollFd { Fd = (int)listener.Handle, Events = LibC.POLLIN };
        // Bloquant (timeout -1) au repos : zero reveil, zero CPU. 16 ms (60 Hz, ecran a 60 Hz) seulement pendant une transition.
        var r = LibC.poll(fds, 2, overlay.Animating ? 16 : -1);
        if (r < 0) continue; // EINTR
        overlay.PumpX();
        if ((fds[1].REvents & LibC.POLLIN) != 0) running = HandleClient(listener, overlay, Log);
        if (overlay.Animating) overlay.Tick();
    }

    Log("arret");
    File.Delete(socketPath);
    return 0;
}

static bool HandleClient(Socket listener, Overlay overlay, Action<string> log)
{
    Socket client;
    var keep = true;
    try { client = listener.Accept(); } catch (SocketException) { return true; }
    using (client)
    {
        client.Blocking = true;
        client.ReceiveTimeout = 500;
        var buf = new byte[1024];
        var sb = new StringBuilder();
        try
        {
            int n;
            while ((n = client.Receive(buf)) > 0) { sb.Append(Encoding.UTF8.GetString(buf, 0, n)); if (sb.ToString().Contains('\n')) break; }
        }
        catch (SocketException) { }
        var line = sb.ToString().Trim();
        var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var verb = parts.Length > 0 ? parts[0] : "";
        string reply;
        switch (verb)
        {
            case "toggle": overlay.SetExpanded(!overlay.Expanded); reply = "ok " + (overlay.Expanded ? "expanded" : "collapsed"); break;
            case "expand": overlay.SetExpanded(true); reply = "ok expanded"; break;
            case "collapse": overlay.SetExpanded(false); reply = "ok collapsed"; break;
            case "hide": overlay.SetVisible(false); reply = "ok hidden"; break;
            case "show": overlay.SetVisible(true); reply = "ok shown"; break;
            case "visible": overlay.SetVisible(!overlay.Visible); reply = "ok " + (overlay.Visible ? "shown" : "hidden"); break;
            case "status": reply = "ok " + overlay.Status(); break;
            case "snapshot" when parts.Length == 2: overlay.Snapshot(parts[1]); reply = "ok " + parts[1]; break;
            case "quit": reply = "ok bye"; keep = false; break;
            default: reply = $"err commande inconnue '{line}'"; break;
        }
        log($"cmd '{line}' -> {reply}");
        try { client.Send(Encoding.UTF8.GetBytes(reply + "\n")); } catch (SocketException) { }
    }
    return keep;
}

internal static unsafe class Stack
{
    /// <summary>Enfants de la racine X dans l'ordre d'empilement serveur (XQueryTree : bas -> haut).</summary>
    public static int Print()
    {
        var dpy = X.XOpenDisplay(0);
        if (dpy == 0) { Console.Error.WriteLine("XOpenDisplay a echoue"); return 2; }
        var root = X.XRootWindow(dpy, X.XDefaultScreen(dpy));
        X.XQueryTree(dpy, root, out _, out _, out var children, out var n);
        Console.WriteLine($"# XQueryTree(root=0x{root:x}) : {n} enfants, ordre bas -> haut ; seuls les visibles (IsViewable) sont listes");
        var idx = 0;
        for (var i = 0; i < n; i++)
        {
            var w = children[i];
            X.XGetWindowAttributes(dpy, w, out var a);
            if (a.MapState != X.IsViewable) continue;
            var name = X.FetchName(dpy, w) ?? ChildName(dpy, w) ?? "";
            Console.WriteLine($"{idx++,3} pos={i,4} 0x{w:x8} {a.Width}x{a.Height}+{a.X}+{a.Y} depth={a.Depth} " +
                              $"override_redirect={a.OverrideRedirect} name=\"{name}\"");
        }
        Console.WriteLine("# la derniere ligne = la fenetre la plus haute dans la pile X");
        if (children != null) X.XFree(children);
        X.XCloseDisplay(dpy);
        return 0;
    }

    private static string? ChildName(nint dpy, nuint w)
    {
        X.XQueryTree(dpy, w, out _, out _, out var kids, out var n);
        string? found = null;
        for (var i = 0; i < n && found == null; i++) found = X.FetchName(dpy, kids[i]);
        if (kids != null) X.XFree(kids);
        return found is null ? null : $"[cadre de] {found}";
    }
}
