namespace Ultima.Core.Model;

// CONTRAT DTO FIGÉ (docs/15 §4) — propriétaire : chef d'orchestre.
// Les sous-agents codent contre ce contrat et ne le modifient JAMAIS (demande → status/<agent>.md).
// Les membres positionnels sont EXACTEMENT ceux de docs/15 §4 ; les propriétés init-only sont
// les extensions chef M0 (rétro-compatibles, valeurs par défaut sûres).
// Placé dans Ultima.Core (et non Ultima/Model) : données pures, testables en CI sans le jeu.

/// <summary>État de combat observé, du point de vue du joueur local. Lecture seule.</summary>
public sealed record CombatSnapshot(
    int Turn,
    string PlayerName,
    int Hp, int MaxHp, int Block, int Energy,
    IReadOnlyList<EnemySnapshot> Enemies,
    IReadOnlyList<CardSnapshot> Hand,
    IReadOnlyList<CardSnapshot> DrawPile,
    IReadOnlyList<CardSnapshot> DiscardPile)
{
    /// <summary>Tous les joueurs du combat, joueur local inclus (<see cref="PlayerSnapshot.IsLocal"/>). Solo : 1 entrée.</summary>
    public IReadOnlyList<PlayerSnapshot> Players { get; init; } = Array.Empty<PlayerSnapshot>();

    /// <summary>NetId du joueur local.</summary>
    public ulong LocalNetId { get; init; }

    public bool IsMultiplayer { get; init; }
}

/// <summary>
/// Un joueur du combat. <paramref name="CharacterId"/> = nom de classe C# du personnage
/// (Ironclad, Silent, Defect, Regent, Necrobinder, Deprived) : clé de la couleur co-op (docs/17 §1.4).
/// </summary>
public sealed record PlayerSnapshot(
    ulong NetId, string Name, string CharacterId,
    int Hp, int MaxHp, int Block, bool IsLocal)
{
    /// <summary>Le joueur porte déjà Vulnerable : la variante « si Vulnerable » ne s'applique pas.</summary>
    public bool IsVulnerable { get; init; }
}

public sealed record EnemySnapshot(
    string Name, int Hp, int MaxHp, int Block,
    IReadOnlyList<IntentSnapshot> Intents)
{
    /// <summary><c>creature.CombatId</c> : clé stable (docs/13 §1 piège 4). 0 si absent.</summary>
    public uint CombatId { get; init; }

    /// <summary>Le dealer porte déjà Weak : afficher « déjà affaibli », ne jamais re-multiplier.</summary>
    public bool IsWeak { get; init; }

    public IReadOnlyList<PowerSnapshot> Powers { get; init; } = Array.Empty<PowerSnapshot>();
}

public sealed record PowerSnapshot(string Id, string Name, int Amount, bool IsDebuff);

public sealed record IntentSnapshot(
    string Kind,      // "Attack", "Defend", "Buff", ...
    int PerHit, int Repeats, int TotalDamage,
    string Label)     // texte localisé fourni par le jeu (oracle de F1, jamais une donnée de calcul)
{
    /// <summary>
    /// Dégâts par coup AVANT troncature (<c>Hook.ModifyDamage</c>, cible = joueur local), pour des
    /// variantes exactes. Invariant : <c>PerHit == Max(0, (int)RawPerHit)</c>.
    /// </summary>
    public decimal RawPerHit { get; init; }

    /// <summary>
    /// Multiplicateur que Weak appliquerait à CE dealer contre le joueur local, LU dans les modèles
    /// (WeakPower.DamageDecrease → PaperKrane → Debilitate). null = non applicable
    /// (pas une attaque, dealer déjà Weak, ou lecture impossible).
    /// </summary>
    public decimal? WeakMultiplier { get; init; }

    /// <summary>
    /// Multiplicateur que Vulnerable appliquerait au joueur local contre CE dealer, LU dans les modèles
    /// (VulnerablePower.DamageIncrease → Cruelty → Debilitate). null = non applicable.
    /// </summary>
    public decimal? VulnerableMultiplier { get; init; }

    /// <summary>Co-op : dégâts par coup contre chaque joueur (même pipeline, cible = ce joueur).</summary>
    public IReadOnlyList<TargetHit> PerTarget { get; init; } = Array.Empty<TargetHit>();
}

public sealed record TargetHit(ulong PlayerNetId, int PerHit);

public sealed record CardSnapshot(
    string ModelId, string Name, int Cost, string Type,
    bool IsUpgraded, string? Enchantment)
{
    /// <summary>Nom de classe C# du modèle (= champ <c>class</c> de data/catalog/*.json).</summary>
    public string ClassName { get; init; } = "";

    /// <summary><c>CardModel.ShouldGlowGold</c> : le bonus conditionnel est actif maintenant (oracle F3).</summary>
    public bool GlowsGold { get; init; }

    /// <summary><c>card.CanPlay(out _, out _)</c> : seul test fiable de jouabilité.</summary>
    public bool Playable { get; init; }
}
