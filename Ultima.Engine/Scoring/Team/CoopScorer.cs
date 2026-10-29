using System.Globalization;
using Ultima.Engine.Contracts;
using Ultima.Engine.Scoring.Cards;
using Ultima.Engine.Scoring.Strength;
using Ultima.Saves.Model;

namespace Ultima.Engine.Scoring.Team;

/// <summary>
/// Valeur d'une carte de soutien pour un groupe, joueur par joueur.
/// <para>
/// <see cref="PerAllyDelta"/> est indexé comme la liste d'entrée : l'index 0 est le porteur,
/// les suivants les alliés dans l'ordre fourni. <see cref="BestAllyIndex"/> désigne le joueur qui
/// gagne le plus (0 = le porteur garde la carte pour lui). Sur une liste vide, il vaut -1 :
/// il n'y a pas de « meilleur joueur » sans contexte.
/// </para>
/// </summary>
public sealed record CoopVerdict(
    string CardId,
    int BestAllyIndex,
    IReadOnlyList<int> PerAllyDelta,
    string Reason)
{
    /// <summary>ΔG du porteur (index 0), 0 si aucun contexte.</summary>
    public int CarrierDelta => PerAllyDelta.Count > 0 ? PerAllyDelta[0] : 0;

    /// <summary>ΔG du meilleur joueur, 0 si aucun contexte.</summary>
    public int BestDelta =>
        BestAllyIndex >= 0 && BestAllyIndex < PerAllyDelta.Count ? PerAllyDelta[BestAllyIndex] : 0;

    /// <summary>Ce que le groupe gagne de plus en cédant la carte plutôt qu'en la gardant.</summary>
    public int GainOverCarrier => PerAllyDelta.Count > 0 ? BestDelta - CarrierDelta : 0;

    /// <summary>Vrai quand le porteur est lui-même le meilleur destinataire.</summary>
    public bool CarrierIsBest => PerAllyDelta.Count > 0 && BestAllyIndex == 0;
}

/// <summary>
/// Scoring des cartes de SOUTIEN co-op (<c>target</c> = <c>AnyAlly</c> ou <c>AllAllies</c>,
/// 13 cartes mesurées dans <c>data/catalog/coop_cards.json</c>).
///
/// <para><b>Le trou que ce scorer bouche.</b> <see cref="PlayerStrengthCalculator.DeltaForCard"/>
/// mesure une carte sur le deck du joueur qui la reçoit. Une carte qui donne 11 de bloc « à un autre
/// joueur » n'ajoute rien au deck du porteur : ΔG ≈ 0 ou négatif, et le scoring la jette. Or sa
/// valeur dépend du deck d'en face — un allié sans bloc en profite beaucoup plus qu'un porteur dont
/// la survie est déjà saturée.</para>
///
/// <para><b>Mesure.</b> Pour chaque joueur <c>i</c> de la liste d'entrée, on calcule
/// <c>ΔG_i = G(deck_i + carte) − G(deck_i)</c> avec le <b>même</b> modèle que le scoring solo
/// (<see cref="PlayerStrengthCalculator.DeltaForCard"/>, docs/27 §7bis). Aucune formule parallèle :
/// une seule définition de G dans le dépôt. La carte est alors « cédée » au joueur qui en tire le
/// plus, si l'écart dépasse <see cref="CedingThreshold"/>.</para>
///
/// <para><b>Limites assumées (voir status/hermes-s23.md).</b>
/// 1. Le seuil <see cref="CedingThreshold"/> = 3 points de G est <b>explicite et non calibré</b> :
///    il sépare « l'allié en profite un peu » de « l'allié en profite nettement », il n'a pas été
///    ajusté sur des runs co-op. 2. Le ΔG de l'allié traite la carte comme si elle entrait dans
///    <b>son</b> deck : pour un soutien, l'effet réel est un don ponctuel (bloc, énergie, Force
///    au tour), pas une carte dans la pioche. C'est un proxy directionnel, pas une simulation.
///    3. Les cartes à montant calculé (Mimic, Demonic Shield) ou non chiffrées (Ignition,
///    Largesse) retombent sur leur valeur de catalogue — donc souvent ≈ 0.</para>
///
/// <para><b>Robustesse.</b> Liste nulle ou vide, deck vide, carte inconnue : aucune exception.
/// Sans allié (un seul contexte, ou aucun) le verdict vaut le comportement solo :
/// <see cref="ShouldTake"/> est faux par construction.</para>
/// </summary>
public sealed class CoopScorer
{
    /// <summary>
    /// Écart de ΔG (points de G) exigé pour recommander de céder la carte à un allié.
    /// <b>Non calibré</b> : 3 points ≈ 3 % de l'échelle 0..100, assez pour ignorer le bruit
    /// d'arrondi d'une dimension et pas assez pour recommander un transfert marginal.
    /// </summary>
    public const int CedingThreshold = 3;

    /// <summary>Nombre de contextes minimal pour parler d'un groupe (le porteur + un allié).</summary>
    public const int TeamSize = 2;

    private readonly PlayerStrengthCalculator _strength;
    private readonly CardEffects _effects;

    /// <summary>
    /// Catalogues injectables (tests) ; par défaut ceux du dépôt, chargés une seule fois par
    /// <see cref="PlayerStrengthCalculator"/> et <see cref="CardEffects"/>.
    /// </summary>
    public CoopScorer(PlayerStrengthCalculator? strength = null, CardEffects? effects = null)
    {
        _strength = strength ?? new PlayerStrengthCalculator();
        _effects = effects ?? CardEffects.Default;
    }

    /// <summary>Vrai pour une carte dont la cible est un allié (donnée de catalogue, pas une devinette).</summary>
    public bool IsSupportCard(CardInstance card) => IsSupportTarget(_effects.Get(card.Id)?.Target);

    /// <summary>Vrai si la cible du catalogue est <c>AnyAlly</c> ou <c>AllAllies</c>.</summary>
    public static bool IsSupportTarget(string? target) =>
        string.Equals(target, "AnyAlly", StringComparison.OrdinalIgnoreCase)
        || string.Equals(target, "AllAllies", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Verdict co-op : un ΔG par joueur (porteur en premier), l'indice du meilleur destinataire et
    /// une phrase chiffrée. <paramref name="players"/> = contextes des joueurs, le porteur en
    /// premier ; un seul contexte = solo.
    /// </summary>
    public CoopVerdict Evaluate(CardInstance card, IReadOnlyList<ScoringContext>? players)
    {
        var list = players ?? Array.Empty<ScoringContext>();
        var deltas = new int[list.Count];

        // Un ΔG par joueur, sur SON deck : même calcul que le fit solo, aucun raccourci.
        for (var i = 0; i < list.Count; i++)
            deltas[i] = _strength.DeltaForCard(list[i], card);

        // Meilleur joueur : strictement supérieur, donc à égalité le porteur (index 0) gagne —
        // on ne recommande jamais un transfert qui ne rapporte rien.
        var best = list.Count > 0 ? 0 : -1;
        for (var i = 1; i < deltas.Length; i++)
            if (deltas[i] > deltas[best]) best = i;

        var target = _effects.Get(card.Id)?.Target;
        return new CoopVerdict(card.Id, best, deltas, Describe(list, deltas, best, target));
    }

    /// <summary>
    /// Recommandation de cession : vrai si le meilleur allié gagne au moins
    /// <see cref="CedingThreshold"/> points de G de plus que le porteur
    /// (<c>ΔG(bestAlly) ≥ ΔG(porteur) + 3</c>). Faux sans allié : le comportement solo ne bouge pas.
    /// </summary>
    public bool ShouldTake(CardInstance card, IReadOnlyList<ScoringContext>? players) =>
        Recommends(Evaluate(card, players));

    /// <summary>
    /// Contrat v1.2 : évalue une carte pour l'équipe réelle — joueur local en premier, puis les
    /// alliés de <see cref="ScoringContext.Allies"/>. L'or des alliés n'est pas dans le contrat ;
    /// il n'entre pas dans le ΔG d'une carte, donc on réutilise celui du joueur local.
    /// </summary>
    public CoopVerdict Evaluate(CardInstance card, ScoringContext ctx)
    {
        var players = new List<ScoringContext>(1 + ctx.Allies.Count) { ctx };

        foreach (var ally in ctx.Allies)
        {
            players.Add(new ScoringContext(
                ally.CharacterId, ctx.ActIndex, ctx.BossId, ctx.Ascension, ctx.Floor,
                ally.Deck, ally.RelicIds, ctx.Gold, ally.Hp, ally.MaxHp));
        }

        return Evaluate(card, players);
    }

    /// <summary>
    /// Même règle, sur un verdict déjà calculé (évite un second passage de G sur tous les decks).
    /// </summary>
    public static bool Recommends(CoopVerdict verdict) =>
        verdict.PerAllyDelta.Count >= TeamSize && verdict.GainOverCarrier >= CedingThreshold;

    /// <summary>
    /// Phrase du verdict : les chiffres de chaque joueur, l'écart et la décision. Pas de phrase sans
    /// chiffre derrière (docs/27 §4).
    /// </summary>
    private static string Describe(
        IReadOnlyList<ScoringContext> players,
        IReadOnlyList<int> deltas,
        int best,
        string? target)
    {
        var kind = target is null
            ? "cible inconnue"
            : IsSupportTarget(target) ? $"carte de soutien ({target})" : $"carte non-soutien ({target})";

        if (players.Count == 0)
            return $"{kind} · aucun contexte de joueur — pas de verdict co-op (ΔG = 0, garder)";

        if (players.Count == 1)
            return $"{kind} · joueur seul : ΔG = {Signed(deltas[0])} — comportement solo inchangé";

        var perPlayer = new List<string>(players.Count) { $"porteur {Signed(deltas[0])}" };
        for (var i = 1; i < players.Count; i++)
        {
            var who = string.IsNullOrWhiteSpace(players[i].CharacterId)
                ? $"allié #{i}"
                : $"allié #{i} ({players[i].CharacterId})";
            perPlayer.Add($"{who} {Signed(deltas[i])}");
        }

        var gain = deltas[best] - deltas[0];
        var decision = gain >= CedingThreshold
            ? $"céder la carte à l'allié #{best} (écart {Signed(gain)} ≥ seuil {CedingThreshold})"
            : $"garder pour le porteur (écart {Signed(gain)} < seuil {CedingThreshold})";

        var carrierBest = best == 0 ? " · le porteur est le meilleur destinataire" : string.Empty;

        return $"{kind} · ΔG par joueur : {string.Join(", ", perPlayer)} · " +
               $"meilleur : #{best} à {Signed(deltas[best])}{carrierBest} → {decision}";
    }

    private static string Signed(int value) =>
        (value >= 0 ? "+" : "−") + Math.Abs(value).ToString(CultureInfo.InvariantCulture);
}
