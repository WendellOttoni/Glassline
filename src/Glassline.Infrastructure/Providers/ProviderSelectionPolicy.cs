using Glassline.Core.Media;

namespace Glassline.Infrastructure.Providers;

/// <summary>
/// Chooses the direct Noctune integration when it is available and falls back
/// to the Windows system media session otherwise.
/// </summary>
public static class ProviderSelectionPolicy
{
    public static IMediaProvider Select(
        IMediaProvider noctune,
        IMediaProvider windowsMedia)
    {
        ArgumentNullException.ThrowIfNull(noctune);
        ArgumentNullException.ThrowIfNull(windowsMedia);

        return noctune.IsConnected ? noctune : windowsMedia;
    }
}
