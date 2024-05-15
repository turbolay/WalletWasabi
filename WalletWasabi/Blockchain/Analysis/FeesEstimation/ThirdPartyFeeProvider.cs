using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Bases;
using WalletWasabi.Nito.AsyncEx;

namespace WalletWasabi.Blockchain.Analysis.FeesEstimation;

/// <summary>
/// Manages multiple fee sources. Returns the best one.
/// The list order considered to be the priority (first one is the highest).
/// </summary>
public class ThirdPartyFeeProvider : PeriodicRunner, IThirdPartyFeeProvider
{
	private int _actualFeeProviderIndex = -1;
	private bool _isPaused;
	private readonly TimeSpan _admitErrorTimeSpan;

	public ThirdPartyFeeProvider(TimeSpan period, ImmutableArray<IThirdPartyFeeProvider> feeProviders, TimeSpan? admitErrorTimeSpan = null)
		: base(period)
	{
		_admitErrorTimeSpan = admitErrorTimeSpan ?? TimeSpan.FromMinutes(1);
		FeeProviders = feeProviders;
		if (FeeProviders.Length > 0)
		{
			ActualFeeProviderIndex = 0;
		}
	}

	public event EventHandler<AllFeeEstimate>? AllFeeEstimateArrived;

	public AllFeeEstimate? LastAllFeeEstimate { get; private set; }
	private object Lock { get; } = new();
	public bool InError { get; private set; }

	public bool IsPaused
	{
		get => _isPaused;
		set
		{
			_isPaused = value;
			SetPauseStates();
		}
	}

	private AbandonedTasks ProcessingEvents { get; } = new();

	private ImmutableArray<IThirdPartyFeeProvider> FeeProviders { get; }

	///<remarks> Aside from the constructor, setter is always guarded by <see cref="Lock"/>.</remarks>
	private int ActualFeeProviderIndex
	{
		get => _actualFeeProviderIndex;
		set
		{
			if (_actualFeeProviderIndex != value)
			{
				_actualFeeProviderIndex = value;
				LastStatusChange = DateTimeOffset.UtcNow;
				SetPauseStates();
			}
		}
	}

	private DateTimeOffset LastStatusChange { get; set; } = DateTimeOffset.UtcNow;

	public override async Task StartAsync(CancellationToken cancellationToken)
	{
		foreach (var feeProvider in FeeProviders)
		{
			SetAllFeeEstimateIfLooksBetter(feeProvider.LastAllFeeEstimate);
			feeProvider.AllFeeEstimateArrived += OnAllFeeEstimateArrived;
		}

		await base.StartAsync(cancellationToken).ConfigureAwait(false);
	}

	public override async Task StopAsync(CancellationToken cancellationToken)
	{
		foreach (var feeProvider in FeeProviders)
		{
			feeProvider.AllFeeEstimateArrived -= OnAllFeeEstimateArrived;
		}

		await ProcessingEvents.WhenAllAsync().ConfigureAwait(false);
		await base.StopAsync(cancellationToken).ConfigureAwait(false);
	}

	private void OnAllFeeEstimateArrived(object? sender, AllFeeEstimate? fees)
	{
		using (RunningTasks.RememberWith(ProcessingEvents))
		{
			// Only go further if we have estimations.
			if (fees is null || fees.Estimations.Count == 0)
			{
				return;
			}

			if (sender is IThirdPartyFeeProvider provider)
			{
				var notify = false;
				lock (Lock)
				{
					int senderIdx = FeeProviders.IndexOf(provider);

					// If we already had a result from a higher priority fee provider then just drop it silently.
					if (senderIdx != -1 && senderIdx <= ActualFeeProviderIndex)
					{
						ActualFeeProviderIndex = senderIdx;
						InError = false;
						LastStatusChange = DateTimeOffset.UtcNow;
						notify = SetAllFeeEstimate(fees);
					}
				}
				if (notify)
				{
					AllFeeEstimateArrived?.Invoke(sender, fees);
				}
			}
		}
	}

	private void SetAllFeeEstimateIfLooksBetter(AllFeeEstimate? fees)
	{
		var current = LastAllFeeEstimate;
		if (fees is null
			|| fees == current
			|| (current is not null && fees.Estimations.Count <= current.Estimations.Count))
		{
			return;
		}
		SetAllFeeEstimate(fees);
	}

	/// <returns>True if changed.</returns>
	private bool SetAllFeeEstimate(AllFeeEstimate fees)
	{
		if (LastAllFeeEstimate == fees)
		{
			return false;
		}
		LastAllFeeEstimate = fees;
		return true;
	}

	private void SetPauseStates()
	{
		int feeProviderIndex = ActualFeeProviderIndex;

		// Even in active mode we pause the lower priority fee providers.
		for (int idx = 0; idx < FeeProviders.Length; idx++)
		{
			var pauseStatusBefore = FeeProviders[idx].IsPaused;
			FeeProviders[idx].IsPaused = IsPaused || idx > feeProviderIndex;

			if (!pauseStatusBefore && FeeProviders[idx].IsPaused)
			{
				FeeProviders[idx].TriggerRound();
			}
		}

	}

	protected override Task ActionAsync(CancellationToken cancel)
	{
		if (IsPaused)
		{
			return Task.CompletedTask;
		}

		lock (Lock)
		{
			bool inError = FeeProviders.Take(ActualFeeProviderIndex + 1).All(f => f.InError);

			// Let's wait a bit more.
			if (inError && !InError && DateTimeOffset.UtcNow - LastStatusChange > _admitErrorTimeSpan)
			{
				// We are in error mode, all fees providers turned on at once and the successful highest priority fee provider will win.
				InError = true;
				ActualFeeProviderIndex = FeeProviders.Length - 1;
				LastStatusChange = DateTimeOffset.UtcNow;
			}
		}

		return Task.CompletedTask;
	}
}
