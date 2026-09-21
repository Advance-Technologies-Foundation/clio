using System.Text.Json;
using Allure.Net.Commons;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>Proves section deletion preserves prefix neighbors through the real MCP process.</summary>
// [AllureNUnit] is intentionally omitted: this fixture awaits multiple long-running writes sequentially.
[TestFixture]
[NonParallelizable]
[Category("LocalOnly")]
[Category("McpE2E.Manual")]
[Explicit("Publishes schemas; requires an exclusively owned disposable sandbox.")]
public sealed class ApplicationSectionDeleteSafetyToolE2ETests : McpContractFixtureBase {
	[TestCase(false)]
	[TestCase(true)]
	[Category("McpE2E.Sandbox")]
	[Description("Creates two prefix-sharing sections, deletes one through MCP and verifies the neighbor's complete workspace inventory and both entity records survive.")]
	[AllureFeature(ApplicationSectionDeleteTool.ApplicationSectionDeleteToolName)]
	[AllureTag(ApplicationSectionDeleteTool.ApplicationSectionDeleteToolName)]
	[AllureName("Section deletion preserves prefix neighbors and data")]
	[AllureDescription("Proves exact schema targeting through the real MCP process and independent runtime readbacks.")]
	public async Task DeleteSection_ShouldPreserveNeighborAndData_WhenSchemasSharePrefix(bool deleteEntity) {
		// Arrange
		TeamCityRunGuard.IgnoreIfRunningUnderTeamCityOrGitHubActions("Requires an exclusively owned disposable sandbox.");
		McpE2ESettings settings = TestConfiguration.Load();
		if (!settings.AllowDestructiveMcpTests) Assert.Ignore("Requires explicit destructive sandbox opt-in.");
		string environment = await ReachableSandboxEnvironment.ResolveConfiguredOrIgnoreAsync(settings, "Requires a disposable Creatio sandbox.");
		using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(15));
		CancellationToken token = timeout.Token;
		string suffix = Guid.NewGuid().ToString("N")[..8];
		CallToolResult appCall = await CallAsync(ApplicationCreateTool.ApplicationCreateToolName, new() {
			["name"] = $"Deletion safety {suffix}", ["code"] = $"DeleteSafety{suffix}",
			["template-code"] = "EmptyApp", ["environment-name"] = environment
		}, token);
		ApplicationContextResponseEnvelope app = ApplicationResultParser.ExtractInfo(appCall);
		app.Success.Should().BeTrue(because: $"the isolated test application must exist: {app.Error}");
		ApplicationSectionContextResponseEnvelope target = await CreateSectionAsync(app.ApplicationCode!, $"Safety {suffix}", environment, token);
		ApplicationSectionContextResponseEnvelope neighbor = await CreateSectionAsync(app.ApplicationCode!, $"Safety {suffix} Neighbor", environment, token);
		string targetEntity = target.Section!.EntitySchemaName!;
		string neighborEntity = neighbor.Section!.EntitySchemaName!;
		neighborEntity.Should().StartWith(target.Section.Code, because: "the fixture must reproduce the dangerous shared prefix");
		string directory = Path.Combine(Path.GetTempPath(), $"clio-delete-safety-{suffix}");
		Directory.CreateDirectory(directory);
		try {
			await SeedAsync(targetEntity, "target sentinel");
			await SeedAsync(neighborEntity, "neighbor sentinel");
			JsonElement before = await ReadServiceAsync("ServiceModel/WorkspaceExplorerService.svc/GetWorkspaceItems", "{}");
			Dictionary<string, string> neighborBefore = Inventory(before, neighborEntity);
			string targetListUId = target.Section.SectionSchemaUId!;
			neighborBefore.Count.Should().BeGreaterThan(1, because: "the neighbor must have real entity and page schemas before deletion");

			// Act
			CallToolResult deleted = await CallAsync(ApplicationSectionDeleteTool.ApplicationSectionDeleteToolName, new() {
				["application-code"] = app.ApplicationCode, ["section-code"] = target.Section.Code,
				["environment-name"] = environment, ["delete-entity-schema"] = deleteEntity
			}, token);
			ApplicationSectionDeleteContextResponseEnvelope result = ApplicationResultParser.ExtractSectionDelete(deleted);

			// Assert
			AllureApi.Step("Deletion succeeds through MCP", () => {
				deleted.IsError.Should().NotBeTrue(because: "a valid delete should not produce an MCP invocation error");
				result.Success.Should().BeTrue(because: $"the section delete must complete: {result.Error}");
			});
			JsonElement after = await ReadServiceAsync("ServiceModel/WorkspaceExplorerService.svc/GetWorkspaceItems", "{}");
			after.GetProperty("items").EnumerateArray().Any(item => item.GetProperty("name").GetString() == targetEntity)
				.Should().Be(!deleteEntity, because: "only explicit entity opt-in may remove the target entity schema");
			after.GetProperty("items").EnumerateArray().Should().NotContain(item => item.GetProperty("uId").GetString() == targetListUId,
				because: "the target section list page must actually be deleted");
			AllureApi.Step("Neighbor identities survive unchanged", () =>
				Inventory(after, neighborEntity).Should().BeEquivalentTo(neighborBefore, because: "all neighboring schemas must retain their original identities"));
			foreach (string entity in deleteEntity ? new[] { neighborEntity } : new[] { targetEntity, neighborEntity }) {
				JsonElement rows = await ReadServiceAsync("DataService/json/SyncReply/SelectQuery", JsonSerializer.Serialize(new {
					rootSchemaName = entity, columns = new { items = new Dictionary<string, object> {
						["Id"] = new { expression = new { expressionType = 0, columnPath = "Id" } }
					}}, rowCount = 10
				}));
				rows.GetProperty("rows").GetArrayLength().Should().Be(1, because: "section deletion must preserve seeded entity data");
			}
			ApplicationSectionListContextResponseEnvelope listed = ApplicationResultParser.ExtractSectionList(await CallAsync(
				ApplicationSectionGetListTool.ApplicationSectionGetListToolName, new() {
					["application-code"] = app.ApplicationCode, ["environment-name"] = environment
				}, token));
			listed.Success.Should().BeTrue(because: "post-delete section discovery must succeed");
			listed.Sections.Should().NotContain(section => section.Code == target.Section.Code, because: "the named section must be removed");
			listed.Sections.Should().Contain(section => section.Code == neighbor.Section.Code, because: "the neighboring section must remain usable");
		} finally {
			// Preserve remote artifacts for inspection; the operator-owned disposable instance is removed after the run.
			Directory.Delete(directory, recursive: true);
		}

		async Task<JsonElement> ReadServiceAsync(string path, string body) {
			string output = Path.Combine(directory, "response.json");
			ClioCliCommandResult response = await ClioCliCommandRunner.RunAsync(settings,
				["call-service", "-e", environment, "--service-path", path, "--body", body, "--destination", output], cancellationToken: token);
			response.ExitCode.Should().Be(0, because: $"the independent read/write probe must succeed: {response.StandardOutput}");
			response.StandardOutput.Should().Contain("[INF]", because: "the command must report its successful execution");
			using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(output, token));
			return document.RootElement.Clone();
		}

		async Task SeedAsync(string entity, string value) {
			JsonElement response = await ReadServiceAsync("DataService/json/SyncReply/InsertQuery", JsonSerializer.Serialize(new {
				rootSchemaName = entity, columnValues = new { items = new Dictionary<string, object> {
					[app.SchemaNamePrefix + "Name"] = new { expressionType = 2, parameter = new { dataValueType = 1, value } }
				}}
			}));
			response.GetProperty("success").GetBoolean().Should().BeTrue(because: "the preservation test requires actual data before deletion");
		}
	}

	private async Task<ApplicationSectionContextResponseEnvelope> CreateSectionAsync(string application, string caption, string environment, CancellationToken token) {
		ApplicationSectionContextResponseEnvelope response = ApplicationResultParser.ExtractSectionCreate(await CallAsync(
			ApplicationSectionCreateTool.ApplicationSectionCreateToolName, new() {
				["application-code"] = application, ["caption"] = caption, ["environment-name"] = environment,
				["with-mobile-pages"] = false
			}, token));
		response.Success.Should().BeTrue(because: $"the safety fixture section must be created: {response.Error}");
		response.Section.Should().NotBeNull(because: "creation must return the authoritative section identity");
		return response;
	}

	private Task<CallToolResult> CallAsync(string tool, Dictionary<string, object?> args, CancellationToken token) =>
		Session.CallToolAsync(tool, new Dictionary<string, object?> { ["args"] = args }, token);

	private static Dictionary<string, string> Inventory(JsonElement response, string prefix) => response.GetProperty("items").EnumerateArray()
		.Where(item => item.GetProperty("name").GetString()?.StartsWith(prefix, StringComparison.Ordinal) == true)
		.ToDictionary(item => item.GetProperty("name").GetString()!, item => item.GetProperty("uId").GetString()!);
}
