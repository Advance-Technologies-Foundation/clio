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
/// Adds dependencies to a package via the Creatio <c>PackageService.svc</c> endpoint.
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

	#endregion

}

#endregion
