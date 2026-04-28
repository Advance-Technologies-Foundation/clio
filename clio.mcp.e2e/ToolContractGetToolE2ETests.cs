using System;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

[TestFixture]
[AllureNUnit]
[AllureFeature(ToolContractGetTool.ToolName)]
[NonParallelizable]
public sealed class ToolContractGetToolE2ETests {
	[Test]
	[AllureTag(ToolContractGetTool.ToolName)]
	[AllureName("get-tool-contract tool is advertised by the MCP server")]
	public async Task ToolContractGet_Should_Be_Listed_By_Mcp_Server() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		IList<McpClientTool> tools = await context.Session.ListToolsAsync(context.CancellationTokenSource.Token);

		// Assert
		tools.Select(tool => tool.Name).Should().Contain(ToolContractGetTool.ToolName,
			because: "the MCP server should advertise get-tool-contract as the bootstrap contract entry point");
	}

	[Test]
	[AllureTag(ToolContractGetTool.ToolName)]
	[AllureName("get-tool-contract returns maintenance-oriented canonical flows")]
	public async Task ToolContractGet_Should_Return_Maintenance_Oriented_Canonical_Contracts() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		ToolContractGetResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["tool-names"] = new[] {
					ApplicationGetListTool.ApplicationGetListToolName,
					PageListTool.ToolName,
					PageGetTool.ToolName,
					PageSyncTool.ToolName,
					PageUpdateTool.ToolName,
					ModifyEntitySchemaColumnTool.ModifyEntitySchemaColumnToolName
				}
			});

		// Assert
		response.Success.Should().BeTrue(
			because: "the requested maintenance-oriented tools are all registered by the MCP server");
		response.Tools.Should().NotBeNull(
			because: "a successful lookup should return the requested contract payload");
		response.Tools!.Select(tool => tool.Name).Should().Equal(
			new[] {
				ApplicationGetListTool.ApplicationGetListToolName,
				PageListTool.ToolName,
				PageGetTool.ToolName,
				PageSyncTool.ToolName,
				PageUpdateTool.ToolName,
				ModifyEntitySchemaColumnTool.ModifyEntitySchemaColumnToolName
			},
			because: "the response should preserve the requested tool order");
		response.Tools.Single(tool => tool.Name == ApplicationGetListTool.ApplicationGetListToolName)
			.PreferredFlow.Tools.Should().Equal(
				new[] {
					ApplicationGetListTool.ApplicationGetListToolName,
					ApplicationGetInfoTool.ApplicationGetInfoToolName
				},
				because: "application discovery should flow into application inspection for existing-app edits");
		response.Tools.Single(tool => tool.Name == PageListTool.ToolName)
			.PreferredFlow.Tools.Should().Equal(
				new[] {
					PageListTool.ToolName,
					PageGetTool.ToolName,
					PageSyncTool.ToolName,
					PageGetTool.ToolName
				},
				because: "page discovery should lead into the canonical clio page workflow");
		response.Tools.Single(tool => tool.Name == PageGetTool.ToolName)
			.PreferredFlow.Tools.Should().Equal(
				new[] {
					PageListTool.ToolName,
					PageGetTool.ToolName,
					PageSyncTool.ToolName,
					PageGetTool.ToolName
				},
				because: "page inspection should advertise sync-pages as the canonical save path");
		response.Tools.Single(tool => tool.Name == PageSyncTool.ToolName)
			.PreferredFlow.Tools.Should().Equal(
				new[] {
					PageListTool.ToolName,
					PageGetTool.ToolName,
					PageSyncTool.ToolName,
					PageGetTool.ToolName
				},
				because: "sync-pages should advertise itself as the canonical page write path");
		response.Tools.Single(tool => tool.Name == PageUpdateTool.ToolName)
			.PreferredFlow.Tools.Should().Equal(
				new[] {
					PageGetTool.ToolName,
					PageUpdateTool.ToolName,
					PageGetTool.ToolName
				},
				because: "update-page should still expose a concrete fallback flow for callers that explicitly require it");
		response.Tools.Single(tool => tool.Name == PageUpdateTool.ToolName)
			.Deprecations.Should().ContainSingle(deprecation =>
				deprecation.ReplacementTools.SequenceEqual(new[] { PageSyncTool.ToolName }) &&
				deprecation.Message.Contains("fallback"),
				because: "update-page should advertise sync-pages as the canonical replacement");
		response.Tools.Single(tool => tool.Name == PageSyncTool.ToolName)
			.InputSchema.Properties.Should().Contain(field =>
				field.Name == "pages" &&
				field.Description.Contains("get-page.raw.body", StringComparison.Ordinal),
				because: "sync-pages should advertise raw.body as the source of page write payloads");
		response.Tools.Single(tool => tool.Name == PageSyncTool.ToolName)
			.OutputContract.Fields.Should().Contain(field =>
				field.Name == "pages" &&
				field.Description.Contains("verified-body-file", StringComparison.Ordinal) &&
				field.Description.Contains("page", StringComparison.Ordinal),
				because: "sync-pages should advertise the richer per-page verify response through get-tool-contract");
		response.Tools.Single(tool => tool.Name == PageUpdateTool.ToolName)
			.InputSchema.Properties.Should().Contain(field =>
				field.Name == "resources" &&
				field.Description.Contains("JSON object string", StringComparison.Ordinal),
				because: "update-page should clarify the concrete resources payload shape through the MCP server");
		response.Tools.Single(tool => tool.Name == ModifyEntitySchemaColumnTool.ModifyEntitySchemaColumnToolName)
			.PreferredFlow.Tools.Should().Equal(
				new[] {
					GetEntitySchemaColumnPropertiesTool.GetEntitySchemaColumnPropertiesToolName,
					ModifyEntitySchemaColumnTool.ModifyEntitySchemaColumnToolName,
					GetEntitySchemaColumnPropertiesTool.GetEntitySchemaColumnPropertiesToolName
				},
				because: "single-column schema edits should inspect current metadata first and verify it again after saving");
	}

	[Test]
	[AllureTag(ToolContractGetTool.ToolName)]
	[AllureName("get-tool-contract returns explicit Data Forge contracts and keeps maintenance tools out of the default bootstrap set")]
	public async Task ToolContractGet_Should_Handle_DataForge_Contract_Policy() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		ToolContractGetResponse defaultResponse = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?>());
		ToolContractGetResponse explicitResponse = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["tool-names"] = new[] {
					DataForgeTool.DataForgeHealthToolName,
					DataForgeTool.DataForgeStatusToolName,
					DataForgeTool.DataForgeFindTablesToolName,
					DataForgeTool.DataForgeFindLookupsToolName,
					DataForgeTool.DataForgeGetRelationsToolName,
					DataForgeTool.DataForgeGetTableColumnsToolName,
					DataForgeTool.DataForgeContextToolName,
					DataForgeTool.DataForgeInitializeToolName,
					DataForgeTool.DataForgeUpdateToolName
				}
			});

		// Assert
		defaultResponse.Success.Should().BeTrue(
			because: "the default bootstrap lookup should succeed before the caller decides whether it needs explicit Data Forge maintenance contracts");
		defaultResponse.Tools!.Select(tool => tool.Name).Should().Contain(DataForgeTool.DataForgeContextToolName,
			because: "the default bootstrap set should include read-only Data Forge discovery/context contracts");
		defaultResponse.Tools!.Select(tool => tool.Name).Should().NotContain(DataForgeTool.DataForgeInitializeToolName,
			because: "destructive Data Forge initialize should stay out of the default bootstrap set");
		defaultResponse.Tools!.Select(tool => tool.Name).Should().NotContain(DataForgeTool.DataForgeUpdateToolName,
			because: "destructive Data Forge update should stay out of the default bootstrap set");
		explicitResponse.Success.Should().BeTrue(
			because: "explicit get-tool-contract lookup should expose the full Data Forge contract surface");
		explicitResponse.Tools!.Select(tool => tool.Name).Should().Contain(DataForgeTool.DataForgeInitializeToolName,
			because: "explicit lookup should still return Data Forge initialize for remediation workflows");
		explicitResponse.Tools!.Select(tool => tool.Name).Should().Contain(DataForgeTool.DataForgeUpdateToolName,
			because: "explicit lookup should still return Data Forge update for remediation workflows");
		explicitResponse.Tools!.Single(tool => tool.Name == DataForgeTool.DataForgeHealthToolName)
			.Defaults.Should().Contain(definition =>
				definition.Name == "scope" && definition.Value == "use_enrichment",
				because: "the explicit Data Forge contract should advertise the default OAuth scope");
	}

	[Test]
	[AllureTag(ToolContractGetTool.ToolName)]
	[AllureName("get-tool-contract advertises check-settings-health bootstrap diagnostics contract")]
	public async Task ToolContractGet_Should_Advertise_Settings_Health_Contract() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		ToolContractGetResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["tool-names"] = new[] {
					SettingsHealthTool.ToolName
				}
			});

		// Assert
		response.Success.Should().BeTrue(
			because: "check-settings-health should remain discoverable through the executable clio MCP contract catalog");
		ToolContractDefinition contract = response.Tools!.Single();
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "status",
			because: "bootstrap diagnostics should expose the health status");
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "settings-file-path",
			because: "bootstrap diagnostics should expose the physical settings file path");
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "repairs-applied",
			because: "bootstrap diagnostics should expose automatic repairs");
	}

	[Test]
	[AllureTag(ToolContractGetTool.ToolName)]
	[AllureName("get-tool-contract returns canonical entity-schema contracts from clio")]
	public async Task ToolContractGet_Should_Return_Canonical_Entity_Schema_Surface() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		ToolContractGetResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["tool-names"] = new[] {
					SchemaSyncTool.ToolName,
					CreateLookupTool.CreateLookupToolName,
					CreateEntitySchemaTool.CreateEntitySchemaToolName,
					UpdateEntitySchemaTool.UpdateEntitySchemaToolName,
					GetEntitySchemaPropertiesTool.GetEntitySchemaPropertiesToolName,
					GetEntitySchemaColumnPropertiesTool.GetEntitySchemaColumnPropertiesToolName,
					ModifyEntitySchemaColumnTool.ModifyEntitySchemaColumnToolName
				}
			});

		// Assert
		response.Success.Should().BeTrue(
			because: "the authoritative entity/schema contract surface should be served by clio MCP");
		response.Tools.Should().NotBeNull(
			because: "a successful lookup should return the canonical entity/schema contracts");
		response.Tools!.Select(tool => tool.Name).Should().Equal(
			new[] {
				SchemaSyncTool.ToolName,
				CreateLookupTool.CreateLookupToolName,
				CreateEntitySchemaTool.CreateEntitySchemaToolName,
				UpdateEntitySchemaTool.UpdateEntitySchemaToolName,
				GetEntitySchemaPropertiesTool.GetEntitySchemaPropertiesToolName,
				GetEntitySchemaColumnPropertiesTool.GetEntitySchemaColumnPropertiesToolName,
				ModifyEntitySchemaColumnTool.ModifyEntitySchemaColumnToolName
			},
			because: "the response should preserve the requested canonical schema tool order");
		response.Tools.Single(tool => tool.Name == SchemaSyncTool.ToolName)
			.PreferredFlow.Tools.Should().Equal(
				new[] {
					ApplicationCreateTool.ApplicationCreateToolName,
					SchemaSyncTool.ToolName,
					ApplicationGetInfoTool.ApplicationGetInfoToolName
				},
				because: "sync-schemas should advertise the canonical batched schema workflow");
		response.Tools.Single(tool => tool.Name == CreateLookupTool.CreateLookupToolName)
			.PreferredFlow.Tools.Should().Equal(
				new[] {
					SchemaSyncTool.ToolName
				},
				because: "create-lookup should advertise sync-schemas as the preferred canonical path");
		response.Tools.Single(tool => tool.Name == CreateEntitySchemaTool.CreateEntitySchemaToolName)
			.PreferredFlow.Tools.Should().Equal(
				new[] {
					SchemaSyncTool.ToolName
				},
				because: "create-entity-schema should advertise sync-schemas as the preferred canonical path");
	}

	[Test]
	[AllureTag(ToolContractGetTool.ToolName)]
	[AllureName("get-tool-contract returns canonical DB-first binding contracts from clio")]
	public async Task ToolContractGet_Should_Return_Canonical_DbFirst_Binding_Surface() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		ToolContractGetResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["tool-names"] = new[] {
					CreateDataBindingDbTool.CreateDataBindingDbToolName,
					UpsertDataBindingRowDbTool.UpsertDataBindingRowDbToolName,
					RemoveDataBindingRowDbTool.RemoveDataBindingRowDbToolName
				}
			});

		// Assert
		response.Success.Should().BeTrue(
			because: "the DB-first binding contract surface should be discoverable through the MCP server");
		response.Tools.Should().NotBeNull(
			because: "a successful lookup should return the requested DB-first binding contracts");
		response.Tools!.Select(tool => tool.Name).Should().Equal(
			new[] {
				CreateDataBindingDbTool.CreateDataBindingDbToolName,
				UpsertDataBindingRowDbTool.UpsertDataBindingRowDbToolName,
				RemoveDataBindingRowDbTool.RemoveDataBindingRowDbToolName
			},
			because: "the response should preserve the requested binding tool order");

		ToolContractDefinition createContract = response.Tools.Single(tool => tool.Name == CreateDataBindingDbTool.CreateDataBindingDbToolName);
		createContract.PreferredFlow.Tools.Should().Equal(
				new[] {
					SchemaSyncTool.ToolName
				},
				because: "create-data-binding-db should advertise sync-schemas as the canonical batched path");
		createContract.Deprecations.Should().ContainSingle(
			because: "create-data-binding-db should advertise its explicit fallback positioning");
		createContract.Deprecations[0].Message.Should().Contain("seed-rows",
			because: "the fallback guidance should direct callers to inline seed-rows inside sync-schemas");
		createContract.Deprecations[0].Message.Should().Contain("direct SQL",
			because: "the fallback guidance should keep standalone lookup seeding on the MCP surface");
		createContract.InputSchema.Properties.Should().Contain(field =>
				field.Name == "rows" &&
				field.Description.Contains("values object"),
			because: "the canonical contract should describe the required rows[].values wrapper shape");
		createContract.Examples.Should().Contain(example =>
				example.Arguments != null &&
				example.Arguments.ContainsKey("rows") &&
				example.Arguments["rows"] != null &&
				example.Arguments["rows"]!.ToString()!.Contains("In Progress", StringComparison.Ordinal),
			because: "the canonical contract should expose a realistic multi-row lookup seeding example");

		ToolContractDefinition upsertContract = response.Tools.Single(tool => tool.Name == UpsertDataBindingRowDbTool.UpsertDataBindingRowDbToolName);
		upsertContract.PreferredFlow.Tools.Should().Equal(
				new[] {
					CreateDataBindingDbTool.CreateDataBindingDbToolName,
					UpsertDataBindingRowDbTool.UpsertDataBindingRowDbToolName
				},
				because: "upsert-data-binding-row-db should advertise the create-then-upsert sequence");
		upsertContract.ErrorContract.Codes.Should().Contain(code => code.Code == "binding-not-found",
			because: "the DB-first upsert contract should advertise the missing-binding failure mode");

		ToolContractDefinition removeContract = response.Tools.Single(tool => tool.Name == RemoveDataBindingRowDbTool.RemoveDataBindingRowDbToolName);
		removeContract.Description.Should().Contain("package schema data record",
			because: "the DB-first remove contract should document the final-row lifecycle cleanup");
	}

	[Test]
	[AllureTag(ToolContractGetTool.ToolName)]
	[AllureName("get-tool-contract returns canonical required field name for modify-entity-schema-column")]
	public async Task ToolContractGet_Should_Return_Canonical_Required_Field_Name() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		ToolContractGetResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["tool-names"] = new[] {
					ModifyEntitySchemaColumnTool.ModifyEntitySchemaColumnToolName
				}
			});

		// Assert
		response.Success.Should().BeTrue(
			because: "the modify-entity-schema-column contract should be readable through the MCP server");
		ToolContractDefinition contract = response.Tools!.Single();
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
	[AllureTag(ToolContractGetTool.ToolName)]
	[AllureName("get-tool-contract advertises installed application identity fields for get-app-info")]
	public async Task ToolContractGet_Should_Advertise_Application_Info_Identity_Fields() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		ToolContractGetResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["tool-names"] = new[] {
					ApplicationGetInfoTool.ApplicationGetInfoToolName
				}
			});

		// Assert
		response.Success.Should().BeTrue(
			because: "the get-app-info contract should be readable through the MCP server");
		ToolContractDefinition contract = response.Tools!.Single();
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "application-id",
			because: "the contract should advertise the installed application identifier");
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "application-name",
			because: "the contract should advertise the installed application display name");
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "application-code",
			because: "the contract should advertise the installed application code");
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "application-version",
			because: "the contract should advertise the installed application version");
		contract.OutputContract.Fields.Should().Contain(field => field.Name == "pages",
			because: "the contract should advertise the shared primary-package page summaries");
	}

	[Test]
	[AllureTag(ToolContractGetTool.ToolName)]
	[AllureName("tool-contract-get advertises create-entity-business-rule validation, actual response fields, and workflow guidance")]
	public async Task ToolContractGet_Should_Advertise_Entity_Business_Rule_Create_Contract() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		ToolContractGetResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["tool-names"] = new[] {
					CreateEntityBusinessRuleTool.BusinessRuleCreateToolName
				}
			});

		// Assert
		response.Success.Should().BeTrue(
			because: "the new business-rule mutation tool should be discoverable through tool-contract-get");
		ToolContractDefinition contract = response.Tools!.Single();
		contract.InputSchema.Required.Should().Contain(["environmentName", "packageName", "entitySchemaName", "rule"],
			because: "entity-business-rule creation requires environment package entity and rule payload");
		contract.InputSchema.Validators.Should().Contain(validator =>

				validator.Name == "enum" &&
				validator.Field == "rule.condition.logicalOperation",
			because: "the contract should advertise the target architecture logicalOperation field");
		contract.InputSchema.Validators.Should().Contain(validator =>
				validator.Name == "enum" &&
				validator.Field == "rule.condition.conditions[*].comparisonType",
			because: "the contract should advertise the target architecture comparisonType field");
		contract.InputSchema.Validators.Should().Contain(validator =>
				validator.Name == "conditional-field" &&
				validator.Field == "rule.condition.conditions[*].rightExpression" &&
				validator.Context!.Contains("Omit or null for is-filled-in and is-not-filled-in", StringComparison.Ordinal),
			because: "the contract should advertise the unary-versus-binary rightExpression rule");
		contract.InputSchema.Validators.Should().Contain(validator =>
				validator.Name == "comparison-family" &&
				validator.Field == "rule.condition.conditions[*]" &&
				validator.Context!.Contains("date/time left attributes", StringComparison.Ordinal),
			because: "the contract should advertise the numeric and date/time scope of relational comparisons");
		contract.InputSchema.Validators.Should().Contain(validator =>
				validator.Name == "date-time-constant" &&
				validator.Field == "rule.condition.conditions[*].rightExpression.value" &&
				validator.Context!.Contains("timezone suffix", StringComparison.Ordinal),
			because: "the contract should require timezone-aware DateTime and Time constants");
		contract.InputSchema.Validators.Should().Contain(validator =>
				validator.Name == "enum" &&
				validator.Field == "rule.actions[*].type",
			because: "the contract should advertise the target architecture action field");
		contract.InputSchema.Validators.Should().Contain(validator =>
				validator.Name == "enum" &&
				validator.Field == "rule.condition.conditions[*].comparisonType" &&
				validator.Context!.Contains("greater-than-or-equal", StringComparison.Ordinal),
			because: "the contract should advertise the full supported comparison set");
		contract.Defaults.Should().BeEmpty(
			because: "the contract should not have defaults after enabled was removed");
		contract.OutputContract.Fields.Should().NotContain(field => field.Name == "rule-name",
			because: "the generated internal rule name is surfaced through execution logs rather than a dedicated top-level field");
		contract.OutputContract.Fields.Should().NotContain(field => field.Name == "rule",
			because: "the contract should not advertise a structured rule object that the tool does not return today");
		contract.OutputContract.Fields.Should().NotContain(field => field.Name == "package-u-id",
			because: "the contract should not advertise package identifiers that are absent from the real tool response");
		contract.OutputContract.Fields.Should().NotContain(field => field.Name == "entity-schema-u-id",
			because: "the contract should not advertise entity identifiers that are absent from the real tool response");
		contract.OutputContract.Kind.Should().Be("command-execution-result",
			because: "create-entity-business-rule returns the standard command execution result payload");
		contract.OutputContract.SuccessField.Should().BeNull(
			because: "command execution result payloads do not include a success field");
		contract.OutputContract.FailureSignals.Should().Contain("exit-code != 0",
			because: "contract-driven clients should detect command failures from the exit code");
		contract.OutputContract.FailureSignals.Should().NotContain("success == false",
			because: "create-entity-business-rule does not emit a success field");
		bool hasUnaryExample = contract.Examples.Any(example =>
			string.Equals(example.Summary, "Create a readonly rule when a text field is filled in", StringComparison.Ordinal));
		hasUnaryExample.Should().BeTrue(
			because: "the contract should include a unary example without rightExpression");
		bool hasRelationalExample = contract.Examples.Any(example =>
			string.Equals(example.Summary, "Create a required-field rule when created date is before a cutoff", StringComparison.Ordinal));
		hasRelationalExample.Should().BeTrue(
			because: "the contract should include a relational example for numeric or date/time comparisons");
		bool hasTimezoneAwareTimeExample = contract.Examples.Any(example =>
			string.Equals(example.Summary, "Create a readonly rule when reminder time is after a timezone-aware cutoff", StringComparison.Ordinal));
		hasTimezoneAwareTimeExample.Should().BeTrue(
			because: "the contract should include a timezone-aware Time example for agent callers");
	}

	[Test]
	[AllureTag(ToolContractGetTool.ToolName)]
	[AllureName("get-tool-contract advertises page discovery selectors and raw body semantics")]
	public async Task ToolContractGet_Should_Advertise_Page_List_And_Page_Get_Metadata() {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		ToolContractGetResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["tool-names"] = new[] {
					PageListTool.ToolName,
					PageGetTool.ToolName
				}
			});

		response.Success.Should().BeTrue(
			because: "page discovery and inspection contracts should be readable through the MCP server");
		ToolContractDefinition pageListContract = response.Tools!.Single(tool => tool.Name == PageListTool.ToolName);
		pageListContract.InputSchema.Properties.Should().Contain(field => field.Name == "code",
			because: "list-pages should advertise code as a first-class selector");
		pageListContract.Aliases.Should().Contain(alias =>
				alias.CanonicalName == "code"
				&& alias.Alias == "app-code"
				&& alias.Status == "rejected",
			because: "list-pages should advertise the legacy app-code selector as rejected");
		pageListContract.OutputContract.Fields.Should().Contain(field =>
				field.Name == "pages" &&
				field.Description.Contains("schema-name", StringComparison.Ordinal),
			because: "list-pages should describe page discovery items through schema-name");
		pageListContract.FallbackFlow.Should().Contain(flow => flow.Tools.SequenceEqual(new[] {
				PageListTool.ToolName,
				PageGetTool.ToolName,
				PageUpdateTool.ToolName,
				PageGetTool.ToolName
			}),
			because: "list-pages should advertise a single update-page fallback sequence after discovery");
		ToolContractDefinition pageGetContract = response.Tools!.Single(tool => tool.Name == PageGetTool.ToolName);
		pageGetContract.OutputContract.Fields.Should().Contain(field =>
				field.Name == "raw" &&
				field.Description.Contains("raw.body", StringComparison.Ordinal),
			because: "get-page should explicitly advertise raw.body as the editable JavaScript source");
	}

	[Test]
	[AllureTag(ToolContractGetTool.ToolName)]
	[AllureName("get-tool-contract returns structured unknown tool suggestions")]
	public async Task ToolContractGet_Should_Return_Structured_Unknown_Tool_Suggestions() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		ToolContractGetResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["tool-names"] = new[] { "page-updte" }
			});

		// Assert
		response.Success.Should().BeFalse(
			because: "a misspelled tool name should fail contract lookup");
		response.Error.Should().NotBeNull(
			because: "the MCP tool should return a structured error envelope for unknown names");
		response.Error!.Code.Should().Be("tool-not-found",
			because: "unknown tool names should map to the tool-not-found error code");
		response.Error.Suggestions.Should().Contain(PageUpdateTool.ToolName,
			because: "the error should suggest the closest matching registered tool name");
	}

	[Test]
	[AllureTag(ToolContractGetTool.ToolName)]
	[AllureName("get-tool-contract returns field-level validation errors for blank tool names")]
	public async Task ToolContractGet_Should_Return_Field_Level_Validation_Error() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		ToolContractGetResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["tool-names"] = new[] { " " }
			});

		// Assert
		response.Success.Should().BeFalse(
			because: "blank tool names are invalid input");
		response.Error.Should().NotBeNull(
			because: "the MCP tool should return a structured validation error");
		response.Error!.Code.Should().Be("missing-required-parameter",
			because: "blank tool names should be treated as missing required values");
		response.Error.FieldErrors.Should().ContainSingle(
			because: "the validation error should identify the exact offending entry");
		response.Error.FieldErrors![0].Field.Should().Be("tool-names[0]",
			because: "the field path should point to the blank element inside tool-names");
	}

	[Test]
	[Description("Returns a top-level MCP invocation error when get-tool-contract is called without the required args wrapper.")]
	[AllureTag(ToolContractGetTool.ToolName)]
	[AllureName("get-tool-contract rejects calls without the args wrapper")]
	public async Task ToolContractGet_Should_Return_Invocation_Error_When_Args_Wrapper_Is_Missing() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolContractGetTool.ToolName,
			new Dictionary<string, object?>(),
			context.CancellationTokenSource.Token);

		// Assert
		AssertInvocationFailure(callResult,
			because: "MCP argument binding should reject transport envelopes that omit the required args wrapper");
	}

	[Test]
	[Description("Returns a top-level MCP invocation error when get-tool-contract receives args in an invalid transport shape.")]
	[AllureTag(ToolContractGetTool.ToolName)]
	[AllureName("get-tool-contract rejects calls with args of the wrong type")]
	public async Task ToolContractGet_Should_Return_Invocation_Error_When_Args_Has_Invalid_Type() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolContractGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = "invalid"
			},
			context.CancellationTokenSource.Token);

		// Assert
		AssertInvocationFailure(callResult,
			because: "MCP argument binding should reject transport envelopes whose args payload cannot bind to ToolContractGetArgs");
	}

	private static async Task<ArrangeContext> ArrangeAsync(McpE2ESettings settings, TimeSpan timeout) {
		CancellationTokenSource cancellationTokenSource = new(timeout);
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new ArrangeContext(session, cancellationTokenSource);
	}

	private static async Task<ToolContractGetResponse> CallAsync(
		McpServerSession session,
		CancellationToken cancellationToken,
		IReadOnlyDictionary<string, object?> arguments) {
		CallToolResult callResult = await session.CallToolAsync(
			ToolContractGetTool.ToolName,
			new Dictionary<string, object?> { ["args"] = arguments },
			cancellationToken);
		callResult.IsError.Should().NotBeTrue(
			because: "get-tool-contract should return a normal MCP tool result envelope for valid request shapes");
		return EntitySchemaStructuredResultParser.Extract<ToolContractGetResponse>(callResult);
	}

	private static void AssertInvocationFailure(CallToolResult callResult, string because) {
		callResult.IsError.Should().BeTrue(
			because: because);
		callResult.StructuredContent.Should().BeNull(
			because: "binding-layer failures should not return a structured get-tool-contract payload");
		callResult.Content.Should().NotBeNullOrEmpty(
			because: "invocation failures should still expose human-readable diagnostics");
		callResult.Content!.Select(content => content.ToString()).Should().Contain(message =>
				message.Contains("An error occurred invoking 'get-tool-contract'.", StringComparison.Ordinal),
			because: "the transport-level failure should surface as the generic invocation error for the tool");
	}

	private sealed record ArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
		}
	}
}
