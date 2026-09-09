using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Allure.Net.Commons;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Relay;
using Clio.Command.McpServer.Tools;
using Clio.Common.McpWorker;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Creatio;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// TC-E-603: a Stage 6 cohort tool answers IDENTICALLY through a worker process and in-process, reached
/// through two of the three dispatch sites — the matched name (site a) and <c>clio-run</c> (site c).
/// </summary>
/// <remarks>
/// <para>
/// <b>How the in-process arm is obtained, and why it is not the DI substitution the story describes.</b>
/// The story's wording ("substituting the metadata reader in DI") assumes an in-process test. This suite
/// drives a real <c>clio mcp-server</c> over stdio, so no container of the process under test is reachable.
/// The arm used instead is a genuine substitution of WHERE the tool executes rather than a simulated one:
/// a <c>clio mcp-server --worker</c> child, driven directly by this fixture as its MCP server. A worker
/// process is refused the worker path by the recursion guard
/// (<c>McpWorkerPathAvailability.ProcessIsWorker</c>), so it executes every cohort tool in its own process
/// — which is exactly the in-process arm, produced by shipped behaviour rather than by a test hook. The
/// fixture asserts that property rather than assuming it: the in-process arm must spawn NO worker at all.
/// </para>
/// <para>
/// <b>Both dispatch sites are covered on purpose.</b> The wedge fixture reaches <c>list-pages</c> as a
/// resident matched name, which is site (a) only. Site (c) — the <c>clio-run</c> inner dispatch, where the
/// router keys on the UNWRAPPED command and the relay forwards the executor call verbatim — is the vector
/// agents actually use for the non-resident half of the cohort, and nothing else in the suite exercises it
/// through a worker.
/// </para>
/// <para>
/// Payloads are compared as the tool's JSON ENVELOPE, not as the whole <see cref="CallToolResult"/>: the
/// two arms legitimately differ in protocol furniture (the parent's own <c>_meta</c>, the negotiated
/// revision), and asserting on that would fail for reasons that have nothing to do with the answer.
/// </para>
/// </remarks>
[TestFixture]
[AllureNUnit]
[NonParallelizable]
public sealed class McpWorkerCohortParityE2ETests {
	private const string ListPagesToolName = PageListTool.ToolName;
	private const string EnvironmentName = "cohort-parity-stub-e2e";

	/// <summary>Bounds the whole fixture; individual calls answer in about a second against the stub.</summary>
	private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("TC-E-603: drives a Stage 6 cohort tool through a real clio MCP host (worker path) and through a clio mcp-server --worker child (in-process arm, forced by the recursion guard), by matched name and through clio-run, and asserts the four JSON envelopes are identical — while the in-process arm demonstrably spawns no worker of its own.")]
	[AllureFeature(ListPagesToolName)]
	[AllureTag(ListPagesToolName)]
	[AllureName("A cohort tool answers identically through a worker and in-process, by matched name and through clio-run")]
	[AllureDescription("Points a registered environment at the deterministic Creatio stub in healthy mode. Calls list-pages twice against an ordinary clio mcp-server — once by its matched name (dispatch site a) and once through clio-run (dispatch site c, where the router keys on the unwrapped inner command) — and twice more against a clio mcp-server --worker child driven directly, which the recursion guard forces to execute in-process. Compares the tool's JSON envelope across all four, asserts the worker arm really did spawn children by reading the host's own worker registry, and asserts the in-process arm spawned none.")]
	public async Task CohortTool_Should_Answer_Identically_ThroughAWorker_AndInProcess() {
		// Arrange
		await using CreatioWedgeStubServer stub = CreatioWedgeStubServer.Start();
		stub.SetMode(CreatioWedgeStubMode.Healthy);
		stub.SetLoginDelay(TimeSpan.Zero);
		string tempHome = Path.Combine(Path.GetTempPath(), $"clio-parity-e2e-{Guid.NewGuid():N}");
		Directory.CreateDirectory(tempHome);
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string homeVariableName = OperatingSystem.IsWindows() ? "LOCALAPPDATA" : "HOME";
		settings.ProcessEnvironmentVariables[homeVariableName] = tempHome;
		settings.ProcessEnvironmentVariables["CLIO_HOME"] = tempHome;
		settings.ProcessEnvironmentVariables[McpWorkerCallDispatcher.BudgetOverrideEnvVar] =
			((int)Budget.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
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
		settingsOverride.AppSettingsPath.Should().StartWith(tempHome,
			because: "the replaced settings file must live in this fixture's own clio home, never the "
				+ "assembly-shared one every other fixture depends on");
		using CancellationTokenSource cancellation = new(TimeSpan.FromMinutes(4));
		Dictionary<string, object?> toolArguments = new() {
			["args"] = new Dictionary<string, object?> { ["environment-name"] = EnvironmentName }
		};
		Dictionary<string, object?> clioRunArguments = new() {
			["command"] = ListPagesToolName,
			["args"] = new Dictionary<string, object?> { ["environment-name"] = EnvironmentName }
		};

		string workerArmMatched;
		string workerArmViaClioRun;
		string inProcessArmMatched;
		string inProcessArmViaClioRun;
		IReadOnlyList<ObservedWorker> workersDuringWorkerArm;
		IReadOnlyList<ObservedWorker> workersDuringInProcessArm;
		try {
			// Act — arm 1: the ordinary host, where a cohort tool is relayed to a child.
			await using (WorkerSpawnObserver hostObserver = WorkerSpawnObserver.Start(tempHome)) {
				await using McpServerSession host = await McpServerSession.StartAsync(settings, cancellation.Token);
				workerArmMatched = await AllureApi.Step(
					"Worker path, dispatch site (a): list-pages by its matched name",
					async () => ReadEnvelope(await host.CallToolRawAsync(
						ListPagesToolName, toolArguments, cancellation.Token)));
				workerArmViaClioRun = await AllureApi.Step(
					"Worker path, dispatch site (c): the same tool through clio-run",
					async () => ReadEnvelope(await host.CallToolRawAsync(
						ClioRunTool.ToolName, clioRunArguments, cancellation.Token)));
				hostObserver.ReadFailures.Should().BeEmpty(
					because: "a failed registry read would make the spawn observation below meaningless");
				workersDuringWorkerArm = hostObserver.Observed;
			}

			// Act — arm 2: a worker child as the server. The recursion guard refuses it the worker path, so
			// it runs the tool itself; that is the in-process arm, and it is shipped behaviour, not a hook.
			await using (WorkerSpawnObserver childObserver = WorkerSpawnObserver.Start(tempHome)) {
				await using DirectWorkerServer worker = await DirectWorkerServer.StartAsync(
					settings, tempHome, cancellation.Token);
				inProcessArmMatched = await AllureApi.Step(
					"In-process arm, dispatch site (a): the same call served by a worker child, which may not relay",
					async () => ReadEnvelope(await worker.CallToolAsync(
						ListPagesToolName, toolArguments, cancellation.Token)));
				inProcessArmViaClioRun = await AllureApi.Step(
					"In-process arm, dispatch site (c): the same call through clio-run inside the worker child",
					async () => ReadEnvelope(await worker.CallToolAsync(
						ClioRunTool.ToolName, clioRunArguments, cancellation.Token)));
				childObserver.ReadFailures.Should().BeEmpty(
					because: "a failed registry read would make the recursion-guard assertion below meaningless");
				workersDuringInProcessArm = childObserver.Observed;
			}

			// Assert
			string diagnostics =
				$"worker/matched:     {Shorten(workerArmMatched)}\n"
				+ $"worker/clio-run:    {Shorten(workerArmViaClioRun)}\n"
				+ $"in-process/matched: {Shorten(inProcessArmMatched)}\n"
				+ $"in-process/clio-run:{Shorten(inProcessArmViaClioRun)}\n"
				+ $"stub: {stub.DescribeState()}";
			stub.UnexpectedHandlerFailures.Should().BeEmpty(
				because: $"a broken stub would make every envelope below equally wrong, which reads as "
					+ $"parity.\n{diagnostics}");
			AllureApi.Step("Every arm actually ANSWERED — parity between four failures would be worthless", () => {
				foreach ((string label, string envelope) in new[] {
					("worker/matched", workerArmMatched),
					("worker/clio-run", workerArmViaClioRun),
					("in-process/matched", inProcessArmMatched),
					("in-process/clio-run", inProcessArmViaClioRun)
				}) {
					IsSuccessful(envelope).Should().BeTrue(
						because: $"{label} must report success:true with page data; four identical failures "
							+ $"would satisfy a parity assertion while proving nothing.\n{diagnostics}");
				}
			});
			AllureApi.Step("Dispatch site (a): the worker's answer is byte-identical to the in-process answer", () =>
				workerArmMatched.Should().Be(inProcessArmMatched,
					because: $"moving execution into a child process must not change the answer — that is the "
						+ $"whole premise of relaying the MCP contract rather than translating it.\n{diagnostics}"));
			AllureApi.Step("Dispatch site (c): the same holds when the tool is reached through clio-run", () =>
				workerArmViaClioRun.Should().Be(inProcessArmViaClioRun,
					because: $"the clio-run site relays the executor call verbatim and the child unwraps it "
						+ $"itself, so an agent reaching a cohort tool through the executor must get the same "
						+ $"answer as one naming it directly.\n{diagnostics}"));
			AllureApi.Step("The two dispatch sites agree with each other as well", () =>
				workerArmViaClioRun.Should().Be(workerArmMatched,
					because: $"one tool reached two ways is one answer; a difference here would mean the "
						+ $"relayed params diverged between the sites.\n{diagnostics}"));
			AllureApi.Step("The worker arm really did spawn children — observed, not inferred", () =>
				workersDuringWorkerArm.Should().NotBeEmpty(
					because: $"if the host had quietly executed both calls itself, the envelopes would match "
						+ $"trivially and this test would certify nothing.\n{diagnostics}"));
			AllureApi.Step("THE RECURSION GUARD: the in-process arm spawned no worker of its own", () =>
				workersDuringInProcessArm.Should().BeEmpty(
					because: $"a worker that relayed would hand its child the very call it was given, and that "
						+ $"child another — unbounded process creation. It is also what makes this arm an "
						+ $"in-process arm at all.\n{diagnostics}"));
		} finally {
			TryDeleteDirectory(tempHome);
		}
	}

	/// <summary>
	/// Extracts the tool's JSON envelope, normalised for comparison: the structured content when present,
	/// otherwise the first text block that parses as a JSON object (clio's server emits the envelope as
	/// text — see the wedge fixture's <c>IsSuccessfulAnswer</c> remarks).
	/// </summary>
	private static string ReadEnvelope(CallToolResult callResult) {
		if (callResult.StructuredContent is JsonElement structured
			&& structured.ValueKind == JsonValueKind.Object) {
			return Normalise(structured);
		}
		foreach (TextContentBlock block in callResult.Content.OfType<TextContentBlock>()) {
			if (string.IsNullOrWhiteSpace(block.Text)) {
				continue;
			}
			try {
				using JsonDocument document = JsonDocument.Parse(block.Text);
				if (document.RootElement.ValueKind == JsonValueKind.Object) {
					return Normalise(document.RootElement);
				}
			} catch (JsonException) {
				// A prose block, not the envelope.
			}
		}
		// Returned rather than thrown so the failure carries the whole diagnostic block instead of an
		// exception from inside a step.
		return $"<no json envelope> isError={callResult.IsError?.ToString() ?? "null"} "
			+ string.Join(" ", callResult.Content.OfType<TextContentBlock>().Select(block => block.Text));
	}

	// Re-serialised through a fixed writer so key ORDER differences between two independent serialisations
	// cannot fail a comparison that is about content.
	private static string Normalise(JsonElement element) =>
		JsonSerializer.Serialize(element, new JsonSerializerOptions { WriteIndented = false });

	private static bool IsSuccessful(string envelope) {
		try {
			using JsonDocument document = JsonDocument.Parse(envelope);
			return document.RootElement.ValueKind == JsonValueKind.Object
				&& document.RootElement.TryGetProperty("success", out JsonElement success)
				&& success.ValueKind == JsonValueKind.True;
		} catch (JsonException) {
			return false;
		}
	}

	private static string Shorten(string value) {
		string flattened = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
		return flattened.Length <= 400 ? flattened : $"{flattened[..400]}…";
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

	/// <summary>
	/// One <c>clio mcp-server --worker</c> child driven directly by this fixture as its MCP server — the
	/// in-process arm.
	/// </summary>
	/// <remarks>
	/// The environment is composed the way the production supervisor composes it (cleared, then the
	/// supervisor's own allowlist, then the explicit variables), so the child sees this fixture's clio home
	/// and nothing ambient. Standard error is drained continuously: an undrained pipe eventually BLOCKS the
	/// child, which would surface as an unexplained hang rather than a failed assertion.
	/// </remarks>
	private sealed class DirectWorkerServer : IAsyncDisposable {

		private readonly Process _process;
		private readonly McpClient _client;
		private readonly StringBuilder _standardError = new();

		private DirectWorkerServer(Process process, McpClient client) {
			_process = process;
			_client = client;
		}

		internal static async Task<DirectWorkerServer> StartAsync(
			McpE2ESettings settings,
			string clioHome,
			CancellationToken cancellationToken) {
			ClioProcessDescriptor descriptor = ClioExecutableResolver.Resolve(
				settings, "mcp-server", McpWorkerEnvironment.WorkerFlag);
			ProcessStartInfo startInfo = new() {
				FileName = descriptor.Command,
				WorkingDirectory = descriptor.WorkingDirectory,
				UseShellExecute = false,
				RedirectStandardInput = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			};
			foreach (string argument in descriptor.Arguments) {
				startInfo.ArgumentList.Add(argument);
			}
			startInfo.Environment.Clear();
			foreach (string name in WorkerProcessSupervisor.DefaultInheritedEnvironmentVariableAllowlist) {
				string? value = Environment.GetEnvironmentVariable(name);
				if (value is not null) {
					startInfo.Environment[name] = value;
				}
			}
			startInfo.Environment["CLIO_HOME"] = clioHome;
			startInfo.Environment[OperatingSystem.IsWindows() ? "LOCALAPPDATA" : "HOME"] = clioHome;
			startInfo.Environment["CLIO_NO_UPDATE_CHECK"] = "true";
			Process process = Process.Start(startInfo)
				?? throw new InvalidOperationException("Unable to start the clio MCP worker child process.");
			DirectWorkerServer server = new(process, await ConnectAsync(process, cancellationToken));
			server.DrainStandardError();
			return server;
		}

		private static async Task<McpClient> ConnectAsync(Process process, CancellationToken cancellationToken) {
			StreamClientTransport transport = new(
				process.StandardInput.BaseStream,
				process.StandardOutput.BaseStream,
				NullLoggerFactory.Instance);
			return await McpClient.CreateAsync(
				transport,
				new McpClientOptions {
					ClientInfo = new Implementation {
						Name = "clio.mcp.e2e.cohort-parity", Version = "1.0.0"
					}
				},
				NullLoggerFactory.Instance,
				cancellationToken);
		}

		internal async Task<CallToolResult> CallToolAsync(
			string toolName,
			IReadOnlyDictionary<string, object?> arguments,
			CancellationToken cancellationToken) {
			using CancellationTokenSource callBudget =
				CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			callBudget.CancelAfter(Budget);
			return await _client.CallToolAsync(toolName, arguments, cancellationToken: callBudget.Token);
		}

		public async ValueTask DisposeAsync() {
			try {
				await _client.DisposeAsync();
			} catch (Exception) {
				// The child's pipes may already be gone; teardown must not mask the test's own result.
			}
			try {
				if (!_process.HasExited) {
					_process.Kill(entireProcessTree: true);
				}
			} catch (Exception) {
				// Best-effort: the process may have exited between the check and the kill.
			}
			_process.Dispose();
		}

		private void DrainStandardError() =>
			_ = Task.Run(async () => {
				try {
					string? line;
					while ((line = await _process.StandardError.ReadLineAsync()) is not null) {
						lock (_standardError) {
							_standardError.AppendLine(line);
						}
					}
				} catch (Exception) {
					// The pipe closing when the child exits is the ordinary end of this loop.
				}
			});
	}
}
