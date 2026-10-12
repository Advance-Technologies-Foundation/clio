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
/// ENG-103020: odata-read checks select / filter / order-by names against the entity's $metadata and answers
/// an unknown name locally with the closest valid names, measured against a live Creatio stand.
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
	[AllureDescription("Selects the non-existent Activity column CategoryId through the real MCP server and verifies the local invalid-query rejection with the closest valid name.")]
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
		response.Error.Should().Contain("Nothing was sent to Creatio",
			because: "the rejection is local, before any query request");
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
