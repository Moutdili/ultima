using Ultima.Engine.Scoring.Powers;
using Xunit;

namespace Ultima.Engine.Tests.Scoring;

/// <summary>
/// Doom est un seuil d'exécution, pas un dégât (DoomPower.cs:65). Ces tests fixent la règle lue
/// dans le code du jeu pour qu'aucune version du moteur ne la transforme en « 13 dégâts ».
/// </summary>
public sealed class DoomTests
{
    [Fact]
    public void Une_cible_a_13_pv_avec_13_doom_est_condamnee()
    {
        Assert.True(Doom.IsLethal(13, 13));
        Assert.Equal(0, Doom.EffectiveHp(13, 13));
    }

    [Fact]
    public void Une_cible_au_dessus_du_seuil_ne_meurt_pas()
    {
        Assert.False(Doom.IsLethal(14, 13));
        Assert.Equal(1, Doom.EffectiveHp(14, 13));
    }

    [Fact]
    public void Sans_doom_rien_n_est_condamne()
    {
        Assert.False(Doom.IsLethal(1, 0));
        Assert.Equal(1, Doom.EffectiveHp(1, 0));
    }

    [Fact]
    public void Pestilence_amelioree_condamne_un_chevalier_ordinaire()
    {
        // Scourge applique 13 Doom (16 améliorée) : 16 PV ou moins = mort en fin de tour adverse.
        Assert.Equal(13, Doom.StacksToCondemn(13));
        Assert.True(Doom.IsLethal(16, 16));
        Assert.False(Doom.IsLethal(17, 16));
    }
}
