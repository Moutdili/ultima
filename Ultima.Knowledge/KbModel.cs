using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ultima.Knowledge;

// Format de kb.json (généré localement par tools/kb/build-kb, JAMAIS commité : contient les textes du jeu).
// Sérialisation camelCase, champs nuls omis.

public sealed class KbDocument
{
    public const int CurrentSchema = 1;

    public int Schema { get; set; } = CurrentSchema;
    public string GameVersion { get; set; } = "";
    public string GeneratedAt { get; set; } = "";
    public string Generator { get; set; } = "";
    public KbSource Source { get; set; } = new();
    public List<KbCard> Cards { get; set; } = [];
    public List<KbRelic> Relics { get; set; } = [];
    public List<KbEnchantment> Enchantments { get; set; } = [];
    public List<KbPotion> Potions { get; set; } = [];
    public List<KbNamed> Encounters { get; set; } = [];
    public List<KbNamed> Characters { get; set; } = [];
    /// <summary>Mots-clés de carte : "Exhaust" → libellés.</summary>
    public Dictionary<string, KbLabel> Keywords { get; set; } = [];
    /// <summary>Libellés d'interface : "CARD_TYPE.ATTACK", "CARD_RARITY.CURSE", "RELIC_RARITY.SHOP"…</summary>
    public Dictionary<string, KbLabel> Labels { get; set; } = [];
    public KbCoverage Coverage { get; set; } = new();
}

public sealed class KbSource
{
    public string GameDir { get; set; } = "";
    public string LocalizationDir { get; set; } = "";
    public string ModelsDir { get; set; } = "";
    public string CatalogDir { get; set; } = "";
    /// <summary>SHA-256 (16 premiers hex) des fichiers lus, pour la traçabilité.</summary>
    public Dictionary<string, string> Hashes { get; set; } = [];
}

public sealed class KbLabel
{
    public string? Fr { get; set; }
    public string? En { get; set; }
    public string Best(string fallback) => Fr ?? En ?? fallback;
}

/// <summary>Variable de description (équivalent d'un DynamicVar canonique).</summary>
public sealed class KbVar
{
    public string Name { get; set; } = "";
    /// <summary>number | bool | string | calculated | amount (= montant de l'enchantement) | unknown</summary>
    public string Kind { get; set; } = "number";
    public decimal Base { get; set; }
    /// <summary>Delta appliqué à l'amélioration (OnUpgrade → UpgradeValueBy).</summary>
    public decimal Upgrade { get; set; }
    public string? Str { get; set; }
    /// <summary>Référence au titre d'un autre modèle (ex. "ENCHANTMENT.GOOPY"), résolue en <see cref="Str"/>.</summary>
    public string? Ref { get; set; }
}

public class KbNamed
{
    public string Id { get; set; } = "";
    public string ClassName { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>fra | eng | id (repli signalé).</summary>
    public string NameSource { get; set; } = "id";
    public string? NameFr { get; set; }
    public string? NameEn { get; set; }
}

public class KbEntry : KbNamed
{
    public string Description { get; set; } = "";
    /// <summary>fra | eng | none.</summary>
    public string DescriptionSource { get; set; } = "none";
    public string? TemplateFr { get; set; }
    public string? TemplateEn { get; set; }
    public List<KbVar> Vars { get; set; } = [];
    /// <summary>Variables citées par le texte mais absentes des valeurs statiques (rendues « X »).</summary>
    public List<string>? UnresolvedVars { get; set; }
}

public sealed class KbCard : KbEntry
{
    public string Type { get; set; } = "";
    public string Rarity { get; set; } = "";
    public int Cost { get; set; }
    public int? CostUpgraded { get; set; }
    public bool CostX { get; set; }
    public string Owner { get; set; } = "";
    public bool IsCurse { get; set; }
    public string Target { get; set; } = "";
    public bool GainsBlock { get; set; }
    public int MaxUpgradeLevel { get; set; } = 1;
    public List<string> Keywords { get; set; } = [];
    public List<string>? KeywordsUpgraded { get; set; }
    public string? DescriptionUpgraded { get; set; }
}

public sealed class KbRelic : KbEntry
{
    public string Rarity { get; set; } = "";
    public string Owner { get; set; } = "";
}

public sealed class KbPotion : KbEntry
{
    public string Rarity { get; set; } = "";
    public string Owner { get; set; } = "";
    public string Usage { get; set; } = "";
}

public sealed class KbEnchantment : KbEntry
{
    public bool ShowAmount { get; set; }
    /// <summary>Texte ajouté sur la carte enchantée (extraCardText), rendu sans montant connu.</summary>
    public string? ExtraCardText { get; set; }
    public string? ExtraTemplateFr { get; set; }
    public string? ExtraTemplateEn { get; set; }
}

public sealed class KbCoverage
{
    public Dictionary<string, KbCategoryCoverage> Categories { get; set; } = [];
    /// <summary>Entrées sans nom FR (repli EN ou identifiant), par catégorie.</summary>
    public Dictionary<string, List<string>> UnresolvedFr { get; set; } = [];
    /// <summary>Divergences relevées (catalogue vs source, variables inconnues…).</summary>
    public List<string> Notes { get; set; } = [];
}

public sealed class KbCategoryCoverage
{
    public int Total { get; set; }
    public int NameFr { get; set; }
    public int NameEnFallback { get; set; }
    public int NameIdFallback { get; set; }
    public int DescriptionFr { get; set; }
    public int DescriptionEnFallback { get; set; }
    public int DescriptionNone { get; set; }
    public int WithUnresolvedVars { get; set; }
}

public static class KbJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
