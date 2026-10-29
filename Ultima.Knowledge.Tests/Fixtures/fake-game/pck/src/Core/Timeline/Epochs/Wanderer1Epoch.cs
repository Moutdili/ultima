namespace MegaCrit.Sts2.Core.Timeline.Epochs;

public sealed class Wanderer1Epoch : EpochModel
{
	public static List<PotionModel> Potions
	{
		get
		{
			span[0] = ModelDb.Potion<FogTonic>();
			return list;
		}
	}
}
