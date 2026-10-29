using System.Globalization;
using System.Text.Json;
using Ultima.Engine.Scoring.Deck;
using Ultima.Engine.Scoring.Model;

namespace Ultima.Engine.Scoring.Strength;

/// <summary>
/// Poids par acte du score global (docs/27 §7bis). La somme vaut 1 : <see cref="Apply"/> reste donc
/// sur l'échelle 0..100 des autres notes, sans renormalisation cachée.
/// </summary>
public sealed record ActWeights(double Survie, double Offense, double Consistance, double Couverture)
{
    /// <summary>Somme des quatre poids (doit valoir 1).</summary>
    public double Sum => Survie + Offense + Consistance + Couverture;

    /// <summary>Les quatre poids sont finis et dans [0, 1].</summary>
    public bool IsInRange =>
        double.IsFinite(Survie) && double.IsFinite(Offense) &&
        double.IsFinite(Consistance) && double.IsFinite(Couverture) &&
        Survie is >= 0 and <= 1 && Offense is >= 0 and <= 1 &&
        Consistance is >= 0 and <= 1 && Couverture is >= 0 and <= 1;

    /// <summary>
    /// Poids ramenés à une somme exactement égale à 1 (division par la somme). Sert à absorber
    /// l'arrondi à 4 décimales du fichier de calibration (0,8180 + 0 + 0,1798 + 0,0021 = 0,9999) :
    /// sans cela, un total pourrait valoir 99,99 au lieu de 100 sur l'échelle des dimensions.
    /// </summary>
    public ActWeights Normalized()
    {
        var sum = Sum;
        if (!double.IsFinite(sum) || Math.Abs(sum - 1.0) < 1e-12 || sum <= 0) return this;
        return new ActWeights(Survie / sum, Offense / sum, Consistance / sum, Couverture / sum);
    }

    /// <summary>Total 0..100 = combinaison linéaire bornée des quatre dimensions.</summary>
    public int Apply(int survie, int offense, int consistance, int couverture) => ScoreMath.Clamp(
        ScoreMath.Round(Survie * survie + Offense * offense + Consistance * consistance + Couverture * couverture),
        0, 100);
}

/// <summary>
/// Poids de G effectivement utilisés, avec leur <b>provenance</b> : calibrés sur le corpus de runs
/// (<c>data/calibration/weights.json</c>) ou constantes écrites à la main.
/// <para>
/// Un objet non calibré n'est jamais une erreur : c'est le repli documenté quand le fichier est
/// absent, illisible, d'un autre schéma ou incohérent (poids hors [0, 1], somme ≠ 1, métadonnées
/// manquantes). <see cref="Problem"/> dit pourquoi, <see cref="Source"/> reste lisible par l'UI.
/// </para>
/// </summary>
public sealed class WeightCalibration
{
    private readonly ActWeights[] _acts;

    private WeightCalibration(
        ActWeights[] acts, bool isCalibrated, int runs, string? date,
        string provenance, string? problem, string? path)
    {
        _acts = acts;
        IsCalibrated = isCalibrated;
        Runs = runs;
        Date = date;
        Provenance = provenance;
        Problem = problem;
        Path = path;
    }

    /// <summary>Constantes écrites à la main (docs/27 §7bis) — repli de référence.</summary>
    public static WeightCalibration Defaults { get; } = new(
        new[] { StrengthWeights.DefaultAct1, StrengthWeights.DefaultAct2, StrengthWeights.DefaultAct3 },
        false, 0, null, "poids par défaut (constantes écrites à la main)", null, null);

    /// <summary>Poids par acte, index 0 = acte 1. Toujours trois entrées.</summary>
    public IReadOnlyList<ActWeights> Acts => _acts;

    /// <summary>true seulement si le fichier de calibration a été lu, validé et appliqué.</summary>
    public bool IsCalibrated { get; }

    /// <summary>Nombre de runs du fit (<c>source.runs_final</c>), 0 si non calibré.</summary>
    public int Runs { get; }

    /// <summary>Date du fit (<c>generated_at</c>, AAAA-MM-JJ), null si non calibré.</summary>
    public string? Date { get; }

    /// <summary>
    /// Libellé court de la provenance : « calibré (n=71 634 runs, 2026-10-29) » ou
    /// « constantes par défaut ».
    /// </summary>
    public string Source => IsCalibrated
        ? $"calibré (n={Group(Runs)} runs, {Date})"
        : "constantes par défaut";

    /// <summary>
    /// Phrase utilisée dans l'explication, devant la valeur du poids :
    /// « poids calibrés sur 71 634 runs (2026-10-29) » ou
    /// « poids par défaut (constantes écrites à la main) ».
    /// </summary>
    public string Provenance { get; }

    /// <summary>Raison du repli sur les constantes (null quand le fichier est appliqué).</summary>
    public string? Problem { get; }

    /// <summary>Fichier lu (ou tenté), null si le chemin n'a pas pu être résolu.</summary>
    public string? Path { get; }

    /// <summary>Poids de l'acte (0-based comme <see cref="ScoringContext.ActIndex"/>) ; au-delà de l'acte 3, acte 3.</summary>
    public ActWeights ForActIndex(int actIndex) => _acts[Math.Clamp(actIndex, 0, _acts.Length - 1)];

    /// <summary>
    /// Poids explicites (tests, simulations) : la provenance est fournie par l'appelant et le repli
    /// sur les constantes n'a pas lieu — l'appelant sait ce qu'il injecte.
    /// </summary>
    public static WeightCalibration Injected(IReadOnlyList<ActWeights> acts, string provenance = "poids injectés")
    {
        ArgumentNullException.ThrowIfNull(acts);
        if (acts.Count == 0) throw new ArgumentException("au moins un acte est requis", nameof(acts));

        var copy = new ActWeights[acts.Count];
        for (var i = 0; i < copy.Length; i++) copy[i] = acts[i];
        return new WeightCalibration(copy, false, 0, null, provenance, null, null);
    }

    /// <summary>Repli interne : constantes + raison, sans exception.</summary>
    internal static WeightCalibration Fallback(string? path, string problem) => new(
        new[] { StrengthWeights.DefaultAct1, StrengthWeights.DefaultAct2, StrengthWeights.DefaultAct3 },
        false, 0, null, "poids par défaut (constantes écrites à la main)", problem, path);

    /// <summary>Calibration validée (usage interne du chargeur).</summary>
    internal static WeightCalibration Calibrated(ActWeights[] acts, int runs, string date, string? path) => new(
        acts, true, runs, date, $"poids calibrés sur {Group(runs)} runs ({date})", null, path);

    private static string Group(int value) =>
        value.ToString("N0", CultureInfo.InvariantCulture).Replace(',', ' ');
}

/// <summary>
/// Poids de G par acte. Deux couches, dans cet ordre :
/// <list type="number">
/// <item><b>constantes écrites à la main</b> (<see cref="DefaultAct1"/>, <see cref="DefaultAct2"/>,
/// <see cref="DefaultAct3"/>) — le repli, toujours disponible, jamais une exception ;</item>
/// <item><b>poids calibrés</b> lus dans <c>data/calibration/weights.json</c> (schéma
/// <see cref="Schema"/>, produit par <c>tools/calibration/fit_weights.py</c>) — utilisés quand le
/// fichier est présent, lisible et cohérent.</item>
/// </list>
/// Le fichier ne propose rien pour l'acte 3 (aucun run ne survit à l'acte 3 sans gagner, donc les
/// poids n'y sont pas identifiables) : l'acte 3 garde les constantes. Act-index 0-based comme
/// <see cref="ScoringContext.ActIndex"/>.
/// <para>
/// Le chargement est fait <b>une seule fois</b> par processus (<see cref="Current"/>), en
/// <c>LazyThreadSafetyMode.ExecutionAndPublication</c>. <see cref="Load"/> et <see cref="Parse"/>
/// restent purs (ils ne touchent pas au cache) pour que les tests puissent valider un fichier
/// arbitraire sans polluer l'état global.
/// </para>
/// </summary>
public static class StrengthWeights
{
    /// <summary>Schéma attendu du fichier de calibration.</summary>
    public const string Schema = "ultima.calibration.weights/1";

    /// <summary>
    /// Tolérance sur la somme des poids. Le fit écrit quatre parts arrondies à 4 décimales : la
    /// somme peut valoir 0,9999 ; au-delà de 1e-3 d'écart, le fichier est considéré incohérent.
    /// </summary>
    public const double SumTolerance = 1e-3;

    /// <summary>Acte 1 : on survit avant de briller (les boss frappent 14..30, le deck est pauvre).</summary>
    public static readonly ActWeights DefaultAct1 = new(0.40, 0.30, 0.20, 0.10);

    /// <summary>Acte 2 : l'offense devient aussi importante que la survie (boss 321..408 PV).</summary>
    public static readonly ActWeights DefaultAct2 = new(0.30, 0.35, 0.20, 0.15);

    /// <summary>Acte 3 : il faut tuer (512+ PV), la couverture passe devant la consistance.</summary>
    public static readonly ActWeights DefaultAct3 = new(0.30, 0.40, 0.15, 0.15);

    private static readonly Lazy<WeightCalibration> Lazy = new(
        () => Load(), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Calibration effective du processus (lue une fois, jamais d'exception).</summary>
    public static WeightCalibration Current => Lazy.Value;

    /// <summary>« calibré (n=71 634 runs, 2026-10-29) » ou « constantes par défaut ».</summary>
    public static string Source => Current.Source;

    /// <summary>true si les poids calibrés sont appliqués (le fichier a été lu et validé).</summary>
    public static bool IsCalibrated => Current.IsCalibrated;

    /// <summary>Poids effectifs de l'acte 1 (calibrés si disponibles).</summary>
    public static ActWeights Act1 => Current.ForActIndex(0);

    /// <summary>Poids effectifs de l'acte 2 (calibrés si disponibles).</summary>
    public static ActWeights Act2 => Current.ForActIndex(1);

    /// <summary>Poids effectifs de l'acte 3 (constantes : non identifiable sur le corpus).</summary>
    public static ActWeights Act3 => Current.ForActIndex(2);

    /// <summary>Poids effectifs de l'acte ; au-delà de l'acte 3 on garde les poids de l'acte 3.</summary>
    public static ActWeights ForActIndex(int actIndex) => Current.ForActIndex(actIndex);

    /// <summary>
    /// Fichier de calibration attendu, ou null si introuvable. Priorité : <c>ULTIMA_DATA</c>,
    /// voisin de <c>data/catalog</c>, puis remontée depuis l'assembly.
    /// </summary>
    public static string? ResolvePath() => DataPaths.CalibrationFile();

    /// <summary>
    /// Lit et valide le fichier de calibration. <b>Ne met pas le cache à jour</b> et ne lève jamais :
    /// toute erreur (fichier absent, JSON invalide, schéma, poids hors [0, 1], somme ≠ 1,
    /// métadonnées manquantes) rend les <b>constantes par défaut</b> avec la raison dans
    /// <see cref="WeightCalibration.Problem"/>.
    /// </summary>
    public static WeightCalibration Load(string? path = null)
    {
        var file = string.IsNullOrWhiteSpace(path) ? ResolvePath() : path;
        if (string.IsNullOrWhiteSpace(file))
            return WeightCalibration.Fallback(null, "fichier de calibration introuvable (data/calibration/weights.json)");

        string text;
        try
        {
            if (!File.Exists(file))
                return WeightCalibration.Fallback(file, "fichier absent");
            text = File.ReadAllText(file);
        }
        catch (Exception ex)
        {
            // Repli sûr : un fichier illisible ne doit jamais empêcher de scorer.
            return WeightCalibration.Fallback(file, $"fichier illisible ({ex.GetType().Name})");
        }

        return Parse(text, file);
    }

    /// <summary>
    /// Valide un contenu JSON de calibration (même contrat que <see cref="Load"/> : jamais
    /// d'exception, repli sur les constantes). Utile aux tests et aux outils.
    /// </summary>
    public static WeightCalibration Parse(string? json, string? path = null)
    {
        if (string.IsNullOrWhiteSpace(json))
            return WeightCalibration.Fallback(path, "contenu vide");

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });

            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return WeightCalibration.Fallback(path, "racine JSON : objet attendu");

            if (!root.TryGetProperty("schema", out var schema) || schema.ValueKind != JsonValueKind.String ||
                !string.Equals(schema.GetString(), Schema, StringComparison.Ordinal))
                return WeightCalibration.Fallback(path, $"schéma absent ou différent de « {Schema} »");

            if (!root.TryGetProperty("proposed_weights", out var proposed) ||
                proposed.ValueKind != JsonValueKind.Object)
                return WeightCalibration.Fallback(path, "proposed_weights absent");

            // L'acte 1 est le seul que les deux estimateurs identifient : sans lui, le fichier
            // n'est pas une calibration utilisable (un acte 2 seul serait un fichier tronqué).
            if (!IsNotIdentifiable(root, 1) && !proposed.TryGetProperty("act_1", out _))
                return WeightCalibration.Fallback(path, "proposed_weights.act_1 absent");

            var acts = new[] { DefaultAct1, DefaultAct2, DefaultAct3 };
            var calibratedActs = 0;

            for (var act = 1; act <= 3; act++)
            {
                // Un acte déclaré non identifiable (act_3 aujourd'hui : aucun run n'y survit sans
                // gagner) n'est jamais appliqué, même si le fit en proposait un.
                if (IsNotIdentifiable(root, act)) continue;
                if (!proposed.TryGetProperty($"act_{act}", out var node)) continue;

                if (node.ValueKind != JsonValueKind.Object ||
                    !node.TryGetProperty("weights", out var weightsNode) || weightsNode.ValueKind != JsonValueKind.Object)
                    return WeightCalibration.Fallback(path, $"act_{act}.weights absent ou invalide");

                if (!TryRead(weightsNode, out var read, out var problem))
                    return WeightCalibration.Fallback(path, $"acte {act} : {problem}");

                acts[act - 1] = read;
                calibratedActs++;
            }

            if (calibratedActs == 0)
                return WeightCalibration.Fallback(path, "aucun poids par acte proposé");

            if (!TryReadMetadata(root, out var runs, out var date, out var metadataProblem))
                return WeightCalibration.Fallback(path, metadataProblem);

            return WeightCalibration.Calibrated(acts, runs, date, path);
        }
        catch (Exception ex)
        {
            // JSON invalide, type inattendu, profondeur excessive : repli, jamais d'exception.
            return WeightCalibration.Fallback(path, $"JSON invalide ({ex.GetType().Name})");
        }
    }

    private static bool IsNotIdentifiable(JsonElement root, int act) =>
        root.TryGetProperty("not_identifiable", out var notIdentifiable) &&
        notIdentifiable.ValueKind == JsonValueKind.Object &&
        notIdentifiable.TryGetProperty($"act_{act}", out _);

    private static bool TryRead(JsonElement weights, out ActWeights result, out string problem)
    {
        result = DefaultAct1;
        if (!TryNumber(weights, "survie", out var survie) ||
            !TryNumber(weights, "offense", out var offense) ||
            !TryNumber(weights, "consistance", out var consistance) ||
            !TryNumber(weights, "couverture", out var couverture))
        {
            problem = "les quatre poids survie/offense/consistance/couverture doivent être numériques";
            return false;
        }

        var read = new ActWeights(survie, offense, consistance, couverture);
        if (!read.IsInRange)
        {
            problem = $"poids hors de [0, 1] ({F(survie)}, {F(offense)}, {F(consistance)}, {F(couverture)})";
            return false;
        }

        if (Math.Abs(read.Sum - 1.0) > SumTolerance)
        {
            problem = $"somme des poids {F(read.Sum)} ≠ 1 (tolérance {F(SumTolerance)})";
            return false;
        }

        // Arrondi du fit à 4 décimales : la somme vaut 0,9999, on la ramène à 1 exactement.
        result = read.Normalized();
        problem = string.Empty;
        return true;
    }

    private static bool TryReadMetadata(JsonElement root, out int runs, out string date, out string problem)
    {
        runs = 0;
        date = string.Empty;
        problem = string.Empty;

        if (!root.TryGetProperty("source", out var source) || source.ValueKind != JsonValueKind.Object ||
            !source.TryGetProperty("runs_final", out var runsNode) || !runsNode.TryGetInt32(out runs) || runs <= 0)
        {
            problem = "source.runs_final absent ou invalide";
            return false;
        }

        if (!root.TryGetProperty("generated_at", out var generated) || generated.ValueKind != JsonValueKind.String ||
            !DateTimeOffset.TryParse(generated.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var when))
        {
            problem = "generated_at absent ou invalide";
            return false;
        }

        date = when.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return true;
    }

    private static bool TryNumber(JsonElement obj, string name, out double value)
    {
        value = 0;
        if (!obj.TryGetProperty(name, out var node) || node.ValueKind != JsonValueKind.Number) return false;
        if (!node.TryGetDouble(out value)) return false;
        return double.IsFinite(value);
    }

    private static string Group(int value) =>
        value.ToString("N0", CultureInfo.InvariantCulture).Replace(',', ' ');

    private static string F(double value) => value.ToString("0.0000", CultureInfo.InvariantCulture);
}
