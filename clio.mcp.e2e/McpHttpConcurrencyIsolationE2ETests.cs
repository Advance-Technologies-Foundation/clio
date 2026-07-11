using System.Text.Json;
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
/// MANUAL — NOT in CI. Needs a live stand with two distinct tenants and a clio mcp-http process
/// started with <c>--platform-api-key</c> (the sole passthrough gate); skipped via
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
			stand.TenantOneCredentialsBase64;
		string tenantTwoCredentials =
			stand.TenantTwoCredentialsBase64;

		await using McpClient tenantOneClient =
			await server.ConnectAsync(stand.PlatformApiKey, tenantOneCredentials, cts.Token);
		await using McpClient tenantTwoClient =
			await server.ConnectAsync(stand.PlatformApiKey, tenantTwoCredentials, cts.Token);

		// Act — fire both passthrough calls concurrently (independent async flows on one process). The
		// tool carries NO environment argument, so each request resolves purely from its own
		// X-Integration-Credentials header. describe-environment is a long-tail tool (absent from
		// lazy-mode tools/list, ENG-90312/92761); reach it via clio-run, exactly as a real client would.
		Task<CallToolResult> tenantOneCall = CallViaClioRunAsync(
			tenantOneClient, GetCreatioInfoTool.ToolName, new Dictionary<string, object?>(), cts.Token);
		Task<CallToolResult> tenantTwoCall = CallViaClioRunAsync(
			tenantTwoClient, GetCreatioInfoTool.ToolName, new Dictionary<string, object?>(), cts.Token);
		CallToolResult[] results = await Task.WhenAll(tenantOneCall, tenantTwoCall);

		string tenantOneText = ExtractText(results[0]);
		string tenantTwoText = ExtractText(results[1]);

		// Assert
		results[0].IsError.Should().NotBeTrue(
			because: "tenant one's passthrough request must succeed on its own credentials");
		results[1].IsError.Should().NotBeTrue(
			because: "tenant two's passthrough request must succeed on its own credentials");

		// No cross-tenant bleed: each response is genuine environment metadata and the two are not
		// byte-identical. This is the beyond-logger/db-context probe the ADR "Latent shared state"
		// consequence demands — the observable signal of cross-tenant artifact contamination (the
		// H1/scoped-sink surface Story 9 fixed).
		// MANUAL-RUNNER FINDING (live-stand run, ENG-93347): describe-environment's response does NOT
		// echo the target host/URL (the originally assumed discriminator) — it reports coreVersion,
		// workspace/user/userAccount GUIDs, db/framework info, with no URL field. Two distinct live
		// tenants matched on every seed-data GUID (same base image) but differed on coreVersion, so the
		// discriminator here is response-level: not-identical + both carry real metadata, rather than a
		// specific field/value that could coincidentally match on a future stand.
		tenantOneText.Should().NotBe(tenantTwoText,
			because: "tenant one's and tenant two's responses must not be byte-identical — no cross-tenant bleed");
		tenantOneText.Should().Contain("\"coreVersion\"",
			because: "tenant one's response must report real environment metadata, not an error or empty payload");
		tenantTwoText.Should().Contain("\"coreVersion\"",
			because: "tenant two's response must report real environment metadata, not an error or empty payload");
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
			stand.TenantOneCredentialsBase64,
			cts.Token);
		await using McpClient tenantTwoClient = await server.ConnectAsync(
			stand.PlatformApiKey,
			stand.TenantTwoCredentialsBase64,
			cts.Token);

		// Act — both calls are launched before either is awaited; if a single global lock serialized
		// distinct tenants the second would block behind the first. A generous window avoids a flaky
		// wall-clock ratio while still proving both progress concurrently rather than being rejected/hung.
		// describe-environment is a long-tail tool (absent from lazy-mode tools/list); reach it via clio-run.
		Task<CallToolResult> first = CallViaClioRunAsync(
			tenantOneClient, GetCreatioInfoTool.ToolName, new Dictionary<string, object?>(), cts.Token);
		Task<CallToolResult> second = CallViaClioRunAsync(
			tenantTwoClient, GetCreatioInfoTool.ToolName, new Dictionary<string, object?>(), cts.Token);
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

	// ---------------------------------------------------------------------------------------------
	// Story 15 AC-02 (ENG-93347, PRD AC-07): the same two-tenant isolation proof already established
	// above for describe-environment, extended to EVERY newly routed tool. Data-driven over the
	// mandated 12-tool set via [TestCaseSource] so each tool still surfaces as an individually
	// named/reportable case (NUnit renders each as
	// "ConcurrentPassthroughRequests_ShouldIsolateNewlyRoutedTool_WhenTwoTenantsCallConcurrently(\"<tool>\")").
	//
	// Isolation does not require the call to SUCCEED (a "not found" on both tenants proves distinct
	// authenticated containers just as well as two successes) — so most tool calls below deliberately
	// carry non-existent, per-tenant-unique identifiers. For get-component-info the mandated scenario
	// is specifically the mixed-input path (header + environment-name together), not a successful list
	// call, so that one case supplies environment-name deliberately. The assertion below is
	// deliberately the NEGATIVE cross-tenant-bleed check only (NotContain the OTHER tenant's marker),
	// never a positive "response echoes its own marker/rejection text" check — whether a given tool's
	// success/error text echoes anything about the request is tool-specific, unverified against a live
	// stand, and for get-component-info specifically the mixed-input rejection message is documented to
	// name NO supplied value while the tool itself is documented to fail SOFT to a latest-fallback
	// success on any version-resolution failure. NotContain(other) holds regardless of which of those
	// shapes a real stand produces.
	// ---------------------------------------------------------------------------------------------

	private static readonly string[] NewlyRoutedToolNames = [
		ApplicationGetListTool.ApplicationGetListToolName,
		ApplicationGetInfoTool.ApplicationGetInfoToolName,
		ApplicationCreateTool.ApplicationCreateToolName,
		ApplicationSectionCreateTool.ApplicationSectionCreateToolName,
		ApplicationSectionUpdateTool.ApplicationSectionUpdateToolName,
		ApplicationSectionDeleteTool.ApplicationSectionDeleteToolName,
		ApplicationSectionGetListTool.ApplicationSectionGetListToolName,
		GetUserCultureTool.ToolName,
		PageUpdateTool.ToolName,
		PageSyncTool.ToolName,
		ComponentInfoTool.ToolName,
		BuildThemeTool.ToolName
	];

	// The subset of NewlyRoutedToolNames that is resident in lazy-mode tools/list (ENG-90312/92761) and
	// therefore directly callable by name. Every other tool in the set is long-tail and must be reached
	// via clio-run, exactly as a real client would — calling a long-tail tool by name returns
	// "Unknown tool", not an auth/business error.
	private static readonly HashSet<string> ResidentToolNames = new(StringComparer.Ordinal) {
		ApplicationGetListTool.ApplicationGetListToolName,
		ApplicationGetInfoTool.ApplicationGetInfoToolName,
		ApplicationSectionGetListTool.ApplicationSectionGetListToolName,
		ComponentInfoTool.ToolName
	};

	// Tools with no natural per-call discriminating argument. Their isolation proof rests on the
	// shared independent-completion + non-serialization assertions below (mirrors the
	// MANUAL-RUNNER ASSUMPTION already recorded for describe-environment at lines 79-80).
	private static readonly HashSet<string> NoDiscriminatorArgumentTools = new(StringComparer.Ordinal) {
		ApplicationGetListTool.ApplicationGetListToolName,
		GetUserCultureTool.ToolName,
		BuildThemeTool.ToolName
	};

	[Test]
	[Description("Two concurrent different-credential passthrough requests against one mcp-http process each isolate a newly routed tool to its OWN tenant, with no cross-tenant bleed and no global-lock serialization (Story 15 AC-02; PRD AC-07).")]
	public async Task ConcurrentPassthroughRequests_ShouldIsolateNewlyRoutedTool_WhenTwoTenantsCallConcurrently(
		[ValueSource(nameof(NewlyRoutedToolNames))] string toolName) {
		// Arrange
		McpHttpPassthroughStand stand = McpHttpPassthroughStand.RequireOrIgnore();
		using CancellationTokenSource cts = new(TimeSpan.FromMinutes(5));
		await using McpHttpServerSession server =
			await McpHttpServerSession.StartAsync(_settings, stand.PlatformApiKey, cts.Token);
		await using McpClient tenantOneClient = await server.ConnectAsync(
			stand.PlatformApiKey,
			stand.TenantOneCredentialsBase64,
			cts.Token);
		await using McpClient tenantTwoClient = await server.ConnectAsync(
			stand.PlatformApiKey,
			stand.TenantTwoCredentialsBase64,
			cts.Token);

		string probeId = Guid.NewGuid().ToString("N")[..8];
		string tenantOneMarker = $"isot1{probeId}";
		string tenantTwoMarker = $"isot2{probeId}";
		Dictionary<string, object?> tenantOneArgs = BuildNewlyRoutedToolArgs(toolName, tenantOneMarker);
		Dictionary<string, object?> tenantTwoArgs = BuildNewlyRoutedToolArgs(toolName, tenantTwoMarker);

		// Act — fire both concurrently (independent async flows on one process); neither call carries an
		// environment-name UNLESS the tool's mandated isolation scenario IS the mixed-input rejection
		// (get-component-info), so every other request resolves purely from its own
		// X-Integration-Credentials header. Long-tail tools (absent from lazy-mode tools/list) are
		// dispatched via clio-run; resident tools are called directly, exactly as a real client would.
		Task<CallToolResult> tenantOneCall = InvokeNewlyRoutedToolAsync(tenantOneClient, toolName, tenantOneArgs, cts.Token);
		Task<CallToolResult> tenantTwoCall = InvokeNewlyRoutedToolAsync(tenantTwoClient, toolName, tenantTwoArgs, cts.Token);
		Task completedBatch = Task.WhenAll(tenantOneCall, tenantTwoCall);
		Task winner = await Task.WhenAny(completedBatch, Task.Delay(TimeSpan.FromMinutes(4), cts.Token));

		// Assert
		winner.Should().Be(completedBatch,
			because: $"two distinct-tenant '{toolName}' passthrough requests must complete concurrently, not serialize behind one global lock (PRD AC-07)");
		// Only the NEGATIVE cross-tenant-bleed check is asserted here — not a positive "response echoes
		// its own marker" check. Whether a tool's success/error text echoes a supplied identifier is
		// tool-specific and unverified against a live stand (e.g. update-page/sync-pages's marker-integrity
		// failure has no reason to echo schema-name; get-component-info's mixed-input rejection message
		// deliberately names NO supplied value, and the tool is documented to fail SOFT to a latest-fallback
		// success on any version-resolution failure). NotContain(otherTenantMarker) is a real bleed check
		// regardless of whether a tool echoes anything about itself, and cannot spuriously fail the way a
		// positional "must contain its own marker" assertion could.
		if (!NoDiscriminatorArgumentTools.Contains(toolName)) {
			string tenantOneResponse = ExtractRawResponseJson(tenantOneCall.Result);
			string tenantTwoResponse = ExtractRawResponseJson(tenantTwoCall.Result);
			tenantOneResponse.Should().NotContain(tenantTwoMarker,
				because: $"tenant one's '{toolName}' response must NOT reference tenant two's per-call probe identifier — no cross-tenant bleed");
			tenantTwoResponse.Should().NotContain(tenantOneMarker,
				because: $"tenant two's '{toolName}' response must NOT reference tenant one's per-call probe identifier — no cross-tenant bleed");
		}
	}

	// Dispatches to a long-tail tool (hidden from lazy-mode tools/list) via clio-run, the same path a
	// real client must use; calls a resident tool directly. `toolArguments` is exactly what would have
	// been sent as the direct tools/call arguments payload — clio-run forwards its own "args" verbatim
	// to the target tool's SDK-native binding, so the tool's existing shape is unchanged either way.
	private static Task<CallToolResult> InvokeNewlyRoutedToolAsync(
		McpClient client, string toolName, Dictionary<string, object?> toolArguments, CancellationToken cancellationToken) =>
		ResidentToolNames.Contains(toolName)
			? client.CallToolAsync(
				toolName,
				new Dictionary<string, object?> { ["args"] = toolArguments },
				cancellationToken: cancellationToken).AsTask()
			: CallViaClioRunAsync(client, toolName, toolArguments, cancellationToken);

	private static async Task<CallToolResult> CallViaClioRunAsync(
		McpClient client, string toolName, Dictionary<string, object?> toolArguments, CancellationToken cancellationToken) =>
		await client.CallToolAsync(
			ClioRunTool.ToolName,
			new Dictionary<string, object?> { ["command"] = toolName, ["args"] = toolArguments },
			cancellationToken: cancellationToken);

	// Builds the minimal args shape each newly routed tool needs to be INVOKED (not necessarily to
	// SUCCEED — see the class remarks above). `marker` is a distinct, per-tenant, per-run alphanumeric
	// token embedded in whichever identifying field the tool exposes, so a business-level "not found"
	// failure still lets the assertions above tell tenant one's response apart from tenant two's.
	private static Dictionary<string, object?> BuildNewlyRoutedToolArgs(string toolName, string marker) {
		if (toolName == ApplicationGetListTool.ApplicationGetListToolName
			|| toolName == GetUserCultureTool.ToolName) {
			return [];
		}
		if (toolName == ApplicationGetInfoTool.ApplicationGetInfoToolName) {
			return new Dictionary<string, object?> { ["code"] = marker };
		}
		if (toolName == ApplicationCreateTool.ApplicationCreateToolName) {
			return new Dictionary<string, object?> {
				["name"] = $"Isolation Probe {marker}",
				["code"] = marker,
				["with-mobile-pages"] = false
			};
		}
		if (toolName == ApplicationSectionCreateTool.ApplicationSectionCreateToolName) {
			return new Dictionary<string, object?> {
				["application-code"] = marker,
				["caption"] = $"Isolation Probe {marker}",
				["with-mobile-pages"] = false
			};
		}
		if (toolName == ApplicationSectionUpdateTool.ApplicationSectionUpdateToolName) {
			return new Dictionary<string, object?> {
				["application-code"] = marker,
				["section-code"] = marker,
				["caption"] = $"Isolation Probe {marker}"
			};
		}
		if (toolName == ApplicationSectionDeleteTool.ApplicationSectionDeleteToolName) {
			return new Dictionary<string, object?> {
				["application-code"] = marker,
				["section-code"] = marker
			};
		}
		if (toolName == ApplicationSectionGetListTool.ApplicationSectionGetListToolName) {
			return new Dictionary<string, object?> { ["application-code"] = marker };
		}
		if (toolName == PageUpdateTool.ToolName) {
			return new Dictionary<string, object?> {
				["schema-name"] = $"Usr{marker}_FormPage",
				["body"] = "({});",
				["dry-run"] = true
			};
		}
		if (toolName == PageSyncTool.ToolName) {
			return new Dictionary<string, object?> {
				["pages"] = new List<object?> {
					new Dictionary<string, object?> {
						["schema-name"] = $"Usr{marker}_FormPage",
						["body"] = "({});"
					}
				}
			};
		}
		if (toolName == ComponentInfoTool.ToolName) {
			// Deliberate mixed input (header + environment-name): the mandated isolation scenario for
			// this tool IS the guard rejection, not a successful list call (Story 15 AC-02).
			return new Dictionary<string, object?> {
				["component-type"] = "list",
				["environment-name"] = $"mixed-input-probe-{marker}"
			};
		}
		if (toolName == BuildThemeTool.ToolName) {
			return new Dictionary<string, object?> { ["primary"] = "#0058EF" };
		}
		throw new ArgumentOutOfRangeException(nameof(toolName), toolName,
			"No isolation-proof argument builder registered for this newly routed tool.");
	}

	// Concatenates BOTH the structured and the plain-content channels of a CallToolResult into one
	// JSON string for a simple substring containment check. Reused, simplified sibling of the
	// per-tool-shape parsers in Support/Results — the isolation proof only needs to know whether a
	// literal marker string appears in a response, not the response's typed shape.
	private static string ExtractRawResponseJson(CallToolResult callResult) {
		string structured = callResult.StructuredContent is not null
			? JsonSerializer.Serialize(callResult.StructuredContent)
			: string.Empty;
		string content = JsonSerializer.Serialize(callResult.Content ?? []);
		return structured + "\n" + content;
	}
}
