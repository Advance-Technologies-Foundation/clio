using Clio.Mcp.E2E.Support.Configuration;

namespace Clio.Mcp.E2E.Support.Mcp;

/// <summary>
/// Shared arrange context for MCP E2E tests. Starts a fresh clio MCP server process and holds the
/// session and cancellation token source for the duration of a single test.
/// </summary>
internal sealed record McpSessionArrangeContext(
	McpServerSession Session,
	CancellationTokenSource CancellationTokenSource) : IAsyncDisposable {

	internal static async Task<McpSessionArrangeContext> ArrangeAsync(TimeSpan timeout) {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		CancellationTokenSource cancellationTokenSource = new(timeout);
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new McpSessionArrangeContext(session, cancellationTokenSource);
	}

	public async ValueTask DisposeAsync() {
		await Session.DisposeAsync();
		CancellationTokenSource.Dispose();
	}
}
