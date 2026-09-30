using Allure.Net.Commons;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the list-entity-client-schemas-to-file MCP tool.
/// </summary>
/// <remarks>
/// The lookup needs a live Creatio, so these cases cover discovery, argument binding and the output-path
/// refusals, which the tool decides before it contacts the stand. The write against a real entity is in
/// <see cref="ListEntityClientSchemasToFileStandE2ETests"/>.
/// </remarks>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(ListEntityClientSchemasToFileTool.ToolName)]
[NonParallelizable]
public sealed class ListEntityClientSchemasToFileToolE2ETests : McpContractFixtureBase {

	private const string ToolName = ListEntityClientSchemasToFileTool.ToolName;

	[Test]
	[Description("Exposes list-entity-client-schemas-to-file as a discoverable, non-destructive tool through the get-tool-contract compact index on the lazy MCP surface.")]
	[AllureTag(ToolName)]
	[AllureName("list-entity-client-schemas-to-file is discoverable on the lazy surface")]
	[AllureDescription("Starts the real clio MCP server and verifies list-entity-client-schemas-to-file is reachable and carries one non-destructive entry in the compact discovery index.")]
	public async Task ListEntityClientSchemasToFile_Should_Be_Discoverable_On_Lazy_Surface() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		IReadOnlyCollection<string> toolNames = await AllureApi.Step(
			"Act by listing the reachable tool names",
			async () => await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token));
		IReadOnlyList<ToolContractIndexEntry> index = await AllureApi.Step(
			"Act by reading the compact discovery index",
			async () => await context.Session.GetToolContractIndexAsync(context.CancellationTokenSource.Token));

		// Assert
		AllureApi.Step("Assert the tool is reachable", () =>
			toolNames.Should().Contain(ToolName,
				because: "list-entity-client-schemas-to-file must be reachable on the lazy surface through clio-run"));
		ToolContractIndexEntry entry = AllureApi.Step("Assert the index has one entry for the tool", () =>
			index.Should().ContainSingle(item => item.Name == ToolName,
				because: "the compact discovery index must carry exactly one entry for list-entity-client-schemas-to-file").Which);
		AllureApi.Step("Assert the tool is not flagged destructive", () =>
			entry.Destructive.Should().NotBe(true,
				because: "the tool only creates a new local file and refuses an existing one, so it is not destructive"));
	}

	[Test]
	[Description("Binds list-entity-client-schemas-to-file arguments through the real MCP server, reports an unknown environment as a structured failure and writes no file.")]
	[AllureTag(ToolName)]
	[AllureName("list-entity-client-schemas-to-file reports an unknown environment and writes nothing")]
	[AllureDescription("Calls list-entity-client-schemas-to-file with an allowed output path and an unregistered environment and verifies the structured failure names the environment and no file is created.")]
	public async Task ListEntityClientSchemasToFile_Should_Report_Invalid_Environment_And_Write_Nothing() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-to-file-env-{Guid.NewGuid():N}";
		string outputFile = Path.Combine(CreateFixtureDirectory("list-entity-to-file-invalid-env"), "graph.json");

		// Act
		ListEntityClientSchemasToFileResponse response = await AllureApi.Step(
			"Act by invoking the tool against an unknown environment",
			async () => await CallAsync(context, "Contact", invalidEnvironmentName, outputFile));

		// Assert
		AllureApi.Step("Assert failure", () =>
			response.Success.Should().BeFalse(
				because: "an unknown registered environment must fail inside tool execution"));
		AllureApi.Step("Assert the error names the environment", () =>
			response.Error.Should().Contain(invalidEnvironmentName,
				because: "the structured failure must identify the missing environment name"));
		AllureApi.Step("Assert no file was written", () =>
			File.Exists(outputFile).Should().BeFalse(
				because: "a failed lookup must not produce a file"));
	}

	[Test]
	[Description("Refuses an output-file that already exists before the lookup: the error is the path error even though the environment does not exist, and the existing content is unchanged.")]
	[AllureTag(ToolName)]
	[AllureName("list-entity-client-schemas-to-file refuses an existing output file before the lookup")]
	[AllureDescription("Pre-creates the output file with a sentinel, calls the tool on that path with an unregistered environment and verifies the call fails with the already-exists error rather than the environment error, and the sentinel is intact.")]
	public async Task ListEntityClientSchemasToFile_Should_Refuse_Existing_Output_File_Before_Lookup() {
		// Arrange
		const string sentinel = "existing-content-must-survive";
		await using var context = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-to-file-env-{Guid.NewGuid():N}";
		string outputFile = Path.Combine(CreateFixtureDirectory("list-entity-to-file-existing"), "graph.json");
		File.WriteAllText(outputFile, sentinel);

		// Act
		ListEntityClientSchemasToFileResponse response = await AllureApi.Step(
			"Act by invoking the tool on an existing file",
			async () => await CallAsync(context, "Contact", invalidEnvironmentName, outputFile));

		// Assert
		AllureApi.Step("Assert failure", () =>
			response.Success.Should().BeFalse(
				because: "an explicit output-file must never overwrite an existing file"));
		AllureApi.Step("Assert the already-exists error", () =>
			response.Error.Should().Contain("already exists",
				because: "the output path is checked before the lookup"));
		AllureApi.Step("Assert the environment was not consulted", () =>
			response.Error.Should().NotContain(invalidEnvironmentName,
				because: "a refused path must not cost the lookup, so the environment error never appears"));
		AllureApi.Step("Assert the existing content is unchanged", () =>
			File.ReadAllText(outputFile).Should().Be(sentinel,
				because: "a refused call must leave the existing file untouched"));
	}

	[Test]
	[Description("Refuses an output-file outside the workspace and the OS temp directory before the lookup and writes nothing.")]
	[AllureTag(ToolName)]
	[AllureName("list-entity-client-schemas-to-file refuses an output file outside the allowed locations")]
	[AllureDescription("Calls the tool with a path at the root of the temp directory's volume and an unregistered environment and verifies the call fails with the confinement error rather than the environment error, and no file appears.")]
	public async Task ListEntityClientSchemasToFile_Should_Refuse_Output_File_Outside_Allowed_Locations() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-to-file-env-{Guid.NewGuid():N}";
		string outputFile = Path.Combine(
			Path.GetPathRoot(Path.GetTempPath())!,
			$"clio-list-entity-to-file-outside-{Guid.NewGuid():N}.json");

		// Act
		ListEntityClientSchemasToFileResponse response = await AllureApi.Step(
			"Act by invoking the tool with a path outside the allowed locations",
			async () => await CallAsync(context, "Contact", invalidEnvironmentName, outputFile));

		// Assert
		AllureApi.Step("Assert failure", () =>
			response.Success.Should().BeFalse(
				because: "output-file is confined to the workspace or the OS temp directory"));
		AllureApi.Step("Assert the confinement error", () =>
			response.Error.Should().Contain("outside the allowed locations",
				because: "the caller must be told where the file may be written"));
		AllureApi.Step("Assert the environment was not consulted", () =>
			response.Error.Should().NotContain(invalidEnvironmentName,
				because: "the output path is checked before the lookup"));
		AllureApi.Step("Assert no file was written", () =>
			File.Exists(outputFile).Should().BeFalse(
				because: "a refused path must never produce a file"));
	}

	private static async Task<ListEntityClientSchemasToFileResponse> CallAsync(
		ArrangeContext context,
		string entityName,
		string environmentName,
		string outputFile) {
		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["entity-name"] = entityName,
					["environment-name"] = environmentName,
					["output-file"] = outputFile
				}
			},
			context.CancellationTokenSource.Token);
		callResult.IsError.Should().NotBeTrue(
			because: "valid list-entity-client-schemas-to-file payloads should bind and return a structured tool response");
		return EntitySchemaStructuredResultParser.Extract<ListEntityClientSchemasToFileResponse>(callResult);
	}
}
