using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using Clio.Command.RuntimeVerification;
using Clio.Common;
using Clio.Common.BrowserSession;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>ENG-103032: page-smoke's argument contract, its mapping to the runner, and its curated contract.</summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class PageSmokeToolTests {

	private const string RecordId = "8D6A5C4E-0F1B-4D6F-9D0A-2B9C1E7F3A10";

	// The retry rule d-krestov asked for on #1812, word for word in both the description and the contract.
	private const string SuccessFalseClause = "success=false: the page did not open, or the click was not made "
		+ "(click.outcome says which); with click.outcome=unknown the button may have been pressed, so read "
		+ "click.created before retrying";

	private const string BrowserExecutable = "/opt/chrome";

	private static IPageSmokeBrowserResolver ReadyBrowser() {
		IPageSmokeBrowserResolver browser = Substitute.For<IPageSmokeBrowserResolver>();
		browser.ResolveAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(new PageSmokeBrowserResolution(BrowserExecutable, null, null, null));
		return browser;
	}

	private static PageSmokeArgs Args(string page = "UsrOrders_FormPage", string recordId = null, int? timeout = null,
		string click = null, int? clickWait = null, string[] watch = null,
		Dictionary<string, JsonElement> extension = null) => new() {
		EnvironmentName = "dev",
		Page = page,
		RecordId = recordId,
		Timeout = timeout,
		Click = click,
		ClickWait = clickWait,
		Watch = watch,
		ExtensionData = extension
	};

	[TestCase("bad page", null, null, null, null, null, "page must be a Freedom UI form page schema name")]
	[TestCase("UsrOrders_FormPage", "not-a-guid", null, null, null, null, "record-id must be a GUID")]
	[TestCase("UsrOrders_FormPage", null, 2, null, null, null, "timeout must be between 5 and 45")]
	[TestCase("UsrOrders_FormPage", null, 46, null, null, null, "timeout must be between 5 and 45")]
	[TestCase("UsrOrders_FormPage", null, null, "  ", null, null, "click must be a button caption")]
	[TestCase("UsrOrders_FormPage", null, null, null, 5, null, "click-wait applies only with click")]
	[TestCase("UsrOrders_FormPage", null, null, "Save", 21, null, "click-wait must be between 1 and 20")]
	[TestCase("UsrOrders_FormPage", null, null, null, null, "UsrOrders", "watch applies only with click")]
	[TestCase("UsrOrders_FormPage", null, null, "Save", null, "Usr Orders", "watch takes 1-5 entity schema names")]
	[Description("Arguments outside the contract are refused with a message naming the rule.")]
	public void Validate_Should_Refuse_Arguments_Outside_The_Contract(string page, string recordId, int? timeout,
		string click, int? clickWait, string watch, string expected) {
		// Arrange
		PageSmokeArgs args = Args(page, recordId, timeout, click, clickWait, watch is null ? null : [watch]);

		// Act
		string error = PageSmokeTool.Validate(args);

		// Assert
		error.Should().Contain(expected, because: "the argument is outside the contract");
	}

	[Test]
	[Description("A valid read-only call and a valid click call pass validation.")]
	public void Validate_Should_Accept_Valid_Calls() {
		// Arrange
		PageSmokeArgs readOnly = Args(recordId: RecordId, timeout: 45);
		PageSmokeArgs click = Args(click: "Save", clickWait: 20, watch: ["UsrOrders"]);

		// Act
		string readOnlyError = PageSmokeTool.Validate(readOnly);
		string clickError = PageSmokeTool.Validate(click);

		// Assert
		readOnlyError.Should().BeNull(because: "every argument is inside the contract");
		clickError.Should().BeNull(because: "a click with its wait and watch is inside the contract");
	}

	[Test]
	[Description("A predictable wrong spelling is answered with a rename, and the runner is never resolved.")]
	public async Task Run_Should_Answer_An_Alias_With_A_Rename() {
		// Arrange
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		PageSmokeTool tool = new(resolver, new OperationCorrelationIdProvider(), ReadyBrowser());
		Dictionary<string, JsonElement> extension = new() { ["schema-name"] = JsonSerializer.SerializeToElement("X") };

		// Act
		PageSmokeResponse response = await tool.Run(Args(extension: extension));

		// Assert
		response.Success.Should().BeFalse(because: "an unknown argument is refused");
		response.Error.Should().Contain("'schema-name' -> 'page'", because: "the caller is told the right name");
		response.CorrelationId.Should().NotBeNullOrWhiteSpace(because: "every reply carries a correlation id");
		resolver.DidNotReceiveWithAnyArgs().Resolve<IPageSmokeRunner>(default(EnvironmentOptions));
	}

	[Test]
	[Description("A valid call resolves the runner for the named environment and passes the normalized request, applying the defaults.")]
	public async Task Run_Should_Map_The_Arguments_To_The_Runner() {
		// Arrange
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		IPageSmokeRunner runner = Substitute.For<IPageSmokeRunner>();
		PageSmokeRequest seen = null;
		runner.RunAsync(Arg.Do<PageSmokeRequest>(request => seen = request), Arg.Any<CancellationToken>())
			.Returns(new PageSmokeResult { Success = true, Ready = true, Mode = "edit" });
		resolver.Resolve<IPageSmokeRunner>(Arg.Is<EnvironmentOptions>(options => options.Environment == "dev")).Returns(runner);
		PageSmokeTool tool = new(resolver, new OperationCorrelationIdProvider(), ReadyBrowser());

		// Act
		PageSmokeResponse response = await tool.Run(Args(page: " UsrOrders_FormPage ", recordId: RecordId, click: " Save "));

		// Assert
		response.Success.Should().BeTrue(because: "the runner succeeded");
		response.Warnings.Should().BeNull(because: "an empty warning list is omitted");
		seen.Page.Should().Be("UsrOrders_FormPage", because: "the page name is trimmed");
		seen.RecordId.Should().Be(RecordId.ToLowerInvariant(), because: "the record id is normalized");
		seen.Click.Should().Be("Save", because: "the caption is trimmed");
		seen.TimeoutSeconds.Should().Be(PageSmokeRunner.DefaultTimeoutSeconds, because: "the default applies");
		seen.ClickWaitSeconds.Should().Be(PageSmokeRunner.DefaultClickWaitSeconds, because: "the default applies");
	}

	[Test]
	[Description("An unexpected failure is answered as a redacted error, not thrown.")]
	public async Task Run_Should_Redact_An_Unexpected_Failure() {
		// Arrange
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IPageSmokeRunner>(Arg.Any<EnvironmentOptions>())
			.Returns(_ => throw new InvalidOperationException("Unknown environment at http://user:pw@host:88/x"));
		PageSmokeTool tool = new(resolver, new OperationCorrelationIdProvider(), ReadyBrowser());

		// Act
		PageSmokeResponse response = await tool.Run(Args());

		// Assert
		response.Success.Should().BeFalse(because: "the runner could not be resolved");
		response.Error.Should().NotContain("pw@host", because: "credentials in a message are redacted");
	}

	[Test]
	[Description("page-smoke is advertised as a destructive write, like trigger-smoke, because its click can delete or change records; it is a worker-cohort member (so it really runs in a worker), shares the browser-session cache, is not resident, and says that success=false after a click may follow a press.")]
	public void Tool_Should_Be_A_Long_Tail_Destructive_Write() {
		// Arrange
		MethodInfo method = typeof(PageSmokeTool).GetMethod(nameof(PageSmokeTool.Run))!;

		// Act
		McpServerToolAttribute tool = method.GetCustomAttribute<McpServerToolAttribute>()!;
		McpToolExecutionAttribute execution = method.GetCustomAttribute<McpToolExecutionAttribute>()!;
		string description = method.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()!.Description;

		// Assert
		tool.Name.Should().Be("page-smoke", because: "the name is the contract");
		tool.ReadOnly.Should().BeFalse(because: "the opt-in click can write");
		tool.Destructive.Should().BeTrue(because: "click can press Delete or Save, and the annotation is the only signal an auto-approving host sees");
		execution.SharedFileResource.Should().Be(McpToolSharedFileResource.BrowserSessionCache,
			because: "it reads the cached browser session get-browser-session writes");
		description.Should().Contain("is a WRITE", because: "the click must be flagged as a write");
		description.Should().Contain(SuccessFalseClause,
			because: "an agent that reads success=false after a click as 'nothing happened' may press Save twice");
		execution.Location.Should().Be(McpToolExecutionLocation.Worker, because: "a headless browser never lives in the host");
		McpWorkerCohort.ShippedNames.Should().Contain("page-smoke",
			because: "the router relays only cohort members, so without membership the Worker declaration is not where it runs");
		McpCoreToolProfile.CoreToolTypes.Should().NotContain(typeof(PageSmokeTool),
			because: "a long-tail tool must not grow the resident tools/list");
	}

	[Test]
	[Description("The curated contract names the required fields, flags the click as a write, warns that success is not error-free, and states the success=false retry rule in its description and its success field.")]
	public void Contract_Should_Flag_The_Click_As_A_Write() {
		// Arrange
		ToolContractGetTool tool = new();

		// Act
		ToolContractDefinition contract = tool.GetToolContracts(new ToolContractGetArgs([PageSmokeTool.ToolName])).Tools!.Single();

		// Assert
		contract.InputSchema.Required.Should().Equal(["environment-name", "page"],
			because: "the contract lists exactly the arguments the tool refuses to run without");
		contract.InputSchema.Properties.Select(field => field.Name).Should().BeEquivalentTo(
			["environment-name", "page", "record-id", "timeout", "click", "click-wait", "watch", "browser", "browser-path"],
			because: "the contract fields are the tool's own arguments");
		contract.Description.Should().Contain("relay browserConsent.question to the user verbatim, never answer it for them",
			because: "the consent rule is stated where an agent reads the contract");
		contract.Description.Should().Contain("click is a WRITE", because: "the agent must know a click can create records");
		contract.Description.Should().Contain("never cleaned up", because: "the agent owns the cleanup of what a click created");
		contract.Description.Should().Contain(SuccessFalseClause,
			because: "the contract states the same retry rule as the tool description");
		contract.OutputContract.Fields.Single(field => field.Name == "success").Description
			.Should().Contain("click.outcome=unknown the button may have been pressed, so read click.created before retrying",
				because: "the success field itself carries the retry rule");
		contract.AntiPatterns!.Select(pattern => pattern.Pattern).Should().Contain(pattern => pattern.Contains("Playwright"),
			because: "the tool exists to replace hand-written Playwright scripts");
	}
	[TestCase("yes", null, "browser must be existing, download or none")]
	[TestCase("download", "/opt/chrome", "browser-path applies only with browser=existing")]
	[TestCase(null, "/opt/chrome", "browser-path applies only with browser=existing")]
	[TestCase("existing", null, "browser=existing needs browser-path")]
	[Description("A browser answer outside the three choices, a browser-path without browser=existing, or browser=existing without the chosen browser-path is refused before any browser is resolved.")]
	public void Validate_Should_Refuse_A_Browser_Answer_Outside_The_Choices(string browser, string browserPath, string expected) {
		// Arrange
		PageSmokeArgs args = Args() with { Browser = browser, BrowserPath = browserPath };

		// Act
		string error = PageSmokeTool.Validate(args);

		// Assert
		error.Should().Contain(expected, because: "only the user's answer to the question is accepted");
	}

	[Test]
	[Description("With no browser decision, the call is refused with code browser-consent-required and the question to relay, and no browser is started.")]
	public async Task Run_Should_Refuse_With_The_Browser_Question_When_The_User_Has_Not_Chosen() {
		// Arrange
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		IPageSmokeRunner runner = Substitute.For<IPageSmokeRunner>();
		resolver.Resolve<IPageSmokeRunner>(Arg.Any<EnvironmentOptions>()).Returns(runner);
		IPageSmokeBrowserResolver browser = Substitute.For<IPageSmokeBrowserResolver>();
		PageSmokeBrowserConsent consent = new() {
			Why = PageSmokeBrowserResolver.Why,
			Question = "Which should it use?",
			Choices = [new PageSmokeBrowserChoice { Browser = "none", Description = "No" }]
		};
		browser.ResolveAsync(null, null, Arg.Any<CancellationToken>()).Returns(new PageSmokeBrowserResolution(null,
			PageSmokeBrowserResolver.ConsentRequiredCode, "relay it", consent));
		PageSmokeTool tool = new(resolver, new OperationCorrelationIdProvider(), browser);

		// Act
		PageSmokeResponse response = await tool.Run(Args());

		// Assert
		response.Success.Should().BeNull(because: "a question is not a failure, so clio-run keeps the browser paths in it");
		response.Message.Should().Be("relay it", because: "the instruction reaches the agent");
		response.Code.Should().Be("browser-consent-required", because: "the agent must tell this apart from a failure");
		response.BrowserConsent.Should().BeSameAs(consent, because: "the question and its choices reach the agent");
		await runner.DidNotReceiveWithAnyArgs().RunAsync(default, default);
	}

	[Test]
	[Description("The environment is resolved before the browser (a mistyped environment fails before any download), the user's answer is passed to the resolver, and the browser it resolves is the one the runner launches.")]
	public async Task Run_Should_Pass_The_Users_Answer_And_Launch_The_Resolved_Browser() {
		// Arrange
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		IPageSmokeRunner runner = Substitute.For<IPageSmokeRunner>();
		PageSmokeRequest seen = null;
		runner.RunAsync(Arg.Do<PageSmokeRequest>(request => seen = request), Arg.Any<CancellationToken>())
			.Returns(new PageSmokeResult { Success = true, Ready = true });
		resolver.Resolve<IPageSmokeRunner>(Arg.Any<EnvironmentOptions>()).Returns(runner);
		IPageSmokeBrowserResolver browser = ReadyBrowser();
		PageSmokeTool tool = new(resolver, new OperationCorrelationIdProvider(), browser);

		// Act
		await tool.Run(Args() with { Browser = "existing", BrowserPath = "/Applications/Edge" });

		// Assert
		Received.InOrder(() => {
			resolver.Resolve<IPageSmokeRunner>(Arg.Any<EnvironmentOptions>());
			browser.ResolveAsync("existing", "/Applications/Edge", Arg.Any<CancellationToken>());
		});
		seen.BrowserPath.Should().Be(BrowserExecutable, because: "the runner starts only the browser the resolver chose");
	}
}
