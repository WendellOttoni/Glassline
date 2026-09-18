namespace Glassline.Core.Media;

public sealed class MediaStateChangedEventArgs(MediaState state) : EventArgs
{
    public MediaState State { get; } = state ?? throw new ArgumentNullException(nameof(state));
}
