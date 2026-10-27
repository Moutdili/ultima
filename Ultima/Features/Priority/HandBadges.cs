using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using Ultima.Core.Model;
using Ultima.UI.Theme;

namespace Ultima.Features.Priority;

/// <summary>
/// F3 « badge sur la carte » : pastille « PRIO » sur chaque carte de la main locale dont la classe est en alerte.
/// Affichage seul : un PanelContainer enfant du NCard (autorisé) ; la carte n'est jamais modifiée
/// (taille, position, scale, modulate intacts) et aucun état de jeu n'est touché.
/// Pooling : les NCard sont recyclés (IPoolable, NCard.cs:32). Chaque badge posé est suivi ; il est retiré dès que
/// sa carte quitte <c>ActiveHolders</c> ou l'alerte, et <see cref="Clear"/> retire tout.
/// Appelé depuis CombatSession (CallDeferred) : aucun _Process, O(cartes en main), aucune animation.
/// </summary>
public static class HandBadges
{
    /// <summary>Nom unique de l'enfant posé sur le NCard (idempotence : jamais deux badges sur une carte).</summary>
    public const string BadgeName = "UltimaPrioBadge";

    private const string BadgeText = "PRIO";

    /// <summary>
    /// Taille en espace carte (300×422, NCard.defaultSize NCard.cs:54). La main affiche les cartes réduites :
    /// 20 px carte ≈ 13-16 px écran (≥ 12 px). À valider en jeu.
    /// </summary>
    private const int FontSize = 20;

    private const float PadX = 6f;
    private const float PadY = 1f;

    /// <summary>Retrait depuis le coin haut-droit, en px espace carte.</summary>
    private const float Inset = 8f;

    private struct Entry
    {
        public ulong Id;
        public NCard Card;
        public PanelContainer Badge;
        public string Cls;
        public int Gen;
    }

    private static readonly List<Entry> Badged = new();
    private static readonly HashSet<string> Wanted = new(StringComparer.Ordinal);
    private static readonly NodePath BadgePath = new(BadgeName);
    private static StyleBoxFlat? _style;
    private static int _gen;

    /// <summary>Pose un badge PRIO sur chaque carte de la main dont la classe figure dans localAlerts ; retire les autres.</summary>
    public static void Update(NCombatUi ui, IReadOnlyList<PriorityAlert> localAlerts)
    {
        try
        {
            Wanted.Clear();
            if (localAlerts != null)
            {
                for (var i = 0; i < localAlerts.Count; i++)
                {
                    var a = localAlerts[i];
                    if (a != null && !a.IsRemote && a.Source == PrioritySource.LocalHand && !string.IsNullOrEmpty(a.CardClass))
                        Wanted.Add(a.CardClass);
                }
            }

            _gen++;
            if (Wanted.Count > 0 && IsAlive(ui))
            {
                var hand = ui.Hand;
                if (IsAlive(hand))
                {
                    var holders = hand.ActiveHolders;
                    for (var i = 0; i < holders.Count; i++)
                    {
                        var holder = holders[i];
                        if (!IsAlive(holder)) continue;
                        var card = holder.CardNode;
                        if (card == null || !IsAlive(card)) continue;
                        var model = card.Model;
                        if (model == null) continue;
                        var cls = model.GetType().Name;
                        if (Wanted.Contains(cls)) Mark(card, cls);
                    }
                }
            }

            // Tout badge non revu à cette génération : carte jouée, défaussée, recyclée, ou alerte levée.
            for (var i = Badged.Count - 1; i >= 0; i--)
            {
                var e = Badged[i];
                if (e.Gen == _gen) continue;
                Badged.RemoveAt(i);
                Detach(e.Card, e.Badge);
                ModEntry.Write($"badge: -{e.Cls}");
            }
        }
        catch (Exception ex)
        {
            ModEntry.Write($"badge: update failed {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Retire tous les badges posés par ULTIMA (fin de combat / session).</summary>
    public static void Clear(NCombatUi ui)
    {
        try
        {
            for (var i = Badged.Count - 1; i >= 0; i--)
            {
                var e = Badged[i];
                Badged.RemoveAt(i);
                Detach(e.Card, e.Badge);
                ModEntry.Write($"badge: -{e.Cls}");
            }

            // Filet : badge non suivi resté sur une carte de la main (ne devrait jamais arriver).
            if (!IsAlive(ui)) return;
            var hand = ui.Hand;
            if (!IsAlive(hand)) return;
            var holders = hand.ActiveHolders;
            for (var i = 0; i < holders.Count; i++)
            {
                var holder = holders[i];
                if (!IsAlive(holder)) continue;
                var card = holder.CardNode;
                if (card == null || !IsAlive(card)) continue;
                var orphan = card.GetNodeOrNull<PanelContainer>(BadgePath);
                if (orphan == null) continue;
                Detach(card, orphan);
                ModEntry.Write($"badge: -{card.Model?.GetType().Name ?? "?"}");
            }
        }
        catch (Exception ex)
        {
            ModEntry.Write($"badge: clear failed {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void Mark(NCard card, string cls)
    {
        var id = card.GetInstanceId();
        for (var i = 0; i < Badged.Count; i++)
        {
            var e = Badged[i];
            if (e.Id != id) continue;
            if (!IsAlive(e.Badge) || e.Badge.GetParent() != card) e.Badge = Attach(card);
            if (e.Cls != cls)
            {
                // Même node, autre carte (transformation / recyclage) : on garde le badge, on journalise le changement.
                ModEntry.Write($"badge: -{e.Cls}");
                ModEntry.Write($"badge: +{cls}");
                e.Cls = cls;
            }
            e.Gen = _gen;
            Badged[i] = e;
            return;
        }

        Badged.Add(new Entry { Id = id, Card = card, Badge = Attach(card), Cls = cls, Gen = _gen });
        ModEntry.Write($"badge: +{cls}");
    }

    /// <summary>Badge existant (cherché par nom) ou nouveau : jamais deux sur la même carte.</summary>
    private static PanelContainer Attach(NCard card)
    {
        var existing = card.GetNodeOrNull<PanelContainer>(BadgePath);
        if (existing != null)
        {
            if (IsAlive(existing)) return existing;
            card.RemoveChild(existing); // libération en cours : libère le nom avant d'en poser un neuf
        }

        var badge = CreateBadge();
        card.AddChild(badge);
        return badge;
    }

    /// <summary>Retire le badge sans toucher à la carte ; carte en cours de libération → le badge part avec elle.</summary>
    private static void Detach(NCard card, PanelContainer badge)
    {
        try
        {
            if (!IsAlive(card) || !IsAlive(badge)) return;
            badge.Visible = false;
            if (badge.GetParent() == card) card.RemoveChild(badge);
            badge.QueueFree();
        }
        catch (Exception ex)
        {
            ModEntry.Write($"badge: detach failed {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static PanelContainer CreateBadge()
    {
        // Visuel centré sur l'origine du NCard (card.tscn : racine et CardContainer à (0,0), taille 0) :
        // coin haut-droit = (+w/2, -h/2). Bord droit et bord haut fixés, le badge grandit vers la gauche et le bas.
        var half = NCard.defaultSize * 0.5f;
        var right = half.X - Inset;
        var top = -half.Y + Inset;

        var badge = new PanelContainer
        {
            Name = BadgeName,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            FocusMode = Control.FocusModeEnum.None,
            ZIndex = 1, // relatif : au-dessus de la carte et de ses voisines en main
            OffsetLeft = right,
            OffsetRight = right,
            OffsetTop = top,
            OffsetBottom = top,
            GrowHorizontal = Control.GrowDirection.Begin,
            GrowVertical = Control.GrowDirection.End,
        };
        badge.AddThemeStyleboxOverride("panel", Style());

        var label = UltimaFonts.NewLabel(FontRole.Title, FontSize, UltimaPalette.Cream, trim: false);
        label.Text = BadgeText;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        badge.AddChild(label);
        return badge;
    }

    /// <summary>
    /// Pastille UltimaStyles.Badge, fond verre opaque (#192930) pour la lisibilité du texte cream,
    /// liseré gold 1 px = l'accent (docs/17 §2 : pas de grande surface dorée). Partagé par tous les badges.
    /// </summary>
    private static StyleBoxFlat Style()
    {
        if (_style != null) return _style;
        var s = UltimaStyles.Badge(UltimaPalette.GlassTint);
        s.BorderColor = UltimaPalette.Gold;
        s.SetBorderWidthAll(UltimaStyles.BorderWidth);
        s.ContentMarginLeft = PadX;
        s.ContentMarginRight = PadX;
        s.ContentMarginTop = PadY;
        s.ContentMarginBottom = PadY;
        _style = s;
        return s;
    }

    private static bool IsAlive(GodotObject? o) =>
        o != null && GodotObject.IsInstanceValid(o) && !o.IsQueuedForDeletion();
}
