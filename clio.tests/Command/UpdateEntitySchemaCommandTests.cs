using System.Collections.Generic;
using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
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
		_columnManager.Received(1).ModifyColumn(Arg.Is<ModifyEntitySchemaColumnOptions>(mutation =>
			mutation.Environment == "dev"
			&& mutation.Package == "UsrPkg"
			&& mutation.SchemaName == "UsrVehicle"
			&& mutation.Action == "add"
			&& mutation.ColumnName == "UsrStatus"
			&& mutation.Type == "Lookup"
			&& mutation.Title == "Status"
			&& mutation.ReferenceSchemaName == "UsrVehicleStatus"
			&& mutation.Required == true));
		_columnManager.Received(1).ModifyColumn(Arg.Is<ModifyEntitySchemaColumnOptions>(mutation =>
			mutation.Environment == "dev"
			&& mutation.Package == "UsrPkg"
			&& mutation.SchemaName == "UsrVehicle"
			&& mutation.Action == "modify"
			&& mutation.ColumnName == "UsrDueDate"
			&& mutation.Title == "Due date"
			&& mutation.DefaultValueSource == "None"));
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
		_columnManager.DidNotReceiveWithAnyArgs().ModifyColumn(default!);
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("At least one operation is required.")));
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
		_columnManager.DidNotReceiveWithAnyArgs().ModifyColumn(default!);
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("Operation payload at index 0 is not valid JSON.")));
	}
}
