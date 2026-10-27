using System;
using System.IO;
using System.Runtime.InteropServices;
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
    private static void PreloadLibGcc()
    {
        string[] candidates =
        {
            "libgcc_s.so.1",
            "/usr/lib/libgcc_s.so.1",
            "/usr/lib/x86_64-linux-gnu/libgcc_s.so.1",
            "/usr/lib64/libgcc_s.so.1",
            "/lib/x86_64-linux-gnu/libgcc_s.so.1",
        };

        foreach (var name in candidates)
        {
            try
            {
                // NativeLibrary.Load utilise dlopen(RTLD_GLOBAL) sous Unix : c'est exactement
                // ce qu'il faut pour rendre _Unwind_RaiseException visible au helper de Harmony.
                if (!NativeLibrary.TryLoad(name, out _)) continue;
                Write($"dlopen: {name} charge en global (workaround godot#81032)");
                return;
            }
            catch
            {
                // on tente le candidat suivant
            }
        }

        Write("dlopen: libgcc_s introuvable — Harmony va probablement echouer");
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
