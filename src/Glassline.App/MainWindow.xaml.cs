using Glassline.App.Diagnostics;
using Glassline.App.Settings;
using Glassline.App.Windowing;
using Glassline.Core.Media;
using Glassline.Core.Settings;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace Glassline.App;

public sealed partial class MainWindow : Window, IDisposable
{
    private static readonly SizeInt32 CompactSize = new(360, 64);
    private static readonly SizeInt32 ExpandedSize = new(420, 184);
    private readonly WindowBoundsAnimator _boundsAnimator;
    private readonly AppSettingsService _settingsService;
    private FullscreenWatcher? _fullscreenWatcher;
    private SizeInt32 _effectiveSize = CompactSize;
    private WindowPlacement _placement;
    private bool _isDisposed;

    private readonly StartupDiagnostics _startupDiagnostics;

    internal MainWindow(
        IMediaProvider mediaProvider,
        AppSettingsService settingsService,
        StartupDiagnostics startupDiagnostics)
    {
        ArgumentNullException.ThrowIfNull(mediaProvider);
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _placement = _settingsService.Current.Placement;
        _startupDiagnostics = startupDiagnostics ?? throw new ArgumentNullException(nameof(startupDiagnostics));
        InitializeComponent();

        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.IsShownInSwitchers = false;

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = true;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false;
        }

        NativeWindow.DisableSystemFrame(this);
        _boundsAnimator = new WindowBoundsAnimator(AppWindow, OnWindowBoundsChanged);
        _boundsAnimator.Place(
            CompactSize,
            rasterizationScale: 1,
            _placement);
        RootFrame.Content = new MainPage(mediaProvider, _settingsService);
        RootFrame.Loaded += OnRootFrameLoaded;
        _settingsService.Changed += OnSettingsChanged;
        Activated += OnActivated;
        Closed += OnClosed;

        if (RootFrame.Content is MainPage page)
        {
            page.ExpansionChanged += OnExpansionChanged;
            page.ExitRequested += OnExitRequested;
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _fullscreenWatcher?.Dispose();
        _fullscreenWatcher = null;
        _boundsAnimator.Dispose();
        RootFrame.Loaded -= OnRootFrameLoaded;
        _settingsService.Changed -= OnSettingsChanged;
        Activated -= OnActivated;
        Closed -= OnClosed;

        if (RootFrame.Content is MainPage page)
        {
            page.ExpansionChanged -= OnExpansionChanged;
            page.ExitRequested -= OnExitRequested;
            page.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    private double RasterizationScale => RootFrame.XamlRoot?.RasterizationScale ?? 1;

    private void OnRootFrameLoaded(object sender, RoutedEventArgs e)
    {
        _boundsAnimator.Place(
            _effectiveSize,
            RasterizationScale,
            _placement);
        _startupDiagnostics.Record("window-content-loaded");
    }

    private void OnWindowBoundsChanged()
    {
        NativeWindow.ApplyRoundedRegion(this, RasterizationScale);
        NativeWindow.DisableSystemFrame(this);
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        NativeWindow.DisableSystemFrame(this);

        if (_settingsService.Current.HideInFullscreen && _fullscreenWatcher is null)
        {
            try
            {
                _fullscreenWatcher = new FullscreenWatcher(this, OnFullscreenChanged);
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Fullscreen detection is unavailable: {exception}");
            }
        }
    }

    private void OnExpansionChanged(bool isExpanded)
    {
        _effectiveSize = isExpanded ? ExpandedSize : CompactSize;
        _boundsAnimator.AnimateTo(_effectiveSize, RasterizationScale);
    }

    private void OnExitRequested() => Close();

    private void OnFullscreenChanged(bool isFullscreen)
    {
        if (isFullscreen)
        {
            AppWindow.Hide();
        }
        else
        {
            AppWindow.Show(activateWindow: false);
        }
    }

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        if (_placement != settings.Placement)
        {
            _placement = settings.Placement;
            _boundsAnimator.Place(_effectiveSize, RasterizationScale, _placement);
        }

        if (settings.HideInFullscreen)
        {
            if (_fullscreenWatcher is null)
            {
                try
                {
                    _fullscreenWatcher = new FullscreenWatcher(this, OnFullscreenChanged);
                }
                catch (Exception exception)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Fullscreen detection is unavailable: {exception}");
                }
            }
        }
        else
        {
            _fullscreenWatcher?.Dispose();
            _fullscreenWatcher = null;
            AppWindow.Show(activateWindow: false);
        }
    }

    private void OnClosed(object sender, WindowEventArgs args) => Dispose();
}
