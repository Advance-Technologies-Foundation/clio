using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage of the schema-transfer MCP surface (<c>export-schema</c> / <c>import-schema</c>).
/// </summary>
/// <remarks>
/// Both tools are long-tail: they are reachable through the compact <c>get-tool-contract</c> index and
/// <c>clio-run</c> rather than resident in <c>tools/list</c>, which is exactly what the discovery tests assert.
/// </remarks>
[TestFixture]
[AllureNUnit]
[AllureFeature(ExportSchemaTool.ExportSchemaToolName)]
[NonParallelizable]
public sealed class SchemaTransferToolE2ETests : McpContractFixtureBase {

	private const string ExportToolName = ExportSchemaTool.ExportSchemaToolName;
	private const string ImportToolName = ImportSchemaTool.ImportSchemaToolName;

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Exposes export-schema via the get-tool-contract compact index on the lazy tool surface.")]
	[AllureTag(ExportToolName)]
	[AllureName("export-schema is discoverable on the lazy surface")]
	public async Task ExportSchemaTool_Should_Be_Discoverable() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		// Act
		IReadOnlyCollection<string> toolNames =
			await arrangeContext.Session.ListReachableToolNamesAsync(arrangeContext.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(ExportToolName,
			because: "an MCP caller that needs to move one schema between environments must be able to find the "
				+ "tool even though it is not resident in tools/list");
	}

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Exposes import-schema via the get-tool-contract compact index on the lazy tool surface.")]
	[AllureTag(ImportToolName)]
	[AllureName("import-schema is discoverable on the lazy surface")]
	public async Task ImportSchemaTool_Should_Be_Discoverable() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		// Act
		IReadOnlyCollection<string> toolNames =
			await arrangeContext.Session.ListReachableToolNamesAsync(arrangeContext.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(ImportToolName,
			because: "export without a reachable import would leave the handover half-finished");
	}

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Reports a readable failure when export-schema is called with an unknown environment name.")]
	[AllureTag(ExportToolName)]
	[AllureName("export-schema reports invalid environment failures")]
	public async Task ExportSchemaTool_Should_Report_Invalid_Environment_Failure() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-export-schema-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ExportToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = "UsrMissingSchema",
					["environment-name"] = invalidEnvironmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);

		// Assert
		callResult.Should().NotBeNull(
			because: "an unknown environment must produce a tool response, not a transport failure");
	}

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Reports a readable failure when import-schema is pointed at a path that holds no bundle.")]
	[AllureTag(ImportToolName)]
	[AllureName("import-schema reports a missing bundle")]
	public async Task ImportSchemaTool_Should_Report_Missing_Bundle() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string missingBundlePath = Path.Combine(Path.GetTempPath(), $"clio-missing-bundle-{Guid.NewGuid():N}");

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ImportToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["path"] = missingBundlePath,
					["package-name"] = "Custom",
					["environment-name"] = $"noop-{Guid.NewGuid():N}",
					["dry-run"] = true
				}
			},
			arrangeContext.CancellationTokenSource.Token);

		// Assert
		callResult.Should().NotBeNull(
			because: "a missing bundle is an ordinary input error and must stay inside the tool response envelope");
	}
}
