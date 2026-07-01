using System;
using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
internal class ModifyEntitySchemaColumnCommandTests : BaseCommandTests<ModifyEntitySchemaColumnOptions>
{
	private ModifyEntitySchemaColumnCommand _command;
	private IRemoteEntitySchemaColumnManager _columnManager;
	private IEntitySchemaDependencyResolver _dependencyResolver;
	private ILogger _logger;

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<ModifyEntitySchemaColumnCommand>();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_columnManager = Substitute.For<IRemoteEntitySchemaColumnManager>();
		_dependencyResolver = Substitute.For<IEntitySchemaDependencyResolver>();
		_logger = Substitute.For<ILogger>();
		containerBuilder.AddTransient(_ => _columnManager);
		containerBuilder.AddTransient(_ => _dependencyResolver);
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

	[Test]
	[Description("Auto-resolves missing package dependencies and retries when the manager throws EntitySchemaDesignerException on the first attempt (ENG-91314).")]
	public void Execute_ShouldAutoResolveDependenciesAndRetry_WhenSchemaIsInitiallyUnavailable() {
		// Arrange — first call throws; auto-resolve succeeds; second call succeeds.
		int callCount = 0;
		_columnManager.When(m => m.ModifyColumn(Arg.Any<ModifyEntitySchemaColumnOptions>()))
			.Do(_ => {
				callCount++;
				if (callCount == 1) {
					throw new EntitySchemaDesignerException("Schema 'UsrVehicle' is not available in package 'UsrPkg'.");
				}
			});
		_dependencyResolver.TryAutoResolve("UsrVehicle", "UsrPkg").Returns(true);
		var options = new ModifyEntitySchemaColumnOptions {
			Package = "UsrPkg", SchemaName = "UsrVehicle",
			Action = "add", ColumnName = "Name", Type = "Text", Title = "Vehicle name"
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "auto-resolve succeeded and the retry completed the mutation");
		_dependencyResolver.Received(1).TryAutoResolve("UsrVehicle", "UsrPkg");
		_columnManager.Received(2).ModifyColumn(options);
	}

	[Test]
	[Description("Returns failure with enriched error when auto-resolve finds no candidate dependency (TryAutoResolve returns false) (ENG-91314).")]
	public void Execute_ShouldReturnFailure_WhenAutoResolveReturnsFalse() {
		// Arrange
		_columnManager.When(m => m.ModifyColumn(Arg.Any<ModifyEntitySchemaColumnOptions>()))
			.Do(_ => throw new EntitySchemaDesignerException("Schema 'UsrVehicle' is not available."));
		_dependencyResolver.TryAutoResolve("UsrVehicle", "UsrPkg").Returns(false);
		var options = new ModifyEntitySchemaColumnOptions {
			Package = "UsrPkg", SchemaName = "UsrVehicle",
			Action = "add", ColumnName = "Name", Type = "Text", Title = "Vehicle name"
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "when auto-resolve fails, the original error must propagate");
		_logger.Received(1).WriteError(Arg.Is<string>(msg => msg.Contains("not available")));
	}

	[Test]
	[Description("Returns failure when auto-resolve succeeds but the retried ModifyColumn still throws — the dependency was added but the schema remains inaccessible (ENG-91314).")]
	public void Execute_ShouldReturnFailure_WhenRetryAfterAutoResolveStillFails() {
		// Arrange — both calls throw.
		_columnManager.When(m => m.ModifyColumn(Arg.Any<ModifyEntitySchemaColumnOptions>()))
			.Do(_ => throw new EntitySchemaDesignerException("Schema 'UsrVehicle' is not available."));
		_dependencyResolver.TryAutoResolve("UsrVehicle", "UsrPkg").Returns(true);
		var options = new ModifyEntitySchemaColumnOptions {
			Package = "UsrPkg", SchemaName = "UsrVehicle",
			Action = "add", ColumnName = "Name", Type = "Text", Title = "Vehicle name"
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1,
			because: "even after adding the dependency, a still-inaccessible schema must return a failure exit code");
		_columnManager.Received(2).ModifyColumn(options);
		_logger.Received(1).WriteError(Arg.Is<string>(msg => msg.Contains("not available")));
	}
}
