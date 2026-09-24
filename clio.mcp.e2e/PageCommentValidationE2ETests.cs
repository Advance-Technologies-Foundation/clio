using Allure.Net.Commons;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[NonParallelizable]
public sealed class PageCommentValidationE2ETests : McpContractFixtureBase {
	private const string Body = """
		define("UsrCommentProof", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/,
		function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return {
		viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[COMMENT]/**SCHEMA_VIEW_CONFIG_DIFF*/,
		viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,
		modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/,
		handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/,
		converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,
		validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });
		""";

	[TestCase(PageValidateTool.ToolName)]
	[TestCase(PageUpdateTool.ToolName)]
	[TestCase(PageSyncTool.ToolName)]
	[Description("Real MCP accepts comments in all three page tools and rejects missing boundaries in the offline validate/sync gates.")]
	[AllureTag(PageValidateTool.ToolName, PageUpdateTool.ToolName, PageSyncTool.ToolName)]
	[AllureName("Comment acceptance never relaxes paired schema markers")]
	[AllureDescription("Exercises real stdio comment validation for all three tools. Write tools target a unique unregistered environment so no save is possible. Missing-boundary assertions cover validate-page and sync-pages; update-page enforces markers after environment resolution and is covered at the command boundary separately.")]
	public async Task PageTools_ShouldAcceptCommentsAndRequireMarkerPairs(string tool) {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));
		string missingEnvironment = "comment-proof-" + Guid.NewGuid().ToString("N");
		foreach (string comment in new[] { "", "// rationale\n", "/* rationale */" }) {
			// update-page resolves its environment before the command-level marker check.
			int[] boundaries = tool == PageUpdateTool.ToolName ? [-1] : [-1, 0, 1];
			foreach (int missingBoundary in boundaries) {
				string body = Body.Replace("COMMENT", comment);
				const string marker = "/**SCHEMA_VIEW_CONFIG_DIFF*/";
				if (missingBoundary >= 0) {
					int index = missingBoundary == 0 ? body.IndexOf(marker, StringComparison.Ordinal)
						: body.LastIndexOf(marker, StringComparison.Ordinal);
					body = body.Remove(index, marker.Length);
				}
				var args = new Dictionary<string, object?> { ["body"] = body };
				if (tool != PageValidateTool.ToolName) {
					args["environment-name"] = missingEnvironment;
					args["validate"] = true;
					args["schema-name"] = "UsrCommentProof";
				}
				if (tool == PageSyncTool.ToolName) {
					args.Remove("body");
					args.Remove("schema-name");
					args["pages"] = new[] { new Dictionary<string, object?> {
						["schema-name"] = "UsrCommentProof", ["body"] = body } };
				}
				// Act
				CallToolResult result = await context.Session.CallToolAsync(tool,
					new Dictionary<string, object?> { ["args"] = args }, context.CancellationTokenSource.Token);
				// Assert
				AllureApi.Step("Assert a structured response", () => result.IsError.Should().NotBeTrue(
					because: "validation and environment errors belong to the tool result"));
				if (tool == PageValidateTool.ToolName) {
					PageValidateResponse response = EntitySchemaStructuredResultParser.Extract<PageValidateResponse>(result);
					AllureApi.Step("Assert marker-sensitive validation", () => response.Valid.Should().Be(missingBoundary < 0,
						because: "ordinary comments are valid only inside a complete marker envelope"));
				} else {
					string error = tool == PageUpdateTool.ToolName
						? EntitySchemaStructuredResultParser.Extract<PageUpdateResponse>(result).Error
						: EntitySchemaStructuredResultParser.Extract<PageSyncResponse>(result).Pages.Single().Error;
					AllureApi.Step("Assert the precise failure boundary", () => error.Should().Contain(
						missingBoundary < 0 ? missingEnvironment : "SCHEMA_VIEW_CONFIG_DIFF",
						because: "valid comments must reach environment resolution, but missing markers must fail before any write"));
				}
			}
		}
	}
}
