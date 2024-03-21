using WalletWasabi.Backend.Models.Responses;
using Xunit;
using WalletWasabi.WabiSabi.Client.CoinJoin.Manager.Logic;
using WalletWasabi.WabiSabi.Client.CoinJoin.Client;
using WalletWasabi.Wallets;
using WalletWasabi.WabiSabi.Client.StatusChangedEvents;

namespace WalletWasabi.Tests.UnitTests.CoinJoinManager;

public class CoinJoinLogicTests
{
	[Fact]
	public void CanStartCoinJoinTest()
	{
		Assert.Equal(
			CoinjoinError.NotEnoughUnprivateBalance,
			Assert.Throws<CoinJoinClientException>(() => CoinJoinLogic.AssertCanStartCoinJoin(
				walletBlockedByUi: false,
				isUnderPlebStop: true,
				overridePlebStop: false,
				lastResponse: new SynchronizeResponse(),
				out _)).CoinjoinError);

		Assert.Equal(
			CoinjoinError.BackendNotSynchronized,
			Assert.Throws<CoinJoinClientException>(() => CoinJoinLogic.AssertCanStartCoinJoin(
				walletBlockedByUi: false,
				isUnderPlebStop: false,
				overridePlebStop: true,
				lastResponse: null,
				out _)).CoinjoinError);

		Assert.Equal(
			CoinjoinError.UserInSendWorkflow,
			Assert.Throws<CoinJoinClientException>(() => CoinJoinLogic.AssertCanStartCoinJoin(
				walletBlockedByUi: true,
				isUnderPlebStop: true,
				overridePlebStop: true,
				lastResponse: new SynchronizeResponse(),
				out _)).CoinjoinError);
	}
}
