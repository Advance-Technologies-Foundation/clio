using System.Linq;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using ModelContextProtocol.Server;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
public sealed class ToolContractGetToolTests {
	[Test]
	[Category("Unit")]
	[Description("Advertises a stable MCP tool name for tool-contract-get.")]
	public void ToolContractGet_Should_Advertise_Stable_Tool_Name() {
		// Arrange

		// Act
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(ToolContractGetTool)
			.GetMethod(nameof(ToolContractGetTool.GetToolContracts))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Assert
		attribute.Name.Should().Be(ToolContractGetTool.ToolName,
			because: "the MCP tool name must stay stable for clients that bootstrap from the contract tool");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the canonical clio MCP contract set when the request omits tool names.")]
	public void ToolContractGet_Should_Return_Canonical_Contracts_When_Request_Is_Empty() {
		// Arrange
		ToolContractGetTool tool = new();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs());

		// Assert
		result.Success.Should().BeTrue(
			because: "an empty request should be a valid bootstrap entry point for canonical contract discovery");
		result.Error.Should().BeNull(
			because: "a successful bootstrap lookup should not return a structured error");
		result.Tools.Should().NotBeNull(
			because: "the bootstrap response should include the canonical contract set");
		result.Tools!.Select(contract => contract.Name).Should().Contain([
				SettingsHealthTool.ToolName,
				ApplicationGetListTool.ApplicationGetListToolName,
				PageUpdateTool.ToolName,
				ModifyEntitySchemaColumnTool.ModifyEntitySchemaColumnToolName
			],
			because: "the canonical contract set should include bootstrap diagnostics plus the key existing-app discovery and minimal mutation tools");
		result.Tools!.Select(contract => contract.Name).Should().NotContain(ToolContractGetTool.ToolName,
			because: "tool-contract-get should not include itself in the default returned contract set");
	}

	[Test]
	[Category("Unit")]
	[Description("Advertises the structured settings-health output contract for bootstrap diagnostics.")]
	public void ToolContractGet_Should_Advertise_Settings_Health_Contract() {
		// Arrange
		ToolContractGetTool tool = new();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs([
			SettingsHealthTool.ToolName
		]));

		// Assert
		result.Success.Should().BeTrue(
			because: "the settings-health contract should be available through tool-contract-get");
		ToolContractDefinition contract = result.Tools!.Single();
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "status",
			because: "bootstrap diagnostics should advertise their high-level health state");
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "settings-file-path",
			because: "bootstrap diagnostics should expose the appsettings.json file path");
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "repairs-applied",
			because: "bootstrap diagnostics should expose automatic repairs in structured form");
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "can-execute-env-tools",
			because: "bootstrap diagnostics should tell callers whether named-environment tools can run");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns maintenance-oriented canonical flows for discovery inspection and minimal mutation tools.")]
	public void ToolContractGet_Should_Return_Maintenance_Oriented_Canonical_Flows() {
		// Arrange
		ToolContractGetTool tool = new();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs([
			ApplicationGetListTool.ApplicationGetListToolName,
			PageUpdateTool.ToolName,
			ModifyEntitySchemaColumnTool.ModifyEntitySchemaColumnToolName
		]));

		// Assert
		result.Success.Should().BeTrue(
			because: "the requested maintenance-oriented contracts are all registered by clio MCP");
		ToolContractDefinition[] contracts = result.Tools!.ToArray();
		ToolContractDefinition applicationListContract = contracts.Single(contract => contract.Name == ApplicationGetListTool.ApplicationGetListToolName);
		applicationListContract.PreferredFlow.Tools.Should().Equal(
				new[] {
					ApplicationGetListTool.ApplicationGetListToolName,
					ApplicationGetInfoTool.ApplicationGetInfoToolName
				},
				because: "application discovery should flow into application inspection for existing-app edits");
		ToolContractDefinition pageUpdateContract = contracts.Single(contract => contract.Name == PageUpdateTool.ToolName);
		pageUpdateContract.PreferredFlow.Tools.Should().Equal(
				new[] {
					PageGetTool.ToolName,
					PageUpdateTool.ToolName,
					PageGetTool.ToolName
				},
				because: "single-page edits should read before write and read back after saving when verification is needed");
		pageUpdateContract.FallbackFlow.Should().Contain(flow => flow.Tools.SequenceEqual(new[] {
				PageListTool.ToolName,
				PageGetTool.ToolName,
				PageSyncTool.ToolName,
				PageGetTool.ToolName
			}),
			because: "page-update should advertise page-sync as the fallback when the work expands into a multi-page flow");
		ToolContractDefinition modifyColumnContract = contracts.Single(contract => contract.Name == ModifyEntitySchemaColumnTool.ModifyEntitySchemaColumnToolName);
		modifyColumnContract.PreferredFlow.Tools.Should().Equal(
				new[] {
					GetEntitySchemaColumnPropertiesTool.GetEntitySchemaColumnPropertiesToolName,
					ModifyEntitySchemaColumnTool.ModifyEntitySchemaColumnToolName,
					GetEntitySchemaColumnPropertiesTool.GetEntitySchemaColumnPropertiesToolName
				},
				because: "single-column schema edits should inspect current metadata first and read it back after saving");
		modifyColumnContract.FallbackFlow.Should().Contain(flow => flow.Tools.SequenceEqual(new[] {
				SchemaSyncTool.ToolName
			}),
			because: "modify-entity-schema-column should still advertise schema-sync when the work expands into a multi-step ordered schema plan");
	}

	[Test]
	[Category("Unit")]
	[Description("Advertises enriched application-get-info output fields for installed application identity.")]
	public void ToolContractGet_Should_Advertise_Application_Info_Identity_Fields() {
		// Arrange
		ToolContractGetTool tool = new();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs([
			ApplicationGetInfoTool.ApplicationGetInfoToolName
		]));

		// Assert
		result.Success.Should().BeTrue(
			because: "the application-get-info contract should be available through tool-contract-get");
		ToolContractDefinition contract = result.Tools!.Single();
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "application-id",
			because: "the contract should advertise the installed application identifier");
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "application-name",
			because: "the contract should advertise the installed application display name");
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "application-code",
			because: "the contract should advertise the installed application code");
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "application-version",
			because: "the contract should advertise the installed application version");
	}

	[Test]
	[Category("Unit")]
	[Description("Advertises the canonical application-create validators aliases and preferred flow through tool-contract-get.")]
	public void ToolContractGet_Should_Advertise_Application_Create_Canonical_Rules() {
		// Arrange
		ToolContractGetTool tool = new();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs([
			ApplicationCreateTool.ApplicationCreateToolName
		]));

		// Assert
		result.Success.Should().BeTrue(
			because: "the application-create contract should be available through tool-contract-get");
		ToolContractDefinition contract = result.Tools!.Single();
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "canonical-main-entity-name",
			because: "application-create should advertise the canonical main entity field in its response shape");
		contract.InputSchema.Validators.Should().ContainSingle(validator =>
				validator.Name == "forbid-fields"
				&& validator.Fields!.Contains("title-localizations")
				&& validator.Fields.Contains("descriptionLocalizations"),
			because: "application-create should advertise forbidden localization maps through the canonical contract");
		contract.Aliases.Should().Contain(alias =>
				alias.CanonicalName == "code"
				&& alias.Alias == "app-code"
				&& alias.Status == "rejected",
			because: "application-create should reject legacy alias parameters through the canonical contract");
		contract.Aliases.Should().Contain(alias =>
				alias.CanonicalName == "name"
				&& alias.Alias == "app-name"
				&& alias.Status == "rejected",
			because: "application-create should reject legacy alias parameters through the canonical contract");
		contract.PreferredFlow.Tools.Should().Equal(
			new[] {
				ApplicationCreateTool.ApplicationCreateToolName,
				SchemaSyncTool.ToolName,
				ApplicationGetInfoTool.ApplicationGetInfoToolName
			},
			because: "application-create should advertise the canonical create -> schema-sync -> refresh flow");
		contract.FallbackFlow.Should().Contain(flow => flow.Tools.SequenceEqual(new[] {
				ApplicationGetListTool.ApplicationGetListToolName,
				ApplicationGetInfoTool.ApplicationGetInfoToolName
			}),
			because: "application-create should advertise the canonical existing-app fallback flow");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns structured unknown tool suggestions when the requested tool name is misspelled.")]
	public void ToolContractGet_Should_Return_Structured_Error_For_Unknown_Tool() {
		// Arrange
		ToolContractGetTool tool = new();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs([
			"page-updte"
		]));

		// Assert
		result.Success.Should().BeFalse(
			because: "a misspelled tool name should fail contract lookup");
		result.Tools.Should().BeNull(
			because: "no contract payload should be returned when the lookup fails");
		result.Error.Should().NotBeNull(
			because: "the tool should return a structured error envelope for unknown names");
		result.Error!.Code.Should().Be("tool-not-found",
			because: "unknown tool names should map to the tool-not-found error code");
		result.Error.Suggestions.Should().Contain(PageUpdateTool.ToolName,
			because: "the error should suggest the closest matching registered tool name");
	}

	[Test]
	[Category("Unit")]
	[Description("Uses the canonical required field name in the modify-entity-schema-column contract.")]
	public void ToolContractGet_Should_Use_Canonical_Required_Key_For_Modify_Entity_Contract() {
		// Arrange
		ToolContractGetTool tool = new();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs([
			ModifyEntitySchemaColumnTool.ModifyEntitySchemaColumnToolName
		]));

		// Assert
		result.Success.Should().BeTrue(
			because: "the modify-entity-schema-column contract lookup should succeed");
		ToolContractDefinition contract = result.Tools!.Single();
		contract.InputSchema.Properties.Should().Contain(field => field.Name == "required",
			because: "the contract should advertise the canonical required field name");
		contract.InputSchema.Properties.Should().NotContain(field => field.Name == "is-required",
			because: "legacy aliases should not be exposed as canonical request fields");
		contract.Examples.SelectMany(example => example.Arguments.Keys).Should().Contain("required",
			because: "the examples should use the canonical required field name");
		contract.Examples.SelectMany(example => example.Arguments.Keys).Should().NotContain("is-required",
			because: "the examples should not teach callers to use the removed legacy alias");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns field-level validation errors when the request contains blank tool names.")]
	public void ToolContractGet_Should_Return_Field_Level_Error_For_Blank_Tool_Name() {
		// Arrange
		ToolContractGetTool tool = new();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs([
			" "
		]));

		// Assert
		result.Success.Should().BeFalse(
			because: "blank tool names are invalid input");
		result.Error.Should().NotBeNull(
			because: "invalid input should return a structured validation error");
		result.Error!.Code.Should().Be("missing-required-parameter",
			because: "blank tool names should be treated as missing required values");
		result.Error.FieldErrors.Should().ContainSingle(
			because: "the validation error should identify the exact offending entry");
		result.Error.FieldErrors![0].Field.Should().Be("tool-names[0]",
			because: "the field path should point to the blank element inside tool-names");
	}
}
