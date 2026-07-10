using System;

namespace Clio.Command.McpServer;

/// <summary>
/// Shared policy for the resilience catch blocks on the MCP surface that deliberately degrade a
/// failure into a warning or a structured result instead of failing the call (e.g. diagnostic
/// enrichment that must never fail an otherwise-valid operation, or the <c>clio-run</c> dispatch
/// boundary). Such a catch must still NOT swallow exceptions that signal a programming defect or a
/// compromised process — masking those as a benign degradation hides real bugs (project rule:
/// "do not write bare catch (Exception)"). Use <see cref="IsUnrecoverable"/> in a
/// <c>catch (Exception ex) when (!McpExceptionPolicy.IsUnrecoverable(ex))</c> filter so the
/// open-ended set of operational failures (HTTP, I/O, data-layer) degrades while the
/// unrecoverable set propagates to the top-level request boundary.
/// </summary>
internal static class McpExceptionPolicy {

	/// <summary>
	/// Returns <see langword="true"/> for exceptions that must never be degraded into a warning or a
	/// recoverable result — fatal process conditions and clear programming defects. These should
	/// propagate rather than be masked.
	/// </summary>
	/// <param name="exception">The caught exception.</param>
	/// <returns><see langword="true"/> when the exception must propagate; otherwise <see langword="false"/>.</returns>
	public static bool IsUnrecoverable(Exception exception) =>
		exception is OutOfMemoryException
			or StackOverflowException
			or AccessViolationException
			or NullReferenceException
			or IndexOutOfRangeException;
}
