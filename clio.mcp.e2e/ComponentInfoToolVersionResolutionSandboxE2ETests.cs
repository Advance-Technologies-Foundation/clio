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
/// ENG-96840 Sandbox-tier regression for get-component-info version resolution. The NoEnvironment
/// fixture (<see cref="ComponentInfoToolE2ETests"/>) can only tolerate <c>resolvedFrom ∈
/// {environment, latest-fallback}</c> because it has no stand, so a silent
/// <c>environment → probe-error → latest-fallback</c> degrade — the exact ENG-96840 bug, where the
/// owned CreatioClient was disposed mid-probe by returning the resolve Task unawaited from inside its
/// <c>using</c> — stays green there. This fixture calls the real MCP tool against a reachable stand
/// with an explicit <c>environment-name</c> and pins <c>resolvedFrom == "environment"</c> (not the
/// tolerance), so a reintroduction against a real environment is caught end-to-end.
/// </summary>
[TestFixture]
[Category("McpE2E.Sandbox")]
[AllureNUnit]
[AllureFeature(ComponentInfoTool.ToolName)]
[NonParallelizable]
public sealed class ComponentInfoToolVersionResolutionSandboxE2ETests : McpContractFixtureBase {

	private const string ToolName = ComponentInfoTool.ToolName;

	[Test]
	[Description("ENG-96840: get-component-info scoped to a reachable environment resolves resolvedFrom=environment (not latest-fallback/probe-error) end-to-end through the real MCP server.")]
	[AllureTag(ToolName)]
	[AllureName("get-component-info resolves resolvedFrom=environment against a reachable stand")]
	[AllureDescription("Starts the real clio MCP server, calls get-component-info with an explicit environment-name against the configured sandbox, and asserts the version resolver landed on the environment tier — pinning the ENG-96840 fix that a reachable stand no longer degrades to latest-fallback/probe-error because the owned client was disposed mid-probe.")]
	public async Task ComponentInfoTool_Should_ResolveFromEnvironment_WhenScopedToReachableStand() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync();

		// Act
		ComponentInfoResponse response = await CallComponentInfoAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> { ["environment-name"] = context.EnvironmentName });

		// Assert
		response.Success.Should().BeTrue(
			because: "a reachable environment must resolve its platform version and return the component catalog, not fail");
		response.ResolvedFrom.Should().BeOneOf(
			new[] {
				ComponentInfoResolution.ResolvedFromEnvironment,
				ComponentInfoResolution.ResolvedFromEnvironmentSuperset
			},
			because: "ENG-96840: a reachable stand must resolve from the environment tier (the version probe succeeded) — either 'environment' (exact per-version catalog on the CDN) or 'environment-superset' (version known from the stand, latest served as the closest catalog). The pre-fix disposal race degraded resolution to 'latest-fallback' with reason 'probe-error' (source != Environment); the NoEnvironment lane's environment-or-latest-fallback tolerance cannot catch that regression");
		response.ResolvedFrom.Should().NotBe(ComponentInfoResolution.ResolvedFromLatestFallback,
			because: "latest-fallback is the degraded tier the ENG-96840 disposal race produced on a stand that was in fact answerable");
		response.ResolvedFromReason.Should().NotBe("probe-error",
			because: "probe-error is the exact ENG-96840 classification produced when the owned CreatioClient is disposed mid-probe; a reachable stand must not report it");
		response.ResolvedTargetVersion.Should().NotBeNullOrEmpty(
			because: "the environment tier must surface the platform version it actually resolved from the stand");
	}

	private static async Task<ComponentInfoResponse> CallComponentInfoAsync(
		McpServerSession session,
		CancellationToken cancellationToken,
		IReadOnlyDictionary<string, object?> arguments) {
		// get-component-info binds a single `args` record (kebab-case fields), like every other clio MCP
		// tool — wrap the per-call fields so the real binding engages instead of dropping them as unknown
		// top-level keys.
		CallToolResult callResult = await session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> { ["args"] = new Dictionary<string, object?>(arguments) },
			cancellationToken);
		callResult.IsError.Should().NotBeTrue(
			because: "get-component-info should return structured responses instead of top-level MCP failures");
		return EntitySchemaStructuredResultParser.Extract<ComponentInfoResponse>(callResult);
	}

	private async Task<ArrangeContext> ArrangeAsync() {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string? environmentName = settings.Sandbox.EnvironmentName;
		if (string.IsNullOrWhiteSpace(environmentName)) {
			Assert.Ignore("Configure McpE2E:Sandbox:EnvironmentName (a reachable stand) to run the get-component-info version-resolution MCP E2E.");
		}
		if (!await ClioCliCommandRunner.IsEnvironmentReachableAsync(settings, environmentName!)) {
			Assert.Ignore($"get-component-info version-resolution MCP E2E requires a reachable configured sandbox environment. '{environmentName}' was not reachable.");
		}
		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		McpServerSession session = Session;
		return new ArrangeContext(session, cancellationTokenSource, environmentName);
	}

	private new sealed record ArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource,
		string? EnvironmentName) : IAsyncDisposable {
		public ValueTask DisposeAsync() {
			CancellationTokenSource.Dispose();
			return ValueTask.CompletedTask;
		}
	}
}
