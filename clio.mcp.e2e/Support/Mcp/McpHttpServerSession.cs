using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Clio.Mcp.E2E.Support.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E.Support.Mcp;

/// <summary>
/// Manual / live-stand-only harness for the <c>clio mcp-http</c> edge — the ENG-93208 credential-passthrough
/// fixtures (Story 15c/15d/AC-07) and the ENG-93386 standard-OAuth-authorization fixtures (Story 8) both
/// reuse this session. Spawns a single <c>clio mcp-http</c> process bound to loopback and lets a test
/// connect one or more Streamable-HTTP MCP clients to it, each carrying its own <c>Authorization: Bearer
/// &lt;value&gt;</c> (a legacy platform-api-key OR a live OAuth JWT, depending on the fixture) and an
/// optional per-request <c>X-Integration-Credentials</c> header.
/// <para>
/// NOT run in CI. The passthrough fixtures need a live Creatio stand and <c>--platform-api-key</c>
/// (<see cref="McpHttpPassthroughStand"/>); the OAuth fixtures need a live identity-platform Authorization
/// Server and <c>--auth-*</c> flags (<see cref="McpHttpOAuthStand"/>). When the relevant live-stand env
/// vars are absent the fixtures <c>Assert.Ignore</c> before this helper is touched.
/// </para>
/// </summary>
internal sealed class McpHttpServerSession : IAsyncDisposable {
	private readonly Process _process;

	private McpHttpServerSession(Process process, int port) {
		_process = process;
		Port = port;
	}

	/// <summary>The loopback port the spawned <c>clio mcp-http</c> process is listening on.</summary>
	public int Port { get; }

	/// <summary>The Streamable-HTTP MCP endpoint URL for the spawned process.</summary>
	public string EndpointUrl => $"http://127.0.0.1:{Port}/mcp";

	/// <summary>
	/// Spawns a single <c>clio mcp-http</c> process on a free loopback port with the given platform API
	/// key, and waits until it is accepting connections.
	/// </summary>
	/// <param name="settings">e2e process/environment settings.</param>
	/// <param name="platformApiKey">Legacy platform-api-key value, or <see langword="null"/> to omit
	/// <c>--platform-api-key</c> entirely.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <param name="extraArguments">Additional raw CLI arguments appended verbatim (e.g. the
	/// ENG-93386 <c>--auth-*</c> flags for the standard-OAuth e2e fixtures). <see langword="null"/>
	/// (default) adds nothing.</param>
	public static async Task<McpHttpServerSession> StartAsync(
		McpE2ESettings settings, string? platformApiKey, CancellationToken cancellationToken,
		IReadOnlyList<string>? extraArguments = null) {
		int port = FindFreeLoopbackPort();
		List<string> arguments = [
			"mcp-http",
			"--host", "127.0.0.1",
			"--port", port.ToString(CultureInfo.InvariantCulture)
		];
		if (!string.IsNullOrWhiteSpace(platformApiKey)) {
			// Omit --platform-api-key entirely for the no-regression (pre-passthrough) leg (15d).
			arguments.Add("--platform-api-key");
			arguments.Add(platformApiKey);
		}
		if (extraArguments is { Count: > 0 }) {
			arguments.AddRange(extraArguments);
		}
		ClioProcessDescriptor descriptor = ClioExecutableResolver.Resolve(settings, [.. arguments]);

		ProcessStartInfo startInfo = new() {
			FileName = descriptor.Command,
			WorkingDirectory = descriptor.WorkingDirectory,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false
		};
		foreach (string argument in descriptor.Arguments) {
			startInfo.ArgumentList.Add(argument);
		}
		foreach (KeyValuePair<string, string?> variable in settings.ProcessEnvironmentVariables) {
			startInfo.Environment[variable.Key] = variable.Value;
		}

		Process process = Process.Start(startInfo)
			?? throw new InvalidOperationException("Failed to start the clio mcp-http process.");
		try {
			await WaitUntilListeningAsync(port, process, cancellationToken);
		}
		catch {
			await KillAsync(process);
			throw;
		}
		return new McpHttpServerSession(process, port);
	}

	/// <summary>
	/// Connects a fresh Streamable-HTTP MCP client to the running process, presenting
	/// <paramref name="platformApiKey"/> as an <c>Authorization: Bearer</c> value and (optionally) a
	/// per-request <c>X-Integration-Credentials</c> header. Each connection is an independent async
	/// flow and, with distinct credentials, a distinct tenant.
	/// </summary>
	/// <remarks>
	/// Despite the parameter name (kept for compatibility with the ENG-93208 passthrough fixtures),
	/// this value is opaque to the transport — it is placed verbatim on the <c>Authorization</c>
	/// header. The ENG-93386 OAuth e2e fixtures pass a live <c>client_credentials</c> JWT here, not a
	/// legacy platform-api-key.
	/// </remarks>
	public async Task<McpClient> ConnectAsync(
		string? platformApiKey,
		string? integrationCredentialsBase64,
		CancellationToken cancellationToken) {
		Dictionary<string, string> headers = [];
		if (!string.IsNullOrWhiteSpace(platformApiKey)) {
			headers["Authorization"] = $"Bearer {platformApiKey}";
		}
		if (integrationCredentialsBase64 is not null) {
			headers[McpHttpPassthroughStand.CredentialsHeaderName] = integrationCredentialsBase64;
		}

		HttpClientTransport transport = new(new HttpClientTransportOptions {
			Endpoint = new Uri(EndpointUrl),
			TransportMode = HttpTransportMode.StreamableHttp,
			AdditionalHeaders = headers,
			Name = "clio-mcp-http-e2e"
		}, NullLoggerFactory.Instance);

		return await McpClient.CreateAsync(
			transport,
			new McpClientOptions {
				ClientInfo = new Implementation { Name = "clio.mcp.e2e.http", Version = "1.0.0" }
			},
			NullLoggerFactory.Instance,
			cancellationToken);
	}

	/// <summary>
	/// Base64-encodes the JSON <c>X-Integration-Credentials</c> payload for a bearer-token tenant, in the
	/// exact shape <c>CredentialHeaderParser</c> expects (<c>url</c> / <c>accessToken</c> /
	/// <c>accessTokenType</c>).
	/// </summary>
	public static string EncodeBearerCredentials(string url, string accessToken, string authScheme = "Bearer") {
		string json = JsonSerializer.Serialize(new {
			url,
			accessToken,
			accessTokenType = authScheme
		});
		return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
	}

	/// <summary>
	/// Base64-encodes the JSON <c>X-Integration-Credentials</c> payload for a forms-auth (on-prem,
	/// <c>IsNetCore=false</c>) tenant that has no OAuth access token, in the exact shape
	/// <c>CredentialHeaderParser</c> expects (<c>url</c> / <c>login</c> / <c>password</c>).
	/// </summary>
	public static string EncodeLoginPasswordCredentials(string url, string login, string password) {
		string json = JsonSerializer.Serialize(new {
			url,
			login,
			password
		});
		return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
	}

	private static int FindFreeLoopbackPort() {
		TcpListener listener = new(IPAddress.Loopback, 0);
		listener.Start();
		try {
			return ((IPEndPoint)listener.LocalEndpoint).Port;
		}
		finally {
			listener.Stop();
		}
	}

	private static async Task WaitUntilListeningAsync(int port, Process process, CancellationToken cancellationToken) {
		for (int attempt = 0; attempt < 150; attempt++) {
			cancellationToken.ThrowIfCancellationRequested();
			if (process.HasExited) {
				throw new InvalidOperationException(
					$"clio mcp-http exited early with code {process.ExitCode}.");
			}
			try {
				using TcpClient client = new();
				await client.ConnectAsync(IPAddress.Loopback, port, cancellationToken);
				return;
			}
			catch (SocketException) {
				await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
			}
		}
		throw new TimeoutException(
			$"clio mcp-http did not start listening on 127.0.0.1:{port} within the timeout.");
	}

	private static async Task KillAsync(Process process) {
		try {
			if (!process.HasExited) {
				process.Kill(entireProcessTree: true);
				await process.WaitForExitAsync();
			}
		}
		catch (InvalidOperationException) {
			// Process already exited between the HasExited check and Kill — nothing to do.
		}
		finally {
			process.Dispose();
		}
	}

	public async ValueTask DisposeAsync() {
		await KillAsync(_process);
	}
}
