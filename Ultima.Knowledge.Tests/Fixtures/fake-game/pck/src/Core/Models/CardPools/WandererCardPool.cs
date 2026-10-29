namespace MegaCrit.Sts2.Core.Models.CardPools;

public sealed class WandererCardPool : CardPoolModel
{
	protected override CardModel[] GenerateAllCards()
	{
		return new CardModel[4]
		{
			ModelDb.Card<EmberSlash>(),
			ModelDb.Card<QuickStudy>(),
			ModelDb.Card<StarToss>(),
			ModelDb.Card<NoText>()
		};
	}
}
