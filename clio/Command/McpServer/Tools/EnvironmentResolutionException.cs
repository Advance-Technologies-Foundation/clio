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

	/// <summary>Initializes a new instance carrying an explicit <see cref="Reason"/>.</summary>
	public EnvironmentResolutionException(string message, EnvironmentResolutionReason reason) : base(message) {
		Reason = reason;
	}

	/// <summary>Initializes a new instance carrying an explicit <see cref="Reason"/> and inner exception.</summary>
	public EnvironmentResolutionException(string message, EnvironmentResolutionReason reason,
		Exception innerException) : base(message, innerException) {
		Reason = reason;
	}

	/// <summary>
	/// WHAT could not be resolved, so a caller does not have to classify by exception type alone.
	/// </summary>
	/// <remarks>
	/// PR #1373 review. This type is not only "unregistered environment": four of the resolver's throw sites are
	/// authentication and target-URL rejections. Classifying by type alone reported those as
	/// <c>Configuration</c> with "register the environment with reg-web-app" — advice a credential-passthrough
	/// caller over mcp-http cannot act on (it has no environment to register, and <c>reg-web-app</c> is not
	/// reachable over that transport), and an agent branching on the category will not re-authenticate because
	/// the category says the problem is local configuration. Defaults to
	/// <see cref="EnvironmentResolutionReason.Configuration"/> so every existing throw site keeps its current
	/// meaning.
	/// </remarks>
	public EnvironmentResolutionReason Reason { get; } = EnvironmentResolutionReason.Configuration;
}

/// <summary>What an <see cref="EnvironmentResolutionException"/> is actually about.</summary>
public enum EnvironmentResolutionReason {

	/// <summary>The environment is not registered, or the local settings are unusable. The default.</summary>
	Configuration,

	/// <summary>Authentication material is missing or of an unsupported kind. Nothing local to register.</summary>
	Authentication,

	/// <summary>The request itself is refused — an egress/allowlist rejection of the target URL.</summary>
	Validation,
}
