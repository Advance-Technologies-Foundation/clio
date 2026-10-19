using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the create-page and list-page-templates MCP tools.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(PageCreateTool.ToolName)]
[NonParallelizable]
public sealed class PageCreateToolE2ETests : McpContractFixtureBase {
	private const string ToolName = PageCreateTool.ToolName;
	private const string ListTemplatesToolName = PageTemplatesListTool.ToolName;
	private const string PackageName = "Custom";

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Exposes create-page and list-page-templates via the get-tool-contract compact index on the lazy tool surface.")]
	[AllureTag(ToolName)]
	[AllureName("create-page and list-page-templates are discoverable on the lazy surface")]
	public async Task PageCreateTool_Should_Be_Listed_By_MCP_Server() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		// Act
		IReadOnlyCollection<string> toolNames =
			await arrangeContext.Session.ListReachableToolNamesAsync(arrangeContext.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(ToolName,
			because: $"the {ToolName} MCP tool must be discoverable on the lazy surface (get-tool-contract compact index) even though it is not resident in tools/list, so MCP callers can discover the page-creation tool");
		toolNames.Should().Contain(ListTemplatesToolName,
			because: $"the {ListTemplatesToolName} MCP tool must be discoverable on the lazy surface alongside create-page for template discovery");
	}

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Reports readable failures when create-page is called with an invalid environment name.")]
	[AllureTag(ToolName)]
	[AllureName("create-page reports invalid environment failures")]
	public async Task PageCreateTool_Should_Report_Invalid_Environment_Failure() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-create-page-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = "UsrMissing_BlankPage",
					["template"] = "BlankPageTemplate",
					["package-name"] = "Custom",
					["environment-name"] = invalidEnvironmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		PageCreateResponse response = EntitySchemaStructuredResultParser.Extract<PageCreateResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "structured create-page failures should stay inside the tool response envelope");
		response.Success.Should().BeFalse();
		response.Error.Should().MatchRegex(
			$"(?is)({Regex.Escape(invalidEnvironmentName)}|environment.*not.*found|not found)");
	}

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Rejects malformed schema-name via create-page before any remote calls.")]
	[AllureTag(ToolName)]
	[AllureName("create-page rejects malformed schema-name")]
	public async Task PageCreateTool_Should_Reject_Invalid_Schema_Name() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = "1BadName",
					["template"] = "BlankPageTemplate",
					["package-name"] = "Custom",
					["environment-name"] = $"noop-{Guid.NewGuid():N}"
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		PageCreateResponse response = EntitySchemaStructuredResultParser.Extract<PageCreateResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue();
		response.Success.Should().BeFalse();
		response.Error.Should().Contain("schema-name must start with a letter");
	}

	[Category("McpE2E.Sandbox")]
	[Test]
	[Description("Lists the live Freedom UI template catalog for the sandbox environment.")]
	[AllureTag(ListTemplatesToolName)]
	[AllureName("list-page-templates returns the live template catalog")]
	public async Task PageTemplatesListTool_Should_Return_Live_Catalog() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ListTemplatesToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		PageTemplateListResponse response = EntitySchemaStructuredResultParser.Extract<PageTemplateListResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue();
		response.Success.Should().BeTrue(because: "schema.template.api must be reachable in a provisioned Creatio environment");
		response.Items.Should().NotBeNull().And.NotBeEmpty(
			because: "the platform always advertises at least one Freedom UI template");
		response.Items.Select(t => t.Name).Should().Contain("BlankPageTemplate",
			because: "BlankPageTemplate is a stable baseline template across Creatio 7.x environments");
		response.Items.Select(t => t.Name).Should().Contain("BaseDashboardTemplate",
			because: "clio injects the dashboard-page parent that the platform template endpoint omits");
	}

	[Category("McpE2E.Sandbox")]
	[Test]
	[Description("Creates a Freedom UI page from BlankPageTemplate and reads it back via get-page.")]
	[AllureTag(ToolName)]
	[AllureName("create-page creates a real page from BlankPageTemplate and get-page reads it back")]
	public async Task PageCreateTool_Should_Create_Real_Page_And_GetPage_Should_Read_It_Back() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(5));
		string schemaName = $"UsrE2E_BlankPage_{Guid.NewGuid():N}".Substring(0, 40);

		// Act
		CallToolResult createResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = schemaName,
					["template"] = "BlankPageTemplate",
					["package-name"] = PackageName,
					["caption"] = "E2E blank page",
					["environment-name"] = environmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		PageCreateResponse createResponse = EntitySchemaStructuredResultParser.Extract<PageCreateResponse>(createResult);

		// Assert create
		createResult.IsError.Should().NotBeTrue();
		createResponse.Success.Should().BeTrue(because: $"create-page must succeed for a fresh schema name '{schemaName}' in the editable '{PackageName}' package. Error: {createResponse.Error}");
		createResponse.SchemaName.Should().Be(schemaName);
		createResponse.SchemaUId.Should().NotBeNullOrWhiteSpace();
		createResponse.TemplateName.Should().Be("BlankPageTemplate");
		createResponse.PackageName.Should().Be(PackageName);

		// Act read-back
		CallToolResult getResult = await arrangeContext.Session.CallToolAsync(
			PageGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = schemaName,
					["environment-name"] = environmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		PageGetResponse getResponse = EntitySchemaStructuredResultParser.Extract<PageGetResponse>(getResult);

		// Assert read-back
		getResult.IsError.Should().NotBeTrue();
		getResponse.Success.Should().BeTrue(because: "the freshly created page must be readable through get-page");
		getResponse.Page.Should().NotBeNull();
		getResponse.Page.SchemaName.Should().Be(schemaName);
		getResponse.Page.ParentSchemaName.Should().Be("BlankPageTemplate",
			because: "create-page must wire the new schema to the requested parent template");
	}

	[Category("McpE2E.Sandbox")]
	[Test]
	[Description("Creates a dashboard from BaseDashboardTemplate with optional-properties and reads it back via get-page.")]
	[AllureTag(ToolName)]
	[AllureName("create-page creates a dashboard with optional-properties and get-page reads it back")]
	public async Task PageCreateTool_Should_Create_Dashboard_With_Optional_Properties() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(5));
		string schemaName = $"UsrE2E_Dashboard_{Guid.NewGuid():N}".Substring(0, 40);
		const string optionalProperties =
			"""[{"key":"DashboardsEntitySchemaName","value":"Contact"},{"key":"DashboardsElementName","value":"Dashboards"}]""";

		// Act
		CallToolResult createResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = schemaName,
					["template"] = "BaseDashboardTemplate",
					["package-name"] = PackageName,
					["caption"] = "E2E dashboard",
					["optional-properties"] = optionalProperties,
					["environment-name"] = environmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		PageCreateResponse createResponse = EntitySchemaStructuredResultParser.Extract<PageCreateResponse>(createResult);

		// Assert create
		createResult.IsError.Should().NotBeTrue();
		createResponse.Success.Should().BeTrue(
			because: $"create-page must accept optional-properties and create the dashboard '{schemaName}'. Error: {createResponse.Error}");
		createResponse.SchemaUId.Should().NotBeNullOrWhiteSpace(
			because: "a created dashboard schema must return its UId");

		// Act read-back
		CallToolResult getResult = await arrangeContext.Session.CallToolAsync(
			PageGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = schemaName,
					["environment-name"] = environmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		PageGetResponse getResponse = EntitySchemaStructuredResultParser.Extract<PageGetResponse>(getResult);

		// Assert read-back
		getResult.IsError.Should().NotBeTrue();
		getResponse.Success.Should().BeTrue(because: "the freshly created dashboard must be readable through get-page");
		getResponse.Page.ParentSchemaName.Should().Be("BaseDashboardTemplate",
			because: "create-page must wire the dashboard to the BaseDashboardTemplate parent");
		getResponse.Files.Should().NotBeNull(
			because: "the MCP get-page wrapper compacts the response to file paths and writes the merged bundle to bundle.json");
		File.Exists(getResponse.Files.BundleFile).Should().BeTrue(
			because: "get-page must materialize the merged bundle for the created dashboard on disk");
		JsonObject bundle = JsonNode.Parse(
				await File.ReadAllTextAsync(getResponse.Files.BundleFile, arrangeContext.CancellationTokenSource.Token))!
			.AsObject();
		JsonArray bundleOptionalProperties = bundle["optionalProperties"]?.AsArray() ?? [];
		Dictionary<string, string?> persistedOptionalProperties = bundleOptionalProperties
			.OfType<JsonNode>()
			.ToDictionary(node => node["key"]?.ToString() ?? string.Empty, node => node["value"]?.ToString());
		persistedOptionalProperties.Should().ContainKey("DashboardsEntitySchemaName",
			because: "the designer service must persist the seeded entity-schema link-back, not silently drop it");
		persistedOptionalProperties["DashboardsEntitySchemaName"].Should().Be("Contact",
			because: "the persisted entity-schema link-back value must match what create-page seeded");
		persistedOptionalProperties.Should().ContainKey("DashboardsElementName",
			because: "the designer service must persist the seeded dashboards-element link-back, not silently drop it");
		persistedOptionalProperties["DashboardsElementName"].Should().Be("Dashboards",
			because: "the persisted dashboards-element link-back value must match what create-page seeded");
	}

	[Category("McpE2E.Sandbox")]
	[Test]
	[Description("create-page returns a duplicate-name error when the schema already exists in the environment.")]
	[AllureTag(ToolName)]
	[AllureName("create-page rejects duplicate schema names")]
	public async Task PageCreateTool_Should_Report_Duplicate_Schema_Name() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(5));
		string schemaName = $"UsrE2E_DupPage_{Guid.NewGuid():N}".Substring(0, 40);

		// Create the page first
		CallToolResult first = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = schemaName,
					["template"] = "BlankPageTemplate",
					["package-name"] = PackageName,
					["environment-name"] = environmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		EntitySchemaStructuredResultParser.Extract<PageCreateResponse>(first)
			.Success.Should().BeTrue(because: "arrange-step create-page must succeed before the duplicate-name assertion");

		// Act duplicate
		CallToolResult duplicateResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = schemaName,
					["template"] = "BlankPageTemplate",
					["package-name"] = PackageName,
					["environment-name"] = environmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		PageCreateResponse duplicateResponse = EntitySchemaStructuredResultParser.Extract<PageCreateResponse>(duplicateResult);

		// Assert
		duplicateResult.IsError.Should().NotBeTrue();
		duplicateResponse.Success.Should().BeFalse();
		duplicateResponse.Error.Should().Contain(schemaName).And.Contain("already exists");
	}

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Reports readable failures when list-page-templates is called with an invalid schema-type.")]
	[AllureTag(ListTemplatesToolName)]
	[AllureName("list-page-templates reports invalid schema-type")]
	public async Task PageTemplatesListTool_Should_Reject_Invalid_Schema_Type() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ListTemplatesToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-type"] = "desktop",
					["environment-name"] = $"noop-{Guid.NewGuid():N}"
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		PageTemplateListResponse response = EntitySchemaStructuredResultParser.Extract<PageTemplateListResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue();
		response.Success.Should().BeFalse();
		response.Error.Should().Contain("Unknown schema-type");
	}

	[Category("McpE2E.Sandbox")]
	[Test]
	[Description("Creates a desktop page from CentralAreaDesktopTemplate, reads it back via get-page to confirm the parent template, and deletes the schema on an FSM-off stand.")]
	[AllureTag(ToolName)]
	[AllureName("create-page creates a desktop from CentralAreaDesktopTemplate and get-page reads it back")]
	public async Task PageCreateTool_Should_Create_Desktop_From_Desktop_Template() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(5));
		CallToolResult fsmResult = await arrangeContext.Session.CallToolAsync(
			FsmModeTool.GetFsmModeToolName,
			new Dictionary<string, object?> { ["environmentName"] = environmentName },
			arrangeContext.CancellationTokenSource.Token);
		FsmModeStatusEnvelope fsmStatus = FsmModeStatusResultParser.Extract(fsmResult);
		if (string.Equals(fsmStatus.Mode, "on", StringComparison.OrdinalIgnoreCase)) {
			Assert.Ignore(
				"Creating a desktop through the remote designer API is not isolated on an FSM stand: the schema is not materialized in package storage and poisons later configuration saves. Run this test against an FSM-off sandbox.");
		}
		string schemaName = $"UsrE2E_Desktop_{Guid.NewGuid():N}".Substring(0, 40);

		try {
			// Act
			CallToolResult createResult = await arrangeContext.Session.CallToolAsync(
				ToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["schema-name"] = schemaName,
						["template"] = "CentralAreaDesktopTemplate",
						["package-name"] = PackageName,
						["caption"] = "E2E desktop",
						["environment-name"] = environmentName
					}
				},
				arrangeContext.CancellationTokenSource.Token);
			PageCreateResponse createResponse = EntitySchemaStructuredResultParser.Extract<PageCreateResponse>(createResult);

			// Assert create
			createResult.IsError.Should().NotBeTrue();
			createResponse.Success.Should().BeTrue(
				because: $"create-page must create a desktop from CentralAreaDesktopTemplate for a fresh schema name '{schemaName}'. Error: {createResponse.Error}");
			createResponse.TemplateName.Should().Be("CentralAreaDesktopTemplate",
				because: "a desktop is created from the CentralAreaDesktopTemplate parent");

			// Act read-back
			CallToolResult getResult = await arrangeContext.Session.CallToolAsync(
				PageGetTool.ToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["schema-name"] = schemaName,
						["environment-name"] = environmentName
					}
				},
				arrangeContext.CancellationTokenSource.Token);
			PageGetResponse getResponse = EntitySchemaStructuredResultParser.Extract<PageGetResponse>(getResult);

			// Assert read-back
			getResult.IsError.Should().NotBeTrue();
			getResponse.Success.Should().BeTrue(because: "the freshly created desktop page must be readable through get-page");
			getResponse.Page.ParentSchemaName.Should().Be("CentralAreaDesktopTemplate",
				because: "the desktop page must inherit the platform desktop template");
		} finally {
			// On an FSM-off stand, deleting the schema auto-removes its Desktop selector row (platform listener),
			// so the sandbox selector is not polluted by E2E desktops.
			using CancellationTokenSource cleanupCts = new(TimeSpan.FromMinutes(2));
			await ClioCliCommandRunner.RunAsync(
				settings,
				["delete-schema", schemaName, "--remote", "-e", environmentName],
				cancellationToken: cleanupCts.Token);
		}
	}

	[Category("McpE2E.Sandbox")]
	[Test]
	[Description("GH-1839: create-page with entity-schema-name binds a primary PDS data source to the entity, and get-page reads it back in the merged model config.")]
	[AllureTag(ToolName)]
	[AllureName("create-page with entity-schema-name creates a page with a primary data source")]
	public async Task PageCreateTool_Should_Bind_Primary_Data_Source_When_Entity_Is_Given() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(5));
		string schemaName = $"UsrE2E_PdsPage_{Guid.NewGuid():N}".Substring(0, 40);

		try {
			// Act
			CallToolResult createResult = await arrangeContext.Session.CallToolAsync(
				ToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["schema-name"] = schemaName,
						["template"] = "PageWithTabsFreedomTemplate",
						["package-name"] = PackageName,
						["entity-schema-name"] = "Contact",
						["environment-name"] = environmentName
					}
				},
				arrangeContext.CancellationTokenSource.Token);
			PageCreateResponse createResponse = EntitySchemaStructuredResultParser.Extract<PageCreateResponse>(createResult);
			PageGetResponse getResponse = await GetPageAsync(arrangeContext, schemaName, environmentName);

			// Assert
			createResponse.Success.Should().BeTrue(because: $"create-page must succeed. Error: {createResponse.Error}");
			createResponse.PrimaryDataSourceName.Should().Be("PDS",
				because: "a tabbed record page template has no primary data source, so create-page adds PDS");
			getResponse.Success.Should().BeTrue(because: $"the created page must be readable. Error: {getResponse.Error}");
			JsonNode modelConfig = await ReadModelConfigAsync(getResponse);
			JsonNode? primaryDataSourceName = modelConfig["primaryDataSourceName"];
			primaryDataSourceName.Should().NotBeNull(because: "the stored page must declare its primary data source");
			primaryDataSourceName!.GetValue<string>().Should().Be("PDS",
				because: "components bound to the primary data source show nothing without it");
			JsonNode? pdsEntitySchemaName = modelConfig["dataSources"]?["PDS"]?["config"]?["entitySchemaName"];
			pdsEntitySchemaName.Should().NotBeNull(because: "the stored PDS must carry an entity binding");
			pdsEntitySchemaName!.GetValue<string>().Should().Be("Contact", because: "PDS must be bound to the requested entity");
			modelConfig["dataSources"].Should().NotBeNull(because: "the stored page must keep its data sources");
			modelConfig["dataSources"]!["AttachmentListDS"].Should().NotBeNull(
				because: "the template's own data sources must survive the seeded merge");
		} finally {
			await DeleteSchemaAsync(settings, schemaName, environmentName);
		}
	}

	[Category("McpE2E.Sandbox")]
	[Test]
	[Description("GH-1839: update-page append of a modelConfigDiff-only fragment onto a page with an empty stored body saves a full page body and reports the config-diff count; an all-empty fragment is rejected.")]
	[AllureTag(PageUpdateTool.ToolName)]
	[AllureName("update-page append of modelConfigDiff onto an empty body is written; an empty fragment is rejected")]
	public async Task PageUpdateTool_Append_ModelConfigDiff_Onto_Empty_Body_Is_Written() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(5));
		string schemaName = $"UsrE2E_AppendMc_{Guid.NewGuid():N}".Substring(0, 40);
		const string fragment =
			"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[" +
			"{\"operation\":\"merge\",\"path\":[],\"values\":{\"primaryDataSourceName\":\"PDS\"}}," +
			"{\"operation\":\"merge\",\"path\":[\"dataSources\"],\"values\":{\"PDS\":{\"type\":\"crt.EntityDataSource\",\"scope\":\"page\",\"config\":{\"entitySchemaName\":\"Contact\"}}}}" +
			"]/**SCHEMA_MODEL_CONFIG_DIFF*/";
		const string emptyFragment = "modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/";

		try {
			CallToolResult createResult = await arrangeContext.Session.CallToolAsync(
				ToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["schema-name"] = schemaName,
						["template"] = "PageWithTabsFreedomTemplate",
						["package-name"] = PackageName,
						["environment-name"] = environmentName
					}
				},
				arrangeContext.CancellationTokenSource.Token);
			PageCreateResponse createResponse = EntitySchemaStructuredResultParser.Extract<PageCreateResponse>(createResult);
			createResponse.Success.Should().BeTrue(because: $"create-page must succeed. Error: {createResponse.Error}");

			// Act
			PageUpdateResponse emptyResponse = await AppendAsync(arrangeContext, schemaName, environmentName, emptyFragment);
			PageUpdateResponse appendResponse = await AppendAsync(arrangeContext, schemaName, environmentName, fragment);
			PageGetResponse getResponse = await GetPageAsync(arrangeContext, schemaName, environmentName);

			// Assert
			emptyResponse.Success.Should().BeFalse(because: "an append that contributes no operation must not report success");
			emptyResponse.Error.Should().Contain("contributes no operation",
				because: "the rejection must say why nothing would be written");
			appendResponse.Success.Should().BeTrue(because: $"the modelConfigDiff fragment must be appended. Error: {appendResponse.Error}");
			appendResponse.AppendProjection.Should().NotBeNull(because: "an append reports its projection");
			appendResponse.AppendProjection!.IncomingModelConfigDiffOperationCount.Should().Be(2,
				because: "the projection reports the config-diff operations, not only viewConfigDiff");
			(await File.ReadAllTextAsync(getResponse.Files.BodyFile)).Should().StartWith($"define(\"{schemaName}\"",
				because: "the fragment is merged into the empty page body, not saved as the whole body");
			JsonNode? appendedPrimaryDataSourceName = (await ReadModelConfigAsync(getResponse))["primaryDataSourceName"];
			appendedPrimaryDataSourceName.Should().NotBeNull(because: "the appended modelConfigDiff operations reach the page");
			appendedPrimaryDataSourceName!.GetValue<string>().Should().Be("PDS",
				because: "the appended modelConfigDiff operations reach the page");
		} finally {
			await DeleteSchemaAsync(settings, schemaName, environmentName);
		}
	}

	private static async Task<PageGetResponse> GetPageAsync(ArrangeContext arrangeContext, string schemaName, string environmentName) {
		CallToolResult getResult = await arrangeContext.Session.CallToolAsync(
			PageGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = schemaName,
					["environment-name"] = environmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		return EntitySchemaStructuredResultParser.Extract<PageGetResponse>(getResult);
	}

	private static async Task<JsonNode> ReadModelConfigAsync(PageGetResponse getResponse) {
		JsonNode bundle = JsonNode.Parse(await File.ReadAllTextAsync(getResponse.Files.BundleFile))!;
		return bundle["modelConfig"] ?? new JsonObject();
	}

	private static async Task<PageUpdateResponse> AppendAsync(
		ArrangeContext arrangeContext, string schemaName, string environmentName, string body) {
		CallToolResult result = await arrangeContext.Session.CallToolAsync(
			PageUpdateTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = schemaName,
					["body"] = body,
					["mode"] = "append",
					["environment-name"] = environmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		return EntitySchemaStructuredResultParser.Extract<PageUpdateResponse>(result);
	}

	private static async Task DeleteSchemaAsync(McpE2ESettings settings, string schemaName, string environmentName) {
		using CancellationTokenSource cleanupCts = new(TimeSpan.FromMinutes(2));
		await ClioCliCommandRunner.RunAsync(
			settings,
			["delete-schema", schemaName, "--remote", "-e", environmentName],
			cancellationToken: cleanupCts.Token);
	}

	private static async Task<string> ResolveReachableEnvironmentAsync(McpE2ESettings settings) {
		// The opt-in is consulted BEFORE the environment is resolved, so neither a clio process nor the
		// stand is reached while it is off. Every caller is a sandbox test that creates a real schema on
		// the environment, which is exactly what this switch authorizes.
		if (!DestructiveStandAuthorization.IsAuthorized(true, settings.AllowDestructiveMcpTests)) {
			Assert.Ignore(DestructiveStandAuthorization.MissingOptInMessage);
		}
		// Configured-only: the opt-in authorizes writes to the disposable stand named in settings, never
		// to a fallback environment that merely answers.
		return await ReachableSandboxEnvironment.ResolveConfiguredOrIgnoreAsync(
			settings,
			$"create-page MCP E2E requires the configured sandbox environment '{settings.Sandbox.EnvironmentName}' to be set and reachable.");
	}

}
