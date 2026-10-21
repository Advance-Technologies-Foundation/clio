using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// ENG-102218: the MCP column payloads (update-entity-schema operations, create-entity-schema / create-lookup
/// columns, modify-entity-schema-column arguments and the same payloads inside sync-schemas) reject a field they
/// cannot bind instead of dropping it. Payloads are bound from JSON with the MCP serializer options, so the tests
/// prove what lands in the overflow bag rather than assume it.
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class ColumnPayloadUnknownFieldTests {

	private static readonly JsonSerializerOptions McpOptions = BindingsModule.CreateMcpSerializerOptions();

	[Test]
	[Category("Unit")]
	[Description("A misspelled operation field is captured in the overflow bag, while a case variant of a known field and the documented 'name' alias bind normally.")]
	public void OperationBinding_Should_Capture_Only_Unknown_Fields() {
		// Arrange
		const string json = """
			{"action":"add","Column-Name":"UsrA","requird":true}
			""";
		const string aliasJson = """
			{"action":"add","name":"UsrB"}
			""";

		// Act
		UpdateEntitySchemaOperationArgs operation = JsonSerializer.Deserialize<UpdateEntitySchemaOperationArgs>(json, McpOptions)!;
		UpdateEntitySchemaOperationArgs aliasOperation = JsonSerializer.Deserialize<UpdateEntitySchemaOperationArgs>(aliasJson, McpOptions)!;

		// Assert
		operation.ExtensionData.Should().NotBeNull(because: "an unknown field must be captured, not dropped");
		operation.ExtensionData!.Keys.Should().BeEquivalentTo(["requird"],
			because: "only the misspelled field is unknown; 'Column-Name' binds case-insensitively");
		operation.ResolveColumnName().Should().Be("UsrA",
			because: "a field differing only by case follows the existing case-insensitive binding");
		aliasOperation.ExtensionData.Should().BeNullOrEmpty(because: "the documented 'name' alias is a known field");
		aliasOperation.ResolveColumnName().Should().Be("UsrB", because: "the 'name' alias must stay accepted");
	}

	[Test]
	[Category("Unit")]
	[Description("The suggestion candidates of an update operation include the inherited canonical fields and their aliases.")]
	public void GetKnownFields_Should_Include_Inherited_Fields_And_Aliases() {
		// Arrange
		string[] expected = ["action", "column-name", "name", "required", "is-required", "data-value-type", "type"];

		// Act
		IReadOnlyList<string> knownFields = ColumnPayloadFieldContract.GetKnownFields(typeof(UpdateEntitySchemaOperationArgs));

		// Assert
		knownFields.Should().Contain(expected,
			because: "the derived record reuses the base record's properties, so their JSON names are the candidates");
	}

	[Test]
	[Category("Unit")]
	[Description("ENG-102218 TC-01: update-entity-schema rejects an operation with an unknown field, names it and the nearest known field, and never resolves the command.")]
	public async Task UpdateEntitySchema_Should_Reject_Unknown_Field_With_Suggestion() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		UpdateEntitySchemaTool tool = new(new FakeUpdateEntitySchemaCommand(), ConsoleLogger.Instance, commandResolver, new OptionSuggestionService());
		UpdateEntitySchemaArgs args = Bind<UpdateEntitySchemaArgs>("""
			{"environment-name":"dev","package-name":"Pkg","schema-name":"UsrS","operations":[
			  {"action":"add","column-name":"UsrA","type":"Text","title-localizations":{"en-US":"A"},"requird":true}]}
			""");

		// Act
		CommandExecutionResult result = await tool.UpdateEntitySchema(args);

		// Assert
		result.ExitCode.Should().Be(1, because: "an operation with an unknown field must fail the call");
		ErrorText(result).Should().Be("Operation payload at index 0 has unknown field 'requird'. Did you mean 'required'?",
			because: "the message must match the CLI wording and suggest the nearest known field");
		commandResolver.DidNotReceive().Resolve<UpdateEntitySchemaCommand>(Arg.Any<EnvironmentOptions>());
	}

	[Test]
	[Category("Unit")]
	[Description("ENG-102218 TC-02: an operation that sends only 'colum-name' is reported as an unknown field, not only as a missing column.")]
	public async Task UpdateEntitySchema_Should_Name_Misspelled_ColumnName() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		UpdateEntitySchemaTool tool = new(new FakeUpdateEntitySchemaCommand(), ConsoleLogger.Instance, commandResolver, new OptionSuggestionService());
		UpdateEntitySchemaArgs args = Bind<UpdateEntitySchemaArgs>("""
			{"environment-name":"dev","package-name":"Pkg","schema-name":"UsrS","operations":[
			  {"action":"add","colum-name":"UsrB","type":"Text","title-localizations":{"en-US":"B"}}]}
			""");

		// Act
		CommandExecutionResult result = await tool.UpdateEntitySchema(args);

		// Assert
		ErrorText(result).Should().Be("Operation payload at index 0 has unknown field 'colum-name'. Did you mean 'column-name'?",
			because: "the unknown field check runs before the column identity check");
	}

	[Test]
	[Category("Unit")]
	[Description("ENG-102218 TC-03: an unknown field in a later operation fails the whole batch with that operation's index, and no operation is applied.")]
	public async Task UpdateEntitySchema_Should_Fail_Whole_Batch_When_Later_Operation_Is_Invalid() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		FakeUpdateEntitySchemaCommand command = new();
		commandResolver.Resolve<UpdateEntitySchemaCommand>(Arg.Any<EnvironmentOptions>()).Returns(command);
		UpdateEntitySchemaTool tool = new(command, ConsoleLogger.Instance, commandResolver, new OptionSuggestionService());
		UpdateEntitySchemaArgs args = Bind<UpdateEntitySchemaArgs>("""
			{"environment-name":"dev","package-name":"Pkg","schema-name":"UsrS","operations":[
			  {"action":"add","column-name":"UsrC","type":"Text","title-localizations":{"en-US":"C"}},
			  {"action":"modify","column-name":"UsrC","indexd":true}]}
			""");

		// Act
		CommandExecutionResult result = await tool.UpdateEntitySchema(args);

		// Assert
		ErrorText(result).Should().StartWith("Operation payload at index 1 has unknown field 'indexd'.",
			because: "the error must name the index of the operation that carries the unknown field");
		command.CapturedOptions.Should().BeNull(because: "no operation of a rejected batch may be applied");
	}

	[Test]
	[Category("Unit")]
	[Description("ENG-102218 TC-04: a batch that uses only known fields, including the 'name' alias, still reaches the command.")]
	public async Task UpdateEntitySchema_Should_Pass_Known_Fields_Through() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		FakeUpdateEntitySchemaCommand command = new();
		commandResolver.Resolve<UpdateEntitySchemaCommand>(Arg.Any<EnvironmentOptions>()).Returns(command);
		UpdateEntitySchemaTool tool = new(command, ConsoleLogger.Instance, commandResolver, new OptionSuggestionService());
		UpdateEntitySchemaArgs args = Bind<UpdateEntitySchemaArgs>("""
			{"environment-name":"dev","package-name":"Pkg","schema-name":"UsrS","operations":[
			  {"action":"add","column-name":"UsrD","type":"Text","title-localizations":{"en-US":"D"}},
			  {"action":"add","name":"UsrE","type":"Text","title-localizations":{"en-US":"E"}},
			  {"action":"modify","column-name":"UsrD","required":true,"title-localizations":{"en-US":"D2"}}]}
			""");

		// Act
		CommandExecutionResult result = await tool.UpdateEntitySchema(args);

		// Assert
		result.ExitCode.Should().Be(0, because: "a batch of known fields must behave exactly as before");
		command.CapturedOptions!.Operations.Should().HaveCount(3, because: "every operation must reach the command");
	}

	[Test]
	[Category("Unit")]
	[Description("modify-entity-schema-column rejects an unknown argument with a suggestion before resolving the command.")]
	public void ModifyEntitySchemaColumn_Should_Reject_Unknown_Field() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		ModifyEntitySchemaColumnTool tool = new(new FakeModifyEntitySchemaColumnCommand(), ConsoleLogger.Instance, commandResolver, new OptionSuggestionService());
		ModifyEntitySchemaColumnArgs args = Bind<ModifyEntitySchemaColumnArgs>("""
			{"environment-name":"dev","package-name":"Pkg","schema-name":"UsrS","action":"modify",
			 "column-name":"UsrA","requird":true}
			""");

		// Act
		CommandExecutionResult result = tool.ModifyEntitySchemaColumn(args);

		// Assert
		ErrorText(result).Should().Be("modify-entity-schema-column has unknown field 'requird'. Did you mean 'required'?",
			because: "modify-entity-schema-column binds to the same column record and must reject the same way");
		commandResolver.DidNotReceive().Resolve<ModifyEntitySchemaColumnCommand>(Arg.Any<EnvironmentOptions>());
	}

	[Test]
	[Category("Unit")]
	[Description("create-entity-schema rejects a column with an unknown field and names the column index.")]
	public async Task CreateEntitySchema_Should_Reject_Unknown_Column_Field() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		CreateEntitySchemaTool tool = new(new FakeCreateEntitySchemaCommand(), ConsoleLogger.Instance, commandResolver, new OptionSuggestionService());
		CreateEntitySchemaArgs args = Bind<CreateEntitySchemaArgs>("""
			{"environment-name":"dev","package-name":"Pkg","schema-name":"UsrS","title-localizations":{"en-US":"S"},
			 "columns":[{"name":"UsrA","type":"Text"},{"name":"UsrB","type":"Text","requird":true}]}
			""");

		// Act
		CommandExecutionResult result = await tool.CreateEntitySchema(args);

		// Assert
		ErrorText(result).Should().Be("Column payload at index 1 has unknown field 'requird'. Did you mean 'required'?",
			because: "create-entity-schema columns must not drop an unknown field silently");
		commandResolver.DidNotReceive().Resolve<CreateEntitySchemaCommand>(Arg.Any<EnvironmentOptions>());
	}

	[Test]
	[Category("Unit")]
	[Description("create-lookup rejects a column with an unknown field, because its columns bind to the same record as create-entity-schema.")]
	public async Task CreateLookup_Should_Reject_Unknown_Column_Field() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		CreateLookupTool tool = new(new FakeCreateEntitySchemaCommand(), ConsoleLogger.Instance, commandResolver, new OptionSuggestionService());
		CreateLookupArgs args = Bind<CreateLookupArgs>("""
			{"environment-name":"dev","package-name":"Pkg","schema-name":"UsrL","title-localizations":{"en-US":"L"},
			 "columns":[{"name":"UsrCode","type":"Text","mased":true}]}
			""");

		// Act
		CommandExecutionResult result = await tool.CreateLookup(args);

		// Assert
		ErrorText(result).Should().Be("Column payload at index 0 has unknown field 'mased'. Did you mean 'masked'?",
			because: "create-lookup columns must not drop an unknown field silently");
		commandResolver.DidNotReceive().Resolve<CreateEntitySchemaCommand>(Arg.Any<EnvironmentOptions>());
	}

	[Test]
	[Category("Unit")]
	[Description("ENG-102218 TC-05: sync-schemas rejects an update-entity operation whose update-operations carry an unknown field before reading the schema, and keeps it out of the resume plan.")]
	public void SyncSchemas_Should_Reject_Unknown_Field_In_UpdateOperations() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		ISchemaConvergenceService convergence = Substitute.For<ISchemaConvergenceService>();
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance, new OptionSuggestionService(), convergence);
		SchemaSyncArgs args = Bind<SchemaSyncArgs>("""
			{"environment-name":"dev","package-name":"Pkg","operations":[
			  {"type":"update-entity","schema-name":"UsrS","update-operations":[
			    {"action":"add","column-name":"UsrA","type":"Text","title-localizations":{"en-US":"A"},"requird":true}]}]}
			""");

		// Act
		SchemaSyncResponse response = tool.ExecuteBatch(args, _ => { });

		// Assert
		response.Success.Should().BeFalse(because: "an operation with an unknown field must fail");
		response.Results.Single().Error.Should().Be(
			"sync-schemas operations[0] is invalid: update-operations[0] has unknown field 'requird'. "
			+ "Did you mean 'required'? Nothing was applied for this operation.",
			because: "sync-schemas must name the operation, the entry and the unknown field");
		convergence.DidNotReceiveWithAnyArgs().ReadColumns(default!, default!);
		commandResolver.DidNotReceive().Resolve<UpdateEntitySchemaCommand>(Arg.Any<EnvironmentOptions>());
		response.ResumePlan?.Operations.Should().BeEmpty(
			because: "a field-shape rejection must not be offered for verbatim resubmission");
	}

	[Test]
	[Category("Unit")]
	[Description("sync-schemas rejects a create-entity operation whose columns carry an unknown field before classifying the schema.")]
	public void SyncSchemas_Should_Reject_Unknown_Field_In_Columns() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		ISchemaConvergenceService convergence = Substitute.For<ISchemaConvergenceService>();
		SchemaSyncTool tool = new(commandResolver, ConsoleLogger.Instance, new OptionSuggestionService(), convergence);
		SchemaSyncArgs args = Bind<SchemaSyncArgs>("""
			{"environment-name":"dev","package-name":"Pkg","operations":[
			  {"type":"create-entity","schema-name":"UsrS","title-localizations":{"en-US":"S"},
			   "columns":[{"name":"UsrA","type":"Text","is-requird":true}]}]}
			""");

		// Act
		SchemaSyncResponse response = tool.ExecuteBatch(args, _ => { });

		// Assert
		response.Results.Single().Error.Should().StartWith(
			"sync-schemas operations[0] is invalid: columns[0] has unknown field 'is-requird'.",
			because: "create-entity columns inside sync-schemas must be checked the same way");
		convergence.DidNotReceiveWithAnyArgs().Classify(default!);
	}

	private static T Bind<T>(string json) => JsonSerializer.Deserialize<T>(json, McpOptions)!;

	private static string ErrorText(CommandExecutionResult result) =>
		result.Output.OfType<ErrorMessage>().Single().Value.ToString()!;

	private sealed class FakeUpdateEntitySchemaCommand() : UpdateEntitySchemaCommand(
		Substitute.For<IRemoteEntitySchemaColumnManager>(), Substitute.For<ILogger>(),
		Substitute.For<IOptionSuggestionService>(), Substitute.For<IFileSystem>()) {
		public UpdateEntitySchemaOptions? CapturedOptions { get; private set; }

		public override int Execute(UpdateEntitySchemaOptions options) {
			CapturedOptions = options;
			return 0;
		}
	}

	private sealed class FakeModifyEntitySchemaColumnCommand() : ModifyEntitySchemaColumnCommand(
		Substitute.For<IRemoteEntitySchemaColumnManager>(), Substitute.For<ILogger>()) {
		public override int Execute(ModifyEntitySchemaColumnOptions options) => 0;
	}

	private sealed class FakeCreateEntitySchemaCommand() : CreateEntitySchemaCommand(
		Substitute.For<IRemoteEntitySchemaCreator>(), Substitute.For<ILogger>()) {
		public override int Execute(CreateEntitySchemaOptions options) => 0;
	}
}
