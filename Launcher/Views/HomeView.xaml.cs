using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using Launcher.Services;

// Disambiguate WPF vs WinForms types
using Application = System.Windows.Application;
using MessageBox  = System.Windows.MessageBox;
using UserControl = System.Windows.Controls.UserControl;

namespace Launcher.Views;

public partial class HomeView : UserControl
{
    private readonly MainWindow _main;
    private string? _gamePath;
    private bool _hasBepInEx;
    private StagedUpdate? _stagedUpdate;

    private readonly LauncherUpdateService _updateService = new();

    public HomeView(MainWindow main)
    {
        InitializeComponent();
        _main = main;
    }

    public void UpdateState(string? gamePath, bool hasBepInEx)
    {
        _gamePath   = gamePath;
        _hasBepInEx = hasBepInEx;

        // Play button enabled only when game is found
        BtnPlay.IsEnabled = gamePath is not null;

        // BepInEx warning
        TxtBepInExWarning.Visibility = (!hasBepInEx && gamePath is not null)
            ? Visibility.Visible
            : Visibility.Collapsed;

        // Locate button
        BtnLocate.Visibility = gamePath is null
            ? Visibility.Visible
            : Visibility.Collapsed;

        // Console toggle — read current setting, suppress the Changed event while setting it
        ChkConsole.IsEnabled = gamePath is not null;
        if (gamePath is not null)
        {
            ChkConsole.Checked   -= ChkConsole_Changed;
            ChkConsole.Unchecked -= ChkConsole_Changed;
            ChkConsole.IsChecked  = BepInExConfig.GetConsoleEnabled(gamePath);
            ChkConsole.Checked   += ChkConsole_Changed;
            ChkConsole.Unchecked += ChkConsole_Changed;
        }

        // Beta UI
        RefreshBetaUI();
    }

    /// <summary>
    /// Checks for a newer launcher release and stages it in the background.
    /// Shows a banner while downloading and again when the update is ready
    /// to install. Called once after the window loads.
    /// </summary>
    public async Task CheckForLauncherUpdateAsync()
    {
        try
        {
            var staged = await _updateService.CheckAndStageAsync(msg =>
                Dispatcher.Invoke(() =>
                {
                    // Show the banner in "downloading" state (button disabled).
                    TxtUpdateAvailable.Text = msg;
                    BtnUpdate.IsEnabled     = false;
                    BannerUpdate.Visibility = Visibility.Visible;
                }));

            if (staged is null) return; // up to date

            _stagedUpdate               = staged;
            TxtUpdateAvailable.Text     = $"✦  Launcher v{staged.Version} is ready to install";
            BtnUpdate.IsEnabled         = true;
            BannerUpdate.Visibility     = Visibility.Visible;
        }
        catch
        {
            // Network failures are silently ignored for the update check.
        }
    }

    // ── Beta UI ───────────────────────────────────────────────────────────────

    private void RefreshBetaUI()
    {
        var settings = _main.Settings;

        if (settings.IsBetaEnabled)
        {
            BetaActiveRow.Visibility = Visibility.Visible;
            BetaEntryRow.Visibility  = Visibility.Collapsed;
        }
        else
        {
            BetaActiveRow.Visibility = Visibility.Collapsed;
            BetaEntryRow.Visibility  = Visibility.Visible;
        }

        TxtBetaStatus.Visibility = Visibility.Collapsed;
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void BtnPlay_Click(object sender, RoutedEventArgs e)
    {
        if (_gamePath is null) return;

        // Enable BepInEx doorstop
        var doorstopCfg = Path.Combine(_gamePath, "doorstop_config.ini");
        if (File.Exists(doorstopCfg))
        {
            var lines = File.ReadAllLines(doorstopCfg);
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].TrimStart().StartsWith("enabled", StringComparison.OrdinalIgnoreCase))
                    lines[i] = "enabled=true";
            }
            File.WriteAllLines(doorstopCfg, lines);
        }

        // Launch via Steam URI
        Process.Start(new ProcessStartInfo
        {
            FileName        = "steam://rungameid/2261430",
            UseShellExecute = true
        });

        Application.Current.Shutdown();
    }

    private void BtnMods_Click(object sender, RoutedEventArgs e)
        => _main.ShowModManagement();

    private void BtnLocate_Click(object sender, RoutedEventArgs e)
        => _main.OpenFolderBrowser();

    private void BtnCopyPath_Click(object sender, RoutedEventArgs e)
    {
        var path = Process.GetCurrentProcess().MainModule?.FileName
                   ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
        System.Windows.Clipboard.SetText(path);
        BtnCopyPath.Content = "✓  Copied!";
        var timer = new System.Windows.Threading.DispatcherTimer
            { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) =>
        {
            BtnCopyPath.Content = "📋  Copy Launcher Path for Steam";
            timer.Stop();
        };
        timer.Start();
    }

    private void ChkConsole_Changed(object sender, RoutedEventArgs e)
    {
        if (_gamePath is null) return;
        BepInExConfig.SetConsoleEnabled(_gamePath, ChkConsole.IsChecked == true);
    }

    private async void BtnUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_stagedUpdate is null) return;

        BtnUpdate.IsEnabled = false;

        try
        {
            await _updateService.ApplyUpdateAsync(_stagedUpdate, msg =>
                Dispatcher.Invoke(() => TxtUpdateAvailable.Text = msg));

            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            TxtUpdateAvailable.Text = $"Update failed: {ex.Message}";
            BtnUpdate.IsEnabled     = true;
        }
    }

    private void BtnActivateBeta_Click(object sender, RoutedEventArgs e)
    {
        var token = TxtBetaToken.Text.Trim();
        if (string.IsNullOrEmpty(token)) return;

        if (_main.Settings.ValidateAndEnableBeta(token))
        {
            TxtBetaToken.Text        = string.Empty;
            TxtBetaStatus.Visibility = Visibility.Collapsed;
            RefreshBetaUI();
        }
        else
        {
            TxtBetaStatus.Text       = "Invalid token.";
            TxtBetaStatus.Visibility = Visibility.Visible;
        }
    }

    private void BtnDisableBeta_Click(object sender, RoutedEventArgs e)
    {
        _main.Settings.DisableBeta();
        RefreshBetaUI();
    }

    private void LinkBugReport_Navigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName        = e.Uri.AbsoluteUri,
            UseShellExecute = true
        });
        e.Handled = true;
    }
}
