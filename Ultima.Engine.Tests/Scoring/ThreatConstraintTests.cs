using System.Text.Json;
using Ultima.Engine.Scoring;
using Ultima.Engine.Scoring.Threat;
using Xunit;

namespace Ultima.Engine.Tests.Scoring;

/// <summary>
/// S19 — contraintes d'ennemis (docs/32 §4) : plafond de dégâts subis (Intangible = 1,
/// HardToKill = Amount) et respawns, extraits du code du jeu par
/// <c>tools/index_constraints.py</c> puis exposés par <see cref="ThreatModel"/>.
/// Les valeurs attendues sont celles du source, vérifiables à la main :
/// <c>Models/Powers/IntangiblePower.cs:46</c> (return 1m),
/// <c>Models/Powers/HardToKillPower.cs:14</c> (return base.Amount = 9 pour Exoskeleton),
/// <c>Models/Monsters/TestSubject.cs:164</c> (Respawns &gt;= 2).
/// </summary>
public sealed class ThreatConstraintTests
{
    private static readonly ThreatModel Model = ThreatModel.Default;

    private static string CatalogDir => ScoringData.ResolveCatalogDir();

    /// <summary>Plafond non nul, ou échec explicite (évite les Assert.Equal sur int?).</summary>
    private static int Cap(ThreatModel model, string encounterId, int ascension = 0)
    {
        var cap = model.DamageCap(encounterId, ascension);
        Assert.NotNull(cap);
        return cap!.Value;
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ultima-s19-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Catalog_publie_les_monstres_et_rencontres_contraints()
    {
        // Trois monstres portent une contrainte dans le source : SoulFysh, Exoskeleton,
        // TestSubject ; quatre rencontres les contiennent (dont les deux Exoskeletons).
        Assert.True(Model.MonsterConstraintCount >= 3,
            $"monstres contraints: {Model.MonsterConstraintCount}");
        Assert.True(Model.ConstraintCount >= 4, $"rencontres contraintes: {Model.ConstraintCount}");

        Assert.NotNull(Model.MonsterConstraint("SOUL_FYSH"));
        Assert.NotNull(Model.MonsterConstraint("SoulFysh"));   // nom de classe accepté
        Assert.NotNull(Model.MonsterConstraint("EXOSKELETON"));
        Assert.NotNull(Model.MonsterConstraint("TEST_SUBJECT"));

        // Un monstre sans contrainte n'a pas d'entrée inventée.
        Assert.Null(Model.MonsterConstraint("AEONGLASS"));
        Assert.Null(Model.MonsterConstraint("PAS_UN_MONSTRE"));
        Assert.Null(Model.Constraint("PAS_UNE_RENCONTRE"));
    }

    [Fact]
    public void TestSubject_revient_deux_fois()
    {
        // ShouldDisappearFromDoom => Respawns >= 2 : il meurt trois fois, respawn deux fois.
        Assert.Equal(2, Model.Respawns("TEST_SUBJECT_BOSS"));
        Assert.Equal(2, Model.Respawns("ENCOUNTER.TEST_SUBJECT_BOSS"));
        Assert.Equal(2, Model.Respawns("test_subject_boss"));

        var constraint = Model.MonsterConstraint("TEST_SUBJECT");
        Assert.NotNull(constraint);
        Assert.Equal(2, constraint!.Respawns);

        // Aucun respawn ailleurs : 0, jamais une valeur par défaut plausible.
        Assert.Equal(0, Model.Respawns("AEONGLASS_BOSS"));
        Assert.Equal(0, Model.Respawns("PAS_UNE_RENCONTRE"));
    }

    [Fact]
    public void Un_monstre_intangible_plafonne_les_degats_a_un()
    {
        // SoulFysh applique Intangible 2 sur son move FADE ; IntangiblePower.ModifyDamageCap
        // renvoie 1 : les dégâts subis sont plafonnés à 1 pendant la durée du pouvoir.
        Assert.Equal(1, Cap(Model, "SOUL_FYSH_BOSS", 0));
        Assert.Equal(1, Cap(Model, "ENCOUNTER.SOUL_FYSH_BOSS", 10));

        var constraint = Model.MonsterConstraint("SOUL_FYSH");
        Assert.NotNull(constraint);
        Assert.Equal(1, constraint!.DamageCap);
        Assert.Null(constraint.HardToKill);
        Assert.Equal(1, constraint.Cap);
        Assert.Equal("periodic", constraint.CapScope);   // le plafond n'est pas permanent
        Assert.False(constraint.IsPermanent);
        Assert.Equal("INTANGIBLE", constraint.CapPower);
        Assert.Contains("INTANGIBLE", constraint.Powers);
        Assert.Contains("IntangiblePower.cs", constraint.Source);
        Assert.Contains("SoulFysh.cs", constraint.Source);
    }

    [Fact]
    public void Exoskeleton_plafonne_a_neuf_par_hard_to_kill()
    {
        // AfterAddedToRoom applique HardToKill 9 (Amount) : plafond permanent à 9.
        Assert.Equal(9, Cap(Model, "EXOSKELETONS_NORMAL"));
        Assert.Equal(9, Cap(Model, "EXOSKELETONS_WEAK"));

        var constraint = Model.MonsterConstraint("EXOSKELETON");
        Assert.NotNull(constraint);
        Assert.Null(constraint!.DamageCap);
        Assert.Equal(9, constraint.HardToKill);
        Assert.Equal(9, constraint.Cap);
        Assert.Equal("always", constraint.CapScope);
        Assert.True(constraint.IsPermanent);
        Assert.Contains("HardToKillPower.cs", constraint.Source);
    }

    [Fact]
    public void Le_plafond_ne_depend_pas_de_l_ascension()
    {
        // Vérifié dans le source : ni IntangiblePower ni HardToKillPower n'utilisent
        // AscensionHelper ; la signature garde l'ascension par symétrie avec MaxHit.
        for (var ascension = 0; ascension <= 20; ascension += 5)
        {
            Assert.Equal(1, Cap(Model, "SOUL_FYSH_BOSS", ascension));
            Assert.Equal(9, Cap(Model, "EXOSKELETONS_NORMAL", ascension));
        }
    }

    [Fact]
    public void IsBurstIneffective_vrai_quand_le_plafond_est_un()
    {
        // Plafond 1 contre un coup attendu de 45 (Big Pounce de TestSubject) : 1 ≤ 25 % → burst
        // inutile. Le coup attendu par défaut est le pire coup de la rencontre.
        Assert.True(Model.IsBurstIneffective("TEST_SUBJECT_BOSS", 0));
        Assert.True(Model.IsBurstIneffective("SOUL_FYSH_BOSS", 0));

        // Surcharge explicite : le burst du deck (60) est encore plus clairement gaspillé.
        Assert.True(Model.IsBurstIneffective("TEST_SUBJECT_BOSS", 10, 60));
        Assert.True(Model.IsBurstIneffective("SOUL_FYSH_BOSS", 0, 40));

        // Borne exacte du seuil (25 %) : 4 × 0,25 = 1 → vrai ; 3 × 0,25 = 0,75 → faux.
        Assert.True(Model.IsBurstIneffective("SOUL_FYSH_BOSS", 0, 4));
        Assert.False(Model.IsBurstIneffective("SOUL_FYSH_BOSS", 0, 3));

        // Aucun coup attendu : rien à conclure, donc faux.
        Assert.False(Model.IsBurstIneffective("SOUL_FYSH_BOSS", 0, 0));
        Assert.False(Model.IsBurstIneffective("PAS_UNE_RENCONTRE", 0));
    }

    [Fact]
    public void IsBurstIneffective_faux_pour_un_boss_sans_plafond()
    {
        // Aeonglass : aucun pouvoir plafonneur, 26 dégâts de pire coup.
        Assert.Null(Model.DamageCap("AEONGLASS_BOSS", 0));
        Assert.False(Model.IsBurstIneffective("AEONGLASS_BOSS", 0));
        Assert.False(Model.IsBurstIneffective("AEONGLASS_BOSS", 0, 5));

        // Exoskeleton plafonne à 9, mais 9 n'est pas ≤ 25 % de son propre pire coup (9) :
        // son plafond ne rend pas le burst inopérant, il le borne.
        Assert.False(Model.IsBurstIneffective("EXOSKELETONS_NORMAL", 0));
        Assert.False(Model.IsBurstIneffective("EXOSKELETONS_NORMAL", 0, 30));
    }

    [Fact]
    public void Un_plafonneur_isole_ne_suffit_pas_dans_une_rencontre_mixte()
    {
        // Rencontre synthétique : SoulFysh (plafond 1) + Aeonglass (aucun plafond). Le plafond
        // est bien exposé, mais le burst reste la bonne réponse contre le second monstre.
        var dir = NewTempDir();
        try
        {
            File.Copy(Path.Combine(CatalogDir, "monsters.json"), Path.Combine(dir, "monsters.json"));
            File.Copy(Path.Combine(CatalogDir, "constraints.json"),
                Path.Combine(dir, "constraints.json"));
            File.WriteAllText(Path.Combine(dir, "encounters.json"),
                """
                {"encounters":[{"id":"MIXTE_TEST","name":"Mixte","act":1,"room_type":"Boss",
                  "monsters":["SOUL_FYSH","AEONGLASS"]}]}
                """);

            var model = ThreatModel.LoadFrom(dir);
            Assert.Equal(1, Cap(model, "MIXTE_TEST"));
            Assert.False(model.IsBurstIneffective("MIXTE_TEST", 0));
            Assert.False(model.IsBurstIneffective("MIXTE_TEST", 0, 60));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Catalogue_absent_ne_leve_pas()
    {
        // Dossier vide : aucun fichier. Toutes les requêtes répondent null / 0 / false.
        var dir = NewTempDir();
        try
        {
            var model = ThreatModel.LoadFrom(dir);
            Assert.Equal(0, model.MonsterCount);
            Assert.Equal(0, model.MonsterConstraintCount);
            Assert.Null(model.DamageCap("SOUL_FYSH_BOSS", 0));
            Assert.Equal(0, model.Respawns("TEST_SUBJECT_BOSS"));
            Assert.False(model.IsBurstIneffective("TEST_SUBJECT_BOSS", 0));
            Assert.Null(model.Constraint("SOUL_FYSH_BOSS"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Constraints_absent_ou_corrompu_ne_casse_pas_le_modele()
    {
        var dir = NewTempDir();
        try
        {
            File.Copy(Path.Combine(CatalogDir, "monsters.json"), Path.Combine(dir, "monsters.json"));
            File.Copy(Path.Combine(CatalogDir, "encounters.json"),
                Path.Combine(dir, "encounters.json"));

            // 1) constraints.json absent : le modèle de menace reste utilisable.
            var without = ThreatModel.LoadFrom(dir);
            Assert.True(without.MonsterCount > 0);
            Assert.Equal(0, without.MonsterConstraintCount);
            Assert.Null(without.DamageCap("SOUL_FYSH_BOSS", 0));
            Assert.Equal(0, without.Respawns("TEST_SUBJECT_BOSS"));
            Assert.Equal(26, without.MaxHit("AEONGLASS_BOSS", 0));   // les dégâts marchent toujours

            // 2) constraints.json corrompu : ignoré, aucune exception.
            File.WriteAllText(Path.Combine(dir, "constraints.json"), "{ ceci n'est pas du json");
            var corrupt = ThreatModel.LoadFrom(dir);
            Assert.Equal(0, corrupt.MonsterConstraintCount);
            Assert.Null(corrupt.DamageCap("SOUL_FYSH_BOSS", 0));
            Assert.False(corrupt.IsBurstIneffective("SOUL_FYSH_BOSS", 0));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Le_json_est_petit_et_chaque_entree_est_sourcee()
    {
        var path = Path.Combine(CatalogDir, "constraints.json");
        Assert.True(File.Exists(path), path);
        Assert.True(new FileInfo(path).Length < 100 * 1024,
            $"constraints.json: {new FileInfo(path).Length} octets");

        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = doc.RootElement;
        Assert.Equal("tools/index_constraints.py", root.GetProperty("generated_by").GetString());

        foreach (var sectionName in new[] { "monsters", "encounters" })
        {
            var section = root.GetProperty(sectionName);
            Assert.True(section.EnumerateObject().Any(), sectionName);
            foreach (var entry in section.EnumerateObject())
            {
                var source = entry.Value.GetProperty("source").GetString();
                Assert.False(string.IsNullOrWhiteSpace(source),
                    $"{sectionName}.{entry.Name} sans source");
                Assert.Contains(".cs:", source);
            }
        }
    }

    [Fact]
    public void Les_contraintes_du_json_et_du_modele_concordent()
    {
        var path = Path.Combine(CatalogDir, "constraints.json");
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var monsters = doc.RootElement.GetProperty("monsters");

        foreach (var entry in monsters.EnumerateObject())
        {
            var constraint = Model.MonsterConstraint(entry.Name);
            Assert.NotNull(constraint);
            Assert.Equal(Int(entry.Value, "damage_cap"), constraint!.DamageCap);
            Assert.Equal(Int(entry.Value, "hard_to_kill"), constraint.HardToKill);
            Assert.Equal(Int(entry.Value, "respawns") ?? 0, constraint.Respawns);
            Assert.Equal(entry.Value.GetProperty("cap_scope").GetString(), constraint.CapScope);

            // Le monstre existe bien dans le catalogue de menace (pas d'entrée orpheline).
            Assert.NotNull(Model.Monster(entry.Name));
        }

        foreach (var entry in doc.RootElement.GetProperty("encounters").EnumerateObject())
        {
            var constraint = Model.Constraint(entry.Name);
            Assert.NotNull(constraint);
            Assert.Equal(Int(entry.Value, "respawns") ?? 0, constraint!.Respawns);

            // La rencontre est connue du catalogue et ses monstres sont tous indexés.
            var encounter = Model.Encounter(entry.Name);
            Assert.NotNull(encounter);
            Assert.Equal(encounter!.MonsterIds.Count, Model.MonstersOf(entry.Name).Count);
        }
    }

    private static int? Int(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;
}
