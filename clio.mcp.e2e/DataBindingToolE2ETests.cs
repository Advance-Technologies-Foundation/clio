using System.Text.Json;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the data-binding MCP tools.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature("data-binding")]
[NonParallelizable]
public sealed class DataBindingToolE2ETests {
	private const string CreateToolName = CreateDataBindingTool.CreateDataBindingToolName;
	private const string AddRowToolName = AddDataBindingRowTool.AddDataBindingRowToolName;
	private const string RemoveRowToolName = RemoveDataBindingRowTool.RemoveDataBindingRowToolName;

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
				["values"] = """{"Code":"UsrMcpSetting","Name":"Created by MCP"}"""
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
	[Description("Fails clearly through MCP when create-data-binding targets a schema without a built-in template and no environment-name or uri is provided.")]
	[AllureTag(CreateToolName)]
	[AllureName("Create non-templated data binding without environment fails clearly")]
	[AllureDescription("Uses the real clio MCP server to invoke create-data-binding for a non-templated schema without environment-name and verifies that MCP returns a clear resolution error instead of silently attempting offline generation.")]
	public async Task CreateDataBinding_Should_Fail_Without_Environment_For_NonTemplated_Schema() {
		// Arrange
		await using DataBindingArrangeContext arrangeContext = await ArrangeWorkspaceAsync(requireEnvironment: false);

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			CreateToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["package-name"] = arrangeContext.PackageName,
					["schema-name"] = "UsrOfflineOnly",
					["workspace-path"] = arrangeContext.WorkspacePath
				}
			},
			arrangeContext.CancellationTokenSource.Token);

		// Assert
		callResult.IsError.Should().BeTrue(
			because: "non-templated schemas still require environment-based resolution in the MCP create tool");
		DescribeCallResult(callResult).Should().Contain("An error occurred invoking 'create-data-binding'.",
			because: "the MCP server currently wraps non-templated resolution failures as a top-level invocation error");
	}

	private static async Task<DataBindingArrangeContext> ArrangeWorkspaceAsync(bool requireEnvironment = true) {
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

		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new DataBindingArrangeContext(
			rootDirectory,
			workspacePath,
			packageName,
			environmentName,
			session,
			cancellationTokenSource);
	}

	private static async Task<string> ResolveReachableEnvironmentAsync(McpE2ESettings settings) {
		string? configuredEnvironmentName = settings.Sandbox.EnvironmentName;
		if (!string.IsNullOrWhiteSpace(configuredEnvironmentName) &&
			await CanReachEnvironmentAsync(settings, configuredEnvironmentName)) {
			return configuredEnvironmentName;
		}

		const string fallbackEnvironmentName = "d2";
		if (await CanReachEnvironmentAsync(settings, fallbackEnvironmentName)) {
			return fallbackEnvironmentName;
		}

		Assert.Ignore(
			$"Data-binding MCP E2E requires a reachable environment. Configured sandbox environment '{configuredEnvironmentName}' was not reachable, and fallback environment '{fallbackEnvironmentName}' was also unavailable.");
		return string.Empty;
	}

	private static async Task<bool> CanReachEnvironmentAsync(McpE2ESettings settings, string environmentName) {
		ClioCliCommandResult result = await ClioCliCommandRunner.RunAsync(
			settings,
			["ping-app", "-e", environmentName]);
		return result.ExitCode == 0;
	}

	private static async Task<CommandExecutionActResult> ActCommandAsync(
		DataBindingArrangeContext arrangeContext,
		string toolName,
		Dictionary<string, object?> args) {
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(arrangeContext.CancellationTokenSource.Token);
		tools.Select(tool => tool.Name).Should().Contain(toolName,
			because: "the requested data-binding MCP tool must be advertised before the end-to-end call can be executed");

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
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
			if (Directory.Exists(RootDirectory)) {
				Directory.Delete(RootDirectory, recursive: true);
			}
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
