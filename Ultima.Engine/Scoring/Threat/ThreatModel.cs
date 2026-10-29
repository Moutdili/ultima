using System.Text.Json;

namespace Ultima.Engine.Scoring.Threat;

/// <summary>
/// Règles de menace mesurées sur les replays combat (docs/30), pas déduites de l'API.
/// </summary>
public static class ThreatRules
{
    /// <summary>Vrai à partir de A10 : deux boss dans l'acte final (compter deux rencontres).</summary>
    public static bool IsDoubleBoss(int ascension) => ascension >= DoubleBossFrom;

    /// <summary>
    /// Table des paliers (Entities/Ascension/AscensionLevel.cs, ordre de l'enum) :
    /// 1 SwarmingElites, 2 WearyTraveler, 3 Poverty, 4 TightBelt, 5 AscendersBane,
    /// 6 Inflation, 7 Scarcity, **8 ToughEnemies (PV)**, **9 DeadlyEnemies (dégâts)**,
    /// **10 DoubleBoss (deux boss de fin)**.
    /// Mesuré sur replays : PV à A8 = variante ascension, dégâts à A8 = normal.
    /// </summary>
    public const int AscensionHpFrom = 8;

    /// <summary>Les dégâts passent à la variante `ascension` à partir de A9 (pas A10).</summary>
    public const int AscensionDamageFrom = 9;

    /// <summary>À partir de A10, l'acte final comporte deux boss.</summary>
    public const int DoubleBossFrom = 10;

    /// <summary>
    /// Seuil d'inopérance du burst : un plafond ≤ 25 % du coup attendu rend un gros coup
    /// unique (ou une salve) largement gaspillé. Lu dans le catalogue
    /// (<c>burst_ineffective_ratio</c>) mais figé ici comme référence documentée.
    /// </summary>
    public const double BurstIneffectiveCapRatio = 0.25;
}

/// <summary>
/// Un move tel que le codex le publie : dégâts normaux ET ascensionnels, nombre de coups,
/// bloc, soin, pouvoirs appliqués. Aucune valeur n'est déduite : un move sans dégâts dans
/// le catalogue reste à 0 ici (voir <see cref="DamageAt"/>).
/// </summary>
public sealed record MoveThreat(
    string Id,
    string Name,
    string Intent,
    int? DamageNormal,
    int? DamageAscension,
    int HitCount,
    int? Block,
    int? Heal)
{
    /// <summary>
    /// Dégâts d'un coup à l'ascension demandée. Le codex ne distingue que « normal » et
    /// « ascension » ; les replays combat montrent que le jeu reste sur « normal » jusqu'à A9 et
    /// bascule à partir de A10 (voir <see cref="ThreatRules.AscensionDamageFrom"/>). Repli sur
    /// l'autre variante quand elle manque.
    /// </summary>
    public int DamageAt(int ascension) => ascension >= ThreatRules.AscensionDamageFrom
        ? DamageAscension ?? DamageNormal ?? 0
        : DamageNormal ?? DamageAscension ?? 0;

    /// <summary>Dégâts du move entier : <c>dégâts × hit_count</c> (un move multi-coups compte tous ses coups).</summary>
    public int HitAt(int ascension) => DamageAt(ascension) * Math.Max(1, HitCount);

    public bool IsAttack => DamageNormal is > 0 || DamageAscension is > 0;
}

/// <summary>Monstre du catalogue de menace : PV, type, et ses moves un par un.</summary>
public sealed record MonsterThreat(
    string Id,
    string Name,
    string Type,
    string ClassName,
    int? HpNormal,
    int? HpAscension,
    IReadOnlyList<MoveThreat> Moves)
{
    /// <summary>Le plus gros coup que ce monstre peut porter en un tour, à l'ascension demandée.</summary>
    public int WorstHit(int ascension)
    {
        var worst = 0;
        foreach (var move in Moves)
        {
            var hit = move.HitAt(ascension);
            if (hit > worst) worst = hit;
        }
        return worst;
    }

    /// <summary>Move qui porte <see cref="WorstHit"/> (null si le monstre n'attaque pas).</summary>
    public MoveThreat? WorstMove(int ascension)
    {
        MoveThreat? best = null;
        var worst = 0;
        foreach (var move in Moves)
        {
            var hit = move.HitAt(ascension);
            if (hit <= worst) continue;
            worst = hit;
            best = move;
        }
        return best;
    }

    public int? HpAt(int ascension) => ascension >= ThreatRules.AscensionHpFrom
        ? HpAscension ?? HpNormal
        : HpNormal ?? HpAscension;
}

/// <summary>Statistiques de combat réellement observées pour une rencontre (aucune estimation).</summary>
public sealed record ObservedStats(
    long? Total,
    long? Fatal,
    long? Abandoned,
    double? AvgDamage,
    double? AvgTurns,
    IReadOnlyDictionary<string, ObservedStats> ByCharacter)
{
    public static ObservedStats Empty { get; } = new(null, null, null, null, null,
        new Dictionary<string, ObservedStats>(StringComparer.OrdinalIgnoreCase));
}

/// <summary>Rencontre : acte, type de salle, monstres qui la composent, stats observées si présentes.</summary>
public sealed record EncounterThreat(
    string Id,
    string Name,
    int? Act,
    string? ActName,
    string RoomType,
    bool IsWeak,
    IReadOnlyList<string> MonsterIds,
    ObservedStats? Stats);

/// <summary>Le pire coup d'une rencontre : quel monstre, quel move, combien de dégâts.</summary>
public sealed record WorstMoveThreat(string MonsterId, string MoveId, string MoveName, int Damage);

/// <summary>
/// Contrainte d'ennemi lue dans le code du jeu (docs/32 §4) et publiée par
/// <c>tools/index_constraints.py</c> : plafond de dégâts subis, respawns, pouvoirs qui
/// les portent.
/// <para>
/// <see cref="DamageCap"/> (Intangible : 1) et <see cref="HardToKill"/> (plafond = Amount)
/// sont deux mécaniques distinctes dans le source ; <see cref="Cap"/> donne le plafond
/// effectif, c'est-à-dire le plus contraignant des deux.
/// </para>
/// </summary>
/// <param name="CapScope">
/// Quand le plafond s'applique : <c>always</c> (dès l'entrée en salle), <c>periodic</c>
/// (un move de la rotation, ex. Fade de SoulFysh), <c>late_phase</c> (après un respawn,
/// ex. Nemesis de TestSubject). Chaîne vide si aucun plafond.
/// </param>
public sealed record ThreatConstraint(
    string Id,
    int? DamageCap,
    int? HardToKill,
    int Respawns,
    string CapScope,
    string? CapPower,
    IReadOnlyList<string> Powers,
    string? Source)
{
    /// <summary>Plafond effectif : minimum de <see cref="DamageCap"/> et <see cref="HardToKill"/>.</summary>
    public int? Cap
    {
        get
        {
            if (DamageCap is null) return HardToKill;
            if (HardToKill is null) return DamageCap;
            return Math.Min(DamageCap.Value, HardToKill.Value);
        }
    }

    /// <summary>Plafond actif dès l'entrée en salle (et non seulement sur un move).</summary>
    public bool IsPermanent => string.Equals(CapScope, "always", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Modèle de menace : dégâts entrants par rencontre et par ascension, PV effectifs.
/// <para>
/// Charge <c>data/catalog/monsters.json</c> et <c>data/catalog/encounters.json</c>
/// (générés par <c>tools/index_threats.py</c> depuis <c>data/codex/</c>). Le codex donne les
/// moves <b>un par un</b>, jamais leur rotation : ce modèle expose donc « pire coup en un
/// tour » et « dégâts moyens observés », <b>jamais</b> une prédiction tour par tour.
/// </para>
/// <para>
/// Fichier absent ou illisible ⇒ <see cref="Empty"/> : toutes les requêtes répondent 0 /
/// null / liste vide, aucune exception et aucun chiffre inventé.
/// </para>
/// </summary>
public sealed class ThreatModel
{
    private readonly Dictionary<string, MonsterThreat> _monsters;
    private readonly Dictionary<string, EncounterThreat> _encounters;
    private readonly Dictionary<string, ThreatConstraint> _constraints;
    private readonly Dictionary<string, ThreatConstraint> _monsterConstraints;

    private ThreatModel(Dictionary<string, MonsterThreat> monsters,
                        Dictionary<string, EncounterThreat> encounters,
                        Dictionary<string, ThreatConstraint> constraints,
                        Dictionary<string, ThreatConstraint> monsterConstraints)
    {
        _monsters = monsters;
        _encounters = encounters;
        _constraints = constraints;
        _monsterConstraints = monsterConstraints;
    }

    /// <summary>Nombre de monstres indexés.</summary>
    public int MonsterCount => _monsters.Count;

    /// <summary>Nombre de rencontres indexées.</summary>
    public int EncounterCount => _encounters.Count;

    /// <summary>Nombre de rencontres qui portent une contrainte (plafond ou respawn).</summary>
    public int ConstraintCount => _constraints.Count;

    /// <summary>Nombre de monstres qui portent une contrainte (plafond ou respawn).</summary>
    public int MonsterConstraintCount => _monsterConstraints.Count;

    /// <summary>Toutes les rencontres du catalogue (lecture seule, ordre d'insertion).</summary>
    public IReadOnlyCollection<EncounterThreat> Encounters => _encounters.Values;

    /// <summary>Modèle vide (aucun fichier chargé) — jamais null, jamais d'exception.</summary>
    public static ThreatModel Empty { get; } = new(
        new Dictionary<string, MonsterThreat>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, EncounterThreat>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, ThreatConstraint>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, ThreatConstraint>(StringComparer.OrdinalIgnoreCase));

    private static readonly Lazy<ThreatModel> LazyDefault = new(
        () => LoadFrom(null), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Modèle du dépôt (lazy) : <c>data/catalog</c> résolu comme les autres catalogues.</summary>
    public static ThreatModel Default => LazyDefault.Value;

    /// <summary>Charge le modèle depuis <paramref name="catalogDir"/> (défaut : data/catalog résolu).</summary>
    public static ThreatModel LoadFrom(string? catalogDir = null)
    {
        string dir;
        try
        {
            dir = ScoringData.ResolveCatalogDir(catalogDir);
        }
        catch (IOException)
        {
            return Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return Empty;
        }

        var monsters = LoadMonsters(Path.Combine(dir, "monsters.json"));
        var encounters = LoadEncounters(Path.Combine(dir, "encounters.json"));
        var constraintsPath = Path.Combine(dir, "constraints.json");
        var monsterConstraints = LoadConstraints(constraintsPath, "monsters");
        var constraints = LoadConstraints(constraintsPath, "encounters");
        if (monsters.Count == 0 && encounters.Count == 0) return Empty;
        return new ThreatModel(monsters, encounters, constraints, monsterConstraints);
    }

    /// <summary>
    /// Pire tour de la rencontre : somme, sur ses monstres, du move le plus fort de chacun
    /// (<c>dégâts × hit_count</c>, variante ascension si <paramref name="ascension"/> &gt; 0).
    /// Rencontre inconnue ⇒ 0. Ce n'est pas une rotation : c'est le maximum simultané.
    /// </summary>
    public int MaxHit(string encounterId, int ascension)
    {
        var encounter = Encounter(encounterId);
        if (encounter is null) return 0;

        var total = 0;
        foreach (var monsterId in encounter.MonsterIds)
        {
            if (!_monsters.TryGetValue(monsterId, out var monster)) continue;
            total += monster.WorstHit(ascension);
        }
        return total;
    }

    /// <summary>
    /// Move le plus fort de la rencontre (monstre + move + dégâts) ; null si aucun monstre
    /// n'attaque ou si la rencontre est inconnue.
    /// </summary>
    public WorstMoveThreat? WorstMove(string encounterId, int ascension)
    {
        var encounter = Encounter(encounterId);
        if (encounter is null) return null;

        WorstMoveThreat? worst = null;
        foreach (var monsterId in encounter.MonsterIds)
        {
            if (!_monsters.TryGetValue(monsterId, out var monster)) continue;
            var move = monster.WorstMove(ascension);
            if (move is null) continue;
            var damage = move.HitAt(ascension);
            if (worst is not null && damage <= worst.Damage) continue;
            worst = new WorstMoveThreat(monster.Id, move.Id, move.Name, damage);
        }
        return worst;
    }

    /// <summary>
    /// Dégâts moyens réellement observés sur la rencontre (<c>avg_damage</c> des stats).
    /// <paramref name="character"/> null/vide ⇒ moyenne tous personnages. Rencontre ou
    /// personnage absent de l'échantillon ⇒ null (jamais une moyenne inventée).
    /// </summary>
    public double? AverageDamage(string encounterId, string? character)
    {
        var stats = Encounter(encounterId)?.Stats;
        if (stats is null) return null;

        var key = CharacterKey(character);
        if (key.Length == 0) return stats.AvgDamage;

        return stats.ByCharacter.TryGetValue(key, out var perCharacter) ? perCharacter.AvgDamage : null;
    }

    /// <summary>Nombre de tours moyen observé (null si la rencontre n'est pas dans l'échantillon).</summary>
    public double? AverageTurns(string encounterId, string? character = null)
    {
        var stats = Encounter(encounterId)?.Stats;
        if (stats is null) return null;

        var key = CharacterKey(character);
        if (key.Length == 0) return stats.AvgTurns;
        return stats.ByCharacter.TryGetValue(key, out var perCharacter) ? perCharacter.AvgTurns : null;
    }

    /// <summary>
    /// Monstres de la rencontre, dans l'ordre de ses entrées ; liste vide si la rencontre est
    /// inconnue. Un monstre absent du catalogue est omis (aucune entrée inventée).
    /// </summary>
    public IReadOnlyList<MonsterThreat> MonstersOf(string? encounterId)
    {
        var encounter = Encounter(encounterId);
        if (encounter is null) return Array.Empty<MonsterThreat>();

        var result = new List<MonsterThreat>(encounter.MonsterIds.Count);
        foreach (var monsterId in encounter.MonsterIds)
            if (_monsters.TryGetValue(monsterId, out var monster))
                result.Add(monster);
        return result;
    }

    /// <summary>Rencontre par id (<c>ENCOUNTER.X</c> accepté) ; null si inconnue.</summary>
    public EncounterThreat? Encounter(string? encounterId)
    {
        var key = NormalizeId(encounterId);
        if (key.Length == 0) return null;
        return _encounters.TryGetValue(key, out var encounter) ? encounter : null;
    }

    /// <summary>
    /// Contrainte agrégée d'une rencontre (source : <c>constraints.json</c>), null si la
    /// rencontre est inconnue ou ne porte ni plafond ni respawn.
    /// </summary>
    public ThreatConstraint? Constraint(string? encounterId)
    {
        var key = NormalizeId(encounterId);
        if (key.Length == 0) return null;
        return _constraints.TryGetValue(key, out var constraint) ? constraint : null;
    }

    /// <summary>Contrainte d'un monstre (<c>SOUL_FYSH</c> ou <c>SoulFysh</c>) ; null sinon.</summary>
    public ThreatConstraint? MonsterConstraint(string? monsterId)
    {
        var key = NormalizeId(monsterId);
        if (key.Length == 0) return null;
        return _monsterConstraints.TryGetValue(key, out var constraint) ? constraint : null;
    }

    /// <summary>
    /// Plafond de dégâts subis de la rencontre, à l'ascension demandée ; null s'il n'y en a
    /// pas ou si la rencontre est inconnue.
    /// <para>
    /// Un plafond est porté par une <b>créature</b> (<c>ModifyDamageCap</c> ne répond que si
    /// <c>target == Owner</c>) : la valeur renvoyée est donc le plafond le plus contraignant
    /// de ses monstres. Elle ne vaut que pour le monstre qui le porte — voir
    /// <see cref="ThreatConstraint"/> et <c>cap_monsters</c> dans le catalogue.
    /// </para>
    /// <para>
    /// Le paramètre <paramref name="ascension"/> est conservé par symétrie avec
    /// <see cref="MaxHit"/> : les deux pouvoirs plafonneurs du source (Intangible = 1,
    /// HardToKill = Amount) n'ont <b>aucune</b> variante d'ascension, donc la valeur ne
    /// change pas avec elle.
    /// </para>
    /// </summary>
    public int? DamageCap(string encounterId, int ascension)
    {
        _ = ascension; // vérifié dans le source : aucun AscensionHelper dans les deux pouvoirs
        var encounter = Encounter(encounterId);
        if (encounter is null) return null;

        int? tightest = null;
        foreach (var monsterId in encounter.MonsterIds)
        {
            if (!_monsterConstraints.TryGetValue(NormalizeId(monsterId), out var constraint)) continue;
            if (constraint.Cap is not { } cap) continue;
            tightest = tightest is null ? cap : Math.Min(tightest.Value, cap);
        }

        // Repli sur l'entrée agrégée si les monstres ne sont pas détaillés dans le catalogue.
        if (tightest is null && Constraint(encounterId) is { } direct) return direct.Cap;
        return tightest;
    }

    /// <summary>
    /// Nombre de fois où un monstre de la rencontre revient après sa mort (0 si aucun ou si
    /// la rencontre est inconnue). TestSubject : 2 (il meurt donc trois fois).
    /// </summary>
    public int Respawns(string encounterId)
    {
        var encounter = Encounter(encounterId);
        if (encounter is null) return 0;

        var respawns = 0;
        foreach (var monsterId in encounter.MonsterIds)
            if (_monsterConstraints.TryGetValue(NormalizeId(monsterId), out var constraint))
                respawns = Math.Max(respawns, constraint.Respawns);

        if (respawns == 0 && Constraint(encounterId) is { } direct) respawns = direct.Respawns;
        return respawns;
    }

    /// <summary>
    /// Le burst est-il inopérant contre cette rencontre ? Vrai quand <b>tous</b> ses monstres
    /// plafonnent leurs dégâts subis et que ce plafond est ≤ 25 % du coup attendu.
    /// <para>
    /// Un plafonneur isolé dans une rencontre mixte ne suffit pas : le burst reste la bonne
    /// réponse contre les autres monstres. Le coup attendu est ici le pire coup de la
    /// rencontre (<see cref="MaxHit"/>) faute de profil de deck dans la signature ; les
    /// appelants qui connaissent la puissance du joueur utilisent la surcharge explicite.
    /// </para>
    /// </summary>
    public bool IsBurstIneffective(string encounterId, int ascension)
        => IsBurstIneffective(encounterId, ascension, MaxHit(encounterId, ascension));

    /// <summary>
    /// Même règle avec un coup attendu explicite (le plus gros hit que le deck peut produire).
    /// <paramref name="expectedHit"/> ≤ 0 ou rencontre inconnue ⇒ false (rien à conclure).
    /// </summary>
    public bool IsBurstIneffective(string encounterId, int ascension, int expectedHit)
    {
        _ = ascension; // aucun plafond ascensionnel dans le source
        if (expectedHit <= 0) return false;

        var encounter = Encounter(encounterId);
        if (encounter is null || encounter.MonsterIds.Count == 0) return false;

        var threshold = expectedHit * ThreatRules.BurstIneffectiveCapRatio;
        foreach (var monsterId in encounter.MonsterIds)
        {
            if (!_monsterConstraints.TryGetValue(NormalizeId(monsterId), out var constraint))
                return false; // un monstre sans contrainte connue : le burst garde une cible
            if (constraint.Cap is not { } cap || cap > threshold) return false;
        }
        return true;
    }

    /// <summary>Monstre par id codex (<c>AEONGLASS</c>) ou nom de classe (<c>Aeonglass</c>) ; null sinon.</summary>
    public MonsterThreat? Monster(string? monsterId)
    {
        var key = NormalizeId(monsterId);
        if (key.Length == 0) return null;
        return _monsters.TryGetValue(key, out var monster) ? monster : null;
    }

    /// <summary>
    /// PV effectifs au début du combat : ce que valent réellement <paramref name="hp"/> PV
    /// face aux statuts du tour 1.
    /// <para>
    /// Formule : <c>PV effectifs = PV / (1,5 si Vulnérable × 0,75 si Faiblesse)</c>, bornés
    /// <c>0..maxHp</c>. Vulnérable augmente les dégâts subis de 50 %, Faiblesse les réduit de
    /// 25 % ; les piles sont des <b>durées</b>, pas des intensités — 2 Vulnérable valent ×1,5,
    /// pas ×3 (donc 90 PV, 2 Vulnérable + 1 Faiblesse → 90 / 1,125 = 80).
    /// </para>
    /// </summary>
    public static int EffectiveHp(int hp, int maxHp, int vulnerable, int weak)
    {
        var ceiling = Math.Max(0, maxHp);
        if (hp <= 0) return 0;

        double multiplier = 1.0;
        if (vulnerable > 0) multiplier *= 1.5;
        if (weak > 0) multiplier *= 0.75;

        var effective = (int)Math.Round(hp / multiplier, MidpointRounding.AwayFromZero);
        return Math.Clamp(effective, 0, ceiling);
    }

    private static string CharacterKey(string? character)
    {
        if (string.IsNullOrWhiteSpace(character)) return string.Empty;
        var key = character!.Trim().ToUpperInvariant();
        const string prefix = "CHARACTER.";
        return key.StartsWith(prefix, StringComparison.Ordinal) ? key[prefix.Length..] : key;
    }

    /// <summary>Clé de comparaison : majuscules alphanumériques (tolère <c>ENCOUNTER.</c> et les underscores).</summary>
    private static string NormalizeId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return string.Empty;

        const string prefix = "ENCOUNTER.";
        var text = id!.Trim();
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) text = text[prefix.Length..];

        var buffer = new char[text.Length];
        var length = 0;
        foreach (var c in text)
            if (char.IsLetterOrDigit(c))
                buffer[length++] = char.ToUpperInvariant(c);
        return new string(buffer, 0, length);
    }

    private static Dictionary<string, MonsterThreat> LoadMonsters(string path)
    {
        var map = new Dictionary<string, MonsterThreat>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return map;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return map;

            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;

                var className = Str(el, "class") ?? string.Empty;
                var id = Str(el, "id");
                // Entrée purement issue de la décompilation (mock, monstre de test, 12 cas) :
                // aucune valeur codex, donc rien à exposer au modèle de menace.
                if (string.IsNullOrWhiteSpace(id)) continue;

                var moves = new List<MoveThreat>();
                if (el.TryGetProperty("moves", out var movesEl) && movesEl.ValueKind == JsonValueKind.Array)
                    foreach (var moveEl in movesEl.EnumerateArray())
                        if (ParseMove(moveEl) is { } move)
                            moves.Add(move);

                var monster = new MonsterThreat(
                    id!.ToUpperInvariant(),
                    Str(el, "name") ?? className,
                    Str(el, "type") ?? "Unknown",
                    className,
                    Int(el, "hp_normal") ?? LegacyHp(el),
                    Int(el, "hp_ascension") ?? LegacyHp(el),
                    moves);

                map[monster.Id] = monster;
                if (className.Length > 0) map[className] = monster;
            }
        }
        catch (JsonException)
        {
        }
        catch (IOException)
        {
        }

        return map;
    }

    private static MoveThreat? ParseMove(JsonElement move)
    {
        var id = Str(move, "id") ?? Str(move, "state");
        var name = Str(move, "name");
        if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(name)) return null;

        int? normal = null, ascension = null;
        var hitCount = 1;

        if (move.TryGetProperty("damage", out var damage) && damage.ValueKind == JsonValueKind.Object)
        {
            normal = Int(damage, "normal");
            ascension = Int(damage, "ascension");
            hitCount = Int(damage, "hit_count") ?? 1;
        }

        if (hitCount <= 1) hitCount = LegacyRepeats(move) ?? hitCount;

        var block = Int(move, "block");
        var heal = Int(move, "heal");

        return new MoveThreat(
            (id ?? name)!.ToUpperInvariant(),
            name ?? id!,
            Str(move, "intent_codex") ?? Str(move, "intent") ?? string.Empty,
            normal,
            ascension,
            Math.Max(1, hitCount),
            block,
            heal);
    }

    /// <summary>PV de la forme historique (<c>min_hp</c> : nombre ou objet à <c>values</c>) — plus petit = normal.</summary>
    private static int? LegacyHp(JsonElement monster)
    {
        if (!monster.TryGetProperty("min_hp", out var hp)) return null;
        if (hp.ValueKind == JsonValueKind.Number) return Int(monster, "min_hp");

        if (hp.ValueKind != JsonValueKind.Object || !hp.TryGetProperty("values", out var values) ||
            values.ValueKind != JsonValueKind.Array) return null;

        int? best = null;
        foreach (var v in values.EnumerateArray())
        {
            if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out var n)) continue;
            if (best is null || n < best) best = n;
        }
        return best;
    }

    /// <summary>Répétitions de la forme historique (<c>values[1]</c>) quand le codex n'a pas de hit_count.</summary>
    private static int? LegacyRepeats(JsonElement move)
    {
        if (!move.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Array) return null;

        var index = 0;
        foreach (var item in values.EnumerateArray())
        {
            if (index++ != 1) continue;
            if (item.ValueKind != JsonValueKind.Array) return null;
            foreach (var v in item.EnumerateArray())
                if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) && n > 0) return n;
            return null;
        }
        return null;
    }

    private static Dictionary<string, EncounterThreat> LoadEncounters(string path)
    {
        var map = new Dictionary<string, EncounterThreat>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return map;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return map;
            if (!doc.RootElement.TryGetProperty("encounters", out var array) ||
                array.ValueKind != JsonValueKind.Array) return map;

            foreach (var el in array.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                var id = Str(el, "id");
                if (string.IsNullOrWhiteSpace(id)) continue;

                var monsterIds = new List<string>();
                if (el.TryGetProperty("monsters", out var monstersEl) && monstersEl.ValueKind == JsonValueKind.Array)
                    foreach (var m in monstersEl.EnumerateArray())
                        if (m.ValueKind == JsonValueKind.String && m.GetString() is { Length: > 0 } mid)
                            monsterIds.Add(mid);

                var encounter = new EncounterThreat(
                    id!,
                    Str(el, "name") ?? id!,
                    Int(el, "act"),
                    Str(el, "act_name"),
                    Str(el, "room_type") ?? "Unknown",
                    el.TryGetProperty("is_weak", out var weak) && weak.ValueKind == JsonValueKind.True,
                    monsterIds,
                    ParseStats(el));

                map[NormalizeId(encounter.Id)] = encounter;
            }
        }
        catch (JsonException)
        {
        }
        catch (IOException)
        {
        }

        return map;
    }

    /// <summary>
    /// Charge une section de <c>constraints.json</c> (<c>monsters</c> ou <c>encounters</c>).
    /// Fichier absent, corrompu ou de forme inattendue ⇒ dictionnaire vide : le modèle
    /// continue de fonctionner, aucune exception (les contraintes sont un bonus, pas une
    /// condition d'utilisation).
    /// </summary>
    private static Dictionary<string, ThreatConstraint> LoadConstraints(string path, string section)
    {
        var map = new Dictionary<string, ThreatConstraint>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return map;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return map;
            if (!doc.RootElement.TryGetProperty(section, out var sectionEl) ||
                sectionEl.ValueKind != JsonValueKind.Object) return map;

            foreach (var entry in sectionEl.EnumerateObject())
            {
                if (entry.Value.ValueKind != JsonValueKind.Object) continue;
                var id = Str(entry.Value, "id") ?? entry.Name;
                if (string.IsNullOrWhiteSpace(id)) continue;

                var powers = new List<string>();
                if (entry.Value.TryGetProperty("powers", out var powersEl) &&
                    powersEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in powersEl.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } pid)
                            powers.Add(pid);
                        else if (item.ValueKind == JsonValueKind.Object &&
                                 Str(item, "id") is { Length: > 0 } oid)
                            powers.Add(oid);
                    }
                }

                var constraint = new ThreatConstraint(
                    id!.ToUpperInvariant(),
                    Int(entry.Value, "damage_cap"),
                    Int(entry.Value, "hard_to_kill"),
                    Int(entry.Value, "respawns") ?? 0,
                    Str(entry.Value, "cap_scope") ?? string.Empty,
                    Str(entry.Value, "cap_power"),
                    powers,
                    Str(entry.Value, "source"));

                map[NormalizeId(constraint.Id)] = constraint;
            }
        }
        catch (JsonException)
        {
        }
        catch (IOException)
        {
        }

        return map;
    }

    private static ObservedStats? ParseStats(JsonElement encounter)
    {
        if (!encounter.TryGetProperty("stats", out var stats) || stats.ValueKind != JsonValueKind.Object)
            return null;

        var byCharacter = new Dictionary<string, ObservedStats>(StringComparer.OrdinalIgnoreCase);
        if (stats.TryGetProperty("characters", out var characters) && characters.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in characters.EnumerateObject())
            {
                if (entry.Value.ValueKind != JsonValueKind.Object) continue;
                byCharacter[CharacterKey(entry.Name)] = new ObservedStats(
                    Long(entry.Value, "total"),
                    Long(entry.Value, "fatal"),
                    null,
                    Double(entry.Value, "avg_damage"),
                    Double(entry.Value, "avg_turns"),
                    ObservedStats.Empty.ByCharacter);
            }
        }

        return new ObservedStats(
            Long(stats, "total"),
            Long(stats, "fatal"),
            Long(stats, "abandoned"),
            Double(stats, "avg_damage"),
            Double(stats, "avg_turns"),
            byCharacter);
    }

    private static string? Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? Int(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number)
        {
            if (v.TryGetInt32(out var i)) return i;
            return v.TryGetDouble(out var d) ? (int)Math.Round(d) : null;
        }
        return null;
    }

    private static long? Long(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Number) return null;
        if (v.TryGetInt64(out var l)) return l;
        return v.TryGetDouble(out var d) ? (long)Math.Round(d) : null;
    }

    private static double? Double(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Number) return null;
        return v.TryGetDouble(out var d) ? d : null;
    }
}
