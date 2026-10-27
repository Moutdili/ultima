namespace Ultima.GameApi;

/// <summary>
/// Journal du lecteur : chaque message distinct n'est écrit qu'une fois (les lectures sont fréquentes,
/// une erreur récurrente ne doit pas inonder le log). Ne lève jamais.
/// </summary>
internal static class ReaderLog
{
    private const int MaxKeys = 512;
    private static readonly HashSet<string> Seen = new();

    private const int MaxMismatchLines = 20;
    private static int _mismatchLines;

    public static void Once(string message) => Once(message, message);

    /// <summary>Écrit <paramref name="message"/> la première fois que <paramref name="key"/> est vu.</summary>
    public static void Once(string key, string message)
    {
        try
        {
            lock (Seen)
            {
                if (Seen.Count >= MaxKeys || !Seen.Add(key))
                {
                    return;
                }
            }
            ModEntry.Write(message);
        }
        catch
        {
            // Le log ne doit jamais remonter vers le jeu.
        }
    }

    /// <summary>Une ligne par (site, type d'exception) : le message variable ne fait pas grossir l'ensemble.</summary>
    public static void Error(string where, Exception ex)
    {
        Once($"err:{where}:{ex.GetType().FullName}", $"reader: {where} failed: {ex.GetType().Name}: {ex.Message}");
    }

    /// <summary>Lignes bornées (valeurs variables) : les <see cref="MaxMismatchLines"/> premières seulement.</summary>
    public static void Limited(string message)
    {
        try
        {
            if (Interlocked.Increment(ref _mismatchLines) <= MaxMismatchLines)
            {
                ModEntry.Write(message);
            }
        }
        catch
        {
            // Le log ne doit jamais remonter vers le jeu.
        }
    }
}
