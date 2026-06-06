using System.Windows;

namespace Launcher.Views;

public partial class ConfirmDialog : Window
{
    public bool Confirmed { get; private set; }

    private ConfirmDialog(string title, string message, string confirmLabel)
    {
        InitializeComponent();
        TxtTitle.Text   = title;
        TxtMessage.Text = message;
        BtnYes.Content  = confirmLabel;
    }

    /// <summary>
    /// Shows a themed confirmation dialog owned by <paramref name="owner"/>.
    /// Returns true if the user clicked the confirm button.
    /// </summary>
    public static bool Show(Window owner, string title, string message,
                            string confirmLabel = "Delete")
    {
        var dlg = new ConfirmDialog(title, message, confirmLabel)
        {
            Owner = owner
        };
        dlg.ShowDialog();
        return dlg.Confirmed;
    }

    private void BtnYes_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close();
    }

    private void BtnNo_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = false;
        Close();
    }
}
