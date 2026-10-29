using System.Collections.Generic;
using MegaCrit.Sts2.Core.Localization.DynamicVars;

namespace MegaCrit.Sts2.Core.Models.Enchantments;

public sealed class Braced : EnchantmentModel
{
	protected override IEnumerable<DynamicVar> CanonicalVars => new global::_003C_003Ez__ReadOnlySingleElementList<DynamicVar>(new BlockVar(0m, ValueProp.Move));

	protected override void OnEnchant()
	{
		base.DynamicVars.Block.BaseValue = base.Amount;
	}
}
