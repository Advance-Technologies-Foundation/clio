using System.Collections.Generic;

namespace Clio.Common;

/// <summary>
/// Convenience helpers over <see cref="ILogger"/>.
/// </summary>
public static class LoggerExtensions {

	/// <summary>
	/// Writes each value as a warning line.
	/// </summary>
	/// <param name="logger">The logger to write to.</param>
	/// <param name="values">The warning lines to write.</param>
	public static void WriteWarnings(this ILogger logger, IEnumerable<string> values) {
		foreach (string value in values) {
			logger.WriteWarning(value);
		}
	}
}
