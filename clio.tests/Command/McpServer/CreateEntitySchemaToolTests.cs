using System.Collections.Generic;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
public class CreateEntitySchemaToolTests {

	[Test]
	[Description("Resolves the create entity schema command for the requested environment and maps structured MCP column inputs into command options.")]
	[Category("Unit")]
	public void CreateEntitySchema_Should_Resolve_Command_For_Requested_Environment() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateEntitySchemaCommand defaultCommand = new();
		FakeCreateEntitySchemaCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(resolvedCommand);
		CreateEntitySchemaTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.CreateEntitySchema(new CreateEntitySchemaArgs(
			"MyPackage",
			"UsrVehicle",
			"Vehicle",
			"docker_fix2",
			"BaseEntity",
			false,
			new List<CreateEntitySchemaColumnArgs> {
				new("Name", "Text", "Vehicle name"),
				new("Owner", "Lookup", "Owner", "Contact")
			}));

		// Assert
		result.ExitCode.Should().Be(0, "because the tool should forward a valid create entity schema request");
		commandResolver.Received(1).Resolve<CreateEntitySchemaCommand>(Arg.Is<CreateEntitySchemaOptions>(options =>
			options.Package == "MyPackage"
			&& options.SchemaName == "UsrVehicle"
			&& options.Title == "Vehicle"
			&& options.ParentSchemaName == "BaseEntity"
			&& options.Environment == "docker_fix2"));
		defaultCommand.CapturedOptions.Should().BeNull(
			"because the environment-aware tool should use the resolved command");
		resolvedCommand.CapturedOptions.Should().NotBeNull(
			"because the resolved command should receive the mapped entity schema options");
		resolvedCommand.CapturedOptions.Columns.Should().BeEquivalentTo(new[] {
			"Name:Text:Vehicle name",
			"Owner:Lookup:Owner:Contact"
		}, "because MCP structured columns should map to the command column format");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Preserves the lookup reference position when a lookup column omits an explicit title.")]
	[Category("Unit")]
	public void CreateEntitySchema_Should_Preserve_Empty_Title_Slot_For_Lookup_Columns() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateEntitySchemaCommand defaultCommand = new();
		FakeCreateEntitySchemaCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(resolvedCommand);
		CreateEntitySchemaTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.CreateEntitySchema(new CreateEntitySchemaArgs(
			"MyPackage",
			"UsrVehicle",
			"Vehicle",
			"docker_fix2",
			Columns: new[] {
				new CreateEntitySchemaColumnArgs("Owner", "Lookup", ReferenceSchemaName: "Contact")
			}));

		// Assert
		result.ExitCode.Should().Be(0, "because the serialized lookup column should remain valid");
		resolvedCommand.CapturedOptions.Should().NotBeNull(
			"because the resolved command should receive the mapped entity schema options");
		resolvedCommand.CapturedOptions.Columns.Should().BeEquivalentTo(new[] {
			"Owner:Lookup::Contact"
		}, "because the command parser expects an empty title slot before the lookup reference");
		ConsoleLogger.Instance.ClearMessages();
	}

	private sealed class FakeCreateEntitySchemaCommand : CreateEntitySchemaCommand {
		public CreateEntitySchemaOptions CapturedOptions { get; private set; }

		public FakeCreateEntitySchemaCommand()
			: base(
				Substitute.For<Clio.Command.EntitySchemaDesigner.IRemoteEntitySchemaCreator>(),
				Substitute.For<ILogger>()) {
		}

		public override int Execute(CreateEntitySchemaOptions options) {
			CapturedOptions = options;
			return 0;
		}
	}
}
