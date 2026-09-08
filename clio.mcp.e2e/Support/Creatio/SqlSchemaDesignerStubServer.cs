using System.Globalization;
using System.Net;
using System.Text;

namespace Clio.Mcp.E2E.Support.Creatio;

/// <summary>
/// The unusable answer the stub returns from every <c>ScriptSchemaDesignerService</c> route.
/// </summary>
public enum SqlSchemaDesignerStubResponse {

	/// <summary>
	/// HTTP 404 with a zero-length body — what a Creatio instance that does not serve the SQL-script
	/// designer <c>.svc</c> actually returns (measured on 10.1.725, .NET Framework). This is the exact
	/// shape that produced the bare <c>Error reading JObject from JsonReader</c> message in issue #1322.
	/// </summary>
	EmptyBody = 0,

	/// <summary>HTTP 500 with an IIS-style HTML error page.</summary>
	HtmlErrorPage = 1,

	/// <summary>HTTP 200 with a login page, the shape an expired session redirects to.</summary>
	HtmlLoginPage = 2
}

/// <summary>
/// Minimal Creatio stub for the issue #1322 regression: forms-auth login, a <c>SelectQuery</c> that
/// resolves the target package and reports the schema as absent, and a
/// <c>ScriptSchemaDesignerService</c> that answers with a body clio cannot parse.
/// </summary>
/// <remarks>
/// <para>
/// It is deliberately separate from <c>CreatioWedgeStubServer</c>: that fixture exists to observe stalls
/// through request counters and dispatches every request to its own task for that reason, none of which
/// this regression needs. What it does need is a designer route that answers with a chosen unusable body,
/// which the wedge stub has no notion of.
/// </para>
/// <para>
/// Two platform facts are carried over verbatim from that stub, because getting either wrong makes the
/// fixture fail for the wrong reason: the <c>0/</c> WebAppAlias prefix appears on service paths for
/// <c>.NET Framework</c> environments while <c>AuthService.svc/Login</c> is served at the site root (so
/// routes are matched by suffix/substring), and the two authentication cookies must be emitted as TWO
/// separate <c>Set-Cookie</c> headers via <c>Headers.Add</c> — the <c>Cookies</c> collection comma-joins
/// them into one header, which clio's client does not harvest.
/// </para>
/// </remarks>
internal sealed class SqlSchemaDesignerStubServer : IAsyncDisposable {

	private const string LoginPathSuffix = "/AuthService.svc/Login";
	private const string SelectQueryPathMarker = "SelectQuery";
	private const string DesignerPathMarker = "ScriptSchemaDesignerService";

	/// <summary>The package UId the stub reports for any package name the command asks about.</summary>
	internal const string PackageUId = "a0000000-0000-0000-0000-0000000000ff";

	private readonly HttpListener _listener;
	private readonly CancellationTokenSource _cancellation = new();
	private readonly Task _acceptLoop;
	private readonly SqlSchemaDesignerStubResponse _designerResponse;

	private SqlSchemaDesignerStubServer(
		HttpListener listener, string baseUrl, SqlSchemaDesignerStubResponse designerResponse) {
		_listener = listener;
		BaseUrl = baseUrl;
		_designerResponse = designerResponse;
		_acceptLoop = Task.Run(AcceptLoopAsync);
	}

	/// <summary>Loopback base URL to register as the environment <c>Uri</c>, without a trailing slash.</summary>
	public string BaseUrl { get; }

	/// <summary>Starts the stub on an ephemeral loopback port, retrying on a port collision.</summary>
	/// <param name="designerResponse">The unusable answer every designer route returns.</param>
	/// <returns>The started stub.</returns>
	public static SqlSchemaDesignerStubServer Start(SqlSchemaDesignerStubResponse designerResponse) {
		for (int attempt = 0; attempt < 5; attempt++) {
			int port = Random.Shared.Next(20_000, 60_000);
			HttpListener listener = new();
			listener.Prefixes.Add($"http://127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}/");
			try {
				listener.Start();
				return new SqlSchemaDesignerStubServer(
					listener,
					$"http://127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}",
					designerResponse);
			} catch (HttpListenerException) {
				listener.Close();
			}
		}

		throw new InvalidOperationException("Unable to start the SQL schema designer stub on a loopback port.");
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

			await RespondAsync(context).ConfigureAwait(false);
		}
	}

	private async Task RespondAsync(HttpListenerContext context) {
		try {
			string path = context.Request.Url?.AbsolutePath ?? string.Empty;
			await DrainRequestBodyAsync(context).ConfigureAwait(false);

			if (path.EndsWith(LoginPathSuffix, StringComparison.Ordinal)) {
				context.Response.Headers.Add("Set-Cookie", ".ASPXAUTH=stub-session; path=/; HttpOnly");
				context.Response.Headers.Add("Set-Cookie", "BPMCSRF=stub-csrf; path=/");
				await WriteAsync(context, 200, "application/json",
					"""{"Code":0,"Message":"","Exception":null,"UserType":"General"}""").ConfigureAwait(false);
				return;
			}

			if (path.Contains(DesignerPathMarker, StringComparison.Ordinal)) {
				await WriteDesignerResponseAsync(context).ConfigureAwait(false);
				return;
			}

			if (path.Contains(SelectQueryPathMarker, StringComparison.Ordinal)) {
				await WriteAsync(context, 200, "application/json", BuildSelectQueryPayload(context))
					.ConfigureAwait(false);
				return;
			}

			await WriteAsync(context, 200, "application/json", """{"success":true,"rows":[]}""")
				.ConfigureAwait(false);
		} catch (HttpListenerException) {
			// The client went away — expected when the clio process exits.
		} catch (ObjectDisposedException) {
			// The listener was closed while this request was in flight.
		} catch (IOException) {
			// The socket was torn down mid-write.
		}
	}

	// The command issues two SysPackage/SysSchema selects before it reaches the designer: the package must
	// resolve (or the run fails before the code under test) and the schema must read as absent (or it fails
	// as "already exists"). Both are answered off the same route, told apart by the requested root schema.
	private static string BuildSelectQueryPayload(HttpListenerContext context) {
		string body = context.Request.Headers["X-Clio-Stub-Body"] ?? string.Empty;
		return body.Contains("SysPackage", StringComparison.Ordinal)
			? $$"""{"success":true,"rows":[{"UId":"{{PackageUId}}"}]}"""
			: """{"success":true,"rows":[]}""";
	}

	private async Task WriteDesignerResponseAsync(HttpListenerContext context) {
		switch (_designerResponse) {
			case SqlSchemaDesignerStubResponse.HtmlErrorPage:
				await WriteAsync(context, 500, "text/html",
					"<!DOCTYPE html><html><head><title>Request Error</title></head>"
					+ "<body>Service Unavailable</body></html>").ConfigureAwait(false);
				return;
			case SqlSchemaDesignerStubResponse.HtmlLoginPage:
				await WriteAsync(context, 200, "text/html",
					"<!DOCTYPE html><html><head><title>Login</title></head>"
					+ "<body><form><input name=\"token\" value=\"stub-session-token\"/></form></body></html>")
					.ConfigureAwait(false);
				return;
			default:
				// 404 plus a zero-length body: the measured shape of an unrouted Creatio .svc.
				await WriteAsync(context, 404, null, string.Empty).ConfigureAwait(false);
				return;
		}
	}

	private static async Task WriteAsync(
		HttpListenerContext context, int statusCode, string? contentType, string body) {
		byte[] payload = Encoding.UTF8.GetBytes(body);
		context.Response.StatusCode = statusCode;
		if (contentType is not null) {
			context.Response.ContentType = contentType;
		}
		context.Response.ContentLength64 = payload.Length;
		await context.Response.OutputStream.WriteAsync(payload).ConfigureAwait(false);
		context.Response.OutputStream.Close();
	}

	private static async Task DrainRequestBodyAsync(HttpListenerContext context) {
		using StreamReader reader = new(context.Request.InputStream, Encoding.UTF8);
		string body = await reader.ReadToEndAsync().ConfigureAwait(false);
		// The request body decides which SelectQuery answer is due; stash it where the responder can read it
		// without threading the string through every branch.
		context.Request.Headers["X-Clio-Stub-Body"] = body;
	}

	/// <summary>Stops the listener and joins the accept loop.</summary>
	/// <returns>A task that completes once the stub is shut down.</returns>
	public async ValueTask DisposeAsync() {
		await _cancellation.CancelAsync().ConfigureAwait(false);
		try {
			_listener.Stop();
		} catch (ObjectDisposedException) {
			// Already closed.
		}

		try {
			await _acceptLoop.ConfigureAwait(false);
		} catch (OperationCanceledException) {
			// Expected on teardown.
		}

		_listener.Close();
		_cancellation.Dispose();
	}
}
