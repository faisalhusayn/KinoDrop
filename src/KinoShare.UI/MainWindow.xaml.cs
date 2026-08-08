namespace KinoShare.UI;

using System.Threading.Tasks;
using KinoShare.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.Storage.Pickers;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.UI;

/// <summary>
/// The main (and only) window: hosts the <see cref="ShareSessionViewModel"/>
/// and wires pickers, clipboard copy, theme, and confirmation dialogs.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly DispatcherQueueTimer _copyResetTimer;

    /// <summary>Initializes a new instance of the <see cref="MainWindow"/> class.</summary>
    public MainWindow()
    {
        // Resolve the view model BEFORE InitializeComponent: the XAML bindings
        // evaluate during InitializeComponent, so the source must exist first.
        IServiceProvider services = ((App)Application.Current).Services;
        ViewModel = services.GetRequiredService<ShareSessionViewModel>();

        InitializeComponent();

        ConfigureWindow();

        _copyResetTimer = DispatcherQueue.CreateTimer();
        _copyResetTimer.Interval = TimeSpan.FromSeconds(1.5);
        _copyResetTimer.Tick += (_, _) =>
        {
            _copyResetTimer.Stop();
            CopySmbButton.Content = "Copy";
            CopyUserButton.Content = "Copy";
            CopyPassButton.Content = "Copy";
        };
    }

    /// <summary>Gets the window's view model.</summary>
    public ShareSessionViewModel ViewModel { get; }

    private void ConfigureWindow()
    {
        SystemBackdrop = new MicaBackdrop();

        AppWindow.Resize(new SizeInt32(760, 760));
        CenterWindow();

        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "app-icon.ico"));

        Title = "KinoDrop";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarElement);

        ApplyTheme(ViewModel.IsDarkTheme);
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ShareSessionViewModel.IsDarkTheme))
            {
                ApplyTheme(ViewModel.IsDarkTheme);
            }
        };
    }

    private void ApplyTheme(bool isDark)
    {
        RootElement.RequestedTheme = isDark ? ElementTheme.Dark : ElementTheme.Light;
    }

    private void CenterWindow()
    {
        DisplayArea display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
        RectInt32 area = display.WorkArea;
        SizeInt32 size = AppWindow.Size;

        int x = area.X + ((area.Width - size.Width) / 2);
        int y = area.Y + ((area.Height - size.Height) / 2);
        AppWindow.Move(new PointInt32(x, y));
    }

    private void CopyField_Click(object sender, RoutedEventArgs e)
    {
        string? text = (sender as FrameworkElement)?.Tag switch
        {
            "smb" => ViewModel.SmbPath,
            "user" => ViewModel.Username,
            "password" => ViewModel.Password,
            _ => null,
        };

        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        DataPackage package = new();
        package.SetText(text);
        Clipboard.SetContent(package);

        if (sender is Button button)
        {
            button.Content = "Copied ✓";
        }

        _copyResetTimer.Stop();
        _copyResetTimer.Start();
    }

    private async void ChangeFolder_Click(object sender, RoutedEventArgs e)
    {
        ContentDialog warning = new()
        {
            Title = "Change transfer folder?",
            Content = "Your files stay where they are. The new folder is where files will be received from now on. " +
                      "Stop the session first, then pick a folder - a 'KinoShare' folder is created inside it.",
            PrimaryButtonText = "Choose folder...",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot,
        };

        if (await warning.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        string? folder = await PickFolderAsync();
        if (folder is null)
        {
            return;
        }

        string? error = await ViewModel.ChangeFolderAsync(folder);
        if (error is not null)
        {
            await ShowErrorAsync(error);
        }
    }

    private async void EditPassword_Click(object sender, RoutedEventArgs e)
    {
        var passwordBox = new TextBox
        {
            Text = ViewModel.Password ?? string.Empty,
            PlaceholderText = "New connection password",
            MaxLength = 64,
        };

        var panel = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                passwordBox,
                new TextBlock
                {
                    Text = "Used for every session. Your iPhone enters it once; after that Files remembers it. Takes effect from the next session start.",
                    Style = (Style)Application.Current.Resources["CaptionTextStyle"],
                    TextWrapping = TextWrapping.Wrap,
                },
            },
        };

        ContentDialog dialog = new()
        {
            Title = "Connection password",
            Content = panel,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        string? error = await ViewModel.ChangePasswordAsync(passwordBox.Text);
        if (error is not null)
        {
            await ShowErrorAsync(error);
        }
    }

    private async void SendFile_Click(object sender, RoutedEventArgs e)
    {
        FileOpenPicker picker = new(AppWindow.Id);
        picker.FileTypeFilter.Add("*");

        PickFileResult result = await picker.PickSingleFileAsync();
        if (result.Path is null)
        {
            return;
        }

        string? error = await ViewModel.SendFileAsync(result.Path);
        if (error is not null)
        {
            await ShowErrorAsync(error);
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
        => ViewModel.OpenTransferFolder();

    private void InfoBar_CloseButtonClick(InfoBar sender, object args)
        => ViewModel.DismissInfo();

    private async Task<string?> PickFolderAsync()
    {
        FolderPicker picker = new(AppWindow.Id);
        picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;

        PickFolderResult result = await picker.PickSingleFolderAsync();
        return result.Path;
    }

    private async Task ShowErrorAsync(string message)
    {
        ContentDialog dialog = new()
        {
            Title = "KinoDrop",
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = Content.XamlRoot,
        };
        await dialog.ShowAsync();
    }
}
