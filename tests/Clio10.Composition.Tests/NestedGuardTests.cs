using Clio10.PrimitiveContracts;
using Clio10.Composition;
using Clio10.Contracts;
using Clio10.Core;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio10.Tests;

/// <summary>Exercises rejected nested routes before any child primitive work.</summary>
public sealed class NestedGuardTests {
    [TestCase("root", "recursive-workflow")]
    [TestCase("missing", "unsupported-operation")]
    [TestCase("child", "incompatible-nested-workflow")]
    [TestCase("child", "invalid-arguments")]
    [Description("Nested calls reject recursion, missing names and incompatible capabilities without reopening Core.")]
    public async Task Rejects_invalid_nested_route(string target, string expected) {
        // Arrange
        var primitive = Substitute.For<IClioPrimitive>();
        var context = Substitute.For<IOperationContext>();
        context.GetCapability("http").Returns(primitive);
        context.PrimitiveVersion.Returns(new Version(10, 0, 0, 0));
        context.Capabilities.Returns(new[] { "http" });
        var core = Substitute.For<IClioCore>();
        core.RunAsync(Arg.Any<string>(), Arg.Any<PrimitiveRequirement>(), Arg.Any<Func<IOperationContext, CancellationToken, Task<OperationResult>>>(), Arg.Any<CancellationToken>()).Returns(call => call.Arg<Func<IOperationContext, CancellationToken, Task<OperationResult>>>()!(context, call.Arg<CancellationToken>()));
        var root = Substitute.For<IClioWorkflow>();
        var rootDescriptor = new OperationDescriptor("root", "root");
        var rootRequirement = new PrimitiveRequirement(new(10, 0, 0, 0), new(11, 0, 0, 0), Capabilities: ["http"]);
        root.ExecuteAsync(Arg.Any<IWorkflowContext>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IWorkflowContext>()!.InvokeAsync(target, call.Arg<CancellationToken>()));
        var child = Substitute.For<IClioWorkflow>();
        var childDescriptor = new OperationDescriptor("child", "child", Arguments:
            expected == "invalid-arguments" ? [new("required", ArgumentKind.String, true)] : null);
        var childRequirement = new PrimitiveRequirement(new(10, 0, 0, 0), new(11, 0, 0, 0),
            Capabilities: expected == "invalid-arguments" ? ["http"] : ["other"]);
        var services = new ServiceCollection();
        services.AddSingleton(core);
        services.AddWorkflow(root, rootDescriptor, rootRequirement);
        services.AddWorkflow(child, childDescriptor, childRequirement);
        services.AddClioComposition(new(new Uri("http://localhost/")));
        using var provider = services.BuildServiceProvider();
        // Act
        var result = await provider.GetRequiredService<IClioComposition>().ExecuteAsync(new("root"));
        // Assert
        result.Code.Should().Be(expected, "invalid child routes must fail before execution");
        primitive.ReceivedCalls().Should().BeEmpty("no child primitive work is allowed");
        core.ReceivedCalls().Count(x => x.GetMethodInfo().Name == "RunAsync").Should().Be(1, "nested calls must not acquire another root context");
    }
}
