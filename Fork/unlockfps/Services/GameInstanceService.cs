using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using Milki.Extensions.Threading;
using UnlockFps.Logging;
using UnlockFps.Utils;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;
using static Windows.Win32.PInvoke;

namespace UnlockFps.Services;

[SupportedOSPlatform("windows5.0")]
public class GameInstanceService : IDisposable, INotifyPropertyChanged
{
    public event Action<uint>? ProcessExit;

    private static readonly ILogger Logger = LogManager.GetLogger(nameof(GameInstanceService));
    private static readonly string[] RequiredModules = ["UnityPlayer.dll", "UserAssembly.dll"];
    private const uint MonitoringProcessAccess =
        ProcessAccess.QUERY_INFORMATION |
        ProcessAccess.QUERY_LIMITED_INFORMATION |
        ProcessAccess.SET_INFORMATION |
        ProcessAccess.VM_OPERATION |
        ProcessAccess.VM_READ |
        ProcessAccess.VM_WRITE |
        StandardAccess.SYNCHRONIZE;

    private readonly Config _config;

    private HWINEVENTHOOK _winEventHook;

    private static readonly uint[] PriorityClasses =
    [
        PriorityClass.REALTIME,
        PriorityClass.HIGH,
        PriorityClass.ABOVE_NORMAL,
        PriorityClass.NORMAL,
        PriorityClass.BELOW_NORMAL,
        PriorityClass.IDLE
    ];

    private SynchronizationContext? _hwndSynchronizationContext;
    private readonly SynchronizationContext _synchronizationContext = new SingleSynchronizationContext("WinEventHook Callback");

    private WINEVENTPROC _eventCallBack;
    private Timer _timer;
    private bool _isRunning;
    private ProcessContext? _context;

    public GameInstanceService(ConfigService configService)
    {
        _config = configService.Config;
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set => SetField(ref _isRunning, value);
    }

    internal ProcessContext? Context
    {
        get => _context;
        private set
        {
            _context = value;
            IsRunning = value is { IsFpsApplied: true };
        }
    }

    // https://blog.walterlv.com/post/monitor-foreground-window-on-windows
    public void Start()
    {
        if (_winEventHook != default) return;

        if (SynchronizationContext.Current == null)
        {
            _hwndSynchronizationContext ??= new SingleSynchronizationContext("HWND SynchronizationContext", true);
            SynchronizationContext.SetSynchronizationContext(_hwndSynchronizationContext);
            _hwndSynchronizationContext.Post(_ =>
            {
                Thread.CurrentThread.Name = "HWND SynchronizationContext";
            }, null);
        }

        SynchronizationContext.Current!.Post(a =>
        {
            if (string.IsNullOrEmpty(Thread.CurrentThread.Name))
            {
                Thread.CurrentThread.Name = "Default SynchronizationContext";
            }

            if (!WineHelper.DetectWine(out _, out _) && _config.WindowQueryUseEvent)
            {
                Logger.LogInformation($"[{Thread.CurrentThread.Name}] Attempting to find game window (Event Mode)");

                _eventCallBack = WinEventProc;
                _winEventHook = SetWinEventHook(
                    EVENT_SYSTEM_FOREGROUND,
                    EVENT_SYSTEM_FOREGROUND,
                    HMODULE.Null,
                    _eventCallBack,
                    0,
                    0,
                    WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS
                );
                if (_hwndSynchronizationContext == null) return;
                if (GetMessage(out var lpMsg, default, default, default))
                {
                    TranslateMessage(in lpMsg);
                    DispatchMessage(in lpMsg);
                }
            }
            else
            {
                Logger.LogInformation($"[{Thread.CurrentThread.Name}] Attempting to find game window (Timer Mode)");

                nint lastWindow = 0;
                _timer = new Timer(_ =>
                {
                    var foregroundWindow = GetForegroundWindow();
                    if (lastWindow != foregroundWindow)
                    {
                        var win32Window = new Win32Window(foregroundWindow);
                        _synchronizationContext.Send(CallBack, win32Window);
                        lastWindow = foregroundWindow;
                    }
                }, null, 300, 300);
            }
        }, null);

        var win32Window = new Win32Window(GetForegroundWindow());
        _synchronizationContext.Send(CallBack, win32Window);
    }

    public void Stop()
    {
        if (_hwndSynchronizationContext != null)
        {
            _hwndSynchronizationContext?.Post(_ =>
            {
                if (!_winEventHook.IsNull && UnhookWinEvent(_winEventHook))
                {
                    _winEventHook = default;
                }
            }, null);
        }
        else
        {
            if (!_winEventHook.IsNull && UnhookWinEvent(_winEventHook))
            {
                _winEventHook = default;
            }
        }

        _eventCallBack = default;
        _timer?.Dispose();
        Context?.CancellationTokenSource.Cancel();
    }

    private void WinEventProc(HWINEVENTHOOK hWinEventHook, uint @event, HWND hwnd,
        int idObject, int idChild, uint idEventThread, uint dwmsEventTime)
    {
        if (Context != null) return;
        var win32Window = new Win32Window(hwnd);
        _synchronizationContext.Send(CallBack, win32Window);
    }

    private void CallBack(object? state)
    {
        try
        {
            if (state is not Win32Window win32Window) return;
            ApplyContext(win32Window);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "WinEventHook Callback Error");
            throw;
        }
    }

    private void ApplyContext(Win32Window win32Window)
    {
        if (Context != null) return;
        if (win32Window.ProcessId == 0)
        {
            Logger.LogDebug($"Invalid window: {win32Window.Handle}");
            return;
        }

        var text =
            $"[0x{win32Window.Handle:X16} {win32Window.ClassName}] ({win32Window.ProcessId} {win32Window.ProcessName}.exe) {win32Window.Title}";
        if (!CheckProcess(win32Window.ProcessId, out var processContext))
        {
            Logger.LogDebug($"Invalid window: {text}");
            return;
        }

        processContext.Win32Window = win32Window;

        Logger.LogInformation($"Find the game window: {text}");
        Logger.LogInformation("Start applying FPS.");

        Task.Factory.StartNew(() =>
        {
            processContext.IsFpsApplied = true;
            IsRunning = true;
            ApplyFpsLoop(processContext.CancellationTokenSource.Token);
            Context?.Dispose();
        }, TaskCreationOptions.LongRunning);
    }

    private void ApplyFpsLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            if (Context is not { NativeProcess.HasExited: false } processContext) break;

            ApplyFpsLimit(processContext);
            if (!TaskUtils.TaskSleep(200, token)) return;
        }

        if (Context != null)
        {
            if (Context.NativeProcess.HasExited && Context.Win32Window != null)
            {
                Logger.LogInformation($"Process exit: {Context.Win32Window.ProcessName}");
            }

            ProcessExit?.Invoke(Context.NativeProcess.Pid);
            Context.Dispose();
            Context = null;
        }

        Logger.LogInformation("Stop applying FPS.");
    }

    private bool CheckProcess(uint pid, [NotNullWhen(true)] out ProcessContext? processContext)
    {
        processContext = null;
        if (!NativeProcess.TryOpen(pid, MonitoringProcessAccess, out var nativeProcess))
            return false;

        if (!CheckProcessPath(nativeProcess, out var fileName, out var directoryName) || nativeProcess.HasExited)
        {
            nativeProcess.Dispose();
            return false;
        }

        Context = processContext = new ProcessContext
        {
            NativeProcess = nativeProcess,
            FileName = fileName,
            DirectoryName = directoryName
        };
        try
        {
            Logger.LogInformation($"Trying to get remote module base address...");
            var success = GetProcessModules(Context, CancellationToken.None);
            if (!success) return false;
            Logger.LogInformation($"Get remote module base address successfully.");

            Logger.LogInformation($"Trying to get FPS address...");
            processContext.FpsValueAddress = FpsPatterns.ProvideAddress(Context.UnityPlayerModule,
                processContext.UserAssemblyModule, nativeProcess.Handle);
            Logger.LogInformation($"Get FPS address successfully: {processContext.FpsValueAddress}");
            return true;
        }
        catch
        {
            Context?.Dispose();
            Context = processContext = null;
            throw;
        }
    }

    private static bool CheckProcessPath(NativeProcess process,
        [NotNullWhen(true)] out string? fileName,
        [NotNullWhen(true)] out string? directoryName)
    {
        if (!process.TryGetImagePath(out var processPath) ||
            !ProcessUtils.IsGamePath(processPath))
        {
            fileName = null;
            directoryName = null;
            return false;
        }

        var gameDirectory = Path.GetDirectoryName(processPath);
        if (string.IsNullOrEmpty(gameDirectory) ||
            !File.Exists(Path.Combine(gameDirectory, "UnityPlayer.dll")))
        {
            fileName = null;
            directoryName = null;
            return false;
        }

        fileName = processPath;
        directoryName = gameDirectory;
        return true;
    }

    private bool GetProcessModules(ProcessContext processContext, CancellationToken token)
    {
        int retryCount = 0;

        while (!processContext.NativeProcess.HasExited && !token.IsCancellationRequested)
        {
            if (processContext.NativeProcess.TryGetModules(RequiredModules, out var modules))
            {
                if (modules.TryGetValue("UnityPlayer.dll", out var unityPlayerModule))
                {
                    processContext.UnityPlayerModule = unityPlayerModule;
                }

                if (modules.TryGetValue("UserAssembly.dll", out var userAssemblyModule))
                {
                    processContext.UserAssemblyModule = userAssemblyModule;
                }
            }

            if (processContext is { UnityPlayerModule: not null, UserAssemblyModule: not null })
            {
                break;
            }

            if (retryCount > 40)
            {
                break;
            }

            if (!TaskUtils.TaskSleep(500, token)) break;
            retryCount++;
            Logger.LogDebug($"({retryCount}) Trying to get remote module base address...");
        }

        return processContext is { UnityPlayerModule: not null, UserAssemblyModule: not null };
    }

    private void ApplyFpsLimit(ProcessContext context)
    {
        var isGameForegroundOld = context.IsGameInForeground;
        context.IsGameInForeground = context.Win32Window != null && GetForegroundWindow() == context.Win32Window.Handle;
        if (context.IsGameInForeground != isGameForegroundOld)
        {
            var activeStr = context.IsGameInForeground ? "active" : "inactive";
            Logger.LogInformation($"Game window is now {activeStr}.");
        }

        if (_config.UsePowerSave)
        {
            var priorityClass = context.IsGameInForeground
                ? PriorityClasses[_config.ProcessPriority]
                : PriorityClass.IDLE;
            context.NativeProcess.TrySetPriorityClass(priorityClass);
        }

        int fpsTarget;
        if (context.IsGameInForeground)
        {
            fpsTarget = _config.FpsTarget;
        }
        else
        {
            fpsTarget = _config.UsePowerSave ? _config.FpsPowerSave : _config.FpsTarget;
        }

        Span<byte> buffer = stackalloc byte[4];
        var readProcessMemory = NativeMethods.ReadProcessMemory(context.NativeProcess.Handle, context.FpsValueAddress,
            buffer, 4, out var readBytes);
        if (!readProcessMemory || readBytes != 4) return;

        var currentFps = BitConverter.ToInt32(buffer);
        if (currentFps == fpsTarget) return;

        var toWrite = BitConverter.GetBytes(fpsTarget);
        if (NativeMethods.WriteProcessMemory(context.NativeProcess.Handle, context.FpsValueAddress, toWrite, 4, out _))
        {
            Logger.LogInformation($"FPS Override: {currentFps} -> {fpsTarget}");
        }
    }

    public void Dispose()
    {
        Stop();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    internal class ProcessContext : IDisposable
    {
        public ProcessContext()
        {
            CancellationTokenSource = new CancellationTokenSource();
        }

        public CancellationTokenSource CancellationTokenSource { get; }

        public required NativeProcess NativeProcess { get; init; }
        public required string FileName { get; init; }
        public required string DirectoryName { get; init; }

        public NativeModuleInfo UnityPlayerModule { get; set; } = null!;
        public NativeModuleInfo UserAssemblyModule { get; set; } = null!;
        public bool IsFpsApplied { get; set; }

        public IntPtr FpsValueAddress { get; set; }
        public bool IsGameInForeground { get; set; }

        public Win32Window? Win32Window { get; set; }

        public void Dispose()
        {
            CancellationTokenSource.Dispose();
            NativeProcess.Dispose();
        }
    }
}
