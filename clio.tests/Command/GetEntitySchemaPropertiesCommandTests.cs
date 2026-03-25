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
	public void Execute_ReadsStructuredSchemaProperties_WhenOptionsAreValid() {
		// Arrange
		GetEntitySchemaPropertiesOptions options = new() {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle"
		};
		_columnManager.GetSchemaProperties(options).Returns(new EntitySchemaPropertiesInfo(
			"UsrVehicle",
			"Vehicle",
			"Vehicle catalog",
			"UsrPkg",
			"BaseEntity",
			true,
			"Id",
			"Name",
			2,
			1,
			3,
			true,
			false,
			true,
			false,
			false,
			true,
			false,
			true,
			false,
			false,
			true,
			[
				new EntitySchemaPropertyColumnInfo(
					"Name",
					System.Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
					"own",
					"Vehicle name",
					"Primary vehicle name",
					"Text",
					true,
					true,
					null),
				new EntitySchemaPropertyColumnInfo(
					"CreatedOn",
					System.Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
					"inherited",
					"Created on",
					null,
					"DateTime",
					false,
					false,
					null)
			]));

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "valid schema read options should call the remote reader");
		_columnManager.Received(1).GetSchemaProperties(options);
		_logger.Received(1).WriteInfo("Entity schema properties");
		_logger.Received(1).WriteInfo("Parent schema: BaseEntity");
		_logger.Received(1).WriteInfo("Own columns");
		_logger.Received(1).WriteInfo("Inherited columns");
		_logger.Received(1).WriteInfo(Arg.Is<string>(message =>
			message.Contains("- Name")
			&& message.Contains("type: Text")
			&& message.Contains("required: true")
			&& message.Contains("description: Primary vehicle name")));
	}

	[Test]
	[Description("Rejects requests that omit the schema name.")]
	public void Execute_ReturnsFailure_WhenSchemaNameIsMissing() {
		// Arrange
		GetEntitySchemaPropertiesOptions options = new() {
			Package = "UsrPkg",
			SchemaName = ""
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "schema identity is required for a schema read");
		_columnManager.DidNotReceiveWithAnyArgs().GetSchemaProperties(default);
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("Schema name is required.")));
	}
}
