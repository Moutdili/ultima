using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace Ultima;

/// <summary>
/// Point d'entrée du mod. Volontairement read-only : rien ici ne modifie l'état de jeu.
/// </summary>
[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
    public const string HarmonyId = "mio.ultima";
    public static readonly string LogPath =
        Path.Combine(OS.GetExecutablePath().GetBaseDir(), "mods", "Ultima", "ultima.log");

    public static void Initialize()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            Log.Warn($"[ULTIMA] boot — game {DetectGameVersion()} — log {LogPath}");
            Write("boot");

            // Le resolver du jeu ne connait que `sts2` et `0Harmony` (ModManager.cs:892).
            // Sans ca, Ultima.Core.dll n'est jamais trouve et l'injection echoue en
            // FileNotFoundException. On charge nos DLL soeurs nous-memes.
            AppDomain.CurrentDomain.AssemblyResolve += ResolveSiblingAssembly;
            PreloadSiblingAssemblies();
            Config.UltimaConfig.Load();
            PreloadLibGcc();
            new Harmony(HarmonyId).PatchAll(typeof(ModEntry).Assembly);
            Log.Warn("[ULTIMA] harmony patches applied");
            Write("harmony applied");
            Write($"ULTIMA ready — game {DetectGameVersion()} — mod {typeof(ModEntry).Assembly.GetName().Version}");
        }
        catch (Exception ex)
        {
            Log.Warn($"[ULTIMA] FATAL init: {ex}");
            try { Write($"FATAL init: {ex}"); } catch { /* jamais crasher le jeu */ }
        }
    }

    /// <summary>
    /// Godot 4 ne charge pas libgcc_s avec RTLD_GLOBAL (godot#81032) : Harmony compile
    /// <c>mm-exhelper.so</c> puis son dlopen échoue sur <c>_Unwind_RaiseException</c>
    /// → aucun patch appliqué, overlay inexistant. On force le chargement global ici pour
    /// que le symbole soit déjà résolu au moment du dlopen de Harmony.
    /// </summary>
    /// <summary>Charge les DLL livrées à côté de Ultima.dll (ex. Ultima.Core.dll).</summary>
    private static void PreloadSiblingAssemblies()
    {
        try
        {
            var dir = Path.GetDirectoryName(typeof(ModEntry).Assembly.Location);
            if (string.IsNullOrEmpty(dir)) return;
            // Le loader du jeu charge chaque mod dans son propre AssemblyLoadContext
            // (ModManager.cs:718). On charge nos dépendances dans CE contexte, sinon on
            // obtient deux identités de type différentes pour Ultima.Core.
            var alc = AssemblyLoadContext.GetLoadContext(typeof(ModEntry).Assembly)
                      ?? AssemblyLoadContext.Default;

            foreach (var dll in Directory.GetFiles(dir, "*.dll"))
            {
                var name = Path.GetFileNameWithoutExtension(dll);
                if (name is "Ultima" or "0Harmony" or "sts2" or "GodotSharp" or "GodotSharpEditor") continue;
                try
                {
                    alc.LoadFromAssemblyPath(dll);
                    Write($"assembly: {name} precharge (ALC {alc.Name ?? \"isole\"})");
                }
                catch (Exception ex)
                {
                    Write($"assembly: {name} non charge ({ex.GetType().Name})");
                }
            }
        }
        catch (Exception ex)
        {
            Write($"assembly: preload failed {ex.Message}");
        }
    }

    /// <summary>Filet de sécurité : résout toute DLL du dossier du mod à la demande.</summary>
    private static Assembly? ResolveSiblingAssembly(object? sender, ResolveEventArgs args)
    {
        try
        {
            var simple = new AssemblyName(args.Name).Name;
            if (string.IsNullOrEmpty(simple)) return null;
            var dir = Path.GetDirectoryName(typeof(ModEntry).Assembly.Location);
            if (string.IsNullOrEmpty(dir)) return null;
            var candidate = Path.Combine(dir, simple + ".dll");
            if (!File.Exists(candidate)) return null;
            Write($"assembly: {simple} resolu a la demande");
            return Assembly.LoadFrom(candidate);
        }
        catch (Exception ex)
        {
            Write($"assembly: resolution {args.Name} echouee ({ex.GetType().Name})");
            return null;
        }
    }

    private const int RtldNow = 0x00002;
    private const int RtldGlobal = 0x00100;

    [DllImport("libdl.so.2", EntryPoint = "dlopen", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr DlOpen(string fileName, int flags);

    private static void PreloadLibGcc()
    {
        // RTLD_GLOBAL obligatoire : c'est ce qui rend les symboles visibles au dlopen suivant
        // (celui que Harmony fait sur son mm-exhelper.so). NativeLibrary ne garantit pas ce flag.
        string[] candidates =
        {
            "libgcc_s.so.1",
            "/usr/lib64/libgcc_s.so.1",
            "/usr/lib/x86_64-linux-gnu/libgcc_s.so.1",
            "/usr/lib/libgcc_s.so.1",
            "/lib/x86_64-linux-gnu/libgcc_s.so.1",
            "libunwind.so.8",
            "/usr/lib64/libunwind.so.8",
            "/usr/lib/x86_64-linux-gnu/libunwind.so.8",
        };

        int loaded = 0;
        foreach (var name in candidates)
        {
            try
            {
                if (DlOpen(name, RtldNow | RtldGlobal) == IntPtr.Zero) continue;
                Write($"dlopen: {name} (RTLD_GLOBAL) — workaround godot#81032");
                loaded++;
            }
            catch
            {
                // candidat suivant
            }
        }

        if (loaded == 0)
            Write("dlopen: libgcc_s/libunwind introuvable — Harmony va probablement echouer");
    }

    public static string DetectGameVersion()
    {
        try
        {
            var root = OS.GetExecutablePath().GetBaseDir();
            var info = Path.Combine(root, "release_info.json");
            if (!File.Exists(info)) return "unknown";
            var text = File.ReadAllText(info);
            var i = text.IndexOf("\"version\"", StringComparison.Ordinal);
            if (i < 0) return "unknown";
            var start = text.IndexOf('"', text.IndexOf(':', i) + 1) + 1;
            var end = text.IndexOf('"', start);
            return start > 0 && end > start ? text[start..end] : "unknown";
        }
        catch
        {
            return "unknown";
        }
    }

    public static void Write(string line)
    {
        try
        {
            // PID : en multi local (tools/multi-lab.sh) les 2 instances partagent ce fichier.
            File.AppendAllText(LogPath, $"{DateTime.Now:O} [{System.Environment.ProcessId}] {line}\n");
        }
        catch
        {
            // le log ne doit jamais faire planter le jeu
        }
    }
}
