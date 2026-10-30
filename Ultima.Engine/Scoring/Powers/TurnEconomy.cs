namespace Ultima.Engine.Scoring.Powers;

/// <summary>Un terme du budget de tour : ce qu'un pouvoir change, ou pourquoi il ne peut pas être chiffré.</summary>
/// <param name="PowerId">Pouvoir (identifiant du catalogue).</param>
/// <param name="Hook">Hook du jeu qui porte l'effet (<c>ModifyHandDraw</c>, <c>ModifyMaxEnergy</c>…).</param>
/// <param name="Value">Valeur calculée par l'évaluateur, ou <c>null</c> si l'expression n'est pas évaluable.</param>
/// <param name="Stacks">Piles du pouvoir (l'<c>Amount</c> du code du jeu).</param>
/// <param name="Resolution"><c>code</c> (expression extraite du code) ou <c>unresolved</c> (aucune valeur).</param>
/// <param name="Source">« fichier:ligne » du hook, ou raison de non-résolution.</param>
public sealed record TurnTerm(string PowerId, string Hook, double? Value, int Stacks, string Resolution, string? Source);

/// <summary>
/// Budget d'un tour avec les pouvoirs comportementaux du catalogue : pioche, énergie, cartes
/// jouables, et multiplicateurs de statuts (Vulnerable, Weak).
/// </summary>
/// <remarks>
/// <para>
/// <b>Pourquoi une couche d'événements et pas des formules par pouvoir.</b> Sur les 260 pouvoirs du
/// catalogue, 11 hooks seulement touchent l'économie du tour — et la plupart portent une expression
/// extraite du code (<c>In + Amount</c>, <c>In + 1</c>, <c>In - Amount</c>…). Un seul évaluateur
/// (<see cref="ExpressionEval"/>) les traite donc « d'un bloc ».
/// </para>
/// <para>
/// <b>Rien n'est inventé.</b> Un pouvoir dont l'expression manque (<c>mind_rot</c> par exemple)
/// produit un <see cref="TurnTerm"/> à <c>Value = null</c> avec sa raison : il ne contribue pas au
/// budget, et le budget ne prétend pas l'avoir pris en compte.
/// </para>
/// </remarks>
public sealed class TurnEconomy
{
    /// <summary>Pioche de base d'un tour, sans pouvoir.</summary>
    public const int BaseDraw = 5;

    /// <summary>Énergie de base d'un tour, sans pouvoir.</summary>
    public const int BaseEnergy = 3;

    /// <summary>Cartes jouables par tour, sans pouvoir.</summary>
    public const int BasePlays = 1;

    /// <summary>Multiplicateur de dégâts subis d'une cible Vulnerable (valeur du jeu).</summary>
    public const double BaseVulnerable = 1.5;

    /// <summary>Multiplicateur de dégâts infligés par un attaquant Weak (valeur du jeu).</summary>
    public const double BaseWeak = 0.75;

    private readonly PowerCatalog _catalog;

    public TurnEconomy(PowerCatalog? catalog = null) => _catalog = catalog ?? PowerCatalog.Default;

    public PowerCatalog Catalog => _catalog;

    /// <summary>
    /// Budget d'un tour pour un ensemble de pouvoirs actifs (<c>id → piles</c>). Les totaux sont
    /// bornés à zéro ; chaque terme dit d'où il vient ou pourquoi il n'a pas de valeur.
    /// </summary>
    public TurnBudget Compute(IReadOnlyDictionary<string, int>? powers)
    {
        powers ??= new Dictionary<string, int>();
        var terms = new List<TurnTerm>();

        int draw = Additive("ModifyHandDraw", BaseDraw, powers, terms);
        int energy = Additive("ModifyMaxEnergy", BaseEnergy, powers, terms);
        int plays = Additive("ModifyCardPlayCount", BasePlays, powers, terms);
        Additive("ModifyEnergyGain", 0, powers, terms); // information : gain d'énergie du tour

        double vulnerable = Multiplicative("ModifyVulnerableMultiplier", BaseVulnerable, powers, terms);
        double weak = Multiplicative("ModifyWeakMultiplier", BaseWeak, powers, terms);

        return new TurnBudget(
            Draw: Math.Max(0, draw),
            Energy: Math.Max(0, energy),
            CardPlays: Math.Max(0, plays),
            VulnerableMultiplier: vulnerable,
            WeakMultiplier: weak,
            Terms: terms);
    }

    /// <summary>Applique la chaîne des hooks additifs d'un nom donné, dans l'ordre du catalogue.</summary>
    private int Additive(string hook, int start, IReadOnlyDictionary<string, int> powers, List<TurnTerm> terms)
    {
        double current = start;
        foreach (var (powerId, expression, stacks, source) in HookEntries(hook, powers))
        {
            var value = ExpressionEval.EvalDouble(expression, stacks, current);
            if (value is null)
            {
                terms.Add(new TurnTerm(powerId, hook, null, stacks, "unresolved", source));
                continue;
            }

            terms.Add(new TurnTerm(powerId, hook, value.Value, stacks, "code", source));
            current = value.Value;
        }

        return (int)Math.Round(current, MidpointRounding.AwayFromZero);
    }

    /// <summary>Applique la chaîne des hooks multiplicatifs d'un nom donné.</summary>
    private double Multiplicative(string hook, double start, IReadOnlyDictionary<string, int> powers, List<TurnTerm> terms)
    {
        double current = start;
        foreach (var (powerId, expression, stacks, source) in HookEntries(hook, powers))
        {
            var value = ExpressionEval.EvalDouble(expression, stacks, current);
            if (value is null)
            {
                terms.Add(new TurnTerm(powerId, hook, null, stacks, "unresolved", source));
                continue;
            }

            terms.Add(new TurnTerm(powerId, hook, value.Value, stacks, "code", source));
            current = value.Value;
        }

        return current;
    }

    private IEnumerable<(string PowerId, string? Expression, int Stacks, string? Source)> HookEntries(
        string hook, IReadOnlyDictionary<string, int> powers)
    {
        foreach (var (powerId, stacks) in powers.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var entry = _catalog.Get(powerId);
            if (entry is null || !entry.Has(hook)) continue;
            yield return (powerId, entry.Expression(hook), stacks, entry.SourceOf(hook) ?? entry.Problem(hook));
        }
    }
}

/// <summary>Résultat chiffré d'un tour : ce qui se pioche, ce qui se joue, et les multiplicateurs.</summary>
public sealed record TurnBudget(
    int Draw,
    int Energy,
    int CardPlays,
    double VulnerableMultiplier,
    double WeakMultiplier,
    IReadOnlyList<TurnTerm> Terms)
{
    /// <summary>Pouvoirs présents dont au moins un terme n'a pas pu être chiffré.</summary>
    public IReadOnlyList<string> Unresolved =>
        Terms.Where(t => t.Value is null).Select(t => t.PowerId).Distinct(StringComparer.Ordinal).ToArray();
}
