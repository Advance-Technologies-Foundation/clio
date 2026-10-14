using System.Text;
using System.Text.Json;
using Allure.Net.Commons;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Knowledge;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// ENG-103016: <c>get-guidance</c> answers name=list/index/catalog/help (or no name) with a compact catalog,
/// and an unknown name with the closest guide names, over the real stdio server with a verified synthetic library.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(GuidanceGetTool.ToolName)]
[NonParallelizable]
public sealed class GuidanceGetCatalogE2ETests : McpContractFixtureBase {
	private const string SyntheticSourceAlias = "synthetic";

	private readonly SyntheticKnowledgeNuGetFixture _fixture;
	// The server is shared by the fixture, so the library is installed once; the fixture is NonParallelizable.
	private bool _libraryInstalled;

	public GuidanceGetCatalogE2ETests() {
		_fixture = SyntheticKnowledgeNuGetFixture.Create();
		_fixture.PublishValid("1.0.0", sequence: 10, revision: "guidance-catalog");
	}

	[OneTimeTearDown]
	public void OneTimeTearDown() {
		_fixture.Dispose();
	}

	private protected override void ConfigureMcpServerSettings(McpE2ESettings settings) {
		settings.ProcessEnvironmentVariables["CLIO_HOME"] = CreateIsolatedClioHome("{}", "guidance-catalog-home");
	}

	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("get-guidance returns a compact catalog for list, index, catalog, help and no name")]
	[AllureDescription("Installs a verified synthetic library into the real MCP server and asks get-guidance for the catalog with every keyword and with no name.")]
	[Description("Every catalog keyword and an omitted name return success with a name-per-line catalog that lists the library's guide, carries no availableGuides array and stays within the byte budget.")]
	public async Task GuidanceGet_ShouldReturnCompactCatalog_ForCatalogKeywordsAndNoName() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));
		await InstallSyntheticLibrary(context);

		// Act
		Dictionary<string, JsonElement> responses = new();
		foreach (string? name in new string?[] { "list", "index", "catalog", "help", "LIST", null }) {
			responses[name ?? "<none>"] = await CallGuidance(context, name);
		}

		// Assert
		foreach ((string name, JsonElement response) in responses) {
			AllureApi.Step($"Assert the catalog reply for '{name}'", () => {
				response.GetProperty("success").GetBoolean().Should().BeTrue(
					because: $"name '{name}' is a catalog request, not a guide lookup");
				response.TryGetProperty("catalog", out JsonElement catalog).Should().BeTrue(
					because: "a catalog request must return the catalog field");
				string text = catalog.GetString()!;
				text.Split('\n').Should().Contain(
					line => line.StartsWith(SyntheticKnowledgeNuGetFixture.SelectedGuideName, StringComparison.Ordinal),
					because: "the installed library's guide must be listed by name");
				response.TryGetProperty("availableGuides", out _).Should().BeFalse(
					because: "the catalog replaces the unbounded availableGuides array");
				Encoding.UTF8.GetByteCount(response.GetRawText()).Should().BeLessThan(
					GuidanceGetTool.CatalogBudgetBytes,
					because: "the catalog reply is budgeted so the mandatory first read stays cheap");
			});
		}
	}

	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("get-guidance suggests the closest names for an unknown guide")]
	[AllureDescription("Asks the real MCP server for a misspelled guide name and verifies the failure names the closest guide and points at name=list instead of dumping every guide.")]
	[Description("An unknown, near-miss name fails with a did-you-mean list and a pointer to name=list, and no availableGuides array.")]
	public async Task GuidanceGet_ShouldSuggestClosestNames_ForUnknownGuide() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));
		await InstallSyntheticLibrary(context);
		string misspelled = SyntheticKnowledgeNuGetFixture.SelectedGuideName.Replace("transport", "transprt");

		// Act
		JsonElement response = await CallGuidance(context, misspelled);

		// Assert
		AllureApi.Step("Assert the unknown name is a structured failure with suggestions", () => {
			response.GetProperty("success").GetBoolean().Should().BeFalse(
				because: "a misspelled guide name does not exist");
			string error = response.GetProperty("error").GetString()!;
			error.Should().Contain("Closest names:",
				because: "a near miss must be answered with suggestions");
			error.Should().Contain(SyntheticKnowledgeNuGetFixture.SelectedGuideName,
				because: "the intended guide is within edit distance of the typo");
			error.Should().Contain("get-guidance name=list",
				because: "the reply must say where to find the full catalog");
			response.TryGetProperty("availableGuides", out _).Should().BeFalse(
				because: "the full guide list is no longer dumped on a miss");
		});
	}

	private async Task InstallSyntheticLibrary(ArrangeContext context) {
		if (_libraryInstalled) {
			return;
		}
		await AllureApi.Step("Install the verified synthetic knowledge library", async () => {
			await RunCommand(context, KnowledgeManagementTools.AddKnowledgeSourceToolName,
				new Dictionary<string, object?> {
					["alias"] = SyntheticSourceAlias,
					["libraryId"] = SyntheticKnowledgeNuGetFixture.LibraryId,
					["type"] = "nuget",
					["location"] = _fixture.Feed.ServiceIndexUri.AbsoluteUri,
					["packageId"] = SyntheticKnowledgeNuGetFixture.PackageId,
					["trustedKeyId"] = _fixture.KeyId,
					["trustedPublicKeyPath"] = _fixture.PublicKeyPath,
					["enabled"] = true,
					["priority"] = 100,
					["participation"] = "authoritative",
					["confirmed"] = true
				});
			await RunCommand(context, KnowledgeManagementTools.InstallKnowledgeToolName,
				new Dictionary<string, object?> { ["source"] = SyntheticSourceAlias });
		});
		_libraryInstalled = true;
	}

	private static async Task RunCommand(ArrangeContext context, string command, Dictionary<string, object?> args) {
		CallToolResult result = await context.Session.CallToolAsync(
			ClioRunTool.ToolName,
			new Dictionary<string, object?> { ["command"] = command, ["args"] = args },
			context.CancellationTokenSource.Token);
		result.IsError.Should().NotBeTrue(because: $"{command} must succeed to arrange an active library");
		Text(result).Should().Contain("\"success\":true", because: $"{command} must report success");
	}

	private static async Task<JsonElement> CallGuidance(ArrangeContext context, string? name) {
		Dictionary<string, object?> args = new();
		if (name is not null) {
			args["name"] = name;
		}
		CallToolResult result = await context.Session.CallToolAsync(
			GuidanceGetTool.ToolName,
			new Dictionary<string, object?> { ["args"] = args },
			context.CancellationTokenSource.Token);
		using JsonDocument document = JsonDocument.Parse(Text(result));
		return document.RootElement.Clone();
	}

	private static string Text(CallToolResult result) =>
		string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));
}
