using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Launcher.Models;
using Launcher.Services;

// Disambiguate WPF vs WinForms / ambiguous types
using Button              = System.Windows.Controls.Button;
using Brushes             = System.Windows.Media.Brushes;
using FontFamily          = System.Windows.Media.FontFamily;
using Orientation         = System.Windows.Controls.Orientation;
using RichTextBox         = System.Windows.Controls.RichTextBox;
using UserControl         = System.Windows.Controls.UserControl;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment   = System.Windows.VerticalAlignment;

namespace Launcher.Views;

public partial class ModManagementView : UserControl
{
    private readonly MainWindow _main;
    private readonly ManifestService _manifestService = new();
    private readonly ModInstaller _modInstaller = new();
    private readonly BepInExInstaller _bepInExInstaller = new();

    public ModManagementView(MainWindow main)
    {
        InitializeComponent();
        _main = main;
    }

    // ── Load ─────────────────────────────────────────────────────────────────

    public async Task LoadAsync()
    {
        SetStatus("Loading…");
        ModList.Children.Clear();

        var gamePath = _main.GamePath;
        if (gamePath is null)
        {
            ModList.Children.Add(MakeEmptyLabel("Game folder not found."));
            SetStatus("Game folder not set.");
            return;
        }

        // BepInEx card always first
        ModList.Children.Add(await MakeBepInExCardAsync(gamePath));

        // Scan installed DLLs
        var installedMods = ScanInstalled(gamePath);

        // Fetch manifest (beta or standard)
        Manifest? manifest = null;
        try
        {
            manifest = await _manifestService.FetchAsync(_main.Settings.IsBetaEnabled);
        }
        catch (Exception ex)
        {
            SetStatus($"Manifest error: {ex.Message}");
        }

        // Build unified list from manifest
        var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (manifest?.Mods is { Count: > 0 })
        {
            foreach (var mod in manifest.Mods)
            {
                seenFiles.Add(mod.FileName);

                var installed = installedMods.FirstOrDefault(m =>
                    string.Equals(Path.GetFileName(m.FilePath), mod.FileName,
                        StringComparison.OrdinalIgnoreCase));

                bool hasUpdate = installed is not null && IsNewer(mod.Version, installed.Version);
                ModList.Children.Add(MakeModCard(mod, installed, hasUpdate, gamePath));
            }
        }

        // Local-only DLLs (installed but absent from manifest)
        foreach (var inst in installedMods)
        {
            if (seenFiles.Contains(Path.GetFileName(inst.FilePath))) continue;
            ModList.Children.Add(MakeLocalOnlyCard(inst));
        }

        if (ModList.Children.Count == 1)
            ModList.Children.Add(MakeEmptyLabel("No mods found."));

        SetStatus("Ready.");
    }

    // ── Installed DLL scanner ─────────────────────────────────────────────────

    private static List<InstalledMod> ScanInstalled(string gamePath)
    {
        var list = new List<InstalledMod>();
        var pluginsDir = Path.Combine(gamePath, "BepInEx", "plugins");
        if (!Directory.Exists(pluginsDir)) return list;

        foreach (var dll in Directory.GetFiles(pluginsDir, "*.dll", SearchOption.AllDirectories))
        {
            try
            {
                var fvi     = FileVersionInfo.GetVersionInfo(dll);
                var name    = !string.IsNullOrWhiteSpace(fvi.ProductName)
                                ? fvi.ProductName
                                : Path.GetFileNameWithoutExtension(dll);
                // Strip git hash / pre-release suffixes (e.g. "1.0.1+abc123" or "1.0.1-beta")
                // that the .NET SDK can append to ProductVersion, which break Version.Parse.
                var rawVersion = fvi.ProductVersion ?? fvi.FileVersion ?? "?.?.?";
                var version = rawVersion.Split('+')[0].Split('-')[0];
                list.Add(new InstalledMod(name, version, dll));
            }
            catch
            {
                list.Add(new InstalledMod(Path.GetFileNameWithoutExtension(dll), "?.?.?", dll));
            }
        }

        return list;
    }

    // ── Card builders ─────────────────────────────────────────────────────────

    private async Task<Border> MakeBepInExCardAsync(string gamePath)
    {
        bool installed = new GameLocator().IsBepInExInstalled(gamePath);

        var dot = StatusDot(installed ? "BrushSuccess" : "BrushWarning");

        var nameBlock = new TextBlock
        {
            Text              = "BepInEx",
            Foreground        = (SolidColorBrush)FindResource("BrushTextPrimary"),
            FontWeight        = FontWeights.SemiBold,
            FontSize          = 13,
            VerticalAlignment = VerticalAlignment.Center
        };
        var frameworkBlock = new TextBlock
        {
            Text              = "Framework",
            Foreground        = (SolidColorBrush)FindResource("BrushTextMuted"),
            FontSize          = 10,
            Margin            = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var verBlock = new TextBlock
        {
            Text                = installed ? "v6.0.0-be.755" : "not installed",
            Foreground          = (SolidColorBrush)FindResource(installed ? "BrushTextMuted" : "BrushWarning"),
            FontSize            = 11,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment   = VerticalAlignment.Center
        };

        var leftRow = new StackPanel { Orientation = Orientation.Horizontal };
        leftRow.Children.Add(dot);
        leftRow.Children.Add(nameBlock);
        leftRow.Children.Add(frameworkBlock);

        var headerGrid = new Grid();
        headerGrid.Children.Add(leftRow);
        headerGrid.Children.Add(verBlock);

        var stack = new StackPanel();
        stack.Children.Add(headerGrid);

        if (!installed)
        {
            var statusLbl  = MakeStatusLabel();
            var installBtn = new Button
            {
                Content             = "Install BepInEx",
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin              = new Thickness(0, 10, 0, 0)
            };
            installBtn.SetResourceReference(StyleProperty, "BtnInstall");
            installBtn.Click += async (_, _) =>
            {
                installBtn.IsEnabled = false;
                statusLbl.Visibility = Visibility.Visible;
                statusLbl.Text       = "Installing…";
                try
                {
                    await _bepInExInstaller.InstallAsync(gamePath,
                        msg => Dispatcher.Invoke(() => statusLbl.Text = msg));
                    _main.SetGamePath(gamePath);
                    SetStatus("BepInEx installed successfully.");
                    await LoadAsync();
                }
                catch (Exception ex)
                {
                    statusLbl.Foreground = (SolidColorBrush)FindResource("BrushError");
                    statusLbl.Text       = $"Error: {ex.Message}";
                    installBtn.IsEnabled = true;
                }
            };
            stack.Children.Add(installBtn);
            stack.Children.Add(statusLbl);
        }

        return MakeCardBorder(stack);
    }

    private Border MakeModCard(ManifestMod mod, InstalledMod? installed,
                               bool hasUpdate, string gamePath)
    {
        // Status dot
        string dotKey = installed is null ? "BrushTextMuted"
                      : hasUpdate         ? "BrushWarning"
                                          : "BrushSuccess";
        var dot = StatusDot(dotKey);

        // Name
        var nameBlock = new TextBlock
        {
            Text              = mod.Name,
            Foreground        = (SolidColorBrush)FindResource("BrushTextPrimary"),
            FontWeight        = FontWeights.SemiBold,
            FontSize          = 13,
            VerticalAlignment = VerticalAlignment.Center
        };
        var authorBlock = new TextBlock
        {
            Text              = $"by {mod.Author}",
            Foreground        = (SolidColorBrush)FindResource("BrushTextMuted"),
            FontSize          = 10,
            Margin            = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        // Expand chevron
        var chevron = new TextBlock
        {
            Text              = "▶",
            Foreground        = (SolidColorBrush)FindResource("BrushTextMuted"),
            FontSize          = 9,
            Margin            = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        var leftRow = new StackPanel { Orientation = Orientation.Horizontal };
        leftRow.Children.Add(dot);
        leftRow.Children.Add(nameBlock);
        leftRow.Children.Add(authorBlock);
        leftRow.Children.Add(chevron);

        // Version + action buttons on the right
        string verText = installed is null ? $"v{mod.Version}"
                       : hasUpdate         ? $"v{installed.Version} → v{mod.Version}"
                                           : $"v{installed.Version}";

        var verBlock = new TextBlock
        {
            Text              = verText,
            Foreground        = (SolidColorBrush)FindResource(hasUpdate ? "BrushWarning" : "BrushTextMuted"),
            FontSize          = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(0, 0, 6, 0)
        };

        var actionsRow = new StackPanel
        {
            Orientation         = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment   = VerticalAlignment.Center
        };
        actionsRow.Children.Add(verBlock);

        if (installed is null || hasUpdate)
        {
            var actionBtn = new Button
            {
                Content = installed is null ? "Install" : "Update",
                Margin  = new Thickness(6, 0, 0, 0)
            };
            actionBtn.SetResourceReference(StyleProperty, hasUpdate ? "BtnUpdate" : "BtnInstall");
            actionBtn.Click += async (_, _) =>
            {
                actionBtn.IsEnabled = false;
                SetStatus(hasUpdate ? $"Updating {mod.Name}…" : $"Installing {mod.Name}…");
                try
                {
                    await _modInstaller.InstallAsync(mod.DownloadUrl, mod.FileName, gamePath);
                    SetStatus($"{mod.Name} {(hasUpdate ? "updated" : "installed")} successfully.");
                    await Task.Delay(1000);
                    await LoadAsync();
                }
                catch (Exception ex)
                {
                    SetStatus($"Error: {ex.Message}");
                    actionBtn.IsEnabled = true;
                }
            };
            actionsRow.Children.Add(actionBtn);
        }

        if (installed is not null)
        {
            var deleteBtn = new Button
            {
                Content = "Delete",
                Margin  = new Thickness(6, 0, 0, 0)
            };
            deleteBtn.SetResourceReference(StyleProperty, "BtnDanger");
            deleteBtn.Click += async (_, _) =>
            {
                var owner = Window.GetWindow(this);
                bool confirmed = ConfirmDialog.Show(
                    owner!, $"Delete {mod.Name}?",
                    $"This will remove {mod.FileName} from your plugins folder. You can reinstall it at any time.",
                    "Delete");
                if (!confirmed) return;
                try
                {
                    File.Delete(installed.FilePath);
                    SetStatus($"{mod.Name} deleted.");
                    await Task.Delay(500);
                    await LoadAsync();
                }
                catch (Exception ex)
                {
                    SetStatus($"Delete error: {ex.Message}");
                }
            };
            actionsRow.Children.Add(deleteBtn);
        }

        var headerGrid = new Grid { Cursor = System.Windows.Input.Cursors.Hand };
        headerGrid.Children.Add(leftRow);
        headerGrid.Children.Add(actionsRow);

        // Detail panel (description + changelog), collapsed by default
        var detailPanel = new StackPanel
        {
            Margin     = new Thickness(16, 10, 0, 2),
            Visibility = Visibility.Collapsed
        };

        if (!string.IsNullOrWhiteSpace(mod.Description))
            detailPanel.Children.Add(MakeMarkdownBlock(mod.Description));

        if (!string.IsNullOrWhiteSpace(mod.Changelog))
        {
            detailPanel.Children.Add(new TextBlock
            {
                Text       = "CHANGELOG",
                Foreground = (SolidColorBrush)FindResource("BrushTextMuted"),
                FontSize   = 10,
                FontWeight = FontWeights.SemiBold,
                Margin     = new Thickness(0, 10, 0, 4)
            });
            detailPanel.Children.Add(MakeMarkdownBlock(mod.Changelog));
        }

        bool hasDetail = detailPanel.Children.Count > 0;
        if (hasDetail)
        {
            headerGrid.MouseLeftButtonUp += (_, _) =>
            {
                bool open = detailPanel.Visibility != Visibility.Visible;
                detailPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
                chevron.Text           = open ? "▼" : "▶";
            };
        }
        else
        {
            chevron.Visibility = Visibility.Collapsed;
        }

        var stack = new StackPanel();
        stack.Children.Add(headerGrid);
        if (hasDetail) stack.Children.Add(detailPanel);

        return MakeCardBorder(stack);
    }

    private Border MakeLocalOnlyCard(InstalledMod mod)
    {
        var nameBlock = new TextBlock
        {
            Text              = mod.Name,
            Foreground        = (SolidColorBrush)FindResource("BrushTextPrimary"),
            FontWeight        = FontWeights.SemiBold,
            FontSize          = 13,
            VerticalAlignment = VerticalAlignment.Center
        };
        var tagBlock = new TextBlock
        {
            Text              = "(local only)",
            Foreground        = (SolidColorBrush)FindResource("BrushTextMuted"),
            FontSize          = 10,
            Margin            = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var verBlock = new TextBlock
        {
            Text                = $"v{mod.Version}",
            Foreground          = (SolidColorBrush)FindResource("BrushTextMuted"),
            FontSize            = 11,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment   = VerticalAlignment.Center
        };

        var leftRow = new StackPanel { Orientation = Orientation.Horizontal };
        leftRow.Children.Add(nameBlock);
        leftRow.Children.Add(tagBlock);

        var grid = new Grid();
        grid.Children.Add(leftRow);
        grid.Children.Add(verBlock);

        return MakeCardBorder(new StackPanel { Children = { grid } });
    }

    // ── Lightweight markdown renderer ─────────────────────────────────────────
    // Handles: **bold**, *italic*, bullet lists (- or *), blank-line paragraphs.

    private RichTextBox MakeMarkdownBlock(string markdown)
    {
        var doc = new FlowDocument
        {
            FontSize   = 11,
            FontFamily = new FontFamily("Segoe UI"),
            Background = Brushes.Transparent,
            Foreground = (SolidColorBrush)FindResource("BrushTextMuted")
        };

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var paraLines = new List<string>();

        void FlushParagraph()
        {
            if (paraLines.Count == 0) return;
            var para = new Paragraph { Margin = new Thickness(0, 0, 0, 6) };
            foreach (var (line, idx) in paraLines.Select((l, i) => (l, i)))
            {
                if (idx > 0) para.Inlines.Add(new LineBreak());
                foreach (var inline in ParseInlines(line))
                    para.Inlines.Add(inline);
            }
            doc.Blocks.Add(para);
            paraLines.Clear();
        }

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();

            // Bullet list item
            if (line.StartsWith("- ") || (line.StartsWith("* ") && !line.StartsWith("**")))
            {
                FlushParagraph();
                var para = new Paragraph { Margin = new Thickness(12, 0, 0, 3), TextIndent = -12 };
                para.Inlines.Add(new Run("• "));
                foreach (var inline in ParseInlines(line[2..]))
                    para.Inlines.Add(inline);
                doc.Blocks.Add(para);
                continue;
            }

            // Blank line → flush current paragraph
            if (string.IsNullOrWhiteSpace(line))
            {
                FlushParagraph();
                continue;
            }

            paraLines.Add(line);
        }

        FlushParagraph();

        return new RichTextBox
        {
            Document            = doc,
            IsReadOnly          = true,
            Background          = Brushes.Transparent,
            BorderThickness     = new Thickness(0),
            IsDocumentEnabled   = true,
            Foreground          = (SolidColorBrush)FindResource("BrushTextMuted"),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility   = ScrollBarVisibility.Disabled
        };
    }

    private static IEnumerable<Inline> ParseInlines(string text)
    {
        // Matches **bold** and *italic*
        var pattern = @"\*\*(.+?)\*\*|\*(.+?)\*";
        var result  = new List<Inline>();
        int last    = 0;

        foreach (Match m in Regex.Matches(text, pattern))
        {
            if (m.Index > last)
                result.Add(new Run(text[last..m.Index]));

            if (m.Groups[1].Success) // **bold**
                result.Add(new Bold(new Run(m.Groups[1].Value)));
            else if (m.Groups[2].Success) // *italic*
                result.Add(new Italic(new Run(m.Groups[2].Value)));

            last = m.Index + m.Length;
        }

        if (last < text.Length)
            result.Add(new Run(text[last..]));

        return result.Count > 0 ? result : new List<Inline> { new Run(text) };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private System.Windows.Shapes.Ellipse StatusDot(string brushKey)
        => new System.Windows.Shapes.Ellipse
        {
            Width             = 8,
            Height            = 8,
            Fill              = (SolidColorBrush)FindResource(brushKey),
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(0, 0, 8, 0)
        };

    private Border MakeCardBorder(StackPanel content)
        => new Border
        {
            CornerRadius    = new CornerRadius(8),
            Background      = (SolidColorBrush)FindResource("BrushSurface"),
            BorderBrush     = (SolidColorBrush)FindResource("BrushBorder"),
            BorderThickness = new Thickness(1),
            Padding         = new Thickness(14, 11, 14, 11),
            Margin          = new Thickness(0, 0, 0, 10),
            Child           = content
        };

    private TextBlock MakeEmptyLabel(string text)
        => new TextBlock
        {
            Text                = text,
            Foreground          = (SolidColorBrush)FindResource("BrushTextMuted"),
            FontSize            = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin              = new Thickness(0, 20, 0, 0)
        };

    private TextBlock MakeStatusLabel()
        => new TextBlock
        {
            Foreground = (SolidColorBrush)FindResource("BrushTextMuted"),
            FontSize   = 11,
            Margin     = new Thickness(0, 4, 0, 0),
            Visibility = Visibility.Collapsed
        };

    private static bool IsNewer(string? remoteVersion, string? localVersion)
    {
        if (!Version.TryParse(remoteVersion, out var remote)) return false;
        if (!Version.TryParse(localVersion,  out var local))  return true;
        return remote > local;
    }

    private void SetStatus(string message)
        => TxtStatus.Text = message;

    // ── Button handlers ───────────────────────────────────────────────────────

    private void BtnBack_Click(object sender, RoutedEventArgs e)
        => _main.ShowHome();

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        => await LoadAsync();
}
