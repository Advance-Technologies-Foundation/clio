using System.Text.Json;
using System.Text.RegularExpressions;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.EntitySchemaDesigner;
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
/// End-to-end tests for entity schema MCP tools.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature("entity-schema")]
[NonParallelizable]
public sealed class EntitySchemaToolE2ETests {
	private const string CreateToolName = CreateEntitySchemaTool.CreateEntitySchemaToolName;
	private const string ReadSchemaToolName = GetEntitySchemaPropertiesTool.GetEntitySchemaPropertiesToolName;
	private const string ReadColumnToolName = GetEntitySchemaColumnPropertiesTool.GetEntitySchemaColumnPropertiesToolName;
	private const string ModifyToolName = ModifyEntitySchemaColumnTool.ModifyEntitySchemaColumnToolName;

	[Test]
	[Description("Creates a remote entity schema, reads its structured properties, adds a column, and reads the structured column properties through the real MCP server.")]
	[AllureTag(CreateToolName)]
	[AllureTag(ReadSchemaToolName)]
	[AllureTag(ReadColumnToolName)]
	[AllureTag(ModifyToolName)]
	[AllureName("Entity schema MCP tools complete a create read modify flow")]
	[AllureDescription("Arranges a unique package in the sandbox environment through the real CLI, then exercises create-entity-schema, get-entity-schema-properties, modify-entity-schema-column, and get-entity-schema-column-properties through the real MCP server and verifies real remote side effects.")]
	public async Task EntitySchemaTools_Should_Complete_Create_Read_Modify_Flow() {
		// Arrange
		await using EntitySchemaArrangeContext arrangeContext = await ArrangeSandboxPackageAsync();

		// Act
		CommandExecutionEnvelope createResult = await ActCreateEntitySchemaAsync(arrangeContext);
		EntitySchemaPropertiesInfo schemaProperties = await ActGetSchemaPropertiesAsync(arrangeContext);
		CommandExecutionEnvelope modifyResult = await ActModifyEntitySchemaColumnAsync(arrangeContext);
		EntitySchemaColumnPropertiesInfo columnProperties = await ActGetColumnPropertiesAsync(arrangeContext);

		// Assert
		AssertCommandSucceeded(createResult,
			"create-entity-schema should succeed for a valid sandbox environment and prepared package");
		AssertIncludesInfoMessage(createResult,
			"successful schema creation should emit progress output");
		AssertSchemaProperties(schemaProperties, arrangeContext);
		AssertCommandSucceeded(modifyResult,
			"modify-entity-schema-column should succeed when adding a valid own text column");
		AssertIncludesInfoMessage(modifyResult,
			"successful column modification should emit progress output");
		AssertColumnProperties(columnProperties, arrangeContext);
	}

	[Test]
	[Description("Reports a readable failure when create-entity-schema is invoked with an unknown environment name.")]
	[AllureTag(CreateToolName)]
	[AllureName("Create entity schema reports invalid environment failures")]
	[AllureDescription("Uses the real MCP server to call create-entity-schema with a guaranteed-missing environment name and verifies the failure is surfaced to the caller.")]
	public async Task CreateEntitySchema_Should_Report_Invalid_Environment() {
		// Arrange
		await using InvalidEnvironmentArrangeContext arrangeContext = await ArrangeInvalidEnvironmentAsync();

		// Act
		CallToolResult callResult = await CallCreateEntitySchemaAsync(arrangeContext.Session, arrangeContext.EnvironmentName,
			"UsrPkg", "UsrBadSchema", arrangeContext.CancellationTokenSource.Token);

		// Assert
		AssertTopLevelFailure(callResult, arrangeContext.EnvironmentName,
			"unknown environment names should fail before remote schema creation starts");
	}

	[Test]
	[Description("Reports a readable failure when get-entity-schema-properties is invoked with an unknown environment name.")]
	[AllureTag(ReadSchemaToolName)]
	[AllureName("Get entity schema properties reports invalid environment failures")]
	[AllureDescription("Uses the real MCP server to call get-entity-schema-properties with a guaranteed-missing environment name and verifies the failure is surfaced to the caller.")]
	public async Task GetEntitySchemaProperties_Should_Report_Invalid_Environment() {
		// Arrange
		await using InvalidEnvironmentArrangeContext arrangeContext = await ArrangeInvalidEnvironmentAsync();

		// Act
		CallToolResult callResult = await CallGetSchemaPropertiesAsync(arrangeContext.Session, arrangeContext.EnvironmentName,
			"UsrPkg", "UsrBadSchema", arrangeContext.CancellationTokenSource.Token);

		// Assert
		AssertTopLevelFailure(callResult, arrangeContext.EnvironmentName,
			"unknown environment names should fail before schema properties are read");
	}

	[Test]
	[Description("Reports a readable failure when get-entity-schema-column-properties is invoked with an unknown environment name.")]
	[AllureTag(ReadColumnToolName)]
	[AllureName("Get entity schema column properties reports invalid environment failures")]
	[AllureDescription("Uses the real MCP server to call get-entity-schema-column-properties with a guaranteed-missing environment name and verifies the failure is surfaced to the caller.")]
	public async Task GetEntitySchemaColumnProperties_Should_Report_Invalid_Environment() {
		// Arrange
		await using InvalidEnvironmentArrangeContext arrangeContext = await ArrangeInvalidEnvironmentAsync();

		// Act
		CallToolResult callResult = await CallGetColumnPropertiesAsync(arrangeContext.Session, arrangeContext.EnvironmentName,
			"UsrPkg", "UsrBadSchema", "Name", arrangeContext.CancellationTokenSource.Token);

		// Assert
		AssertTopLevelFailure(callResult, arrangeContext.EnvironmentName,
			"unknown environment names should fail before column properties are read");
	}

	[Test]
	[Description("Reports a readable failure when modify-entity-schema-column is invoked with an unknown environment name.")]
	[AllureTag(ModifyToolName)]
	[AllureName("Modify entity schema column reports invalid environment failures")]
	[AllureDescription("Uses the real MCP server to call modify-entity-schema-column with a guaranteed-missing environment name and verifies the failure is surfaced to the caller.")]
	public async Task ModifyEntitySchemaColumn_Should_Report_Invalid_Environment() {
		// Arrange
		await using InvalidEnvironmentArrangeContext arrangeContext = await ArrangeInvalidEnvironmentAsync();

		// Act
		CallToolResult callResult = await CallModifyEntitySchemaColumnAsync(
			arrangeContext.Session,
			arrangeContext.EnvironmentName,
			"UsrPkg",
			"UsrBadSchema",
			"add",
			"Name",
			arrangeContext.CancellationTokenSource.Token,
			type: "Text");

		// Assert
		AssertTopLevelFailure(callResult, arrangeContext.EnvironmentName,
			"unknown environment names should fail before column mutations start");
	}

	[AllureStep("Arrange sandbox package and MCP session for entity schema tools")]
	private static async Task<EntitySchemaArrangeContext> ArrangeSandboxPackageAsync() {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("Set McpE2E:AllowDestructiveMcpTests=true to run destructive entity schema MCP end-to-end tests.");
		}

		TestConfiguration.EnsureSandboxIsConfigured(settings);
		string rootDirectory = Path.Combine(Path.GetTempPath(), $"clio-entity-schema-mcp-e2e-{Guid.NewGuid():N}");
		Directory.CreateDirectory(rootDirectory);

		string workspaceName = $"workspace-{Guid.NewGuid():N}";
		string workspacePath = Path.Combine(rootDirectory, workspaceName);
		string packageName = $"Pkg{Guid.NewGuid():N}".Substring(0, 18);
		string schemaName = $"Usr{Guid.NewGuid():N}".Substring(0, 22);
		string initialColumnName = "Name";
		string addedColumnName = "Code";
		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(8));

		try {
			await ClioCliCommandRunner.EnsureCliogateInstalledAsync(
				settings,
				settings.Sandbox.EnvironmentName!,
				cancellationTokenSource.Token);
		}
		catch (Exception ex) {
			Assert.Ignore(
				$"Skipping destructive entity schema MCP end-to-end test because cliogate could not be installed or verified for '{settings.Sandbox.EnvironmentName}'. {ex.Message}");
		}
		await CreateEmptyWorkspaceAsync(settings, rootDirectory, workspaceName, cancellationTokenSource.Token);
		await AddPackageAsync(settings, workspacePath, packageName, cancellationTokenSource.Token);
		await PushWorkspaceAsync(settings, workspacePath, settings.Sandbox.EnvironmentName!, cancellationTokenSource.Token);

		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new EntitySchemaArrangeContext(
			settings,
			rootDirectory,
			workspacePath,
			settings.Sandbox.EnvironmentName!,
			packageName,
			schemaName,
			initialColumnName,
			addedColumnName,
			session,
			cancellationTokenSource);
	}

	[AllureStep("Arrange invalid-environment MCP session for entity schema tools")]
	private static async Task<InvalidEnvironmentArrangeContext> ArrangeInvalidEnvironmentAsync() {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(2));
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new InvalidEnvironmentArrangeContext(
			$"missing-entity-schema-env-{Guid.NewGuid():N}",
			session,
			cancellationTokenSource);
	}

	[AllureStep("Act by invoking create-entity-schema through MCP")]
	private static async Task<CommandExecutionEnvelope> ActCreateEntitySchemaAsync(EntitySchemaArrangeContext arrangeContext) {
		CallToolResult callResult = await CallCreateEntitySchemaAsync(
			arrangeContext.Session,
			arrangeContext.EnvironmentName,
			arrangeContext.PackageName,
			arrangeContext.SchemaName,
			arrangeContext.CancellationTokenSource.Token,
			columns: [
				new Dictionary<string, object?> {
					["name"] = arrangeContext.InitialColumnName,
					["type"] = "Text",
					["title"] = "Vehicle name"
				}
			]);
		return McpCommandExecutionParser.Extract(callResult);
	}

	[AllureStep("Act by invoking get-entity-schema-properties through MCP")]
	private static async Task<EntitySchemaPropertiesInfo> ActGetSchemaPropertiesAsync(
		EntitySchemaArrangeContext arrangeContext) {
		CallToolResult callResult = await CallGetSchemaPropertiesAsync(
			arrangeContext.Session,
			arrangeContext.EnvironmentName,
			arrangeContext.PackageName,
			arrangeContext.SchemaName,
			arrangeContext.CancellationTokenSource.Token);
		return EntitySchemaStructuredResultParser.Extract<EntitySchemaPropertiesInfo>(callResult);
	}

	[AllureStep("Act by invoking modify-entity-schema-column through MCP")]
	private static async Task<CommandExecutionEnvelope> ActModifyEntitySchemaColumnAsync(
		EntitySchemaArrangeContext arrangeContext) {
		CallToolResult callResult = await CallModifyEntitySchemaColumnAsync(
			arrangeContext.Session,
			arrangeContext.EnvironmentName,
			arrangeContext.PackageName,
			arrangeContext.SchemaName,
			"add",
			arrangeContext.AddedColumnName,
			arrangeContext.CancellationTokenSource.Token,
			type: "Text",
			title: "Vehicle code",
			indexed: true);
		return McpCommandExecutionParser.Extract(callResult);
	}

	[AllureStep("Act by invoking get-entity-schema-column-properties through MCP")]
	private static async Task<EntitySchemaColumnPropertiesInfo> ActGetColumnPropertiesAsync(
		EntitySchemaArrangeContext arrangeContext) {
		CallToolResult callResult = await CallGetColumnPropertiesAsync(
			arrangeContext.Session,
			arrangeContext.EnvironmentName,
			arrangeContext.PackageName,
			arrangeContext.SchemaName,
			arrangeContext.AddedColumnName,
			arrangeContext.CancellationTokenSource.Token);
		return EntitySchemaStructuredResultParser.Extract<EntitySchemaColumnPropertiesInfo>(callResult);
	}

	private static async Task<CallToolResult> CallCreateEntitySchemaAsync(
		McpServerSession session,
		string environmentName,
		string packageName,
		string schemaName,
		CancellationToken cancellationToken,
		IReadOnlyList<Dictionary<string, object?>>? columns = null) {
		IList<McpClientTool> tools = await session.ListToolsAsync(cancellationToken);
		tools.Select(tool => tool.Name).Should().Contain(CreateToolName,
			because: "the create-entity-schema MCP tool must be advertised before the end-to-end call can be executed");

		return await session.CallToolAsync(
			CreateToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["package-name"] = packageName,
					["schema-name"] = schemaName,
					["title"] = "Vehicle",
					["columns"] = columns
				}
			},
			cancellationToken);
	}

	private static async Task<CallToolResult> CallGetSchemaPropertiesAsync(
		McpServerSession session,
		string environmentName,
		string packageName,
		string schemaName,
		CancellationToken cancellationToken) {
		IList<McpClientTool> tools = await session.ListToolsAsync(cancellationToken);
		tools.Select(tool => tool.Name).Should().Contain(ReadSchemaToolName,
			because: "the get-entity-schema-properties MCP tool must be advertised before the end-to-end call can be executed");

		return await session.CallToolAsync(
			ReadSchemaToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["package-name"] = packageName,
					["schema-name"] = schemaName
				}
			},
			cancellationToken);
	}

	private static async Task<CallToolResult> CallGetColumnPropertiesAsync(
		McpServerSession session,
		string environmentName,
		string packageName,
		string schemaName,
		string columnName,
		CancellationToken cancellationToken) {
		IList<McpClientTool> tools = await session.ListToolsAsync(cancellationToken);
		tools.Select(tool => tool.Name).Should().Contain(ReadColumnToolName,
			because: "the get-entity-schema-column-properties MCP tool must be advertised before the end-to-end call can be executed");

		return await session.CallToolAsync(
			ReadColumnToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["package-name"] = packageName,
					["schema-name"] = schemaName,
					["column-name"] = columnName
				}
			},
			cancellationToken);
	}

	private static async Task<CallToolResult> CallModifyEntitySchemaColumnAsync(
		McpServerSession session,
		string environmentName,
		string packageName,
		string schemaName,
		string action,
		string columnName,
		CancellationToken cancellationToken,
		string? type = null,
		string? title = null,
		bool? indexed = null) {
		IList<McpClientTool> tools = await session.ListToolsAsync(cancellationToken);
		tools.Select(tool => tool.Name).Should().Contain(ModifyToolName,
			because: "the modify-entity-schema-column MCP tool must be advertised before the end-to-end call can be executed");

		Dictionary<string, object?> args = new() {
			["environment-name"] = environmentName,
			["package-name"] = packageName,
			["schema-name"] = schemaName,
			["action"] = action,
			["column-name"] = columnName
		};
		if (!string.IsNullOrWhiteSpace(type)) {
			args["type"] = type;
		}
		if (!string.IsNullOrWhiteSpace(title)) {
			args["title"] = title;
		}
		if (indexed.HasValue) {
			args["indexed"] = indexed.Value;
		}

		return await session.CallToolAsync(
			ModifyToolName,
			new Dictionary<string, object?> { ["args"] = args },
			cancellationToken);
	}

	[AllureStep("Assert command execution succeeded")]
	private static void AssertCommandSucceeded(CommandExecutionEnvelope execution, string because) {
		execution.ExitCode.Should().Be(0, because: because);
	}

	[AllureStep("Assert execution includes Info message")]
	private static void AssertIncludesInfoMessage(CommandExecutionEnvelope execution, string because) {
		execution.Output.Should().NotBeNullOrEmpty(
			because: "successful command execution should emit human-readable diagnostics");
		execution.Output!.Should().Contain(message => message.MessageType == LogDecoratorType.Info,
			because: because);
	}

	[AllureStep("Assert structured schema properties")]
	private static void AssertSchemaProperties(EntitySchemaPropertiesInfo properties, EntitySchemaArrangeContext arrangeContext) {
		properties.Name.Should().Be(arrangeContext.SchemaName,
			because: "the created schema should be readable through the structured schema properties tool");
		properties.PackageName.Should().Be(arrangeContext.PackageName,
			because: "the structured result should report the package that owns the created schema");
		properties.Title.Should().Be("Vehicle",
			because: "the structured result should preserve the schema title from creation");
		properties.OwnColumnCount.Should().BeGreaterThan(0,
			because: "the created schema should contain at least the created text column and generated primary guid column");
	}

	[AllureStep("Assert structured column properties")]
	private static void AssertColumnProperties(EntitySchemaColumnPropertiesInfo properties, EntitySchemaArrangeContext arrangeContext) {
		properties.SchemaName.Should().Be(arrangeContext.SchemaName,
			because: "the structured result should identify the mutated schema");
		properties.ColumnName.Should().Be(arrangeContext.AddedColumnName,
			because: "the added column should be readable through the structured column properties tool");
		properties.Source.Should().Be("own",
			because: "columns added through modify-entity-schema-column should be reported as own columns");
		properties.Type.Should().Be("Text",
			because: "the structured result should preserve the added column type");
		properties.Title.Should().Be("Vehicle code",
			because: "the structured result should preserve the added column title");
		properties.Indexed.Should().BeTrue(
			because: "the structured result should preserve boolean mutation flags");
	}

	[AllureStep("Assert top-level MCP failure is readable")]
	private static void AssertTopLevelFailure(CallToolResult callResult, string environmentName, string because) {
		callResult.IsError.Should().BeTrue(because: because);
		string content = JsonSerializer.Serialize(callResult.Content);
		string structuredContent = JsonSerializer.Serialize(callResult.StructuredContent);
		$"{content}{Environment.NewLine}{structuredContent}".Should().MatchRegex(
			$"(?is)({Regex.Escape(environmentName)}|environment.*not.*found|not found|error occurred invoking)",
			because: "the failure should either identify the missing environment explicitly or at least report a readable MCP invocation failure");
	}

	private static async Task CreateEmptyWorkspaceAsync(
		McpE2ESettings settings,
		string rootDirectory,
		string workspaceName,
		CancellationToken cancellationToken) {
		await ClioCliCommandRunner.RunAndAssertSuccessAsync(
			settings,
			["create-workspace", workspaceName, "--empty", "--directory", rootDirectory],
			workingDirectory: rootDirectory,
			cancellationToken: cancellationToken);
	}

	private static async Task AddPackageAsync(
		McpE2ESettings settings,
		string workspacePath,
		string packageName,
		CancellationToken cancellationToken) {
		await ClioCliCommandRunner.RunAndAssertSuccessAsync(
			settings,
			["add-package", packageName],
			workingDirectory: workspacePath,
			cancellationToken: cancellationToken);
	}

	private static async Task PushWorkspaceAsync(
		McpE2ESettings settings,
		string workspacePath,
		string environmentName,
		CancellationToken cancellationToken) {
		await ClioCliCommandRunner.RunAndAssertSuccessAsync(
			settings,
			["push-workspace", "-e", environmentName, "--use-application-installer", "true"],
			workingDirectory: workspacePath,
			cancellationToken: cancellationToken);
	}

	private sealed record EntitySchemaArrangeContext(
		McpE2ESettings Settings,
		string RootDirectory,
		string WorkspacePath,
		string EnvironmentName,
		string PackageName,
		string SchemaName,
		string InitialColumnName,
		string AddedColumnName,
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

	private sealed record InvalidEnvironmentArrangeContext(
		string EnvironmentName,
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
		}
	}
}
