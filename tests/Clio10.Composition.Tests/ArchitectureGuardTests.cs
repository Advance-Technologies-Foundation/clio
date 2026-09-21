using Clio10.PrimitiveContracts;
using Clio10.Contracts;
using Clio10.Core;
using Clio10.Composition;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;
namespace Clio10.Tests;

/// <summary>Adversarial checks for extension contracts and execution ownership.</summary>
public sealed class ArchitectureGuardTests {
    private static PrimitiveRequirement Requirement(params string[] capabilities) => new(new(10, 0), new(11, 0), Capabilities: capabilities);
    private static IClioWorkflow Workflow(Func<IWorkflowContext, CancellationToken, Task<OperationResult>> execute) {
        var workflow = Substitute.For<IClioWorkflow>();
        workflow.ExecuteAsync(Arg.Any<IWorkflowContext>(), Arg.Any<CancellationToken>()).Returns(call => execute(call.Arg<IWorkflowContext>()!, call.Arg<CancellationToken>()));
        return workflow;
    }
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static IServiceCollection Register(IServiceCollection services, string name, IClioWorkflow workflow, params string[] capabilities) =>
        services.AddWorkflow(workflow, new(name, name), Requirement(capabilities));

    [Test]
    [Description("Cancelling a running child drains the root and disposes its session once before later work starts.")]
    public async Task Running_child_cancellation_releases_root() {
        // Arrange
        var entered = Signal();
        var firstSession = Substitute.For<IPrimitiveSession>();
        var bundle = Substitute.For<IPrimitiveBundle>(); bundle.Version.Returns(new Version(10, 0)); bundle.ContractVersion.Returns(2);
        bundle.Capabilities.Returns(Array.Empty<string>());
        bundle.OpenSession(Arg.Any<PrimitiveSessionOptions>()).Returns(firstSession, Substitute.For<IPrimitiveSession>());
        var services = new ServiceCollection(); services.AddSingleton(bundle);
        services.AddClioComposition(new CompositionOptions(IncludeDefaultPrimitives: false));
        Register(services, "child", Workflow(async (_, token) => {
            entered.SetResult(); await Task.Delay(Timeout.Infinite, token); return new(true, "completed");
        }));
        Register(services, "parent", Workflow((context, token) => context.InvokeAsync("child", token)));
        Register(services, "next", Workflow((_, _) => Task.FromResult(new OperationResult(true, "completed"))));
        await using var provider = services.BuildServiceProvider(); var composition = provider.GetRequiredService<IClioComposition>();
        using var cancel = new CancellationTokenSource(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        // Act
        var root = composition.ExecuteAsync(new("parent"), cancellationToken: cancel.Token);
        await entered.Task.WaitAsync(timeout.Token); cancel.Cancel();
        Func<Task> cancelled = () => root;
        await cancelled.Should().ThrowAsync<OperationCanceledException>("caller cancellation must survive the child completion bridge");
        var next = await composition.ExecuteAsync(new("next"), cancellationToken: timeout.Token);
        // Assert
        firstSession.ReceivedCalls().Count(x => x.GetMethodInfo().Name == "DisposeAsync").Should().Be(1, "the root alone owns primitive session cleanup");
        next.Accepted.Should().BeTrue("cancelled execution cannot strand the target gate");
    }

    [Test]
    [Description("A default bundle's extra capabilities cannot hide undeclared workflow access.")]
    public async Task Undeclared_capability_is_rejected() {
        // Arrange
        var services = new ServiceCollection(); services.AddClioComposition(new CompositionOptions());
        Register(services, "bad", Workflow((context, _) => { context.Get<IFileSystemPrimitive>(); return Task.FromResult(new OperationResult(true, "completed")); }), "http");
        await using var provider = services.BuildServiceProvider();
        // Act
        var result = await provider.GetRequiredService<IClioComposition>().ExecuteAsync(new("bad"));
        // Assert
        result.Code.Should().Be("capability-undeclared", "declared requirements must match actual capability use even with the complete default bundle");
    }
    [Test]
    [Description("Child capability requirements must fit the root declaration, not merely the installed bundle.")]
    public async Task Child_cannot_expand_root_capabilities() {
        // Arrange
        bool invoked = false;
        var services = new ServiceCollection(); services.AddClioComposition(new CompositionOptions());
        Register(services, "root", Workflow((context, token) => context.InvokeAsync("child", token)), "http");
        Register(services, "child", Workflow((_, _) => { invoked = true; return Task.FromResult(new OperationResult(true, "completed")); }), "filesystem");
        await using var provider = services.BuildServiceProvider();
        // Act
        var result = await provider.GetRequiredService<IClioComposition>().ExecuteAsync(new("root"));
        // Assert
        result.Code.Should().Be("incompatible-nested-workflow", "a root declares the union of child capability needs");
        invoked.Should().BeFalse("a mismatched child must fail before its implementation executes");
    }
    [Test]
    [Description("Duplicate metadata fails discovery and execution consistently before Core starts.")]
    public async Task Duplicate_registration_is_visible() {
        // Arrange
        var services = new ServiceCollection(); var core = Substitute.For<IClioCore>(); services.AddSingleton(core);
        services.AddClioComposition(new CompositionOptions());
        Register(services, "duplicate", Workflow((_, _) => Task.FromResult(new OperationResult(true, "completed"))));
        Register(services, "duplicate", Workflow((_, _) => Task.FromResult(new OperationResult(true, "completed"))));
        await using var provider = services.BuildServiceProvider(); var composition = provider.GetRequiredService<IClioComposition>();
        // Act
        Action discover = () => _ = composition.Operations;
        var result = await composition.ExecuteAsync(new("restart"));
        // Assert
        discover.Should().Throw<WorkflowContractException>("discovery must not advertise a healthy catalog when identities collide");
        result.Code.Should().Be("duplicate-operation", "all adapters receive an explicit registration failure");
        core.ReceivedCalls().Should().BeEmpty("invalid metadata cannot start execution");
    }
    [TestCase("default")]
    [TestCase("other")]
    [Description("Calling composition as another root from inside a workflow returns a diagnostic rather than deadlocking.")]
    public async Task Nested_composition_root_is_rejected(string target) {
        // Arrange
        IClioComposition? composition = null;
        var services = new ServiceCollection();
        services.AddClioComposition(new CoreOptions(new Dictionary<string, ClioEnvironment> { ["default"] = new(), ["other"] = new() }));
        Register(services, "root", Workflow((_, token) => composition!.ExecuteAsync(new("child", target), cancellationToken: token)));
        Register(services, "child", Workflow((_, _) => Task.FromResult(new OperationResult(true, "completed"))));
        await using var provider = services.BuildServiceProvider(); composition = provider.GetRequiredService<IClioComposition>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        // Act
        var result = await composition.ExecuteAsync(new("root"), cancellationToken: timeout.Token);
        var next = await composition.ExecuteAsync(new("child"), cancellationToken: timeout.Token);
        // Assert
        result.Code.Should().Be("nested-root-not-supported", "root calls must not wait on gates from inside another root");
        next.Accepted.Should().BeTrue("the rejected nested root must release all ownership");
    }
    [Test]
    [Description("An early-returning parent with overlapping children cannot dispose their session before the first child drains.")]
    public async Task Unawaited_child_drains_before_session_release() {
        // Arrange
        var finishChild = Signal(); var parentReturning = Signal(); bool childDone = false; bool disposedEarly = false;
        string? rejectedCode = null;
        var session = Substitute.For<IPrimitiveSession>();
        session.DisposeAsync().Returns(_ => { disposedEarly = !childDone; return ValueTask.CompletedTask; });
        var bundle = Substitute.For<IPrimitiveBundle>(); bundle.Version.Returns(new Version(10, 0)); bundle.ContractVersion.Returns(2);
        bundle.Capabilities.Returns(Array.Empty<string>());
        bundle.OpenSession(Arg.Any<PrimitiveSessionOptions>()).Returns(session, Substitute.For<IPrimitiveSession>());
        var services = new ServiceCollection(); services.AddSingleton(bundle);
        services.AddClioComposition(new CompositionOptions(IncludeDefaultPrimitives: false));
        Register(services, "child", Workflow(async (_, _) => { await finishChild.Task; childDone = true; return new(true, "completed"); }));
        Register(services, "parent", Workflow(async (context, token) => {
            _ = context.InvokeAsync("child", token);
            var rejected = await context.InvokeAsync("child", token);
            rejectedCode = rejected.Code; parentReturning.SetResult();
            return rejected;
        }));
        Register(services, "next", Workflow((_, _) => Task.FromResult(new OperationResult(true, "completed"))));
        await using var provider = services.BuildServiceProvider(); var composition = provider.GetRequiredService<IClioComposition>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        // Act
        var parent = composition.ExecuteAsync(new("parent"), cancellationToken: timeout.Token);
        await parentReturning.Task.WaitAsync(timeout.Token);
        var next = composition.ExecuteAsync(new("next"), cancellationToken: timeout.Token);
        bool held = !parent.IsCompleted && !next.IsCompleted;
        finishChild.SetResult(); var result = await parent; await next;
        // Assert
        rejectedCode.Should().Be("concurrent-child-not-supported", "overlapping children cannot share non-thread-safe session state");
        held.Should().BeTrue("Core ownership must remain while the first child is still executing");
        disposedEarly.Should().BeFalse("the child must finish before its session is disposed");
        result.Code.Should().Be("unawaited-child-outcome-unknown", "early return is an explicit workflow contract failure");
    }
    [Test]
    [Description("Throwing workflow execution releases ownership for a later root and preserves a safe failure category.")]
    public async Task Throwing_workflow_does_not_strand_Core() {
        // Arrange
        var services = new ServiceCollection(); services.AddClioComposition(new CompositionOptions());
        Register(services, "bad", Workflow((_, _) => throw new InvalidOperationException("secret")));
        Register(services, "good", Workflow((_, _) => Task.FromResult(new OperationResult(true, "completed"))));
        await using var provider = services.BuildServiceProvider(); var composition = provider.GetRequiredService<IClioComposition>();
        // Act
        var first = await composition.ExecuteAsync(new("bad")); var next = await composition.ExecuteAsync(new("good"));
        // Assert
        first.Code.Should().Be("unexpected-failure", "execution exceptions must not leak implementation details");
        next.Accepted.Should().BeTrue("the failed root must release its session and DI scope");
    }
}
