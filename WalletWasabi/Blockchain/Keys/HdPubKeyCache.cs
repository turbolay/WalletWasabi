using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using NBitcoin;

namespace WalletWasabi.Blockchain.Keys;

public class HdPubKeyCache : IEnumerable<HdPubKeyInfo>
{
	private int _currentCapacity = 1_000;
	private readonly Dictionary<Script, HdPubKeyInfo> _hdPubKeyIndexedByScriptPubKey = new(1_000);

	public IEnumerable<HdPubKey> HdPubKeys =>
		_hdPubKeyIndexedByScriptPubKey.Values.Select(x => x.HdPubKey);

	public bool TryGetPubKey(Script destination, [NotNullWhen(true)] out HdPubKey? hdPubKey)
	{
		if (!_hdPubKeyIndexedByScriptPubKey.TryGetValue(destination, out var hdPubKeyInfo))
		{
			hdPubKey = null;
			return false;
		}

		hdPubKey = hdPubKeyInfo.HdPubKey;
		return true;
	}

	public HdPubKeyPathView GetView(KeyPath keyPath) =>
		new(_hdPubKeyIndexedByScriptPubKey.Values.Select(x => x.HdPubKey).Where(x => x.FullKeyPath.Parent == keyPath));

	public IEnumerable<HdPubKey> AddRangeKeys(IEnumerable<HdPubKey> keys)
	{
		foreach (var key in keys)
		{
			AddKey(key, key.FullKeyPath.GetScriptTypeFromKeyPath());
		}

		return keys;
	}

	public void AddKey(HdPubKey hdPubKey, ScriptPubKeyType scriptPubKeyType)
	{
		if (_hdPubKeyIndexedByScriptPubKey.Count == _currentCapacity)
		{
			_currentCapacity = _hdPubKeyIndexedByScriptPubKey.EnsureCapacity(2 * _currentCapacity);
		}

		var info = new HdPubKeyInfo(hdPubKey, scriptPubKeyType);
		_hdPubKeyIndexedByScriptPubKey[info.ScriptPubKey] = info;
	}

	public IEnumerator<HdPubKeyInfo> GetEnumerator() =>
		_hdPubKeyIndexedByScriptPubKey
			.Values
			.OrderBy(x => x.HdPubKey.Index)
			.GetEnumerator();

	IEnumerator IEnumerable.GetEnumerator() =>
		GetEnumerator();
}
