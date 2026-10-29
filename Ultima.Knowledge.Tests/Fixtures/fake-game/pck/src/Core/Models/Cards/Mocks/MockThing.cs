namespace MegaCrit.Sts2.Core.Models.Cards.Mocks;

public sealed class MockThing : CardModel
{
	public MockThing()
		: base(1, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
	{
	}
}
