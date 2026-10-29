using System.Text;

namespace Ultima.Knowledge.Build;

/// <summary>Rapport de couverture lisible (Markdown). Contient des identifiants, jamais de texte du jeu.</summary>
public static class CoverageReport
{
    public static string ToMarkdown(KbDocument doc)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Couverture de la base de connaissance ULTIMA");
        sb.AppendLine();
        sb.AppendLine($"- Jeu : {doc.GameVersion} — généré le {doc.GeneratedAt} par {doc.Generator}");
        sb.AppendLine($"- Source : `{doc.Source.GameDir}` ; catalogue : `{doc.Source.CatalogDir}`");
        sb.AppendLine();
        sb.AppendLine("| Catégorie | Total | Nom FR | Repli EN | Repli id | Desc. FR | Desc. EN | Sans desc. | Var. non résolues |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var (name, c) in doc.Coverage.Categories)
        {
            var pct = c.Total == 0 ? 100.0 : 100.0 * c.NameFr / c.Total;
            sb.AppendLine($"| {name} | {c.Total} | {c.NameFr} ({pct:0.#} %) | {c.NameEnFallback} | {c.NameIdFallback} | " +
                          $"{c.DescriptionFr} | {c.DescriptionEnFallback} | {c.DescriptionNone} | {c.WithUnresolvedVars} |");
        }
        sb.AppendLine();
        sb.AppendLine("## Entrées sans nom français");
        foreach (var (name, list) in doc.Coverage.UnresolvedFr)
        {
            sb.AppendLine($"- **{name}** ({list.Count}) : {(list.Count == 0 ? "aucune" : string.Join(", ", list))}");
        }
        sb.AppendLine();
        sb.AppendLine("## Variables non résolues statiquement (rendues « X »)");
        var withVars = doc.Cards.Cast<KbEntry>().Concat(doc.Relics).Concat(doc.Potions).Concat(doc.Enchantments)
            .Where(e => e.UnresolvedVars is { Count: > 0 }).ToList();
        if (withVars.Count == 0) sb.AppendLine("- aucune");
        foreach (var e in withVars) sb.AppendLine($"- {e.Id} : {string.Join(", ", e.UnresolvedVars!)}");
        sb.AppendLine();
        sb.AppendLine("## Descriptions absentes (FR et EN)");
        var none = doc.Cards.Cast<KbEntry>().Concat(doc.Relics).Concat(doc.Potions).Concat(doc.Enchantments)
            .Where(e => e.DescriptionSource == "none").Select(e => e.Id).ToList();
        sb.AppendLine(none.Count == 0 ? "- aucune" : "- " + string.Join(", ", none));
        sb.AppendLine();
        sb.AppendLine($"## Notes d'extraction ({doc.Coverage.Notes.Count})");
        foreach (var n in doc.Coverage.Notes) sb.AppendLine($"- {n}");
        return sb.ToString();
    }
}
