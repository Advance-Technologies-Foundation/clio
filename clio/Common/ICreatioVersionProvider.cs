using System;

namespace Clio.Common;

/// <summary>
/// Resolves the Creatio platform (core) version of the target environment so declarative
/// <see cref="RequiresCreatioVersionAttribute"/> requirements can be checked against it.
/// </summary>
/// <remarks>
/// Implementations talk to the environment (for example via <c>get-info</c> / the platform version
/// resolver) and are expected to cache the resolution for the lifetime of the invocation. The provider
/// is the single seam through which <see cref="ICreatioVersionChecker"/> learns the environment's
/// version (and whether it could be reached at all), which keeps the checker free of any I/O concern
/// and trivially testable.
/// </remarks>
public interface ICreatioVersionProvider
{
	/// <summary>
	/// Resolves the Creatio core version of the target environment, distinguishing the three failure
	/// classes the version gate must report.
	/// </summary>
	/// <returns>
	/// A <see cref="CreatioVersionResolution"/> whose status is
	/// <see cref="CreatioVersionResolutionStatus.Resolved"/> (carrying the parsed core
	/// <see cref="Version"/>) when a source produced a parseable version;
	/// <see cref="CreatioVersionResolutionStatus.ReachableWithoutVersion"/> when a source responded but
	/// no usable version was found; or <see cref="CreatioVersionResolutionStatus.ProbeFailed"/> when no
	/// source responded at all (unreachable / access denied / probe failed). A development build is
	/// reported as a resolved <c>0.0.0.0</c> / <c>0.0.0</c>.
	/// </returns>
	CreatioVersionResolution Resolve();
}
