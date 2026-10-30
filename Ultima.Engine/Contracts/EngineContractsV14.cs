namespace Ultima.Engine.Contracts;

// CONTRAT v1.4 — ADDITIF (chef, 2026-10-30). Personnalisation : docs/44-personnalisation.md.
// Modules déclaratifs et plugins, évalués par le daemon, publiés dans EngineState.Custom ; les surfaces
// (HUD, compagnon, TUI) ne font que les dessiner. Les couleurs passent par des tons (Tones), jamais en hex.

/// <summary>Une ligne d'un module personnalisé. Tone ∈ Tones.All (null = text). Grade ∈ S..F (null = pas de note).</summary>
public sealed record CustomRowView(string Text, string? Tone, string? Grade);

/// <summary>
/// Module personnalisé (Source = "module" : fichier déclaratif ; "plugin" : processus isolé).
/// Id commence par CustomIds.Prefix. Screens null = écrans par défaut. Health ∈ CustomHealth.All ;
/// Problem = raison lisible quand Health ≠ ok.
/// </summary>
public sealed record CustomModuleView(string Id, string Label, string Source, IReadOnlyList<string>? Screens,
    IReadOnlyList<CustomRowView> Rows, string Health, string? Problem);

public static class CustomIds
{
    /// <summary>Préfixe obligatoire des ids de modules perso et plugins (ex. « x.or-par-etage »).</summary>
    public const string Prefix = "x.";
    public const string SourceModule = "module", SourcePlugin = "plugin";
}

public static class CustomHealth
{
    public const string Ok = "ok", Stale = "stale", Error = "error", Disabled = "disabled";
    public static readonly IReadOnlyList<string> All = new[] { Ok, Stale, Error, Disabled };
}

/// <summary>Tons sémantiques : chaque thème associe une couleur à chacun (docs/44 §4).</summary>
public static class Tones
{
    public const string Text = "text", Muted = "muted", Accent = "accent", Danger = "danger", Ok = "ok",
        Warn = "warn", Curse = "curse", Enchant = "enchant", Info = "info";
    public static readonly IReadOnlyList<string> All = new[] { Text, Muted, Accent, Danger, Ok, Warn, Curse, Enchant, Info };
    /// <summary>Tons des notes : « grade.S » … « grade.F ».</summary>
    public static readonly IReadOnlyList<string> Grades = new[] { "grade.S", "grade.A", "grade.B", "grade.C", "grade.D", "grade.F" };
}

/// <summary>Écrans nommés pour ModuleSetting.Screens (camelCase de l'enum Screen).</summary>
public static class ScreenIds
{
    public static readonly IReadOnlyList<string> All =
        new[] { "menu", "map", "combat", "reward", "shop", "event", "restSite", "treasure" };
}
