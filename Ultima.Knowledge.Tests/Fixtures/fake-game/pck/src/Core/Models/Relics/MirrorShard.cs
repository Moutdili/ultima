using System.Collections.Generic;
using MegaCrit.Sts2.Core.Localization.DynamicVars;

namespace MegaCrit.Sts2.Core.Models.Relics;

public sealed class MirrorShard : RelicModel
{
	public override RelicRarity Rarity => RelicRarity.Ancient;

	protected override IEnumerable<DynamicVar> CanonicalVars => new global::_003C_003Ez__ReadOnlyArray<DynamicVar>(new DynamicVar[3]
	{
		new StringVar("StarterCard"),
		new StringVar("Enchantment", ModelDb.Enchantment<Gleam>().Title.GetFormattedText()),
		new DynamicVar("GoldIncrease", 1.25m)
	});
}
