﻿using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace UnlockFps;

public class LaunchOptions
{
    public string? GamePath { get; set; }

    public bool IsWindowBorderless { get; set; }
    public bool Fullscreen { get; set; } = true;
    public bool IsExclusiveFullscreen { get; set; }
    public bool UseCustomResolution { get; set; }
    public int CustomResolutionX { get; set; } = 1920;
    public int CustomResolutionY { get; set; } = 1080;
    public bool UseMobileUI { get; set; }

    public int MonitorId { get; set; } = 1;

    public bool SuspendLoad { get; set; }
    public ObservableCollection<string> DllList { get; set; } = new();
}

public class Config : INotifyPropertyChanged
{
    public LaunchOptions LaunchOptions { get; set; } = new();

    public bool AutoLaunch { get; set; }
    public bool AutoClose { get; set; }
    public bool UsePowerSave { get; set; }
    public int FpsTarget { get; set; } = 120;
    public int FpsPowerSave { get; set; } = 10;
    public int ProcessPriority { get; set; } = 3;
    public bool ShowDebugConsole { get; set; }
    public bool UseQueryEvent { get; set; } = true;

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
}