using System.Text.Json;
using System.Text.RegularExpressions;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.EntitySchemaDesigner;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Creatio;
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
	private const string CreateLookupToolName = CreateLookupTool.CreateLookupToolName;
	private const string UpdateToolName = UpdateEntitySchemaTool.UpdateEntitySchemaToolName;
	private const string ReadSchemaToolName = GetEntitySchemaPropertiesTool.GetEntitySchemaPropertiesToolName;
	private const string ReadColumnToolName = GetEntitySchemaColumnPropertiesTool.GetEntitySchemaColumnPropertiesToolName;
	private const string ModifyToolName = ModifyEntitySchemaColumnTool.ModifyEntitySchemaColumnToolName;

	[Test]
	[Description("Creates a remote entity schema, reads its structured properties, adds, modifies, and removes a column, and verifies the structured readbacks through the real MCP server.")]
	[AllureTag(CreateToolName)]
	[AllureTag(ReadSchemaToolName)]
	[AllureTag(ReadColumnToolName)]
	[AllureTag(ModifyToolName)]
	[AllureName("Entity schema MCP tools complete a create read add modify remove flow")]
	[AllureDescription("Arranges a unique package in the sandbox environment through the real CLI, then exercises create-entity-schema, get-entity-schema-properties, modify-entity-schema-column, and get-entity-schema-column-properties through the real MCP server and verifies real remote side effects plus structured readback parsing.")]
	public async Task EntitySchemaTools_Should_Complete_Create_Read_Modify_Flow() {
		// Arrange
		await using EntitySchemaArrangeContext arrangeContext = await ArrangeSandboxPackageAsync();

		// Act
		CommandExecutionEnvelope createResult = await ActCreateEntitySchemaAsync(arrangeContext);
		EntitySchemaPropertiesInfo schemaProperties = await ActGetSchemaPropertiesAsync(arrangeContext);
		CommandExecutionEnvelope addResult = await ActAddEntitySchemaColumnAsync(arrangeContext);
		EntitySchemaPropertiesInfo schemaPropertiesAfterAdd = await ActGetSchemaPropertiesAsync(arrangeContext);
		EntitySchemaColumnPropertiesInfo addedColumnProperties = await ActGetColumnPropertiesAsync(arrangeContext);
		CommandExecutionEnvelope modifyResult = await ActModifyEntitySchemaColumnAsync(arrangeContext);
		EntitySchemaPropertiesInfo schemaPropertiesAfterModify = await ActGetSchemaPropertiesAsync(arrangeContext);
		EntitySchemaColumnPropertiesInfo modifiedColumnProperties = await ActGetColumnPropertiesAsync(arrangeContext);
		CommandExecutionEnvelope removeResult = await ActRemoveEntitySchemaColumnAsync(arrangeContext);
		EntitySchemaPropertiesInfo schemaPropertiesAfterRemove = await ActGetSchemaPropertiesAsync(arrangeContext);

		// Assert
		AssertCommandSucceeded(createResult,
			"create-entity-schema should succeed for a valid sandbox environment and prepared package");
		AssertIncludesInfoMessage(createResult,
			"successful schema creation should emit progress output");
		AssertSchemaProperties(schemaProperties, arrangeContext);
		AssertCommandSucceeded(addResult,
			"modify-entity-schema-column should succeed when adding a valid own text-like column");
		AssertIncludesInfoMessage(addResult,
			"successful add mutation should emit progress output");
		AssertSchemaPropertiesAfterAdd(schemaPropertiesAfterAdd, arrangeContext);
		AssertAddedColumnProperties(addedColumnProperties, arrangeContext);
		AssertCommandSucceeded(modifyResult,
			"modify-entity-schema-column should succeed when updating the previously added own column");
		AssertIncludesInfoMessage(modifyResult,
			"successful modify mutation should emit progress output");
		AssertSchemaPropertiesAfterModify(schemaPropertiesAfterModify, arrangeContext);
		AssertModifiedColumnProperties(modifiedColumnProperties, arrangeContext);
		AssertCommandSucceeded(removeResult,
			"modify-entity-schema-column should succeed when removing the previously added own column");
		AssertIncludesInfoMessage(removeResult,
			"successful remove mutation should emit progress output");
		AssertSchemaPropertiesAfterRemove(schemaProperties, schemaPropertiesAfterRemove, arrangeContext);
	}

	[Test]
	[Description("Creates a remote lookup schema through MCP and verifies the resulting schema inherits from BaseLookup.")]
	[AllureTag(CreateLookupToolName)]
	[AllureTag(ReadSchemaToolName)]
	[AllureName("Create lookup MCP tool creates a BaseLookup schema")]
	[AllureDescription("Arranges a unique package in the sandbox environment through the real CLI, then exercises create-lookup and get-entity-schema-properties through the real MCP server and verifies real remote side effects plus BaseLookup inheritance.")]
	public async Task CreateLookup_Should_Create_BaseLookup_Schema() {
		// Arrange
		await using EntitySchemaArrangeContext arrangeContext = await ArrangeSandboxPackageAsync();

		// Act
		CommandExecutionEnvelope createResult = await ActCreateLookupAsync(arrangeContext);
		EntitySchemaPropertiesInfo schemaProperties = await ActGetSchemaPropertiesAsync(arrangeContext);
		LookupRegistrationSnapshot registrationSnapshot = LookupRegistrationProbe.Read(
			arrangeContext.EnvironmentName,
			arrangeContext.PackageName,
			arrangeContext.SchemaName);
		string lookupColumnName = arrangeContext.LookupColumnName;

		// Assert
		AssertCommandSucceeded(createResult,
			"create-lookup should succeed for a valid sandbox environment and prepared package");
		AssertIncludesInfoMessage(createResult,
			"successful lookup creation should emit progress output");
		schemaProperties.Name.Should().Be(arrangeContext.SchemaName,
			because: "the created lookup should be readable through the structured schema properties tool");
		schemaProperties.ParentSchemaName.Should().Be("BaseLookup",
			because: "create-lookup should force BaseLookup inheritance");
		schemaProperties.Columns.Should().NotBeNullOrEmpty(
			because: "lookup schema readback should expose nested columns for structured inspection");
		schemaProperties.Columns!.Should().Contain(column => column.Source == "inherited",
			because: "BaseLookup-derived schemas should expose inherited base columns in the schema read model");
		schemaProperties.Columns!.Should().Contain(column =>
				column.Name == lookupColumnName
				&& column.Source == "own"
				&& column.Type == "Integer",
			because: "create-lookup should still allow explicit custom columns beyond the inherited BaseLookup fields");
		registrationSnapshot.LookupRowCount.Should().Be(1,
			because: "create-lookup should register the schema exactly once in the Lookup entity");
		registrationSnapshot.LookupRowTitle.Should().Be("Order status",
			because: "the Lookup registration row should reuse the requested business caption");
		registrationSnapshot.BindingCount.Should().Be(1,
			because: "create-lookup should create exactly one canonical package schema data binding");
		registrationSnapshot.BindingEntitySchemaName.Should().Be("Lookup",
			because: "the package schema data binding should target the Lookup entity");
		registrationSnapshot.BoundRecordIds.Should().Equal([registrationSnapshot.LookupRowId!],
			because: "the canonical Lookup binding should point only to the created registration row");
	}

	[Test]
	[Description("Adds Binary, Image, and File columns through update-entity-schema and verifies friendly type names through structured readback.")]
	[AllureTag(CreateToolName)]
	[AllureTag(UpdateToolName)]
	[AllureTag(ReadSchemaToolName)]
	[AllureTag(ReadColumnToolName)]
	[AllureName("Update entity schema MCP tool adds binary-like columns with friendly readback types")]
	[AllureDescription("Creates a sandbox entity schema, applies a batch update that adds Binary, Image, and File columns through the real MCP server, and verifies both schema and column readback use normalized friendly type names.")]
	public async Task UpdateEntitySchema_Should_Add_BinaryLike_Columns_And_Read_Back_Friendly_Types() {
		// Arrange
		await using EntitySchemaArrangeContext arrangeContext = await ArrangeSandboxPackageAsync();
		const string binaryColumnName = "Payload";
		const string imageColumnName = "Preview";
		const string fileColumnName = "Document";

		// Act
		CommandExecutionEnvelope createResult = await ActCreateEntitySchemaAsync(arrangeContext);
		CommandExecutionEnvelope updateResult = await ActBatchAddBinaryLikeColumnsAsync(arrangeContext, binaryColumnName, imageColumnName, fileColumnName);
		EntitySchemaPropertiesInfo schemaProperties = await ActGetSchemaPropertiesAsync(arrangeContext);
		EntitySchemaColumnPropertiesInfo binaryColumnProperties = await ActGetColumnPropertiesAsync(arrangeContext, binaryColumnName);
		EntitySchemaColumnPropertiesInfo imageColumnProperties = await ActGetColumnPropertiesAsync(arrangeContext, imageColumnName);
		EntitySchemaColumnPropertiesInfo fileColumnProperties = await ActGetColumnPropertiesAsync(arrangeContext, fileColumnName);

		// Assert
		AssertCommandSucceeded(createResult,
			"the schema must exist before the batch update can add binary-like columns");
		AssertIncludesInfoMessage(createResult,
			"successful schema creation should emit progress output before the batch update");
		AssertCommandSucceeded(updateResult,
			"update-entity-schema should succeed when adding supported binary-like column types");
		AssertIncludesInfoMessage(updateResult,
			"successful batch updates should emit progress output");
		AssertSchemaPropertiesIncludeBinaryLikeColumns(schemaProperties, binaryColumnName, imageColumnName, fileColumnName);
		AssertBinaryLikeColumnProperties(binaryColumnProperties, binaryColumnName, "Binary");
		AssertBinaryLikeColumnProperties(imageColumnProperties, imageColumnName, "Image");
		AssertBinaryLikeColumnProperties(fileColumnProperties, fileColumnName, "File");
	}

	[Test]
	[Description("Rejects inherited BaseLookup columns before environment resolution when create-lookup tries to redefine Name.")]
	[AllureTag(CreateLookupToolName)]
	[AllureName("Create lookup rejects inherited BaseLookup columns")]
	[AllureDescription("Uses the real MCP server to call create-lookup with a Name column and verifies the tool returns a structured validation failure before any environment lookup is needed.")]
	public async Task CreateLookup_Should_Reject_Inherited_BaseLookup_Columns() {
		// Arrange
		await using InvalidEnvironmentArrangeContext arrangeContext = await ArrangeInvalidEnvironmentAsync();

		// Act
		CallToolResult callResult = await CallCreateLookupAsync(
			arrangeContext.Session,
			arrangeContext.EnvironmentName,
			"UsrPkg",
			"UsrInvalidLookup",
			arrangeContext.CancellationTokenSource.Token,
			columns: [
				new Dictionary<string, object?> {
					["name"] = "Name",
					["type"] = "Text",
					["title"] = "Lookup name"
				}
			]);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);
		string environmentName = arrangeContext.EnvironmentName;

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "create-lookup should surface inherited-column validation as a structured command failure");
		execution.ExitCode.Should().Be(1,
			because: "create-lookup should fail when callers try to redefine inherited BaseLookup columns");
		execution.Output.Should().Contain(message =>
				message.Value != null && message.Value.ToString().Contains("BaseLookup", StringComparison.Ordinal),
			because: "the failure should explain that Name already comes from BaseLookup");
		execution.Output.Should().Contain(message =>
				message.Value != null && message.Value.ToString().Contains("Name", StringComparison.Ordinal),
			because: "the failure should identify the rejected inherited column");
		execution.Output.Should().NotContain(message =>
				message.Value != null && message.Value.ToString().Contains(environmentName, StringComparison.Ordinal),
			because: "validation should happen before environment resolution");
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

	[Test]
	[Description("Reports a readable failure when create-lookup is invoked with an unknown environment name.")]
	[AllureTag(CreateLookupToolName)]
	[AllureName("Create lookup reports invalid environment failures")]
	[AllureDescription("Uses the real MCP server to call create-lookup with a guaranteed-missing environment name and verifies the failure is surfaced to the caller.")]
	public async Task CreateLookup_Should_Report_Invalid_Environment() {
		// Arrange
		await using InvalidEnvironmentArrangeContext arrangeContext = await ArrangeInvalidEnvironmentAsync();

		// Act
		CallToolResult callResult = await CallCreateLookupAsync(
			arrangeContext.Session,
			arrangeContext.EnvironmentName,
			"UsrPkg",
			"UsrBadLookup",
			arrangeContext.CancellationTokenSource.Token);

		// Assert
		AssertTopLevelFailure(callResult, arrangeContext.EnvironmentName,
			"unknown environment names should fail before remote lookup creation starts");
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
		string lookupColumnName = "UsrSortOrder";
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
			rootDirectory,
			settings.Sandbox.EnvironmentName!,
			packageName,
			schemaName,
			initialColumnName,
			lookupColumnName,
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
	private static async Task<CommandExecutionEnvelope> ActAddEntitySchemaColumnAsync(
		EntitySchemaArrangeContext arrangeContext) {
		CallToolResult callResult = await CallModifyEntitySchemaColumnAsync(
			arrangeContext.Session,
			arrangeContext.EnvironmentName,
			arrangeContext.PackageName,
			arrangeContext.SchemaName,
			"add",
			arrangeContext.AddedColumnName,
			arrangeContext.CancellationTokenSource.Token,
			type: "ShortText",
			title: "Vehicle code",
			indexed: true,
			defaultValueSource: "Const",
			defaultValue: "Draft");
		return McpCommandExecutionParser.Extract(callResult);
	}

	[AllureStep("Act by invoking create-lookup through MCP")]
	private static async Task<CommandExecutionEnvelope> ActCreateLookupAsync(EntitySchemaArrangeContext arrangeContext) {
		CallToolResult callResult = await CallCreateLookupAsync(
			arrangeContext.Session,
			arrangeContext.EnvironmentName,
			arrangeContext.PackageName,
			arrangeContext.SchemaName,
			arrangeContext.CancellationTokenSource.Token,
			columns: [
				new Dictionary<string, object?> {
					["name"] = arrangeContext.LookupColumnName,
					["type"] = "Integer",
					["title"] = "Sort order"
				}
			]);
		return McpCommandExecutionParser.Extract(callResult);
	}

	[AllureStep("Act by invoking modify-entity-schema-column through MCP for modify")]
	private static async Task<CommandExecutionEnvelope> ActModifyEntitySchemaColumnAsync(
		EntitySchemaArrangeContext arrangeContext) {
		CallToolResult callResult = await CallModifyEntitySchemaColumnAsync(
			arrangeContext.Session,
			arrangeContext.EnvironmentName,
			arrangeContext.PackageName,
			arrangeContext.SchemaName,
			"modify",
			arrangeContext.AddedColumnName,
			arrangeContext.CancellationTokenSource.Token,
			title: "Vehicle code updated",
			defaultValueSource: "None");
		return McpCommandExecutionParser.Extract(callResult);
	}

	[AllureStep("Act by invoking modify-entity-schema-column through MCP for remove")]
	private static async Task<CommandExecutionEnvelope> ActRemoveEntitySchemaColumnAsync(
		EntitySchemaArrangeContext arrangeContext) {
		CallToolResult callResult = await CallModifyEntitySchemaColumnAsync(
			arrangeContext.Session,
			arrangeContext.EnvironmentName,
			arrangeContext.PackageName,
			arrangeContext.SchemaName,
			"remove",
			arrangeContext.AddedColumnName,
			arrangeContext.CancellationTokenSource.Token);
		return McpCommandExecutionParser.Extract(callResult);
	}

	[AllureStep("Act by invoking get-entity-schema-column-properties through MCP")]
	private static async Task<EntitySchemaColumnPropertiesInfo> ActGetColumnPropertiesAsync(
		EntitySchemaArrangeContext arrangeContext,
		string? columnName = null) {
		CallToolResult callResult = await CallGetColumnPropertiesAsync(
			arrangeContext.Session,
			arrangeContext.EnvironmentName,
			arrangeContext.PackageName,
			arrangeContext.SchemaName,
			columnName ?? arrangeContext.AddedColumnName,
			arrangeContext.CancellationTokenSource.Token);
		return EntitySchemaStructuredResultParser.Extract<EntitySchemaColumnPropertiesInfo>(callResult);
	}

	[AllureStep("Act by invoking update-entity-schema through MCP for binary-like columns")]
	private static async Task<CommandExecutionEnvelope> ActBatchAddBinaryLikeColumnsAsync(
		EntitySchemaArrangeContext arrangeContext,
		string binaryColumnName,
		string imageColumnName,
		string fileColumnName) {
		CallToolResult callResult = await CallUpdateEntitySchemaAsync(
			arrangeContext.Session,
			arrangeContext.EnvironmentName,
			arrangeContext.PackageName,
			arrangeContext.SchemaName,
			arrangeContext.CancellationTokenSource.Token,
			[
				new Dictionary<string, object?> {
					["action"] = "add",
					["column-name"] = binaryColumnName,
					["type"] = "Binary",
					["title"] = "Payload"
				},
				new Dictionary<string, object?> {
					["action"] = "add",
					["column-name"] = imageColumnName,
					["type"] = "Image",
					["title"] = "Preview"
				},
				new Dictionary<string, object?> {
					["action"] = "add",
					["column-name"] = fileColumnName,
					["type"] = "File",
					["title"] = "Document"
				}
			]);
		return McpCommandExecutionParser.Extract(callResult);
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

	private static async Task<CallToolResult> CallCreateLookupAsync(
		McpServerSession session,
		string environmentName,
		string packageName,
		string schemaName,
		CancellationToken cancellationToken,
		IReadOnlyList<Dictionary<string, object?>>? columns = null) {
		IList<McpClientTool> tools = await session.ListToolsAsync(cancellationToken);
		tools.Select(tool => tool.Name).Should().Contain(CreateLookupToolName,
			because: "the create-lookup MCP tool must be advertised before the end-to-end call can be executed");

		return await session.CallToolAsync(
			CreateLookupToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["package-name"] = packageName,
					["schema-name"] = schemaName,
					["title"] = "Order status",
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
		bool? indexed = null,
		string? defaultValueSource = null,
		string? defaultValue = null) {
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
		if (!string.IsNullOrWhiteSpace(defaultValueSource)) {
			args["default-value-source"] = defaultValueSource;
		}
		if (defaultValue is not null) {
			args["default-value"] = defaultValue;
		}

		return await session.CallToolAsync(
			ModifyToolName,
			new Dictionary<string, object?> { ["args"] = args },
			cancellationToken);
	}

	private static async Task<CallToolResult> CallUpdateEntitySchemaAsync(
		McpServerSession session,
		string environmentName,
		string packageName,
		string schemaName,
		CancellationToken cancellationToken,
		IReadOnlyList<Dictionary<string, object?>> operations) {
		IList<McpClientTool> tools = await session.ListToolsAsync(cancellationToken);
		tools.Select(tool => tool.Name).Should().Contain(UpdateToolName,
			because: "the update-entity-schema MCP tool must be advertised before the end-to-end call can be executed");

		return await session.CallToolAsync(
			UpdateToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["package-name"] = packageName,
					["schema-name"] = schemaName,
					["operations"] = operations
				}
			},
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
		properties.Columns.Should().NotBeNullOrEmpty(
			because: "the structured schema readback should expose nested columns for direct verification");
		properties.Columns!.Should().Contain(column =>
			column.Name == arrangeContext.InitialColumnName
			&& column.Source == "own"
			&& column.Title == "Vehicle name",
			because: "the created schema should surface the initially created own column in data.columns");
	}

	[AllureStep("Assert schema properties after add")]
	private static void AssertSchemaPropertiesAfterAdd(
		EntitySchemaPropertiesInfo properties,
		EntitySchemaArrangeContext arrangeContext) {
		properties.Columns.Should().NotBeNull(
			because: "schema readback after add should still include nested columns");
		EntitySchemaPropertyColumnInfo addedColumn = properties.Columns!.Single(column =>
			column.Name == arrangeContext.AddedColumnName);
		addedColumn.Source.Should().Be("own",
			because: "columns added through modify-entity-schema-column should be reported as own");
		addedColumn.Type.Should().Be("ShortText",
			because: "the schema readback should preserve the added frontend-compatible column type alias");
		addedColumn.Indexed.Should().BeTrue(
			because: "schema readback should include indexed state for added columns");
	}

	[AllureStep("Assert schema properties after modify")]
	private static void AssertSchemaPropertiesAfterModify(
		EntitySchemaPropertiesInfo properties,
		EntitySchemaArrangeContext arrangeContext) {
		properties.Columns.Should().NotBeNull(
			because: "schema readback after modify should still include nested columns");
		EntitySchemaPropertyColumnInfo modifiedColumn = properties.Columns!.Single(column =>
			column.Name == arrangeContext.AddedColumnName);
		modifiedColumn.Title.Should().Be("Vehicle code updated",
			because: "schema readback should reflect updated column metadata after modify");
		modifiedColumn.Source.Should().Be("own",
			because: "the modified column should remain an own column after update");
	}

	[AllureStep("Assert structured column properties")]
	private static void AssertAddedColumnProperties(EntitySchemaColumnPropertiesInfo properties, EntitySchemaArrangeContext arrangeContext) {
		properties.SchemaName.Should().Be(arrangeContext.SchemaName,
			because: "the structured result should identify the mutated schema");
		properties.ColumnName.Should().Be(arrangeContext.AddedColumnName,
			because: "the added column should be readable through the structured column properties tool");
		properties.Source.Should().Be("own",
			because: "columns added through modify-entity-schema-column should be reported as own columns");
		properties.Type.Should().Be("ShortText",
			because: "the structured result should preserve the added frontend-compatible column type alias");
		properties.Title.Should().Be("Vehicle code",
			because: "the structured result should preserve the added column title");
		properties.Indexed.Should().BeTrue(
			because: "the structured result should preserve boolean mutation flags");
		properties.DefaultValueSource.Should().Be("Const",
			because: "the structured result should preserve explicit default-value-source metadata");
		properties.DefaultValue.Should().Be("Draft",
			because: "the structured result should preserve the configured default value");
	}

	[AllureStep("Assert structured modified column properties")]
	private static void AssertModifiedColumnProperties(EntitySchemaColumnPropertiesInfo properties, EntitySchemaArrangeContext arrangeContext) {
		properties.SchemaName.Should().Be(arrangeContext.SchemaName,
			because: "the structured result should still identify the mutated schema after modify");
		properties.ColumnName.Should().Be(arrangeContext.AddedColumnName,
			because: "the modified column should still be addressable by the same name");
		properties.Title.Should().Be("Vehicle code updated",
			because: "the structured result should preserve the updated column title");
		properties.DefaultValueSource.Should().BeNull(
			because: "clearing a default should be reflected in the structured result");
		properties.DefaultValue.Should().BeNull(
			because: "clearing a default should remove the stored value from the structured result");
	}

	[AllureStep("Assert schema properties include Binary, Image, and File columns")]
	private static void AssertSchemaPropertiesIncludeBinaryLikeColumns(
		EntitySchemaPropertiesInfo properties,
		string binaryColumnName,
		string imageColumnName,
		string fileColumnName) {
		properties.Columns.Should().NotBeNullOrEmpty(
			because: "schema readback after a batch update should expose nested columns for direct verification");
		properties.Columns!.Should().Contain(column => column.Name == binaryColumnName && column.Type == "Binary",
			because: "batch-added Binary columns should be reported with a normalized friendly type name");
		properties.Columns!.Should().Contain(column => column.Name == imageColumnName && column.Type == "Image",
			because: "batch-added Image columns should be reported with a normalized friendly type name");
		properties.Columns!.Should().Contain(column => column.Name == fileColumnName && column.Type == "File",
			because: "batch-added File columns should be reported with a normalized friendly type name");
	}

	[AllureStep("Assert structured binary-like column properties")]
	private static void AssertBinaryLikeColumnProperties(
		EntitySchemaColumnPropertiesInfo properties,
		string columnName,
		string expectedTypeName) {
		properties.ColumnName.Should().Be(columnName,
			because: "the structured column readback should identify the requested binary-like column");
		properties.Source.Should().Be("own",
			because: "columns added through batch update should be reported as own columns");
		properties.Type.Should().Be(expectedTypeName,
			because: "structured column readback should normalize binary-like type names into stable friendly values");
	}

	[AllureStep("Assert schema properties after remove")]
	private static void AssertSchemaPropertiesAfterRemove(
		EntitySchemaPropertiesInfo propertiesBeforeAdd,
		EntitySchemaPropertiesInfo propertiesAfterRemove,
		EntitySchemaArrangeContext arrangeContext) {
		propertiesAfterRemove.Name.Should().Be(arrangeContext.SchemaName,
			because: "the schema should remain readable after removing the added column");
		propertiesAfterRemove.OwnColumnCount.Should().Be(propertiesBeforeAdd.OwnColumnCount,
			because: "removing the added column should restore the original own column count");
		propertiesAfterRemove.Columns.Should().NotContain(column => column.Name == arrangeContext.AddedColumnName,
			because: "removed columns should disappear from the schema readback columns list");
	}

	[AllureStep("Assert top-level MCP failure is readable")]
	private static void AssertTopLevelFailure(CallToolResult callResult, string environmentName, string because) {
		callResult.IsError.Should().NotBeFalse(because: because);
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
			["push-workspace", "-e", environmentName],
			workingDirectory: workspacePath,
			cancellationToken: cancellationToken);
	}

	private sealed record EntitySchemaArrangeContext(
		string RootDirectory,
		string EnvironmentName,
		string PackageName,
		string SchemaName,
		string InitialColumnName,
		string LookupColumnName,
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
