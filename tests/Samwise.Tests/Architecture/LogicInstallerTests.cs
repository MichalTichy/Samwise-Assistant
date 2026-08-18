using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Samwise.Logic.Inbox;
using Samwise.Logic.Inbox.Usecases;
using Samwise.Logic.Installers;

namespace Samwise.Tests.Architecture;

public sealed class LogicInstallerTests
{
    [Fact]
    public async Task CaptureUsecaseIsDiscoveredAndDelegatesToTheInboundStore()
    {
        var expectedId = Guid.NewGuid();
        var store = new RecordingInboundEventStore(expectedId);
        var services = new ServiceCollection();
        services.AddSingleton<IInboundEventStore>(store);
        new LogicInstaller().Install(
            services,
            new ConfigurationBuilder().Build(),
            isProduction: false);

        await using var provider = services.BuildServiceProvider();
        var input = new InboundEventCapture("Web", "manual:test", "Prepare the proposal.");
        var result = await provider.GetRequiredService<ISender>()
            .Send(new CaptureInboundEventUsecase(input));

        Assert.Equal(expectedId, result);
        Assert.Equal(input, store.Captured);
    }

    private sealed class RecordingInboundEventStore(Guid result) : IInboundEventStore
    {
        public InboundEventCapture? Captured { get; private set; }

        public Task<Guid> CaptureAsync(
            InboundEventCapture input,
            CancellationToken cancellationToken = default)
        {
            Captured = input;
            return Task.FromResult(result);
        }

        public Task RetryAsync(Guid inboundEventId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
