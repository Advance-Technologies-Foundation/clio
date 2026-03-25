using System.Collections.Generic;

namespace Clio.Common;

internal static class LoggerMessageCaptureExtensions {
	internal static IReadOnlyList<LogMessage> FlushAndSnapshotMessages(this ILogger logger, bool clearMessages = false) {
		if (logger is ConsoleLogger consoleLogger) {
			return consoleLogger.FlushAndSnapshotMessages(clearMessages);
		}

		List<LogMessage> snapshot = [.. logger.LogMessages];
		if (clearMessages) {
			logger.ClearMessages();
		}
		return snapshot;
	}
}
