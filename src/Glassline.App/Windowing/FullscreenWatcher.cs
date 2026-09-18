using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Glassline.App.Windowing;

internal sealed partial class FullscreenWatcher : IDisposable
{
    private const uint EventSystemForeground = 0x0003;
    private const uint EventObjectLocationChange = 0x800B;
    private const uint WinEventOutOfContext = 0x0000;
    private const uint WinEventSkipOwnProcess = 0x0002;
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const int ObjectIdWindow = 0;

    private readonly nint _glasslineWindow;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly Action<bool> _fullscreenChanged;
    private readonly WinEventDelegate _callback;
    private readonly nint _foregroundHook;
    private readonly nint _locationHook;
    private bool? _isFullscreen;
    private bool _isDisposed;

    internal FullscreenWatcher(Window window, Action<bool> fullscreenChanged)
    {
        ArgumentNullException.ThrowIfNull(window);
        _fullscreenChanged = fullscreenChanged
            ?? throw new ArgumentNullException(nameof(fullscreenChanged));
        _glasslineWindow = WinRT.Interop.WindowNative.GetWindowHandle(window);
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("The XAML dispatcher queue is not available.");
        _callback = OnWindowEvent;

        _foregroundHook = SetWinEventHook(
            EventSystemForeground,
            EventSystemForeground,
            0,
            _callback,
            0,
            0,
            WinEventOutOfContext | WinEventSkipOwnProcess);
        _locationHook = SetWinEventHook(
            EventObjectLocationChange,
            EventObjectLocationChange,
            0,
            _callback,
            0,
            0,
            WinEventOutOfContext | WinEventSkipOwnProcess);

        if (_foregroundHook == 0 || _locationHook == 0)
        {
            Dispose();
            throw new InvalidOperationException("Could not register fullscreen window events.");
        }

        Evaluate();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        if (_foregroundHook != 0)
        {
            _ = UnhookWinEvent(_foregroundHook);
        }

        if (_locationHook != 0)
        {
            _ = UnhookWinEvent(_locationHook);
        }
    }

    private void OnWindowEvent(
        nint hook,
        uint eventType,
        nint window,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime)
    {
        if (_isDisposed || window == 0 || (eventType == EventObjectLocationChange && objectId != ObjectIdWindow))
        {
            return;
        }

        _ = _dispatcherQueue.TryEnqueue(Evaluate);
    }

    private void Evaluate()
    {
        if (_isDisposed)
        {
            return;
        }

        var foreground = GetForegroundWindow();
        var isFullscreen = foreground != 0
            && foreground != _glasslineWindow
            && foreground != GetShellWindow()
            && IsWindowVisible(foreground) != 0
            && CoversMonitor(foreground);

        if (_isFullscreen == isFullscreen)
        {
            return;
        }

        _isFullscreen = isFullscreen;
        _fullscreenChanged(isFullscreen);
    }

    private static bool CoversMonitor(nint window)
    {
        if (GetWindowRect(window, out var windowBounds) == 0)
        {
            return false;
        }

        var monitor = MonitorFromWindow(window, MonitorDefaultToNearest);
        var monitorInfo = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (monitor == 0 || GetMonitorInfo(monitor, ref monitorInfo) == 0)
        {
            return false;
        }

        const int tolerance = 1;
        var bounds = monitorInfo.Monitor;
        return windowBounds.Left <= bounds.Left + tolerance
            && windowBounds.Top <= bounds.Top + tolerance
            && windowBounds.Right >= bounds.Right - tolerance
            && windowBounds.Bottom >= bounds.Bottom - tolerance;
    }

    private delegate void WinEventDelegate(
        nint hook,
        uint eventType,
        nint window,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        internal uint Size;
        internal NativeRect Monitor;
        internal NativeRect WorkArea;
        internal uint Flags;
    }

    [LibraryImport("user32.dll")]
    private static partial nint SetWinEventHook(
        uint eventMin,
        uint eventMax,
        nint eventHookModule,
        [MarshalAs(UnmanagedType.FunctionPtr)]
        WinEventDelegate callback,
        uint processId,
        uint threadId,
        uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWinEvent(nint hook);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial nint GetShellWindow();

    [LibraryImport("user32.dll")]
    private static partial int IsWindowVisible(nint window);

    [LibraryImport("user32.dll")]
    private static partial int GetWindowRect(nint window, out NativeRect rectangle);

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromWindow(nint window, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    private static partial int GetMonitorInfo(nint monitor, ref MonitorInfo monitorInfo);
}
