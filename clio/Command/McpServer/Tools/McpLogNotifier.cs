using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Clio.Common;
using ModelContextProtocol.Protocol;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Forwards tool execution log messages to the MCP client as <c>notifications/message</c>
/// notifications, enabling real-time observability in MCP Inspector and other clients.
/// Uses a static accessor (same pattern as <see cref="McpToolExecutionLock"/>) to avoid
/// modifying all 40+ BaseTool-derived constructors.
/// </summary>
internal static class McpLogNotifier {

	private static volatile ModelContextProtocol.Server.McpServer _server;

	/// <summary>
	/// Initializes the notifier with the active MCP server instance.
	/// Must be called once before the server enters the message loop.
	/// </summary>
	internal static void Initialize(ModelContextProtocol.Server.McpServer server) => _server = server;

	/// <summary>
	/// Clears the active MCP server reference. Intended for tests and graceful shutdown
	/// so that stale server instances do not receive notifications from later executions.
	/// </summary>
	internal static void Reset() => _server = null;

	/// <summary>
	/// Sends each <see cref="LogMessage"/> as an MCP logging notification to the connected client.
	/// Respects the client-configured logging level threshold. Notifications are dispatched
	/// asynchronously and errors are swallowed so that log forwarding never breaks tool execution.
	/// </summary>
	/// <param name="messages">Log messages collected during tool execution.</param>
	/// <param name="correlationId">Optional correlation ID used as the logger category suffix.</param>
	internal static void ForwardMessages(IReadOnlyList<LogMessage> messages, string correlationId = null) {
		ModelContextProtocol.Server.McpServer server = _server;
		if (server is null || messages is null || messages.Count == 0) {
			return;
		}

		// Snapshot inputs so the background task works against a stable view.
		LogMessage[] snapshot = messages.ToArray();
		_ = ForwardMessagesAsync(server, snapshot, correlationId);
	}

	private static async Task ForwardMessagesAsync(
		ModelContextProtocol.Server.McpServer server,
		IReadOnlyList<LogMessage> messages,
		string correlationId) {
		LoggingLevel? threshold = server.LoggingLevel;
		string loggerName = correlationId != null ? $"clio.tool.{correlationId}" : "clio.tool";

		foreach (LogMessage msg in messages) {
			LoggingLevel level = MapToMcpLevel(msg);

			if (threshold.HasValue && level < threshold.Value) {
				continue;
			}

			string text = msg.Value?.ToString();
			if (string.IsNullOrEmpty(text)) {
				continue;
			}

			try {
				await server.SendNotificationAsync(
					NotificationMethods.LoggingMessageNotification,
					new LoggingMessageNotificationParams {
						Level = level,
						Logger = loggerName,
						Data = JsonSerializer.SerializeToElement(text)
					}).ConfigureAwait(false);
			}
			catch {
				// Forwarding logs must never break tool execution; errors (disconnected
				// client, serialization issues) are intentionally swallowed.
			}
		}
	}

	private static LoggingLevel MapToMcpLevel(LogMessage msg) => msg switch {
		ErrorMessage => LoggingLevel.Error,
		WarningMessage => LoggingLevel.Warning,
		DebugMessage => LoggingLevel.Debug,
		_ => LoggingLevel.Info
	};
}
