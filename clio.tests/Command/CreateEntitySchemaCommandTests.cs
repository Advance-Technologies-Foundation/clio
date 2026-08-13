using System.Collections.Generic;
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
			SchemaName = "UsrVehicle",
			Title = "Vehicle",
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

	[Test]
	[Description("Rejects --extend-parent without an explicit --parent and does not call the remote creator.")]
	public void Execute_Should_ReturnFailure_WhenExtendParentIsUsedWithoutParent()
	{
		// Arrange
		var options = new CreateEntitySchemaOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Title = "Vehicle",
			ExtendParent = true
		};

		// Act
		var result = _command.Execute(options);

		// Assert
		result.Should().Be(1,
			because: "a replacement schema requires an explicit parent and must fail fast otherwise");
		_creator.DidNotReceiveWithAnyArgs().Create(default);
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("--extend-parent requires --parent")));
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
}
