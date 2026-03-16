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
	[Description("Creates a SysModule binding through MCP, adds a row whose image-content column points to a local image file, and verifies that the stored binding value is base64-encoded file content.")]
	[AllureTag(CreateToolName)]
	[AllureTag(AddRowToolName)]
	[AllureName("Add data-binding row encodes image-content file path")]
	[AllureDescription("Uses the real clio MCP server to create an offline SysModule binding, add a row with an Image16 file path, and verify that data.json contains the base64-encoded file bytes rather than the original path string.")]
	public async Task AddDataBindingRow_Should_Encode_Image_File() {
		// Arrange
		await using DataBindingArrangeContext arrangeContext = await ArrangeWorkspaceAsync(requireEnvironment: false);
		string relativeImagePath = await WriteImageFileAsync(arrangeContext.WorkspacePath);
		string bindingDirectoryPath = Path.Combine(arrangeContext.WorkspacePath, "packages", arrangeContext.PackageName, "Data", "SysModule");
		CommandExecutionActResult createResult = await ActCommandAsync(
			arrangeContext,
			CreateToolName,
			new Dictionary<string, object?> {
				["package-name"] = arrangeContext.PackageName,
				["schema-name"] = "SysModule",
				["workspace-path"] = arrangeContext.WorkspacePath
			});
		AssertToolCallSucceeded(createResult);
		AssertCommandExitCode(createResult, 0,
			"create-data-binding should create the offline SysModule binding before the row add flow runs");

		// Act
		CommandExecutionActResult addResult = await ActCommandAsync(
			arrangeContext,
			AddRowToolName,
			new Dictionary<string, object?> {
				["package-name"] = arrangeContext.PackageName,
				["binding-name"] = "SysModule",
				["workspace-path"] = arrangeContext.WorkspacePath,
				["values"] = JsonSerializer.Serialize(new Dictionary<string, string> {
					["Code"] = "UsrImageModule",
					["Image16"] = relativeImagePath
				})
			});

		// Assert
		AssertToolCallSucceeded(addResult);
		AssertCommandExitCode(addResult, 0,
			"add-data-binding-row should succeed when an image-content column points to an existing local file");
		AssertIncludesInfoMessage(addResult,
			"successful add-data-binding-row execution should emit progress output");
		string dataJson = await File.ReadAllTextAsync(Path.Combine(bindingDirectoryPath, "data.json"));
		dataJson.Should().Contain("UsrImageModule",
			because: "the added row should preserve the non-image payload values");
		dataJson.Should().Contain("\"Value\": \"AQID\"",
			because: "the image-content file bytes should be base64-encoded into the binding instead of keeping the original file path");
	}

	[Test]
	[Description("Creates an offline SysModule binding through MCP with explicit displayValue objects for lookup and image-reference columns and verifies that both display texts are written to data.json.")]
	[AllureTag(CreateToolName)]
	[AllureName("Create SysModule binding preserves explicit lookup and image-reference display values")]
	[AllureDescription("Uses the real clio MCP server to create an offline SysModule binding with structured FolderMode and Logo values and verifies that data.json contains both identifier values and their DisplayValue fields.")]
	public async Task CreateDataBinding_Should_Write_DisplayValue_For_Lookup_And_ImageReference_Columns() {
		// Arrange
		await using DataBindingArrangeContext arrangeContext = await ArrangeWorkspaceAsync(requireEnvironment: false);

		// Act
		CommandExecutionActResult createResult = await ActCommandAsync(
			arrangeContext,
			CreateToolName,
			new Dictionary<string, object?> {
				["package-name"] = arrangeContext.PackageName,
				["schema-name"] = "SysModule",
				["workspace-path"] = arrangeContext.WorkspacePath,
				["values"] =
					"""{"Code":"UsrDisplayModule","FolderMode":{"value":"b659d704-3955-e011-981f-00155d043204","displayValue":"Folder mode display"},"Logo":{"value":"1171d0f0-63eb-4bd1-a50b-001ecbaf0001","displayValue":"Logo display"}}"""
			});

		// Assert
		AssertToolCallSucceeded(createResult);
		AssertCommandExitCode(createResult, 0,
			"create-data-binding should accept structured display-value payloads for offline lookup and image-reference columns");
		string bindingDirectoryPath = Path.Combine(arrangeContext.WorkspacePath, "packages", arrangeContext.PackageName, "Data", "SysModule");
		string dataJson = await File.ReadAllTextAsync(Path.Combine(bindingDirectoryPath, "data.json"));
		dataJson.Should().Contain("\"DisplayValue\": \"Folder mode display\"",
			because: "lookup columns should preserve caller-supplied display text in the generated binding");
		dataJson.Should().Contain("\"DisplayValue\": \"Logo display\"",
			because: "image-reference columns should preserve caller-supplied display text in the generated binding");
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

	[Test]
	[Description("Fails clearly through MCP when SysModule.IconBackground uses a color outside the predefined palette.")]
	[AllureTag(CreateToolName)]
	[AllureName("Create SysModule binding with invalid IconBackground color fails clearly")]
	[AllureDescription("Uses the real clio MCP server to invoke create-data-binding for the offline SysModule template with an invalid IconBackground color and verifies that command execution fails with a human-readable validation error.")]
	public async Task CreateDataBinding_Should_Fail_For_Invalid_SysModule_IconBackground_Color() {
		// Arrange
		await using DataBindingArrangeContext arrangeContext = await ArrangeWorkspaceAsync(requireEnvironment: false);

		// Act
		CommandExecutionActResult result = await ActCommandAsync(
			arrangeContext,
			CreateToolName,
			new Dictionary<string, object?> {
				["package-name"] = arrangeContext.PackageName,
				["schema-name"] = "SysModule",
				["workspace-path"] = arrangeContext.WorkspacePath,
				["values"] = """{"Code":"UsrModule","IconBackground":"#123456"}"""
			});

		// Assert
		result.CallResult.IsError.Should().NotBeTrue(
			because: "validation failures should still be returned as normal command execution envelopes");
		AssertCommandExitCode(result, 1,
			"create-data-binding should reject SysModule IconBackground colors outside the allowed palette");
		result.Execution.Output.Should().Contain(message => message.MessageType == LogDecoratorType.Error,
			because: "validation failures should emit an error message in the execution log");
		DescribeExecution(result.Execution).Should().Contain("predefined colors",
			because: "the failure should explain why the requested SysModule color was rejected");
	}

	[Test]
	[Description("Fails clearly through MCP when an offline lookup payload omits DisplayValue for a non-null SysModule FolderMode value.")]
	[AllureTag(CreateToolName)]
	[AllureName("Create SysModule binding without lookup display value fails clearly")]
	[AllureDescription("Uses the real clio MCP server to invoke create-data-binding for the offline SysModule template with a scalar FolderMode lookup value and verifies that command execution fails with a human-readable displayValue validation error.")]
	public async Task CreateDataBinding_Should_Fail_When_Lookup_DisplayValue_Is_Missing_Offline() {
		// Arrange
		await using DataBindingArrangeContext arrangeContext = await ArrangeWorkspaceAsync(requireEnvironment: false);

		// Act
		CommandExecutionActResult result = await ActCommandAsync(
			arrangeContext,
			CreateToolName,
			new Dictionary<string, object?> {
				["package-name"] = arrangeContext.PackageName,
				["schema-name"] = "SysModule",
				["workspace-path"] = arrangeContext.WorkspacePath,
				["values"] = """{"Code":"UsrModule","FolderMode":"b659d704-3955-e011-981f-00155d043204"}"""
			});

		// Assert
		result.CallResult.IsError.Should().NotBeTrue(
			because: "command validation failures should still be returned as normal MCP command envelopes");
		AssertCommandExitCode(result, 1,
			"offline create-data-binding should reject non-null lookup values that do not include display text");
		DescribeExecution(result.Execution).Should().Contain("requires displayValue",
			because: "the error should explain how the lookup value must be shaped");
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

	private static async Task<string> WriteImageFileAsync(string workspacePath) {
		string assetsDirectoryPath = Path.Combine(workspacePath, "assets");
		Directory.CreateDirectory(assetsDirectoryPath);
		string imagePath = Path.Combine(assetsDirectoryPath, "icon.png");
		await File.WriteAllBytesAsync(imagePath, [1, 2, 3]);
		return Path.Combine("assets", "icon.png");
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
