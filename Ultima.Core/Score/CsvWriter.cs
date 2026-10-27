using System.Globalization;
using System.Text;

namespace Ultima.Core.Score;

/// <summary>
/// Export CSV RFC 4180 (séparateur virgule, fin de ligne CRLF, champs cités si besoin).
/// Une ligne par (joueur, carte) et par combat, puis une ligne par source non attribuée.
/// </summary>
public static class CsvWriter
{
    public const string Header =
        "combat_index,player,card_class,card_name,hits,unblocked,blocked,overkill,kills,attributed,reason";

    public const string NewLine = "\r\n";

    /// <summary>Cite le champ s'il contient une virgule, un guillemet ou un retour ligne ; double les guillemets.</summary>
    public static string Escape(string? field)
    {
        if (string.IsNullOrEmpty(field)) return string.Empty;
        if (field.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0) return field;
        return "\"" + field.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    public static string Line(int combatIndex, DamageAggregate a)
    {
        ArgumentNullException.ThrowIfNull(a);
        var inv = CultureInfo.InvariantCulture;
        return string.Join(",",
            combatIndex.ToString(inv),
            Escape(a.DealerName),
            Escape(a.SourceClass),
            Escape(a.SourceName),
            a.Hits.ToString(inv),
            a.Unblocked.ToString(inv),
            a.Blocked.ToString(inv),
            a.Overkill.ToString(inv),
            a.Kills.ToString(inv),
            a.Attributed ? "true" : "false",
            Escape(a.Reason));
    }

    public static IReadOnlyList<string> Lines(DamageLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        return ledger.Aggregates().Select(a => Line(ledger.CombatIndex, a)).ToList();
    }

    /// <summary>Texte à ajouter au fichier : en-tête si fichier neuf, puis les lignes, chacune terminée par CRLF.</summary>
    public static string Render(DamageLedger ledger, bool includeHeader)
    {
        var sb = new StringBuilder();
        var lines = Lines(ledger);
        if (includeHeader) sb.Append(Header).Append(NewLine);
        foreach (var l in lines) sb.Append(l).Append(NewLine);
        return sb.ToString();
    }

    /// <summary>
    /// Plus grand combat_index déjà présent dans un CSV existant (0 si aucun) : la numérotation
    /// continue d'une session de jeu à l'autre au lieu de repartir à 1.
    /// </summary>
    public static int LastCombatIndex(IEnumerable<string> existingLines)
    {
        ArgumentNullException.ThrowIfNull(existingLines);
        var max = 0;
        foreach (var line in existingLines)
        {
            var comma = line.IndexOf(',');
            if (comma <= 0) continue;
            if (int.TryParse(line.AsSpan(0, comma), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > max)
                max = n;
        }
        return max;
    }
}
