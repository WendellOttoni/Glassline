using Glassline.Core.Media;

namespace Glassline.Infrastructure.Providers;

/// <summary>
/// Presents one provider to the UI, preferring the direct integration only
/// while it is connected and retaining the latest fallback state.
/// </summary>
public sealed class PreferredMediaProvider : IMediaProvider
{
    private readonly IMediaProvider _preferred;
    private readonly IMediaProvider _fallback;
    private MediaState _preferredState = MediaState.Empty;
    private MediaState _fallbackState = MediaState.Empty;
    private bool _isStarted;
    private bool _isDisposed;

    public PreferredMediaProvider(IMediaProvider preferred, IMediaProvider fallback)
    {
        _preferred = preferred ?? throw new ArgumentNullException(nameof(preferred));
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
    }

    public event EventHandler<MediaStateChangedEventArgs>? StateChanged;

    public string Name => ActiveProvider.Name;

    public bool IsConnected => _preferred.IsConnected || _fallback.IsConnected;

    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (_isStarted)
        {
            return;
        }

        _isStarted = true;
        _preferred.StateChanged += OnPreferredStateChanged;
        _fallback.StateChanged += OnFallbackStateChanged;

        await StartProviderAsync(_preferred, cancellationToken);
        await StartProviderAsync(_fallback, cancellationToken);
        PublishActiveState();
    }

    public ValueTask SendCommandAsync(
        MediaCommand command,
        CancellationToken cancellationToken = default) =>
        ActiveProvider.SendCommandAsync(command, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _preferred.StateChanged -= OnPreferredStateChanged;
        _fallback.StateChanged -= OnFallbackStateChanged;

        Exception? firstException = null;
        try
        {
            await _preferred.DisposeAsync();
        }
        catch (Exception exception)
        {
            firstException = exception;
        }

        try
        {
            await _fallback.DisposeAsync();
        }
        catch when (firstException is not null)
        {
        }

        StateChanged = null;
        if (firstException is not null)
        {
            throw firstException;
        }
    }

    private IMediaProvider ActiveProvider => _preferred.IsConnected ? _preferred : _fallback;

    private static async ValueTask StartProviderAsync(
        IMediaProvider provider,
        CancellationToken cancellationToken)
    {
        try
        {
            await provider.StartAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Media provider {provider.Name} could not start: {exception}");
        }
    }

    private void OnPreferredStateChanged(object? sender, MediaStateChangedEventArgs e)
    {
        _preferredState = e.State;
        PublishActiveState();
    }

    private void OnFallbackStateChanged(object? sender, MediaStateChangedEventArgs e)
    {
        _fallbackState = e.State;
        PublishActiveState();
    }

    private void PublishActiveState()
    {
        var state = _preferred.IsConnected ? _preferredState : _fallbackState;
        StateChanged?.Invoke(this, new MediaStateChangedEventArgs(state));
    }
}
