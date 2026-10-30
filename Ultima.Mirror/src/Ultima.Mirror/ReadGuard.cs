namespace Ultima.Mirror;

/// <summary>Thrown when code tries to read a field outside the whitelist or a forbidden one.</summary>
public sealed class ForbiddenReadException(string message) : Exception(message);

/// <summary>
/// Runtime whitelist. Every field read by <see cref="MirrorSession"/> goes through
/// <see cref="Check"/>: it must be declared in <see cref="ObjectPaths.All"/> and must not match
/// <see cref="ObjectPaths.ForbiddenFieldFragments"/>. Collection internals of BCL types
/// (List&lt;T&gt;._items/_size, Nullable&lt;T&gt;) are allowed explicitly.
/// </summary>
public static class ReadGuard
{
    static readonly HashSet<string> Allowed = new(ObjectPaths.All.Select(s => s.Field), StringComparer.Ordinal)
    {
        "_items", "_size",          // System.Collections.Generic.List<T>
        "hasValue", "value",        // System.Nullable<T>
    };

    public static void Check(string fieldName)
    {
        var lower = fieldName.ToLowerInvariant();
        foreach (var bad in ObjectPaths.ForbiddenFieldFragments)
            if (lower.Contains(bad))
                throw new ForbiddenReadException($"champ interdit (docs/20 §8.2) : {fieldName}");
        if (!Allowed.Contains(fieldName))
            throw new ForbiddenReadException($"champ hors liste blanche ObjectPaths : {fieldName}");
    }

    static readonly HashSet<string> UnorderedAllowed = new(ObjectPaths.UnorderedOnly.Select(s => s.Field), StringComparer.Ordinal);

    /// <summary>
    /// Gate for <see cref="ObjectPaths.UnorderedOnly"/> (draw pile). Accepts ONLY those fields, and
    /// is called ONLY by <see cref="MirrorSession.ReadUnorderedPile"/>, which erases the order before
    /// returning. Any other field — including every forbidden fragment — is refused here too.
    /// </summary>
    public static void CheckUnordered(string fieldName)
    {
        if (!UnorderedAllowed.Contains(fieldName))
            throw new ForbiddenReadException($"champ hors liste « contenu sans ordre » : {fieldName}");
    }

    /// <summary>Self-check: no whitelisted field may match a forbidden fragment.</summary>
    public static IEnumerable<string> WhitelistViolations() =>
        ObjectPaths.All.Where(s => ObjectPaths.ForbiddenFieldFragments
            .Any(b => s.Field.ToLowerInvariant().Contains(b))).Select(s => $"{s.Type}.{s.Field}");
}
