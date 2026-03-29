using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using ConsoleTables;
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
		ILookupRegistrationService registrationService = Substitute.For<ILookupRegistrationService>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(fakeCreateCommand);
		commandResolver.Resolve<ILookupRegistrationService>(Arg.Any<EnvironmentOptions>())
			.Returns(registrationService);
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance);
		SchemaSyncArgs args = new(
			"dev", "UsrPkg",
			[new SchemaSyncOperation("create-lookup", "UsrTodoStatus", TitleLocalizations: Localizations("Todo Status"))]);

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
		registrationService.Received(1).EnsureLookupRegistration("UsrPkg", "UsrTodoStatus", "Todo Status");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects inherited BaseLookup columns inside schema-sync create-lookup operations before any command executes.")]
	public void SchemaSync_CreateLookup_Should_Reject_Inherited_BaseLookup_Columns() {
		// Arrange
		var fakeCreateCommand = new FakeCreateEntitySchemaCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(fakeCreateCommand);
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance);
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
		SchemaSyncResponse response = tool.SchemaSync(args);

		// Assert
		response.Success.Should().BeFalse(
			because: "schema-sync should fail fast when a create-lookup operation tries to redefine inherited BaseLookup columns");
		response.Results.Should().HaveCount(1,
			because: "validation should stop the batch on the rejected create-lookup operation");
		response.Results[0].Operation.Should().Be("create-lookup",
			because: "the failed result should still identify the rejected operation");
		response.Results[0].Error.Should().Contain("BaseLookup",
			because: "the failure should explain the inherited-column guardrail");
		response.Results[0].Error.Should().Contain("Name",
			because: "the failure should identify the rejected inherited column");
		fakeCreateCommand.CapturedOptions.Should().BeNull(
			because: "schema-sync should not execute the create command after validation fails");
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
				TitleLocalizations: Localizations("Todo List"), ParentSchemaName: "BaseEntity")]);

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
						Type: "Lookup", TitleLocalizations: Localizations("Status"), ReferenceSchemaName: "UsrTodoStatus"),
					new UpdateEntitySchemaOperationArgs("add", "UsrDueDate",
						Type: "Date", TitleLocalizations: Localizations("Due date"))
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
		ILookupRegistrationService registrationService = Substitute.For<ILookupRegistrationService>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(fakeCreateCommand);
		commandResolver.Resolve<CreateDataBindingDbCommand>(Arg.Any<CreateDataBindingDbOptions>())
			.Returns(fakeSeedCommand);
		commandResolver.Resolve<ILookupRegistrationService>(Arg.Any<EnvironmentOptions>())
			.Returns(registrationService);
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance);
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
		registrationService.Received(1).EnsureLookupRegistration("UsrPkg", "UsrTodoStatus", "Todo Status");
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
				new SchemaSyncOperation("create-lookup", "UsrFirst", TitleLocalizations: Localizations("First")),
				new SchemaSyncOperation("create-lookup", "UsrSecond", TitleLocalizations: Localizations("Second"))
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
	[Description("Rejects legacy scalar title fields in schema-sync create operations even when title-localizations are also provided.")]
	public void SchemaSync_CreateLookup_Should_Reject_Legacy_Title_Field() {
		// Arrange
		var fakeCreateCommand = new FakeCreateEntitySchemaCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(fakeCreateCommand);
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance);
		SchemaSyncArgs args = new(
			"dev",
			"UsrPkg",
			[
				new SchemaSyncOperation(
					"create-lookup",
					"UsrTodoStatus",
					TitleLocalizations: Localizations("Todo Status")) {
					LegacyTitle = "Todo Status"
				}
			]);

		// Act
		SchemaSyncResponse response = tool.SchemaSync(args);

		// Assert
		response.Success.Should().BeFalse();
		response.Results.Should().ContainSingle();
		response.Results[0].Error.Should().Contain("legacy 'title'",
			because: "schema-sync should reject the old scalar field instead of silently accepting it");
		fakeCreateCommand.CapturedOptions.Should().BeNull();
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
				TitleLocalizations: Localizations("Broken"),
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
		ILookupRegistrationService registrationService = Substitute.For<ILookupRegistrationService>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(fakeCreateCommand);
		commandResolver.Resolve<UpdateEntitySchemaCommand>(Arg.Any<UpdateEntitySchemaOptions>())
			.Returns(fakeUpdateCommand);
		commandResolver.Resolve<ILookupRegistrationService>(Arg.Any<EnvironmentOptions>())
			.Returns(registrationService);
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance);
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
		registrationService.Received(1).EnsureLookupRegistration("UsrPkg", "UsrTodoStatus", "Status");
	}

	[Test]
	[Category("Unit")]
	[Description("Captures each schema-sync message under the matching operation result without leaking into adjacent results")]
	public void SchemaSync_Should_Assign_Messages_To_The_Correct_Operation() {
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
		SchemaSyncTool tool = new(commandResolver, logger);
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
		SchemaSyncResponse response = tool.SchemaSync(args);
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
	[Description("Fails create-lookup when Lookups registration throws so schema-sync does not report partial success")]
	public void SchemaSync_CreateLookup_Should_Fail_When_Lookup_Registration_Fails() {
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
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance);

		// Act
		SchemaSyncResponse response = tool.SchemaSync(new SchemaSyncArgs(
			"dev",
			"UsrPkg",
			[new SchemaSyncOperation("create-lookup", "UsrTodoStatus", TitleLocalizations: Localizations("Todo Status"))]));

		// Assert
		response.Success.Should().BeFalse(
			because: "lookup registration is part of successful create-lookup execution");
		response.Results.Should().HaveCount(1,
			because: "schema-sync should stop after the create-lookup registration failure");
		response.Results[0].Success.Should().BeFalse(
			because: "the create-lookup result should surface the registration failure");
		response.Results[0].Error.Should().Contain("Lookup registration failed",
			because: "the failing registration error should be returned to the caller");
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
				_logger.WriteInfo(message);
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
