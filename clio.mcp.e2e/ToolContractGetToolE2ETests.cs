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
	[AllureName("tool-contract-get tool is advertised by the MCP server")]
	public async Task ToolContractGet_Should_Be_Listed_By_Mcp_Server() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		IList<McpClientTool> tools = await context.Session.ListToolsAsync(context.CancellationTokenSource.Token);

		// Assert
		tools.Select(tool => tool.Name).Should().Contain(ToolContractGetTool.ToolName,
			because: "the MCP server should advertise tool-contract-get as the bootstrap contract entry point");
	}

	[Test]
	[AllureTag(ToolContractGetTool.ToolName)]
	[AllureName("tool-contract-get returns maintenance-oriented canonical flows")]
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
				because: "page inspection should advertise page-sync as the canonical save path");
		response.Tools.Single(tool => tool.Name == PageSyncTool.ToolName)
			.PreferredFlow.Tools.Should().Equal(
				new[] {
					PageListTool.ToolName,
					PageGetTool.ToolName,
					PageSyncTool.ToolName,
					PageGetTool.ToolName
				},
				because: "page-sync should advertise itself as the canonical page write path");
		response.Tools.Single(tool => tool.Name == PageUpdateTool.ToolName)
			.PreferredFlow.Tools.Should().Equal(
				new[] {
					PageGetTool.ToolName,
					PageUpdateTool.ToolName,
					PageGetTool.ToolName
				},
				because: "page-update should still expose a concrete fallback flow for callers that explicitly require it");
		response.Tools.Single(tool => tool.Name == PageUpdateTool.ToolName)
			.Deprecations.Should().ContainSingle(deprecation =>
				deprecation.ReplacementTools.SequenceEqual(new[] { PageSyncTool.ToolName }) &&
				deprecation.Message.Contains("fallback"),
				because: "page-update should advertise page-sync as the canonical replacement");
		response.Tools.Single(tool => tool.Name == PageSyncTool.ToolName)
			.InputSchema.Properties.Should().Contain(field =>
				field.Name == "pages" &&
				field.Description.Contains("page-get.raw.body", StringComparison.Ordinal),
				because: "page-sync should advertise raw.body as the source of page write payloads");
		response.Tools.Single(tool => tool.Name == PageSyncTool.ToolName)
			.OutputContract.Fields.Should().Contain(field =>
				field.Name == "pages" &&
				field.Description.Contains("verified-body", StringComparison.Ordinal) &&
				field.Description.Contains("page", StringComparison.Ordinal),
				because: "page-sync should advertise the richer per-page verify response through tool-contract-get");
		response.Tools.Single(tool => tool.Name == PageUpdateTool.ToolName)
			.InputSchema.Properties.Should().Contain(field =>
				field.Name == "resources" &&
				field.Description.Contains("JSON object string", StringComparison.Ordinal),
				because: "page-update should clarify the concrete resources payload shape through the MCP server");
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
	[AllureName("tool-contract-get returns explicit Data Forge contracts and keeps maintenance tools out of the default bootstrap set")]
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
			because: "explicit tool-contract-get lookup should expose the full Data Forge contract surface");
		explicitResponse.Tools!.Select(tool => tool.Name).Should().Contain(DataForgeTool.DataForgeInitializeToolName,
			because: "explicit lookup should still return Data Forge initialize for remediation workflows");
		explicitResponse.Tools!.Select(tool => tool.Name).Should().Contain(DataForgeTool.DataForgeUpdateToolName,
			because: "explicit lookup should still return Data Forge update for remediation workflows");
		explicitResponse.Tools.Single(tool => tool.Name == DataForgeTool.DataForgeHealthToolName)
			.Defaults.Should().Contain(definition =>
				definition.Name == "scope" && definition.Value == "use_enrichment",
				because: "the explicit Data Forge contract should advertise the default OAuth scope");
	}

	[Test]
	[AllureTag(ToolContractGetTool.ToolName)]
	[AllureName("tool-contract-get advertises settings-health bootstrap diagnostics contract")]
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
			because: "settings-health should remain discoverable through the executable clio MCP contract catalog");
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
	[AllureName("tool-contract-get returns canonical entity-schema contracts from clio")]
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
				because: "schema-sync should advertise the canonical batched schema workflow");
		response.Tools.Single(tool => tool.Name == CreateLookupTool.CreateLookupToolName)
			.PreferredFlow.Tools.Should().Equal(
				new[] {
					SchemaSyncTool.ToolName
				},
				because: "create-lookup should advertise schema-sync as the preferred canonical path");
		response.Tools.Single(tool => tool.Name == CreateEntitySchemaTool.CreateEntitySchemaToolName)
			.PreferredFlow.Tools.Should().Equal(
				new[] {
					SchemaSyncTool.ToolName
				},
				because: "create-entity-schema should advertise schema-sync as the preferred canonical path");
	}

	[Test]
	[AllureTag(ToolContractGetTool.ToolName)]
	[AllureName("tool-contract-get returns canonical DB-first binding contracts from clio")]
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
				because: "create-data-binding-db should advertise schema-sync as the canonical batched path");
		createContract.Deprecations.Should().ContainSingle(
			because: "create-data-binding-db should advertise its explicit fallback positioning");
		createContract.Deprecations[0].Message.Should().Contain("seed-rows",
			because: "the fallback guidance should direct callers to inline seed-rows inside schema-sync");
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
	[AllureName("tool-contract-get returns canonical required field name for modify-entity-schema-column")]
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
	[AllureName("tool-contract-get advertises installed application identity fields for application-get-info")]
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
			because: "the application-get-info contract should be readable through the MCP server");
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
	[AllureName("tool-contract-get advertises page discovery selectors and raw body semantics")]
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
			because: "page-list should advertise code as a first-class selector");
		pageListContract.Aliases.Should().Contain(alias =>
				alias.CanonicalName == "code"
				&& alias.Alias == "app-code"
				&& alias.Status == "rejected",
			because: "page-list should advertise the legacy app-code selector as rejected");
		pageListContract.OutputContract.Fields.Should().Contain(field =>
				field.Name == "pages" &&
				field.Description.Contains("schema-name", StringComparison.Ordinal),
			because: "page-list should describe page discovery items through schema-name");
		pageListContract.FallbackFlow.Should().Contain(flow => flow.Tools.SequenceEqual(new[] {
				PageListTool.ToolName,
				PageGetTool.ToolName,
				PageUpdateTool.ToolName,
				PageGetTool.ToolName
			}),
			because: "page-list should advertise a single page-update fallback sequence after discovery");
		ToolContractDefinition pageGetContract = response.Tools!.Single(tool => tool.Name == PageGetTool.ToolName);
		pageGetContract.OutputContract.Fields.Should().Contain(field =>
				field.Name == "raw" &&
				field.Description.Contains("raw.body", StringComparison.Ordinal),
			because: "page-get should explicitly advertise raw.body as the editable JavaScript source");
	}

	[Test]
	[AllureTag(ToolContractGetTool.ToolName)]
	[AllureName("tool-contract-get returns structured unknown tool suggestions")]
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
	[AllureName("tool-contract-get returns field-level validation errors for blank tool names")]
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
			because: "tool-contract-get should return a normal MCP tool result envelope for valid request shapes");
		return EntitySchemaStructuredResultParser.Extract<ToolContractGetResponse>(callResult);
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
