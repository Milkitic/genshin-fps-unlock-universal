using System.Buffers;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using UnlockFps.Gui.Utils;

namespace UnlockFps.Utils;

[SupportedOSPlatform("windows5.0")]
public class Win32Window
{
    private readonly HWND _hWnd;
    private string? _className;
    private string? _title;
    private string? _processName;
    private uint _pid;

    public Win32Window(nint handle)
    {
        _hWnd = (HWND)handle;
    }

    public nint Handle => _hWnd;

    public string ClassName => _className ??= CallWin32ToGetPWSTR(512, (p, l) => PInvoke.GetClassName(_hWnd, p, l));

    public string Title => _title ??= CallWin32ToGetPWSTR(512, (p, l) => PInvoke.GetWindowText(_hWnd, p, l));

    public uint ProcessId => _pid is 0 ? (_pid = GetProcessIdCore()) : _pid;

    //public string ProcessName => _processName ??= Process.GetProcessById((int)ProcessId).ProcessName;
    public string ProcessName
    {
        get
        {
            if (_processName == null)
            {
                if (!ProcessUtils.TryGetProcessPath(ProcessId, out var processPath))
                    return "";

                _processName = Path.GetFileNameWithoutExtension(processPath);
            }

            return _processName;
        }
    }

    private unsafe uint GetProcessIdCore()
    {
        uint pid = 0;
        PInvoke.GetWindowThreadProcessId(_hWnd, &pid);
        return pid;
    }

    private unsafe string CallWin32ToGetPWSTR(int bufferLength, Func<PWSTR, int, int> getter)
    {
        var buffer = ArrayPool<char>.Shared.Rent(bufferLength);
        try
        {
            fixed (char* ptr = buffer)
            {
                getter(ptr, bufferLength);
                return new string(ptr);
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }
}