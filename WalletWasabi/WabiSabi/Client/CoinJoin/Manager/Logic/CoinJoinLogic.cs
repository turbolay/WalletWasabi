using WalletWasabi.Backend.Models.Responses;
using WalletWasabi.WabiSabi.Client.CoinJoin.Client;
using WalletWasabi.WabiSabi.Client.StatusChangedEvents;
using WalletWasabi.Wallets;

namespace WalletWasabi.WabiSabi.Client.CoinJoin.Manager.Logic;

public static class CoinJoinLogic
{
	public static void AssertCanStartCoinJoin(bool walletBlockedByUi, bool isUnderPlebStop, bool overridePlebStop, SynchronizeResponse? lastResponse, out SynchronizeResponse synchronizerResponse)
	{
		if (walletBlockedByUi)
		{
			throw new CoinJoinClientException(CoinjoinError.UserInSendWorkflow);
		}

		if (!overridePlebStop && isUnderPlebStop)
		{
			throw new CoinJoinClientException(CoinjoinError.NotEnoughUnprivateBalance);
		}

		synchronizerResponse = lastResponse ?? throw new CoinJoinClientException(CoinjoinError.BackendNotSynchronized);
	}
}
