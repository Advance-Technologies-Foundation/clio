using System.Text.Json;
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
/// End-to-end tests for the update-client-unit-schema MCP tool. Every call is a dry run against a schema present on
/// every stand, so the stand is never changed: the dry run resolves the schema and lints the body without saving it.
/// </summary>
[TestFixture]
[Category("McpE2E.Sandbox")]
[AllureNUnit]
[AllureFeature("update-client-unit-schema")]
[NonParallelizable]
public sealed class ClientUnitSchemaUpdateToolE2ETests : McpContractFixtureBase {
	private const string ToolName = ClientUnitSchemaUpdateTool.ToolName;

	// A base-product classic client unit present on every stand; a dry run only resolves it.
	private const string ExistingSchema = "ContactPageV2";

	private const string ModuleWithBareComparisonType =
		"define(\"UsrE2EClientModule\", [\"@creatio-devkit/common\"], function(sdk) {\n"
		+ "\tfunction statusFilter(statusId) {\n"
		+ "\t\treturn { filterType: 1, comparisonType: 3, leftExpression: { expressionType: 0, columnPath: \"Status\" },\n"
		+ "\t\t\trightExpression: { expressionType: 2, parameter: { dataValueType: 0, value: statusId } } };\n"
		+ "\t}\n"
		+ "\treturn { statusFilter: statusFilter };\n"
		+ "});";

	private const string CleanModule =
		"define(\"UsrE2EClientModule\", [\"@creatio-devkit/common\"], function(sdk) {\n"
		+ "\tfunction add(a, b) { return a + b; }\n"
		+ "\treturn { add: add };\n"
		+ "});";

	[Test]
	[Description("A dry-run save of a client-module body returns its code-quality findings in `warnings` and still succeeds; a clean module returns no `warnings` key.")]
	[AllureTag(ToolName)]
	[AllureName("update-client-unit-schema returns module lint warnings without blocking the save")]
	[AllureDescription("Calls update-client-unit-schema through the real clio MCP server with dry-run=true for ContactPageV2, first with a client-module body that uses a bare numeric comparisonType, then with a clean module body. The first response succeeds and carries one numeric-comparison-type warning; the second succeeds and carries no warnings key. Nothing is saved.")]
	public async Task UpdateClientUnitSchema_Should_ReturnModuleLintWarnings_WithoutBlockingTheSave() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		CallToolResult flaggedResult = await DryRunAsync(arrangeContext, ModuleWithBareComparisonType);
		CallToolResult cleanResult = await DryRunAsync(arrangeContext, CleanModule);
		ClientUnitSchemaUpdateResponse flagged = EntitySchemaStructuredResultParser.Extract<ClientUnitSchemaUpdateResponse>(flaggedResult);
		ClientUnitSchemaUpdateResponse clean = EntitySchemaStructuredResultParser.Extract<ClientUnitSchemaUpdateResponse>(cleanResult);

		// Assert
		flaggedResult.IsError.Should().NotBeTrue(because: "a reachable environment and a known schema return a structured response");
		flagged.Success.Should().BeTrue(
			because: $"a code-quality finding is a warning and never blocks the save. Error: {flagged.Error}");
		flagged.DryRun.Should().BeTrue(because: "the call asked for a dry run, so nothing is saved");
		flagged.Warnings.Should().ContainSingle(w => w.Contains(PageBodyAstLinter.RuleNumericComparisonType),
			because: "the module's bare `comparisonType: 3` is reported the way validate-page reports it");
		clean.Success.Should().BeTrue(because: $"a clean module saves as before. Error: {clean.Error}");
		clean.Warnings.Should().BeNull(because: "a body that breaks no rule gets no warnings");
		JsonSerializer.Serialize(cleanResult.StructuredContent).Should().NotContain("\"warnings\"",
			because: "the MCP response leaves the warnings key out when there is nothing to report");
	}

	private static async Task<CallToolResult> DryRunAsync(ArrangeContext arrangeContext, string body) =>
		await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = ExistingSchema,
					["body"] = body,
					["dry-run"] = true,
					["environment-name"] = arrangeContext.EnvironmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);

	private async Task<ArrangeContext> ArrangeAsync(McpE2ESettings settings, TimeSpan timeout) {
		CancellationTokenSource cancellationTokenSource = new(timeout);
		string environmentName = await ReachableSandboxEnvironment.ResolveOrIgnoreAsync(
			settings,
			$"update-client-unit-schema MCP E2E requires a reachable environment. Configured sandbox environment " +
			$"'{settings.Sandbox.EnvironmentName}' was not reachable, and fallback environment '{ReachableSandboxEnvironment.FallbackEnvironmentName}' was also unavailable.");
		return new ArrangeContext(Session, cancellationTokenSource, environmentName);
	}

	private new sealed record ArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource,
		string EnvironmentName) : IAsyncDisposable {
		public ValueTask DisposeAsync() {
			CancellationTokenSource.Dispose();
			return ValueTask.CompletedTask;
		}
	}
}
