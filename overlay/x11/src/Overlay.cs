using System.Diagnostics;
using static Ultima.Overlay.X11.Cairo;

namespace Ultima.Overlay.X11;

/// <summary>
/// Fenetre X11 override-redirect (aucun gestionnaire de fenetres), visuel ARGB 32 bits,
/// region d'entree vide (XFixes ShapeInput) : aucun clic ni survol n'est jamais pris au jeu.
/// Aucun rendu en boucle : on ne dessine que sur Expose, toggle et pendant les 180 ms d'expansion.
/// </summary>
internal sealed unsafe class Overlay : IDisposable
{
    // Unites LOGIQUES (px GNOME). Converties en px X par Scale (Xwayland « native scaling » :
    // sur la machine de Mio, Xft.dpi=192 -> 1 px logique = 2 px X ; ecran 1920x1200 a 125 %).
    public const int RailWidth = 14;    // lecon v1 : 8 px invisible -> >= 14 px
    public const int PanelWidth = 260;  // charte §4
    public const int Height = 232;
    public const int AnimMs = 180;      // charte §4 : 150-220 ms, ease-out

    private readonly nint _dpy;
    private readonly int _screen;
    private readonly nuint _root;
    private readonly XVisualInfo _vi;
    private readonly int _screenW, _screenH;
    private readonly int _top;
    private readonly nint _xsurf;     // surface cairo de la fenetre
    private readonly nint _buffer;    // double tampon du rail (evite tout scintillement)
    private nint _panelCache;         // panneau deploye, rendu une seule fois
    private readonly Action<string> _log;
    public double Scale { get; }
    private int Px(double logical) => (int)Math.Round(logical * Scale);

    public nuint Window { get; }
    public int Fd { get; }
    public bool Expanded { get; private set; }
    public bool Visible { get; private set; } = true;
    public int CurrentWidth { get; private set; }

    // animation (uniquement pendant une transition, jamais en boucle)
    private int _animFrom, _animTo, _frames;
    private readonly Stopwatch _anim = new();
    public bool Animating => _anim.IsRunning;

    private static readonly (int Cost, string Name, Rgba Rarity, int Count)[] DeckLines =
    {
        // Contenu factice (noms FR issus de pck/localization/fra/cards.json).
        (1, "Frappe", UltimaPalette.RarityCommon, 5),
        (1, "Défense", UltimaPalette.RarityCommon, 4),
        (2, "Heurt", UltimaPalette.RarityCommon, 1),
        (1, "Même pas mal", UltimaPalette.RarityUncommon, 1),
        (1, "Enflammer", UltimaPalette.RarityRare, 1),
    };

    public Overlay(Action<string> log, bool startExpanded)
    {
        _log = log;
        _dpy = X.XOpenDisplay(0);
        if (_dpy == 0) throw new InvalidOperationException("XOpenDisplay a echoue (DISPLAY non defini ?)");
        _screen = X.XDefaultScreen(_dpy);
        _root = X.XRootWindow(_dpy, _screen);
        _screenW = X.XDisplayWidth(_dpy, _screen);
        _screenH = X.XDisplayHeight(_dpy, _screen);
        Fd = X.XConnectionNumber(_dpy);
        Scale = DetectScale(_dpy, out var scaleSrc);
        _top = Px(int.TryParse(Environment.GetEnvironmentVariable("ULTIMA_X11_TOP"), out var t) ? t : 120);

        if (X.XMatchVisualInfo(_dpy, _screen, 32, X.TrueColor, out _vi) == 0)
            throw new InvalidOperationException("Pas de visuel ARGB 32 bits (TrueColor depth 32)");
        if (XFixes.XFixesQueryExtension(_dpy, out _, out _) == 0)
            throw new InvalidOperationException("Extension XFixes absente");
        XFixes.XFixesQueryVersion(_dpy, out var fxMaj, out var fxMin);

        Expanded = startExpanded;
        CurrentWidth = startExpanded ? PanelWidth : RailWidth;

        var attrs = new XSetWindowAttributes
        {
            OverrideRedirect = 1,                 // pas de WM : pas de decoration, pas de focus, pas de taskbar
            Colormap = X.XCreateColormap(_dpy, _root, _vi.Visual, 0),
            BackgroundPixel = 0,                  // ARGB 0 = transparent
            BorderPixel = 0,
            SaveUnder = 0,
            EventMask = X.ExposureMask | X.StructureNotifyMask, // AUCUN masque clavier/souris
        };
        Window = X.XCreateWindow(_dpy, _root, _screenW - Px(CurrentWidth), _top, (uint)Px(CurrentWidth), (uint)Px(Height), 0,
            _vi.Depth, X.InputOutput, _vi.Visual,
            X.CWOverrideRedirect | X.CWColormap | X.CWBackPixel | X.CWBorderPixel | X.CWSaveUnder | X.CWEventMask,
            ref attrs);

        var nameBytes = X.Utf8("ULTIMA");
        fixed (byte* p = nameBytes) X.XStoreName(_dpy, Window, p);
        var cls = "ultima-x11\0Ultima\0"u8.ToArray();
        fixed (byte* p = cls) X.XChangeProperty(_dpy, Window, X.Atom(_dpy, "WM_CLASS"), X.XA_STRING, 8, X.PropModeReplace, p, cls.Length);
        // Declaration explicite « jamais focusable » (redondant avec override-redirect, mais lisible par xprop).
        var hints = new XWMHints { Flags = 1 /* InputHint */, Input = 0 };
        X.XSetWMHints(_dpy, Window, ref hints);
        // Ne pas laisser le compositeur rediriger/court-circuiter de facon inattendue : hint neutre.
        uint pid = (uint)Environment.ProcessId;
        X.XChangeProperty(_dpy, Window, X.Atom(_dpy, "_NET_WM_PID"), X.XA_CARDINAL, 32, X.PropModeReplace, (byte*)&pid, 1);

        ApplyEmptyInputRegion();

        _xsurf = cairo_xlib_surface_create(_dpy, Window, _vi.Visual, Px(CurrentWidth), Px(Height));
        _buffer = cairo_image_surface_create(FormatArgb32, Px(RailWidth), Px(Height));

        LoadGameFonts();
        X.XMapWindow(_dpy, Window);
        X.XFlush(_dpy);
        _log($"window=0x{Window:x} visual=0x{_vi.VisualId:x} depth={_vi.Depth} override_redirect=1 " +
             $"screen={_screenW}x{_screenH} scale={Scale:0.###} ({scaleSrc}) geom={Px(CurrentWidth)}x{Px(Height)}+{_screenW - Px(CurrentWidth)}+{_top} xfixes={fxMaj}.{fxMin}");
        _log($"input_region_rects={InputRegionRectCount()} (0 = click-through total)");
    }

    /// <summary>Region d'entree vide via XFixes : le serveur X ne livre aucun evenement pointeur a cette fenetre.</summary>
    private void ApplyEmptyInputRegion()
    {
        var region = XFixes.XFixesCreateRegion(_dpy, null, 0);
        XFixes.XFixesSetWindowShapeRegion(_dpy, Window, XFixes.ShapeInput, 0, 0, region);
        XFixes.XFixesDestroyRegion(_dpy, region);
    }

    /// <summary>Relecture cote serveur (extension SHAPE) du nombre de rectangles de la region d'entree.</summary>
    public int InputRegionRectCount()
    {
        X.XSync(_dpy, 0);
        var rects = XShape.XShapeGetRectangles(_dpy, Window, XFixes.ShapeInput, out var n, out _);
        if (rects != null) X.XFree(rects);
        return n;
    }

    private void LoadGameFonts()
    {
        var dir = Environment.GetEnvironmentVariable("ULTIMA_FONTS_DIR")
                  ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "sts2/game/pck/fonts");
        var ok = new List<string>();
        foreach (var f in new[] { "kreon_bold.ttf", "kreon_regular.ttf", "source_code_pro_semibold.ttf" })
            if (Fc.AddFile(Path.Combine(dir, f))) ok.Add(f);
        _log(ok.Count == 3 ? $"polices du jeu chargees depuis {dir}" : $"polices du jeu partielles ({string.Join(",", ok)}) -> repli fontconfig");
    }

    // ------------------------------------------------------------------ etat

    public void SetExpanded(bool expanded)
    {
        if (expanded == Expanded && !Animating) return;
        Expanded = expanded;
        _animFrom = CurrentWidth;
        _animTo = expanded ? PanelWidth : RailWidth;
        _frames = 0;
        _anim.Restart();
        Tick();
    }

    public void SetVisible(bool visible)
    {
        if (visible == Visible) return;
        Visible = visible;
        if (visible) X.XMapWindow(_dpy, Window); else X.XUnmapWindow(_dpy, Window);
        X.XFlush(_dpy);
    }

    /// <summary>Avance l'animation d'une image. Retourne le delai (ms) avant la prochaine, ou -1 si fini.</summary>
    public int Tick()
    {
        if (!Animating) return -1;
        var t = Math.Min(1.0, _anim.Elapsed.TotalMilliseconds / AnimMs);
        var eased = 1 - Math.Pow(1 - t, 3); // ease-out cubique, sans overshoot
        Resize((int)Math.Round(_animFrom + (_animTo - _animFrom) * eased));
        _frames++;
        if (t >= 1.0)
        {
            _anim.Stop();
            _log($"transition {_animFrom}->{_animTo}px en {_anim.Elapsed.TotalMilliseconds:F0} ms, {_frames} images");
            return -1;
        }
        return 16;
    }

    private void Resize(int w)
    {
        if (w != CurrentWidth)
        {
            CurrentWidth = w;
            X.XMoveResizeWindow(_dpy, Window, _screenW - Px(w), _top, (uint)Px(w), (uint)Px(Height));
            cairo_xlib_surface_set_size(_xsurf, Px(w), Px(Height));
        }
        Paint();
    }

    public void PumpX()
    {
        var expose = false;
        while (X.XPending(_dpy) > 0)
        {
            X.XNextEvent(_dpy, out var ev);
            if (ev.Type == X.Expose) expose = true;
        }
        if (expose) Paint();
    }

    // ------------------------------------------------------------------ rendu

    public void Paint()
    {
        // Le panneau est rendu UNE fois puis mis en cache : pendant l'expansion, chaque image n'est qu'un blit
        // rogne a la largeur courante (le contenu suit le bord gauche ; le bord droit est hors ecran).
        nint src;
        if (CurrentWidth > RailWidth)
        {
            if (_panelCache == 0)
            {
                _panelCache = cairo_image_surface_create(FormatArgb32, Px(PanelWidth), Px(Height));
                Render(_panelCache, PanelWidth, Scale);
            }
            src = _panelCache;
        }
        else
        {
            Render(_buffer, RailWidth, Scale);
            src = _buffer;
        }
        var cr = cairo_create(_xsurf);
        cairo_set_operator(cr, OperatorSource);
        cairo_set_source_surface(cr, src, 0, 0);
        cairo_paint(cr);
        cairo_destroy(cr);
        cairo_surface_flush(_xsurf);
        X.XFlush(_dpy);
    }

    /// <summary>Ecrit le rendu interne (pixels exacts envoyes au serveur X) en PNG. Ce n'est PAS une capture composee.</summary>
    public void Snapshot(string path)
    {
        var img = cairo_image_surface_create(FormatArgb32, Px(CurrentWidth), Px(Height));
        Render(img, CurrentWidth, Scale);
        WritePng(img, path);
        cairo_surface_destroy(img);
    }

    private static void Set(nint cr, Rgba c, double alphaMul = 1.0) =>
        cairo_set_source_rgba(cr, c.R, c.G, c.B, c.A * alphaMul);

    /// <summary>Rectangle arrondi uniquement a gauche (le bord droit colle au bord de l'ecran).</summary>
    private static void LeftRounded(nint cr, double x, double y, double w, double h, double r)
    {
        cairo_new_path(cr);
        cairo_move_to(cr, x + w, y);
        cairo_line_to(cr, x + w, y + h);
        cairo_line_to(cr, x + r, y + h);
        cairo_arc(cr, x + r, y + h - r, r, Math.PI / 2, Math.PI);
        cairo_line_to(cr, x, y + r);
        cairo_arc(cr, x + r, y + r, r, Math.PI, 3 * Math.PI / 2);
        cairo_close_path(cr);
    }

    private static void Render(nint surface, int w, double scale)
    {
        var cr = cairo_create(surface);
        cairo_set_operator(cr, OperatorClear);
        cairo_paint(cr);
        cairo_set_operator(cr, OperatorOver);
        cairo_scale(cr, scale, scale);
        cairo_rectangle(cr, 0, 0, w, Height);
        cairo_clip(cr);

        var radius = w <= RailWidth ? 6.0 : 10.0;
        // verre : teinte #192930 a 0.78
        LeftRounded(cr, 0, 0, w, Height, radius);
        Set(cr, UltimaPalette.Glass, UltimaPalette.GlassAlpha);
        cairo_fill_preserve(cr);
        // contour or discret
        cairo_set_line_width(cr, 1);
        Set(cr, UltimaPalette.Gold, 0.35);
        cairo_stroke(cr);
        // liseré or franc sur le bord gauche (visible a distance, lecon v1)
        cairo_rectangle(cr, 0, radius, 2, Height - 2 * radius);
        Set(cr, UltimaPalette.Gold, 0.9);
        cairo_fill(cr);

        if (w <= RailWidth) RenderRail(cr);
        else RenderPanel(cr);
        cairo_destroy(cr);
        cairo_surface_flush(surface);
    }

    private static void RenderRail(nint cr)
    {
        // niveau 0 : 3 pastilles (PV / letal / priorite), diametre 6 px, centrees dans la zone 2..14 px
        var cx = 2 + (RailWidth - 2) / 2.0;
        var cy = Height / 2.0;
        (Rgba c, double dy)[] dots = { (UltimaPalette.Red, -16), (UltimaPalette.Orange, 0), (UltimaPalette.Gold, 16) };
        foreach (var (c, dy) in dots)
        {
            cairo_new_sub_path(cr);
            cairo_arc(cr, cx, cy + dy, 3, 0, 2 * Math.PI);
            Set(cr, c);
            cairo_fill(cr);
        }
    }

    private static void RenderPanel(nint cr)
    {
        const double pad = 16;
        // titre
        Font(cr, "Kreon", true, 16);
        Set(cr, UltimaPalette.Cream);
        cairo_move_to(cr, pad, 30);
        Text(cr, "ULTIMA");
        var tw = TextWidth(cr, "ULTIMA");
        cairo_rectangle(cr, pad, 36, tw, 1);
        Set(cr, UltimaPalette.Gold);
        cairo_fill(cr);

        Font(cr, "Source Code Pro", true, 12);
        Set(cr, UltimaPalette.HalfCream);
        var deck = "DECK 12";
        cairo_move_to(cr, PanelWidth - pad - TextWidth(cr, deck), 30);
        Text(cr, deck);

        // separateur
        cairo_rectangle(cr, pad, 48, PanelWidth - 2 * pad, 1);
        Set(cr, UltimaPalette.Cream, 0.12);
        cairo_fill(cr);

        // 5 lignes de deck, chiffres en colonne (monospace)
        var y = 60.0;
        foreach (var (cost, name, rarity, count) in DeckLines)
        {
            cairo_rectangle(cr, pad, y + 3, 3, 18);
            Set(cr, rarity);
            cairo_fill(cr);

            Font(cr, "Source Code Pro", true, 15);
            Set(cr, UltimaPalette.Energy);
            cairo_move_to(cr, pad + 10, y + 18);
            Text(cr, cost.ToString());

            Font(cr, "Kreon", false, 15);
            Set(cr, UltimaPalette.Cream);
            cairo_move_to(cr, pad + 30, y + 18);
            Text(cr, name);

            Font(cr, "Source Code Pro", true, 14);
            Set(cr, UltimaPalette.Cream, 0.7);
            var c = $"x{count}";
            cairo_move_to(cr, PanelWidth - pad - TextWidth(cr, c), y + 18);
            Text(cr, c);
            y += 28;
        }

        Font(cr, "Kreon", false, 12);
        Set(cr, UltimaPalette.HalfCream);
        cairo_move_to(cr, pad, Height - 16);
        Text(cr, "spike S1 · moteur A (X11) · factice");
    }

    public string Status() =>
        $"window=0x{Window:x} expanded={Expanded.ToString().ToLowerInvariant()} visible={Visible.ToString().ToLowerInvariant()} " +
        $"width={CurrentWidth} height={Height} scale={Scale:0.###} xgeom={Px(CurrentWidth)}x{Px(Height)}+{_screenW - Px(CurrentWidth)}+{_top} " +
        $"input_rects={InputRegionRectCount()} area_pct={100.0 * Px(CurrentWidth) * Px(Height) / (_screenW * (double)_screenH):F2}";

    /// <summary>
    /// Facteur px logique -> px X. Priorite : ULTIMA_X11_SCALE, puis Xft.dpi/96 (RESOURCE_MANAGER, ecrit par
    /// gsd-xsettings ; 192 sous Xwayland « native scaling » a 125 %), sinon 1.
    /// </summary>
    private static double DetectScale(nint dpy, out string source)
    {
        if (double.TryParse(Environment.GetEnvironmentVariable("ULTIMA_X11_SCALE"),
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s) && s > 0)
        { source = "ULTIMA_X11_SCALE"; return s; }
        var rm = X.XResourceManagerString(dpy);
        if (rm != null)
        {
            foreach (var line in (System.Runtime.InteropServices.Marshal.PtrToStringUTF8((nint)rm) ?? "").Split('\n'))
                if (line.StartsWith("Xft.dpi:") && double.TryParse(line[8..].Trim(),
                        System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dpi) && dpi > 0)
                { source = $"Xft.dpi={dpi}"; return dpi / 96.0; }
        }
        source = "defaut";
        return 1.0;
    }

    public void Dispose()
    {
        cairo_surface_destroy(_buffer);
        if (_panelCache != 0) cairo_surface_destroy(_panelCache);
        cairo_surface_destroy(_xsurf);
        X.XDestroyWindow(_dpy, Window);
        X.XCloseDisplay(_dpy);
    }
}
