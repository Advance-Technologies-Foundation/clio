using Clio.Mcp.E2E.Support;
using Clio.Mcp.E2E.Support.Configuration;

namespace Clio.Mcp.E2E.Support.Mcp;

/// <summary>
/// Base class for fixtures that can share one clio MCP server process.
/// Starts a single clio MCP server process once for the entire fixture
/// and tears it down after all tests finish, eliminating the per-test
/// startup overhead (~10 s on the TeamCity agent, ~4 s on a Linux workstation).
/// </summary>
/// <remarks>
/// <para>
/// A fixture may inherit when every test would start the server with the SAME settings — the
/// NoEnvironment contract fixtures, and the Sandbox fixtures whose tests load identical
/// <c>TestConfiguration</c> settings (ApplicationTool, DataForge, WorkspaceSync, …). The server itself
/// touches no stand: starting it performs the MCP handshake and nothing else, so the destructive opt-in,
/// <c>EnsureSandboxIsConfigured</c>, the reachability probe and every <c>Assert.Ignore</c> stay INSIDE the
/// test bodies (NUnit then still reports skips per test; a stand that is missing never turns into a
/// whole-fixture <c>[OneTimeSetUp]</c> failure). <c>clio.tests/McpFixturePolicyTests</c> pins that rule.
/// </para>
/// <para>
/// What a shared process does keep between tests is the server's own per-tenant state: one DI container
/// and therefore one authenticated Creatio session per environment key, with a 5-minute idle eviction
/// (<c>SessionContainerCache</c>), refreshed by <c>ReauthExecutor</c> when the platform rejects a stale
/// cookie after a recycle. That is production behaviour — a real agent's server lives across recycles
/// too — and no shared-server assertion depends on a fresh login.
/// </para>
/// <para>
/// Fixtures that need a DIFFERENT server per test — an <c>appsettings.json</c> or environment variable
/// that has to be in place before the child starts (SettingsHealthToolE2ETests, the deadline and
/// worker-budget fixtures), or a raw process to test shutdown behaviour (McpServerShutdownE2ETests) —
/// must NOT inherit from this class; a single test with such a need starts and owns its private
/// <see cref="McpServerSession"/> instead (see the DataForge proxy-poisoning test).
/// </para>
/// </remarks>
public abstract class McpContractFixtureBase {

	private McpServerSession? _session;
	private readonly List<string> _fixtureDirectories = [];

	[OneTimeSetUp]
	public async Task StartSharedMcpServerAsync() {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = string.IsNullOrWhiteSpace(settings.ClioProcessPath)
			? TestConfiguration.ResolveFreshClioProcessPath()
			: Path.GetFullPath(settings.ClioProcessPath);
		ConfigureMcpServerSettings(settings);
		using CancellationTokenSource startupCts = new(TimeSpan.FromMinutes(5));
		_session = await McpServerSession.StartAsync(settings, startupCts.Token);
	}

	[OneTimeTearDown]
	public async Task StopSharedMcpServerAsync() {
		try {
			if (_session is not null) {
				await _session.DisposeAsync();
			}
		} finally {
			CleanupFixtureDirectories();
		}
	}

	/// <summary>
	/// Allows a derived fixture to customize the child MCP server settings before the
	/// shared process starts. Use this for fixture-scoped environment overrides such as
	/// <c>CLIO_HOME</c>, <c>HOME</c>, <c>USERPROFILE</c>, or feature-specific test inputs —
	/// or to set <see cref="McpE2ESettings.ClientInfo"/> when the fixture needs the session
	/// to present a specific client identity during the MCP "initialize" handshake.
	/// </summary>
	/// <param name="settings">The settings that will be used to start the shared MCP server process.</param>
	/// <remarks>
	/// Implementations must mutate only <see cref="McpE2ESettings.ProcessEnvironmentVariables"/>,
	/// <see cref="McpE2ESettings.ClientInfo"/>, or other child-process/client-session settings.
	/// Do not call <see cref="Environment.SetEnvironmentVariable(string, string?)"/>.
	/// </remarks>
	private protected virtual void ConfigureMcpServerSettings(McpE2ESettings settings) {
	}

	/// <summary>
	/// Creates a temporary directory owned by this fixture and removes it during one-time teardown.
	/// </summary>
	/// <param name="purpose">A short filesystem-safe suffix that identifies why the directory exists.</param>
	/// <returns>The full path to the created directory.</returns>
	private protected string CreateFixtureDirectory(string purpose) {
		string safePurpose = string.IsNullOrWhiteSpace(purpose)
			? "fixture"
			: string.Concat(purpose.Select(character => char.IsLetterOrDigit(character) ? character : '-'));
		// The temp root is resolved to its physical location because clio refuses a knowledge root whose
		// ancestry contains a reparse point, and the system temp directory is itself a symlink on macOS.
		string directoryPath = Path.Combine(
			PhysicalPath.Resolve(Path.GetTempPath()),
			$"clio-mcp-e2e-{safePurpose}-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directoryPath);
		_fixtureDirectories.Add(directoryPath);
		return directoryPath;
	}

	/// <summary>
	/// Creates an isolated clio home, writes <c>appsettings.json</c>, and tracks it for cleanup.
	/// </summary>
	/// <param name="appSettingsJson">The appsettings JSON content for the isolated home.</param>
	/// <param name="purpose">A short filesystem-safe suffix that identifies why the home exists.</param>
	/// <returns>The full path to the isolated clio home.</returns>
	private protected string CreateIsolatedClioHome(string appSettingsJson, string purpose = "clio-home") {
		string clioHome = CreateFixtureDirectory(purpose);
		File.WriteAllText(Path.Combine(clioHome, "appsettings.json"), appSettingsJson);
		return clioHome;
	}

	private void CleanupFixtureDirectories() {
		foreach (string directoryPath in _fixtureDirectories) {
			if (!Directory.Exists(directoryPath)) {
				continue;
			}

			// Best-effort: a child clio process can still hold a handle on the isolated
			// home's appsettings.json when teardown runs, so a leaked temp dir is harmless
			// and must not fail an otherwise-green fixture.
			try {
				Directory.Delete(directoryPath, recursive: true);
			} catch (IOException) {
			} catch (UnauthorizedAccessException) {
			}
		}
		_fixtureDirectories.Clear();
	}

	/// <summary>
	/// The shared MCP server session started once for the whole fixture.
	/// Use this from fixtures that build their own context record carrying extra
	/// per-test fields (e.g. a resolved environment name or workspace path) instead of
	/// the lightweight <see cref="ArrangeContext"/>; pair it with a per-test
	/// <see cref="CancellationTokenSource"/> and do NOT dispose the session yourself —
	/// the fixture owns its lifecycle.
	/// </summary>
	private protected McpServerSession Session => _session!;

	// Suppressed: NUnit1032 sees Task<T> as IDisposable, but this is a completed, successfully resolved
	// probe result that owns no handle — Task.Dispose would be a no-op here, and tearing it down would
	// only discard the memoized answer the tests are meant to share.
	[System.Diagnostics.CodeAnalysis.SuppressMessage("Structure", "NUnit1032:An IDisposable field/property should be Disposed in a TearDown method",
		Justification = "A cached, already-completed probe Task holds no disposable resource.")]
	private Task<string>? _environmentResolvedOnce;

	/// <summary>
	/// Runs an environment-resolution probe (typically one or two <c>clio ping-app</c> child processes
	/// ending in <see cref="Assert.Ignore(string)"/> when nothing is reachable) once per fixture and hands
	/// every later test the same answer, re-probing only if the previous attempt failed.
	/// </summary>
	/// <param name="probe">The fixture's own probe; invoked only by the first caller.</param>
	/// <returns>The resolved environment name: the cached one when a previous test already resolved it.</returns>
	/// <remarks>
	/// <para>
	/// Call it from the test body, not from <c>[OneTimeSetUp]</c>: the probe ends in an
	/// <c>Assert.Ignore</c> when nothing is reachable, and NUnit must report that per test rather than as
	/// a whole-fixture setup outcome.
	/// </para>
	/// <para>
	/// ONLY a successful resolution is cached. A faulted probe is deliberately not remembered: memoizing
	/// the fault would turn one transient window — an app-pool recycle, the global OData rebuild — into a
	/// skip for every remaining test of a 16-test fixture, reported with a stale reason and
	/// indistinguishable from an unconfigured workstation. Re-probing after a failure costs one
	/// <c>ping-app</c> process; the saving this method exists for is on the success path, which still
	/// probes exactly once.
	/// </para>
	/// <para>
	/// Assignment is not atomic and needs no lock: NUnit never runs two tests of one fixture concurrently
	/// under <c>ParallelScope.Self</c> (or <c>[NonParallelizable]</c>), which is every fixture in this
	/// project — there is no assembly-level <c>[Parallelizable]</c> and no <c>ParallelScope.Children</c>.
	/// </para>
	/// </remarks>
	private protected async Task<string> ResolveEnvironmentOnceAsync(Func<Task<string>> probe) {
		if (_environmentResolvedOnce is not null) {
			return await _environmentResolvedOnce;
		}
		Task<string> attempt = probe();
		string resolved = await attempt;
		_environmentResolvedOnce = attempt;
		return resolved;
	}

	/// <summary>
	/// Returns an <see cref="ArrangeContext"/> that references the shared server and
	/// a fresh per-test <see cref="CancellationTokenSource"/> with the given timeout.
	/// The context does NOT stop the server when disposed.
	/// Use <c>await using var context = Arrange(...);</c> in test bodies.
	/// </summary>
	private protected ArrangeContext Arrange(TimeSpan? timeout = null) =>
		new(_session!, new CancellationTokenSource(timeout ?? TimeSpan.FromMinutes(2)));

	/// <summary>
	/// Lightweight per-test context that carries the shared <see cref="McpServerSession"/>
	/// and a per-test <see cref="CancellationTokenSource"/>.
	/// Disposing this context cancels the per-test CTS but leaves the server running.
	/// </summary>
	internal sealed record ArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource) : IAsyncDisposable {

		public ValueTask DisposeAsync() {
			CancellationTokenSource.Dispose();
			return ValueTask.CompletedTask;
		}
	}
}
