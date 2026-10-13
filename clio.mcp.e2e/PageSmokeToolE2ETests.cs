using System.Text.Json;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Creatio;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// ENG-103032: page-smoke opens a Freedom UI form page headless and reports its buttons, fields and console errors,
/// and can click one button - against the configured sandbox stand, on an agent with a Chromium-based browser.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(PageSmokeTool.ToolName)]
[NonParallelizable]
public sealed class PageSmokeToolE2ETests : McpContractFixtureBase {

	private const string ToolName = PageSmokeTool.ToolName;

	// A form page every Creatio product ships; add mode needs no record, so the read-only check changes nothing.
	private const string ContactFormPage = "Contacts_FormPage";

	[Test]
	[Category("McpE2E.NoEnvironment")]
	[Description("page-smoke is discoverable through the get-tool-contract index and advertised as non-destructive, so it is reached through clio-run.")]
	[AllureTag(ToolName)]
	[AllureName("page-smoke is discoverable and non-destructive")]
	public async Task PageSmoke_Should_Be_Discoverable_And_Not_Destructive() {
		// Arrange
		await using ArrangeContext arrange = Arrange(TimeSpan.FromMinutes(5));

		// Act
		IReadOnlyList<ToolContractIndexEntry> index =
			await arrange.Session.GetToolContractIndexAsync(arrange.CancellationTokenSource.Token);
		ToolContractIndexEntry? entry = index.FirstOrDefault(item => item.Name == ToolName);

		// Assert
		entry.Should().NotBeNull(because: "a long-tail tool must be in the index to be found at all");
		entry!.Destructive.Should().BeFalse(because: "it never deletes; its opt-in click is flagged as a write instead");
	}

	[Test]
	[Category("McpE2E.NoEnvironment")]
	[Description("An argument outside the contract is refused through clio-run before any environment or browser is touched.")]
	[AllureTag(ToolName)]
	[AllureName("page-smoke refuses an invalid page name")]
	public async Task PageSmoke_Should_Refuse_An_Invalid_Page_Name() {
		// Arrange
		await using ArrangeContext arrange = Arrange(TimeSpan.FromMinutes(5));

		// Act
		JsonElement response = await RunAsync(arrange, new Dictionary<string, object?> {
			["environment-name"] = "no-such-environment",
			["page"] = "not a page"
		});

		// Assert
		response.GetProperty("success").GetBoolean().Should().BeFalse(because: "the page name is not a schema name");
		response.GetProperty("error").GetString().Should().Contain("page must be a Freedom UI form page schema name",
			because: "the refusal names the rule");
	}

	// A page with the Freedom UI DOM contract page-smoke reads (crt-root, the page outlet, named crt-* controls, a hidden
	// loading mask). Its Run button navigates, logs a console error and throws, so a click has something to report.
	private const string StubShellPage = """
		<!doctype html>
		<html><head><title>Stub Shell</title></head><body>
		<crt-root>
		  <crt-navigation-schema-outlet>
		    <crt-label element-name="PageTitle">Stub order</crt-label>
		    <crt-button element-name="SaveButton"><button>Save</button></crt-button>
		    <crt-button element-name="RunButton"><button>Run check</button></crt-button>
		    <crt-input element-name="Name"><label>Name</label><input value="Order 1" required></crt-input>
		  </crt-navigation-schema-outlet>
		  <crt-mask><div class="crt-mask-wrap"><div class="crt-mask-container" style="display:none"></div></div></crt-mask>
		</crt-root>
		<script>
		  document.querySelector('[element-name=RunButton] button').addEventListener('click', () => {
		    location.hash = '#Section/Orders_ListPage';
		    console.error('stub handler failed');
		    setTimeout(() => { throw new Error('stub async failure'); }, 10);
		  });
		</script>
		</body></html>
		""";

	[Test]
	[Category("McpE2E.NoEnvironment")]
	[Description("Through clio-run, page-smoke logs in to a loopback Creatio stub, injects the session into a headless Chromium (the stub serves the Shell only with the session cookie), reads the page, clicks a button and reports the navigation and the errors that followed - with no cookie in the reply.")]
	[AllureTag(ToolName)]
	[AllureName("page-smoke reads and clicks a page served by a Creatio stub")]
	public async Task PageSmoke_Should_Read_And_Click_A_Stub_Page_End_To_End() {
		await StubEnvironmentStand.RunAsync(
			"clio-page-smoke-e2e",
			new RuntimeDetectionStubServerConfiguration(
				NetCoreHealthEnabled: false,
				NetFrameworkHealthEnabled: true,
				NetCoreServiceEnabled: false,
				NetFrameworkServiceEnabled: true,
				NetFrameworkUiMarkerEnabled: true,
				ShellPageHtml: StubShellPage),
			async (session, environmentName, _, cancellationToken) => {
				// Act
				CallToolResult result = await session.CallToolAsync(ClioRunTool.ToolName,
					new Dictionary<string, object?> {
						["command"] = ToolName,
						["args"] = new Dictionary<string, object?> {
							["environment-name"] = environmentName,
							["page"] = "Orders_FormPage",
							["timeout"] = 20,
							["click"] = "run check",
							["click-wait"] = 3
						}
					},
					cancellationToken);
				string text = result.Content.OfType<TextContentBlock>().First().Text;
				JsonElement response = JsonDocument.Parse(text).RootElement.Clone();
				IgnoreWithoutBrowser(response);

				// Assert
				response.GetProperty("success").GetBoolean().Should().BeTrue(because: text);
				response.GetProperty("ready").GetBoolean().Should().BeTrue(
					because: "the stub served the page, so the session cookie reached the browser");
				response.GetProperty("url").GetString().Should().Be("/0/Shell/#Card/Orders_FormPage/add",
					because: "add mode is opened and the address is relative to the environment");
				response.GetProperty("title").GetString().Should().Be("Stub order", because: "the PageTitle element is the title");
				response.GetProperty("buttons").EnumerateArray().Select(button => button.GetProperty("name").GetString())
					.Should().Equal(["SaveButton", "RunButton"], because: "both visible buttons are listed");
				response.GetProperty("fields").EnumerateArray().Single().GetProperty("required").GetBoolean()
					.Should().BeTrue(because: "the Name field is required");
				JsonElement click = response.GetProperty("click");
				click.GetProperty("outcome").GetString().Should().Be("clicked", because: "the caption matched");
				click.GetProperty("navigations").EnumerateArray().Select(item => item.GetString())
					.Should().Contain("/0/Shell/#Section/Orders_ListPage", because: "the click navigated");
				click.GetProperty("consoleErrors").EnumerateArray().Select(item => item.GetProperty("text").GetString())
					.Should().Contain(error => error!.Contains("stub handler failed"), because: "the console error is collected")
					.And.Contain(error => error!.Contains("stub async failure"), because: "the uncaught exception is collected");
				text.Should().NotContain("stub-auth", because: "a session cookie value never reaches the reply");
				text.Should().NotContain("stub-csrf", because: "a session cookie value never reaches the reply");
			},
			TimeSpan.FromMinutes(3));
	}

	[Test]
	[Category("McpE2E.Sandbox")]
	[Description("A Freedom UI form page in add mode opens ready, with its visible buttons and fields listed and no cookie in the reply.")]
	[AllureTag(ToolName)]
	[AllureName("page-smoke reads a Freedom UI form page")]
	public async Task PageSmoke_Should_Read_A_Form_Page() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ReachableSandboxEnvironment.ResolveConfiguredOrIgnoreAsync(settings,
			"page-smoke MCP E2E requires the configured sandbox environment to be set and reachable.");
		await using ArrangeContext arrange = Arrange(TimeSpan.FromMinutes(5));

		// Act
		JsonElement response = await RunAsync(arrange, new Dictionary<string, object?> {
			["environment-name"] = environmentName,
			["page"] = ContactFormPage,
			["timeout"] = 45
		});
		IgnoreWithoutBrowser(response);

		// Assert
		response.GetProperty("success").GetBoolean().Should().BeTrue(because: response.ToString());
		response.GetProperty("ready").GetBoolean().Should().BeTrue(because: "the page reached the ready state");
		response.GetProperty("mode").GetString().Should().Be("add", because: "no record id was passed");
		response.GetProperty("buttons").GetArrayLength().Should().BeGreaterThan(0, because: "a form page has buttons");
		response.GetProperty("fields").GetArrayLength().Should().BeGreaterThan(0, because: "a contact form has fields");
		response.GetProperty("consoleErrors").ValueKind.Should().Be(JsonValueKind.Array,
			because: "console errors are always answered");
		response.ToString().Should().NotContain("BPMCSRF", because: "no cookie reaches the reply");
		response.ToString().Length.Should().BeLessThan(20_000, because: "the reply stays under the spill line");
	}

	[Test]
	[Category("McpE2E.Sandbox")]
	[Description("Clicking the Close button of an untouched add page is reported as clicked, with what followed; nothing is saved.")]
	[AllureTag(ToolName)]
	[AllureName("page-smoke clicks a button and reports what followed")]
	public async Task PageSmoke_Should_Click_A_Button_And_Report_What_Followed() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ReachableSandboxEnvironment.ResolveConfiguredOrIgnoreAsync(settings,
			"page-smoke MCP E2E requires the configured sandbox environment to be set and reachable.");
		await using ArrangeContext arrange = Arrange(TimeSpan.FromMinutes(5));

		// Act
		JsonElement response = await RunAsync(arrange, new Dictionary<string, object?> {
			["environment-name"] = environmentName,
			["page"] = ContactFormPage,
			["timeout"] = 45,
			["click"] = "CloseButton"
		});
		IgnoreWithoutBrowser(response);

		// Assert
		response.GetProperty("success").GetBoolean().Should().BeTrue(because: response.ToString());
		JsonElement click = response.GetProperty("click");
		click.GetProperty("outcome").GetString().Should().Be("clicked", because: "the page has one Close button");
		click.GetProperty("name").GetString().Should().Be("CloseButton", because: "the element name matched");
		click.TryGetProperty("settled", out JsonElement settled).Should().BeTrue(because: "the settle state is reported");
		settled.ValueKind.Should().BeOneOf([JsonValueKind.True, JsonValueKind.False], because: "it is a boolean");
	}

	private static void IgnoreWithoutBrowser(JsonElement response) {
		if (response.TryGetProperty("error", out JsonElement error)
			&& error.GetString()?.Contains("Chromium", StringComparison.OrdinalIgnoreCase) == true) {
			Assert.Ignore("page-smoke needs a Chromium-based browser on the agent: " + error.GetString());
		}
	}

	private static async Task<JsonElement> RunAsync(ArrangeContext arrange, Dictionary<string, object?> args) {
		CallToolResult result = await arrange.Session.CallToolAsync(ClioRunTool.ToolName,
			new Dictionary<string, object?> { ["command"] = ToolName, ["args"] = args },
			arrange.CancellationTokenSource.Token);
		string text = result.Content.OfType<TextContentBlock>().First().Text;
		return JsonDocument.Parse(text).RootElement.Clone();
	}
}
