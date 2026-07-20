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
	[Description("Creates a desktop page from CentralAreaDesktopTemplate, reads it back via get-page to confirm the parent template, and deletes the schema (which auto-removes the platform-registered selector record).")]
	[AllureTag(ToolName)]
	[AllureName("create-page creates a desktop from CentralAreaDesktopTemplate and get-page reads it back")]
	public async Task PageCreateTool_Should_Create_Desktop_From_Desktop_Template() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(5));
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
			// Teardown: deleting the schema auto-removes its Desktop selector row (platform listener),
			// so the sandbox selector is not polluted by E2E desktops.
			using CancellationTokenSource cleanupCts = new(TimeSpan.FromMinutes(2));
			await ClioCliCommandRunner.RunAsync(
				settings,
				["delete-schema", schemaName, "--remote", "-e", environmentName],
				cancellationToken: cleanupCts.Token);
		}
	}

	private static async Task<string> ResolveReachableEnvironmentAsync(McpE2ESettings settings) {
		string? configuredEnvironmentName = settings.Sandbox.EnvironmentName;
		if (!string.IsNullOrWhiteSpace(configuredEnvironmentName) &&
			await CanReachEnvironmentAsync(settings, configuredEnvironmentName)) {
			return configuredEnvironmentName;
		}

		const string fallbackEnvironmentName = "d2";
		if (await CanReachEnvironmentAsync(settings, fallbackEnvironmentName)) {
			return fallbackEnvironmentName;
		}

		Assert.Ignore(
			$"create-page MCP E2E requires a reachable environment. Configured sandbox environment '{configuredEnvironmentName}' was not reachable, and fallback environment '{fallbackEnvironmentName}' was also unavailable.");
		return string.Empty;
	}

	private static async Task<bool> CanReachEnvironmentAsync(McpE2ESettings settings, string environmentName) {
		using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));
		try {
			ClioCliCommandResult result = await ClioCliCommandRunner.RunAsync(
				settings,
				["ping-app", "-e", environmentName],
				cancellationToken: cts.Token);
			return result.ExitCode == 0;
		} catch (OperationCanceledException) {
			return false;
		}
	}

}
