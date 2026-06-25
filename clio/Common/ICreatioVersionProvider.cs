using System;

namespace Clio.Common;

/// <summary>
/// Resolves the Creatio platform (core) version of the target environment so declarative
/// <see cref="RequiresCreatioVersionAttribute"/> requirements can be checked against it.
/// </summary>
/// <remarks>
/// Implementations talk to the environment (for example via <c>get-info</c> / the platform version
/// resolver) and are expected to cache the result for the lifetime of the invocation. The provider
/// is the single seam through which <see cref="ICreatioVersionChecker"/> learns the environment's
/// version, which keeps the checker free of any I/O concern and trivially testable.
/// </remarks>
public interface ICreatioVersionProvider
{
	/// <summary>
	/// Gets the Creatio core version of the target environment.
	/// </summary>
	/// <returns>
	/// The environment's core <see cref="Version"/>; <c>null</c> when the version cannot be determined
	/// (for example the environment is unreachable or returns no recognizable version). A development
	/// build is reported as <c>0.0.0.0</c>.
	/// </returns>
	Version GetCoreVersion();
}
