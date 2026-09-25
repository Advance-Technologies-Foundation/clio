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
public sealed class PageParentValidationE2ETests : McpContractFixtureBase {
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

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    [Description("Real MCP reports missing template context, rejects typos with known containers, and accepts real parents.")]
    [AllureTag(PageValidateTool.ToolName)]
    [AllureName("validate-page resolves parent references")]
    [AllureDescription("Verifies missing template context warnings, rejected unknown parents, and acceptance of known parent containers through the real MCP server.")]
    public async Task ValidatePage_ShouldReportParentResolution(bool supplyContainers, bool correctParent) {
        // Arrange
        await using var context = Arrange(TimeSpan.FromMinutes(3));
        string parent = correctParent ? "MainContainer" : "MainContaner";
        string diff = System.Text.Json.JsonSerializer.Serialize(new { operation = "insert", name = "Child", parentName = parent, propertyName = "items", values = new { type = "crt.FlexContainer", items = Array.Empty<object>() } });
        var args = new Dictionary<string, object?> { ["body"] = Body.Replace("COMMENT", diff) };
        if (supplyContainers) args["known-containers"] = new[] { "MainContainer" };
        // Act
        CallToolResult result = await context.Session.CallToolAsync(PageValidateTool.ToolName,
            new Dictionary<string, object?> { ["args"] = args }, context.CancellationTokenSource.Token);
        var response = EntitySchemaStructuredResultParser.Extract<PageValidateResponse>(result);
        // Assert
        result.IsError.Should().NotBeTrue(because: "validation is a structured result");
        response.Valid.Should().Be(!supplyContainers || correctParent, because: "only a confirmed missing parent is an error");
        if (!supplyContainers) response.Validation.Warnings.Should().Contain(x => x.Contains("MainContaner"), because: "unknown template context must be visible");
        if (supplyContainers && !correctParent) response.Validation.Errors.Should().Contain(x => x.Contains("MainContainer"), because: "the diagnostic suggests the actual container");
    }
    [Test]
    [Description("Unsupported dynamic diff expressions return structured invalid results rather than MCP failures.")]
    [AllureTag(PageValidateTool.ToolName)]
    [AllureName("validate-page rejects dynamic diff expressions")]
    [AllureDescription("Verifies that a dynamic diff expression produces a structured invalid result through the real MCP server.")]
    public async Task ValidatePage_ShouldReturnStructuredFailure_WhenDiffIsNotJson() {
        // Arrange
        await using var context = Arrange(TimeSpan.FromMinutes(3));
        var args = new Dictionary<string, object?> { ["body"] = Body.Replace("COMMENT", "...items") };
        // Act
        CallToolResult result = await context.Session.CallToolAsync(PageValidateTool.ToolName,
            new Dictionary<string, object?> { ["args"] = args }, context.CancellationTokenSource.Token);
        // Assert
        result.IsError.Should().NotBeTrue(because: "bad section content belongs in the validation envelope");
        EntitySchemaStructuredResultParser.Extract<PageValidateResponse>(result).Valid.Should().BeFalse(
            because: "a dynamic expression cannot be validated as a static diff");
    }

}
