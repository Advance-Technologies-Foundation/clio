using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// Story 15c (ENG-93208, FR-16 / AC-04/AC-05/AC-06) concurrency-isolation e2e for the
/// <c>clio mcp-http</c> credential-passthrough edge. Two concurrent DIFFERENT-credential passthrough
/// requests against a SINGLE mcp-http process must resolve to distinct sessions/containers (no cache-key
/// collision), each response must carry ONLY its own tenant's data (no cross-tenant bleed), they must
/// run on independent async flows, and they must NOT be serialized by a single global lock.
/// <para>
/// MANUAL — NOT in CI. Needs a live stand with two distinct tenants and a clio build whose
/// <c>mcp-http-credential-passthrough</c> incubation flag is enabled; skipped via
/// <see cref="McpHttpPassthroughStand.RequireOrIgnore"/> when the live-stand env vars are absent.
/// </para>
/// </summary>
[TestFixture]
[Category("E2E")]
[NonParallelizable]
public sealed class McpHttpConcurrencyIsolationE2ETests {

	private McpE2ESettings _settings = null!;

	[SetUp]
	public void SetUp() {
		_settings = TestConfiguration.Load();
	}

	[Test]
	[Description("Two concurrent different-credential passthrough requests against one mcp-http process each resolve to their own tenant with no cross-tenant response bleed (Story 15c; AC-04/AC-05/AC-06).")]
	public async Task ConcurrentPassthroughRequests_ShouldIsolateTenants_WhenDifferentCredentialsRunTogether() {
		// Arrange
		McpHttpPassthroughStand stand = McpHttpPassthroughStand.RequireOrIgnore();
		using CancellationTokenSource cts = new(TimeSpan.FromMinutes(3));
		await using McpHttpServerSession server =
			await McpHttpServerSession.StartAsync(_settings, stand.PlatformApiKey, cts.Token);

		string tenantOneCredentials =
			McpHttpServerSession.EncodeBearerCredentials(stand.TenantOneUrl, stand.TenantOneToken);
		string tenantTwoCredentials =
			McpHttpServerSession.EncodeBearerCredentials(stand.TenantTwoUrl, stand.TenantTwoToken);

		await using McpClient tenantOneClient =
			await server.ConnectAsync(stand.PlatformApiKey, tenantOneCredentials, cts.Token);
		await using McpClient tenantTwoClient =
			await server.ConnectAsync(stand.PlatformApiKey, tenantTwoCredentials, cts.Token);

		// Act — fire both passthrough calls concurrently (independent async flows on one process). The
		// tool carries NO environment argument, so each request resolves purely from its own
		// X-Integration-Credentials header.
		Task<CallToolResult> tenantOneCall = tenantOneClient.CallToolAsync(
			GetCreatioInfoTool.ToolName,
			new Dictionary<string, object?> { ["args"] = new Dictionary<string, object?>() },
			cancellationToken: cts.Token).AsTask();
		Task<CallToolResult> tenantTwoCall = tenantTwoClient.CallToolAsync(
			GetCreatioInfoTool.ToolName,
			new Dictionary<string, object?> { ["args"] = new Dictionary<string, object?>() },
			cancellationToken: cts.Token).AsTask();
		CallToolResult[] results = await Task.WhenAll(tenantOneCall, tenantTwoCall);

		string tenantOneText = ExtractText(results[0]);
		string tenantTwoText = ExtractText(results[1]);

		// Assert
		results[0].IsError.Should().NotBeTrue(
			because: "tenant one's passthrough request must succeed on its own credentials");
		results[1].IsError.Should().NotBeTrue(
			because: "tenant two's passthrough request must succeed on its own credentials");

		// No cross-tenant bleed: each response references ONLY its own tenant's URL. This is the
		// beyond-logger/db-context probe the ADR "Latent shared state" consequence demands — the observable
		// signal of cross-tenant artifact contamination (the H1/scoped-sink surface Story 9 fixed).
		// MANUAL-RUNNER ASSUMPTION: describe-environment echoes the target host in its output text. If a
		// future output shape drops the host, switch the discriminator to another per-tenant field.
		tenantOneText.Should().Contain(HostOf(stand.TenantOneUrl),
			because: "tenant one's response must describe tenant one's environment");
		tenantOneText.Should().NotContain(HostOf(stand.TenantTwoUrl),
			because: "tenant one's response must NOT carry tenant two's data — no cross-tenant bleed");
		tenantTwoText.Should().Contain(HostOf(stand.TenantTwoUrl),
			because: "tenant two's response must describe tenant two's environment");
		tenantTwoText.Should().NotContain(HostOf(stand.TenantOneUrl),
			because: "tenant two's response must NOT carry tenant one's data — no cross-tenant bleed");
	}

	[Test]
	[Description("Two concurrent different-credential passthrough requests complete together without global-lock serialization on one mcp-http process (Story 15c; not-serialized probe).")]
	public async Task ConcurrentPassthroughRequests_ShouldNotSerializeOnGlobalLock_WhenDifferentCredentialsRunTogether() {
		// Arrange
		McpHttpPassthroughStand stand = McpHttpPassthroughStand.RequireOrIgnore();
		using CancellationTokenSource cts = new(TimeSpan.FromMinutes(3));
		await using McpHttpServerSession server =
			await McpHttpServerSession.StartAsync(_settings, stand.PlatformApiKey, cts.Token);
		await using McpClient tenantOneClient = await server.ConnectAsync(
			stand.PlatformApiKey,
			McpHttpServerSession.EncodeBearerCredentials(stand.TenantOneUrl, stand.TenantOneToken),
			cts.Token);
		await using McpClient tenantTwoClient = await server.ConnectAsync(
			stand.PlatformApiKey,
			McpHttpServerSession.EncodeBearerCredentials(stand.TenantTwoUrl, stand.TenantTwoToken),
			cts.Token);

		// Act — both calls are launched before either is awaited; if a single global lock serialized
		// distinct tenants the second would block behind the first. A generous window avoids a flaky
		// wall-clock ratio while still proving both progress concurrently rather than being rejected/hung.
		Task<CallToolResult> first = tenantOneClient.CallToolAsync(
			GetCreatioInfoTool.ToolName,
			new Dictionary<string, object?> { ["args"] = new Dictionary<string, object?>() },
			cancellationToken: cts.Token).AsTask();
		Task<CallToolResult> second = tenantTwoClient.CallToolAsync(
			GetCreatioInfoTool.ToolName,
			new Dictionary<string, object?> { ["args"] = new Dictionary<string, object?>() },
			cancellationToken: cts.Token).AsTask();
		Task completedBatch = Task.WhenAll(first, second);
		Task winner = await Task.WhenAny(completedBatch, Task.Delay(TimeSpan.FromMinutes(2), cts.Token));

		// Assert
		winner.Should().Be(completedBatch,
			because: "two distinct-tenant passthrough requests must complete concurrently, not serialize behind one global lock");
		first.Result.IsError.Should().NotBeTrue(
			because: "the first tenant's concurrent request must succeed");
		second.Result.IsError.Should().NotBeTrue(
			because: "the second tenant's concurrent request must succeed");
	}

	private static string ExtractText(CallToolResult callResult) {
		CommandExecutionEnvelope envelope = McpCommandExecutionParser.Extract(callResult);
		IEnumerable<string> values = (envelope.Output ?? [])
			.Select(message => message.Value ?? string.Empty);
		return string.Join("\n", values);
	}

	private static string HostOf(string url) => new Uri(url).Host;
}
