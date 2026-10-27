using System;
using System.IO;
using Godot;

namespace Ultima.Config;

/// <summary>
/// Configuration utilisateur : <c>mods/Ultima/ultima.cfg</c> (INI lu par Godot.ConfigFile).
/// JAMAIS de .json dans mods/ : le loader du jeu le prendrait pour un manifeste (R7).
/// Fichier absent ou illisible → valeurs par défaut, sans exception.
/// </summary>
public static class UltimaConfig
{
    public static readonly string CfgPath =
        Path.Combine(Path.GetDirectoryName(ModEntry.LogPath)!, "ultima.cfg");

    /// <summary>Retour au rail après N secondes sans survol (docs/17 §4).</summary>
    public static double CollapseDelaySec { get; private set; } = 2.5;

    /// <summary>Flou du verre (docs/17 §3). 1.0 si le FPS casse, 0 = repli StyleBoxFlat.</summary>
    public static float BlurAmount { get; private set; } = 2.0f;

    /// <summary>
    /// Voie B (INetMessage custom : hello, priorité, ping). OFF par défaut : le comportement d'un
    /// client vanilla face à un ID inconnu n'est pas vérifié (research/notes/multiplayer.md:325).
    /// À activer uniquement quand TOUS les joueurs ont le même mod-set (R8).
    /// </summary>
    public static bool CustomNetMessages { get; private set; }

    public static void Load()
    {
        try
        {
            if (!File.Exists(CfgPath))
            {
                WriteDefaults();
                return;
            }

            var cfg = new ConfigFile();
            if (cfg.Load(CfgPath) != Error.Ok)
            {
                ModEntry.Write($"config: illisible, defauts utilises ({CfgPath})");
                return;
            }

            CollapseDelaySec = Math.Clamp((double)cfg.GetValue("ui", "collapse_delay_sec", CollapseDelaySec), 0.5, 30.0);
            BlurAmount = Math.Clamp((float)cfg.GetValue("ui", "blur_amount", BlurAmount), 0f, 6f);
            CustomNetMessages = (bool)cfg.GetValue("net", "custom_messages", CustomNetMessages);
            ModEntry.Write($"config: collapse={CollapseDelaySec}s blur={BlurAmount} custom_net={CustomNetMessages}");
        }
        catch (Exception ex)
        {
            ModEntry.Write($"config: load failed {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void WriteDefaults()
    {
        try
        {
            var cfg = new ConfigFile();
            cfg.SetValue("ui", "collapse_delay_sec", CollapseDelaySec);
            cfg.SetValue("ui", "blur_amount", BlurAmount);
            cfg.SetValue("net", "custom_messages", CustomNetMessages);
            cfg.Save(CfgPath);
            ModEntry.Write($"config: defauts ecrits dans {CfgPath}");
        }
        catch (Exception ex)
        {
            ModEntry.Write($"config: write defaults failed {ex.Message}");
        }
    }
}
