using System.Collections.Specialized;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Clio.Mcp.E2E.Support.Creatio;

/// <summary>
/// Behaviour the stub applies to an incoming <c>SelectQuery</c>.
/// </summary>
internal enum CreatioWedgeStubMode {
	/// <summary>Answer immediately (after the configured global delay) with a valid one-row result.</summary>
	Healthy = 0,

	/// <summary>
	/// Accept the request and NEVER write anything — not even response headers. This is the wedged-backend
	/// case: the client's socket read blocks forever, so a call with no transport bound never returns.
	/// </summary>
	StallHeaders = 1,

	/// <summary>
	/// Write 200 + <c>Content-Length: 100000</c> + a partial JSON prefix, flush, then stall. Proves whether
	/// the client's timeout covers the response READ, not only the header exchange.
	/// </summary>
	StallBody = 2
}

/// <summary>
/// Deterministic Creatio stub for the ENG-95262 tenant-wedge regression: forms-auth login plus
/// <c>SelectQuery</c>, with REQUEST COUNTERS and controllable stall behaviour. It is the C# port of the
/// reproduction lab's <c>stub_creatio.py</c> (branch <c>spike/eng-95262-lab</c>).
/// </summary>
/// <remarks>
/// <para>
/// The counters are the point. The wedge's signature is <em>a call that returns at the deadline having
/// never issued an HTTP request</em>, and a timing assertion cannot see that: the wedged system also
/// finishes at the deadline. Only <see cref="SelectCount"/> distinguishes "answered" from "never asked",
/// which is why the test plan mandates asserting on counter deltas rather than elapsed time.
/// </para>
/// <para>
/// <b>Each accepted request is dispatched to its own task</b>, unlike every other in-repo loopback stub
/// (which responds inline in the accept loop). That is mandatory here, not a style choice: a stalled
/// <c>SelectQuery</c> handled inline would block the accept loop, so the second call could not even reach
/// the stub and the wedge would be unobservable — the test would go green for the wrong reason. The Python
/// original used <c>ThreadingHTTPServer</c> for exactly this reason.
/// </para>
/// <para>
/// Stall switches are GLOBAL server-side state on purpose. clio builds its own request URLs, so a
/// per-request query flag is unreachable from the MCP call path; the mode has to live on the server.
/// </para>
/// <para>
/// Two platform facts carried over from the lab:
/// </para>
/// <list type="bullet">
/// <item><description>
/// The <c>0/</c> WebAppAlias prefix is mandatory on the DataService path for <c>.NET Framework</c>
/// environments (<c>ServiceUrlBuilder.Build</c> prepends it when <c>IsNetCore = false</c>), while
/// <c>AuthService.svc/Login</c> is served at the SITE ROOT on both runtimes. Both routes are therefore
/// matched by path SUFFIX / SUBSTRING so the prefixed and unprefixed forms both hit.
/// </description></item>
/// <item><description>
/// The two authentication cookies must arrive as TWO SEPARATE <c>Set-Cookie</c> headers, never
/// comma-joined into one. Verified against this exact <see cref="HttpListener"/> on macOS:
/// <c>Headers.Add("Set-Cookie", …)</c> twice emits two headers, but
/// <c>Response.Cookies.Add(new Cookie(…))</c> emits a single
/// <c>Set-Cookie: .ASPXAUTH=…; Path=/, BPMCSRF=…; Path=/</c>. Do NOT "simplify" this to the
/// <see cref="HttpListenerResponse.Cookies"/> collection.
/// </description></item>
/// </list>
/// </remarks>
internal sealed class CreatioWedgeStubServer : IAsyncDisposable {
	private const string LoginPathSuffix = "/AuthService.svc/Login";
	private const string SelectQueryPathMarker = "SelectQuery";
	private const string CountersPath = "/counters";
	private const string ResetPath = "/reset";
	private const string ControlPath = "/control";
	private const string PingPathSuffix = "/ping";

	/// <summary>Name of the forms-auth session cookie clio's application client harvests.</summary>
	private const string SessionCookieName = ".ASPXAUTH";

	private readonly HttpListener _listener;
	private readonly CancellationTokenSource _cancellation = new();
	private readonly Task _acceptLoop;
	private readonly object _sync = new();

	// Requests parked by a stall mode. The accept loop is the only writer, so teardown joins it first
	// and only then aborts these — the same single-writer discipline the read-deadline fixture uses.
	private readonly List<HttpListenerResponse> _stalledResponses = [];

	private readonly List<string> _observedSelectSessions = [];
	private readonly List<string> _observedSelectAuthorizationHeaders = [];
	private readonly List<string> _observedLoginPrincipals = [];
	private readonly List<string> _unexpectedHandlerFailures = [];
	private int _loginCount;
	private int _selectCount;
	private CreatioWedgeStubMode _mode = CreatioWedgeStubMode.Healthy;
	private TimeSpan _selectDelay = TimeSpan.Zero;
	private TimeSpan _loginDelay = TimeSpan.FromMilliseconds(200);

	private CreatioWedgeStubServer(HttpListener listener, string baseUrl) {
		_listener = listener;
		BaseUrl = baseUrl;
		_acceptLoop = Task.Run(AcceptLoopAsync);
	}

	/// <summary>Loopback base URL to register as the environment <c>Uri</c>, without a trailing slash.</summary>
	public string BaseUrl { get; }

	/// <summary>Number of <c>AuthService.svc/Login</c> requests received since the last reset.</summary>
	public int LoginCount {
		get {
			lock (_sync) {
				return _loginCount;
			}
		}
	}

	/// <summary>
	/// Number of <c>SelectQuery</c> requests received since the last reset. This is the counter the wedge
	/// assertions sample before and after every call: a delta of zero means the call never reached the
	/// network.
	/// </summary>
	public int SelectCount {
		get {
			lock (_sync) {
				return _selectCount;
			}
		}
	}

	/// <summary>
	/// The forms-auth session token carried by every observed <c>SelectQuery</c>, in arrival order. Each
	/// login mints a fresh token, so this is what proves a later call ran on a session DISTINCT from the
	/// stalled one instead of reusing it — the half of TC-E-601 that "a request was issued" cannot show.
	/// A request that carried no session cookie is recorded as <c>&lt;none&gt;</c>.
	/// </summary>
	public IReadOnlyList<string> ObservedSelectSessions {
		get {
			lock (_sync) {
				return [.. _observedSelectSessions];
			}
		}
	}

	/// <summary>
	/// The <c>Authorization</c> header carried by every observed <c>SelectQuery</c>, in arrival order; a
	/// request that carried none is recorded as <c>&lt;none&gt;</c>.
	/// </summary>
	/// <remarks>
	/// This is the identity assertion's only trustworthy witness (ENG-95262 TC-E-302). "The call succeeded" is
	/// explicitly insufficient: a bearer principal that silently fell back to the <c>Supervisor</c>
	/// login/password default succeeds just as well, and the two are distinguishable only by what the request
	/// actually presented — a <c>Bearer</c> header for the delegated principal, versus a forms-auth cookie for
	/// the fallback.
	/// </remarks>
	public IReadOnlyList<string> ObservedSelectAuthorizationHeaders {
		get {
			lock (_sync) {
				return [.. _observedSelectAuthorizationHeaders];
			}
		}
	}

	/// <summary>
	/// The <c>UserName</c> presented by every observed forms-auth login, in arrival order; a body that carried
	/// none is recorded as <c>&lt;none&gt;</c> and an unparseable one as <c>&lt;unparsed&gt;</c>.
	/// </summary>
	/// <remarks>
	/// The other half of the identity witness (ENG-95262 TC-E-302). clio's client construction falls back to
	/// <c>Login ?? "Supervisor"</c>, so "which principal logged in" is the only observation that distinguishes a
	/// correctly delegated call from that fallback — and the fallback SUCCEEDS, which is why no assertion on the
	/// call's outcome can substitute for this one.
	/// </remarks>
	public IReadOnlyList<string> ObservedLoginPrincipals {
		get {
			lock (_sync) {
				return [.. _observedLoginPrincipals];
			}
		}
	}

	/// <summary>
	/// Starts the stub on an ephemeral loopback port, retrying a few times on a port collision (the same
	/// start shape the other in-repo loopback stubs use).
	/// </summary>
	public static CreatioWedgeStubServer Start() {
		for (int attempt = 0; attempt < 5; attempt++) {
			int port = Random.Shared.Next(20_000, 60_000);
			HttpListener listener = new();
			listener.Prefixes.Add($"http://127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}/");
			try {
				listener.Start();
				return new CreatioWedgeStubServer(
					listener,
					$"http://127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}");
			} catch (HttpListenerException) {
				listener.Close();
			}
		}

		throw new InvalidOperationException("Unable to start the Creatio wedge stub server on a loopback port.");
	}

	/// <summary>
	/// Sets the behaviour applied to subsequent <c>SelectQuery</c> requests. In-process equivalent of
	/// <c>POST /control?stall=…</c>, so a test never has to HTTP-call its own stub.
	/// </summary>
	public void SetMode(CreatioWedgeStubMode mode) {
		lock (_sync) {
			_mode = mode;
		}
	}

	/// <summary>
	/// Sets the delay applied to a healthy <c>SelectQuery</c> and to the generic catch-all, so a long
	/// operation can be simulated without a real Creatio build. In-process equivalent of
	/// <c>POST /control?delay=…</c>.
	/// </summary>
	public void SetSelectDelay(TimeSpan delay) {
		lock (_sync) {
			_selectDelay = delay;
		}
	}

	/// <summary>
	/// Sets the login delay. A real cold login was measured at 3.6-19.4 s; the default 200 ms is enough to
	/// expose a login stampede without making the run long. In-process equivalent of
	/// <c>POST /control?login_delay=…</c>.
	/// </summary>
	public void SetLoginDelay(TimeSpan delay) {
		lock (_sync) {
			_loginDelay = delay;
		}
	}

	/// <summary>Zeroes the counters and clears the observed session list. Equivalent of <c>POST /reset</c>.</summary>
	public void ResetCounters() {
		lock (_sync) {
			_loginCount = 0;
			_selectCount = 0;
			_observedSelectSessions.Clear();
			_observedSelectAuthorizationHeaders.Clear();
			_observedLoginPrincipals.Clear();
		}
	}

	/// <summary>
	/// Exceptions a request handler failed with for a reason this fixture does not expect (a torn-down socket
	/// and a cancelled delay are expected and not recorded). A handler runs on an abandoned task, so without
	/// this the stub could break silently and the fixture would go green for the wrong reason — every
	/// assertion message therefore carries <see cref="DescribeState"/>, which includes this list.
	/// </summary>
	public IReadOnlyList<string> UnexpectedHandlerFailures {
		get {
			lock (_sync) {
				return [.. _unexpectedHandlerFailures];
			}
		}
	}

	/// <summary>A single-line snapshot of the stub's counters and switches, for assertion diagnostics.</summary>
	public string DescribeState() {
		lock (_sync) {
			string failures = _unexpectedHandlerFailures.Count == 0
				? string.Empty
				: $", handler-failures=[{string.Join(" | ", _unexpectedHandlerFailures)}]";
			return $"login={_loginCount.ToString(CultureInfo.InvariantCulture)}, "
				+ $"select={_selectCount.ToString(CultureInfo.InvariantCulture)}, "
				+ $"mode={_mode}, "
				+ $"select-sessions=[{string.Join(", ", _observedSelectSessions)}], "
				+ $"select-authorization=[{string.Join(", ", _observedSelectAuthorizationHeaders)}], "
				+ $"login-principals=[{string.Join(", ", _observedLoginPrincipals)}]"
				+ failures;
		}
	}

	private async Task AcceptLoopAsync() {
		while (!_cancellation.IsCancellationRequested) {
			HttpListenerContext context;
			try {
				context = await _listener.GetContextAsync().WaitAsync(_cancellation.Token).ConfigureAwait(false);
			} catch (OperationCanceledException) {
				return;
			} catch (HttpListenerException) {
				return;
			} catch (ObjectDisposedException) {
				return;
			} catch (InvalidOperationException) {
				return;
			}

			// THE MANDATORY DIVERGENCE from the other in-repo stubs: dispatch to its own task instead of
			// responding inline. A stalled SelectQuery must not block the accept loop, or the second
			// concurrent call never reaches the stub and the wedge cannot be observed at all.
			_ = Task.Run(() => RespondAsync(context));
		}
	}

	private async Task RespondAsync(HttpListenerContext context) {
		try {
			string path = context.Request.Url?.AbsolutePath ?? string.Empty;
			string requestBody = await DrainRequestBodyAsync(context).ConfigureAwait(false);

			if (string.Equals(path, CountersPath, StringComparison.Ordinal)) {
				await WriteJsonAsync(context, BuildCountersPayload()).ConfigureAwait(false);
				return;
			}

			if (string.Equals(path, ResetPath, StringComparison.Ordinal)) {
				ResetCounters();
				await WriteJsonAsync(context, new JsonObject { ["ok"] = true }).ConfigureAwait(false);
				return;
			}

			if (string.Equals(path, ControlPath, StringComparison.Ordinal)) {
				ApplyControlQuery(context.Request.QueryString);
				await WriteJsonAsync(context, BuildStatePayload()).ConfigureAwait(false);
				return;
			}

			if (path.EndsWith(LoginPathSuffix, StringComparison.Ordinal)) {
				await RespondToLoginAsync(context, requestBody).ConfigureAwait(false);
				return;
			}

			if (path.Contains(SelectQueryPathMarker, StringComparison.Ordinal)) {
				await RespondToSelectQueryAsync(context).ConfigureAwait(false);
				return;
			}

			if (path.EndsWith(PingPathSuffix, StringComparison.Ordinal)) {
				await WriteJsonAsync(context, new JsonObject { ["ok"] = true }).ConfigureAwait(false);
				return;
			}

			// Generic fallback for every other endpoint clio probes (runtime detection, build/compile).
			// Honours the global delay so a long operation is simulable without a real Creatio build.
			TimeSpan fallbackDelay;
			lock (_sync) {
				fallbackDelay = _selectDelay;
			}
			if (fallbackDelay > TimeSpan.Zero) {
				await Task.Delay(fallbackDelay, _cancellation.Token).ConfigureAwait(false);
			}
			await WriteJsonAsync(context, BuildEmptyRowsPayload()).ConfigureAwait(false);
		} catch (OperationCanceledException) {
			// Teardown cancelled a delay; the response is abandoned on purpose.
		} catch (HttpListenerException) {
			// The client went away (killed clio process, aborted stall) — expected in this fixture.
		} catch (ObjectDisposedException) {
			// The listener was closed while this request was in flight.
		} catch (IOException) {
			// The socket was torn down mid-write — expected when a stalled response is aborted.
		} catch (Exception exception) {
			// Anything else means the STUB is broken, not the system under test. Record it so it reaches the
			// fixture's diagnostics instead of disappearing into an abandoned task.
			lock (_sync) {
				_unexpectedHandlerFailures.Add($"{exception.GetType().Name}: {exception.Message}");
			}
		}
	}

	private async Task RespondToLoginAsync(HttpListenerContext context, string requestBody) {
		int loginNumber;
		TimeSpan loginDelay;
		lock (_sync) {
			loginNumber = ++_loginCount;
			loginDelay = _loginDelay;
			_observedLoginPrincipals.Add(ReadLoginPrincipal(requestBody));
		}
		if (loginDelay > TimeSpan.Zero) {
			await Task.Delay(loginDelay, _cancellation.Token).ConfigureAwait(false);
		}

		// A FRESH session token per login. Reusing one token across logins would make "D ran on a session
		// distinct from A's" unobservable, and that distinction is what separates a new clean session from
		// a reused one (TC-E-601).
		string sessionToken = BuildSessionToken(loginNumber);

		// Two SEPARATE Set-Cookie headers. Headers.Add emits them separately; the Cookies collection
		// comma-joins them into one header, which is the platform trap the lab recorded.
		context.Response.Headers.Add("Set-Cookie", $"{SessionCookieName}={sessionToken}; path=/; HttpOnly");
		context.Response.Headers.Add("Set-Cookie", "BPMCSRF=stub-csrf; path=/");
		JsonObject payload = new() {
			["Code"] = 0,
			["Message"] = string.Empty,
			["Exception"] = null,
			["UserType"] = "General",
			["n"] = loginNumber
		};
		await WriteJsonAsync(context, payload).ConfigureAwait(false);
	}

	private async Task RespondToSelectQueryAsync(HttpListenerContext context) {
		CreatioWedgeStubMode mode;
		TimeSpan selectDelay;
		lock (_sync) {
			_selectCount++;
			_observedSelectSessions.Add(ReadSessionToken(context.Request));
			_observedSelectAuthorizationHeaders.Add(ReadAuthorizationHeader(context.Request));
			mode = _mode;
			selectDelay = _selectDelay;
		}

		if (mode == CreatioWedgeStubMode.StallHeaders) {
			// Accept and never write: not even headers. Parked until teardown aborts it.
			Park(context.Response);
			await StallForeverAsync().ConfigureAwait(false);
			return;
		}

		if (mode == CreatioWedgeStubMode.StallBody) {
			context.Response.StatusCode = (int)HttpStatusCode.OK;
			context.Response.ContentType = "application/json; charset=utf-8";
			context.Response.ContentLength64 = 100_000;
			byte[] prefix = Encoding.UTF8.GetBytes("{\"success\":true,\"rows\":[");
			await context.Response.OutputStream.WriteAsync(prefix).ConfigureAwait(false);
			await context.Response.OutputStream.FlushAsync().ConfigureAwait(false);
			Park(context.Response);
			await StallForeverAsync().ConfigureAwait(false);
			return;
		}

		if (selectDelay > TimeSpan.Zero) {
			await Task.Delay(selectDelay, _cancellation.Token).ConfigureAwait(false);
		}
		await WriteJsonAsync(context, BuildOneRowPayload()).ConfigureAwait(false);
	}

	// Never returns until teardown cancels. The caller ABANDONS this task; DisposeAsync must not await it.
	private async Task StallForeverAsync() =>
		await Task.Delay(Timeout.InfiniteTimeSpan, _cancellation.Token).ConfigureAwait(false);

	private void Park(HttpListenerResponse response) {
		lock (_sync) {
			_stalledResponses.Add(response);
		}
	}

	private void ApplyControlQuery(NameValueCollection query) {
		string? stall = query["stall"];
		if (!string.IsNullOrWhiteSpace(stall)) {
			SetMode(ParseBoolean(stall) ? CreatioWedgeStubMode.StallHeaders : CreatioWedgeStubMode.Healthy);
		}
		string? stallBody = query["stall_body"];
		if (!string.IsNullOrWhiteSpace(stallBody) && ParseBoolean(stallBody)) {
			SetMode(CreatioWedgeStubMode.StallBody);
		}
		string? delay = query["delay"];
		if (TryParseSeconds(delay, out TimeSpan parsedDelay)) {
			SetSelectDelay(parsedDelay);
		}
		string? loginDelay = query["login_delay"];
		if (TryParseSeconds(loginDelay, out TimeSpan parsedLoginDelay)) {
			SetLoginDelay(parsedLoginDelay);
		}
	}

	private static bool ParseBoolean(string value) =>
		value.Equals("1", StringComparison.OrdinalIgnoreCase)
		|| value.Equals("true", StringComparison.OrdinalIgnoreCase)
		|| value.Equals("yes", StringComparison.OrdinalIgnoreCase);

	private static bool TryParseSeconds(string? value, out TimeSpan parsed) {
		if (!string.IsNullOrWhiteSpace(value)
			&& double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)
			&& seconds >= 0) {
			parsed = TimeSpan.FromSeconds(seconds);
			return true;
		}

		parsed = TimeSpan.Zero;
		return false;
	}

	private static string BuildSessionToken(int loginNumber) =>
		$"stub-session-{loginNumber.ToString(CultureInfo.InvariantCulture)}";

	private static string ReadLoginPrincipal(string requestBody) {
		if (string.IsNullOrWhiteSpace(requestBody)) {
			return "<none>";
		}
		try {
			JsonNode? parsed = JsonNode.Parse(requestBody);
			JsonNode? userName = parsed?["UserName"];
			return userName is null ? "<none>" : userName.ToString();
		} catch (JsonException) {
			return "<unparsed>";
		}
	}

	private static string ReadAuthorizationHeader(HttpListenerRequest request) {
		string? header = request.Headers["Authorization"];
		return string.IsNullOrWhiteSpace(header) ? "<none>" : header.Trim();
	}

	private static string ReadSessionToken(HttpListenerRequest request) {
		string? rawCookieHeader = request.Headers["Cookie"];
		if (string.IsNullOrWhiteSpace(rawCookieHeader)) {
			return "<none>";
		}
		foreach (string pair in rawCookieHeader.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)) {
			int separator = pair.IndexOf('=', StringComparison.Ordinal);
			if (separator > 0
				&& pair[..separator].Trim().Equals(SessionCookieName, StringComparison.OrdinalIgnoreCase)) {
				return pair[(separator + 1)..].Trim();
			}
		}

		return "<none>";
	}

	private JsonObject BuildCountersPayload() {
		lock (_sync) {
			JsonArray sessions = [];
			foreach (string session in _observedSelectSessions) {
				sessions.Add(session);
			}
			return new JsonObject {
				["login"] = _loginCount,
				["select"] = _selectCount,
				["select-sessions"] = sessions
			};
		}
	}

	private JsonObject BuildStatePayload() {
		lock (_sync) {
			return new JsonObject {
				["mode"] = _mode.ToString(),
				["delay"] = _selectDelay.TotalSeconds,
				["login_delay"] = _loginDelay.TotalSeconds
			};
		}
	}

	private static JsonObject BuildOneRowPayload() =>
		new() {
			["success"] = true,
			["rows"] = new JsonArray {
				new JsonObject {
					["Id"] = "00000000-0000-0000-0000-000000000001",
					["Name"] = "UsrStubPage",
					["UId"] = "00000000-0000-0000-0000-000000000002",
					["PackageName"] = "UsrStubPackage",
					["ParentSchemaName"] = "BasePage"
				}
			}
		};

	private static JsonObject BuildEmptyRowsPayload() =>
		new() {
			["success"] = true,
			["rows"] = new JsonArray()
		};

	// Returns the request body as text. It is read (rather than merely drained) because the forms-auth login
	// body is the only place the presented principal appears.
	private static async Task<string> DrainRequestBodyAsync(HttpListenerContext context) {
		if (!context.Request.HasEntityBody) {
			return string.Empty;
		}
		using MemoryStream buffer = new();
		await context.Request.InputStream.CopyToAsync(buffer).ConfigureAwait(false);
		return Encoding.UTF8.GetString(buffer.ToArray());
	}

	private static async Task WriteJsonAsync(HttpListenerContext context, JsonNode payload) {
		byte[] body = Encoding.UTF8.GetBytes(payload.ToJsonString(new JsonSerializerOptions {
			WriteIndented = false
		}));
		context.Response.StatusCode = (int)HttpStatusCode.OK;
		context.Response.ContentType = "application/json; charset=utf-8";
		context.Response.ContentLength64 = body.Length;
		await context.Response.OutputStream.WriteAsync(body).ConfigureAwait(false);
		context.Response.Close();
	}

	/// <inheritdoc />
	/// <remarks>
	/// The stall handlers never return, so this ABANDONS them instead of awaiting them. The accept loop is
	/// joined FIRST because it is the only writer to the parked-response list, which turns the subsequent
	/// iteration into a single-threaded read and removes the latent <see cref="List{T}"/> data race.
	/// </remarks>
	public async ValueTask DisposeAsync() {
		await _cancellation.CancelAsync().ConfigureAwait(false);
		try {
			_listener.Stop();
		} catch (ObjectDisposedException) {
			// Already stopped.
		}

		try {
			await _acceptLoop.ConfigureAwait(false);
		} catch (OperationCanceledException) {
			// Expected on teardown.
		} catch (HttpListenerException) {
			// Expected once the listener stops.
		}

		HttpListenerResponse[] parked;
		lock (_sync) {
			parked = [.. _stalledResponses];
			_stalledResponses.Clear();
		}
		foreach (HttpListenerResponse response in parked) {
			try {
				response.Abort();
			} catch (Exception) {
				// Test cleanup must not hide assertion failures.
			}
		}

		try {
			_listener.Close();
		} catch (ObjectDisposedException) {
			// Already closed.
		}

		_cancellation.Dispose();
	}
}
