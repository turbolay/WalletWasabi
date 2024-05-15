using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Bases;
using WalletWasabi.Blockchain.Analysis.FeesEstimation;

namespace WalletWasabi.WebClients.BlockstreamInfo;

public class BlockstreamInfoFeeProvider : PeriodicRunner, IThirdPartyFeeProvider
{
	private readonly TimeSpan _intervalAfterSuccess = TimeSpan.FromMinutes(3);

	public BlockstreamInfoFeeProvider(TimeSpan period, BlockstreamInfoClient blockstreamInfoClient) : base(period)
	{
		BlockstreamInfoClient = blockstreamInfoClient;
	}

	public event EventHandler<AllFeeEstimate>? AllFeeEstimateArrived;

	public BlockstreamInfoClient BlockstreamInfoClient { get; set; }
	public AllFeeEstimate? LastAllFeeEstimate { get; private set; }
	public bool InError { get; private set; } = false;
	public bool IsPaused { get; set; } = false;

	private DateTime DateLastSuccess { get; set; } = DateTime.MinValue;

	protected override async Task ActionAsync(CancellationToken cancel)
	{
		if (IsPaused || DateTime.UtcNow - DateLastSuccess < _intervalAfterSuccess)
		{
			return;
		}
		try
		{
			var allFeeEstimate = await BlockstreamInfoClient.GetFeeEstimatesAsync(cancel).ConfigureAwait(false);
			LastAllFeeEstimate = allFeeEstimate;

			if (allFeeEstimate.Estimations.Count != 0)
			{
				AllFeeEstimateArrived?.Invoke(this, allFeeEstimate);
			}

			DateLastSuccess = DateTime.UtcNow;
			InError = false;
		}
		catch
		{
			InError = true;
			throw;
		}
	}
}
