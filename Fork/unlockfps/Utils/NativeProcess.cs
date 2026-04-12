using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;

namespace UnlockFps.Utils;

internal sealed class NativeProcess : IDisposable
{
    private string? _imagePath;

    private NativeProcess(uint pid, nint handle)
    {
        Pid = pid;
        Handle = handle;
    }

    public uint Pid { get; }

    public nint Handle { get; }

    public bool HasExited
    {
        get
        {
            if (!NativeMethods.GetExitCodeProcess(Handle, out var exitCode))
                return true;

            return exitCode != ProcessExitCode.STILL_ACTIVE;
        }
    }

    public static bool TryOpen(uint pid, uint desiredAccess, [NotNullWhen(true)] out NativeProcess? process)
    {
        process = null;
        if (pid == 0)
            return false;

        var handle = NativeMethods.OpenProcess(desiredAccess, false, pid);
        if (handle == nint.Zero)
            return false;

        process = new NativeProcess(pid, handle);
        return true;
    }

    public bool TryGetImagePath([NotNullWhen(true)] out string? imagePath)
    {
        if (!string.IsNullOrEmpty(_imagePath))
        {
            imagePath = _imagePath;
            return true;
        }

        var path = ProcessUtils.GetProcessPath(Handle);
        if (string.IsNullOrEmpty(path))
        {
            imagePath = null;
            return false;
        }

        _imagePath = path;
        imagePath = path;
        return true;
    }

    public bool TryGetMainModule([NotNullWhen(true)] out NativeModuleInfo? module)
    {
        if (!TryGetImagePath(out var imagePath))
        {
            module = null;
            return false;
        }

        var mainModuleName = Path.GetFileName(imagePath);
        return TryGetModule(mainModuleName, out module);
    }

    public bool TryGetModule(string moduleName, [NotNullWhen(true)] out NativeModuleInfo? module)
    {
        if (TryGetModules([moduleName], out var modules) &&
            modules.TryGetValue(moduleName, out var foundModule))
        {
            module = foundModule;
            return true;
        }

        module = null;
        return false;
    }

    public bool TryGetModules(IReadOnlyCollection<string> moduleNames, out Dictionary<string, NativeModuleInfo> modules)
    {
        var requiredNames = new HashSet<string>(moduleNames, StringComparer.OrdinalIgnoreCase);
        modules = new Dictionary<string, NativeModuleInfo>(StringComparer.OrdinalIgnoreCase);
        if (requiredNames.Count == 0)
            return true;

        var handles = new nint[1024];
        if (!NativeMethods.EnumProcessModulesEx(Handle, handles, (uint)(handles.Length * nint.Size), out var bytesNeeded, 0x03))
        {
            return false;
        }

        var count = (int)(bytesNeeded / nint.Size);
        for (var i = 0; i < count && i < handles.Length; i++)
        {
            var moduleHandle = handles[i];
            if (moduleHandle == nint.Zero)
                continue;

            StringBuilder moduleNameBuilder = new StringBuilder(1024);
            if (NativeMethods.GetModuleBaseName(Handle, moduleHandle, moduleNameBuilder,
                    (uint)moduleNameBuilder.Capacity) == 0)
                continue;

            var foundName = moduleNameBuilder.ToString();
            if (!requiredNames.Contains(foundName))
                continue;

            StringBuilder fileNameBuilder = new StringBuilder(1024);
            if (NativeMethods.GetModuleFileNameEx(Handle, moduleHandle, fileNameBuilder,
                    (uint)fileNameBuilder.Capacity) == 0)
                continue;

            if (!NativeMethods.GetModuleInformation(Handle, moduleHandle, out var moduleInfo,
                    (uint)Marshal.SizeOf<MODULEINFO>()))
                continue;

            modules[foundName] = new NativeModuleInfo
            {
                Name = foundName,
                FilePath = fileNameBuilder.ToString(),
                BaseAddress = moduleInfo.lpBaseOfDll,
                SizeOfImage = moduleInfo.SizeOfImage
            };

            if (modules.Count == requiredNames.Count)
                return true;
        }

        return modules.Count == requiredNames.Count;
    }

    public bool TrySetPriorityClass(uint priorityClass)
    {
        return NativeMethods.SetPriorityClass(Handle, priorityClass);
    }

    public bool TryTerminate(uint exitCode = 0)
    {
        return NativeMethods.TerminateProcess(Handle, exitCode);
    }

    public void Dispose()
    {
        if (Handle != nint.Zero)
        {
            NativeMethods.CloseHandle(Handle);
        }
    }
}

internal sealed class NativeModuleInfo
{
    public required string Name { get; init; }
    public required string FilePath { get; init; }
    public required nint BaseAddress { get; init; }
    public required uint SizeOfImage { get; init; }
}
