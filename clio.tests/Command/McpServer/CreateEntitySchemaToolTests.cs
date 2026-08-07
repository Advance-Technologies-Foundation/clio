using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Globalization;
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
[Property("Module", "McpServer")]
[NonParallelizable]
public class CreateEntitySchemaToolTests {

	[Test]
	[Description("Advertises a stable MCP tool name for remote entity schema creation.")]
	[Category("Unit")]
	public async Task CreateEntitySchemaTool_Should_Advertise_Stable_Tool_Name() {
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
	public async Task CreateEntitySchema_Should_Resolve_Command_For_Requested_Environment() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateEntitySchemaCommand defaultCommand = new();
		FakeCreateEntitySchemaCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(resolvedCommand);
		CreateEntitySchemaTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = await tool.CreateEntitySchema(new CreateEntitySchemaArgs(
			"MyPackage",
			"UsrVehicle",
			Localizations("Vehicle"),
			"docker_fix2",
			"BaseEntity",
			false,
			new List<CreateEntitySchemaColumnArgs> {
				new("Name", "Text", Localizations("Vehicle name")),
				new("Owner", "Lookup", Localizations("Owner"), "Contact")
			}) { IsVirtual = true });

		// Assert
		result.ExitCode.Should().Be(0, "because the tool should forward a valid create entity schema request");
		commandResolver.Received(1).Resolve<CreateEntitySchemaCommand>(Arg.Is<CreateEntitySchemaOptions>(options =>
			options.Package == "MyPackage"
			&& options.SchemaName == "UsrVehicle"
			&& options.Title == "Vehicle"
			&& options.ParentSchemaName == "BaseEntity"
			&& options.IsVirtual
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
	public async Task CreateEntitySchema_Should_Be_Marked_As_Destructive() {
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
	[Description("Auto-derives the en-US column caption from the humanized column name when a create-entity-schema column omits title-localizations.")]
	[Category("Unit")]
	public async Task CreateEntitySchema_Should_AutoDefault_Column_EnUs_From_ColumnName_When_Title_Localizations_Omitted() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateEntitySchemaCommand defaultCommand = new();
		FakeCreateEntitySchemaCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(resolvedCommand);
		CreateEntitySchemaTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = await tool.CreateEntitySchema(new CreateEntitySchemaArgs(
			"MyPackage",
			"UsrVehicle",
			Localizations("Vehicle"),
			"docker_fix2",
			Columns: new[] {
				new CreateEntitySchemaColumnArgs("UsrDueDate", "Date")
			}));

		// Assert
		result.ExitCode.Should().Be(0,
			because: "a bare {column-name, type} column must not hard-fail purely for a missing localization map");
		resolvedCommand.CapturedOptions.Should().NotBeNull(
			because: "the auto-defaulted column should reach command execution");
		resolvedCommand.CapturedOptions!.Columns.Should().Contain(column =>
				column.Contains("Due Date", StringComparison.Ordinal),
			because: "the en-US caption must be the humanized column name (Usr prefix stripped, PascalCase space-split)");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Serializes advanced create-column metadata as structured JSON when the MCP caller supplies fields beyond the legacy CLI column format.")]
	[Category("Unit")]
	public async Task CreateEntitySchema_Should_Serialize_Advanced_Column_Metadata_As_Json() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateEntitySchemaCommand defaultCommand = new();
		FakeCreateEntitySchemaCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(resolvedCommand);
		CreateEntitySchemaTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = await tool.CreateEntitySchema(new CreateEntitySchemaArgs(
			"MyPackage",
			"UsrVehicle",
			Localizations("Vehicle", "Транспорт"),
			"docker_fix2",
			Columns: [
				new CreateEntitySchemaColumnArgs("Status", "ShortText", Localizations("Status", "Статус")) {
					Required = true,
					DefaultValueSource = "Const",
					DefaultValue = "Draft",
					Masked = true
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
		document.RootElement.GetProperty("masked").GetBoolean().Should().BeTrue(
			because: "structured serialization should preserve the optional masked flag");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Serializes structured default-value-config metadata when the MCP caller supplies non-legacy default settings.")]
	[Category("Unit")]
	public async Task CreateEntitySchema_Should_Serialize_DefaultValueConfig_As_Json() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateEntitySchemaCommand defaultCommand = new();
		FakeCreateEntitySchemaCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(resolvedCommand);
		CreateEntitySchemaTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = await tool.CreateEntitySchema(new CreateEntitySchemaArgs(
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
	public async Task CreateEntitySchema_Should_Reject_Title_Localizations_Without_EnUs() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateEntitySchemaCommand defaultCommand = new();
		FakeCreateEntitySchemaCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(resolvedCommand);
		CreateEntitySchemaTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = await tool.CreateEntitySchema(new CreateEntitySchemaArgs(
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
	[Description("When extend-parent is true and no parent-schema-name is supplied, the tool should return exit code 1 because CreateEntitySchemaCommand.Validate rejects extend-parent without an explicit parent.")]
	[Category("Unit")]
	public async Task CreateEntitySchema_Should_Fail_When_ExtendParent_Is_True_And_ParentSchemaName_Is_Omitted() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateEntitySchemaCommand defaultCommand = new();
		CreateEntitySchemaCommand realCommand = new(
			Substitute.For<IRemoteEntitySchemaCreator>(),
			ConsoleLogger.Instance);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(realCommand);
		CreateEntitySchemaTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = await tool.CreateEntitySchema(new CreateEntitySchemaArgs(
			"MyPackage",
			"UsrVehicle",
			Localizations("Vehicle"),
			"docker_fix2",
			ParentSchemaName: null,
			ExtendParent: true));

		// Assert
		result.ExitCode.Should().Be(1,
			because: "extend-parent=true without a parent-schema-name must be rejected by the command validation guard, not silently proceed against BaseEntity");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Forwards an omitted parent-schema-name unchanged to the command; parent defaulting is centralized in CreateEntitySchemaCommand.NormalizeParentSchema (the single source of truth), not re-implemented in the MCP mapping layer.")]
	[Category("Unit")]
	public async Task CreateEntitySchema_Should_ForwardOmittedParentUnchanged_SoTheCommandAppliesTheDefault() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateEntitySchemaCommand defaultCommand = new();
		FakeCreateEntitySchemaCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(resolvedCommand);
		CreateEntitySchemaTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = await tool.CreateEntitySchema(new CreateEntitySchemaArgs(
			"MyPackage",
			"UsrVehicle",
			Localizations("Vehicle"),
			"docker_fix2"));

		// Assert
		result.ExitCode.Should().Be(0,
			because: "omitting parent-schema-name should still produce a valid schema creation request");
		resolvedCommand.CapturedOptions.Should().NotBeNull(
			because: "the resolved command should receive the mapped options");
		resolvedCommand.CapturedOptions!.ParentSchemaName.Should().BeNull(
			because: "the mapping layer must forward the omitted parent unchanged; the BaseEntity default is applied by CreateEntitySchemaCommand.NormalizeParentSchema (covered by CreateEntitySchemaCommandTests) so the predicate lives in exactly one place");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Derives the internal schema title from MCP title-localizations and passes provided localizations through as-is without synthesizing additional cultures.")]
	[Category("Unit")]
	public async Task CreateEntitySchema_Should_Derive_Internal_Title_From_TitleLocalizations_Without_CultureSynthesis() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateEntitySchemaCommand defaultCommand = new();
		FakeCreateEntitySchemaCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(resolvedCommand);
		CreateEntitySchemaTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = await tool.CreateEntitySchema(new CreateEntitySchemaArgs(
			"MyPackage",
			"UsrVehicle",
			Localizations("Vehicle"),
			"docker_fix2"));

		// Assert
		result.ExitCode.Should().Be(0,
			because: "strict MCP localization payloads should still map to a validator-safe internal schema title");
		resolvedCommand.CapturedOptions.Should().NotBeNull(
			because: "the resolved command should receive mapped create options");
		resolvedCommand.CapturedOptions!.Title.Should().Be("Vehicle",
			because: "Clio should derive the internal scalar schema title from title-localizations");
		resolvedCommand.CapturedOptions.TitleLocalizations.Should().ContainKey("en-US",
			because: "the canonical en-US title localization must be preserved");
		resolvedCommand.CapturedOptions.TitleLocalizations.Should().HaveCount(1,
			because: "Clio must not synthesize additional culture localizations beyond what was explicitly provided");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Rejects create-column localization maps that contain empty values.")]
	[Category("Unit")]
	public async Task CreateEntitySchema_Should_Reject_Column_Title_Localizations_With_Empty_Value() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateEntitySchemaCommand defaultCommand = new();
		FakeCreateEntitySchemaCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>())
			.Returns(resolvedCommand);
		CreateEntitySchemaTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = await tool.CreateEntitySchema(new CreateEntitySchemaArgs(
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
	public async Task CreateEntitySchema_Should_Preserve_BinaryLike_Type_Names_In_Column_Serialization(string typeName) {
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
	[Description("Binds a wire payload that carries ONLY 'column-name' onto the column record through the production MCP serializer options, so the alias survives JSON deserialization and is not rejected before ResolveName can run (issue #947, the actual reported failure path).")]
	[Category("Unit")]
	public void CreateEntitySchemaColumnArgs_Should_Deserialize_WireShape_WithOnlyColumnName() {
		// Arrange — the exact JSON an agent following the contract sends: `column-name`, no `name`. This is the
		// half the direct-record tests cannot cover: `Name` is a non-nullable positional parameter, so the
		// question is whether the binder accepts a payload that omits it at all.
		const string payload = """
		{"column-name":"UsrName","type":"MediumText","title-localizations":{"en-US":"Customer name"}}
		""";
		JsonSerializerOptions options = BindingsModule.CreateMcpSerializerOptions();

		// Act
		CreateEntitySchemaColumnArgs? column =
			JsonSerializer.Deserialize<CreateEntitySchemaColumnArgs>(payload, options);

		// Assert
		column.Should().NotBeNull(
			because: "omitting `name` must not fail binding — `column-name` is an equally valid spelling");
		column!.ResolveName().Should().Be("UsrName",
			because: "the wire alias must reach the resolver, which is what the command layer serializes");
		column.ResolveType().Should().Be("MediumText",
			because: "the rest of the wire shape must bind unaffected");
	}

	[Test]
	[Description("The emitted create-entity-schema input schema does not mark a column's 'name' as required, so a strict MCP client that validates against the schema cannot reject a contract-following payload that sends only 'column-name' (issue #947).")]
	[Category("Unit")]
	public void CreateEntitySchemaTool_Should_NotRequireColumnName_InEmittedInputSchema() {
		// Arrange — built through the SDK on the production serializer options, the same path BindingsModule
		// registers, so the assertion is about the schema clients actually receive.
		McpServerTool tool = McpServerTool.Create(
			typeof(CreateEntitySchemaTool).GetMethod(nameof(CreateEntitySchemaTool.CreateEntitySchema))!,
			target: new CreateEntitySchemaTool(
				new FakeCreateEntitySchemaCommand(),
				ConsoleLogger.Instance,
				Substitute.For<IToolCommandResolver>()),
			new McpServerToolCreateOptions { SerializerOptions = BindingsModule.CreateMcpSerializerOptions() });

		// Act
		string schema = JsonSerializer.Serialize(tool.ProtocolTool.InputSchema);

		// Assert — `name` is one of two accepted spellings, so requiring it in the schema would contradict the
		// contract that advertises `column-name` as canonical. The `required` array itself is asserted by
		// navigating the schema in ColumnIdentityEmittedSchemaTests: the exact-substring form this test used to
		// carry (`NotContain("\"required\":[\"name\",\"type\"]")`) passed vacuously on any element-order,
		// whitespace, or extra-field change — i.e. precisely when the relaxation had regressed (PR #984 review).
		schema.Should().Contain("column-name",
			because: "the canonical column identity field must appear in the emitted schema at all");
	}

	[Test]
	[Description("Reports a missing column type as a missing column type — naming both accepted spellings — now that `type` is optional in the emitted schema so its `data-value-type` alias stays usable (issue #947).")]
	[Category("Unit")]
	public void CreateEntitySchema_Should_Fail_With_ColumnType_Message_WhenNeitherTypeSpellingSupplied() {
		// Arrange
		var columns = new[] {
			new CreateEntitySchemaColumnArgs("UsrName", null, Localizations("Customer name"))
		};

		// Act
		Action act = () => CreateEntitySchemaTool.SerializeColumns(columns, "Schema 'UsrOrder'");

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("*data-value-type*",
				because: "both accepted spellings must be named so the caller can pick either")
			.And.Message.Should().Contain("column type",
				because: "the message must identify WHICH field is missing, not fail vaguely downstream");
	}

	[Test]
	[Description("Accepts the 'data-value-type' alias alone as the column type, mirroring the column-name alias so a get-app-info read shape round-trips into a create without translation.")]
	[Category("Unit")]
	public void CreateEntitySchema_Should_Serialize_DataValueTypeAlias_AsType() {
		// Arrange
		var columns = new[] {
			new CreateEntitySchemaColumnArgs("UsrName", null, Localizations("Customer name")) {
				DataValueTypeAlias = "MediumText"
			}
		};

		// Act
		string serializedColumn = CreateEntitySchemaTool.SerializeColumns(columns, "Schema 'UsrOrder'")!.Single();

		// Assert
		using JsonDocument document = JsonDocument.Parse(serializedColumn);
		document.RootElement.GetProperty("type").GetString().Should().Be("MediumText",
			because: "the read-shape alias must reach the command layer's 'type' field");
	}

	[Test]
	[Description("Keeps the wire shape working when only the 'name' alias is sent, so accepting 'column-name' did not regress the spelling already in use by get-app-info round-trips (issue #947).")]
	[Category("Unit")]
	public void CreateEntitySchemaColumnArgs_Should_Deserialize_WireShape_WithOnlyName() {
		// Arrange
		const string payload = """
		{"name":"UsrName","type":"MediumText","title-localizations":{"en-US":"Customer name"}}
		""";
		JsonSerializerOptions options = BindingsModule.CreateMcpSerializerOptions();

		// Act
		CreateEntitySchemaColumnArgs? column =
			JsonSerializer.Deserialize<CreateEntitySchemaColumnArgs>(payload, options);

		// Assert
		column!.ResolveName().Should().Be("UsrName",
			because: "the canonical field must keep binding exactly as before");
	}

	[Test]
	[Description("Binds a sync-schemas create-entity operation whose column carries only 'column-name' through the production MCP serializer options, covering the batch wire shape as well as the single-column one (issue #947).")]
	[Category("Unit")]
	public void SchemaSyncOperation_Should_Deserialize_CreateEntityColumn_WithOnlyColumnName() {
		// Arrange
		const string payload = """
		{"type":"create-entity","schema-name":"UsrOrder","title-localizations":{"en-US":"Order"},
		 "columns":[{"column-name":"UsrName","type":"MediumText","title-localizations":{"en-US":"Customer name"}}]}
		""";
		JsonSerializerOptions options = BindingsModule.CreateMcpSerializerOptions();

		// Act
		SchemaSyncOperation? operation = JsonSerializer.Deserialize<SchemaSyncOperation>(payload, options);

		// Assert
		operation.Should().NotBeNull(because: "the create-entity operation shape must bind");
		operation!.Columns!.Single().ResolveName().Should().Be("UsrName",
			because: "the batch path must carry the alias through to the resolver too");
	}

	[Test]
	[Description("Serializes the canonical 'column-name' spelling into the command layer's 'name' field, so a caller following the get-tool-contract column identity field is not silently reduced to name:null (issue #947).")]
	[Category("Unit")]
	public void CreateEntitySchema_Should_Serialize_Canonical_ColumnName_Alias() {
		// Arrange — only `column-name` is supplied, exactly as the advertised contract describes it.
		var columns = new[] {
			new CreateEntitySchemaColumnArgs(null!, "MediumText", Localizations("Customer name")) {
				ColumnNameAlias = "UsrName"
			}
		};

		// Act
		string serializedColumn = CreateEntitySchemaTool.SerializeColumns(columns, "Schema 'UsrOrder'")!.Single();

		// Assert
		using JsonDocument document = JsonDocument.Parse(serializedColumn);
		document.RootElement.GetProperty("name").GetString().Should().Be("UsrName",
			because: "the contract advertises 'column-name' as the column identity field, so it must reach the " +
				"command layer's 'name' instead of being dropped");
	}

	[Test]
	[Description("Prefers the canonical 'name' over the 'column-name' alias when a caller sends both, so the resolution order is deterministic.")]
	[Category("Unit")]
	public void CreateEntitySchema_Should_Prefer_Name_Over_ColumnNameAlias_WhenBothSupplied() {
		// Arrange
		var columns = new[] {
			new CreateEntitySchemaColumnArgs("UsrCanonical", "Text", Localizations("Canonical")) {
				ColumnNameAlias = "UsrAlias"
			}
		};

		// Act
		string serializedColumn = CreateEntitySchemaTool.SerializeColumns(columns, "Schema 'UsrOrder'")!.Single();

		// Assert
		using JsonDocument document = JsonDocument.Parse(serializedColumn);
		document.RootElement.GetProperty("name").GetString().Should().Be("UsrCanonical",
			because: "ResolveName prefers the canonical field and falls back to the alias only when it is absent");
	}

	[Test]
	[Description("Reports a missing column identity as a missing target column — naming both accepted spellings — instead of failing later on an unrelated localization or type message (issue #947). The wording is single-sourced in ColumnIdentityContract.RequireColumnIdentity, so all three throw sites say the same thing (PR #984 review).")]
	[Category("Unit")]
	public void CreateEntitySchema_Should_Fail_With_ColumnIdentity_Message_WhenNeitherSpellingSupplied() {
		// Arrange — neither `name` nor `column-name`, and no title either: the localization contract would
		// otherwise be the first to fail and would blame the caption.
		var columns = new[] {
			new CreateEntitySchemaColumnArgs(null!, "Text", null)
		};

		// Act
		Action act = () => CreateEntitySchemaTool.SerializeColumns(columns, "Schema 'UsrOrder'");

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("*column-name*",
				because: "the error must name the canonical field the caller is expected to send")
			.And.Message.Should().Contain("'name'",
				because: "both accepted spellings must be named so the caller can pick either");
		act.Should().Throw<ArgumentException>()
			.And.Message.Should().Contain("missing the target column",
				because: "the three throw sites now share one message through RequireColumnIdentity — this pins " +
					"the canonical noun so the wording cannot drift back apart");
	}

	[Test]
	[Description("Maps create-lookup MCP arguments into create-entity-schema command options and forces BaseLookup as the parent schema.")]
	[Category("Unit")]
	public async Task CreateLookup_Should_Resolve_Command_For_Requested_Environment() {
		// Arrange
		using CultureScope cultureScope = new("en-US");
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
		CommandExecutionResult result = await tool.CreateLookup(new CreateLookupArgs(
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
	public async Task CreateLookup_Should_Reject_Inherited_BaseLookup_Columns() {
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
		CommandExecutionResult result = await tool.CreateLookup(new CreateLookupArgs(
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
	[Description("Rejects inherited BaseLookup columns spelled with the contract's canonical 'column-name' field, not just the 'name' alias — the guardrail must read the resolved name or {\"column-name\":\"Name\"} bypasses it entirely (PR #984 review).")]
	[Category("Unit")]
	public async Task CreateLookup_Should_Reject_Inherited_BaseLookup_Columns_WhenSpelledAsColumnName() {
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

		// Act — `column-name` only, no `name`: the spelling get-tool-contract advertises as canonical.
		CommandExecutionResult result = await tool.CreateLookup(new CreateLookupArgs(
			"MyPackage",
			"UsrOrderStatus",
			Localizations("Order status"),
			"docker_fix2",
			[
				new CreateEntitySchemaColumnArgs(null, "Text", Localizations("Name")) {
					ColumnNameAlias = "Name"
				}
			]));
		string[] outputValues = result.Output
			.Select(message => message.Value?.ToString() ?? string.Empty)
			.ToArray();

		// Assert
		result.ExitCode.Should().Be(1,
			because: "the inherited-column guardrail must fire on the canonical spelling too, otherwise the " +
				"shadowing column reaches RemoteEntitySchemaCreator, which has no equivalent check");
		outputValues.Should().Contain(value =>
				value.Contains("BaseLookup", StringComparison.Ordinal) && value.Contains("Name", StringComparison.Ordinal),
			because: "the caller should still get clio's purpose-built explanation, not a remote failure");
		resolvedCommand.CapturedOptions.Should().BeNull(
			because: "no command may run once the guardrail rejects the payload");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Preserves omitted optional columns when create-lookup callers only provide the required lookup schema metadata.")]
	[Category("Unit")]
	public async Task CreateLookup_Should_Preserve_Defaults_When_Columns_Are_Omitted() {
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
		CommandExecutionResult result = await tool.CreateLookup(new CreateLookupArgs(
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
	public async Task CreateLookup_Should_Serialize_Advanced_Column_Metadata_As_Json() {
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
		CommandExecutionResult result = await tool.CreateLookup(new CreateLookupArgs(
			"MyPackage",
			"UsrOrderStatus",
			Localizations("Order status"),
			"docker_fix2",
			[
				new CreateEntitySchemaColumnArgs("Status", "ShortText", Localizations("Status", "Статус")) {
					Required = true,
					DefaultValueSource = "Const",
					DefaultValue = "Draft",
					Masked = true
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
		document.RootElement.GetProperty("masked").GetBoolean().Should().BeTrue(
			because: "structured serialization should preserve the optional masked flag");
		registrationService.Received(1).EnsureLookupRegistration("MyPackage", "UsrOrderStatus", "Order status");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns a failed MCP result when lookup creation succeeds but Lookups registration fails.")]
	[Category("Unit")]
	public async Task CreateLookup_Should_Fail_When_Lookup_Registration_Fails() {
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
		CommandExecutionResult result = await tool.CreateLookup(new CreateLookupArgs(
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
