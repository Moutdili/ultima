namespace MegaCrit.Sts2.Core.Models.CardPools;

public sealed class CurseCardPool : CardPoolModel
{
	protected override CardModel[] GenerateAllCards()
	{
		return new CardModel[1] { ModelDb.Card<HollowHex>() };
	}
}
