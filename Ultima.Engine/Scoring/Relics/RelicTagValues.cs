using Ultima.Engine.Contracts;
using Ultima.Engine.Scoring.Model;

namespace Ultima.Engine.Scoring.Relics;

/// <summary>Valeur de repli d'une relique sans note communautaire, calculée sur ses tags.</summary>
public sealed record RelicTagScore(int Total, IReadOnlyList<ScoreTerm> Terms, string Basis);

/// <summary>
/// Convertit les tags d'une relique en points, <b>uniquement quand aucune mesure n'existe</b>.
/// </summary>
/// <remarks>
/// <para>
/// 163 reliques sont taguées (docs/36) mais muettes pour le scorer : ni consensus communautaire,
/// ni entrée méta. Une relique « Diadème » ne vaut rien, mais une relique qui ressuscite vaut
/// quelque chose. Ce barème donne un ordre de grandeur explicable, jamais une note S.
/// </para>
/// <para>
/// Bornes : plafond <see cref="MaxTagScore"/> (55 = B). Une règle de tag ne doit pas battre une
/// relique réellement notée par la communauté ; elle sert à sortir du 0 silencieux, pas à classer.
/// Les tags négatifs (<c>penalty</c>) ne peuvent pas rendre le total négatif : le score reste 0..100.
/// </para>
/// </remarks>
public static class RelicTagValues
{
    /// <summary>Plafond d'une note dérivée des tags : une règle ne bat jamais une mesure.</summary>
    public const int MaxTagScore = 55;

    /// <summary>Points par tag. Valeurs volontairement petites et ordonnées par impact réel.</summary>
    private static readonly Dictionary<string, int> Points = new(StringComparer.OrdinalIgnoreCase)
    {
        ["revive"] = 8,        // une seconde vie, le seul effet qui annule une mort
        ["maxhp"] = 6,
        ["damage"] = 5,
        ["strength"] = 4,
        ["energy"] = 4,        // une énergie par tour change tout le deck
        ["draw"] = 4,
        ["mitigation"] = 4,
        ["debuff"] = 4,
        ["block"] = 3,
        ["heal"] = 3,
        ["gold"] = 3,
        ["upgrade"] = 3,
        ["remove"] = 3,
        ["transform"] = 3,
        ["aoe"] = 3,
        ["cost"] = 3,
        ["reward"] = 2,
        ["potion"] = 2,
        ["relic"] = 2,
        ["rest"] = 2,
        ["path"] = 2,
        ["elite"] = 2,
        ["orb"] = 2,
        ["copy"] = 2,
        ["dexterity"] = 2,
        ["poison"] = 2,
        ["thorns"] = 2,
        ["summon"] = 2,
        ["curse"] = 2,
        ["enchant"] = 2,
        ["exhaust"] = 1,
        ["retain"] = 1,
        ["discard"] = 1,
        ["add_card"] = 1,      // dilue le deck : positif mais faible
        ["shop"] = 1,
        ["star"] = 1,
        ["focus"] = 1,
        ["forge"] = 1,
        ["ethereal"] = 0,
        ["no_effect"] = 0,
        ["penalty"] = -4,      // effet négatif explicite
    };

    private static readonly Dictionary<string, string> Labels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["revive"] = "Résurrection",
        ["maxhp"] = "PV max",
        ["damage"] = "Dégâts",
        ["strength"] = "Force",
        ["energy"] = "Énergie",
        ["draw"] = "Pioche",
        ["mitigation"] = "Réduction de dégâts",
        ["debuff"] = "Affaiblissement",
        ["block"] = "Armure",
        ["heal"] = "Soin",
        ["gold"] = "Or",
        ["upgrade"] = "Amélioration",
        ["remove"] = "Retrait de carte",
        ["transform"] = "Transformation",
        ["aoe"] = "Dégâts de zone",
        ["cost"] = "Coût des cartes",
        ["reward"] = "Récompenses",
        ["potion"] = "Potions",
        ["relic"] = "Reliques",
        ["rest"] = "Repos",
        ["path"] = "Carte / route",
        ["elite"] = "Élites",
        ["orb"] = "Orbes",
        ["copy"] = "Copie de carte",
        ["dexterity"] = "Dextérité",
        ["poison"] = "Poison",
        ["thorns"] = "Épines",
        ["summon"] = "Invocation",
        ["curse"] = "Malédiction",
        ["enchant"] = "Enchantement",
        ["exhaust"] = "Épuisement",
        ["retain"] = "Rétention",
        ["discard"] = "Défausse",
        ["add_card"] = "Ajout de carte",
        ["shop"] = "Boutique",
        ["star"] = "Étoiles",
        ["focus"] = "Concentration",
        ["forge"] = "Forge",
        ["ethereal"] = "Éthéré",
        ["no_effect"] = "Sans effet",
        ["penalty"] = "Effet négatif",
    };

    /// <summary>
    /// Note de repli d'une relique : somme bornée des tags connus, chaque terme portant le tag
    /// et le début de la description FR du jeu.
    /// </summary>
    public static RelicTagScore Evaluate(RelicEntry? entry)
    {
        if (entry is null)
            return new RelicTagScore(0, Array.Empty<ScoreTerm>(), "relique absente du catalogue du jeu");

        var tags = entry.Tags ?? Array.Empty<string>();
        if (tags.Count == 0)
            return new RelicTagScore(0, Array.Empty<ScoreTerm>(), "aucun tag au catalogue");

        var terms = new List<ScoreTerm>(tags.Count);
        var total = 0;

        foreach (var tag in tags.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Points.TryGetValue(tag, out var points) || points == 0) continue;
            total += points;
            terms.Add(new ScoreTerm(
                $"Effet : {Label(tag)}",
                points,
                $"tag « {tag} » — {Shorten(entry.DescriptionFr)}"));
        }

        if (terms.Count == 0)
            return new RelicTagScore(0, Array.Empty<ScoreTerm>(), $"tags sans effet ({string.Join(", ", tags)})");

        // Plafond : le terme porte l'écart exact, donc la somme des termes reste égale au total.
        if (total > MaxTagScore)
        {
            terms.Add(new ScoreTerm("Plafond des tags", MaxTagScore - total,
                $"une règle de tag ne dépasse pas {MaxTagScore} (B) — mesure communautaire absente"));
            total = MaxTagScore;
        }

        // Un effet négatif ne rend pas le score négatif : le contrat borne 0..100.
        if (total < 0)
        {
            terms.Add(new ScoreTerm("Plancher", -total, "effet négatif : la note ne descend pas sous 0"));
            total = 0;
        }

        return new RelicTagScore(total, terms,
            $"règle de tags ({terms.Count} termes) — aucune note communautaire disponible");
    }

    private static string Label(string tag) => Labels.TryGetValue(tag, out var label) ? label : tag;

    private static string Shorten(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "description absente";
        var clean = text.Replace('\n', ' ').Trim();
        return clean.Length <= 90 ? clean : clean[..87] + "…";
    }
}
