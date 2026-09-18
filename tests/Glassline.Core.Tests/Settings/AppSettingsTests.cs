using System.Text.Json;
using Glassline.Core.Settings;

namespace Glassline.Core.Tests.Settings;

public sealed class AppSettingsTests
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    [Fact]
    public void DefaultsAreSafeForFirstLaunch()
    {
        var settings = new AppSettings();

        Assert.Equal(WindowPlacement.TopCenter, settings.Placement);
        Assert.Equal(AppAppearance.System, settings.Appearance);
        Assert.True(settings.HideInFullscreen);
        Assert.False(settings.StartWithWindows);
    }

    [Fact]
    public void JsonRoundTripsEveryPreference()
    {
        var expected = new AppSettings
        {
            Placement = WindowPlacement.TopRight,
            Appearance = AppAppearance.Dark,
            HideInFullscreen = false,
            StartWithWindows = true,
        };

        var json = JsonSerializer.Serialize(expected, SerializerOptions);
        var actual = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions);

        Assert.NotNull(actual);
        Assert.Equal(expected, actual!);
    }
}
