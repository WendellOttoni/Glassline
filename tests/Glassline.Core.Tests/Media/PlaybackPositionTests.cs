using Glassline.Core.Media;

namespace Glassline.Core.Tests.Media;

public sealed class PlaybackPositionTests
{
    private static readonly DateTimeOffset CapturedAt = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AtInterpolatesWhilePlaying()
    {
        var state = CreateState(PlaybackState.Playing, positionSeconds: 20, durationSeconds: 180);

        var position = PlaybackPosition.At(state, CapturedAt.AddSeconds(3));

        Assert.Equal(TimeSpan.FromSeconds(23), position);
    }

    [Fact]
    public void AtDoesNotInterpolateWhilePaused()
    {
        var state = CreateState(PlaybackState.Paused, positionSeconds: 20, durationSeconds: 180);

        var position = PlaybackPosition.At(state, CapturedAt.AddSeconds(3));

        Assert.Equal(TimeSpan.FromSeconds(20), position);
    }

    [Fact]
    public void AtClampsPositionToDuration()
    {
        var state = CreateState(PlaybackState.Playing, positionSeconds: 179, durationSeconds: 180);

        var position = PlaybackPosition.At(state, CapturedAt.AddSeconds(5));

        Assert.Equal(TimeSpan.FromSeconds(180), position);
    }

    private static MediaState CreateState(
        PlaybackState playback,
        double positionSeconds,
        double durationSeconds) => new()
        {
            TrackId = "test:track",
            Title = "Test track",
            Duration = TimeSpan.FromSeconds(durationSeconds),
            Position = TimeSpan.FromSeconds(positionSeconds),
            PositionCapturedAt = CapturedAt,
            Playback = playback,
        };
}
