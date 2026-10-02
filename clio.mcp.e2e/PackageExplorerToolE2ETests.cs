using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using System.Text.Json.Nodes;

namespace Clio.Mcp.E2E;

/// <summary>Real stdio and Creatio checks; explicitly select a feature-capable disposable environment.</summary>
[TestFixture, Category("E2E"), AllureNUnit, AllureFeature("Package dependency explorer")]
[NonParallelizable]
public sealed class PackageExplorerToolE2ETests : McpContractFixtureBase {
	private static IEnumerable<TestCaseData> Operations() {
		yield return new TestCaseData(PackageExplorerTool.GetDependenciesToolName, "{\"package\":\"CrtUIv2\",\"transitive\":true}", "packages");
		yield return new TestCaseData(PackageExplorerTool.GetPathToolName, "{\"from\":\"CrtUIv2\",\"to\":\"CrtCoreBase\"}", "path");
		yield return new TestCaseData(PackageExplorerTool.GetReasonsToolName, "{\"from\":\"CrtUIv2\",\"to\":\"CrtNUI\",\"check-removal\":true}", "dropImpact");
		yield return new TestCaseData(PackageExplorerTool.FindSchemaToolName, "{\"schema\":\"Contact\",\"manager-name\":\"EntitySchemaManager\"}", "schemas");
		yield return new TestCaseData(PackageExplorerTool.FindSchemaToolName, "{\"schema\":\"Contact\",\"package-name\":\"CrtUIv2\"}", "availableSchemaUIds");
		yield return new TestCaseData(PackageExplorerTool.ExportGraphToolName, "{}", "dependencies");
		yield return new TestCaseData(PackageExplorerTool.CheckDependencyToolName, "{\"from\":\"CrtUIv2\",\"to\":\"CrtNUI\",\"action\":\"remove\"}", "assessment");
	}

	[TestCaseSource(nameof(Operations))]
	[Description("Executes the real tool against Creatio and verifies meaningful readback and unchanged dependency edges.")]
	[AllureDescription("Requires CLIO_DEPENDENCY_E2E_ENVIRONMENT pointing at a disposable v1 runtime.")]
	public async Task Invoke_ShouldReturnRuntimeEvidence_WhenV1Supported(string tool, string argsJson, string field) {
		// Arrange
		string? environment = Environment.GetEnvironmentVariable("CLIO_DEPENDENCY_E2E_ENVIRONMENT");
		if (string.IsNullOrWhiteSpace(environment)) { Assert.Ignore("Set CLIO_DEPENDENCY_E2E_ENVIRONMENT for this source-branch fixture."); }
		await using var context = Arrange();
		JsonObject args = JsonNode.Parse(argsJson)!.AsObject();
		args["environment-name"] = environment;
		JsonObject before = await ReadGraph(context, environment!);
		// Act
		var response = await context.Session.CallToolAsync(tool,
			new Dictionary<string, object?> { ["args"] = args }, context.CancellationTokenSource.Token);
		var execution = McpCommandExecutionParser.Extract(response);
		JsonObject after = await ReadGraph(context, environment!);
		// Assert
		response.IsError.Should().NotBe(true, because: "read-only supported operations must succeed over stdio");
		execution.ExitCode.Should().Be(0, because: "the command must receive a successful platform response");
		execution.Output.Should().Contain(item => item.MessageType == LogDecoratorType.Info,
			because: "successful calls must expose human-readable evidence");
		string output = string.Join("\n", execution.Output!.Select(item => item.Value));
		JsonNode evidence = JsonNode.Parse(output)!;
		evidence[field].Should().NotBeNull(because: "the operation must return its actual runtime evidence");
		before["dependencies"]!.ToJsonString().Should().Be(after["dependencies"]!.ToJsonString(),
			because: "inspection and validation must not change package edges");
		if (tool == PackageExplorerTool.GetPathToolName) {
			evidence["reachable"]!.GetValue<bool>().Should().BeTrue(because: "the known two-hop fixture must be reachable");
			var path = evidence["path"]!.AsArray();
			path.First()!["name"]!.GetValue<string>().Should().Be("CrtUIv2", because: "the path starts at the requested package");
			path.Last()!["name"]!.GetValue<string>().Should().Be("CrtCoreBase", because: "the path reaches its indirect dependency");
		}
		if (tool == PackageExplorerTool.GetReasonsToolName) {
			evidence["reasonCount"]!.GetValue<int>().Should().BeGreaterThan(0, because: "the redundant edge still has known references");
			evidence["dropImpact"]!["assessment"]!.GetValue<string>().Should().Be("noKnownBlockers",
				because: "alternative paths retain visibility despite the informational reasons");
		}
		if (tool == PackageExplorerTool.CheckDependencyToolName) {
			evidence["assessment"]!.GetValue<string>().Should().Be("noKnownBlockers", because: "the fixture edge is redundant");
			evidence["checkedKinds"]!.AsArray().Select(item => item!.GetValue<string>()).Should().Contain("ClientUnitRequire",
				because: "the improved assessment covers registered client module references");
		}
		if (tool == PackageExplorerTool.FindSchemaToolName && args["package-name"] != null) {
			evidence["status"]!.GetValue<string>().Should().Be("resolved", because: "the designer resolves Contact in CrtUIv2");
			evidence["availableSchemaUIds"]!.AsArray().Select(item => item!.GetValue<string>()).Should()
				.Contain("16be3651-8fe2-4159-8dd0-a803d4683dd3", because: "the 10.2.344 fixture has an independently observed designer-selected layer");
		}
		if (tool is PackageExplorerTool.GetDependenciesToolName or PackageExplorerTool.ExportGraphToolName) {
			evidence[field]!.AsArray().Should().NotBeEmpty(because: "this installed fixture has a nonempty package graph");
		}

		if (tool == PackageExplorerTool.FindSchemaToolName && args["package-name"] == null) {
			evidence["schemas"]!.AsArray().Should().NotBeEmpty(because: "Contact exists in the seed fixture");
			evidence["schemas"]!.AsArray().Should().OnlyContain(item => item!["name"]!.GetValue<string>() == "Contact",
				because: "exact search must not include schemas merely containing Contact");
		}
	}

	[Test, Description("A pre-feature Creatio instance is rejected through real MCP without silently using legacy endpoints.")]
	public async Task Invoke_ShouldReportUnsupported_WhenStockRuntimeSelected() {
		// Arrange
		string? environment = Environment.GetEnvironmentVariable("CLIO_DEPENDENCY_E2E_OLD_ENVIRONMENT");
		if (string.IsNullOrWhiteSpace(environment)) { Assert.Ignore("Set CLIO_DEPENDENCY_E2E_OLD_ENVIRONMENT for compatibility validation."); }
		await using var context = Arrange();
		// Act
		var response = await context.Session.CallToolAsync(PackageExplorerTool.ExportGraphToolName,
			new Dictionary<string, object?> { ["args"] = new Dictionary<string, object?> { ["environment-name"] = environment } },
			context.CancellationTokenSource.Token);
		var execution = McpCommandExecutionParser.Extract(response);
		// Assert
		execution.ExitCode.Should().Be(1, because: "stock Creatio does not provide dependency v1");
		string.Join(" ", execution.Output!.Select(item => item.Value)).Should().Contain("does not support the versioned package dependency API",
			because: "an unrelated failure does not prove compatibility handling");
		execution.Output.Should().Contain(item => item.MessageType == LogDecoratorType.Error,
			because: "unsupported versions must produce explicit diagnostics");
	}

	private static async Task<JsonObject> ReadGraph(ArrangeContext context, string environment) {
		var response = await context.Session.CallToolAsync(PackageExplorerTool.ExportGraphToolName,
			new Dictionary<string, object?> { ["args"] = new Dictionary<string, object?> { ["environment-name"] = environment } },
			context.CancellationTokenSource.Token);
		var execution = McpCommandExecutionParser.Extract(response);
		execution.ExitCode.Should().Be(0, because: "independent before/after readback must succeed");
		return JsonNode.Parse(string.Join("\n", execution.Output!.Select(item => item.Value)))!.AsObject();
	}
}
