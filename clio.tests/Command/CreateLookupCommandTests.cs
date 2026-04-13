using System;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
public sealed class CreateLookupCommandTests : BaseCommandTests<CreateLookupOptions>
{
	private CreateLookupCommand _command;
	private CreateEntitySchemaCommand _createEntitySchemaCommand;
	private ILookupRegistrationService _lookupRegistrationService;
	private ILogger _logger;

	public override void Setup()
	{
		base.Setup();
		_command = Container.GetRequiredService<CreateLookupCommand>();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder)
	{
		base.AdditionalRegistrations(containerBuilder);
		_createEntitySchemaCommand = Substitute.For<CreateEntitySchemaCommand>(
			Substitute.For<Clio.Command.EntitySchemaDesigner.IRemoteEntitySchemaCreator>(),
			Substitute.For<ILogger>());
		_lookupRegistrationService = Substitute.For<ILookupRegistrationService>();
		_logger = Substitute.For<ILogger>();
		containerBuilder.AddTransient(_ => _createEntitySchemaCommand);
		containerBuilder.AddTransient(_ => _lookupRegistrationService);
		containerBuilder.AddTransient(_ => _logger);
	}

	[TearDown]
	public void ClearReceivedCalls()
	{
		_createEntitySchemaCommand.ClearReceivedCalls();
		_lookupRegistrationService.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
	}

	[Test]
	[Description("Creates the entity schema and registers the lookup in the catalog on success.")]
	public void Execute_Should_Create_Schema_And_Register_Lookup_When_Options_Are_Valid()
	{
		// Arrange
		CreateLookupOptions options = new() {
			Environment = "dev",
			Package = "UsrPkg",
			SchemaName = "UsrOrderStatus",
			Title = "Order Status"
		};
		_createEntitySchemaCommand.Execute(Arg.Any<CreateEntitySchemaOptions>()).Returns(0);

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "valid options should return the standard success exit code");
		_createEntitySchemaCommand.Received(1).Execute(
			Arg.Is<CreateEntitySchemaOptions>(o =>
				o.Package == "UsrPkg" &&
				o.SchemaName == "UsrOrderStatus" &&
				o.Title == "Order Status" &&
				o.ParentSchemaName == "BaseLookup" &&
				!o.ExtendParent));
		_lookupRegistrationService.Received(1).EnsureLookupRegistration("UsrPkg", "UsrOrderStatus", "Order Status");
	}

	[Test]
	[Description("Returns failure and skips lookup registration when the entity schema creation fails.")]
	public void Execute_Should_Return_Failure_And_Skip_Registration_When_Schema_Creation_Fails()
	{
		// Arrange
		CreateLookupOptions options = new() {
			Environment = "dev",
			Package = "UsrPkg",
			SchemaName = "UsrOrderStatus",
			Title = "Order Status"
		};
		_createEntitySchemaCommand.Execute(Arg.Any<CreateEntitySchemaOptions>()).Returns(1);

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "a failed schema creation should propagate the non-zero exit code");
		_lookupRegistrationService.DidNotReceiveWithAnyArgs().EnsureLookupRegistration(default!, default!, default!);
	}

	[Test]
	[Description("Returns failure when schema name exceeds 22 characters.")]
	public void Execute_Should_Return_Failure_When_SchemaName_Is_Too_Long()
	{
		// Arrange
		CreateLookupOptions options = new() {
			Environment = "dev",
			Package = "UsrPkg",
			SchemaName = "UsrVeryLongSchemaNameThatExceeds22",
			Title = "Title"
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "a schema name longer than 22 characters must be rejected");
		_createEntitySchemaCommand.DidNotReceiveWithAnyArgs().Execute(default!);
		_logger.Received(1).WriteError(Arg.Is<string>(m => m.Contains("22")));
	}

	[Test]
	[Description("Returns failure exit code and logs an error when the environment is missing.")]
	public void Execute_Should_Return_Failure_When_Environment_Is_Missing()
	{
		// Arrange
		CreateLookupOptions options = new() {
			Environment = string.Empty,
			Package = "UsrPkg",
			SchemaName = "UsrStatus",
			Title = "Status"
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "missing environment should yield a non-zero exit code");
		_createEntitySchemaCommand.DidNotReceiveWithAnyArgs().Execute(default!);
	}

	[Test]
	[Description("Returns failure exit code and logs the exception message when lookup registration throws.")]
	public void Execute_Should_Return_Failure_When_Registration_Throws()
	{
		// Arrange
		CreateLookupOptions options = new() {
			Environment = "dev",
			Package = "UsrPkg",
			SchemaName = "UsrStatus",
			Title = "Status"
		};
		_createEntitySchemaCommand.Execute(Arg.Any<CreateEntitySchemaOptions>()).Returns(0);
		_lookupRegistrationService
			.When(s => s.EnsureLookupRegistration(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()))
			.Do(_ => throw new InvalidOperationException("Catalog error"));

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "a registration exception should yield a non-zero exit code");
		_logger.Received(1).WriteError(Arg.Is<string>(m => m.Contains("Catalog error")));
	}
}
