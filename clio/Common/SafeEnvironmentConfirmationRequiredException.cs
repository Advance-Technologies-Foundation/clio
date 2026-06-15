using System;

namespace Clio.Common;

/// <summary>
/// Thrown when an operation targets a Safe-flagged (production) environment but the confirmation
/// prompt was declined or the context is non-interactive (the stdio MCP server, CI). It is a
/// dedicated type — deliberately <b>not</b> <see cref="OperationCanceledException"/> — so it cannot
/// be silently swallowed by task-cancellation plumbing and surfaces as a structured error instead
/// of hanging the process.
/// </summary>
public sealed class SafeEnvironmentConfirmationRequiredException : Exception {
	/// <summary>Initializes the exception for the given environment URI.</summary>
	/// <param name="environmentUri">The Safe environment URI whose confirmation was declined.</param>
	public SafeEnvironmentConfirmationRequiredException(string environmentUri)
		: base($"Safe environment confirmation required but it was declined or the context is " +
			$"non-interactive. Environment: '{environmentUri}'.") {
	}
}
