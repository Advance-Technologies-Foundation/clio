using System.Text.Json;
using Allure.Net.Commons;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>Real stdio SQL export tests against an explicitly configured disposable Creatio instance.</summary>
// [AllureNUnit] is intentionally omitted. Sequential async MCP and file reads must not block continuations.
[TestFixture]
[AllureFeature(ExecuteSqlScriptTool.ToolName)]
[Category("McpE2E.Sandbox")]
[NonParallelizable]
public sealed class ExecuteSqlScriptToolE2ETests : McpContractFixtureBase {
	private string _environmentName = null!;

	/// <inheritdoc />
	private protected override void ConfigureMcpServerSettings(McpE2ESettings settings) {
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("Set McpE2E__AllowDestructiveMcpTests=true and configure a disposable SQL sandbox.");
		}
		_environmentName = settings.Sandbox.EnvironmentName!;
		if (string.IsNullOrWhiteSpace(_environmentName)) {
			throw new InvalidOperationException("McpE2E__Sandbox__EnvironmentName is required for SQL export validation.");
		}
	}

	[TestCase("json", false, false)]
	[TestCase("json", true, false)]
	[TestCase("json", false, true)]
	[TestCase("csv", false, false)]
	[TestCase("xlsx", false, false)]
	[Description("Exports a 200-character value through the real MCP process and reads the complete value from disk.")]
	[AllureTag(ExecuteSqlScriptTool.ToolName)]
	[AllureName("SQL file exports preserve long cell content")]
	[AllureDescription("Runs a SELECT on the configured disposable Creatio instance through clio-run, exercising format, destination and silent options, and verifies the saved result.")]
	public async Task Execute_ShouldPreserveFullValue_WhenExporting(string view, bool useInputFile, bool escapedText) {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));
		string directory = CreateFixtureDirectory("sql-export");
		string destination = Path.Combine(directory, "result." + view);
		string expected = new('X', 200);
		if (escapedText) {
			expected += "; \"quoted\"\nsecond line\\path";
		}
		string sql = $"SELECT '{expected}' AS value";
		Dictionary<string, object?> args = new() {
			["environment-name"] = _environmentName, ["view"] = view,
			["destination-path"] = destination, ["silent"] = true
		};
		if (useInputFile) {
			string input = Path.Combine(directory, "query.sql");
			await File.WriteAllTextAsync(input, sql);
			args["file"] = input;
		} else {
			args["script"] = sql;
		}
		IReadOnlyCollection<string> names = await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);
		AllureApi.Step("Verify SQL tool discovery", () => names.Should().Contain(ExecuteSqlScriptTool.ToolName,
			because: "the new tool must be discoverable through the MCP catalog"));

		// Act
		CallToolResult result = await AllureApi.Step("Execute SQL and export through MCP", async () =>
			await context.Session.CallToolAsync(ExecuteSqlScriptTool.ToolName,
				new Dictionary<string, object?> { ["args"] = args }, context.CancellationTokenSource.Token));
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(result);

		// Assert
		AllureApi.Step("Verify transport success", () => result.IsError.Should().NotBeTrue(
			because: "export must complete without an MCP error"));
		AllureApi.Step("Verify command success", () => execution.ExitCode.Should().Be(0,
			because: "the SELECT and file export must succeed"));
		AllureApi.Step("Verify informational completion", () => execution.Output.Should().Contain(
			message => message.MessageType == LogDecoratorType.Info,
			because: "silent mode retains the completion message"));
		AllureApi.Step("Verify silent output omits the result body", () => execution.Output.Should().NotContain(
			message => message.Value != null && message.Value.Contains(expected),
			because: "a file export must not flood the MCP response with SQL input or result values"));
		AllureApi.Step("Verify the saved path is returned", () => execution.Output.Should().Contain(
			message => message.Value != null && message.Value.Contains(destination),
			because: "the caller needs the artifact location for later inspection"));
		AllureApi.Step("Verify destination exists", () => File.Exists(destination).Should().BeTrue(
			because: "the destination is the durable result for later inspection"));
		string value;
		if (view == "xlsx") {
			using SpreadsheetDocument workbook = SpreadsheetDocument.Open(destination, false);
			value = workbook.WorkbookPart!.WorksheetParts.Single().Worksheet.Descendants<Row>()
				.Skip(1).Single().Elements<Cell>().Single().CellValue!.Text;
		} else if (view == "json") {
			using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(destination));
			value = document.RootElement[0].GetProperty("value").GetString()!;
		} else {
			value = (await File.ReadAllLinesAsync(destination))[1];
		}
		AllureApi.Step("Verify all 200 characters survived export", () => value.Should().Be(expected,
			because: "file results must bypass the 40-character table display limit"));
	}

	[TestCase("json")]
	[TestCase("table")]
	[TestCase("csv")]
	[TestCase("xlsx")]
	[Description("An empty query result overwrites an older destination instead of leaving stale rows behind.")]
	[AllureTag(ExecuteSqlScriptTool.ToolName)]
	[AllureName("Empty SQL exports replace stale results")]
	[AllureDescription("Seeds an existing output file, executes a zero-row SELECT through the real MCP server, and reads back an empty result in each format.")]
	public async Task Execute_ShouldOverwriteOldResult_WhenQueryReturnsNoRows(string view) {
		// Arrange
		await using ArrangeContext context = Arrange();
		string destination = Path.Combine(CreateFixtureDirectory("sql-empty"), "result." + view);
		await File.WriteAllTextAsync(destination, "[{\"value\":\"stale\"}]");

		// Act
		CallToolResult result = await context.Session.CallToolAsync(ExecuteSqlScriptTool.ToolName,
			new Dictionary<string, object?> { ["args"] = new Dictionary<string, object?> {
				["environment-name"] = _environmentName, ["script"] = "SELECT 1 AS value WHERE 1 = 0",
				["view"] = view, ["destination-path"] = destination, ["silent"] = true
			}}, context.CancellationTokenSource.Token);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(result);

		// Assert
		AllureApi.Step("Verify empty query succeeds", () => execution.ExitCode.Should().Be(0,
			because: "zero rows is a successful query"));
		if (view == "xlsx") {
			using SpreadsheetDocument workbook = SpreadsheetDocument.Open(destination, false);
			AllureApi.Step("Verify workbook has no data rows", () => workbook.WorkbookPart!.WorksheetParts.Single()
				.Worksheet.Descendants<Row>().Skip(1).Should().BeEmpty(
					because: "the new empty workbook must replace previous content"));
		} else {
			string content = await File.ReadAllTextAsync(destination);
			AllureApi.Step("Verify stale rows were replaced", () => content.Trim().Should().Be(view == "json" ? "[]" : "",
				because: "the exported result must describe this query, not the previous one"));
		}
	}

	[TestCase("csv")]
	[TestCase("xlsx")]
	[Description("Rejects an export with no destination over real MCP before SQL can execute.")]
	[AllureTag(ExecuteSqlScriptTool.ToolName)]
	[AllureName("SQL export requires a destination")]
	[AllureDescription("Uses an invalid environment to prove missing destination validation precedes environment resolution and SQL execution.")]
	public async Task Execute_ShouldRejectMissingDestination_WhenFormatRequiresFile(string view) {
		// Arrange
		await using ArrangeContext context = Arrange();

		// Act
		CallToolResult result = await context.Session.CallToolAsync(ExecuteSqlScriptTool.ToolName,
			new Dictionary<string, object?> { ["args"] = new Dictionary<string, object?> {
				["environment-name"] = "missing-sql-export-environment", ["script"] = "SELECT 1", ["view"] = view
			}}, context.CancellationTokenSource.Token);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(result);

		// Assert
		AllureApi.Step("Verify validation failure", () => execution.ExitCode.Should().Be(1,
			because: "a missing destination is an actionable input error"));
		AllureApi.Step("Verify destination diagnostic", () => execution.Output.Should().Contain(
			message => message.MessageType == LogDecoratorType.Error && message.Value!.Contains("destination-path"),
			because: "the validation error must be returned before environment resolution"));
	}

	[TestCase(true)]
	[TestCase(false)]
	[Description("SQL and output-file failures remain visible in silent mode and never report a completed export.")]
	[AllureTag(ExecuteSqlScriptTool.ToolName)]
	[AllureName("SQL exports report execution failures")]
	[AllureDescription("Exercises an invalid SQL query and a missing destination directory on the disposable instance, asserting failure and no output file.")]
	public async Task Execute_ShouldReportFailure_WhenSqlOrDestinationIsInvalid(bool invalidSql) {
		// Arrange
		await using ArrangeContext context = Arrange();
		string directory = CreateFixtureDirectory("sql-failure");
		string destination = invalidSql ? Path.Combine(directory, "result.json")
			: Path.Combine(directory, "missing", "result.json");

		// Act
		CallToolResult result = await context.Session.CallToolAsync(ExecuteSqlScriptTool.ToolName,
			new Dictionary<string, object?> { ["args"] = new Dictionary<string, object?> {
				["environment-name"] = _environmentName,
				["script"] = invalidSql ? "SELECT FROM" : "SELECT 1 AS value",
				["view"] = "json", ["destination-path"] = destination, ["silent"] = true
			}}, context.CancellationTokenSource.Token);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(result);

		// Assert
		AllureApi.Step("Verify failure exit code", () => execution.ExitCode.Should().Be(1,
			because: "SQL and file failures cannot count as successful exports"));
		AllureApi.Step("Verify error remains visible", () => execution.Output.Should().Contain(
			message => message.MessageType == LogDecoratorType.Error,
			because: "silent mode suppresses results, not failures"));
		AllureApi.Step("Verify no output file was created", () => File.Exists(destination).Should().BeFalse(
			because: "failed queries and invalid destinations must not produce a successful-looking file"));
	}
}
