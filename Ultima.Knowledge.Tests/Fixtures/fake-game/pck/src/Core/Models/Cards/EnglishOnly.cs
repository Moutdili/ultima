using System.Collections.Generic;
using MegaCrit.Sts2.Core.Localization.DynamicVars;

namespace MegaCrit.Sts2.Core.Models.Cards;

public sealed class EnglishOnly : CardModel
{
	public override bool GainsBlock => true;

	protected override IEnumerable<DynamicVar> CanonicalVars => new global::_003C_003Ez__ReadOnlySingleElementList<DynamicVar>(new BlockVar(5m, ValueProp.Move));

	public EnglishOnly()
		: base(0, CardType.Skill, CardRarity.Common, TargetType.Self)
	{
	}
}
