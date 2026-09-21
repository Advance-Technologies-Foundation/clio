using Clio10.PrimitiveContracts;
using Clio10.Contracts;
using Clio10.Composition;
using Clio10.Core;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio10.Tests;

/// <summary>Controlled primitive outcomes exercise workflows through real Core session ownership.</summary>
public sealed class CompositionTests {
    [TestCase(Operation.Restart, true, "RestartApp")]
    [TestCase(Operation.Restart, false, "UnloadAppDomain")]
    [TestCase(Operation.FlushRedis, true, "ClearRedisDb")]
    [TestCase(Operation.FlushRedis, false, "ClearRedisDb")]
    [Description("Composition authenticates before selecting the platform-specific maintenance endpoint.")]
    public async Task Routes_after_login(string operation, bool netCore, string endpoint) {
        // Arrange
        var primitive = Primitive();
        using var provider = Build(primitive, netCore);
        using var scope = provider.CreateScope();
        // Act
        var result = await scope.ServiceProvider.GetRequiredService<IClioComposition>().ExecuteAsync(new(operation));
        // Assert
        result.Accepted.Should().BeTrue("authentication and the requested HTTP operation succeeded");
        var calls = primitive.ReceivedCalls().Select(x => x.GetMethodInfo().Name).ToArray();
        calls.Should().Equal(new[] { "LoginAsync", "ExecuteAsync" }, "authentication must precede the operation");
        primitive.ReceivedCalls().Last().GetArguments()[0].Should().Be(
            new PrimitiveRequest("POST", (netCore ? "" : "0/") + "ServiceModel/AppInstallerService.svc/" + endpoint, "{}"),
            "workflow policy owns platform routing");
    }

    [TestCase("{\"Code\":1}")]
    [TestCase("not-json")]
    [TestCase("{}")]
    [TestCase("[]")]
    [Description("Malformed or rejected login stops the workflow before external side effects.")]
    public async Task Failed_login_stops_workflow(string body) {
        // Arrange
        var primitive = Primitive();
        primitive.LoginAsync(Arg.Any<CancellationToken>()).Returns(new PrimitiveResponse(200, body));
        using var provider = Build(primitive);
        using var scope = provider.CreateScope();
        // Act
        var result = await scope.ServiceProvider.GetRequiredService<IClioComposition>().ExecuteAsync(new(Operation.Restart));
        // Assert
        result.Accepted.Should().BeFalse("the response did not establish authentication");
        primitive.ReceivedCalls().Count(x => x.GetMethodInfo().Name == "ExecuteAsync").Should().Be(0,
            "no destructive operation may follow rejected authentication");
    }

    [Test]
    [Description("An interrupted restart is an unknown outcome without an automatic retry.")]
    public async Task Interrupted_restart_is_not_success() {
        // Arrange
        var primitive = Primitive();
        primitive.ExecuteAsync(Arg.Any<PrimitiveRequest>(), Arg.Any<CancellationToken>()).Returns(new PrimitiveResponse(null, "", "transport-failure"));
        using var provider = Build(primitive);
        using var scope = provider.CreateScope();
        // Act
        var result = await scope.ServiceProvider.GetRequiredService<IClioComposition>().ExecuteAsync(new(Operation.Restart));
        // Assert
        result.Code.Should().Be("outcome-unknown", "disconnect is not evidence of completion");
        primitive.ReceivedCalls().Count(x => x.GetMethodInfo().Name == "ExecuteAsync").Should().Be(1, "side effects must not be blindly retried");
    }

    [Test]
    [Description("Caller cancellation propagates before external execution.")]
    public async Task Cancellation_propagates() {
        // Arrange
        using var provider = Build(Primitive());
        using var scope = provider.CreateScope();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        // Act
        Func<Task> act = () => scope.ServiceProvider.GetRequiredService<IClioComposition>().ExecuteAsync(new(Operation.Restart), cancellationToken: cancellation.Token);
        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>("the caller cancelled the operation");
    }

    [Test]
    [Description("Workflow code has no HTTP or presentation SDK dependency.")]
    public void Dependency_boundary_is_clean() {
        // Arrange
        var references = typeof(CreatioComposition).Assembly.GetReferencedAssemblies().Select(x => x.Name);
        // Act
        var forbidden = references.Where(x => x is "System.Net.Http" || x!.Contains("Spectre") || x.Contains("ModelContextProtocol") || x.Contains("Creatio.Client"));
        // Assert
        forbidden.Should().BeEmpty("external SDK calls belong in primitives and presentation belongs in adapters");
    }

    private static IClioPrimitive Primitive() {
        var primitive = Substitute.For<IClioPrimitive>();
        primitive.LoginAsync(Arg.Any<CancellationToken>()).Returns(new PrimitiveResponse(200, "{\"Code\":0}"));
        primitive.ExecuteAsync(Arg.Any<PrimitiveRequest>(), Arg.Any<CancellationToken>()).Returns(new PrimitiveResponse(200, "{}"));
        primitive.ClearReceivedCalls();
        return primitive;
    }
    private static ServiceProvider Build(IClioPrimitive primitive, bool netCore = true) {
        var services = new ServiceCollection();
        var session = Substitute.For<IPrimitiveSession>();
        session.GetCapability("http").Returns(primitive);
        var bundle = Substitute.For<IPrimitiveBundle>();
        bundle.Version.Returns(new Version(10, 0, 0, 0));
        bundle.ContractVersion.Returns(2);
        bundle.Capabilities.Returns(new[] { "http" });
        bundle.OpenSession(Arg.Any<PrimitiveSessionOptions>()).Returns(session);
        services.AddSingleton(bundle);
        services.AddClioComposition(new(new Uri("http://127.0.0.1:1/"), "test-user", "test-password", netCore, IncludeDefaultPrimitives: false));
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}
