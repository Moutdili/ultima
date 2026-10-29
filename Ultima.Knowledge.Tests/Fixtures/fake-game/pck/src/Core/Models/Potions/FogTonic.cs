using System.Collections.Generic;
using MegaCrit.Sts2.Core.Localization.DynamicVars;

namespace MegaCrit.Sts2.Core.Models.Potions;

public sealed class FogTonic : PotionModel
{
	private const string _damageDecreaseKey = "DamageDecrease";

	public override PotionRarity Rarity => PotionRarity.Rare;

	public override PotionUsage Usage => PotionUsage.CombatOnly;

	protected override IEnumerable<DynamicVar> CanonicalVars => new global::_003C_003Ez__ReadOnlyArray<DynamicVar>(new DynamicVar[4]
	{
		new DynamicVar(_damageDecreaseKey, 30m),
		new RepeatVar(4),
		new DynamicVar("Shrink", 0.75m),
		new DynamicVar("Delta", -3m)
	});
}
