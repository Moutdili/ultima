using System;
using System.IO;
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
            File.AppendAllText(LogPath, $"{DateTime.Now:O} {line}\n");
        }
        catch
        {
            // le log ne doit jamais faire planter le jeu
        }
    }
}
