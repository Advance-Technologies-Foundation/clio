using Clio10.Composition;
using Clio10.Contracts;
using Clio10.Core;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;
namespace Clio10.Tests;

/// <summary>Checks portable inputs at the execution boundary.</summary>
public sealed class ArgumentTests {
    [Test]
    [Description("Snapshotting copies typed numeric arrays and nested dictionaries before caller mutation.")]
    public void Snapshot_copies_nested_values() {
        // Arrange
        var numbers = new[] { 1, 2 };
        var nested = new Dictionary<string, object?> { ["numbers"] = numbers };
        // Act
        var copy = ArgumentValues.Snapshot(new Dictionary<string, object?> { ["nested"] = nested });
        numbers[0] = 99;
        nested.Clear();
        // Assert
        var copied = (IReadOnlyDictionary<string, object?>)copy["nested"]!;
        ((IReadOnlyList<object?>)copied["numbers"]!).Should().Equal(new object?[] { 1, 2 }, "the caller must not mutate a workflow's inputs");
    }

    [TestCase("missing")]
    [TestCase("unknown")]
    [TestCase("wrong-type")]
    [TestCase("unsupported")]
    [TestCase("deep")]
    [TestCase("infinite")]
    [Description("Invalid nested or nonportable inputs fail before Core opens a primitive session.")]
    public async Task Rejects_invalid_input_before_Core(string kind) {
        // Arrange
        object? value = new Dictionary<string, object?> { ["name"] = "valid" };
        if (kind == "missing") value = new Dictionary<string, object?>();
        if (kind == "unknown") value = new Dictionary<string, object?> { ["name"] = "valid", ["extra"] = true };
        if (kind == "wrong-type") value = new Dictionary<string, object?> { ["name"] = 42 };
        if (kind == "unsupported") value = new object();
        if (kind == "infinite") value = double.NaN;
        if (kind == "deep") for (int i = 0; i < 34; i++) value = new Dictionary<string, object?> { ["next"] = value };
        var workflow = Substitute.For<IClioWorkflow>();
        var workflowDescriptor = new OperationDescriptor("test", "test", Arguments: [new("input", ArgumentKind.Object, true, Fields: [new("name", ArgumentKind.String, true)])]);
        var core = Substitute.For<ICompositionHost>();
        var services = new ServiceCollection();
        services.AddSingleton(core).AddWorkflow(workflow, workflowDescriptor).AddScoped<IClioComposition, CreatioComposition>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        // Act
        var result = await scope.ServiceProvider.GetRequiredService<IClioComposition>().ExecuteAsync(new("test", Arguments: new Dictionary<string, object?> { ["input"] = value }));
        // Assert
        result.Code.Should().Be("invalid-arguments", "the operation's input contract is checked before execution");
        core.ReceivedCalls().Should().BeEmpty("invalid input must not create sessions or acquire target ownership");
    }

    [Test]
    [Description("A pending workflow owns a snapshot and omitted child arguments never inherit parent options.")]
    public async Task Running_input_and_child_input_are_isolated() {
        // Arrange
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var core = Substitute.For<ICompositionHost>();
        var context = Substitute.For<IOperationContext>();
        context.PrimitiveVersion.Returns(new Version(10, 0));
        context.Capabilities.Returns(Array.Empty<string>());
        core.RunAsync(Arg.Any<string>(), Arg.Any<PrimitiveRequirement>(), Arg.Any<Func<IOperationContext, CancellationToken, Task<OperationResult>>>(), Arg.Any<CancellationToken>()).Returns(call => call.Arg<Func<IOperationContext, CancellationToken, Task<OperationResult>>>()!(context, call.Arg<CancellationToken>()));
        var root = Substitute.For<IClioWorkflow>();
        var rootDescriptor = new OperationDescriptor("root", "root", Arguments: [new("name", ArgumentKind.String)]);
        var rootRequirement = new PrimitiveRequirement(new(10, 0), new(11, 0));
        root.ExecuteAsync(Arg.Any<IWorkflowContext>(), Arg.Any<CancellationToken>()).Returns(async call => {
            var run = call.Arg<IWorkflowContext>()!;
            entered.SetResult();
            await resume.Task;
            var result = await run.InvokeAsync("child", call.Arg<CancellationToken>());
            return result with { Payload = run.Arguments["name"] };
        });
        var child = Substitute.For<IClioWorkflow>();
        var childDescriptor = new OperationDescriptor("child", "child");
        var childRequirement = new PrimitiveRequirement(new(10, 0), new(11, 0));
        child.ExecuteAsync(Arg.Any<IWorkflowContext>(), Arg.Any<CancellationToken>()).Returns(new OperationResult(true, "completed"));
        var services = new ServiceCollection();
        services.AddSingleton(core).AddWorkflow(root, rootDescriptor, rootRequirement).AddWorkflow(child, childDescriptor, childRequirement).AddScoped<IClioComposition, CreatioComposition>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var input = new Dictionary<string, object?> { ["name"] = "original" };
        // Act
        var pending = scope.ServiceProvider.GetRequiredService<IClioComposition>().ExecuteAsync(new("root", Arguments: input));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        input["name"] = "changed";
        resume.SetResult();
        var result = await pending;
        // Assert
        result.Accepted.Should().BeTrue("the child accepts no arguments, so parent input must not leak into it");
        result.Payload.Should().Be("original", "caller mutation after startup must not alter the running workflow");
    }
}
