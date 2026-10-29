using Ultima.Engine.Contracts;
using Ultima.Engine.Scoring.Model;

namespace Ultima.Engine.Scoring.Potions;

/// <summary>
/// Score d'une potion possédée (contrat v1.1, tâche S6bis). Aucune méta publique ne note les
/// potions : le score est dérivé de l'effet DÉCRIT dans le jeu, contextualisé par l'état de la run
/// (PV manquants, boss connu, acte). Tous les termes sont nommés et sourcés.
/// </summary>
public sealed class PotionScorer
{
    private const string SourceGame = "description du jeu (localisation FR)";
    private const int BasePoints = 40;

    private readonly PotionCatalog _catalog;

    public PotionScorer(PotionCatalog catalog) => _catalog = catalog;

    /// <summary>Null si la potion n'est pas dans le catalogue : jamais de score inventé.</summary>
    public Score? Score(string potionId, ScoringContext ctx)
    {
        var potion = _catalog.Get(potionId);
        if (potion is null) return null;

        var b = new ScoreBuilder();
        b.Add("Base potion (aucune méta publique)", BasePoints, SourceGame);

        double hpRatio = ctx.MaxHp > 0 ? ScoreMath.Clamp((double)ctx.Hp / ctx.MaxHp, 0, 1) : 1.0;
        double missing = 1.0 - hpRatio;

        if (potion.Has("revive"))
        {
            int pts = hpRatio <= 0.5 ? 25 : 10;
            b.Add("Filet de sécurité (survie)", pts, $"{potion.NameFr} — PV {ctx.Hp}/{ctx.MaxHp}");
        }

        if (potion.Has("heal"))
        {
            int pts = Math.Min(25, ScoreMath.Round(missing * 40));
            if (pts > 0)
                b.Add("Soin proportionnel aux PV manquants", pts,
                    $"{potion.NameFr} — il manque {ScoreMath.Round(missing * 100)} % des PV");
            else
                b.Add("Soin inutile à PV pleins", 0, potion.NameFr);
        }

        if (potion.Has("damage"))
        {
            int pts = ctx.BossId is { Length: > 0 } ? 12 : 4;
            if (ctx.ActIndex >= 1) pts += 2;
            b.Add(ctx.BossId is { Length: > 0 } ? "Burst sur le boss" : "Dégâts immédiats", pts,
                ctx.BossId is { Length: > 0 } ? $"boss {ctx.BossId}" : "pas de boss identifié");
        }

        if (potion.Has("debuff"))
        {
            int pts = ctx.BossId is { Length: > 0 } ? 8 : 3;
            b.Add("Affaiblit un ennemi", pts, potion.NameFr);
        }

        if (potion.Has("block")) b.Add("Défense immédiate", 8, potion.NameFr);
        if (potion.Has("energy")) b.Add("Tempo (énergie)", 8, potion.NameFr);
        if (potion.Has("draw")) b.Add("Tempo (pioche)", 6, potion.NameFr);
        if (potion.Has("buff")) b.Add("Buff durable", 6, potion.NameFr);
        if (potion.Has("maxhp")) b.Add("Gain permanent (PV max)", 5, potion.NameFr);
        if (potion.Has("gold")) b.Add("Gain d'or", 4, potion.NameFr);

        return b.Build(Confidence.Low);
    }
}
