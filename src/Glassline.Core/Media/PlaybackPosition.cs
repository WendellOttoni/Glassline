namespace Glassline.Core.Media;

public static class PlaybackPosition
{
    public static TimeSpan At(MediaState state, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);

        var position = state.Position < TimeSpan.Zero ? TimeSpan.Zero : state.Position;

        if (state.Playback is PlaybackState.Playing && now > state.PositionCapturedAt)
        {
            position += now - state.PositionCapturedAt;
        }

        if (state.Duration > TimeSpan.Zero && position > state.Duration)
        {
            return state.Duration;
        }

        return position;
    }
}
