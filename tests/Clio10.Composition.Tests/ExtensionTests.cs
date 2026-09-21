using Clio10.PrimitiveContracts;
using Clio10.Composition;
using Clio10.Contracts;
using Clio10.Core;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;
using Partner.Composition;

namespace Clio10.Tests;

/// <summary>Proves third-party orchestration, shared session lifetime and failure propagation.</summary>
public sealed class ExtensionTests {
    [TestCase(true, true, "true", true, 2)]
    [TestCase(true, false, "true", false, 2)]
    [TestCase(true, true, "false", true, 1)]
    [TestCase(true, true, "maybe", false, 0)]
    [TestCase(false, true, "true", false, 1)]
    [Description("A separately compiled partner workflow reuses one pinned primitive session and stops on failed steps.")]
    public async Task Partner_reuses_one_session(bool flushSucceeds, bool restartSucceeds, string restartAfterFlush, bool accepted, int operations) {
        // Arrange
        var primitive = Substitute.For<IClioPrimitive>();
        primitive.LoginAsync(Arg.Any<CancellationToken>()).Returns(new PrimitiveResponse(200, "{\"Code\":0}"));
        primitive.ExecuteAsync(Arg.Any<PrimitiveRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => new PrimitiveResponse((call.Arg<PrimitiveRequest>()!.RelativePath.EndsWith("RestartApp") ? restartSucceeds : flushSucceeds) ? 200 : 500, "{}"));
        var session = Substitute.For<IPrimitiveSession>();
        session.GetCapability("http").Returns(primitive);
        var bundle = Substitute.For<IPrimitiveBundle>();
        bundle.Version.Returns(new Version(10, 0, 0, 0));
        bundle.ContractVersion.Returns(2);
        bundle.Capabilities.Returns(new[] { "http" });
        bundle.OpenSession(Arg.Any<PrimitiveSessionOptions>()).Returns(session);
        var services = new ServiceCollection();
        services.AddSingleton(bundle);
        services.AddClioComposition(new(new Uri("http://localhost/"), "test", "test", IncludeDefaultPrimitives: false));
        services.AddPartnerWorkflows();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var composition = scope.ServiceProvider.GetRequiredService<IClioComposition>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        // Act
        var result = await composition.ExecuteAsync(new("partner.flush-then-restart", Arguments: new Dictionary<string, object?> { ["restart-after-flush"] = bool.TryParse(restartAfterFlush, out var restartFlag) ? restartFlag : restartAfterFlush }), cancellationToken: timeout.Token);
        // Assert
        result.Accepted.Should().Be(accepted, "a failed first step must stop the partner workflow");
        composition.Operations.Select(x => x.Name).Should().Contain("partner.flush-then-restart", "partners register without editing the vendor catalog");
        bundle.ReceivedCalls().Count(x => x.GetMethodInfo().Name == "OpenSession").Should().Be(operations == 0 ? 0 : 1, "nested workflows must not reacquire the same target gate");
        session.ReceivedCalls().Count(x => x.GetMethodInfo().Name == "DisposeAsync").Should().Be(operations == 0 ? 0 : 1, "only the root owns session disposal");
        primitive.ReceivedCalls().Where(x => x.GetMethodInfo().Name == "ExecuteAsync").Select(x => ((PrimitiveRequest)x.GetArguments()[0]!).RelativePath)
            .Should().Equal(new[] { "ServiceModel/AppInstallerService.svc/ClearRedisDb", "ServiceModel/AppInstallerService.svc/RestartApp" }.Take(operations), "steps must execute in order and respect the per-call option");
        if (flushSucceeds && operations > 0) result.AcceptedSteps.Should().Contain(Operation.FlushRedis, "partial acceptance must survive a later failed restart");
    }

    [Test]
    [Description("Unknown operations fail before environment resolution or primitive execution.")]
    public async Task Unknown_operation_fails_before_core() {
        // Arrange
        var core = Substitute.For<IClioCore>();
        var services = new ServiceCollection();
        services.AddSingleton(core);
        services.AddClioComposition(new(new Uri("http://localhost/")));
        using var provider = services.BuildServiceProvider();
        // Act
        var result = await provider.GetRequiredService<IClioComposition>().ExecuteAsync(new("not-installed"));
        // Assert
        result.Code.Should().Be("unsupported-operation", "unsupported operation names are explicit failures");
        core.ReceivedCalls().Should().BeEmpty("there is no workflow to execute");
    }
}
