using System.Windows;
using System.Windows.Media;
using Launcher.Services;
using Launcher.Views;

// Disambiguate WPF vs WinForms
using Application = System.Windows.Application;

namespace Launcher;

public partial class MainWindow : Window
{
    private readonly GameLocator _locator = new();
    private HomeView _homeView;
    private ModManagementView _modView;

    public string? GamePath { get; private set; }
    public bool HasBepInEx { get; private set; }

    /// <summary>Loaded once on startup; shared with all views.</summary>
    public SettingsService Settings { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        Settings.Load();

        _homeView = new HomeView(this);
        _modView  = new ModManagementView(this);
        ShowHome();
        Loaded += async (_, _) => await InitializeAsync();
    }

    // ── Navigation ──────────────────────────────────────────────────────────

    public void ShowHome()
    {
        MainContent.Content = _homeView;
        _homeView.UpdateState(GamePath, HasBepInEx);
    }

    public async void ShowModManagement()
    {
        MainContent.Content = _modView;
        await _modView.LoadAsync();
    }

    // ── Initialisation ───────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        GamePath   = _locator.FindGamePath();
        HasBepInEx = GamePath is not null && _locator.IsBepInExInstalled(GamePath);

        UpdateStatusBar();
        _homeView.UpdateState(GamePath, HasBepInEx);

        // Fire-and-forget launcher update check
        await _homeView.CheckForLauncherUpdateAsync();
    }

    private void UpdateStatusBar()
    {
        if (HasBepInEx)
        {
            DotBepInEx.Fill       = (SolidColorBrush)FindResource("BrushSuccess");
            TxtBepInExStatus.Text = "BepInEx: installed";
            TxtBepInExStatus.Foreground = (SolidColorBrush)FindResource("BrushSuccess");
        }
        else
        {
            DotBepInEx.Fill       = (SolidColorBrush)FindResource("BrushWarning");
            TxtBepInExStatus.Text = "BepInEx: not installed";
            TxtBepInExStatus.Foreground = (SolidColorBrush)FindResource("BrushWarning");
        }

        if (GamePath is not null)
        {
            DotGame.Fill       = (SolidColorBrush)FindResource("BrushSuccess");
            TxtGameStatus.Text = "Game: found";
            TxtGameStatus.Foreground = (SolidColorBrush)FindResource("BrushSuccess");
        }
        else
        {
            DotGame.Fill       = (SolidColorBrush)FindResource("BrushError");
            TxtGameStatus.Text = "Game: not found";
            TxtGameStatus.Foreground = (SolidColorBrush)FindResource("BrushError");
        }
    }

    // ── Game folder browse ───────────────────────────────────────────────────

    public void OpenFolderBrowser()
    {
        var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description            = "Select the LumenTale: Memories of Trey game folder",
            UseDescriptionForTitle = true,
            ShowNewFolderButton    = false
        };

        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            SetGamePath(dlg.SelectedPath);
        }
    }

    public void SetGamePath(string path)
    {
        GamePath   = path;
        HasBepInEx = _locator.IsBepInExInstalled(path);
        UpdateStatusBar();
        _homeView.UpdateState(GamePath, HasBepInEx);
    }

    // ── Title bar chrome ─────────────────────────────────────────────────────

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1)
            DragMove();
    }

    private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void BtnClose_Click(object sender, RoutedEventArgs e)
        => Application.Current.Shutdown();
}
