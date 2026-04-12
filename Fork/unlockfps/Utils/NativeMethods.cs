using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Text;
using PInvoke = Windows.Win32.PInvoke;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.LibraryLoader;
using Windows.Win32.System.Memory;
using Windows.Win32.System.ProcessStatus;
using Windows.Win32.System.Threading;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.WindowsAndMessaging;
using PROCESS_INFORMATIONW = Windows.Win32.System.Threading.PROCESS_INFORMATION;

namespace UnlockFps.Utils;

[SuppressMessage("Interoperability", "CA1416:Validate platform compatibility",
    Justification = "unlockfps is Windows-only and this type is a thin Win32 interop adapter.")]
internal static class NativeMethods
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    public delegate bool ConsoleControlHandler(int eventType);
    
    public static bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam) =>
        PInvoke.EnumWindows((hWnd, param) => enumProc(hWnd, param), (LPARAM)lParam);

    public static unsafe uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId)
    {
        fixed (uint* processId = &lpdwProcessId)
        {
            return PInvoke.GetWindowThreadProcessId((HWND)hWnd, processId);
        }
    }

    public static IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId) =>
        PInvoke.OpenProcess((PROCESS_ACCESS_RIGHTS)dwDesiredAccess, bInheritHandle, dwProcessId);

    public static bool CloseHandle(IntPtr hHandle) => PInvoke.CloseHandle((HANDLE)hHandle);

    public static bool TerminateProcess(IntPtr hProcess, uint uExitCode) =>
        PInvoke.TerminateProcess((HANDLE)hProcess, uExitCode);

    public static unsafe bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, StringBuilder lpExeName,
        ref uint lpdwSize)
    {
        var buffer = ArrayPool<char>.Shared.Rent((int)lpdwSize);
        try
        {
            fixed (char* ptr = buffer)
            {
                uint size = lpdwSize;
                var result =
                    PInvoke.QueryFullProcessImageName((HANDLE)hProcess, (PROCESS_NAME_FORMAT)dwFlags, ptr, &size);
                if (!result)
                {
                    return false;
                }

                lpExeName.Clear();
                lpExeName.Append(buffer, 0, (int)size);
                lpdwSize = size;
                return true;
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    public static unsafe bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode)
    {
        fixed (uint* exitCode = &lpExitCode)
        {
            return PInvoke.GetExitCodeProcess((HANDLE)hProcess, exitCode);
        }
    }

    public static unsafe bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int nSize,
        out int lpNumberOfBytesWritten)
    {
        var result = WriteProcessMemory(hProcess, lpBaseAddress, lpBuffer.AsSpan(0, nSize), out var bytesWritten);
        lpNumberOfBytesWritten = checked((int)bytesWritten);
        return result;
    }

    public static unsafe bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, ReadOnlySpan<byte> lpBuffer,
        out nuint lpNumberOfBytesWritten)
    {
        fixed (byte* buffer = lpBuffer)
        {
            nuint bytesWritten = 0;
            var result = PInvoke.WriteProcessMemory((HANDLE)hProcess, (void*)lpBaseAddress, buffer, (nuint)lpBuffer.Length,
                &bytesWritten);
            lpNumberOfBytesWritten = bytesWritten;
            return result;
        }
    }

    public static unsafe bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, Span<byte> lpBuffer,
        out nuint lpNumberOfBytesRead)
    {
        fixed (byte* buffer = lpBuffer)
        {
            nuint bytesRead = 0;
            var result = PInvoke.ReadProcessMemory((HANDLE)hProcess, (void*)lpBaseAddress, buffer, (nuint)lpBuffer.Length,
                &bytesRead);
            lpNumberOfBytesRead = bytesRead;
            return result;
        }
    }

    public static unsafe IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, uint dwStackSize,
        IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, out uint lpThreadId)
    {
        using var processHandle = new BorrowedSafeHandle(hProcess);
        var startRoutine = Marshal.GetDelegateForFunctionPointer<LPTHREAD_START_ROUTINE>(lpStartAddress);
        fixed (uint* threadId = &lpThreadId)
        {
            var threadHandle = PInvoke.CreateRemoteThread(processHandle, null, dwStackSize, startRoutine,
                (void*)lpParameter, dwCreationFlags, threadId);
            var handle = threadHandle.DangerousGetHandle();
            threadHandle.SetHandleAsInvalid();
            return handle;
        }
    }

    public static unsafe bool CreateProcess(string applicationName, ref Span<char> commandLine,
        PROCESS_CREATION_FLAGS creationFlags, string? currentDirectory, in STARTUPINFOW startupInfo,
        out PROCESS_INFORMATIONW processInformation) =>
        PInvoke.CreateProcess(applicationName, ref commandLine, default, default, false, creationFlags, default,
            currentDirectory, in startupInfo, out processInformation);

    public static uint ResumeThread(IntPtr hThread) => PInvoke.ResumeThread((HANDLE)hThread);

    public static unsafe IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType,
        uint flProtect) =>
        (IntPtr)PInvoke.VirtualAllocEx((HANDLE)hProcess, (void*)lpAddress, dwSize,
            (VIRTUAL_ALLOCATION_TYPE)flAllocationType, (PAGE_PROTECTION_FLAGS)flProtect);

    public static unsafe bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint dwFreeType) =>
        PInvoke.VirtualFreeEx((HANDLE)hProcess, (void*)lpAddress, dwSize, (VIRTUAL_FREE_TYPE)dwFreeType);

    public static uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds) =>
        (uint)PInvoke.WaitForSingleObject((HANDLE)hHandle, dwMilliseconds);

    public static unsafe IntPtr LoadLibrary(string lpFileName)
    {
        fixed (char* fileName = lpFileName)
        {
            return PInvoke.LoadLibrary(new PCWSTR(fileName));
        }
    }

    public static unsafe IntPtr LoadLibraryEx(string lpFileName, uint dwFlags)
    {
        fixed (char* fileName = lpFileName)
        {
            return (IntPtr)PInvoke.LoadLibraryEx(new PCWSTR(fileName), HANDLE.Null, (LOAD_LIBRARY_FLAGS)dwFlags);
        }
    }

    public static unsafe void FreeLibrary(IntPtr handle) => PInvoke.FreeLibrary((HMODULE)handle);

    public static unsafe IntPtr GetProcAddress(IntPtr hModule, string procedureName)
    {
        var bytes = Encoding.ASCII.GetBytes(procedureName + '\0');
        fixed (byte* procName = bytes)
        {
            return (IntPtr)PInvoke.GetProcAddress((HMODULE)hModule, new PCSTR(procName));
        }
    }

    public static bool SetPriorityClass(IntPtr hProcess, uint dwPriorityClass) =>
        PInvoke.SetPriorityClass((HANDLE)hProcess, (PROCESS_CREATION_FLAGS)dwPriorityClass);

    public static unsafe string GetClassName(IntPtr hWnd, int bufferLength) =>
        CallWin32String(bufferLength, (buffer, length) => PInvoke.GetClassName((HWND)hWnd, buffer, length));

    public static unsafe string GetWindowText(IntPtr hWnd, int bufferLength) =>
        CallWin32String(bufferLength, (buffer, length) => PInvoke.GetWindowText((HWND)hWnd, buffer, length));

    public static HWINEVENTHOOK SetForegroundWinEventHook(WINEVENTPROC callback) =>
        SetWinEventHook(PInvoke.EVENT_SYSTEM_FOREGROUND, PInvoke.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, callback, 0, 0,
            (uint)(PInvoke.WINEVENT_OUTOFCONTEXT | PInvoke.WINEVENT_SKIPOWNPROCESS));

    public static bool UnhookWinEvent(HWINEVENTHOOK hWinEventHook) => PInvoke.UnhookWinEvent(hWinEventHook);

    public static IntPtr GetForegroundWindow() => PInvoke.GetForegroundWindow();

    public static bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax) =>
        PInvoke.GetMessage(out lpMsg, (HWND)hWnd, wMsgFilterMin, wMsgFilterMax);

    public static bool TranslateMessage(in MSG lpMsg) => PInvoke.TranslateMessage(lpMsg);

    public static IntPtr DispatchMessage(in MSG lpMsg) => PInvoke.DispatchMessage(lpMsg);

    public static unsafe bool EnumProcesses([Out] uint[] lpidProcess, uint cb, out uint lpcbNeeded)
    {
        fixed (uint* processIds = lpidProcess)
        fixed (uint* bytesNeeded = &lpcbNeeded)
        {
            return PInvoke.K32EnumProcesses(processIds, cb, bytesNeeded);
        }
    }

    public static unsafe bool EnumProcessModulesEx(IntPtr hProcess, [Out] IntPtr[] lphModule, uint cb,
        out uint lpcbNeeded, uint dwFilterFlag)
    {
        fixed (IntPtr* moduleHandles = lphModule)
        fixed (uint* bytesNeeded = &lpcbNeeded)
        {
            return PInvoke.K32EnumProcessModulesEx((HANDLE)hProcess, (HMODULE*)moduleHandles, cb, bytesNeeded,
                dwFilterFlag);
        }
    }

    public static unsafe uint GetModuleBaseName(IntPtr hProcess, IntPtr hModule, StringBuilder lpBaseName, uint nSize)
    {
        var buffer = ArrayPool<char>.Shared.Rent((int)nSize);
        try
        {
            fixed (char* ptr = buffer)
            {
                var length = PInvoke.K32GetModuleBaseName((HANDLE)hProcess, (HMODULE)hModule, ptr, nSize);
                if (length == 0)
                {
                    return 0;
                }

                lpBaseName.Clear();
                lpBaseName.Append(buffer, 0, (int)length);
                return length;
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    public static unsafe uint GetModuleFileNameEx(IntPtr hProcess, IntPtr hModule, StringBuilder lpFilename,
        uint nSize)
    {
        var buffer = ArrayPool<char>.Shared.Rent((int)nSize);
        try
        {
            fixed (char* ptr = buffer)
            {
                var length = PInvoke.K32GetModuleFileNameEx((HANDLE)hProcess, (HMODULE)hModule, ptr, nSize);
                if (length == 0)
                {
                    return 0;
                }

                lpFilename.Clear();
                lpFilename.Append(buffer, 0, (int)length);
                return length;
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    public static unsafe bool GetModuleInformation(IntPtr hProcess, IntPtr hModule, out MODULEINFO lpmodinfo, uint cb)
    {
        using var processHandle = new BorrowedSafeHandle(hProcess);
        using var moduleHandle = new BorrowedSafeHandle(hModule);
        if (!PInvoke.K32GetModuleInformation(processHandle, moduleHandle, out Windows.Win32.System.ProcessStatus.MODULEINFO moduleInfo,
                cb))
        {
            lpmodinfo = default;
            return false;
        }

        lpmodinfo = new MODULEINFO
        {
            lpBaseOfDll = (IntPtr)moduleInfo.lpBaseOfDll,
            SizeOfImage = moduleInfo.SizeOfImage,
            EntryPoint = (IntPtr)moduleInfo.EntryPoint
        };
        return true;
    }

    private static HWINEVENTHOOK SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        WINEVENTPROC callback, uint idProcess, uint idThread, uint dwFlags) =>
        PInvoke.SetWinEventHook(eventMin, eventMax, (HMODULE)hmodWinEventProc, callback, idProcess, idThread,
            dwFlags);

    public static bool AllocConsole() => PInvoke.AllocConsole();

    public static bool FreeConsole() => PInvoke.FreeConsole();

    public static IntPtr GetConsoleWindow() => PInvoke.GetConsoleWindow();

    public static bool SetConsoleCtrlHandler(ConsoleControlHandler callback, bool add) =>
        PInvoke.SetConsoleCtrlHandler(ctrlType => callback((int)ctrlType), add);

    public static int DeleteMenu(IntPtr hMenu, int nPosition, int wFlags) =>
        PInvoke.DeleteMenu((HMENU)hMenu, (uint)nPosition, (MENU_ITEM_FLAGS)wFlags) ? 1 : 0;

    public static IntPtr GetSystemMenu(IntPtr hWnd, bool bRevert) => PInvoke.GetSystemMenu((HWND)hWnd, bRevert);

    [DllImport("ntdll.dll")]
    public static extern uint RtlAdjustPrivilege(uint Privilege, bool bEnablePrivilege, bool IsThreadPrivilege,
        out bool PreviousValue);

    private static unsafe string CallWin32String(int bufferLength, Func<PWSTR, int, int> getter)
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

    private sealed class BorrowedSafeHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public BorrowedSafeHandle(IntPtr handle) : base(false)
        {
            SetHandle(handle);
        }

        protected override bool ReleaseHandle() => true;
    }
}
