using System.Text.Json;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Common.BrowserSession;
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

	// A form page every Creatio product ships (CrtUIv2, Studio included); add mode needs no record, so the read-only check changes nothing.
	private const string TaskFormPage = "Tasks_FormPage";

	[Test]
	[Category("McpE2E.NoEnvironment")]
	[Description("page-smoke is discoverable through the get-tool-contract index and advertised as destructive, because its click can delete or change records.")]
	[AllureTag(ToolName)]
	[AllureName("page-smoke is discoverable and destructive")]
	public async Task PageSmoke_Should_Be_Discoverable_And_Destructive() {
		// Arrange
		await using ArrangeContext arrange = Arrange(TimeSpan.FromMinutes(5));

		// Act
		IReadOnlyList<ToolContractIndexEntry> index =
			await arrange.Session.GetToolContractIndexAsync(arrange.CancellationTokenSource.Token);
		ToolContractIndexEntry? entry = index.FirstOrDefault(item => item.Name == ToolName);

		// Assert
		entry.Should().NotBeNull(because: "a long-tail tool must be in the index to be found at all");
		entry!.Destructive.Should().BeTrue(because: "its opt-in click can press Delete, so a host must confirm the call");
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
	[Description("Through clio-run, page-smoke runs in a worker child (observed in the host's worker registry), logs in to a loopback Creatio stub, injects the session into a headless Chromium (the stub serves the Shell only with the session cookie), reads the page, clicks a button and reports the navigation and the errors that followed - with no cookie in the reply, and with the browser's own output kept out of the JSON-RPC stream.")]
	[AllureTag(ToolName)]
	[AllureName("page-smoke runs in a worker, and reads and clicks a page served by a Creatio stub")]
	public async Task PageSmoke_Should_Read_And_Click_A_Stub_Page_End_To_End() {
		await StubEnvironmentStand.RunWithClioHomeAsync(
			"clio-page-smoke-e2e",
			new RuntimeDetectionStubServerConfiguration(
				NetCoreHealthEnabled: false,
				NetFrameworkHealthEnabled: true,
				NetCoreServiceEnabled: false,
				NetFrameworkServiceEnabled: true,
				NetFrameworkUiMarkerEnabled: true,
				ShellPageHtml: StubShellPage),
			async (session, environmentName, _, clioHome, cancellationToken) => {
				// Act
				await using WorkerSpawnObserver workers = WorkerSpawnObserver.Start(clioHome);
				Dictionary<string, object?> args = new() {
					["environment-name"] = environmentName,
					["page"] = "Orders_FormPage",
					["timeout"] = 20,
					["click"] = "run check",
					["click-wait"] = 3
				};
				await AnswerTheBrowserQuestionAsync(args, clioHome, probe => CallAsync(session, probe, cancellationToken));
				CallToolResult result = await session.CallToolAsync(ClioRunTool.ToolName,
					new Dictionary<string, object?> { ["command"] = ToolName, ["args"] = args },
					cancellationToken);
				string text = result.Content.OfType<TextContentBlock>().First().Text;
				JsonElement response = JsonDocument.Parse(text).RootElement.Clone();
				TestContext.Progress.WriteLine($"page-smoke reply: {System.Text.Encoding.UTF8.GetByteCount(text)} bytes");

				// Assert
				workers.ReadFailures.Should().BeEmpty(because: "a failed registry read would make the worker observation meaningless");
				workers.Observed.Should().NotBeEmpty(
					because: "page-smoke is a worker-cohort member: a browser started in the host would inherit its JSON-RPC "
						+ "stdout and outlive a killed client, so the call must be relayed to a worker child");
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
				response.TryGetProperty("buttonsOmitted", out JsonElement _).Should().BeFalse(because: "nothing is over the cap");
				System.Text.Encoding.UTF8.GetByteCount(text).Should().BeLessThan(14_000, because: "the reply stays bounded");
				text.Should().NotContain("stub-auth", because: "a session cookie value never reaches the reply");
				text.Should().NotContain("stub-csrf", because: "a session cookie value never reaches the reply");
			},
			TimeSpan.FromMinutes(3));
	}

	[Test]
	[Category("McpE2E.NoEnvironment")]
	[Description("ENG-103287: with no browser decision, page-smoke (in a worker, through clio-run) refuses with browser-consent-required and the question with its choices, without touching the environment; the user's 'no' is then remembered across calls.")]
	[AllureTag(ToolName)]
	[AllureName("page-smoke asks before it uses or downloads a browser, and remembers no")]
	public async Task PageSmoke_Should_Ask_For_A_Browser_And_Remember_No() {
		if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CHROME_PATH"))) {
			Assert.Ignore("CHROME_PATH is set on this agent, so page-smoke uses it without asking.");
		}
		await StubEnvironmentStand.RunWithClioHomeAsync(
			"clio-page-smoke-consent-e2e",
			new RuntimeDetectionStubServerConfiguration(
				NetCoreHealthEnabled: false,
				NetFrameworkHealthEnabled: true,
				NetCoreServiceEnabled: false,
				NetFrameworkServiceEnabled: true),
			async (session, environmentName, _, clioHome, cancellationToken) => {
				// Act
				await using WorkerSpawnObserver workers = WorkerSpawnObserver.Start(clioHome);
				Dictionary<string, object?> args = new() { ["environment-name"] = environmentName, ["page"] = "Orders_FormPage" };
				JsonElement asked = await CallAsync(session, args, cancellationToken);
				JsonElement declined = await CallAsync(session, new(args) { ["browser"] = "none" }, cancellationToken);
				JsonElement remembered = await CallAsync(session, args, cancellationToken);

				// Assert
				workers.Observed.Should().NotBeEmpty(because: "the question is answered by the worker that would run the browser");
				asked.TryGetProperty("success", out JsonElement _).Should().BeFalse(
					because: "a question is not a failure, so clio-run does not redact the browser paths the user must see");
				asked.GetProperty("code").GetString().Should().Be("browser-consent-required", because: asked.ToString());
				asked.GetProperty("message").GetString().Should().Contain("verbatim",
					because: "the agent is told to relay the question, not to answer it");
				JsonElement consent = asked.GetProperty("browserConsent");
				consent.GetProperty("question").GetString().Should().Contain("Which should it use?",
					because: "the question is worded for the user");
				consent.GetProperty("choices").EnumerateArray().Select(choice => choice.GetProperty("browser").GetString())
					.Should().EndWith("none", because: "no is always a choice")
					.And.Contain("download", because: "a download is offered on every platform with a pinned build");
				declined.GetProperty("code").GetString().Should().Be("browser-declined", because: declined.ToString());
				remembered.GetProperty("code").GetString().Should().Be("browser-declined",
					because: "an explicit no is remembered in clio settings, so the user is not asked again");
				File.ReadAllText(Path.Combine(clioHome, "appsettings.json")).Should().Contain("page-smoke-browser",
					because: "the answer is kept in the user's clio settings");
			},
			TimeSpan.FromMinutes(3));
	}

	[Test]
	[Explicit("Downloads about 100-125 MB from Google's Chrome for Testing.")]
	[Category("McpE2E.Download")]
	[Description("ENG-103287: on 'download', page-smoke downloads the pinned chrome-headless-shell into the user's clio home (answering browser-ready without running the check), and the next call runs the check with it in a worker.")]
	[AllureTag(ToolName)]
	[AllureName("page-smoke downloads the headless shell on yes and checks a page with it")]
	public async Task PageSmoke_Should_Download_The_Headless_Shell_And_Check_With_It() {
		await StubEnvironmentStand.RunWithClioHomeAsync(
			"clio-page-smoke-download-e2e",
			new RuntimeDetectionStubServerConfiguration(
				NetCoreHealthEnabled: false,
				NetFrameworkHealthEnabled: true,
				NetCoreServiceEnabled: false,
				NetFrameworkServiceEnabled: true,
				NetFrameworkUiMarkerEnabled: true,
				ShellPageHtml: StubShellPage),
			async (session, environmentName, _, clioHome, cancellationToken) => {
				// Act
				Dictionary<string, object?> args = new() { ["environment-name"] = environmentName, ["page"] = "Orders_FormPage" };
				JsonElement downloaded = await CallAsync(session, new(args) { ["browser"] = "download" }, cancellationToken);
				JsonElement checkedPage = await CallAsync(session, args, cancellationToken);

				// Assert
				downloaded.GetProperty("code").GetString().Should().Be("browser-ready", because: downloaded.ToString());
				Directory.GetDirectories(Path.Combine(clioHome, "browsers")).Should().ContainSingle(
					because: "the pinned version is installed once into the user's clio home");
				checkedPage.GetProperty("success").GetBoolean().Should().BeTrue(because: checkedPage.ToString());
				checkedPage.GetProperty("title").GetString().Should().Be("Stub order", because: "the downloaded shell read the page");
			},
			TimeSpan.FromMinutes(4));
	}

	private static async Task<JsonElement> CallAsync(McpServerSession session, Dictionary<string, object?> args,
		CancellationToken cancellationToken) {
		CallToolResult result = await session.CallToolAsync(ClioRunTool.ToolName,
			new Dictionary<string, object?> { ["command"] = ToolName, ["args"] = args }, cancellationToken);
		return JsonDocument.Parse(result.Content.OfType<TextContentBlock>().First().Text).RootElement.Clone();
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
		Dictionary<string, object?> args = new() {
			["environment-name"] = environmentName,
			["page"] = TaskFormPage,
			["timeout"] = 45
		};
		await AnswerTheBrowserQuestionAsync(args, null, probe => RunAsync(arrange, probe));

		// Act
		JsonElement response = await RunAsync(arrange, args);

		// Assert
		response.GetProperty("success").GetBoolean().Should().BeTrue(because: response.ToString());
		response.GetProperty("ready").GetBoolean().Should().BeTrue(because: "the page reached the ready state");
		response.GetProperty("mode").GetString().Should().Be("add", because: "no record id was passed");
		response.GetProperty("buttons").GetArrayLength().Should().BeGreaterThan(0, because: "a form page has buttons");
		response.GetProperty("fields").GetArrayLength().Should().BeGreaterThan(0, because: "a task form has fields");
		response.GetProperty("consoleErrors").ValueKind.Should().Be(JsonValueKind.Array,
			because: "console errors are always answered");
		response.ToString().Should().NotContain("BPMCSRF", because: "no cookie reaches the reply");
		response.ToString().Length.Should().BeLessThan(20_000, because: "the reply stays under the spill line");
	}

	[Test]
	[Category("McpE2E.Sandbox")]
	[Description("Clicking the Back button of an untouched add page is reported as clicked, with what followed; nothing is saved.")]
	[AllureTag(ToolName)]
	[AllureName("page-smoke clicks a button and reports what followed")]
	public async Task PageSmoke_Should_Click_A_Button_And_Report_What_Followed() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ReachableSandboxEnvironment.ResolveConfiguredOrIgnoreAsync(settings,
			"page-smoke MCP E2E requires the configured sandbox environment to be set and reachable.");
		await using ArrangeContext arrange = Arrange(TimeSpan.FromMinutes(5));
		Dictionary<string, object?> args = new() {
			["environment-name"] = environmentName,
			["page"] = TaskFormPage,
			["timeout"] = 45,
			["click"] = "BackButton"
		};
		await AnswerTheBrowserQuestionAsync(args, null, probe => RunAsync(arrange, probe));

		// Act
		JsonElement response = await RunAsync(arrange, args);

		// Assert
		response.GetProperty("success").GetBoolean().Should().BeTrue(because: response.ToString());
		JsonElement click = response.GetProperty("click");
		click.GetProperty("outcome").GetString().Should().Be("clicked", because: "the page has one Back button");
		click.GetProperty("name").GetString().Should().Be("BackButton", because: "the element name matched");
		click.TryGetProperty("settled", out JsonElement settled).Should().BeTrue(because: "the settle state is reported");
		settled.ValueKind.Should().BeOneOf([JsonValueKind.True, JsonValueKind.False], because: "it is a boolean");
	}

	// Plays the user who answers page-smoke's browser question with the first browser already on the machine, as the
	// question offers it (a cached headless shell first), and adds that answer to the call's arguments. The question is
	// asked by a read-only probe of the same page; with CHROME_PATH set it is not asked and nothing is added.
	//
	// When the WORKER offers no existing browser the test is ignored only if this test process, looking with the
	// environment the clio host was started with (the stub stand points HOME, or LOCALAPPDATA on Windows, at its own
	// folder), cannot find one either. Otherwise it fails: page-smoke runs in an MCP worker started with a cleared
	// environment, and "nothing found" there while the test process finds a browser is a worker-environment regression -
	// the Windows one of ENG-103286 hid behind the old guard.
	private static async Task AnswerTheBrowserQuestionAsync(Dictionary<string, object?> args, string? clioHostHome,
		Func<Dictionary<string, object?>, Task<JsonElement>> call) {
		Dictionary<string, object?> probe = new(args);
		probe.Remove("click");
		probe.Remove("click-wait");
		JsonElement answer = await call(probe);
		if (!answer.TryGetProperty("code", out JsonElement code)) {
			return;
		}
		code.GetString().Should().Be("browser-consent-required", because: answer.ToString());
		answer.ToString().Should().NotContain("[redacted-path]", because: "the user must see which browser they choose");
		string? offered = answer.GetProperty("browserConsent").GetProperty("choices").EnumerateArray()
			.Where(choice => choice.GetProperty("browser").GetString() == "existing")
			.Select(choice => choice.GetProperty("browser-path").GetString())
			.FirstOrDefault();
		if (offered is null) {
			string? seen = LocateBrowserAsTheHostSeesIt(clioHostHome);
			if (seen is not null) {
				Assert.Fail($"The test process finds a browser at '{seen}', but page-smoke in the clio worker offered none: "
					+ answer.GetProperty("browserConsent").GetProperty("question").GetString());
			}
			Assert.Ignore("page-smoke needs a Chromium-based browser on the agent, and none was found.");
		}
		TestContext.Progress.WriteLine($"page-smoke browser chosen as the user would: {offered}");
		args["browser"] = "existing";
		args["browser-path"] = offered;
	}

	private static string? LocateBrowserAsTheHostSeesIt(string? clioHostHome) {
		string homeVariable = OperatingSystem.IsWindows() ? "LOCALAPPDATA" : "HOME";
		Func<string, string?> readEnvironment = name => clioHostHome is not null && name == homeVariable
			? clioHostHome
			: Environment.GetEnvironmentVariable(name);
		System.Runtime.InteropServices.OSPlatform platform = OperatingSystem.IsWindows()
			? System.Runtime.InteropServices.OSPlatform.Windows
			: OperatingSystem.IsMacOS() ? System.Runtime.InteropServices.OSPlatform.OSX : System.Runtime.InteropServices.OSPlatform.Linux;
		System.IO.Abstractions.FileSystem fileSystem = new();
		ChromiumLocator installed = new(new Clio.Common.FileSystem(fileSystem), readEnvironment!, platform);
		BrowserCacheLocator cached = new(fileSystem, readEnvironment!, platform);
		return installed.ChromePathOverride()
			?? cached.FindCached().Select(found => found.Path).FirstOrDefault()
			?? installed.FindInstalled().Select(found => found.Path).FirstOrDefault();
	}

	private static async Task<JsonElement> RunAsync(ArrangeContext arrange, Dictionary<string, object?> args) {
		CallToolResult result = await arrange.Session.CallToolAsync(ClioRunTool.ToolName,
			new Dictionary<string, object?> { ["command"] = ToolName, ["args"] = args },
			arrange.CancellationTokenSource.Token);
		string text = result.Content.OfType<TextContentBlock>().First().Text;
		TestContext.Progress.WriteLine($"page-smoke reply: {System.Text.Encoding.UTF8.GetByteCount(text)} bytes");
		return JsonDocument.Parse(text).RootElement.Clone();
	}
}
