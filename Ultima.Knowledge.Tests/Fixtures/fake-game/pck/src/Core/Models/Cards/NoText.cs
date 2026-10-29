namespace MegaCrit.Sts2.Core.Models.Cards;

public sealed class NoText : CardModel
{
	public NoText()
		: base(2, CardType.Power, CardRarity.Rare, TargetType.Self)
	{
	}
}
