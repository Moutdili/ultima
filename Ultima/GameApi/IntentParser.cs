using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.ValueProps;
using Ultima.Core.Model;

namespace Ultima.GameApi;

/// <summary>
/// Traduit une intention de monstre en <see cref="IntentSnapshot"/> en LISANT les modèles du jeu
/// (jamais le texte du label, jamais une constante de jeu en dur). Lecture seule, ne lève jamais.
/// </summary>
public static class IntentParser
{
    /// <param name="intent">Intention de <c>creature.Monster.NextMove.Intents</c>.</param>
    /// <param name="dealer">Créature ennemie qui porte l'intention.</param>
    /// <param name="targets"><c>dealer.CombatState.PlayerCreatures</c> (mêmes cibles que l'affichage du jeu).</param>
    /// <param name="me">Joueur local (<c>LocalContext.GetMe</c>).</param>
    /// <param name="players">Tous les joueurs du combat (co-op : <see cref="IntentSnapshot.PerTarget"/>).</param>
    public static IntentSnapshot Parse(
        AbstractIntent intent, Creature dealer, IEnumerable<Creature> targets,
        Player me, IReadOnlyList<Player> players)
    {
        string kind = "";
        try
        {
            kind = intent.IntentType.ToString();
        }
        catch (Exception ex)
        {
            ReaderLog.Error("intent.Kind", ex);
        }

        string label = "";
        try
        {
            // Oracle d'affichage uniquement : jamais une source de calcul.
            label = intent.GetIntentLabel(targets, dealer).GetFormattedText() ?? "";
        }
        catch (Exception ex)
        {
            ReaderLog.Error("intent.Label", ex);
        }

        if (intent is not AttackIntent ai)
        {
            return new IntentSnapshot(kind, 0, 0, 0, label);
        }

        try
        {
            return ParseAttack(ai, kind, label, dealer, targets, me, players);
        }
        catch (Exception ex)
        {
            ReaderLog.Error("intent.Attack", ex);
            return new IntentSnapshot(kind, 0, 0, 0, label);
        }
    }

    private static IntentSnapshot ParseAttack(
        AttackIntent ai, string kind, string label, Creature dealer, IEnumerable<Creature> targets,
        Player me, IReadOnlyList<Player> players)
    {
        // SingleAttackIntent → 1, MultiAttackIntent → N ; la base renvoie 0 : une attaque frappe au moins une fois.
        int repeats = 1;
        try
        {
            repeats = Math.Max(1, ai.Repeats);
        }
        catch (Exception ex)
        {
            ReaderLog.Error("intent.Repeats", ex);
        }

        // Pipeline réel du jeu (AttackIntent.cs:84), cible = joueur local.
        int perHit = 0;
        bool perHitOk = false;
        try
        {
            perHit = ai.GetSingleDamage(targets, dealer);
            perHitOk = true;
        }
        catch (Exception ex)
        {
            ReaderLog.Error("intent.PerHit", ex);
        }

        // Dégâts de base de l'intention (DamageCalc), lus une seule fois et réutilisés pour chaque cible.
        decimal? baseDamage = null;
        try
        {
            if (ai.DamageCalc is { } calc)
            {
                baseDamage = calc();
            }
        }
        catch (Exception ex)
        {
            ReaderLog.Error("intent.DamageCalc", ex);
        }

        // Même appel EXACT que AttackIntent.cs:84-93, avant troncature.
        decimal raw = perHit;
        bool rawOk = false;
        if (baseDamage is { } dmg)
        {
            try
            {
                raw = ModifyDamage(me, dealer, dmg);
                rawOk = true;
            }
            catch (Exception ex)
            {
                ReaderLog.Error("intent.RawPerHit", ex);
            }
        }

        if (rawOk && !perHitOk)
        {
            // Le pipeline brut a répondu : PerHit s'en déduit exactement (Math.Max(0, (int)num), AttackIntent.cs:92).
            perHit = Math.Max(0, (int)raw);
        }
        else if (!rawOk)
        {
            // Repli : garde l'invariant PerHit == Max(0, (int)RawPerHit).
            raw = perHit;
        }

        if (rawOk && Math.Max(0, (int)raw) != perHit)
        {
            ReaderLog.Limited($"reader: raw/perHit mismatch dealer={SafeDealerName(dealer)} raw={raw} perHit={perHit}");
        }

        var perTarget = new List<TargetHit>(players.Count);
        if (baseDamage is { } dmgForTargets)
        {
            for (int i = 0; i < players.Count; i++)
            {
                Player p = players[i];
                if (p == null)
                {
                    continue;
                }
                try
                {
                    decimal value = Hook.ModifyDamage(
                        p.RunState, p.Creature.CombatState, p.Creature, dealer, dmgForTargets,
                        ValueProp.Move, null, ModifyDamageHookType.All, CardPreviewMode.None,
                        out IEnumerable<AbstractModel> _);
                    perTarget.Add(new TargetHit(p.NetId, Math.Max(0, (int)value)));
                }
                catch (Exception ex)
                {
                    // Entrée absente = inconnue (jamais un 0 inventé).
                    ReaderLog.Error("intent.PerTarget", ex);
                }
            }
        }

        // Sans dégâts bruts fiables, les variantes Weak/Vulnerable ne sont pas calculables.
        decimal? weak = rawOk ? ReadWeakMultiplier(dealer, me) : null;
        decimal? vulnerable = rawOk ? ReadVulnerableMultiplier(dealer, me) : null;

        return new IntentSnapshot(kind, perHit, repeats, perHit * repeats, label)
        {
            RawPerHit = raw,
            WeakMultiplier = weak,
            VulnerableMultiplier = vulnerable,
            PerTarget = perTarget,
        };
    }

    /// <summary>Appel identique à AttackIntent.cs:84-93 (cible = créature du joueur local).</summary>
    private static decimal ModifyDamage(Player me, Creature dealer, decimal damage)
    {
        return Hook.ModifyDamage(
            me.RunState, me.Creature.CombatState, me.Creature, dealer, damage,
            ValueProp.Move, null, ModifyDamageHookType.All, CardPreviewMode.None,
            out IEnumerable<AbstractModel> _);
    }

    /// <summary>
    /// Miroir de WeakPower.ModifyDamageMultiplicative (Models/Powers/WeakPower.cs:24-46), valeurs LUES.
    /// null si le dealer porte déjà Weak ou si la lecture échoue.
    /// </summary>
    private static decimal? ReadWeakMultiplier(Creature dealer, Player me)
    {
        try
        {
            if (dealer.GetPower<WeakPower>() != null)
            {
                return null;
            }
            if (!ValueProp.Move.IsPoweredAttack())
            {
                return 1m;
            }
            Creature target = me.Creature;
            decimal m = ModelDb.Power<WeakPower>().DynamicVars["DamageDecrease"].BaseValue;
            PaperKrane? paperKrane = target.Player?.GetRelic<PaperKrane>();
            if (paperKrane != null)
            {
                m = paperKrane.ModifyWeakMultiplier(target, m, ValueProp.Move, dealer, null);
            }
            DebilitatePower? debilitate = dealer.GetPower<DebilitatePower>();
            if (debilitate != null)
            {
                // WeakPower.cs passe le dealer comme premier argument : reproduit tel quel.
                m = debilitate.ModifyWeakMultiplier(dealer, m, ValueProp.Move, dealer, null);
            }
            return m;
        }
        catch (Exception ex)
        {
            ReaderLog.Error("intent.WeakMultiplier", ex);
            return null;
        }
    }

    /// <summary>
    /// Miroir de VulnerablePower.ModifyDamageMultiplicative (Models/Powers/VulnerablePower.cs:27-57), valeurs LUES.
    /// null si le joueur local porte déjà Vulnerable ou si la lecture échoue.
    /// </summary>
    private static decimal? ReadVulnerableMultiplier(Creature dealer, Player me)
    {
        try
        {
            Creature target = me.Creature;
            if (target.GetPower<VulnerablePower>() != null)
            {
                return null;
            }
            if (!ValueProp.Move.IsPoweredAttack())
            {
                return 1m;
            }
            decimal m = ModelDb.Power<VulnerablePower>().DynamicVars["DamageIncrease"].BaseValue;
            PaperPhrog? paperPhrog = dealer.Player?.GetRelic<PaperPhrog>();
            if (paperPhrog != null)
            {
                m = paperPhrog.ModifyVulnerableMultiplier(target, m, ValueProp.Move, dealer, null);
            }
            CrueltyPower? cruelty = dealer.GetPower<CrueltyPower>();
            if (cruelty != null)
            {
                m = cruelty.ModifyVulnerableMultiplier(target, m, ValueProp.Move, dealer, null);
            }
            DebilitatePower? debilitate = target.GetPower<DebilitatePower>();
            if (debilitate != null)
            {
                m = debilitate.ModifyVulnerableMultiplier(target, m, ValueProp.Move, dealer, null);
            }
            return m;
        }
        catch (Exception ex)
        {
            ReaderLog.Error("intent.VulnerableMultiplier", ex);
            return null;
        }
    }

    private static string SafeDealerName(Creature dealer)
    {
        try
        {
            return dealer.Monster?.Title.GetFormattedText() ?? "?";
        }
        catch
        {
            return "?";
        }
    }
}
