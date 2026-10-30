using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Ultima.Mirror;

public sealed record LayoutCheck(FieldSpec Spec, bool TypeFound, bool FieldFound, bool StaticMatches);

/// <summary>
/// Reads sts2.dll METADATA only (System.Reflection.Metadata — the assembly is never loaded or
/// executed) and checks that every <see cref="ObjectPaths.AllForLayout"/> entry (whitelist + unordered-only) exists with the right
/// static/instance kind. This is the patch-drift detector: run it after every game update.
/// </summary>
public static class StaticLayout
{
    public static IReadOnlyList<LayoutCheck> Verify(string assemblyPath)
    {
        using var fs = File.OpenRead(assemblyPath);
        using var pe = new PEReader(fs);
        var md = pe.GetMetadataReader();
        var fields = IndexFields(md);
        var result = new List<LayoutCheck>();
        foreach (var spec in ObjectPaths.AllForLayout)
        {
            if (!fields.TryGetValue(spec.Type, out var typeFields))
            {
                result.Add(new LayoutCheck(spec, false, false, false));
                continue;
            }
            bool found = typeFields.TryGetValue(spec.Field, out bool isStatic);
            result.Add(new LayoutCheck(spec, true, found, found && isStatic == spec.IsStatic));
        }
        return result;
    }

    /// <summary>Dump every field of one type (debug / doc helper).</summary>
    public static IReadOnlyDictionary<string, bool>? FieldsOf(string assemblyPath, string typeFullName)
    {
        using var fs = File.OpenRead(assemblyPath);
        using var pe = new PEReader(fs);
        var md = pe.GetMetadataReader();
        return IndexFields(md).TryGetValue(typeFullName, out var f) ? f : null;
    }

    static Dictionary<string, Dictionary<string, bool>> IndexFields(MetadataReader md)
    {
        var map = new Dictionary<string, Dictionary<string, bool>>(StringComparer.Ordinal);
        foreach (var h in md.TypeDefinitions)
        {
            var td = md.GetTypeDefinition(h);
            var name = FullName(md, td);
            var f = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var fh in td.GetFields())
            {
                var fd = md.GetFieldDefinition(fh);
                f[md.GetString(fd.Name)] = (fd.Attributes & System.Reflection.FieldAttributes.Static) != 0;
            }
            map[name] = f;
        }
        return map;
    }

    static string FullName(MetadataReader md, TypeDefinition td)
    {
        var name = md.GetString(td.Name);
        var declaring = td.GetDeclaringType();
        if (!declaring.IsNil)
            return FullName(md, md.GetTypeDefinition(declaring)) + "+" + name;
        var ns = md.GetString(td.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }
}
