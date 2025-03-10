using NBitcoin.RPC;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Backend.Models;
using WalletWasabi.Backend.Models.Responses;
using WalletWasabi.Bases;
using WalletWasabi.Blockchain.BlockFilters;
using WalletWasabi.Blockchain.Blocks;
using WalletWasabi.Models;
using WalletWasabi.Stores;
using WalletWasabi.WabiSabi.Client;
using WalletWasabi.WebClients.Wasabi;

namespace WalletWasabi.Services;

public class WasabiSynchronizer : PeriodicRunner, INotifyPropertyChanged, IWasabiBackendStatusProvider
{
	private TorStatus _torStatus;

	private BackendStatus _backendStatus;
	private bool _backendNotCompatible;

	public WasabiSynchronizer(TimeSpan period, int maxFiltersToSync, BitcoinStore bitcoinStore, IHttpClientFactory httpClientFactory) : base(period)
	{
		_maxFiltersToSync = maxFiltersToSync;

		LastResponse = null;
		_smartHeaderChain = bitcoinStore.SmartHeaderChain;
		_filterProcessor = new FilterProcessor(bitcoinStore);
		HttpClient = httpClientFactory.CreateClient("long-live-satoshi-backend");
	}

	#region EventsPropertiesMembers

	public event EventHandler<bool>? SynchronizeRequestFinished;

	public event EventHandler<SynchronizeResponse>? ResponseArrived;

	public event PropertyChangedEventHandler? PropertyChanged;

	/// <summary>Task completion source that is completed once a first synchronization request succeeds or fails.</summary>
	public TaskCompletionSource<bool> InitialRequestTcs { get; } = new();

	public SynchronizeResponse? LastResponse { get; private set; }
	public HttpClient HttpClient { get; }

	public TorStatus TorStatus
	{
		get => _torStatus;
		private set => RaiseAndSetIfChanged(ref _torStatus, value);
	}

	public BackendStatus BackendStatus
	{
		get => _backendStatus;
		private set
		{
			if (RaiseAndSetIfChanged(ref _backendStatus, value))
			{
				BackendStatusChangedAt = DateTimeOffset.UtcNow;
			}
		}
	}

	public bool BackendNotCompatible
	{
		get => _backendNotCompatible;
		private set => RaiseAndSetIfChanged(ref _backendNotCompatible, value);
	}

	private DateTimeOffset BackendStatusChangedAt { get; set; } = DateTimeOffset.UtcNow;
	public TimeSpan BackendStatusChangedSince => DateTimeOffset.UtcNow - BackendStatusChangedAt;
	private readonly int _maxFiltersToSync;
	private readonly SmartHeaderChain _smartHeaderChain;
	private readonly FilterProcessor _filterProcessor;

	#endregion EventsPropertiesMembers

	protected override async Task ActionAsync(CancellationToken cancel)
	{
		var wasabiClient = new WasabiClient(HttpClient);
		try
		{
			SynchronizeResponse response;

			ushort lastUsedApiVersion = WasabiClient.ApiVersion;
			try
			{
				if (_smartHeaderChain.TipHash is null)
				{
					return;
				}

				response = await wasabiClient
					.GetSynchronizeAsync(_smartHeaderChain.TipHash, _maxFiltersToSync, EstimateSmartFeeMode.Conservative, cancel)
					.ConfigureAwait(false);

				// NOT GenSocksServErr
				BackendStatus = BackendStatus.Connected;
				BackendNotCompatible = false;
				TorStatus = TorStatus.Running;
				OnSynchronizeRequestFinished();
			}
			catch (HttpRequestException ex) when (ex.InnerException is SocketException innerEx)
			{
				//TODO: check the source is the proxy
				TorStatus = innerEx.SocketErrorCode == SocketError.ConnectionRefused
					? TorStatus.NotRunning
					: TorStatus.Running;
				BackendStatus = BackendStatus.NotConnected;
				OnSynchronizeRequestFinished();
				throw;
			}
			catch (HttpRequestException ex) when (ex.Message.Contains("Not Found"))
			{
				TorStatus = TorStatus.Running;
				BackendStatus = BackendStatus.NotConnected;

				// Backend API version might be updated meanwhile. Trying to update the versions.
				bool backendCompatible;
				try
				{
					backendCompatible = await wasabiClient.CheckUpdatesAsync(cancel).ConfigureAwait(false);
				}
				catch (HttpRequestException) when (ex.Message.Contains("Not Found"))
				{
					// Backend is online but the endpoint for versions doesn't exist -> backend is not compatible.
					BackendNotCompatible = true;
					return;
				}

				// If the backend is compatible and the Api version updated then we just used the wrong API.
				if (backendCompatible && lastUsedApiVersion != WasabiClient.ApiVersion)
				{
					// Next request will be fine, do not throw exception.
					TriggerRound();
					return;
				}

				BackendNotCompatible = !backendCompatible;
				throw;
			}
			catch (Exception)
			{
				TorStatus = TorStatus.Running;
				BackendStatus = BackendStatus.NotConnected;
				OnSynchronizeRequestFinished();
				throw;
			}

			// If it's not fully synced or reorg happened.
			if (response.Filters.Count() == _maxFiltersToSync || response.FiltersResponseState == FiltersResponseState.BestKnownHashNotFound)
			{
				TriggerRound();
			}

			await _filterProcessor.ProcessAsync((uint)response.BestHeight, response.FiltersResponseState, response.Filters).ConfigureAwait(false);

			LastResponse = response;
			ResponseArrived?.Invoke(this, response);
		}
		catch (HttpRequestException)
		{
			await Task.Delay(3000, cancel).ConfigureAwait(false); // Retry sooner in case of connection error.
			TriggerRound();
			throw;
		}
	}

	private void OnSynchronizeRequestFinished()
	{
		var isBackendConnected = BackendStatus is BackendStatus.Connected;

		// One time trigger for the UI about the first request.
		InitialRequestTcs.TrySetResult(isBackendConnected);

		SynchronizeRequestFinished?.Invoke(this, isBackendConnected);
	}

	protected bool RaiseAndSetIfChanged<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
	{
		if (EqualityComparer<T>.Default.Equals(field, value))
		{
			return false;
		}

		field = value;
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		return true;
	}
}
