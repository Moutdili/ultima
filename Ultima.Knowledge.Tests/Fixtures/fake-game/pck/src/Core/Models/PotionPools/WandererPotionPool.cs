namespace MegaCrit.Sts2.Core.Models.PotionPools;

public sealed class WandererPotionPool : PotionPoolModel
{
	protected override IEnumerable<PotionModel> GenerateAllPotions()
	{
		return Wanderer1Epoch.Potions;
	}
}
