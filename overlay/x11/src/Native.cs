using System.Runtime.InteropServices;
using System.Text;

namespace Ultima.Overlay.X11;

// P/Invoke minimal. Aucune API de focus (XSetInputFocus, XRaiseWindow sur le jeu...) n'est declaree :
// la fenetre ne peut pas voler le focus par construction.

[StructLayout(LayoutKind.Sequential)]
internal struct XVisualInfo
{
    public nint Visual;
    public nuint VisualId;
    public int Screen;
    public int Depth;
    public int Class;
    public nuint RedMask, GreenMask, BlueMask;
    public int ColormapSize;
    public int BitsPerRgb;
}

[StructLayout(LayoutKind.Sequential)]
internal struct XSetWindowAttributes
{
    public nuint BackgroundPixmap;
    public nuint BackgroundPixel;
    public nuint BorderPixmap;
    public nuint BorderPixel;
    public int BitGravity;
    public int WinGravity;
    public int BackingStore;
    public nuint BackingPlanes;
    public nuint BackingPixel;
    public int SaveUnder;
    public nint EventMask;
    public nint DoNotPropagateMask;
    public int OverrideRedirect;
    public nuint Colormap;
    public nuint Cursor;
}

[StructLayout(LayoutKind.Sequential)]
internal struct XWindowAttributes
{
    public int X, Y, Width, Height, BorderWidth, Depth;
    public nint Visual;
    public nuint Root;
    public int Class, BitGravity, WinGravity, BackingStore;
    public nuint BackingPlanes, BackingPixel;
    public int SaveUnder;
    public nuint Colormap;
    public int MapInstalled;
    public int MapState;
    public nint AllEventMasks, YourEventMask, DoNotPropagateMask;
    public int OverrideRedirect;
    public nint Screen;
}

[StructLayout(LayoutKind.Sequential)]
internal struct XWMHints
{
    public nint Flags;
    public int Input;
    public int InitialState;
    public nuint IconPixmap;
    public nuint IconWindow;
    public int IconX, IconY;
    public nuint IconMask;
    public nuint WindowGroup;
}

[StructLayout(LayoutKind.Sequential)]
internal struct XRectangle
{
    public short X, Y;
    public ushort Width, Height;
}

[StructLayout(LayoutKind.Sequential, Size = 192)]
internal struct XEvent
{
    public int Type;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PollFd
{
    public int Fd;
    public short Events;
    public short REvents;
}

[StructLayout(LayoutKind.Sequential)]
internal struct CairoTextExtents
{
    public double XBearing, YBearing, Width, Height, XAdvance, YAdvance;
}

internal static unsafe class X
{
    private const string Lib = "libX11.so.6";

    public const int InputOutput = 1;
    public const int TrueColor = 4;
    public const int Expose = 12;
    public const int IsViewable = 2;

    public const nuint CWBackPixel = 1 << 1;
    public const nuint CWBorderPixel = 1 << 3;
    public const nuint CWOverrideRedirect = 1 << 9;
    public const nuint CWSaveUnder = 1 << 10;
    public const nuint CWEventMask = 1 << 11;
    public const nuint CWColormap = 1 << 13;

    public const nint ExposureMask = 1 << 15;
    public const nint StructureNotifyMask = 1 << 17;

    public const int PropModeReplace = 0;
    public const nuint XA_ATOM = 4;
    public const nuint XA_CARDINAL = 6;
    public const nuint XA_STRING = 31;

    [DllImport(Lib)] public static extern int XInitThreads();
    [DllImport(Lib)] public static extern nint XOpenDisplay(nint name);
    [DllImport(Lib)] public static extern int XCloseDisplay(nint dpy);
    [DllImport(Lib)] public static extern int XDefaultScreen(nint dpy);
    [DllImport(Lib)] public static extern nuint XRootWindow(nint dpy, int screen);
    [DllImport(Lib)] public static extern int XDisplayWidth(nint dpy, int screen);
    [DllImport(Lib)] public static extern int XDisplayHeight(nint dpy, int screen);
    [DllImport(Lib)] public static extern int XConnectionNumber(nint dpy);
    [DllImport(Lib)] public static extern int XMatchVisualInfo(nint dpy, int screen, int depth, int cls, out XVisualInfo info);
    [DllImport(Lib)] public static extern nuint XCreateColormap(nint dpy, nuint w, nint visual, int alloc);
    [DllImport(Lib)] public static extern nuint XCreateWindow(nint dpy, nuint parent, int x, int y, uint w, uint h,
        uint border, int depth, uint cls, nint visual, nuint valueMask, ref XSetWindowAttributes attrs);
    [DllImport(Lib)] public static extern int XMapWindow(nint dpy, nuint w);
    [DllImport(Lib)] public static extern int XUnmapWindow(nint dpy, nuint w);
    [DllImport(Lib)] public static extern int XMoveResizeWindow(nint dpy, nuint w, int x, int y, uint width, uint height);
    [DllImport(Lib)] public static extern int XDestroyWindow(nint dpy, nuint w);
    [DllImport(Lib)] public static extern int XFlush(nint dpy);
    [DllImport(Lib)] public static extern int XSync(nint dpy, int discard);
    [DllImport(Lib)] public static extern int XPending(nint dpy);
    [DllImport(Lib)] public static extern int XNextEvent(nint dpy, out XEvent ev);
    [DllImport(Lib)] public static extern int XStoreName(nint dpy, nuint w, byte* name);
    [DllImport(Lib)] public static extern nuint XInternAtom(nint dpy, byte* name, int onlyIfExists);
    [DllImport(Lib)] public static extern int XChangeProperty(nint dpy, nuint w, nuint prop, nuint type, int format,
        int mode, byte* data, int nelements);
    [DllImport(Lib)] public static extern int XSetWMHints(nint dpy, nuint w, ref XWMHints hints);
    [DllImport(Lib)] public static extern int XQueryTree(nint dpy, nuint w, out nuint root, out nuint parent,
        out nuint* children, out uint n);
    [DllImport(Lib)] public static extern int XGetWindowAttributes(nint dpy, nuint w, out XWindowAttributes attrs);
    [DllImport(Lib)] public static extern int XFetchName(nint dpy, nuint w, out byte* name);
    [DllImport(Lib)] public static extern int XFree(void* p);
    [DllImport(Lib)] public static extern byte* XResourceManagerString(nint dpy);

    public static nuint Atom(nint dpy, string name)
    {
        var b = Utf8(name);
        fixed (byte* p = b) return XInternAtom(dpy, p, 0);
    }

    public static byte[] Utf8(string s)
    {
        var b = new byte[Encoding.UTF8.GetByteCount(s) + 1];
        Encoding.UTF8.GetBytes(s, 0, s.Length, b, 0);
        return b;
    }

    public static string? FetchName(nint dpy, nuint w)
    {
        if (XFetchName(dpy, w, out var p) == 0 || p == null) return null;
        var s = Marshal.PtrToStringUTF8((nint)p);
        XFree(p);
        return s;
    }
}

internal static unsafe class XFixes
{
    private const string Lib = "libXfixes.so.3";
    public const int ShapeBounding = 0;
    public const int ShapeInput = 2;

    [DllImport(Lib)] public static extern int XFixesQueryExtension(nint dpy, out int evBase, out int errBase);
    [DllImport(Lib)] public static extern int XFixesQueryVersion(nint dpy, out int major, out int minor);
    [DllImport(Lib)] public static extern nuint XFixesCreateRegion(nint dpy, XRectangle* rects, int n);
    [DllImport(Lib)] public static extern void XFixesSetWindowShapeRegion(nint dpy, nuint w, int kind, int x, int y, nuint region);
    [DllImport(Lib)] public static extern void XFixesDestroyRegion(nint dpy, nuint region);
}

internal static unsafe class XShape
{
    private const string Lib = "libXext.so.6";
    [DllImport(Lib)] public static extern int XShapeQueryExtension(nint dpy, out int evBase, out int errBase);
    // Relecture serveur de la region d'entree (preuve du click-through).
    [DllImport(Lib)] public static extern XRectangle* XShapeGetRectangles(nint dpy, nuint w, int kind, out int count, out int ordering);
}

internal static unsafe class Cairo
{
    private const string Lib = "libcairo.so.2";
    public const int FormatArgb32 = 0;
    public const int OperatorClear = 0;
    public const int OperatorSource = 1;
    public const int OperatorOver = 2;

    [DllImport(Lib)] public static extern nint cairo_xlib_surface_create(nint dpy, nuint drawable, nint visual, int w, int h);
    [DllImport(Lib)] public static extern void cairo_xlib_surface_set_size(nint surface, int w, int h);
    [DllImport(Lib)] public static extern nint cairo_image_surface_create(int format, int w, int h);
    [DllImport(Lib)] public static extern int cairo_surface_write_to_png(nint surface, byte* file);
    [DllImport(Lib)] public static extern void cairo_surface_flush(nint surface);
    [DllImport(Lib)] public static extern void cairo_surface_destroy(nint surface);
    [DllImport(Lib)] public static extern nint cairo_create(nint surface);
    [DllImport(Lib)] public static extern void cairo_destroy(nint cr);
    [DllImport(Lib)] public static extern void cairo_save(nint cr);
    [DllImport(Lib)] public static extern void cairo_restore(nint cr);
    [DllImport(Lib)] public static extern void cairo_set_operator(nint cr, int op);
    [DllImport(Lib)] public static extern void cairo_set_source_rgba(nint cr, double r, double g, double b, double a);
    [DllImport(Lib)] public static extern void cairo_set_source_surface(nint cr, nint surface, double x, double y);
    [DllImport(Lib)] public static extern void cairo_paint(nint cr);
    [DllImport(Lib)] public static extern void cairo_rectangle(nint cr, double x, double y, double w, double h);
    [DllImport(Lib)] public static extern void cairo_arc(nint cr, double xc, double yc, double r, double a1, double a2);
    [DllImport(Lib)] public static extern void cairo_new_sub_path(nint cr);
    [DllImport(Lib)] public static extern void cairo_new_path(nint cr);
    [DllImport(Lib)] public static extern void cairo_close_path(nint cr);
    [DllImport(Lib)] public static extern void cairo_move_to(nint cr, double x, double y);
    [DllImport(Lib)] public static extern void cairo_line_to(nint cr, double x, double y);
    [DllImport(Lib)] public static extern void cairo_fill(nint cr);
    [DllImport(Lib)] public static extern void cairo_fill_preserve(nint cr);
    [DllImport(Lib)] public static extern void cairo_stroke(nint cr);
    [DllImport(Lib)] public static extern void cairo_clip(nint cr);
    [DllImport(Lib)] public static extern void cairo_translate(nint cr, double x, double y);
    [DllImport(Lib)] public static extern void cairo_scale(nint cr, double sx, double sy);
    [DllImport(Lib)] public static extern void cairo_set_line_width(nint cr, double w);
    [DllImport(Lib)] public static extern void cairo_select_font_face(nint cr, byte* family, int slant, int weight);
    [DllImport(Lib)] public static extern void cairo_set_font_size(nint cr, double size);
    [DllImport(Lib)] public static extern void cairo_show_text(nint cr, byte* utf8);
    [DllImport(Lib)] public static extern void cairo_text_extents(nint cr, byte* utf8, out CairoTextExtents ext);

    public static void Font(nint cr, string family, bool bold, double size)
    {
        var b = X.Utf8(family);
        fixed (byte* p = b) cairo_select_font_face(cr, p, 0, bold ? 1 : 0);
        cairo_set_font_size(cr, size);
    }

    public static void Text(nint cr, string s)
    {
        var b = X.Utf8(s);
        fixed (byte* p = b) cairo_show_text(cr, p);
    }

    public static double TextWidth(nint cr, string s)
    {
        var b = X.Utf8(s);
        CairoTextExtents e;
        fixed (byte* p = b) cairo_text_extents(cr, p, out e);
        return e.XAdvance;
    }

    public static void WritePng(nint surface, string path)
    {
        var b = X.Utf8(path);
        fixed (byte* p = b) cairo_surface_write_to_png(surface, p);
    }
}

internal static unsafe class Fc
{
    [DllImport("libfontconfig.so.1")] private static extern int FcConfigAppFontAddFile(nint config, byte* file);

    public static bool AddFile(string path)
    {
        if (!File.Exists(path)) return false;
        var b = X.Utf8(path);
        fixed (byte* p = b) return FcConfigAppFontAddFile(0, p) != 0;
    }
}

internal static class LibC
{
    public const short POLLIN = 1;
    [DllImport("libc", SetLastError = true)] public static extern unsafe int poll(PollFd* fds, nuint nfds, int timeout);
}
