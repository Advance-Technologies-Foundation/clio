using System;

namespace Clio.Command;

/// <summary>
/// Classifies a failed section-create operation so callers (AI agents, CLI users) can make
/// a rational retry-vs-abandon decision instead of retrying blindly (ENG-90679).
/// </summary>
public enum ApplicationSectionCreateFailureClass {
	/// <summary>
	/// The request never reached the Creatio server (DNS, connect, or TLS failure).
	/// No side effect was produced; retrying is safe once the environment is reachable.
	/// </summary>
	Transport,

	/// <summary>
	/// The request was sent but Creatio produced no response within the budget.
	/// Side effects are unknown: the section may still be created server-side after the timeout.
	/// </summary>
	CreatioTimeout,

	/// <summary>
	/// Creatio responded with an error (HTTP error status, non-JSON body, or a rejected insert).
	/// Retrying the same arguments will most likely fail again.
	/// </summary>
	ServerError,

	/// <summary>
	/// Creatio aborted the insert with a detail-less <c>InsertQuery failed</c> rejection — the signature of
	/// lock/contention when several sections are created in one application at once (ENG-93089). No section
	/// was created, so the operation is safe to retry once the creations are serialized; clio verifies by the
	/// generated section id and auto-retries once before surfacing this class.
	/// </summary>
	Contention
}

/// <summary>
/// Wire-format helpers for <see cref="ApplicationSectionCreateFailureClass"/>.
/// </summary>
public static class ApplicationSectionCreateFailureClassExtensions {
	/// <summary>
	/// Maps the failure class to the kebab-case value carried by the MCP error envelope.
	/// </summary>
	/// <param name="failureClass">Failure class to map.</param>
	/// <returns><c>transport</c>, <c>creatio-timeout</c>, <c>contention</c>, or <c>server-error</c>.</returns>
	public static string ToWireValue(this ApplicationSectionCreateFailureClass failureClass) =>
		failureClass switch {
			ApplicationSectionCreateFailureClass.Transport => "transport",
			ApplicationSectionCreateFailureClass.CreatioTimeout => "creatio-timeout",
			ApplicationSectionCreateFailureClass.Contention => "contention",
			_ => "server-error"
		};
}

/// <summary>
/// Carries the failure classification and the post-failure side-effect verification outcome
/// of a section-create operation, so the MCP tool can return a structured error instead of
/// an opaque message indistinguishable from a transport-level timeout.
/// </summary>
/// <remarks>
/// Derives from <see cref="InvalidOperationException"/> so existing catch sites and callers
/// that match on the previous exception type keep working unchanged.
/// </remarks>
public sealed class ApplicationSectionCreateException : InvalidOperationException {
	/// <summary>
	/// Initializes the classified section-create failure.
	/// </summary>
	/// <param name="message">Human-readable description of what happened.</param>
	/// <param name="failureClass">Failure classification.</param>
	/// <param name="sectionCreated">
	/// Whether the section row was visible during post-failure verification;
	/// <c>null</c> when verification was not possible.
	/// </param>
	/// <param name="retryGuidance">Agent-actionable next step.</param>
	/// <param name="innerException">Original failure, when available.</param>
	public ApplicationSectionCreateException(
		string message,
		ApplicationSectionCreateFailureClass failureClass,
		bool? sectionCreated,
		string retryGuidance,
		Exception? innerException = null)
		: base(message, innerException) {
		FailureClass = failureClass;
		SectionCreated = sectionCreated;
		RetryGuidance = retryGuidance;
	}

	/// <summary>Failure classification surfaced as <c>error-class</c> on the MCP envelope.</summary>
	public ApplicationSectionCreateFailureClass FailureClass { get; }

	/// <summary>
	/// Side-effect verification outcome surfaced as <c>section-created</c> on the MCP envelope:
	/// <c>true</c>/<c>false</c> when verified, <c>null</c> when unknown.
	/// </summary>
	public bool? SectionCreated { get; }

	/// <summary>Agent-actionable next step surfaced as <c>retry-guidance</c> on the MCP envelope.</summary>
	public string RetryGuidance { get; }
}
