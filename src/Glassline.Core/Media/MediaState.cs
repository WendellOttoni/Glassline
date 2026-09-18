namespace Glassline.Core.Media;

public sealed record MediaState
{
    public static MediaState Empty { get; } = new();

    public string? TrackId { get; init; }

    public string Title { get; init; } = string.Empty;

    public string Artist { get; init; } = string.Empty;

    public string Album { get; init; } = string.Empty;

    public string? ArtworkPath { get; init; }

    public TimeSpan Duration { get; init; }

    public TimeSpan Position { get; init; }

    public DateTimeOffset PositionCapturedAt { get; init; }

    public PlaybackState Playback { get; init; } = PlaybackState.Stopped;

    public bool HasTrack => !string.IsNullOrWhiteSpace(TrackId) || !string.IsNullOrWhiteSpace(Title);
}
