using System;
using System.Linq;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using ModelContextProtocol.Server;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class ToolContractGetToolTests {
	[Test]
	[Category("Unit")]
	[Description("Advertises a stable MCP tool name for get-tool-contract.")]
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
				GuidanceGetTool.ToolName,
				SettingsHealthTool.ToolName,
				ApplicationGetListTool.ApplicationGetListToolName,
				ApplicationSectionCreateTool.ApplicationSectionCreateToolName,
				ApplicationSectionUpdateTool.ApplicationSectionUpdateToolName,
				DataForgeTool.DataForgeHealthToolName,
				DataForgeTool.DataForgeContextToolName,
				PageSyncTool.ToolName,
				PageUpdateTool.ToolName,
				ModifyEntitySchemaColumnTool.ModifyEntitySchemaColumnToolName
			],
			because: "the canonical contract set should include bootstrap diagnostics, read-only Data Forge discovery/context tools, and the key existing-app discovery and page mutation tools");
		result.Tools!.Select(contract => contract.Name).Should().NotContain([
				DataForgeTool.DataForgeInitializeToolName,
				DataForgeTool.DataForgeUpdateToolName
			],
			because: "destructive Data Forge maintenance tools should stay available only through explicit contract lookup rather than the default bootstrap set");
		result.Tools!.Select(contract => contract.Name).Should().NotContain(ToolContractGetTool.ToolName,
			because: "get-tool-contract should not include itself in the default returned contract set");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the canonical get-guidance contract so callers can retrieve guidance through a tool instead of docs URI routing.")]
	public void ToolContractGet_Should_Return_Guidance_Get_Contract() {
		// Arrange
		ToolContractGetTool tool = new();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs([
			GuidanceGetTool.ToolName
		]));

		// Assert
		result.Success.Should().BeTrue(
			because: "get-guidance is part of the executable clio MCP contract surface");
		ToolContractDefinition contract = result.Tools!.Single();
		contract.InputSchema.Required.Should().ContainSingle(required => required == "name",
			because: "guidance lookup should require the stable guide name");
		contract.InputSchema.Properties.Should().Contain(field =>
				field.Name == "name" &&
				field.Description.Contains("page-schema-handlers", StringComparison.Ordinal) &&
				field.Description.Contains("page-schema-validators", StringComparison.Ordinal),
			because: "the contract should advertise the stable guidance-name selector");
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "guidance",
			because: "successful lookups should return the resolved article payload");
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "available-guides",
			because: "failed lookups should expose recovery names");
		contract.Examples.Any(example =>
			example.Arguments.TryGetValue("name", out object? value)
			&& string.Equals(value?.ToString(), "page-schema-handlers", StringComparison.Ordinal)).Should().BeTrue(
			because: "the contract should advertise the canonical handler guidance lookup example");
	}

	[Test]
	[Category("Unit")]
	[Description("Advertises the structured check-settings-health output contract for bootstrap diagnostics.")]
	public void ToolContractGet_Should_Advertise_Settings_Health_Contract() {
		// Arrange
		ToolContractGetTool tool = new();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs([
			SettingsHealthTool.ToolName
		]));

		// Assert
		result.Success.Should().BeTrue(
			because: "the check-settings-health contract should be available through get-tool-contract");
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
			because: "list-apps should advertise the minimal top-level payload explicitly");
		ToolContractDefinition pageListContract = contracts.Single(contract => contract.Name == PageListTool.ToolName);
			pageListContract.PreferredFlow.Tools.Should().Equal(
					new[] {
						PageListTool.ToolName,
						PageGetTool.ToolName,
						PageSyncTool.ToolName,
						PageGetTool.ToolName
					},
					because: "list-pages should advertise the canonical clio page workflow after discovery");
			pageListContract.Aliases.Should().Contain(alias =>
					alias.CanonicalName == "code"
					&& alias.Alias == "app-code"
					&& alias.Status == "rejected",
				because: "list-pages should reject the legacy app-code selector through the canonical contract");
			pageListContract.FallbackFlow.Should().Contain(flow => flow.Tools.SequenceEqual(new[] {
					PageListTool.ToolName,
					PageGetTool.ToolName,
					PageUpdateTool.ToolName,
					PageGetTool.ToolName
				}),
				because: "list-pages should keep the legacy update-page fallback as a single-save sequence after discovery");
			ToolContractDefinition pageGetContract = contracts.Single(contract => contract.Name == PageGetTool.ToolName);
		pageGetContract.PreferredFlow.Tools.Should().Equal(
				new[] {
					PageListTool.ToolName,
					PageGetTool.ToolName,
					PageSyncTool.ToolName,
					PageGetTool.ToolName
				},
				because: "get-page should advertise sync-pages as the canonical save path after inspection");
		ToolContractDefinition pageSyncContract = contracts.Single(contract => contract.Name == PageSyncTool.ToolName);
		pageSyncContract.PreferredFlow.Tools.Should().Equal(
				new[] {
					PageListTool.ToolName,
					PageGetTool.ToolName,
					PageSyncTool.ToolName,
					PageGetTool.ToolName
				},
				because: "sync-pages should advertise itself as the canonical page write path");
		ToolContractDefinition pageUpdateContract = contracts.Single(contract => contract.Name == PageUpdateTool.ToolName);
		pageUpdateContract.PreferredFlow.Tools.Should().Equal(
				new[] {
					PageGetTool.ToolName,
					PageUpdateTool.ToolName,
					PageGetTool.ToolName
				},
				because: "update-page still needs a concrete fallback flow for callers that explicitly require it");
		pageUpdateContract.Deprecations.Should().ContainSingle(deprecation =>
				deprecation.ReplacementTools.SequenceEqual(new[] { PageSyncTool.ToolName }) &&
				deprecation.Message.Contains("fallback"),
			because: "update-page should advertise sync-pages as the canonical replacement");
		pageUpdateContract.FallbackFlow.Should().Contain(flow => flow.Tools.SequenceEqual(new[] {
				PageListTool.ToolName,
				PageGetTool.ToolName,
				PageSyncTool.ToolName,
				PageGetTool.ToolName
			}),
			because: "update-page should point callers back to the canonical sync-pages workflow");
		pageSyncContract.InputSchema.Properties.Should().Contain(field =>
				field.Name == "pages" &&
				field.Description.Contains("get-page.raw.body"),
			because: "sync-pages should advertise raw.body as the source of page write payloads");
		pageUpdateContract.InputSchema.Properties.Should().Contain(field =>
				field.Name == "body" &&
				field.Description.Contains("get-page.raw.body"),
			because: "update-page should advertise raw.body as the source of fallback single-page saves");
		pageUpdateContract.InputSchema.Properties.Should().Contain(field =>
				field.Name == "resources" &&
				field.Description.Contains("JSON object string"),
			because: "update-page should clarify the concrete resources payload shape");
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
			because: "modify-entity-schema-column should still advertise sync-schemas when the work expands into a multi-step ordered schema plan");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the canonical existing-app section-create contract with selector rules, scalar validation, defaults, and preferred flow guidance.")]
	public void ToolContractGet_Should_Return_ApplicationSectionCreate_Contract() {
		// Arrange
		ToolContractGetTool tool = new();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs([
			ApplicationSectionCreateTool.ApplicationSectionCreateToolName
		]));

		// Assert
		result.Success.Should().BeTrue(
			because: "get-tool-contract should expose the create-app-section contract");
		ToolContractDefinition contract = result.Tools!.Single();
		contract.Name.Should().Be(ApplicationSectionCreateTool.ApplicationSectionCreateToolName,
			because: "the requested tool contract should be returned verbatim");
		contract.InputSchema.Required.Should().Contain(["environment-name", "application-code", "caption"],
			because: "section-create requires environment-name, application-code, and caption as the minimal payload");
		contract.InputSchema.AnyOf.Should().BeNullOrEmpty(
			because: "section-create now uses a single required application-code selector");
		contract.InputSchema.Validators.Should().Contain(validator =>
				validator.Name == "forbid-fields" &&
				validator.Fields!.Contains("title-localizations"),
			because: "the contract should reject localization maps on the scalar section-create tool");
		contract.Defaults.Should().Contain(defaultValue =>
				defaultValue.Name == "with-mobile-pages" &&
				defaultValue.Value == "true",
			because: "section-create should document its mobile-enabled default explicitly");
		contract.Aliases.Should().Contain(alias =>
				alias.CanonicalName == "application-code" &&
				alias.Alias == "app-code" &&
				alias.Status == "rejected",
			because: "the contract should reject legacy app-code naming for the section-create tool");
		contract.Aliases.Should().Contain(alias =>
				alias.CanonicalName == "application-code" &&
				alias.Alias == "application-id" &&
				alias.Status == "rejected",
			because: "the contract should reject the removed application-id selector explicitly");
		contract.PreferredFlow.Tools.Should().Equal(
				new[] {
					ApplicationGetListTool.ApplicationGetListToolName,
					ApplicationGetInfoTool.ApplicationGetInfoToolName,
					ApplicationSectionCreateTool.ApplicationSectionCreateToolName,
					ApplicationGetInfoTool.ApplicationGetInfoToolName
				},
				because: "section-create should advertise the canonical discover-inspect-mutate-verify flow for existing apps");
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "section",
			because: "the output contract should advertise the created section payload explicitly");
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "entity",
			because: "the output contract should advertise the created or targeted entity payload explicitly");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the canonical existing-app section-update contract with selector rules, scalar partial-update validation, and preferred flow guidance.")]
	public void ToolContractGet_Should_Return_ApplicationSectionUpdate_Contract() {
		// Arrange
		ToolContractGetTool tool = new();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs([
			ApplicationSectionUpdateTool.ApplicationSectionUpdateToolName
		]));

		// Assert
		result.Success.Should().BeTrue(
			because: "get-tool-contract should expose the update-app-section contract");
		ToolContractDefinition contract = result.Tools!.Single();
		contract.Name.Should().Be(ApplicationSectionUpdateTool.ApplicationSectionUpdateToolName,
			because: "the requested tool contract should be returned verbatim");
		contract.InputSchema.Required.Should().Contain(["environment-name", "application-code", "section-code"],
			because: "section-update requires environment-name, application-code, and section-code as the selector payload");
		contract.InputSchema.Properties.Should().Contain(field => field.Name == "caption",
			because: "section-update should advertise caption as an optional mutable field");
		contract.InputSchema.Properties.Should().Contain(field => field.Name == "description",
			because: "section-update should advertise description as an optional mutable field");
		contract.InputSchema.Properties.Should().Contain(field => field.Name == "icon-id",
			because: "section-update should advertise icon-id as an optional mutable field");
		contract.InputSchema.Properties.Should().Contain(field => field.Name == "icon-background",
			because: "section-update should advertise icon-background as an optional mutable field");
		contract.InputSchema.Validators.Should().Contain(validator =>
				validator.Name == "forbid-fields" &&
				validator.Fields!.Contains("title-localizations"),
			because: "the contract should reject localization maps on the scalar section-update tool");
		contract.Aliases.Should().Contain(alias =>
				alias.CanonicalName == "section-code" &&
				alias.Alias == "sectionCode" &&
				alias.Status == "rejected",
			because: "the contract should reject camelCase section selectors in favor of kebab-case");
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "previous-section",
			because: "section-update should return the section metadata before the update for auditability");
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "section",
			because: "section-update should return the section metadata after the update");
		contract.PreferredFlow.Tools.Should().Equal(
				new[] {
					ApplicationGetListTool.ApplicationGetListToolName,
					ApplicationGetInfoTool.ApplicationGetInfoToolName,
					ApplicationSectionUpdateTool.ApplicationSectionUpdateToolName
				},
				because: "section-update should advertise the canonical discover-inspect-mutate flow for existing section metadata edits");
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
			because: "get-tool-contract should expose the full canonical entity/schema MCP surface from clio");
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
			because: "sync-schemas should advertise the canonical batched entity workflow");
		result.Tools.Should().Contain(contract =>
				contract.Name == CreateLookupTool.CreateLookupToolName
				&& contract.PreferredFlow.Tools.SequenceEqual(new[] { SchemaSyncTool.ToolName }),
			because: "create-lookup should advertise sync-schemas as the preferred canonical path");
		result.Tools.Should().Contain(contract =>
				contract.Name == CreateEntitySchemaTool.CreateEntitySchemaToolName
				&& contract.PreferredFlow.Tools.SequenceEqual(new[] { SchemaSyncTool.ToolName }),
			because: "create-entity-schema should advertise sync-schemas as the preferred canonical path");
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
				because: "create-data-binding-db should advertise sync-schemas as the canonical batched path");
		createContract.Deprecations.Should().ContainSingle(
			because: "create-data-binding-db should advertise that it is a fallback or standalone path");
		createContract.Deprecations[0].Message.Should().Contain("fallback",
			because: "the deprecation guidance should explicitly frame create-data-binding-db as a fallback");
		createContract.Deprecations[0].Message.Should().Contain("seed-rows",
			because: "the deprecation guidance should point callers at inline seed-rows inside sync-schemas");
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
	[Description("Advertises enriched get-app-info output fields for installed application identity.")]
	public void ToolContractGet_Should_Advertise_Application_Info_Identity_Fields() {
		// Arrange
		ToolContractGetTool tool = new();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs([
			ApplicationGetInfoTool.ApplicationGetInfoToolName
		]));

		// Assert
		result.Success.Should().BeTrue(
			because: "the get-app-info contract should be available through get-tool-contract");
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
	[Description("Advertises the canonical create-app validators aliases and preferred flow through get-tool-contract.")]
	public void ToolContractGet_Should_Advertise_Application_Create_Canonical_Rules() {
		// Arrange
		ToolContractGetTool tool = new();

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs([
			ApplicationCreateTool.ApplicationCreateToolName
		]));

		// Assert
		result.Success.Should().BeTrue(
			because: "the create-app contract should be available through get-tool-contract");
		ToolContractDefinition contract = result.Tools!.Single();
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "canonical-main-entity-name",
			because: "create-app should advertise the canonical main entity field in its response shape");
		contract.OutputContract.Fields.Should().Contain(field =>
				field.Name == "dataforge" &&
				field.Description.Contains("context-summary", StringComparison.Ordinal),
			because: "create-app should advertise the built-in Data Forge diagnostics block in its response contract");
		contract.InputSchema.Validators.Should().ContainSingle(validator =>
				validator.Name == "forbid-fields"
				&& validator.Fields!.Contains("title-localizations")
				&& validator.Fields.Contains("descriptionLocalizations"),
			because: "create-app should advertise forbidden localization maps through the canonical contract");
		contract.Aliases.Should().Contain(alias =>
				alias.CanonicalName == "code"
				&& alias.Alias == "app-code"
				&& alias.Status == "rejected",
			because: "create-app should reject legacy alias parameters through the canonical contract");
		contract.Aliases.Should().Contain(alias =>
				alias.CanonicalName == "name"
				&& alias.Alias == "app-name"
				&& alias.Status == "rejected",
			because: "create-app should reject legacy alias parameters through the canonical contract");
		contract.PreferredFlow.Tools.Should().Equal(
			new[] {
				ApplicationCreateTool.ApplicationCreateToolName,
				SchemaSyncTool.ToolName,
				ApplicationGetInfoTool.ApplicationGetInfoToolName
			},
			because: "create-app should advertise the canonical create -> sync-schemas -> refresh flow");
		contract.FallbackFlow.Should().Contain(flow => flow.Tools.SequenceEqual(new[] {
				ApplicationGetListTool.ApplicationGetListToolName,
				ApplicationGetInfoTool.ApplicationGetInfoToolName
			}),
			because: "create-app should advertise the canonical existing-app fallback flow");
		contract.Examples.Should().ContainSingle(example =>
				example.Summary.Contains("top-level payload", StringComparison.Ordinal),
			because: "create-app should advertise the minimal top-level request shape explicitly");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the full Data Forge contract surface through explicit lookup while keeping maintenance tools out of the default bootstrap set.")]
	public void ToolContractGet_Should_Return_Full_DataForge_Surface_On_Explicit_Lookup() {
		// Arrange
		ToolContractGetTool tool = new();
		string[] requestedTools = [
			DataForgeTool.DataForgeHealthToolName,
			DataForgeTool.DataForgeStatusToolName,
			DataForgeTool.DataForgeFindTablesToolName,
			DataForgeTool.DataForgeFindLookupsToolName,
			DataForgeTool.DataForgeGetRelationsToolName,
			DataForgeTool.DataForgeGetTableColumnsToolName,
			DataForgeTool.DataForgeContextToolName,
			DataForgeTool.DataForgeInitializeToolName,
			DataForgeTool.DataForgeUpdateToolName
		];

		// Act
		ToolContractGetResponse result = tool.GetToolContracts(new ToolContractGetArgs(requestedTools));

		// Assert
		result.Success.Should().BeTrue(
			because: "the full Data Forge surface should remain available through explicit get-tool-contract lookup");
		result.Tools.Should().NotBeNull(
			because: "explicit Data Forge lookup should return the requested contracts");
		result.Tools!.Select(contract => contract.Name).Should().Equal(requestedTools,
			because: "the response should preserve the requested Data Forge tool order");
		result.Tools.Should().Contain(contract =>
				contract.Name == DataForgeTool.DataForgeInitializeToolName &&
				contract.OutputContract.Fields.Any(field => field.Name == "status"),
			because: "the maintenance initialize contract should remain available through explicit lookup");
		result.Tools.Should().Contain(contract =>
				contract.Name == DataForgeTool.DataForgeUpdateToolName &&
				contract.OutputContract.Fields.Any(field => field.Name == "status"),
			because: "the maintenance update contract should remain available through explicit lookup");
		result.Tools.Should().Contain(contract =>
				contract.Name == DataForgeTool.DataForgeHealthToolName &&
				contract.Defaults.Any(definition => definition.Name == "scope" && definition.Value == "use_enrichment"),
			because: "Data Forge contracts should advertise the default OAuth scope through the canonical contract catalog");
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
