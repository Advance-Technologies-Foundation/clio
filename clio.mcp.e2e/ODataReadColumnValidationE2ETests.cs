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
/// ENG-103020: odata-read explains an invalid-query rejection by checking select / filter / order-by names against
/// the entity's $metadata and listing the closest valid names, measured against a live Creatio stand.
/// </summary>
[TestFixture]
[Category("McpE2E.Sandbox")]
[AllureNUnit]
[AllureFeature(ODataReadTool.ToolName)]
[NonParallelizable]
public sealed class ODataReadColumnValidationE2ETests : McpContractFixtureBase {

	[Test]
	[AllureTag(ODataReadTool.ToolName)]
	[AllureName("odata-read suggests ActivityCategoryId for an Activity CategoryId select")]
	[AllureDescription("Selects the non-existent Activity column CategoryId through the real MCP server and verifies the invalid-query rejection carries the closest valid name.")]
	[Description("odata-read with an unknown Activity column returns invalid-query, unknown-members with ActivityCategoryId suggested and the entity column count.")]
	public async Task ODataRead_Should_Suggest_ActivityCategoryId_For_Unknown_CategoryId() {
		// Arrange
		string environmentName = await ResolveEnvironmentAsync();
		await using var arrange = Arrange(TimeSpan.FromMinutes(5));

		// Act
		ODataReadResponse response = await ReadAsync(arrange, environmentName, new Dictionary<string, object?> {
			["entity"] = "Activity",
			["select"] = new[] { "Id", "CategoryId" },
			["top"] = 1
		});

		// Assert
		response.Success.Should().BeFalse(because: "Activity has no CategoryId column, so the query must be rejected");
		response.ErrorCode.Should().Be("invalid-query",
			because: "an unknown member is a request-shape failure");
		response.UnknownMembers.Should().NotBeNull(because: "the rejection must name what was unknown");
		ODataUnknownMember member = response.UnknownMembers!.Should().ContainSingle(
			because: "only CategoryId is unknown; Id exists").Which;
		member.Argument.Should().Be("select", because: "the name was sent in select");
		member.Name.Should().Be("CategoryId", because: "the member is reported as the caller spelled it");
		member.Suggestions.Should().Contain("ActivityCategoryId",
			because: "ActivityCategoryId is the real lookup column the caller meant");
		member.Suggestions.Count.Should().BeLessThanOrEqualTo(3,
			because: "at most three suggestions are reported per name");
		response.ValidColumnCount.Should().BeGreaterThan(0,
			because: "the entity's column count is reported next to unknown-members");
		response.Error.Should().Contain("Creatio rejected this OData query as invalid",
			because: "the server's rejection is kept, not replaced");
		response.Error.Should().Contain("Checked against the OData metadata of Activity",
			because: "the names are explained after the server rejected the query, appended to the rejection");
	}

	[Test]
	[AllureTag(ODataReadTool.ToolName)]
	[AllureName("odata-read passes valid Activity names through")]
	[AllureDescription("Reads one Activity row selecting real columns and verifies the validator does not reject a valid query.")]
	[Description("odata-read with valid select, order-by and filter names succeeds and carries no unknown-members.")]
	public async Task ODataRead_Should_Accept_Valid_Names() {
		// Arrange
		string environmentName = await ResolveEnvironmentAsync();
		await using var arrange = Arrange(TimeSpan.FromMinutes(5));

		// Act
		ODataReadResponse response = await ReadAsync(arrange, environmentName, new Dictionary<string, object?> {
			["entity"] = "Activity",
			["select"] = new[] { "Id", "Title", "ActivityCategoryId" },
			["order-by"] = "Title",
			["top"] = 1
		});

		// Assert
		response.Success.Should().BeTrue(
			because: $"every name exists on Activity, so validation must not reject it: {response.Error}");
		response.UnknownMembers.Should().BeNull(because: "nothing was unknown");
	}

	[Test]
	[AllureTag(ODataReadTool.ToolName)]
	[AllureName("odata-read suggests the ActivityCategory/Id navigation path for a filter, and Creatio accepts it")]
	[AllureDescription("Filters Activity on the non-existent CategoryId, takes the first suggestion and re-runs the read with it: the suggestion must be a form Creatio accepts in a filter.")]
	[Description("QA ENG-103020: a filter suggestion for a lookup is the navigation path ActivityCategory/Id (not ActivityCategoryId, which Creatio rejects in a filter), and a read using it succeeds.")]
	public async Task ODataRead_Should_Suggest_A_Filter_Form_Creatio_Accepts() {
		// Arrange
		string environmentName = await ResolveEnvironmentAsync();
		await using var arrange = Arrange(TimeSpan.FromMinutes(5));
		const string anyCategory = "f51c4643-58e6-df11-971b-001d60e938c6";

		// Act
		ODataReadResponse rejected = await ReadAsync(arrange, environmentName, new Dictionary<string, object?> {
			["entity"] = "Activity",
			["select"] = new[] { "Id" },
			["filters"] = new { all = new[] { new { field = "CategoryId", op = "eq", value = anyCategory } } },
			["top"] = 1
		});
		string suggestion = rejected.UnknownMembers!.Should().ContainSingle(
			because: "only the filter field is unknown").Which.Suggestions.First();
		ODataReadResponse retried = await ReadAsync(arrange, environmentName, new Dictionary<string, object?> {
			["entity"] = "Activity",
			["select"] = new[] { "Id" },
			["filters"] = new { all = new[] { new { field = suggestion, op = "eq", value = anyCategory } } },
			["order-by"] = suggestion,
			["top"] = 1
		});

		// Assert
		suggestion.Should().Be("ActivityCategory/Id",
			because: "a lookup is filtered through its navigation path; the scalar ActivityCategoryId is rejected there");
		retried.Success.Should().BeTrue(
			because: $"the suggested name must be one Creatio accepts in a filter and order-by: {retried.Error}");
	}

	[Test]
	[AllureTag(ODataReadToFileTool.ToolName)]
	[AllureName("odata-read-to-file explains an invalid-query rejection like odata-read")]
	[AllureDescription("Selects the non-existent Activity column CategoryId through odata-read-to-file and verifies the rejection carries the same unknown-members explanation as the inline read, and that no file is written.")]
	[Description("Review #1798: odata-read-to-file runs the same metadata explanation on invalid-query as odata-read.")]
	public async Task ODataReadToFile_Should_Explain_Unknown_Columns_Like_The_Inline_Read() {
		// Arrange
		string environmentName = await ResolveEnvironmentAsync();
		await using var arrange = Arrange(TimeSpan.FromMinutes(5));
		string outputFile = Path.Combine(Path.GetTempPath(), $"odata-col-e2e-{Guid.NewGuid():N}.json");

		// Act
		CallToolResult callResult = await arrange.Session.CallToolAsync(
			ODataReadToFileTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["entity"] = "Activity",
					["select"] = new[] { "Id", "CategoryId" },
					["top"] = 1,
					["output-file"] = outputFile
				}
			},
			arrange.CancellationTokenSource.Token);
		ODataReadResponse response = EntitySchemaStructuredResultParser.Extract<ODataReadResponse>(callResult);

		// Assert
		response.Success.Should().BeFalse(because: "Activity has no CategoryId column");
		response.ErrorCode.Should().Be("invalid-query", because: "both read paths classify the rejection the same way");
		response.UnknownMembers.Should().ContainSingle(because: "only CategoryId is unknown")
			.Which.Suggestions.Should().Contain("ActivityCategoryId",
				because: "the file path suggests the same names as the inline read");
		response.ValidColumnCount.Should().BeGreaterThan(0, because: "the column count travels with unknown-members");
		response.Error.Should().Contain("Creatio rejected this OData query as invalid",
			because: "the server's rejection is kept, not replaced");
		File.Exists(outputFile).Should().BeFalse(because: "a rejected read never publishes a file");
	}

	private static async Task<ODataReadResponse> ReadAsync(ArrangeContext arrange, string environmentName,
			Dictionary<string, object?> args) {
		args["environment-name"] = environmentName;
		CallToolResult callResult = await arrange.Session.CallToolAsync(
			ODataReadTool.ToolName,
			new Dictionary<string, object?> { ["args"] = args },
			arrange.CancellationTokenSource.Token);
		callResult.IsError.Should().NotBeTrue(
			because: "odata-read reports failures as structured results, not protocol errors");
		return EntitySchemaStructuredResultParser.Extract<ODataReadResponse>(callResult);
	}

	private static async Task<string> ResolveEnvironmentAsync() {
		McpE2ESettings settings = TestConfiguration.Load();
		return await ReachableSandboxEnvironment.ResolveConfiguredOrIgnoreAsync(settings,
			$"odata-read column validation MCP E2E requires the configured sandbox environment '{settings.Sandbox.EnvironmentName}' to be set and reachable.");
	}
}
