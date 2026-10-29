using System.Text;
using System.Text.Json;
using Ultima.Engine.Scoring;
using Ultima.Engine.Scoring.Cards;
using Ultima.Engine.Scoring.Deck;
using Ultima.Engine.Scoring.Meta;
using Ultima.Engine.Scoring.Potions;
using Ultima.Engine.Scoring.Relics;
using Ultima.Engine.Scoring.Threat;
using Xunit;

namespace Ultima.Engine.Tests;

/// <summary>
/// Garde-fou de PACKAGING (exigence Mio : un clone du dépôt doit fonctionner hors ligne, sans
/// télécharger les gros corpus). Ces tests tournent dans un clone propre produit par
/// <c>tools/check-offline.sh</c> (<c>git archive HEAD | tar -x</c>, donc uniquement le commité) :
/// s'ils passent là-bas, c'est que tout ce dont le moteur a besoin est versionné.
/// <para>
/// Ils échouent volontairement si quelqu'un : retire un catalogue du commit, gonfle <c>data/</c>
/// au-delà du budget de clone, code en dur un chemin utilisateur, ou fait dépendre le moteur de
/// <c>data/codex</c> / <c>data/index</c> / <c>data/exports</c> / <c>data/runs</c>.
/// </para>
/// </summary>
public sealed class OfflinePackagingTests
{
    /// <summary>Budget de données versionnées (docs/28 §3 : « &lt; 2 Mo au total »).</summary>
    private const long DataBudgetBytes = 2 * 1024 * 1024;

    private static readonly string[] HeavyCorpora =
        { "data/exports", "data/index", "data/runs", "data/codex" };

    /// <summary>Fichiers que le moteur lit à l'exécution et qui DOIVENT être commités.</summary>
    private static readonly string[] RequiredCatalogFiles =
    {
        "cards.json",               // CardCatalog (types, raretés, coûts, malédictions)
        "cards_effects.json",       // CardEffects (dégâts, bloc, pioche, énergie, pouvoirs)
        "monsters.json",            // ThreatModel + BossCatalog (PV, moves)
        "encounters.json",          // ThreatModel (rencontre → monstres, acte, type)
        "intents_by_monster.json",  // BossCatalog (plus gros coup)
        "potions.json",             // PotionCatalog
        "relics.json",              // RelicCatalog
        "priority_cards.json",      // PriorityCatalog (embarqué aussi dans Ultima.dll)
        "powers.json",              // catalogue déclaratif des 260 pouvoirs (hooks + expressions)
        "powers_damage.json",       // index historique des pouvoirs modifiant les dégâts
        "stats.json",              // compteurs de l'indexation (documentation/debug)
    };

    private static readonly string[] RequiredMetaFiles =
    {
        "card_tiers.json", "relic_tiers.json", "archetypes.json", "sources.json",
    };

    /// <summary>Racine du dépôt : on remonte depuis le binaire de test jusqu'à <c>Ultima.sln</c>.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 10 && dir is not null; i++)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Ultima.sln"))) return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Ultima.sln introuvable au-dessus de {AppContext.BaseDirectory} : le test ne peut pas " +
            "vérifier le packaging (clone incomplet ?)");
    }

    private static string DataDir() => Path.Combine(RepoRoot(), "data");

    [Fact]
    public void Data_resolves_inside_the_checkout()
    {
        var root = RepoRoot();
        var catalog = Path.GetFullPath(ScoringData.ResolveCatalogDir());
        var meta = Path.GetFullPath(ScoringData.ResolveMetaDir());

        Assert.True(Directory.Exists(catalog), $"data/catalog introuvable : {catalog}");
        Assert.True(Directory.Exists(meta), $"data/meta introuvable : {meta}");

        // Aucune fuite hors du checkout : pas de ULTIMA_DATA resté dans l'environnement, pas de
        // résolution vers un autre clone ou vers un dossier utilisateur.
        Assert.StartsWith(root, catalog, StringComparison.Ordinal);
        Assert.StartsWith(root, meta, StringComparison.Ordinal);
        Assert.Equal("catalog", Path.GetFileName(catalog));
        Assert.Equal("meta", Path.GetFileName(meta));
    }

    [Fact]
    public void Committed_data_stays_within_the_clone_budget()
    {
        var data = DataDir();
        long total = 0;
        var sizes = new List<string>();

        foreach (var file in EnumerateCommittedData(data))
        {
            if (!File.Exists(file)) continue; // signalé par Every_file_the_engine_reads_is_committed
            var info = new FileInfo(file);
            total += info.Length;
            sizes.Add($"{info.Length / 1024.0:0.#} Ko  {Path.GetRelativePath(data, file)}");
            Assert.True(info.Length < DataBudgetBytes,
                $"{Path.GetRelativePath(data, file)} pèse {info.Length} octets : trop gros pour être commité");
        }

        Assert.True(total <= DataBudgetBytes,
            $"données commitées = {total} octets (budget {DataBudgetBytes}) :\n  " + string.Join("\n  ", sizes));
    }

    /// <summary>Les fichiers de données commités (jamais les variantes locales/cache, non versionnées).</summary>
    private static IEnumerable<string> EnumerateCommittedData(string dataDir)
    {
        var catalog = Path.Combine(dataDir, "catalog");
        if (Directory.Exists(catalog))
        {
            // Tout JSON du catalogue, y compris un futur fichier oublié dans la liste ci-dessus.
            foreach (var path in Directory.GetFiles(catalog, "*.json").OrderBy(p => p, StringComparer.Ordinal))
                yield return path;
        }

        foreach (var name in RequiredMetaFiles)
            yield return Path.Combine(dataDir, "meta", name);
    }

    [Fact]
    public void Every_file_the_engine_reads_is_committed()
    {
        var data = DataDir();
        foreach (var name in RequiredCatalogFiles)
        {
            var path = Path.Combine(data, "catalog", name);
            Assert.True(File.Exists(path), $"fichier requis NON commité : data/catalog/{name}");
            Assert.True(new FileInfo(path).Length > 0, $"data/catalog/{name} est vide");
            using var _ = JsonDocument.Parse(File.ReadAllBytes(path)); // JSON valide
        }

        foreach (var name in RequiredMetaFiles)
        {
            var path = Path.Combine(data, "meta", name);
            Assert.True(File.Exists(path), $"fichier requis NON commité : data/meta/{name}");
            using var _ = JsonDocument.Parse(File.ReadAllBytes(path));
        }
    }

    [Fact]
    public void Engine_loads_every_committed_catalogue()
    {
        // Aucun de ces chargeurs ne doit retomber sur Empty dans un clone propre : ce sont les
        // volumes réels du catalogue commité (data/catalog/*), pas des valeurs inventées.
        var cards = CardCatalog.Default;
        Assert.True(cards.Count > 500, $"cartes du catalogue : {cards.Count}");
        Assert.NotNull(cards.Get("CARD.STRIKE_IRONCLAD") ?? cards.Get("STRIKE_IRONCLAD"));

        var effects = CardEffects.Default;
        Assert.True(effects.Count > 500, $"effets de cartes : {effects.Count}");
        Assert.NotEqual(CardEffects.Empty.Count, effects.Count);

        var bosses = BossCatalog.Default;
        Assert.True(bosses.Count > 100, $"boss/rencontres indexés : {bosses.Count}");

        var threat = ThreatModel.Default;
        Assert.True(threat.MonsterCount > 100, $"monstres du modèle de menace : {threat.MonsterCount}");
        Assert.True(threat.EncounterCount > 80, $"rencontres du modèle de menace : {threat.EncounterCount}");

        var potions = PotionCatalog.Load();
        Assert.True(potions.Count >= 60, $"potions : {potions.Count}");

        var relics = RelicCatalog.Load();
        Assert.True(relics.Count > 150, $"reliques : {relics.Count}");

        var meta = MetaTable.Load();
        Assert.True(meta.CardCount > 400, $"cartes de la méta : {meta.CardCount}");
        Assert.True(meta.RelicCount > 200, $"reliques de la méta : {meta.RelicCount}");
        Assert.NotEqual("empty", meta.Variant);
    }

    [Fact]
    public void Committed_data_contains_no_absolute_user_path()
    {
        // Motifs construits sans littéral : le code de vérification ne doit pas lui-même
        // déclencher le grep de tools/check-offline.sh sur les *.cs / *.json.
        var posixUserRoot = string.Concat('/', "home", '/');
        var macUserRoot = string.Concat('/', "Users", '/');
        var windowsUserRoot = string.Concat('\\', "Users", '\\');

        var data = DataDir();
        foreach (var file in EnumerateCommittedData(data))
        {
            if (!File.Exists(file)) continue;
            var text = File.ReadAllText(file);
            Assert.DoesNotContain(posixUserRoot, text);
            Assert.DoesNotContain(macUserRoot, text);
            Assert.DoesNotContain(windowsUserRoot, text);
        }
    }

    /// <summary>
    /// Le binaire du moteur ne doit contenir aucun littéral pointant vers un corpus non commité :
    /// c'est la preuve qu'aucun chemin de code ne peut exiger <c>data/codex</c> &amp; co. à l'exécution.
    /// (Les commentaires XML ne sont pas compilés : seuls les vrais littéraux sont vus ici.)
    /// </summary>
    [Fact]
    public void Engine_binary_embeds_no_heavy_corpus_path()
    {
        var assemblies = new[]
        {
            typeof(Scorer).Assembly,        // Ultima.Engine
            typeof(Ultima.Core.Priority.PriorityCatalog).Assembly, // Ultima.Core
        };

        foreach (var assembly in assemblies)
        {
            var path = assembly.Location;
            Assert.False(string.IsNullOrEmpty(path), $"assembly sans chemin : {assembly.GetName().Name}");
            var bytes = File.ReadAllBytes(path!);

            foreach (var heavy in HeavyCorpora)
            {
                // #US heap = UTF-16 ; on teste aussi l'UTF-8 par prudence.
                Assert.False(Contains(bytes, Encoding.Unicode.GetBytes(heavy)),
                    $"{assembly.GetName().Name}.dll contient le littéral « {heavy} » : le moteur ne doit " +
                    "jamais lire un corpus non commité");
                Assert.False(Contains(bytes, Encoding.UTF8.GetBytes(heavy)),
                    $"{assembly.GetName().Name}.dll contient le littéral « {heavy} » (utf8)");
            }
        }
    }

    private static bool Contains(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || haystack.Length < needle.Length) return false;
        for (int i = 0; i <= haystack.Length - needle.Length; i++)
        {
            int j = 0;
            while (j < needle.Length && haystack[i + j] == needle[j]) j++;
            if (j == needle.Length) return true;
        }

        return false;
    }
}
