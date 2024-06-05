using Avalonia;
using Avalonia.Controls;
using WalletWasabi.Fluent.Helpers;

namespace WalletWasabi.Fluent.Controls;

public class UriEntryBox : TextBox
{
	protected override Type StyleKeyOverride => typeof(TextBox);

	public UriEntryBox()
	{
		this.GetObservable(TextProperty).Subscribe(text =>
		{
			if (string.IsNullOrEmpty(text))
			{
				return;
			}

			var trimmedText = text.TotalTrim();
			if (text != trimmedText)
			{
				Text = trimmedText;
			}
		});
	}
}
