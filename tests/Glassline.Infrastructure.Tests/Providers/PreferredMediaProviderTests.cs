using Glassline.Core.Media;
using Glassline.Infrastructure.Providers;

namespace Glassline.Infrastructure.Tests.Providers;

public sealed class PreferredMediaProviderTests
{
    [Fact]
    public async Task UsesFallbackUntilPreferredProviderConnects()
    {
        var preferred = new StubMediaProvider("Noctune");
        var fallback = new StubMediaProvider("Windows") { IsConnected = true };
        await using var provider = new PreferredMediaProvider(preferred, fallback);
        MediaState? published = null;
        provider.StateChanged += (_, args) => published = args.State;

        await provider.StartAsync(TestContext.Current.CancellationToken);
        fallback.Publish(new MediaState { TrackId = "windows", Title = "Windows track" });

        Assert.Equal("windows", published?.TrackId);
        Assert.Equal("Windows", provider.Name);

        preferred.IsConnected = true;
        preferred.Publish(new MediaState { TrackId = "noctune", Title = "Noctune track" });

        Assert.Equal("noctune", published?.TrackId);
        Assert.Equal("Noctune", provider.Name);
    }

    [Fact]
    public async Task SendsCommandsToActiveProvider()
    {
        var preferred = new StubMediaProvider("Noctune");
        var fallback = new StubMediaProvider("Windows") { IsConnected = true };
        await using var provider = new PreferredMediaProvider(preferred, fallback);
        await provider.StartAsync(TestContext.Current.CancellationToken);

        await provider.SendCommandAsync(
            MediaCommand.Next,
            TestContext.Current.CancellationToken);
        preferred.IsConnected = true;
        preferred.Publish(MediaState.Empty);
        await provider.SendCommandAsync(
            MediaCommand.TogglePlayback,
            TestContext.Current.CancellationToken);

        Assert.Equal(new[] { MediaCommand.TogglePlayback }, preferred.Commands);
        Assert.Equal(new[] { MediaCommand.Next }, fallback.Commands);
    }

    private sealed class StubMediaProvider(string name) : IMediaProvider
    {
        public event EventHandler<MediaStateChangedEventArgs>? StateChanged;

        public string Name { get; } = name;

        public bool IsConnected { get; set; }

        public List<MediaCommand> Commands { get; } = [];

        public ValueTask StartAsync(CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask SendCommandAsync(
            MediaCommand command,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command);
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        internal void Publish(MediaState state) =>
            StateChanged?.Invoke(this, new MediaStateChangedEventArgs(state));
    }
}
