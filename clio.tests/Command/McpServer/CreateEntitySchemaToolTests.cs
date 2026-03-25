using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using ModelContextProtocol.Server;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
public class CreateEntitySchemaToolTests {

	[Test]
	[Description("Advertises a stable MCP tool name for remote entity schema creation.")]
	[Category("Unit")]
	public void CreateEntitySchemaTool_Should_Advertise_Stable_Tool_Name() {
		// Arrange

		// Act
		string toolName = CreateEntitySchemaTool.CreateEntitySchemaToolName;

		// Assert
		toolName.Should().Be("create-entity-schema",
			because: "tests and MCP callers should use the shared production constant");
	}

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
	[Description("Marks create-entity-schema as destructive because it mutates a remote Creatio package.")]
	[Category("Unit")]
	public void CreateEntitySchema_Should_Be_Marked_As_Destructive() {
		// Arrange
		System.Reflection.MethodInfo method = typeof(CreateEntitySchemaTool)
			.GetMethod(nameof(CreateEntitySchemaTool.CreateEntitySchema))!;
		McpServerToolAttribute attribute = method
			.GetCustomAttributes(typeof(McpServerToolAttribute), inherit: false)
			.Cast<McpServerToolAttribute>()
			.Single();

		// Act
		bool destructive = attribute.Destructive;

		// Assert
		destructive.Should().BeTrue(
			because: "creating a remote entity schema changes the target package state");
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

	[Test]
	[Description("Serializes advanced create-column metadata as structured JSON when the MCP caller supplies fields beyond the legacy CLI column format.")]
	[Category("Unit")]
	public void CreateEntitySchema_Should_Serialize_Advanced_Column_Metadata_As_Json() {
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
			Columns: [
				new CreateEntitySchemaColumnArgs("Status", "ShortText", "Status") {
					Required = true,
					DefaultValueSource = "Const",
					DefaultValue = "Draft"
				}
			]));

		// Assert
		result.ExitCode.Should().Be(0, because: "the tool should preserve valid advanced create-column metadata");
		resolvedCommand.CapturedOptions.Should().NotBeNull(
			because: "the resolved command should receive the serialized create-column payload");
		string serializedColumn = resolvedCommand.CapturedOptions!.Columns!.Single();
		using JsonDocument document = JsonDocument.Parse(serializedColumn);
		document.RootElement.GetProperty("name").GetString().Should().Be("Status",
			because: "structured serialization should preserve the column name");
		document.RootElement.GetProperty("type").GetString().Should().Be("ShortText",
			because: "structured serialization should preserve frontend-style type aliases");
		document.RootElement.GetProperty("required").GetBoolean().Should().BeTrue(
			because: "structured serialization should preserve required metadata");
		document.RootElement.GetProperty("default-value-source").GetString().Should().Be("Const",
			because: "structured serialization should preserve the requested default source");
		document.RootElement.GetProperty("default-value").GetString().Should().Be("Draft",
			because: "structured serialization should preserve the default value");
		ConsoleLogger.Instance.ClearMessages();
	}

	[TestCase("Binary")]
	[TestCase("Blob")]
	[TestCase("Image")]
	[TestCase("File")]
	[Description("Preserves Binary, Blob alias, Image, and File type names when MCP create-column inputs are serialized for the command layer.")]
	[Category("Unit")]
	public void CreateEntitySchema_Should_Preserve_BinaryLike_Type_Names_In_Column_Serialization(string typeName) {
		// Arrange
		var columns = new[] {
			new CreateEntitySchemaColumnArgs("Payload", typeName, "Payload")
		};

		// Act
		string serializedColumn = CreateEntitySchemaTool.SerializeColumns(columns)!.Single();

		// Assert
		serializedColumn.Should().Be($"Payload:{typeName}:Payload",
			because: "the MCP adapter should pass supported binary-like type names through without rewriting them");
	}

	[Test]
	[Description("Maps create-lookup MCP arguments into create-entity-schema command options and forces BaseLookup as the parent schema.")]
	[Category("Unit")]
	public void CreateLookup_Should_Resolve_Command_For_Requested_Environment() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateEntitySchemaCommand defaultCommand = new();
		FakeCreateEntitySchemaCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(resolvedCommand);
		CreateLookupTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.CreateLookup(new CreateLookupArgs(
			"MyPackage",
			"UsrOrderStatus",
			"Order status",
			"docker_fix2",
			new List<CreateEntitySchemaColumnArgs> {
				new("Name", "Text", "Name")
			}));

		// Assert
		result.ExitCode.Should().Be(0,
			because: "the tool should forward a valid create-lookup request through the resolved command");
		commandResolver.Received(1).Resolve<CreateEntitySchemaCommand>(Arg.Is<CreateEntitySchemaOptions>(options =>
			options.Package == "MyPackage"
			&& options.SchemaName == "UsrOrderStatus"
			&& options.Title == "Order status"
			&& options.ParentSchemaName == "BaseLookup"
			&& !options.ExtendParent
			&& options.Environment == "docker_fix2"));
		defaultCommand.CapturedOptions.Should().BeNull(
			because: "the environment-aware tool should use the resolved command");
		resolvedCommand.CapturedOptions.Should().NotBeNull(
			because: "the resolved command should receive the mapped lookup creation options");
		resolvedCommand.CapturedOptions!.Columns.Should().BeEquivalentTo(new[] {
			"Name:Text:Name"
		}, because: "create-lookup should reuse the existing create-entity-schema column serialization");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Preserves omitted optional columns when create-lookup callers only provide the required lookup schema metadata.")]
	[Category("Unit")]
	public void CreateLookup_Should_Preserve_Defaults_When_Columns_Are_Omitted() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateEntitySchemaCommand defaultCommand = new();
		FakeCreateEntitySchemaCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(resolvedCommand);
		CreateLookupTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.CreateLookup(new CreateLookupArgs(
			"MyPackage",
			"UsrOrderStatus",
			"Order status",
			"docker_fix2"));

		// Assert
		result.ExitCode.Should().Be(0,
			because: "the tool should accept the minimum required create-lookup arguments");
		resolvedCommand.CapturedOptions.Should().NotBeNull(
			because: "the resolved command should receive the mapped lookup creation options");
		resolvedCommand.CapturedOptions!.Columns.Should().BeNull(
			because: "omitted optional columns should stay omitted");
		resolvedCommand.CapturedOptions.ParentSchemaName.Should().Be("BaseLookup",
			because: "lookup creation should always inherit from BaseLookup");
		resolvedCommand.CapturedOptions.ExtendParent.Should().BeFalse(
			because: "lookup creation should not create replacement schemas");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Serializes advanced create-lookup column metadata as structured JSON so lookup creation keeps parity with create-entity-schema.")]
	[Category("Unit")]
	public void CreateLookup_Should_Serialize_Advanced_Column_Metadata_As_Json() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateEntitySchemaCommand defaultCommand = new();
		FakeCreateEntitySchemaCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(resolvedCommand);
		CreateLookupTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.CreateLookup(new CreateLookupArgs(
			"MyPackage",
			"UsrOrderStatus",
			"Order status",
			"docker_fix2",
			[
				new CreateEntitySchemaColumnArgs("Status", "ShortText", "Status") {
					Required = true,
					DefaultValueSource = "Const",
					DefaultValue = "Draft"
				}
			]));

		// Assert
		result.ExitCode.Should().Be(0,
			because: "the lookup adapter should preserve valid advanced create-column metadata");
		resolvedCommand.CapturedOptions.Should().NotBeNull(
			because: "the resolved command should receive the serialized lookup create-column payload");
		string serializedColumn = resolvedCommand.CapturedOptions!.Columns!.Single();
		using JsonDocument document = JsonDocument.Parse(serializedColumn);
		document.RootElement.GetProperty("name").GetString().Should().Be("Status",
			because: "structured serialization should preserve the column name");
		document.RootElement.GetProperty("type").GetString().Should().Be("ShortText",
			because: "structured serialization should preserve the requested type alias");
		document.RootElement.GetProperty("required").GetBoolean().Should().BeTrue(
			because: "structured serialization should preserve required metadata");
		document.RootElement.GetProperty("default-value-source").GetString().Should().Be("Const",
			because: "structured serialization should preserve the requested default source");
		document.RootElement.GetProperty("default-value").GetString().Should().Be("Draft",
			because: "structured serialization should preserve the default value");
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
