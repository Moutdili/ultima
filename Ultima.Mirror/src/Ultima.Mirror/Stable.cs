using System.Text.Json;

namespace Ultima.Mirror;

public sealed record StableResult<T>(T? Value, bool Stable, int Attempts, string? Error);

/// <summary>
/// The target is NOT suspended, so a single read can be torn by a concurrent GC or mutation.
/// A value is only reported when two consecutive reads are identical (JSON-equal). Otherwise
/// the caller gets Stable=false and must display "inconnu" — never the last value.
/// </summary>
public static class Stable
{
    public static StableResult<T> Read<T>(MirrorSession session, Func<T> read, int maxAttempts = 4)
    {
        string? prev = null;
        string? lastError = null;
        for (int i = 1; i <= maxAttempts; i++)
        {
            try
            {
                if (i > 1) session.Runtime.FlushCachedData();
                var v = read();
                var json = JsonSerializer.Serialize(v);
                if (json == prev) return new StableResult<T>(v, true, i, null);
                prev = json;
            }
            catch (MirrorMissException e) { lastError = e.Message; prev = null; }
            catch (ForbiddenReadException) { throw; }
            catch (Exception e) when (e is InvalidOperationException or IndexOutOfRangeException or ArgumentException)
            {
                lastError = $"{e.GetType().Name}: {e.Message}"; prev = null;
            }
        }
        return new StableResult<T>(default, false, maxAttempts, lastError ?? "lectures successives différentes");
    }
}
