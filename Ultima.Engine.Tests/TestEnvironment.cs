using System.Runtime.CompilerServices;

namespace Ultima.Engine.Tests;

/// <summary>
/// Les tests doivent lire les catalogues **du dépôt**, jamais d'un override laissé dans le shell.
/// Sur une machine où plusieurs worktrees tournent en parallèle, un agent peut exporter
/// <c>ULTIMA_DATA</c> vers son worktree : sans ce garde-fou, la suite vérifie les données d'un
/// autre arbre (9 tests de contraintes en échec constaté, avec des catalogues plus anciens).
/// </summary>
internal static class TestEnvironment
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        Environment.SetEnvironmentVariable("ULTIMA_DATA", null);

        // Même règle pour la méta : data/meta/local/ (notes Untapped de la machine) ne doit pas
        // décider si un test passe. Les tests lisent la variante commitée, toujours.
        Environment.SetEnvironmentVariable("ULTIMA_META_NO_LOCAL", "1");
    }
}
