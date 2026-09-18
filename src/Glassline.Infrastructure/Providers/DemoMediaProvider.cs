using Glassline.Core.Media;

namespace Glassline.Infrastructure.Providers;

/// <summary>
/// Event-driven provider used while the Windows and Noctune integrations are
/// not available. It exercises the complete UI/provider flow without polling.
/// </summary>
public sealed class DemoMediaProvider : IMediaProvider
{
    private MediaState _state = new()
    {
        TrackId = "glassline-demo",
        Title = "Midnight Glass",
        Artist = "Glassline",
        Album = "Development preview",
        Duration = TimeSpan.FromMinutes(3.5),
        Position = TimeSpan.FromSeconds(42),
        PositionCapturedAt = DateTimeOffset.UtcNow,
        Playback = PlaybackState.Paused,
    };
    private bool _isDisposed;

    public event EventHandler<MediaStateChangedEventArgs>? StateChanged;

    public string Name => "Demo";

    public bool IsConnected { get; private set; }

    public ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        IsConnected = true;
        Publish(_state);
        return ValueTask.CompletedTask;
    }

    public ValueTask SendCommandAsync(
        MediaCommand command,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsConnected)
        {
            throw new InvalidOperationException("The demo media provider has not been started.");
        }

        var now = DateTimeOffset.UtcNow;
        var position = PlaybackPosition.At(_state, now);
        _state = command switch
        {
            MediaCommand.Previous => _state with
            {
                Position = TimeSpan.Zero,
                PositionCapturedAt = now,
            },
            MediaCommand.TogglePlayback => _state with
            {
                Position = position,
                PositionCapturedAt = now,
                Playback = _state.Playback is PlaybackState.Playing
                    ? PlaybackState.Paused
                    : PlaybackState.Playing,
            },
            MediaCommand.Next => _state with
            {
                TrackId = _state.TrackId == "glassline-demo" ? "glassline-demo-2" : "glassline-demo",
                Title = _state.TrackId == "glassline-demo" ? "After the Rain" : "Midnight Glass",
                Position = TimeSpan.Zero,
                PositionCapturedAt = now,
            },
            MediaCommand.ShowPlayer => _state,
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, null),
        };

        Publish(_state);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return ValueTask.CompletedTask;
        }

        _isDisposed = true;
        IsConnected = false;
        StateChanged = null;
        return ValueTask.CompletedTask;
    }

    private void Publish(MediaState state) =>
        StateChanged?.Invoke(this, new MediaStateChangedEventArgs(state));
}
