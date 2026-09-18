using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace Glassline.App.Windowing;

internal static partial class NativeWindow
{
    private const int WindowStyleIndex = -16;
    private const long WindowStylePopup = 0x80000000L;
    private const long WindowStyleCaption = 0x00C00000L;
    private const long WindowStyleThickFrame = 0x00040000L;
    private const long WindowStyleSystemMenu = 0x00080000L;
    private const long WindowStyleMinimizeBox = 0x00020000L;
    private const long WindowStyleMaximizeBox = 0x00010000L;
    private const uint SetWindowPositionNoSize = 0x0001;
    private const uint SetWindowPositionNoMove = 0x0002;
    private const uint SetWindowPositionNoZOrder = 0x0004;
    private const uint SetWindowPositionNoActivate = 0x0010;
    private const uint SetWindowPositionFrameChanged = 0x0020;
    private const double CompactHeight = 64;
    private const double ExpandedHeight = 184;
    private const double CompactRadius = 26;
    private const double ExpandedRadius = 24;

    internal static void ApplyRoundedRegion(Window window, double scale)
    {
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var size = window.AppWindow.Size;
        var effectiveHeight = size.Height / scale;
        var expansionProgress = Math.Clamp(
            (effectiveHeight - CompactHeight) / (ExpandedHeight - CompactHeight),
            0,
            1);
        var radius = CompactRadius + ((ExpandedRadius - CompactRadius) * expansionProgress);
        var inset = (int)Math.Round(6 * scale);
        var diameter = (int)Math.Round(radius * 2 * scale);
        var region = CreateRoundRectRgn(
            inset,
            inset,
            size.Width - inset + 1,
            size.Height - inset + 1,
            diameter,
            diameter);
        if (region == 0)
        {
            throw new InvalidOperationException("Could not create the capsule window region.");
        }

        // On success Windows owns HRGN and deletes it when replaced or closed.
        if (SetWindowRgn(handle, region, 1) == 0)
        {
            _ = DeleteObject(region);
            throw new InvalidOperationException("Could not apply the capsule window region.");
        }
    }

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);

    [LibraryImport("user32.dll")]
    private static partial int SetWindowRgn(nint window, nint region, int redraw);

    [LibraryImport("gdi32.dll")]
    private static partial int DeleteObject(nint handle);

    private const int DwmWindowAttributeBorderColor = 34;
    private const uint DwmColorNone = 0xFFFFFFFE;

    internal static void DisableSystemFrame(Window window)
    {
        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var style = GetWindowStyle(windowHandle);
        style &= ~(WindowStyleCaption
            | WindowStyleThickFrame
            | WindowStyleSystemMenu
            | WindowStyleMinimizeBox
            | WindowStyleMaximizeBox);
        style |= WindowStylePopup;
        SetWindowStyle(windowHandle, style);

        _ = SetWindowPos(
            windowHandle,
            0,
            0,
            0,
            0,
            0,
            SetWindowPositionNoSize
                | SetWindowPositionNoMove
                | SetWindowPositionNoZOrder
                | SetWindowPositionNoActivate
                | SetWindowPositionFrameChanged);

        var color = DwmColorNone;

        var borderResult = DwmSetWindowAttribute(
            windowHandle,
            DwmWindowAttributeBorderColor,
            ref color,
            sizeof(uint));

        if (borderResult < 0 && Environment.OSVersion.Version.Build >= 22000)
        {
            Marshal.ThrowExceptionForHR(borderResult);
        }
    }

    private static long GetWindowStyle(nint windowHandle) => IntPtr.Size == 8
        ? GetWindowLongPtr64(windowHandle, WindowStyleIndex)
        : GetWindowLong32(windowHandle, WindowStyleIndex);

    private static void SetWindowStyle(nint windowHandle, long style)
    {
        if (IntPtr.Size == 8)
        {
            _ = SetWindowLongPtr64(windowHandle, WindowStyleIndex, (nint)style);
        }
        else
        {
            _ = SetWindowLong32(windowHandle, WindowStyleIndex, (int)style);
        }
    }

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial nint GetWindowLongPtr64(nint windowHandle, int index);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static partial int GetWindowLong32(nint windowHandle, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static partial nint SetWindowLongPtr64(nint windowHandle, int index, nint value);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static partial int SetWindowLong32(nint windowHandle, int index, int value);

    [LibraryImport("user32.dll")]
    private static partial int SetWindowPos(
        nint windowHandle,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(
        nint windowHandle,
        int attribute,
        ref uint value,
        int valueSize);
}
