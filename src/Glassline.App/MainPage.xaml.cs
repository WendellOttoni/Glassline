using Glassline.Core.Media;
using Glassline.Core.Settings;
using Glassline.App.Settings;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Glassline.App;

public sealed partial class MainPage : Page, IDisposable
{
    private static readonly TimeSpan ExpandDelay = TimeSpan.FromMilliseconds(110);
    private static readonly TimeSpan CollapseDelay = TimeSpan.FromMilliseconds(240);

    private CancellationTokenSource? _pendingTransition;
    private readonly IMediaProvider _mediaProvider;
    private readonly AppSettingsService _settingsService;
    private readonly DispatcherQueueTimer _progressTimer;
    private MediaState _mediaState = MediaState.Empty;
    private bool _isLoaded;
    private bool _isDisposed;
    private ExpansionMode _mode;

    internal MainPage(IMediaProvider mediaProvider, AppSettingsService settingsService)
    {
        _mediaProvider = mediaProvider ?? throw new ArgumentNullException(nameof(mediaProvider));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        InitializeComponent();
        _mediaProvider.StateChanged += OnMediaStateChanged;
        _settingsService.Changed += OnSettingsChanged;
        _progressTimer = DispatcherQueue.CreateTimer();
        _progressTimer.Interval = TimeSpan.FromMilliseconds(250);
        _progressTimer.IsRepeating = true;
        _progressTimer.Tick += OnProgressTimerTick;
        ApplySettings(_settingsService.Current);
    }

    public event Action<bool>? ExpansionChanged;

    public event Action? ExitRequested;

    private bool IsExpanded => _mode is not ExpansionMode.Compact;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        VisualStateManager.GoToState(this, nameof(CompactState), useTransitions: false);
        UpdateProgressTimer();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = false;
        _progressTimer.Stop();
        CancelPendingTransition();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _progressTimer.Stop();
        _progressTimer.Tick -= OnProgressTimerTick;
        _mediaProvider.StateChanged -= OnMediaStateChanged;
        _settingsService.Changed -= OnSettingsChanged;
        CancelPendingTransition();
        ExpansionChanged = null;
        ExitRequested = null;
        GC.SuppressFinalize(this);
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        CancelPendingTransition();

        if (_mode is ExpansionMode.Compact)
        {
            ScheduleTransition(ExpansionMode.Preview, ExpandDelay);
        }
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        CancelPendingTransition();

        if (_mode is ExpansionMode.Preview)
        {
            ScheduleTransition(ExpansionMode.Compact, CollapseDelay);
        }
    }

    private void OnSurfaceTapped(object sender, TappedRoutedEventArgs e)
    {
        CancelPendingTransition();
        SetMode(_mode is ExpansionMode.Pinned ? ExpansionMode.Compact : ExpansionMode.Pinned);
    }

    private void OnPreviousClick(object sender, RoutedEventArgs e) =>
        SendCommand(MediaCommand.Previous);

    private void OnTogglePlaybackClick(object sender, RoutedEventArgs e) =>
        SendCommand(MediaCommand.TogglePlayback);

    private void OnNextClick(object sender, RoutedEventArgs e) =>
        SendCommand(MediaCommand.Next);

    private void OnEscapeInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (IsExpanded)
        {
            CancelPendingTransition();
            SetMode(ExpansionMode.Compact);
            args.Handled = true;
        }
    }

    private void OnToggleExpansionInvoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        CancelPendingTransition();
        SetMode(IsExpanded ? ExpansionMode.Compact : ExpansionMode.Pinned);
        args.Handled = true;
    }

    private void OnExitClick(object sender, RoutedEventArgs e) => ExitRequested?.Invoke();

    private void OnPlacementClick(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleMenuFlyoutItem { Tag: string value }
            && Enum.TryParse<WindowPlacement>(value, out var placement))
        {
            _settingsService.SetPlacement(placement);
            ApplySettings(_settingsService.Current);
        }
    }

    private void OnAppearanceClick(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleMenuFlyoutItem { Tag: string value }
            && Enum.TryParse<AppAppearance>(value, out var appearance))
        {
            _settingsService.SetAppearance(appearance);
            ApplySettings(_settingsService.Current);
        }
    }

    private void OnHideInFullscreenClick(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleMenuFlyoutItem item)
        {
            _settingsService.SetHideInFullscreen(item.IsChecked);
        }
    }

    private async void OnStartWithWindowsClick(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleMenuFlyoutItem item)
        {
            return;
        }

        try
        {
            item.IsChecked = await _settingsService.SetStartWithWindowsAsync(item.IsChecked);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Could not change startup preference: {exception}");
            ApplySettings(_settingsService.Current);
        }
    }

    private async void ScheduleTransition(ExpansionMode mode, TimeSpan delay)
    {
        var cancellation = new CancellationTokenSource();
        _pendingTransition = cancellation;

        try
        {
            await Task.Delay(delay, cancellation.Token);

            if (!cancellation.IsCancellationRequested)
            {
                SetMode(mode);
            }
        }
        catch (OperationCanceledException)
        {
            // A newer pointer or keyboard interaction replaced this transition.
        }
        finally
        {
            if (ReferenceEquals(_pendingTransition, cancellation))
            {
                _pendingTransition = null;
            }

            cancellation.Dispose();
        }
    }

    private void SetMode(ExpansionMode mode)
    {
        if (_mode == mode)
        {
            return;
        }

        var wasExpanded = IsExpanded;
        _mode = mode;
        var isExpanded = IsExpanded;

        VisualStateManager.GoToState(
            this,
            isExpanded ? nameof(ExpandedState) : nameof(CompactState),
            useTransitions: true);

        if (wasExpanded != isExpanded)
        {
            ExpansionChanged?.Invoke(isExpanded);
        }
    }

    private void CancelPendingTransition()
    {
        _pendingTransition?.Cancel();
        _pendingTransition = null;
    }

    private void OnMediaStateChanged(object? sender, MediaStateChangedEventArgs e)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            _ = DispatcherQueue.TryEnqueue(() => ApplyMediaState(e.State));
            return;
        }

        ApplyMediaState(e.State);
    }

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            _ = DispatcherQueue.TryEnqueue(() => ApplySettings(settings));
            return;
        }

        ApplySettings(settings);
    }

    private void ApplySettings(AppSettings settings)
    {
        RequestedTheme = settings.Appearance switch
        {
            AppAppearance.Light => ElementTheme.Light,
            AppAppearance.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };

        TopLeftPlacementItem.IsChecked = settings.Placement is WindowPlacement.TopLeft;
        TopCenterPlacementItem.IsChecked = settings.Placement is WindowPlacement.TopCenter;
        TopRightPlacementItem.IsChecked = settings.Placement is WindowPlacement.TopRight;
        SystemAppearanceItem.IsChecked = settings.Appearance is AppAppearance.System;
        LightAppearanceItem.IsChecked = settings.Appearance is AppAppearance.Light;
        DarkAppearanceItem.IsChecked = settings.Appearance is AppAppearance.Dark;
        HideInFullscreenItem.IsChecked = settings.HideInFullscreen;
        StartWithWindowsItem.IsChecked = settings.StartWithWindows;
    }

    private void ApplyMediaState(MediaState state)
    {
        _mediaState = state;
        var title = state.HasTrack ? state.Title : "Glassline";
        var artist = state.HasTrack
            ? string.IsNullOrWhiteSpace(state.Artist) ? "Artista desconhecido" : state.Artist
            : "Nenhuma mídia ativa";
        var playbackGlyph = state.Playback is PlaybackState.Playing ? "\uE769" : "\uE768";
        var playbackAction = state.Playback is PlaybackState.Playing ? "Pausar" : "Reproduzir";

        CompactTitle.Text = title;
        CompactArtist.Text = artist;
        ExpandedTitle.Text = title;
        ExpandedArtist.Text = artist;
        CompactPlaybackIcon.Glyph = playbackGlyph;
        ExpandedPlaybackIcon.Glyph = playbackGlyph;
        AutomationProperties.SetName(TogglePlaybackButton, playbackAction);
        PreviousButton.IsEnabled = state.HasTrack;
        TogglePlaybackButton.IsEnabled = state.HasTrack;
        NextButton.IsEnabled = state.HasTrack;
        ApplyArtwork(state.ArtworkPath);
        UpdateProgress();
        UpdateProgressTimer();
    }

    private void ApplyArtwork(string? artworkPath)
    {
        BitmapImage? artwork = null;
        if (!string.IsNullOrWhiteSpace(artworkPath)
            && Path.IsPathFullyQualified(artworkPath)
            && File.Exists(artworkPath))
        {
            artwork = new BitmapImage(new Uri(artworkPath));
        }

        CompactArtwork.Source = artwork;
        ExpandedArtwork.Source = artwork;
        CompactArtwork.Visibility = artwork is null ? Visibility.Collapsed : Visibility.Visible;
        ExpandedArtwork.Visibility = artwork is null ? Visibility.Collapsed : Visibility.Visible;
        CompactArtworkFallback.Visibility = artwork is null ? Visibility.Visible : Visibility.Collapsed;
        ExpandedArtworkFallback.Visibility = artwork is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnArtworkFailed(object sender, ExceptionRoutedEventArgs e)
    {
        CompactArtwork.Source = null;
        ExpandedArtwork.Source = null;
        CompactArtwork.Visibility = Visibility.Collapsed;
        ExpandedArtwork.Visibility = Visibility.Collapsed;
        CompactArtworkFallback.Visibility = Visibility.Visible;
        ExpandedArtworkFallback.Visibility = Visibility.Visible;
        System.Diagnostics.Debug.WriteLine($"Could not decode media artwork: {e.ErrorMessage}");
    }

    private void OnProgressTimerTick(DispatcherQueueTimer sender, object args) => UpdateProgress();

    private void UpdateProgress()
    {
        var position = PlaybackPosition.At(_mediaState, DateTimeOffset.UtcNow);
        PlaybackProgress.Value = _mediaState.Duration > TimeSpan.Zero
            ? Math.Clamp(position.TotalMilliseconds / _mediaState.Duration.TotalMilliseconds, 0, 1)
            : 0;
    }

    private void UpdateProgressTimer()
    {
        var shouldRun = _isLoaded
            && _mediaState.Playback is PlaybackState.Playing
            && _mediaState.Duration > TimeSpan.Zero;

        if (shouldRun && !_progressTimer.IsRunning)
        {
            _progressTimer.Start();
        }
        else if (!shouldRun && _progressTimer.IsRunning)
        {
            _progressTimer.Stop();
        }
    }

    private async void SendCommand(MediaCommand command)
    {
        try
        {
            await _mediaProvider.SendCommandAsync(command);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Media command {command} failed: {exception}");
        }
    }

    private enum ExpansionMode
    {
        Compact,
        Preview,
        Pinned,
    }
}
