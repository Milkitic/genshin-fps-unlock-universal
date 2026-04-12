using UnlockFps.Logging;
using UnlockFps.Utils;

namespace UnlockFps;

internal static class FpsPatterns
{
    private static readonly ILogger Logger = LogManager.GetLogger(nameof(FpsPatterns));
    private const uint LoadLibraryAsImageResource = 0x20;
    private const string Il2CppSectionName = "il2cpp";
    private const string FpsPattern = "B9 3C 00 00 00 E8";

    public static unsafe nint ProvideAddress(NativeModuleInfo mainModule)
    {
        using ModuleGuard mappedMainModule = NativeMethods.LoadLibraryEx(mainModule.FilePath, nint.Zero, LoadLibraryAsImageResource);
        if (!mappedMainModule)
        {
            throw new InvalidOperationException($"Failed to map main module image: {mainModule.FilePath}");
        }

        if (!ProcessUtils.TryGetSection(mappedMainModule.BaseAddress, Il2CppSectionName, out var il2cppSection))
        {
            throw new InvalidOperationException($"Failed to find '{Il2CppSectionName}' section in {mainModule.FilePath}.");
        }

        var candidates = ProcessUtils.PatternScanAll(il2cppSection, FpsPattern);
        Logger.LogDebug($"Found {candidates.Count} FPS pattern candidate(s) in {Il2CppSectionName}.");

        foreach (var candidateAddress in candidates)
        {
            var localFpsAddress = TryResolveLocalFpsAddress((byte*)candidateAddress);
            if (localFpsAddress == null)
                continue;

            var localImageBase = (byte*)mappedMainModule.BaseAddress;
            var remoteImageBase = (byte*)mainModule.BaseAddress;
            var remoteFpsAddress = remoteImageBase + (localFpsAddress - localImageBase);

            Logger.LogDebug($"Resolved FPS address: local=0x{(nint)localFpsAddress:X16}, remote=0x{(nint)remoteFpsAddress:X16}");
            return (nint)remoteFpsAddress;
        }

        throw new InvalidOperationException("Unrecognized FPS pattern.");
    }

    private static unsafe byte* TryResolveLocalFpsAddress(byte* candidate)
    {
        var branch = candidate + 5;
        if (!IsExpectedBranchChain(branch))
            return null;

        while (branch[0] is 0xE8 or 0xE9)
        {
            branch = FollowBranch(branch);
        }

        return ResolveRipRelativeAddress(branch, displacementOffset: 2, instructionSize: 6);
    }

    private static unsafe bool IsExpectedBranchChain(byte* branch)
    {
        if (branch[0] != 0xE8)
            return false;

        var firstTarget = FollowBranch(branch);
        return firstTarget[0] == 0xE9;
    }

    private static unsafe byte* FollowBranch(byte* instruction)
    {
        var displacement = *(int*)(instruction + 1);
        return instruction + displacement + 5;
    }

    private static unsafe byte* ResolveRipRelativeAddress(byte* instruction, int displacementOffset, int instructionSize)
    {
        var displacement = *(int*)(instruction + displacementOffset);
        return instruction + displacement + instructionSize;
    }
}
