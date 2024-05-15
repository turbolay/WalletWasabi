using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Blockchain.Analysis.FeesEstimation;
using Xunit;

namespace WalletWasabi.Tests.UnitTests.Blockchain.Analysis.FeeEstimation;

public class ThirdPartyFeeProviderTests
{
	[Fact]
	public async void PriorityTestsAsync()
	{
		var feeProvider1 = new TestFeeProvider();
		var feeProvider2 = new TestFeeProvider();
		var feeProvider3 = new TestFeeProvider();

		using CancellationTokenSource cts = new CancellationTokenSource();
		using ThirdPartyFeeProvider thirdPartyFeeProvider = new(TimeSpan.FromSeconds(2), [feeProvider1, feeProvider2, feeProvider3], TimeSpan.FromSeconds(4));
		await thirdPartyFeeProvider.StartAsync(cts.Token);

		int result = 0;

		// We shouldn't move to error mode instantly.
		feeProvider1.InError = true;
		thirdPartyFeeProvider.TriggerRound();
		Assert.False(thirdPartyFeeProvider.InError);

		// More than 4 sec elapsed, time to move to error mode.
		await Task.Delay(5000, cts.Token);
		thirdPartyFeeProvider.TriggerRound();
		Assert.True(thirdPartyFeeProvider.InError);

		// First result, accept it, not in error mode anymore.
		feeProvider3.SendSimpleEstimate(2, 3);
		thirdPartyFeeProvider.LastAllFeeEstimate?.Estimations.TryGetValue(2, out result);
		Assert.False(thirdPartyFeeProvider.InError);
		Assert.Equal(3, result);

		// Result from a provider with higher priority, we should accept it.
		feeProvider1.SendSimpleEstimate(2, 1);
		thirdPartyFeeProvider.LastAllFeeEstimate?.Estimations.TryGetValue(2, out result);
		Assert.Equal(1, result);

		// Lower priority result, we shouldn't accept it.
		result = 0;
		feeProvider2.SendSimpleEstimate(2, 2);
		thirdPartyFeeProvider.LastAllFeeEstimate?.Estimations.TryGetValue(2, out result);
		Assert.Equal(1, result);

		// All providers except 1 should be paused as 1 (highest priority) provided a result.
		Assert.False(feeProvider1.IsPaused);
		Assert.True(feeProvider2.IsPaused);
		Assert.True(feeProvider3.IsPaused);

		await thirdPartyFeeProvider.StopAsync(cts.Token);
	}
}
