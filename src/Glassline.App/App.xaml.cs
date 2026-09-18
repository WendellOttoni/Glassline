using Glassline.App.Diagnostics;
using Glassline.App.Settings;
using Glassline.Core.Media;
using Glassline.Infrastructure.Providers;
using Microsoft.UI.Xaml;

namespace Glassline.App;

public partial class App : Application, IDisposable
{
    private const string SingleInstanceMutexName = @"Local\Glassline.Main";

    private readonly Mutex _singleInstanceMutex;
    private readonly bool _ownsSingleInstanceMutex;
    private readonly StartupDiagnostics _startupDiagnostics = new();
    private readonly AppSettingsService _settingsService = new();
    private IMediaProvider? _mediaProvider;
    private Window? _window;
    private bool _isDisposed;

    public App()
    {
        _singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            SingleInstanceMutexName,
            out _ownsSingleInstanceMutex);
        InitializeComponent();
        _startupDiagnostics.Record("application-created");
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (!_ownsSingleInstanceMutex)
        {
            Dispose();
            Exit();
            return;
        }

        await _settingsService.LoadAsync();

        _mediaProvider = new PreferredMediaProvider(
            new NoctuneMediaProvider(),
            new WindowsMediaProvider());
        _window = new MainWindow(_mediaProvider, _settingsService, _startupDiagnostics);
        _window.Closed += OnMainWindowClosed;
        _window.Activate();
        _ = StartMediaProviderAsync(_mediaProvider);
        _ = RecordIdleDiagnosticsAsync(_startupDiagnostics);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        if (_ownsSingleInstanceMutex)
        {
            _singleInstanceMutex.ReleaseMutex();
        }

        _singleInstanceMutex.Dispose();
        _settingsService.Dispose();

        GC.SuppressFinalize(this);
    }

    private async void OnMainWindowClosed(object sender, WindowEventArgs args)
    {
        if (_window is not null)
        {
            _window.Closed -= OnMainWindowClosed;
            _window = null;
        }

        if (_mediaProvider is not null)
        {
            var mediaProvider = _mediaProvider;
            _mediaProvider = null;

            try
            {
                await mediaProvider.DisposeAsync();
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine($"Could not stop media providers: {exception}");
            }
        }

        await _settingsService.FlushAsync();

        Dispose();
    }

    private static async Task StartMediaProviderAsync(IMediaProvider mediaProvider)
    {
        try
        {
            await mediaProvider.StartAsync();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Could not start media provider: {exception}");
        }
    }

    private static async Task RecordIdleDiagnosticsAsync(StartupDiagnostics diagnostics)
    {
        try
        {
            await diagnostics.RecordIdleSampleAsync();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Could not record idle diagnostics: {exception}");
        }
    }
}
