using Clio.Mcp.E2E.Support.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
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

	public static async Task<McpServerSession> StartAsync(
		McpE2ESettings settings,
		CancellationToken cancellationToken,
		string? workingDirectory = null) {
		ClioProcessDescriptor process = ClioExecutableResolver.Resolve(settings);
		StdioClientTransport transport = new(new StdioClientTransportOptions {
			Command = process.Command,
			Arguments = [.. process.Arguments],
			WorkingDirectory = workingDirectory ?? process.WorkingDirectory,
			EnvironmentVariables = settings.ProcessEnvironmentVariables,
			Name = "clio-mcp-e2e",
			ShutdownTimeout = TimeSpan.FromSeconds(10)
		}, NullLoggerFactory.Instance);

		McpClient client = await McpClient.CreateAsync(
			transport,
			new McpClientOptions {
				ClientInfo = new Implementation {
					Name = "clio.mcp.e2e",
					Version = "1.0.0"
				}
			},
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

	public async ValueTask DisposeAsync() {
		await Client.DisposeAsync();
	}
}
