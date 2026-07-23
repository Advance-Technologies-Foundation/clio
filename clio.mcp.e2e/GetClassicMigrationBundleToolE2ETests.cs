using System.Text.Json;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the get-classic-migration-bundle MCP tool. The tool is long-tail, so it is invoked
/// through clio-run (it is intentionally not advertised in tools/list). These tests validate the CLIO side on
/// a real stand: the tool assembles the layer chain and writes an engine-consumable manifest to disk.
///
/// FR-10 (the manifest folds cleanly through the Node engine) is a CROSS-REPO manual step — clio.mcp.e2e must
/// not depend on the toolkit engine path. After this test writes the manifest, run the fold manually and
/// confirm empty diagnostics:
///   node &lt;toolkit&gt;/skills/classic-to-freedom-migration/engine/migrate.mjs &lt;manifestPath&gt;
///   =&gt; parseErrors: [], warnings: [], unresolvedParents: []
/// Record that result in the PR. clio.mcp.e2e is NOT in CI — run manually on ts1-core-dev04-15735109.
/// </summary>
[TestFixture]
[Category("McpE2E.Sandbox")]
[AllureNUnit]
[AllureFeature("get-classic-migration-bundle")]
[NonParallelizable]
public sealed class GetClassicMigrationBundleToolE2ETests : McpContractFixtureBase {
	private const string ToolName = GetClassicMigrationBundleTool.ToolName;

	// A base-product classic page present on every stand and replaced across many packages (multi-layer).
	private const string MultiLayerPage = "ContactPageV2";

	[Test]
	[Description("Assembles a real multi-layer classic page's bundle via clio-run and writes an engine-consumable manifest to disk.")]
	[AllureTag(ToolName)]
	[AllureName("get-classic-migration-bundle assembles and writes an engine-consumable manifest")]
	[AllureDescription("Uses the real clio MCP server to invoke the long-tail get-classic-migration-bundle tool through clio-run for ContactPageV2, then verifies the manifest was written to disk with a base->top schemas chain in the shape the migration engine folds.")]
	public async Task GetBundle_Should_Assemble_And_Write_EngineConsumable_Manifest() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));
		string outputDirectory = CreateFixtureDirectory("classic-migration-bundle");
		string outputFile = Path.Combine(outputDirectory, "manifest.json");

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = MultiLayerPage,
					["environment-name"] = arrangeContext.EnvironmentName,
					["output-file"] = outputFile
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		GetClassicMigrationBundleResponse response =
			EntitySchemaStructuredResultParser.Extract<GetClassicMigrationBundleResponse>(callResult);

		// Assert — the tool reported success and a manifest path
		callResult.IsError.Should().NotBeTrue(
			because: "the routed MCP call must return a structured payload");
		response.Success.Should().BeTrue(
			because: $"the bundle must assemble for the multi-layer page '{MultiLayerPage}'. Error: {response.Error}");
		response.ManifestPath.Should().Be(outputFile,
			because: "an explicit absolute output-file must be echoed as the manifest location");

		// Assert — the manifest on disk is in the engine's contract shape (bodies live here, not in the response)
		File.Exists(response.ManifestPath).Should().BeTrue(because: "the manifest must be written to disk");
		using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllTextAsync(response.ManifestPath));
		manifest.RootElement.TryGetProperty("schemas", out JsonElement schemas).Should().BeTrue(
			because: "the manifest must carry the replacing-schema layer chain");
		schemas.ValueKind.Should().Be(JsonValueKind.Array);
		schemas.GetArrayLength().Should().BeGreaterThan(0,
			because: "at least one classic layer must be assembled for a real page");
		foreach (JsonElement layer in schemas.EnumerateArray()) {
			layer.TryGetProperty("pkg", out _).Should().BeTrue(because: "each layer must carry its owning package");
			layer.TryGetProperty("body", out _).Should().BeTrue(because: "each layer must carry its raw body for the engine to fold");
		}
	}

	[Test]
	[Description("Reports a readable failure when get-classic-migration-bundle is asked for a schema that does not exist.")]
	[AllureTag(ToolName)]
	[AllureName("get-classic-migration-bundle reports a missing-schema failure")]
	[AllureDescription("Invokes the long-tail tool through clio-run for a schema name that does not exist and verifies the failure stays human-readable (not a transport crash).")]
	public async Task GetBundle_Should_Report_Failure_For_Missing_Schema() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));
		string missingSchema = $"UsrMissingClassicPage{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = missingSchema,
					["environment-name"] = arrangeContext.EnvironmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		GetClassicMigrationBundleResponse response =
			EntitySchemaStructuredResultParser.Extract<GetClassicMigrationBundleResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a missing schema is a command-level failure, not an MCP transport failure");
		response.Success.Should().BeFalse(because: "the requested schema does not exist");
		response.Error.Should().Contain(missingSchema,
			because: "the failure should identify the schema the caller requested");
		response.Error.Should().Contain("not found",
			because: "the failure should explain that no schema layer resolved");
	}

	private async Task<ArrangeContext> ArrangeAsync(McpE2ESettings settings, TimeSpan timeout) {
		CancellationTokenSource cancellationTokenSource = new(timeout);
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		return new ArrangeContext(Session, cancellationTokenSource, environmentName);
	}

	private static async Task<string> ResolveReachableEnvironmentAsync(McpE2ESettings settings) {
		string? configuredEnvironmentName = settings.Sandbox.EnvironmentName;
		if (!string.IsNullOrWhiteSpace(configuredEnvironmentName) &&
			await CanReachEnvironmentAsync(settings, configuredEnvironmentName)) {
			return configuredEnvironmentName;
		}

		const string fallbackEnvironmentName = "d2";
		if (await CanReachEnvironmentAsync(settings, fallbackEnvironmentName)) {
			return fallbackEnvironmentName;
		}

		Assert.Ignore(
			$"get-classic-migration-bundle MCP E2E requires a reachable environment. Configured sandbox environment " +
			$"'{configuredEnvironmentName}' was not reachable, and fallback environment '{fallbackEnvironmentName}' was also unavailable.");
		return string.Empty;
	}

	private static async Task<bool> CanReachEnvironmentAsync(McpE2ESettings settings, string environmentName) {
		using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));
		try {
			ClioCliCommandResult result = await ClioCliCommandRunner.RunAsync(
				settings,
				["ping-app", "-e", environmentName],
				cancellationToken: cts.Token);
			return result.ExitCode == 0;
		} catch (OperationCanceledException) {
			return false;
		}
	}

	private new sealed record ArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource,
		string EnvironmentName) : IAsyncDisposable {
		public ValueTask DisposeAsync() {
			CancellationTokenSource.Dispose();
			return ValueTask.CompletedTask;
		}
	}
}
