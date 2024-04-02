using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using NBitcoin;
using NBitcoin.Policy;
using WalletWasabi.Blockchain.Analysis.Clustering;
using WalletWasabi.Blockchain.TransactionBuilding;
using WalletWasabi.Blockchain.TransactionOutputs;
using WalletWasabi.Exceptions;
using WalletWasabi.Logging;
using WalletWasabi.Models;
using WalletWasabi.Wallets;

namespace WalletWasabi.Helpers;

public static class FeeHelpers
{
	public static bool TryGetMaxFeeRateForChangeless(
		Wallet wallet,
		IDestination destination,
		LabelsArray labels,
		FeeRate startingFeeRate,
		IEnumerable<SmartCoin> coins,
		[NotNullWhen(true)] out FeeRate? maxFeeRate,
		bool allowDoubleSpend = false,
		bool tryToSign = false)
	{
		maxFeeRate = SeekMaxFeeRate(
			startingFeeRate,
			feeRate => wallet.BuildChangelessTransaction(destination, labels, feeRate, coins, allowDoubleSpend, tryToSign));

		return maxFeeRate is not null;
	}

	public static bool TryGetMaxFeeRate(
		Wallet wallet,
		IDestination destination,
		Money amount,
		LabelsArray labels,
		FeeRate startingFeeRate,
		IEnumerable<SmartCoin> coins,
		bool subtractFee,
		[NotNullWhen(true)] out FeeRate? maxFeeRate,
		bool tryToSign = false)
	{
		maxFeeRate = SeekMaxFeeRate(
			startingFeeRate,
			feeRate => wallet.BuildTransaction(destination, amount, labels, feeRate, coins, subtractFee, null, tryToSign));

		return maxFeeRate is not null;
	}

	/// <summary>
	/// SeekMaxFeeRate iteratively searches for the highest feasible fee rate
	/// that allows the provided 'buildTransaction' action to succeed.
	/// If the action succeeded it increases the fee rate to try, if fails, it reduces.
	/// It repeats it until the difference between the last succeeded and last wrong fee rate is 0.001.
	/// </summary>
	/// <param name="feeRate">The initial fee rate to start the search from.</param>
	/// <param name="buildTransaction">The action to build a transaction with the given fee rate.</param>
	/// <returns>
	/// The maximum feasible fee rate discovered, or null if no suitable fee rate is found.
	/// </returns>
	private static FeeRate? SeekMaxFeeRate(FeeRate feeRate, Action<FeeRate> buildTransaction)
	{
		if (feeRate.SatoshiPerByte < 1m)
		{
			feeRate = new FeeRate(1m);
		}

		var lastWrongFeeRate = new FeeRate(0m);
		var lastCorrectFeeRate = new FeeRate(0m);

		var foundClosestSolution = false;
		while (!foundClosestSolution)
		{
			try
			{
				buildTransaction(feeRate);
				lastCorrectFeeRate = feeRate;
				var increaseBy = lastWrongFeeRate.SatoshiPerByte == 0 ? feeRate.SatoshiPerByte : (lastWrongFeeRate.SatoshiPerByte - feeRate.SatoshiPerByte) / 2;
				feeRate = new FeeRate(feeRate.SatoshiPerByte + increaseBy);
			}
			catch (Exception ex) when (ex is NotEnoughFundsException or TransactionFeeOverpaymentException or InsufficientBalanceException || (ex is InvalidTxException itx && itx.Errors.OfType<FeeTooHighPolicyError>().Any()))
			{
				if (feeRate.SatoshiPerByte == 1m)
				{
					break;
				}

				lastWrongFeeRate = feeRate;
				var decreaseBy = (feeRate.SatoshiPerByte - lastCorrectFeeRate.SatoshiPerByte) / 2;
				var nextSatPerByteCandidate = feeRate.SatoshiPerByte - decreaseBy;
				var newSatPerByte = nextSatPerByteCandidate < 1m ? 1m : nextSatPerByteCandidate; // make sure to always try 1 sat/vbyte as a last chance.
				feeRate = new FeeRate(newSatPerByte);
			}
			catch (Exception ex)
			{
				Logger.LogWarning(ex);
				return null;
			}

			foundClosestSolution = Math.Abs(lastWrongFeeRate.SatoshiPerByte - lastCorrectFeeRate.SatoshiPerByte) == 0.001m;
		}

		return foundClosestSolution ? lastCorrectFeeRate : null;
	}

	public static FeeRate? CalculateEffectiveFeeRateOfUnconfirmedChain(
		List<UnconfirmedTransactionChainItem> unconfirmedTransactionChain,
		uint256 targetTxId)
	{

		// This data structure is like a linked list with the exception that a Node can have several children.
		// It can have multiple roots: Every transaction without any parent. The leaves are transactions without any child.
		var tree = ConstructTransactionsTree(unconfirmedTransactionChain);

		// We do this to avoid accounting for a branch that joins the tree at a lower level than the target tx.
		// Eg: TX A (1s/vb) and B (100s/vb) are parents of C (3s/vb), if we want effective fee rate of B we have to exclude A.
		var rootsWithTargetTxInPath = tree.Where(x => ContainsTxInPath(x, targetTxId));

		Dictionary<uint256, FeeRate> effectiveFeeRatesOfRoots = new();
		foreach (var root in rootsWithTargetTxInPath)
		{
			var (totalFee, totalSize) = ComputeFeeRateFromDescendants(root);
			effectiveFeeRatesOfRoots.Add(root.Value.TxId, new FeeRate(totalFee, totalSize));
		}

		// TODO: Until there the logic should be more or less correct, after that I'm not sure and my brain is now completely fried.
		// TODO: I think that we might need to request the as parameter of the function the targetTxId, at least that was my initial intuition.

		return effectiveFeeRatesOfRoots.Values.Min();
	}

	// TODO: Both ContainsTxInPath and ComputeFeeRateFromDescendants are almost the same functions, maybe we could improve the algo.
	private static bool ContainsTxInPath(TreeNode<UnconfirmedTransactionChainItem> root, uint256 txid)
	{
		if (txid == root.Value.TxId)
		{
			return true;
		}

		var nextChildren = root.Children.ToHashSet();

		while (nextChildren.Count > 0)
		{
			var currentChild = nextChildren.First();
			nextChildren.Remove(currentChild);

			if (txid == currentChild.Value.TxId)
			{
				return true;
			}

			foreach (var nextChild in currentChild.Children)
			{
				nextChildren.Add(nextChild);
			}
		}

		return false;
	}

	private static FeeInfo ComputeFeeRateFromDescendants(TreeNode<UnconfirmedTransactionChainItem> root)
	{
		if (root.EffectiveFeeInfo is { } alreadyComputedFeeInfo)
		{
			return alreadyComputedFeeInfo;
		}

		// If no children, then immediately return with the values of the current node.
		if (root.Children.Count == 0)
		{
			// Store the computed info to avoid new computation if you search through this branch again.
			root.EffectiveFeeInfo = new FeeInfo(root.Value.Fee, root.Value.Size);

			return root.EffectiveFeeInfo;
		}

		// If there are some children, recursively call the function to get the leaves first (bottom up).
		List<FeeInfo> childrenFeeRate = root.Children.Select(ComputeFeeRateFromDescendants).ToList();

		// Select only the best chain from each iteration to follow Bitcoin Core's default behaviour.
		var bestDescendant = childrenFeeRate.MaxBy(x => (double)x.TotalFee.Satoshi / x.TotalSize);

		// If the transaction's fee rate is higher than the fee rate of its best descendant, there is no CPFP.
		// Otherwise, we have to return the total of the best descendant chain + the current node.
		root.EffectiveFeeInfo = (double)root.Value.Fee.Satoshi / root.Value.Size > (double)bestDescendant!.TotalFee.Satoshi / bestDescendant.TotalSize ?
			new FeeInfo(root.Value.Fee, root.Value.Size) :
			new FeeInfo(bestDescendant.TotalFee + root.Value.Fee, bestDescendant.TotalSize + root.Value.Size);

		return root.EffectiveFeeInfo;
	}

	// TODO: This method is O(n2), consider mapping the existing nodes in a dictionary to avoid FirstOrDefault lookup and make it O(n).
	private static List<TreeNode<UnconfirmedTransactionChainItem>> ConstructTransactionsTree(
		IReadOnlyCollection<UnconfirmedTransactionChainItem> unconfirmedTransactionChain)
	{
		// First, add the roots to the tree. Roots are transactions without parents.
		var tree = unconfirmedTransactionChain
			.Where(x => x.Parents.Count == 0)
			.Select(root => new TreeNode<UnconfirmedTransactionChainItem>(root))
			.ToList();

		// Then, go recursively through all nodes to find and add their children.
		var nextNodes = new Queue<TreeNode<UnconfirmedTransactionChainItem>>();
		foreach (var root in tree)
		{
			nextNodes.Enqueue(root);
		}

		while (nextNodes.Count > 0)
		{
			var currentNode = nextNodes.Dequeue();
			var children = unconfirmedTransactionChain.Where(x => x.Parents.Contains(currentNode.Value.TxId));
			foreach (var child in children)
			{

				// If the node for child's transaction was not already in the tree, create it, enqueue it to go through all its children, and add it as a child for current node.
				// If it was already in the tree, just add it as a child, but no need to enqueue it again as it was already done when creating it.
				var childNode = tree.FirstOrDefault(x => x.Value.TxId == child.TxId);
				if (childNode == default(TreeNode<UnconfirmedTransactionChainItem>))
				{
					childNode = new TreeNode<UnconfirmedTransactionChainItem>(child);
					nextNodes.Enqueue(childNode);
				}
				currentNode.AddChild(childNode);
			}
		}

		return tree;
	}

	public class TreeNode<T>(T value)
	{
		public T Value { get; } = value;
		public List<TreeNode<T>> Children { get; } = new();
		public List<TreeNode<T>> Parents { get; } = new();
		public FeeInfo? EffectiveFeeInfo { get; set; }

		public void AddChild(TreeNode<T> child)
		{
			Children.Add(child);
			child.Parents.Add(this);
		}
	}

	public record FeeInfo(Money TotalFee, int TotalSize);

}
