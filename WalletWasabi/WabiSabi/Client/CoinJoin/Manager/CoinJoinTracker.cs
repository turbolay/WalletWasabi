using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Blockchain.TransactionOutputs;
using WalletWasabi.WabiSabi.Client.CoinJoin.Client;
using WalletWasabi.WabiSabi.Client.CoinJoinProgressEvents;
using WalletWasabi.WabiSabi.Coordinator.Rounds;
using WalletWasabi.Wallets;

namespace WalletWasabi.WabiSabi.Client;

public class CoinJoinTracker : IDisposable
{
	private bool _disposedValue;

	public CoinJoinTracker(
		IWallet wallet,
		CoinJoinClient coinJoinClient,
		Func<Task<IEnumerable<SmartCoin>>> coinCandidatesFunc,
		bool stopWhenAllMixed,
		bool overridePlebStop,
		IWallet outputWallet,
		CancellationToken cancellationToken)
	{
		Wallet = wallet;
		_coinJoinClient = coinJoinClient;
		_coinJoinClient.CoinJoinClientProgress += CoinJoinClient_CoinJoinClientProgress;

		StopWhenAllMixed = stopWhenAllMixed;
		OverridePlebStop = overridePlebStop;
		OutputWallet = outputWallet;
		_cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		CoinJoinTask = coinJoinClient.StartCoinJoinAsync(coinCandidatesFunc, stopWhenAllMixed, _cancellationTokenSource.Token);
	}

	public event EventHandler<CoinJoinProgressEventArgs>? WalletCoinJoinProgressChanged;

	public ImmutableList<SmartCoin> CoinsInCriticalPhase => _coinJoinClient.CoinsInCriticalPhase;
	private readonly CoinJoinClient _coinJoinClient;
	private readonly CancellationTokenSource _cancellationTokenSource;

	public IWallet Wallet { get; }
	public Task<CoinJoinResult> CoinJoinTask { get; }
	public bool StopWhenAllMixed { get; set; }
	public bool OverridePlebStop { get; }
	public IWallet OutputWallet { get; }

	public bool IsCompleted => CoinJoinTask.IsCompleted;
	public bool InCriticalCoinJoinState { get; private set; }
	public bool IsStopped { get; set; }
	public List<CoinBanned> BannedCoins { get; private set; } = new();

	public void Stop()
	{
		IsStopped = true;
		if (!InCriticalCoinJoinState)
		{
			_cancellationTokenSource.Cancel();
		}
	}

	private void CoinJoinClient_CoinJoinClientProgress(object? sender, CoinJoinProgressEventArgs coinJoinProgressEventArgs)
	{
		switch (coinJoinProgressEventArgs)
		{
			case EnteringCriticalPhase:
				InCriticalCoinJoinState = true;
				break;

			case LeavingCriticalPhase:
				InCriticalCoinJoinState = false;
				break;

			case RoundEnded roundEnded:
				if (roundEnded.LastRoundState.EndRoundState != EndRoundState.TransactionBroadcasted)
				{
					Wallet.BatchedPayments.MovePaymentsToPending();
				}

				roundEnded.IsStopped = IsStopped;
				break;

			case CoinBanned coinBanned:
				BannedCoins.Add(coinBanned);
				break;
		}

		WalletCoinJoinProgressChanged?.Invoke(Wallet, coinJoinProgressEventArgs);
	}

	protected virtual void Dispose(bool disposing)
	{
		if (!_disposedValue)
		{
			if (disposing)
			{
				_coinJoinClient.CoinJoinClientProgress -= CoinJoinClient_CoinJoinClientProgress;
				_cancellationTokenSource.Dispose();
			}

			_disposedValue = true;
		}
	}

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}
}
