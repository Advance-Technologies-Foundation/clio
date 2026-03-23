using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
public sealed class SchemaSyncToolTests {

	[Test]
	[Category("Unit")]
	[Description("Advertises a stable MCP tool name for schema-sync")]
	public void SchemaSyncTool_Should_Advertise_Stable_Tool_Name() {
		// Arrange

		// Act
		string toolName = SchemaSyncTool.ToolName;

		// Assert
		toolName.Should().Be("schema-sync",
			because: "the schema-sync MCP tool identifier must remain stable for callers");
	}

	[Test]
	[Category("Unit")]
	[Description("Marks schema-sync as destructive and not read-only")]
	public void SchemaSyncTool_Should_Advertise_Safety_Metadata() {
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
			because: "schema-sync mutates remote schemas and should not be marked read-only");
		destructive.Should().BeTrue(
			because: "schema-sync creates and modifies remote schemas and should warn clients");
	}

	[Test]
	[Category("Unit")]
	[Description("Routes create-lookup operation through CreateEntitySchemaCommand with BaseLookup parent")]
	public void SchemaSync_CreateLookup_Should_Route_Through_CreateEntitySchemaCommand() {
		// Arrange
		var fakeCreateCommand = new FakeCreateEntitySchemaCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(fakeCreateCommand);
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance);
		SchemaSyncArgs args = new(
			"dev", "UsrPkg",
			[new SchemaSyncOperation("create-lookup", "UsrTodoStatus", Title: "Todo Status")]);

		// Act
		SchemaSyncResponse response = tool.SchemaSync(args);

		// Assert
		response.Success.Should().BeTrue(
			because: "a create-lookup with exit code 0 should succeed");
		response.Results.Should().HaveCount(1,
			because: "one create-lookup operation was requested");
		response.Results[0].Operation.Should().Be("create-lookup",
			because: "the result should reflect the requested operation type");
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
	}

	[Test]
	[Category("Unit")]
	[Description("Routes create-entity operation with custom parent schema")]
	public void SchemaSync_CreateEntity_Should_Use_Custom_Parent_Schema() {
		// Arrange
		var fakeCreateCommand = new FakeCreateEntitySchemaCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(fakeCreateCommand);
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance);
		SchemaSyncArgs args = new(
			"dev", "UsrPkg",
			[new SchemaSyncOperation("create-entity", "UsrTodoList",
				Title: "Todo List", ParentSchemaName: "BaseEntity")]);

		// Act
		SchemaSyncResponse response = tool.SchemaSync(args);

		// Assert
		response.Success.Should().BeTrue(
			because: "a create-entity with exit code 0 should succeed");
		fakeCreateCommand.CapturedOptions!.ParentSchemaName.Should().Be("BaseEntity",
			because: "create-entity should use the specified parent schema");
	}

	[Test]
	[Category("Unit")]
	[Description("Routes update-entity operation through UpdateEntitySchemaCommand")]
	public void SchemaSync_UpdateEntity_Should_Route_Through_UpdateEntitySchemaCommand() {
		// Arrange
		var fakeUpdateCommand = new FakeUpdateEntitySchemaCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<UpdateEntitySchemaCommand>(Arg.Any<UpdateEntitySchemaOptions>())
			.Returns(fakeUpdateCommand);
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance);
		SchemaSyncArgs args = new(
			"dev", "UsrPkg",
			[new SchemaSyncOperation("update-entity", "UsrTodoList",
				UpdateOperations: [
					new UpdateEntitySchemaOperationArgs("add", "UsrStatus",
						Type: "Lookup", ReferenceSchemaName: "UsrTodoStatus"),
					new UpdateEntitySchemaOperationArgs("add", "UsrDueDate",
						Type: "Date")
				])]);

		// Act
		SchemaSyncResponse response = tool.SchemaSync(args);

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
	public void SchemaSync_SeedRows_Should_Execute_After_Create() {
		// Arrange
		var fakeCreateCommand = new FakeCreateEntitySchemaCommand();
		var fakeSeedCommand = new FakeCreateDataBindingDbCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(fakeCreateCommand);
		commandResolver.Resolve<CreateDataBindingDbCommand>(Arg.Any<CreateDataBindingDbOptions>())
			.Returns(fakeSeedCommand);
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance);
		SchemaSyncArgs args = new(
			"dev", "UsrPkg",
			[new SchemaSyncOperation("create-lookup", "UsrTodoStatus",
				Title: "Todo Status",
				SeedRows: [
					new SchemaSyncSeedRow(new Dictionary<string, System.Text.Json.JsonElement> {
						["Name"] = ToJsonElement("New")
					}),
					new SchemaSyncSeedRow(new Dictionary<string, System.Text.Json.JsonElement> {
						["Name"] = ToJsonElement("Done")
					})
				])]);

		// Act
		SchemaSyncResponse response = tool.SchemaSync(args);

		// Assert
		response.Success.Should().BeTrue(
			because: "both create-lookup and seed-data should succeed");
		response.Results.Should().HaveCount(2,
			because: "a create-lookup and its seed-data produce two results");
		response.Results[0].Operation.Should().Be("create-lookup",
			because: "the first result should be the create operation");
		response.Results[1].Operation.Should().Be("seed-data",
			because: "the second result should be the seed operation");
		fakeSeedCommand.CapturedOptions.Should().NotBeNull(
			because: "seed-data should be executed after create");
		fakeSeedCommand.CapturedOptions!.SchemaName.Should().Be("UsrTodoStatus",
			because: "seed-data should target the same schema as the create operation");
		fakeSeedCommand.CapturedOptions.RowsJson.Should().Contain("New",
			because: "the seed rows should be serialized to JSON");
	}

	[Test]
	[Category("Unit")]
	[Description("Stops processing on first operation failure")]
	public void SchemaSync_Should_Stop_On_First_Failure() {
		// Arrange
		var failingCommand = new FakeCreateEntitySchemaCommand(exitCode: 1);
		var secondCommand = new FakeCreateEntitySchemaCommand();
		int resolveCount = 0;
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(_ => resolveCount++ == 0 ? failingCommand : secondCommand);
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance);
		SchemaSyncArgs args = new(
			"dev", "UsrPkg",
			[
				new SchemaSyncOperation("create-lookup", "UsrFirst", Title: "First"),
				new SchemaSyncOperation("create-lookup", "UsrSecond", Title: "Second")
			]);

		// Act
		SchemaSyncResponse response = tool.SchemaSync(args);

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
	[Description("Returns error for unknown operation type without stopping")]
	public void SchemaSync_Unknown_OperationType_Should_Return_Error() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance);
		SchemaSyncArgs args = new(
			"dev", "UsrPkg",
			[new SchemaSyncOperation("delete-schema", "UsrOops")]);

		// Act
		SchemaSyncResponse response = tool.SchemaSync(args);

		// Assert
		response.Success.Should().BeFalse(
			because: "an unknown operation type should fail");
		response.Results.Should().HaveCount(1,
			because: "one operation was attempted");
		response.Results[0].Error.Should().Contain("Unknown operation type",
			because: "the error should describe the unsupported type");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns error when update-entity has no update-operations")]
	public void SchemaSync_UpdateEntity_Without_Operations_Should_Fail() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance);
		SchemaSyncArgs args = new(
			"dev", "UsrPkg",
			[new SchemaSyncOperation("update-entity", "UsrTodoList")]);

		// Act
		SchemaSyncResponse response = tool.SchemaSync(args);

		// Assert
		response.Success.Should().BeFalse(
			because: "update-entity without operations should fail validation");
		response.Results[0].Error.Should().Contain("update-operations",
			because: "the error should indicate that update-operations are required");
	}

	[Test]
	[Category("Unit")]
	[Description("Stops seed-data when create-lookup fails and does not seed")]
	public void SchemaSync_SeedRows_Should_Not_Execute_When_Create_Fails() {
		// Arrange
		var failingCreateCommand = new FakeCreateEntitySchemaCommand(exitCode: 1);
		var fakeSeedCommand = new FakeCreateDataBindingDbCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(failingCreateCommand);
		commandResolver.Resolve<CreateDataBindingDbCommand>(Arg.Any<CreateDataBindingDbOptions>())
			.Returns(fakeSeedCommand);
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance);
		SchemaSyncArgs args = new(
			"dev", "UsrPkg",
			[new SchemaSyncOperation("create-lookup", "UsrBroken",
				Title: "Broken",
				SeedRows: [new SchemaSyncSeedRow(new Dictionary<string, System.Text.Json.JsonElement> {
					["Name"] = ToJsonElement("value")
				})])]);

		// Act
		SchemaSyncResponse response = tool.SchemaSync(args);

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
	[Description("Executes multiple operations in order when all succeed")]
	public void SchemaSync_Should_Execute_Multiple_Operations_In_Order() {
		// Arrange
		var fakeCreateCommand = new FakeCreateEntitySchemaCommand();
		var fakeUpdateCommand = new FakeUpdateEntitySchemaCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(fakeCreateCommand);
		commandResolver.Resolve<UpdateEntitySchemaCommand>(Arg.Any<UpdateEntitySchemaOptions>())
			.Returns(fakeUpdateCommand);
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance);
		SchemaSyncArgs args = new(
			"dev", "UsrPkg",
			[
				new SchemaSyncOperation("create-lookup", "UsrTodoStatus", Title: "Status"),
				new SchemaSyncOperation("update-entity", "UsrTodoList",
					UpdateOperations: [
						new UpdateEntitySchemaOperationArgs("add", "UsrStatus",
							Type: "Lookup", ReferenceSchemaName: "UsrTodoStatus")
					])
			]);

		// Act
		SchemaSyncResponse response = tool.SchemaSync(args);

		// Assert
		response.Success.Should().BeTrue(
			because: "all operations should succeed");
		response.Results.Should().HaveCount(2,
			because: "two operations were requested");
		response.Results[0].Operation.Should().Be("create-lookup",
			because: "operations should be processed in order");
		response.Results[1].Operation.Should().Be("update-entity",
			because: "the update should follow the create");
	}

	private static System.Text.Json.JsonElement ToJsonElement(string value) {
		return System.Text.Json.JsonDocument.Parse($"\"{value}\"").RootElement.Clone();
	}

	private sealed class FakeCreateEntitySchemaCommand : CreateEntitySchemaCommand {
		private readonly int _exitCode;
		public CreateEntitySchemaOptions CapturedOptions { get; private set; }
		public FakeCreateEntitySchemaCommand(int exitCode = 0)
			: base(Substitute.For<IRemoteEntitySchemaCreator>(), Substitute.For<ILogger>()) {
			_exitCode = exitCode;
		}
		public override int Execute(CreateEntitySchemaOptions options) {
			CapturedOptions = options;
			return _exitCode;
		}
	}

	private sealed class FakeUpdateEntitySchemaCommand : UpdateEntitySchemaCommand {
		public UpdateEntitySchemaOptions CapturedOptions { get; private set; }
		public FakeUpdateEntitySchemaCommand()
			: base(Substitute.For<IRemoteEntitySchemaColumnManager>(), Substitute.For<ILogger>()) {
		}
		public override int Execute(UpdateEntitySchemaOptions options) {
			CapturedOptions = options;
			return 0;
		}
	}

	private sealed class FakeCreateDataBindingDbCommand : CreateDataBindingDbCommand {
		public CreateDataBindingDbOptions CapturedOptions { get; private set; }
		public FakeCreateDataBindingDbCommand()
			: base(Substitute.For<IDataBindingDbService>(), Substitute.For<ILogger>()) {
		}
		public override int Execute(CreateDataBindingDbOptions options) {
			CapturedOptions = options;
			return 0;
		}
	}
}
