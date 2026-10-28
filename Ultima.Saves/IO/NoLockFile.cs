using System.Runtime.InteropServices;

namespace Ultima.Saves.IO;

/// <summary>
/// Lecture de fichier SANS verrou et SANS acces en ecriture.
///
/// Pourquoi pas File.ReadAllBytes : sous Linux, .NET 9 pose un verrou consultatif
/// <c>flock(LOCK_SH)</c> sur tout fichier ouvert via FileStream/SafeFileHandle (constate par
/// strace : <c>flock(21, LOCK_SH|LOCK_NB) = 0</c>). Le non-negociable « pas de verrou » interdit
/// cela dans le dossier du jeu. Sous Linux on appelle donc directement libc
/// <c>open(O_RDONLY|O_CLOEXEC)</c> / <c>read</c> / <c>close</c>. Ailleurs (non cible) on retombe
/// sur un FileStream partage au maximum.
/// </summary>
public static class NoLockFile
{
    private const int O_RDONLY = 0;
    private const int O_CLOEXEC = 0x80000; // x86_64 et aarch64 Linux
    private const int EINTR = 4;
    private const int ENOENT = 2;

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int sys_open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [DllImport("libc", EntryPoint = "read", SetLastError = true)]
    private static extern nint sys_read(int fd, byte[] buffer, nint count);

    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int sys_close(int fd);

    [DllImport("libc", EntryPoint = "lseek", SetLastError = true)]
    private static extern long sys_lseek(int fd, long offset, int whence);

    /// <summary>Lit tout le fichier, sans verrou, en lecture seule.</summary>
    /// <exception cref="FileNotFoundException">si le fichier n'existe pas.</exception>
    public static byte[] ReadAllBytes(string path)
    {
        if (!OperatingSystem.IsLinux())
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var ms0 = new MemoryStream();
            fs.CopyTo(ms0);
            return ms0.ToArray();
        }

        int fd = Open(path);
        try
        {
            using var ms = new MemoryStream();
            var buf = new byte[64 * 1024];
            while (true)
            {
                int n = ReadFd(fd, buf);
                if (n == 0) break;
                ms.Write(buf, 0, n);
            }
            return ms.ToArray();
        }
        finally
        {
            sys_close(fd);
        }
    }

    internal static int Open(string path)
    {
        while (true)
        {
            int fd = sys_open(path, O_RDONLY | O_CLOEXEC);
            if (fd >= 0) return fd;
            int err = Marshal.GetLastPInvokeError();
            if (err == EINTR) continue;
            if (err == ENOENT) throw new FileNotFoundException("Fichier introuvable", path);
            throw new IOException($"open({path}) a echoue, errno={err}");
        }
    }

    internal static int ReadFd(int fd, byte[] buf)
    {
        while (true)
        {
            nint n = sys_read(fd, buf, buf.Length);
            if (n >= 0) return (int)n;
            int err = Marshal.GetLastPInvokeError();
            if (err == EINTR) continue;
            throw new IOException($"read a echoue, errno={err}");
        }
    }

    internal static void Close(int fd) => sys_close(fd);

    internal static long Seek(int fd, long offset, SeekOrigin origin)
    {
        long r = sys_lseek(fd, offset, (int)origin);
        if (r < 0) throw new IOException($"lseek a echoue, errno={Marshal.GetLastPInvokeError()}");
        return r;
    }
}

/// <summary>
/// Descripteur de fichier ouvert en lecture seule et sans verrou (pour le suivi d'un log).
/// Sous Linux : fd libc brut ; ailleurs : FileStream partage.
/// </summary>
public sealed class NoLockReader : IDisposable
{
    private readonly int _fd = -1;
    private readonly FileStream? _fs;
    private bool _disposed;

    public string OpenedPath { get; }

    public NoLockReader(string path)
    {
        OpenedPath = path;
        if (OperatingSystem.IsLinux())
            _fd = NoLockFile.Open(path);
        else
            _fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    }

    public int Read(byte[] buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _fs is not null ? _fs.Read(buffer, 0, buffer.Length) : NoLockFile.ReadFd(_fd, buffer);
    }

    public long Seek(long offset, SeekOrigin origin)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _fs is not null ? _fs.Seek(offset, origin) : NoLockFile.Seek(_fd, offset, origin);
    }

    /// <summary>
    /// Chemin ACTUEL du fichier ouvert (Linux : lien /proc/self/fd/N). Permet de detecter une
    /// rotation (le jeu renomme godot.log en godot&lt;date&gt;.log au demarrage). Null si inconnu.
    /// </summary>
    public string? CurrentPath()
    {
        if (_fs is not null || _disposed) return null;
        try
        {
            var target = File.ResolveLinkTarget($"/proc/self/fd/{_fd}", returnFinalTarget: false);
            return target?.FullName;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_fs is not null) _fs.Dispose();
        else if (_fd >= 0) NoLockFile.Close(_fd);
    }
}
