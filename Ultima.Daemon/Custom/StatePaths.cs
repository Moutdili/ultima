using System.Collections;
using System.Globalization;
using System.Reflection;
using Ultima.Engine.Contracts;

namespace Ultima.Daemon.Custom;

/// <summary>
/// Résolution des chemins des modules déclaratifs : <c>{run.gold}</c>, <c>{run.deck[0].name}</c>,
/// <c>{reward.offered.count}</c>. Marche par réflexion sur les enregistrements du contrat (propriétés
/// publiques, insensible à la casse, camelCase du JSON) — **aucune exécution de code**, aucun accès hors
/// de l'objet <see cref="EngineState"/> publié (donc aucune donnée cachée). Borné : 8 segments, index
/// &lt; 1000, longueur de chemin ≤ 80.
/// </summary>
public static class StatePaths
{
    public const int MaxSegments = 8;
    public const int MaxIndex = 1000;
    public const int MaxPathLength = 80;

    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

    /// <summary>Valeur trouvée, ou false si le chemin est absent (jamais 0 ni une chaîne inventée).</summary>
    public static bool TryGet(object? root, string path, out object? value)
    {
        value = null;
        if (root is null || string.IsNullOrWhiteSpace(path) || path.Length > MaxPathLength) return false;
        var current = root;
        var segments = path.Split('.');
        if (segments.Length > MaxSegments) return false;
        foreach (var raw in segments)
        {
            var seg = raw.Trim();
            if (seg.Length == 0) return false;
            string name = seg;
            int index = -1;
            var bracket = seg.IndexOf('[');
            if (bracket >= 0)
            {
                if (!seg.EndsWith(']')) return false;
                name = seg[..bracket];
                var inner = seg[(bracket + 1)..^1];
                if (!int.TryParse(inner, NumberStyles.None, CultureInfo.InvariantCulture, out index) || index < 0 || index >= MaxIndex)
                    return false;
            }
            if (!Step(ref current, name, out var next)) return false;
            current = next;
            if (index >= 0)
            {
                if (!Index(current, index, out var item)) return false;
                current = item;
            }
            if (current is null) return false;
        }
        value = current;
        return true;
    }

    private static bool Step(ref object? current, string name, out object? next)
    {
        next = null;
        if (current is null) return false;
        if (name.Equals("count", StringComparison.OrdinalIgnoreCase) || name.Equals("length", StringComparison.OrdinalIgnoreCase))
            return Count(current, out next);
        var type = current.GetType();
        var prop = type.GetProperty(name, Flags);
        if (prop is null || !prop.CanRead || prop.GetIndexParameters().Length > 0)
        {
            // les énumérations exposent aussi leur nom par « name »/« value » : rien à faire, absent
            return false;
        }
        try { next = prop.GetValue(current); return true; }
        catch (TargetInvocationException) { return false; }
    }

    private static bool Index(object? current, int index, out object? item)
    {
        item = null;
        switch (current)
        {
            case string s when index < s.Length: item = s[index].ToString(); return true;
            case IList list when index < list.Count: item = list[index]; return true;
            default:
                if (current is IEnumerable e && current is not string)
                {
                    int i = 0;
                    foreach (var o in e)
                    {
                        if (i++ == index) { item = o; return true; }
                        if (i > MaxIndex) break;
                    }
                }
                return false;
        }
    }

    /// <summary>Nombre d'éléments (liste, tableau, chaîne) ou valeur d'un nombre.</summary>
    private static bool Count(object? current, out object? next)
    {
        next = null;
        switch (current)
        {
            case null: return false;
            case string s: next = s.Length; return true;
            case ICollection c: next = c.Count; return true;
            case IEnumerable e:
            {
                int n = 0;
                foreach (var _ in e) { if (++n > MaxIndex) break; }
                next = n;
                return true;
            }
            default: return false;
        }
    }

    /// <summary>Texte affichable d'une valeur, ou null si la valeur n'a pas de texte (absent).</summary>
    public static string? Display(object? value) => value switch
    {
        null => null,
        string s => s,
        bool b => b ? "oui" : "non",
        int i => i.ToString(CultureInfo.InvariantCulture),
        long l => l.ToString(CultureInfo.InvariantCulture),
        short sh => sh.ToString(CultureInfo.InvariantCulture),
        byte by => by.ToString(CultureInfo.InvariantCulture),
        double d => Number(d),
        float f => Number(f),
        decimal m => Number((double)m),
        DateTimeOffset dt => dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        Enum e => e.ToString(),
        IEnumerable e when value is not string => CountText(e),
        _ => value.ToString(),
    };

    /// <summary>Nombre à la française : entier sans décimale, sinon deux décimales au plus, virgule.</summary>
    public static string Number(double d)
    {
        if (double.IsNaN(d) || double.IsInfinity(d)) return "—";
        var s = Math.Abs(d - Math.Round(d)) < 1e-9
            ? ((long)Math.Round(d)).ToString(CultureInfo.InvariantCulture)
            : d.ToString("0.##", CultureInfo.InvariantCulture);
        return s.Replace('.', ',');
    }

    private static string CountText(IEnumerable e)
    {
        int n = 0;
        foreach (var _ in e) if (++n > MaxIndex) break;
        return n.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Valeur numérique d'un objet (booléen = absent : on ne compare pas un drapeau à un nombre).</summary>
    public static bool TryNumber(object? value, out double number)
    {
        number = 0;
        switch (value)
        {
            case int i: number = i; return true;
            case long l: number = l; return true;
            case short sh: number = sh; return true;
            case byte b: number = b; return true;
            case double d when !double.IsNaN(d) && !double.IsInfinity(d): number = d; return true;
            case float f when !float.IsNaN(f) && !float.IsInfinity(f): number = f; return true;
            case decimal m: number = (double)m; return true;
            case string s: return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
            case Enum en: number = Convert.ToInt64(en, CultureInfo.InvariantCulture); return true;
            default: return false;
        }
    }

    /// <summary>Identifiant d'écran (camelCase du contrat) pour une valeur de <see cref="Screen"/>.</summary>
    public static string? ScreenId(Screen screen) => screen switch
    {
        Screen.Menu => "menu",
        Screen.Map => "map",
        Screen.Combat => "combat",
        Screen.Reward => "reward",
        Screen.Shop => "shop",
        Screen.Event => "event",
        Screen.RestSite => "restSite",
        Screen.Treasure => "treasure",
        _ => null, // Unknown : aucun écran nommé
    };
}
