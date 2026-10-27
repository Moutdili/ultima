using Godot;
using Ultima.Core.Model;
using Ultima.UI.Theme;

namespace Ultima.UI.Widgets;

/// <summary>
/// Ligne ennemi. Niveau 1 : nom (Kreon 14) | NxM (Source Code Pro, colonne alignée) | issue
/// (létal = red + mot LÉTAL). Niveau 2 : variantes Weak/Vuln (hypothèses) + label du jeu (oracle).
/// </summary>
public partial class EnemyRow : VBoxContainer
{
    public const int DamageColumnWidth = 52;
    public const int OutcomeColumnWidth = 100;

    private readonly Label _name;
    private readonly Label _damage;
    private readonly Label _outcome;
    private readonly VBoxContainer _detail;
    private readonly List<Label> _detailLines = new();
    private int _detailUsed;

    public EnemyRow()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeConstantOverride("separation", 0);

        var line = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        line.AddThemeConstantOverride("separation", 4);
        _name = UltimaFonts.NewLabel(FontRole.Body, 14, UltimaPalette.Cream);
        _name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _damage = UltimaFonts.NewLabel(FontRole.Mono, 14, UltimaPalette.Cream, trim: false);
        _damage.CustomMinimumSize = new Vector2(DamageColumnWidth, 0);
        _damage.HorizontalAlignment = HorizontalAlignment.Right;
        // jamais tronqué : le mot LÉTAL doit rester lisible
        _outcome = UltimaFonts.NewLabel(FontRole.Body, 14, UltimaPalette.Cream, trim: false);
        _outcome.CustomMinimumSize = new Vector2(OutcomeColumnWidth, 0);
        line.AddChild(_name);
        line.AddChild(_damage);
        line.AddChild(_outcome);
        AddChild(line);

        _detail = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        _detail.AddThemeConstantOverride("separation", 0);
        AddChild(_detail);
    }

    /// <param name="forecast">null → dégâts lus dans les intents, aucune issue (pas de létalité inventée).</param>
    public void Bind(EnemySnapshot enemy, EnemyForecast? forecast, bool detail)
    {
        bool attacks = forecast != null ? forecast.Attacks.Count > 0 : OverlayText.HasAttack(enemy.Intents);
        _name.Text = attacks ? "⚔ " + enemy.Name : enemy.Name;
        _damage.Text = forecast != null
            ? OverlayText.Damage(forecast.Attacks)
            : OverlayText.DamageFromIntents(enemy.Intents);

        if (forecast == null)
        {
            _outcome.Text = "";
        }
        else
        {
            var r = forecast.Lethal;
            _outcome.Text = OverlayText.Outcome(r, attacks);
            UltimaFonts.SetColor(_outcome,
                r.IsLethal ? UltimaPalette.Red
                : attacks && r.FullyBlocked ? UltimaPalette.Blue
                : UltimaPalette.Cream);
        }

        _detailUsed = 0;
        if (detail)
        {
            if (forecast != null)
            {
                bool many = forecast.Attacks.Count > 1;
                for (int i = 0; i < forecast.Attacks.Count; i++)
                {
                    var a = forecast.Attacks[i];
                    string prefix = many ? $"{i + 1}. " : "";
                    AddDetail(OverlayText.WeakVariant(a), prefix, UltimaPalette.Aqua, FontRole.Mono);
                    AddDetail(OverlayText.VulnVariant(a), prefix, UltimaPalette.Aqua, FontRole.Mono);
                }
            }
            AddDetail(OverlayText.Oracle(enemy.Intents), "", UltimaPalette.HalfTransparentCream, FontRole.Body);
        }
        for (int i = 0; i < _detailLines.Count; i++) _detailLines[i].Visible = i < _detailUsed;
        _detail.Visible = detail && _detailUsed > 0;
    }

    private void AddDetail(string? text, string prefix, Color color, FontRole role)
    {
        if (text == null) return;
        Label l;
        if (_detailUsed < _detailLines.Count)
        {
            l = _detailLines[_detailUsed];
        }
        else
        {
            l = UltimaFonts.NewLabel(role, 12, color);
            _detailLines.Add(l);
            _detail.AddChild(l);
        }
        var font = UltimaFonts.Get(role);
        if (font != null && l.GetThemeFont("font") != font) l.AddThemeFontOverride("font", font);
        UltimaFonts.SetColor(l, color);
        l.Text = "  " + prefix + text;
        _detailUsed++;
    }
}
