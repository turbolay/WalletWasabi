using NBitcoin;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using WalletWasabi.Blockchain.Analysis.FeesEstimation;
using WalletWasabi.Blockchain.Transactions;
using WalletWasabi.Blockchain.Transactions.Summary;
using WalletWasabi.Fluent.Helpers;
using WalletWasabi.Fluent.Models;
using WalletWasabi.Models;

namespace WalletWasabi.Fluent.Extensions;

public static class SmartTransactionExtension
{
	public static IEnumerable<BitcoinAddress> GetDestinationAddresses(this SmartTransaction transaction)
	{
		var inputs = transaction.GetInputs().ToList();
		var outputs = transaction.GetOutputs().ToList();

		var myOwnInputs = inputs.OfType<KnownInput>().ToList();
		var foreignInputs = inputs.OfType<ForeignInput>().ToList();
		var myOwnOutputs = outputs.OfType<OwnOutput>().ToList();
		var foreignOutputs = outputs.OfType<ForeignOutput>().ToList();

		// All inputs and outputs are my own, transaction is a self-spend.
		if (!foreignInputs.Any() && !foreignOutputs.Any())
		{
			// Classic self-spend to one or more external addresses.
			if (myOwnOutputs.Any(x => !x.IsInternal))
			{
				// Destinations are the external addresses.
				return myOwnOutputs.Where(x => !x.IsInternal).Select(x => x.DestinationAddress);
			}

			// Edge-case: self-spend to one or more internal addresses.
			// We can't know the destinations, return all the outputs.
			return myOwnOutputs.Select(x => x.DestinationAddress);
		}

		// All inputs are foreign but some outputs are my own, someone is sending coins to me.
		if (!myOwnInputs.Any() && myOwnOutputs.Any())
		{
			// All outputs that are my own are the destinations.
			return myOwnOutputs.Select(x => x.DestinationAddress);
		}

		// I'm sending a transaction to someone else.
		// All outputs that are not my own are the destinations.
		return foreignOutputs.Select(x => x.DestinationAddress);
	}

	public static IEnumerable<Output> GetOutputs(this SmartTransaction smartTransaction)
	{
		var known = smartTransaction.WalletOutputs.Select(
			coin =>
		{
			var address = coin.TxOut.ScriptPubKey.GetDestinationAddress(Services.WalletManager.Network)!;
			return new OwnOutput(coin.TxOut.Value, address, coin.HdPubKey.IsInternal);
		}).Cast<Output>();

		var unknown = smartTransaction.ForeignOutputs.Select(
			coin =>
		{
			var address = coin.TxOut.ScriptPubKey.GetDestinationAddress(Services.WalletManager.Network)!;
			return new ForeignOutput(coin.TxOut.Value, address);
		}).Cast<Output>();

		return known.Concat(unknown);
	}

	public static IEnumerable<IInput> GetInputs(this SmartTransaction transaction)
	{
		var known = transaction.WalletInputs
			.Select(x => new KnownInput(x.Amount))
			.OfType<IInput>();

		var unknown = transaction.ForeignInputs
			.Select(_ => new ForeignInput())
			.OfType<IInput>();

		return known.Concat(unknown);
	}

	public static long GetAmount(this SmartTransaction transaction)
	{
		return transaction.WalletOutputs.Sum(x => x.Amount) - transaction.WalletInputs.Sum(x => x.Amount);
	}

	public static FeeRate? GetFeeRate(this SmartTransaction transaction)
	{
		if (transaction.TryGetFeeRate(out var feeRate))
		{
			return feeRate;
		}

		return null;
	}

	public static Money? GetFee(this SmartTransaction transaction)
	{
		if (transaction.TryGetFee(out var fee))
		{
			return fee;
		}

		return null;
	}

	public static int GetConfirmations(this SmartTransaction transaction) => transaction.Height.Type == HeightType.Chain ? (int)Services.BitcoinStore.SmartHeaderChain.ServerTipHeight - transaction.Height.Value + 1 : 0;

	public static bool TryGetConfirmationTime(this SmartTransaction transaction, [NotNullWhen(true)] out TimeSpan? estimate)
		=> TransactionFeeHelper.TryEstimateConfirmationTime(Services.HostedServices.Get<HybridFeeProvider>(), Services.WalletManager.Network, transaction, out estimate);
}
