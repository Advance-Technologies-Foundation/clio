using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E.Support.Creatio;

/// <summary>
/// Stands up an isolated clio home, a loopback Creatio stub, a real mcp-server session and an environment
/// registered against the stub, then runs the caller's assertions against them.
/// </summary>
/// <remarks>
/// Every test that needs a REAL environment without a real stand repeats the same six-step arrange, and it is
/// the part that must not drift between fixtures: the isolated home is what keeps a test from writing into
/// the developer's own settings, and a registration that silently failed would turn a genuine tool failure
/// into a missing-environment error that looks like a different bug.
/// </remarks>
internal static class StubEnvironmentStand {

	private const string RegisterToolName = "reg-web-app";

	/// <summary>Runs <paramref name="act"/> against a session whose registered environment points at the stub.</summary>
	/// <param name="homePrefix">Prefix of the isolated home directory, used to tell fixtures apart on disk.</param>
	/// <param name="configuration">Stub behaviour for this run.</param>
	/// <param name="act">Assertions to run against the session, the environment name and the stub.</param>
	/// <param name="timeout">Wall-clock budget for the whole run.</param>
	public static Task RunAsync(
		string homePrefix,
		RuntimeDetectionStubServerConfiguration configuration,
		Func<McpServerSession, string, RuntimeDetectionStubServer, CancellationToken, Task> act,
		TimeSpan? timeout = null) =>
		RunCoreAsync(homePrefix, configuration, (session, environmentName, stubServer, _, ct) =>
			act(session, environmentName, stubServer, ct), timeout, pinClioHome: false);

	/// <summary>
	/// Runs <paramref name="act"/> like <see cref="RunAsync"/>, with <c>CLIO_HOME</c> pinned to the isolated home and
	/// that home passed to the caller, so it can watch the host's worker registry (<see cref="WorkerSpawnObserver"/>).
	/// </summary>
	/// <param name="homePrefix">Prefix of the isolated home directory, used to tell fixtures apart on disk.</param>
	/// <param name="configuration">Stub behaviour for this run.</param>
	/// <param name="act">Assertions to run against the session, the environment name, the stub and the clio home.</param>
	/// <param name="timeout">Wall-clock budget for the whole run.</param>
	public static Task RunWithClioHomeAsync(
		string homePrefix,
		RuntimeDetectionStubServerConfiguration configuration,
		Func<McpServerSession, string, RuntimeDetectionStubServer, string, CancellationToken, Task> act,
		TimeSpan? timeout = null) =>
		RunCoreAsync(homePrefix, configuration, act, timeout, pinClioHome: true);

	private static async Task RunCoreAsync(
		string homePrefix,
		RuntimeDetectionStubServerConfiguration configuration,
		Func<McpServerSession, string, RuntimeDetectionStubServer, string, CancellationToken, Task> act,
		TimeSpan? timeout,
		bool pinClioHome) {
		string tempHome = Path.Combine(Path.GetTempPath(), $"{homePrefix}-{Guid.NewGuid():N}");
		Directory.CreateDirectory(tempHome);
		try {
			string envVarName = OperatingSystem.IsWindows() ? "LOCALAPPDATA" : "HOME";
			McpE2ESettings settings = TestConfiguration.Load();
			settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
			settings.ProcessEnvironmentVariables[envVarName] = tempHome;
			if (pinClioHome) {
				settings.ProcessEnvironmentVariables["CLIO_HOME"] = tempHome;
			}
			using TemporaryClioSettingsOverride settingsOverride = TemporaryClioSettingsOverride.ReplaceContent(
				"""
				{
				  "ActiveEnvironmentKey": null,
				  "Environments": {}
				}
				""",
				settings.ClioProcessPath,
				settings.ProcessEnvironmentVariables);
			await using RuntimeDetectionStubServer stubServer = RuntimeDetectionStubServer.Start(configuration);
			using CancellationTokenSource cancellationTokenSource = new(timeout ?? TimeSpan.FromMinutes(3));
			await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
			string environmentName = $"{homePrefix}-env-{Guid.NewGuid():N}";
			await RegisterEnvironmentAsync(session, environmentName, stubServer.BaseUrl, cancellationTokenSource.Token);

			await act(session, environmentName, stubServer, tempHome, cancellationTokenSource.Token);
		} finally {
			TryDeleteDirectory(tempHome);
		}
	}

	private static void TryDeleteDirectory(string path) {
		try {
			if (Directory.Exists(path)) {
				Directory.Delete(path, recursive: true);
			}
		} catch (IOException) {
			// Best-effort cleanup of the isolated home directory; a leaked temp dir must not fail the test.
		} catch (UnauthorizedAccessException) {
			// Same reasoning: cleanup is not part of what the test proves.
		}
	}

	private static async Task RegisterEnvironmentAsync(
		McpServerSession session,
		string environmentName,
		string baseUrl,
		CancellationToken cancellationToken) {
		IReadOnlyCollection<string> toolNames = await session.ListReachableToolNamesAsync(cancellationToken);
		toolNames.Should().Contain(RegisterToolName,
			because: $"the {RegisterToolName} MCP tool must be discoverable before the test can register the stub environment");

		CallToolResult registerResult = await session.CallToolAsync(
			RegisterToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["uri"] = baseUrl,
					["login"] = "Supervisor",
					["password"] = "Supervisor"
				}
			},
			cancellationToken);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(registerResult);
		execution.ExitCode.Should().Be(0,
			because: "the stub environment must register successfully before the tools can be exercised against it");
	}
}
