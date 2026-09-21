using Clio10.Contracts;
using Microsoft.Extensions.DependencyInjection;
namespace Clio10.Tests;

internal static class WorkflowTestRegistration {
    internal static IServiceCollection AddWorkflow(this IServiceCollection services, IClioWorkflow workflow,
        OperationDescriptor descriptor, PrimitiveRequirement? requirement = null) {
        services.AddLogging();
        services.AddKeyedSingleton(descriptor.Name, workflow);
        services.AddSingleton(new WorkflowRegistration(descriptor, requirement ?? new(new(10, 0), new(11, 0))));
        return services;
    }
}
