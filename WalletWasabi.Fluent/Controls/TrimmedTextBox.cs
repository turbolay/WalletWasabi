using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using WalletWasabi.Fluent.Helpers;

namespace WalletWasabi.Fluent.Controls
{
	public class TrimmedTextBox : TextBox
	{
		protected override Type StyleKeyOverride => typeof(TextBox);

		protected override void OnTextInput(TextInputEventArgs e)
		{
			var input = e.Text == null ? "" : e.Text.TotalTrim();

			// Reject space char input.
			if (string.IsNullOrWhiteSpace(input))
			{
				e.Handled = true;
				base.OnTextInput(e);
				return;
			}

			base.OnTextInput(e);
		}

		protected override void OnKeyDown(KeyEventArgs e)
        {
        	DoPasteCheck(e);
        }

        private void DoPasteCheck(KeyEventArgs e)
        {
        	var keymap = Application.Current?.PlatformSettings?.HotkeyConfiguration;

        	bool Match(IEnumerable<KeyGesture> gestures) => gestures.Any(g => g.Matches(e));

        	if (keymap is { } && Match(keymap.Paste))
        	{
        		ModifiedPasteAsync();
        	}
        	else
        	{
        		base.OnKeyDown(e);
        	}
		}

        private async void ModifiedPasteAsync()
        {
        	var text = await ApplicationHelper.GetTextAsync();

        	if (string.IsNullOrEmpty(text))
        	{
        		return;
        	}

        	text = text.TotalTrim();

	        OnTextInput(new TextInputEventArgs { Text = text});

        	Dispatcher.UIThread.Post(() =>
        	{
        		ClearSelection();
        		CaretIndex = Text?.Length ?? 0;
        	});
        }
	}
}
