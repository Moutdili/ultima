using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace Ultima.Patches;

/// <summary>
/// Zones que l'overlay ne doit jamais couvrir (SPEC F7) : intents, corps/PV des créatures, main,
/// piles, bouton de fin de tour. Lecture de géométrie de nodes uniquement (read-only).
/// Sources vérifiées v0.107.1 : NCombatUi.EndTurnButton (:60), DrawPile/DiscardPile/ExhaustPile
/// (:64-68), Hand (:70) ; NPlayerHand.ActiveHolders (:182) ; NCardHolder.CardNode (:49) ;
/// NCard.defaultSize (:54) ; NCombatRoom.CreatureNodes (:90) ; NCreature.Hitbox (:83),
/// IntentContainer (:99).
/// </summary>
internal static class AvoidRects
{
    // La barre de PV et les powers sont dessinés SOUS la hitbox du corps : on étend la zone
    // vers le bas d'une fraction de sa hauteur (marge de mise en page, pas une valeur de jeu).
    private const float BelowHitboxFraction = 0.35f;

    public static IReadOnlyList<Rect2> Collect(NCombatUi ui)
    {
        var rects = new List<Rect2>(32);
        try
        {
            // Un conteneur plein écran (ex. la main ancrée en full rect) rendrait tout placement
            // impossible : au-delà de 50 % du viewport on prend ses éléments un par un.
            var vp = ui.GetViewportRect().Size;
            _maxArea = vp.X * vp.Y * 0.5f;

            AddControl(rects, ui.EndTurnButton);
            AddControl(rects, ui.DrawPile as Control);
            AddControl(rects, ui.DiscardPile as Control);
            AddControl(rects, ui.ExhaustPile as Control);
            if (!AddControl(rects, ui.Hand)) AddHandCards(rects, ui);

            var room = NCombatRoom.Instance;
            if (room != null && IsLive(room))
            {
                foreach (var creature in room.CreatureNodes)
                {
                    if (!IsLive(creature)) continue;
                    AddCreatureBody(rects, creature);
                    var intents = creature.IntentContainer;
                    AddControl(rects, intents);
                    if (intents == null || !IsLive(intents)) continue;
                    foreach (var child in intents.GetChildren())
                        if (child is Control c) AddControl(rects, c);
                }
            }
        }
        catch (Exception ex)
        {
            ModEntry.Write($"avoid: collect failed {ex.GetType().Name}: {ex.Message}");
        }
        return rects;
    }

    private static float _maxArea = float.MaxValue;

    /// <summary>Ajoute la zone du control ; false si absent, invisible ou trop grand (ignoré).</summary>
    private static bool AddControl(List<Rect2> rects, Control? c)
    {
        if (c == null || !IsLive(c) || !c.IsVisibleInTree()) return false;
        var r = c.GetGlobalRect();
        var area = r.Size.X * r.Size.Y;
        if (area <= 0 || area > _maxArea) return false;
        rects.Add(r);
        return true;
    }

    /// <summary>
    /// Cartes de la main une par une. La racine d'un NCard est de taille nulle, visuel centré
    /// sur l'origine : zone = NCard.defaultSize à l'échelle globale, centrée sur la position.
    /// </summary>
    private static void AddHandCards(List<Rect2> rects, NCombatUi ui)
    {
        var hand = ui.Hand;
        if (hand == null || !IsLive(hand)) return;
        foreach (var holder in hand.ActiveHolders)
        {
            var card = holder?.CardNode;
            if (card == null || !IsLive(card) || !card.IsVisibleInTree()) continue;
            var size = NCard.defaultSize * card.GetGlobalTransform().Scale;
            rects.Add(new Rect2(card.GlobalPosition - size * 0.5f, size));
        }
    }

    private static void AddCreatureBody(List<Rect2> rects, NCreature creature)
    {
        var hitbox = creature.Hitbox;
        if (hitbox == null || !IsLive(hitbox) || !hitbox.IsVisibleInTree()) return;
        var r = hitbox.GetGlobalRect();
        if (r.Size.X <= 0 || r.Size.Y <= 0) return;
        rects.Add(r.GrowSide(Side.Bottom, r.Size.Y * BelowHitboxFraction));
    }

    private static bool IsLive(GodotObject? o) =>
        o != null && GodotObject.IsInstanceValid(o) && !(o is Node n && n.IsQueuedForDeletion());
}
