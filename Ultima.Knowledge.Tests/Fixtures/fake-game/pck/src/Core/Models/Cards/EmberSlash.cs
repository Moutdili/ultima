using System.Collections.Generic;
using MegaCrit.Sts2.Core.Localization.DynamicVars;

namespace MegaCrit.Sts2.Core.Models.Cards;

public sealed class EmberSlash : CardModel
{
	protected override IEnumerable<DynamicVar> CanonicalVars => new global::_003C_003Ez__ReadOnlyArray<DynamicVar>(new DynamicVar[2]
	{
		new DamageVar(7m, ValueProp.Move),
		new CardsVar(1)
	});

	public EmberSlash()
		: base(1, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
	{
	}

	protected override void OnUpgrade()
	{
		base.DynamicVars.Damage.UpgradeValueBy(3m);
		base.DynamicVars.Cards.UpgradeValueBy(1m);
	}
}
