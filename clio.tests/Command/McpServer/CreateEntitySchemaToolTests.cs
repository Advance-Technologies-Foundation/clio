using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
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
			Localizations("Vehicle"),
			"docker_fix2",
			"BaseEntity",
			false,
			new List<CreateEntitySchemaColumnArgs> {
				new("Name", "Text", Localizations("Vehicle name")),
				new("Owner", "Lookup", Localizations("Owner"), "Contact")
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
		string[] serializedColumns = resolvedCommand.CapturedOptions.Columns!.ToArray();
		using (JsonDocument firstColumn = JsonDocument.Parse(serializedColumns[0]))
		using (JsonDocument secondColumn = JsonDocument.Parse(serializedColumns[1])) {
			firstColumn.RootElement.GetProperty("title-localizations").GetProperty("en-US").GetString()
				.Should().Be("Vehicle name");
			secondColumn.RootElement.GetProperty("reference-schema-name").GetString().Should().Be("Contact");
			secondColumn.RootElement.GetProperty("title-localizations").GetProperty("en-US").GetString()
				.Should().Be("Owner");
		}
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
	[Description("Rejects create-entity-schema column payloads that omit the required title-localizations field.")]
	[Category("Unit")]
	public void CreateEntitySchema_Should_Reject_Columns_Without_Title_Localizations() {
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
			Localizations("Vehicle"),
			"docker_fix2",
			Columns: new[] {
				new CreateEntitySchemaColumnArgs("Owner", "Lookup", Localizations("Owner"), "Contact") {
					LegacyTitle = "Owner"
				}
			}));

		// Assert
		result.ExitCode.Should().Be(1, "because MCP create-column payloads must use title-localizations");
		result.Output.Should().Contain(message =>
				message.Value != null && message.Value.ToString().Contains("legacy 'title'", StringComparison.Ordinal),
			"because the validation error should point callers to title-localizations");
		resolvedCommand.CapturedOptions.Should().BeNull(
			"because invalid MCP payloads should be rejected before command execution");
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
			Localizations("Vehicle", "Транспорт"),
			"docker_fix2",
			Columns: [
				new CreateEntitySchemaColumnArgs("Status", "ShortText", Localizations("Status", "Статус")) {
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
		document.RootElement.GetProperty("title-localizations").GetProperty("en-US").GetString().Should().Be("Status",
			because: "structured serialization should preserve explicit title localizations");
		document.RootElement.GetProperty("title-localizations").GetProperty("uk-UA").GetString().Should().Be("Статус",
			because: "structured serialization should preserve secondary localizations");
		document.RootElement.GetProperty("required").GetBoolean().Should().BeTrue(
			because: "structured serialization should preserve required metadata");
		document.RootElement.GetProperty("default-value-source").GetString().Should().Be("Const",
			because: "structured serialization should preserve the requested default source");
		document.RootElement.GetProperty("default-value").GetString().Should().Be("Draft",
			because: "structured serialization should preserve the default value");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Serializes structured default-value-config metadata when the MCP caller supplies non-legacy default settings.")]
	[Category("Unit")]
	public void CreateEntitySchema_Should_Serialize_DefaultValueConfig_As_Json() {
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
			Localizations("Vehicle"),
			"docker_fix2",
			Columns: [
				new CreateEntitySchemaColumnArgs("UsrStartDate", "DateTime", Localizations("Start date")) {
					DefaultValueConfig = new EntitySchemaDefaultValueConfig {
						Source = "SystemValue",
						ValueSource = "CurrentDateTime"
					}
				}
			]));

		// Assert
		result.ExitCode.Should().Be(0, because: "structured default-value-config should be a valid MCP create-column payload");
		string serializedColumn = resolvedCommand.CapturedOptions!.Columns!.Single();
		using JsonDocument document = JsonDocument.Parse(serializedColumn);
		document.RootElement.GetProperty("default-value-config").GetProperty("source").GetString().Should().Be("SystemValue",
			because: "structured serialization should preserve the default source name");
		document.RootElement.GetProperty("default-value-config").GetProperty("value-source").GetString().Should().Be("CurrentDateTime",
			because: "structured serialization should preserve the system value source");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Rejects schema title-localizations payloads that omit the required en-US value.")]
	[Category("Unit")]
	public void CreateEntitySchema_Should_Reject_Title_Localizations_Without_EnUs() {
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
			new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
				["uk-UA"] = "Транспорт"
			},
			"docker_fix2"));

		// Assert
		result.ExitCode.Should().Be(1);
		result.Output.Should().Contain(message =>
				message.Value != null && message.Value.ToString().Contains("en-US", StringComparison.Ordinal),
			because: "the validation error should explain the required base localization");
		resolvedCommand.CapturedOptions.Should().BeNull();
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Rejects create-column localization maps that contain empty values.")]
	[Category("Unit")]
	public void CreateEntitySchema_Should_Reject_Column_Title_Localizations_With_Empty_Value() {
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
			Localizations("Vehicle"),
			"docker_fix2",
			Columns: [
				new CreateEntitySchemaColumnArgs(
					"Name",
					"Text",
					new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
						["en-US"] = string.Empty
					})
			]));

		// Assert
		result.ExitCode.Should().Be(1);
		result.Output.Should().Contain(message =>
				message.Value != null && message.Value.ToString().Contains("empty values", StringComparison.Ordinal),
			because: "the validation error should reject blank localization values");
		resolvedCommand.CapturedOptions.Should().BeNull();
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
			new CreateEntitySchemaColumnArgs("Payload", typeName, Localizations("Payload"))
		};

		// Act
		string serializedColumn = CreateEntitySchemaTool.SerializeColumns(columns, "Schema 'UsrVehicle'")!.Single();

		// Assert
		using JsonDocument document = JsonDocument.Parse(serializedColumn);
		document.RootElement.GetProperty("type").GetString().Should().Be(typeName,
			because: "the MCP adapter should pass supported binary-like type names through without rewriting them");
		document.RootElement.GetProperty("title-localizations").GetProperty("en-US").GetString().Should().Be("Payload");
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
		ILookupRegistrationService registrationService = Substitute.For<ILookupRegistrationService>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(resolvedCommand);
		commandResolver.Resolve<ILookupRegistrationService>(Arg.Any<EnvironmentOptions>())
			.Returns(registrationService);
		CreateLookupTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.CreateLookup(new CreateLookupArgs(
			"MyPackage",
			"UsrOrderStatus",
			Localizations("Order status", "Статус замовлення"),
			"docker_fix2",
			new List<CreateEntitySchemaColumnArgs> {
				new("UsrSortOrder", "Integer", Localizations("Sort order"))
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
		using (JsonDocument document = JsonDocument.Parse(resolvedCommand.CapturedOptions!.Columns!.Single())) {
			document.RootElement.GetProperty("title-localizations").GetProperty("en-US").GetString().Should().Be("Sort order");
		}
		registrationService.Received(1).EnsureLookupRegistration("MyPackage", "UsrOrderStatus", "Order status");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Rejects inherited BaseLookup columns when create-lookup callers try to redefine Name or Description.")]
	[Category("Unit")]
	public void CreateLookup_Should_Reject_Inherited_BaseLookup_Columns() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateEntitySchemaCommand defaultCommand = new();
		FakeCreateEntitySchemaCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		ILookupRegistrationService registrationService = Substitute.For<ILookupRegistrationService>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(resolvedCommand);
		commandResolver.Resolve<ILookupRegistrationService>(Arg.Any<EnvironmentOptions>())
			.Returns(registrationService);
		CreateLookupTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.CreateLookup(new CreateLookupArgs(
			"MyPackage",
			"UsrOrderStatus",
			Localizations("Order status"),
			"docker_fix2",
			[
				new CreateEntitySchemaColumnArgs("Name", "Text", Localizations("Name")),
				new CreateEntitySchemaColumnArgs("Description", "Text", Localizations("Description"))
			]));
		string[] outputValues = result.Output
			.Select(message => message.Value?.ToString() ?? string.Empty)
			.ToArray();

		// Assert
		result.ExitCode.Should().Be(1,
			because: "create-lookup should reject attempts to redefine inherited BaseLookup columns");
		outputValues.Should().Contain(value => value.Contains("BaseLookup", StringComparison.Ordinal),
			because: "the MCP caller should receive a readable explanation of the inherited-column guardrail");
		outputValues.Should().Contain(value =>
				value.Contains("Name", StringComparison.Ordinal)
				&& value.Contains("Description", StringComparison.Ordinal),
			because: "the validation error should identify the rejected inherited columns");
		defaultCommand.CapturedOptions.Should().BeNull(
			because: "the default injected command should not be executed when validation fails");
		resolvedCommand.CapturedOptions.Should().BeNull(
			because: "the resolved command should not be executed when validation fails");
		registrationService.DidNotReceiveWithAnyArgs().EnsureLookupRegistration(default!, default!, default!);
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
		ILookupRegistrationService registrationService = Substitute.For<ILookupRegistrationService>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(resolvedCommand);
		commandResolver.Resolve<ILookupRegistrationService>(Arg.Any<EnvironmentOptions>())
			.Returns(registrationService);
		CreateLookupTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.CreateLookup(new CreateLookupArgs(
			"MyPackage",
			"UsrOrderStatus",
			Localizations("Order status"),
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
		registrationService.Received(1).EnsureLookupRegistration("MyPackage", "UsrOrderStatus", "Order status");
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
		ILookupRegistrationService registrationService = Substitute.For<ILookupRegistrationService>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(resolvedCommand);
		commandResolver.Resolve<ILookupRegistrationService>(Arg.Any<EnvironmentOptions>())
			.Returns(registrationService);
		CreateLookupTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.CreateLookup(new CreateLookupArgs(
			"MyPackage",
			"UsrOrderStatus",
			Localizations("Order status"),
			"docker_fix2",
			[
				new CreateEntitySchemaColumnArgs("Status", "ShortText", Localizations("Status", "Статус")) {
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
		document.RootElement.GetProperty("title-localizations").GetProperty("en-US").GetString().Should().Be("Status");
		document.RootElement.GetProperty("title-localizations").GetProperty("uk-UA").GetString().Should().Be("Статус");
		document.RootElement.GetProperty("required").GetBoolean().Should().BeTrue(
			because: "structured serialization should preserve required metadata");
		document.RootElement.GetProperty("default-value-source").GetString().Should().Be("Const",
			because: "structured serialization should preserve the requested default source");
		document.RootElement.GetProperty("default-value").GetString().Should().Be("Draft",
			because: "structured serialization should preserve the default value");
		registrationService.Received(1).EnsureLookupRegistration("MyPackage", "UsrOrderStatus", "Order status");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns a failed MCP result when lookup creation succeeds but Lookups registration fails.")]
	[Category("Unit")]
	public void CreateLookup_Should_Fail_When_Lookup_Registration_Fails() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateEntitySchemaCommand defaultCommand = new();
		FakeCreateEntitySchemaCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		ILookupRegistrationService registrationService = Substitute.For<ILookupRegistrationService>();
		registrationService
			.When(service => service.EnsureLookupRegistration("MyPackage", "UsrOrderStatus", "Order status"))
			.Do(_ => throw new InvalidOperationException("Lookup registration failed."));
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(resolvedCommand);
		commandResolver.Resolve<ILookupRegistrationService>(Arg.Any<EnvironmentOptions>())
			.Returns(registrationService);
		CreateLookupTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.CreateLookup(new CreateLookupArgs(
			"MyPackage",
			"UsrOrderStatus",
			Localizations("Order status"),
			"docker_fix2"));
		bool hasRegistrationFailure = result.Output.Any(message =>
			message.Value != null &&
			message.Value.ToString().Contains("Lookup registration failed.", StringComparison.Ordinal));

		// Assert
		result.ExitCode.Should().Be(1,
			because: "create-lookup should fail when Lookups registration does not complete");
		hasRegistrationFailure.Should().BeTrue(
			because: "the registration failure should be surfaced to the MCP caller");
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

	private static Dictionary<string, string> Localizations(string enUs, string? ukUa = null) {
		Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase) {
			["en-US"] = enUs
		};
		if (!string.IsNullOrWhiteSpace(ukUa)) {
			result["uk-UA"] = ukUa;
		}
		return result;
	}
}
