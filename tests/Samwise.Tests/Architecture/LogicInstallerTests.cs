using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Samwise.Logic.Installers;
using Samwise.Logic.Prompts;

namespace Samwise.Tests.Architecture;

public sealed class LogicInstallerTests
{
    [Fact]
    public void RegistersMediatRAndPromptRendering()
    {
        var services = new ServiceCollection();
        new LogicInstaller().Install(
            services,
            new ConfigurationBuilder().Build(),
            isProduction: false);

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<MediatR.ISender>());
        Assert.IsType<PromptRenderer>(provider.GetRequiredService<IPromptRenderer>());
    }
}
