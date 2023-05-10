using ReactiveUI;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using WalletWasabi.Fluent.ViewModels.Navigation;
using WalletWasabi.WabiSabi.Client;

namespace WalletWasabi.Fluent.ViewModels.Dialogs;

[NavigationMetaData(Title = "Please wait to shut down...")]
public partial class ShuttingDownViewModel : RoutableViewModel
{
	private readonly ApplicationViewModel _applicationViewModel;
	private readonly bool _restart;
	private readonly CoinJoinManager? _coinJoinManager;

	public ShuttingDownViewModel(ApplicationViewModel applicationViewModel, bool restart)
	{
		_applicationViewModel = applicationViewModel;
		_restart = restart;
		_coinJoinManager = Services.HostedServices.GetOrDefault<CoinJoinManager>();

		NextCommand = ReactiveCommand.Create(
			() =>
		{
			if (_coinJoinManager is { })
			{
				_coinJoinManager.NewCoinjoinsPreventedByUi = false;
			}

			Navigate().Clear();
		});
	}

	protected override void OnNavigatedTo(bool isInHistory, CompositeDisposable disposables)
	{
		if (_coinJoinManager is { })
		{
			_coinJoinManager.NewCoinjoinsPreventedByUi = true;
		}

		Observable.Interval(TimeSpan.FromSeconds(60))
				  .ObserveOn(RxApp.MainThreadScheduler)
				  .Subscribe(_ =>
				  {
					  if (_applicationViewModel.CanShutdown(_restart))
					  {
						  Navigate().Clear();
						  _applicationViewModel.Shutdown(_restart);
					  }
				  })
				  .DisposeWith(disposables);
	}
}
