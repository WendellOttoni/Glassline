using System.IO.Pipes;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Glassline.Core.Media;
using Glassline.Infrastructure.Noctune;

namespace Glassline.Infrastructure.Providers;

public sealed class NoctuneMediaProvider : IMediaProvider
{
    private const string PipePrefix = "glassline-noctune-v1";

    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<bool>> _pendingCommands = new();
    private readonly object _connectionLock = new();
    private NamedPipeServerStream? _connection;
    private Task? _listenTask;
    private MediaState _state = MediaState.Empty;
    private bool _isDisposed;

    public event EventHandler<MediaStateChangedEventArgs>? StateChanged;

    public string Name => "Noctune";

    public bool IsConnected
    {
        get
        {
            lock (_connectionLock)
            {
                return _connection?.IsConnected is true;
            }
        }
    }

    public ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        _listenTask ??= ListenAsync(_lifetime.Token);
        return ValueTask.CompletedTask;
    }

    public async ValueTask SendCommandAsync(
        MediaCommand command,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        var id = Guid.NewGuid();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pendingCommands.TryAdd(id, completion))
        {
            throw new InvalidOperationException("Could not register the Noctune command.");
        }

        try
        {
            await _writeLock.WaitAsync(cancellationToken);
            try
            {
                NamedPipeServerStream connection;
                lock (_connectionLock)
                {
                    connection = _connection is { IsConnected: true } active
                        ? active
                        : throw new InvalidOperationException("Noctune is not connected.");
                }

                await NoctuneProtocol.WriteMessageAsync(
                    connection,
                    NoctuneProtocol.CreateCommand(command, id),
                    cancellationToken);
            }
            finally
            {
                _writeLock.Release();
            }

            var succeeded = await completion.Task.WaitAsync(
                TimeSpan.FromSeconds(2),
                cancellationToken);
            if (!succeeded)
            {
                throw new InvalidOperationException($"Noctune rejected the {command} command.");
            }
        }
        finally
        {
            _pendingCommands.TryRemove(id, out _);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        await _lifetime.CancelAsync();
        FailPendingCommands(new ObjectDisposedException(nameof(NoctuneMediaProvider)));
        DisposeConnection();

        if (_listenTask is not null)
        {
            try
            {
                await _listenTask;
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
            }
        }

        _lifetime.Dispose();
        _writeLock.Dispose();
        StateChanged = null;
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var server = CreateServer();
            SetConnection(server);

            try
            {
                await server.WaitForConnectionAsync(cancellationToken);
                await ReadConnectionAsync(server, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException)
            {
                System.Diagnostics.Debug.WriteLine($"Noctune pipe connection ended: {exception.Message}");
            }
            finally
            {
                ClearConnection(server);
                FailPendingCommands(new IOException("The Noctune connection was closed."));
                _state = MediaState.Empty;
                Publish(_state);
            }
        }
    }

    private async Task ReadConnectionAsync(
        NamedPipeServerStream connection,
        CancellationToken cancellationToken)
    {
        while (connection.IsConnected && !cancellationToken.IsCancellationRequested)
        {
            using var document = await NoctuneProtocol.ReadMessageAsync(connection, cancellationToken);
            if (document is null)
            {
                return;
            }

            Apply(NoctuneProtocol.ReadEnvelope(document));
        }
    }

    private void Apply(NoctuneEnvelope envelope)
    {
        var receivedAt = DateTimeOffset.UtcNow;
        _state = envelope.Type switch
        {
            "snapshot" or "track_changed" => NoctuneProtocol.ReadFullState(envelope.Payload),
            "playback_changed" => _state with
            {
                Position = PlaybackPosition.At(_state, receivedAt),
                PositionCapturedAt = receivedAt,
                Playback = NoctuneProtocol.ReadPlayback(envelope.Payload),
            },
            "position_anchor" => ApplyPositionAnchor(_state, envelope.Payload),
            "command_result" => ApplyCommandResult(_state, envelope.Payload),
            _ => throw new InvalidDataException($"Unknown Noctune message type: {envelope.Type}."),
        };

        if (envelope.Type is not "command_result")
        {
            Publish(_state);
        }
    }

    private static MediaState ApplyPositionAnchor(MediaState state, System.Text.Json.JsonElement payload)
    {
        var anchor = NoctuneProtocol.ReadPositionAnchor(payload);
        return state with
        {
            Position = anchor.Position,
            PositionCapturedAt = anchor.CapturedAt,
        };
    }

    private MediaState ApplyCommandResult(MediaState state, System.Text.Json.JsonElement payload)
    {
        var result = NoctuneProtocol.ReadCommandResult(payload);
        if (_pendingCommands.TryRemove(result.Id, out var completion))
        {
            if (result.Success)
            {
                completion.TrySetResult(true);
            }
            else if (!string.IsNullOrWhiteSpace(result.Error))
            {
                completion.TrySetException(new InvalidOperationException(result.Error));
            }
            else
            {
                completion.TrySetResult(false);
            }
        }

        return state;
    }

    private static NamedPipeServerStream CreateServer() => new(
        GetCurrentUserPipeName(),
        PipeDirection.InOut,
        maxNumberOfServerInstances: 1,
        PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    public static string GetCurrentUserPipeName()
    {
        var identity = Environment.UserDomainName + "\\" + Environment.UserName;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16];
        return $"{PipePrefix}-{hash}";
    }

    private void SetConnection(NamedPipeServerStream connection)
    {
        lock (_connectionLock)
        {
            _connection = connection;
        }
    }

    private void ClearConnection(NamedPipeServerStream connection)
    {
        lock (_connectionLock)
        {
            if (ReferenceEquals(_connection, connection))
            {
                _connection = null;
            }
        }
    }

    private void DisposeConnection()
    {
        lock (_connectionLock)
        {
            _connection?.Dispose();
            _connection = null;
        }
    }

    private void FailPendingCommands(Exception exception)
    {
        foreach (var command in _pendingCommands)
        {
            if (_pendingCommands.TryRemove(command.Key, out var completion))
            {
                completion.TrySetException(exception);
            }
        }
    }

    private void Publish(MediaState state) =>
        StateChanged?.Invoke(this, new MediaStateChangedEventArgs(state));
}
