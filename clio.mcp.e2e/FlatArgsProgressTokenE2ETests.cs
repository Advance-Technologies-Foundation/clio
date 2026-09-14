using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// ENG-95885, requirement R6: proves over the real <c>clio mcp-server</c> that a CANONICAL FLAT argument
/// payload — sent WITHOUT the <c>{"args":{…}}</c> wrapper — still delivers the caller's
/// <see cref="ProgressToken"/> to a long-running tool after the call-tool filter rewrites
/// <c>Arguments</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is the transport-level counterpart to unit test T8. A unit test can assert that the normalizer
/// mutated <c>Arguments</c> on the EXISTING <see cref="CallToolRequestParams"/> instance; only the real
/// server process can prove that the surviving <c>ProgressToken</c> is then handed to the tool method and
/// that <c>notifications/progress</c> actually flow for the rewritten call — and, since ENG-95262 moved
/// this tool into the Stage 6 worker cohort, that the normalized arguments survive the matched-route
/// RELAY into the child process, which the filter performs before any deadline wrapper. Building a new
/// <c>CallToolRequestParams</c> instead of replacing <c>Arguments</c> in place would drop the token and
/// this fixture would go silent.
/// </para>
/// <para>
/// The target is <c>list-app-sections</c> because it is the only shape that can carry this proof without a
/// Creatio stand: it is RESIDENT (so the filter has a <c>MatchedPrimitive</c> and normalization applies),
/// it takes exactly one bindable composite <c>args</c> parameter (so the normalizer's trigger gate fires),
/// and it runs its backend read under <see cref="McpProgressHeartbeat"/> using
/// <c>requestContext.Params.ProgressToken</c> — the exact field R6 is about. The long-running deploy /
/// uninstall tools cannot carry it: they are NOT resident, so they are reached through <c>clio-run</c>,
/// whose two-parameter shape is deliberately excluded from normalization (R4/R8).
/// </para>
/// <para>
/// The fixture is non-destructive and needs no environment: the registered environment points at a
/// loopback listener that accepts the TCP connection and never answers, so the read blocks, heartbeats
/// stream, and the call ends in a cancellation payload once the parent bounds the relayed child.
/// Nothing is deployed, installed, or mutated.
/// </para>
/// </remarks>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("mcp-flat-argument-normalization")]
[NonParallelizable]
public sealed class FlatArgsProgressTokenE2ETests : McpContractFixtureBase {
	private const string ToolName = ApplicationSectionGetListTool.ApplicationSectionGetListToolName;
	private const string StallingEnvironmentName = "mcp-e2e-flat-args-progress-stall";
	private const string FixturePassword = "fixture-password";

	// Text fragment unique to the pre-ENG-95885 wrapper hint. Its ABSENCE proves the flat payload was
	// normalized and reached the tool instead of being answered with the hint.
	private const string WrapperHintFragment = "expects arguments wrapped inside";

	// Beat cadence and response budget for the child server process. The heartbeat must fire several
	// times inside the deadline so a single stalled read deterministically yields notifications.
	private const string HeartbeatIntervalSeconds = "0.2";
	private const string ReadDeadlineSeconds = "5";

	// Suppressed: both must be created inside ConfigureMcpServerSettings (the listener's port goes into
	// the child-process appsettings before the shared server starts), which the analyzer cannot track;
	// both ARE disposed in the [OneTimeTearDown] StopStallingListenerAsync below.
	[System.Diagnostics.CodeAnalysis.SuppressMessage("Structure", "NUnit1032:An IDisposable field/property should be Disposed in a TearDown method")]
	private TcpListener _stallListener = null!;

	[System.Diagnostics.CodeAnalysis.SuppressMessage("Structure", "NUnit1032:An IDisposable field/property should be Disposed in a TearDown method")]
	private CancellationTokenSource _acceptCancellation = null!;

	private Task _acceptLoop = null!;
	private readonly List<TcpClient> _heldConnections = [];

	/// <inheritdoc />
	private protected override void ConfigureMcpServerSettings(McpE2ESettings settings) {
		// A loopback listener that accepts the connection but never sends a response, so the backend read
		// hangs past the read deadline. A refused port would instead fail fast as a transport error,
		// classified before a single heartbeat could fire.
		_stallListener = new TcpListener(IPAddress.Loopback, 0);
		_stallListener.Start();
		int stallPort = ((IPEndPoint)_stallListener.LocalEndpoint).Port;
		_acceptCancellation = new CancellationTokenSource();
		_acceptLoop = Task.Run(async () => {
			try {
				while (!_acceptCancellation.IsCancellationRequested) {
					TcpClient client = await _stallListener.AcceptTcpClientAsync(_acceptCancellation.Token);
					_heldConnections.Add(client); // hold the socket open and never write a response
				}
			}
			catch (OperationCanceledException) { /* expected on teardown */ }
			catch (ObjectDisposedException) { /* expected when the listener stops */ }
			catch (SocketException) { /* expected when the listener stops */ }
		});

		JsonObject appSettings = new() {
			["ActiveEnvironmentKey"] = StallingEnvironmentName,
			["Autoupdate"] = false,
			["Features"] = new JsonObject(),
			["Environments"] = new JsonObject {
				[StallingEnvironmentName] = new JsonObject {
					["Uri"] = $"http://127.0.0.1:{stallPort}",
					["Login"] = "fixture-user",
					["Password"] = FixturePassword,
					["IsNetCore"] = false
				}
			}
		};
		settings.ProcessEnvironmentVariables["CLIO_HOME"] = CreateIsolatedClioHome(
			appSettings.ToJsonString(),
			"flat-args-progress-clio-home");
		// Read at child-process startup, so the static defaults pick up both overrides.
		settings.ProcessEnvironmentVariables[McpProgressHeartbeat.IntervalOverrideEnvVar] =
			HeartbeatIntervalSeconds;
		settings.ProcessEnvironmentVariables[McpReadResponseDeadline.ReadDeadlineOverrideEnvVar] =
			ReadDeadlineSeconds;
	}

	[OneTimeTearDown]
	public async Task StopStallingListenerAsync() {
		// Join the accept loop before touching _heldConnections: it is the only writer, so awaiting it
		// first turns the iteration below into a single-threaded read.
		await _acceptCancellation.CancelAsync();
		try {
			await _acceptLoop;
		}
		catch (OperationCanceledException) { /* expected on teardown */ }
		foreach (TcpClient client in _heldConnections) {
			client.Dispose();
		}
		_heldConnections.Clear();
		_stallListener.Stop();
		_stallListener.Dispose();
		_acceptCancellation.Dispose();
	}

	[Test]
	[Description("Calls the resident long-running list-app-sections tool with a canonical FLAT payload and an explicit MCP progress token over the real MCP server, and verifies the token still reaches the tool after the filter rewrites Arguments — the tool's own heartbeat streams notifications/progress carrying that exact token, and the call ends in the structured read-deadline envelope rather than the wrapper hint.")]
	[AllureTag(ToolName)]
	[AllureName("A canonical flat payload still delivers the caller's progress token to a long-running tool")]
	[AllureDescription("ENG-95885 R6 over the wire: sends list-app-sections WITHOUT the args wrapper with a caller-supplied ProgressToken, against an environment that accepts the TCP connection and never answers. Asserts the wrapper hint is gone (the payload was normalized and bound), that notifications/progress carrying that exact token arrive from the tool's own heartbeat (so ProgressToken survived the in-place Arguments rewrite), and that the call terminates in the relayed cancellation payload of the worker-routed tool.")]
	public async Task FlatArgs_Should_Deliver_ProgressToken_To_LongRunningTool_After_Normalization() {
		// Arrange
		await using ArrangeContext arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		arrangeContext.Session.StartCapturingProgressNotifications();
		ProgressToken progressToken = new($"clio-mcp-e2e-{Guid.NewGuid():N}");

		// Act — the CANONICAL FLAT shape: every key is a wire property of the args record, sent at the top
		// level with no {"args":{…}} wrapper. Use the same explicit-token + raw-handler path as ClioRing;
		// the SDK's typed progress overload installs a competing handler and mints its own token.
		CallToolResult callResult = await arrangeContext.Session.CallToolWithRawProgressAsync(
			ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = StallingEnvironmentName,
				["application-code"] = "McpE2EFlatArgsProgress"
			},
			progressToken,
			arrangeContext.CancellationTokenSource.Token);

		// Assert
		string responseText = string.Join('\n',
			callResult.Content.OfType<TextContentBlock>().Select(block => block.Text));
		// Diagnostic: dump the payload BEFORE asserting on progress. Zero notifications is the CORRECT
		// outcome whenever the call never reached the backend read — a refused argument shape, or a fast
		// configuration failure — and only the payload tells that apart from a broken keep-alive path.
		TestContext.Out.WriteLine($"[payload] isError={callResult.IsError} text={responseText}");
		TestContext.Out.WriteLine($"[structured] {callResult.StructuredContent?.ToString() ?? "<null>"}");

		responseText.Should().NotContain(WrapperHintFragment,
			because: "a canonical flat payload must be normalized into the wrapped shape and executed, "
				+ "never answered with the pre-ENG-95885 wrapper hint");

		IReadOnlyList<JsonNode> rawParams = await arrangeContext.Session.WaitForCapturedProgressAsync(
			progressToken,
			snapshot => snapshot.Count > 0,
			TimeSpan.FromSeconds(30),
			arrangeContext.CancellationTokenSource.Token);

		// WaitForCapturedProgressAsync already filters by the requested token, so a non-empty snapshot IS
		// the proof: the caller's ProgressToken survived the in-place Arguments rewrite and was read back
		// off Params by the tool method.
		rawParams.Should().NotBeEmpty(
			because: "the caller's progress token must survive normalization and reach the long-running "
				+ "tool, which reads it from requestContext.Params.ProgressToken — building a new "
				+ "CallToolRequestParams instead of replacing Arguments in place would drop it");

		string beatMessages = string.Join('\n',
			rawParams.Select(node => node["message"]?.GetValue<string>() ?? string.Empty));
		beatMessages.Should().Contain(ToolName,
			because: "the beats must come from the target tool's own heartbeat, which labels every message "
				+ "with the operation name — proving the notifications belong to the normalized call and "
				+ "are not incidental traffic on the shared session");

		// The terminal shape is the WORKER-relay one, not the ENG-93373 read-deadline envelope: ENG-95262
		// put this tool in the Stage 6 worker cohort, and the matched-route relay in McpToolErrorFilter
		// answers before the read-deadline wrapper is ever reached in the host. The relayed stalled read
		// therefore ends in the tool's own cancellation payload once the parent bounds the child. Pinned
		// explicitly below so a future cohort change fails HERE, naming the expectation to revisit, rather
		// than as an unexplained envelope mismatch.
		McpWorkerCohort.ShippedNames.Should().Contain(ToolName,
			because: "this fixture's terminal-shape expectation assumes the call is relayed to a worker "
				+ "(ENG-95262 Stage 6); moving the tool back into the host would restore the ENG-93373 "
				+ "read-deadline envelope and this assertion is the signpost for that");
		responseText.Should().Contain("canceled",
			because: "the stalled read must terminate in the relayed cancellation payload, which is only "
				+ "reachable if the rewritten arguments bound in the worker and the tool actually started "
				+ "its backend read");
		responseText.Should().NotContain(FixturePassword,
			because: "no configured credential may appear in the terminal payload either");

		string wire = string.Join('\n', rawParams.Select(node => node.ToJsonString()));
		wire.Should().NotContain(FixturePassword,
			because: "no configured credential may cross the wire on the progress channel");
	}
}
