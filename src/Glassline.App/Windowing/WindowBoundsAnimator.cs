using System.Diagnostics;
using Glassline.Core.Settings;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace Glassline.App.Windowing;

internal sealed class WindowBoundsAnimator : IDisposable
{
    private static readonly TimeSpan AnimationDuration = TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(16);
    private const int TopMargin = 12;

    private readonly AppWindow _window;
    private readonly Action _boundsChanged;
    private readonly DispatcherQueueTimer _timer;
    private readonly Stopwatch _stopwatch = new();
    private RectInt32 _startBounds;
    private RectInt32 _targetBounds;
    private WindowPlacement _placement;

    internal WindowBoundsAnimator(AppWindow window, Action boundsChanged)
    {
        _window = window;
        _boundsChanged = boundsChanged;
        var dispatcherQueue = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("The XAML dispatcher queue is not available.");
        _timer = dispatcherQueue.CreateTimer();
        _timer.Interval = FrameInterval;
        _timer.IsRepeating = true;
        _timer.Tick += OnTick;
    }

    internal void Place(
        SizeInt32 effectiveSize,
        double rasterizationScale,
        WindowPlacement placement)
    {
        _timer.Stop();
        _stopwatch.Reset();
        _placement = placement;
        _window.MoveAndResize(GetTargetBounds(effectiveSize, rasterizationScale, placement));
        _boundsChanged();
    }

    internal void AnimateTo(SizeInt32 effectiveSize, double rasterizationScale)
    {
        _startBounds = new RectInt32(
            _window.Position.X,
            _window.Position.Y,
            _window.Size.Width,
            _window.Size.Height);
        _targetBounds = GetTargetBounds(effectiveSize, rasterizationScale, _placement);

        _stopwatch.Restart();
        _timer.Start();
        ApplyProgress(0);
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
    }

    private static RectInt32 GetTargetBounds(
        SizeInt32 effectiveSize,
        double rasterizationScale,
        WindowPlacement placement)
    {
        var displayArea = DisplayArea.Primary;
        var workArea = displayArea.WorkArea;
        var width = (int)Math.Round(effectiveSize.Width * rasterizationScale);
        var height = (int)Math.Round(effectiveSize.Height * rasterizationScale);
        var margin = (int)Math.Round(TopMargin * rasterizationScale);
        var horizontalMargin = (int)Math.Round(TopMargin * rasterizationScale);
        var x = placement switch
        {
            WindowPlacement.TopLeft => workArea.X + horizontalMargin,
            WindowPlacement.TopCenter => workArea.X + ((workArea.Width - width) / 2),
            WindowPlacement.TopRight => workArea.X + workArea.Width - width - horizontalMargin,
            _ => throw new ArgumentOutOfRangeException(nameof(placement), placement, null),
        };
        var y = workArea.Y + margin;

        return new RectInt32(x, y, width, height);
    }

    private void OnTick(DispatcherQueueTimer sender, object args)
    {
        var progress = Math.Clamp(
            _stopwatch.Elapsed.TotalMilliseconds / AnimationDuration.TotalMilliseconds,
            0,
            1);
        ApplyProgress(EaseOutCubic(progress));

        if (progress >= 1)
        {
            sender.Stop();
            _stopwatch.Reset();
        }
    }

    private void ApplyProgress(double progress)
    {
        _window.MoveAndResize(new RectInt32(
            Interpolate(_startBounds.X, _targetBounds.X, progress),
            Interpolate(_startBounds.Y, _targetBounds.Y, progress),
            Interpolate(_startBounds.Width, _targetBounds.Width, progress),
            Interpolate(_startBounds.Height, _targetBounds.Height, progress)));
        _boundsChanged();
    }

    private static double EaseOutCubic(double value) => 1 - Math.Pow(1 - value, 3);

    private static int Interpolate(int start, int end, double progress) =>
        (int)Math.Round(start + ((end - start) * progress));
}
