using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Ultima.Engine.Contracts;
using Ultima.Engine.Scoring.Curses;
using Ultima.Engine.Scoring.Model;
using Ultima.Engine.Scoring.Personal;
using Ultima.Engine.Scoring.Strength;
using Ultima.Saves.Model;

namespace Ultima.Engine.Scoring.Cards;

/// <summary>
/// Score d'une carte en récompense (0-100, explicable) et score de l'option « passer ».
/// Formule figée (docs/24-hermes-arch.md §2) — somme de termes nommés, bornée 0..100 :
/// <list type="bullet">
///   <item><b>Méta communautaire</b> : round(consensus × 0.55), source <see cref="SourceLabel"/> ;</item>
///   <item><b>Enchantement</b> : +3 + min(2, amount) ;</item>
///   <item><b>Malédiction (impact mesuré)</b> : <see cref="CurseScorer.Penalty"/> — dérivée de la
///         toxicité mesurée sur 63 634 runs (docs/35), p. ex. −25 pour Poor Sleep, −17 pour
///         Injury, −14 pour Clumsy ; −12 explicite pour une malédiction sans mesure ; −6
///         forfaitaire pour un statut. Remplace le −30 fixe, qui ne distinguait pas une
///         malédiction mortelle d'une malédiction quasi neutre ;</item>
///   <item><b>Historique perso</b> : clamp(round(delta × 40), −8, +8), seulement si ≥5 runs des deux côtés ;</item>
///   <item><b>Fit au deck (ΔG)</b> : clamp(round(2·ΔG), −20, +20), ΔG = G(deck+carte) − G(deck)
///         (docs/27 §3 couche L1 et §7bis) — c'est le terme qui fait remonter une carte médiocre
///         en général mais parfaite pour ce deck ;</item>
///   <item><b>Synergie deck / reliques</b> : déléguée à <see cref="SynergyEngine"/> (+3/tag, cap 15 ; reliques 0..+5) ;</item>
///   <item><b>Adéquation boss</b> : −10..+10, lue dans <see cref="BossProfile"/> (multi-coups, débuffs, scaling) ;</item>
///   <item><b>Courbe de coût</b> : −5..+5, densité du deck au même coût ;</item>
///   <item><b>Besoin immédiat (L2)</b> : 0..+15, état de la run — PV bas pour les cartes de bloc/soin
///         (0..+10, proportionnel) et pire coup du boss au-dessus du bloc/tour du deck (+5).
///         La couche L2 ne regarde <b>pas</b> la composition du deck : ça, c'est le fit (L1).</item>
/// </list>
/// Aucun terme à 0 point n'est conservé : un score sans terme est un score sans donnée.
/// </summary>
public sealed class CardScorer
{
    /// <summary>Part du consensus communautaire retenue (≤ 55 points).</summary>
    private const double ConsensusWeight = 0.55;

    /// <summary>En dessous, la courbe de coût n'a pas de sens statistique.</summary>
    private const int MinDeckForCurve = 5;

    /// <summary>
    /// Plafond de la couche L2 (docs/27 §3) : un besoin parfaitement rempli vaut +15, jamais plus.
    /// C'est ce qui autorise une carte C-tier basse (45) + fit maximal (+20) + besoin (10) à sortir A.
    /// </summary>
    public const int MaxNeed = 15;

    /// <summary>
    /// Part du besoin venant des PV pour une carte de bloc/soin. docs/27 §3 écrit « 0..15 » pour la
    /// couche ; on réserve 10 aux PV et 5 à la menace du boss, la somme valant exactement
    /// <see cref="MaxNeed"/> (aucun rognage caché).
    /// </summary>
    public const int MaxHpNeed = 10;

    /// <summary>
    /// Points quand le pire coup du boss de l'acte dépasse le bloc par tour du deck : le coup passe,
    /// donc une carte de bloc répond à une menace réelle, pas théorique.
    /// </summary>
    public const int BossPressureNeed = 5;

    /// <summary>
    /// Au-dessus de cette part de PV, le pool encaisse encore : aucun besoin de bloc (docs/27 §3 L2,
    /// même esprit que le barème des reliques, mais ici proportionnel et non par bandes).
    /// </summary>
    public const double HpComfortRatio = 0.50;

    /// <summary>
    /// À cette part de PV (ou en dessous), le besoin de bloc est maximal : 20 % des PV, c'est moins
    /// de deux coups du boss de l'acte 1. Entre <see cref="HpCriticalRatio"/> et
    /// <see cref="HpComfortRatio"/>, le besoin décroît linéairement (proportionnel aux PV manquants).
    /// </summary>
    public const double HpCriticalRatio = 0.20;

    /// <summary>
    /// Amplitude maximale du fit au deck (docs/27 §3, couche L1 : ±20). Une carte à 45 (C) + 20 de
    /// fit + besoin peut donc dépasser 70 (A) ; S reste réservé aux cas où le fit s'ajoute à un
    /// socle déjà haut ou à une synergie forte.
    /// </summary>
    public const int MaxFit = 20;

    /// <summary>
    /// Échelle ΔG → points du terme. docs/27 §7bis écrit <c>note ≈ 2·ΔG + prior</c> : on garde ce 2.
    /// Un ΔG de 10 (carte qui comble un trou : Impervious dans un deck sans bloc) atteint donc le
    /// plafond de 20. <b>Non calibré</b> : la valeur attendue vient du backtest (docs/27 §5).
    /// </summary>
    public const double FitScale = 2.0;

    /// <summary>
    /// Taille minimale du deck pour mesurer un fit. En dessous, le débit de G est dominé par la
    /// dilution : un deck d'une carte la joue à chaque tour, donc ajouter n'importe quoi divise les
    /// dégâts/tour par deux et ΔG mesure cet artefact, pas un manque. Même seuil que la courbe de
    /// coût (<see cref="MinDeckForCurve"/>) : « pas de fit sur un deck naissant ».
    /// </summary>
    public const int MinDeckForFit = 5;

    private static readonly Lazy<PlayerStrengthCalculator> LazyStrength =
        new(() => new PlayerStrengthCalculator(), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Mémoïsation par contexte : <c>DeckEvaluator</c> note les 60 cartes d'un deck en boucle avec
    /// le MÊME <see cref="ScoringContext"/>, et ΔG coûte deux G par carte. Clé = référence du
    /// contexte (weak, aucune fuite), valeur = ΔG par (carte, niveau d'amélioration). Un contexte
    /// ne doit pas être muté en place entre deux appels — ils sont immuables par construction.
    /// Instance (pas statique) : un calculateur injecté ne doit pas relire un cache calculé par un autre.
    /// </summary>
    private readonly ConditionalWeakTable<ScoringContext, ConcurrentDictionary<string, StrengthDelta>>
        _fitMemos = new();

    /// <summary>
    /// Mémoïsation du profil de besoin par contexte (PV, bloc/tour, menace). Un même contexte sert à
    /// noter toutes les cartes offertes : sans ce cache, chaque carte relancerait un G complet.
    /// </summary>
    private readonly ConditionalWeakTable<ScoringContext, NeedProfile> _needMemos = new();

    private readonly IMetaTable _meta;
    private readonly IPersonalStats _personal;
    private readonly SynergyEngine _synergy;
    private readonly PlayerStrengthCalculator _strength;
    private readonly CardEffects _effects;
    private readonly CurseScorer _curses;

    public CardScorer(IMetaTable meta, IPersonalStats personal, SynergyEngine synergy,
        PlayerStrengthCalculator? strength = null, CardEffects? effects = null, CurseScorer? curses = null)
    {
        _meta = meta;
        _personal = personal;
        _synergy = synergy;
        _strength = strength ?? LazyStrength.Value;
        _effects = effects ?? CardEffects.Default;
        _curses = curses ?? CurseScorer.Default;
    }

    public Score ScoreCard(CardInstance card, ScoringContext ctx) => ScoreCard(card, ctx, includeNeed: true);

    /// <summary>
    /// Score d'une carte, avec ou sans la couche L2. <paramref name="includeNeed"/> = false sert à la
    /// note de deck : docs/27 §2 exige qu'elle reste <b>indépendante des PV</b> (sinon le joueur voit sa
    /// note de deck chuter quand il prend un coup). Les notes d'items, elles, gardent L2.
    /// </summary>
    internal Score ScoreCard(CardInstance card, ScoringContext ctx, bool includeNeed)
    {
        var b = new ScoreBuilder();
        var meta = _meta.Card(card.Id);

        if (meta?.Consensus is double consensus)
        {
            // Calibration sur le RANG dans la table, pas sur la valeur brute : les notes locales
            // (Untapped/slaythetierlist) n'ont pas les mêmes bornes que la variante publique.
            // Dernier rang → 20 (F), médiane → 48 (C), meilleur rang → 75 (A) ; S (85+) reste rare
            // et demande synergie/relique/boss en plus.
            int calibrated = meta.ConsensusPercentile is double p
                ? 20 + ScoreMath.Round(p * 55)
                : Math.Min(78, 7 + ScoreMath.Round(consensus * 0.65));   // repli sans distribution

            b.Add("Méta communautaire", Math.Clamp(calibrated, 0, 78), SourceLabel(meta));
        }

        if (card.Enchantment != null)
            b.Add($"Enchantement {card.Enchantment.Id}", 3 + Math.Min(2, card.Enchantment.Amount), "enchantement");

        // Malédiction : pénalité dérivée de l'impact mesuré (docs/35), plus de −30 forfaitaire.
        // La détection passe par le catalogue du jeu, car la méta publique ne note pas les
        // malédictions (0 entrée « Curse » dans card_tiers.json) : sans ça, le terme ne
        // s'appliquait jamais à une vraie malédiction.
        // Contrat v1.2 : le Total reste borné 0..100 (c'est la lettre S-F), mais l'effet SIGNÉ
        // sur la force du joueur part dans DeltaG — sinon une malédiction tombe à 0 et seule
        // la lettre bouge, ce qui masque la pénalité.
        var curseDelta = 0;
        if (_curses.Term(card.Id, _meta) is { } curse)
        {
            b.Add(curse);
            curseDelta = curse.Points;
        }

        var stats = _personal.Card(card.Id);
        if (stats?.WinRateDelta is double delta)
        {
            b.Add("Ton historique", ScoreMath.Clamp(ScoreMath.Round(delta * 40), -8, 8),
                $"tes {stats.RunsWithCard + stats.RunsWithoutCard} runs");
        }

        // Fit : le terme affiché et le ΔG signé (contrat v1.2) viennent du même calcul.
        double? fitDelta = null;
        if (Fit(card, ctx) is { } fit)
        {
            if (fit.Term is { } fitTerm) b.Add(fitTerm);
            fitDelta = fit.DeltaG;
        }

        b.AddRange(_synergy.Terms(card, ctx));

        if (BossFit(card, ctx) is { } bossFit) b.Add(bossFit);
        if (CostCurve(meta, ctx) is { } curve) b.Add(curve);
        if (includeNeed) b.AddRange(Need(card, ctx));

        var score = b.Build(Confidence.Of(meta?.SourceCount ?? 0, _personal.RunCount));
        var deltaG = curseDelta != 0 ? curseDelta : fitDelta;
        return deltaG is { } signed && signed != 0 ? score with { DeltaG = signed } : score;
    }

    /// <summary>
    /// Couche L2 « besoin immédiat » (docs/27 §3) : ce que la run traverse maintenant, jamais ce que
    /// le deck contient (ça, c'est le fit L1). Deux règles, chacune avec son libellé et sa source :
    /// <list type="number">
    ///   <item><b>PV bas</b> : sous <see cref="HpComfortRatio"/> des PV max, une carte de bloc ou de
    ///   soin gagne 1..<see cref="MaxHpNeed"/> points, proportionnellement aux PV manquants
    ///   (<see cref="HpCriticalRatio"/> ⇒ maximum). À PV plein : 0.</item>
    ///   <item><b>Menace du boss</b> : si le pire coup de l'acte dépasse le bloc par tour du deck,
    ///   une carte de bloc gagne <see cref="BossPressureNeed"/> (le coup passe aujourd'hui).</item>
    /// </list>
    /// Total borné 0..<see cref="MaxNeed"/>. La règle « rencontre multi-cibles (+5 zone) » de docs/27
    /// n'est <b>pas</b> implémentée : <see cref="ScoringContext"/> ne porte aucune rencontre en cours
    /// et on n'invente pas de champ (aucune donnée ⇒ aucun terme).
    /// </summary>
    private IReadOnlyList<ScoreTerm> Need(CardInstance card, ScoringContext ctx)
    {
        if (ctx.MaxHp <= 0) return Array.Empty<ScoreTerm>();

        var profile = _needMemos.GetValue(ctx, ComputeNeedProfile);
        bool block = IsBlockCard(card);
        bool heal = IsHealCard(card);
        if (!block && !heal) return Array.Empty<ScoreTerm>();

        var terms = new List<ScoreTerm>(2);
        var budget = MaxNeed;

        if (profile.HpRatio < HpComfortRatio)
        {
            // Proportionnel aux PV manquants : 0 à 50 %, maximum à 20 % (linéaire entre les deux).
            var span = HpComfortRatio - HpCriticalRatio;
            int points = ScoreMath.Round(MaxHpNeed * (HpComfortRatio - profile.HpRatio) / span);
            points = ScoreMath.Clamp(points, 1, MaxHpNeed); // sous le seuil, le besoin n'est jamais nul

            if (points > budget) points = budget;
            if (points > 0)
            {
                budget -= points;
                var what = block && heal ? "bloc/soin" : block ? "bloc" : "soin";
                terms.Add(new ScoreTerm($"Besoin immédiat — {what} (PV bas)", points,
                    $"PV {ctx.Hp}/{ctx.MaxHp} ({Percent(profile.HpRatio)}) → besoin de {what} ; " +
                    $"barème : {Percent(HpComfortRatio)} → 0, {Percent(HpCriticalRatio)} → {MaxHpNeed} (linéaire)"));
            }
        }

        if (block && profile.Threat > 0 && profile.Threat > profile.BlockPerTurn && budget > 0)
        {
            int points = Math.Min(BossPressureNeed, budget);
            terms.Add(new ScoreTerm("Besoin immédiat — coup du boss", points,
                $"pire coup {profile.Threat} > bloc/tour {Num(profile.BlockPerTurn)} — menace : {profile.ThreatSource}"));
        }

        return terms;
    }

    /// <summary>Profil d'état d'une run : part de PV, bloc par tour du deck, pire coup de l'acte.</summary>
    private sealed record NeedProfile(double HpRatio, double BlockPerTurn, int Threat, string ThreatSource);

    private NeedProfile ComputeNeedProfile(ScoringContext ctx)
    {
        var deck = ctx.Deck ?? Array.Empty<CardInstance>();
        double blockPerTurn = 0;

        // G expose « bloc/tour » comme mesure : on réutilise le modèle de débit du moteur plutôt que
        // d'en réécrire un (une seule définition de « bloc par tour » dans tout le dépôt).
        if (deck.Count > 0 && _strength.Compute(ctx).Measures.TryGetValue("blockPerTurn", out var block))
            blockPerTurn = block;

        var threat = _strength.ResolveThreat(ctx, Math.Max(1, ctx.ActIndex + 1));
        var ratio = ScoreMath.Clamp((double)ctx.Hp / ctx.MaxHp, 0, 1);
        return new NeedProfile(ratio, blockPerTurn, threat.WorstHit, threat.Source);
    }

    /// <summary>Carte de bloc : valeur de bloc chiffrée du catalogue, ou tag « bloc » (Barricade…).</summary>
    private bool IsBlockCard(CardInstance card) =>
        _effects.Get(card.Id)?.Block(card.UpgradeLevel) is > 0
        || _synergy.Tags(card).Contains("block", StringComparer.Ordinal);

    /// <summary>Carte de soin : le catalogue chiffre un soin (<c>vars["Heal"]</c>) — jamais deviné au nom.</summary>
    private bool IsHealCard(CardInstance card)
    {
        var effect = _effects.Get(card.Id);
        if (effect is null) return false;

        foreach (var (key, amount) in effect.Vars)
            if (amount > 0 && key.Contains("Heal", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static string Percent(double ratio) => $"{ScoreMath.Round(ratio * 100)} %";

    private static string Num(double value) =>
        value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Fit au deck (docs/27 §3 L1) : <c>ΔG = G(deck + carte) − G(deck)</c>, converti en points par
    /// <see cref="FitScale"/> et borné à <see cref="MaxFit"/>. Les dimensions de G saturent, donc
    /// une carte dont le besoin est déjà couvert vaut ≈ 0 : c'est l'idempotence exigée par docs/27.
    /// Deck vide ou naissant (&lt; <see cref="MinDeckForFit"/>) ⇒ aucun fit : il n'y a pas encore de
    /// deck dont mesurer le manque, et le modèle de débit y est dominé par la dilution.
    /// </summary>
    /// <summary>Terme affiché (null si arrondi à 0) + ΔG signé non arrondi, pour <c>Score.DeltaG</c>.</summary>
    private readonly record struct FitOutcome(ScoreTerm? Term, double DeltaG);

    private FitOutcome? Fit(CardInstance card, ScoringContext ctx)
    {
        var deck = ctx.Deck ?? Array.Empty<CardInstance>();
        if (deck.Count < MinDeckForFit) return null;

        var memo = _fitMemos.GetOrCreateValue(ctx);
        var key = $"{Ids.Bare(card.Id)}#{card.UpgradeLevel}";
        var delta = memo.GetOrAdd(key, _ => _strength.DeltaForCardDetailed(ctx, card));

        var points = ScoreMath.Clamp(ScoreMath.Round(delta.Delta * FitScale), -MaxFit, MaxFit);
        var term = points == 0 ? null : new ScoreTerm("Fit au deck (ΔG)", points, delta.Describe());

        // Un ΔG non nul reste porté même si le terme s'arrondit à 0 : c'est lui que lit le contrat.
        return delta.Delta == 0 ? null : new FitOutcome(term, delta.Delta);
    }

    /// <summary>
    /// Score de l'option « passer » : round(skipRate × 60) − round(meilleure carte offerte × 0.35),
    /// borné 0..100. Liste vide ⇒ seule la référence communautaire compte.
    /// </summary>
    public Score ScoreSkip(IReadOnlyList<CardInstance> offered, ScoringContext ctx)
    {
        var b = new ScoreBuilder();
        double? skip = _meta.SkipRate;
        if (skip.HasValue)
            b.Add("Taux de passage communautaire", ScoreMath.Round(skip.Value * 60),
                "Spire Codex (toutes versions)");

        var best = 0;
        if (offered != null)
            foreach (var card in offered)
                best = Math.Max(best, ScoreCard(card, ctx).Total);

        b.Add("Meilleure carte offerte", -ScoreMath.Round(best * 0.35), "comparaison des cartes proposées");

        return b.Build(Confidence.Of(_meta.SkipRate.HasValue ? 1 : 0, _personal.RunCount));
    }

    /// <summary>
    /// Adéquation au boss de l'acte : seuls comptent les traits réellement présents dans les moves du
    /// monstre (data/catalog/monsters.json via <see cref="BossProfile"/>). Boss inconnu ⇒ aucun terme.
    /// </summary>
    private ScoreTerm? BossFit(CardInstance card, ScoringContext ctx)
    {
        var boss = BossProfile.For(ctx.BossId);
        if (boss == null) return null;

        var tags = new HashSet<string>(_synergy.Tags(card), StringComparer.Ordinal);
        var points = 0;
        var reasons = new List<string>();

        if (boss.MultiHits)
        {
            if (tags.Contains("block"))
            {
                points += 3;
                reasons.Add("bloc contre les multi-coups");
            }

            if (tags.Contains("weak"))
            {
                points += 2;
                reasons.Add("Faiblesse réduit chaque coup");
            }
        }

        if (boss.Scaling)
        {
            if (tags.Contains("poison"))
            {
                points += 3;
                reasons.Add("dégâts dans le temps contre un boss qui monte");
            }

            if (tags.Contains("strength"))
            {
                points += 2;
                reasons.Add("course à la force");
            }

            if (tags.Contains("power"))
            {
                points += 1;
                reasons.Add("moteur de scaling");
            }
        }

        if (boss.Sustains && tags.Contains("poison"))
        {
            points += 2;
            reasons.Add("le poison passe avant les soins");
        }

        if (!boss.HasAttack && tags.Contains("block"))
        {
            points -= 3;
            reasons.Add("le boss n'attaque pas : le bloc ne sert à rien");
        }

        if (points == 0) return null;

        points = ScoreMath.Clamp(points, -10, 10);
        return new ScoreTerm($"Adéquation boss — {boss.Name}", points,
            $"data/catalog/monsters.json : {string.Join(", ", reasons)} ({boss.Evidence})");
    }

    /// <summary>
    /// Courbe de coût : compare le coût de la carte à la densité du deck au même coût.
    /// Coût absent du deck ⇒ +3 (place libre) ; ≥50 % du deck ⇒ −4 ; ≥35 % ⇒ −2 ; ≤10 % ⇒ +2.
    /// Ignoré sous <see cref="MinDeckForCurve"/> cartes (pas de courbe sur un deck naissant).
    /// </summary>
    private ScoreTerm? CostCurve(MetaEntry? meta, ScoringContext ctx)
    {
        if (meta?.Cost is not int cost) return null;
        if (ctx.Deck.Count < MinDeckForCurve) return null;

        // Le coût vient du CATALOGUE (578 cartes), pas de la méta : celle-ci ne note qu'une partie
        // des cartes, et s'y fier faisait conclure « 2/4 cartes à 1 énergie = saturation » sur un
        // deck de 14 cartes (2 réels). La méta reste le repli pour une carte hors catalogue.
        var same = 0;
        var known = 0;
        foreach (var card in ctx.Deck)
        {
            if (!TryDeckCost(card.Id, out var deckCost)) continue;
            known++;
            if (deckCost == cost) same++;
        }

        if (known == 0) return null;

        var share = (double)same / known;
        int points;
        string verdict;

        if (same == 0)
        {
            points = 3;
            verdict = $"aucune carte à {cost} énergie sur {known} : ce coût est encore libre";
        }
        else if (share >= 0.5)
        {
            points = -4;
            verdict = $"{same}/{known} cartes à {cost} énergie : ce coût sature le deck";
        }
        else if (share >= 0.35)
        {
            points = -2;
            verdict = $"{same}/{known} cartes à {cost} énergie : ce coût est déjà bien fourni";
        }
        else if (share <= 0.1)
        {
            points = 2;
            verdict = $"{same}/{known} cartes à {cost} énergie : ce coût est rare dans le deck";
        }
        else
        {
            return null;
        }

        var unresolved = Math.Max(0, ctx.Deck.Count - known);
        var caveat = unresolved == 0
            ? ""
            : $" — {unresolved} carte(s) du deck sans coût connu, hors calcul";

        return new ScoreTerm($"Courbe de coût à {cost} énergie", ScoreMath.Clamp(points, -5, 5),
            $"deck courant ({known} cartes comptées sur {ctx.Deck.Count}) : {verdict}{caveat}");
    }

    /// <summary>Coût d'une carte du deck : catalogue d'abord, méta en repli.</summary>
    private bool TryDeckCost(string cardId, out int cost)
    {
        if (CardCosts.TryCost(cardId, out cost)) return true;
        if (_meta.Card(cardId)?.Cost is int metaCost)
        {
            cost = metaCost;
            return true;
        }

        cost = 0;
        return false;
    }

    /// <summary>
    /// Vrai si l'entrée méta décrit une malédiction (Type/Rarity « Curse »). Conservé pour les
    /// appelants qui n'ont que la méta ; le <b>score</b>, lui, passe par <see cref="CurseScorer"/>,
    /// qui consulte aussi le catalogue du jeu (la méta publique ne note aucune malédiction).
    /// </summary>
    internal static bool IsCurse(MetaEntry? meta) =>
        meta != null && ((meta.Type ?? "").Contains("Curse", StringComparison.OrdinalIgnoreCase)
                         || (meta.Rarity ?? "").Contains("Curse", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Libellé de source lisible pour le joueur : « Spire Codex — consensus 78/100 (1 source, écart 0) ».
    /// L'explication doit nommer la source, pas seulement un identifiant technique.
    /// </summary>
    internal static string SourceLabel(MetaEntry meta)
    {
        var parts = meta.Ratings.Select(r => FriendlySource(r.SourceId)).Distinct().Take(2).ToArray();
        var who = parts.Length > 0 ? string.Join(" + ", parts) : "méta";

        var consensus = meta.Consensus is double c ? $"{c:0.#}/100" : "—";
        return $"{who} — consensus {consensus} ({meta.SourceCount} source(s), écart {meta.Spread:0.#})";
    }

    private static string FriendlySource(string sourceId) => sourceId switch
    {
        "spirecodex-scores" => "Spire Codex",
        "slaythetierlist" => "Slay the Tier List",
        "untapped" or "untapped-gg" => "Untapped.gg",
        _ => sourceId
    };
}
