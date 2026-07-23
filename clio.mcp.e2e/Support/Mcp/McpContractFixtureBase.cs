using Clio.Mcp.E2E.Support.Configuration;

namespace Clio.Mcp.E2E.Support.Mcp;

/// <summary>
/// Base class for MCP contract (NoEnvironment) test fixtures.
/// Starts a single clio MCP server process once for the entire fixture
/// and tears it down after all tests finish, eliminating the per-test
/// ~10 s startup overhead.
/// </summary>
/// <remarks>
/// Only use for fixtures that share a read-only or stateless server.
/// Fixtures that modify server-side settings at startup (e.g. SettingsHealthToolE2ETests)
/// or that spawn a raw process to test shutdown behaviour (McpServerShutdownE2ETests)
/// must NOT inherit from this class.
/// </remarks>
public abstract class McpContractFixtureBase {

	private McpServerSession? _session;
	private readonly List<string> _fixtureDirectories = [];

	[OneTimeSetUp]
	public async Task StartSharedMcpServerAsync() {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
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
		string directoryPath = Path.Combine(
			Path.GetTempPath(),
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
