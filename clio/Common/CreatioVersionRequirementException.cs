using System;

namespace Clio.Common;

/// <summary>
/// Thrown when a command declares a Creatio platform version requirement (via
/// <see cref="RequiresCreatioVersionAttribute"/>) that the target environment does not satisfy — the
/// environment runs an older core version, or the version could not be determined.
/// </summary>
/// <remarks>
/// The message is intended to be shown directly to the user, so it must be friendly and actionable
/// (naming the required and actual versions, and how to proceed) rather than a stack trace.
/// In addition, the exception carries a stable, machine-readable <see cref="ErrorCode"/> so agents
/// and automation can branch on the failure class without parsing the human message. Callers and
/// tests can catch this dedicated type to distinguish unmet version requirements from other failures.
/// </remarks>
public sealed class CreatioVersionRequirementException : Exception
{
	/// <summary>
	/// Stable error code reported when the environment's core version is lower than the minimum
	/// required by the command.
	/// </summary>
	public const string VersionTooOldCode = "version-too-old";

	/// <summary>
	/// Stable error code reported when the environment's core version could not be determined and the
	/// gate fails closed (denies execution) rather than running against an unknown platform.
	/// </summary>
	public const string VersionUndeterminableCode = "version-undeterminable";

	/// <summary>
	/// Initializes a new instance of the <see cref="CreatioVersionRequirementException"/> class.
	/// </summary>
	/// <param name="message">The user-friendly, actionable error message.</param>
	/// <param name="errorCode">
	/// The stable, machine-readable error code — one of <see cref="VersionTooOldCode"/> or
	/// <see cref="VersionUndeterminableCode"/>.
	/// </param>
	public CreatioVersionRequirementException(string message, string errorCode)
		: base(message) {
		ErrorCode = errorCode;
	}

	/// <summary>
	/// Gets the stable, machine-readable error code identifying the failure class
	/// (see <see cref="VersionTooOldCode"/> and <see cref="VersionUndeterminableCode"/>).
	/// </summary>
	public string ErrorCode { get; }
}
