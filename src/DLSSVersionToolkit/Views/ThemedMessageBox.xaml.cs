using System.Windows;

namespace DLSSVersionToolkit.Views;

/// <summary>
/// Themed replacement for the native Win32 MessageBox (v0.0.59).
///
/// Native MessageBox renders OS-chrome — bright white in Windows light theme — in the middle of
/// an all-dark app. Every call site routes through <see cref="Show"/> so the whole app speaks one
/// visual language. Same modal semantics: returns the button the user clicked, owner taken from
/// the active window.
///
/// NOT a re-implementation of MessageBox's full contract: no default-button parameter (Tab order
/// is Yes/No/Cancel left-to-right, primary first), no help button, no options row. If a future
/// call site needs those, extend this class — do not fall back to MessageBox.Show for one site.
/// </summary>
public partial class ThemedMessageBox : Window
{
    private MessageBoxResult _result = MessageBoxResult.None;

    private ThemedMessageBox()
    {
        InitializeComponent();
    }

    /// <summary>Show a themed message box. Mirrors the MessageBox.Show(title-last) shape the
    /// codebase already uses, so call-site migration is mechanical.</summary>
    public static MessageBoxResult Show(
        string messageBoxText,
        string caption = "",
        MessageBoxButton button = MessageBoxButton.OK,
        MessageBoxImage icon = MessageBoxImage.None)
    {
        var box = new ThemedMessageBox
        {
            Title = caption,
            Owner = System.Windows.Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
        };

        box.MessageText.Text = messageBoxText;
        box.IconText.Text = IconGlyph(icon);
        box.IconText.Foreground = IconBrush(icon);

        switch (button)
        {
            case MessageBoxButton.OK:
                box.ButtonsPanel.Children.Add(MakeButton("OK", MessageBoxResult.OK, isPrimary: true));
                break;
            case MessageBoxButton.OKCancel:
                box.ButtonsPanel.Children.Add(MakeButton("OK", MessageBoxResult.OK, isPrimary: true));
                box.ButtonsPanel.Children.Add(MakeButton("Cancel", MessageBoxResult.Cancel));
                break;
            case MessageBoxButton.YesNo:
                box.ButtonsPanel.Children.Add(MakeButton("Yes", MessageBoxResult.Yes, isPrimary: true));
                box.ButtonsPanel.Children.Add(MakeButton("No", MessageBoxResult.No));
                break;
            case MessageBoxButton.YesNoCancel:
                box.ButtonsPanel.Children.Add(MakeButton("Yes", MessageBoxResult.Yes, isPrimary: true));
                box.ButtonsPanel.Children.Add(MakeButton("No", MessageBoxResult.No));
                box.ButtonsPanel.Children.Add(MakeButton("Cancel", MessageBoxResult.Cancel));
                break;
        }

        // Escape cancels when a Cancel exists; otherwise it activates the only button.
        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Escape) return;
            var cancelable = button is MessageBoxButton.OKCancel or MessageBoxButton.YesNoCancel;
            box._result = cancelable ? MessageBoxResult.Cancel : MessageBoxResult.OK;
            box.Close();
        };

        box.ShowDialog();
        return box._result == MessageBoxResult.None ? MessageBoxResult.Cancel : box._result;
    }

    private static System.Windows.Controls.Button MakeButton(string label, MessageBoxResult result, bool isPrimary = false)
    {
        var b = new System.Windows.Controls.Button
        {
            Content = label,
            MinWidth = 96,
            Margin = new Thickness(6, 0, 0, 0),
            Style = (Style)System.Windows.Application.Current!.FindResource(
                isPrimary ? "PrimaryButtonStyle" : "DarkButtonStyle")
        };
        b.Click += (snd, _) =>
        {
            // Walk the visual tree to the hosting Window — buttons have no Window property.
            var dep = (System.Windows.DependencyObject)snd!;
            while (dep is not null && dep is not Window)
                dep = System.Windows.Media.VisualTreeHelper.GetParent(dep);
            ((ThemedMessageBox)dep)._CloseWith(result);
        };
        return b;
    }

    private void _CloseWith(MessageBoxResult result)
    {
        _result = result;
        Close();
    }

    private static string IconGlyph(MessageBoxImage icon) => icon switch
    {
        MessageBoxImage.Error => "✕",
        MessageBoxImage.Warning => "!",
        MessageBoxImage.Question => "?",
        MessageBoxImage.Information => "✓",
        _ => ""
    };

    private static System.Windows.Media.Brush IconBrush(MessageBoxImage icon) =>
        (System.Windows.Media.Brush)System.Windows.Application.Current!.FindResource(icon switch
        {
            MessageBoxImage.Error => "ErrorBrush",
            MessageBoxImage.Warning => "WarningBrush",
            MessageBoxImage.Question => "NvidiaGreenBrush",
            MessageBoxImage.Information => "SuccessBrush",
            _ => "Text2Brush"
        });
}
