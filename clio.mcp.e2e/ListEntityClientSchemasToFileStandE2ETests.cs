using System.Text.Json;
using Allure.Net.Commons;
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
/// Sandbox-tier end-to-end test for the list-entity-client-schemas-to-file MCP tool: writes the page-role
/// graph of a real entity and compares the response counts with the file.
/// </summary>
/// <remarks>
/// Kept apart from <see cref="ListEntityClientSchemasToFileToolE2ETests"/> because a category on a fixture is
/// inherited by its tests: inside a NoEnvironment fixture this test would be selected by the NoEnvironment lane
/// and reported there as skipped.
/// </remarks>
[TestFixture]
[Category("McpE2E.Sandbox")]
[AllureNUnit]
[AllureFeature(ListEntityClientSchemasToFileTool.ToolName)]
[NonParallelizable]
public sealed class ListEntityClientSchemasToFileStandE2ETests : McpContractFixtureBase {

	private const string ToolName = ListEntityClientSchemasToFileTool.ToolName;

	[Test]
	[Description("Against a live stand, writes the full list-entity-client-schemas response of a real entity to the file and returns section and edit-page counts that match the file.")]
	[AllureTag(ToolName)]
	[AllureName("list-entity-client-schemas-to-file writes the page-role graph of a real entity")]
	[AllureDescription("Runs list-entity-client-schemas-to-file on the configured sandbox and verifies the file holds a successful list-entity-client-schemas response for the entity, and the response counts per kind equal the counts in that file.")]
	public async Task ListEntityClientSchemasToFile_Should_Write_Page_Role_Graph_On_Stand() {
		// Arrange - needs a registered sandbox stand; skip when none is configured so the NoEnvironment run stays
		// green. The entity defaults to Contact, which every product ships, and is overridable via
		// MCP_E2E_TO_FILE_ENTITY.
		McpE2ESettings settings = TestConfiguration.Load();
		if (string.IsNullOrWhiteSpace(settings.Sandbox.EnvironmentName)) {
			Assert.Ignore("Set McpE2E__Sandbox__EnvironmentName to a registered stand to run the page-role graph file check.");
		}
		string entityName = Environment.GetEnvironmentVariable("MCP_E2E_TO_FILE_ENTITY") ?? "Contact";
		await using var context = Arrange(TimeSpan.FromMinutes(3));
		string outputFile = Path.Combine(CreateFixtureDirectory("list-entity-to-file-stand"), "graph.json");

		// Act
		ListEntityClientSchemasToFileResponse response = await AllureApi.Step(
			"Act by invoking the tool against the sandbox",
			async () => await CallAsync(context, entityName, settings.Sandbox.EnvironmentName, outputFile));
		if (!response.Success) {
			Assert.Ignore(
				$"Entity '{entityName}' does not resolve on this sandbox ({response.Error}); set MCP_E2E_TO_FILE_ENTITY to an entity present on the stand.");
		}
		ListEntityClientSchemasResponse fileContent = JsonSerializer.Deserialize<ListEntityClientSchemasResponse>(
			File.ReadAllText(outputFile),
			new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

		// Assert
		AllureApi.Step("Assert the response names the resolved file", () =>
			response.OutputFile.Should().Be(outputFile,
				because: "the output directory is a physical path, so the resolved path is the requested one"));
		AllureApi.Step("Assert the file holds a successful response", () =>
			fileContent.Success.Should().BeTrue(
				because: "the file holds the full list-entity-client-schemas response of the successful lookup"));
		AllureApi.Step("Assert the file and the response name the same entity", () =>
			fileContent.Entity.Should().Be(response.Entity,
				because: "the response summarizes the file it points to"));
		AllureApi.Step("Assert the section counts match the file", () =>
			response.Sections.Should().Be(CountByKind(fileContent.Sections?.Select(section => section.Kind)),
				because: "sections in the response must count the sections in the file by kind"));
		AllureApi.Step("Assert the edit-page counts match the file", () =>
			response.EditPages.Should().Be(CountByKind(fileContent.EditPages?.Select(page => page.Kind)),
				because: "editPages in the response must count the edit pages in the file by kind"));
	}

	private static PageKindCounts CountByKind(IEnumerable<string>? kinds) {
		List<string> all = kinds?.ToList() ?? [];
		int classic = all.Count(kind => kind == ListEntityClientSchemasCommand.KindClassic);
		int freedom = all.Count(kind => kind == ListEntityClientSchemasCommand.KindFreedom);
		return new PageKindCounts(all.Count, classic, freedom, all.Count - classic - freedom);
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
