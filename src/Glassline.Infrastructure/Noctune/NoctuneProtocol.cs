using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using Glassline.Core.Media;

namespace Glassline.Infrastructure.Noctune;

public static class NoctuneProtocol
{
    public const int Version = 1;
    public const int MaximumMessageSize = 1024 * 1024;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
    };

    public static async ValueTask<JsonDocument?> ReadMessageAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var prefix = new byte[sizeof(int)];
        var prefixBytes = await ReadExactlyOrEndAsync(stream, prefix, cancellationToken);
        if (prefixBytes is 0)
        {
            return null;
        }

        if (prefixBytes != prefix.Length)
        {
            throw new EndOfStreamException("The Noctune message length prefix was truncated.");
        }

        var length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (length is <= 0 or > MaximumMessageSize)
        {
            throw new InvalidDataException($"Invalid Noctune message length: {length}.");
        }

        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken);

        try
        {
            return JsonDocument.Parse(payload);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The Noctune message is not valid JSON.", exception);
        }
    }

    public static async ValueTask WriteMessageAsync<T>(
        Stream stream,
        T message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(message);

        var payload = JsonSerializer.SerializeToUtf8Bytes(message, SerializerOptions);
        if (payload.Length is 0 or > MaximumMessageSize)
        {
            throw new InvalidDataException($"Invalid Noctune message length: {payload.Length}.");
        }

        var prefix = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, payload.Length);
        await stream.WriteAsync(prefix, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    internal static NoctuneEnvelope ReadEnvelope(JsonDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        NoctuneEnvelope? envelope;
        try
        {
            envelope = document.RootElement.Deserialize<NoctuneEnvelope>(SerializerOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The Noctune envelope is invalid.", exception);
        }

        if (envelope is null || envelope.Version != Version || string.IsNullOrWhiteSpace(envelope.Type))
        {
            throw new InvalidDataException("Unsupported or incomplete Noctune envelope.");
        }

        return envelope;
    }

    internal static MediaState ReadFullState(JsonElement payload)
    {
        var state = DeserializePayload<NoctuneMediaPayload>(payload);
        return new MediaState
        {
            TrackId = state.TrackId,
            Title = state.Title ?? string.Empty,
            Artist = state.Artist ?? string.Empty,
            Album = state.Album ?? string.Empty,
            ArtworkPath = state.ArtworkPath,
            Duration = Milliseconds(state.DurationMs),
            Position = Milliseconds(state.PositionMs),
            PositionCapturedAt = Timestamp(state.SentAtUnixMs),
            Playback = ParsePlayback(state.Playback),
        };
    }

    internal static PlaybackState ReadPlayback(JsonElement payload) =>
        ParsePlayback(DeserializePayload<PlaybackPayload>(payload).Playback);

    internal static (TimeSpan Position, DateTimeOffset CapturedAt) ReadPositionAnchor(
        JsonElement payload)
    {
        var anchor = DeserializePayload<PositionAnchorPayload>(payload);
        return (Milliseconds(anchor.PositionMs), Timestamp(anchor.SentAtUnixMs));
    }

    internal static (Guid Id, bool Success, string? Error) ReadCommandResult(JsonElement payload)
    {
        var result = DeserializePayload<CommandResultPayload>(payload);
        if (!Guid.TryParse(result.Id, out var id))
        {
            throw new InvalidDataException("The Noctune command result id is invalid.");
        }

        return (id, result.Success, result.Error);
    }

    internal static object CreateCommand(MediaCommand command, Guid id) => new
    {
        version = Version,
        type = "command",
        payload = new
        {
            id,
            name = command switch
            {
                MediaCommand.Previous => "previous",
                MediaCommand.TogglePlayback => "toggle_playback",
                MediaCommand.Next => "next",
                MediaCommand.ShowPlayer => "show_noctune",
                _ => throw new ArgumentOutOfRangeException(nameof(command), command, null),
            },
        },
    };

    private static T DeserializePayload<T>(JsonElement payload)
    {
        try
        {
            return payload.Deserialize<T>(SerializerOptions)
                ?? throw new InvalidDataException("The Noctune payload is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The Noctune payload is invalid.", exception);
        }
    }

    private static PlaybackState ParsePlayback(string? playback) => playback switch
    {
        "playing" => PlaybackState.Playing,
        "paused" => PlaybackState.Paused,
        "stopped" => PlaybackState.Stopped,
        _ => throw new InvalidDataException($"Unknown Noctune playback state: {playback ?? "<null>"}."),
    };

    private static TimeSpan Milliseconds(long value)
    {
        if (value < 0)
        {
            throw new InvalidDataException("Noctune time values cannot be negative.");
        }

        return TimeSpan.FromMilliseconds(value);
    }

    private static DateTimeOffset Timestamp(long unixMilliseconds)
    {
        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidDataException("The Noctune timestamp is outside the valid range.", exception);
        }
    }

    private static async ValueTask<int> ReadExactlyOrEndAsync(
        Stream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer[read..], cancellationToken);
            if (count is 0)
            {
                break;
            }

            read += count;
        }

        return read;
    }
}

internal sealed record NoctuneEnvelope(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("payload")] JsonElement Payload);

internal sealed record NoctuneMediaPayload(
    string? TrackId,
    string? Title,
    string? Artist,
    string? Album,
    string? ArtworkPath,
    long DurationMs,
    long PositionMs,
    long SentAtUnixMs,
    string? Playback);

internal sealed record PlaybackPayload(string? Playback);

internal sealed record PositionAnchorPayload(long PositionMs, long SentAtUnixMs);

internal sealed record CommandResultPayload(string? Id, bool Success, string? Error);
