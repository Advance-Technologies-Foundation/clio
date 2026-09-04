using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Allure.Net.Commons;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Relay;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Creatio;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// The tenant-wedge anchor scenario (ENG-95262, test plan §4 / TC-E-601): the C# port of the reproduction
/// lab's <c>mcp_wedge_harness.py</c>, driving four <c>list-pages</c> calls (A, B, C, D) through the real
/// <c>clio mcp-server</c> against a deterministic Creatio stub.
/// </summary>
/// <remarks>
/// <para>
/// <b>TC-E-604 is carried by call D, not by a test of its own.</b> "The environment recovers as soon as
/// the backend does, with no restart" is exactly what D asserts: the stub is un-stalled, and the SAME
/// long-lived <c>clio mcp-server</c> that has just taken three bounded calls answers D with its own
/// backend request, on a session A never touched. A second fixture would restate that on the same host
/// and the same stub, and two tests asserting one property is how one of them silently stops asserting it.
/// </para>
/// <para>
/// <b>This fixture asserts on BACKEND REQUEST COUNTERS, never on elapsed time.</b> That is the whole point
/// of it. The defect's signature is a call that returns at the read deadline having never issued an HTTP
/// request, and a timing assertion cannot see that: the wedged system also finishes at the deadline. Only
/// the stub's <c>SelectQuery</c> counter distinguishes "answered" from "never asked", so every call samples
/// the counter immediately before and after and asserts the DELTA.
/// </para>
/// <para>
/// The worker execution boundary does not exist yet, so today the fixture DOCUMENTS THE DEFECT: it asserts
/// the master-today column of the test plan §4 table — A issues 1 backend request, and B, C and D issue
/// ZERO, D even though the backend is healthy again. Building the measuring instrument before the fix lets
/// the instrument be proven: one that cannot reproduce the defect cannot certify its repair.
/// </para>
/// <para>
/// Stage 6 flips the expected column at ONE place — see <c>Expected</c> below. The master-today expectation
/// stays as a named constant for the flag-off / byte-identical-behaviour half of TC-E-602 rather than being
/// deleted.
/// </para>
/// <para>
/// <c>list-pages</c> is load-bearing, not incidental: it takes the per-tenant monitor and therefore
/// reproduces. <c>list-packages</c> — the lab harness's own default <c>--tool</c> — takes no lock and does
/// NOT reproduce, so a literal port of the lab defaults would produce a test that never sees the wedge.
/// </para>
/// </remarks>
[TestFixture]
[AllureNUnit]
[NonParallelizable]
public sealed class McpWorkerWedgeE2ETests {
	private const string ListPagesToolName = PageListTool.ToolName;
	private const string EnvironmentName = "wedge-stub-e2e";

	/// <summary>
	/// Allure tag for the harness self-check, whose subject is the measuring instrument rather than a clio
	/// command or MCP tool.
	/// </summary>
	private const string StubHarnessAllureTag = "mcp-worker-wedge-stub";

	/// <summary>
	/// Both deadline budgets handed to the child. The test plan's anchor uses 12 s; the calls are not
	/// asserted on time, so this only bounds how long the run takes.
	/// </summary>
	private static readonly TimeSpan Budget = TimeSpan.FromSeconds(12);

	/// <summary>Delay before call B, so call A certainly owns the per-tenant monitor first.</summary>
	private static readonly TimeSpan CallBDelay = TimeSpan.FromSeconds(1.5);

	// ═══════════════════════════════════════════════════════════════════════════════════════════════════
	// STAGE-6 FLIPPED (TC-E-601). The worker path is wired and list-pages is a cohort member, so the
	// acceptance column is what this fixture now requires. WedgeExpectation.MasterToday is KEPT as the
	// record of the defect it used to document — deleting it would erase the only statement of what was
	// wrong. Nothing else in this fixture encodes which column is expected.
	// ═══════════════════════════════════════════════════════════════════════════════════════════════════
	private static readonly WedgeExpectation Expected = WedgeExpectation.AfterStage6;

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Drives the ENG-95262 wedge sequence (A stalls, B overlaps A, C after both returned, D with the backend healthy again) through the real clio MCP server against a deterministic Creatio stub, and asserts each call's SelectQuery request-counter DELTA — proving on master today that A issues one backend request while B, C and D issue none, so the environment never recovers.")]
	[AllureFeature(ListPagesToolName)]
	[AllureTag(ListPagesToolName)]
	[AllureName("Stalled tool call wedges the environment: later calls return at the deadline having issued no backend request")]
	[AllureDescription("Points a registered environment at a deterministic Creatio stub that counts login and SelectQuery requests and can stall a SelectQuery without ever answering. Issues four list-pages calls — A (stalls), B (concurrent, +1.5 s), C (strictly after A and B returned) and D (backend un-stalled) — sampling the stub's request counter immediately before and after each call. Asserts the counter DELTAS, never elapsed time, because the wedged system also returns at the deadline: only the counter distinguishes a call that reached the network from one that died queued behind abandoned work. Today it documents the defect (A=1, B=C=D=0); Stage 6 flips the single expectation constant to require D to succeed with its own request on a session distinct from A's.")]
	public async Task WedgeSequence_Should_Match_The_Expected_RequestCounter_Column() {
		// Arrange
		await using CreatioWedgeStubServer stub = CreatioWedgeStubServer.Start();
		string tempHome = Path.Combine(Path.GetTempPath(), $"clio-wedge-e2e-{Guid.NewGuid():N}");
		Directory.CreateDirectory(tempHome);
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string homeVariableName = OperatingSystem.IsWindows() ? "LOCALAPPDATA" : "HOME";
		settings.ProcessEnvironmentVariables[homeVariableName] = tempHome;
		// TestConfiguration.Load() injects the ASSEMBLY-SHARED CLIO_HOME. Overwriting it here is what keeps
		// the settings replacement below inside this fixture's own home: TemporaryClioSettingsOverride
		// resolves the settings path by asking the child clio process, so an inherited shared CLIO_HOME
		// would make it rewrite the home every other fixture reads.
		settings.ProcessEnvironmentVariables["CLIO_HOME"] = tempHome;
		string budgetSeconds = ((int)Budget.TotalSeconds).ToString(CultureInfo.InvariantCulture);
		settings.ProcessEnvironmentVariables[McpReadResponseDeadline.ReadDeadlineOverrideEnvVar] = budgetSeconds;
		settings.ProcessEnvironmentVariables[McpProgressHeartbeat.ResponseDeadlineOverrideEnvVar] = budgetSeconds;
		// THE bound that matters once list-pages routes to a worker. The two deadlines above bound
		// IN-PROCESS work and a relayed call never reaches them: the router answers before the read-deadline
		// wrapper, and the parent bounds the child by KILLING it at this budget instead. Without this the
		// stalled calls would each hold their worker for the 120 s default and the run would exceed its own
		// cancellation window.
		settings.ProcessEnvironmentVariables[McpWorkerCallDispatcher.BudgetOverrideEnvVar] = budgetSeconds;
		// IsNetCore=false pins the .NET Framework DataService route, so the SelectQuery arrives at
		// "0/DataService/json/SyncReply/SelectQuery" — the mandatory WebAppAlias prefix the stub matches by
		// substring. Registering inline rather than through reg-web-app also removes a registration round
		// trip and clio's runtime auto-detection from the reproduction.
		using TemporaryClioSettingsOverride settingsOverride = TemporaryClioSettingsOverride.ReplaceContent(
			$$"""
			{
			  "ActiveEnvironmentKey": "{{EnvironmentName}}",
			  "Environments": {
			    "{{EnvironmentName}}": {
			      "Uri": "{{stub.BaseUrl}}",
			      "Login": "Supervisor",
			      "Password": "Supervisor",
			      "IsNetCore": false
			    }
			  }
			}
			""",
			settings.ClioProcessPath,
			settings.ProcessEnvironmentVariables);
		// Structural guard for the isolation the CLIO_HOME override above buys: if this fixture ever resolved
		// the assembly-shared home instead, it would rewrite the settings file every other fixture reads.
		settingsOverride.AppSettingsPath.Should().StartWith(tempHome,
			because: "the replaced settings file must live in this fixture's own clio home, never the "
				+ "assembly-shared one every other fixture depends on");
		using CancellationTokenSource cancellation = new(TimeSpan.FromMinutes(5));

		List<WedgeCallResult> observed = [];
		// Started BEFORE the host, so nothing a worker does can happen before the instrument is watching.
		await using WorkerSpawnObserver workerObserver = WorkerSpawnObserver.Start(tempHome);
		try {
			// The session is created INSIDE the try so it is disposed at the end of this block — before the
			// stub, whose parked stall must be aborted only once the client is already gone.
			await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellation.Token);
			// Prefetch the advertised tool set once, so the first concurrent call does not race a
			// tools/list request through the middle of the A/B overlap window.
			bool listPagesIsResident = await session.IsToolAdvertisedAsync(ListPagesToolName, cancellation.Token);
			listPagesIsResident.Should().BeTrue(
				because: "list-pages must be reached natively; a clio-run detour would add a dispatch hop to the reproduction");
			stub.ResetCounters();
			stub.SetMode(CreatioWedgeStubMode.StallHeaders);

			// Act
			Stopwatch sequenceClock = Stopwatch.StartNew();
			await AllureApi.Step("A and B overlap: A stalls, B arrives 1.5 s later while A holds the monitor", async () => {
				Task<WedgeCallResult> callA = InvokeAsync(
					session, stub, sequenceClock, "A (stalls)", TimeSpan.Zero, cancellation.Token);
				Task<WedgeCallResult> callB = InvokeAsync(
					session, stub, sequenceClock, "B (same tool, +1.5 s)", CallBDelay, cancellation.Token);
				WedgeCallResult[] overlapped = await Task.WhenAll(callA, callB);
				observed.AddRange(overlapped);
			});
			await AllureApi.Step("C runs strictly after A and B returned: is the environment permanently wedged?", async () =>
				observed.Add(await InvokeAsync(
					session, stub, sequenceClock, "C (after A and B returned)", TimeSpan.Zero, cancellation.Token)));
			await AllureApi.Step("D runs with the backend healthy again: does the environment ever recover?", async () => {
				stub.SetMode(CreatioWedgeStubMode.Healthy);
				observed.Add(await InvokeAsync(
					session, stub, sequenceClock, "D (backend healthy again)", TimeSpan.Zero, cancellation.Token));
			});
			sequenceClock.Stop();

			// Assert
			string table = BuildDiagnosticTable(observed, stub);
			// A request handler runs on an abandoned task, so a broken stub would otherwise be invisible and
			// every counter delta would read as zero — the defect's own signature. Check the instrument first.
			stub.UnexpectedHandlerFailures.Should().BeEmpty(
				because: $"the stub must answer every request it was designed to answer; a handler failure "
					+ $"would make a zero request delta meaningless.{table}");
			observed.Should().HaveCount(4,
				because: $"the anchor sequence is exactly four calls — A, B, C and D.{table}");
			WedgeCallResult resultA = observed[0];
			WedgeCallResult resultB = observed[1];
			WedgeCallResult resultC = observed[2];
			WedgeCallResult resultD = observed[3];

			AllureApi.Step("A issued its own backend request — the stall is genuinely a backend that never answers", () =>
				AssertRequestDelta(resultA, Expected.A, table));
			AllureApi.Step("B, overlapping A, is bounded by the deadline and its request delta matches the expected column", () =>
				AssertRequestDelta(resultB, Expected.B, table));
			AllureApi.Step("C, after A and B returned, shows whether the wedge is permanent", () =>
				AssertRequestDelta(resultC, Expected.C, table));
			AllureApi.Step("TC-E-604: D, with the backend healthy again, shows whether the environment ever recovers — no restart, no new host, the same session that just took three bounded calls", () =>
				AssertRequestDelta(resultD, Expected.D, table));
			AllureApi.Step("Each call's outcome matches the expected column (bounded error today, success after Stage 6)", () => {
				AssertOutcome(resultA, Expected.A, table);
				AssertOutcome(resultB, Expected.B, table);
				AssertOutcome(resultC, Expected.C, table);
				AssertOutcome(resultD, Expected.D, table);
			});
			AllureApi.Step("D ran on a session distinct from A's, where the expected column requires it", () =>
				AssertSessionDistinctFromA(resultA, resultD, Expected.D, table));
			AllureApi.Step("The one legitimate timing bound, secondary to the request counter", () =>
				AssertElapsedBound(resultD, Expected.D, table));
			AllureApi.Step("A worker process was actually SPAWNED — observed in the host's own registry, not inferred from D succeeding", () =>
				AssertWorkersWereSpawned(workerObserver, table));
			AllureApi.Step("TC-E-601b: A was CLEANED UP, not merely outrun — its child is gone and the environment holds no session on its behalf", () =>
				AssertStalledCallWasCleanedUp(workerObserver, resultD, stub, table));
		} finally {
			TryDeleteDirectory(tempHome);
		}
	}

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Drives the deterministic Creatio stub directly over raw HTTP to prove the parts of the measuring instrument the wedge sequence does not exercise: the login response emits the two authentication cookies as SEPARATE Set-Cookie headers, the /control, /counters and /reset endpoints work, stall-body sends headers plus a partial body and then stalls, and healthy mode answers a valid one-row SelectQuery result.")]
	[AllureFeature(ListPagesToolName)]
	[AllureTag(StubHarnessAllureTag)]
	[AllureName("The wedge stub's control surface, cookie shape and stall-body mode behave as the reproduction lab specified")]
	[AllureDescription("The wedge sequence only exercises stall-headers and the in-process counters, so this test covers the rest of the instrument before Stage 6 depends on it. It asserts on the RAW response bytes that login emits two distinct Set-Cookie headers rather than one comma-joined header (HttpListener's Cookies collection comma-joins them, which breaks clio's cookie harvesting), that POST /control switches modes and GET /counters and POST /reset report and clear the counters, that stall-body writes response headers and a partial JSON prefix and then never completes the body, and that healthy mode returns a parseable success payload.")]
	public async Task StubControlSurface_Should_Behave_As_The_Reproduction_Lab_Specified() {
		// Arrange
		await using CreatioWedgeStubServer stub = CreatioWedgeStubServer.Start();
		stub.SetLoginDelay(TimeSpan.Zero);
		using CancellationTokenSource cancellation = new(TimeSpan.FromMinutes(1));
		using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(20) };
		string selectQueryUrl = $"{stub.BaseUrl}/0/DataService/json/SyncReply/SelectQuery";
		string countersUrl = $"{stub.BaseUrl}/counters";

		// Act — drive every branch of the stub's surface once, capturing what each returned. The login goes
		// over a raw socket because HttpClient splits Set-Cookie on commas, which would make one comma-joined
		// header indistinguishable from two separate ones — exactly the distinction under test.
		string loginResponse = await SendRawRequestAsync(
			stub.BaseUrl,
			"POST /0/ServiceModel/AuthService.svc/Login HTTP/1.1",
			cancellation.Token);
		string[] setCookieHeaders = [.. loginResponse
			.Split('\n')
			.Select(line => line.TrimEnd('\r'))
			.Where(line => line.StartsWith("Set-Cookie:", StringComparison.OrdinalIgnoreCase))];
		string healthyBody = await PostForStringAsync(client, selectQueryUrl, cancellation.Token);
		string countersAfterHealthy = await client.GetStringAsync(countersUrl, cancellation.Token);
		using HttpResponseMessage resetResponse = await client.PostAsync(
			new Uri($"{stub.BaseUrl}/reset"), content: null, cancellation.Token);
		string countersAfterReset = await client.GetStringAsync(countersUrl, cancellation.Token);
		using HttpResponseMessage controlResponse = await client.PostAsync(
			new Uri($"{stub.BaseUrl}/control?stall_body=true"), content: null, cancellation.Token);
		using HttpRequestMessage stallRequest = new(HttpMethod.Post, selectQueryUrl) {
			Content = new StringContent("{\"rootSchemaName\":\"SysSchema\"}")
		};
		using HttpResponseMessage stallResponse = await client.SendAsync(
			stallRequest, HttpCompletionOption.ResponseHeadersRead, cancellation.Token);
		Exception? stalledBodyReadFailure = await CaptureStalledBodyReadFailureAsync(stallResponse);
		int selectCountAfterStall = stub.SelectCount;

		// Assert
		AllureApi.Step("Login emits the two authentication cookies as separate Set-Cookie headers", () => {
			// The platform fact from the reproduction lab, asserted on the wire rather than trusted: clio's
			// application client harvests Set-Cookie itself and a single comma-joined header does not parse.
			setCookieHeaders.Should().HaveCount(2,
				because: $"the session and CSRF cookies must arrive as two separate Set-Cookie headers, never "
					+ $"comma-joined into one. Raw response:\n{loginResponse}");
			setCookieHeaders.Should().ContainSingle(header => header.Contains(".ASPXAUTH=", StringComparison.Ordinal),
				because: $"exactly one header must carry the forms-auth session cookie.\n{loginResponse}");
			setCookieHeaders.Should().ContainSingle(header => header.Contains("BPMCSRF=", StringComparison.Ordinal),
				because: $"exactly one header must carry the CSRF cookie.\n{loginResponse}");
		});
		AllureApi.Step("Healthy mode answers a valid one-row SelectQuery result and /counters reports it", () => {
			healthyBody.Should().Contain("\"success\":true",
				because: $"healthy mode must answer the shape clio's SelectQuery parser expects. Body: {healthyBody}");
			countersAfterHealthy.Should().Contain("\"select\":1",
				because: $"the SelectQuery counter is the fixture's measurement instrument and must count the "
					+ $"request. Counters: {countersAfterHealthy}");
			countersAfterHealthy.Should().Contain("\"login\":1",
				because: $"the login counter must count the raw login above. Counters: {countersAfterHealthy}");
		});
		AllureApi.Step("POST /reset zeroes the counters", () => {
			resetResponse.IsSuccessStatusCode.Should().BeTrue(
				because: "the reset endpoint must answer so a manual repro can zero the counters between runs");
			countersAfterReset.Should().Contain("\"select\":0",
				because: $"reset must zero the SelectQuery counter. Counters: {countersAfterReset}");
			countersAfterReset.Should().Contain("\"login\":0",
				because: $"reset must zero the login counter. Counters: {countersAfterReset}");
		});
		AllureApi.Step("stall-body sends headers and a partial body, then never completes it", () => {
			controlResponse.IsSuccessStatusCode.Should().BeTrue(
				because: "POST /control must switch the stall mode so a manual repro does not need in-process access");
			stallResponse.StatusCode.Should().Be(HttpStatusCode.OK,
				because: "stall-body must complete the header exchange, unlike stall-headers");
			stallResponse.Content.Headers.ContentLength.Should().Be(100_000,
				because: "the declared length must exceed the partial prefix, or the client would see a complete body");
			stalledBodyReadFailure.Should().BeAssignableTo<OperationCanceledException>(
				because: "the body never completes, so only the reader's own cancellation ends the read — which "
					+ "is exactly the case a header-only timeout would miss");
			selectCountAfterStall.Should().Be(1,
				because: "a stalled SelectQuery still counts as a request that reached the network");
		});
		AllureApi.Step("The stub itself reported no unexpected handler failure", () =>
			stub.UnexpectedHandlerFailures.Should().BeEmpty(
				because: $"a broken handler would make every counter reading meaningless. "
					+ $"Stub: {stub.DescribeState()}"));
	}

	// ─────────────────────────────────────────────────────────────────────────────────────────────────
	// Invocation and measurement
	// ─────────────────────────────────────────────────────────────────────────────────────────────────

	/// <summary>
	/// Sends a minimal HTTP/1.1 request over a raw socket and returns the response verbatim. Raw bytes are
	/// the only way to assert how many <c>Set-Cookie</c> headers were emitted: <see cref="HttpClient"/>
	/// splits that header on commas, so a comma-joined header and two separate headers are indistinguishable
	/// through its typed header collection.
	/// </summary>
	private static async Task<string> SendRawRequestAsync(
		string baseUrl,
		string requestLine,
		CancellationToken cancellationToken) {
		Uri baseUri = new(baseUrl);
		using TcpClient socket = new();
		await socket.ConnectAsync(baseUri.Host, baseUri.Port, cancellationToken);
		await using NetworkStream stream = socket.GetStream();
		byte[] request = Encoding.ASCII.GetBytes(
			$"{requestLine}\r\nHost: {baseUri.Authority}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
		await stream.WriteAsync(request, cancellationToken);
		using StreamReader reader = new(stream, Encoding.UTF8);
		return await reader.ReadToEndAsync(cancellationToken);
	}

	/// <summary>
	/// Attempts to read a stalled response body under a short cancellation and returns the exception that
	/// ended the read, or <see langword="null"/> if the body unexpectedly completed.
	/// </summary>
	private static async Task<Exception?> CaptureStalledBodyReadFailureAsync(HttpResponseMessage stallResponse) {
		using CancellationTokenSource bodyReadCancellation = new(TimeSpan.FromSeconds(2));
		try {
			await stallResponse.Content.ReadAsStringAsync(bodyReadCancellation.Token);
			return null;
		} catch (Exception exception) {
			return exception;
		}
	}

	private static async Task<string> PostForStringAsync(
		HttpClient client,
		string url,
		CancellationToken cancellationToken) {
		using StringContent content = new("{\"rootSchemaName\":\"SysSchema\"}");
		using HttpResponseMessage response = await client.PostAsync(new Uri(url), content, cancellationToken);
		return await response.Content.ReadAsStringAsync(cancellationToken);
	}

	private static async Task<WedgeCallResult> InvokeAsync(
		McpServerSession session,
		CreatioWedgeStubServer stub,
		Stopwatch sequenceClock,
		string label,
		TimeSpan startDelay,
		CancellationToken cancellationToken) {
		if (startDelay > TimeSpan.Zero) {
			await Task.Delay(startDelay, cancellationToken);
		}

		// Sample the counter IMMEDIATELY before and after the call; the delta is the measurement.
		int selectBefore = stub.SelectCount;
		int loginBefore = stub.LoginCount;
		int sessionsBefore = stub.ObservedSelectSessions.Count;
		TimeSpan startedAt = sequenceClock.Elapsed;
		Stopwatch callClock = Stopwatch.StartNew();
		CallToolResult callResult = await session.CallToolRawAsync(
			ListPagesToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = EnvironmentName
				}
			},
			cancellationToken);
		callClock.Stop();
		TimeSpan endedAt = sequenceClock.Elapsed;
		int selectAfter = stub.SelectCount;
		int loginAfter = stub.LoginCount;
		IReadOnlyList<string> sessionsAfter = stub.ObservedSelectSessions;

		return new WedgeCallResult(
			label,
			startedAt,
			endedAt,
			callClock.Elapsed,
			selectAfter - selectBefore,
			loginAfter - loginBefore,
			[.. sessionsAfter.Skip(sessionsBefore)],
			IsBoundedError(callResult),
			callResult.IsError == true,
			IsSuccessfulAnswer(callResult),
			DescribeAnswer(callResult));
	}

	/// <summary>
	/// True when the result is the structured bounded-timeout envelope the read deadline produces
	/// (<c>error-class = creatio-timeout</c>), which is what a wedged or budget-killed call returns.
	/// </summary>
	private static bool IsBoundedError(CallToolResult callResult) =>
		TryReadPayload(callResult, out JsonElement payload)
		&& payload.TryGetProperty("error-class", out JsonElement errorClass)
		&& errorClass.ValueKind == JsonValueKind.String
		&& string.Equals(errorClass.GetString(), "creatio-timeout", StringComparison.Ordinal);

	/// <summary>
	/// True when the tool genuinely answered: a <c>success = true</c> envelope. Asserting the POSITIVE shape
	/// matters because "not a timeout" also covers an auth failure, a parse failure and a
	/// <c>success:false</c> from a broken relay, none of which leave the environment usable.
	/// </summary>
	/// <remarks>
	/// <b>Read from the structured content OR the JSON text block</b>, the way every other envelope parser
	/// in this suite does (see <c>Support/Results/*Envelope.cs</c>). clio's MCP server does not emit
	/// <c>structuredContent</c> at all — verified by hand-driving the server over raw stdio on both protocol
	/// revisions and in both host and worker mode — so a tool's success envelope travels as a text block.
	/// A structured-only predicate was therefore unsatisfiable for a real answer; it went unnoticed only
	/// because the defect column this fixture used to assert never expected one.
	/// </remarks>
	private static bool IsSuccessfulAnswer(CallToolResult callResult) {
		if (callResult.IsError == true) {
			return false;
		}
		return TryReadPayload(callResult, out JsonElement payload)
			&& payload.TryGetProperty("success", out JsonElement success)
			&& success.ValueKind == JsonValueKind.True;
	}

	/// <summary>
	/// Extracts the tool's JSON envelope from whichever channel carries it: structured content when present,
	/// otherwise the first text content block that parses as a JSON object.
	/// </summary>
	private static bool TryReadPayload(CallToolResult callResult, out JsonElement payload) {
		if (callResult.StructuredContent is JsonElement structured
			&& structured.ValueKind == JsonValueKind.Object) {
			payload = structured;
			return true;
		}
		foreach (TextContentBlock block in callResult.Content.OfType<TextContentBlock>()) {
			if (string.IsNullOrWhiteSpace(block.Text)) {
				continue;
			}
			try {
				using JsonDocument document = JsonDocument.Parse(block.Text);
				if (document.RootElement.ValueKind == JsonValueKind.Object) {
					payload = document.RootElement.Clone();
					return true;
				}
			} catch (JsonException) {
				// A prose block, not the envelope; keep looking.
			}
		}
		payload = default;
		return false;
	}

	private static string DescribeAnswer(CallToolResult callResult) {
		// The SOURCE of the rendering is named, not just its text. A relayed worker answer and a
		// parent-built envelope print almost identically, so a run where structuredContent was lost in the
		// relay would otherwise look exactly like one where it arrived (found the hard way).
		string prefix = $"[isError={callResult.IsError?.ToString() ?? "null"}, "
			+ $"structured={callResult.StructuredContent?.GetType().Name ?? "null"}] ";
		if (TryReadPayload(callResult, out JsonElement payload)) {
			return prefix + Shorten(payload.GetRawText());
		}
		string text = string.Join(
			" ",
			callResult.Content.OfType<TextContentBlock>().Select(block => block.Text ?? string.Empty));
		return prefix + Shorten(text);
	}

	// 600 rather than 160: once a call can fail because a CHILD PROCESS failed, the answer carries the
	// worker's own standard-error tail, and that tail is the only evidence of why the child died. Truncating
	// it away leaves a red CI run saying "the worker relay failed" and nothing else.
	private const int AnswerDiagnosticLimit = 600;

	private static string Shorten(string value) {
		string flattened = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
		return flattened.Length <= AnswerDiagnosticLimit
			? flattened
			: $"{flattened[..AnswerDiagnosticLimit]}…";
	}

	// ─────────────────────────────────────────────────────────────────────────────────────────────────
	// Assertions — every one carries the whole observed table, so a TeamCity log distinguishes
	// "the wedge did not reproduce" from "the two calls never overlapped".
	// ─────────────────────────────────────────────────────────────────────────────────────────────────

	private static void AssertRequestDelta(WedgeCallResult result, CallExpectation expectation, string table) {
		result.SelectRequestDelta.Should().BeGreaterThanOrEqualTo(expectation.MinSelectDelta,
			because: $"{result.Label} must issue at least {expectation.MinSelectDelta} backend SelectQuery "
				+ $"request(s) in the '{Expected.Name}' column; a smaller delta means the call died without "
				+ $"reaching the network.{table}");
		if (expectation.MaxSelectDelta is int maxDelta) {
			result.SelectRequestDelta.Should().BeLessThanOrEqualTo(maxDelta,
				because: $"{result.Label} must issue at most {maxDelta} backend SelectQuery request(s) in the "
					+ $"'{Expected.Name}' column — a delta of zero here IS the defect signature, and a "
					+ $"non-zero delta means the wedge did not reproduce (check that A and B really "
					+ $"overlapped: B starting after A returned would issue its own request).{table}");
		}
	}

	private static void AssertOutcome(WedgeCallResult result, CallExpectation expectation, string table) {
		if (expectation.ExpectSuccess) {
			// "Not a timeout" is NOT success. A call that issues a request and then fails on auth, on a parse
			// error, or with success:false from a broken relay would satisfy a not-timed-out assertion while
			// leaving the environment just as unusable, so the positive shape is asserted explicitly.
			result.IsBoundedError.Should().BeFalse(
				because: $"{result.Label} must succeed in the '{Expected.Name}' column instead of returning "
					+ $"the bounded creatio-timeout envelope.{table}");
			result.IsProtocolError.Should().BeFalse(
				because: $"{result.Label} must not come back as an MCP tool error in the '{Expected.Name}' "
					+ $"column.{table}");
			result.IsSuccessfulAnswer.Should().BeTrue(
				because: $"{result.Label} must report success:true with page data in the '{Expected.Name}' "
					+ $"column — a request that reached the backend and then failed for any other reason "
					+ $"leaves the environment just as unusable.{table}");
			return;
		}

		result.IsBoundedError.Should().BeTrue(
			because: $"{result.Label} must return the bounded creatio-timeout envelope in the "
				+ $"'{Expected.Name}' column rather than hanging or answering with data.{table}");
	}

	private static void AssertSessionDistinctFromA(
		WedgeCallResult resultA,
		WedgeCallResult resultD,
		CallExpectation expectation,
		string table) {
		if (!expectation.RequireSessionDistinctFromA) {
			// On master today D issues no request at all, so there is no session to compare — the zero
			// delta already asserted above is the stronger statement.
			resultD.ObservedSessions.Should().BeEmpty(
				because: $"the '{Expected.Name}' column expects D to reach no session at all, so there is no "
					+ $"session token to attribute to it.{table}");
			return;
		}

		resultD.ObservedSessions.Should().NotBeEmpty(
			because: $"D must carry a session token for the distinctness check to mean anything.{table}");
		resultA.ObservedSessions.Should().NotBeEmpty(
			because: $"A's session token is the baseline D must differ from.{table}");
		// "D issued a request" is necessary but not sufficient: it does not distinguish a new clean session
		// from a reused one. No session object may be referenced by both A and D (TC-E-601).
		resultD.ObservedSessions.Should().NotIntersectWith(resultA.ObservedSessions,
			because: $"D must run on a session DISTINCT from A's — a reused session means the recovery came "
				+ $"from the same authenticated context the stalled call poisoned.{table}");
	}

	private static void AssertElapsedBound(WedgeCallResult result, CallExpectation expectation, string table) {
		if (expectation.MaxElapsed is not TimeSpan maxElapsed) {
			return;
		}

		// The ONLY legitimate timing assertion in this fixture, and it is secondary to the request-counter
		// assertion above: both the wedged and the fixed system return at the deadline, so time alone
		// proves nothing. This bound only says a recovered call is fast, once the counter has already said
		// it happened at all.
		result.Elapsed.Should().BeLessThan(maxElapsed,
			because: $"{result.Label} must answer promptly once the backend is healthy again.{table}");
	}

	/// <summary>
	/// TC-E-601 (spawn half): a worker child was OBSERVED, not inferred. "D succeeded quickly" is equally
	/// consistent with the host having executed the call itself, so the assertion has to be about a process.
	/// </summary>
	private static void AssertWorkersWereSpawned(WorkerSpawnObserver observer, string table) {
		string described = observer.Describe();
		// The instrument first: a reader that silently failed would report zero workers, which is the same
		// shape as "the worker path never engaged" — the exact confusion this fixture exists to prevent.
		observer.ReadFailures.Should().BeEmpty(
			because: $"a failed registry read makes an empty observation meaningless. {described}{table}");
		observer.Observed.Should().NotBeEmpty(
			because: $"every one of the four calls names a cohort tool, so the host must have started at "
				+ $"least one child process — and the acceptance criterion is that this is OBSERVED rather "
				+ $"than concluded from a call succeeding. {described}{table}");
		observer.Observed.Select(worker => worker.ProcessId).Distinct().Should().HaveCountGreaterThan(1,
			because: $"the calls are served by SEPARATE short-lived children; a single reused process would "
				+ $"mean the isolation the fix rests on does not exist. {described}{table}");
	}

	/// <summary>
	/// TC-E-601b: the stalled call was cleaned up rather than merely outrun. D succeeding proves the
	/// environment recovered; it does not prove A stopped existing, and a leaked stalled child holding a
	/// live session is the failure this feature would otherwise have moved rather than removed.
	/// </summary>
	private static void AssertStalledCallWasCleanedUp(
		WorkerSpawnObserver observer,
		WedgeCallResult resultD,
		CreatioWedgeStubServer stub,
		string table) {
		string described = observer.Describe();
		observer.ReadCurrent().Should().BeEmpty(
			because: $"every lease is disposed when its call answers, so no worker may still be RECORDED "
				+ $"once the sequence has finished. {described}{table}");
		observer.Observed.Where(worker => worker.IsStillRunning()).Should().BeEmpty(
			because: $"a recorded entry disappearing is not the same as the process dying — the identity "
				+ $"(pid AND start time) of every worker seen during the run must be gone, or the stalled "
				+ $"call was outrun rather than killed. {described}{table}");
		// The environment side of the same statement: A's authenticated session must never be used again.
		// A reused token after D would mean the stalled context was still alive somewhere and had simply
		// stopped blocking, which is a quieter version of the same defect.
		// A's OWN token is the FIRST one the stub ever saw — A is issued at t=0 and B only at +1.5 s.
		// resultA.ObservedSessions cannot be used for this: it is everything seen inside A's twelve-second
		// window, which legitimately includes B's session because B overlaps A on purpose.
		IReadOnlyList<string> allSessions = stub.ObservedSelectSessions;
		allSessions.Should().NotBeEmpty(
			because: $"A must have reached the network for there to be a session to reason about.{table}");
		string sessionA = allSessions[0];
		allSessions.Count(session => string.Equals(session, sessionA, StringComparison.Ordinal))
			.Should().Be(1,
			because: $"A's authenticated session '{sessionA}' must be used exactly once and then die with "
				+ $"its worker; a second request carrying it would mean the poisoned context outlived the "
				+ $"call and had merely stopped blocking.{table}");
		resultD.ObservedSessions.Should().NotContain(sessionA,
			because: $"the recovery must come from a NEW session, not from the one the stalled call "
				+ $"poisoned.{table}");
	}

	private static string BuildDiagnosticTable(IReadOnlyList<WedgeCallResult> observed, CreatioWedgeStubServer stub) {
		StringBuilder builder = new();
		builder.AppendLine();
		builder.AppendLine($"Expected column: {Expected.Name}");
		builder.AppendLine("call                          start     end   elapsed  select  login  sessions  answer");
		foreach (WedgeCallResult result in observed) {
			builder.AppendLine(string.Format(
				CultureInfo.InvariantCulture,
				"{0,-28}{1,7:0.0}s{2,6:0.0}s{3,8:0.0}s{4,8}{5,7}  {6,-9} {7}",
				result.Label,
				result.StartedAt.TotalSeconds,
				result.EndedAt.TotalSeconds,
				result.Elapsed.TotalSeconds,
				result.SelectRequestDelta,
				result.LoginRequestDelta,
				result.ObservedSessions.Count == 0 ? "-" : string.Join("|", result.ObservedSessions),
				result.Answer));
		}
		builder.AppendLine($"stub: {stub.DescribeState()}");
		return builder.ToString();
	}

	private static void TryDeleteDirectory(string path) {
		try {
			if (Directory.Exists(path)) {
				Directory.Delete(path, recursive: true);
			}
		} catch (IOException) {
			// Best-effort cleanup.
		} catch (UnauthorizedAccessException) {
			// Best-effort cleanup.
		}
	}

	// ─────────────────────────────────────────────────────────────────────────────────────────────────
	// Measurement and expectation shapes
	// ─────────────────────────────────────────────────────────────────────────────────────────────────

	/// <summary>What one call of the anchor sequence actually did, measured rather than inferred.</summary>
	/// <param name="Label">Human-readable call name (A, B, C or D) as it appears in the test plan.</param>
	/// <param name="StartedAt">Offset from the start of the sequence at which the call was issued.</param>
	/// <param name="EndedAt">Offset from the start of the sequence at which the call returned.</param>
	/// <param name="Elapsed">Wall time the call itself took.</param>
	/// <param name="SelectRequestDelta">Backend <c>SelectQuery</c> requests observed during the call.</param>
	/// <param name="LoginRequestDelta">Backend login requests observed during the call (diagnostic only).</param>
	/// <param name="ObservedSessions">Session tokens the call's backend requests carried.</param>
	/// <param name="IsBoundedError">Whether the call returned the bounded <c>creatio-timeout</c> envelope.</param>
	/// <param name="IsProtocolError">Whether the call came back as an MCP tool error.</param>
	/// <param name="IsSuccessfulAnswer">Whether the call answered with a structured <c>success = true</c> payload.</param>
	/// <param name="Answer">Shortened rendering of the call's answer, for diagnostics.</param>
	private sealed record WedgeCallResult(
		string Label,
		TimeSpan StartedAt,
		TimeSpan EndedAt,
		TimeSpan Elapsed,
		int SelectRequestDelta,
		int LoginRequestDelta,
		IReadOnlyList<string> ObservedSessions,
		bool IsBoundedError,
		bool IsProtocolError,
		bool IsSuccessfulAnswer,
		string Answer);

	/// <summary>
	/// What one call of the anchor sequence is expected to do. Bounds are deliberately expressed as a
	/// minimum plus an OPTIONAL maximum: after Stage 6 every call issues its own request, so an earlier
	/// call's sampling window legitimately contains a later call's request and an upper bound would be
	/// wrong. The <c>MaxSelectDelta = 0</c> entries are the defect signature itself.
	/// </summary>
	/// <param name="MinSelectDelta">Fewest backend <c>SelectQuery</c> requests the call must issue.</param>
	/// <param name="MaxSelectDelta">Most it may issue, or <see langword="null"/> for unbounded.</param>
	/// <param name="ExpectSuccess">Whether the call must answer with data instead of a bounded error.</param>
	/// <param name="MaxElapsed">Optional wall-time bound; always secondary to the request counter.</param>
	/// <param name="RequireSessionDistinctFromA">Whether the call must run on a session A never touched.</param>
	private sealed record CallExpectation(
		int MinSelectDelta,
		int? MaxSelectDelta,
		bool ExpectSuccess,
		TimeSpan? MaxElapsed = null,
		bool RequireSessionDistinctFromA = false);

	/// <summary>
	/// The two columns of the test plan §4 table. <see cref="MasterToday"/> is the defect as shipped;
	/// <see cref="AfterStage6"/> is the acceptance shape the worker path must produce.
	/// </summary>
	/// <param name="Name">Column name, quoted in every assertion message.</param>
	/// <param name="A">Expectation for call A, which stalls.</param>
	/// <param name="B">Expectation for call B, which overlaps A.</param>
	/// <param name="C">Expectation for call C, issued after A and B returned.</param>
	/// <param name="D">Expectation for call D, issued with the backend healthy again.</param>
	private sealed record WedgeExpectation(
		string Name,
		CallExpectation A,
		CallExpectation B,
		CallExpectation C,
		CallExpectation D) {
		/// <summary>
		/// The defect, as shipped on master: one backend request for four calls, and the environment never
		/// recovers. A and B and C returning at the budget is CORRECT — the backend genuinely is not
		/// answering. D returning zero requests against a healthy backend is the defect.
		/// </summary>
		public static WedgeExpectation MasterToday { get; } = new(
			"master today (the defect)",
			A: new CallExpectation(MinSelectDelta: 1, MaxSelectDelta: 1, ExpectSuccess: false),
			B: new CallExpectation(MinSelectDelta: 0, MaxSelectDelta: 0, ExpectSuccess: false),
			C: new CallExpectation(MinSelectDelta: 0, MaxSelectDelta: 0, ExpectSuccess: false),
			D: new CallExpectation(MinSelectDelta: 0, MaxSelectDelta: 0, ExpectSuccess: false));

		/// <summary>
		/// After Stage 6: every call issues its own backend request and is killed at the parent budget,
		/// and D succeeds quickly on a session A never touched. No upper bound on any delta — concurrent
		/// calls' sampling windows overlap once they all reach the network.
		/// </summary>
		public static WedgeExpectation AfterStage6 { get; } = new(
			"after Stage 6 (worker path)",
			A: new CallExpectation(MinSelectDelta: 1, MaxSelectDelta: null, ExpectSuccess: false),
			B: new CallExpectation(MinSelectDelta: 1, MaxSelectDelta: null, ExpectSuccess: false),
			C: new CallExpectation(MinSelectDelta: 1, MaxSelectDelta: null, ExpectSuccess: false),
			D: new CallExpectation(
				MinSelectDelta: 1,
				MaxSelectDelta: null,
				ExpectSuccess: true,
				// The macOS prototype measured 0.8 s and the plan wrote 2 s from it. That figure cannot
				// hold on the platform this suite actually runs on: child spawn plus MCP `initialize`
				// measured p50 2.763 s on Windows Server 2022 (ADR §2.4, n=8, max 2.904), so a 2 s bound
				// would fail a perfectly healthy Windows run before the tool did any work. 8 s is that
				// measurement plus the call itself plus headroom, and it stays well inside the 12 s budget
				// so a budget-KILLED call can never slip through as a fast one. Widened on a recorded
				// measurement, which is what the rule below asks for — never to make a red run green.
				MaxElapsed: TimeSpan.FromSeconds(8),
				RequireSessionDistinctFromA: true));
	}
}
