using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Relay;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for the TWO bounds a stalled retry-safe read can hit, and for the fact that they
/// are different bounds with distinguishable envelopes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Which bound applies is decided by cohort membership, and that changed at ENG-95262 Stage 6.</b> A
/// tool in <c>McpWorkerCohort</c> executes in a child process, so the parent bounds it by KILLING that
/// child at <c>CLIO_MCP_WORKER_BUDGET_SECONDS</c> and the in-process read deadline never wraps it — the
/// router answers before the deadline wrapper is reached, at every dispatch site. Everything OUTSIDE the
/// cohort is still bounded by <see cref="McpReadResponseDeadline"/> exactly as ENG-93373 built it, and
/// stays that way until Stage 10 deletes it.
/// </para>
/// <para>
/// <b>So this fixture asserts both, and asserts that they are TELLABLE APART.</b> They deliberately share
/// the <c>error-class=creatio-timeout</c> wire token — from a client's point of view "clio bounded this
/// read" is one situation with one correct response, and shipped agent guidance keyed on that token keeps
/// applying as tools move into workers. What differs is the mechanism, and the mechanism matters:
/// <c>read-response-timed-out</c> means the work was ABANDONED and still holds its thread and the tenant
/// monitor, while <c>worker-budget-expired</c> means the work was TERMINATED and nothing survives it. A
/// test that accepted either marker for either tool would no longer notice a cohort tool silently falling
/// back to the abandoning path — which is the wedge this work removes.
/// </para>
/// <para>
/// The original ENG-93373 cases pointed at <c>list-pages</c> and <c>execute-esq</c>; both are now cohort
/// members, so those two moved to the worker-budget assertions and a NON-cohort read
/// (<c>list-packages</c>) took over the read-deadline coverage.
/// </para>
/// </remarks>
[TestFixture]
[AllureNUnit]
[NonParallelizable]
public sealed class ReadResponseDeadlineToolE2ETests {
	private const string ListPagesToolName = PageListTool.ToolName;
	private const string ListPackagesToolName = GetPkgListTool.GetPkgListToolName;

	/// <summary>
	/// Worker budget for the cohort cases. Generous enough to reach <c>initialize</c> on the slow platform —
	/// spawn + handshake measured p50 2.763 s on Windows Server 2022 (ADR §2.4) — so the envelope under test
	/// is the one produced by bounding the CALL, not an artefact of killing a child that never started.
	/// </summary>
	private const string WorkerBudgetSeconds = "8";

	/// <summary>Read deadline for the non-cohort case; the original ENG-93373 value.</summary>
	private const string ReadDeadlineSeconds = "1";

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Starts the real clio MCP server against a stalling endpoint and verifies that list-pages — a Stage 6 cohort tool, so it executes in a child process — is bounded by the parent KILLING that child at the worker budget, returning a structured error-class=creatio-timeout / worker-budget-expired envelope rather than the in-process read-deadline envelope it used to return.")]
	[AllureFeature(ListPagesToolName)]
	[AllureTag(ListPagesToolName)]
	[AllureName("A cohort read is bounded by the parent killing its worker, not by the in-process read deadline")]
	[AllureDescription("Points the environment at a TCP endpoint that accepts the connection but never responds, sets CLIO_MCP_WORKER_BUDGET_SECONDS, invokes list-pages, and verifies the answer is the worker-budget envelope: error-class=creatio-timeout with worker-budget-expired=true and NO read-response-timed-out marker. The distinction is the point — the abandoning in-process deadline kept the work and the tenant monitor alive, the worker kill does not.")]
	public async Task ListPages_Should_Return_WorkerBudgetEnvelope_When_TheParentBudgetElapses() {
		// Arrange
		using StallingEndpoint stall = StallingEndpoint.Start();
		using DeadlineFixtureHome home = DeadlineFixtureHome.Create(
			"clio-worker-budget-e2e", "stalling-worker-budget-e2e", stall.Port,
			new Dictionary<string, string> {
				[McpWorkerCallDispatcher.BudgetOverrideEnvVar] = WorkerBudgetSeconds
			});
		using CancellationTokenSource cancellation = new(TimeSpan.FromMinutes(3));
		await using McpServerSession session = await McpServerSession.StartAsync(home.Settings, cancellation.Token);

		try {
			// Act
			CallToolResult callResult = await session.CallToolAsync(
				ListPagesToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = home.EnvironmentName,
						["package-name"] = "UsrStallPackage"
					}
				},
				cancellation.Token);

			// Assert
			AssertWorkerBudgetEnvelope(callResult, ListPagesToolName);
		}
		finally {
			await stall.StopAsync();
		}
	}

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("The same worker-budget bound applies to a cohort read reached through clio-run — the long-tail dispatch vector — proving dispatch site (c) hands the call to the worker path and bounds it identically to a directly named call.")]
	[AllureFeature(ClioRunTool.ToolName)]
	[AllureTag(ClioRunTool.ToolName)]
	[AllureName("A cohort read dispatched through clio-run is bounded by the same worker budget")]
	[AllureDescription("Points the environment at a TCP endpoint that never responds, sets CLIO_MCP_WORKER_BUDGET_SECONDS, and invokes clio-run{command=execute-esq} — a non-resident cohort read. Verifies the inner dispatch routes to a worker and is bounded by the parent kill, yielding the worker-budget envelope. Keying on the wrapper's own name instead would give the entire long tail clio-run's in-process row, which is the unbounded wedge this work removes.")]
	public async Task ClioRun_Should_Return_WorkerBudgetEnvelope_When_DispatchingACohortRead() {
		// Arrange
		using StallingEndpoint stall = StallingEndpoint.Start();
		using DeadlineFixtureHome home = DeadlineFixtureHome.Create(
			"clio-worker-budget-clr-e2e", "stalling-worker-budget-clr-e2e", stall.Port,
			new Dictionary<string, string> {
				[McpWorkerCallDispatcher.BudgetOverrideEnvVar] = WorkerBudgetSeconds
			});
		using CancellationTokenSource cancellation = new(TimeSpan.FromMinutes(3));
		await using McpServerSession session = await McpServerSession.StartAsync(home.Settings, cancellation.Token);

		try {
			// Act
			CallToolResult callResult = await session.CallToolAsync(
				ClioRunTool.ToolName,
				new Dictionary<string, object?> {
					["command"] = ExecuteEsqTool.ToolName,
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = home.EnvironmentName,
						// A minimal but VALID SelectQuery (non-empty rootSchemaName) so execute-esq passes
						// validation and reaches the network POST, which hangs on the stalling endpoint.
						["query"] = new Dictionary<string, object?> { ["rootSchemaName"] = "Contact" }
					}
				},
				cancellation.Token);

			// Assert
			AssertWorkerBudgetEnvelope(callResult, ExecuteEsqTool.ToolName);
		}
		finally {
			await stall.StopAsync();
		}
	}

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("ENG-93373 regression, retargeted at a NON-cohort read: list-packages still executes in the host process, so a stalled read is still bounded by the in-process read deadline and still returns the read-response-timed-out envelope. This is what keeps the old mechanism covered while the cohort moves out from under it — it is only deleted at Stage 10.")]
	[AllureFeature(ListPackagesToolName)]
	[AllureTag(ListPackagesToolName)]
	[AllureName("A read outside the worker cohort is still bounded by the in-process read deadline")]
	[AllureDescription("Points the environment at a TCP endpoint that never responds, sets CLIO_MCP_READ_DEADLINE_SECONDS=1, invokes list-packages (retry-safe and NOT a Stage 6 cohort member, so it runs in the host process), and verifies the ENG-93373 envelope is unchanged: error-class=creatio-timeout with read-response-timed-out=true and retry guidance. A regression here would mean the worker path swallowed the bound that covers every tool it has not yet taken over.")]
	public async Task ListPackages_Should_Return_ReadTimeout_When_Response_Deadline_Elapses() {
		// Arrange
		using StallingEndpoint stall = StallingEndpoint.Start();
		using DeadlineFixtureHome home = DeadlineFixtureHome.Create(
			"clio-read-deadline-e2e", "stalling-read-e2e", stall.Port,
			new Dictionary<string, string> {
				[McpReadResponseDeadline.ReadDeadlineOverrideEnvVar] = ReadDeadlineSeconds
			});
		using CancellationTokenSource cancellation = new(TimeSpan.FromMinutes(3));
		await using McpServerSession session = await McpServerSession.StartAsync(home.Settings, cancellation.Token);

		try {
			// Act
			CallToolResult callResult = await session.CallToolAsync(
				ListPackagesToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = home.EnvironmentName
					}
				},
				cancellation.Token);

			// Assert
			callResult.StructuredContent.Should().NotBeNull(
				because: $"a read timeout must return a machine-readable structured envelope. "
					+ $"{Describe(callResult)}");
			JsonElement structured = callResult.StructuredContent!.Value;
			structured.GetProperty("error-class").GetString().Should().Be("creatio-timeout",
				because: $"the read deadline reuses the creatio-timeout class so existing client guidance "
					+ $"applies. {Describe(callResult)}");
			structured.GetProperty("read-response-timed-out").GetBoolean().Should().BeTrue(
				because: $"this tool is NOT in the worker cohort, so it must still be bounded by the "
					+ $"in-process read deadline — a worker-budget envelope here would mean something routed "
					+ $"a non-cohort tool to a child. {Describe(callResult)}");
			structured.TryGetProperty("worker-budget-expired", out JsonElement _).Should().BeFalse(
				because: $"the two bounds must stay tellable apart: this one ABANDONS the work and keeps the "
					+ $"tenant monitor, the worker kill does not. {Describe(callResult)}");
			structured.GetProperty("retry-guidance").GetString().Should().NotBeNullOrWhiteSpace(
				because: $"the agent must be told the read is safe to retry instead of blocking "
					+ $"indefinitely. {Describe(callResult)}");
			structured.GetProperty("tool").GetString().Should().Be(ListPackagesToolName,
				because: $"the envelope must name the tool that timed out. {Describe(callResult)}");
		}
		finally {
			await stall.StopAsync();
		}
	}

	// ─────────────────────────────────────────────────────────────────────────────────────────────────
	// Assertions and scaffolding
	// ─────────────────────────────────────────────────────────────────────────────────────────────────

	private static void AssertWorkerBudgetEnvelope(CallToolResult callResult, string expectedToolName) {
		callResult.StructuredContent.Should().NotBeNull(
			because: $"a bounded call must return a machine-readable structured envelope. {Describe(callResult)}");
		JsonElement structured = callResult.StructuredContent!.Value;
		structured.GetProperty("error-class").GetString().Should().Be("creatio-timeout",
			because: $"the worker budget deliberately reuses the same wire token as the deadline it replaces, "
				+ $"so client guidance keyed on it keeps applying. {Describe(callResult)}");
		structured.GetProperty("worker-budget-expired").GetBoolean().Should().BeTrue(
			because: $"this marker is the whole behavioural difference: the work was TERMINATED with its "
				+ $"process rather than abandoned while still holding a thread and the tenant monitor. "
				+ $"{Describe(callResult)}");
		structured.TryGetProperty("read-response-timed-out", out JsonElement _).Should().BeFalse(
			because: $"a cohort tool must not be bounded by the in-process read deadline as well — that "
				+ $"marker appearing here would mean the call never left the host process. "
				+ $"{Describe(callResult)}");
		structured.GetProperty("tool").GetString().Should().Be(expectedToolName,
			because: $"the envelope must name the tool that was bounded, unwrapped from any executor. "
				+ $"{Describe(callResult)}");
		structured.GetProperty("budget-seconds").GetInt32().Should()
			.Be(int.Parse(WorkerBudgetSeconds, System.Globalization.CultureInfo.InvariantCulture),
			because: $"an agent cannot choose between narrowing the query and raising the budget without "
				+ $"knowing which budget expired. {Describe(callResult)}");
	}

	private static string Describe(CallToolResult callResult) {
		string structured = callResult.StructuredContent?.GetRawText() ?? "<none>";
		string text = string.Join(" ", callResult.Content
			.OfType<TextContentBlock>()
			.Select(block => block.Text ?? string.Empty));
		string flattened = $"isError={callResult.IsError?.ToString() ?? "null"}, structured={structured}, "
			+ $"text={text}";
		flattened = flattened.Replace('\r', ' ').Replace('\n', ' ');
		return flattened.Length <= 800 ? flattened : $"{flattened[..800]}…";
	}

	/// <summary>
	/// A loopback endpoint that ACCEPTS the TCP connection and never answers. A refused port would instead
	/// fail fast as a transport error, classified long before either bound.
	/// </summary>
	private sealed class StallingEndpoint : IDisposable {

		private readonly TcpListener _listener;
		private readonly CancellationTokenSource _cancellation = new();
		private readonly List<TcpClient> _heldConnections = [];
		private readonly Task _acceptLoop;

		private StallingEndpoint(TcpListener listener) {
			_listener = listener;
			Port = ((IPEndPoint)listener.LocalEndpoint).Port;
			_acceptLoop = Task.Run(AcceptAsync);
		}

		internal int Port { get; }

		internal static StallingEndpoint Start() {
			TcpListener listener = new(IPAddress.Loopback, 0);
			listener.Start();
			return new StallingEndpoint(listener);
		}

		/// <summary>
		/// Stops accepting and releases every held socket. The accept loop is joined FIRST because it is the
		/// only writer to the held-connection list, which turns the disposal below into a single-threaded
		/// read and removes a latent data race.
		/// </summary>
		internal async Task StopAsync() {
			await _cancellation.CancelAsync();
			try {
				await _acceptLoop;
			} catch (OperationCanceledException) {
				// Expected on teardown.
			}
			foreach (TcpClient client in _heldConnections) {
				client.Dispose();
			}
			_listener.Stop();
		}

		public void Dispose() {
			_cancellation.Dispose();
			_listener.Dispose();
		}

		private async Task AcceptAsync() {
			try {
				while (!_cancellation.IsCancellationRequested) {
					// Held open and never written to: this is the stall.
					_heldConnections.Add(await _listener.AcceptTcpClientAsync(_cancellation.Token));
				}
			} catch (OperationCanceledException) {
				// Expected on teardown.
			} catch (ObjectDisposedException) {
				// Expected when the listener stops.
			} catch (SocketException) {
				// Expected when the listener stops.
			}
		}
	}

	/// <summary>
	/// An isolated clio home with one registered environment pointing at the stalling endpoint, plus the
	/// process environment the MCP host under test is started with.
	/// </summary>
	private sealed class DeadlineFixtureHome : IDisposable {

		private readonly string _path;
		private readonly TemporaryClioSettingsOverride _settingsOverride;

		private DeadlineFixtureHome(
			string path,
			string environmentName,
			McpE2ESettings settings,
			TemporaryClioSettingsOverride settingsOverride) {
			_path = path;
			EnvironmentName = environmentName;
			Settings = settings;
			_settingsOverride = settingsOverride;
		}

		internal string EnvironmentName { get; }

		internal McpE2ESettings Settings { get; }

		internal static DeadlineFixtureHome Create(
			string homePrefix,
			string environmentName,
			int stallPort,
			IReadOnlyDictionary<string, string> extraEnvironmentVariables) {
			string path = Path.Combine(Path.GetTempPath(), $"{homePrefix}-{Guid.NewGuid():N}");
			Directory.CreateDirectory(path);
			McpE2ESettings settings = TestConfiguration.Load();
			settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
			settings.ProcessEnvironmentVariables[OperatingSystem.IsWindows() ? "LOCALAPPDATA" : "HOME"] = path;
			foreach (KeyValuePair<string, string> variable in extraEnvironmentVariables) {
				// Read by the fresh clio MCP process at startup, so the static defaults pick the override up.
				settings.ProcessEnvironmentVariables[variable.Key] = variable.Value;
			}
			TemporaryClioSettingsOverride settingsOverride = TemporaryClioSettingsOverride.ReplaceContent(
				$$"""
				{
				  "ActiveEnvironmentKey": "{{environmentName}}",
				  "Environments": {
				    "{{environmentName}}": {
				      "Uri": "http://127.0.0.1:{{stallPort}}",
				      "Login": "Supervisor",
				      "Password": "Supervisor",
				      "IsNetCore": false
				    }
				  }
				}
				""",
				settings.ClioProcessPath,
				settings.ProcessEnvironmentVariables);
			return new DeadlineFixtureHome(path, environmentName, settings, settingsOverride);
		}

		public void Dispose() {
			_settingsOverride.Dispose();
			try {
				if (Directory.Exists(_path)) {
					Directory.Delete(_path, recursive: true);
				}
			} catch (IOException) {
				// Best-effort cleanup.
			} catch (UnauthorizedAccessException) {
				// Best-effort cleanup.
			}
		}
	}
}
