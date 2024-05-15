using System.Collections.Generic;
using WalletWasabi.Blockchain.Analysis.FeesEstimation;

namespace WalletWasabi.Tests.UnitTests.Blockchain.Analysis.FeeEstimation;

public class TestFeeProvider : IThirdPartyFeeProvider
{
	public event EventHandler<AllFeeEstimate>? AllFeeEstimateArrived;

	public AllFeeEstimate? LastAllFeeEstimate { get; private set; }
	public bool InError { get; set; }
	public bool IsPaused { get; set; }

	public void SendSimpleEstimate(int blocks, int satsPerVb)
	{
		InError = false;
		AllFeeEstimate fees = new(new Dictionary<int, int> { { blocks, satsPerVb } });
		LastAllFeeEstimate = fees;
		AllFeeEstimateArrived?.Invoke(this, fees);
	}

	public void TriggerRound()
	{
		throw new NotImplementedException();
	}
}
