namespace MegaCrit.Sts2.Core.Models.RelicPools;

public sealed class SharedRelicPool : RelicPoolModel
{
	protected override IEnumerable<RelicModel> GenerateAllRelics()
	{
		return new RelicModel[1] { ModelDb.Relic<CopperBell>() };
	}
}
