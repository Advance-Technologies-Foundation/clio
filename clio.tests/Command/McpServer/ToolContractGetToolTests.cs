using System;
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
				PageSyncTool.ToolName,
				PageUpdateTool.ToolName,
				ModifyEntitySchemaColumnTool.ModifyEntitySchemaColumnToolName
			],
			because: "the canonical contract set should include bootstrap diagnostics plus the key existing-app discovery and page mutation tools");
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
	[Description("Returns maintenance-oriented canonical flows for discovery inspection and canonical page mutation tools.")]
	public void ToolContractGet_Should_Return_Maintenance_Oriented_Canonical_Flows() {
		// Arrange
		ToolContractGetTool tool = new();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs([
			ApplicationGetListTool.ApplicationGetListToolName,
			PageListTool.ToolName,
			PageGetTool.ToolName,
			PageSyncTool.ToolName,
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
		applicationListContract.Examples.Should().ContainSingle(example =>
				example.Arguments.Keys.SequenceEqual(new[] { "environment-name" }),
			because: "application-get-list should advertise the minimal top-level payload explicitly");
		ToolContractDefinition pageListContract = contracts.Single(contract => contract.Name == PageListTool.ToolName);
		pageListContract.PreferredFlow.Tools.Should().Equal(
				new[] {
					PageListTool.ToolName,
					PageGetTool.ToolName,
					PageSyncTool.ToolName,
					PageGetTool.ToolName
				},
				because: "page-list should advertise the canonical clio page workflow after discovery");
		ToolContractDefinition pageGetContract = contracts.Single(contract => contract.Name == PageGetTool.ToolName);
		pageGetContract.PreferredFlow.Tools.Should().Equal(
				new[] {
					PageListTool.ToolName,
					PageGetTool.ToolName,
					PageSyncTool.ToolName,
					PageGetTool.ToolName
				},
				because: "page-get should advertise page-sync as the canonical save path after inspection");
		ToolContractDefinition pageSyncContract = contracts.Single(contract => contract.Name == PageSyncTool.ToolName);
		pageSyncContract.PreferredFlow.Tools.Should().Equal(
				new[] {
					PageListTool.ToolName,
					PageGetTool.ToolName,
					PageSyncTool.ToolName,
					PageGetTool.ToolName
				},
				because: "page-sync should advertise itself as the canonical page write path");
		ToolContractDefinition pageUpdateContract = contracts.Single(contract => contract.Name == PageUpdateTool.ToolName);
		pageUpdateContract.PreferredFlow.Tools.Should().Equal(
				new[] {
					PageGetTool.ToolName,
					PageUpdateTool.ToolName,
					PageGetTool.ToolName
				},
				because: "page-update still needs a concrete fallback flow for callers that explicitly require it");
		pageUpdateContract.Deprecations.Should().ContainSingle(deprecation =>
				deprecation.ReplacementTools.SequenceEqual(new[] { PageSyncTool.ToolName }) &&
				deprecation.Message.Contains("fallback"),
			because: "page-update should advertise page-sync as the canonical replacement");
		pageUpdateContract.FallbackFlow.Should().Contain(flow => flow.Tools.SequenceEqual(new[] {
				PageListTool.ToolName,
				PageGetTool.ToolName,
				PageSyncTool.ToolName,
				PageGetTool.ToolName
			}),
			because: "page-update should point callers back to the canonical page-sync workflow");
		pageSyncContract.InputSchema.Properties.Should().Contain(field =>
				field.Name == "pages" &&
				field.Description.Contains("page-get.raw.body"),
			because: "page-sync should advertise raw.body as the source of page write payloads");
		pageUpdateContract.InputSchema.Properties.Should().Contain(field =>
				field.Name == "body" &&
				field.Description.Contains("page-get.raw.body"),
			because: "page-update should advertise raw.body as the source of fallback single-page saves");
		pageUpdateContract.InputSchema.Properties.Should().Contain(field =>
				field.Name == "resources" &&
				field.Description.Contains("JSON object string"),
			because: "page-update should clarify the concrete resources payload shape");
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
	[Description("Returns the full canonical entity-schema contract surface with authoritative flows and metadata from clio.")]
	public void ToolContractGet_Should_Return_Canonical_EntitySchema_Surface() {
		// Arrange
		ToolContractGetTool tool = new();
		string[] requestedTools = [
			SchemaSyncTool.ToolName,
			CreateLookupTool.CreateLookupToolName,
			CreateEntitySchemaTool.CreateEntitySchemaToolName,
			UpdateEntitySchemaTool.UpdateEntitySchemaToolName,
			GetEntitySchemaPropertiesTool.GetEntitySchemaPropertiesToolName,
			GetEntitySchemaColumnPropertiesTool.GetEntitySchemaColumnPropertiesToolName,
			ModifyEntitySchemaColumnTool.ModifyEntitySchemaColumnToolName
		];

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs(requestedTools));

		// Assert
		result.Success.Should().BeTrue(
			because: "tool-contract-get should expose the full canonical entity/schema MCP surface from clio");
		result.Tools.Should().NotBeNull(
			because: "successful canonical surface lookup should include contract definitions");
		result.Tools!.Select(contract => contract.Name).Should().BeEquivalentTo(requestedTools,
			because: "the canonical entity/schema tool surface should be retrievable as one consistent contract set");
		result.Tools.Should().OnlyContain(contract =>
				contract.OutputContract != null
				&& contract.ErrorContract != null
				&& contract.PreferredFlow != null
				&& contract.FallbackFlow != null,
			because: "each canonical schema tool contract should publish output, error, and flow metadata");
		result.Tools.Should().Contain(contract =>
				contract.Name == SchemaSyncTool.ToolName
				&& contract.PreferredFlow.Tools.SequenceEqual(new[] {
					ApplicationCreateTool.ApplicationCreateToolName,
					SchemaSyncTool.ToolName,
					ApplicationGetInfoTool.ApplicationGetInfoToolName
				}),
			because: "schema-sync should advertise the canonical batched entity workflow");
		result.Tools.Should().Contain(contract =>
				contract.Name == CreateLookupTool.CreateLookupToolName
				&& contract.PreferredFlow.Tools.SequenceEqual(new[] { SchemaSyncTool.ToolName }),
			because: "create-lookup should advertise schema-sync as the preferred canonical path");
		result.Tools.Should().Contain(contract =>
				contract.Name == CreateEntitySchemaTool.CreateEntitySchemaToolName
				&& contract.PreferredFlow.Tools.SequenceEqual(new[] { SchemaSyncTool.ToolName }),
			because: "create-entity-schema should advertise schema-sync as the preferred canonical path");
		result.Tools.Should().Contain(contract =>
				contract.Name == UpdateEntitySchemaTool.UpdateEntitySchemaToolName
				&& contract.PreferredFlow.Tools.SequenceEqual(new[] {
					GetEntitySchemaPropertiesTool.GetEntitySchemaPropertiesToolName,
					UpdateEntitySchemaTool.UpdateEntitySchemaToolName,
					GetEntitySchemaPropertiesTool.GetEntitySchemaPropertiesToolName
				}),
			because: "update-entity-schema should advertise the canonical inspect-mutate-verify flow");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the canonical DB-first binding contract surface with explicit fallback, lifecycle, and failure guidance.")]
	public void ToolContractGet_Should_Return_Canonical_DbFirst_Binding_Surface() {
		// Arrange
		ToolContractGetTool tool = new();
		string[] requestedTools = [
			CreateDataBindingDbTool.CreateDataBindingDbToolName,
			UpsertDataBindingRowDbTool.UpsertDataBindingRowDbToolName,
			RemoveDataBindingRowDbTool.RemoveDataBindingRowDbToolName
		];

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs(requestedTools));

		// Assert
		result.Success.Should().BeTrue(
			because: "the authoritative DB-first binding surface should be served by clio");
		result.Tools.Should().NotBeNull(
			because: "a successful lookup should return the requested DB-first binding contracts");
		result.Tools!.Select(contract => contract.Name).Should().Equal(requestedTools,
			because: "the response should preserve the requested DB-first binding tool order");

		ToolContractDefinition createContract = result.Tools.Single(contract =>
			contract.Name == CreateDataBindingDbTool.CreateDataBindingDbToolName);
		createContract.PreferredFlow.Tools.Should().Equal(
				new[] {
					SchemaSyncTool.ToolName
				},
				because: "create-data-binding-db should advertise schema-sync as the canonical batched path");
		createContract.Deprecations.Should().ContainSingle(
			because: "create-data-binding-db should advertise that it is a fallback or standalone path");
		createContract.Deprecations[0].Message.Should().Contain("fallback",
			because: "the deprecation guidance should explicitly frame create-data-binding-db as a fallback");
		createContract.Deprecations[0].Message.Should().Contain("seed-rows",
			because: "the deprecation guidance should point callers at inline seed-rows inside schema-sync");
		createContract.Deprecations[0].Message.Should().Contain("direct SQL",
			because: "the deprecation guidance should keep standalone lookup seeding on the MCP surface");
		createContract.InputSchema.Properties.Should().Contain(field =>
				field.Name == "rows" &&
				field.Description.Contains("values object"),
			because: "create-data-binding-db should canonically describe the required rows[].values shape");
		createContract.Examples.Should().Contain(example =>
				example.Arguments["rows"] != null &&
				example.Arguments["rows"].ToString()!.Contains("In Progress", StringComparison.Ordinal),
			because: "create-data-binding-db should advertise a realistic multi-row lookup seeding example");

		ToolContractDefinition upsertContract = result.Tools.Single(contract =>
			contract.Name == UpsertDataBindingRowDbTool.UpsertDataBindingRowDbToolName);
		upsertContract.PreferredFlow.Tools.Should().Equal(
				new[] {
					CreateDataBindingDbTool.CreateDataBindingDbToolName,
					UpsertDataBindingRowDbTool.UpsertDataBindingRowDbToolName
				},
				because: "upsert-data-binding-row-db should advertise the required create-then-upsert sequence");
		upsertContract.ErrorContract.Codes.Should().Contain(code => code.Code == "binding-not-found",
			because: "upsert-data-binding-row-db should document the missing-binding failure mode");

		ToolContractDefinition removeContract = result.Tools.Single(contract =>
			contract.Name == RemoveDataBindingRowDbTool.RemoveDataBindingRowDbToolName);
		removeContract.Description.Should().Contain("package schema data record",
			because: "remove-data-binding-row-db should document the last-row lifecycle cleanup");
		removeContract.InputSchema.Properties.Should().Contain(field => field.Name == "key-value",
			because: "remove-data-binding-row-db should continue advertising the canonical key-value parameter name");
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
		contract.Examples.Should().ContainSingle(example =>
				example.Summary.Contains("top-level payload", StringComparison.Ordinal),
			because: "application-create should advertise the minimal top-level request shape explicitly");
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
