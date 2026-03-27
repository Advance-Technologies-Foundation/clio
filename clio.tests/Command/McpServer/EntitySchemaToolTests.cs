using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using Clio.Command.McpServer.Prompts;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using ModelContextProtocol.Server;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
public sealed class EntitySchemaToolTests {

	[Test]
	[Category("Unit")]
	[Description("Advertises stable MCP tool names for the entity schema tool family so tests and callers share the same identifiers.")]
	public void EntitySchemaTools_Should_Advertise_Stable_Tool_Names() {
		// Arrange

		// Act
		string[] toolNames = [
			CreateEntitySchemaTool.CreateEntitySchemaToolName,
			CreateLookupTool.CreateLookupToolName,
			UpdateEntitySchemaTool.UpdateEntitySchemaToolName,
			GetEntitySchemaPropertiesTool.GetEntitySchemaPropertiesToolName,
			GetEntitySchemaColumnPropertiesTool.GetEntitySchemaColumnPropertiesToolName,
			ModifyEntitySchemaColumnTool.ModifyEntitySchemaColumnToolName
		];

		// Assert
			toolNames.Should().Equal(new[] {
				"create-entity-schema",
				"create-lookup",
				"update-entity-schema",
				"get-entity-schema-properties",
				"get-entity-schema-column-properties",
				"modify-entity-schema-column"
			},
			because: "the entity schema MCP tool identifiers should remain stable for callers and tests");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns structured schema properties from the resolved environment-specific command.")]
	public void GetEntitySchemaProperties_Should_Return_Structured_Result() {
		// Arrange
		EntitySchemaPropertiesInfo expectedResult = new(
			"UsrVehicle",
			"Vehicle",
			"Vehicle catalog",
			"UsrPkg",
			"BaseEntity",
			false,
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
					null)
			]);
		IRemoteEntitySchemaColumnManager columnManager = Substitute.For<IRemoteEntitySchemaColumnManager>();
		columnManager.GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>()).Returns(expectedResult);
		GetEntitySchemaPropertiesCommand resolvedCommand = new(columnManager, Substitute.For<ILogger>());
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GetEntitySchemaPropertiesCommand>(Arg.Any<GetEntitySchemaPropertiesOptions>())
			.Returns(resolvedCommand);
		GetEntitySchemaPropertiesTool tool = new(
			new GetEntitySchemaPropertiesCommand(Substitute.For<IRemoteEntitySchemaColumnManager>(), Substitute.For<ILogger>()),
			ConsoleLogger.Instance,
			commandResolver);

		// Act
		EntitySchemaPropertiesInfo result = tool.GetEntitySchemaProperties(
			new GetEntitySchemaPropertiesArgs("dev", "UsrPkg", "UsrVehicle"));

		// Assert
		result.Should().BeEquivalentTo(expectedResult,
			because: "the read-only MCP tool should return the structured schema snapshot from the resolved command");
		commandResolver.Received(1).Resolve<GetEntitySchemaPropertiesCommand>(Arg.Is<GetEntitySchemaPropertiesOptions>(
			options => options.Environment == "dev" && options.Package == "UsrPkg" && options.SchemaName == "UsrVehicle"));
	}

	[Test]
	[Category("Unit")]
	[Description("Returns structured column properties from the resolved environment-specific command.")]
	public void GetEntitySchemaColumnProperties_Should_Return_Structured_Result() {
		// Arrange
		EntitySchemaColumnPropertiesInfo expectedResult = new(
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
			false);
		IRemoteEntitySchemaColumnManager columnManager = Substitute.For<IRemoteEntitySchemaColumnManager>();
		columnManager.GetColumnProperties(Arg.Any<GetEntitySchemaColumnPropertiesOptions>()).Returns(expectedResult);
		GetEntitySchemaColumnPropertiesCommand resolvedCommand = new(columnManager, Substitute.For<ILogger>());
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GetEntitySchemaColumnPropertiesCommand>(Arg.Any<GetEntitySchemaColumnPropertiesOptions>())
			.Returns(resolvedCommand);
		GetEntitySchemaColumnPropertiesTool tool = new(
			new GetEntitySchemaColumnPropertiesCommand(Substitute.For<IRemoteEntitySchemaColumnManager>(), Substitute.For<ILogger>()),
			ConsoleLogger.Instance,
			commandResolver);

		// Act
		EntitySchemaColumnPropertiesInfo result = tool.GetEntitySchemaColumnProperties(
			new GetEntitySchemaColumnPropertiesArgs("dev", "UsrPkg", "UsrVehicle", "Name"));

		// Assert
		result.Should().BeEquivalentTo(expectedResult,
			because: "the read-only MCP tool should return the structured column snapshot from the resolved command");
		commandResolver.Received(1).Resolve<GetEntitySchemaColumnPropertiesCommand>(
			Arg.Is<GetEntitySchemaColumnPropertiesOptions>(options =>
				options.Environment == "dev"
				&& options.Package == "UsrPkg"
				&& options.SchemaName == "UsrVehicle"
				&& options.ColumnName == "Name"));
	}

	[Test]
	[Category("Unit")]
	[Description("Maps structured batch MCP mutation arguments into update-entity-schema command options.")]
	public void UpdateEntitySchema_Should_Map_All_Arguments() {
		// Arrange
		FakeUpdateEntitySchemaCommand defaultCommand = new();
		FakeUpdateEntitySchemaCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<UpdateEntitySchemaCommand>(Arg.Any<UpdateEntitySchemaOptions>())
			.Returns(resolvedCommand);
		UpdateEntitySchemaTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.UpdateEntitySchema(new UpdateEntitySchemaArgs(
			"dev",
			"UsrPkg",
			"UsrVehicle",
			[
				new UpdateEntitySchemaOperationArgs(
					"add",
					"UsrStatus",
					Type: "Lookup",
					Title: "Status",
					ReferenceSchemaName: "UsrVehicleStatus",
					IsRequired: true),
				new UpdateEntitySchemaOperationArgs(
					"modify",
					"UsrDueDate",
					Title: "Due date",
					DefaultValueSource: "None")
			]));

		// Assert
		result.ExitCode.Should().Be(0,
			because: "the tool should forward a valid batch update through the resolved command");
		defaultCommand.CapturedOptions.Should().BeNull(
			because: "the environment-aware tool should execute the resolved command instance");
		resolvedCommand.CapturedOptions.Should().NotBeNull(
			because: "the resolved command should receive the mapped batch update options");
		resolvedCommand.CapturedOptions!.Environment.Should().Be("dev");
		resolvedCommand.CapturedOptions.Package.Should().Be("UsrPkg");
		resolvedCommand.CapturedOptions.SchemaName.Should().Be("UsrVehicle");
		resolvedCommand.CapturedOptions.Operations.Should().HaveCount(2);
		resolvedCommand.CapturedOptions.Operations!.First().Should().Contain("\"action\":\"add\"");
		resolvedCommand.CapturedOptions.Operations!.First().Should().Contain("\"column-name\":\"UsrStatus\"");
		resolvedCommand.CapturedOptions.Operations!.Last().Should().Contain("\"default-value-source\":\"None\"");
	}

	[Test]
	[Category("Unit")]
	[Description("Maps structured MCP mutation arguments into modify-entity-schema-column command options.")]
	public void ModifyEntitySchemaColumn_Should_Map_All_Arguments() {
		// Arrange
		FakeModifyEntitySchemaColumnCommand defaultCommand = new();
		FakeModifyEntitySchemaColumnCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ModifyEntitySchemaColumnCommand>(Arg.Any<ModifyEntitySchemaColumnOptions>())
			.Returns(resolvedCommand);
		ModifyEntitySchemaColumnTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.ModifyEntitySchemaColumn(new ModifyEntitySchemaColumnArgs(
			"dev",
			"UsrPkg",
			"UsrVehicle",
			"modify",
			"Name",
			NewName: "DisplayName",
			Type: "Text",
			Title: "Vehicle name",
			Description: "Readable vehicle name",
			ReferenceSchemaName: "Contact",
			IsRequired: true,
			Indexed: true,
			Cloneable: false,
			TrackChanges: true,
			DefaultValueSource: "Const",
			DefaultValue: "Vehicle",
			MultilineText: true,
			LocalizableText: true,
			AccentInsensitive: true,
			Masked: false,
			FormatValidated: false,
			UseSeconds: false,
			SimpleLookup: false,
			Cascade: false,
			DoNotControlIntegrity: false));

		// Assert
		result.ExitCode.Should().Be(0,
			because: "the tool should forward a valid modify request through the resolved command");
		defaultCommand.CapturedOptions.Should().BeNull(
			because: "the environment-aware tool should execute the resolved command instance");
		resolvedCommand.CapturedOptions.Should().NotBeNull(
			because: "the resolved command should receive the mapped mutation options");
		resolvedCommand.CapturedOptions!.Environment.Should().Be("dev",
			because: "the requested environment must be preserved");
		resolvedCommand.CapturedOptions.Package.Should().Be("UsrPkg",
			because: "the package name must be preserved");
		resolvedCommand.CapturedOptions.SchemaName.Should().Be("UsrVehicle",
			because: "the schema name must be preserved");
		resolvedCommand.CapturedOptions.Action.Should().Be("modify",
			because: "the requested mutation action must be preserved");
		resolvedCommand.CapturedOptions.ColumnName.Should().Be("Name",
			because: "the target column name must be preserved");
		resolvedCommand.CapturedOptions.NewName.Should().Be("DisplayName",
			because: "rename options should be mapped");
		resolvedCommand.CapturedOptions.ReferenceSchemaName.Should().Be("Contact",
			because: "lookup reference changes should be mapped");
		resolvedCommand.CapturedOptions.Required.Should().BeTrue(
			because: "nullable boolean mutation flags should be mapped");
		resolvedCommand.CapturedOptions.DefaultValueSource.Should().Be("Const",
			because: "default-value-source should be mapped for mutation flows that need explicit clearing or const defaults");
		resolvedCommand.CapturedOptions.LocalizableText.Should().BeTrue(
			because: "text-specific options should be mapped");
	}

	[Test]
	[Category("Unit")]
	[Description("Marks the entity schema read tools as read-only and the mutating tools as destructive.")]
	[TestCase(typeof(CreateEntitySchemaTool), nameof(CreateEntitySchemaTool.CreateEntitySchema), false, true)]
	[TestCase(typeof(CreateLookupTool), nameof(CreateLookupTool.CreateLookup), false, true)]
	[TestCase(typeof(UpdateEntitySchemaTool), nameof(UpdateEntitySchemaTool.UpdateEntitySchema), false, true)]
	[TestCase(typeof(GetEntitySchemaPropertiesTool), nameof(GetEntitySchemaPropertiesTool.GetEntitySchemaProperties), true, false)]
	[TestCase(typeof(GetEntitySchemaColumnPropertiesTool), nameof(GetEntitySchemaColumnPropertiesTool.GetEntitySchemaColumnProperties), true, false)]
	[TestCase(typeof(ModifyEntitySchemaColumnTool), nameof(ModifyEntitySchemaColumnTool.ModifyEntitySchemaColumn), false, true)]
	public void EntitySchemaTools_Should_Advertise_Safety_Metadata(
		System.Type toolType,
		string methodName,
		bool readOnly,
		bool destructive) {
		// Arrange
		System.Reflection.MethodInfo method = toolType.GetMethod(methodName)!;
		McpServerToolAttribute attribute = method
			.GetCustomAttributes(typeof(McpServerToolAttribute), inherit: false)
			.Cast<McpServerToolAttribute>()
			.Single();

		// Act
		bool actualReadOnly = attribute.ReadOnly;
		bool actualDestructive = attribute.Destructive;

		// Assert
		actualReadOnly.Should().Be(readOnly,
			because: "the tool metadata should distinguish read-only and mutating entity schema operations");
		actualDestructive.Should().Be(destructive,
			because: "the tool metadata should warn clients before mutating remote schema state");
	}

	[Test]
	[Category("Unit")]
	[Description("Prompt guidance for entity schema tools references the exact production tool names and argument shapes.")]
	public void EntitySchemaPrompt_Should_Reference_Production_Tool_Names() {
		// Arrange

		// Act
		string createPrompt = EntitySchemaPrompt.CreateEntitySchema("UsrPkg", "UsrVehicle", "Vehicle", "dev");
		string lookupPrompt = EntitySchemaPrompt.CreateLookup("UsrPkg", "UsrOrderStatus", "Order status", "dev");
		string updatePrompt = EntitySchemaPrompt.UpdateEntitySchema("UsrPkg", "UsrVehicle", "dev");
		string schemaPrompt = EntitySchemaPrompt.GetEntitySchemaProperties("UsrPkg", "UsrVehicle", "dev");
		string columnPrompt = EntitySchemaPrompt.GetEntitySchemaColumnProperties("UsrPkg", "UsrVehicle", "Name", "dev");
		string modifyPrompt = EntitySchemaPrompt.ModifyEntitySchemaColumn("UsrPkg", "UsrVehicle", "modify", "Name", "dev");

		// Assert
		createPrompt.Should().Contain(CreateEntitySchemaTool.CreateEntitySchemaToolName,
			because: "create prompt guidance should reference the exact production tool name");
		lookupPrompt.Should().Contain(CreateLookupTool.CreateLookupToolName,
			because: "lookup prompt guidance should reference the exact production tool name");
		lookupPrompt.Should().Contain("Lookups",
			because: "lookup prompt guidance should mention automatic registration in the standard Lookups section");
		lookupPrompt.Should().Contain(GetEntitySchemaPropertiesTool.GetEntitySchemaPropertiesToolName,
			because: "lookup prompt guidance should direct callers to the canonical post-create verification path");
		lookupPrompt.Should().Contain("`Name`",
			because: "lookup prompt guidance should explain the inherited display-field contract for BaseLookup");
		lookupPrompt.Should().Contain("docs://mcp/guides/app-modeling",
			because: "lookup prompt guidance should point callers to the MCP-owned modeling guide");
		updatePrompt.Should().Contain(UpdateEntitySchemaTool.UpdateEntitySchemaToolName,
			because: "batch update prompt guidance should reference the exact production tool name");
		updatePrompt.Should().Contain("schema default",
			because: "batch update prompt guidance should explain that seed rows do not define defaults");
		schemaPrompt.Should().Contain(GetEntitySchemaPropertiesTool.GetEntitySchemaPropertiesToolName,
			because: "schema-read prompt guidance should reference the exact production tool name");
		columnPrompt.Should().Contain(GetEntitySchemaColumnPropertiesTool.GetEntitySchemaColumnPropertiesToolName,
			because: "column-read prompt guidance should reference the exact production tool name");
		modifyPrompt.Should().Contain(ModifyEntitySchemaColumnTool.ModifyEntitySchemaColumnToolName,
			because: "modify prompt guidance should reference the exact production tool name");
		modifyPrompt.Should().Contain("type",
			because: "mutation prompt guidance should remind callers about action-specific options");
	}

	[Test]
	[Category("Unit")]
	[Description("MCP argument descriptions and prompts advertise Binary, Image, File, Blob alias support, and binary default restrictions.")]
	public void EntitySchemaMcpContract_Should_Advertise_BinaryLike_Types_And_Default_Restrictions() {
		// Arrange
		string createTypeDescription = typeof(CreateEntitySchemaColumnArgs)
			.GetProperty(nameof(CreateEntitySchemaColumnArgs.Type))!
			.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), inherit: false)
			.Cast<System.ComponentModel.DescriptionAttribute>()
			.Single()
			.Description;
		string createDefaultSourceDescription = typeof(CreateEntitySchemaColumnArgs)
			.GetProperty(nameof(CreateEntitySchemaColumnArgs.DefaultValueSource))!
			.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), inherit: false)
			.Cast<System.ComponentModel.DescriptionAttribute>()
			.Single()
			.Description;
		string modifyTypeDescription = typeof(ColumnModificationArgsBase)
			.GetProperty(nameof(ColumnModificationArgsBase.Type))!
			.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), inherit: false)
			.Cast<System.ComponentModel.DescriptionAttribute>()
			.Single()
			.Description;
		string modifyDefaultDescription = typeof(ColumnModificationArgsBase)
			.GetProperty(nameof(ColumnModificationArgsBase.DefaultValue))!
			.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), inherit: false)
			.Cast<System.ComponentModel.DescriptionAttribute>()
			.Single()
			.Description;

		// Act
		string createPrompt = EntitySchemaPrompt.CreateEntitySchema("UsrPkg", "UsrVehicle", "Vehicle", "dev");
		string updatePrompt = EntitySchemaPrompt.UpdateEntitySchema("UsrPkg", "UsrVehicle", "dev");
		string modifyPrompt = EntitySchemaPrompt.ModifyEntitySchemaColumn("UsrPkg", "UsrVehicle", "add", "Payload", "dev");

		// Assert
		createTypeDescription.Should().Contain("Binary",
			because: "create MCP input descriptions should list supported binary-like column types");
		createTypeDescription.Should().Contain("Image",
			because: "create MCP input descriptions should list supported image column types");
		createTypeDescription.Should().Contain("File",
			because: "create MCP input descriptions should list supported file column types");
		createTypeDescription.Should().Contain("Blob",
			because: "create MCP input descriptions should advertise the Binary compatibility alias");
		createDefaultSourceDescription.Should().Contain("do not support Const",
			because: "create MCP input descriptions should explain binary-like default restrictions");
		modifyTypeDescription.Should().Contain("Binary",
			because: "mutation MCP input descriptions should list supported binary-like column types");
		modifyTypeDescription.Should().Contain("Blob",
			because: "mutation MCP input descriptions should advertise the Binary compatibility alias");
		modifyDefaultDescription.Should().Contain("do not support constant defaults",
			because: "mutation MCP input descriptions should explain binary-like default restrictions");
		createPrompt.Should().Contain("Blob",
			because: "create prompt guidance should advertise the Binary compatibility alias");
		updatePrompt.Should().Contain("Binary",
			because: "update prompt guidance should advertise binary-like column support");
		updatePrompt.Should().Contain("default-value-source=Const",
			because: "update prompt guidance should explain unsupported binary default usage");
		updatePrompt.Should().Contain("schema-sync",
			because: "update prompt guidance should steer multi-step schema workflows toward the composite MCP tool");
		modifyPrompt.Should().Contain("File",
			because: "modify prompt guidance should advertise file column support");
	}

	private sealed class FakeModifyEntitySchemaColumnCommand : ModifyEntitySchemaColumnCommand {
		public ModifyEntitySchemaColumnOptions CapturedOptions { get; private set; }

		public FakeModifyEntitySchemaColumnCommand()
			: base(Substitute.For<IRemoteEntitySchemaColumnManager>(), Substitute.For<ILogger>()) {
		}

		public override int Execute(ModifyEntitySchemaColumnOptions options) {
			CapturedOptions = options;
			return 0;
		}
	}

	private sealed class FakeUpdateEntitySchemaCommand : UpdateEntitySchemaCommand {
		public UpdateEntitySchemaOptions CapturedOptions { get; private set; }

		public FakeUpdateEntitySchemaCommand()
			: base(Substitute.For<IRemoteEntitySchemaColumnManager>(), Substitute.For<ILogger>()) {
		}

		public override int Execute(UpdateEntitySchemaOptions options) {
			CapturedOptions = options;
			return 0;
		}
	}
}
