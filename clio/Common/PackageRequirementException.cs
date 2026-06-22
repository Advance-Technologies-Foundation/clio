using System;

namespace Clio.Common;

/// <summary>
/// Thrown when a command declares a package requirement (via <see cref="RequiresPackageAttribute"/>)
/// that is not satisfied by the target environment — the package is missing or installed at a
/// version lower than required.
/// </summary>
/// <remarks>
/// The message is intended to be shown directly to the user, so it must be friendly and actionable
/// (naming the package and the required version) rather than a stack trace. Callers and tests can
/// catch this dedicated type to distinguish unmet package requirements from other failures.
/// </remarks>
public sealed class PackageRequirementException : Exception
{
	/// <summary>
	/// Initializes a new instance of the <see cref="PackageRequirementException"/> class.
	/// </summary>
	/// <param name="message">The user-friendly, actionable error message.</param>
	public PackageRequirementException(string message)
		: base(message) {
	}
}
