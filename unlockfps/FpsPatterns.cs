using System.Diagnostics;
using System.Runtime.InteropServices;
using UnlockFps.Logging;
using UnlockFps.Utils;

namespace UnlockFps;

internal static class FpsPatterns
{
    private static readonly ILogger Logger = LogManager.GetLogger(nameof(FpsPatterns));
    private const uint Unity43TimeDateStamp = 0x656FFAF7U;
    private const int PointerReadRetryCount = 50;

    public static unsafe nint ProvideAddress(ProcessModule mdUnityPlayer, ProcessModule mdUserAssembly, Process process)
    {
        var unityPlayerPath = mdUnityPlayer.FileName;
        var userAssemblyPath = mdUserAssembly.FileName;

        using ModuleGuard shUnityPlayer = Utils.NativeMethods.LoadLibraryEx(unityPlayerPath, nint.Zero, 0x20);
        using ModuleGuard shUserAssembly = Utils.NativeMethods.LoadLibraryEx(userAssemblyPath, nint.Zero, 0x20);

        var pUnityPlayer = shUnityPlayer.BaseAddress;
        var pUserAssembly = shUserAssembly.BaseAddress;

        var dosHeader = Marshal.PtrToStructure<IMAGE_DOS_HEADER>(pUnityPlayer);
        var ntHeader =
            Marshal.PtrToStructure<IMAGE_NT_HEADERS>((nint)(pUnityPlayer.ToInt64() + dosHeader.e_lfanew));

        var timeDateStamp = ntHeader.FileHeader.TimeDateStamp;
        Logger.LogDebug($"TimeDateStamp: {timeDateStamp}");

        // Prefer the pattern family that matches the UnityPlayer timestamp, then
        // fall back to the other known upstream-compatible path before giving up.
        if (timeDateStamp >= Unity43TimeDateStamp && TryGet43PlusAddress(pUnityPlayer, pUserAssembly, mdUnityPlayer, mdUserAssembly, process, out var address))
        {
            return address;
        }

        if (TryGetPre43Address(pUnityPlayer, pUserAssembly, mdUnityPlayer, mdUserAssembly, process, out address))
        {
            return address;
        }

        if (timeDateStamp < Unity43TimeDateStamp
            && TryGet43PlusAddress(pUnityPlayer, pUserAssembly, mdUnityPlayer, mdUserAssembly, process, out address))
        {
            return address;
        }

        throw new Exception("Unrecognized FPS pattern.");
    }

    private static unsafe bool TryGetPre43Address(nint pUnityPlayer, nint pUserAssembly,
        ProcessModule mdUnityPlayer, ProcessModule mdUserAssembly, Process process, out nint address)
    {
        if (TryGetPre37Address(pUnityPlayer, mdUnityPlayer, out address))
        {
            return true;
        }

        var addressPtr =
            ProcessUtils.PatternScan(pUserAssembly, "E8 ?? ?? ?? ?? 85 C0 7E 07 E8 ?? ?? ?? ?? EB 05");
        byte* patternAddress = (byte*)addressPtr;
        if (patternAddress == null)
        {
            address = nint.Zero;
            return false;
        }

        Logger.LogDebug($"Scanned pre-4.3 pattern successfully: 0x{addressPtr:X16}");
        byte* rip = patternAddress;
        rip += *(int*)(rip + 1) + 5;
        rip += *(int*)(rip + 3) + 7;

        return TryResolveUnityPlayerAddress(rip, pUnityPlayer, pUserAssembly, mdUnityPlayer, mdUserAssembly, process, out address);
    }

    private static unsafe bool TryGet43PlusAddress(nint pUnityPlayer, nint pUserAssembly,
        ProcessModule mdUnityPlayer, ProcessModule mdUserAssembly, Process process, out nint address)
    {
        var addressPtr = ProcessUtils.PatternScan(pUserAssembly, "B9 3C 00 00 00 FF 15");
        byte* patternAddress = (byte*)addressPtr;
        if (patternAddress == null)
        {
            address = nint.Zero;
            return false;
        }

        Logger.LogDebug($"Scanned 4.3+ pattern successfully: 0x{addressPtr:X16}");
        byte* rip = patternAddress;
        rip += 5;
        rip += *(int*)(rip + 2) + 6;

        return TryResolveUnityPlayerAddress(rip, pUnityPlayer, pUserAssembly, mdUnityPlayer, mdUserAssembly, process, out address);
    }

    private static unsafe bool TryGetPre37Address(nint pUnityPlayer, ProcessModule mdUnityPlayer, out nint address)
    {
        var addressPtr = ProcessUtils.PatternScan(pUnityPlayer, "7F 0F 8B 05 ?? ?? ?? ??");
        byte* patternAddress = (byte*)addressPtr;
        if (patternAddress == null)
        {
            address = nint.Zero;
            return false;
        }

        Logger.LogDebug($"Scanned pre-3.7 pattern successfully: 0x{addressPtr:X16}");
        byte* rip = patternAddress + 2;
        int rel = *(int*)(rip + 2);
        var localVa = rip + rel + 6;
        var rva = localVa - pUnityPlayer.ToInt64();
        address = (nint)(mdUnityPlayer.BaseAddress.ToInt64() + rva);
        return true;
    }

    private static unsafe bool TryResolveUnityPlayerAddress(byte* rip, nint pUnityPlayer, nint pUserAssembly,
        ProcessModule mdUnityPlayer, ProcessModule mdUserAssembly, Process process, out nint address)
    {
        byte* remoteVa = rip - pUserAssembly.ToInt64() + mdUserAssembly.BaseAddress.ToInt64();
        byte* dataPtr = null;

        // userAssembly may publish the pointer a little later than the scan hits,
        // so poll the remote slot for a short bounded window instead of spinning forever.
        Span<byte> readResult = stackalloc byte[8];
        for (var retryCount = 0; retryCount < PointerReadRetryCount && dataPtr == null; retryCount++)
        {
            if (!Utils.NativeMethods.ReadProcessMemory(process.Handle, (nint)remoteVa, readResult, readResult.Length, out var readBytes)
                || readBytes != readResult.Length)
            {
                Logger.LogWarning($"Failed to read FPS pointer from remote address 0x{(nint)remoteVa:X16}.");
                address = nint.Zero;
                return false;
            }

            ulong value = BitConverter.ToUInt64(readResult);
            dataPtr = (byte*)value;
            if (dataPtr == null)
            {
                Thread.Sleep(100);
            }
        }

        if (dataPtr == null)
        {
            Logger.LogWarning($"Timed out while waiting for FPS pointer at remote address 0x{(nint)remoteVa:X16}.");
            address = nint.Zero;
            return false;
        }

        byte* localVa = dataPtr - mdUnityPlayer.BaseAddress.ToInt64() + pUnityPlayer.ToInt64();
        while (localVa[0] == 0xE8 || localVa[0] == 0xE9)
        {
            localVa += *(int*)(localVa + 1) + 5;
        }

        localVa += *(int*)(localVa + 2) + 6;
        var rva = localVa - pUnityPlayer.ToInt64();
        address = (nint)(mdUnityPlayer.BaseAddress.ToInt64() + rva);
        return true;
    }
}
