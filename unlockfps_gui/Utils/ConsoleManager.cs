﻿using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;

namespace UnlockFps.Gui.Utils;

[SuppressUnmanagedCodeSecurity]
internal static class ConsoleManager
{
    [Flags]
    public enum CharacterAttributes
    {
        FOREGROUND_BLUE = 0x0001,
        FOREGROUND_GREEN = 0x0002,
        FOREGROUND_RED = 0x0004,
        FOREGROUND_INTENSITY = 0x0008,
        BACKGROUND_BLUE = 0x0010,
        BACKGROUND_GREEN = 0x0020,
        BACKGROUND_RED = 0x0040,
        BACKGROUND_INTENSITY = 0x0080,
        COMMON_LVB_LEADING_BYTE = 0x0100,
        COMMON_LVB_TRAILING_BYTE = 0x0200,
        COMMON_LVB_GRID_HORIZONTAL = 0x0400,
        COMMON_LVB_GRID_LVERTICAL = 0x0800,
        COMMON_LVB_GRID_RVERTICAL = 0x1000,
        COMMON_LVB_REVERSE_VIDEO = 0x4000,
        COMMON_LVB_UNDERSCORE = 0x8000
    }

    public static bool HasConsole => GetConsoleWindow() != IntPtr.Zero;

    private const string Kernel32_DllName = "kernel32.dll";

    [DllImport(Kernel32_DllName)]
    private static extern bool AllocConsole();

    [DllImport(Kernel32_DllName)]
    private static extern bool FreeConsole();

    [DllImport(Kernel32_DllName)]
    private static extern IntPtr GetConsoleWindow();

    [DllImport(Kernel32_DllName)]
    private static extern int GetConsoleOutputCP();
    [DllImport(Kernel32_DllName)]
    private static extern int SetConsoleTextAttribute(IntPtr hConsoleOutput,
        CharacterAttributes wAttributes);

    /// <summary>
    /// Creates a new console instance if the process is not attached to a console already.
    /// </summary>
    public static void Show()
    {
        if (HasConsole) return;
        AllocConsole();
        //var intPtr = GetConsoleWindow();
        //SetConsoleTextAttribute(intPtr,
        //    CharacterAttributes.BACKGROUND_INTENSITY | CharacterAttributes.FOREGROUND_INTENSITY);
        InvalidateOutAndError();
    }

    /// <summary>
    /// If the process has a console attached to it, it will be detached and no longer visible. Writing to the System.Console is still possible, but no output will be shown.
    /// </summary>
    public static void Hide()
    {
        if (!HasConsole) return;
        SetOutAndErrorNull();
        FreeConsole();
    }

    //public static void Toggle()
    //{
    //    if (HasConsole) Hide();
    //    else Show();
    //}

    private static void InvalidateOutAndError()
    {
        Type type = typeof(System.Console);
        System.Reflection.FieldInfo? _out = type.GetField(
#if !NETSTANDARD2_0
            "s_out",
#else
            "_out",
#endif

            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

        System.Reflection.FieldInfo? _error = type.GetField(
#if !NETSTANDARD2_0
            "s_error",
#else
            "_error",
#endif
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

        //System.Reflection.MethodInfo? _InitializeStdOutError = type.GetMethod("InitializeStdOutError",
        //    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

        Debug.Assert(_out != null);
        Debug.Assert(_error != null);

        //Debug.Assert(_InitializeStdOutError != null);

        _out.SetValue(null, null);
        _error.SetValue(null, null);

        var o = Console.Out;
        o = Console.Error;
        //_InitializeStdOutError.Invoke(null, new object[] { true });
    }

    private static void SetOutAndErrorNull()
    {
        Console.SetOut(TextWriter.Null);
        Console.SetError(TextWriter.Null);
    }
}