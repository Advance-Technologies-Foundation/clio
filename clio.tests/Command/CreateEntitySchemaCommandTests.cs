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

	[Test]
	public void Execute_CallsRemoteCreator_WhenOptionsAreValid()
	{
		var options = new CreateEntitySchemaOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Title = "Vehicle"
		};

		var result = _command.Execute(options);

		result.Should().Be(0);
		_creator.Received(1).Create(options);
	}

	[Test]
	public void Execute_ReturnsFailure_WhenExtendParentIsUsedWithoutParent()
	{
		var options = new CreateEntitySchemaOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Title = "Vehicle",
			ExtendParent = true
		};

		var result = _command.Execute(options);

		result.Should().Be(1);
		_creator.DidNotReceiveWithAnyArgs().Create(default);
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("--extend-parent requires --parent")));
	}

	[Test]
	public void Execute_ReturnsFailure_WhenSchemaNameIsTooLong()
	{
		var options = new CreateEntitySchemaOptions {
			Package = "UsrPkg",
			SchemaName = "UsrCodexEntitySchemaTest0307",
			Title = "Vehicle"
		};

		var result = _command.Execute(options);

		result.Should().Be(1);
		_creator.DidNotReceiveWithAnyArgs().Create(default);
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("must not exceed 22 characters")));
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
}
