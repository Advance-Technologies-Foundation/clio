using System.Collections.Generic;
using System.Text.Json;
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

	private static ModelContextProtocol.Server.McpServer _server;

	/// <summary>
	/// Initializes the notifier with the active MCP server instance.
	/// Must be called once before the server enters the message loop.
	/// </summary>
	internal static void Initialize(ModelContextProtocol.Server.McpServer server) => _server = server;

	/// <summary>
	/// Sends each <see cref="LogMessage"/> as an MCP logging notification to the connected client.
	/// Respects the client-configured logging level threshold.
	/// </summary>
	/// <param name="messages">Log messages collected during tool execution.</param>
	/// <param name="correlationId">Optional correlation ID used as the logger category suffix.</param>
	internal static void ForwardMessages(IReadOnlyList<LogMessage> messages, string correlationId = null) {
		ModelContextProtocol.Server.McpServer server = _server;
		if (server is null || messages is null || messages.Count == 0) {
			return;
		}

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

			server.SendNotificationAsync(
				NotificationMethods.LoggingMessageNotification,
				new LoggingMessageNotificationParams {
					Level = level,
					Logger = loggerName,
					Data = JsonSerializer.SerializeToElement(text)
				}).GetAwaiter().GetResult();
		}
	}

	private static LoggingLevel MapToMcpLevel(LogMessage msg) => msg switch {
		ErrorMessage => LoggingLevel.Error,
		WarningMessage => LoggingLevel.Warning,
		DebugMessage => LoggingLevel.Debug,
		_ => LoggingLevel.Info
	};
}
