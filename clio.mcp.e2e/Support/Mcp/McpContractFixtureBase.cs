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

	[OneTimeSetUp]
	public async Task StartSharedMcpServerAsync() {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		using CancellationTokenSource startupCts = new(TimeSpan.FromMinutes(5));
		_session = await McpServerSession.StartAsync(settings, startupCts.Token);
	}

	[OneTimeTearDown]
	public async Task StopSharedMcpServerAsync() {
		if (_session is not null) {
			await _session.DisposeAsync();
		}
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
