using Ultima.Engine.Scoring.Model;

namespace Ultima.Engine.Scoring.Cards;

/// <summary>
/// Tags mécaniques d'une carte. Deux sources explicites, aucune autre, aucune boîte noire :
/// <list type="number">
///   <item><b>Nom anglais</b> (<see cref="NamingRule"/>) : correspondance sur mot entier ou préfixe
///   de mot (pour attraper « Poisoned », « Hailstorm », « Guards!!! »). Le filtre <c>Owners</c>
///   limite les tags propres à un personnage (orbe = Defect, shiv = Silent, épuisement = Ironclad)
///   pour éviter les faux positifs (« Thunderclap » n'est pas un orbe de foudre).</item>
///   <item><b>Archétypes</b> de <c>data/meta/archetypes.json</c> (<see cref="ArchetypeRule"/>) : un
///   cluster ou un guide dont le nom contient un mot-clé transmet son tag à toutes ses
///   <c>key_cards</c>. C'est la seule façon d'obtenir un tag qu'aucun nom ne porte
///   (ex. « Outbreak » dans le cluster « Deadly Poison + Poisoned Stab »).</item>
/// </list>
/// Le tag <c>power</c> vient du <b>type</b> renvoyé par la méta (moteur de scaling : les pouvoirs
/// s'empilent) et non du nom. Rien n'est appris, rien n'est aléatoire : mêmes entrées ⇒ mêmes tags.
/// </summary>
internal static class CardTags
{
    internal readonly record struct TagHit(string Tag, string Label, string Basis);

    /// <summary>Règle de tag par mots-clés du nom anglais. <c>Owners</c> null = tous personnages.</summary>
    private sealed record NamingRule(string Tag, string Label, string[] Keywords, string[]? Owners, int Threshold);

    /// <summary>Règle de tag par nom d'archétype (français ou anglais), source archetypes.json.</summary>
    private sealed record ArchetypeRule(string Tag, string[] Keywords);

    /// <summary>Seuils : nombre de cartes du deck nécessaires pour que le tag « compte ».</summary>
    private const int DefaultThreshold = 3;

    /// <summary>Un deck « moteur » compte au moins 5 pouvoirs : c'est là que le tag power paie.</summary>
    private const int PowerThreshold = 5;

    private static readonly NamingRule[] NamingRules =
    {
        // Poison (Silent) — poison, envenom, venom, noxious, snakebite.
        new("poison", "Poison", new[] { "poison", "envenom", "venom", "noxious", "snakebite", "toxic" },
            new[] { "silent", "colorless" }, DefaultThreshold),
        // Faiblesse — weak, weaken, malaise, debilitate, enfeebling.
        new("weak", "Faiblesse", new[] { "weak", "weaken", "malaise", "debilitate", "enfeebl" },
            null, DefaultThreshold),
        // Vulnérabilité — vulnerable, expose.
        new("vulnerable", "Vulnérabilité", new[] { "vulnerable", "expose" }, null, DefaultThreshold),
        // Force (self-scaling) — strength, inflame, rupture, primal, « demon form ».
        new("strength", "Force", new[] { "strength", "inflame", "rupture", "primal", "demon form" },
            null, DefaultThreshold),
        // Focus (Defect) — focus.
        new("focus", "Focus", new[] { "focus" }, new[] { "defect" }, DefaultThreshold),
        // Givre (Defect) — frost, ice, chill, cold, glacier, hail, snow, coolhead(ed).
        new("frost", "Givre",
            new[] { "frost", "ice", "chill", "cold", "glacier", "hail", "snow", "coolhead" },
            new[] { "defect" }, DefaultThreshold),
        // Foudre (Defect) — lightning, thunder, voltaic, tesla, tempest, spark.
        new("lightning", "Foudre",
            new[] { "lightning", "thunder", "voltaic", "tesla", "tempest", "spark" },
            new[] { "defect" }, DefaultThreshold),
        // Shivs (Silent) — shiv, knife, dagger, noms de cartes à couteaux.
        new("shiv", "Shivs",
            new[]
            {
                "shiv", "knife", "dagger", "blade dance", "phantom blades", "blade of ink",
                "hidden daggers", "infinite blades", "accuracy", "flechette", "fan of knives",
            },
            new[] { "silent" }, DefaultThreshold),
        // Bloc — block, defend, guard, shield, armor, aegis, barricade, impervious, wall, bulwark,
        // parry, blur, dodge, deflect, tank, taunt, shrug.
        new("block", "Bloc",
            new[]
            {
                "block", "defend", "guard", "shield", "armor", "aegis", "barricade", "impervious",
                "wall", "bulwark", "parry", "blur", "dodge", "deflect", "tank", "taunt", "shrug",
            },
            null, DefaultThreshold),
        // Pioche — draw, skim, acrobatics, backflip, expertise, prepared, scrawl, discovery,
        // machine learning, thinking ahead.
        new("draw", "Pioche",
            new[]
            {
                "draw", "skim", "acrobatics", "backflip", "expertise", "prepared", "scrawl",
                "discovery", "machine learning", "thinking ahead",
            },
            null, DefaultThreshold),
        // Épuisement (Ironclad) — exhaust, burn, cinder, ashen, feel no pain, dark embrace…
        new("exhaust", "Épuisement",
            new[]
            {
                "exhaust", "burn", "cinder", "ashen", "feel no pain", "dark embrace",
                "second wind", "fiend fire",
            },
            new[] { "ironclad" }, DefaultThreshold),
        // Défausse (Silent) — discard, reflex, tactician, sly, calculated gamble.
        new("discard", "Défausse",
            new[] { "discard", "reflex", "tactician", "sly", "calculated gamble" },
            new[] { "silent", "colorless" }, DefaultThreshold),
        // Griffe (Defect) — claw.
        new("claw", "Griffe", new[] { "claw" }, new[] { "defect" }, DefaultThreshold),
    };

    private static readonly ArchetypeRule[] ArchetypeRules =
    {
        new("poison", new[] { "poison" }),
        new("shiv", new[] { "shiv" }),
        new("lightning", new[] { "foudre" }),
        new("frost", new[] { "givre" }),
        new("dark", new[] { "ténèbres", "tenebres" }),
        new("discard", new[] { "défausse", "defausse", "sly" }),
        new("claw", new[] { "claw" }),
    };

    private static readonly char[] WordSeparators =
        { ' ', '-', '_', ',', '.', '!', '?', ':', ';', '\'', '’', '"', '(', ')', '/' };

    /// <summary>Tags triés (ordre alphabétique stable) avec leur libellé et leur justification.</summary>
    internal static IReadOnlyList<TagHit> For(MetaEntry? meta, IMetaTable table)
    {
        if (meta == null) return Array.Empty<TagHit>();

        var hits = new Dictionary<string, TagHit>(StringComparer.Ordinal);
        var owner = Ids.CharacterKey(meta.Owner);
        var name = string.IsNullOrWhiteSpace(meta.NameEng) ? meta.ClassName : meta.NameEng;

        foreach (var rule in NamingRules)
        {
            if (rule.Owners != null && !rule.Owners.Contains(owner, StringComparer.OrdinalIgnoreCase)) continue;
            if (!rule.Keywords.Any(k => NameMatches(name, k))) continue;
            hits[rule.Tag] = new TagHit(rule.Tag, rule.Label, $"nom anglais « {name} »");
        }

        if (string.Equals(meta.Type, "Power", StringComparison.OrdinalIgnoreCase))
            hits["power"] = new TagHit("power", "Powers", "type Power (méta)");

        if (!string.IsNullOrEmpty(owner))
        {
            foreach (var archetype in table.Archetypes(owner))
            {
                if (!archetype.KeyCardIds.Contains(meta.Id, StringComparer.OrdinalIgnoreCase)) continue;
                var archetypeName = archetype.Name.ToLowerInvariant();
                foreach (var rule in ArchetypeRules)
                {
                    if (!rule.Keywords.Any(k => archetypeName.Contains(k, StringComparison.Ordinal))) continue;
                    if (hits.ContainsKey(rule.Tag)) continue;
                    hits[rule.Tag] = new TagHit(rule.Tag, LabelOf(rule.Tag),
                        $"archétype « {archetype.Name} » (data/meta/archetypes.json)");
                }
            }
        }

        return hits.Values.OrderBy(h => h.Tag, StringComparer.Ordinal).ToArray();
    }

    /// <summary>Libellé affichable d'un tag (français quand il existe).</summary>
    internal static string LabelOf(string tag)
    {
        foreach (var rule in NamingRules)
            if (rule.Tag == tag) return rule.Label;
        return tag switch
        {
            "power" => "Powers",
            "dark" => "Ténèbres",
            _ => tag,
        };
    }

    /// <summary>Nombre de cartes du deck nécessaires pour que ce tag compte.</summary>
    internal static int ThresholdOf(string tag) => tag == "power" ? PowerThreshold : DefaultThreshold;

    private static bool NameMatches(string name, string keyword)
    {
        if (keyword.Contains(' '))
        {
            var normalized = " " + string.Join(' ', name.ToLowerInvariant().Split(WordSeparators,
                StringSplitOptions.RemoveEmptyEntries)) + " ";
            return normalized.Contains(keyword, StringComparison.Ordinal);
        }

        foreach (var word in name.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries))
            if (word.StartsWith(keyword, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
