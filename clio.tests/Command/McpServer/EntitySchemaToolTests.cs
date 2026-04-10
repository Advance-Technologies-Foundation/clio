using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
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
[NonParallelizable]
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
	public async Task UpdateEntitySchema_Should_Map_All_Arguments() {
		// Arrange
		FakeUpdateEntitySchemaCommand defaultCommand = new();
		FakeUpdateEntitySchemaCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<UpdateEntitySchemaCommand>(Arg.Any<UpdateEntitySchemaOptions>())
			.Returns(resolvedCommand);
		UpdateEntitySchemaTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = await tool.UpdateEntitySchema(new UpdateEntitySchemaArgs(
			"dev",
			"UsrPkg",
			"UsrVehicle",
			[
				new UpdateEntitySchemaOperationArgs(
					"add",
					"UsrStatus",
					Type: "Lookup",
					TitleLocalizations: Localizations("Status"),
					ReferenceSchemaName: "UsrVehicleStatus",
					IsRequired: true),
				new UpdateEntitySchemaOperationArgs(
					"modify",
					"UsrDueDate",
					TitleLocalizations: Localizations("Due date"),
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
		resolvedCommand.CapturedOptions.Operations!.First().Should().Contain("\"title-localizations\"");
		resolvedCommand.CapturedOptions.Operations!.First().Should().Contain("\"en-US\":\"Status\"");
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
			TitleLocalizations: Localizations("Vehicle name", "Назва транспорту"),
			DescriptionLocalizations: Localizations("Readable vehicle name"),
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
		resolvedCommand.CapturedOptions.TitleLocalizations.Should().BeEquivalentTo(Localizations("Vehicle name", "Назва транспорту"));
		resolvedCommand.CapturedOptions.DescriptionLocalizations.Should().BeEquivalentTo(Localizations("Readable vehicle name"));
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
	[Description("Maps structured default-value-config MCP mutation arguments into modify-entity-schema-column command options.")]
	public void ModifyEntitySchemaColumn_Should_Map_DefaultValueConfig() {
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
			"UsrStartDate") {
			DefaultValueConfig = new EntitySchemaDefaultValueConfig {
				Source = "SystemValue",
				ValueSource = "CurrentDateTime"
			}
		});

		// Assert
		result.ExitCode.Should().Be(0, because: "structured default-value-config should be a valid MCP mutation payload");
		resolvedCommand.CapturedOptions!.DefaultValueConfig.Should().NotBeNull(
			because: "the resolved command should receive the structured default value config");
		resolvedCommand.CapturedOptions.DefaultValueConfig!.Source.Should().Be("SystemValue",
			because: "the source should be preserved through tool mapping");
		resolvedCommand.CapturedOptions.DefaultValueConfig.ValueSource.Should().Be("CurrentDateTime",
			because: "the value-source should be preserved through tool mapping");
		resolvedCommand.CapturedOptions.DefaultValueSource.Should().BeNull(
			because: "structured default configs should not be flattened into legacy shorthand fields");
	}

	[Test]
	[Category("Unit")]
	[Description("Derives the internal scalar title and current-culture localization from MCP title-localizations for mutation flows.")]
	public void ModifyEntitySchemaColumn_Should_Derive_Title_And_CurrentCultureLocalization_From_TitleLocalizations() {
		// Arrange
		using CultureScope cultureScope = new("uk-UA");
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
			TitleLocalizations: Localizations("Vehicle name")));

		// Assert
		result.ExitCode.Should().Be(0,
			because: "localized MCP mutation payloads should still map to validator-safe internal options");
		resolvedCommand.CapturedOptions.Should().NotBeNull(
			because: "the resolved command should receive mapped mutation options");
		resolvedCommand.CapturedOptions!.Title.Should().Be("Vehicle name",
			because: "Clio should derive the internal scalar title from title-localizations");
		resolvedCommand.CapturedOptions.TitleLocalizations.Should().ContainKey("en-US",
			because: "the canonical en-US title localization must be preserved");
		resolvedCommand.CapturedOptions.TitleLocalizations.Should().ContainKey("uk-UA",
			because: "Clio should synthesize a current-culture title localization before save");
		resolvedCommand.CapturedOptions.TitleLocalizations!["uk-UA"].Should().Be("Vehicle name",
			because: "the synthesized current-culture localization should reuse the effective title value");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects legacy scalar title and description fields on the MCP mutation surface.")]
	public void ModifyEntitySchemaColumn_Should_Reject_Legacy_Localization_Fields() {
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
			TitleLocalizations: Localizations("Vehicle name"),
			DescriptionLocalizations: Localizations("Readable vehicle name")) {
			LegacyTitle = "Vehicle name",
			LegacyDescription = "Readable vehicle name"
		});

		// Assert
		result.ExitCode.Should().Be(1);
		result.Output.Should().Contain(message =>
				message.Value != null && message.Value.ToString().Contains("legacy 'title'", System.StringComparison.Ordinal),
			because: "the tool should force callers onto the explicit localization contract");
		resolvedCommand.CapturedOptions.Should().BeNull();
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects localization maps on remove actions because remove must not carry property updates.")]
	public void ModifyEntitySchemaColumn_Should_Reject_Remove_With_Localization_Fields() {
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
			"remove",
			"Name",
			TitleLocalizations: Localizations("Vehicle name")));

		// Assert
		result.ExitCode.Should().Be(1);
		result.Output.Should().Contain(message =>
				message.Value != null && message.Value.ToString().Contains("action is 'remove'", System.StringComparison.Ordinal),
			because: "remove must reject localization-map fields");
		resolvedCommand.CapturedOptions.Should().BeNull();
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects description-localizations payloads that omit en-US.")]
	public void ModifyEntitySchemaColumn_Should_Reject_Description_Localizations_Without_EnUs() {
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
			DescriptionLocalizations: new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase) {
				["uk-UA"] = "Опис"
			}));

		// Assert
		result.ExitCode.Should().Be(1);
		result.Output.Should().Contain(message =>
				message.Value != null && message.Value.ToString().Contains("en-US", System.StringComparison.Ordinal),
			because: "optional localization maps still require the canonical en-US value");
		resolvedCommand.CapturedOptions.Should().BeNull();
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
		createPrompt.Should().Contain("canonical clio MCP",
			because: "create prompt guidance should position the tool inside the neutral clio MCP contract instead of ADAC framing");
		createPrompt.Should().NotContain("ADAC",
			because: "create prompt guidance should no longer describe the entity-schema surface as ADAC-specific");
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
		updatePrompt.Should().Contain("title-localizations",
			because: "batch update prompt guidance should instruct callers to use localization maps");
		updatePrompt.Should().Contain("schema default",
			because: "batch update prompt guidance should explain that seed rows do not define defaults");
		updatePrompt.Should().Contain("docs://mcp/guides/existing-app-maintenance",
			because: "batch update prompt guidance should point callers to the dedicated maintenance guide for existing-app edits");
		updatePrompt.Should().Contain("get-entity-schema-properties",
			because: "batch update prompt guidance should tell callers to inspect the current schema before mutating it");
		updatePrompt.Should().Contain("modify-entity-schema-column",
			because: "batch update prompt guidance should distinguish single-column edits from batch schema updates");
		schemaPrompt.Should().Contain(GetEntitySchemaPropertiesTool.GetEntitySchemaPropertiesToolName,
			because: "schema-read prompt guidance should reference the exact production tool name");
		schemaPrompt.Should().Contain("docs://mcp/guides/existing-app-maintenance",
			because: "schema-read prompt guidance should point callers to the existing-app maintenance guide");
		schemaPrompt.Should().Contain("read step before `modify-entity-schema-column` or `schema-sync`",
			because: "schema-read prompt guidance should describe the canonical inspect step before mutation");
		columnPrompt.Should().Contain(GetEntitySchemaColumnPropertiesTool.GetEntitySchemaColumnPropertiesToolName,
			because: "column-read prompt guidance should reference the exact production tool name");
		columnPrompt.Should().Contain("docs://mcp/guides/existing-app-maintenance",
			because: "column-read prompt guidance should point callers to the existing-app maintenance guide");
		columnPrompt.Should().Contain("before and after `modify-entity-schema-column`",
			because: "column-read prompt guidance should describe the canonical verification pattern for single-column edits");
		modifyPrompt.Should().Contain(ModifyEntitySchemaColumnTool.ModifyEntitySchemaColumnToolName,
			because: "modify prompt guidance should reference the exact production tool name");
		modifyPrompt.Should().Contain("description-localizations",
			because: "modify prompt guidance should instruct callers to use localization maps");
		modifyPrompt.Should().Contain("type",
			because: "mutation prompt guidance should remind callers about action-specific options");
		modifyPrompt.Should().Contain("docs://mcp/guides/existing-app-maintenance",
			because: "modify prompt guidance should point callers to the existing-app maintenance guide");
		modifyPrompt.Should().Contain(GetEntitySchemaColumnPropertiesTool.GetEntitySchemaColumnPropertiesToolName,
			because: "modify prompt guidance should tell callers to inspect current column metadata before mutating it");
	}

	[Test]
	[Category("Unit")]
	[Description("MCP argument descriptions and prompts advertise Binary, Blob, SecureText, and Email aliases plus binary default restrictions.")]
	public void EntitySchemaMcpContract_Should_Advertise_Type_Aliases_And_Default_Restrictions() {
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
		createTypeDescription.Should().Contain("Email",
			because: "create MCP input descriptions should advertise the dedicated Email data type");
		createTypeDescription.Should().Contain("EmailAddress",
			because: "create MCP input descriptions should advertise the supported Email aliases");
		createDefaultSourceDescription.Should().Contain("do not support Const",
			because: "create MCP input descriptions should explain binary-like default restrictions");
		createDefaultSourceDescription.Should().Contain("default-value-config",
			because: "create MCP input descriptions should direct callers to the structured default value contract");
		modifyTypeDescription.Should().Contain("Binary",
			because: "mutation MCP input descriptions should list supported binary-like column types");
		modifyTypeDescription.Should().Contain("Blob",
			because: "mutation MCP input descriptions should advertise the Binary compatibility alias");
		modifyTypeDescription.Should().Contain("EmailAddress",
			because: "mutation MCP input descriptions should advertise the supported Email aliases");
		modifyDefaultDescription.Should().Contain("do not support constant defaults",
			because: "mutation MCP input descriptions should explain binary-like default restrictions");
		createPrompt.Should().Contain("Blob",
			because: "create prompt guidance should advertise the Binary compatibility alias");
		createPrompt.Should().Contain("Email",
			because: "create prompt guidance should advertise the dedicated Email data type");
		createPrompt.Should().Contain("EmailAddress",
			because: "create prompt guidance should advertise the supported Email aliases");
		createPrompt.Should().Contain("default-value-config",
			because: "create prompt guidance should advertise the structured default value contract");
		updatePrompt.Should().Contain("Binary",
			because: "update prompt guidance should advertise binary-like column support");
		updatePrompt.Should().Contain("default-value-config",
			because: "update prompt guidance should advertise the structured default value contract");
		updatePrompt.Should().Contain("EmailAddress",
			because: "update prompt guidance should advertise the supported Email aliases");
		updatePrompt.Should().Contain("default-value-source=Const",
			because: "update prompt guidance should still explain unsupported legacy binary default usage");
		updatePrompt.Should().Contain("schema-sync",
			because: "update prompt guidance should steer multi-step schema workflows toward the composite MCP tool");
		modifyPrompt.Should().Contain("File",
			because: "modify prompt guidance should advertise file column support");
		modifyPrompt.Should().Contain("default-value-config",
			because: "modify prompt guidance should advertise the structured default value contract");
		modifyPrompt.Should().Contain("EmailAddress",
			because: "modify prompt guidance should advertise the supported Email aliases");
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

	private static Dictionary<string, string> Localizations(string enUs, string? ukUa = null) {
		Dictionary<string, string> result = new(System.StringComparer.OrdinalIgnoreCase) {
			["en-US"] = enUs
		};
		if (!string.IsNullOrWhiteSpace(ukUa)) {
			result["uk-UA"] = ukUa;
		}
		return result;
	}

	private sealed class CultureScope : IDisposable {
		private readonly CultureInfo _originalCurrentCulture;
		private readonly CultureInfo _originalCurrentUiCulture;

		public CultureScope(string cultureName) {
			_originalCurrentCulture = CultureInfo.CurrentCulture;
			_originalCurrentUiCulture = CultureInfo.CurrentUICulture;
			CultureInfo culture = CultureInfo.GetCultureInfo(cultureName);
			CultureInfo.CurrentCulture = culture;
			CultureInfo.CurrentUICulture = culture;
		}

		public void Dispose() {
			CultureInfo.CurrentCulture = _originalCurrentCulture;
			CultureInfo.CurrentUICulture = _originalCurrentUiCulture;
		}
	}
}
