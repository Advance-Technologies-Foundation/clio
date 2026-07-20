using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using ConsoleTables;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class SchemaSyncToolTests {

	[Test]
	[Category("Unit")]
	[Description("Advertises a stable MCP tool name for sync-schemas")]
	public async Task SchemaSyncTool_Should_Advertise_Stable_Tool_Name() {
		// Arrange

		// Act
		string toolName = SchemaSyncTool.ToolName;

		// Assert
		toolName.Should().Be("sync-schemas",
			because: "the sync-schemas MCP tool identifier must remain stable for callers");
	}

	[Test]
	[Category("Unit")]
	[Description("Marks sync-schemas as destructive and not read-only")]
	public async Task SchemaSyncTool_Should_Advertise_Safety_Metadata() {
		// Arrange
		var method = typeof(SchemaSyncTool).GetMethod(nameof(SchemaSyncTool.SchemaSync))!;
		var attribute = method
			.GetCustomAttributes(typeof(ModelContextProtocol.Server.McpServerToolAttribute), false)
			.Cast<ModelContextProtocol.Server.McpServerToolAttribute>()
			.Single();

		// Act
		bool readOnly = attribute.ReadOnly;
		bool destructive = attribute.Destructive;

		// Assert
		readOnly.Should().BeFalse(
			because: "sync-schemas mutates remote schemas and should not be marked read-only");
		destructive.Should().BeTrue(
			because: "sync-schemas creates and modifies remote schemas and should warn clients");
	}

	[Test]
	[Category("Unit")]
	[Description("Routes batched virtual entity creation callers to the canonical virtual-entities guidance at the decision point.")]
	public void SchemaSyncTool_ShouldRouteToVirtualEntitiesGuidance_WhenVirtualSchemaIsConsidered() {
		// Arrange
		System.Reflection.MethodInfo method = typeof(SchemaSyncTool)
			.GetMethod(nameof(SchemaSyncTool.SchemaSync))!;

		// Act
		string description = method
			.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
			.Cast<System.ComponentModel.DescriptionAttribute>()
			.Single().Description;

		// Assert
		description.Should().Contain("get-guidance with name virtual-entities",
			because: "batched is-virtual operations must expose the canonical lifecycle and safety guide");
	}

	[Test]
	[Category("Unit")]
	[Description("Routes create-lookup operation through CreateEntitySchemaCommand with BaseLookup parent")]
	public async Task SchemaSync_CreateLookup_Should_Route_Through_CreateEntitySchemaCommand() {
		// Arrange
		var fakeCreateCommand = new FakeCreateEntitySchemaCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		ILookupRegistrationService registrationService = Substitute.For<ILookupRegistrationService>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(fakeCreateCommand);
		commandResolver.Resolve<ILookupRegistrationService>(Arg.Any<EnvironmentOptions>())
			.Returns(registrationService);
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance, Convergence());
		SchemaSyncArgs args = new(
			"dev", "UsrPkg",
			[new SchemaSyncOperation("create-lookup", "UsrTodoStatus", TitleLocalizations: Localizations("Todo Status"))]);

		// Act
		SchemaSyncResponse response = await tool.SchemaSync(args);

		// Assert
		response.Success.Should().BeTrue(
			because: "a create-lookup with exit code 0 should succeed");
		response.Results.Should().HaveCount(1,
			because: "one create-lookup operation was requested");
		response.Results[0].Type.Should().Be("create-lookup",
			because: "sync-schemas results should expose the canonical type field");
		fakeCreateCommand.CapturedOptions.Should().NotBeNull(
			because: "the tool should forward the operation to CreateEntitySchemaCommand");
		fakeCreateCommand.CapturedOptions!.Package.Should().Be("UsrPkg",
			because: "the package name should be forwarded from the batch args");
		fakeCreateCommand.CapturedOptions.SchemaName.Should().Be("UsrTodoStatus",
			because: "the schema name should come from the operation");
		fakeCreateCommand.CapturedOptions.ParentSchemaName.Should().Be("BaseLookup",
			because: "create-lookup must always inherit from BaseLookup");
		fakeCreateCommand.CapturedOptions.Environment.Should().Be("dev",
			because: "the environment should be forwarded from the batch args");
		fakeCreateCommand.CapturedOptions.IsVirtual.Should().BeFalse(
			because: "lookup schemas remain persistent even if the virtual property is absent");
		registrationService.Received(1).EnsureLookupRegistration("UsrPkg", "UsrTodoStatus", "Todo Status");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects inherited BaseLookup columns inside sync-schemas create-lookup operations before any command executes.")]
	public async Task SchemaSync_CreateLookup_Should_Reject_Inherited_BaseLookup_Columns() {
		// Arrange
		var fakeCreateCommand = new FakeCreateEntitySchemaCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(fakeCreateCommand);
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance, Convergence());
		SchemaSyncArgs args = new(
			"dev", "UsrPkg",
			[
				new SchemaSyncOperation(
					"create-lookup",
					"UsrTodoStatus",
					TitleLocalizations: Localizations("Todo Status"),
					Columns: [
						new CreateEntitySchemaColumnArgs("Name", "Text", Localizations("Name"))
					])
			]);

		// Act
		SchemaSyncResponse response = await tool.SchemaSync(args);

		// Assert
		response.Success.Should().BeFalse(
			because: "sync-schemas should fail fast when a create-lookup operation tries to redefine inherited BaseLookup columns");
		response.Results.Should().HaveCount(1,
			because: "validation should stop the batch on the rejected create-lookup operation");
		response.Results[0].Type.Should().Be("create-lookup",
			because: "validation failures should still report the canonical type field");
		response.Results[0].Error.Should().Contain("BaseLookup",
			because: "the failure should explain the inherited-column guardrail");
		response.Results[0].Error.Should().Contain("Name",
			because: "the failure should identify the rejected inherited column");
		fakeCreateCommand.CapturedOptions.Should().BeNull(
			because: "sync-schemas should not execute the create command after validation fails");
	}

	[Test]
	[Category("Unit")]
	[Description("Routes create-entity operation with custom parent schema")]
	public async Task SchemaSync_CreateEntity_Should_Use_Custom_Parent_Schema() {
		// Arrange
		var fakeCreateCommand = new FakeCreateEntitySchemaCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(fakeCreateCommand);
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance, Convergence());
		SchemaSyncArgs args = new(
			"dev", "UsrPkg",
			[new SchemaSyncOperation("create-entity", "UsrTodoList",
				TitleLocalizations: Localizations("Todo List"), ParentSchemaName: "BaseEntity") { IsVirtual = true }]);

		// Act
		SchemaSyncResponse response = await tool.SchemaSync(args);

		// Assert
		response.Success.Should().BeTrue(
			because: "a create-entity with exit code 0 should succeed");
		fakeCreateCommand.CapturedOptions!.ParentSchemaName.Should().Be("BaseEntity",
			because: "create-entity should use the specified parent schema");
		fakeCreateCommand.CapturedOptions.IsVirtual.Should().BeTrue(
			because: "create-entity should preserve the explicit virtual-schema request");
	}

	[Test]
	[Category("Unit")]
	[Description("Routes update-entity operation through UpdateEntitySchemaCommand")]
	public async Task SchemaSync_UpdateEntity_Should_Route_Through_UpdateEntitySchemaCommand() {
		// Arrange
		var fakeUpdateCommand = new FakeUpdateEntitySchemaCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<UpdateEntitySchemaCommand>(Arg.Any<UpdateEntitySchemaOptions>())
			.Returns(fakeUpdateCommand);
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance, Convergence());
		SchemaSyncArgs args = new(
			"dev", "UsrPkg",
			[new SchemaSyncOperation("update-entity", "UsrTodoList",
				UpdateOperations: [
					new UpdateEntitySchemaOperationArgs("add", "UsrStatus",
						Type: "Lookup", TitleLocalizations: Localizations("Status"), ReferenceSchemaName: "UsrTodoStatus"),
					new UpdateEntitySchemaOperationArgs("add", "UsrDueDate",
						Type: "Date", TitleLocalizations: Localizations("Due date"))
				])]);

		// Act
		SchemaSyncResponse response = await tool.SchemaSync(args);

		// Assert
		response.Success.Should().BeTrue(
			because: "an update-entity with exit code 0 should succeed");
		response.Results.Should().HaveCount(1,
			because: "one update-entity operation was requested");
		fakeUpdateCommand.CapturedOptions.Should().NotBeNull(
			because: "the tool should forward the operation to UpdateEntitySchemaCommand");
		fakeUpdateCommand.CapturedOptions!.Package.Should().Be("UsrPkg",
			because: "the package name should be forwarded");
		fakeUpdateCommand.CapturedOptions.SchemaName.Should().Be("UsrTodoList",
			because: "the schema name should come from the operation");
		fakeUpdateCommand.CapturedOptions.Operations.Should().HaveCount(2,
			because: "both column operations should be serialized");
	}

	[Test]
	[Category("Unit")]
	[Description("Executes seed-data after a successful create-lookup with seed-rows")]
	public async Task SchemaSync_SeedRows_Should_Execute_After_Create() {
		// Arrange
		var fakeCreateCommand = new FakeCreateEntitySchemaCommand();
		var fakeSeedCommand = new FakeCreateDataBindingDbCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		ILookupRegistrationService registrationService = Substitute.For<ILookupRegistrationService>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(fakeCreateCommand);
		commandResolver.Resolve<CreateDataBindingDbCommand>(Arg.Any<CreateDataBindingDbOptions>())
			.Returns(fakeSeedCommand);
		commandResolver.Resolve<ILookupRegistrationService>(Arg.Any<EnvironmentOptions>())
			.Returns(registrationService);
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance, Convergence());
		SchemaSyncArgs args = new(
			"dev", "UsrPkg",
			[new SchemaSyncOperation("create-lookup", "UsrTodoStatus",
				TitleLocalizations: Localizations("Todo Status"),
				SeedRows: [
					new SchemaSyncSeedRow(new Dictionary<string, System.Text.Json.JsonElement> {
						["Name"] = ToJsonElement("New")
					}),
					new SchemaSyncSeedRow(new Dictionary<string, System.Text.Json.JsonElement> {
						["Name"] = ToJsonElement("Done")
					})
				])]);

		// Act
		SchemaSyncResponse response = await tool.SchemaSync(args);

		// Assert
		response.Success.Should().BeTrue(
			because: "both create-lookup and seed-data should succeed");
		response.Results.Should().HaveCount(2,
			because: "a create-lookup and its seed-data produce two results");
		response.Results[0].Type.Should().Be("create-lookup",
			because: "create results should expose the canonical type field");
		response.Results[1].Type.Should().Be("seed-data",
			because: "seed results should expose the canonical type field");
		fakeSeedCommand.CapturedOptions.Should().NotBeNull(
			because: "seed-data should be executed after create");
		fakeSeedCommand.CapturedOptions!.SchemaName.Should().Be("UsrTodoStatus",
			because: "seed-data should target the same schema as the create operation");
		fakeSeedCommand.CapturedOptions.RowsJson.Should().Contain("New",
			because: "the seed rows should be serialized to JSON");
		registrationService.Received(1).EnsureLookupRegistration("UsrPkg", "UsrTodoStatus", "Todo Status");
	}

	[Test]
	[Category("Unit")]
	[Description("Stops processing on first operation failure")]
	public async Task SchemaSync_Should_Stop_On_First_Failure() {
		// Arrange
		var failingCommand = new FakeCreateEntitySchemaCommand(exitCode: 1);
		var secondCommand = new FakeCreateEntitySchemaCommand();
		int resolveCount = 0;
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(_ => resolveCount++ == 0 ? failingCommand : secondCommand);
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance, Convergence());
		SchemaSyncArgs args = new(
			"dev", "UsrPkg",
			[
				new SchemaSyncOperation("create-lookup", "UsrFirst", TitleLocalizations: Localizations("First")),
				new SchemaSyncOperation("create-lookup", "UsrSecond", TitleLocalizations: Localizations("Second"))
			]);

		// Act
		SchemaSyncResponse response = await tool.SchemaSync(args);

		// Assert
		response.Success.Should().BeFalse(
			because: "the first operation failed");
		response.Results.Should().HaveCount(1,
			because: "processing should stop after the first failure");
		response.Results[0].SchemaName.Should().Be("UsrFirst",
			because: "only the first operation result should be present");
		response.Results[0].Success.Should().BeFalse(
			because: "the first operation returned a non-zero exit code");
		secondCommand.CapturedOptions.Should().BeNull(
			because: "the second operation should not be executed after the first failure");
	}

	[Test]
	[Category("Unit")]
	[Description("Surfaces command error details in sync-schemas operation error when available")]
	public async Task SchemaSync_Should_Include_Detailed_Command_Error_When_Present() {
		// Arrange
		TestLogger logger = new();
		var failingCommand = new FakeCreateEntitySchemaCommand(
			logger,
			exitCode: 1,
			messages: ["Schema 'UsrFirst' already exists."]);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(failingCommand);
		SchemaSyncTool tool = new(commandResolver, logger, Convergence());
		SchemaSyncArgs args = new(
			"dev", "UsrPkg",
			[new SchemaSyncOperation("create-lookup", "UsrFirst", TitleLocalizations: Localizations("First"))]);

		// Act
		SchemaSyncResponse response = await tool.SchemaSync(args);

		// Assert
		response.Success.Should().BeFalse(
			because: "the underlying command returned a non-zero exit code");
		response.Results.Should().HaveCount(1,
			because: "processing should stop after the first failed operation");
		response.Results[0].Error.Should().Contain("Schema 'UsrFirst' already exists.",
			because: "the sync-schemas error should include the command-level error details");
	}

	[Test]
	[Category("Unit")]
	[Description("Surfaces the cross-package collision pre-emptively without ever attempting the create when the convergence classifier reports a different owning package.")]
	public async Task SchemaSync_CreateLookup_Should_Include_CollisionInfo_When_Schema_Exists_In_Different_Package() {
		// Arrange
		TestLogger logger = new();
		var fakeCreateCommand = new FakeCreateEntitySchemaCommand(logger);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(fakeCreateCommand);
		ISchemaConvergenceService convergence = Convergence(
			SchemaConvergenceOutcome.Collision,
			collisionPackageName: "OtherPackage",
			error: "Error: schema 'UsrFirst' already exists in package 'OtherPackage'.");
		SchemaSyncTool tool = new(commandResolver, logger, convergence);
		SchemaSyncArgs args = new(
			"dev", "UsrPkg",
			[new SchemaSyncOperation("create-lookup", "UsrFirst", TitleLocalizations: Localizations("First"))]);

		// Act
		SchemaSyncResponse response = await tool.SchemaSync(args);

		// Assert
		response.Results[0].Success.Should().BeFalse(
			because: "a cross-package name collision is a durable failure, not a success");
		response.Results[0].Outcome.Should().Be("collision",
			because: "the collision outcome discriminator must be surfaced to callers");
		response.Results[0].CollisionInfo.Should().NotBeNull(
			because: "the classifier found the schema in a different package");
		response.Results[0].CollisionInfo!.ExistingPackageName.Should().Be("OtherPackage",
			because: "the collision info should name the package that owns the stale schema");
		fakeCreateCommand.CapturedOptions.Should().BeNull(
			because: "the collision must be detected pre-emptively — create is never attempted");
	}

	[Test]
	[Category("Unit")]
	[Description("Omits collision-info and reports the created outcome when the convergence classifier reports the schema is absent.")]
	public async Task SchemaSync_CreateLookup_Should_Not_Include_CollisionInfo_When_Schema_Not_Found() {
		// Arrange
		TestLogger logger = new();
		var fakeCreateCommand = new FakeCreateEntitySchemaCommand(logger);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		ILookupRegistrationService registrationService = Substitute.For<ILookupRegistrationService>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(fakeCreateCommand);
		commandResolver.Resolve<ILookupRegistrationService>(Arg.Any<EnvironmentOptions>())
			.Returns(registrationService);
		SchemaSyncTool tool = new(commandResolver, logger, Convergence(SchemaConvergenceOutcome.Create));
		SchemaSyncArgs args = new(
			"dev", "UsrPkg",
			[new SchemaSyncOperation("create-lookup", "UsrFirst", TitleLocalizations: Localizations("First"))]);

		// Act
		SchemaSyncResponse response = await tool.SchemaSync(args);

		// Assert
		response.Results[0].CollisionInfo.Should().BeNull(
			because: "no existing schema was found, so there is no collision to report");
		response.Results[0].Outcome.Should().Be("created",
			because: "an absent schema that is created must report the created outcome");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns error for unknown operation type without stopping")]
	public async Task SchemaSync_Unknown_OperationType_Should_Return_Error() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance, Convergence());
		SchemaSyncArgs args = new(
			"dev", "UsrPkg",
			[new SchemaSyncOperation("delete-schema", "UsrOops")]);

		// Act
		SchemaSyncResponse response = await tool.SchemaSync(args);

		// Assert
		response.Success.Should().BeFalse(
			because: "an unknown operation type should fail");
		response.Results.Should().HaveCount(1,
			because: "one operation was attempted");
		response.Results[0].Error.Should().Contain("operations[0].type",
			because: "the error should identify the failing operation slot and field name");
		response.Results[0].Error.Should().Contain("delete-schema",
			because: "the error should describe the unsupported type value");
		response.Results[0].Error.Should().Contain("Supported values",
			because: "the error should list the accepted sync-schemas operation types");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects the legacy operation field name with a targeted sync-schemas validation message.")]
	public async Task SchemaSync_Should_Reject_Legacy_Operation_Field_Name() {
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance, Convergence());
		SchemaSyncArgs args = new(
			"dev",
			"UsrPkg",
			[
				new SchemaSyncOperation(
					null!,
					"UsrTodoStatus",
					TitleLocalizations: Localizations("Todo Status")) {
					ExtensionData = new Dictionary<string, System.Text.Json.JsonElement> {
						["operation"] = ToJsonElement("create-lookup")
					}
				}
			]);

		SchemaSyncResponse response = await tool.SchemaSync(args);

		response.Success.Should().BeFalse(
			because: "legacy request field names should fail before any schema command executes");
		response.Results.Should().HaveCount(1,
			because: "the rejected request should produce one targeted validation result");
		response.Results[0].Type.Should().Be("create-lookup",
			because: "the validation result should preserve the reported legacy operation name in the canonical type field");
		response.Results[0].Error.Should().Contain("unsupported request field 'operation'",
			because: "the error should explain the legacy field-name mistake directly");
		response.Results[0].Error.Should().Contain("Send 'type': 'create-lookup' instead",
			because: "the error should point callers to the exact canonical replacement");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns error when update-entity has no update-operations")]
	public async Task SchemaSync_UpdateEntity_Without_Operations_Should_Fail() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance, Convergence());
		SchemaSyncArgs args = new(
			"dev", "UsrPkg",
			[new SchemaSyncOperation("update-entity", "UsrTodoList")]);

		// Act
		SchemaSyncResponse response = await tool.SchemaSync(args);

		// Assert
		response.Success.Should().BeFalse(
			because: "update-entity without operations should fail validation");
		response.Results[0].Error.Should().Contain("update-operations",
			because: "the error should indicate that update-operations are required");
	}

	[Test]
	[Category("Unit")]
	[Description("Enumerates the accepted update-entity shapes and read-shape aliases when neither update-operations nor columns are supplied (ENG-90313 AC4).")]
	public async Task SchemaSync_UpdateEntity_Without_Operations_Should_Enumerate_Accepted_Shapes() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance, Convergence());
		SchemaSyncArgs args = new(
			"dev", "UsrPkg",
			[new SchemaSyncOperation("update-entity", "UsrTodoList")]);

		// Act
		SchemaSyncResponse response = await tool.SchemaSync(args);

		// Assert
		string error = response.Results[0].Error!;
		error.Should().Contain("columns",
			because: "the rejection must tell the agent the implicit add-batch 'columns' shape is also accepted");
		error.Should().Contain("action",
			because: "the rejection must explain that update-operations require an add|modify|remove action verb");
		error.Should().Contain("name",
			because: "the rejection must list the read-shape 'name' alias for column-name");
		error.Should().Contain("data-value-type",
			because: "the rejection must list the read-shape 'data-value-type' alias for type");
		error.Should().Contain("get-app-info",
			because: "the rejection must point the agent at the read shape it can send back verbatim");
	}

	[Test]
	[Category("Unit")]
	[Description("Coerces an update-entity 'columns' payload (read/create shape, no action verbs) into an implicit add-batch (ENG-90313 Option A).")]
	public async Task SchemaSync_UpdateEntity_Should_Coerce_Columns_To_Add_Batch() {
		// Arrange
		var fakeUpdateCommand = new FakeUpdateEntitySchemaCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<UpdateEntitySchemaCommand>(Arg.Any<UpdateEntitySchemaOptions>())
			.Returns(fakeUpdateCommand);
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance, Convergence());
		SchemaSyncArgs args = new(
			"dev", "UsrPkg",
			[new SchemaSyncOperation("update-entity", "UsrTodoList",
				Columns: [
					new CreateEntitySchemaColumnArgs("UsrPriority", "Integer", Localizations("Priority")),
					new CreateEntitySchemaColumnArgs("UsrOwner", "Lookup", Localizations("Owner"),
						ReferenceSchemaName: "Contact") { Required = true }
				])]);

		// Act
		SchemaSyncResponse response = await tool.SchemaSync(args);

		// Assert
		response.Success.Should().BeTrue(
			because: "a columns-only update-entity should be accepted as an implicit add-batch");
		fakeUpdateCommand.CapturedOptions.Should().NotBeNull(
			because: "the coerced add-batch should be routed to UpdateEntitySchemaCommand");
		fakeUpdateCommand.CapturedOptions!.Operations.Should().HaveCount(2,
			because: "each column should become one add operation");
		fakeUpdateCommand.CapturedOptions.Operations.Should().OnlyContain(
			operation => operation.Contains("\"action\":\"add\"", StringComparison.Ordinal),
			because: "columns without action verbs must be coerced to add operations");
		fakeUpdateCommand.CapturedOptions.Operations.Should().Contain(
			operation => operation.Contains("\"column-name\":\"UsrPriority\"", StringComparison.Ordinal),
			because: "the column identity should be carried into the coerced add operation");
		fakeUpdateCommand.CapturedOptions.Operations.Should().Contain(
			operation => operation.Contains("\"reference-schema-name\":\"Contact\"", StringComparison.Ordinal),
			because: "lookup reference metadata should survive the coercion");
	}

	[Test]
	[Category("Unit")]
	[Description("Accepts the get-app-info read-shape aliases (name, data-value-type, reference-schema) on update-operations and resolves them to canonical fields (ENG-90313 AC1/AC2).")]
	public async Task SchemaSync_UpdateEntity_Should_Accept_Read_Shape_Aliases_On_Update_Operations() {
		// Arrange
		var fakeUpdateCommand = new FakeUpdateEntitySchemaCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<UpdateEntitySchemaCommand>(Arg.Any<UpdateEntitySchemaOptions>())
			.Returns(fakeUpdateCommand);
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance, Convergence());
		SchemaSyncArgs args = new(
			"dev", "UsrPkg",
			[new SchemaSyncOperation("update-entity", "UsrTodoList",
				UpdateOperations: [
					// modify echoes the read shape: identity as 'name', type as 'data-value-type'
					new UpdateEntitySchemaOperationArgs("modify", null!) {
						NameAlias = "UsrStatus",
						DataValueTypeAlias = "Lookup",
						ReferenceSchemaAlias = "UsrTodoStatus"
					},
					// remove echoes only the read-shape 'name'
					new UpdateEntitySchemaOperationArgs("remove", null!) { NameAlias = "UsrObsolete" }
				])]);

		// Act
		SchemaSyncResponse response = await tool.SchemaSync(args);

		// Assert
		response.Success.Should().BeTrue(
			because: "the read-shape aliases should be accepted without manual field translation");
		fakeUpdateCommand.CapturedOptions!.Operations.Should().Contain(
			operation => operation.Contains("\"column-name\":\"UsrStatus\"", StringComparison.Ordinal),
			because: "the 'name' read-shape alias must resolve to the canonical column-name");
		fakeUpdateCommand.CapturedOptions.Operations.Should().Contain(
			operation => operation.Contains("\"type\":\"Lookup\"", StringComparison.Ordinal),
			because: "the 'data-value-type' read-shape alias must resolve to the canonical type");
		fakeUpdateCommand.CapturedOptions.Operations.Should().Contain(
			operation => operation.Contains("\"reference-schema-name\":\"UsrTodoStatus\"", StringComparison.Ordinal),
			because: "the 'reference-schema' read-shape alias must resolve to the canonical reference-schema-name");
		fakeUpdateCommand.CapturedOptions.Operations.Should().Contain(
			operation => operation.Contains("\"column-name\":\"UsrObsolete\"", StringComparison.Ordinal),
			because: "a remove echoing only the read-shape 'name' must still resolve the target column");
	}

	[Test]
	[Category("Unit")]
	[Description("Accepts a legacy scalar title as an en-US fallback in a sync-schemas create operation when title-localizations is omitted.")]
	public async Task SchemaSync_CreateLookup_Should_Use_Legacy_Title_As_EnUs_Fallback() {
		// Arrange
		var fakeCreateCommand = new FakeCreateEntitySchemaCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(fakeCreateCommand);
		commandResolver.Resolve<ILookupRegistrationService>(Arg.Any<EnvironmentOptions>())
			.Returns(Substitute.For<ILookupRegistrationService>());
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance, Convergence());
		SchemaSyncArgs args = new(
			"dev",
			"UsrPkg",
			[
				new SchemaSyncOperation(
					"create-lookup",
					"UsrTodoStatus") {
					LegacyTitle = "Todo Status"
				}
			]);

		// Act
		SchemaSyncResponse response = await tool.SchemaSync(args);

		// Assert
		response.Success.Should().BeTrue(
			because: "a scalar title must be promoted to the en-US localization instead of hard-failing the create");
		fakeCreateCommand.CapturedOptions!.TitleLocalizations.Should().ContainKey("en-US",
			because: "the legacy scalar title is the en-US fallback when no localization map is supplied");
		fakeCreateCommand.CapturedOptions!.TitleLocalizations!["en-US"].Should().Be("Todo Status",
			because: "the derived en-US caption must be the scalar title value");
	}

	[Test]
	[Category("Unit")]
	[Description("Stops seed-data when create-lookup fails and does not seed")]
	public async Task SchemaSync_SeedRows_Should_Not_Execute_When_Create_Fails() {
		// Arrange
		var failingCreateCommand = new FakeCreateEntitySchemaCommand(exitCode: 1);
		var fakeSeedCommand = new FakeCreateDataBindingDbCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(failingCreateCommand);
		commandResolver.Resolve<CreateDataBindingDbCommand>(Arg.Any<CreateDataBindingDbOptions>())
			.Returns(fakeSeedCommand);
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance, Convergence());
		SchemaSyncArgs args = new(
			"dev", "UsrPkg",
			[new SchemaSyncOperation("create-lookup", "UsrBroken",
				TitleLocalizations: Localizations("Broken"),
				SeedRows: [new SchemaSyncSeedRow(new Dictionary<string, System.Text.Json.JsonElement> {
					["Name"] = ToJsonElement("value")
				})])]);

		// Act
		SchemaSyncResponse response = await tool.SchemaSync(args);

		// Assert
		response.Success.Should().BeFalse(
			because: "the create operation failed");
		response.Results.Should().HaveCount(1,
			because: "seed-data should be skipped when create fails");
		fakeSeedCommand.CapturedOptions.Should().BeNull(
			because: "seed-data should not be executed when the preceding create failed");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects malformed seed-rows before executing the schema operation command")]
	public async Task SchemaSync_SeedRows_Should_Fail_Before_Command_Resolution_When_Values_Are_Missing() {
		// Arrange
		var fakeCreateCommand = new FakeCreateEntitySchemaCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(fakeCreateCommand);
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance, Convergence());
		SchemaSyncArgs args = new(
			"missing-env", "UsrPkg",
			[
				new SchemaSyncOperation("create-lookup", "UsrTodoStatus",
					TitleLocalizations: Localizations("Todo Status"),
					SeedRows: [new SchemaSyncSeedRow(null!)])
			]);

		// Act
		SchemaSyncResponse response = await tool.SchemaSync(args);

		// Assert
		response.Success.Should().BeFalse(
			because: "malformed seed-rows must fail sync-schemas before any remote command is executed");
		response.Results.Should().ContainSingle(
			because: "the batch should stop immediately on the malformed seed-data payload");
		response.Results[0].Type.Should().Be("seed-data",
			because: "the failure should be attributed to the seed-data step");
		response.Results[0].Error.Should().Contain("values",
			because: "the caller must be told that each seed row requires a values wrapper");
		fakeCreateCommand.CapturedOptions.Should().BeNull(
			because: "sync-schemas should not attempt create-lookup after local seed-row validation fails");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects seed rows for virtual entity creation before executing any remote command.")]
	public async Task SchemaSync_VirtualEntityWithSeedRows_Should_Fail_Before_Command_Resolution() {
		// Arrange
		var fakeCreateCommand = new FakeCreateEntitySchemaCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(fakeCreateCommand);
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance, Convergence());
		SchemaSyncArgs args = new(
			"missing-env", "UsrPkg",
			[
				new SchemaSyncOperation("create-entity", "UsrVirtualItem",
					TitleLocalizations: Localizations("Virtual item"),
					SeedRows: [new SchemaSyncSeedRow(new Dictionary<string, System.Text.Json.JsonElement> {
						["Name"] = ToJsonElement("Unavailable")
					})]) {
					IsVirtual = true
				}
			]);

		// Act
		SchemaSyncResponse response = await tool.SchemaSync(args);

		// Assert
		response.Success.Should().BeFalse(
			because: "a virtual entity has no physical table that seed rows could populate");
		response.Results.Should().ContainSingle(
			because: "the invalid combined operation must stop before mutating the target environment");
		response.Results[0].Error.Should().Contain("cannot include seed-rows",
			because: "the caller needs an actionable explanation of the incompatible fields");
		fakeCreateCommand.CapturedOptions.Should().BeNull(
			because: "validation must reject the request before resolving or executing the create command");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects malformed seed-rows when the batch contains a null seed row entry")]
	public async Task SchemaSync_SeedRows_Should_Fail_Before_Command_Resolution_When_Row_Is_Null() {
		// Arrange
		var fakeCreateCommand = new FakeCreateEntitySchemaCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(fakeCreateCommand);
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance, Convergence());
		SchemaSyncArgs args = new(
			"missing-env", "UsrPkg",
			[
				new SchemaSyncOperation("create-lookup", "UsrTodoStatus",
					TitleLocalizations: Localizations("Todo Status"),
					SeedRows: [null!, new SchemaSyncSeedRow(new Dictionary<string, System.Text.Json.JsonElement> {
						["Name"] = ToJsonElement("New")
					})])
			]);

		// Act
		SchemaSyncResponse response = await tool.SchemaSync(args);

		// Assert
		response.Success.Should().BeFalse(
			because: "a null seed row should produce a structured validation failure instead of throwing");
		response.Results.Should().ContainSingle(
			because: "the batch should stop immediately on the malformed seed-data payload");
		response.Results[0].Type.Should().Be("seed-data",
			because: "the failure should be attributed to the seed-data step");
		response.Results[0].Error.Should().Contain("values",
			because: "the caller must be told that each seed row requires a non-null values wrapper");
		fakeCreateCommand.CapturedOptions.Should().BeNull(
			because: "sync-schemas should not attempt create-lookup after local seed-row validation fails");
	}

	[Test]
	[Category("Unit")]
	[Description("Executes multiple operations in order when all succeed")]
	public async Task SchemaSync_Should_Execute_Multiple_Operations_In_Order() {
		// Arrange
		var fakeCreateCommand = new FakeCreateEntitySchemaCommand();
		var fakeUpdateCommand = new FakeUpdateEntitySchemaCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		ILookupRegistrationService registrationService = Substitute.For<ILookupRegistrationService>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(fakeCreateCommand);
		commandResolver.Resolve<UpdateEntitySchemaCommand>(Arg.Any<UpdateEntitySchemaOptions>())
			.Returns(fakeUpdateCommand);
		commandResolver.Resolve<ILookupRegistrationService>(Arg.Any<EnvironmentOptions>())
			.Returns(registrationService);
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance, Convergence());
		SchemaSyncArgs args = new(
			"dev", "UsrPkg",
			[
				new SchemaSyncOperation("create-lookup", "UsrTodoStatus", TitleLocalizations: Localizations("Status")),
				new SchemaSyncOperation("update-entity", "UsrTodoList",
					UpdateOperations: [
						new UpdateEntitySchemaOperationArgs("add", "UsrStatus",
							Type: "Lookup", TitleLocalizations: Localizations("Status"), ReferenceSchemaName: "UsrTodoStatus")
					])
			]);

		// Act
		SchemaSyncResponse response = await tool.SchemaSync(args);

		// Assert
		response.Success.Should().BeTrue(
			because: "all operations should succeed");
		response.Results.Should().HaveCount(2,
			because: "two operations were requested");
		response.Results[0].Type.Should().Be("create-lookup",
			because: "operations should be processed in order");
		response.Results[1].Type.Should().Be("update-entity",
			because: "the update should follow the create");
		registrationService.Received(1).EnsureLookupRegistration("UsrPkg", "UsrTodoStatus", "Status");
	}

	[Test]
	[Category("Unit")]
	[Description("Captures each sync-schemas message under the matching operation result without leaking into adjacent results")]
	public async Task SchemaSync_Should_Assign_Messages_To_The_Correct_Operation() {
		// Arrange
		TestLogger logger = new();
		var fakeCreateCommand = new FakeCreateEntitySchemaCommand(logger, messages: [
			"Entity schema 'UsrTodoStatus' created in package 'UsrPkg'.",
			"Done"
		]);
		var fakeSeedCommand = new FakeCreateDataBindingDbCommand(logger, messages: [
			"Created row: 11111111-1111-1111-1111-111111111111 (Name=New)",
			"Done"
		]);
		var fakeUpdateCommand = new FakeUpdateEntitySchemaCommand(logger, messages: [
			"Column 'UsrStatus' action 'add' completed for schema 'UsrTodoList'.",
			"Done"
		]);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		ILookupRegistrationService registrationService = Substitute.For<ILookupRegistrationService>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(fakeCreateCommand);
		commandResolver.Resolve<CreateDataBindingDbCommand>(Arg.Any<CreateDataBindingDbOptions>())
			.Returns(fakeSeedCommand);
		commandResolver.Resolve<UpdateEntitySchemaCommand>(Arg.Any<UpdateEntitySchemaOptions>())
			.Returns(fakeUpdateCommand);
		commandResolver.Resolve<ILookupRegistrationService>(Arg.Any<EnvironmentOptions>())
			.Returns(registrationService);
		SchemaSyncTool tool = new(commandResolver, logger, Convergence());
		SchemaSyncArgs args = new(
			"dev", "UsrPkg",
			[
				new SchemaSyncOperation("create-lookup", "UsrTodoStatus",
					TitleLocalizations: Localizations("Todo Status"),
					SeedRows: [
						new SchemaSyncSeedRow(new Dictionary<string, System.Text.Json.JsonElement> {
							["Name"] = ToJsonElement("New")
						})
					]),
				new SchemaSyncOperation("update-entity", "UsrTodoList",
					UpdateOperations: [
						new UpdateEntitySchemaOperationArgs("add", "UsrStatus",
							Type: "Lookup", TitleLocalizations: Localizations("Status"), ReferenceSchemaName: "UsrTodoStatus")
					])
			]);

		// Act
		SchemaSyncResponse response = await tool.SchemaSync(args);
		string[] createMessages = GetMessageValues(response.Results[0]);
		string[] seedMessages = GetMessageValues(response.Results[1]);
		string[] updateMessages = GetMessageValues(response.Results[2]);

		// Assert
		response.Success.Should().BeTrue(
			because: "all operations in the batch completed successfully");
		response.Results.Should().HaveCount(3,
			because: "create-lookup with seed rows followed by update-entity should emit three results");
		createMessages.Should().Contain(message => message.Contains("UsrTodoStatus", System.StringComparison.Ordinal),
			because: "the create result should keep only schema creation messages");
		createMessages.Should().NotContain(message => message.Contains("Created row:", System.StringComparison.Ordinal),
			because: "seed-data messages must not leak into the create result");
		createMessages.Should().NotContain(message => message.Contains("UsrTodoList", System.StringComparison.Ordinal),
			because: "update-entity messages must not leak into the create result");
		seedMessages.Should().Contain(message => message.Contains("Created row:", System.StringComparison.Ordinal),
			because: "the seed result should keep only seed-data messages");
		seedMessages.Should().NotContain(message => message.Contains("Entity schema", System.StringComparison.Ordinal),
			because: "schema creation messages must not leak into the seed result");
		seedMessages.Should().NotContain(message => message.Contains("UsrTodoList", System.StringComparison.Ordinal),
			because: "update-entity messages must not leak into the seed result");
		updateMessages.Should().Contain(message => message.Contains("UsrTodoList", System.StringComparison.Ordinal),
			because: "the update result should keep only update-entity messages");
		updateMessages.Should().NotContain(message => message.Contains("Created row:", System.StringComparison.Ordinal),
			because: "seed-data messages must not leak into the update result");
		updateMessages.Should().NotContain(message => message.Contains("Entity schema", System.StringComparison.Ordinal),
			because: "schema creation messages must not leak into the update result");
		registrationService.Received(1).EnsureLookupRegistration("UsrPkg", "UsrTodoStatus", "Todo Status");
	}

	[Test]
	[Category("Unit")]
	[Description("Fails create-lookup when Lookups registration throws so sync-schemas does not report partial success")]
	public async Task SchemaSync_CreateLookup_Should_Fail_When_Lookup_Registration_Fails() {
		// Arrange
		var fakeCreateCommand = new FakeCreateEntitySchemaCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		ILookupRegistrationService registrationService = Substitute.For<ILookupRegistrationService>();
		registrationService
			.When(service => service.EnsureLookupRegistration("UsrPkg", "UsrTodoStatus", "Todo Status"))
			.Do(_ => throw new InvalidOperationException("Lookup registration failed."));
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(fakeCreateCommand);
		commandResolver.Resolve<ILookupRegistrationService>(Arg.Any<EnvironmentOptions>())
			.Returns(registrationService);
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance, Convergence());

		// Act
		SchemaSyncResponse response = await tool.SchemaSync(new SchemaSyncArgs(
			"dev",
			"UsrPkg",
			[new SchemaSyncOperation("create-lookup", "UsrTodoStatus", TitleLocalizations: Localizations("Todo Status"))]));

		// Assert
		response.Success.Should().BeFalse(
			because: "lookup registration is part of successful create-lookup execution");
		response.Results.Should().HaveCount(1,
			because: "sync-schemas should stop after the create-lookup registration failure");
		response.Results[0].Success.Should().BeFalse(
			because: "the create-lookup result should surface the registration failure");
		response.Results[0].Error.Should().Contain("Lookup registration failed",
			because: "the failing registration error should be returned to the caller");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves the read-shape column aliases (data-value-type, reference-schema) when coercing update-entity columns into an add-batch (ENG-90313).")]
	public async Task SchemaSync_UpdateEntity_Coercion_Should_Resolve_Read_Shape_Column_Aliases() {
		// Arrange
		var fakeUpdateCommand = new FakeUpdateEntitySchemaCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<UpdateEntitySchemaCommand>(Arg.Any<UpdateEntitySchemaOptions>())
			.Returns(fakeUpdateCommand);
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance, Convergence());
		SchemaSyncArgs args = new(
			"dev", "UsrPkg",
			[new SchemaSyncOperation("update-entity", "UsrTodoList",
				Columns: [
					// a column copied from the get-app-info read shape: type as 'data-value-type', lookup as 'reference-schema'
					new CreateEntitySchemaColumnArgs("UsrOwner", null!, Localizations("Owner")) {
						DataValueTypeAlias = "Lookup",
						ReferenceSchemaAlias = "Contact"
					}
				])]);

		// Act
		SchemaSyncResponse response = await tool.SchemaSync(args);

		// Assert
		response.Success.Should().BeTrue(
			because: "a read-shape column copied verbatim should coerce into a valid add operation");
		fakeUpdateCommand.CapturedOptions!.Operations.Should().Contain(
			operation => operation.Contains("\"type\":\"Lookup\"", StringComparison.Ordinal),
			because: "the column 'data-value-type' read-shape alias must resolve to the canonical type");
		fakeUpdateCommand.CapturedOptions.Operations.Should().Contain(
			operation => operation.Contains("\"reference-schema-name\":\"Contact\"", StringComparison.Ordinal),
			because: "the column 'reference-schema' read-shape alias must resolve to the canonical reference-schema-name");
	}

	[Test]
	[Category("Unit")]
	[Description("Accepts the 'is-required' read-shape spelling on update-operations and resolves it to the canonical required flag (ENG-90313).")]
	public async Task SchemaSync_UpdateEntity_Should_Accept_IsRequired_Alias_On_Update_Operations() {
		// Arrange
		var fakeUpdateCommand = new FakeUpdateEntitySchemaCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<UpdateEntitySchemaCommand>(Arg.Any<UpdateEntitySchemaOptions>())
			.Returns(fakeUpdateCommand);
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance, Convergence());
		SchemaSyncArgs args = new(
			"dev", "UsrPkg",
			[new SchemaSyncOperation("update-entity", "UsrTodoList",
				UpdateOperations: [
					// modify echoes the read shape and sends the flag as the natural kebab-case 'is-required'
					new UpdateEntitySchemaOperationArgs("modify", null!) {
						NameAlias = "UsrStatus",
						IsRequiredAlias = true
					}
				])]);

		// Act
		SchemaSyncResponse response = await tool.SchemaSync(args);

		// Assert
		response.Success.Should().BeTrue(
			because: "the 'is-required' alias should be accepted without manual field translation");
		fakeUpdateCommand.CapturedOptions!.Operations.Should().Contain(
			operation => operation.Contains("\"required\":true", StringComparison.Ordinal),
			because: "the 'is-required' read-shape alias must resolve to the canonical required flag instead of being dropped");
	}

	[Test]
	[Category("Unit")]
	[Description("Coerces a columns add-batch that uses the 'is-required' alias and resolves it to the canonical required flag (ENG-90313).")]
	public async Task SchemaSync_UpdateEntity_Coercion_Should_Resolve_IsRequired_Alias() {
		// Arrange
		var fakeUpdateCommand = new FakeUpdateEntitySchemaCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<UpdateEntitySchemaCommand>(Arg.Any<UpdateEntitySchemaOptions>())
			.Returns(fakeUpdateCommand);
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance, Convergence());
		SchemaSyncArgs args = new(
			"dev", "UsrPkg",
			[new SchemaSyncOperation("update-entity", "UsrTodoList",
				Columns: [
					new CreateEntitySchemaColumnArgs("UsrFlag", "Boolean", Localizations("Flag")) {
						IsRequiredAlias = true
					}
				])]);

		// Act
		SchemaSyncResponse response = await tool.SchemaSync(args);

		// Assert
		response.Success.Should().BeTrue(
			because: "a columns add-batch using the 'is-required' alias should be accepted");
		fakeUpdateCommand.CapturedOptions!.Operations.Should().Contain(
			operation => operation.Contains("\"column-name\":\"UsrFlag\"", StringComparison.Ordinal)
				&& operation.Contains("\"required\":true", StringComparison.Ordinal),
			because: "the column 'is-required' alias must resolve to the canonical required flag on the coerced add");
	}

	[Test]
	[Category("Unit")]
	[Description("Coerces a columns add-batch whose item identifies the column via the contract-advertised 'column-name' alias and resolves it to a non-empty ColumnName (field-test defect #1).")]
	public async Task SchemaSync_UpdateEntity_Coercion_Should_Resolve_ColumnName_Alias_In_Columns_Array() {
		// Arrange
		var fakeUpdateCommand = new FakeUpdateEntitySchemaCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<UpdateEntitySchemaCommand>(Arg.Any<UpdateEntitySchemaOptions>())
			.Returns(fakeUpdateCommand);
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance, Convergence());
		SchemaSyncArgs args = new(
			"dev", "UsrPkg",
			[new SchemaSyncOperation("update-entity", "UsrTodoList",
				Columns: [
					// an agent following get-tool-contract puts the advertised 'column-name' field into columns[]
					new CreateEntitySchemaColumnArgs(null!, "Text", Localizations("Status")) {
						ColumnNameAlias = "UsrStatus"
					}
				])]);

		// Act
		SchemaSyncResponse response = await tool.SchemaSync(args);

		// Assert
		response.Success.Should().BeTrue(
			because: "a columns[] add specifying the contract-advertised 'column-name' field must resolve to a valid ColumnName instead of failing 'Column name is required'");
		fakeUpdateCommand.CapturedOptions!.Operations.Should().Contain(
			operation => operation.Contains("\"column-name\":\"UsrStatus\"", StringComparison.Ordinal),
			because: "the 'column-name' alias must resolve to the canonical column name on the coerced add");
	}

	[Test]
	[Category("Unit")]
	[Description("Coerces a columns add-batch whose item identifies the column via the canonical 'name' field and resolves it to a non-empty ColumnName (field-test defect #1).")]
	public async Task SchemaSync_UpdateEntity_Coercion_Should_Resolve_Name_In_Columns_Array() {
		// Arrange
		var fakeUpdateCommand = new FakeUpdateEntitySchemaCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<UpdateEntitySchemaCommand>(Arg.Any<UpdateEntitySchemaOptions>())
			.Returns(fakeUpdateCommand);
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance, Convergence());
		SchemaSyncArgs args = new(
			"dev", "UsrPkg",
			[new SchemaSyncOperation("update-entity", "UsrTodoList",
				Columns: [
					new CreateEntitySchemaColumnArgs("UsrPriority", "Text", Localizations("Priority"))
				])]);

		// Act
		SchemaSyncResponse response = await tool.SchemaSync(args);

		// Assert
		response.Success.Should().BeTrue(
			because: "a columns[] add using the canonical 'name' field must keep working unchanged");
		fakeUpdateCommand.CapturedOptions!.Operations.Should().Contain(
			operation => operation.Contains("\"column-name\":\"UsrPriority\"", StringComparison.Ordinal),
			because: "the canonical 'name' field must resolve to the canonical column name on the coerced add");
	}

	[Test]
	[Category("Unit")]
	[Description("Prefers the canonical 'name' over the 'column-name' alias when both are present on a columns add-batch item (field-test defect #1 precedence).")]
	public async Task SchemaSync_UpdateEntity_Coercion_Should_Prefer_Name_Over_ColumnName_Alias() {
		// Arrange
		var fakeUpdateCommand = new FakeUpdateEntitySchemaCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<UpdateEntitySchemaCommand>(Arg.Any<UpdateEntitySchemaOptions>())
			.Returns(fakeUpdateCommand);
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance, Convergence());
		SchemaSyncArgs args = new(
			"dev", "UsrPkg",
			[new SchemaSyncOperation("update-entity", "UsrTodoList",
				Columns: [
					new CreateEntitySchemaColumnArgs("UsrCanonical", "Text", Localizations("Canonical")) {
						ColumnNameAlias = "UsrAlias"
					}
				])]);

		// Act
		SchemaSyncResponse response = await tool.SchemaSync(args);

		// Assert
		response.Success.Should().BeTrue(
			because: "an explicit canonical 'name' must take precedence over the 'column-name' alias");
		fakeUpdateCommand.CapturedOptions!.Operations.Should().Contain(
			operation => operation.Contains("\"column-name\":\"UsrCanonical\"", StringComparison.Ordinal),
			because: "the explicit canonical 'name' wins over the 'column-name' alias when both are present");
	}

	[Test]
	[Category("Unit")]
	[Description("Promotes the read-shape scalar 'caption' to title-localizations when coercing a columns add-batch so a get-app-info column round-trips into an add (ENG-90313 AC1).")]
	public async Task SchemaSync_UpdateEntity_Coercion_Should_Promote_Caption_To_TitleLocalizations() {
		// Arrange
		var fakeUpdateCommand = new FakeUpdateEntitySchemaCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<UpdateEntitySchemaCommand>(Arg.Any<UpdateEntitySchemaOptions>())
			.Returns(fakeUpdateCommand);
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance, Convergence());
		SchemaSyncArgs args = new(
			"dev", "UsrPkg",
			[new SchemaSyncOperation("update-entity", "UsrTodoList",
				Columns: [
					// a column copied verbatim from get-app-info: caption as a scalar, no title-localizations
					new CreateEntitySchemaColumnArgs("UsrCaptioned", "Integer", null!) {
						LegacyCaption = "Captioned"
					}
				])]);

		// Act
		SchemaSyncResponse response = await tool.SchemaSync(args);

		// Assert
		response.Success.Should().BeTrue(
			because: "a get-app-info column carrying only a scalar caption must round-trip into an add without manual translation");
		fakeUpdateCommand.CapturedOptions!.Operations.Should().Contain(
			operation => operation.Contains("\"column-name\":\"UsrCaptioned\"", StringComparison.Ordinal)
				&& operation.Contains("Captioned", StringComparison.Ordinal),
			because: "the read-shape scalar caption must be promoted to the en-US title-localization so the add is accepted");
	}

	[Test]
	[Category("Unit")]
	[Description("Enumerates the 'is-required' and 'caption' read-shape aliases in the missing-operations rejection (ENG-90313 AC4).")]
	public async Task SchemaSync_UpdateEntity_Without_Operations_Should_Enumerate_IsRequired_And_Caption_Aliases() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance, Convergence());
		SchemaSyncArgs args = new(
			"dev", "UsrPkg",
			[new SchemaSyncOperation("update-entity", "UsrTodoList")]);

		// Act
		SchemaSyncResponse response = await tool.SchemaSync(args);

		// Assert
		string error = response.Results[0].Error!;
		error.Should().Contain("is-required",
			because: "the rejection must list the 'is-required' alias agents commonly send for the required flag");
		error.Should().Contain("caption",
			because: "the rejection must tell the agent the read-shape scalar 'caption' is accepted in place of title-localizations");
	}

	[Test]
	[Category("Unit")]
	[Description("Auto-derives the en-US caption from the humanized column name for an update-operations add that supplies only column-name and type (field-test title-localizations blocker).")]
	public async Task SchemaSync_UpdateOperations_Add_Should_AutoDefault_EnUs_From_ColumnName_When_Title_Localizations_Omitted() {
		// Arrange
		var fakeUpdateCommand = new FakeUpdateEntitySchemaCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<UpdateEntitySchemaCommand>(Arg.Any<UpdateEntitySchemaOptions>())
			.Returns(fakeUpdateCommand);
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance, Convergence());
		SchemaSyncArgs args = new(
			"dev", "UsrPkg",
			[new SchemaSyncOperation("update-entity", "UsrTodoList",
				UpdateOperations: [
					new UpdateEntitySchemaOperationArgs("add", "UsrDueDate", Type: "Date")
				])]);

		// Act
		SchemaSyncResponse response = await tool.SchemaSync(args);

		// Assert
		response.Success.Should().BeTrue(
			because: "a bare {column-name, type} add must not hard-fail purely for a missing localization map");
		fakeUpdateCommand.CapturedOptions!.Operations.Should().Contain(
			operation => operation.Contains("\"column-name\":\"UsrDueDate\"", StringComparison.Ordinal)
				&& operation.Contains("Due Date", StringComparison.Ordinal),
			because: "the en-US caption must be the humanized column name (Usr prefix stripped, PascalCase space-split)");
	}

	[Test]
	[Category("Unit")]
	[Description("Promotes a scalar legacy title to the en-US caption for an update-operations add when no localization map is provided.")]
	public async Task SchemaSync_UpdateOperations_Add_Should_Promote_Scalar_Title_When_Map_Omitted() {
		// Arrange
		var fakeUpdateCommand = new FakeUpdateEntitySchemaCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<UpdateEntitySchemaCommand>(Arg.Any<UpdateEntitySchemaOptions>())
			.Returns(fakeUpdateCommand);
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance, Convergence());
		SchemaSyncArgs args = new(
			"dev", "UsrPkg",
			[new SchemaSyncOperation("update-entity", "UsrTodoList",
				UpdateOperations: [
					new UpdateEntitySchemaOperationArgs("add", "UsrDueDate", Type: "Date") {
						LegacyTitle = "Deadline"
					}
				])]);

		// Act
		SchemaSyncResponse response = await tool.SchemaSync(args);

		// Assert
		response.Success.Should().BeTrue(
			because: "a scalar title must be promoted to en-US instead of hard-failing the add");
		fakeUpdateCommand.CapturedOptions!.Operations.Should().Contain(
			operation => operation.Contains("Deadline", StringComparison.Ordinal),
			because: "the scalar title outranks the humanized column name in the en-US derivation precedence");
	}

	[Test]
	[Category("Unit")]
	[Description("Promotes a scalar legacy caption to the en-US caption for an update-operations add when no title or localization map is provided.")]
	public async Task SchemaSync_UpdateOperations_Add_Should_Promote_Scalar_Caption_When_Title_And_Map_Omitted() {
		// Arrange
		var fakeUpdateCommand = new FakeUpdateEntitySchemaCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<UpdateEntitySchemaCommand>(Arg.Any<UpdateEntitySchemaOptions>())
			.Returns(fakeUpdateCommand);
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance, Convergence());
		SchemaSyncArgs args = new(
			"dev", "UsrPkg",
			[new SchemaSyncOperation("update-entity", "UsrTodoList",
				UpdateOperations: [
					new UpdateEntitySchemaOperationArgs("add", "UsrDueDate", Type: "Date") {
						LegacyCaption = "Target Date"
					}
				])]);

		// Act
		SchemaSyncResponse response = await tool.SchemaSync(args);

		// Assert
		response.Success.Should().BeTrue(
			because: "a scalar caption must be promoted to en-US instead of hard-failing the add");
		fakeUpdateCommand.CapturedOptions!.Operations.Should().Contain(
			operation => operation.Contains("Target Date", StringComparison.Ordinal),
			because: "the scalar caption outranks the humanized column name when no scalar title is present");
	}

	[Test]
	[Category("Unit")]
	[Description("Honors an explicit title-localizations.en-US over every scalar/column-name fallback for an update-operations add.")]
	public async Task SchemaSync_UpdateOperations_Add_Should_Prefer_Explicit_EnUs_Over_Fallbacks() {
		// Arrange
		var fakeUpdateCommand = new FakeUpdateEntitySchemaCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<UpdateEntitySchemaCommand>(Arg.Any<UpdateEntitySchemaOptions>())
			.Returns(fakeUpdateCommand);
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance, Convergence());
		SchemaSyncArgs args = new(
			"dev", "UsrPkg",
			[new SchemaSyncOperation("update-entity", "UsrTodoList",
				UpdateOperations: [
					new UpdateEntitySchemaOperationArgs("add", "UsrDueDate", Type: "Date",
						TitleLocalizations: Localizations("Explicit Caption")) {
						LegacyTitle = "Scalar Title",
						LegacyCaption = "Scalar Caption"
					}
				])]);

		// Act
		SchemaSyncResponse response = await tool.SchemaSync(args);

		// Assert
		response.Success.Should().BeTrue(
			because: "an explicit en-US map remains valid for an add");
		fakeUpdateCommand.CapturedOptions!.Operations.Should().Contain(
			operation => operation.Contains("Explicit Caption", StringComparison.Ordinal)
				&& !operation.Contains("Scalar Title", StringComparison.Ordinal),
			because: "the explicit title-localizations.en-US must win over the scalar title/caption fallbacks");
	}

	[Test]
	[Category("Unit")]
	[Description("Still rejects a Cyrillic en-US value supplied explicitly for an update-operations add (ENG-91044 script guard preserved).")]
	public async Task SchemaSync_UpdateOperations_Add_Should_Reject_Cyrillic_EnUs() {
		// Arrange
		var fakeUpdateCommand = new FakeUpdateEntitySchemaCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<UpdateEntitySchemaCommand>(Arg.Any<UpdateEntitySchemaOptions>())
			.Returns(fakeUpdateCommand);
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance, Convergence());
		SchemaSyncArgs args = new(
			"dev", "UsrPkg",
			[new SchemaSyncOperation("update-entity", "UsrTodoList",
				UpdateOperations: [
					new UpdateEntitySchemaOperationArgs("add", "UsrDueDate", Type: "Date",
						TitleLocalizations: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
							["en-US"] = "Термін"
						})
				])]);

		// Act
		SchemaSyncResponse response = await tool.SchemaSync(args);

		// Assert
		response.Success.Should().BeFalse(
			because: "non-English text under en-US must still be rejected by the script/culture guard");
		response.Results[0].Error.Should().Contain("en-US",
			because: "the error must identify the culture key whose value is in the wrong script");
		fakeUpdateCommand.CapturedOptions.Should().BeNull(
			because: "an invalid en-US value must be rejected before the command executes");
	}

	[Test]
	[Category("Unit")]
	[Description("Degrades an operational enrichment failure into a dataforge: warning without failing an otherwise-valid batch (diagnostic enrichment must never gate schema operations).")]
	public async Task SchemaSync_Should_Degrade_Operational_Enrichment_Failure_Into_Warning() {
		// Arrange
		var fakeCreateCommand = new FakeCreateEntitySchemaCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(fakeCreateCommand);
		commandResolver.Resolve<ILookupRegistrationService>(Arg.Any<EnvironmentOptions>())
			.Returns(Substitute.For<ILookupRegistrationService>());
		ISchemaEnrichmentService enrichmentService = Substitute.For<ISchemaEnrichmentService>();
		enrichmentService
			.Enrich(Arg.Any<string?>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<IReadOnlyList<string>?>())
			.Throws(new InvalidOperationException("baseUri: Value cannot be null"));
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance, Convergence(), enrichmentService);
		SchemaSyncArgs args = new(
			"dev", "UsrPkg",
			[new SchemaSyncOperation("create-lookup", "UsrTodoStatus", TitleLocalizations: Localizations("Todo Status"))]);

		// Act
		SchemaSyncResponse response = await tool.SchemaSync(args);

		// Assert
		response.Success.Should().BeTrue(
			because: "a throwing enrichment service is diagnostic-only and must not fail an otherwise-valid operation");
		response.DataForge.Should().NotBeNull(
			because: "the degraded enrichment result must still be attached so the warning surfaces");
		response.DataForge!.Warnings.Should().ContainSingle(warning => warning.StartsWith("dataforge:", StringComparison.Ordinal),
			because: "the operational failure must be reported as a dataforge: warning, not swallowed silently");
	}

	[Test]
	[Category("Unit")]
	[Description("Redacts paths/URIs out of the degraded dataforge: warning before it surfaces, so a data-layer failure carrying an absolute path or target host never leaks into the MCP transcript.")]
	public async Task SchemaSync_Should_Redact_Sensitive_Tokens_In_Degraded_Enrichment_Warning() {
		// Arrange
		var fakeCreateCommand = new FakeCreateEntitySchemaCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(fakeCreateCommand);
		commandResolver.Resolve<ILookupRegistrationService>(Arg.Any<EnvironmentOptions>())
			.Returns(Substitute.For<ILookupRegistrationService>());
		ISchemaEnrichmentService enrichmentService = Substitute.For<ISchemaEnrichmentService>();
		enrichmentService
			.Enrich(Arg.Any<string?>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<IReadOnlyList<string>?>())
			.Throws(new InvalidOperationException("dataforge call to https://target.creatio.com/0/rest failed: /Users/dev/secret/appsettings.json missing"));
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance, Convergence(), enrichmentService);
		SchemaSyncArgs args = new(
			"dev", "UsrPkg",
			[new SchemaSyncOperation("create-lookup", "UsrTodoStatus", TitleLocalizations: Localizations("Todo Status"))]);

		// Act
		SchemaSyncResponse response = await tool.SchemaSync(args);

		// Assert
		response.DataForge.Should().NotBeNull(
			because: "the degraded enrichment result must still be attached so the warning surfaces");
		string warning = response.DataForge!.Warnings.Single();
		warning.Should().StartWith("dataforge:",
			because: "the operational failure must still be reported as a dataforge: warning");
		warning.Should().NotContain("https://target.creatio.com",
			because: "the target host/URI must be redacted before surfacing to the MCP transcript");
		warning.Should().NotContain("/Users/dev/secret/appsettings.json",
			because: "the absolute path must be redacted before surfacing to the MCP transcript");
		warning.Should().Contain("[redacted",
			because: "redaction replaces the sensitive tokens with a stable placeholder rather than dropping them");
	}

	[Test]
	[Category("Unit")]
	[Description("Does NOT mask an unrecoverable exception (programming defect) from enrichment as a warning — it propagates so the real bug is not hidden as a recoverable degradation.")]
	public async Task SchemaSync_Should_Propagate_Unrecoverable_Enrichment_Exception() {
		// Arrange
		var fakeCreateCommand = new FakeCreateEntitySchemaCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(fakeCreateCommand);
		ISchemaEnrichmentService enrichmentService = Substitute.For<ISchemaEnrichmentService>();
		enrichmentService
			.Enrich(Arg.Any<string?>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<IReadOnlyList<string>?>())
			.Throws(new NullReferenceException("object reference not set"));
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance, Convergence(), enrichmentService);
		SchemaSyncArgs args = new(
			"dev", "UsrPkg",
			[new SchemaSyncOperation("create-lookup", "UsrTodoStatus", TitleLocalizations: Localizations("Todo Status"))]);

		// Act
		Func<Task> act = async () => await tool.SchemaSync(args);

		// Assert
		await act.Should().ThrowAsync<NullReferenceException>(
			because: "a programming defect must not be hidden as a benign dataforge: degradation");
	}

	[Test]
	[Category("Unit")]
	[Description("Streams a per-operation stage marker before each operation and before the seed step, in batch order (ENG-93087).")]
	public void ExecuteBatch_Should_Stream_Ordered_Stage_Markers_When_Operations_Succeed() {
		// Arrange
		var fakeCreateCommand = new FakeCreateEntitySchemaCommand();
		var fakeSeedCommand = new FakeCreateDataBindingDbCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		ILookupRegistrationService registrationService = Substitute.For<ILookupRegistrationService>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(fakeCreateCommand);
		commandResolver.Resolve<CreateDataBindingDbCommand>(Arg.Any<CreateDataBindingDbOptions>())
			.Returns(fakeSeedCommand);
		commandResolver.Resolve<ILookupRegistrationService>(Arg.Any<EnvironmentOptions>())
			.Returns(registrationService);
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance, Convergence());
		SchemaSyncArgs args = new(
			"dev", "UsrPkg",
			[
				new SchemaSyncOperation("create-entity", "UsrAlpha",
					TitleLocalizations: Localizations("Alpha"),
					SeedRows: [
						new SchemaSyncSeedRow(new Dictionary<string, System.Text.Json.JsonElement> {
							["Name"] = ToJsonElement("New")
						})
					]),
				new SchemaSyncOperation("create-lookup", "UsrBeta", TitleLocalizations: Localizations("Beta"))
			]);
		var markers = new List<string>();

		// Act
		SchemaSyncResponse response = tool.ExecuteBatch(args, markers.Add);

		// Assert
		response.Success.Should().BeTrue(
			because: "every operation in the batch returned exit code 0");
		markers.Should().Equal(
			["1/2: create-entity UsrAlpha", "1/2: seed-data UsrAlpha", "2/2: create-lookup UsrBeta"],
			because: "sync-schemas must stream one marker per operation plus one before the seed step, in batch order");
	}

	[Test]
	[Category("Unit")]
	[Description("Does not stream a marker for a later operation when an earlier operation fails (stop-on-failure).")]
	public void ExecuteBatch_Should_Not_Stream_Later_Marker_When_Earlier_Operation_Fails() {
		// Arrange
		var failingCreateCommand = new FakeCreateEntitySchemaCommand(exitCode: 1);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(failingCreateCommand);
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance, Convergence());
		SchemaSyncArgs args = new(
			"dev", "UsrPkg",
			[
				new SchemaSyncOperation("create-entity", "UsrAlpha", TitleLocalizations: Localizations("Alpha")),
				new SchemaSyncOperation("create-lookup", "UsrBeta", TitleLocalizations: Localizations("Beta"))
			]);
		var markers = new List<string>();

		// Act
		SchemaSyncResponse response = tool.ExecuteBatch(args, markers.Add);

		// Assert
		response.Success.Should().BeFalse(
			because: "the first operation returned a non-zero exit code");
		markers.Should().Contain("1/2: create-entity UsrAlpha",
			because: "the first operation's marker is streamed before it runs");
		markers.Should().NotContain(marker => marker.Contains("2/2", StringComparison.Ordinal),
			because: "sync-schemas must stop on the first failure and never announce a later operation");
	}

	[Test]
	[Category("Unit")]
	[Description("Aborts the batch and never resolves the second operation's backend command when cancellation is signalled mid-batch after the first operation's marker (ENG-93087).")]
	public void ExecuteBatch_Should_Abort_Remaining_Operations_When_Cancelled_MidBatch() {
		// Arrange
		var fakeCreateCommand = new FakeCreateEntitySchemaCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(fakeCreateCommand);
		commandResolver.Resolve<ILookupRegistrationService>(Arg.Any<EnvironmentOptions>())
			.Returns(Substitute.For<ILookupRegistrationService>());
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance, Convergence());
		SchemaSyncArgs args = new(
			"dev", "UsrPkg",
			[
				new SchemaSyncOperation("create-entity", "UsrAlpha", TitleLocalizations: Localizations("Alpha")),
				new SchemaSyncOperation("create-lookup", "UsrBeta", TitleLocalizations: Localizations("Beta"))
			]);
		using var cts = new System.Threading.CancellationTokenSource();
		Action<string> reportStage = marker => {
			if (marker.Contains("1/2", StringComparison.Ordinal)) {
				cts.Cancel();
			}
		};

		// Act
		Action act = () => tool.ExecuteBatch(args, reportStage, cts.Token);

		// Assert
		FluentActions.Invoking(act).Should().Throw<OperationCanceledException>(
			because: "a cancellation signalled after the first operation's marker must abort the batch on the calling thread");
		commandResolver.DidNotReceive().Resolve<CreateEntitySchemaCommand>(
			Arg.Is<CreateEntitySchemaOptions>(options => options.SchemaName == "UsrBeta"));
	}

	[Test]
	[Category("Unit")]
	[Description("Creates the schema and ensures the Lookups registration when the convergence classifier reports the schema is absent.")]
	public async Task ExecuteCreateSchema_ShouldReturnCreatedOutcomeAndEnsureRegistration_WhenSchemaAbsent() {
		// Arrange
		var fakeCreateCommand = new FakeCreateEntitySchemaCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		ILookupRegistrationService registrationService = Substitute.For<ILookupRegistrationService>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(fakeCreateCommand);
		commandResolver.Resolve<ILookupRegistrationService>(Arg.Any<EnvironmentOptions>())
			.Returns(registrationService);
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance, Convergence(SchemaConvergenceOutcome.Create));
		SchemaSyncArgs args = new(
			"dev", "UsrPkg",
			[new SchemaSyncOperation("create-lookup", "UsrTodoStatus", TitleLocalizations: Localizations("Todo Status"))]);

		// Act
		SchemaSyncResponse response = await tool.SchemaSync(args);

		// Assert
		response.Results[0].Success.Should().BeTrue(
			because: "an absent schema must be created successfully");
		response.Results[0].Outcome.Should().Be("created",
			because: "the created outcome discriminator must be surfaced for an absent schema");
		fakeCreateCommand.CapturedOptions.Should().NotBeNull(
			because: "the create path must run when the schema is absent");
		registrationService.Received(1).EnsureLookupRegistration("UsrPkg", "UsrTodoStatus", "Todo Status");
	}

	[Test]
	[Category("Unit")]
	[Description("Adds only the missing columns via the update-entity add-column path without recreating the schema when the classifier reports a same-package subset.")]
	public async Task ExecuteCreateSchema_ShouldAddOnlyMissingColumnsWithoutRecreate_WhenSchemaExistsWithSubset() {
		// Arrange
		var fakeCreateCommand = new FakeCreateEntitySchemaCommand();
		var fakeUpdateCommand = new FakeUpdateEntitySchemaCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		ILookupRegistrationService registrationService = Substitute.For<ILookupRegistrationService>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(fakeCreateCommand);
		commandResolver.Resolve<UpdateEntitySchemaCommand>(Arg.Any<UpdateEntitySchemaOptions>())
			.Returns(fakeUpdateCommand);
		commandResolver.Resolve<ILookupRegistrationService>(Arg.Any<EnvironmentOptions>())
			.Returns(registrationService);
		ISchemaConvergenceService convergence = Convergence(
			SchemaConvergenceOutcome.Reconcile,
			columnsToAdd: [new CreateEntitySchemaColumnArgs("UsrExtra", "Text", Localizations("Extra"))]);
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance, convergence);
		SchemaSyncArgs args = new(
			"dev", "UsrPkg",
			[new SchemaSyncOperation("create-lookup", "UsrTodoStatus", TitleLocalizations: Localizations("Todo Status"))]);

		// Act
		SchemaSyncResponse response = await tool.SchemaSync(args);

		// Assert
		response.Results[0].Success.Should().BeTrue(
			because: "adding a missing column to an existing schema should succeed");
		response.Results[0].Outcome.Should().Be("reconciled",
			because: "an existing schema with a missing column must report the reconciled outcome");
		fakeCreateCommand.CapturedOptions.Should().BeNull(
			because: "CreateEntitySchemaCommand is create-only and must never recreate an existing schema");
		fakeUpdateCommand.CapturedOptions.Should().NotBeNull(
			because: "missing columns are added through the update-entity add-column path");
		fakeUpdateCommand.CapturedOptions!.Operations.Should().ContainSingle(
			operation => operation.Contains("\"column-name\":\"UsrExtra\"", StringComparison.Ordinal)
				&& operation.Contains("\"action\":\"add\"", StringComparison.Ordinal),
			because: "only the single missing column must be added, as an add operation");
		registrationService.Received(1).EnsureLookupRegistration("UsrPkg", "UsrTodoStatus", "Todo Status");
	}

	[Test]
	[Category("Unit")]
	[Description("Ensures the Lookups registration on the already-exists path (moved out of the freshly-created branch) when the classifier reports the schema is already satisfied.")]
	public async Task ExecuteCreateSchema_ShouldEnsureLookupRegistrationOnAlreadyExistsPath_WhenRegistrationMissing() {
		// Arrange
		var fakeCreateCommand = new FakeCreateEntitySchemaCommand();
		var fakeUpdateCommand = new FakeUpdateEntitySchemaCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		ILookupRegistrationService registrationService = Substitute.For<ILookupRegistrationService>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(fakeCreateCommand);
		commandResolver.Resolve<UpdateEntitySchemaCommand>(Arg.Any<UpdateEntitySchemaOptions>())
			.Returns(fakeUpdateCommand);
		commandResolver.Resolve<ILookupRegistrationService>(Arg.Any<EnvironmentOptions>())
			.Returns(registrationService);
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance, Convergence(SchemaConvergenceOutcome.AlreadySatisfied));
		SchemaSyncArgs args = new(
			"dev", "UsrPkg",
			[new SchemaSyncOperation("create-lookup", "UsrTodoStatus", TitleLocalizations: Localizations("Todo Status"))]);

		// Act
		SchemaSyncResponse response = await tool.SchemaSync(args);

		// Assert
		response.Results[0].Success.Should().BeTrue(
			because: "an already-satisfied schema requires no mutation and must succeed");
		response.Results[0].Outcome.Should().Be("already-satisfied",
			because: "a schema that already matches the requested shape must report already-satisfied");
		fakeCreateCommand.CapturedOptions.Should().BeNull(
			because: "an already-satisfied schema must not be recreated");
		fakeUpdateCommand.CapturedOptions.Should().BeNull(
			because: "an already-satisfied schema has no columns to add");
		registrationService.Received(1).EnsureLookupRegistration("UsrPkg", "UsrTodoStatus", "Todo Status");
	}

	[Test]
	[Category("Unit")]
	[Description("Cross-package pre-existing schema is surfaced pre-emptively as a collision without ever calling create.")]
	public async Task ExecuteCreateSchema_ShouldFailWithCollisionAndNotCallCreate_WhenSchemaInDifferentPackage() {
		// Arrange
		var fakeCreateCommand = new FakeCreateEntitySchemaCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(fakeCreateCommand);
		ISchemaConvergenceService convergence = Convergence(
			SchemaConvergenceOutcome.Collision,
			collisionPackageName: "OtherPkg",
			error: "Error: schema 'UsrTodoStatus' already exists in package 'OtherPkg'.");
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance, convergence);
		SchemaSyncArgs args = new(
			"dev", "UsrPkg",
			[new SchemaSyncOperation("create-lookup", "UsrTodoStatus", TitleLocalizations: Localizations("Todo Status"))]);

		// Act
		SchemaSyncResponse response = await tool.SchemaSync(args);

		// Assert
		response.Results[0].Success.Should().BeFalse(
			because: "a cross-package name collision is a durable failure, not a success");
		response.Results[0].Outcome.Should().Be("collision",
			because: "the collision outcome discriminator must be surfaced to callers");
		response.Results[0].CollisionInfo.Should().NotBeNull(
			because: "the owning package must be machine-readable");
		response.Results[0].CollisionInfo!.ExistingPackageName.Should().Be("OtherPkg",
			because: "the collision info must name the owning package");
		response.Results[0].Error.Should().StartWith("Error:",
			because: "errors must be user-friendly Error: {message} strings");
		fakeCreateCommand.CapturedOptions.Should().BeNull(
			because: "the collision must be detected pre-emptively — create is never attempted (no masked collision)");
	}

	[Test]
	[Category("Unit")]
	[Description("Stops the batch on the first operation when a create collision is detected and never runs the following operation.")]
	public async Task SchemaSync_ShouldStopOnFirstFailure_WhenCreateCollisionDetected() {
		// Arrange
		var fakeCreateCommand = new FakeCreateEntitySchemaCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(fakeCreateCommand);
		ISchemaConvergenceService convergence = Convergence(
			SchemaConvergenceOutcome.Collision,
			collisionPackageName: "OtherPkg",
			error: "Error: schema 'UsrFirst' already exists in package 'OtherPkg'.");
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance, convergence);
		SchemaSyncArgs args = new(
			"dev", "UsrPkg",
			[
				new SchemaSyncOperation("create-lookup", "UsrFirst", TitleLocalizations: Localizations("First")),
				new SchemaSyncOperation("create-lookup", "UsrSecond", TitleLocalizations: Localizations("Second"))
			]);

		// Act
		SchemaSyncResponse response = await tool.SchemaSync(args);

		// Assert
		response.Success.Should().BeFalse(
			because: "a collision on the first operation fails the batch");
		response.Results.Should().HaveCount(1,
			because: "stop-on-first-failure must prevent the second operation from running");
		response.Results[0].SchemaName.Should().Be("UsrFirst",
			because: "only the first (failing) operation result should be present");
		fakeCreateCommand.CapturedOptions.Should().BeNull(
			because: "the collision is pre-emptive, so no create is attempted on either operation");
	}

	[Test]
	[Category("Unit")]
	[Description("Omits the outcome field from the serialized result when the outcome is null so the existing wire shape is preserved.")]
	public void SchemaSyncOperationResult_ShouldOmitOutcomeField_WhenOutcomeIsNull() {
		// Arrange
		SchemaSyncOperationResult result = new() {
			Type = "create-lookup",
			SchemaName = "UsrTodoStatus",
			Success = true,
			Outcome = null
		};

		// Act
		string json = System.Text.Json.JsonSerializer.Serialize(result);

		// Assert
		json.Should().NotContain("\"outcome\"",
			because: "the outcome field is JsonIgnoreCondition.WhenWritingNull, so a null outcome must not appear on the wire");
	}

	private static ISchemaConvergenceService Convergence(
		SchemaConvergenceOutcome outcome = SchemaConvergenceOutcome.Create,
		IReadOnlyList<CreateEntitySchemaColumnArgs>? columnsToAdd = null,
		IReadOnlyList<UpdateEntitySchemaOperationArgs>? columnsToModify = null,
		string? collisionPackageName = null,
		string? error = null) {
		ISchemaConvergenceService convergence = Substitute.For<ISchemaConvergenceService>();
		convergence.Classify(Arg.Any<SchemaConvergenceTarget>())
			.Returns(new SchemaConvergencePlan(outcome, columnsToAdd ?? [], columnsToModify ?? [], collisionPackageName, error));
		return convergence;
	}

	private static System.Text.Json.JsonElement ToJsonElement(string value) {
		return System.Text.Json.JsonDocument.Parse($"\"{value}\"").RootElement.Clone();
	}

	private static string[] GetMessageValues(SchemaSyncOperationResult result) {
		return result.Messages?
			.Select(message => message.Value?.ToString() ?? string.Empty)
			.ToArray() ?? [];
	}

	private static Dictionary<string, string> Localizations(string enUs, string? ukUa = null) {
		Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase) {
			["en-US"] = enUs
		};
		if (!string.IsNullOrWhiteSpace(ukUa)) {
			result["uk-UA"] = ukUa;
		}
		return result;
	}

	private sealed class FakeCreateEntitySchemaCommand : CreateEntitySchemaCommand {
		private readonly int _exitCode;
		private readonly ILogger _logger;
		private readonly IReadOnlyList<string> _messages;
		public CreateEntitySchemaOptions CapturedOptions { get; private set; }
		public FakeCreateEntitySchemaCommand(ILogger logger = null, int exitCode = 0, IReadOnlyList<string> messages = null)
			: base(Substitute.For<IRemoteEntitySchemaCreator>(), logger ?? Substitute.For<ILogger>()) {
			_logger = logger ?? Substitute.For<ILogger>();
			_exitCode = exitCode;
			_messages = messages ?? [];
		}
		public override int Execute(CreateEntitySchemaOptions options) {
			CapturedOptions = options;
			foreach (string message in _messages) {
				if (_exitCode == 0) {
					_logger.WriteInfo(message);
				} else {
					_logger.WriteError(message);
				}
			}
			return _exitCode;
		}
	}

	private sealed class FakeUpdateEntitySchemaCommand : UpdateEntitySchemaCommand {
		private readonly ILogger _logger;
		private readonly IReadOnlyList<string> _messages;
		public UpdateEntitySchemaOptions CapturedOptions { get; private set; }
		public FakeUpdateEntitySchemaCommand(ILogger logger = null, IReadOnlyList<string> messages = null)
			: base(Substitute.For<IRemoteEntitySchemaColumnManager>(), logger ?? Substitute.For<ILogger>()) {
			_logger = logger ?? Substitute.For<ILogger>();
			_messages = messages ?? [];
		}
		public override int Execute(UpdateEntitySchemaOptions options) {
			CapturedOptions = options;
			foreach (string message in _messages) {
				_logger.WriteInfo(message);
			}
			return 0;
		}
	}

	private sealed class FakeCreateDataBindingDbCommand : CreateDataBindingDbCommand {
		private readonly ILogger _logger;
		private readonly IReadOnlyList<string> _messages;
		public CreateDataBindingDbOptions CapturedOptions { get; private set; }
		public FakeCreateDataBindingDbCommand(ILogger logger = null, IReadOnlyList<string> messages = null)
			: base(Substitute.For<IDataBindingDbService>(), logger ?? Substitute.For<ILogger>()) {
			_logger = logger ?? Substitute.For<ILogger>();
			_messages = messages ?? [];
		}
		public override int Execute(CreateDataBindingDbOptions options) {
			CapturedOptions = options;
			foreach (string message in _messages) {
				_logger.WriteInfo(message);
			}
			return 0;
		}
	}

	private sealed class TestLogger : ILogger {
		List<LogMessage> ILogger.LogMessages => LogMessages;
		bool ILogger.PreserveMessages { get; set; }
		internal List<LogMessage> LogMessages { get; } = [];

		public void ClearMessages() => LogMessages.Clear();
		public IDisposable BeginScopedFileSink(string logFilePath) => Substitute.For<IDisposable>();
		public void Start(string logFilePath = "") { }
		public void SetCreatioLogStreamer(ILogStreamer creatioLogStreamer) { }
		public void StartWithStream() { }
		public void Stop() { }
		public void Write(string value) => LogMessages.Add(new UndecoratedMessage(value));
		public void WriteLine() => LogMessages.Add(new UndecoratedMessage(string.Empty));
		public void WriteLine(string value) => LogMessages.Add(new UndecoratedMessage(value));
		public void WriteWarning(string value) => LogMessages.Add(new WarningMessage(value));
		public void WriteError(string value) => LogMessages.Add(new ErrorMessage(value));
		public void WriteInfo(string value) => LogMessages.Add(new InfoMessage(value));
		public void WriteDebug(string value) => LogMessages.Add(new DebugMessage(value));
		public void PrintTable(ConsoleTable table) => LogMessages.Add(new TableMessage(table));
		public void PrintValidationFailureErrors(IEnumerable<FluentValidation.Results.ValidationFailure> errors) { }
	}
}
