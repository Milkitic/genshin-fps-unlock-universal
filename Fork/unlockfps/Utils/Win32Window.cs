using System.Runtime.Versioning;

namespace UnlockFps.Utils;

[SupportedOSPlatform("windows5.0")]
public class Win32Window
{
    private string? _className;
    private string? _title;
    private string? _processName;
    private uint _pid;

    public Win32Window(nint handle)
    {
        Handle = handle;
    }

    public nint Handle { get; }

    public string ClassName => _className ??= NativeMethods.GetClassName(Handle, 512);

    public string Title => _title ??= NativeMethods.GetWindowText(Handle, 512);

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

    private uint GetProcessIdCore() => NativeMethods.GetWindowThreadProcessId(Handle, out var pid) == 0 ? 0 : pid;
}
