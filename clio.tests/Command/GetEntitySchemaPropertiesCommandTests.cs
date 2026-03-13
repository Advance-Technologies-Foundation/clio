using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
internal class GetEntitySchemaPropertiesCommandTests : BaseCommandTests<GetEntitySchemaPropertiesOptions>
{
	private GetEntitySchemaPropertiesCommand _command;
	private IRemoteEntitySchemaColumnManager _columnManager;
	private ILogger _logger;

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<GetEntitySchemaPropertiesCommand>();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_columnManager = Substitute.For<IRemoteEntitySchemaColumnManager>();
		_logger = Substitute.For<ILogger>();
		containerBuilder.AddTransient(_ => _columnManager);
		containerBuilder.AddTransient(_ => _logger);
	}

	[Test]
	[Description("Prints schema properties when all required identifiers are provided.")]
	public void Execute_CallsColumnManager_WhenOptionsAreValid() {
		// Arrange
		var options = new GetEntitySchemaPropertiesOptions {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle"
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "valid schema read options should call the remote reader");
		_columnManager.Received(1).PrintSchemaProperties(options);
	}

	[Test]
	[Description("Rejects requests that omit the schema name.")]
	public void Execute_ReturnsFailure_WhenSchemaNameIsMissing() {
		// Arrange
		var options = new GetEntitySchemaPropertiesOptions {
			Package = "UsrPkg",
			SchemaName = ""
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "schema identity is required for a schema read");
		_columnManager.DidNotReceiveWithAnyArgs().PrintSchemaProperties(default);
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("Schema name is required.")));
	}
}
