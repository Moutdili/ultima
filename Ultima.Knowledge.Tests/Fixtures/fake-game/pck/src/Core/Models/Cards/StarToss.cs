using System.Collections.Generic;
using MegaCrit.Sts2.Core.Localization.DynamicVars;

namespace MegaCrit.Sts2.Core.Models.Cards;

public sealed class StarToss : CardModel
{
	private const string _hitsKey = "Hits";

	protected override bool HasEnergyCostX => true;

	protected override IEnumerable<DynamicVar> CanonicalVars => new global::_003C_003Ez__ReadOnlyArray<DynamicVar>(new DynamicVar[2]
	{
		new StarsVar(2),
		new IntVar(_hitsKey, 3m)
	});

	public StarToss()
		: base(-1, CardType.Skill, CardRarity.Rare, TargetType.Self)
	{
	}
}
