using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using UnlockFps.Services;
using UnlockFps.Utils;
using UnlockFps.ViewModels;

namespace UnlockFps.Views;

public class InitializationWindowViewModel : ViewModelBase
{
    public required Config Config { get; init; }
    public bool IsSearching { get; set; } = true;
    public ObservableCollection<string> InstallationPaths { get; } = new();
    public string? SelectedInstallationPath { get; set; }
}

public partial class InitializationWindow : Window
{
    private readonly ConfigService _configService;
    private readonly InitializationWindowViewModel _viewModel;

    private CancellationTokenSource? _cts;

#if DEBUG
    public InitializationWindow()
    {
        if (!Design.IsDesignMode) throw new InvalidOperationException();
        InitializeComponent();
    }
#endif

    public InitializationWindow(ConfigService configService)
    {
        this.SetSystemChrome();
        _configService = configService;
        DataContext = _viewModel = new InitializationWindowViewModel()
        {
            Config = _configService.Config,
            SelectedInstallationPath = configService.Config.LaunchOptions.GamePath
        };

        InitializeComponent();
    }

    private async void Control_OnLoaded(object? sender, RoutedEventArgs e)
    {
        _cts = new CancellationTokenSource();
        StartSearchWindow();
        await Task.Run(() => SearchRegistry(_cts.Token));
    }

    private void Control_OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (_cts is { } cts)
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    private void StartSearchWindow()
    {
        Task.Run(async () =>
        {
            while (_cts is { Token: { IsCancellationRequested: false } token })
            {
                if (await FindWindowAsync())
                {
                    break;
                }

                await Task.Delay(1000, token);
            }

            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var infoWindow = App.DefaultServices.GetRequiredService<AlertWindow>();
                infoWindow.IsError = false;
                infoWindow.Text = $"""
                                   Game Found!
                                   {_configService.Config.LaunchOptions.GamePath}
                                   """;
                await infoWindow.ShowDialog(this);
                Close();
            });
        });
    }

    private ValueTask<bool> FindWindowAsync()
    {
        IntPtr windowHandle = IntPtr.Zero;
        IntPtr processHandle = IntPtr.Zero;
        string processPath = string.Empty;

        NativeMethods.EnumWindows((hWnd, lParam) =>
        {
            var win32Window = new Win32Window(hWnd);
            if (win32Window.ClassName != "UnityWndClass") return true;

            var err = NativeMethods.GetWindowThreadProcessId(hWnd, out var pid);
            if (err == 0) return true;

            if (!ProcessUtils.TryGetGameProcessFromPid(pid, out processPath, out processHandle))
                return true;

            windowHandle = hWnd;
            return false;
        }, IntPtr.Zero);

        if (windowHandle == IntPtr.Zero)
            return ValueTask.FromResult(false);

        NativeMethods.TerminateProcess(processHandle, 0);
        NativeMethods.CloseHandle(processHandle);

        _configService.Config.LaunchOptions.GamePath = Path.GetFullPath(processPath);
        _configService.Save();
        return ValueTask.FromResult(true);
    }

#pragma warning disable CA1416
    private void SearchRegistry(CancellationToken token = default)
    {
        if (_viewModel == null) return;
        var installationPaths = _viewModel.InstallationPaths;
        TryAddInstallationPath(
            Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Cognosphere\HYP\1_1\hk4e_global"),
            "GameInstallPath",
            "GenshinImpact.exe",
            installationPaths,
            token);
        TryAddInstallationPath(
            Registry.CurrentUser.OpenSubKey(@"SOFTWARE\miHoYo\HYP\1_1\hk4e_cn"),
            "GameInstallPath",
            "YuanShen.exe",
            installationPaths,
            token);

        var selectedPath = _viewModel.SelectedInstallationPath;
        if (installationPaths.Count > 0 && (selectedPath == null || !installationPaths.Contains(selectedPath)))
        {
            _viewModel.SelectedInstallationPath = installationPaths[0];
        }

        _viewModel.IsSearching = false;
    }
#pragma warning restore CA1416

    private static void TryAddInstallationPath(RegistryKey? key, string valueName, string exeName,
        ObservableCollection<string> installationPaths, CancellationToken token)
    {
        using (key)
        {
            if (token.IsCancellationRequested || key == null) return;

            var installPath = key.GetValue(valueName) as string;
            if (string.IsNullOrWhiteSpace(installPath)) return;

            var gamePath = Path.GetFullPath(Path.Combine(installPath, exeName));
            if (!File.Exists(gamePath)) return;

            Dispatcher.UIThread.Invoke(() =>
            {
                if (!installationPaths.Contains(gamePath))
                {
                    installationPaths.Add(gamePath);
                }
            });
        }
    }

    private void BtnConfirm_OnClick(object? sender, RoutedEventArgs e)
    {
        var selectedPath = _viewModel.SelectedInstallationPath;
        if (string.IsNullOrEmpty(selectedPath))
            return;

        _configService.Config.LaunchOptions.GamePath = selectedPath;
        _configService.Save();
        Close();
    }

    private async void BtnBrowse_OnClick(object? sender, RoutedEventArgs e)
    {
        var selectedPath = (await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select GenshinImpact.exe or YuanShen.exe",
            FileTypeFilter =
            [
                new FilePickerFileType("Executable Files (*.exe)") { Patterns = ["GenshinImpact.exe;YuanShen.exe"] }
            ],
            AllowMultiple = true
        })).FirstOrDefault()?.TryGetLocalPath();
        if (selectedPath == null) return;

        var fileName = Path.GetFileNameWithoutExtension(selectedPath);

        if (fileName != "GenshinImpact" && fileName != "YuanShen")
        {
            var alertWindow = App.DefaultServices.GetRequiredService<AlertWindow>();
            alertWindow.Text =
                $"""
                 Please select the game exe
                 GenshinImpact.exe or YuanShen.exe
                 """;
            await alertWindow.ShowDialog(this);
            return;
        }

        var directory = Path.GetDirectoryName(selectedPath);
        var unityPlayer = Path.Combine(directory, "UnityPlayer.dll");
        if (!File.Exists(unityPlayer))
        {
            var alertWindow = App.DefaultServices.GetRequiredService<AlertWindow>();
            alertWindow.Text = "That's not the right place";
            await alertWindow.ShowDialog(this);
            return;
        }

        _configService.Config.LaunchOptions.GamePath = selectedPath;
        _configService.Save();
        Close();
    }
}
