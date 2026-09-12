using System.Text.Json;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the data-binding MCP tools.
/// </summary>
[TestFixture]
[Category("McpE2E.Sandbox")]
[AllureNUnit]
[AllureFeature("data-binding")]
[NonParallelizable]
public sealed class DataBindingToolE2ETests : McpContractFixtureBase {
	private const string CreateToolName = CreateDataBindingTool.CreateDataBindingToolName;
	private const string AddRowToolName = AddDataBindingRowTool.AddDataBindingRowToolName;
	private const string RemoveRowToolName = RemoveDataBindingRowTool.RemoveDataBindingRowToolName;

	[Test]
	[Description("Creates localization-only columns over real MCP and verifies invalid localization input preserves existing artifacts and creates no partial new binding.")]
	[AllureTag(CreateToolName)]
	[AllureName("Local binding localization validation preserves files")]
	[AllureDescription("Checks local-only SysSettings localization generation and failure preservation through the external MCP server.")]
	public async Task CreateDataBinding_ShouldPreserveArtifacts_WhenLocalizationValidationFails() {
		// Arrange
		await using DataBindingArrangeContext context = await ArrangeWorkspaceAsync(requireEnvironment: false);
		Dictionary<string, object?> args = new() {
			["package-name"] = context.PackageName,
			["schema-name"] = "SysSettings",
			["workspace-path"] = context.WorkspacePath,
			["values"] = """{"Code":"UsrLocalized"}""",
			["localizations"] = """{"en-US":{"Name":"Localized name","Description":"Localized description"}}"""
		};

		// Act
		CommandExecutionActResult created = await ActCommandAsync(context, CreateToolName, args);

		// Assert
		Allure.Net.Commons.AllureApi.Step("Verify successful localization-only generation", () => {
			AssertToolCallSucceeded(created);
			AssertCommandExitCode(created, 0, "localization-only columns must be accepted");
			AssertIncludesInfoMessage(created, "successful creation must report progress");
		});
		string bindingPath = Path.Combine(context.WorkspacePath, "packages", context.PackageName, "Data", "SysSettings");
		Allure.Net.Commons.AllureApi.Step("Verify culture content", () =>
			File.ReadAllText(Path.Combine(bindingPath, "Localization", "data.en-US.json"))
				.Should().Contain("Localized description", because: "Description must be generated without a duplicate base value"));

		// Arrange
		Dictionary<string, string> original = Directory.GetFiles(bindingPath, "*", SearchOption.AllDirectories)
			.ToDictionary(path => path, File.ReadAllText);
		args["values"] = """{"Code":"Changed"}""";
		args["localizations"] = """{"en-US":{"Name":"Valid first culture"},"de-DE":{"Unknown":"Invalid"}}""";

		// Act
		CommandExecutionActResult failed = await ActCommandAsync(context, CreateToolName, args);

		// Assert
		Allure.Net.Commons.AllureApi.Step("Verify failure diagnostics", () => {
			AssertCommandExitCode(failed, 1, "unknown localization columns must fail");
			failed.Execution.Output.Should().Contain(message => message.MessageType == LogDecoratorType.Error,
				because: "validation failure must report an error");
		});
		Allure.Net.Commons.AllureApi.Step("Verify all existing content is unchanged", () =>
			Directory.GetFiles(bindingPath, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllText)
				.Should().BeEquivalentTo(original, because: "failed regeneration must preserve every binding artifact"));

		// Arrange
		args["binding-name"] = "InvalidNewBinding";

		// Act
		CommandExecutionActResult failedNew = await ActCommandAsync(context, CreateToolName, args);

		// Assert
		Allure.Net.Commons.AllureApi.Step("Verify invalid new binding leaves no folder", () => {
			AssertCommandExitCode(failedNew, 1, "the same invalid localization must fail for a new binding");
			Directory.Exists(Path.Combine(Path.GetDirectoryName(bindingPath)!, "InvalidNewBinding")).Should().BeFalse(
				because: "validation must precede any new binding files");
		});
	}

	[Test]
	[Description("Creates a workspace and package with the real clio CLI, invokes create-data-binding through MCP for the built-in SysSettings template without a Creatio environment, and verifies the descriptor and data files are generated with an auto-created GUID primary key.")]
	[AllureTag(CreateToolName)]
	[AllureName("Create templated data binding offline auto-generates missing GUID primary key")]
	[AllureDescription("Uses the real clio MCP server to create a data binding from the built-in SysSettings template with values that omit Id and verifies that the package Data folder contains the expected generated files plus an auto-generated GUID primary key without requiring Creatio access.")]
	public async Task CreateDataBinding_Should_Create_Files() {
		// Arrange
		await using DataBindingArrangeContext arrangeContext = await ArrangeWorkspaceAsync(requireEnvironment: false);

		// Act
		CommandExecutionActResult createResult = await ActCommandAsync(
			arrangeContext,
			CreateToolName,
			new Dictionary<string, object?> {
				["package-name"] = arrangeContext.PackageName,
				["schema-name"] = "SysSettings",
				["workspace-path"] = arrangeContext.WorkspacePath,
				["values"] = """{"Code":"UsrMcpSetting","Name":"Created by MCP","ReferenceSchemaUId":"27aeadd6-d508-4572-8061-5b55b667c902"}"""
			});

		// Assert
		AssertToolCallSucceeded(createResult);
		AssertCommandExitCode(createResult, 0,
			"create-data-binding should succeed for a valid workspace package and sandbox environment");
		AssertIncludesInfoMessage(createResult,
			"successful create-data-binding execution should emit progress output");
		string bindingDirectoryPath = Path.Combine(arrangeContext.WorkspacePath, "packages", arrangeContext.PackageName, "Data", "SysSettings");
		File.Exists(Path.Combine(bindingDirectoryPath, "descriptor.json")).Should().BeTrue(
			because: "create-data-binding should generate the descriptor file");
		File.Exists(Path.Combine(bindingDirectoryPath, "data.json")).Should().BeTrue(
			because: "create-data-binding should generate the package data file");
		string descriptorJson = await File.ReadAllTextAsync(Path.Combine(bindingDirectoryPath, "descriptor.json"));
		string dataJson = await File.ReadAllTextAsync(Path.Combine(bindingDirectoryPath, "data.json"));
		descriptorJson.Should().Contain("\"Name\": \"SysSettings\"",
			because: "the generated descriptor should use the default binding folder name");
		descriptorJson.Should().Contain("\"UId\": \"27aeadd6-d508-4572-8061-5b55b667c902\"",
			because: "the generated descriptor should use the built-in template schema identity");
		descriptorJson.Should().Contain("\"ColumnName\": \"Code\"",
			because: "explicit-value mode should retain the requested business columns from the template");
		dataJson.Should().Contain("UsrMcpSetting",
			because: "the created row should preserve the user-provided payload columns");
		descriptorJson.Should().NotContain("ReferenceSchemaName", because: "the platform rejects generator-only descriptor properties");
		dataJson.Should().Contain("\"Value\": \"27aeadd6-d508-4572-8061-5b55b667c902\"", because: "the referenced schema identity must remain in the row payload");
		string? keyColumnUId = null;
		foreach (JsonElement column in JsonDocument.Parse(descriptorJson).RootElement.GetProperty("Descriptor").GetProperty("Columns").EnumerateArray()) {
			if (column.GetProperty("IsKey").GetBoolean()) {
				keyColumnUId = column.GetProperty("ColumnUId").GetString();
				break;
			}
		}
		string? generatedId = null;
		foreach (JsonElement rowValue in JsonDocument.Parse(dataJson).RootElement.GetProperty("PackageData")[0].GetProperty("Row").EnumerateArray()) {
			if (rowValue.GetProperty("SchemaColumnUId").GetString() == keyColumnUId) {
				generatedId = rowValue.GetProperty("Value").GetString();
				break;
			}
		}
		Guid.TryParse(generatedId, out _).Should().BeTrue(
			because: "create-data-binding should auto-generate a GUID primary key when the values payload omits it");
	}

	[Test]
	[Description("Creates a templated binding through MCP without Creatio, then adds and removes a row through MCP, and verifies the resulting file mutations on disk.")]
	[AllureTag(CreateToolName)]
	[AllureTag(AddRowToolName)]
	[AllureTag(RemoveRowToolName)]
	[AllureName("Add and remove data-binding row updates local binding files")]
	[AllureDescription("Uses the real clio MCP server to create a binding from the built-in SysSettings template, add a row, remove the same row, and verify the expected data.json mutations plus user-visible command diagnostics.")]
	public async Task AddAndRemoveDataBindingRow_Should_Mutate_Files() {
		// Arrange
		await using DataBindingArrangeContext arrangeContext = await ArrangeWorkspaceAsync(requireEnvironment: false);
		string bindingDirectoryPath = Path.Combine(arrangeContext.WorkspacePath, "packages", arrangeContext.PackageName, "Data", "SysSettings");
		await ActCommandAsync(
			arrangeContext,
			CreateToolName,
			new Dictionary<string, object?> {
				["package-name"] = arrangeContext.PackageName,
				["schema-name"] = "SysSettings",
				["workspace-path"] = arrangeContext.WorkspacePath,
				["values"] = """{"Name":"Created by MCP"}"""
			});

		// Act
		CommandExecutionActResult addResult = await ActCommandAsync(
			arrangeContext,
			AddRowToolName,
			new Dictionary<string, object?> {
				["package-name"] = arrangeContext.PackageName,
				["binding-name"] = "SysSettings",
				["workspace-path"] = arrangeContext.WorkspacePath,
				["values"] = """{"Name":"Updated by MCP"}""",
				["localizations"] = """{"en-US":{"Name":"Localized by MCP"}}"""
			});

		// Assert
		AssertToolCallSucceeded(addResult);
		AssertCommandExitCode(addResult, 0,
			"add-data-binding-row should succeed for a valid binding and row payload");
		AssertIncludesInfoMessage(addResult,
			"successful add-data-binding-row execution should emit progress output");
		string descriptorJson = await File.ReadAllTextAsync(Path.Combine(bindingDirectoryPath, "descriptor.json"));
		string? keyColumnUId = null;
		foreach (JsonElement column in JsonDocument.Parse(descriptorJson).RootElement.GetProperty("Descriptor").GetProperty("Columns").EnumerateArray()) {
			if (column.GetProperty("IsKey").GetBoolean()) {
				keyColumnUId = column.GetProperty("ColumnUId").GetString();
				break;
			}
		}
		string dataJsonAfterAdd = await File.ReadAllTextAsync(Path.Combine(bindingDirectoryPath, "data.json"));
		string? generatedRowId = null;
		foreach (JsonElement packageRow in JsonDocument.Parse(dataJsonAfterAdd).RootElement.GetProperty("PackageData").EnumerateArray()) {
			bool containsUpdatedName = false;
			string? rowId = null;
			foreach (JsonElement rowValue in packageRow.GetProperty("Row").EnumerateArray()) {
				string? schemaColumnUId = rowValue.GetProperty("SchemaColumnUId").GetString();
				string? value = rowValue.GetProperty("Value").ValueKind == JsonValueKind.String
					? rowValue.GetProperty("Value").GetString()
					: null;
				if (schemaColumnUId == keyColumnUId) {
					rowId = value;
				}
				if (value == "Updated by MCP") {
					containsUpdatedName = true;
				}
			}
			if (containsUpdatedName) {
				generatedRowId = rowId;
				break;
			}
		}
		Guid.TryParse(generatedRowId, out _).Should().BeTrue(
			because: "add-data-binding-row should generate a GUID primary key for the appended row when the payload omits it");
		CommandExecutionActResult removeResult = await ActCommandAsync(
			arrangeContext,
			RemoveRowToolName,
			new Dictionary<string, object?> {
				["package-name"] = arrangeContext.PackageName,
				["binding-name"] = "SysSettings",
				["workspace-path"] = arrangeContext.WorkspacePath,
				["key-value"] = generatedRowId
			});
		AssertToolCallSucceeded(removeResult);
		AssertCommandExitCode(removeResult, 0,
			"remove-data-binding-row should succeed for an existing primary-key value");
		AssertIncludesInfoMessage(removeResult,
			"successful remove-data-binding-row execution should emit progress output");
		string dataJson = await File.ReadAllTextAsync(Path.Combine(bindingDirectoryPath, "data.json"));
		dataJson.Should().NotContain("Updated by MCP",
			because: "the removed row should no longer be present in the main data file after remove-data-binding-row");
	}

	[Test]
	[Description("Fails clearly through MCP when create-data-binding targets a schema without a built-in template and no environment-name is provided.")]
	[AllureTag(CreateToolName)]
	[AllureName("Create non-templated data binding without environment fails clearly")]
	[AllureDescription("Uses the real clio MCP server to invoke create-data-binding for a non-templated schema without environment-name and verifies that command execution fails with a clear runtime-resolution error instead of silently attempting offline generation.")]
	public async Task CreateDataBinding_Should_Fail_Without_Environment_For_NonTemplated_Schema() {
		// Arrange
		await using DataBindingArrangeContext arrangeContext = await ArrangeWorkspaceAsync(requireEnvironment: false);

		// Act
		CommandExecutionActResult result = await ActCommandAsync(
			arrangeContext,
			CreateToolName,
			new Dictionary<string, object?> {
				["package-name"] = arrangeContext.PackageName,
				["schema-name"] = "UsrOfflineOnly",
				["workspace-path"] = arrangeContext.WorkspacePath
			});

		// Assert
		result.CallResult.IsError.Should().NotBeTrue(
			because: "non-templated runtime-resolution failures should be returned as normal command execution envelopes");
		AssertCommandExitCode(result, 1,
			"non-templated schemas still require environment-based resolution in the MCP create tool");
		DescribeExecution(result.Execution).Should().Contain("configured environment name or an explicit URI is required",
			because: "the failure should explain why offline generation is unavailable for the requested schema");
	}

	[TestCase(CreateToolName)]
	[TestCase(AddRowToolName)]
	[TestCase(RemoveRowToolName)]
	[Description("Rejects a package directory passed as workspace-path through the real MCP process, with actionable diagnostics and no binding output.")]
	[AllureTag(CreateToolName, AddRowToolName, RemoveRowToolName)]
	[AllureName("Local binding commands require the workspace root")]
	[AllureDescription("Passes a real local package directory to each binding tool and verifies the missing root marker diagnostic, error log, failure exit code, and absence of output.")]
	public async Task DataBinding_ShouldExplainWorkspaceRoot_WhenPackageDirectoryIsSupplied(string toolName) {
		// Arrange
		await using DataBindingArrangeContext context = await ArrangeWorkspaceAsync(requireEnvironment: false);
		string packagePath = Path.Combine(context.WorkspacePath, "packages", context.PackageName);
		Dictionary<string, object?> args = new() {
			["package-name"] = context.PackageName,
			["workspace-path"] = packagePath
		};
		if (toolName == CreateToolName) {
			args["schema-name"] = "SysSettings";
		} else {
			args["binding-name"] = "SysSettings";
			args[toolName == AddRowToolName ? "values" : "key-value"] =
				toolName == AddRowToolName ? "{}" : Guid.NewGuid().ToString();
		}

		// Act
		CommandExecutionActResult result = await ActCommandAsync(context, toolName, args);

		// Assert
		Allure.Net.Commons.AllureApi.Step("Verify failure envelope and root guidance", () => {
			result.CallResult.IsError.Should().NotBeTrue(because: "path validation returns a command execution envelope");
			AssertCommandExitCode(result, 1, "a package directory does not contain the workspace marker");
			result.Execution.Output.Should().Contain(message => message.MessageType == LogDecoratorType.Error,
				because: "invalid workspace input must emit an Error diagnostic");
			DescribeExecution(result.Execution).Should()
				.Contain(Path.Combine(packagePath, ".clio", "workspaceSettings.json"), because: "the diagnostic identifies the missing marker")
				.And.Contain("not the package directory", because: "the correction must name the expected root")
				.And.Contain("packages/<package-name>", because: "the package resolution rule must be explicit");
		});
		Allure.Net.Commons.AllureApi.Step("Verify no binding artifacts were written", () => {
			Directory.Exists(Path.Combine(packagePath, "Data", "SysSettings")).Should().BeFalse(
				because: "invalid workspace input must be rejected before local binding writes");
		});
	}

	private async Task<DataBindingArrangeContext> ArrangeWorkspaceAsync(bool requireEnvironment = true) {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string? environmentName = requireEnvironment
			? await ResolveReachableEnvironmentAsync(settings)
			: null;

		string rootDirectory = Path.Combine(Path.GetTempPath(), $"clio-data-binding-e2e-{Guid.NewGuid():N}");
		Directory.CreateDirectory(rootDirectory);
		string workspaceName = $"workspace-{Guid.NewGuid():N}";
		string workspacePath = Path.Combine(rootDirectory, workspaceName);
		string packageName = $"Pkg{Guid.NewGuid():N}".Substring(0, 18);
		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(5));

		await ClioCliCommandRunner.RunAndAssertSuccessAsync(
			settings,
			["create-workspace", workspaceName, "--empty", "--directory", rootDirectory],
			cancellationToken: cancellationTokenSource.Token);
		await ClioCliCommandRunner.RunAndAssertSuccessAsync(
			settings,
			["add-package", packageName],
			workingDirectory: workspacePath,
			cancellationToken: cancellationTokenSource.Token);

		McpServerSession session = Session;
		return new DataBindingArrangeContext(
			rootDirectory,
			workspacePath,
			packageName,
			environmentName,
			session,
			cancellationTokenSource);
	}

	private static async Task<string> ResolveReachableEnvironmentAsync(McpE2ESettings settings) =>
		await ReachableSandboxEnvironment.ResolveOrIgnoreAsync(
			settings,
			$"Data-binding MCP E2E requires a reachable environment. Configured sandbox environment '{settings.Sandbox.EnvironmentName}' was not reachable, and fallback environment '{ReachableSandboxEnvironment.FallbackEnvironmentName}' was also unavailable.");

	private static async Task<CommandExecutionActResult> ActCommandAsync(
		DataBindingArrangeContext arrangeContext,
		string toolName,
		Dictionary<string, object?> args) {
		IReadOnlyCollection<string> toolNames =
			await arrangeContext.Session.ListReachableToolNamesAsync(arrangeContext.CancellationTokenSource.Token);
		toolNames.Should().Contain(toolName,
			because: "the requested data-binding MCP tool must be discoverable via the get-tool-contract compact index before the end-to-end call can be executed");

		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			toolName,
			new Dictionary<string, object?> {
				["args"] = args
			},
			arrangeContext.CancellationTokenSource.Token);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);
		return new CommandExecutionActResult(callResult, execution);
	}

	private static void AssertToolCallSucceeded(CommandExecutionActResult actResult) {
		actResult.CallResult.IsError.Should().NotBeTrue(
			because: $"successful data-binding requests should return a normal MCP tool result. Actual MCP content: {DescribeCallResult(actResult.CallResult)}. Parsed execution: {DescribeExecution(actResult.Execution)}");
	}

	private static void AssertCommandExitCode(CommandExecutionActResult actResult, int expectedExitCode, string because) {
		actResult.Execution.ExitCode.Should().Be(expectedExitCode, because: because);
	}

	private static void AssertIncludesInfoMessage(CommandExecutionActResult actResult, string because) {
		actResult.Execution.Output.Should().NotBeNullOrEmpty(
			because: "successful data-binding execution should emit human-readable diagnostics");
		actResult.Execution.Output!.Should().Contain(message => message.MessageType == LogDecoratorType.Info,
			because: because);
	}

	private sealed record DataBindingArrangeContext(
		string RootDirectory,
		string WorkspacePath,
		string PackageName,
		string? EnvironmentName,
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource) : IAsyncDisposable {
		public ValueTask DisposeAsync() {
			CancellationTokenSource.Dispose();
			if (Directory.Exists(RootDirectory)) {
				Directory.Delete(RootDirectory, recursive: true);
			}
			return ValueTask.CompletedTask;
		}
	}

	private sealed record CommandExecutionActResult(CallToolResult CallResult, CommandExecutionEnvelope Execution);

	private static string DescribeCallResult(CallToolResult callResult) {
		if (callResult.Content is null || callResult.Content.Count == 0) {
			return "<no content>";
		}

		return string.Join(
			" | ",
			callResult.Content.Select(content => content?.ToString() ?? "<null>"));
	}

	private static string DescribeExecution(CommandExecutionEnvelope execution) {
		string messages = execution.Output is null
			? "<no messages>"
			: string.Join(" | ", execution.Output.Select(message => $"{message.MessageType}: {message.Value}"));
		return $"ExitCode={execution.ExitCode}; Messages={messages}";
	}
}
