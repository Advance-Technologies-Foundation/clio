using Clio.Mcp.E2E.Support.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E.Support.Mcp;

internal sealed class McpServerSession : IAsyncDisposable {
	private readonly StdioClientTransport _transport;

	private McpServerSession(McpClient client, StdioClientTransport transport) {
		Client = client;
		_transport = transport;
	}

	public McpClient Client { get; }

	public static async Task<McpServerSession> StartAsync(McpE2ESettings settings, CancellationToken cancellationToken) =>
		await StartAsync(settings, elicitationHandler: null, cancellationToken);

	/// <summary>
	/// Starts a clio MCP server session. When <paramref name="elicitationHandler"/> is supplied the
	/// client advertises the elicitation capability and routes server elicitation requests to that
	/// handler — letting a test exercise the elicitation path (for example a client that never
	/// answers, simulating a headless agent). When it is <see langword="null"/> the client behaves as
	/// before and does not advertise elicitation.
	/// </summary>
	public static async Task<McpServerSession> StartAsync(
		McpE2ESettings settings,
		Func<ElicitRequestParams?, CancellationToken, ValueTask<ElicitResult>>? elicitationHandler,
		CancellationToken cancellationToken) {
		ClioProcessDescriptor process = ClioExecutableResolver.Resolve(settings);
		StdioClientTransport transport = new(new StdioClientTransportOptions {
			Command = process.Command,
			Arguments = [.. process.Arguments],
			WorkingDirectory = process.WorkingDirectory,
			EnvironmentVariables = settings.ProcessEnvironmentVariables,
			Name = "clio-mcp-e2e",
			ShutdownTimeout = TimeSpan.FromSeconds(10)
		}, NullLoggerFactory.Instance);

		McpClientOptions options = new() {
			ClientInfo = new Implementation {
				Name = "clio.mcp.e2e",
				Version = "1.0.0"
			}
		};
		if (elicitationHandler is not null) {
			options.Capabilities = new ClientCapabilities { Elicitation = new ElicitationCapability() };
			options.Handlers = new McpClientHandlers { ElicitationHandler = elicitationHandler };
		}

		McpClient client = await McpClient.CreateAsync(
			transport,
			options,
			NullLoggerFactory.Instance,
			cancellationToken);

		return new McpServerSession(client, transport);
	}

	public async Task<IList<McpClientTool>> ListToolsAsync(CancellationToken cancellationToken) =>
		await Client.ListToolsAsync(cancellationToken: cancellationToken);

	public async Task<IList<McpClientResource>> ListResourcesAsync(CancellationToken cancellationToken) =>
		await Client.ListResourcesAsync(cancellationToken: cancellationToken);

	public async Task<ReadResourceResult> ReadResourceAsync(string uri, CancellationToken cancellationToken) =>
		await Client.ReadResourceAsync(uri, cancellationToken: cancellationToken);

	public async Task<CallToolResult> CallToolAsync(
		string toolName,
		IReadOnlyDictionary<string, object?> arguments,
		CancellationToken cancellationToken) =>
		await Client.CallToolAsync(toolName, arguments, cancellationToken: cancellationToken);

	/// <summary>
	/// Invokes a tool while forwarding the server's <c>notifications/progress</c> to
	/// <paramref name="progress"/>. The SDK generates a progress token for the request, so this
	/// overload is the way E2E tests observe the long-running heartbeat (ENG-91274).
	/// </summary>
	public async Task<CallToolResult> CallToolAsync(
		string toolName,
		IReadOnlyDictionary<string, object?> arguments,
		IProgress<ProgressNotificationValue> progress,
		CancellationToken cancellationToken) =>
		await Client.CallToolAsync(toolName, arguments, progress: progress, cancellationToken: cancellationToken);

	public async ValueTask DisposeAsync() {
		await Client.DisposeAsync();
	}
}
