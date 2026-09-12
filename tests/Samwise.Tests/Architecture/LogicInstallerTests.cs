using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Samwise.Logic.Agents.Services;
using Samwise.Logic.Inbox.Services;
using Samwise.Logic.Inputs.Email.Services;
using Samwise.Logic.Installers;
using Samwise.Logic.Prompts;
using Samwise.Logic.WorkItems.Services;

namespace Samwise.Tests.Architecture;

public sealed class LogicInstallerTests
{
    [Fact]
    public void RegistersMediatRPromptRenderingAndWorkflowServices()
    {
        var services = new ServiceCollection();
        new LogicInstaller().Install(
            services,
            new ConfigurationBuilder().Build(),
            isProduction: false);

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<MediatR.ISender>());
        Assert.IsType<PromptRenderer>(provider.GetRequiredService<IPromptRenderer>());
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IAgentInvocationService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IInboundEventClassificationService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IInboundEventProcessingService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IInboundEventCaptureService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IEmailAccountProcessingLock));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWorkItemPlanningService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWorkItemExecutionService));
    }

    [Fact]
    public void UsecaseHandlersDoNotSendNestedUsecases()
    {
        var nestedSenders = typeof(LogicInstaller).Assembly
            .GetTypes()
            .Where(type => type.Namespace?.EndsWith(".Usecases", StringComparison.Ordinal) == true)
            .SelectMany(type => type.GetConstructors())
            .Where(constructor => constructor.GetParameters().Any(
                parameter => typeof(MediatR.ISender).IsAssignableFrom(parameter.ParameterType)))
            .Select(constructor => constructor.DeclaringType?.FullName)
            .ToList();

        Assert.Empty(nestedSenders);
    }

    [Fact]
    public void UsecaseNamespaceContainsNoReadWrappers()
    {
        var readUsecases = typeof(LogicInstaller).Assembly
            .GetTypes()
            .Where(type => type.Namespace?.EndsWith(".Usecases", StringComparison.Ordinal) == true)
            .Where(type => type.Name.StartsWith("Get", StringComparison.Ordinal))
            .Select(type => type.FullName)
            .ToList();

        Assert.Empty(readUsecases);
    }
}
