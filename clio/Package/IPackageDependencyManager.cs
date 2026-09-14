using System.Collections.Generic;

namespace Clio.Package;

#region Class: PackageDependencySpec

/// <summary>
/// Describes a single package dependency requested for addition.
/// </summary>
/// <param name="Name">Dependency package name (for example <c>CrtLeadOppMgmtApp</c>).</param>
/// <param name="Version">
/// Optional explicit package version. When <see langword="null"/> or empty, the installed version of the
/// dependency package is used.
/// </param>
public sealed record PackageDependencySpec(string Name, string Version = null);

#endregion

#region Interface: IPackageDependencyManager

/// <summary>
/// Adds dependencies to or removes dependencies from a package via the Creatio <c>PackageService.svc</c> endpoint.
/// </summary>
public interface IPackageDependencyManager
{

	#region Methods: Public

	/// <summary>
	/// Adds the requested dependencies to <paramref name="packageName"/> and persists the change.
	/// Adding a dependency that is already present is a no-op (idempotent).
	/// </summary>
	/// <param name="packageName">Target package whose dependency list is extended.</param>
	/// <param name="dependencies">Dependencies to add.</param>
	/// <returns>The resulting dependency package names after the merge.</returns>
	IReadOnlyList<string> AddDependencies(string packageName, IEnumerable<PackageDependencySpec> dependencies);

	/// <summary>
	/// Reads the dependencies <paramref name="packageName"/> currently declares, without changing anything.
	/// Returns the DIRECT dependencies only - what the package descriptor declares, not the transitive
	/// closure.
	/// </summary>
	/// <param name="packageName">Package whose dependency list is read.</param>
	/// <returns>The declared dependency package names.</returns>
	IReadOnlyList<string> GetDependencies(string packageName);

	/// <summary>
	/// Reads the dependencies <paramref name="packageName"/> currently declares with an explicit
	/// per-request timeout, without changing anything.
	/// </summary>
	/// <remarks>
	/// Same contract as <see cref="GetDependencies(string)"/>, which reads with no timeout at all. The bound
	/// exists for callers that run this read inside an already-failing operation - the entity-schema designer
	/// reads it only to build an error message. An environment that accepts the connection and then stops
	/// answering must cost such a caller a bounded wait, not a hung tool call; in a long-lived MCP server an
	/// unbounded read there holds the tenant open with no way back.
	/// </remarks>
	/// <param name="packageName">Package whose dependency list is read.</param>
	/// <param name="requestTimeoutMs">
	/// Per-request timeout in milliseconds, applied to both the package lookup and the properties read, or
	/// <see cref="System.Threading.Timeout.Infinite"/> for no bound.
	/// </param>
	/// <returns>The declared dependency package names.</returns>
	IReadOnlyList<string> GetDependencies(string packageName, int requestTimeoutMs);

	/// <summary>
	/// Removes the requested dependencies from <paramref name="packageName"/> and persists the change.
	/// Removing a dependency that is not present is a no-op (idempotent). Dependencies are matched by name
	/// (case-insensitive); the version is ignored.
	/// </summary>
	/// <param name="packageName">Target package whose dependency list is trimmed.</param>
	/// <param name="dependencyNames">Names of the dependency packages to remove.</param>
	/// <returns>The resulting dependency package names after the removal.</returns>
	IReadOnlyList<string> RemoveDependencies(string packageName, IEnumerable<string> dependencyNames);

	#endregion

}

#endregion
