using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using Clio.Common.BrowserSession;

namespace Clio.Command.RuntimeVerification;

/// <summary>
/// Opens a Freedom UI form page in a headless browser with the environment's session, reports what it shows and what
/// it logged, and optionally clicks one button and reports what followed.
/// </summary>
public interface IPageSmokeRunner {

	/// <summary>Runs one page check. Never throws for a page or browser problem; it is reported instead.</summary>
	/// <param name="request">The page, the record, the waits and the optional click.</param>
	/// <param name="ct">Cancels the check; the browser is still killed and its profile deleted.</param>
	/// <returns>The outcome.</returns>
	Task<PageSmokeResult> RunAsync(PageSmokeRequest request, CancellationToken ct);
}

/// <summary>Input of <see cref="IPageSmokeRunner.RunAsync"/>.</summary>
/// <param name="Page">Freedom UI form page schema name.</param>
/// <param name="RecordId">Record to open in edit mode; null opens the page in add mode.</param>
/// <param name="TimeoutSeconds">How long to wait for the page to be ready.</param>
/// <param name="Click">Caption or element name of the button to click; null clicks nothing.</param>
/// <param name="ClickWaitSeconds">How long to wait for the page to settle after the click.</param>
/// <param name="Watch">Entities diffed around the click to report the records it created.</param>
public sealed record PageSmokeRequest(
	string Page,
	string RecordId,
	int TimeoutSeconds,
	string Click,
	int ClickWaitSeconds,
	IReadOnlyList<string> Watch);

/// <summary>What happened after the click.</summary>
public sealed class PageClickReport {

	/// <summary>
	/// <c>clicked</c>; or why nothing was clicked: <c>not-found</c>, <c>ambiguous</c> (several buttons match) or
	/// <c>disabled</c>.
	/// </summary>
	[JsonPropertyName("outcome")]
	public string Outcome { get; set; }

	/// <summary>The clicked button's element name.</summary>
	[JsonPropertyName("name"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Name { get; set; }

	/// <summary>The clicked button's caption.</summary>
	[JsonPropertyName("caption"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Caption { get; set; }

	/// <summary>When nothing was clicked: the buttons that could have been meant.</summary>
	[JsonPropertyName("candidates"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public List<PageButton> Candidates { get; set; }

	/// <summary>The page address once the click settled, relative to the environment.</summary>
	[JsonPropertyName("url"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Url { get; set; }

	/// <summary>Addresses the page moved to after the click, in order.</summary>
	[JsonPropertyName("navigations"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<string> Navigations { get; set; }

	/// <summary>Errors logged after the click.</summary>
	[JsonPropertyName("consoleErrors"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<PageConsoleError> ConsoleErrors { get; set; }

	/// <summary>Visible dialogs after the click (their text).</summary>
	[JsonPropertyName("dialogs"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public List<string> Dialogs { get; set; }

	/// <summary>Visible notifications after the click (their text), for example a validation message.</summary>
	[JsonPropertyName("toasts"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public List<string> Toasts { get; set; }

	/// <summary>Fields that show a validation error after the click.</summary>
	[JsonPropertyName("invalidFields"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public List<PageField> InvalidFields { get; set; }

	/// <summary>Records that appeared in the watched entities around the click. Never deleted by this tool.</summary>
	[JsonPropertyName("created"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public List<CreatedRecord> Created { get; set; }

	/// <summary>False when the page was still changing when the click wait ended; null when nothing was clicked.</summary>
	[JsonPropertyName("settled"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? Settled { get; set; }
}

/// <summary>Outcome of <see cref="IPageSmokeRunner.RunAsync"/>.</summary>
public sealed class PageSmokeResult {

	/// <summary>
	/// False when the browser or the session could not be started, the page did not become ready, or the requested
	/// click could not be made. Console errors alone do not make it false: read <see cref="ConsoleErrors"/>.
	/// </summary>
	[JsonPropertyName("success")]
	public bool Success { get; set; }

	/// <summary>Why the check failed.</summary>
	[JsonPropertyName("error"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Error { get; set; }

	/// <summary>The page address that was opened, relative to the environment.</summary>
	[JsonPropertyName("url"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Url { get; set; }

	/// <summary><c>edit</c> with a record, <c>add</c> without.</summary>
	[JsonPropertyName("mode"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Mode { get; set; }

	/// <summary>Whether the page reached the ready state.</summary>
	[JsonPropertyName("ready")]
	public bool Ready { get; set; }

	/// <summary>Milliseconds from navigation to ready.</summary>
	[JsonPropertyName("loadMs"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public long? LoadMs { get; set; }

	/// <summary>The page title.</summary>
	[JsonPropertyName("title"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Title { get; set; }

	/// <summary>Visible buttons, at most <see cref="FreedomUiPageScripts.MaxButtons"/>.</summary>
	[JsonPropertyName("buttons"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public List<PageButton> Buttons { get; set; }

	/// <summary>Visible buttons not listed, because of the cap or to keep the reply small.</summary>
	[JsonPropertyName("buttonsOmitted"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public int ButtonsOmitted { get; set; }

	/// <summary>Visible fields, at most <see cref="FreedomUiPageScripts.MaxFields"/>.</summary>
	[JsonPropertyName("fields"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public List<PageField> Fields { get; set; }

	/// <summary>Visible fields not listed, because of the cap or to keep the reply small.</summary>
	[JsonPropertyName("fieldsOmitted"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public int FieldsOmitted { get; set; }

	/// <summary>Errors the page logged while it loaded.</summary>
	[JsonPropertyName("consoleErrors")]
	public IReadOnlyList<PageConsoleError> ConsoleErrors { get; set; } = [];

	/// <summary>What the click did; null when no click was asked for.</summary>
	[JsonPropertyName("click"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public PageClickReport Click { get; set; }

	/// <summary>Caveats, including what was cut to keep the reply small.</summary>
	[JsonPropertyName("warnings")]
	public List<string> Warnings { get; set; } = [];
}

internal sealed class PageSmokeRunner(
	EnvironmentSettings environment,
	IBrowserSessionService browserSessionService,
	IFileSystem fileSystem,
	IHeadlessBrowserLauncher browserLauncher,
	ICdpSessionFactory cdpSessionFactory,
	ICreatedRecordWatcher recordWatcher)
	: IPageSmokeRunner {

	internal const int DefaultTimeoutSeconds = 30;
	internal const int MaxTimeoutSeconds = 45;
	internal const int DefaultClickWaitSeconds = 8;
	internal const int MaxClickWaitSeconds = 20;
	internal const int MaxWatchedEntities = 5;
	internal const int MaxConsoleErrors = 15;
	// The serialized reply is kept under this; lists are cut, in order of least use, until it fits.
	internal const int MaxReplyBytes = 14_000;

	// The worker's per-call budget is 120 s; everything stops here so the answer always gets out.
	private const int TotalBudgetMs = 100_000;
	private const int CommandMs = 10_000;
	private const int PollMs = 250;
	// The page is ready (or settled) when its signature has not changed for this long.
	private const int QuietMs = 1_500;
	// A click handler can start its work a moment after the click; the settle wait never ends before this.
	private const int MinimumSettleMs = 1_000;
	private const int RequestMs = 10_000;
	// Kept free at the end of the budget for the reads that follow a click and for the answer itself.
	private const int AnswerReserveMs = 5_000;
	private const string EvaluateMethod = "Runtime.evaluate";

	// Test seams: how the waits sleep, and the clock they measure with.
	internal Func<int, CancellationToken, Task> Delay { get; set; } = (ms, ct) => Task.Delay(ms, ct);

	internal Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

	/// <inheritdoc />
	public async Task<PageSmokeResult> RunAsync(PageSmokeRequest request, CancellationToken ct) {
		PageSmokeResult result = new() { Mode = request.RecordId is null ? "add" : "edit" };
		string url = BuildCardUrl(environment, request.Page, request.RecordId);
		result.Url = RelativeUrl(url);
		using CancellationTokenSource budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
		budget.CancelAfter(TotalBudgetMs);
		DateTimeOffset budgetEnd = Clock().AddMilliseconds(TotalBudgetMs);
		PageEventCollector events = new(RelativeUrl);
		try {
			IReadOnlyList<BrowserCookie> cookies = await ReadSessionCookiesAsync(budget.Token).ConfigureAwait(false);
			await using IHeadlessBrowser browser = await browserLauncher.LaunchAsync(budget.Token).ConfigureAwait(false);
			await using ICdpSession cdp = await cdpSessionFactory.ConnectAsync(browser.PageWebSocketUrl, budget.Token)
				.ConfigureAwait(false);
			cdp.EventReceived += events.OnEvent;
			events.NativeDialogOpened += () => DismissNativeDialog(cdp);
			await OpenPageAsync(cdp, cookies, url, budget.Token).ConfigureAwait(false);
			DateTimeOffset navigatedAt = Clock();
			int readyMs = (int)Math.Clamp((budgetEnd - Clock()).TotalMilliseconds - AnswerReserveMs, 0,
				request.TimeoutSeconds * 1000);
			PageReadyState state = await WaitForReadyAsync(cdp, events, readyMs, budget.Token).ConfigureAwait(false);
			if (state.State != "ready") {
				ReadLoadErrors(result, events, int.MaxValue);
				result.Error = ExplainNotReady(state, request);
				return Fit(result);
			}
			result.Ready = true;
			result.LoadMs = (long)(Clock() - navigatedAt).TotalMilliseconds;
			PageInventory inventory = await EvaluateAsync<PageInventory>(cdp, FreedomUiPageScripts.InventoryScript,
				budget.Token).ConfigureAwait(false);
			ApplyInventory(result, inventory);
			result.Success = true;
			if (request.Click is null) {
				ReadLoadErrors(result, events, int.MaxValue);
			}
			else {
				await ClickAsync(cdp, events, request, result, budgetEnd, budget.Token).ConfigureAwait(false);
			}
		}
		catch (ChromiumNotFoundException exception) {
			result.Error = exception.Message + " page-smoke needs a Chromium-based browser on the machine clio runs on.";
		}
		catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
			ReadLoadErrors(result, events, int.MaxValue);
			result.Success = false;
			result.Error ??= $"The check ran out of its {TotalBudgetMs / 1000} s budget; what was read before is reported.";
		}
		catch (Exception exception) when (exception is InvalidOperationException or CdpCommandException
			or CreatioAuthenticationException or SafeEnvironmentConfirmationRequiredException
			or System.Net.WebSockets.WebSocketException or System.Net.Http.HttpRequestException
			or System.IO.IOException or JsonException) {
			ReadLoadErrors(result, events, int.MaxValue);
			result.Success = false;
			result.Error ??= SensitiveErrorTextRedactor.Redact(exception.Message);
		}
		return Fit(result);
	}

	internal static string BuildCardUrl(EnvironmentSettings environment, string page, string recordId) =>
		AuthenticatedBrowserLauncher.BuildShellUrl(environment) + "#Card/" + page
		+ (recordId is null ? "/add" : "/edit/" + recordId);

	// The page address without the environment's own prefix, and never with a query string (it can carry a token).
	private string RelativeUrl(string url) {
		if (string.IsNullOrEmpty(url)) {
			return url;
		}
		int hash = url.IndexOf('#');
		string address = hash >= 0 ? url[..hash] : url;
		string fragment = hash >= 0 ? url[hash..] : string.Empty;
		url = StripQuery(address) + StripQuery(fragment);
		string baseUri = environment.Uri?.TrimEnd('/') ?? string.Empty;
		return baseUri.Length > 0 && url.StartsWith(baseUri, StringComparison.OrdinalIgnoreCase)
			? url[baseUri.Length..]
			: url;
	}

	// A query in the address or in a hash route can carry a token, so it is never reported.
	private static string StripQuery(string part) {
		int query = part.IndexOf('?');
		return query >= 0 ? part[..query] : part;
	}

	// The load's errors are read once, up to the click (or to the end when there is none), so an error logged between
	// the page getting ready and the click is reported with the load rather than lost.
	private static void ReadLoadErrors(PageSmokeResult result, PageEventCollector events, int upTo) {
		if (result.ConsoleErrors.Count > 0 || (result.Click is not null && upTo == int.MaxValue)) {
			return;
		}
		result.ConsoleErrors = events.ErrorsBetween(0, upTo, MaxConsoleErrors, out int omitted);
		NoteOmitted(result, omitted, "load");
	}

	// A native alert/confirm/prompt/beforeunload blocks the page until it is answered, which would stall every later
	// evaluation. It is dismissed, never accepted - page-smoke never confirms anything - and reported. The answer is
	// sent without waiting: this runs on the session's receive loop, which must stay free to deliver the reply.
	private static void DismissNativeDialog(ICdpSession cdp) =>
		_ = Task.Run(async () => {
			try {
				await cdp.SendAsync("Page.handleJavaScriptDialog", new { accept = false }, CancellationToken.None)
					.WaitAsync(TimeSpan.FromMilliseconds(CommandMs)).ConfigureAwait(false);
			}
			catch (Exception exception) when (exception is InvalidOperationException or CdpCommandException
				or TimeoutException) {
				// The dialog closed by itself or the session ended; nothing to do.
			}
		});

	private async Task<IReadOnlyList<BrowserCookie>> ReadSessionCookiesAsync(CancellationToken ct) {
		string path = await browserSessionService.GetSessionPathAsync(environment, ct: ct).ConfigureAwait(false);
		IReadOnlyList<BrowserCookie> cookies = StorageStateJson.ParseCookies(fileSystem.ReadAllText(path));
		if (cookies.Count == 0) {
			throw new InvalidOperationException("The environment's browser session holds no cookie, so the page would "
				+ "open on the login form. Check the environment's credentials with ping-app.");
		}
		return cookies;
	}

	// Network is enabled only for the cookies and disabled again, so its per-request events do not flood the session;
	// failed requests still arrive through Log.
	private async Task OpenPageAsync(ICdpSession cdp, IReadOnlyList<BrowserCookie> cookies, string url,
		CancellationToken ct) {
		await SendAsync(cdp, "Runtime.enable", null, ct).ConfigureAwait(false);
		await SendAsync(cdp, "Log.enable", null, ct).ConfigureAwait(false);
		await SendAsync(cdp, "Page.enable", null, ct).ConfigureAwait(false);
		await SendAsync(cdp, "Network.enable", null, ct).ConfigureAwait(false);
		foreach (BrowserCookie cookie in cookies) {
			await SendAsync(cdp, "Network.setCookie", AuthenticatedBrowserLauncher.BuildSetCookieParams(cookie, environment.Uri),
				ct).ConfigureAwait(false);
		}
		await SendAsync(cdp, "Network.disable", null, ct).ConfigureAwait(false);
		JsonElement navigation = await SendAsync(cdp, "Page.navigate", new { url }, ct).ConfigureAwait(false);
		if (navigation.TryGetProperty("errorText", out JsonElement errorText)
			&& !string.IsNullOrWhiteSpace(errorText.GetString())) {
			throw new InvalidOperationException($"The browser could not open the environment: {errorText.GetString()}. "
				+ "Check that the environment answers (ping-app).");
		}
	}

	// Polls the ready script until the page is ready and quiet, it lands on the login form or a Classic UI page twice in
	// a row, or the wait ends. A script that fails while the page navigates counts as still loading.
	private async Task<PageReadyState> WaitForReadyAsync(ICdpSession cdp, PageEventCollector events, int waitMs,
		CancellationToken ct) {
		DateTimeOffset deadline = Clock().AddMilliseconds(waitMs);
		PageReadyState last = new("loading", "the page has not answered yet", null, null);
		ReadyTracker tracker = new();
		while (true) {
			PageReadyState state = await TryEvaluateAsync<PageReadyState>(cdp, FreedomUiPageScripts.ReadyScript, ct)
				.ConfigureAwait(false) ?? last with { State = "loading", Why = "the page is navigating" };
			if (tracker.IsDone(state, events.NavigationCount, Clock())) {
				return state;
			}
			last = state;
			if (Clock() >= deadline) {
				return last.State == "ready" ? last with { State = "unsettled" } : last;
			}
			await Delay(PollMs, ct).ConfigureAwait(false);
		}
	}

	// Decides when the ready wait is over: the login form or a Classic UI page seen twice in a row, or a ready page whose
	// signature (and navigation count) has not changed for the quiet period.
	private sealed class ReadyTracker {
		private string _signature;
		private DateTimeOffset _stableSince;
		private string _terminal;

		public bool IsDone(PageReadyState state, int navigationCount, DateTimeOffset now) {
			if (state.State is "login" or "classic") {
				bool repeated = _terminal == state.State;
				_terminal = state.State;
				return repeated;
			}
			_terminal = null;
			if (state.State != "ready") {
				_signature = null;
				return false;
			}
			string current = state.Signature + "|" + navigationCount;
			if (current != _signature) {
				_signature = current;
				_stableSince = now;
				return false;
			}
			return (now - _stableSince).TotalMilliseconds >= QuietMs;
		}
	}

	private static string ExplainNotReady(PageReadyState state, PageSmokeRequest request) => state.State switch {
		"login" => "The browser landed on the login form: the environment did not accept the session. Run "
			+ "get-browser-session with force-refresh=true, or check the credentials with ping-app.",
		"classic" => $"'{request.Page}' opened as a Classic UI page; page-smoke reads Freedom UI pages only.",
		"unsettled" => $"The page kept changing for the whole {request.TimeoutSeconds} s wait; raise timeout, or read "
			+ "consoleErrors for a loop.",
		_ => $"The page was not ready after {request.TimeoutSeconds} s ({state.Why}). Check that '{request.Page}' is a "
			+ "Freedom UI form page (list-pages), that the record exists, and read consoleErrors."
	};

	private static void ApplyInventory(PageSmokeResult result, PageInventory inventory) {
		result.Title = inventory.Title;
		result.Buttons = inventory.Buttons ?? [];
		result.Fields = inventory.Fields ?? [];
		result.ButtonsOmitted = Math.Max(0, inventory.TotalButtons - result.Buttons.Count);
		result.FieldsOmitted = Math.Max(0, inventory.TotalFields - result.Fields.Count);
		if (inventory.Scope != "page") {
			result.Warnings.Add("No page outlet (crt-navigation-schema-outlet) was found, so buttons and fields were "
				+ "read from the whole document, the Shell's own included.");
		}
	}

	private async Task ClickAsync(ICdpSession cdp, PageEventCollector events, PageSmokeRequest request,
		PageSmokeResult result, DateTimeOffset budgetEnd, CancellationToken ct) {
		WatchBaseline baseline = request.Watch.Count > 0
			? recordWatcher.Capture(request.Watch, RequestMs, WatchStop(budgetEnd))
			: null;
		int mark = events.Mark();
		ReadLoadErrors(result, events, mark);
		PageClickReport report = new() { Outcome = "unknown" };
		result.Click = report;
		try {
			PageClickOutcome outcome;
			try {
				outcome = await EvaluateAsync<PageClickOutcome>(cdp, FreedomUiPageScripts.BuildClickScript(request.Click), ct)
					.ConfigureAwait(false);
			}
			catch (Exception exception) when (exception is InvalidOperationException or CdpCommandException
				or JsonException) {
				// The reply can be lost after the button was pressed (a native dialog, a navigation, a hung handler), so
				// what followed is still reported and the watched entities are still read.
				result.Success = false;
				result.Error = "The click script did not answer (" + SensitiveErrorTextRedactor.Redact(exception.Message)
					+ "); the button may have been pressed, so read click.created and the record before retrying.";
				ReportWhatFollowed(report, events, mark, result);
				return;
			}
			report.Outcome = outcome.Outcome;
			report.Name = outcome.Name;
			report.Caption = outcome.Caption;
			if (outcome.Outcome != "clicked") {
				report.Candidates = outcome.Candidates;
				result.Success = false;
				result.Error = outcome.Outcome switch {
					"ambiguous" => $"Several visible buttons match '{request.Click}'; click.candidates lists them. Pass an "
						+ "element name instead of the caption.",
					"disabled" => $"The button '{request.Click}' is disabled, so it was not clicked.",
					_ => $"No visible button matches '{request.Click}' by caption or element name; click.candidates lists "
						+ "the page's buttons."
				};
				return;
			}
			int settleMs = (int)Math.Clamp((budgetEnd - Clock()).TotalMilliseconds - AnswerReserveMs, 0,
				request.ClickWaitSeconds * 1000);
			report.Settled = await WaitToSettleAsync(cdp, events, settleMs, ct).ConfigureAwait(false);
			ReportWhatFollowed(report, events, mark, result);
			PageReadyState after = await TryEvaluateAsync<PageReadyState>(cdp, FreedomUiPageScripts.ReadyScript, ct)
				.ConfigureAwait(false);
			report.Url = after?.Href is null ? null : RelativeUrl(after.Href);
			PageOverlays overlays = await TryEvaluateAsync<PageOverlays>(cdp, FreedomUiPageScripts.OverlayScript, ct)
				.ConfigureAwait(false);
			if (overlays?.Dialogs is { Count: > 0 } dialogs) {
				report.Dialogs = [.. report.Dialogs ?? [], .. dialogs.Select(Untrusted)];
			}
			report.Toasts = overlays?.Toasts is { Count: > 0 } toasts ? toasts.Select(Untrusted).ToList() : null;
			PageInventory inventory = await TryEvaluateAsync<PageInventory>(cdp, FreedomUiPageScripts.InventoryScript, ct)
				.ConfigureAwait(false);
			List<PageField> invalid = inventory?.Fields?.Where(field => field.Error is not null).ToList();
			report.InvalidFields = invalid is { Count: > 0 } ? invalid : null;
			if (report.Settled == false) {
				result.Warnings.Add($"The page was still changing when click-wait ({request.ClickWaitSeconds} s) ended; "
					+ "what followed may be incomplete.");
			}
		}
		finally {
			// Whatever happened after the press, the records it may have created are read - unless it was never pressed.
			if (baseline is not null && report.Outcome is "clicked" or "unknown") {
				WatchOutcome created = recordWatcher.Collect(baseline, RequestMs, WatchStop(budgetEnd));
				report.Created = created.Created;
				result.Warnings.AddRange(created.Warnings);
			}
		}
	}

	// The navigations, errors and native dialogs that followed the click.
	private static void ReportWhatFollowed(PageClickReport report, PageEventCollector events, int mark,
		PageSmokeResult result) {
		IReadOnlyList<string> navigations = events.NavigationsSince(mark);
		report.Navigations = navigations.Count > 0 ? navigations : null;
		IReadOnlyList<PageConsoleError> errors = events.ErrorsSince(mark, MaxConsoleErrors, out int omitted);
		report.ConsoleErrors = errors.Count > 0 ? errors : null;
		NoteOmitted(result, omitted, "click");
		IReadOnlyList<string> native = events.NativeDialogsSince(mark);
		report.Dialogs = native.Count > 0 ? native.Select(Untrusted).ToList() : null;
	}

	// The watcher's reads are synchronous and do not see the token, so each gets a deadline inside the overall budget
	// that leaves room for the answer.
	private DateTimeOffset WatchStop(DateTimeOffset budgetEnd) {
		DateTimeOffset byRequest = Clock().AddMilliseconds(RequestMs * 2);
		DateTimeOffset byBudget = budgetEnd.AddMilliseconds(-AnswerReserveMs);
		return byRequest < byBudget ? byRequest : byBudget;
	}

	private static string Untrusted(string text) => UntrustedText.Fenced(text) ?? text;

	// The page has settled when nothing masks it and neither its address, its named elements nor its overlays changed for
	// the quiet period; a click that navigates resets the wait.
	private async Task<bool> WaitToSettleAsync(ICdpSession cdp, PageEventCollector events, int waitMs,
		CancellationToken ct) {
		DateTimeOffset start = Clock();
		DateTimeOffset deadline = start.AddMilliseconds(waitMs);
		string signature = null;
		DateTimeOffset stableSince = start;
		while (true) {
			PageReadyState state = await TryEvaluateAsync<PageReadyState>(cdp, FreedomUiPageScripts.ReadyScript, ct)
				.ConfigureAwait(false);
			string current = state is null || state.State == "loading"
				? null
				: state.State + "|" + state.Signature + "|" + events.NavigationCount;
			if (current is null || current != signature) {
				signature = current;
				stableSince = Clock();
			}
			else if ((Clock() - stableSince).TotalMilliseconds >= QuietMs
				&& (Clock() - start).TotalMilliseconds >= MinimumSettleMs) {
				return true;
			}
			if (Clock() >= deadline) {
				return false;
			}
			await Delay(PollMs, ct).ConfigureAwait(false);
		}
	}

	private static void NoteOmitted(PageSmokeResult result, int omitted, string phase) {
		if (omitted > 0) {
			result.Warnings.Add($"{omitted} more distinct console error(s) during {phase} not listed.");
		}
	}

	private static async Task<JsonElement> SendAsync(ICdpSession cdp, string method, object parameters,
		CancellationToken ct) {
		using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
		timeout.CancelAfter(CommandMs);
		try {
			return await cdp.SendAsync(method, parameters, timeout.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
			throw new InvalidOperationException($"The browser did not answer {method} within {CommandMs / 1000} s.");
		}
	}

	private static async Task<T> EvaluateAsync<T>(ICdpSession cdp, string script, CancellationToken ct) {
		JsonElement answer = await SendAsync(cdp, EvaluateMethod, new {
			expression = script,
			returnByValue = true
		}, ct).ConfigureAwait(false);
		if (answer.TryGetProperty("exceptionDetails", out JsonElement details)) {
			throw new InvalidOperationException("The page script failed: " + DescribeScriptError(details));
		}
		if (!answer.TryGetProperty("result", out JsonElement result) || !result.TryGetProperty("value", out JsonElement json)) {
			throw new InvalidOperationException("The page script returned no value.");
		}
		return FreedomUiPageScripts.Read<T>(json);
	}

	private static string DescribeScriptError(JsonElement details) {
		if (details.TryGetProperty("exception", out JsonElement exception)
			&& exception.TryGetProperty("description", out JsonElement description)) {
			return description.GetString();
		}
		return details.TryGetProperty("text", out JsonElement text) ? text.GetString() : "unknown error";
	}

	// While the page navigates its execution context is replaced, and an evaluation in flight fails; that is not an
	// error, just "not yet".
	private static async Task<T> TryEvaluateAsync<T>(ICdpSession cdp, string script, CancellationToken ct)
		where T : class {
		try {
			return await EvaluateAsync<T>(cdp, script, ct).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is (InvalidOperationException and not CdpSessionClosedException)
			or CdpCommandException or JsonException) {
			return null;
		}
	}

	// Keeps the reply under MaxReplyBytes by cutting, in order: field values, then the field list, the button list and the
	// error list by half each round.
	internal static PageSmokeResult Fit(PageSmokeResult result) {
		if (Size(result) <= MaxReplyBytes) {
			return result;
		}
		if (result.Fields is { Count: > 0 }) {
			result.Fields = result.Fields.Select(field => field with { Value = null }).ToList();
			result.Warnings.Add("Field values were dropped to keep the reply small.");
		}
		int before = Count(result);
		for (int round = 0; round < 6 && Size(result) > MaxReplyBytes; round++) {
			int fields = result.Fields?.Count ?? 0;
			int buttons = result.Buttons?.Count ?? 0;
			result.Fields = Half(result.Fields);
			result.Buttons = Half(result.Buttons);
			result.FieldsOmitted += fields - (result.Fields?.Count ?? 0);
			result.ButtonsOmitted += buttons - (result.Buttons?.Count ?? 0);
			result.ConsoleErrors = Half(result.ConsoleErrors.ToList());
			if (result.Click is not null) {
				result.Click.Candidates = Half(result.Click.Candidates);
				result.Click.InvalidFields = Half(result.Click.InvalidFields);
				result.Click.Created = Half(result.Click.Created);
				result.Click.ConsoleErrors = result.Click.ConsoleErrors is null ? null : Half(result.Click.ConsoleErrors.ToList());
			}
		}
		int cut = before - Count(result);
		if (cut > 0) {
			result.Warnings.Add($"{cut} list item(s) were cut to keep the reply under {MaxReplyBytes / 1000} KB.");
		}
		return result;
	}

	private static int Count(PageSmokeResult result) =>
		(result.Fields?.Count ?? 0) + (result.Buttons?.Count ?? 0) + result.ConsoleErrors.Count
		+ (result.Click?.Candidates?.Count ?? 0) + (result.Click?.InvalidFields?.Count ?? 0)
		+ (result.Click?.Created?.Count ?? 0) + (result.Click?.ConsoleErrors?.Count ?? 0);

	private static List<T> Half<T>(List<T> items) => items is null || items.Count <= 1 ? items : items.Take(items.Count / 2).ToList();

	private static int Size(PageSmokeResult result) => JsonSerializer.SerializeToUtf8Bytes(result).Length;
}
