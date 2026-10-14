using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using CommandLine;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
internal class CreateEntitySchemaCommandTests : BaseCommandTests<CreateEntitySchemaOptions>
{
	private CreateEntitySchemaCommand _command;
	private IRemoteEntitySchemaCreator _creator;
	private ILogger _logger;

	[TestCase(null)]
	[TestCase(true)]
	[TestCase(false)]
	[Description("Parses omitted and explicit DB-view values without treating false as omission.")]
	public void Parse_ShouldPreserveDbView_WhenSupplied(bool? expected) {
		// Arrange
		List<string> arguments = ["--package", "UsrPkg", "--name", "UsrView", "--title", "View"];
		if (expected.HasValue) {
			arguments.AddRange(["--is-db-view", expected.Value ? "true" : "false"]);
		}
		CreateEntitySchemaOptions parsed = null;
		// Act
		ParserResult<CreateEntitySchemaOptions> result = Parser.Default
			.ParseArguments<CreateEntitySchemaOptions>(arguments).WithParsed(options => parsed = options);
		// Assert
		result.Tag.Should().Be(ParserResultType.Parsed, because: "the documented boolean syntax must parse");
		parsed.IsDBView.Should().Be(expected, because: "omission and explicit false have different metadata semantics");
	}

	public override void Setup()
	{
		base.Setup();
		_command = Container.GetRequiredService<CreateEntitySchemaCommand>();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder)
	{
		base.AdditionalRegistrations(containerBuilder);
		_creator = Substitute.For<IRemoteEntitySchemaCreator>();
		_logger = Substitute.For<ILogger>();
		containerBuilder.AddTransient(_ => _creator);
		containerBuilder.AddTransient(_ => _logger);
	}

	[TearDown]
	public void ClearReceivedCalls()
	{
		_creator.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
	}

	// Captures the parent schema name AT THE MOMENT the creator is invoked (not read off the mutable options
	// object after Execute returns), so a test can pin the Validate -> NormalizeParentSchema -> Create ordering:
	// if normalization ever moved after the Create call, the captured value would be the un-normalized parent
	// and the assertion would fail.
	private List<string?> CaptureParentSchemaNamesAtCreateTime()
	{
		List<string?> capturedParents = [];
		_creator.When(creator => creator.Create(Arg.Any<CreateEntitySchemaOptions>()))
			.Do(callInfo => capturedParents.Add(callInfo.Arg<CreateEntitySchemaOptions>().ParentSchemaName));
		return capturedParents;
	}

	[TestCase(null, TestName = "Execute_Should_DefaultParentToBaseEntity_WhenParentIsNull")]
	[TestCase("", TestName = "Execute_Should_DefaultParentToBaseEntity_WhenParentIsEmpty")]
	[TestCase("   ", TestName = "Execute_Should_DefaultParentToBaseEntity_WhenParentIsWhitespace")]
	[Description("Defaults the parent to BaseEntity when --parent is null, empty, or whitespace-only, so the created root schema keeps an Id primary column and is reachable over OData (ENG-94424).")]
	public void Execute_Should_DefaultParentToBaseEntity_WhenParentOmitted(string parentSchemaName)
	{
		// Arrange
		var options = new CreateEntitySchemaOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Title = "Vehicle",
			ParentSchemaName = parentSchemaName
		};
		List<string?> parentAtCreateTime = CaptureParentSchemaNamesAtCreateTime();

		// Act
		var result = _command.Execute(options);

		// Assert
		result.Should().Be(0,
			because: "creating a root schema with the defaulted parent should succeed");
		parentAtCreateTime.Should().ContainSingle(
			because: "the remote creator must be invoked exactly once")
			.Which.Should().Be(CreateEntitySchemaOptions.DefaultParentSchemaName,
			because: "an absent, empty, or whitespace-only --parent must be defaulted to BaseEntity BEFORE the schema reaches the creator, otherwise it would produce a parentless, OData-unusable schema");
	}

	[Test]
	[Description("Defaults a virtual schema's parent to BaseEntity when --parent is omitted, matching the create-entity-schema MCP tool; --is-virtual suppresses only the physical table, not parent defaulting (ENG-94424).")]
	public void Execute_Should_DefaultParentToBaseEntity_WhenVirtualAndParentOmitted()
	{
		// Arrange
		var options = new CreateEntitySchemaOptions {
			Package = "UsrPkg",
			SchemaName = "UsrExternalVehicle",
			Title = "External vehicle",
			IsVirtual = true
		};
		List<string?> parentAtCreateTime = CaptureParentSchemaNamesAtCreateTime();

		// Act
		var result = _command.Execute(options);

		// Assert
		result.Should().Be(0,
			because: "creating a virtual schema with the defaulted parent should succeed");
		parentAtCreateTime.Should().ContainSingle(
			because: "the remote creator must be invoked exactly once")
			.Which.Should().Be(CreateEntitySchemaOptions.DefaultParentSchemaName,
			because: "a virtual schema with an omitted --parent must also default to BaseEntity to stay consistent with the MCP tool; the virtual flag only controls physical-table materialization");
		options.IsVirtual.Should().BeTrue(
			because: "defaulting the parent must not disturb the virtual flag");
	}

	[Test]
	[Description("Keeps an explicitly supplied --parent instead of overriding it with the BaseEntity default.")]
	public void Execute_Should_PreserveExplicitParent_WhenParentSupplied()
	{
		// Arrange
		var options = new CreateEntitySchemaOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Title = "Vehicle",
			ParentSchemaName = "Contact"
		};
		List<string?> parentAtCreateTime = CaptureParentSchemaNamesAtCreateTime();

		// Act
		var result = _command.Execute(options);

		// Assert
		result.Should().Be(0,
			because: "creating a schema with an explicit parent should succeed");
		parentAtCreateTime.Should().ContainSingle(
			because: "the remote creator must be invoked exactly once")
			.Which.Should().Be("Contact",
			because: "an explicitly supplied parent must reach the creator unchanged, not be replaced by the BaseEntity default");
	}

	[Test]
	[Description("Replacement schema (--extend-parent with --parent) passes validation, NormalizeParentSchema no-ops, and the creator receives the original parent intact.")]
	public void Execute_Should_SkipNormalizationAndCallCreator_WhenExtendParentIsTrue()
	{
		// Arrange
		var options = new CreateEntitySchemaOptions {
			Package = "UsrPkg",
			SchemaName = "Contact",
			Title = "Contact",
			ExtendParent = true,
			ParentSchemaName = "Contact"
		};
		List<string?> parentAtCreateTime = CaptureParentSchemaNamesAtCreateTime();

		// Act
		var result = _command.Execute(options);

		// Assert
		result.Should().Be(0,
			because: "a replacement schema with an explicit parent must complete successfully");
		parentAtCreateTime.Should().ContainSingle(
			because: "the remote creator must be invoked exactly once")
			.Which.Should().Be("Contact",
			because: "NormalizeParentSchema must not overwrite the explicit parent when ExtendParent is true; a future guard regression that drops the ExtendParent check would overwrite it with BaseEntity and fail here");
	}

	[TestCase(null)]
	[TestCase("")]
	[TestCase("   ")]
	[Description("Infers the same-name parent before invoking the creator when a replacement omits its parent.")]
	public void Execute_ShouldInferSameNameParent_WhenReplacementParentIsOmitted(string? parent)
	{
		// Arrange
		var options = new CreateEntitySchemaOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Title = "Vehicle",
			ExtendParent = true,
			ParentSchemaName = parent
		};
		List<string?> captured = CaptureParentSchemaNamesAtCreateTime();

		// Act
		var result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "a replacement's name identifies its parent");
		captured.Should().Equal(["UsrVehicle"], because: "the parent must be inferred before the creator runs");
	}

	[Test]
	[Description("Rejects a replacement whose explicit parent has a different name before any remote work.")]
	public void Execute_ShouldRejectReplacement_WhenParentNameDiffers() {
		// Arrange
		CreateEntitySchemaOptions options = new() {
			Package = "UsrPkg", SchemaName = "UsrVehicle", Title = "Vehicle",
			ExtendParent = true, ParentSchemaName = "Contact"
		};
		List<string?> captured = CaptureParentSchemaNamesAtCreateTime();
		// Act
		int result = _command.Execute(options);
		// Assert
		result.Should().Be(1, because: "replacement schemas retain their parent's name");
		captured.Should().BeEmpty(because: "invalid replacement names must not reach the remote creator");
	}

	[Test]
	[Description("Preserves semicolons inside structured JSON --column payloads so valid captions and defaults are not split by the command-line parser.")]
	public void Parse_Should_Preserve_Semicolons_In_Json_Column_Payload() {
		// Arrange
		string jsonColumn = """{"name":"Status","type":"ShortText","title":"Needs;Review","default-value-source":"Const","default-value":"A;B"}""";
		string[] arguments = [
			"--package", "UsrPkg",
			"--name", "UsrVehicle",
			"--title", "Vehicle",
			"--column", jsonColumn
		];
		CreateEntitySchemaOptions? parsedOptions = null;

		// Act
		ParserResult<CreateEntitySchemaOptions> parseResult = Parser.Default
			.ParseArguments<CreateEntitySchemaOptions>(arguments)
			.WithParsed(result => parsedOptions = result);

		// Assert
		parseResult.Tag.Should().Be(ParserResultType.Parsed,
			because: "valid structured JSON column payloads should remain intact during CLI parsing");
		parsedOptions.Should().NotBeNull(
			because: "a successful parse should produce create-entity-schema options");
		parsedOptions!.Columns.Should().BeEquivalentTo([jsonColumn],
			because: "semicolons inside a JSON title or default value are part of the payload, not column separators");
	}

	[TestCase(false)]
	[TestCase(true)]
	[Description("Parses the optional --is-virtual flag and defaults it to false for persistent entity schemas.")]
	public void Parse_Should_Map_IsVirtual_Option(bool expected) {
		// Arrange
		List<string> arguments = [
			"--package", "UsrPkg",
			"--name", "UsrVehicle",
			"--title", "Vehicle"
		];
		if (expected) {
			arguments.Add("--is-virtual");
		}
		CreateEntitySchemaOptions? parsedOptions = null;

		// Act
		ParserResult<CreateEntitySchemaOptions> parseResult = Parser.Default
			.ParseArguments<CreateEntitySchemaOptions>(arguments)
			.WithParsed(result => parsedOptions = result);

		// Assert
		parseResult.Tag.Should().Be(ParserResultType.Parsed,
			because: "the optional virtual-schema flag should be accepted by the command parser");
		parsedOptions!.IsVirtual.Should().Be(expected,
			because: "the command must distinguish persistent schemas from explicitly virtual schemas");
	}

	[TestCase(false)]
	[TestCase(true)]
	[Description("Accepts repeated column groups across other options, preserving sequence order and JSON punctuation.")]
	public void Parse_ShouldAcceptRepeatedColumns_WhenGroupsAreSeparated(bool useEquals) {
		// Arrange
		const string json = """{"name":"Notes","type":"Text","title":"A;B,C: D"}""";
		List<string> args = ["create-entity-schema", "--name", "UsrVehicle", "--column", json, "Amount:Integer",
			"--title", "Vehicle", "--package", "UsrPkg"];
		args.AddRange(useEquals ? ["--column=Active:Boolean"] : ["--column", "Active:Boolean"]);
		CreateEntitySchemaOptions? options = null;
		// Act
		var result = Parser.Default.ParseArguments<CreateEntitySchemaOptions>(
			Program.NormalizeCommandLineArgs(args.ToArray()).Skip(1)).WithParsed(value => options = value);
		// Assert
		result.Tag.Should().Be(ParserResultType.Parsed, because: "documented repeated flags must parse successfully");
		options!.Columns.Should().Equal([json, "Amount:Integer", "Active:Boolean"],
			because: "every column token must reach the creator intact and in input order");
		options.Title.Should().Be("Vehicle", because: "intervening options must keep their own values");
	}

	[TestCase("--column")]
	[TestCase("--column=")]
	[Description("Does not hide a missing repeated column value by combining it with a valid group.")]
	public void Parse_ShouldRejectMissingColumn_WhenAnotherGroupIsValid(string emptyGroup) {
		// Arrange
		string[] args = ["create-entity-schema", "--name", "UsrVehicle", "--title", "Vehicle",
			"--column", "Notes:Text", emptyGroup];
		// Act
		var result = Parser.Default.ParseArguments<CreateEntitySchemaOptions>(Program.NormalizeCommandLineArgs(args).Skip(1));
		// Assert
		result.Tag.Should().Be(ParserResultType.NotParsed, because: "an incomplete request must fail before schema creation");
	}

	[Test]
	[Description("Creates the schema under the --schema-name alias when --name is omitted, so create-entity-schema accepts the flag spelling the other entity-schema commands use (ENG-101526).")]
	public void Execute_Should_UseSchemaNameAlias_WhenNameOmitted() {
		// Arrange
		CreateEntitySchemaOptions options = new() {
			Package = "UsrPkg",
			SchemaNameAlias = "UsrVehicle",
			Title = "Vehicle"
		};
		List<string> schemaNamesAtCreateTime = [];
		_creator.When(creator => creator.Create(Arg.Any<CreateEntitySchemaOptions>()))
			.Do(callInfo => schemaNamesAtCreateTime.Add(callInfo.Arg<CreateEntitySchemaOptions>().SchemaName));

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "--schema-name is an accepted spelling of --name");
		schemaNamesAtCreateTime.Should().ContainSingle(because: "the remote creator must be invoked exactly once")
			.Which.Should().Be("UsrVehicle",
				because: "the alias value must reach the creator as the schema name");
	}

	[Test]
	[Description("Fails with an error naming both --name and --schema-name, and makes no remote call, when neither is supplied (ENG-101526).")]
	public void Execute_Should_Fail_WhenNeitherNameNorAliasSupplied() {
		// Arrange
		CreateEntitySchemaOptions options = new() {
			Package = "UsrPkg",
			Title = "Vehicle"
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "a schema cannot be created without a name");
		_creator.DidNotReceiveWithAnyArgs().Create(default!);
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("--name") && message.Contains("--schema-name")));
	}

	[Test]
	[Description("Rejects --name and --schema-name with different values before any remote call, naming both values (ENG-101526).")]
	public void Execute_Should_Fail_WhenNameAndAliasConflict() {
		// Arrange
		CreateEntitySchemaOptions options = new() {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			SchemaNameAlias = "UsrTruck",
			Title = "Vehicle"
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "two different schema names are ambiguous");
		_creator.DidNotReceiveWithAnyArgs().Create(default!);
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("'UsrVehicle'") && message.Contains("'UsrTruck'")
			&& message.Contains("--name") && message.Contains("--schema-name")));
	}

	[Test]
	[Description("Accepts --name and --schema-name when they carry the same value ignoring case, and keeps the --name spelling (ENG-101526).")]
	public void Execute_Should_Succeed_WhenNameAndAliasMatchIgnoringCase() {
		// Arrange
		CreateEntitySchemaOptions options = new() {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			SchemaNameAlias = "usrvehicle",
			Title = "Vehicle"
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "the same name supplied twice is not a conflict");
		_creator.Received(1).Create(Arg.Is<CreateEntitySchemaOptions>(created => created.SchemaName == "UsrVehicle"));
	}

	[Test]
	[Description("The parser accepts --schema-name as the only name option of create-entity-schema (ENG-101526).")]
	public void Parse_Should_AcceptSchemaNameAlias_WhenNameOmitted() {
		// Arrange
		string[] arguments = ["--package", "UsrPkg", "--schema-name", "UsrVehicle", "--title", "Vehicle"];
		CreateEntitySchemaOptions? parsed = null;

		// Act
		ParserResult<CreateEntitySchemaOptions> result = Parser.Default
			.ParseArguments<CreateEntitySchemaOptions>(arguments).WithParsed(options => parsed = options);

		// Assert
		result.Tag.Should().Be(ParserResultType.Parsed,
			because: "--name is no longer parser-required once the alias can supply it");
		parsed!.SchemaNameAlias.Should().Be("UsrVehicle", because: "the alias value must be captured for the command");
	}

	[Test]
	[Description("The --schema-name alias is hidden from generated create-entity-schema help (ENG-101526).")]
	public void SchemaNameAlias_Should_BeHiddenFromGeneratedHelp() {
		// Arrange
		System.Reflection.PropertyInfo property = typeof(CreateEntitySchemaOptions)
			.GetProperty(nameof(CreateEntitySchemaOptions.SchemaNameAlias))!;

		// Act
		OptionAttribute attribute = (OptionAttribute)System.Attribute.GetCustomAttribute(property, typeof(OptionAttribute))!;

		// Assert
		attribute.LongName.Should().Be("schema-name", because: "the alias must use the spelling of the other entity-schema commands");
		attribute.Hidden.Should().BeTrue(because: "--name stays the documented option; the alias must not appear in --help");
		attribute.Required.Should().BeFalse(because: "either --name or the alias may supply the schema name");
	}

	[TestCase("update-entity-schema")]
	[TestCase("create-data-binding")]
	[Description("Scopes repeated-column normalization to entity creation.")]
	public void Normalize_ShouldPreserveArguments_WhenCommandIsUnrelated(string verb) {
		// Arrange
		string[] args = [verb, "--column", "Notes:Text", "--column", "Amount:Integer"];
		// Act
		string[] result = Program.NormalizeCommandLineArgs(args);
		// Assert
		result.Should().Equal(args, because: "this workaround must not alter other command contracts");
	}
}
