using System;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Thrown by <see cref="IToolCommandResolver"/> for an <em>expected</em>, caller-actionable
/// environment-resolution failure — an unknown environment name, a missing URI, or a broken
/// settings bootstrap. These are validation/precondition errors, not runtime bugs, so the MCP
/// surface maps them to exit code <c>1</c> (see <see cref="CommandExecutionResult.FromResolverError"/>).
/// </summary>
/// <remarks>
/// This type exists specifically so a deliberate "bad environment" failure is distinguishable from
/// an unexpected DI/wiring failure (e.g. <c>GetRequiredService</c> or <c>BindingsModule.Register</c>
/// throwing): those keep propagating as plain exceptions and are mapped to exit code <c>-1</c> via
/// <see cref="CommandExecutionResult.FromException"/>. <c>InvalidOperationException</c> alone cannot
/// separate the two cases because the DI container also throws it.
/// </remarks>
public sealed class EnvironmentResolutionException : Exception {
	/// <summary>Initializes a new instance with the supplied caller-facing message.</summary>
	/// <param name="message">A user-actionable description of the resolution failure.</param>
	public EnvironmentResolutionException(string message) : base(message) {
	}

	/// <summary>Initializes a new instance with the supplied message and inner exception.</summary>
	/// <param name="message">A user-actionable description of the resolution failure.</param>
	/// <param name="innerException">The exception that caused this resolution failure.</param>
	public EnvironmentResolutionException(string message, Exception innerException) : base(message, innerException) {
	}
}
