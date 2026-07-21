using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(GuidanceGetTool.ToolName)]
[NonParallelizable]
public sealed class GuidanceGetToolE2ETests : McpContractFixtureBase {
	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("get-guidance tool is advertised by the MCP server")]
	public async Task GuidanceGet_Should_Be_Listed_By_Mcp_Server() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		IList<McpClientTool> tools = await context.Session.ListToolsAsync(context.CancellationTokenSource.Token);

		// Assert
		tools.Select(tool => tool.Name).Should().Contain(GuidanceGetTool.ToolName,
			because: "the MCP server should advertise get-guidance as the tool-native way to read canonical guidance");
	}

	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("get-guidance returns the canonical handler guidance article")]
	public async Task GuidanceGet_Should_Return_Page_Schema_Handlers_Guide() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		GuidanceGetResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["name"] = "page-schema-handlers"
			});

		// Assert
		response.Success.Should().BeTrue(
			because: "page-schema-handlers is a registered guidance name");
		response.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article payload");
		response.Article!.Uri.Should().Be("docs://mcp/guides/page-schema-handlers",
			because: "the canonical resource URI should still be visible in the tool response");
		response.Article.Text.Should().Contain("clio MCP page-schema handlers guide",
			because: "the guidance tool should return the canonical handler guide text");
		response.Article.Text.Should().Contain("There is no page for new or existing record",
			because: "the page-schema-handlers guide must carry the crt.CreateRecordRequest page-resolution note warning that the request throws this runtime error on a section-less detail entity with no registered page");
	}

	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("get-guidance returns the canonical page-to-object binding guidance article")]
	public async Task GuidanceGet_Should_Return_Related_Page_Binding_Guide() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		GuidanceGetResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["name"] = "related-page-binding"
			});

		// Assert
		response.Success.Should().BeTrue(
			because: "related-page-binding is a registered guidance name");
		response.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article payload");
		response.Article!.Uri.Should().Be("docs://mcp/guides/related-page-binding",
			because: "the canonical resource URI should still be visible in the tool response");
		response.Article.Text.Should().Contain("clio MCP page-to-object binding guide",
			because: "the guidance tool should return the canonical page-to-object binding guide text");
		response.Article.Text.Should().Contain("create-related-page-addon",
			because: "the resolved article must document the create-related-page-addon write tool");
	}

	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("get-guidance returns the canonical validator guidance article")]
	public async Task GuidanceGet_Should_Return_Page_Schema_Validators_Guide() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		GuidanceGetResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["name"] = "page-schema-validators"
			});

		// Assert
		response.Success.Should().BeTrue(
			because: "page-schema-validators is a registered guidance name");
		response.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article payload");
		response.Article!.Uri.Should().Be("docs://mcp/guides/page-schema-validators",
			because: "the canonical resource URI should still be visible in the tool response");
		response.Article.Text.Should().Contain("clio MCP page-schema validators guide",
			because: "the guidance tool should return the canonical validator guide text");
	}

	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("get-guidance returns the canonical sdk common guidance article")]
	public async Task GuidanceGet_Should_Return_Page_Schema_Sdk_Common_Guide() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		GuidanceGetResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["name"] = "page-schema-creatio-devkit-common"
			});

		// Assert
		response.Success.Should().BeTrue(
			because: "page-schema-creatio-devkit-common is a registered guidance name");
		response.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article payload");
		response.Article!.Uri.Should().Be("docs://mcp/guides/page-schema-creatio-devkit-common",
			because: "the canonical resource URI should still be visible in the tool response");
		response.Article.Text.Should().Contain("clio MCP page-schema sdk common guide",
			because: "the guidance tool should return the canonical sdk common guide text");
	}

	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("get-guidance returns the canonical page localizable string guidance article")]
	public async Task GuidanceGet_Should_Return_Page_Schema_Resources_Guide() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		GuidanceGetResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["name"] = "page-schema-resources"
			});

		// Assert
		response.Success.Should().BeTrue(
			because: "page-schema-resources is a registered guidance name");
		response.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article payload");
		response.Article!.Uri.Should().Be("docs://mcp/guides/page-schema-resources",
			because: "the canonical resource URI should still be visible in the tool response");
		response.Article.Text.Should().Contain("clio MCP page-schema resources guide",
			because: "the guidance tool should return the canonical resources guide text");
	}

	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("get-guidance returns the canonical converter guidance article")]
	public async Task GuidanceGet_Should_Return_Page_Schema_Converters_Guide() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		GuidanceGetResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["name"] = "page-schema-converters"
			});

		// Assert
		response.Success.Should().BeTrue(
			because: "page-schema-converters is a registered guidance name");
		response.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article payload");
		response.Article!.Uri.Should().Be("docs://mcp/guides/page-schema-converters",
			because: "the canonical resource URI should still be visible in the tool response");
		response.Article.Text.Should().Contain("clio MCP page-schema converters guide",
			because: "the guidance tool should return the canonical converter guide text");
	}

	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("get-guidance returns the canonical indicator widget guidance article")]
	public async Task GuidanceGet_Should_Return_Indicator_Widget_Guide() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		GuidanceGetResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["name"] = "indicator-widget"
			});

		// Assert
		response.Success.Should().BeTrue(
			because: "indicator-widget is a registered guidance name");
		response.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article payload");
		response.Article!.Uri.Should().Be("docs://mcp/guides/indicator-widget",
			because: "the canonical resource URI should still be visible in the tool response");
		response.Article.Text.Should().Contain("clio MCP indicator widget guide",
			because: "the guidance tool should return the canonical indicator widget guide text");
		response.Article.Text.Should().Contain("get-component-info",
			because: "the trimmed guide should point external callers to get-component-info as the source of truth");
	}

	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("get-guidance returns the canonical related-list guidance article")]
	public async Task GuidanceGet_Should_Return_Related_List_Guide() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		GuidanceGetResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["name"] = "related-list"
			});

		// Assert
		response.Success.Should().BeTrue(
			because: "related-list is a registered guidance name");
		response.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article payload");
		response.Article!.Uri.Should().Be("docs://mcp/guides/related-list",
			because: "the canonical resource URI should still be visible in the tool response");
		response.Article.Text.Should().Contain("clio MCP related list guide",
			because: "the guidance tool should return the canonical related-list guide text");
		response.Article.Text.Should().Contain("modelConfig.dependencies",
			because: "the related-list guide must teach the declarative dependencies entry that scopes a list by the open record");
		response.Article.Text.Should().Contain("\"relationPath\": \"PDS.Id\"",
			because: "the related-list guide must show the canonical relationPath pointing at the page primary data source id");
		response.Article.Text.Should().Contain("There is no page for new or existing record",
			because: "the related-list guide must warn that a header CreateRecordRequest Add button on a section-less detail entity throws this runtime error on click");
		response.Article.Text.Should().Contain("inline add row IS the add affordance",
			because: "the related-list guide must still name the inline add affordance (Mechanism B) for the simple-line-item case, even though page-based add is the primary path");
	}

	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("get-guidance page-modification pins the anti-bundle-reverse-engineering rule")]
	public async Task GuidanceGet_Should_Pin_AntiBundleReverseEngineering_ForPageModification() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		GuidanceGetResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["name"] = "page-modification-overview"
			});

		// Assert
		response.Success.Should().BeTrue(
			because: "page-modification-overview is a registered guidance name");
		response.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article payload");
		response.Article!.Uri.Should().Be("docs://mcp/guides/page-modification-overview",
			because: "the overview sub-guide owns the anti-bundle reverse-engineering rule after the guidance split");
		response.Article.Text.Should().Contain("reverse-engineering one is NOT a substitute",
			because: "the anti-bundle-reverse-engineering guidance is a core ENG-91953 deliverable and must survive over the real MCP wire");
	}

	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("get-guidance returns the canonical configuration web-service guide")]
	public async Task GuidanceGet_Should_Return_Configuration_WebService_Guide() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		GuidanceGetResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["name"] = "configuration-webservice"
			});

		// Assert
		response.Success.Should().BeTrue(
			because: "configuration-webservice is a registered guidance name");
		response.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article payload");
		response.Article!.Uri.Should().Be("docs://mcp/guides/configuration-webservice",
			because: "the canonical resource URI should still be visible in the tool response");
		response.Article.Text.Should().Contain("creatio-config-webservice",
			because: "the guidance tool should return the canonical configuration web-service guide text");
	}

	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("get-guidance returns the canonical configuration web-service test guide")]
	public async Task GuidanceGet_Should_Return_Configuration_WebService_Tests_Guide() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		GuidanceGetResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["name"] = "configuration-webservice-tests"
			});

		// Assert
		response.Success.Should().BeTrue(
			because: "configuration-webservice-tests is a registered guidance name");
		response.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article payload");
		response.Article!.Uri.Should().Be("docs://mcp/guides/configuration-webservice-tests",
			because: "the canonical resource URI should still be visible in the tool response");
		response.Article.Text.Should().Contain("configuration-webservice-tests",
			because: "the guidance tool should return the canonical configuration web-service test guide text");
	}

	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("get-guidance returns generated composable-app skill guides")]
	public async Task GuidanceGet_Should_Return_Generated_Composable_App_Skill_Guides() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		GuidanceGetResponse atfResponse = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["name"] = "atf-repository-dev"
			});
		GuidanceGetResponse sysSettingTestsResponse = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["name"] = "sys-setting-tests"
			});

		// Assert
		atfResponse.Success.Should().BeTrue(
			because: "atf-repository-dev is a generated guidance name");
		atfResponse.Article.Should().NotBeNull(
			because: "successful generated guidance lookups should return the resolved article payload");
		atfResponse.Article!.Uri.Should().Be("docs://mcp/guides/atf-repository-dev",
			because: "the canonical generated resource URI should still be visible in the tool response");
		atfResponse.Article.Text.Should().Contain("ATF.Repository",
			because: "the guidance tool should return the generated source skill text");

		sysSettingTestsResponse.Success.Should().BeTrue(
			because: "sys-setting-tests is a generated guidance name");
		sysSettingTestsResponse.Article.Should().NotBeNull(
			because: "successful generated test guidance lookups should return the resolved article payload");
		sysSettingTestsResponse.Article!.Uri.Should().Be("docs://mcp/guides/sys-setting-tests",
			because: "the canonical generated test resource URI should still be visible in the tool response");
		sysSettingTestsResponse.Article.Text.Should().Contain("SetupSysSettings",
			because: "the guidance tool should return the generated test skill text");
	}

	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("get-guidance returns the mobile page modification guide")]
	[Description("Verifies that get-guidance returns the mobile-page-modification article with correct URI and text about limited page-level business-rule support plus mobile body constraints.")]
	public async Task GuidanceGet_Should_Return_Mobile_Page_Modification_Guide() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		GuidanceGetResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["name"] = "mobile-page-modification"
			});

		// Assert
		response.Success.Should().BeTrue(
			because: "mobile-page-modification is a registered guidance name");
		response.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article payload");
		response.Article!.Uri.Should().Be("docs://mcp/guides/mobile-page-modification",
			because: "the canonical resource URI for the mobile page guide should be stable");
		response.Article.Text.Should().Contain("clio MCP mobile page modification guide",
			because: "the guidance tool should return the canonical mobile page guide text");
		response.Article.Text.Should().Contain("create-page-business-rule",
			because: "the mobile guide should explicitly document page-level business-rule support");
		response.Article.Text.Should().Contain("create-entity-business-rule",
			because: "the mobile guide should explicitly document entity-level business-rule support too");
		response.Article.Text.Should().Contain("limited set of conditions and actions",
			because: "the mobile guide should warn callers that mobile business rules do not have full web parity");
		response.Article.Text.Should().Contain("validators",
			because: "the mobile guide must document that validators are not supported in mobile pages");
		response.Article.Text.Should().Contain("handlers",
			because: "the mobile guide must document that handlers are not supported in mobile pages");
	}

	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("get-guidance returns the web page modification guide with mandatory component-type verification")]
	[Description("Verifies that get-guidance returns the page-modification article and that it mandates verifying a component type via get-component-info and asking the user when no OOTB component matches (ENG-90939).")]
	public async Task GuidanceGet_Should_Return_Page_Modification_Guide_With_Component_Verification() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		GuidanceGetResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["name"] = "page-modification"
			});

		// Assert
		response.Success.Should().BeTrue(
			because: "page-modification is a registered guidance name");
		response.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article payload");
		response.Article!.Uri.Should().Be("docs://mcp/guides/page-modification",
			because: "the canonical resource URI for the web page guide should be stable");
		response.Article.Text.Should().Contain("ENTRY guide for editing a Freedom UI page",
			because: "the guidance tool should return the canonical page-modification entry guide text");
		response.Article.Text.Should().Contain("COMPONENT-TYPE VERIFICATION IS MANDATORY",
			because: "the web page guide must force component-type verification before any viewConfigDiff insert to prevent invented crt.* types");
		response.Article.Text.Should().Contain("get-component-info",
			because: "the verification rule must route the agent to get-component-info as the authoritative component catalog");
		response.Article.Text.Should().Contain("ASK THE USER",
			because: "the web page guide must tell the agent to ask the user (existing component vs custom) when no OOTB component matches");
		response.Article.Text.Should().Contain("page-modification-containers",
			because: "the entry guide must route container placement and content-slot work to the dedicated containers sub-guide after the guidance split");
		response.Article.Text.Should().Contain("showing a user-facing message/confirmation/info/success/error popup",
			because: "the gate table must route a 'show a confirmation message' requirement into page-schema-handlers so the agent uses crt.ShowDialogRequest (ENG-91748)");
		response.Article.Text.Should().Contain("NEVER use `alert(...)`, `window.alert(...)`, `confirm(...)`, or `prompt(...)`",
			because: "the web page guide must forbid raw browser dialog primitives in page-body handlers so the agent stops emitting alert() (ENG-91748)");
	}

	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("get-guidance returns the page-modification-overview sub-guide")]
	[Description("Verifies get-guidance returns the page-modification-overview sub-guide created by the ENG-91556 split, carrying the relocated body save-lifecycle content.")]
	public async Task GuidanceGet_Should_Return_Page_Modification_Overview_Guide() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		GuidanceGetResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["name"] = "page-modification-overview"
			});

		// Assert
		response.Success.Should().BeTrue(
			because: "page-modification-overview is a registered guidance name after the split");
		response.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article payload");
		response.Article!.Uri.Should().Be("docs://mcp/guides/page-modification-overview",
			because: "the canonical resource URI for the overview sub-guide should be stable");
		response.Article.Text.Should().Contain("clio MCP page modification overview guide",
			because: "the guidance tool should return the canonical overview sub-guide text");
		response.Article.Text.Should().Contain("do NOT resend the full raw.body",
			because: "the do-not-resend rule moved into the overview sub-guide");
	}

	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("get-guidance returns the page-modification-field-contract sub-guide")]
	[Description("Verifies get-guidance returns the page-modification-field-contract sub-guide created by the ENG-91556 split, carrying the relocated inserted-field contract content.")]
	public async Task GuidanceGet_Should_Return_Page_Modification_Field_Contract_Guide() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		GuidanceGetResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["name"] = "page-modification-field-contract"
			});

		// Assert
		response.Success.Should().BeTrue(
			because: "page-modification-field-contract is a registered guidance name after the split");
		response.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article payload");
		response.Article!.Uri.Should().Be("docs://mcp/guides/page-modification-field-contract",
			because: "the canonical resource URI for the field-contract sub-guide should be stable");
		response.Article.Text.Should().Contain("clio MCP page modification field-contract guide",
			because: "the guidance tool should return the canonical field-contract sub-guide text");
		response.Article.Text.Should().Contain("Inserted-field contract for a new data-bound field control",
			because: "the inserted-field contract section moved into the field-contract sub-guide");
	}

	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("get-guidance returns the page-modification-containers sub-guide")]
	[Description("Verifies get-guidance returns the page-modification-containers sub-guide created by the ENG-91556 split, carrying the relocated bundle.json / parentName content.")]
	public async Task GuidanceGet_Should_Return_Page_Modification_Containers_Guide() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		GuidanceGetResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["name"] = "page-modification-containers"
			});

		// Assert
		response.Success.Should().BeTrue(
			because: "page-modification-containers is a registered guidance name after the split");
		response.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article payload");
		response.Article!.Uri.Should().Be("docs://mcp/guides/page-modification-containers",
			because: "the canonical resource URI for the containers sub-guide should be stable");
		response.Article.Text.Should().Contain("clio MCP page modification containers guide",
			because: "the guidance tool should return the canonical containers sub-guide text");
		response.Article.Text.Should().Contain("Finding a container for a new component",
			because: "the container-selection section moved into the containers sub-guide");
	}

	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("get-guidance returns the page-modification-components sub-guide")]
	[Description("Verifies get-guidance returns the page-modification-components sub-guide created by the ENG-91556 split, carrying the relocated viewConfigDiff/handler/get-component-info content.")]
	public async Task GuidanceGet_Should_Return_Page_Modification_Components_Guide() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		GuidanceGetResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["name"] = "page-modification-components"
			});

		// Assert
		response.Success.Should().BeTrue(
			because: "page-modification-components is a registered guidance name after the split");
		response.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article payload");
		response.Article!.Uri.Should().Be("docs://mcp/guides/page-modification-components",
			because: "the canonical resource URI for the components sub-guide should be stable");
		response.Article.Text.Should().Contain("clio MCP page modification components guide",
			because: "the guidance tool should return the canonical components sub-guide text");
		response.Article.Text.Should().Contain("Adding a button with a click handler",
			because: "the button+handler section moved into the components sub-guide");
	}

	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("get-guidance returns the canonical home-page guidance article")]
	[Description("Verifies get-guidance resolves the home-page guide over the real stdio MCP path, confirming the create-page tool and routing map route to a live catalog entry.")]
	public async Task GuidanceGet_Should_Return_Home_Page_Guide() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		GuidanceGetResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["name"] = "home-page"
			});

		// Assert
		response.Success.Should().BeTrue(
			because: "home-page is a registered guidance name");
		response.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article payload");
		response.Article!.Uri.Should().Be("docs://mcp/guides/home-page",
			because: "the canonical resource URI for the home-page guide should be stable");
		response.Article.Text.Should().Contain("clio MCP home-page guide",
			because: "the guidance tool should return the canonical home-page guide text");
	}

	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("get-guidance returns the canonical dashboard-and-home-page-layout guidance article")]
	[Description("Verifies get-guidance resolves the shared dashboard-and-home-page-layout guide over the real stdio MCP path — the layout/styling guide the dashboards router and home-page guide both route to after the extraction.")]
	public async Task GuidanceGet_Should_Return_Widget_Layout_Guide() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		GuidanceGetResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["name"] = "dashboard-and-home-page-layout"
			});

		// Assert
		response.Success.Should().BeTrue(
			because: "dashboard-and-home-page-layout is a registered guidance name");
		response.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article payload");
		response.Article!.Uri.Should().Be("docs://mcp/guides/dashboard-and-home-page-layout",
			because: "the canonical resource URI for the dashboard-and-home-page-layout guide should be stable");
		response.Article.Text.Should().Contain("clio MCP dashboard and home page layout guide",
			because: "the guidance tool should return the canonical dashboard-and-home-page-layout guide text");
	}

	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("get-guidance returns the canonical ESQ guidance article")]
	public async Task GuidanceGet_Should_Return_Esq_Guide() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		GuidanceGetResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["name"] = "esq"
			});

		// Assert
		response.Success.Should().BeTrue(
			because: "esq is a registered guidance name");
		response.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article payload");
		response.Article!.Uri.Should().Be("docs://mcp/guides/esq",
			because: "the canonical resource URI should still be visible in the tool response");
		response.Article.Text.Should().Contain("clio MCP ESQ guide",
			because: "the guidance tool should return the canonical ESQ guide text");
	}

	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("get-guidance returns the canonical ESQ filter family router")]
	[Description("Verifies the stable esq-filters name now routes callers to responsibility-specific frontend, backend, and parsing articles.")]
	public async Task GuidanceGet_ShouldReturnEsqFilterRouter_WhenStableFamilyNameIsRequested() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		GuidanceGetResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["name"] = "esq-filters"
			});

		// Assert
		response.Success.Should().BeTrue(
			because: "esq-filters is a registered guidance name");
		response.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article payload");
		response.Article!.Uri.Should().Be("docs://mcp/guides/esq-filters",
			because: "the canonical resource URI should still be visible in the tool response");
		response.Article.Text.Should().Contain("esq-filters-frontend",
			because: "the family router should expose the frontend construction owner");
		response.Article.Text.Should().Contain("esq-filters-backend",
			because: "the family router should expose the backend construction owner");
		response.Article.Text.Should().Contain("esq-filter-parsing",
			because: "the family router should expose the runtime parsing owner");
		response.Article.Text.Should().Contain("inclusive Between ranges",
			because: "get-guidance should report the current promoted backend validation status");
		response.Article.Text.Should().Contain("lookup equality/membership",
			because: "get-guidance should report promoted typed lookup coverage");
		response.Article.Text.Should().Contain("temporal literals/macros/date parts",
			because: "get-guidance should report promoted temporal coverage");
		response.Article.Text.Should().Contain("Exists/NotExists/aggregate subqueries",
			because: "get-guidance should report promoted subquery coverage");
		response.Article.Text.Should().Contain("saved Segment membership",
			because: "get-guidance should report promoted Segment coverage");
	}

	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("get-guidance returns every responsibility-specific ESQ filter article")]
	[Description("Verifies frontend construction, backend construction, and runtime parsing ESQ filter articles are independently retrievable by stable name.")]
	public async Task GuidanceGet_ShouldReturnEsqFilterChildGuides_WhenStableChildNamesAreRequested() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		GuidanceGetResponse frontend = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> { ["name"] = "esq-filters-frontend" });
		GuidanceGetResponse backend = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> { ["name"] = "esq-filters-backend" });
		GuidanceGetResponse parsing = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> { ["name"] = "esq-filter-parsing" });

		// Assert
		frontend.Success.Should().BeTrue(
			because: "serialized filter construction should have one retrievable frontend owner");
		frontend.Article!.Uri.Should().Be("docs://mcp/guides/esq-filters/frontend",
			because: "the frontend catalog name should preserve the hierarchical frontend URI");
		backend.Success.Should().BeTrue(
			because: "native C# filter construction should have one retrievable backend owner");
		backend.Article!.Uri.Should().Be("docs://mcp/guides/esq-filters/backend",
			because: "the backend catalog name should preserve the hierarchical backend URI");
		backend.Article!.Text.Should().Contain("FilterComparisonType.NotEndWith",
			because: "get-guidance should return the concrete backend scalar Compare recipes");
		backend.Article!.Text.Should().Contain("disabledLeaf.IsEnabled = false",
			because: "get-guidance should return the verified native disabled-leaf recipe");
		backend.Article!.Text.Should().Contain("CreateIsNullFilter(\"UsrDescription\")",
			because: "get-guidance should return the verified native null-filter recipe");
		backend.Article!.Text.Should().Contain("object[] sequenceNumbers = { 10, 30 }",
			because: "get-guidance should return the verified native membership recipe");
		backend.Article!.Text.Should().Contain("FilterComparisonType.Between",
			because: "get-guidance should return the verified native Between recipe");
		backend.Article!.Text.Should().Contain("LookupDataValueType`, not `GuidDataValueType`",
			because: "get-guidance should return the verified lookup type distinction");
		backend.Article!.Text.Should().Contain("EntitySchemaQueryMacrosType.CurrentYear",
			because: "get-guidance should return verified native temporal macro construction");
		backend.Article!.Text.Should().Contain("createdOnDate.TrimDateTimeParameterToDate = true",
			because: "get-guidance should return verified date-only construction");
		backend.Article!.Text.Should().Contain("esq.CreateExistsFilter(ownerActivities)",
			because: "get-guidance should return verified native Exists construction");
		backend.Article!.Text.Should().Contain("out EntitySchemaQuery activitySubQuery",
			because: "get-guidance should return verified aggregate child-filter construction");
		backend.Article!.Text.Should().Contain("new SegmentFilterOptions",
			because: "get-guidance should return verified native Segment construction");
		backend.Article!.Text.Should().Contain("UseSegmentFiltering",
			because: "get-guidance should retain the Segment feature gate");
		parsing.Success.Should().BeTrue(
			because: "runtime C# filter interpretation should have one retrievable parsing owner");
		parsing.Article!.Uri.Should().Be("docs://mcp/guides/esq-filter-parsing",
			because: "the parsing catalog name should preserve the independent parsing URI");
		parsing.Article!.Text.Should().Contain("ReadScalarParameter",
			because: "get-guidance should return the verified runtime scalar parameter parsing recipe");
		parsing.Article!.Text.Should().Contain("ReadIntegerBetween",
			because: "get-guidance should return the verified runtime Between parsing recipe");
		parsing.Article!.Text.Should().Contain("ReadTypedParameter<bool, BooleanDataValueType>",
			because: "get-guidance should return verified typed parameter parsing");
		parsing.Article!.Text.Should().Contain("return group.IsNot ? !result : result",
			because: "get-guidance should return the verified group-negation evaluation rule");
		parsing.Article!.Text.Should().Contain("ReadNullComparison",
			because: "get-guidance should return the verified null-filter parsing contract");
		parsing.Article!.Text.Should().Contain("ReadIntegerMembership",
			because: "get-guidance should return the verified membership parsing contract");
		parsing.Article!.Text.Should().Contain("ReadTrimmedDate",
			because: "get-guidance should return the verified trim-to-date parsing contract");
		parsing.Article!.Text.Should().Contain("Function.GetArguments()",
			because: "get-guidance should return recursive temporal function parsing rules");
		parsing.Article!.Text.Should().Contain("Capture one provider-clock snapshot",
			because: "get-guidance should return query-scoped temporal boundary caching guidance");
		parsing.Article!.Text.Should().Contain("ReadActivityExistenceSubquery",
			because: "get-guidance should return verified existence-subquery parsing guidance");
		parsing.Article!.Text.Should().Contain("Do not call `child.Columns.Single()`",
			because: "get-guidance should return verified aggregate-column parsing guidance");
		parsing.Article!.Text.Should().Contain("Count(Id) without Distinct",
			because: "get-guidance should preserve exact aggregate operand validation");
		parsing.Article!.Text.Should().Contain("materialize an unbounded child source",
			because: "get-guidance should preserve bounded fallback execution guidance");
		parsing.Article!.Text.Should().Contain("ReadSegmentMembership",
			because: "get-guidance should return verified expanded Segment parsing guidance");
		parsing.Article!.Text.Should().Contain("ValidateCurrentMembershipFilters",
			because: "get-guidance should preserve complete shape validation before external work");
		parsing.Article!.Text.Should().Contain("RequireAuthorizedCurrentSegmentOncePerQuery",
			because: "get-guidance should preserve request-scoped Segment authorization");
		parsing.Article!.Text.Should().Contain("Never reuse a cross-caller",
			because: "get-guidance should preserve safe query-scoped authorization caching");
		parsing.Article!.Text.Should().Contain("SQL table identifiers cannot be parameters",
			because: "get-guidance should preserve dynamic membership-table identifier safety");
	}

	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("get-guidance returns the canonical theming orchestration guide")]
	[Description("Verifies get-guidance returns the theming article that builds the theme CSS with the native build-theme tool and routes the no-code flow to create-theme.")]
	public async Task GuidanceGet_Should_Return_Theming_Guide() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);

		// Act
		GuidanceGetResponse response = await CallAsync(
			session,
			cancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["name"] = "theming"
			});

		// Assert
		response.Success.Should().BeTrue(
			because: "theming is a registered guidance name");
		response.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article payload");
		response.Article!.Uri.Should().Be("docs://mcp/guides/theming",
			because: "the canonical resource URI should still be visible in the tool response");
		response.Article.Text.Should().NotContain("@creatio/theming",
			because: "the npm package is retired — theme CSS is built by the native build-theme tool");
		response.Article.Text.Should().Contain("push-workspace",
			because: "the theming guide must route deployment through push-workspace");
		response.Article.Text.Should().Contain("create-theme",
			because: "the theming guide must route the no-code/server flow to the create-theme MCP tool");
		response.Article.Text.Should().NotContain("-by-environment",
			because: "the theming guide must reference the theming tools by their single clean names");
		response.Article.Text.Should().NotContain("-by-credentials",
			because: "the theming guide must reference the theming tools by their single clean names");
		response.Article.Text.Should().Contain("build-theme",
			because: "the no-code/server flow's primary path is the native build-theme tool, not hand-computed colors");
		response.Article.Text.Should().Contain("get-guidance name=branding",
			because: "the theming guide must route branding-beyond-the-theme (logos, shell background) to the dedicated branding guide");
		response.Article.Text.Should().NotContain("CrtBackgroundConfig",
			because: "the branding mechanics moved to the dedicated branding guide (ENG-92981) and must not be duplicated in the theming guide");
	}

	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("get-guidance returns the canonical branding guide")]
	[Description("Verifies get-guidance returns the branding article that carries the logo sys-setting slots, routes the shell-background flow through the dedicated upload-image and set-background-image tools, and routes the theme part of branding to the theming guide (ENG-92981).")]
	public async Task GuidanceGet_Should_Return_Branding_Guide() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);

		// Act
		GuidanceGetResponse response = await CallAsync(
			session,
			cancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["name"] = "branding"
			});

		// Assert
		response.Success.Should().BeTrue(
			because: "branding is a registered guidance name");
		response.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article payload");
		response.Article!.Uri.Should().Be("docs://mcp/guides/branding",
			because: "the canonical resource URI should still be visible in the tool response");
		response.Article.Text.Should().Contain("get-guidance name=theming",
			because: "the branding guide must route the theme part of branding to the theming guide instead of restating it");
		response.Article.Text.Should().Contain("CrtAppToolbarLogo",
			because: "the logos section must map the Freedom UI top-panel logo slot to its Binary sys setting");
		response.Article.Text.Should().Contain("upload-image",
			because: "the shell-background upload must route through the dedicated upload-image tool");
		response.Article.Text.Should().NotContain("ImageAPIService",
			because: "the raw image-API recipe is owned by the upload-image tool implementation and must not be hand-executed from the guide");
		response.Article.Text.Should().Contain("set-background-image",
			because: "the shell-background activation must route through the dedicated set-background-image tool");
		response.Article.Text.Should().NotContain("CrtBackgroundConfig",
			because: "the background-configuration mechanics are owned by the set-background-image tool implementation, not hand-executed from the guide");
	}

	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("get-guidance hides process-modeling while the process-designer feature is off and treats the removed run-process-button guide as unknown")]
	[Description("Verifies that with the default (process-designer disabled) configuration the always-on get-guidance tool treats process-modeling as an unknown guide and omits it from availableGuides, while ungated guides stay advertised. Also pins the ENG-93187 removal of the standalone run-process-button guide (removed with no alias, so it resolves as unknown and is no longer advertised); its successor guide (when-to-use-requests) ships always-on and is covered by RequestInfoToolE2ETests.")]
	public async Task GuidanceGet_Should_Hide_ProcessModeling_And_Treat_RemovedRunProcessButton_As_Unknown_When_Feature_Disabled() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		GuidanceGetResponse processModeling = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["name"] = "process-modeling"
			});
		GuidanceGetResponse runProcessButton = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["name"] = "run-process-button"
			});

		// Assert
		processModeling.Success.Should().BeFalse(
			because: "process-modeling is gated behind the disabled process-designer feature and must resolve as unknown");
		processModeling.Article.Should().BeNull(
			because: "a disabled gated guide must not return its article over the real MCP transport");
		processModeling.AvailableGuides.Should().NotContain("process-modeling",
			because: "the disabled process-modeling guide must not be advertised in availableGuides");
		processModeling.AvailableGuides.Should().Contain("page-schema-handlers",
			because: "ungated guides must stay advertised while the process-designer feature is off");
		processModeling.AvailableGuides.Should().NotContain("run-process-button",
			because: "the standalone run-process-button guide was removed under ENG-93187 with no alias and must no longer be advertised in availableGuides");
		runProcessButton.Success.Should().BeFalse(
			because: "the standalone run-process-button guide was removed under ENG-93187 with no alias and must now resolve as an unknown guidance name");
		runProcessButton.Article.Should().BeNull(
			because: "an unknown guidance name must not return an article over the real MCP transport");
	}

	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("get-guidance returns the core-rules guide that the server instructions mandate reading first")]
	[Description("Verifies get-guidance returns the core-rules guide over the real stdio MCP path and that it carries the non-negotiable invariants the always-on instructions now point at instead of inlining.")]
	public async Task GuidanceGet_Should_Return_Core_Rules_Guide() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		GuidanceGetResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["name"] = "core-rules"
			});

		// Assert
		response.Success.Should().BeTrue(
			because: "core-rules is a registered guidance name");
		response.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article payload");
		response.Article!.Uri.Should().Be("docs://mcp/guides/core-rules",
			because: "the canonical resource URI for the core-rules guide should be stable");
		response.Article.Text.Should().Contain("compile-creatio is NOT needed",
			because: "the core-rules guide must carry the non-negotiable invariants");
	}

	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("get-guidance returns the routing map that the server instructions mandate reading first")]
	[Description("Verifies get-guidance returns the routing guide over the real stdio MCP path and that it carries the domain routing table (the table the always-on instructions now point at instead of inlining).")]
	public async Task GuidanceGet_Should_Return_Routing_Guide() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		GuidanceGetResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["name"] = "routing"
			});

		// Assert
		response.Success.Should().BeTrue(
			because: "routing is a registered guidance name");
		response.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article payload");
		response.Article!.Uri.Should().Be("docs://mcp/guides/routing",
			because: "the canonical resource URI for the routing guide should be stable");
		response.Article.Text.Should().Contain("name=page-modification",
			because: "the routing map must carry the domain routing table that points at the matching guides");
	}

	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("get-guidance returns the canonical server-to-server OAuth guidance article")]
	[Description("Verifies that get-guidance returns the OAuth client-credentials article for outside callers that need to mint and use bearer tokens.")]
	public async Task GuidanceGet_Should_Return_Server_To_Server_OAuth_Guide() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		GuidanceGetResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["name"] = "server-to-server-oauth"
			});

		// Assert
		response.Success.Should().BeTrue(
			because: "server-to-server-oauth is a registered guidance name");
		response.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article payload");
		response.Article!.Uri.Should().Be("docs://mcp/guides/server-to-server-oauth",
			because: "the canonical resource URI should still be visible in the tool response");
		response.Article.Text.Should().Contain("/connect/token",
			because: "the guidance tool should return the token minting instructions");
		response.Article.Text.Should().Contain("mint a new token",
			because: "the guidance tool should return the no-refresh-token expiry recovery instruction");
	}

	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("get-guidance returns the virtual entity lifecycle guide")]
	[Description("Verifies get-guidance returns virtual-entities with schema-before-executor and Creatio 10.0 virtual-write prerequisites.")]
	public async Task GuidanceGet_ShouldReturnVirtualEntitiesGuide_WhenStableNameIsRequested() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		GuidanceGetResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> { ["name"] = "virtual-entities" });

		// Assert
		response.Success.Should().BeTrue(
			because: "virtual-entities should be registered in the guidance catalog");
		response.Article.Should().NotBeNull(
			because: "a successful lookup should return the resolved virtual entity article");
		response.Article!.Uri.Should().Be("docs://mcp/guides/virtual-entities",
			because: "the stable guidance name should preserve the virtual-entities resource URI");
		response.Article.Text.Should().Contain("virtual entity schema MUST already exist",
			because: "tool-based retrieval should preserve the schema-before-executor gate");
		response.Article.Text.Should().Contain("virtual writes require Creatio 10.0 or later",
			because: "tool-based retrieval must preserve the hard virtual-write version boundary");
		response.Article.Text.Should().Contain("Creatio 8.3.4 or earlier",
			because: "tool-based retrieval should state the unsupported release boundary explicitly");
		response.Article.Text.Should().Contain("EnableVirtualEntitySupport",
			because: "tool-based retrieval should require the virtual CRUD feature on supported versions");
		response.Article.Text.Should().Contain("record/tenant scope",
			because: "tool-based retrieval should preserve the provider authorization boundary");
		response.Article.Text.Should().Contain("maximum page size",
			because: "tool-based retrieval should preserve bounded provider execution");
		response.Article.Text.Should().Contain("clio set-feature EnableVirtualEntitySupport 1 -e <environment>",
			because: "tool-based retrieval should preserve the executable feature-enablement fallback");
	}

	private static async Task<GuidanceGetResponse> CallAsync(
		McpServerSession session,
		CancellationToken cancellationToken,
		IReadOnlyDictionary<string, object?> arguments) {
		CallToolResult callResult = await session.CallToolAsync(
			GuidanceGetTool.ToolName,
			new Dictionary<string, object?> { ["args"] = arguments },
			cancellationToken);
		callResult.IsError.Should().NotBeTrue(
			because: "get-guidance should return a normal MCP tool result envelope for valid request shapes");
		return EntitySchemaStructuredResultParser.Extract<GuidanceGetResponse>(callResult);
	}

}
