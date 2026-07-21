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
/// End-to-end tests for the get-client-unit-schema MCP tool.
/// </summary>
[TestFixture]
[Category("McpE2E.Sandbox")]
[AllureNUnit]
[AllureFeature("get-client-unit-schema")]
[NonParallelizable]
public sealed class GetClientUnitSchemaToolE2ETests : McpContractFixtureBase {
	private const string ToolName = GetClientUnitSchemaTool.ToolName;

	// A base-product classic client unit present on every stand and replaced across many packages.
	private const string MultiLayerSchema = "ContactPageV2";

	[Test]
	[Description("Reads a real client unit schema through MCP with full-hierarchy=true and then fetches the same layer by schema-uid only.")]
	[AllureTag(ToolName)]
	[AllureName("get-client-unit-schema supports full-hierarchy and schema-uid")]
	[AllureDescription("Uses the real clio MCP server to call get-client-unit-schema for ContactPageV2 with full-hierarchy=true and output-file, verifies the written localizable-strings contract, then calls the same tool with schema-uid only and verifies the body is written to disk.")]
	public async Task GetSchema_Should_ReadFullHierarchy_And_FetchBySchemaUid() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));
		string outputDirectory = CreateFixtureDirectory("client-unit-schema");
		string fullHierarchyFile = Path.Combine(outputDirectory, "ContactPageV2.full-hierarchy.json");
		string bodyFile = Path.Combine(outputDirectory, "ContactPageV2.body.js");

		// Act 1: read by name with full-hierarchy enabled and materialize the compact contract to disk
		CallToolResult fullHierarchyCallResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = MultiLayerSchema,
					["full-hierarchy"] = true,
					["output-file"] = fullHierarchyFile,
					["environment-name"] = arrangeContext.EnvironmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		GetClientUnitSchemaResponse fullHierarchyResponse =
			EntitySchemaStructuredResultParser.Extract<GetClientUnitSchemaResponse>(fullHierarchyCallResult);

		// Assert 1: full-hierarchy response and file contract are usable by MCP clients
		fullHierarchyCallResult.IsError.Should().NotBeTrue(
			because: "a reachable environment and known schema should return a structured response");
		fullHierarchyResponse.Success.Should().BeTrue(
			because: $"full-hierarchy read should succeed for '{MultiLayerSchema}'. Error: {fullHierarchyResponse.Error}");
		fullHierarchyResponse.FullHierarchy.Should().BeTrue(
			because: "the response should echo the requested full-hierarchy mode");
		fullHierarchyResponse.SchemaUId.Should().NotBeNullOrWhiteSpace(
			because: "the schema-uid returned by the name-based read is the stable selector for the direct fetch");
		fullHierarchyResponse.LocalizableStringCount.Should().BeGreaterThan(0,
			because: "ContactPageV2 should expose merged localizable strings on a real stand");
		fullHierarchyResponse.Body.Should().BeNull(
			because: "with output-file set, the large body should be written to disk instead of returned inline");
		File.Exists(fullHierarchyFile).Should().BeTrue(
			because: "full-hierarchy output-file should materialize the documented JSON contract");
		using JsonDocument fullHierarchyDocument = JsonDocument.Parse(await File.ReadAllTextAsync(fullHierarchyFile));
		JsonElement root = fullHierarchyDocument.RootElement;
		root.GetProperty("schemaName").GetString().Should().Be(MultiLayerSchema,
			because: "the written contract should identify the requested schema");
		root.GetProperty("fullHierarchy").GetBoolean().Should().BeTrue(
			because: "the written contract should identify the merge mode");
		root.GetProperty("body").GetString().Should().NotBeNullOrWhiteSpace(
			because: "the body remains the top layer body in the full-hierarchy contract");
		JsonElement localizableStrings = root.GetProperty("localizableStrings");
		localizableStrings.ValueKind.Should().Be(JsonValueKind.Array,
			because: "merged localizable strings should be written as an array");
		localizableStrings.GetArrayLength().Should().BeGreaterThan(0,
			because: "the full-hierarchy contract should include at least one merged localizable string");
		bool hasParentSchemaUId = localizableStrings.EnumerateArray().Any(item =>
			item.TryGetProperty("parentSchemaUId", out JsonElement parentSchemaUId)
				&& parentSchemaUId.ValueKind == JsonValueKind.String
				&& !string.IsNullOrWhiteSpace(parentSchemaUId.GetString()));
		hasParentSchemaUId.Should().BeTrue(
			because: "each merged localizable string should preserve parentSchemaUId provenance");

		// Act 2: fetch the same exact layer by schema-uid only
		CallToolResult schemaUIdCallResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-uid"] = fullHierarchyResponse.SchemaUId,
					["output-file"] = bodyFile,
					["environment-name"] = arrangeContext.EnvironmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		GetClientUnitSchemaResponse schemaUIdResponse =
			EntitySchemaStructuredResultParser.Extract<GetClientUnitSchemaResponse>(schemaUIdCallResult);

		// Assert 2: schema-uid-only invocation is accepted and writes the body
		schemaUIdCallResult.IsError.Should().NotBeTrue(
			because: "schema-uid-only reads should return a structured response rather than a binding failure");
		schemaUIdResponse.Success.Should().BeTrue(
			because: $"schema-uid-only read should succeed for '{fullHierarchyResponse.SchemaUId}'. Error: {schemaUIdResponse.Error}");
		schemaUIdResponse.SchemaUId.Should().Be(fullHierarchyResponse.SchemaUId,
			because: "the direct fetch should use the exact UId returned by the name-based call");
		schemaUIdResponse.BodyLength.Should().BeGreaterThan(0,
			because: "the exact layer should have a non-empty body");
		schemaUIdResponse.Body.Should().BeNull(
			because: "with output-file set, the raw body should be omitted from the response");
		File.Exists(bodyFile).Should().BeTrue(
			because: "schema-uid-only output-file should materialize the raw body");
		(await File.ReadAllTextAsync(bodyFile)).Should().NotBeNullOrWhiteSpace(
			because: "the written body should contain the raw client-unit schema body");
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
			$"get-client-unit-schema MCP E2E requires a reachable environment. Configured sandbox environment " +
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
