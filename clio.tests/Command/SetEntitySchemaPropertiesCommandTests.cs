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
internal class SetEntitySchemaPropertiesCommandTests : BaseCommandTests<SetEntitySchemaPropertiesOptions>
{
	private SetEntitySchemaPropertiesCommand _command;
	private IRemoteEntitySchemaColumnManager _columnManager;
	private ILogger _logger;

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<SetEntitySchemaPropertiesCommand>();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_columnManager = Substitute.For<IRemoteEntitySchemaColumnManager>();
		_logger = Substitute.For<ILogger>();
		containerBuilder.AddTransient(_ => _columnManager);
		containerBuilder.AddTransient(_ => _logger);
	}

	[TearDown]
	public void ClearReceived() {
		_columnManager.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
	}

	[Test]
	[Description("Delegates to the column manager and returns success when all required options are provided.")]
	public void Execute_ShouldDelegateToManagerAndReturnZero_WhenOptionsAreValid() {
		// Arrange
		SetEntitySchemaPropertiesOptions options = new() {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			PrimaryDisplayColumn = "UsrName"
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "a valid set request should complete successfully");
		_columnManager.Received(1).SetSchemaProperties(options);
	}

	[Test]
	[Description("Maps the hidden --package-name and --name aliases to the canonical Package/SchemaName options so an alias-only invocation is accepted and delegated.")]
	public void Execute_ShouldMapHiddenAliases_WhenOnlyAliasOptionsAreProvided() {
		// Arrange
		SetEntitySchemaPropertiesOptions options = new() {
			PackageNameAlias = "UsrPkg",
			SchemaNameAlias = "UsrVehicle",
			PrimaryDisplayColumn = "UsrName"
		};

		// Assert — the aliases delegate to the canonical properties
		options.Package.Should().Be("UsrPkg", because: "--package-name must map to the canonical Package option");
		options.SchemaName.Should().Be("UsrVehicle", because: "--name must map to the canonical SchemaName option");

		// Act
		int result = _command.Execute(options);

		// Assert — an alias-only invocation is accepted and delegates to the manager
		result.Should().Be(0, because: "an alias-only invocation should pass validation and delegate to the manager");
		_columnManager.Received(1).SetSchemaProperties(options);
	}

	[Test]
	[Description("Returns a failure exit code and logs the error when the package is missing.")]
	public void Execute_ShouldReturnFailure_WhenPackageIsMissing() {
		// Arrange
		SetEntitySchemaPropertiesOptions options = new() {
			Package = "",
			SchemaName = "UsrVehicle",
			PrimaryDisplayColumn = "UsrName"
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "a write must target a concrete package layer");
		_columnManager.DidNotReceiveWithAnyArgs().SetSchemaProperties(default);
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("Package is required.")));
	}

	[Test]
	[Description("Returns a failure exit code and logs the error when the schema name is missing.")]
	public void Execute_ShouldReturnFailure_WhenSchemaNameIsMissing() {
		// Arrange
		SetEntitySchemaPropertiesOptions options = new() {
			Package = "UsrPkg",
			SchemaName = "",
			PrimaryDisplayColumn = "UsrName"
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "schema identity is required for a schema-property write");
		_columnManager.DidNotReceiveWithAnyArgs().SetSchemaProperties(default);
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("Schema name is required.")));
	}

	[Test]
	[Description("Returns a failure exit code when no settable schema property is supplied.")]
	public void Execute_ShouldReturnFailure_WhenNoPropertyIsSupplied() {
		// Arrange
		SetEntitySchemaPropertiesOptions options = new() {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			PrimaryDisplayColumn = null
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "at least one settable property is required for the setter to do anything");
		_columnManager.DidNotReceiveWithAnyArgs().SetSchemaProperties(default);
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("At least one schema property to set is required")));
	}

	[Test]
	[Description("Returns a failure exit code and logs the error when the manager rejects the request.")]
	public void Execute_ShouldReturnFailure_WhenManagerThrows() {
		// Arrange
		SetEntitySchemaPropertiesOptions options = new() {
			Package = "UsrPkg",
			SchemaName = "UsrVehicle",
			PrimaryDisplayColumn = "Missing"
		};
		_columnManager
			.When(manager => manager.SetSchemaProperties(options))
			.Do(_ => throw new EntitySchemaDesignerException("Column 'Missing' was not found in schema 'UsrVehicle'."));

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "a manager failure must surface as a non-zero exit code");
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("was not found in schema")));
	}
}
