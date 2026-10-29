using System.Globalization;
using Ultima.Engine.Contracts;
using Ultima.Engine.Scoring.Cards;
using Ultima.Engine.Scoring.Model;
using Ultima.Engine.Scoring.Relics;
using Ultima.Engine.Scoring.Threat;
using Ultima.Saves.Model;

namespace Ultima.Engine.Scoring.Strength;

/// <summary>
/// Statuts que le boss applique au tour 1, quand ils sont connus. Ils entrent dans les PV effectifs
/// (docs/27 §7bis) : un boss qui applique Vulnérable rend chaque point de PV moins utile.
/// <para>
/// Le modèle de menace ne publie pas encore les pouvoirs PAR move : l'appelant qui les connaît
/// (replay, future extension du catalogue) les passe ici. Par défaut zéro statut.
/// </para>
/// </summary>
public sealed record ThreatStatuses(int Vulnerable = 0, int Weak = 0)
{
    public static ThreatStatuses None { get; } = new();
}

/// <summary>
/// Référence de menace d'un acte : pire coup encaissable en un tour et PV du boss, avec sa source.
/// <see cref="IsActMedian"/> = true quand le boss exact est inconnu et qu'on prend la médiane des
/// boss de l'acte (donnée du catalogue, jamais une valeur inventée).
/// </summary>
public sealed record ThreatReference(int WorstHit, int BossHp, string Source, bool IsActMedian, int Act)
{
    public static ThreatReference None { get; } = new(0, 0, "aucune donnée de menace", false, 0);
}

/// <summary>
/// Score global du joueur (docs/27 §7bis), base du scoring contextuel :
/// <code>
/// G = w_s·Survie + w_o·Offense + w_c·Consistance + w_v·Couverture   (0..100)
/// </code>
/// Chaque dimension est normalisée 0..100 et <b>sature</b> (rendements décroissants) : la deuxième
/// carte de bloc d'un deck déjà couvert rapporte presque rien. C'est ce qui rend ΔG idempotent.
/// <para>
/// <see cref="Terms"/> porte une entrée par dimension (points = note de la dimension, source =
/// mesures exactes) plus les entrées « données insuffisantes ». Les points ne s'additionnent
/// <b>pas</b> au Total : c'est <see cref="Weights"/> qui combine les dimensions.
/// </para>
/// </summary>
public sealed record PlayerStrength(
    int Survie,
    int Offense,
    int Consistance,
    int Couverture,
    int Total,
    IReadOnlyList<ScoreTerm> Terms)
{
    /// <summary>Numéro d'acte 1-based utilisé pour les poids.</summary>
    public int Act { get; init; } = 1;

    /// <summary>Poids appliqués aux quatre dimensions (calibrés par défaut, cf. <see cref="StrengthWeights"/>).</summary>
    public ActWeights Weights { get; init; } = StrengthWeights.ForActIndex(0);

    /// <summary>
    /// Provenance des poids : « poids calibrés sur 71 634 runs (2026-10-29) » ou
    /// « poids par défaut (constantes écrites à la main) ». Reprise telle quelle par l'explication.
    /// </summary>
    public string WeightSource { get; init; } = WeightCalibration.Defaults.Provenance;

    /// <summary>Mesures brutes (bloc/tour, dégâts/tour, menace…) — utiles au ΔG et à l'explication.</summary>
    public IReadOnlyDictionary<string, double> Measures { get; init; } =
        new Dictionary<string, double>(StringComparer.Ordinal);

    /// <summary>high = données complètes, low = cartes ou boss non chiffrés.</summary>
    public string Confidence { get; init; } = Model.Confidence.Unknown;

    /// <summary>Score global du joueur dans son état courant (catalogues du dépôt).</summary>
    public static PlayerStrength G(ScoringContext ctx) => new PlayerStrengthCalculator().Compute(ctx);

    /// <summary>Idem, avec les statuts que le boss applique au tour 1 (PV effectifs).</summary>
    public static PlayerStrength G(ScoringContext ctx, ThreatStatuses statuses) =>
        new PlayerStrengthCalculator().Compute(ctx, statuses);
}

/// <summary>
/// ΔG d'un objet (carte ou relique) avec son état avant/après : <c>Delta = After.Total − Before.Total</c>.
/// <see cref="Describe"/> explique le chiffre en nommant les dimensions qui bougent — c'est la source
/// du terme « Fit au deck (ΔG) » (docs/27 §4 : pas de phrase sans chiffre derrière).
/// </summary>
public sealed record StrengthDelta(PlayerStrength Before, PlayerStrength After, string ItemId)
{
    /// <summary>Valeur de l'objet en points de G. Négative possible (dilution d'un deck saturé).</summary>
    public int Delta => After.Total - Before.Total;

    /// <summary>Libellé chiffré : dimensions déplacées + mesures qui les portent.</summary>
    public string Describe()
    {
        var parts = new List<string>();
        Dimension(parts, "survie", Before.Survie, After.Survie);
        Dimension(parts, "offense", Before.Offense, After.Offense);
        Dimension(parts, "consistance", Before.Consistance, After.Consistance);
        Dimension(parts, "couverture", Before.Couverture, After.Couverture);

        Measure(parts, "bloc/tour", "blockPerTurn");
        Measure(parts, "dégâts/tour", "damagePerTurn");
        Measure(parts, "pioche/tour", "drawPerTurn");
        Measure(parts, "énergie/tour", "energyPerTurn");

        var moved = parts.Count > 0 ? string.Join(", ", parts) : "aucune dimension ne bouge";
        var sign = Delta >= 0 ? "+" : "−";
        return $"ΔG = {sign}{Math.Abs(Delta)} pts (G {Before.Total} → {After.Total}, acte {Before.Act}) : " +
               $"{moved} · docs/27 §7bis";
    }

    private void Dimension(List<string> parts, string name, int before, int after)
    {
        if (before == after) return;
        var sign = after > before ? "+" : "−";
        parts.Add($"{sign}{name} {before}→{after} ({sign}{Math.Abs(after - before)})");
    }

    private void Measure(List<string> parts, string name, string key)
    {
        if (!Before.Measures.TryGetValue(key, out var before) || !After.Measures.TryGetValue(key, out var after))
            return;
        if (Math.Abs(after - before) < 0.05) return;
        parts.Add($"{name} {Num(before)}→{Num(after)}");
    }

    private static string Num(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);
}

/// <summary>
/// Calcule <see cref="PlayerStrength"/> depuis les <b>effets numériques</b> des cartes
/// (<c>data/catalog/cards_effects.json</c>), les tags des reliques (<c>data/catalog/relics.json</c>),
/// les PV du joueur et le modèle de menace (<c>data/catalog/monsters.json</c> +
/// <c>encounters.json</c>).
///
/// <para><b>Modèle de débit (le cœur, identique pour les quatre dimensions).</b>
/// On ne simule pas un combat : on mesure ce que le deck peut <i>débiter par tour</i> sous ses deux
/// contraintes réelles, la pioche et l'énergie.
/// <code>
/// pioche/tour   D = 5 + cartes qui piochent (auto-cohérent) + équivalents reliques
/// énergie/tour  E = 3 + énergie rendue par les cartes jouées + équivalents reliques
/// cartes jouées par tour = D × taux,  taux = min(1, E / (D × coût moyen))
/// bloc/tour     = Σ bloc du deck / N × D × taux + équivalents pouvoirs défensifs
/// dégâts/tour   = somme des k plus gros dégâts du deck, k = cartes jouées par tour
/// </code>
/// <see cref="BaseDrawPerTurn"/> et <see cref="BaseEnergyPerTurn"/> sont les règles du jeu ;
/// <see cref="XCostAssumedEnergy"/> est l'énergie supposée d'une carte X (la seule inconnue du
/// modèle).
/// </para>
///
/// <para><b>Pourquoi l'offense est un plafond et non une moyenne (S17).</b>
/// La densité moyenne <c>Σ dégâts / N × D × taux</c> suppose des cartes jouées au hasard : ajouter
/// un pouvoir (0 dégât) fait <i>baisser</i> la mesure alors que c'est ainsi qu'on gagne. Sur
/// 71 634 runs, la dimension bâtie dessus ne séparait pas les gagnants (AUC 0,50 à l'acte 1, 0,46
/// à l'acte 2, coefficient négatif aux deux actes) ; la somme des k meilleures cartes, elle, garde
/// le tempo (k monte avec la pioche et l'énergie), ne compte pas le remplissage, et donne le signal
/// positif le plus fort des 24 définitions testées (AUC 0,55 à l'acte 1, β +0,16 ; aucune ne
/// sépare à l'acte 2, où le poids proposé est 0).
/// Le scaling (Force/Poison/Focus, cartes et reliques) s'ajoute à ce burst puis passe la même
/// saturation : mesurer le scaling à part, avec son propre poids, diluait le signal.
/// Le bloc reste une densité moyenne — il faut bloquer <i>chaque</i> tour — et c'est la mesure qui
/// sépare le mieux dans la data (AUC 0,67 à l'acte 1).
/// Détail et chiffres : <c>status/hermes-s17.md</c>, <c>tools/calibration/report.md</c>.
/// </para>
///
/// <para><b>Saturation.</b> Toute sous-mesure est convertie par <see cref="Saturate"/> :
/// <c>100·x/(x+k)</c>, où <c>k</c> est le point à 50. Doubler une mesure déjà au-dessus de <c>k</c>
/// ne double pas la note — d'où l'idempotence exigée par docs/27 §7bis (une 2ᵉ source de bloc quand
/// le bloc est déjà couvert vaut ≈ 0).
/// </para>
///
/// <para><b>Robustesse.</b> Deck vide, carte inconnue, boss inconnu, catalogue absent : aucune
/// exception. Une dimension non calculable vaut 0 avec un terme explicite « données insuffisantes ».
/// </para>
/// </summary>
public sealed class PlayerStrengthCalculator
{
    // --- Règles du jeu (constantes, pas des poids appris) ---------------------------------------
    /// <summary>Cartes piochées par tour au repos (règle du jeu).</summary>
    public const int BaseDrawPerTurn = 5;

    /// <summary>Énergie par tour au repos (règle du jeu).</summary>
    public const int BaseEnergyPerTurn = 3;

    /// <summary>
    /// Énergie supposée d'une carte X (coût -1 dans le jeu) : une carte X se paie avec l'énergie
    /// disponible. On la chiffre à 1 pour ne pas inventer un build ; c'est un plancher conservateur.
    /// </summary>
    public const double XCostAssumedEnergy = 1.0;

    /// <summary>Coût moyen plancher : évite une division par ~0 sur un deck de cartes gratuites.</summary>
    public const double AvgCostFloor = 0.75;

    /// <summary>Durée de combat supposée pour convertir un soin par tour en soin par combat.</summary>
    public const double AssumedCombatTurns = 5.0;

    /// <summary>Une carte coûtant plus que l'énergie de base immobilise un tour entier → situationnelle.</summary>
    public const int HeavyCostThreshold = BaseEnergyPerTurn + 1;

    /// <summary>Taille de deck au-delà de laquelle la consistance se dégrade (25 cartes).</summary>
    public const double IdealDeckSize = 25.0;

    // --- Références de saturation (le « k » de 100·x/(x+k)) ------------------------------------
    /// <summary>Bloc/tour rapporté au pire coup du boss : 40 % couverts = 50 points (docs/27 : ~12 bloc/tour à l'acte 2, boss ~31).</summary>
    public const double BlockShareAtHalf = 0.40;

    /// <summary>PV effectifs en « pire coup encaissable » : 2,5 coups = 50 points (sans bloc, on meurt en 3 coups).</summary>
    public const double HpBufferHitsAtHalf = 2.5;

    /// <summary>Soins par combat en « pire coup » : un demi-coup soigné = 50 points.</summary>
    public const double HealHitsAtHalf = 0.5;

    /// <summary>
    /// Dégâts/tour visés pour tuer le boss en ce nombre de tours (boss acte 1 : 240 PV → 30/tour).
    /// Point à 50 de la saturation d'Offense.
    /// </summary>
    public const double OffenseKillTurns = 8.0;

    /// <summary>Couverture énergétique (énergie / coût moyen) : 2 = 50 points.</summary>
    public const double EnergyCoverageAtHalf = 2.0;

    /// <summary>Ratio pioche/tour rapporté à la pioche de base : 1 = 50 points.</summary>
    public const double DrawRatioAtHalf = 1.0;

    /// <summary>Part des dégâts en zone : 15 % = 50 points.</summary>
    public const double AoeShareAtHalf = 0.15;

    /// <summary>Cartes à statut négatif : 2 = 50 points.</summary>
    public const double DebuffCardsAtHalf = 2.0;

    /// <summary>Catégories d'utilité distinctes couvertes : 3 = 50 points.</summary>
    public const double UtilityAtHalf = 3.0;

    // --- Poids internes des dimensions (somme = 1 par dimension) --------------------------------
    public const double SurvivalBlockWeight = 0.55;
    public const double SurvivalHpWeight = 0.35;
    public const double SurvivalHealWeight = 0.10;

    public const double ConsistencyDrawWeight = 0.35;
    public const double ConsistencyEnergyWeight = 0.35;
    public const double ConsistencySizeWeight = 0.30;

    public const double CoverageAoeWeight = 0.45;
    public const double CoverageDebuffWeight = 0.35;
    public const double CoverageUtilityWeight = 0.20;

    // --- Pénalités de consistance (points de dimension) ----------------------------------------
    /// <summary>Une malédiction occupe une pioche et ne fait rien.</summary>
    public const int CursePenalty = 6;

    /// <summary>Un statut (Brûlure…) coûte moins qu'une malédiction : il disparaît parfois.</summary>
    public const int StatusPenalty = 3;

    /// <summary>Carte situationnelle (Éthérée, Sly, injouable, token, coût > énergie de base).</summary>
    public const int SituationalPenalty = 2;

    /// <summary>Plafond des pénalités de situation : elles expliquent, elles n'écrasent pas.</summary>
    public const int SituationalPenaltyMax = 10;

    /// <summary>Carte absente du catalogue d'effets : on ne sait pas ce qu'elle fait.</summary>
    public const int UnknownCardPenalty = 1;

    // --- Équivalents reliques (ordre de grandeur, NON calibrés) ---------------------------------
    // Les descriptions de reliques ne portent pas de valeurs numériques dans les données
    // commitées : on convertit donc chaque tag en ressource équivalente, avec un plafond.
    // Chaque constante est un ordre de grandeur assumé, pas une mesure.
    public const double RelicBlockPerTurn = 2.0;
    public const double RelicDrawPerTurn = 0.5;
    public const double RelicEnergyPerTurn = 0.5;
    public const double RelicHealPerCombat = 5.0;
    public const double RelicScalingPoints = 1.5;
    public const int RelicTagCap = 2;
    public const int RelicCursePenalty = 4;
    public const int RelicCurseCap = 3;

    // --- Équivalents de scaling (points par point de pouvoir) -----------------------------------
    public const double StrengthDamagePerPoint = 1.5;
    public const double PoisonDamagePerPoint = 0.8;
    public const double FocusDamagePerPoint = 1.0;
    public const double DexterityBlockPerPoint = 0.8;
    public const double PlatingBlockPerPoint = 0.6;
    public const double ThornsBlockPerPoint = 0.4;

    private static readonly HashSet<string> DebuffPowers = new(StringComparer.OrdinalIgnoreCase)
    {
        "Vulnerable", "Weak", "Poison", "Doom", "StrengthLoss", "Debilitate", "Disintegration", "Mind Rot",
    };

    private static readonly Lazy<RelicCatalog> LazyRelics = new(
        () => RelicCatalog.Load(), LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly CardEffects _effects;
    private readonly ThreatModel _threat;
    private readonly RelicCatalog _relics;
    private readonly WeightCalibration? _calibration;

    /// <summary>
    /// Catalogues injectables (tests) ; par défaut ceux du dépôt, chargés une seule fois.
    /// <paramref name="calibration"/> fixe les poids de G : null = poids effectifs du processus
    /// (<see cref="StrengthWeights.Current"/>), sinon les poids fournis (tests, simulation).
    /// </summary>
    public PlayerStrengthCalculator(
        CardEffects? effects = null,
        ThreatModel? threat = null,
        RelicCatalog? relics = null,
        WeightCalibration? calibration = null)
    {
        _effects = effects ?? CardEffects.Default;
        _threat = threat ?? ThreatModel.Default;
        _relics = relics ?? LazyRelics.Value;
        _calibration = calibration;
    }

    /// <summary>Courbe de saturation : <c>100·x/(x+k)</c>, jamais 100, jamais négative.</summary>
    public static double Saturate(double x, double halfPoint)
    {
        if (double.IsNaN(x) || x <= 0) return 0;
        var k = halfPoint > 0 ? halfPoint : 1.0;
        var value = 100.0 * x / (x + k);
        return double.IsNaN(value) ? 0 : Math.Min(100.0, value);
    }

    /// <summary>
    /// Score global du joueur. <paramref name="statuses"/> = statuts du tour 1 connus (PV effectifs) ;
    /// null = aucun statut publié.
    /// </summary>
    public PlayerStrength Compute(ScoringContext ctx, ThreatStatuses? statuses = null)
    {
        var st = statuses ?? ThreatStatuses.None;
        var deck = ctx.Deck ?? Array.Empty<CardInstance>();
        var calibration = _calibration ?? StrengthWeights.Current;
        var weights = calibration.ForActIndex(ctx.ActIndex);
        var act = ActNumber(ctx.ActIndex);
        var terms = new List<ScoreTerm>();
        var measures = new Dictionary<string, double>(StringComparer.Ordinal);

        // Deck vide : aucune mesure, zéro partout, une seule explication. Jamais d'exception.
        if (deck.Count == 0)
        {
            terms.Add(new ScoreTerm("Données insuffisantes", 0, "deck vide — aucune dimension calculable"));
            return new PlayerStrength(0, 0, 0, 0, 0, terms)
            {
                Act = act,
                Weights = weights,
                WeightSource = calibration.Provenance,
                Measures = measures,
                Confidence = Model.Confidence.Unknown,
            };
        }

        var profile = Profile(deck);
        var relic = RelicResources(ctx.RelicIds);

        var drawPerTurn = DrawPerTurn(profile.Draw, deck.Count, relic.Draw);
        var energyPerTurn = EnergyPerTurn(profile.EnergyGain, drawPerTurn, deck.Count, relic.Energy);
        var avgCost = profile.Playable > 0
            ? profile.Cost / profile.Playable
            : AvgCostFloor;
        var playRate = PlayRate(energyPerTurn, drawPerTurn, avgCost);
        var cardsPerTurn = playRate * drawPerTurn;

        // Offense = plafond de dégâts par tour : on joue ses k meilleures cartes (k = cartes
        // réellement jouables par tour), pas la moyenne du deck. Voir la note « Offense » de la
        // classe et status/hermes-s17.md.
        var burstCards = BurstCardCount(profile.Damages.Count, cardsPerTurn);
        var damagePerTurn = BurstDamage(profile.Damages, burstCards);
        // Densité moyenne (Σ dégâts / N × pioche × taux) : conservée comme mesure de diagnostic,
        // plus utilisée par la dimension — elle diluait les decks à pouvoirs et ignorait le tempo.
        var damageDensityPerTurn = playRate * drawPerTurn * profile.Damage / deck.Count;
        var cardBlockPerTurn = playRate * drawPerTurn * profile.Block / deck.Count;
        var blockPerTurn = cardBlockPerTurn + profile.DefenseScaling + relic.Block;
        var healPerCombat = playRate * drawPerTurn * profile.Heal / deck.Count * AssumedCombatTurns + relic.Heal;

        // Le scaling (Force/Poison/Focus des cartes, Force des reliques) s'ajoute aux dégâts par
        // tour : c'est la part récurrente du deck, plafonnée par la même saturation.
        var scaling = profile.OffenseScaling + relic.Scaling;

        measures["act"] = act;
        measures["deck"] = deck.Count;
        measures["drawPerTurn"] = drawPerTurn;
        measures["energyPerTurn"] = energyPerTurn;
        measures["avgCost"] = avgCost;
        measures["playRate"] = playRate;
        measures["damagePerTurn"] = damagePerTurn;
        measures["damageDensityPerTurn"] = damageDensityPerTurn;
        measures["burstCards"] = burstCards;
        measures["blockPerTurn"] = blockPerTurn;
        measures["healPerCombat"] = healPerCombat;
        measures["scaling"] = scaling;
        measures["scalingCards"] = profile.OffenseScaling;
        measures["aoeShare"] = profile.Damage > 0 ? (double)profile.AoeDamage / profile.Damage : 0;
        measures["debuffCards"] = profile.DebuffCards + relic.Debuff;
        measures["utility"] = profile.Utility.Count;
        measures["curses"] = profile.Curses;
        measures["statuses"] = profile.Statuses;
        measures["situational"] = profile.Situational;
        measures["unknownCards"] = profile.Unknown;

        var threat = ResolveThreat(ctx, act);
        measures["threat"] = threat.WorstHit;
        measures["bossHp"] = threat.BossHp;

        // --- Survie -------------------------------------------------------------------------
        var survie = 0;
        var effectiveHp = ThreatModel.EffectiveHp(ctx.Hp, ctx.MaxHp, st.Vulnerable, st.Weak);
        measures["effectiveHp"] = effectiveHp;

        if (threat.WorstHit <= 0)
        {
            terms.Add(new ScoreTerm("Survie", 0, "données insuffisantes — aucune menace chiffrée pour l'acte"));
        }
        else
        {
            double blockRatio = blockPerTurn / threat.WorstHit;
            double hpBuffer = effectiveHp / (double)threat.WorstHit;
            double healHits = healPerCombat / threat.WorstHit;
            double blockTarget = threat.WorstHit * BlockShareAtHalf;

            var sBlock = Saturate(blockRatio, BlockShareAtHalf);
            var sHp = Saturate(hpBuffer, HpBufferHitsAtHalf);
            var sHeal = Saturate(healHits, HealHitsAtHalf);

            survie = ScoreMath.Clamp(ScoreMath.Round(
                SurvivalBlockWeight * sBlock + SurvivalHpWeight * sHp + SurvivalHealWeight * sHeal), 0, 100);

            terms.Add(new ScoreTerm("Survie", survie,
                $"bloc {F(blockPerTurn)}/tour (réf. {F(blockTarget)} = {Pct(BlockShareAtHalf)} du pire coup " +
                $"{threat.WorstHit}) · PV effectifs {effectiveHp}/{ctx.MaxHp} → {F(hpBuffer)} coups " +
                $"(réf. {F(HpBufferHitsAtHalf)}) · soins {F(healPerCombat)} PV/combat · " +
                $"menace : {threat.Source} · {WeightNote(weights.Survie, calibration)}"));
        }

        // --- Offense ------------------------------------------------------------------------
        var offense = 0;
        if (threat.BossHp <= 0)
        {
            terms.Add(new ScoreTerm("Offense", 0, "données insuffisantes — PV du boss inconnus"));
        }
        else
        {
            var damageTarget = threat.BossHp / OffenseKillTurns;
            var sustained = damagePerTurn + scaling;

            offense = ScoreMath.Clamp(ScoreMath.Round(Saturate(sustained, damageTarget)), 0, 100);

            terms.Add(new ScoreTerm("Offense", offense,
                $"burst {F(damagePerTurn)} dgt/tour sur les {burstCards} meilleure(s) carte(s) " +
                $"+ scaling {F(scaling)} (cible {F(damageTarget)} = {threat.BossHp} PV / " +
                $"{F(OffenseKillTurns)} tours ; densité moyenne {F(damageDensityPerTurn)} dgt/tour, " +
                $"non utilisée) · {WeightNote(weights.Offense, calibration)}"));
        }

        // --- Consistance --------------------------------------------------------------------
        var sDraw = Saturate(drawPerTurn / BaseDrawPerTurn, DrawRatioAtHalf);
        var sEnergy = Saturate(energyPerTurn / Math.Max(avgCost, AvgCostFloor), EnergyCoverageAtHalf);
        // Taille : 100 jusqu'à l'idéal, puis 100·idéal/(idéal + excédent) — un deck qui grossit
        // au-delà de 25 cartes dilue ses meilleures cartes.
        var excess = Math.Max(0, deck.Count - IdealDeckSize);
        var sSize = 100.0 * IdealDeckSize / (IdealDeckSize + excess);

        var penalties = profile.Curses * CursePenalty
                        + profile.Statuses * StatusPenalty
                        + Math.Min(SituationalPenaltyMax, profile.Situational * SituationalPenalty)
                        + profile.Unknown * UnknownCardPenalty
                        + relic.CursePenalty;

        var consistance = ScoreMath.Clamp(ScoreMath.Round(
            ConsistencyDrawWeight * sDraw + ConsistencyEnergyWeight * sEnergy +
            ConsistencySizeWeight * sSize) - penalties, 0, 100);

        terms.Add(new ScoreTerm("Consistance", consistance,
            $"pioche {F(drawPerTurn)}/tour (réf. {BaseDrawPerTurn}) · énergie {F(energyPerTurn)}/tour " +
            $"pour un coût moyen {F(avgCost)} (taux de jeu {Pct(playRate)}) · deck {deck.Count} cartes " +
            $"(idéal ≤ {F(IdealDeckSize)}) · pénalités {penalties} " +
            $"({profile.Curses} malédiction(s), {profile.Statuses} statut(s), {profile.Situational} situationnelle(s), " +
            $"{profile.Unknown} inconnue(s)) · {WeightNote(weights.Consistance, calibration)}"));
        measures["penalties"] = penalties;

        // --- Couverture ---------------------------------------------------------------------
        var aoeShare = profile.Damage > 0 ? (double)profile.AoeDamage / profile.Damage : 0;
        var sAoe = Saturate(aoeShare, AoeShareAtHalf);
        var sDebuff = Saturate(profile.DebuffCards + relic.Debuff, DebuffCardsAtHalf);
        var sUtility = Saturate(profile.Utility.Count, UtilityAtHalf);

        var couverture = ScoreMath.Clamp(ScoreMath.Round(
            CoverageAoeWeight * sAoe + CoverageDebuffWeight * sDebuff +
            CoverageUtilityWeight * sUtility), 0, 100);

        terms.Add(new ScoreTerm("Couverture", couverture,
            $"dégâts de zone {Pct(aoeShare)} (réf. {Pct(AoeShareAtHalf)}) · " +
            $"{profile.DebuffCards + relic.Debuff} carte(s) à statut négatif (réf. {F(DebuffCardsAtHalf)}) · " +
            $"{profile.Utility.Count} catégorie(s) d'utilité ({string.Join(", ", profile.Utility)}) " +
            $"(réf. {F(UtilityAtHalf)}) · {WeightNote(weights.Couverture, calibration)}"));

        // --- Données insuffisantes (transparence) -------------------------------------------
        if (profile.Unknown > 0)
        {
            terms.Add(new ScoreTerm("Données insuffisantes", 0,
                $"{profile.Unknown} carte(s) absente(s) de data/catalog/cards_effects.json — comptées au deck, " +
                "sans dégâts/bloc/pioche"));
        }
        if (threat.IsActMedian)
        {
            terms.Add(new ScoreTerm("Données insuffisantes", 0,
                $"boss « {ctx.BossId ?? "aucun"} » inconnu — référence = {threat.Source}"));
        }
        if (relic.Describe() is { Length: > 0 } relicText)
        {
            terms.Add(new ScoreTerm("Reliques (équivalents)", 0, relicText));
        }

        var total = weights.Apply(survie, offense, consistance, couverture);
        var coverage = deck.Count > 0 ? 1.0 - (double)profile.Unknown / deck.Count : 0;

        return new PlayerStrength(survie, offense, consistance, couverture, total, terms)
        {
            Act = act,
            Weights = weights,
            WeightSource = calibration.Provenance,
            Measures = measures,
            Confidence = coverage >= 0.99 && profile.Unknown == 0 && !threat.IsActMedian && threat.WorstHit > 0
                ? Model.Confidence.High
                : coverage >= 0.6 && threat.WorstHit > 0 ? Model.Confidence.Medium : Model.Confidence.Low,
        };
    }

    /// <summary>
    /// Valeur d'une carte en points de G (docs/27 §7bis) : <c>ΔG = G(deck + carte) − G(deck)</c>.
    /// Négatif possible (une carte qui dilue un deck saturé).
    /// </summary>
    public int DeltaForCard(ScoringContext ctx, CardInstance card, ThreatStatuses? statuses = null) =>
        DeltaForCardDetailed(ctx, card, statuses).Delta;

    /// <summary>
    /// ΔG d'une relique : <c>G(reliques + relique) − G(reliques)</c>, à deck constant. Les reliques
    /// passent par les <b>mêmes tags</b> que S11 (<see cref="RelicResources"/>) : une relique déjà
    /// possédée vaut 0 (aucun double comptage).
    /// </summary>
    public int DeltaForRelic(ScoringContext ctx, string relicId, ThreatStatuses? statuses = null) =>
        DeltaForRelicDetailed(ctx, relicId, statuses).Delta;

    /// <summary>
    /// ΔG d'une carte avec l'état avant/après : c'est ce que le terme « Fit au deck » affiche pour
    /// nommer les dimensions qui bougent. Le score reste <c>After.Total − Before.Total</c>.
    /// </summary>
    public StrengthDelta DeltaForCardDetailed(ScoringContext ctx, CardInstance card, ThreatStatuses? statuses = null)
    {
        var before = Compute(ctx, statuses);
        var deck = new List<CardInstance>(ctx.Deck ?? Array.Empty<CardInstance>()) { card };
        var after = Compute(ctx with { Deck = deck }, statuses);
        return new StrengthDelta(before, after, card.Id);
    }

    /// <summary>ΔG d'une relique avec l'état avant/après (relique déjà possédée ⇒ delta 0).</summary>
    public StrengthDelta DeltaForRelicDetailed(ScoringContext ctx, string relicId, ThreatStatuses? statuses = null)
    {
        var before = Compute(ctx, statuses);
        var owned = ctx.RelicIds ?? Array.Empty<string>();

        foreach (var id in owned)
        {
            if (string.Equals(Ids.Bare(id), Ids.Bare(relicId), StringComparison.OrdinalIgnoreCase))
                return new StrengthDelta(before, before, relicId);
        }

        var relics = new List<string>(owned) { relicId };
        var after = Compute(ctx with { RelicIds = relics }, statuses);
        return new StrengthDelta(before, after, relicId);
    }

    // --- Menace ---------------------------------------------------------------------------------

    /// <summary>
    /// Pire coup de l'acte et PV du boss : boss exact si <c>ctx.BossId</c> se résout, sinon médiane
    /// des boss de l'acte du catalogue (<see cref="ThreatReference.IsActMedian"/>), sinon
    /// <see cref="ThreatReference.None"/>. Aucune valeur inventée.
    /// </summary>
    public ThreatReference ResolveThreat(ScoringContext ctx, int act)
    {
        var bossId = ctx.BossId;
        if (!string.IsNullOrWhiteSpace(bossId))
        {
            var monsters = _threat.MonstersOf(bossId);
            if (monsters.Count > 0)
            {
                var worst = _threat.MaxHit(bossId!, ctx.Ascension);
                var hp = SumHp(monsters, ctx.Ascension);
                if (worst > 0 && hp > 0)
                {
                    var name = _threat.Encounter(bossId)?.Name ?? bossId!;
                    return new ThreatReference(worst, hp,
                        $"{name} — pire coup {worst} dgt, {hp} PV (data/catalog)", false, act);
                }
            }
        }

        var hits = new List<int>();
        var hps = new List<int>();
        foreach (var encounter in _threat.Encounters)
        {
            if (encounter.RoomType != "Boss" || encounter.Act != act) continue;
            var hit = _threat.MaxHit(encounter.Id, ctx.Ascension);
            if (hit > 0) hits.Add(hit);
            var bossHp = SumHp(_threat.MonstersOf(encounter.Id), ctx.Ascension);
            if (bossHp > 0) hps.Add(bossHp);
        }

        if (hits.Count == 0 || hps.Count == 0) return ThreatReference.None;

        var medianHit = ScoreMath.Round(ScoreMath.Median(hits.Select(h => (double)h).ToArray()));
        var medianHp = ScoreMath.Round(ScoreMath.Median(hps.Select(h => (double)h).ToArray()));
        return new ThreatReference(medianHit, medianHp,
            $"médiane des {hits.Count} boss de l'acte {act} — pire coup {medianHit} dgt, {medianHp} PV", true, act);
    }

    private static int SumHp(IReadOnlyList<MonsterThreat> monsters, int ascension)
    {
        var total = 0;
        foreach (var monster in monsters)
            if (monster.HpAt(ascension) is int hp && hp > 0)
                total += hp;
        return total;
    }

    // --- Débit du deck --------------------------------------------------------------------------

    /// <summary>
    /// Pioche par tour : <c>5 / (1 − part de pioche du deck)</c>, plafonnée à deux fois la base.
    /// Auto-cohérent : une carte qui pioche est elle-même piochée à <c>D/N</c>, donc elle ajoute
    /// <c>d × D/N</c> cartes par tour. Plafond : une part ≥ 60 % est déjà une boucle infinie en pratique.
    /// </summary>
    private static double DrawPerTurn(int drawSum, int deckSize, double relicDraw)
    {
        if (deckSize <= 0) return BaseDrawPerTurn + relicDraw;
        var share = Math.Min(0.6, drawSum / (double)deckSize);
        var draw = BaseDrawPerTurn / (1.0 - share);
        return Math.Min(2.0 * BaseDrawPerTurn, draw) + relicDraw;
    }

    /// <summary>
    /// Énergie par tour : base + énergie rendue par les cartes jouées (<c>montant × D/N</c>), plafonnée
    /// à deux fois la base. Une carte d'énergie coûte elle-même de l'énergie : son coût est dans le
    /// coût moyen, on ne le recompte pas ici.
    /// </summary>
    private static double EnergyPerTurn(int energySum, double drawPerTurn, int deckSize, double relicEnergy)
    {
        var gain = deckSize > 0 ? energySum * drawPerTurn / deckSize : 0;
        return Math.Min(2.0 * BaseEnergyPerTurn, BaseEnergyPerTurn + gain) + relicEnergy;
    }

    /// <summary>Part des cartes piochées réellement jouables sous contrainte d'énergie.</summary>
    private static double PlayRate(double energyPerTurn, double drawPerTurn, double avgCost)
    {
        if (drawPerTurn <= 0) return 0;
        var demand = drawPerTurn * Math.Max(avgCost, AvgCostFloor);
        return demand <= 0 ? 1 : Math.Clamp(energyPerTurn / demand, 0, 1);
    }

    /// <summary>
    /// Nombre de cartes dont on peut réellement jouer les dégâts en un tour : l'arrondi supérieur
    /// des cartes jouées par tour, borné par le nombre de cartes de dégâts présentes.
    /// </summary>
    public static int BurstCardCount(int damageCards, double cardsPerTurn)
    {
        if (damageCards <= 0 || cardsPerTurn <= 0) return 0;
        // -1e-9 : 3.0000000001 cartes/tour ne doit pas compter 4 cartes.
        var k = (int)Math.Ceiling(cardsPerTurn - 1e-9);
        return Math.Clamp(k, 1, damageCards);
    }

    /// <summary>
    /// Somme des <paramref name="burstCards"/> plus gros dégâts du deck : ce qu'un tour peut
    /// sortir si le joueur joue ses meilleures cartes, au lieu de la moyenne de toutes les cartes.
    /// </summary>
    public static double BurstDamage(IReadOnlyList<int> damages, int burstCards)
    {
        if (burstCards <= 0 || damages.Count == 0) return 0;
        var sorted = new int[damages.Count];
        for (var i = 0; i < damages.Count; i++) sorted[i] = damages[i];
        Array.Sort(sorted);
        double total = 0;
        var taken = 0;
        for (var i = sorted.Length - 1; i >= 0 && taken < burstCards; i--, taken++)
            total += sorted[i];
        return total;
    }

    // --- Profil du deck -------------------------------------------------------------------------

    private DeckProfile Profile(IReadOnlyList<CardInstance> deck)
    {
        var p = new DeckProfile();

        foreach (var card in deck)
        {
            var effect = _effects.Get(card.Id);
            if (effect is null)
            {
                p.Unknown++;
                continue;
            }

            var type = effect.Type ?? string.Empty;
            var isCurse = type.Equals("Curse", StringComparison.OrdinalIgnoreCase);
            var isStatus = type.Equals("Status", StringComparison.OrdinalIgnoreCase);
            if (isCurse) p.Curses++;
            if (isStatus) p.Statuses++;

            // Injouable : aucune contribution (Brûlure « 2 dégâts » est subie, pas infligée), mais
            // elle occupe une place dans la pioche — la dilution est comptée par la taille du deck.
            if (effect.HasKeyword("Unplayable"))
            {
                if (!isCurse) p.Situational++;
                continue;
            }

            var upgrade = card.UpgradeLevel;
            var xCost = effect.IsXCost;
            var repeats = xCost ? XCostAssumedEnergy : 1.0;

            p.Playable++;
            p.Cost += xCost ? XCostAssumedEnergy : Math.Max(0, effect.CostAfter(upgrade) ?? 0);

            var damage = effect.TotalDamage(upgrade) * repeats;
            if (damage > 0)
            {
                var rounded = (int)Math.Round(damage);
                p.Damage += rounded;
                p.Damages.Add(rounded);
                if (string.Equals(effect.Target, "AllEnemies", StringComparison.OrdinalIgnoreCase))
                    p.AoeDamage += rounded;
                p.Utility.Add("damage");
            }

            var block = (effect.Block(upgrade) ?? 0) * repeats;
            if (block > 0)
            {
                p.Block += (int)Math.Round(block);
                p.Utility.Add("bloc");
            }

            var draw = effect.Draw(upgrade);
            if (draw > 0) p.Utility.Add("pioche");
            p.Draw += draw;

            var energy = effect.Energy(upgrade);
            if (energy > 0) p.Utility.Add("énergie");
            p.EnergyGain += energy;

            if (effect.Vars.TryGetValue("Heal", out var heal) && heal > 0)
            {
                p.Heal += heal;
                p.Utility.Add("soin");
            }

            var offenseScaling = OffenseScaling(effect);
            if (offenseScaling > 0)
            {
                p.OffenseScaling += offenseScaling;
                p.Utility.Add("scaling");
            }

            p.DefenseScaling += DefenseScaling(effect);

            if (HasDebuff(effect))
            {
                p.DebuffCards++;
                p.Utility.Add("statut");
            }

            if (IsSituational(effect, type, upgrade))
                p.Situational++;
        }

        return p;
    }

    private static bool HasDebuff(CardEffect effect)
    {
        foreach (var power in effect.Powers)
            if (DebuffPowers.Contains(power))
                return true;
        return false;
    }

    /// <summary>Carte qui ne produit son effet que sous condition (Éthérée, Sly, token, injouable, coût lourd).</summary>
    private static bool IsSituational(CardEffect effect, string type, int upgrade)
    {
        if (effect.HasKeyword("Ethereal") || effect.HasKeyword("Sly")) return true;
        if (effect.HasKeyword("Unplayable")) return true;
        if (type.Equals("Quest", StringComparison.OrdinalIgnoreCase)) return true;
        if (effect.Rarity is "Token") return true;
        var cost = effect.IsXCost ? 0 : effect.CostAfter(upgrade) ?? 0;
        return cost > HeavyCostThreshold;
    }

    private static double OffenseScaling(CardEffect effect) =>
        (effect.PowerAmount("Strength") ?? 0) * StrengthDamagePerPoint
        + (effect.PowerAmount("Poison") ?? 0) * PoisonDamagePerPoint
        + (effect.PowerAmount("Focus") ?? 0) * FocusDamagePerPoint;

    /// <summary>Dextérité/Plating/Thorns : bloc par tour équivalent (la Dextérité n'est pas des dégâts).</summary>
    private static double DefenseScaling(CardEffect effect) =>
        (effect.PowerAmount("Dexterity") ?? 0) * DexterityBlockPerPoint
        + (effect.PowerAmount("Plating") ?? 0) * PlatingBlockPerPoint
        + (effect.PowerAmount("Thorns") ?? 0) * ThornsBlockPerPoint;

    // --- Reliques -------------------------------------------------------------------------------

    /// <summary>
    /// Conversion des tags de reliques en ressources équivalentes (plafonnée). Les données commitées
    /// ne portent pas les valeurs numériques des reliques : ce sont des ordres de grandeur assumés,
    /// jamais présentés comme mesurés (voir status/hermes-s11.md).
    /// </summary>
    private RelicEquivalents RelicResources(IReadOnlyList<string>? relicIds)
    {
        var r = new RelicEquivalents();
        if (relicIds is null || relicIds.Count == 0) return r;

        int block = 0, draw = 0, energy = 0, heal = 0, scaling = 0, debuff = 0, curse = 0;

        foreach (var id in relicIds)
        {
            // Ids.Bare : ctx.RelicIds porte « RELIC.X », le catalogue indexe « X ».
            var entry = _relics.Get(Ids.Bare(id));
            if (entry is null) continue;

            if (entry.Has("block") && block++ < RelicTagCap) r.Block += RelicBlockPerTurn;
            if (entry.Has("draw") && draw++ < RelicTagCap) r.Draw += RelicDrawPerTurn;
            if (entry.Has("energy") && energy++ < RelicTagCap) r.Energy += RelicEnergyPerTurn;
            if (entry.Has("heal") && heal++ < RelicTagCap) r.Heal += RelicHealPerCombat;
            if (entry.Has("strength") && scaling++ < RelicTagCap) r.Scaling += RelicScalingPoints;
            if (entry.Has("debuff") && debuff++ < RelicTagCap) r.Debuff += 1;
            if (entry.Has("curse") && curse++ < RelicCurseCap) r.CursePenalty += RelicCursePenalty;
        }

        // Une relique qui augmente les PV max est déjà dans ctx.MaxHp : rien à rajouter (pas de
        // double comptage). Les reliques de Force/Dextérité sont converties en scaling offense.
        r.Scaling = Math.Min(r.Scaling, RelicTagCap * RelicScalingPoints);
        return r;
    }

    private sealed class RelicEquivalents
    {
        public double Block;
        public double Draw;
        public double Energy;
        public double Heal;
        public double Scaling;
        public int Debuff;
        public int CursePenalty;

        /// <summary>Résumé lisible des équivalents réellement utilisés (vide si aucun).</summary>
        public string Describe()
        {
            var parts = new List<string>();
            if (Block > 0) parts.Add($"+{F(Block)} bloc/tour");
            if (Draw > 0) parts.Add($"+{F(Draw)} pioche/tour");
            if (Energy > 0) parts.Add($"+{F(Energy)} énergie/tour");
            if (Heal > 0) parts.Add($"+{F(Heal)} PV/combat");
            if (Scaling > 0) parts.Add($"+{F(Scaling)} scaling");
            if (Debuff > 0) parts.Add($"+{Debuff} statut négatif");
            if (CursePenalty > 0) parts.Add($"−{CursePenalty} consistance (malédiction)");
            return parts.Count == 0 ? string.Empty : string.Join(", ", parts);
        }
    }

    // --- Profil ---------------------------------------------------------------------------------

    private sealed class DeckProfile
    {
        public int Playable;
        public int Unknown;
        public int Curses;
        public int Statuses;
        public int Situational;
        public int Damage;
        /// <summary>Dégâts arrondis de chaque carte qui en inflige (pour le burst top-k).</summary>
        public List<int> Damages { get; } = new();
        public int Block;
        public double Cost;
        public int Draw;
        public int EnergyGain;
        public int Heal;
        public int AoeDamage;
        public int DebuffCards;
        public double OffenseScaling;
        public double DefenseScaling;
        public HashSet<string> Utility { get; } = new(StringComparer.Ordinal);
    }

    // --- Format ---------------------------------------------------------------------------------

    private static int ActNumber(int actIndex) => Math.Clamp(actIndex, 0, 2) + 1;

    /// <summary>
    /// Libellé du poids dans un terme de dimension : « poids calibrés sur 71 634 runs
    /// (2026-10-29) : 0.85 » ou « poids par défaut (constantes écrites à la main) : 0.40 ». Le
    /// libellé de la dimension (« Survie », « Offense »…) est inchangé — seule la provenance
    /// s'ajoute devant la valeur.
    /// </summary>
    private static string WeightNote(double weight, WeightCalibration calibration) =>
        $"{calibration.Provenance} : {F2(weight)}";

    private static string F(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);

    private static string F2(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Pct(double ratio) =>
        (Math.Clamp(ratio, 0, 1) * 100).ToString("0", CultureInfo.InvariantCulture) + " %";
}
