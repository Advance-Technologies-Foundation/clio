using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// ENG-96840 Sandbox-tier regression for get-request-info version resolution. The NoEnvironment
/// fixture (<see cref="RequestInfoToolE2ETests"/>) runs offline and can only assert the degraded
/// <c>latest-fallback</c> tier, so a silent <c>environment → probe-error → latest-fallback</c> degrade
/// — the exact ENG-96840 bug, where the owned CreatioClient was disposed mid-probe by returning the
/// resolve Task unawaited from inside its <c>using</c> — has no guard there. This fixture calls the
/// real MCP tool against a reachable stand with an explicit <c>environment-name</c> and pins
/// <c>resolvedFrom == "environment"</c>, so a reintroduction against a real environment is caught
/// end-to-end.
/// </summary>
[TestFixture]
[Category("McpE2E.Sandbox")]
[AllureNUnit]
[AllureFeature(RequestInfoTool.ToolName)]
[NonParallelizable]
public sealed class RequestInfoToolVersionResolutionSandboxE2ETests {

	private const string ToolName = RequestInfoTool.ToolName;

	[Test]
	[Description("ENG-96840: get-request-info scoped to a reachable environment resolves resolvedFrom=environment (not latest-fallback/probe-error) end-to-end through the real MCP server.")]
	[AllureTag(ToolName)]
	[AllureName("get-request-info resolves resolvedFrom=environment against a reachable stand")]
	[AllureDescription("Starts the real clio MCP server, calls get-request-info with an explicit environment-name against the configured sandbox, and asserts the version resolver landed on the environment tier — pinning the ENG-96840 fix that a reachable stand no longer degrades to latest-fallback/probe-error because the owned client was disposed mid-probe.")]
	public async Task RequestInfoTool_Should_ResolveFromEnvironment_WhenScopedToReachableStand() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync();

		// Act
		RequestInfoResponse response = await CallRequestInfoAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> { ["environment-name"] = context.EnvironmentName });

		// Assert
		response.Success.Should().BeTrue(
			because: "a reachable environment must resolve its platform version and return the request catalog, not fail");
		response.ResolvedFrom.Should().Be("environment",
			because: "ENG-96840: a reachable stand must resolve resolvedFrom=environment — the pre-fix disposal race degraded it to latest-fallback with reason probe-error, and the offline NoEnvironment lane can only assert the degraded tier");
		response.ResolvedFromReason.Should().NotBe("probe-error",
			because: "probe-error is the exact ENG-96840 classification produced when the owned CreatioClient is disposed mid-probe; a reachable stand must not report it");
		response.ResolvedTargetVersion.Should().NotBeNullOrEmpty(
			because: "the environment tier must surface the platform version it actually resolved from the stand");
	}

	private static async Task<RequestInfoResponse> CallRequestInfoAsync(
		McpServerSession session,
		CancellationToken cancellationToken,
		IReadOnlyDictionary<string, object?> arguments) {
		// get-request-info binds a single `args` record (kebab-case fields), like every other clio MCP
		// tool — wrap the per-call fields so the real binding engages instead of dropping them as unknown
		// top-level keys.
		CallToolResult callResult = await session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> { ["args"] = new Dictionary<string, object?>(arguments) },
			cancellationToken);
		callResult.IsError.Should().NotBeTrue(
			because: "get-request-info should return structured responses instead of top-level MCP failures");
		return EntitySchemaStructuredResultParser.Extract<RequestInfoResponse>(callResult);
	}

	private static async Task<ArrangeContext> ArrangeAsync() {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string? environmentName = settings.Sandbox.EnvironmentName;
		if (string.IsNullOrWhiteSpace(environmentName)) {
			Assert.Ignore("Configure McpE2E:Sandbox:EnvironmentName (a reachable stand) to run the get-request-info version-resolution MCP E2E.");
		}
		if (!await ClioCliCommandRunner.IsEnvironmentReachableAsync(settings, environmentName!)) {
			Assert.Ignore($"get-request-info version-resolution MCP E2E requires a reachable configured sandbox environment. '{environmentName}' was not reachable.");
		}
		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new ArrangeContext(session, cancellationTokenSource, environmentName);
	}

	private sealed record ArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource,
		string? EnvironmentName) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
		}
	}
}
