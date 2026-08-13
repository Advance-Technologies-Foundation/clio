// ENG-95262 relay spike — the PARENT, written against the same MCP C# SDK version clio ships (1.4.1).
//
// The Python proxy prototype proved the execution model but could not test the thing that can invalidate
// it: whether a parent built on THIS SDK can relay faithfully. Three properties are under test:
//
//   1. server->client requests. A child tool calling `sampling/createMessage` (what `update-page` does via
//      server.SampleAsync) must reach the REAL client and its answer must come back to the child. If the
//      parent cannot do this, semantic review silently degrades to Skipped=true.
//   2. `_meta` fidelity. A notification carrying `_meta.clioStageEvent` and an exact progress token must
//      arrive at the client unchanged — ClioRing correlates on the token and buffers by (runId, sequence).
//   3. ordering under concurrency.
//
// Everything here is throwaway. It is deliberately built OUTSIDE clio so nothing in the repo is touched.

using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Threading.Channels;

string childCommand = Environment.GetEnvironmentVariable("SPIKE_CHILD_COMMAND") ?? "python3";
string childArgs = Environment.GetEnvironmentVariable("SPIKE_CHILD_ARGS") ?? "spike_child.py";

HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());

builder.Services
	.AddMcpServer(options => {
		options.ServerInfo = new Implementation { Name = "relay-spike-parent", Version = "1" };
		options.Capabilities = new ServerCapabilities { Tools = new ToolsCapability() };
	})
	.WithStdioServerTransport()
	.WithListToolsHandler(async (ctx, ct) => {
		McpClient child = await ChildFactory.GetAsync(childCommand, childArgs, ctx.Server!, ct);
		IList<McpClientTool> tools = await child.ListToolsAsync(cancellationToken: ct);
		return new ListToolsResult { Tools = [.. tools.Select(tool => tool.ProtocolTool)] };
	})
	.WithCallToolHandler(async (ctx, ct) => {
		McpClient child = await ChildFactory.GetAsync(childCommand, childArgs, ctx.Server!, ct);
		IDictionary<string, JsonElement> args =
			ctx.Params?.Arguments ?? new Dictionary<string, JsonElement>();
		return await child.CallToolAsync(
			ctx.Params!.Name,
			args.ToDictionary(pair => pair.Key, pair => (object?)pair.Value),
			cancellationToken: ct);
	});

IHost host = builder.Build();
await host.RunAsync();

static class NotificationRelay {
	private static readonly Channel<(McpServer Server, JsonRpcNotification Notification)> Queue =
		Channel.CreateUnbounded<(McpServer, JsonRpcNotification)>(
			new UnboundedChannelOptions { SingleReader = true });
	private static int _started;

	internal static void Enqueue(McpServer server, JsonRpcNotification notification) {
		if (Interlocked.Exchange(ref _started, 1) == 0) {
			_ = Task.Run(PumpAsync);
		}
		Queue.Writer.TryWrite((server, new JsonRpcNotification {
			Method = notification.Method, Params = notification.Params
		}));
	}

	private static async Task PumpAsync() {
		await foreach ((McpServer server, JsonRpcNotification notification)
			in Queue.Reader.ReadAllAsync()) {
			try {
				await server.SendMessageAsync(notification, CancellationToken.None);
			}
			catch {
				// spike: a dead client is not what is under test
			}
		}
	}
}

static class ChildFactory {
	private static McpClient? _child;
	private static readonly SemaphoreSlim Gate = new(1, 1);

	// One child for the whole spike run: lifetime policy is not what is under test here — fidelity is.
	internal static async Task<McpClient> GetAsync(string command, string args, McpServer parentServer,
		CancellationToken ct) {
		if (_child is not null) {
			return _child;
		}
		await Gate.WaitAsync(ct);
		try {
			if (_child is not null) {
				return _child;
			}
			StdioClientTransport transport = new(new StdioClientTransportOptions {
				Name = "spike-child",
				Command = command,
				Arguments = args.Split(' ', StringSplitOptions.RemoveEmptyEntries)
			});
			McpClientOptions options = new() {
				ClientInfo = new Implementation { Name = "relay-spike-parent-as-client", Version = "1" },
				Capabilities = new ClientCapabilities {
					// (1) Accept the child's sampling request so it can be forwarded to the real client.
					Sampling = new SamplingCapability()
				},
				Handlers = new McpClientHandlers {
					SamplingHandler = async (request, progress, cancellationToken) =>
						// The parent is a server toward the real client: ask IT to sample.
						await parentServer.SampleAsync(request!, cancellationToken),
					NotificationHandlers = [
						// (2) + (3) Re-emit the child's notification with its Params object untouched, so
						// `_meta` and the progress token survive unchanged and in order.
						new KeyValuePair<string, Func<JsonRpcNotification, CancellationToken, ValueTask>>(
							"notifications/progress",
							// Enqueue instead of sending inline: a single consumer re-emits in FIFO order.
							// If the SDK dispatches handlers concurrently, enqueue order is already lost —
							// which is exactly what this variant of the spike measures.
							(notification, cancellationToken) => {
								// Diagnostic: record the order in which the SDK INVOKES the handler.
								Console.Error.WriteLine($"handler-entry {notification.Params}");
								NotificationRelay.Enqueue(parentServer, notification);
								return ValueTask.CompletedTask;
							})
					]
				}
			};
			_child = await McpClient.CreateAsync(transport, options, loggerFactory: null,
				cancellationToken: ct);
			return _child;
		}
		finally {
			Gate.Release();
		}
	}
}
