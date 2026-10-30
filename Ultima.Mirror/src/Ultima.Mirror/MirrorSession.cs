using Microsoft.Diagnostics.Runtime;

namespace Ultima.Mirror;

/// <summary>
/// Read-only ClrMD session on a LIVE process.
///
/// How it stays read-only (verified by decompiling ClrMD 4.2.751201
/// Microsoft.Diagnostics.Runtime.Utilities.LinuxLiveDataReader, see docs/mirror/read-only-proof.md):
/// - <c>AttachToProcess(pid, suspend: false)</c> never calls PTRACE_ATTACH (only the suspend
///   branch does); the constructor only does kill(pid, 0) (existence test, no signal delivered)
///   and parses /proc/PID/maps.
/// - Memory is read with process_vm_readv, falling back to /proc/PID/mem opened FileAccess.Read.
/// - The only other ptrace request in the reader is PTRACE_GETREGS (register read, used by stack
///   walks). This class never walks stacks, threads or GC roots, so it is never reached.
/// - The DAC is given explicitly: no symbol server download.
///
/// Consequence of not suspending: reads can be torn (GC may move objects mid-read). Callers must
/// read twice and compare (see <see cref="Stable.Read{T}"/>), and treat failures as "unknown".
/// </summary>
public sealed class MirrorSession : IDisposable
{
    public DataTarget Target { get; }
    public ClrInfo Clr { get; }
    public ClrRuntime Runtime { get; }
    public string DacPath { get; }
    /// <summary>"@(#)Version … @Commit: …" of the target's libcoreclr.so (== DAC's, checked at attach).</summary>
    public string RuntimeBuild { get; }

    MirrorSession(DataTarget target, ClrInfo clr, ClrRuntime runtime, string dacPath, string build)
    {
        Target = target; Clr = clr; Runtime = runtime; DacPath = dacPath; RuntimeBuild = build;
    }

    /// <param name="coreClrFile">libcoreclr.so of the target, readable from the host (host path or /proc/PID/root/...).</param>
    public static MirrorSession Attach(int pid, string dacPath, string coreClrFile)
    {
        // ClrMD 4.2 looks for the "@(#)Version" string only in the first WRITABLE PT_LOAD segment of
        // libcoreclr; in .NET 9 it lives in .rodata, so ClrInfo.Version is 0.0 and its own DAC check
        // always fails (observed 2026-10-28: "Mismatched dac … expected: 0.0"). We do a STRICTER check
        // ourselves (version AND commit hash, both files) and only then skip ClrMD's.
        var runtimeBuild = BuildString.Of(coreClrFile) ?? throw new InvalidOperationException($"version introuvable dans {coreClrFile}");
        var dacBuild = BuildString.Of(dacPath) ?? throw new InvalidOperationException($"version introuvable dans {dacPath}");
        if (runtimeBuild != dacBuild)
            throw new InvalidOperationException($"DAC incompatible : runtime « {runtimeBuild} » ≠ DAC « {dacBuild} »");

        var options = new DataTargetOptions { SymbolPaths = Array.Empty<string>() };
        // suspend: false  ==> NO ptrace attach. This literal must never change (grep-audited).
        var dt = DataTarget.AttachToProcess(pid, suspend: false, options);
        try
        {
            var clr = dt.ClrVersions.FirstOrDefault()
                      ?? throw new InvalidOperationException("aucun runtime CoreCLR trouvé dans le processus");
            var rt = clr.CreateRuntime(dacPath, ignoreMismatch: true);
            return new MirrorSession(dt, clr, rt, dacPath, runtimeBuild);
        }
        catch
        {
            dt.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        Runtime.Dispose();
        Target.Dispose();
    }

    readonly Dictionary<string, ClrType?> _typeCache = new();

    public ClrType? FindType(string fullName)
    {
        if (_typeCache.TryGetValue(fullName, out var t)) return t;
        foreach (var m in Runtime.EnumerateModules())
        {
            t = m.GetTypeByName(fullName);
            if (t is not null) break;
        }
        return _typeCache[fullName] = t;
    }

    public ClrObject ReadStaticObject(string typeName, string field)
    {
        ReadGuard.Check(field);
        var type = FindType(typeName) ?? throw new MirrorMissException($"type absent : {typeName}");
        var sf = type.GetStaticFieldByName(field) ?? throw new MirrorMissException($"statique absent : {typeName}.{field}");
        foreach (var ad in Runtime.AppDomains)
        {
            if (!sf.IsInitialized(ad)) continue;
            var o = sf.ReadObject(ad);
            if (!o.IsNull) return o;
        }
        return default;
    }

    public ClrValueType? ReadStaticStruct(string typeName, string field)
    {
        ReadGuard.Check(field);
        var type = FindType(typeName) ?? throw new MirrorMissException($"type absent : {typeName}");
        var sf = type.GetStaticFieldByName(field) ?? throw new MirrorMissException($"statique absent : {typeName}.{field}");
        foreach (var ad in Runtime.AppDomains)
            if (sf.IsInitialized(ad)) return sf.ReadStruct(ad);
        return null;
    }

    static ClrInstanceField FieldOf(ClrObject o, string field)
    {
        ReadGuard.Check(field);
        for (var t = o.Type; t is not null; t = t.BaseType)
        {
            var f = t.GetFieldByName(field);
            if (f is not null) return f;
        }
        throw new MirrorMissException($"champ absent : {o.Type?.Name}.{field}");
    }

    public static ClrObject Obj(ClrObject o, string field)
    {
        if (o.IsNull) return default;
        return FieldOf(o, field).ReadObject(o.Address, interior: false);
    }

    public static T Val<T>(ClrObject o, string field) where T : unmanaged
    {
        if (o.IsNull) throw new MirrorMissException($"lecture de {field} sur null");
        return FieldOf(o, field).Read<T>(o.Address, interior: false);
    }

    public static string? Str(ClrObject o, string field)
    {
        if (o.IsNull) return null;
        return FieldOf(o, field).ReadString(o.Address, interior: false);
    }

    /// <summary>Elements of a System.Collections.Generic.List&lt;T&gt; of reference types (nulls kept).</summary>
    public IReadOnlyList<ClrObject> List(ClrObject list)
    {
        if (list.IsNull) return Array.Empty<ClrObject>();
        if (list.Type?.Name?.StartsWith("System.Collections.Generic.List<", StringComparison.Ordinal) != true)
            throw new MirrorMissException($"attendu List<T>, trouvé {list.Type?.Name}");
        int size = Val<int>(list, "_size");
        var items = Obj(list, "_items");
        if (items.IsNull) return Array.Empty<ClrObject>();
        var arr = items.AsArray();
        if (size < 0 || size > arr.Length || size > 10_000)
            throw new MirrorMissException($"taille de liste incohérente ({size}/{arr.Length}) : lecture déchirée ?");
        var res = new List<ClrObject>(size);
        for (int i = 0; i < size; i++) res.Add(arr.GetObjectValue(i));
        return res;
    }

    /// <summary>
    /// The ONLY way to follow a field of <see cref="ObjectPaths.UnorderedOnly"/> (the draw pile):
    /// reads the CardPile, maps each card, and returns a counted multiset sorted by card key.
    /// The ordered list exists only in this method's locals; nothing ordered is returned, so a
    /// caller cannot learn the draw order (Mio decision « probabiliste seulement »).
    /// </summary>
    public CountedPile ReadUnorderedPile(ClrObject owner, string field, Func<ClrObject, CardView> card)
    {
        ReadGuard.CheckUnordered(field);
        if (owner.IsNull) throw new MirrorMissException($"lecture de {field} sur null");
        ClrInstanceField? f = null;
        for (var t = owner.Type; t is not null && f is null; t = t.BaseType) f = t.GetFieldByName(field);
        if (f is null) throw new MirrorMissException($"champ absent : {owner.Type?.Name}.{field}");
        var pile = f.ReadObject(owner.Address, interior: false);
        if (pile.IsNull) throw new MirrorMissException($"{field} null");
        return CountedPile.From(List(Obj(pile, "_cards")).Select(card));
    }

    /// <summary>ModelId of an AbstractModel, as "CATEGORY.ENTRY" (same form as current_run.save).</summary>
    public static string? ModelIdOf(ClrObject model)
    {
        if (model.IsNull) return null;
        var id = Obj(model, $"<Id>k__BackingField");
        if (id.IsNull) return null;
        return $"{Str(id, "<Category>k__BackingField")}.{Str(id, "<Entry>k__BackingField")}";
    }
}

/// <summary>A path step was not found: layout changed, object not there yet, or torn read.</summary>
public sealed class MirrorMissException(string message) : Exception(message);

/// <summary>Extracts the "@(#)Version x.y.z.w @Commit: sha" sccsid string from a CoreCLR ELF file.</summary>
public static class BuildString
{
    static readonly byte[] Marker = "@(#)Version "u8.ToArray();

    public static string? Of(string path)
    {
        var bytes = File.ReadAllBytes(path);
        int i = bytes.AsSpan().IndexOf(Marker);
        if (i < 0) return null;
        int end = Array.IndexOf(bytes, (byte)0, i);
        if (end < 0 || end - i > 200) return null;
        return System.Text.Encoding.ASCII.GetString(bytes, i + 4, end - i - 4);
    }
}
