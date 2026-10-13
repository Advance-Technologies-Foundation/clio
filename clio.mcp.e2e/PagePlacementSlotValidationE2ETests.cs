using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// GH-1752: validate-page, through the real MCP server, rejects a viewConfigDiff insert/move that names a
/// parentName but no propertyName on web and mobile bodies — the shape update-page and sync-pages now refuse
/// to save because the Creatio differ cannot place it and get-page cannot resolve the page afterwards.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(PageValidateTool.ToolName)]
[NonParallelizable]
public sealed class PagePlacementSlotValidationE2ETests : McpContractFixtureBase {
	private const string WebBody = """
		define("UsrSlotProof", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/,
		function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return {
		viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/@@DIFF@@/**SCHEMA_VIEW_CONFIG_DIFF*/,
		viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,
		modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/,
		handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/,
		converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,
		validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });
		""";

	private const string MobileBody = """{"viewConfigDiff": @@DIFF@@, "viewModelConfigDiff": [], "modelConfigDiff": []}""";

	[TestCase(true, false)]
	[TestCase(true, true)]
	[TestCase(false, false)]
	[TestCase(false, true)]
	[Description("validate-page rejects a move with a parentName but no propertyName on mobile and web bodies, and accepts the same move once it names its slot.")]
	[AllureTag(PageValidateTool.ToolName)]
	[AllureName("validate-page requires the slot of a placement that names a parent")]
	[AllureDescription("Sends a mobile and a web body with a move that names parentName FeedTabContainer, with and without propertyName, through the real MCP server, and verifies only the slotless move is rejected with an error naming the operation and the missing propertyName.")]
	public async Task ValidatePage_ShouldRequireSlot_WhenMoveNamesParent(bool mobile, bool withSlot) {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));
		string slot = withSlot ? "\"propertyName\": \"items\", " : string.Empty;
		string diff = "[{\"operation\": \"move\", \"name\": \"AreaProfileContainer\", \"parentName\": \"FeedTabContainer\", "
			+ slot + "\"index\": 0}]";
		string body = (mobile ? MobileBody : WebBody).Replace("@@DIFF@@", diff);

		// Act
		CallToolResult result = await context.Session.CallToolAsync(PageValidateTool.ToolName,
			new Dictionary<string, object?> { ["args"] = new Dictionary<string, object?> { ["body"] = body } },
			context.CancellationTokenSource.Token);
		var response = EntitySchemaStructuredResultParser.Extract<PageValidateResponse>(result);

		// Assert
		result.IsError.Should().NotBeTrue(because: "validation findings are a structured result, not an MCP failure");
		response.Valid.Should().Be(withSlot, because: "only a placement without its slot is rejected");
		if (!withSlot) {
			response.Validation.Errors.Should().Contain(
				error => error.Contains("move 'AreaProfileContainer'") && error.Contains("propertyName"),
				because: "the caller must learn which operation lacks which field");
		}
	}
}
