namespace Glassline.Core.Media;

public interface IMediaProvider : IAsyncDisposable
{
    event EventHandler<MediaStateChangedEventArgs>? StateChanged;

    string Name { get; }

    bool IsConnected { get; }

    ValueTask StartAsync(CancellationToken cancellationToken = default);

    ValueTask SendCommandAsync(
        MediaCommand command,
        CancellationToken cancellationToken = default);
}
