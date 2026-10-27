namespace Ultima.Core.Model;

/// <summary>Cible d'une attaque ennemie (joueur local ou allié en co-op).</summary>
public readonly record struct TargetState(string Name, int Hp, int Block);

/// <summary>Intent offensif observé sur un ennemi. Les valeurs viennent du jeu, jamais d'ici.</summary>
public readonly record struct EnemyAttack(string EnemyName, int PerHit, int Repeats, ValueKind Kind);

public enum ValueKind
{
    /// <summary>Dégâts tels que le jeu les affiche (pipeline complet appliqué).</summary>
    Current,
    /// <summary>Hypothèse : le dealer gagne Weak (multiplicateur appliqué par l'appelant).</summary>
    WithWeak,
    /// <summary>Hypothèse : la cible gagne Vulnerable.</summary>
    WithVulnerable,
}
