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
internal sealed class UpdateEntitySchemaCommandTests : BaseClioModuleTests
{
	private UpdateEntitySchemaCommand _command;
	private IRemoteEntitySchemaColumnManager _columnManager;
	private ILogger _logger;

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<UpdateEntitySchemaCommand>();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_columnManager = Substitute.For<IRemoteEntitySchemaColumnManager>();
		_logger = Substitute.For<ILogger>();
		containerBuilder.AddTransient(_ => _columnManager);
		containerBuilder.AddTransient(_ => _logger);
	}

	[Test]
	[Description("Applies every structured operation in order by mapping them onto existing column mutation options.")]
	public void Execute_CallsColumnManager_ForEachOperation_WhenOptionsAreValid() {
		// Arrange
		UpdateEntitySchemaOptions options = new() {
			Environment = "dev",
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Operations = [
				"""{"action":"add","column-name":"UsrStatus","type":"Lookup","title":"Status","reference-schema-name":"UsrVehicleStatus","required":true}""",
				"""{"action":"modify","column-name":"UsrDueDate","title":"Due date","default-value-source":"None"}"""
			]
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "valid batch requests should execute through the existing column mutation flow");
		_columnManager.Received(1).ModifyColumns(Arg.Is<IEnumerable<ModifyEntitySchemaColumnOptions>>(mutations =>
			mutations.Count() == 2
			&& mutations.ElementAt(0).Environment == "dev"
			&& mutations.ElementAt(0).Package == "UsrPkg"
			&& mutations.ElementAt(0).SchemaName == "UsrVehicle"
			&& mutations.ElementAt(0).Action == "add"
			&& mutations.ElementAt(0).ColumnName == "UsrStatus"
			&& mutations.ElementAt(0).Type == "Lookup"
			&& mutations.ElementAt(0).Title == "Status"
			&& mutations.ElementAt(0).ReferenceSchemaName == "UsrVehicleStatus"
			&& mutations.ElementAt(0).Required == true
			&& mutations.ElementAt(1).Environment == "dev"
			&& mutations.ElementAt(1).Package == "UsrPkg"
			&& mutations.ElementAt(1).SchemaName == "UsrVehicle"
			&& mutations.ElementAt(1).Action == "modify"
			&& mutations.ElementAt(1).ColumnName == "UsrDueDate"
			&& mutations.ElementAt(1).Title == "Due date"
			&& mutations.ElementAt(1).DefaultValueSource == "None"));
		_logger.Received(1).WriteInfo("Done");
	}

	[Test]
	[Description("Rejects empty operation batches before any remote mutation is attempted.")]
	public void Execute_ReturnsFailure_WhenNoOperationsWereProvided() {
		// Arrange
		UpdateEntitySchemaOptions options = new() {
			Environment = "dev",
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Operations = []
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "a batch update without operations is invalid");
		_columnManager.DidNotReceiveWithAnyArgs().ModifyColumns(default!);
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("At least one operation is required.")));
	}

	[Test]
	[Description("Trims operation titles and maps whitespace-only values to null before forwarding batch mutations.")]
	public void Execute_MapsTrimmedAndWhitespaceTitles_WhenBuildingMutations() {
		// Arrange
		UpdateEntitySchemaOptions options = new() {
			Environment = "dev",
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Operations = [
				"""{"action":"modify","column-name":"UsrStatus","title":"  Status  "}""",
				"""{"action":"add","column-name":"UsrPriority","type":"Text","title":"   "}"""
			]
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "title normalization should preserve successful execution for valid operations");
		_columnManager.Received(1).ModifyColumns(Arg.Is<IEnumerable<ModifyEntitySchemaColumnOptions>>(mutations =>
			mutations.Count() == 2
			&& mutations.ElementAt(0).ColumnName == "UsrStatus"
			&& mutations.ElementAt(0).Title == "Status"
			&& mutations.ElementAt(1).ColumnName == "UsrPriority"
			&& mutations.ElementAt(1).Action == "add"
			&& mutations.ElementAt(1).Title == null));
	}

	[Test]
	[Description("Rejects malformed JSON operation payloads with a clear error before executing any mutation.")]
	public void Execute_ReturnsFailure_WhenOperationJsonIsInvalid() {
		// Arrange
		UpdateEntitySchemaOptions options = new() {
			Environment = "dev",
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Operations = ["""{"action":"add","column-name":"UsrStatus","type":"Lookup""" ]
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "malformed operation payloads should fail validation early");
		_columnManager.DidNotReceiveWithAnyArgs().ModifyColumns(default!);
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("Operation payload at index 0 is not valid JSON.")));
	}

	[Test]
	[Description("Preserves semicolons inside structured JSON --operation payloads so valid titles and defaults are not split by the command-line parser.")]
	public void Parse_Should_Preserve_Semicolons_In_Json_Operation_Payload() {
		// Arrange
		string jsonOperation = """{"action":"modify","column-name":"Status","title":"Needs;Review","default-value-source":"Const","default-value":"A;B"}""";
		string[] arguments = [
			"--package", "UsrPkg",
			"--schema-name", "UsrVehicle",
			"--operation", jsonOperation
		];
		UpdateEntitySchemaOptions? parsedOptions = null;

		// Act
		ParserResult<UpdateEntitySchemaOptions> parseResult = Parser.Default
			.ParseArguments<UpdateEntitySchemaOptions>(arguments)
			.WithParsed(result => parsedOptions = result);

		// Assert
		parseResult.Tag.Should().Be(ParserResultType.Parsed,
			because: "valid structured JSON operation payloads should remain intact during CLI parsing");
		parsedOptions.Should().NotBeNull(
			because: "a successful parse should produce update-entity-schema options");
		parsedOptions!.Operations.Should().BeEquivalentTo([jsonOperation],
			because: "semicolons inside a JSON title or default value are part of the operation payload, not separators");
	}
}
