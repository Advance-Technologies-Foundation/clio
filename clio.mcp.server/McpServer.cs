using System.Text.Json;

namespace Clio.McpServer;

internal sealed class McpServer {
	private const string JsonRpcVersion = "2.0";
	private readonly McpToolRegistry _toolRegistry;
	private readonly StdioJsonRpcTransport _transport;

	public McpServer(McpToolRegistry toolRegistry, StdioJsonRpcTransport transport, JsonSerializerOptions jsonOptions) {
		_toolRegistry = toolRegistry;
		_transport = transport;
		_ = jsonOptions;
	}

	public async Task RunAsync(CancellationToken cancellationToken) {
		while (!cancellationToken.IsCancellationRequested) {
			JsonDocument? message = await _transport.ReadMessageAsync(cancellationToken);
			if (message is null) {
				return;
			}

			JsonElement root = message.RootElement;
			if (!root.TryGetProperty("method", out JsonElement methodElement)) {
				continue;
			}

			string method = methodElement.GetString() ?? string.Empty;
			JsonElement? id = root.TryGetProperty("id", out JsonElement idElement) ? idElement : null;
			JsonElement @params = root.TryGetProperty("params", out JsonElement paramsElement)
				? paramsElement
				: default;

			if (string.IsNullOrWhiteSpace(method)) {
				continue;
			}

			if (id is null) {
				continue;
			}

			object response = method switch {
				"initialize" => BuildSuccess(id.Value, new {
					protocolVersion = "2024-11-05",
					capabilities = new {
						tools = new {
							listChanged = false
						}
					},
					serverInfo = new {
						name = "clio-mcp-server",
						version = "0.1.0"
					}
				}),
				"tools/list" => BuildSuccess(id.Value, new {
					tools = _toolRegistry.ListTools()
				}),
				"tools/call" => await HandleToolsCallAsync(id.Value, @params),
				"ping" => BuildSuccess(id.Value, new { }),
				_ => BuildError(id.Value, -32601, $"Method '{method}' is not supported")
			};

			await _transport.WriteResponseAsync(response, cancellationToken);
		}
	}

	private async Task<object> HandleToolsCallAsync(JsonElement id, JsonElement @params) {
		if (@params.ValueKind != JsonValueKind.Object) {
			return BuildError(id, -32602, "tools/call requires object params");
		}

		if (!@params.TryGetProperty("name", out JsonElement nameElement)) {
			return BuildError(id, -32602, "tools/call requires tool name");
		}

		string? toolName = nameElement.GetString();
		if (string.IsNullOrWhiteSpace(toolName)) {
			return BuildError(id, -32602, "tools/call requires non-empty tool name");
		}

		JsonElement args = @params.TryGetProperty("arguments", out JsonElement argsElement)
			? argsElement
			: default;

		ToolCallResult result = await _toolRegistry.CallToolAsync(toolName, args);
		return BuildSuccess(id, new {
			content = new[] {
				new {
					type = "text",
					text = result.Text
				}
			},
			structuredContent = result.StructuredContent,
			isError = result.IsError
		});
	}

	private object BuildSuccess(JsonElement id, object result) {
		return new {
			jsonrpc = JsonRpcVersion,
			id,
			result
		};
	}

	private object BuildError(JsonElement id, int code, string message) {
		return new {
			jsonrpc = JsonRpcVersion,
			id,
			error = new {
				code,
				message
			}
		};
	}
}

internal readonly record struct ToolCallResult(bool IsError, string Text, object StructuredContent);
