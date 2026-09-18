namespace Glassline.Core.Settings;

public sealed record AppSettings
{
    public WindowPlacement Placement { get; init; } = WindowPlacement.TopCenter;

    public AppAppearance Appearance { get; init; } = AppAppearance.System;

    public bool HideInFullscreen { get; init; } = true;

    public bool StartWithWindows { get; init; }
}

public enum WindowPlacement
{
    TopLeft,
    TopCenter,
    TopRight,
}

public enum AppAppearance
{
    System,
    Light,
    Dark,
}
