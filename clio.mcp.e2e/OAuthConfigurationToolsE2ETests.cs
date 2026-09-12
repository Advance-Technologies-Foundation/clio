using System.Net;
using System.Text;
using System.Text.Json;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the server-to-server OAuth configuration MCP tools. These tools share the
/// <c>deploy-identity</c> feature toggle AND live on the hidden long tail of the lazy tool surface,
/// so the real clio MCP server never lists them in <c>tools/list</c>; when the feature is enabled
/// they are discoverable through the <c>get-tool-contract</c> compact index and callable through the
/// <c>clio-run</c> executor. The tests start the real <c>clio mcp-server</c> process, enable the
/// feature in an isolated CLIO_HOME, and assert lazy-surface discovery, safety metadata, the approved
/// argument schemas, and secret-handling guidance.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("deploy-identity")]
[Parallelizable(ParallelScope.Self)]
public sealed class OAuthConfigurationToolsE2ETests : McpContractFixtureBase
{
	private const string StubEnvironmentName = "oauth-verification-stub";
	// The stub must start while ConfigureMcpServerSettings builds the child-process environment and
	// is disposed by the fixture's [OneTimeTearDown]; NUnit's field-flow analyzer cannot infer that.
	[System.Diagnostics.CodeAnalysis.SuppressMessage("Structure", "NUnit1032:An IDisposable field/property should be Disposed in a TearDown method",
		Justification = "The system under test owns and disposes the factory-returned substitute.")]
	private OAuthVerificationStub? _stub;

	private static readonly string[] ExpectedToolNames = [
		GetIdentityServiceConfigTool.GetIdentityServiceConfigToolName,
		ResolveOAuthSystemUserTool.ResolveOAuthSystemUserToolName,
		CreateOAuthTechnicalUserTool.CreateOAuthTechnicalUserToolName,
		CreateServerToServerOAuthAppTool.CreateServerToServerOAuthAppToolName,
		VerifyOAuthAppTool.VerifyOAuthAppToolName
	];

	[Test]
	[Description("Starts the real clio MCP server with the deploy-identity feature enabled and verifies all five server-to-server OAuth configuration tools are discoverable via the get-tool-contract compact index.")]
	[AllureTag("oauth-mcp-tools")]
	[AllureName("Server-to-server OAuth configuration tools are discoverable on the lazy surface")]
	[AllureDescription("Starts the real clio MCP server with the deploy-identity feature enabled and verifies all five OAuth configuration tools are discoverable via the get-tool-contract compact index.")]
	public async Task OAuthConfigTools_Should_Be_Advertised_When_FeatureEnabled()
	{
		// Arrange
		await using var arrangeContext = Arrange();

		// Act
		IReadOnlyCollection<string> toolNames =
			await arrangeContext.Session.ListReachableToolNamesAsync(arrangeContext.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(ExpectedToolNames,
			because: "all five server-to-server OAuth configuration tools must be discoverable on the lazy surface (get-tool-contract compact index) when the deploy-identity feature is enabled");
	}

	[Test]
	[Description("Verifies the create-* OAuth tools carry the destructive flag in the get-tool-contract compact index, the read-only tools do not, and the full contracts keep the secret-handling guidance.")]
	[AllureTag("oauth-mcp-tools")]
	[AllureName("OAuth configuration tools expose correct safety metadata on the lazy surface")]
	[AllureDescription("Verifies destructive vs read-only safety flags via the get-tool-contract compact index and the secret-handling guidance via the full tool contracts of the five OAuth configuration tools.")]
	public async Task OAuthConfigTools_Should_Advertise_Correct_SafetyMetadata_When_FeatureEnabled()
	{
		// Arrange
		await using var arrangeContext = Arrange();

		// Act
		IReadOnlyList<ToolContractIndexEntry> index =
			await arrangeContext.Session.GetToolContractIndexAsync(arrangeContext.CancellationTokenSource.Token);
		IReadOnlyList<ToolContractDefinition> contracts = await FetchContractsAsync(
			arrangeContext,
			CreateOAuthTechnicalUserTool.CreateOAuthTechnicalUserToolName,
			CreateServerToServerOAuthAppTool.CreateServerToServerOAuthAppToolName,
			VerifyOAuthAppTool.VerifyOAuthAppToolName);

		// Assert
		// The destructive flag of a hidden tool now travels on the compact discovery index; the index only
		// carries a non-null flag when the invoker registry registered the tool, i.e. the feature is enabled.
		IndexEntryOf(index, GetIdentityServiceConfigTool.GetIdentityServiceConfigToolName).Destructive.Should().NotBe(true,
			because: "reading the identity service config is read-only");

		IndexEntryOf(index, ResolveOAuthSystemUserTool.ResolveOAuthSystemUserToolName).Destructive.Should().NotBe(true,
			because: "resolving a system user is read-only");

		IndexEntryOf(index, CreateOAuthTechnicalUserTool.CreateOAuthTechnicalUserToolName).Destructive.Should().BeTrue(
			because: "creating a technical user mutates Creatio");
		ContractOf(contracts, CreateOAuthTechnicalUserTool.CreateOAuthTechnicalUserToolName).Description
			.Should().Contain("ROLE GRANT IS DEFERRED",
				because: "agents must be told the REST-only path does not grant a Creatio role");

		IndexEntryOf(index, CreateServerToServerOAuthAppTool.CreateServerToServerOAuthAppToolName).Destructive.Should().BeTrue(
			because: "creating an OAuth app mutates Creatio");
		ContractOf(contracts, CreateServerToServerOAuthAppTool.CreateServerToServerOAuthAppToolName).Description
			.Should().Contain("never written to logs",
				because: "agents must be told the client secret is surfaced only in the structured result");

		IndexEntryOf(index, VerifyOAuthAppTool.VerifyOAuthAppToolName).Destructive.Should().NotBe(true,
			because: "verifying an OAuth app is read-only");
		ContractOf(contracts, VerifyOAuthAppTool.VerifyOAuthAppToolName).Description
			.Should().Contain("never returned or logged",
				because: "agents must be told the access token text is never surfaced");
	}

	[Test]
	[Description("Verifies the approved argument schema of the OAuth configuration tools via the full get-tool-contract payload on the lazy surface.")]
	[AllureTag("oauth-mcp-tools")]
	[AllureName("OAuth configuration tools expose the approved argument schema through get-tool-contract")]
	[AllureDescription("Verifies required arguments of the OAuth configuration tools via the full tool contracts returned by get-tool-contract.")]
	public async Task OAuthConfigTools_Should_Advertise_Approved_ArgumentSchema_When_FeatureEnabled()
	{
		// Arrange
		await using var arrangeContext = Arrange();

		// Act
		IReadOnlyList<ToolContractDefinition> contracts = await FetchContractsAsync(
			arrangeContext,
			GetIdentityServiceConfigTool.GetIdentityServiceConfigToolName,
			ResolveOAuthSystemUserTool.ResolveOAuthSystemUserToolName,
			VerifyOAuthAppTool.VerifyOAuthAppToolName);

		// Assert
		ContractOf(contracts, GetIdentityServiceConfigTool.GetIdentityServiceConfigToolName).InputSchema.Required
			.Should().BeEquivalentTo(["environment-name"],
				because: "get-identity-service-config requires only the environment name");

		ContractOf(contracts, ResolveOAuthSystemUserTool.ResolveOAuthSystemUserToolName).InputSchema.Required
			.Should().BeEquivalentTo(["environment-name"],
				because: "resolve-oauth-system-user requires only the environment name; name and id are optional");

		ContractOf(contracts, VerifyOAuthAppTool.VerifyOAuthAppToolName).InputSchema.Required
			.Should().BeEquivalentTo(["environment-name", "client-id", "client-secret"],
				because: "verify-oauth-app requires the environment plus the client credentials to verify");
	}

	[Test]
	[Description("Invokes verify-oauth-app through the real MCP server and proves the acquired bearer token reaches the Creatio DataService smoke request.")]
	[AllureTag(VerifyOAuthAppTool.VerifyOAuthAppToolName)]
	[AllureName("verify-oauth-app completes token acquisition and bearer smoke test end to end")]
	[AllureDescription("Uses a loopback IdentityService and Creatio surface to verify the real MCP command acquires a client_credentials token and sends it through CreatioClient to DataService.")]
	public async Task VerifyOAuthApp_Should_AcquireToken_And_RunBearerSmokeTest_WhenEndpointsAcceptCredentials()
	{
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			VerifyOAuthAppTool.VerifyOAuthAppToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = StubEnvironmentName,
					["client-id"] = "client-id",
					["client-secret"] = "client-secret",
					["identity-server-url"] = _stub!.BaseUri
				}
			},
			context.CancellationTokenSource.Token);
		VerifyOAuthAppResponse response =
			EntitySchemaStructuredResultParser.Extract<VerifyOAuthAppResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "the reachable loopback endpoints must produce a normal structured MCP result");
		response.Success.Should().BeTrue(
			because: "the verify command completed without a transport or binding failure");
		response.Result.Should().NotBeNull(
			because: "the tool must return its end-to-end verification result");
		response.Result!.Ok.Should().BeTrue(
			because: "both token acquisition and the bearer DataService probe succeeded");
		_stub.TokenRequestBody.Should().Contain("client_id=client-id",
			because: "the requested OAuth application must be the one verified");
		_stub.AuthorizationHeader.Should().Be("Bearer stub-access-token",
			because: "CreatioClient must carry the acquired token into the DataService request");
		string serializedResult = JsonSerializer.Serialize(callResult);
		serializedResult.Should().NotContain("stub-access-token",
			because: "the acquired access token is secret and must never cross the MCP response boundary");
		serializedResult.Should().NotContain("client-secret",
			because: "the supplied client secret must never be echoed by the tool");
	}

	[OneTimeTearDown]
	public async Task StopStubAsync()
	{
		if (_stub is not null) {
			await _stub.DisposeAsync();
		}
	}

	private static async Task<IReadOnlyList<ToolContractDefinition>> FetchContractsAsync(
		ArrangeContext arrangeContext,
		params string[] toolNames)
	{
		CallToolResult contractResult = await arrangeContext.Session.CallToolAsync(
			ToolContractGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> { ["tool-names"] = toolNames }
			},
			arrangeContext.CancellationTokenSource.Token);
		ToolContractGetResponse contracts =
			EntitySchemaStructuredResultParser.Extract<ToolContractGetResponse>(contractResult);
		contracts.Tools.Should().NotBeNull(
			because: "get-tool-contract must expand the requested OAuth tool names into full contracts when the deploy-identity feature is enabled");
		return contracts.Tools!;
	}

	private static ToolContractIndexEntry IndexEntryOf(IReadOnlyList<ToolContractIndexEntry> index, string toolName)
	{
		return index.Should().ContainSingle(entry => entry.Name == toolName,
			because: $"the {toolName} MCP tool must be discoverable via the get-tool-contract compact index when the deploy-identity feature is enabled")
			.Which;
	}

	private static ToolContractDefinition ContractOf(IReadOnlyList<ToolContractDefinition> contracts, string toolName)
	{
		return contracts.Should().ContainSingle(contract => contract.Name == toolName,
			because: $"get-tool-contract must return the full contract of {toolName} on the lazy surface")
			.Which;
	}

	/// <inheritdoc />
	private protected override void ConfigureMcpServerSettings(McpE2ESettings settings)
	{
		_stub = OAuthVerificationStub.Start();
		settings.ProcessEnvironmentVariables["CLIO_HOME"] = CreateIsolatedClioHome(
			$$"""
			{
			  "ActiveEnvironmentKey": "{{StubEnvironmentName}}",
			  "Autoupdate": false,
			  "Features": {
			    "deploy-identity": true
			  },
			  "Environments": {
			    "{{StubEnvironmentName}}": {
			      "Uri": "{{_stub.BaseUri}}",
			      "Login": "Supervisor",
			      "Password": "Supervisor",
			      "IsNetCore": true
			    }
			  }
			}
			""",
			"deploy-identity");
	}

	private sealed class OAuthVerificationStub : IAsyncDisposable
	{
		private readonly CancellationTokenSource _cancellationTokenSource = new();
		private readonly HttpListener _listener;
		private readonly Task _listenerLoop;

		private OAuthVerificationStub(HttpListener listener, string baseUri)
		{
			_listener = listener;
			BaseUri = baseUri;
			_listenerLoop = Task.Run(ListenAsync);
		}

		public string BaseUri { get; }

		public string? TokenRequestBody { get; private set; }

		public string? AuthorizationHeader { get; private set; }

		public static OAuthVerificationStub Start()
		{
			for (int attempt = 0; attempt < 5; attempt++) {
				int port = Random.Shared.Next(20_000, 60_000);
				HttpListener listener = new();
				listener.Prefixes.Add($"http://127.0.0.1:{port}/");
				try {
					listener.Start();
					return new OAuthVerificationStub(listener, $"http://127.0.0.1:{port}");
				} catch (HttpListenerException) {
					listener.Close();
				}
			}
			throw new InvalidOperationException("Unable to start the OAuth verification loopback fake.");
		}

		public async ValueTask DisposeAsync()
		{
			_cancellationTokenSource.Cancel();
			_listener.Stop();
			try {
				await _listenerLoop.ConfigureAwait(false);
			} catch (OperationCanceledException) {
				// Expected while stopping the loopback fake.
			} finally {
				_listener.Close();
				_cancellationTokenSource.Dispose();
			}
		}

		private async Task ListenAsync()
		{
			while (!_cancellationTokenSource.IsCancellationRequested) {
				HttpListenerContext context;
				try {
					context = await _listener.GetContextAsync()
						.WaitAsync(_cancellationTokenSource.Token)
						.ConfigureAwait(false);
				} catch (OperationCanceledException) {
					return;
				} catch (HttpListenerException) when (_cancellationTokenSource.IsCancellationRequested) {
					return;
				} catch (ObjectDisposedException) when (_cancellationTokenSource.IsCancellationRequested) {
					return;
				}
				await RespondAsync(context).ConfigureAwait(false);
			}
		}

		private async Task RespondAsync(HttpListenerContext context)
		{
			string path = context.Request.Url?.AbsolutePath ?? string.Empty;
			byte[] bytes;
			if (path.EndsWith("/connect/token", StringComparison.Ordinal)) {
				using StreamReader reader = new(context.Request.InputStream, context.Request.ContentEncoding);
				TokenRequestBody = await reader.ReadToEndAsync().ConfigureAwait(false);
				context.Response.ContentType = "application/json";
				if (!TokenRequestBody.Contains("grant_type=client_credentials", StringComparison.Ordinal)
					|| !TokenRequestBody.Contains("client_id=client-id", StringComparison.Ordinal)
					|| !TokenRequestBody.Contains("client_secret=client-secret", StringComparison.Ordinal)) {
					context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
					bytes = Encoding.UTF8.GetBytes("{\"error\":\"invalid_client\"}");
				} else {
					bytes = Encoding.UTF8.GetBytes("{\"access_token\":\"stub-access-token\",\"token_type\":\"Bearer\"}");
				}
			} else if (path.EndsWith("/DataService/json/SyncReply/SelectQuery", StringComparison.Ordinal)) {
				AuthorizationHeader = context.Request.Headers["Authorization"];
				context.Response.ContentType = "application/json";
				if (AuthorizationHeader != "Bearer stub-access-token") {
					context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
				}
				bytes = Encoding.UTF8.GetBytes("{\"success\":true,\"rows\":[]}");
			} else {
				context.Response.StatusCode = (int)HttpStatusCode.NotFound;
				bytes = Encoding.UTF8.GetBytes("Not Found");
			}
			context.Response.ContentLength64 = bytes.Length;
			await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
			context.Response.Close();
		}
	}
}
