using System.Text.Json;
using Allure.Net.Commons;
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
/// End-to-end tests for the get-component-info-to-file MCP tool.
/// </summary>
/// <remarks>
/// The server runs with an isolated <c>CLIO_HOME</c> and reads the component registry and its <c>docs/</c> tree
/// from a local fixture (<c>CLIO_COMPONENT_REGISTRY_LOCAL_FILE</c>), so the documentation written to disk is a
/// known markdown file and every scenario is offline-deterministic. Output files go to a separate fixture
/// directory: clio refuses an output-file inside its own configuration directory.
/// </remarks>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(ComponentInfoToFileTool.ToolName)]
[NonParallelizable]
public sealed class ComponentInfoToFileToolE2ETests : McpContractFixtureBase {

	private const string ToolName = ComponentInfoToFileTool.ToolName;
	private const string DocumentedComponent = "crt.ToFileDocProbe";
	private const string UndocumentedComponent = "crt.ToFileNoDocProbe";

	// A '#' line inside a fenced block is a code comment, not a heading, so documentationSections must skip it.
	private const string DocumentationMarkdown =
		"# To-file doc probe\n\n" +
		"Intro text.\n\n" +
		"## Usage\n\n" +
		"```js\n" +
		"# not a heading\n" +
		"```\n\n" +
		"### Example ###\n\n" +
		"Body.\n";

	private static readonly string[] ExpectedSections = ["# To-file doc probe", "## Usage", "### Example"];

	private const string RegistryJson = """
	{
	  "components": [
	    { "componentType": "crt.ToFileDocProbe", "category": "display", "description": "Documented to-file probe.", "properties": {},
	      "references": { "docs": ["docs/to-file-doc-probe.component.md"] } },
	    { "componentType": "crt.ToFileNoDocProbe", "category": "display", "description": "Undocumented to-file probe.", "properties": {} }
	  ]
	}
	""";

	private string _outputDirectory = string.Empty;

	/// <inheritdoc />
	private protected override void ConfigureMcpServerSettings(McpE2ESettings settings) {
		string clioHome = CreateIsolatedClioHome(
			"""
			{
			  "ActiveEnvironmentKey": "dev",
			  "Autoupdate": false,
			  "Environments": {
			    "dev": {
			      "Uri": "http://localhost",
			      "Login": "Supervisor",
			      "Password": "Supervisor",
			      "IsNetCore": true
			    }
			  }
			}
			""",
			GetType().Name);
		string registryDirectory = CreateFixtureDirectory("component-to-file-registry");
		Directory.CreateDirectory(Path.Combine(registryDirectory, "docs"));
		string registryPath = Path.Combine(registryDirectory, "ComponentRegistry.json");
		File.WriteAllText(registryPath, RegistryJson);
		File.WriteAllText(Path.Combine(registryDirectory, "docs", "to-file-doc-probe.component.md"), DocumentationMarkdown);
		_outputDirectory = CreateFixtureDirectory("component-to-file-output");
		settings.ProcessEnvironmentVariables["CLIO_HOME"] = clioHome;
		settings.ProcessEnvironmentVariables[RegistryFlavor.Web.LocalFileEnvironmentVariable] = registryPath;
	}

	[Test]
	[Description("Exposes get-component-info-to-file as a discoverable, non-destructive tool through the get-tool-contract compact index on the lazy MCP surface.")]
	[AllureTag(ToolName)]
	[AllureName("get-component-info-to-file is discoverable on the lazy surface")]
	[AllureDescription("Starts the real clio MCP server and verifies get-component-info-to-file is reachable and carries one non-destructive entry in the compact discovery index.")]
	public async Task ComponentInfoToFile_Should_Be_Discoverable_On_Lazy_Surface() {
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
				because: "get-component-info-to-file must be reachable on the lazy surface through clio-run"));
		ToolContractIndexEntry entry = AllureApi.Step("Assert the index has one entry for the tool", () =>
			index.Should().ContainSingle(item => item.Name == ToolName,
				because: "the compact discovery index must carry exactly one entry for get-component-info-to-file").Which);
		AllureApi.Step("Assert the tool is not flagged destructive", () =>
			entry.Destructive.Should().NotBe(true,
				because: "the tool only creates a new local file and refuses an existing one, so it is not destructive"));
	}

	[Test]
	[Description("Writes the component documentation markdown to the requested file byte-for-byte and returns documentationFile and the markdown headings instead of documentation.")]
	[AllureTag(ToolName)]
	[AllureName("get-component-info-to-file writes documentation and returns its path and headings")]
	[AllureDescription("Requests a documented component from the local registry fixture and verifies the file holds exactly the fixture markdown, the response drops documentation, and documentationSections lists the ATX headings outside the fenced block.")]
	public async Task ComponentInfoToFile_Should_Write_Documentation_And_Return_Path_And_Headings() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));
		string outputFile = Path.Combine(_outputDirectory, $"component-docs-{Guid.NewGuid():N}.md");

		// Act
		JsonElement response = await AllureApi.Step(
			"Act by requesting the documented component",
			async () => await CallAsync(context, DocumentedComponent, outputFile));

		// Assert
		AllureApi.Step("Assert success", () =>
			response.GetProperty("success").GetBoolean().Should().BeTrue(
				because: $"the fixture registry publishes {DocumentedComponent}: {response}"));
		AllureApi.Step("Assert the requested component is echoed", () =>
			response.GetProperty("componentType").GetString().Should().Be(DocumentedComponent,
				because: "every other get-component-info field is kept in the to-file response"));
		AllureApi.Step("Assert documentation is not inline", () =>
			response.TryGetProperty("documentation", out _).Should().BeFalse(
				because: "the documentation markdown is replaced by the file path"));
		AllureApi.Step("Assert documentationFile is the requested path", () =>
			response.GetProperty("documentationFile").GetString().Should().Be(outputFile,
				because: "the output directory is a physical path, so the resolved path is the requested one"));
		AllureApi.Step("Assert the headings outside the fence", () =>
			response.GetProperty("documentationSections").EnumerateArray().Select(item => item.GetString())
				.Should().Equal(ExpectedSections,
					because: "documentationSections lists the ATX headings in order, with their '#' prefix, without a closing '#' run and without a '#' line inside a fenced block"));
		AllureApi.Step("Assert the file holds the documentation markdown", () =>
			File.ReadAllText(outputFile).Should().Be(DocumentationMarkdown,
				because: "the file must hold exactly the documentation get-component-info returns inline"));
	}

	[Test]
	[Description("Writes no file and returns the get-component-info response unchanged when the component has no documentation.")]
	[AllureTag(ToolName)]
	[AllureName("get-component-info-to-file writes nothing for an undocumented component")]
	[AllureDescription("Requests a component without docs from the local registry fixture and verifies the call succeeds, no file is created, and the response carries neither documentationFile nor documentationSections.")]
	public async Task ComponentInfoToFile_Should_Write_No_File_When_Component_Has_No_Documentation() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));
		string outputFile = Path.Combine(_outputDirectory, $"component-no-docs-{Guid.NewGuid():N}.md");

		// Act
		JsonElement response = await AllureApi.Step(
			"Act by requesting the undocumented component",
			async () => await CallAsync(context, UndocumentedComponent, outputFile));

		// Assert
		AllureApi.Step("Assert success", () =>
			response.GetProperty("success").GetBoolean().Should().BeTrue(
				because: $"the fixture registry publishes {UndocumentedComponent}: {response}"));
		AllureApi.Step("Assert the requested component is echoed", () =>
			response.GetProperty("componentType").GetString().Should().Be(UndocumentedComponent,
				because: "the response is the get-component-info detail response"));
		AllureApi.Step("Assert no documentationFile", () =>
			response.TryGetProperty("documentationFile", out _).Should().BeFalse(
				because: "there is no documentation to write, so no file is named"));
		AllureApi.Step("Assert no documentationSections", () =>
			response.TryGetProperty("documentationSections", out _).Should().BeFalse(
				because: "the response is returned as get-component-info returns it"));
		AllureApi.Step("Assert no file was written", () =>
			File.Exists(outputFile).Should().BeFalse(
				because: "a response without documentation writes no file"));
	}

	[Test]
	[Description("Refuses an output-file that already exists and leaves its content unchanged.")]
	[AllureTag(ToolName)]
	[AllureName("get-component-info-to-file refuses an existing output file")]
	[AllureDescription("Pre-creates the output file with a sentinel, requests the documented component into it and verifies the call fails with the already-exists error and the sentinel is intact.")]
	public async Task ComponentInfoToFile_Should_Refuse_Existing_Output_File() {
		// Arrange
		const string sentinel = "existing-content-must-survive";
		await using var context = Arrange(TimeSpan.FromMinutes(3));
		string outputFile = Path.Combine(_outputDirectory, $"component-existing-{Guid.NewGuid():N}.md");
		File.WriteAllText(outputFile, sentinel);

		// Act
		JsonElement response = await AllureApi.Step(
			"Act by requesting the documented component into an existing file",
			async () => await CallAsync(context, DocumentedComponent, outputFile));

		// Assert
		AllureApi.Step("Assert failure", () =>
			response.GetProperty("success").GetBoolean().Should().BeFalse(
				because: "an explicit output-file must never overwrite an existing file"));
		AllureApi.Step("Assert the already-exists error", () =>
			response.GetProperty("error").GetString().Should().Contain("already exists",
				because: "the caller must be told why the path was refused"));
		AllureApi.Step("Assert the existing content is unchanged", () =>
			File.ReadAllText(outputFile).Should().Be(sentinel,
				because: "a refused call must leave the existing file untouched"));
	}

	[Test]
	[Description("Refuses an output-file outside the workspace and the OS temp directory and writes nothing.")]
	[AllureTag(ToolName)]
	[AllureName("get-component-info-to-file refuses an output file outside the allowed locations")]
	[AllureDescription("Requests the documented component into a path at the root of the temp directory's volume and verifies the call fails with the confinement error and no file appears.")]
	public async Task ComponentInfoToFile_Should_Refuse_Output_File_Outside_Allowed_Locations() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));
		string outputFile = Path.Combine(
			Path.GetPathRoot(Path.GetTempPath())!,
			$"clio-component-to-file-outside-{Guid.NewGuid():N}.md");

		// Act
		JsonElement response = await AllureApi.Step(
			"Act by requesting the documented component into a path outside the allowed locations",
			async () => await CallAsync(context, DocumentedComponent, outputFile));

		// Assert
		AllureApi.Step("Assert failure", () =>
			response.GetProperty("success").GetBoolean().Should().BeFalse(
				because: "output-file is confined to the workspace or the OS temp directory"));
		AllureApi.Step("Assert the confinement error", () =>
			response.GetProperty("error").GetString().Should().Contain("outside the allowed locations",
				because: "the caller must be told where the file may be written"));
		AllureApi.Step("Assert no file was written", () =>
			File.Exists(outputFile).Should().BeFalse(
				because: "a refused path must never produce a file"));
	}

	private static async Task<JsonElement> CallAsync(ArrangeContext context, string componentType, string outputFile) {
		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["component-type"] = componentType,
					["output-file"] = outputFile
				}
			},
			context.CancellationTokenSource.Token);
		callResult.IsError.Should().NotBeTrue(
			because: "get-component-info-to-file should return a structured response instead of a top-level MCP failure");
		return EntitySchemaStructuredResultParser.Extract<JsonElement>(callResult);
	}
}
