namespace Ultima.Engine.Scoring.Powers;

/// <summary>
/// Doom (Necrobinder) : ce n'est pas un dégât, c'est un <b>seuil d'exécution</b>. En fin de tour du
/// camp qui le porte, une créature dont les PV courants sont inférieurs ou égaux aux piles de Doom
/// meurt — d'un coup, sans passer par les dégâts ni le bloc.
/// </summary>
/// <remarks>
/// <para>
/// Preuves dans le code du jeu, recopiées pour éviter toute réinterprétation :
/// <c>DoomPower.cs:65</c> — <c>IsOwnerDoomed() =&gt; base.Owner.CurrentHp &lt;= base.Amount</c> ;
/// <c>DoomPower.cs:68</c> — <c>BeforeSideTurnEnd(...)</c> tue les créatures marquées.
/// </para>
/// <para>
/// Conséquence pour le scoring : 13 piles de Doom ne valent pas « 13 dégâts ». Elles valent
/// « l'ennemi meurt dès qu'il tombe à 13 PV ou moins », donc <see cref="EffectiveHp"/> est ce qu'il
/// reste à encaisser. Le catalogue des pouvoirs ne peut pas le voir : <c>DoomPower</c> ne surcharge
/// aucun hook indexé (ses <c>hooks</c> sont vides), d'où la note de Pestilence qui ne repose que sur
/// la méta.
/// </para>
/// </remarks>
public static class Doom
{
    /// <summary>Source exacte dans le code du jeu (fichier:ligne).</summary>
    public const string Source = "DoomPower.cs:65 (IsOwnerDoomed) et :68 (BeforeSideTurnEnd)";

    /// <summary>L'ennemi meurt-il en fin de tour avec ces PV et ces piles de Doom ?</summary>
    public static bool IsLethal(int currentHp, int doomStacks) => doomStacks > 0 && currentHp <= doomStacks;

    /// <summary>
    /// PV qu'il faut encore retirer avant que Doom ne fasse le travail. Zéro signifie que la cible
    /// est déjà condamnée : continuer à taper dessus est du gaspillage.
    /// </summary>
    public static int EffectiveHp(int currentHp, int doomStacks) =>
        doomStacks <= 0 ? Math.Max(0, currentHp) : Math.Max(0, currentHp - doomStacks);

    /// <summary>
    /// Piles de Doom nécessaires pour condamner une cible ayant ces PV. Utile pour comparer deux
    /// sources de Doom (Pestilence 13, améliorée 16).
    /// </summary>
    public static int StacksToCondemn(int currentHp) => Math.Max(0, currentHp);
}
