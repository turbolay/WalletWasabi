using Avalonia.Data.Converters;

namespace WalletWasabi.Fluent.Converters;

public static class FeeRateConverters
{
	public static readonly IValueConverter RoundedTwoDecimalsNoTrailing =
		new FuncValueConverter<decimal, string>(x => $"{x:0.##} sat/vByte");

}
