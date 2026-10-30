using System.Text.Json;
using Ultima.Engine.Scoring;

namespace Ultima.Engine.Scoring.Cards;

/// <summary>
/// Coût des cartes tel que le <b>catalogue</b> le déclare (<c>data/catalog/cards.json</c>, 578 cartes),
/// indépendamment de la méta communautaire — qui ne couvre qu'une partie des cartes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pourquoi ce fichier existe.</b> La courbe de coût comparait le coût d'une carte à la densité
/// du deck en n'interrogeant que <see cref="Meta.IMetaTable"/>. Or la méta ne note pas toutes les
/// cartes : un deck de 14 cartes n'exposait que 4 coûts, et le terme affichait « 2/4 cartes à
/// 1 énergie » en concluant à la saturation (−4) alors que le deck en avait 2 sur 14. Le catalogue,
/// lui, donne le coût de chaque carte du jeu : c'est la seule source qui permette un dénominateur
/// honnête.
/// </para>
/// <para>
/// Les identifiants sont normalisés (préfixe <c>CARD.</c> retiré, casse et ponctuation ignorées) :
/// <c>POMMEL_STRIKE</c>, <c>PommelStrike</c> et <c>card.pommel_strike</c> désignent la même entrée.
/// Une carte inconnue n'a pas de coût — jamais un 0 par défaut.
/// </para>
/// </remarks>
public static class CardCosts
{
    private static readonly Lazy<IReadOnlyDictionary<string, int>> Lazy = new(
        Load, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Mémo des résolutions déjà faites (identifiant brut → coût, ou null si inconnu).</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int?> Cache =
        new(StringComparer.Ordinal);

    /// <summary>Nombre de cartes dont le coût est connu (0 si le catalogue est introuvable).</summary>
    public static int Count => Lazy.Value.Count;

    /// <summary>Le catalogue a-t-il pu être lu ?</summary>
    public static bool IsLoaded => Lazy.Value.Count > 0;

    /// <summary>
    /// Coût déclaré par le catalogue, ou <c>false</c> si la carte est inconnue. Le résultat est
    /// mémorisé par identifiant brut : une évaluation de deck interroge 60 fois les mêmes cartes,
    /// et la normalisation de clé allouait une chaîne à chaque appel (la courbe de coût est dans
    /// le chemin chaud de la note de deck).
    /// </summary>
    public static bool TryCost(string? cardId, out int cost)
    {
        cost = 0;
        if (string.IsNullOrWhiteSpace(cardId)) return false;

        var known = Cache.GetOrAdd(cardId, static id =>
            Lazy.Value.TryGetValue(Key(id), out var found) ? found : (int?)null);

        if (known is null) return false;
        cost = known.Value;
        return true;
    }

    /// <summary>Clé de rapprochement : dernier segment après un point, sans ponctuation, en minuscules.</summary>
    internal static string Key(string raw)
    {
        var text = raw.Trim();
        var dot = text.LastIndexOf('.');
        if (dot >= 0 && dot < text.Length - 1) text = text[(dot + 1)..];

        Span<char> buffer = text.Length <= 128 ? stackalloc char[text.Length] : new char[text.Length];
        var n = 0;
        foreach (var ch in text)
        {
            if (!char.IsLetterOrDigit(ch)) continue;
            buffer[n++] = char.ToLowerInvariant(ch);
        }

        return new string(buffer[..n]);
    }

    private static IReadOnlyDictionary<string, int> Load()
    {
        // Même résolution que les autres catalogues (ULTIMA_DATA, puis remontée depuis
        // l'assembly) : trois résolveurs différents finissent par diverger sur une machine où
        // plusieurs worktrees tournent.
        var path = Path.Combine(ScoringData.ResolveCatalogDir(), "cards.json");
        if (!File.Exists(path)) return new Dictionary<string, int>(0);

        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return new Dictionary<string, int>(0);

            var map = new Dictionary<string, int>(640, StringComparer.Ordinal);
            foreach (var row in document.RootElement.EnumerateArray())
            {
                if (!row.TryGetProperty("class", out var cls) || cls.ValueKind != JsonValueKind.String) continue;
                if (!row.TryGetProperty("cost", out var costEl) || costEl.ValueKind != JsonValueKind.Number) continue;
                if (!costEl.TryGetInt32(out var cost)) continue;

                var key = Key(cls.GetString()!);
                if (key.Length > 0) map[key] = cost;
            }

            return map;
        }
        catch (IOException)
        {
            return new Dictionary<string, int>(0);
        }
        catch (JsonException)
        {
            return new Dictionary<string, int>(0);
        }
    }

}
