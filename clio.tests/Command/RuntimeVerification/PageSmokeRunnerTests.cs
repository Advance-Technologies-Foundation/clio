using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.RuntimeVerification;
using Clio.Common;
using Clio.Common.BrowserSession;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.RuntimeVerification;

/// <summary>ENG-103032: page-smoke's flow over a fake CDP session - readiness, inventory, click, bounds and cleanup.</summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class PageSmokeRunnerTests {

	private const string EnvUri = "http://sandbox";
	private const string RecordId = "8d6a5c4e-0f1b-4d6f-9d0a-2b9c1e7f3a10";
	private const string CookieValue = "SECRET-COOKIE-VALUE";
	private const string Ready = """{"state":"ready","href":"http://sandbox/0/Shell/#Card/UsrOrders_FormPage/edit/1","signature":"s1"}""";
	private const string Inventory = """
		{"title":"Order 1","scope":"page","totalButtons":2,"totalFields":1,
		 "buttons":[{"name":"SaveButton","caption":"Save"},{"name":"RunButton","caption":"Run check"}],
		 "fields":[{"name":"Name","type":"input","caption":"Name","required":true,"value":"Order 1"}]}
		""";

	// A CDP session that answers each page script from the test's script, records every command, and lets the test
	// push events as the browser would.
	private sealed class FakeCdpSession : ICdpSession {
		private readonly Queue<string> _ready = new();
		private string _lastReady = """{"state":"loading","why":"start"}""";

		public List<(string Method, JsonElement Params)> Calls { get; } = [];

		public string Inventory { get; set; } = PageSmokeRunnerTests.Inventory;

		public string Overlays { get; set; } = """{"dialogs":[],"toasts":["Record saved"]}""";

		public string ClickOutcome { get; set; } = """{"outcome":"clicked","name":"RunButton","caption":"Run check"}""";

		public Action<FakeCdpSession> OnClick { get; set; }

		public Action<FakeCdpSession> OnNavigate { get; set; }

		public string NavigateErrorText { get; set; }

		public Exception ClickException { get; set; }

		public Exception EvaluateException { get; set; }

		public bool Disposed { get; private set; }

		public event EventHandler<CdpEvent> EventReceived;

		public void QueueReady(params string[] states) {
			foreach (string state in states) {
				_ready.Enqueue(state);
			}
		}

		public void Raise(string method, string parameters) =>
			EventReceived?.Invoke(this, new CdpEvent(method, JsonDocument.Parse(parameters).RootElement.Clone()));

		public Task<JsonElement> SendAsync(string method, object parameters, CancellationToken ct) {
			JsonElement sent = JsonSerializer.SerializeToElement(parameters ?? new { });
			lock (Calls) {
				Calls.Add((method, sent));
			}
			if (method == "Runtime.evaluate" && EvaluateException is not null) {
				return Task.FromException<JsonElement>(EvaluateException);
			}
			if (method == "Page.navigate") {
				OnNavigate?.Invoke(this);
			}
			string answer = method switch {
				"Runtime.evaluate" => Evaluate(sent.GetProperty("expression").GetString()),
				"Page.navigate" => NavigateErrorText is null
					? """{"frameId":"F"}"""
					: JsonSerializer.Serialize(new { frameId = "F", errorText = NavigateErrorText }),
				_ => "{}"
			};
			return Task.FromResult(JsonDocument.Parse(answer).RootElement.Clone());
		}

		private string Evaluate(string expression) {
			string value;
			if (expression == FreedomUiPageScripts.ReadyScript) {
				if (_ready.Count > 0) {
					_lastReady = _ready.Dequeue();
				}
				value = _lastReady;
			}
			else if (expression == FreedomUiPageScripts.InventoryScript) {
				value = Inventory;
			}
			else if (expression == FreedomUiPageScripts.OverlayScript) {
				value = Overlays;
			}
			else {
				OnClick?.Invoke(this);
				if (ClickException is not null) {
					throw ClickException;
				}
				value = ClickOutcome;
			}
			return "{\"result\":{\"type\":\"object\",\"value\":" + value + "}}";
		}

		public ValueTask DisposeAsync() {
			Disposed = true;
			return ValueTask.CompletedTask;
		}
	}

	private sealed record Harness(
		PageSmokeRunner Runner,
		FakeCdpSession Cdp,
		IHeadlessBrowser Browser,
		IHeadlessBrowserLauncher Launcher,
		ICreatedRecordWatcher Watcher);

	private static Harness Create(FakeCdpSession cdp = null) {
		cdp ??= new FakeCdpSession();
		EnvironmentSettings environment = new() { Uri = EnvUri, IsNetCore = false };
		IBrowserSessionService sessions = Substitute.For<IBrowserSessionService>();
		sessions.GetSessionPathAsync(environment, null, false, Arg.Any<CancellationToken>()).Returns("/home/state.json");
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		fileSystem.ReadAllText("/home/state.json").Returns($$"""
			{"cookies":[{"name":"BPMCSRF","value":"{{CookieValue}}","domain":"sandbox","path":"/"},
			            {"name":".ASPXAUTH","value":"{{CookieValue}}","domain":"sandbox","path":"/","httpOnly":true}],"origins":[]}
			""");
		IHeadlessBrowser browser = Substitute.For<IHeadlessBrowser>();
		browser.PageWebSocketUrl.Returns(new Uri("ws://127.0.0.1:9222/devtools/page/1"));
		IHeadlessBrowserLauncher launcher = Substitute.For<IHeadlessBrowserLauncher>();
		launcher.LaunchAsync(Arg.Any<CancellationToken>()).Returns(browser);
		ICdpSessionFactory factory = Substitute.For<ICdpSessionFactory>();
		factory.ConnectAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>()).Returns(cdp);
		ICreatedRecordWatcher watcher = Substitute.For<ICreatedRecordWatcher>();
		DateTimeOffset now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
		PageSmokeRunner runner = new(environment, sessions, fileSystem, launcher, factory, watcher) {
			Clock = () => now,
			Delay = (ms, _) => {
				now = now.AddMilliseconds(ms);
				return Task.CompletedTask;
			}
		};
		return new Harness(runner, cdp, browser, launcher, watcher);
	}

	private static PageSmokeRequest Request(string recordId = RecordId, string click = null, int timeout = 30,
		IReadOnlyList<string> watch = null) =>
		new("UsrOrders_FormPage", recordId, timeout, click, 8, watch ?? []);

	[Test]
	[Description("A page that becomes ready and stays quiet is reported with its buttons, fields and load errors; the session cookies are injected before navigation to the edit URL, and the browser is disposed.")]
	public async Task RunAsync_Should_Report_A_Ready_Page() {
		// Arrange
		Harness harness = Create();
		harness.Cdp.QueueReady("""{"state":"loading","why":"the Shell splash is showing"}""", Ready);

		// Act
		PageSmokeResult result = await harness.Runner.RunAsync(Request(), CancellationToken.None);

		// Assert
		result.Success.Should().BeTrue(because: "the page became ready: " + result.Error);
		result.Ready.Should().BeTrue(because: "the ready state held for the quiet period");
		result.Mode.Should().Be("edit", because: "a record id opens edit mode");
		result.Url.Should().Be("/0/Shell/#Card/UsrOrders_FormPage/edit/" + RecordId,
			because: "the url is reported relative to the environment");
		result.Title.Should().Be("Order 1", because: "the inventory carries the title");
		result.Buttons.Select(button => button.Name).Should().Equal(["SaveButton", "RunButton"], because: "both are visible");
		result.ButtonsOmitted.Should().Be(0, because: "nothing was over the cap");
		result.Fields.Should().ContainSingle(because: "one field is visible").Which.Required.Should().BeTrue(
			because: "the required flag is kept");
		List<string> methods = harness.Cdp.Calls.Select(call => call.Method).ToList();
		methods.Count(method => method == "Network.setCookie").Should().Be(2, because: "every session cookie is injected");
		methods.IndexOf("Page.navigate").Should().BeGreaterThan(methods.LastIndexOf("Network.setCookie"),
			because: "the cookies must be in place before the page loads");
		methods.Should().Contain(["Runtime.enable", "Log.enable", "Page.enable"],
			because: "exceptions, console errors and navigations are collected");
		harness.Cdp.Calls.Single(call => call.Method == "Page.navigate").Params.GetProperty("url").GetString()
			.Should().Be(EnvUri + "/0/Shell/#Card/UsrOrders_FormPage/edit/" + RecordId, because: "the card URL is opened");
		harness.Cdp.Disposed.Should().BeTrue(because: "the session is closed when the check ends");
		await harness.Browser.Received(1).DisposeAsync();
	}

	[Test]
	[Description("Without a record id the page opens in add mode.")]
	public async Task RunAsync_Should_Open_Add_Mode_Without_A_Record() {
		// Arrange
		Harness harness = Create();
		harness.Cdp.QueueReady(Ready);

		// Act
		PageSmokeResult result = await harness.Runner.RunAsync(Request(recordId: null), CancellationToken.None);

		// Assert
		result.Mode.Should().Be("add", because: "no record was named");
		harness.Cdp.Calls.Single(call => call.Method == "Page.navigate").Params.GetProperty("url").GetString()
			.Should().EndWith("#Card/UsrOrders_FormPage/add", because: "add mode takes no id");
	}

	[Test]
	[Description("The login form seen twice in a row fails the check with advice, keeps the load errors, and still disposes the browser.")]
	public async Task RunAsync_Should_Fail_On_The_Login_Form() {
		// Arrange
		Harness harness = Create();
		harness.Cdp.QueueReady("""{"state":"login"}""");
		harness.Cdp.OnNavigate = cdp => cdp.Raise("Log.entryAdded",
			"""{"entry":{"level":"error","text":"Failed to load resource: 401","url":"http://sandbox/0/rest/X?token=abc"}}""");

		// Act
		PageSmokeResult result = await harness.Runner.RunAsync(Request(), CancellationToken.None);

		// Assert
		result.Success.Should().BeFalse(because: "the page never opened");
		result.Ready.Should().BeFalse(because: "a login form is not the page");
		result.Error.Should().Contain("login form", because: "the caller learns the session was refused");
		result.ConsoleErrors.Should().ContainSingle(because: "load errors are reported even on failure")
			.Which.Url.Should().Be("/0/rest/X", because: "a failed request is reported by its path, never its query");
		await harness.Browser.Received(1).DisposeAsync();
	}

	[Test]
	[Description("A page that never gets ready fails after the timeout with the last reason, and the wait is bounded by the fake clock.")]
	public async Task RunAsync_Should_Time_Out_With_The_Last_Reason() {
		// Arrange
		Harness harness = Create();
		harness.Cdp.QueueReady("""{"state":"loading","why":"a loading mask is showing"}""");

		// Act
		PageSmokeResult result = await harness.Runner.RunAsync(Request(timeout: 5), CancellationToken.None);

		// Assert
		result.Success.Should().BeFalse(because: "the page was not ready in time");
		result.Error.Should().Contain("not ready after 5 s").And.Contain("a loading mask is showing",
			because: "the reason the page was still loading is given");
		harness.Cdp.Calls.Count(call => call.Method == "Runtime.evaluate").Should().BeLessThan(30,
			because: "the wait polls every 250 ms for 5 s");
	}

	[Test]
	[Description("A Classic UI page is refused.")]
	public async Task RunAsync_Should_Refuse_A_Classic_Page() {
		// Arrange
		Harness harness = Create();
		harness.Cdp.QueueReady("""{"state":"classic"}""");

		// Act
		PageSmokeResult result = await harness.Runner.RunAsync(Request(), CancellationToken.None);

		// Assert
		result.Success.Should().BeFalse(because: "only Freedom UI pages are read");
		result.Error.Should().Contain("Classic UI", because: "the caller learns why");
	}

	[Test]
	[Description("A click reports only what followed it: its navigations, its console errors (not the load's), the toast, the invalid fields and the records that appeared in the watched entity.")]
	public async Task RunAsync_Should_Report_What_The_Click_Did() {
		// Arrange
		Harness harness = Create();
		harness.Cdp.QueueReady(Ready);
		harness.Cdp.OnNavigate = cdp => cdp.Raise("Runtime.consoleAPICalled",
			"""{"type":"error","args":[{"type":"string","value":"load noise"}]}""");
		harness.Cdp.OnClick = cdp => {
			cdp.Raise("Page.navigatedWithinDocument", """{"frameId":"F","url":"http://sandbox/0/Shell/#Section/UsrOrders_ListPage"}""");
			cdp.Raise("Runtime.exceptionThrown",
				"""{"exceptionDetails":{"text":"Uncaught","exception":{"description":"TypeError: x is undefined\n    at handler (app.js:1:2)\n    at more"}}}""");
			cdp.Inventory = """{"title":"Order 1","scope":"page","totalButtons":0,"totalFields":1,"buttons":[],"fields":[{"name":"Name","type":"input","caption":"Name","error":"Required field"}]}""";
		};
		WatchBaseline baseline = new();
		harness.Watcher.Capture(Arg.Any<IReadOnlyList<string>>(), Arg.Any<int>(), Arg.Any<DateTimeOffset?>()).Returns(baseline);
		WatchOutcome outcome = new();
		outcome.Created.Add(new CreatedRecord("UsrOrders", RecordId, "Order 2"));
		harness.Watcher.Collect(baseline, Arg.Any<int>(), Arg.Any<DateTimeOffset?>()).Returns(outcome);

		// Act
		PageSmokeResult result = await harness.Runner.RunAsync(Request(click: "run check", watch: ["UsrOrders"]),
			CancellationToken.None);

		// Assert
		result.Success.Should().BeTrue(because: "the click was made: " + result.Error);
		PageClickReport click = result.Click;
		click.Outcome.Should().Be("clicked", because: "one enabled button matched");
		click.Settled.Should().Be(true, because: "the page stayed quiet after the click");
		click.Navigations.Should().Equal(["/0/Shell/#Section/UsrOrders_ListPage"], because: "the click navigated");
		click.ConsoleErrors.Should().ContainSingle(because: "only errors after the click count")
			.Which.Text.Should().Contain("TypeError: x is undefined").And.Contain("at handler").And.NotContain("at more",
				because: "the message and the first frame are kept, the rest of the stack is not");
		result.ConsoleErrors.Should().ContainSingle(because: "the load error stays with the load")
			.Which.Text.Should().Contain("load noise", because: "console.error text is reported");
		click.Toasts.Should().ContainSingle(because: "the notification is reported").Which.Should().Contain("Record saved",
			because: "its text is kept, fenced as page data");
		click.InvalidFields.Should().ContainSingle(because: "the field shows a validation error").Which.Error
			.Should().Be("Required field", because: "the message is reported");
		click.Created.Should().ContainSingle(because: "one order appeared").Which.Id.Should().Be(RecordId,
			because: "the watcher's record is reported");
		harness.Watcher.Received(1).Capture(Arg.Is<IReadOnlyList<string>>(list => list.Single() == "UsrOrders"),
			Arg.Any<int>(), Arg.Any<DateTimeOffset?>());
	}

	[Test]
	[Description("A click that matches nothing fails the check, lists the candidates, and the page report is kept.")]
	public async Task RunAsync_Should_Fail_A_Click_That_Matches_Nothing() {
		// Arrange
		Harness harness = Create();
		harness.Cdp.QueueReady(Ready);
		harness.Cdp.ClickOutcome = """{"outcome":"not-found","candidates":[{"name":"SaveButton","caption":"Save"}]}""";

		// Act
		PageSmokeResult result = await harness.Runner.RunAsync(Request(click: "Approve"), CancellationToken.None);

		// Assert
		result.Success.Should().BeFalse(because: "the requested click could not be made");
		result.Error.Should().Contain("No visible button matches 'Approve'", because: "the caller learns why");
		result.Click.Candidates.Should().ContainSingle(because: "the page's buttons are offered").Which.Name
			.Should().Be("SaveButton", because: "the candidate is named");
		result.Buttons.Should().NotBeEmpty(because: "the page report is still returned");
		harness.Watcher.DidNotReceive().Collect(Arg.Any<WatchBaseline>(), Arg.Any<int>(), Arg.Any<DateTimeOffset?>());
	}

	[Test]
	[Description("A navigation the browser refuses fails the check and still disposes the session and the browser.")]
	public async Task RunAsync_Should_Dispose_The_Browser_When_Navigation_Fails() {
		// Arrange
		FakeCdpSession cdp = new() { NavigateErrorText = "net::ERR_NAME_NOT_RESOLVED" };
		Harness harness = Create(cdp);

		// Act
		PageSmokeResult result = await harness.Runner.RunAsync(Request(), CancellationToken.None);

		// Assert
		result.Success.Should().BeFalse(because: "the environment could not be opened");
		result.Error.Should().Contain("ERR_NAME_NOT_RESOLVED", because: "the browser's reason is given");
		cdp.Disposed.Should().BeTrue(because: "the session is always closed");
		await harness.Browser.Received(1).DisposeAsync();
	}

	[Test]
	[Description("A missing browser is reported as such, not thrown.")]
	public async Task RunAsync_Should_Report_A_Missing_Browser() {
		// Arrange
		Harness harness = Create();
		harness.Launcher.LaunchAsync(Arg.Any<CancellationToken>())
			.Returns<IHeadlessBrowser>(_ => throw new ChromiumNotFoundException("Error: Chromium binary not found."));

		// Act
		PageSmokeResult result = await harness.Runner.RunAsync(Request(), CancellationToken.None);

		// Assert
		result.Success.Should().BeFalse(because: "nothing could be opened");
		result.Error.Should().Contain("Chromium-based browser", because: "the caller learns what to install");
	}

	[Test]
	[Description("No cookie value reaches the reply, whatever the page reports.")]
	public async Task RunAsync_Should_Never_Return_A_Cookie_Value() {
		// Arrange
		Harness harness = Create();
		harness.Cdp.QueueReady(Ready);

		// Act
		PageSmokeResult result = await harness.Runner.RunAsync(Request(), CancellationToken.None);

		// Assert
		JsonSerializer.Serialize(result).Should().NotContain(CookieValue,
			because: "the session cookies are bearer secrets and only ever go to the browser");
	}

	[Test]
	[Description("A native confirm the click opens is dismissed (never accepted) and reported in click.dialogs.")]
	public async Task RunAsync_Should_Dismiss_And_Report_A_Native_Dialog() {
		// Arrange
		Harness harness = Create();
		harness.Cdp.QueueReady(Ready);
		harness.Cdp.OnClick = cdp => cdp.Raise("Page.javascriptDialogOpening",
			"""{"url":"http://sandbox/0/Shell/","message":"Delete this order?","type":"confirm"}""");

		// Act
		PageSmokeResult result = await harness.Runner.RunAsync(Request(click: "Run check"), CancellationToken.None);
		await WaitForCallAsync(harness.Cdp, "Page.handleJavaScriptDialog");

		// Assert
		result.Click.Dialogs.Should().ContainSingle(because: "the native dialog is reported").Which
			.Should().Contain("confirm: Delete this order?", because: "its type and message are kept, fenced as page data");
		JsonElement answer;
		lock (harness.Cdp.Calls) {
			answer = harness.Cdp.Calls.First(call => call.Method == "Page.handleJavaScriptDialog").Params;
		}
		answer.GetProperty("accept").GetBoolean().Should().BeFalse(because: "page-smoke never confirms anything");
	}

	[Test]
	[Description("When the click script never answers, the check fails with outcome unknown, still reports what followed, and still reads the watched entities, because the button may have been pressed.")]
	public async Task RunAsync_Should_Report_An_Unanswered_Click_As_Unknown_And_Still_Collect() {
		// Arrange
		Harness harness = Create();
		harness.Cdp.QueueReady(Ready);
		harness.Cdp.OnClick = cdp => cdp.Raise("Page.navigatedWithinDocument",
			"""{"frameId":"F","url":"http://sandbox/0/Shell/#Section/X?token=abc"}""");
		harness.Cdp.ClickException = new InvalidOperationException("The browser did not answer Runtime.evaluate within 10 s.");
		WatchBaseline baseline = new();
		harness.Watcher.Capture(Arg.Any<IReadOnlyList<string>>(), Arg.Any<int>(), Arg.Any<DateTimeOffset?>()).Returns(baseline);
		WatchOutcome outcome = new();
		outcome.Created.Add(new CreatedRecord("UsrOrders", RecordId, "Order 2"));
		harness.Watcher.Collect(baseline, Arg.Any<int>(), Arg.Any<DateTimeOffset?>()).Returns(outcome);

		// Act
		PageSmokeResult result = await harness.Runner.RunAsync(Request(click: "Save", watch: ["UsrOrders"]),
			CancellationToken.None);

		// Assert
		result.Success.Should().BeFalse(because: "the click's result is not known");
		result.Error.Should().Contain("may have been pressed", because: "the caller must check before retrying");
		result.Click.Outcome.Should().Be("unknown", because: "nothing says whether the button was pressed");
		result.Click.Created.Should().ContainSingle(because: "the records that appeared are still reported");
		result.Click.Navigations.Should().Equal(["/0/Shell/#Section/X"],
			because: "what followed is reported, and a query inside a hash route is never reported");
	}

	[Test]
	[Description("When a read after a successful click gets a reply over 4 MB, the check still reports the click and the records it created, and warns that what followed may be incomplete.")]
	public async Task RunAsync_Should_Keep_A_Pressed_Click_When_A_Later_Reply_Is_Too_Large() {
		// Arrange
		Harness harness = Create();
		harness.Cdp.QueueReady(Ready);
		harness.Cdp.OnClick = cdp => cdp.EvaluateException = new CdpReplyTooLargeException("Runtime.evaluate", 4 * 1024 * 1024);
		WatchBaseline baseline = new();
		harness.Watcher.Capture(Arg.Any<IReadOnlyList<string>>(), Arg.Any<int>(), Arg.Any<DateTimeOffset?>()).Returns(baseline);
		WatchOutcome outcome = new();
		outcome.Created.Add(new CreatedRecord("UsrOrders", RecordId, "Order 2"));
		harness.Watcher.Collect(baseline, Arg.Any<int>(), Arg.Any<DateTimeOffset?>()).Returns(outcome);

		// Act
		PageSmokeResult result = await harness.Runner.RunAsync(Request(click: "Run check", watch: ["UsrOrders"]),
			CancellationToken.None);

		// Assert
		result.Success.Should().BeTrue(because: "the button was pressed; only a read after it was lost");
		result.Click.Outcome.Should().Be("clicked", because: "the click script answered");
		result.Click.Created.Should().ContainSingle(because: "the records the click wrote are still reported");
		result.Warnings.Should().Contain(warning => warning.Contains("exceeded 4 MB") && warning.Contains("may be incomplete"),
			because: "the caller is told which part of the report is missing and why");
	}

	[Test]
	[Description("A browser that dies while the page loads ends the wait at once with the reason, instead of polling until the timeout.")]
	public async Task RunAsync_Should_Stop_At_Once_When_The_Browser_Dies() {
		// Arrange
		Harness harness = Create();
		harness.Cdp.EvaluateException = new CdpSessionClosedException(
			"CDP command 'Runtime.evaluate' could not complete: the browser closed the DevTools connection.");

		// Act
		PageSmokeResult result = await harness.Runner.RunAsync(Request(), CancellationToken.None);

		// Assert
		result.Success.Should().BeFalse(because: "the browser is gone");
		result.Error.Should().Contain("closed the DevTools connection", because: "the real cause is reported");
		harness.Cdp.Calls.Count(call => call.Method == "Runtime.evaluate").Should().Be(1,
			because: "a closed session is not retried");
		await harness.Browser.Received(1).DisposeAsync();
	}

	[Test]
	[Description("The distinct-message cap is per phase: a noisy load cannot crowd out the click's errors, and what was dropped is counted.")]
	public void Collector_Should_Cap_Distinct_Errors_Per_Phase() {
		// Arrange
		PageEventCollector events = new(url => url);
		for (int i = 0; i < PageEventCollector.MaxDistinctPerPhase + 10; i++) {
			events.OnEvent(this, Event("Runtime.consoleAPICalled", $$"""{"type":"error","args":[{"value":"load {{i}}"}]}"""));
		}

		// Act
		int mark = events.Mark();
		events.OnEvent(this, Event("Runtime.consoleAPICalled", """{"type":"error","args":[{"value":"click failure"}]}"""));
		IReadOnlyList<PageConsoleError> load = events.ErrorsBetween(0, mark, 15, out int loadOmitted);
		IReadOnlyList<PageConsoleError> click = events.ErrorsSince(mark, 15, out int clickOmitted);

		// Assert
		load.Should().HaveCount(15, because: "the list is capped");
		loadOmitted.Should().Be(PageEventCollector.MaxDistinctPerPhase + 10 - 15,
			because: "both the distinct messages past the list cap and the occurrences past the phase cap are counted");
		click.Should().ContainSingle(because: "the click's phase has its own cap").Which.Text.Should().Contain("click failure",
			because: "the click's error is kept");
		clickOmitted.Should().Be(0, because: "nothing of the click was dropped");
	}

	private static CdpEvent Event(string method, string parameters) =>
		new(method, JsonDocument.Parse(parameters).RootElement.Clone());

	private static async Task WaitForCallAsync(FakeCdpSession cdp, string method) {
		for (int attempt = 0; attempt < 100; attempt++) {
			lock (cdp.Calls) {
				if (cdp.Calls.Any(call => call.Method == method)) {
					return;
				}
			}
			await Task.Delay(20);
		}
	}

	[Test]
	[Description("A reply over the size bound is cut - field values first, then the lists - and says so.")]
	public void Fit_Should_Keep_The_Reply_Under_The_Bound() {
		// Arrange
		string longText = new('x', 60);
		PageSmokeResult result = new() {
			Success = true,
			Buttons = Enumerable.Range(0, 40).Select(i => new PageButton("Button" + i + longText, longText)).ToList(),
			Fields = Enumerable.Range(0, 60).Select(i => new PageField("Field" + i + longText, "input", longText) { Value = longText })
				.ToList(),
			ConsoleErrors = Enumerable.Range(0, 15).Select(i => new PageConsoleError("console", longText + longText + longText + i, 1))
				.ToList()
		};

		// Act
		PageSmokeRunner.Fit(result);

		// Assert
		JsonSerializer.SerializeToUtf8Bytes(result).Length.Should().BeLessThanOrEqualTo(PageSmokeRunner.MaxReplyBytes,
			because: "the reply must stay well under the 20 KB spill line");
		result.Fields.Should().OnlyContain(field => field.Value == null, because: "values go first");
		result.Warnings.Should().Contain(warning => warning.Contains("cut"), because: "the caller is told what is missing");
		(result.FieldsOmitted + result.Fields.Count).Should().Be(60, because: "every field cut is counted");
		(result.ButtonsOmitted + result.Buttons.Count).Should().Be(40, because: "every button cut is counted");
	}

	[Test]
	[Description("The diagnostic lists are cut before click.created loses a single record, and the warning names each list that was cut and by how much.")]
	public void Fit_Should_Cut_The_Diagnostics_Before_The_Created_Records() {
		// Arrange
		string longText = new('x', 60);
		List<CreatedRecord> created = Enumerable.Range(0, 4)
			.Select(i => new CreatedRecord("UsrOrders", Guid.NewGuid().ToString(), "Order " + i)).ToList();
		PageSmokeResult result = new() {
			Success = true,
			Buttons = Enumerable.Range(0, 40).Select(i => new PageButton("Button" + i + longText, longText)).ToList(),
			Fields = Enumerable.Range(0, 60).Select(i => new PageField("Field" + i + longText, "input", longText)).ToList(),
			ConsoleErrors = Enumerable.Range(0, 15)
				.Select(i => new PageConsoleError("console", new string('e', 900) + i, 1)).ToList(),
			Click = new PageClickReport {
				Outcome = "clicked",
				Created = created,
				ConsoleErrors = Enumerable.Range(0, 15)
					.Select(i => new PageConsoleError("console", new string('c', 900) + i, 1)).ToList()
			}
		};

		// Act
		PageSmokeRunner.Fit(result);

		// Assert
		JsonSerializer.SerializeToUtf8Bytes(result).Length.Should().BeLessThanOrEqualTo(PageSmokeRunner.MaxReplyBytes,
			because: "the reply must still fit");
		result.Click.Created.Should().Equal(created,
			because: "the records the click wrote are never cut while a diagnostic list can still give way");
		(result.FieldsOmitted + result.Fields.Count).Should().Be(60, because: "every field cut is counted");
		string warning = result.Warnings.Single(item => item.StartsWith("Lists cut"));
		warning.Should().Contain($"fields -{result.FieldsOmitted}").And.Contain("buttons -").And.Contain("click.consoleErrors -",
			because: "the warning names which list lost how many items");
		result.Warnings.Should().NotContain(item => item.Contains("click.created"),
			because: "click.created was left whole");
	}

	[Test]
	[Description("When the reply is still too big with every diagnostic list empty, click.created loses its titles first, then records, and the warning says so.")]
	public void Fit_Should_Cut_Created_Only_As_A_Last_Resort() {
		// Arrange
		List<CreatedRecord> created = Enumerable.Range(0, 400)
			.Select(i => new CreatedRecord("UsrOrders", Guid.NewGuid().ToString(), new string('t', 60))).ToList();
		PageSmokeResult result = new() {
			Success = true,
			Buttons = [new PageButton("SaveButton", "Save")],
			Click = new PageClickReport { Outcome = "clicked", Created = created }
		};

		// Act
		PageSmokeRunner.Fit(result);

		// Assert
		JsonSerializer.SerializeToUtf8Bytes(result).Length.Should().BeLessThanOrEqualTo(PageSmokeRunner.MaxReplyBytes,
			because: "the reply must still fit");
		result.Buttons.Should().BeEmpty(because: "the diagnostics go first");
		result.Click.Created.Should().NotBeEmpty().And.OnlyContain(record => record.Title == null,
			because: "titles go before any record does");
		result.Click.Created.Count.Should().BeLessThan(400, because: "400 ids do not fit in the bound");
		result.Click.Created.Select(record => record.Id).Should().Equal(
			created.Take(result.Click.Created.Count).Select(record => record.Id), because: "the kept records are the first ones");
		result.Warnings.Should().Contain(item => item.Contains("click.created") && item.Contains("read the watched entities"),
			because: "the caller must learn that some written records are not listed");
	}
}
