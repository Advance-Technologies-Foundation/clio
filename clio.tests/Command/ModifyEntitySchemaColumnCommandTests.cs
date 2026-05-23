using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
internal class ModifyEntitySchemaColumnCommandTests : BaseCommandTests<ModifyEntitySchemaColumnOptions>
{
	private ModifyEntitySchemaColumnCommand _command;
	private IRemoteEntitySchemaColumnManager _columnManager;
	private ILogger _logger;

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<ModifyEntitySchemaColumnCommand>();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_columnManager = Substitute.For<IRemoteEntitySchemaColumnManager>();
		_logger = Substitute.For<ILogger>();
		containerBuilder.AddTransient(_ => _columnManager);
		containerBuilder.AddTransient(_ => _logger);
	}

	[Test]
	[Description("Calls the remote column manager when modify command options are valid.")]
	public void Execute_CallsColumnManager_WhenOptionsAreValid() {
		// Arrange
		var options = new ModifyEntitySchemaColumnOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Action = "modify",
			ColumnName = "Name",
			Title = "Vehicle name"
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "valid options should invoke the remote mutation flow");
		_columnManager.Received(1).ModifyColumn(options);
		_logger.Received(1).WriteInfo("Done");
	}

	[Test]
	[Description("Rejects an unsupported action before calling the remote column manager.")]
	public void Execute_ReturnsFailure_WhenActionIsInvalid() {
		// Arrange
		var options = new ModifyEntitySchemaColumnOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Action = "rename",
			ColumnName = "Name",
			Title = "Vehicle name"
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "unsupported actions must fail during command validation");
		_columnManager.DidNotReceiveWithAnyArgs().ModifyColumn(default);
		_logger.Received(1)
			.WriteError(Arg.Is<string>(message => message.Contains("Action must be one of: add, modify, remove.")));
	}

	[Test]
	[Description("Rejects modify action when no mutable properties were supplied.")]
	public void Execute_ReturnsFailure_WhenModifyHasNoProperties() {
		// Arrange
		var options = new ModifyEntitySchemaColumnOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Action = "modify",
			ColumnName = "Name"
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "modify needs at least one requested change");
		_columnManager.DidNotReceiveWithAnyArgs().ModifyColumn(default);
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("Modify action requires at least one property option to change.")));
	}

	[Test]
	[Description("Rejects property options when remove action is requested.")]
	public void Execute_ReturnsFailure_WhenRemoveIncludesPropertyOptions() {
		// Arrange
		var options = new ModifyEntitySchemaColumnOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Action = "remove",
			ColumnName = "Name",
			Title = "Vehicle name"
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "remove should not accept mutation properties");
		_columnManager.DidNotReceiveWithAnyArgs().ModifyColumn(default);
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("Remove action does not accept column property options.")));
	}

	[Test]
	[Description("Treats default-value-source as a mutable option so callers can explicitly clear or apply defaults without changing other fields.")]
	public void Execute_CallsColumnManager_WhenModifyOnlyChangesDefaultValueSource() {
		// Arrange
		var options = new ModifyEntitySchemaColumnOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Action = "modify",
			ColumnName = "Name",
			DefaultValueSource = "None"
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "explicit default source changes should count as a valid modification request");
		_columnManager.Received(1).ModifyColumn(options);
		_logger.Received(1).WriteInfo("Done");
	}

	[Test]
	[Description("Treats default-value-config as a mutable option so MCP callers can apply structured defaults without changing other fields.")]
	public void Execute_CallsColumnManager_WhenModifyOnlyChangesDefaultValueConfig() {
		// Arrange
		var options = new ModifyEntitySchemaColumnOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			Action = "modify",
			ColumnName = "UsrStartDate",
			DefaultValueConfig = new EntitySchemaDefaultValueConfig {
				Source = "SystemValue",
				ValueSource = "CurrentDateTime"
			}
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "structured default value changes should count as a valid modification request");
		_columnManager.Received(1).ModifyColumn(options);
		_logger.Received(1).WriteInfo("Done");
	}
}
