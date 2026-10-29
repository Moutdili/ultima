using System.Collections.Generic;
using MegaCrit.Sts2.Core.Localization.DynamicVars;

namespace MegaCrit.Sts2.Core.Models.Cards;

public sealed class QuickStudy : CardModel
{
	public override IEnumerable<CardKeyword> CanonicalKeywords => new global::_003C_003Ez__ReadOnlySingleElementList<CardKeyword>(CardKeyword.Exhaust);

	protected override IEnumerable<DynamicVar> CanonicalVars => new global::_003C_003Ez__ReadOnlyArray<DynamicVar>(new DynamicVar[4]
	{
		new EnergyVar(1),
		new CalculationBaseVar(0m),
		new CalculationExtraVar(1m),
		new CalculatedVar("CalculatedCards").WithMultiplier((CardModel card, Creature? _) => PileType.Hand.GetPile(card.Owner).Cards.Count((CardModel c) => c.Type == CardType.Attack))
	});

	public QuickStudy()
		: base(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
	{
	}

	protected override void OnUpgrade()
	{
		base.DynamicVars.Energy.UpgradeValueBy(1m);
		base.EnergyCost.UpgradeBy(-1);
		RemoveKeyword(CardKeyword.Exhaust);
		AddKeyword(CardKeyword.Retain);
	}
}
