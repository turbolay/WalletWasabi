using System.IO;
using NBitcoin;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.BitcoinCore;
using WalletWasabi.BitcoinCore.Rpc;
using WalletWasabi.Blockchain.BlockFilters;
using WalletWasabi.Helpers;
using WalletWasabi.Logging;
using WalletWasabi.WabiSabi.Backend.Statistics;

namespace WalletWasabi.Backend;

public class StartupTask
{
	private P2pNode P2PNode { get; }
	private IndexBuilderService IndexBuilderService { get; }
	private IRPCClient RpcClient { get; }

	public StartupTask(Config config, IRPCClient rpc, P2pNode p2pNode, IndexBuilderService indexBuilderService)
	{
		P2PNode = p2pNode;
		IndexBuilderService = indexBuilderService;
		RpcClient = rpc;
	}

	public async Task ExecuteAsync(CancellationToken cancellationToken)
	{
		Logger.LogInfo("Wasabi Backend");

		AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
		TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

		// Make sure RPC works.
		await AssertRpcNodeFullyInitializedAsync(cancellationToken).ConfigureAwait(false);
		await P2PNode.ConnectAsync(cancellationToken).ConfigureAwait(false);
		IndexBuilderService.Synchronize();
		Logger.LogInfo($"{nameof(IndexBuilderService)} is successfully initialized and started synchronization.");
	}

	private async Task AssertRpcNodeFullyInitializedAsync(CancellationToken cancellationToken)
	{
		try
		{
			var blockchainInfo = await RpcClient.GetBlockchainInfoAsync(cancellationToken);

			var blocks = blockchainInfo.Blocks;
			if (blocks == 0 && RpcClient.Network != Network.RegTest)
			{
				throw new NotSupportedException($"{nameof(blocks)} == 0");
			}

			var headers = blockchainInfo.Headers;
			if (headers == 0 && RpcClient.Network != Network.RegTest)
			{
				throw new NotSupportedException($"{nameof(headers)} == 0");
			}

			if (blocks != headers)
			{
				throw new NotSupportedException($"{Constants.BuiltinBitcoinNodeName} is not fully synchronized.");
			}

			Logger.LogInfo($"{Constants.BuiltinBitcoinNodeName} is fully synchronized.");

			if (RpcClient.Network == Network.RegTest) // Make sure there's at least 101 block, if not generate it
			{
				if (blocks < 101)
				{
					using Key key = new();
					var generateBlocksResponse = await RpcClient.GenerateToAddressAsync(101, key.GetAddress(ScriptPubKeyType.Segwit, Network.RegTest), cancellationToken);
					if (generateBlocksResponse is null)
					{
						throw new NotSupportedException($"{Constants.BuiltinBitcoinNodeName} cannot generate blocks on the {Network.RegTest}.");
					}

					blockchainInfo = await RpcClient.GetBlockchainInfoAsync(cancellationToken);
					blocks = blockchainInfo.Blocks;
					if (blocks == 0)
					{
						throw new NotSupportedException($"{nameof(blocks)} == 0");
					}
					Logger.LogInfo($"Generated 101 block on {Network.RegTest}. Number of blocks {blocks}.");
				}
			}
		}
		catch (WebException)
		{
			Logger.LogError($"{Constants.BuiltinBitcoinNodeName} is not running, or incorrect RPC credentials, or network is given in the config file.");
			throw;
		}
	}

	private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
	{
		Logger.LogWarning(e.Exception, "Unexpected unobserved task exception.");
	}

	private void CurrentDomain_UnhandledException(object? sender, UnhandledExceptionEventArgs e)
	{
		if (e.ExceptionObject is Exception ex)
		{
			Logger.LogWarning(ex, "Unhandled exception.");
		}
	}
}
