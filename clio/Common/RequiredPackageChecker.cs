using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Clio.Package;
using Clio.Project.NuGet;
using Version = System.Version;

namespace Clio.Common;

/// <summary>
/// Validates declarative package requirements (<see cref="RequiresPackageAttribute"/>) against the
/// packages installed in the target environment.
/// </summary>
/// <remarks>
/// This generalizes the cliogate-specific <see cref="IClioGateway"/> check to arbitrary packages,
/// looked up by name (with a configurable alias map for packages that ship under several names).
/// </remarks>
public interface IRequiredPackageChecker
{
	/// <summary>
	/// Retrieves the installed version of the named package.
	/// </summary>
	/// <param name="packageName">The package name to look up (case-insensitive, alias-aware).</param>
	/// <returns>
	/// The installed <see cref="PackageVersion"/>, or <c>null</c> when the package is not installed.
	/// When several aliases of the package are installed, the lowest version is returned.
	/// </returns>
	PackageVersion GetInstalledVersion(string packageName);

	/// <summary>
	/// Determines whether the named package is installed in the target environment.
	/// </summary>
	/// <param name="packageName">The package name to look up (case-insensitive, alias-aware).</param>
	/// <returns><c>true</c> when the package is installed; otherwise <c>false</c>.</returns>
	bool IsInstalled(string packageName);

	/// <summary>
	/// Determines whether the installed version of the named package is greater than or equal to
	/// the specified version.
	/// </summary>
	/// <param name="packageName">The package name to look up (case-insensitive, alias-aware).</param>
	/// <param name="version">The minimum required version.</param>
	/// <returns>
	/// <c>true</c> when the package is installed and its version is compatible; <c>false</c> when the
	/// package is not installed or its version is lower than required.
	/// </returns>
	/// <exception cref="PackageRequirementException">
	/// Thrown when <paramref name="version"/> is a malformed, unparseable non-empty version string.
	/// </exception>
	bool IsCompatible(string packageName, string version);

	/// <summary>
	/// Validates every <see cref="RequiresPackageAttribute"/> declared on the specified options type.
	/// </summary>
	/// <param name="optionsType">The type whose package requirements must be satisfied.</param>
	/// <exception cref="PackageRequirementException">
	/// Thrown when any declared requirement is not satisfied (missing package or incompatible version).
	/// </exception>
	/// <remarks>
	/// When <paramref name="optionsType"/> declares no <see cref="RequiresPackageAttribute"/> the method
	/// returns immediately without fetching the installed package list, so commands without requirements
	/// incur no cost.
	/// </remarks>
	void EnsureRequirements(Type optionsType);
}

/// <inheritdoc cref="IRequiredPackageChecker"/>
public class RequiredPackageChecker : IRequiredPackageChecker
{

	#region Fields: Private

	// Maps a canonical package name to the additional names the same package can ship under. The
	// lookup, and the comparison against installed package names, are case-insensitive. Extend this
	// map as new multi-name packages are discovered.
	private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> PackageAliases =
		new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) {
			["cliogate"] = ["cliogate_netcore"]
		};

	private readonly IApplicationPackageListProvider _applicationPackageListProvider;

	// Cached for the lifetime of the service instance so multiple requirements on one command cause
	// a single fetch.
	private IReadOnlyList<PackageInfo> _packagesCache;

	#endregion

	#region Constructors: Public

	public RequiredPackageChecker(IApplicationPackageListProvider applicationPackageListProvider) {
		_applicationPackageListProvider = applicationPackageListProvider;
	}

	#endregion

	#region Methods: Private

	private IReadOnlyList<PackageInfo> GetPackages() =>
		_packagesCache ??= _applicationPackageListProvider.GetPackages().ToList();

	private static IReadOnlyList<string> ResolveNames(string packageName) {
		List<string> names = [packageName];
		if (PackageAliases.TryGetValue(packageName, out IReadOnlyList<string> aliases)) {
			names.AddRange(aliases);
		}
		return names;
	}

	#endregion

	#region Methods: Public

	public PackageVersion GetInstalledVersion(string packageName) {
		IReadOnlyList<string> names = ResolveNames(packageName);
		return GetPackages()
			.Where(p => names.Any(n => string.Equals(n, p.Descriptor.Name, StringComparison.OrdinalIgnoreCase)))
			// Fail-closed: when several aliases (e.g. cliogate / cliogate_netcore) are installed, return the
			// LOWEST version so a requirement is only satisfied when the weakest installed alias meets the bar.
			.MinBy(p => p.Version)
			?.Version;
	}

	public bool IsInstalled(string packageName) => GetInstalledVersion(packageName) is not null;

	public bool IsCompatible(string packageName, string version) {
		PackageVersion installedVersion = GetInstalledVersion(packageName);
		if (installedVersion is null) {
			return false;
		}
		// Parse defensively: a malformed non-empty version (e.g. "2.0.x") would make new Version(...)
		// throw FormatException/ArgumentException, which is NOT a PackageRequirementException and would
		// escape both dispatch chokepoints as a raw stack trace. Convert it into the friendly-error path.
		if (!Version.TryParse(version, out Version requiredVersion)) {
			throw new PackageRequirementException(
				$"The [RequiresPackage] requirement for package '{packageName}' has an invalid version '{version}'.");
		}
		return installedVersion >= new PackageVersion(requiredVersion, string.Empty);
	}

	public void EnsureRequirements(Type optionsType) {
		RequiresPackageAttribute[] requirements = (RequiresPackageAttribute[])
			optionsType.GetCustomAttributes(typeof(RequiresPackageAttribute), inherit: true);

		// Zero-cost path: no requirements means we must not touch the package list (no HTTP).
		if (requirements.Length == 0) {
			return;
		}

		foreach (RequiresPackageAttribute requirement in requirements) {
			bool presenceOnly = string.IsNullOrEmpty(requirement.Version);
			if (presenceOnly) {
				if (!IsInstalled(requirement.Name)) {
					throw new PackageRequirementException(
						AppendHint(
							$"To use this command, you need to install the {requirement.Name} package. " +
							"Install the package in the target environment and retry.",
							requirement.Hint));
				}
				continue;
			}

			if (!IsCompatible(requirement.Name, requirement.Version)) {
				throw new PackageRequirementException(
					AppendHint(
						$"To use this command, you need to install the {requirement.Name} package " +
						$"version {requirement.Version} or higher. " +
						"Install or update the package in the target environment and retry.",
						requirement.Hint));
			}
		}
	}

	// Appends the attribute-supplied actionable hint to the base message as its own sentence/line.
	// The checker is package-agnostic: it only reads the free-text hint and never knows the package.
	private static string AppendHint(string message, string hint) =>
		string.IsNullOrEmpty(hint) ? message : $"{message}{Environment.NewLine}{hint}";

	#endregion

}
