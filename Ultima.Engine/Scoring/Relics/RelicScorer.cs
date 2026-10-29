using Ultima.Engine.Contracts;
using Ultima.Engine.Scoring.Cards;
using Ultima.Engine.Scoring.Model;
using Ultima.Engine.Scoring.Personal;
using Ultima.Engine.Scoring.Strength;
using Ultima.Saves.Model;

namespace Ultima.Engine.Scoring.Relics;

/// <summary>
/// Score d'une relique (récompense, shop). Formule figée (docs/24-hermes-arch.md §2) :
/// <c>consensus × 0.75 + synergie deck/personnage (0..+5)</c>, plus le <b>fit au deck (ΔG)</b>
/// borné ±15 (docs/27 §3 L1, §7bis) : une relique médiocre en général mais qui comble un trou du
/// deck (bloc, pioche, énergie) remonte. Total borné 0..100.
///
/// Une relique sans note chiffrée — catégorie d'Ancien (« Always Amazing », « Needs Synergy »…
/// non converties en score, cf. <c>score_conventions.ancient_category</c> de
/// data/meta/relic_tiers.json) ou entrée absente de la méta — ne reçoit <b>jamais</b> un faux
/// chiffre : le score se limite à la synergie et un terme explicite dit pourquoi.
/// </summary>
public sealed class RelicScorer
{
    /// <summary>Plafond de la synergie deck/personnage (formule figée : 0..+5).</summary>
    public const int MaxSynergy = 5;

    /// <summary>
    /// Amplitude maximale du fit d'une relique (docs/27 §3 L1). Plus étroite que celle des cartes
    /// (±20) : une relique ne fait pas gagner un combat à elle seule, et ses effets sont des tags,
    /// pas des valeurs mesurées (S11).
    /// </summary>
    public const int MaxFit = 15;

    /// <summary>
    /// Échelle ΔG → points. Les <b>tags</b> de reliques ne portent pas de valeurs numériques (S11) :
    /// leur ΔG est structurellement plus petit que celui d'une carte (max mesuré 6 sur les 304
    /// reliques du catalogue, deck sans bloc). ×2,5 porte ce maximum au plafond de 15.
    /// <b>Non calibré</b> : ordre de grandeur assumé, à revoir au backtest (docs/27 §5).
    /// </summary>
    public const double FitScale = 2.5;

    /// <summary>
    /// Taille minimale du deck pour mesurer un fit (même règle que les cartes : pas de fit sur un
    /// deck naissant, où G mesure surtout la dilution).
    /// </summary>
    public const int MinDeckForFit = CardScorer.MinDeckForFit;

    private readonly IMetaTable _meta;
    private readonly IPersonalStats _personal;
    private readonly RelicCatalog _catalog;
    private readonly PlayerStrengthCalculator _strength;

    public RelicScorer(IMetaTable meta, IPersonalStats personal, RelicCatalog? catalog = null,
        PlayerStrengthCalculator? strength = null)
    {
        _meta = meta;
        _personal = personal;
        _catalog = catalog ?? RelicCatalog.Load();

        // Même catalogue de reliques que le scoring : les tags sont lus une seule fois.
        _strength = strength ?? new PlayerStrengthCalculator(relics: _catalog);
    }

    public Score Score(string relicId, ScoringContext ctx)
    {
        var meta = _meta.Relic(relicId);

        // Relique inconnue : score borné + terme explicite, jamais une exception.
        if (meta == null)
            return NoData($"relique « {Ids.Bare(relicId)} » absente de data/meta/relic_tiers.json");

        var b = new ScoreBuilder();
        string? note = null, noteSource = null;

        if (meta.Consensus is double consensus)
        {
            // Même principe que les cartes : le rang décide, pas l'échelle du fichier.
            int calibrated = meta.ConsensusPercentile is double p
                ? 20 + ScoreMath.Round(p * 58)
                : Math.Min(80, 10 + ScoreMath.Round(ScoreMath.Clamp(consensus, 0, 100) * 0.70));

            b.Add("Méta communautaire", Math.Clamp(calibrated, 0, 80), CardScorer.SourceLabel(meta));
        }
        else
        {
            note = "Pas de note chiffrée";
            noteSource = NoScoreBasis(meta);
        }

        if (Fit(meta, ctx) is { } fit) b.Add(fit);

        foreach (var term in Synergy(meta, ctx)) b.Add(term);
        foreach (var term in Needs(meta, ctx)) b.Add(term);

        var confidence = meta.Consensus.HasValue
            ? Confidence.Of(meta.SourceCount, _personal.RunCount)
            : Confidence.Unknown;

        return AddNote(b.Build(confidence), note, noteSource);
    }

    /// <summary>
    /// Fit au deck (docs/27 §3 L1) : ΔG de la relique dans les tags de S11
    /// (<see cref="PlayerStrengthCalculator"/> convertit déjà les reliques en bloc/pioche/énergie),
    /// borné à <see cref="MaxFit"/>. Deck vide ou relique déjà possédée ⇒ aucun fit.
    /// </summary>
    private ScoreTerm? Fit(MetaEntry meta, ScoringContext ctx)
    {
        var deck = ctx.Deck ?? Array.Empty<CardInstance>();
        if (deck.Count < MinDeckForFit) return null;

        var delta = _strength.DeltaForRelicDetailed(ctx, meta.Id);
        var points = ScoreMath.Clamp(ScoreMath.Round(delta.Delta * FitScale), -MaxFit, MaxFit);
        if (points == 0) return null;

        return new ScoreTerm("Fit au deck (ΔG)", points, delta.Describe());
    }

    /// <summary>
    /// Besoin du joueur : une relique qui donne des PV max (ou soigne) ne vaut pas la même chose
    /// à 70/70 PV qu'à 15/70. C'est le seul endroit où les PV entrent dans un score — la note de
    /// deck reste, elle, indépendante des PV (stabilité de l'évaluation du deck).
    /// Barème : ≥ 80 % des PV → 0 ; 60-80 % → 5 ; 40-60 % → 10 ; &lt; 40 % → 15.
    /// </summary>
    private IEnumerable<ScoreTerm> Needs(MetaEntry meta, ScoringContext ctx)
    {
        var entry = _catalog.Get(meta.Id);
        if (entry is null || ctx.MaxHp <= 0) yield break;

        double ratio = ScoreMath.Clamp((double)ctx.Hp / ctx.MaxHp, 0, 1);

        if (entry.Has("revive"))
        {
            int pts = ratio <= 0.5 ? 15 : 5;
            yield return new ScoreTerm("Filet de sécurité (survie)", pts,
                $"{entry.NameFr} — PV {ctx.Hp}/{ctx.MaxHp}");
        }

        if (entry.Has("maxhp") || entry.Has("heal"))
        {
            int pts = ratio switch { < 0.40 => 15, < 0.60 => 10, < 0.80 => 5, _ => 0 };
            if (pts > 0)
                yield return new ScoreTerm("Répond à ton besoin de PV", pts,
                    $"{entry.NameFr} — {ctx.Hp}/{ctx.MaxHp} PV, {ScoreMath.Round((1 - ratio) * 100)} % manquants");
        }
    }

    /// <summary>
    /// Synergie explicite, plafonnée à <see cref="MaxSynergy"/> :
    /// — relique du pool du personnage joué : +1 ;
    /// — relique clé d'un cluster d'archétype dont le deck contient des cartes clés : +1..+3
    ///   (une carte clé = +1, plafond 3), le cluster le mieux aligné seulement.
    /// </summary>
    private IEnumerable<ScoreTerm> Synergy(MetaEntry relic, ScoringContext ctx)
    {
        int total = 0;

        var owner = Ids.CharacterKey(relic.Owner);
        var character = Ids.CharacterKey(ctx.CharacterId);
        if (owner.Length > 0 && owner == character && total < MaxSynergy)
        {
            total += 1;
            yield return new ScoreTerm("Relique de ton personnage", 1, $"{owner} (pool du personnage)");
        }

        var deck = ctx.Deck ?? Array.Empty<CardInstance>();
        if (deck.Count == 0) yield break;

        var deckIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var card in deck) deckIds.Add(Ids.Bare(card.Id));

        ScoreTerm? best = null;
        int bestPoints = 0;

        foreach (var archetype in _meta.Archetypes(ctx.CharacterId))
        {
            if (!Contains(archetype.KeyRelicIds, relic.Id)) continue;

            int present = archetype.KeyCardIds.Count(k => deckIds.Contains(Ids.Bare(k)));
            if (present < 2) continue; // une seule carte clé ne fait pas un cluster

            int points = Math.Min(3, present);
            if (points <= bestPoints) continue;

            bestPoints = points;
            best = new ScoreTerm($"Cluster « {archetype.Name} »", points,
                $"{present}/{archetype.KeyCardIds.Count} cartes clés du cluster dans ton deck");
        }

        if (best == null) yield break;

        if (total + bestPoints > MaxSynergy) bestPoints = MaxSynergy - total;
        if (bestPoints > 0) yield return best with { Points = bestPoints };
    }

    private static bool Contains(IReadOnlyList<string> ids, string relicId)
    {
        foreach (var id in ids)
            if (string.Equals(Ids.Bare(id), relicId, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>Pourquoi aucune note : catégorie d'Ancien (non convertie) ou source muette.</summary>
    private static string NoScoreBasis(MetaEntry meta)
    {
        bool ancient = string.Equals(meta.Owner, "ancient_event", StringComparison.OrdinalIgnoreCase);
        return ancient
            ? "catégorie d'Ancien non convertie en score (relic_tiers.json → score_conventions.ancient_category)"
            : $"aucune source chiffrée pour {Ids.Bare(meta.Id)} (0 source)";
    }

    /// <summary>Score 0 borné, confiance inconnue, une seule ligne : « aucune donnée ».</summary>
    private static Score NoData(string reason) =>
        new(0, new[] { new ScoreTerm("Aucune donnée", 0, reason) }, Confidence.Unknown);

    /// <summary>
    /// Ajoute un terme informatif à 0 point (ScoreBuilder ignore les 0) pour que l'absence de
    /// note soit lisible dans l'UI au lieu d'un total muet.
    /// </summary>
    private static Score AddNote(Score score, string? label, string? source)
    {
        if (string.IsNullOrWhiteSpace(label)) return score;
        var terms = new List<ScoreTerm>(score.Terms) { new(label!, 0, source ?? "data/meta") };
        return score with { Terms = terms };
    }
}
