using System.Text.Json;
using Glassline.Core.Settings;
using Windows.ApplicationModel;
using Windows.Storage;

namespace Glassline.App.Settings;

internal sealed class AppSettingsService : IDisposable
{
    private const string SettingsFileName = "settings.json";
    private const string StartupTaskId = "GlasslineStartup";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private Task _pendingSave = Task.CompletedTask;
    private bool _isDisposed;

    internal AppSettings Current { get; private set; } = new();

    internal event EventHandler<AppSettings>? Changed;

    internal async ValueTask LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var file = await ApplicationData.Current.LocalFolder.TryGetItemAsync(SettingsFileName)
                as StorageFile;
            if (file is not null)
            {
                await using var stream = await file.OpenStreamForReadAsync();
                Current = await JsonSerializer.DeserializeAsync<AppSettings>(
                    stream,
                    SerializerOptions,
                    cancellationToken) ?? new AppSettings();
            }

        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"Could not load Glassline settings: {exception}");
            Current = new AppSettings();
        }

        try
        {
            Current = Current with { StartWithWindows = await ReadStartupStateAsync() };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"Could not read startup state: {exception}");
        }
    }

    internal void SetPlacement(WindowPlacement placement) =>
        Update(Current with { Placement = placement });

    internal void SetAppearance(AppAppearance appearance) =>
        Update(Current with { Appearance = appearance });

    internal void SetHideInFullscreen(bool value) =>
        Update(Current with { HideInFullscreen = value });

    internal async ValueTask<bool> SetStartWithWindowsAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var startupTask = await StartupTask.GetAsync(StartupTaskId);
        if (enabled)
        {
            _ = await startupTask.RequestEnableAsync();
        }
        else
        {
            startupTask.Disable();
        }

        cancellationToken.ThrowIfCancellationRequested();
        var isEnabled = IsEnabled(startupTask.State);
        Update(Current with { StartWithWindows = isEnabled });
        return isEnabled;
    }

    internal ValueTask FlushAsync() => new(_pendingSave);

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _saveLock.Dispose();
        GC.SuppressFinalize(this);
    }

    private void Update(AppSettings value)
    {
        if (Current == value)
        {
            return;
        }

        Current = value;
        Changed?.Invoke(this, value);
        _pendingSave = SaveAsync(value);
    }

    private async Task SaveAsync(AppSettings value)
    {
        await _saveLock.WaitAsync();
        try
        {
            var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(
                SettingsFileName,
                CreationCollisionOption.ReplaceExisting);
            await using var stream = await file.OpenStreamForWriteAsync();
            await JsonSerializer.SerializeAsync(stream, value, SerializerOptions);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Could not save Glassline settings: {exception}");
        }
        finally
        {
            _saveLock.Release();
        }
    }

    private static async ValueTask<bool> ReadStartupStateAsync()
    {
        var startupTask = await StartupTask.GetAsync(StartupTaskId);
        return IsEnabled(startupTask.State);
    }

    private static bool IsEnabled(StartupTaskState state) => state is
        StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
}
