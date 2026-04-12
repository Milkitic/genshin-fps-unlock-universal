using System.Runtime.InteropServices;
using System.Text;

namespace UnlockFps.Utils;

internal class ProcessUtils
{
    public static string GetProcessPath(nint processHandle)
    {
        if (processHandle == nint.Zero)
            return string.Empty;

        StringBuilder sb = new StringBuilder(1024);
        uint bufferSize = (uint)sb.Capacity;
        if (!Native.QueryFullProcessImageName(processHandle, 0, sb, ref bufferSize))
            return string.Empty;

        return sb.ToString();
    }

    public static string GetProcessPathFromPid(uint pid, out nint processHandle)
    {
        processHandle = Native.OpenProcess(ProcessAccess.QUERY_LIMITED_INFORMATION, false, pid);
        return GetProcessPath(processHandle);
    }

    public static bool TryGetProcessPath(uint pid, out string? processPath)
    {
        var path = GetProcessPathFromPid(pid, out var processHandle);
        try
        {
            if (string.IsNullOrEmpty(path))
            {
                processPath = null;
                return false;
            }

            processPath = path;
            return true;
        }
        finally
        {
            if (processHandle != nint.Zero)
            {
                Native.CloseHandle(processHandle);
            }
        }
    }

    public static bool IsGamePath(string? processPath)
    {
        if (string.IsNullOrWhiteSpace(processPath))
            return false;

        var processName = Path.GetFileNameWithoutExtension(processPath);
        return Array.IndexOf(GameConstants.GameNames, processName) != -1;
    }

    public static bool TryFindRunningGameProcessId(out uint pid)
    {
        pid = 0;

        var processIds = new uint[2048];
        if (!Native.EnumProcesses(processIds, (uint)(processIds.Length * sizeof(uint)), out var bytesNeeded))
            return false;

        var count = (int)(bytesNeeded / sizeof(uint));
        for (var i = 0; i < count && i < processIds.Length; i++)
        {
            var currentPid = processIds[i];
            if (!TryGetProcessPath(currentPid, out var processPath) || !IsGamePath(processPath))
                continue;

            pid = currentPid;
            return true;
        }

        return false;
    }

    public static bool TryGetGameProcessFromPid(uint pid, out string processPath, out nint processHandle)
    {
        processPath = GetProcessPathFromPid(pid, out var queryHandle);
        try
        {
            if (string.IsNullOrEmpty(processPath) || !IsGamePath(processPath))
            {
                processPath = string.Empty;
                processHandle = nint.Zero;
                return false;
            }
        }
        finally
        {
            if (queryHandle != nint.Zero)
            {
                Native.CloseHandle(queryHandle);
            }
        }

        processHandle = Native.OpenProcess(
            ProcessAccess.QUERY_LIMITED_INFORMATION |
            ProcessAccess.TERMINATE |
            StandardAccess.SYNCHRONIZE, false, pid);
        if (processHandle == nint.Zero)
        {
            processPath = string.Empty;
            return false;
        }

        return true;
    }

    public static bool InjectDlls(nint processHandle, IReadOnlyList<string> dllPaths)
    {
        if (dllPaths.Count == 0)
            return true;

        Native.RtlAdjustPrivilege(20, true, false, out var _);

        var kernel32 = Native.LoadLibrary("kernel32.dll");
        var loadLibrary = Native.GetProcAddress(kernel32, "LoadLibraryW");

        var remoteVa = Native.VirtualAllocEx(processHandle, nint.Zero, 0x1000,
            AllocationType.COMMIT | AllocationType.RESERVE, MemoryProtection.READWRITE);
        if (remoteVa == nint.Zero)
            return false;

        foreach (var dllPath in dllPaths)
        {
            var nativeString = Marshal.StringToHGlobalUni(dllPath);
            var bytes = Encoding.Unicode.GetBytes(dllPath);
            Marshal.FreeHGlobal(nativeString);

            if (!Native.WriteProcessMemory(processHandle, remoteVa, bytes, bytes.Length, out var bytesWritten))
                return false;

            var thread = Native.CreateRemoteThread(processHandle, nint.Zero, 0, loadLibrary, remoteVa, 0, out var threadId);
            if (thread == nint.Zero)
                return false;

            Native.WaitForSingleObject(thread, uint.MaxValue);
            Native.CloseHandle(thread);
            Native.WriteProcessMemory(processHandle, remoteVa, new byte[bytes.Length], bytes.Length, out _);
        }

        Native.VirtualFreeEx(processHandle, remoteVa, 0, FreeType.RELEASE);

        return true;
    }

    public static unsafe IReadOnlyList<nint> PatternScanAll(ModuleSectionInfo section, string signature)
    {
        var sectionBytes = new ReadOnlySpan<byte>((void*)section.Address, section.Size);
        return PatternScanAll(sectionBytes, signature, section.Address);
    }

    public static bool TryGetSection(nint module, string sectionName, out ModuleSectionInfo section)
    {
        var dosHeader = Marshal.PtrToStructure<IMAGE_DOS_HEADER>(module);
        var ntHeaderAddress = module + dosHeader.e_lfanew;
        var ntHeader = Marshal.PtrToStructure<IMAGE_NT_HEADERS>(ntHeaderAddress);
        var sectionHeaderAddress = ntHeaderAddress + Marshal.SizeOf<IMAGE_NT_HEADERS>();
        var sectionHeaderSize = Marshal.SizeOf<IMAGE_SECTION_HEADER>();

        for (var i = 0; i < ntHeader.FileHeader.NumberOfSections; i++)
        {
            var currentSection = Marshal.PtrToStructure<IMAGE_SECTION_HEADER>(sectionHeaderAddress + i * sectionHeaderSize);
            if (!sectionName.Equals(currentSection.GetName(), StringComparison.Ordinal))
                continue;

            var size = checked((int)(currentSection.VirtualSize == 0 ? currentSection.SizeOfRawData : currentSection.VirtualSize));
            section = new ModuleSectionInfo(module + (int)currentSection.VirtualAddress, size);
            return true;
        }

        section = default;
        return false;
    }

    private static IReadOnlyList<nint> PatternScanAll(ReadOnlySpan<byte> data, string signature, nint baseAddress)
    {
        var (patternBytes, wildcardMask) = ParseSignature(signature);
        var results = new List<nint>();
        if (patternBytes.Length == 0 || data.Length < patternBytes.Length)
            return results;

        for (var offset = 0; offset <= data.Length - patternBytes.Length; offset++)
        {
            if (!IsPatternMatch(data, offset, patternBytes, wildcardMask))
                continue;

            results.Add(baseAddress + offset);
        }

        return results;
    }

    private static bool IsPatternMatch(ReadOnlySpan<byte> data, int offset, byte[] patternBytes, bool[] wildcardMask)
    {
        for (var i = 0; i < patternBytes.Length; i++)
        {
            if (wildcardMask[i])
                continue;

            if (data[offset + i] != patternBytes[i])
                return false;
        }

        return true;
    }

    private static (byte[] Bytes, bool[] WildcardMask) ParseSignature(string signature)
    {
        var tokens = signature.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var patternBytes = new byte[tokens.Length];
        var wildcardMask = new bool[tokens.Length];

        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (token is "?" or "??")
            {
                wildcardMask[i] = true;
                continue;
            }

            patternBytes[i] = Convert.ToByte(token, 16);
        }

        return (patternBytes, wildcardMask);
    }
}

internal readonly record struct ModuleSectionInfo(nint Address, int Size);
