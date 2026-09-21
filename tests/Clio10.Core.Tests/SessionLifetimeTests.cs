using Clio10.PrimitiveContracts;
using Clio10.Contracts;
using Clio10.Core;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;
namespace Clio10.Tests;

/// <summary>Checks cleanup cannot replace an execution result or release ownership prematurely.</summary>
public sealed class SessionLifetimeTests {
    [Test]
    [Description("An injected bundle's short version produces the canonical identity in the borrowed context.")]
    public async Task Context_reports_normalized_bundle_version() {
        // Arrange
        var bundle = Bundle();
        bundle.OpenSession(Arg.Any<PrimitiveSessionOptions>()).Returns(Substitute.For<IPrimitiveSession>());
        await using var provider = Build(bundle);
        // Act
        var version = await provider.GetRequiredService<IClioCore>().RunAsync("default", Requirement(),
            (context, _) => Task.FromResult(context.PrimitiveVersion), default);
        // Assert
        version.Should().Be(new Version(10, 0, 0, 0), "reporting and catalog identity must not depend on the bundle acquisition path");
    }

    [TestCase(false)]
    [TestCase(true)]
    [Description("Async cleanup holds the gate; a cleanup failure preserves the known callback result.")]
    public async Task Async_disposal_retains_gate_until_completed(bool fails) {
        // Arrange
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleaning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstSession = Substitute.For<IPrimitiveSession>();
        firstSession.DisposeAsync().Returns(_ => { cleaning.TrySetResult(); return new ValueTask(finish.Task); });
        var bundle = Bundle();
        bundle.OpenSession(Arg.Any<PrimitiveSessionOptions>()).Returns(firstSession, Substitute.For<IPrimitiveSession>());
        await using var provider = Build(bundle); var core = provider.GetRequiredService<IClioCore>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        // Act
        var first = core.RunAsync("default", Requirement(), (_, _) => Task.FromResult(new OperationResult(true, "completed")), timeout.Token);
        await cleaning.Task.WaitAsync(timeout.Token);
        var next = core.RunAsync("default", Requirement(), (_, _) => Task.FromResult(2), timeout.Token);
        bool waiting = !next.IsCompleted;
        if (fails) finish.SetException(new IOException("test cleanup failure")); else finish.SetResult();
        var result = await first; await next;
        // Assert
        waiting.Should().BeTrue("the next callback cannot run while resources are closing");
        result.Accepted.Should().BeTrue("cleanup cannot replace known execution acceptance");
        core.Diagnostics.Count.Should().Be(fails ? 1 : 0, "cleanup problems remain observable through safe diagnostics");
    }
    [Test]
    [Description("Callback exceptions remain primary even when cleanup also fails, and the next root can run.")]
    public async Task Callback_exception_wins_over_cleanup() {
        // Arrange
        var failed = Substitute.For<IPrimitiveSession>();
        failed.DisposeAsync().Returns(_ => ValueTask.FromException(new IOException("cleanup")));
        var bundle = Bundle(); bundle.OpenSession(Arg.Any<PrimitiveSessionOptions>()).Returns(failed, Substitute.For<IPrimitiveSession>());
        await using var provider = Build(bundle); var core = provider.GetRequiredService<IClioCore>();
        // Act
        Func<Task> first = () => core.RunAsync<int>("default", Requirement(), (_, _) => throw new InvalidOperationException("callback"), default);
        await first.Should().ThrowAsync<InvalidOperationException>("the execution failure must not be replaced by cleanup").WithMessage("callback", "the primary exception remains intact");
        var next = await core.RunAsync("default", Requirement(), (_, _) => Task.FromResult(2), default);
        // Assert
        next.Should().Be(2, "failed execution and cleanup must still release the gate");
    }
    [TestCase(false)]
    [TestCase(true)]
    [Description("Invalid capability initialization releases the partial session and retains the safe error despite cleanup failure.")]
    public async Task Invalid_capability_releases_failed_session(bool cleanupFails) {
        // Arrange
        var invalid = Substitute.For<IPrimitiveSession>(); invalid.GetCapability("http").Returns(new object());
        if (cleanupFails) invalid.DisposeAsync().Returns(_ => ValueTask.FromException(new IOException("cleanup")));
        var valid = Substitute.For<IPrimitiveSession>(); valid.GetCapability("http").Returns(Substitute.For<IClioPrimitive>());
        var bundle = Bundle(); bundle.Capabilities.Returns(new[] { "http" });
        bundle.OpenSession(Arg.Any<PrimitiveSessionOptions>()).Returns(invalid, valid);
        await using var provider = Build(bundle); var core = provider.GetRequiredService<IClioCore>();
        // Act
        Func<Task> first = () => core.RunAsync("default", Requirement(), (_, _) => Task.FromResult(0), default);
        var failure = await first.Should().ThrowAsync<CoreResolutionException>("an invalid provider must fail before callback execution");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var next = await core.RunAsync("default", Requirement(), (context, _) => Task.FromResult(context.Get<IClioPrimitive>()), timeout.Token);
        // Assert
        failure.Which.Code.Should().Be("invalid-capability-provider", "cleanup failure must not replace initialization failure");
        invalid.ReceivedCalls().Count(x => x.GetMethodInfo().Name == "DisposeAsync").Should().Be(1, "partial initialization owns cleanup");
        next.Should().NotBeNull("the next root must acquire released ownership");
    }
    private static PrimitiveRequirement Requirement() => new(new(10, 0), new(11, 0));
    private static IPrimitiveBundle Bundle() {
        var bundle = Substitute.For<IPrimitiveBundle>(); bundle.Version.Returns(new Version(10, 0));
        bundle.ContractVersion.Returns(2); bundle.Capabilities.Returns(Array.Empty<string>()); return bundle;
    }
    private static ServiceProvider Build(IPrimitiveBundle bundle) {
        var services = new ServiceCollection(); services.AddSingleton(bundle);
        services.AddClioCore(new(new Dictionary<string, ClioEnvironment> { ["default"] = new() }));
        return services.BuildServiceProvider();
    }
}
