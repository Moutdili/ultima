using Godot;
using Ultima.Core.Model;
using Ultima.UI.Layout;
using Ultima.UI.Theme;
using Ultima.UI.Widgets;

namespace Ultima.UI;

/// <summary>
/// Overlay ULTIMA (docs/17) : rail 8 px → panneau verre 260 px au survol, ancré au bord droit.
/// Ne lit PAS le jeu : reçoit des DTO via <see cref="Render"/>. Click-through : tous les nodes
/// en MouseFilter Ignore, survol testé dans <c>_Input</c> sans jamais consommer la souris.
/// Aucune animation en boucle, aucun traitement par frame : seul un Tween de 0.18 s à l'expansion.
/// </summary>
public partial class UltimaOverlay : Control
{
    public const int RailWidth = 8;
    public const int ExpandedWidth = 260;
    public const double ExpandDuration = 0.18;
    public const double DefaultCollapseDelaySec = 2.5;
    public const float RestAlpha = 0.30f;            // skill sts2-overlay-ui ; SPEC F7 : 0.25-0.35
    public const double FadeDuration = 0.15;         // fondu d'opacité au survol (skill : 150 ms)
    public const int Pad = 10;
    public const int ContentWidth = ExpandedWidth - RailWidth - 2 * Pad;
    public const int CriticalHpPercent = 30;
    private const int PastilleHeight = 14, PastilleGap = 6, RailPad = 4;
    public const int RailHeight = 2 * RailPad + 3 * PastilleHeight + 2 * PastilleGap;
    private const string GlassShaderResource = "Ultima.Shaders.glass_blur.gdshader";
    private const int MenuDetail = 0, MenuHide = 1;

    public float BlurAmount { get; set; } = 2.0f;
    public double CollapseDelaySec { get; set; } = DefaultCollapseDelaySec;
    public event Action? PingRequested;

    private Control? _root, _clip, _rail;
    private ShaderMaterial? _glassMat;
    private ColorRect? _glass;
    private VBoxContainer? _content, _enemies;
    private Label? _header, _vitals, _notice, _turn, _turnBy;
    private ThreatList? _threats;
    private PriorityStrip? _priority;
    private readonly List<EnemyRow> _rows = new();
    private readonly StyleBoxFlat[] _pastilles = new StyleBoxFlat[3];
    private Godot.Timer? _collapseTimer;
    private PopupMenu? _menu;
    private Tween? _tween, _fadeTween;
    private float _fadeTarget = RestAlpha;
    private Viewport? _viewport;

    private CombatSnapshot? _snapshot;
    private TurnForecast? _forecast;
    private IReadOnlyList<PriorityAlert> _alerts = Array.Empty<PriorityAlert>();
    private IReadOnlyList<Rect2> _avoid = Array.Empty<Rect2>();
    private bool _built, _hidden, _detail, _expanded, _hovered, _lethal;
    private float _progress, _expandedHeight = RailHeight, _rightX, _topY, _placementAlpha = 1f;

    /// <summary>Rendu sur événement (au plus 1×/frame). Réutilise les nodes ; liste reconstruite
    /// seulement si le nombre d'ennemis change. snapshot null → état vide explicite.</summary>
    public void Render(CombatSnapshot? snapshot, TurnForecast? forecast, IReadOnlyList<PriorityAlert> alerts)
    {
        try
        {
            _snapshot = snapshot;
            _forecast = forecast;
            _alerts = alerts ?? Array.Empty<PriorityAlert>();
            if (_built) Apply();
        }
        catch (Exception ex) { ModEntry.Write($"ui Render: {ex}"); }
    }

    /// <summary>Zones (coordonnées globales) que le panneau ne doit jamais couvrir.</summary>
    public void SetAvoidRects(IReadOnlyList<Rect2> globalRects)
    {
        try
        {
            _avoid = globalRects ?? Array.Empty<Rect2>();
            if (_built) Relayout();
        }
        catch (Exception ex) { ModEntry.Write($"ui SetAvoidRects: {ex}"); }
    }

    public override void _Ready()
    {
        try
        {
            MouseFilter = MouseFilterEnum.Ignore;
            SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            // Le jeu consomme les InputEvent (SetInputAsHandled) avant notre _UnhandledInput :
            // les touches n'arrivaient jamais. On lit l'état clavier chaque frame (3 appels,
            // coût négligeable) au lieu de dépendre du routage d'événements.
            SetProcess(true);
            SetPhysicsProcess(false);
            Build();
            _viewport = GetViewport();
            _viewport.SizeChanged += OnViewportSizeChanged;
            _built = true;
            Apply();
            ModEntry.Write($"ui overlay ready (blur={BlurAmount}, collapse={CollapseDelaySec}s)");
        }
        catch (Exception ex) { ModEntry.Write($"ui _Ready: {ex}"); }
    }

    public override void _ExitTree()
    {
        try
        {
            if (_viewport != null) _viewport.SizeChanged -= OnViewportSizeChanged;
            _tween?.Kill();
            _fadeTween?.Kill();
        }
        catch (Exception ex) { ModEntry.Write($"ui _ExitTree: {ex}"); }
    }

    /// <summary>
    /// Survol : ne marque JAMAIS l'événement comme traité. Seule exception (charte §4, menu) :
    /// un clic DROIT dans la zone de l'overlay est consommé et ouvre le menu contextuel.
    /// </summary>
    public override void _Input(InputEvent @event)
    {
        try
        {
            if (_root == null || _hidden) return;
            if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Right } mb)
            {
                if (!_root.GetGlobalRect().HasPoint(_root.GetGlobalMousePosition())) return;
                GetViewport()?.SetInputAsHandled();
                if (mb.Pressed) OpenMenu();
                return;
            }
            if (@event is not InputEventMouseMotion) return;
            bool inside = _root.GetGlobalRect().HasPoint(_root.GetGlobalMousePosition());
            if (inside == _hovered) return;
            _hovered = inside;
            if (inside)
            {
                _collapseTimer?.Stop();
                Expand();
            }
            else if (_expanded)
            {
                _collapseTimer?.Start(Math.Max(0.05, CollapseDelaySec));
            }
        }
        catch (Exception ex) { ModEntry.Write($"ui _Input: {ex}"); }
    }

    public override void _Process(double delta)
    {
        try { PollKeys(); }
        catch (Exception ex) { ModEntry.Write($"ui _Process: {ex}"); }
    }

    private bool _f7Down, _f8Down, _f9Down;
    private bool _k1Down, _k2Down, _k3Down;

    /// <summary>
    /// F8 masquer, F9 détail, F7 ping — lus en polling avec détection de front (une seule
    /// action par appui). On ne consomme plus l'événement : le jeu ne voit pas ces touches
    /// aujourd'hui, et les consommer depuis _Input risquerait de casser un raccourci du jeu.
    /// </summary>
    private void PollKeys()
    {
        // MAJ + 1/2/3 : les chiffres seuls sont mappés par le jeu (NInputManager), donc on
        // exige Shift pour ne jamais marcher sur un raccourci existant.
        bool shift = Input.IsPhysicalKeyPressed(Key.Shift);
        bool k1 = shift && Input.IsPhysicalKeyPressed(Key.Key1);
        if (k1 && !_k1Down)
        {
            _detail = !_detail;
            Apply();
            ModEntry.Write($"ui key Maj+1 -> detail={_detail}");
        }
        _k1Down = k1;

        bool k2 = shift && Input.IsPhysicalKeyPressed(Key.Key2);
        if (k2 && !_k2Down)
        {
            ToggleHidden();
            ModEntry.Write($"ui key Maj+2 -> hidden={_hidden}");
        }
        _k2Down = k2;

        bool k3 = shift && Input.IsPhysicalKeyPressed(Key.Key3);
        if (k3 && !_k3Down)
        {
            PingRequested?.Invoke();
            ModEntry.Write("ui key Maj+3 -> ping");
        }
        _k3Down = k3;

        // Alias F7 / F8 / F9 conservés pour les claviers sans conflit.
        bool f8 = Input.IsPhysicalKeyPressed(Key.F8);
        if (f8 && !_f8Down)
        {
            ToggleHidden();
            ModEntry.Write($"ui key F8 -> hidden={_hidden}");
        }
        _f8Down = f8;

        bool f9 = Input.IsPhysicalKeyPressed(Key.F9);
        if (f9 && !_f9Down)
        {
            _detail = !_detail;
            Apply();
            ModEntry.Write($"ui key F9 -> detail={_detail}");
        }
        _f9Down = f9;

        bool f7 = Input.IsPhysicalKeyPressed(Key.F7);
        if (f7 && !_f7Down)
        {
            PingRequested?.Invoke();
            ModEntry.Write("ui key F7 -> ping");
        }
        _f7Down = f7;
    }

    private void Build()
    {
        _root = new Control { Name = "UltimaPanel", MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_root);
        BuildGlass(_root);

        _clip = new Control { MouseFilter = MouseFilterEnum.Ignore, ClipContents = true, Visible = false };
        _clip.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _root.AddChild(_clip);
        _content = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _content.AddThemeConstantOverride("separation", 4);
        _clip.AddChild(_content);

        var headerLine = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        headerLine.AddThemeConstantOverride("separation", 6);
        _header = UltimaFonts.NewLabel(FontRole.Title, 16, UltimaPalette.Cream, trim: false);
        _vitals = UltimaFonts.NewLabel(FontRole.Mono, 16, UltimaPalette.Cream); // chiffres PV : SCP
        _vitals.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        headerLine.AddChild(_header);
        headerLine.AddChild(_vitals);
        _enemies = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _enemies.AddThemeConstantOverride("separation", 2);
        _notice = UltimaFonts.NewLabel(FontRole.Body, 14, UltimaPalette.Orange);
        _notice.Text = OverlayText.ForecastUnavailable;
        _turn = UltimaFonts.NewLabel(FontRole.Mono, 14, UltimaPalette.Cream); // chiffres PV : SCP
        _turnBy = UltimaFonts.NewLabel(FontRole.Body, 14, UltimaPalette.Cream);
        _threats = new ThreatList();
        _priority = new PriorityStrip();
        var footer = UltimaFonts.NewLabel(FontRole.Body, 14, UltimaPalette.HalfTransparentCream);
        footer.Text = OverlayText.Footer;
        foreach (var c in new Control[] { headerLine, _enemies, _notice, _turn, _turnBy, _threats, _priority, footer })
            _content.AddChild(c);

        _rail = new Control { MouseFilter = MouseFilterEnum.Ignore, Size = new Vector2(RailWidth, RailHeight) };
        _root.AddChild(_rail);
        for (int i = 0; i < _pastilles.Length; i++)
        {
            _pastilles[i] = UltimaStyles.Badge(UltimaPalette.Gray);
            var p = new Panel
            {
                MouseFilter = MouseFilterEnum.Ignore,
                Position = new Vector2(0, RailPad + i * (PastilleHeight + PastilleGap)),
                Size = new Vector2(RailWidth, PastilleHeight),
            };
            p.AddThemeStyleboxOverride("panel", _pastilles[i]);
            _rail.AddChild(p);
        }

        BuildMenu();

        _collapseTimer = new Godot.Timer { OneShot = true, WaitTime = Math.Max(0.05, CollapseDelaySec) };
        _collapseTimer.Timeout += OnCollapseTimeout;
        AddChild(_collapseTimer);
    }

    /// <summary>Un seul ColorRect flouté (shader) derrière le contenu ; sinon repli StyleBoxFlat.</summary>
    private void BuildGlass(Control root)
    {
        Shader? shader = BlurAmount > 0f ? LoadGlassShader() : null;
        if (shader == null)
        {
            ModEntry.Write(BlurAmount > 0f
                ? "ui: shader verre introuvable -> repli StyleBoxFlat"
                : "ui: BlurAmount=0 -> repli StyleBoxFlat");
            var panel = new Panel { MouseFilter = MouseFilterEnum.Ignore };
            panel.AddThemeStyleboxOverride("panel", UltimaStyles.Glass());
            panel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            root.AddChild(panel);
            return;
        }
        // ombre DERRIÈRE le verre (Panel dédié, StyleBoxFlat ombre seule)
        var shadow = new Panel { MouseFilter = MouseFilterEnum.Ignore };
        shadow.AddThemeStyleboxOverride("panel", UltimaStyles.ShadowOnly());
        shadow.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        root.AddChild(shadow);

        _glassMat = new ShaderMaterial { Shader = shader };
        _glassMat.SetShaderParameter("blur_amount", Mathf.Clamp(BlurAmount, 0f, 6f));
        _glassMat.SetShaderParameter("corner_radius", (float)UltimaStyles.CornerRadius);
        // Color = repli visuel si le shader ne compile pas (sinon rectangle blanc) ; réécrit par COLOR.
        var glass = new ColorRect { MouseFilter = MouseFilterEnum.Ignore, Material = _glassMat, Color = UltimaPalette.GlassFill };
        glass.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        glass.Resized += OnGlassResized;   // rect_size du masque SDF : sur événement, pas par frame
        root.AddChild(glass);
        _glass = glass;
        var edge = new Panel { MouseFilter = MouseFilterEnum.Ignore };
        edge.AddThemeStyleboxOverride("panel", UltimaStyles.GlassEdge());
        edge.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        root.AddChild(edge);
    }

    /// <summary>Menu clic droit (charte §4) : « Détail (F9) » / « Masquer (F8) », style verre.</summary>
    private void BuildMenu()
    {
        _menu = new PopupMenu { Name = "UltimaMenu" };
        _menu.AddCheckItem("Détail (Maj+1)", MenuDetail);
        _menu.AddItem("Masquer (Maj+2)", MenuHide);
        _menu.AddThemeStyleboxOverride("panel", UltimaStyles.Glass());
        var font = UltimaFonts.Get(FontRole.Body);
        if (font != null) _menu.AddThemeFontOverride("font", font);
        _menu.AddThemeFontSizeOverride("font_size", UltimaFonts.MinFontSize);
        _menu.AddThemeColorOverride("font_color", UltimaPalette.Cream);
        _menu.AddThemeColorOverride("font_hover_color", UltimaPalette.Gold);
        _menu.IdPressed += OnMenuId;
        AddChild(_menu);
    }

    private void OpenMenu()
    {
        if (_menu == null || _root == null) return;
        _menu.SetItemChecked(_menu.GetItemIndex(MenuDetail), _detail);
        Vector2 at = _root.GetScreenTransform() * _root.GetLocalMousePosition();
        _menu.Position = new Vector2I((int)at.X, (int)at.Y);
        _menu.ResetSize();
        _menu.Popup();
    }

    private void OnMenuId(long id)
    {
        try
        {
            if (id == MenuDetail) { _detail = !_detail; Apply(); }
            else if (id == MenuHide) ToggleHidden();
        }
        catch (Exception ex) { ModEntry.Write($"ui menu: {ex}"); }
    }

    private void OnGlassResized()
    {
        try
        {
            if (_glass != null) _glassMat?.SetShaderParameter("rect_size", _glass.Size);
        }
        catch (Exception ex) { ModEntry.Write($"ui glass resized: {ex}"); }
    }

    private static Shader? LoadGlassShader()
    {
        try
        {
            using var stream = typeof(UltimaOverlay).Assembly.GetManifestResourceStream(GlassShaderResource);
            if (stream == null) return null;
            using var reader = new StreamReader(stream);
            return new Shader { Code = reader.ReadToEnd() };
        }
        catch (Exception ex)
        {
            ModEntry.Write($"ui: lecture shader echouee ({ex.Message})");
            return null;
        }
    }

}
