using Allure.Net.Commons;
using System.Text.Json;
using Allure.NUnit.Attributes;
using Clio.Command.EntitySchemaDesigner;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>Validates multi-column creation on an exclusively owned real Creatio sandbox.</summary>
[TestFixture, NonParallelizable, Explicit("Requires an exclusive sandbox for schema publishing.")]
[Category("LocalOnly"), Category("McpE2E.Manual"), Category("McpE2E.Sandbox")]
[AllureFeature("create-entity-schema")]
// [AllureNUnit] is intentionally omitted because its lifecycle hooks can deadlock long async flows.
public sealed class EntitySchemaMultipleColumnsE2ETests : McpContractFixtureBase {
	private const string CreateToolName = CreateEntitySchemaTool.CreateEntitySchemaToolName;
	private string? _root;
	private string? _package;
	[TestCase("repeated")]
	[TestCase("objects")]
	[TestCase("array")]
	[TestCase("sequence")]
	[TestCase("single")]
	[TestCase("mcp")]
	[Explicit("Publishes configuration; requires an exclusive local sandbox.")]
	[Category("LocalOnly")]
	[Category("McpE2E.Manual")]
	[Category("McpE2E.Sandbox")]
	[Description("Creates columns through real CLI input forms or MCP and independently reads their persisted metadata.")]
	[AllureTag(CreateToolName)]
	[AllureName("Create entity schema preserves multiple columns")]
	[AllureDescription("Verifies repeated flags, JSON objects, JSON arrays, sequence values, a single column, and MCP using a dedicated Creatio instance.")]
	public async Task CreateEntitySchema_ShouldPersistColumns_WhenUsingSupportedInputForms(string form) {
		TeamCityRunGuard.IgnoreIfRunningUnderTeamCityOrGitHubActions("Schema publishing requires an exclusive local sandbox.");
		// Arrange
		await using EntitySchemaArrangeContext context = await ArrangeSandboxPackageAsync();
		const string notesJson = """{"name":"UsrNotes","type":"Text","title":"Notes; commas, colons: preserved"}""";
		const string amountJson = """{"name":"UsrAmount","type":"Integer","required":true}""";
		string[] columnArgs = form switch {
			"repeated" => ["--column", "UsrNotes:Text", "--column", "UsrAmount:Integer"],
			"objects" => ["--column", notesJson, "--column", amountJson],
			"array" => ["--column", $"[{notesJson},{amountJson}]"],
			"sequence" => ["--column", "UsrNotes:Text", "UsrAmount:Integer"],
			_ => ["--column", notesJson]
		};
		// Act
		await AllureApi.Step("Create schema through the real process", async () => {
			if (form == "mcp") {
				var result = McpCommandExecutionParser.Extract(await CallCreateEntitySchemaAsync(
					context.Session, context.EnvironmentName, context.PackageName, context.SchemaName,
					context.CancellationTokenSource.Token, [
						new() { ["column-name"] = "UsrNotes", ["type"] = "Text" },
						new() { ["column-name"] = "UsrAmount", ["type"] = "Integer", ["required"] = true }
					]));
				AssertCommandSucceeded(result, "MCP must continue to accept its structured column list");
				AssertIncludesInfoMessage(result, "successful MCP creation emits progress");
			} else {
				McpE2ESettings settings = TestConfiguration.Load();
				settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
				await ClioCliCommandRunner.RunAndAssertSuccessAsync(settings,
					["create-entity-schema", "-e", context.EnvironmentName, "--package", context.PackageName,
						"--name", context.SchemaName, "--title", "Column validation", ..columnArgs],
					workingDirectory: context.RootDirectory, cancellationToken: context.CancellationTokenSource.Token);
			}
		});
		EntitySchemaPropertiesInfo schema = await ActGetSchemaPropertiesAsync(context);
		EntitySchemaColumnPropertiesInfo notes = await ActGetColumnPropertiesAsync(context, "UsrNotes");
		// Assert
		AllureApi.Step("Verify persisted columns", () => {
			schema.Columns!.Select(column => column.Name).Should().Contain("UsrNotes", because: "the text column must be persisted");
			schema.OwnColumnCount.Should().Be(form == "single" ? 1 : 2, because: "no requested column may be lost or duplicated");
		});
		if (form is "objects" or "array" or "single") {
			AllureApi.Step("Verify caption punctuation", () => notes.Title.Should().Be(
				"Notes; commas, colons: preserved", because: "JSON punctuation must survive parsing and persistence"));
		}
		if (form != "single") {
			EntitySchemaColumnPropertiesInfo amount = await ActGetColumnPropertiesAsync(context, "UsrAmount");
			AllureApi.Step("Verify second column type and required flag", () => {
				amount.Type.Should().Be("Integer", because: "the second column must retain its type");
				amount.Required.Should().Be(form is "objects" or "array" or "mcp", because: "structured required metadata must persist");
			});
		}
		await AllureApi.Step("Wait for this schema's runtime before publishing another", () => WaitForRuntimeAsync(context));
		if (form == "array") {
			await AllureApi.Step("Write and read both columns through OData", () => VerifyRuntimeAsync(context));
		}
		TestContext.Out.WriteLine($"Validated {form}: {context.EnvironmentName}/{context.PackageName}/{context.SchemaName}");
	}

	private static async Task WaitForRuntimeAsync(EntitySchemaArrangeContext context) {
		Dictionary<string, object?> readArgs = new() {
			["environment-name"] = context.EnvironmentName, ["entity"] = context.SchemaName,
			["select"] = new[] { "UsrNotes" }, ["top"] = 1
		};
		ODataReadResponse ready;
		do {
			ready = EntitySchemaStructuredResultParser.Extract<ODataReadResponse>(await context.Session.CallToolAsync(
				ODataReadTool.ToolName, new Dictionary<string, object?> { ["args"] = readArgs }, context.CancellationTokenSource.Token));
			if (!ready.Success) await Task.Delay(TimeSpan.FromSeconds(5), context.CancellationTokenSource.Token);
		} while (!ready.Success);
	}

	private static async Task VerifyRuntimeAsync(EntitySchemaArrangeContext context) {
		Dictionary<string, object?> readArgs = new() {
			["environment-name"] = context.EnvironmentName, ["entity"] = context.SchemaName,
			["select"] = new[] { "UsrNotes", "UsrAmount" }, ["top"] = 1
		};
		var create = EntitySchemaStructuredResultParser.Extract<ODataCreateBatchResponse>(await context.Session.CallToolAsync(
			ODataCreateTool.ToolName, new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = context.EnvironmentName, ["entity"] = context.SchemaName,
					["rows"] = new[] { new { UsrNotes = "Runtime; punctuation, intact", UsrAmount = 647 } }
				}
			}, context.CancellationTokenSource.Token));
		create.Created.Should().Be(1, because: "both generated columns must accept a real record without retrying a write");
		var read = EntitySchemaStructuredResultParser.Extract<ODataReadResponse>(await context.Session.CallToolAsync(
			ODataReadTool.ToolName, new Dictionary<string, object?> { ["args"] = readArgs }, context.CancellationTokenSource.Token));
		read.Success.Should().BeTrue(because: "the saved record must be readable through the generated runtime schema");
		JsonElement row = read.Value!.Value.EnumerateArray().Single();
		row.GetProperty("UsrNotes").GetString().Should().Be("Runtime; punctuation, intact", because: "text must round-trip");
		row.GetProperty("UsrAmount").GetInt32().Should().Be(647, because: "the second column must store its numeric value");
	}

	private async Task<EntitySchemaArrangeContext> ArrangeSandboxPackageAsync() {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		settings.AllowDestructiveMcpTests.Should().BeTrue(because: "this manual test requires explicit mutation opt-in");
		TestConfiguration.EnsureSandboxIsConfigured(settings);
		string environmentName = settings.Sandbox.EnvironmentName!;
		using var setupTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
		if (_package is null) {
			_root = CreateFixtureDirectory("multiple-columns");
			string workspace = Path.Combine(_root, "workspace");
			string package = $"Pkg{Guid.NewGuid():N}"[..18];
			await ClioCliCommandRunner.RunAndAssertSuccessAsync(settings,
				["create-workspace", "workspace", "--empty", "--directory", _root], _root, setupTimeout.Token);
			await ClioCliCommandRunner.RunAndAssertSuccessAsync(settings, ["add-package", package], workspace, setupTimeout.Token);
			await ClioCliCommandRunner.RunAndAssertSuccessAsync(settings, ["push-workspace", "-e", environmentName], workspace, setupTimeout.Token);
			await ClioCliCommandRunner.WaitForEnvironmentRecoveryAsync(settings, environmentName, setupTimeout.Token);
			await ClioCliCommandRunner.RunAndAssertSuccessAsync(settings,
				["pkg-hotfix", package, "true", "-e", environmentName], workspace, setupTimeout.Token);
			_package = package;
		}
		return new(_root!, environmentName, _package, $"Usr{Guid.NewGuid():N}", Session,
			new CancellationTokenSource(TimeSpan.FromMinutes(8)));
	}

	private static Task<CallToolResult> CallCreateEntitySchemaAsync(McpServerSession session, string environment,
		string package, string schema, CancellationToken cancellationToken, IReadOnlyList<Dictionary<string, object?>> columns) =>
		session.CallToolAsync(CreateToolName, new Dictionary<string, object?> {
			["args"] = new Dictionary<string, object?> {
				["environment-name"] = environment, ["package-name"] = package, ["schema-name"] = schema,
				["title-localizations"] = new Dictionary<string, string> { ["en-US"] = "Column validation" }, ["columns"] = columns
			}
		}, cancellationToken);

	private static async Task<T> ReadAsync<T>(EntitySchemaArrangeContext context, string tool, string? column = null) {
		Dictionary<string, object?> args = new() {
			["environment-name"] = context.EnvironmentName, ["package-name"] = context.PackageName,
			["schema-name"] = context.SchemaName
		};
		if (column is not null) args["column-name"] = column;
		CallToolResult result = await context.Session.CallToolAsync(tool, new Dictionary<string, object?> { ["args"] = args }, context.CancellationTokenSource.Token);
		result.IsError.Should().NotBeTrue(because: "independent metadata reads must succeed");
		return EntitySchemaStructuredResultParser.Extract<T>(result);
	}

	private static Task<EntitySchemaPropertiesInfo> ActGetSchemaPropertiesAsync(EntitySchemaArrangeContext context) =>
		ReadAsync<EntitySchemaPropertiesInfo>(context, GetEntitySchemaPropertiesTool.GetEntitySchemaPropertiesToolName);

	private static Task<EntitySchemaColumnPropertiesInfo> ActGetColumnPropertiesAsync(EntitySchemaArrangeContext context, string column) =>
		ReadAsync<EntitySchemaColumnPropertiesInfo>(context, GetEntitySchemaColumnPropertiesTool.GetEntitySchemaColumnPropertiesToolName, column);

	private static void AssertCommandSucceeded(CommandExecutionEnvelope result, string because) =>
		result.ExitCode.Should().Be(0, because: because);

	private static void AssertIncludesInfoMessage(CommandExecutionEnvelope result, string because) =>
		result.Output.Should().Contain(message => message.MessageType == LogDecoratorType.Info, because: because);

	private sealed record EntitySchemaArrangeContext(string RootDirectory, string EnvironmentName, string PackageName,
		string SchemaName, McpServerSession Session, CancellationTokenSource CancellationTokenSource) : IAsyncDisposable {
		public ValueTask DisposeAsync() {
			CancellationTokenSource.Dispose();
			return ValueTask.CompletedTask;
		}
	}
}
