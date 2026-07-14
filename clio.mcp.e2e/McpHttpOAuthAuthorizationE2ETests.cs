using System.Net.Http;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using NUnit.Framework;

namespace Clio.Mcp.E2E;

/// <summary>
/// ENG-93386 Story 8 e2e coverage for standard OAuth 2.1 Resource-Server authorization on
/// <c>clio mcp-http</c>: token-happy-path, 401 (missing/invalid token), discovery, and OAuth+
/// credential-passthrough interop.
/// <para>
/// MANUAL — NOT in CI. Needs a live identity-platform Authorization Server with a pre-registered
/// confidential <c>client_credentials</c> client; skipped via
/// <see cref="McpHttpOAuthStand.RequireOrIgnore"/> when the live-stand env vars are absent. The
/// unit suite (<c>McpHttpAuthenticationPipelineTests</c>, <c>CredentialPassthroughAuthHardeningTests</c>,
/// <c>PlatformApiKeyDispositionTests</c>) already proves the 401/403/discovery/no-token-passthrough
/// contracts against an in-memory <c>TestServer</c> with a self-signed key — these fixtures are
/// authored to additionally confirm the same contracts against a REAL Authorization Server, but have
/// NOT been run against one as of this writing (no live stand was available in this session).
/// </para>
/// <para>
/// The FR-09 no-token-passthrough invariant (the inbound MCP JWT never reaches the outbound Creatio
/// call) is proven at the unit level (<c>CredentialPassthroughAuthHardeningTests</c>) by construction
/// — asserting it at the e2e layer would require a network-level proxy/capture and is out of scope here.
/// </para>
/// </summary>
[TestFixture]
[Category("E2E")]
[NonParallelizable]
public sealed class McpHttpOAuthAuthorizationE2ETests {

	private McpE2ESettings _settings = null!;

	[SetUp]
	public void SetUp() {
		_settings = TestConfiguration.Load();
	}

	private static IReadOnlyList<string> AuthArguments(McpHttpOAuthStand stand) {
		List<string> arguments = ["--auth-authority", stand.AuthAuthority, "--auth-audience", stand.AuthAudience];
		if (!string.IsNullOrWhiteSpace(stand.RequiredScopes)) {
			arguments.Add("--auth-required-scopes");
			arguments.Add(stand.RequiredScopes);
		}
		return arguments;
	}

	[Test]
	[Description("The Protected Resource Metadata document is served anonymously at the well-known path, even with authorization enabled.")]
	public async Task WellKnownEndpoint_ShouldServeResourceMetadata_Anonymously() {
		// Arrange
		McpHttpOAuthStand stand = McpHttpOAuthStand.RequireOrIgnore();
		using CancellationTokenSource cts = new(TimeSpan.FromMinutes(2));
		await using McpHttpServerSession server =
			await McpHttpServerSession.StartAsync(_settings, platformApiKey: null, cts.Token, AuthArguments(stand));
		Uri wellKnownUri = new(new Uri(server.EndpointUrl), "/.well-known/oauth-protected-resource");
		using HttpClient httpClient = new();

		// Act
		using HttpResponseMessage response = await httpClient.GetAsync(wellKnownUri, cts.Token);
		string body = await response.Content.ReadAsStringAsync(cts.Token);

		// Assert
		response.IsSuccessStatusCode.Should().BeTrue(
			because: "RFC 9728 discovery must be reachable without any credential (AC-06)");
		body.Should().Contain(stand.AuthAuthority,
			because: "the configured authority must be advertised as an authorization server");
	}

	[Test]
	[Description("A request with no bearer token at all fails to connect (401) once standard OAuth authorization is enabled (AC-01).")]
	public async Task Unauthenticated_ShouldFailToConnect() {
		// Arrange
		McpHttpOAuthStand stand = McpHttpOAuthStand.RequireOrIgnore();
		using CancellationTokenSource cts = new(TimeSpan.FromMinutes(2));
		await using McpHttpServerSession server =
			await McpHttpServerSession.StartAsync(_settings, platformApiKey: null, cts.Token, AuthArguments(stand));

		// Act
		Func<Task> act = async () => {
			await using McpClient _ = await server.ConnectAsync(
				platformApiKey: null, integrationCredentialsBase64: null, cts.Token);
		};

		// Assert
		// Deliberately broad: the exact exception shape the SDK surfaces for a 401 during the
		// initial handshake has not been confirmed against a live run (this fixture has never been
		// executed against a real Authorization Server, per the class remarks). Tighten this to
		// assert the specific HTTP 401 once a live run confirms the exact failure shape.
		await act.Should().ThrowAsync<Exception>(
			because: "no bearer token was presented and the endpoint requires one when --auth-authority is configured");
	}

	[Test]
	[Description("A garbage bearer token fails to connect (401) -- an invalid token is rejected exactly like a missing one.")]
	public async Task InvalidToken_ShouldFailToConnect() {
		// Arrange
		McpHttpOAuthStand stand = McpHttpOAuthStand.RequireOrIgnore();
		using CancellationTokenSource cts = new(TimeSpan.FromMinutes(2));
		await using McpHttpServerSession server =
			await McpHttpServerSession.StartAsync(_settings, platformApiKey: null, cts.Token, AuthArguments(stand));

		// Act
		Func<Task> act = async () => {
			await using McpClient _ = await server.ConnectAsync(
				platformApiKey: "not-a-real-token", integrationCredentialsBase64: null, cts.Token);
		};

		// Assert
		// Deliberately broad -- see the comment in Unauthenticated_ShouldFailToConnect above.
		await act.Should().ThrowAsync<Exception>(
			because: "a malformed/invalid bearer token must be rejected the same as a missing one");
	}

	[Test]
	[Description("A valid client_credentials token acquired from the live Authorization Server is accepted and can list tools (token happy path).")]
	public async Task ValidToken_ShouldListTools() {
		// Arrange
		McpHttpOAuthStand stand = McpHttpOAuthStand.RequireOrIgnore();
		using CancellationTokenSource cts = new(TimeSpan.FromMinutes(2));
		string token = await OAuthClientCredentialsTokenFetcher.AcquireAsync(
			stand.AuthAuthority, stand.ClientId, stand.ClientSecret, stand.RequiredScopes, cts.Token);
		await using McpHttpServerSession server =
			await McpHttpServerSession.StartAsync(_settings, platformApiKey: null, cts.Token, AuthArguments(stand));

		// Act
		await using McpClient client = await server.ConnectAsync(token, integrationCredentialsBase64: null, cts.Token);
		IList<McpClientTool> tools = await client.ListToolsAsync(cancellationToken: cts.Token);

		// Assert
		tools.Should().NotBeEmpty(
			because: "a validly authenticated request must reach the tool listing exactly like stdio mode");
	}

	[Test]
	[Description("A valid OAuth token combined with a per-request X-Integration-Credentials header successfully executes a passthrough tool call against a live tenant (OAuth + credential-passthrough interop, Stories 5-7).")]
	public async Task ValidToken_ShouldExecutePassthroughToolAgainstTenant() {
		// Arrange
		McpHttpOAuthStand stand = McpHttpOAuthStand.RequireOrIgnore();
		if (string.IsNullOrWhiteSpace(stand.TenantUrl)
			|| string.IsNullOrWhiteSpace(stand.TenantToken)
			|| !stand.TenantIsNetCore.HasValue) {
			Assert.Ignore(
				"OAuth+passthrough interop leg needs CLIO_MCP_HTTP_E2E_TENANT1_URL/_TOKEN in addition to "
				+ "the auth stand variables and CLIO_MCP_HTTP_E2E_TENANT1_IS_NET_CORE=true|false.");
		}
		using CancellationTokenSource cts = new(TimeSpan.FromMinutes(2));
		string token = await OAuthClientCredentialsTokenFetcher.AcquireAsync(
			stand.AuthAuthority, stand.ClientId, stand.ClientSecret, stand.RequiredScopes, cts.Token);
		await using McpHttpServerSession server =
			await McpHttpServerSession.StartAsync(_settings, platformApiKey: null, cts.Token, AuthArguments(stand));

		// Act
		await using McpClient client = await server.ConnectAsync(
			token, McpHttpServerSession.EncodeBearerCredentials(stand.TenantUrl!, stand.TenantToken!, stand.TenantIsNetCore.Value), cts.Token);
		CallToolResult result = await client.CallToolAsync(
			GetCreatioInfoTool.ToolName,
			new Dictionary<string, object?> { ["args"] = new Dictionary<string, object?>() },
			cancellationToken: cts.Token);

		// Assert
		result.IsError.Should().NotBeTrue(
			because: "a request authenticated by the gateway's OAuth token, carrying a per-tenant credential header, must succeed");
		ExtractText(result).Should().Contain(new Uri(stand.TenantUrl!).Host,
			because: "the response must describe the tenant targeted by the credential header, not any pre-registered environment");
	}

	private static string ExtractText(CallToolResult callResult) {
		CommandExecutionEnvelope envelope = McpCommandExecutionParser.Extract(callResult);
		return string.Join("\n", (envelope.Output ?? []).Select(message => message.Value ?? string.Empty));
	}
}
