using Glassline.Core.Media;
using System.Security.Cryptography;
using System.Text;
using Windows.Media.Control;

namespace Glassline.Infrastructure.Providers;

/// <summary>
/// Reads and controls the Windows global media session. Updates are driven by
/// SMTC events; no background polling is performed.
/// </summary>
public sealed class WindowsMediaProvider : IMediaProvider
{
    private const int MaximumArtworkFiles = 64;
    private const long MaximumArtworkCacheBytes = 128L * 1024 * 1024;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private string? _artworkTrackId;
    private string? _artworkPath;
    private bool _isDisposed;

    public event EventHandler<MediaStateChangedEventArgs>? StateChanged;

    public string Name => "Windows Media";

    public bool IsConnected => _session is not null;

    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (_manager is not null)
        {
            return;
        }

        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        cancellationToken.ThrowIfCancellationRequested();
        _manager.CurrentSessionChanged += OnCurrentSessionChanged;
        await SelectCurrentSessionAsync(cancellationToken);
    }

    public async ValueTask SendCommandAsync(
        MediaCommand command,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        var session = _session
            ?? throw new InvalidOperationException("No Windows media session is active.");

        _ = command switch
        {
            MediaCommand.Previous => await session.TrySkipPreviousAsync(),
            MediaCommand.TogglePlayback => await session.TryTogglePlayPauseAsync(),
            MediaCommand.Next => await session.TrySkipNextAsync(),
            MediaCommand.ShowPlayer => throw new NotSupportedException(
                "Windows SMTC does not expose an operation to show the player."),
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, null),
        };
    }

    public ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return ValueTask.CompletedTask;
        }

        _isDisposed = true;
        if (_manager is not null)
        {
            _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
            _manager = null;
        }

        DetachSession();
        StateChanged = null;
        return ValueTask.CompletedTask;
    }

    private async void OnCurrentSessionChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        CurrentSessionChangedEventArgs args) => await TryRunAsync(SelectCurrentSessionAsync);

    private async void OnSessionStateChanged(
        GlobalSystemMediaTransportControlsSession sender,
        object args) => await TryRunAsync(RefreshStateAsync);

    private async Task SelectCurrentSessionAsync(CancellationToken cancellationToken)
    {
        var nextSession = _manager?.GetCurrentSession();
        if (!ReferenceEquals(nextSession, _session))
        {
            DetachSession();
            _session = nextSession;
            AttachSession();
        }

        await RefreshStateAsync(cancellationToken);
    }

    private async Task RefreshStateAsync(CancellationToken cancellationToken)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            var session = _session;
            if (session is null)
            {
                Publish(MediaState.Empty);
                return;
            }

            var properties = await session.TryGetMediaPropertiesAsync();
            cancellationToken.ThrowIfCancellationRequested();
            var timeline = session.GetTimelineProperties();
            var playback = session.GetPlaybackInfo();
            var trackId = BuildTrackId(
                session.SourceAppUserModelId,
                properties.Title,
                properties.Artist);
            if (!string.Equals(trackId, _artworkTrackId, StringComparison.Ordinal))
            {
                _artworkTrackId = trackId;
                _artworkPath = await CacheArtworkAsync(
                    properties.Thumbnail,
                    trackId,
                    cancellationToken);
            }

            Publish(new MediaState
            {
                TrackId = trackId,
                Title = properties.Title ?? string.Empty,
                Artist = properties.Artist ?? string.Empty,
                Album = properties.AlbumTitle ?? string.Empty,
                ArtworkPath = _artworkPath,
                Duration = timeline.EndTime > TimeSpan.Zero ? timeline.EndTime : TimeSpan.Zero,
                Position = timeline.Position,
                PositionCapturedAt = DateTimeOffset.UtcNow,
                Playback = MapPlaybackState(playback.PlaybackStatus),
            });
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private void AttachSession()
    {
        if (_session is null)
        {
            return;
        }

        _session.MediaPropertiesChanged += OnSessionStateChanged;
        _session.PlaybackInfoChanged += OnSessionStateChanged;
        _session.TimelinePropertiesChanged += OnSessionStateChanged;
    }

    private void DetachSession()
    {
        if (_session is null)
        {
            return;
        }

        _session.MediaPropertiesChanged -= OnSessionStateChanged;
        _session.PlaybackInfoChanged -= OnSessionStateChanged;
        _session.TimelinePropertiesChanged -= OnSessionStateChanged;
        _session = null;
        _artworkTrackId = null;
        _artworkPath = null;
    }

    private async Task TryRunAsync(Func<CancellationToken, Task> operation)
    {
        try
        {
            await operation(CancellationToken.None);
        }
        catch (ObjectDisposedException) when (_isDisposed)
        {
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Windows media provider update failed: {exception}");
        }
    }

    private void Publish(MediaState state) =>
        StateChanged?.Invoke(this, new MediaStateChangedEventArgs(state));

    private static string? BuildTrackId(string source, string? title, string? artist)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        return $"{source}\u001f{title}\u001f{artist}";
    }

    private static PlaybackState MapPlaybackState(
        GlobalSystemMediaTransportControlsSessionPlaybackStatus status) => status switch
        {
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => PlaybackState.Playing,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => PlaybackState.Paused,
            _ => PlaybackState.Stopped,
        };

    private static async ValueTask<string?> CacheArtworkAsync(
        Windows.Storage.Streams.IRandomAccessStreamReference? thumbnail,
        string? trackId,
        CancellationToken cancellationToken)
    {
        if (thumbnail is null || string.IsNullOrWhiteSpace(trackId))
        {
            return null;
        }

        try
        {
            var cacheDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Glassline",
                "artwork-cache");
            Directory.CreateDirectory(cacheDirectory);
            var fileName = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(trackId))) + ".image";
            var path = Path.Combine(cacheDirectory, fileName);

            if (File.Exists(path))
            {
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
                PruneArtworkCache(cacheDirectory, path);
                return path;
            }

            using var randomAccessStream = await thumbnail.OpenReadAsync();
            await using var source = randomAccessStream.AsStreamForRead();
            await using var destination = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 81920,
                FileOptions.Asynchronous);
            await source.CopyToAsync(destination, cancellationToken);
            PruneArtworkCache(cacheDirectory, path);
            return path;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"Could not cache SMTC artwork: {exception.Message}");
            return null;
        }
    }

    private static void PruneArtworkCache(string directory, string retainedPath)
    {
        try
        {
            var files = new DirectoryInfo(directory)
                .EnumerateFiles("*.image", SearchOption.TopDirectoryOnly)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ToList();
            var totalBytes = files.Sum(file => file.Length);

            for (var index = files.Count - 1;
                 index >= 0 && (files.Count > MaximumArtworkFiles || totalBytes > MaximumArtworkCacheBytes);
                 index--)
            {
                var file = files[index];
                if (string.Equals(file.FullName, retainedPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                totalBytes -= file.Length;
                file.Delete();
                files.RemoveAt(index);
            }
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Could not prune SMTC artwork cache: {exception.Message}");
        }
    }
}
