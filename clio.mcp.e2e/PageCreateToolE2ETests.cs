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
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the create-page and list-page-templates MCP tools.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(PageCreateTool.ToolName)]
[NonParallelizable]
public sealed class PageCreateToolE2ETests {
	private const string ToolName = PageCreateTool.ToolName;
	private const string ListTemplatesToolName = PageTemplatesListTool.ToolName;

	[Test]
	[Description("Advertises create-page and list-page-templates in the MCP tool manifest.")]
	[AllureTag(ToolName)]
	[AllureName("create-page and list-page-templates are advertised by the MCP server")]
	public async Task PageCreateTool_Should_Be_Listed_By_MCP_Server() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));

		// Act
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(arrangeContext.CancellationTokenSource.Token);
		IEnumerable<string> toolNames = tools.Select(tool => tool.Name).ToList();

		// Assert
		toolNames.Should().Contain(ToolName,
			because: "create-page must be advertised so MCP callers can discover the page-creation tool directly");
		toolNames.Should().Contain(ListTemplatesToolName,
			because: "list-page-templates must be advertised alongside create-page for template discovery");
	}

	[Test]
	[Description("Reports readable failures when create-page is called with an invalid environment name.")]
	[AllureTag(ToolName)]
	[AllureName("create-page reports invalid environment failures")]
	public async Task PageCreateTool_Should_Report_Invalid_Environment_Failure() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-create-page-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = "UsrMissing_BlankPage",
					["template"] = "BlankPageTemplate",
					["package-name"] = "Custom",
					["dry-run"] = true,
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

	[Test]
	[Description("Rejects malformed schema-name via create-page before any remote calls.")]
	[AllureTag(ToolName)]
	[AllureName("create-page rejects malformed schema-name")]
	public async Task PageCreateTool_Should_Reject_Invalid_Schema_Name() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = "1BadName",
					["template"] = "BlankPageTemplate",
					["package-name"] = "Custom",
					["dry-run"] = true,
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

	[Test]
	[Description("Lists the live Freedom UI template catalog for the sandbox environment.")]
	[AllureTag(ListTemplatesToolName)]
	[AllureName("list-page-templates returns the live template catalog")]
	public async Task PageTemplatesListTool_Should_Return_Live_Catalog() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));

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
	}

	[Test]
	[Description("Creates a Freedom UI page from BlankPageTemplate and reads it back via get-page.")]
	[AllureTag(ToolName)]
	[AllureName("create-page creates a real page from BlankPageTemplate and get-page reads it back")]
	public async Task PageCreateTool_Should_Create_Real_Page_And_GetPage_Should_Read_It_Back() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(5));
		string packageName = await ResolveWritablePackageAsync(settings, environmentName, arrangeContext.CancellationTokenSource.Token);
		string schemaName = $"UsrE2E_BlankPage_{Guid.NewGuid():N}".Substring(0, 40);

		// Act
		CallToolResult createResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = schemaName,
					["template"] = "BlankPageTemplate",
					["package-name"] = packageName,
					["caption"] = "E2E blank page",
					["environment-name"] = environmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		PageCreateResponse createResponse = EntitySchemaStructuredResultParser.Extract<PageCreateResponse>(createResult);

		// Assert create
		createResult.IsError.Should().NotBeTrue();
		createResponse.Success.Should().BeTrue(because: $"create-page must succeed for a fresh schema name '{schemaName}' in package '{packageName}'. Error: {createResponse.Error}");
		createResponse.SchemaName.Should().Be(schemaName);
		createResponse.SchemaUId.Should().NotBeNullOrWhiteSpace();
		createResponse.TemplateName.Should().Be("BlankPageTemplate");
		createResponse.PackageName.Should().Be(packageName);
		createResponse.DryRun.Should().BeFalse();

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

	[Test]
	[Description("create-page returns a duplicate-name error when the schema already exists in the environment.")]
	[AllureTag(ToolName)]
	[AllureName("create-page rejects duplicate schema names")]
	public async Task PageCreateTool_Should_Report_Duplicate_Schema_Name() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(5));
		string packageName = await ResolveWritablePackageAsync(settings, environmentName, arrangeContext.CancellationTokenSource.Token);
		string schemaName = $"UsrE2E_DupPage_{Guid.NewGuid():N}".Substring(0, 40);

		// Create the page first
		CallToolResult first = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = schemaName,
					["template"] = "BlankPageTemplate",
					["package-name"] = packageName,
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
					["package-name"] = packageName,
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

	[Test]
	[Description("Reports readable failures when list-page-templates is called with an invalid schema-type.")]
	[AllureTag(ListTemplatesToolName)]
	[AllureName("list-page-templates reports invalid schema-type")]
	public async Task PageTemplatesListTool_Should_Reject_Invalid_Schema_Type() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));

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

	private static async Task<ArrangeContext> ArrangeAsync(TimeSpan timeout) {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		CancellationTokenSource cancellationTokenSource = new(timeout);
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new ArrangeContext(session, cancellationTokenSource);
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

	private static async Task<string> ResolveWritablePackageAsync(
		McpE2ESettings settings, string environmentName, CancellationToken cancellationToken) {
		string? configured = settings.Sandbox.PackageName;
		if (!string.IsNullOrWhiteSpace(configured)) {
			return configured;
		}
		ClioCliCommandResult result = await ClioCliCommandRunner.RunAsync(
			settings,
			["get-pkg-list", "-e", environmentName],
			cancellationToken: cancellationToken);
		if (result.ExitCode == 0 && result.StandardOutput.Contains("Custom")) {
			return "Custom";
		}
		Assert.Ignore(
			$"create-page MCP E2E requires a writable package. Configure McpE2E:Sandbox:PackageName or ensure a 'Custom' package exists in environment '{environmentName}'.");
		return string.Empty;
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
