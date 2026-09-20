using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Samwise.Logic.Agents.Services;
using Samwise.Logic.Inbox.Services;
using Samwise.Inputs.Email.Services;
using Samwise.Inputs.Email.Installers;
using Samwise.Inputs.Manual.Installers;
using Samwise.Logic.Installers;
using Samwise.Logic.Prompts;
using Samwise.Logic.Prompts.Services;
using Samwise.Logic.WorkItems.Services;

namespace Samwise.Tests.Architecture;

public sealed class LogicInstallerTests
{
    [Fact]
    public void GenericCoreDoesNotReferenceConcreteInputModules()
    {
        var forbiddenReferences = new[]
        {
            typeof(Samwise.Domain.Inbox.InboundEvent).Assembly,
            typeof(LogicInstaller).Assembly
        }
            .SelectMany(assembly => assembly.GetReferencedAssemblies())
            .Where(reference => reference.Name?.StartsWith("Samwise.Inputs.", StringComparison.Ordinal) == true)
            .Select(reference => reference.Name)
            .ToList();

        Assert.Empty(forbiddenReferences);
    }

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
        Assert.IsType<AgentPromptPlaceholderCatalog>(
            provider.GetRequiredService<IAgentPromptPlaceholderCatalog>());
        Assert.IsType<PromptRenderer>(provider.GetRequiredService<IPromptRenderer>());
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IAgentInvocationService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IInboundEventClassificationService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IInboundEventProcessingService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IInboundEventCaptureService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWorkItemPlanningService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWorkItemExecutionService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IAgentPromptBuilder));
    }

    [Fact]
    public void InputModulesRegisterTheirOwnHandlersAndServices()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        new ManualInputInstaller().Install(services, configuration, isProduction: false);
        new EmailInputInstaller().Install(services, configuration, isProduction: false);

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IEmailAccountProcessingLock));
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(MediatR.IRequestHandler<
                Samwise.Inputs.Manual.Usecases.SubmitManualInputUsecase,
                string>));
    }

    [Fact]
    public void UsecaseHandlersDoNotSendNestedUsecases()
    {
        var nestedSenders = ApplicationAssemblies()
            .SelectMany(assembly => assembly
            .GetTypes()
            .Where(type => type.Namespace?.EndsWith(".Usecases", StringComparison.Ordinal) == true)
            .SelectMany(type => type.GetConstructors())
            .Where(constructor => constructor.GetParameters().Any(
                parameter => typeof(MediatR.ISender).IsAssignableFrom(parameter.ParameterType)))
            .Select(constructor => constructor.DeclaringType?.FullName))
            .ToList();

        Assert.Empty(nestedSenders);
    }

    [Fact]
    public void UsecaseNamespaceContainsNoReadWrappers()
    {
        var readUsecases = ApplicationAssemblies()
            .SelectMany(assembly => assembly
            .GetTypes()
            .Where(type => type.Namespace?.EndsWith(".Usecases", StringComparison.Ordinal) == true)
            .Where(type => type.Name.StartsWith("Get", StringComparison.Ordinal))
            .Select(type => type.FullName))
            .ToList();

        Assert.Empty(readUsecases);
    }

    private static IEnumerable<System.Reflection.Assembly> ApplicationAssemblies() =>
    [
        typeof(LogicInstaller).Assembly,
        typeof(ManualInputInstaller).Assembly,
        typeof(EmailInputInstaller).Assembly
    ];
}
