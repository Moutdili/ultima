using System.Collections.Generic;
using MegaCrit.Sts2.Core.Localization.DynamicVars;

namespace MegaCrit.Sts2.Core.Models.Relics;

public sealed class CopperBell : RelicModel
{
	private const string _combatsKey = "Combats";

	private int _combatsLeft = 3;

	public override RelicRarity Rarity => RelicRarity.Shop;

	protected override IEnumerable<DynamicVar> CanonicalVars => new global::_003C_003Ez__ReadOnlyArray<DynamicVar>(new DynamicVar[2]
	{
		new DynamicVar(_combatsKey, CombatsLeft),
		new GoldVar(15)
	});

	public int CombatsLeft
	{
		get
		{
			return _combatsLeft;
		}
	}
}
