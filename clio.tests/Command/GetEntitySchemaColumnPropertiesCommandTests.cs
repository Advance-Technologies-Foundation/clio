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
internal class GetEntitySchemaColumnPropertiesCommandTests : BaseCommandTests<GetEntitySchemaColumnPropertiesOptions>
{
	private GetEntitySchemaColumnPropertiesCommand _command;
	private IRemoteEntitySchemaColumnManager _columnManager;
	private ILogger _logger;

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<GetEntitySchemaColumnPropertiesCommand>();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_columnManager = Substitute.For<IRemoteEntitySchemaColumnManager>();
		_logger = Substitute.For<ILogger>();
		containerBuilder.AddTransient(_ => _columnManager);
		containerBuilder.AddTransient(_ => _logger);
	}

	[Test]
	[Description("Prints column properties when all required identifiers are provided.")]
	public void Execute_ReadsStructuredColumnProperties_WhenOptionsAreValid() {
		// Arrange
		GetEntitySchemaColumnPropertiesOptions options = new() {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			ColumnName = "Name"
		};
		_columnManager.GetColumnProperties(options).Returns(new EntitySchemaColumnPropertiesInfo(
			"UsrVehicle",
			"UsrPkg",
			"Name",
			"own",
			"Vehicle name",
			"Readable vehicle name",
			"Text",
			true,
			true,
			false,
			true,
			"Const",
			"Vehicle",
			null,
			false,
			false,
			false,
			true,
			true,
			true,
			false,
			false,
			false));

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "valid read options should call the remote reader");
		_columnManager.Received(1).GetColumnProperties(options);
		_logger.Received(1).WriteInfo("Entity schema column properties");
		_logger.Received(1).WriteInfo("Source: own");
		_logger.Received(1).WriteInfo("Default value source: Const");
	}

	[Test]
	[Description("Rejects requests that omit the column name.")]
	public void Execute_ReturnsFailure_WhenColumnNameIsMissing() {
		// Arrange
		GetEntitySchemaColumnPropertiesOptions options = new() {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			ColumnName = ""
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "column identity is required for a column read");
		_columnManager.DidNotReceiveWithAnyArgs().GetColumnProperties(default);
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("Column name is required.")));
	}

	[Test]
	[Description("Allows package omission for merged discovery and prints runtime-unavailable booleans as unknown.")]
	public void Execute_AllowsMergedDiscovery_WhenPackageIsMissing() {
		// Arrange
		GetEntitySchemaColumnPropertiesOptions options = new() {
			SchemaName = "Contact",
			ColumnName = "UsrStatus"
		};
		_columnManager.GetColumnProperties(options).Returns(new EntitySchemaColumnPropertiesInfo(
			"Contact", "(merged: all packages)", "UsrStatus", "own", "Status", null, "Lookup",
			false, false, true, null, null, null, "UsrStatus", true, false, null, false, null,
			false, false, false, false));

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "an omitted package now selects merged discovery rather than failing validation");
		_columnManager.Received(1).GetColumnProperties(options);
		_logger.Received(1).WriteInfo("Track changes: <unknown>");
		_logger.Received(1).WriteInfo("Do not control integrity: <unknown>");
		_logger.Received(1).WriteInfo("Localizable text: <unknown>");
	}
}
