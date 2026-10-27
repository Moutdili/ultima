using Godot;
using Ultima.Core.Model;
using Ultima.UI.Layout;
using Ultima.UI.Theme;
using Ultima.UI.Widgets;

namespace Ultima.UI;

// Composition (niveaux 1/2), placement et expansion rail ↔ panneau. Voir UltimaOverlay.cs.
public partial class UltimaOverlay
{
    private void Apply()
    {
        if (!_built || _content == null) return;
        var s = _snapshot;
        var f = _forecast;
        bool waiting = s == null;

        _header!.Text = s == null ? OverlayText.Waiting : OverlayText.HeaderTurn(s);
        UltimaFonts.SetColor(_header, waiting ? UltimaPalette.HalfTransparentCream : UltimaPalette.Cream);
        _vitals!.Visible = s != null;
        if (s != null) _vitals.Text = OverlayText.HeaderVitals(s);

        BindEnemies(s, f);
        _notice!.Visible = !waiting && f == null;   // pas de létalité inventée

        // Niveau 2 uniquement (F9) : total du tour + co-op.
        bool showTurn = !waiting && _detail && f != null;
        _turn!.Visible = showTurn;
        string? by = showTurn ? OverlayText.TurnBy(f!) : null;
        _turnBy!.Visible = by != null;
        if (showTurn)
        {
            _turn.Text = OverlayText.Turn(f!);
            UltimaFonts.SetColor(_turn, f!.LethalHitIndex != null ? UltimaPalette.Red : UltimaPalette.Cream);
            if (by != null) _turnBy.Text = by;
        }
        _threats!.Bind(showTurn && f!.Players.Count > 1 ? f.Players : Array.Empty<PlayerThreat>());
        _priority!.Bind(waiting ? Array.Empty<PriorityAlert>() : _alerts, _detail);

        _lethal = !waiting && f?.LethalHitIndex != null;
        SetPastille(0, s != null && OverlayText.IsHpCritical(s, CriticalHpPercent) ? UltimaPalette.Red : UltimaPalette.Gray);
        SetPastille(1, _lethal ? UltimaPalette.Red : UltimaPalette.Gray);
        SetPastille(2, !waiting && _alerts.Count > 0 ? UltimaPalette.Gold : UltimaPalette.Gray);

        Relayout();
    }

    private void BindEnemies(CombatSnapshot? s, TurnForecast? f)
    {
        var enemies = s?.Enemies ?? Array.Empty<EnemySnapshot>();
        if (_rows.Count != enemies.Count)
        {
            while (_rows.Count > enemies.Count)
            {
                var last = _rows[^1];
                _rows.RemoveAt(_rows.Count - 1);
                _enemies!.RemoveChild(last); // hors du calcul de hauteur dès ce Render
                last.QueueFree();
            }
            while (_rows.Count < enemies.Count)
            {
                var row = new EnemyRow();
                _rows.Add(row);
                _enemies!.AddChild(row);
            }
        }
        for (int i = 0; i < enemies.Count; i++)
            _rows[i].Bind(enemies[i], FindForecast(f, enemies[i], i), _detail);
        _enemies!.Visible = enemies.Count > 0;
    }

    /// <summary>Appariement par CombatId ; par index seulement si l'un des deux n'a pas d'id.</summary>
    private static EnemyForecast? FindForecast(TurnForecast? f, EnemySnapshot enemy, int index)
    {
        if (f == null) return null;
        if (enemy.CombatId != 0)
        {
            for (int i = 0; i < f.Enemies.Count; i++)
                if (f.Enemies[i].CombatId == enemy.CombatId) return f.Enemies[i];
        }
        if (index < f.Enemies.Count && (enemy.CombatId == 0 || f.Enemies[index].CombatId == 0))
            return f.Enemies[index];
        return null;
    }

    private void SetPastille(int i, Color color)
    {
        if (_pastilles[i] != null && _pastilles[i].BgColor != color) _pastilles[i].BgColor = color;
    }

    /// <summary>Hauteur déployée + placement. Sur Render / SetAvoidRects / size_changed — jamais par frame.</summary>
    private void Relayout()
    {
        if (_root == null || _content == null) return;
        _content.Size = new Vector2(ContentWidth, 0); // se rétracte à sa taille minimale
        var area = GetViewportRect();
        float maxH = area.Size.Y * Placement.MaxHeightFraction;
        _expandedHeight = Mathf.Clamp(_content.Size.Y + 2 * Pad, RailHeight, Math.Max(RailHeight, maxH));
        var r = Placement.Compute(area, new Vector2(ExpandedWidth, _expandedHeight), _avoid);
        _rightX = area.End.X;
        _topY = r.Position.Y;
        _placementAlpha = r.Alpha;
        ApplyProgress(_progress);
        UpdateAlpha();

        if (!_loggedLayout)
        {
            _loggedLayout = true;
            ModEntry.Write($"ui layout: viewport={area.Size} pos={_root.GlobalPosition} " +
                           $"size={_root.Size} alpha={TargetAlpha():F2} parent={GetParent()?.Name}");
        }
    }

    /// <summary>t ∈ [0,1] : 0 = rail, 1 = déployé. S'étend vers la gauche, bord droit fixe.</summary>
    private void ApplyProgress(float t)
    {
        if (_root == null || _content == null || _rail == null || _clip == null) return;
        _progress = t;
        float w = Mathf.Lerp(RailWidth, ExpandedWidth, t);
        float h = Mathf.Lerp(RailHeight, _expandedHeight, t);
        var size = new Vector2(w, h);
        _root.Size = size;
        _root.GlobalPosition = new Vector2(_rightX - w, _topY);
        _rail.Position = new Vector2(w - RailWidth, 0);
        _clip.Visible = t > 0f;
        // Perf (QA v2) : un shader qui lit l'écran force une copie plein écran à CHAQUE frame,
        // même sur 8 px. Au repos le rail garde le remplissage plat du ColorRect (GlassFill),
        // le flou n'est actif que pendant l'ouverture et à l'état déployé.
        if (_glass != null)
        {
            var wanted = t > 0f ? _glassMat : null;
            if (!ReferenceEquals(_glass.Material, wanted)) _glass.Material = wanted;
        }
        // contenu fixe à l'écran, révélé par le clip (rien ne glisse sous le regard)
        _content.Position = new Vector2(w - RailWidth - Pad - ContentWidth, Pad);
    }

    private void Expand()
    {
        if (_expanded || _hidden) return;
        _expanded = true;
        UpdateAlpha(animate: true);
        StartTween(1f);
    }

    private void Collapse()
    {
        if (!_expanded) return;
        _expanded = false;
        UpdateAlpha(animate: true);
        StartTween(0f);
    }

    private void StartTween(float target)
    {
        _tween?.Kill();
        _tween = CreateTween();
        _tween.TweenMethod(Callable.From<float>(OnTweenStep), _progress, target, ExpandDuration)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        _tween.TweenCallback(Callable.From(OnTweenDone));
    }

    private void OnTweenStep(float t)
    {
        try { ApplyProgress(t); }
        catch (Exception ex) { ModEntry.Write($"ui tween: {ex}"); }
    }

    private void OnTweenDone()
    {
        try { UpdateAlpha(); }
        catch (Exception ex) { ModEntry.Write($"ui tween done: {ex}"); }
    }

    private float TargetAlpha()
    {
        // G5 : déployé = toujours 1.0, même en repli de placement (à 0.35 le texte tombait
        // à 2.99:1, QA docs/18 §12). Seul le rail au repos peut baisser.
        if (_expanded) return 1f;
        return Math.Min(_lethal ? 1f : RestAlpha, _placementAlpha);
    }

    /// <summary>
    /// Repos 0.30 ; 1.0 si déployé ou si létal (statique, jamais de clignotement).
    /// <paramref name="animate"/> : fondu one-shot 150 ms ease-out (survol / retour au repli) ;
    /// sinon valeur posée directement (Render, placement).
    /// </summary>
    private void UpdateAlpha(bool animate = false)
    {
        if (_root == null) return;
        float a = TargetAlpha();
        if (animate)
        {
            _fadeTween?.Kill();
            _fadeTarget = a;
            _fadeTween = CreateTween();
            _fadeTween.TweenProperty(_root, "modulate:a", a, FadeDuration)
                .SetTrans(Tween.TransitionType.Cubic)
                .SetEase(Tween.EaseType.Out);
            return;
        }
        if (_fadeTween != null && _fadeTween.IsValid() && _fadeTween.IsRunning())
        {
            if (Mathf.IsEqualApprox(_fadeTarget, a)) return; // le fondu en cours y va déjà
            _fadeTween.Kill();
        }
        if (!Mathf.IsEqualApprox(_root.Modulate.A, a)) _root.Modulate = _root.Modulate with { A = a };
    }

    private void ToggleHidden()
    {
        _hidden = !_hidden;
        if (_root != null) _root.Visible = !_hidden;
        if (!_hidden) return;
        _collapseTimer?.Stop();
        _tween?.Kill();
        _fadeTween?.Kill();
        _expanded = false;
        _hovered = false;
        ApplyProgress(0f);
        UpdateAlpha();
    }

    private void OnCollapseTimeout()
    {
        try { if (!_hovered) Collapse(); }
        catch (Exception ex) { ModEntry.Write($"ui collapse: {ex}"); }
    }

    private void OnViewportSizeChanged()
    {
        try { Relayout(); }
        catch (Exception ex) { ModEntry.Write($"ui size_changed: {ex}"); }
    }
}
