namespace MegaCrit.Sts2.Core.Models.CardPools;

public sealed class ColorlessCardPool : CardPoolModel
{
	protected override CardModel[] GenerateAllCards()
	{
		return new CardModel[2]
		{
			ModelDb.Card<EnglishOnly>(),
			ModelDb.Card<EmberSlash>()
		};
	}
}
