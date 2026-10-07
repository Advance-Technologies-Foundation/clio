using System;
using System.IO;
using System.Linq;
using Clio.Common;
using Clio.UserEnvironment;

namespace Clio.Package;

/// <summary>
/// Outcome of locating a package folder on the local file system of a Creatio environment.
/// </summary>
/// <param name="PackageFolderPath">Absolute path of <c>Pkg/&lt;package&gt;</c>; <c>null</c> when it was not found.</param>
/// <param name="Problem">Why the folder could not be located; <c>null</c> when it was found.</param>
public sealed record EnvironmentPackageFolderResolution(string PackageFolderPath, string Problem) {

	/// <summary>Whether the package folder was found.</summary>
	public bool IsResolved => !string.IsNullOrWhiteSpace(PackageFolderPath);

	/// <summary>Creates a resolution that found the package folder.</summary>
	/// <param name="packageFolderPath">Absolute path of the package folder.</param>
	/// <returns>A resolved outcome.</returns>
	public static EnvironmentPackageFolderResolution Found(string packageFolderPath) => new(packageFolderPath, null);

	/// <summary>Creates a resolution that did not find the package folder.</summary>
	/// <param name="problem">Why the folder could not be located, naming what the caller can do about it.</param>
	/// <returns>An unresolved outcome.</returns>
	public static EnvironmentPackageFolderResolution NotFound(string problem) => new(null, problem);
}

/// <summary>
/// Locates the folder that holds a package on the local file system of a Creatio environment: the
/// <c>Terrasoft.Configuration/Pkg/&lt;package&gt;</c> folder the site reads in file system mode. When the package
/// was linked with <c>link-from-repository</c>, that folder is a symbolic link into the repository, so a change
/// made through the returned path lands in the repository working tree.
/// </summary>
public interface IEnvironmentPackageFolderResolver {

	/// <summary>
	/// Resolves <c>Pkg/&lt;package&gt;</c> for an environment.
	/// </summary>
	/// <param name="environmentName">
	/// Registered clio environment name. Its <c>EnvironmentPath</c> (the site root folder) is the base of the
	/// lookup; may be <c>null</c> when <paramref name="environmentPathOverride"/> is supplied.
	/// </param>
	/// <param name="packageName">Package name; must be a single folder name.</param>
	/// <param name="environmentPathOverride">
	/// Site root folder supplied by the caller (for example <c>--ep</c>); takes precedence over the registered
	/// <c>EnvironmentPath</c>.
	/// </param>
	/// <returns>The package folder, or the reason it could not be located. A missing folder is not an exception.</returns>
	EnvironmentPackageFolderResolution Resolve(string environmentName, string packageName,
		string environmentPathOverride = null);
}

/// <inheritdoc cref="IEnvironmentPackageFolderResolver"/>
public sealed class EnvironmentPackageFolderResolver : IEnvironmentPackageFolderResolver {

	private const string ConfigurationFolderName = "Terrasoft.Configuration";
	private const string PackagesFolderName = "Pkg";
	private const string WebAppFolderName = "Terrasoft.WebApp";

	private readonly ISettingsRepository _settingsRepository;
	private readonly IFileSystem _fileSystem;

	/// <summary>
	/// Initializes a new instance of the <see cref="EnvironmentPackageFolderResolver"/> class.
	/// </summary>
	/// <param name="settingsRepository">Source of the registered environment's <c>EnvironmentPath</c>.</param>
	/// <param name="fileSystem">File system used to probe the candidate folders.</param>
	public EnvironmentPackageFolderResolver(ISettingsRepository settingsRepository, IFileSystem fileSystem) {
		_settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
		_fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
	}

	/// <inheritdoc />
	public EnvironmentPackageFolderResolution Resolve(string environmentName, string packageName,
		string environmentPathOverride = null) {
		if (!PackageItemFolders.IsSafeFolderName(packageName)) {
			return EnvironmentPackageFolderResolution.NotFound(
				$"package name '{packageName}' is not a valid folder name");
		}
		EnvironmentSettings registered = string.IsNullOrWhiteSpace(environmentName)
			? null
			: _settingsRepository.FindEnvironment(environmentName);
		string environmentPath = string.IsNullOrWhiteSpace(environmentPathOverride)
			? registered?.EnvironmentPath
			: environmentPathOverride.Trim();
		if (string.IsNullOrWhiteSpace(environmentPath)) {
			return EnvironmentPackageFolderResolution.NotFound(string.IsNullOrWhiteSpace(environmentName)
				? "the call names no registered environment, so no site folder is known; pass --ep <site folder>"
				: $"no site folder (EnvironmentPath) is registered for environment '{environmentName}'; pass --ep "
					+ "<site folder>");
		}
		string packagesRoot = GetPackagesRootCandidates(environmentPath, registered?.IsNetCore ?? false)
			.FirstOrDefault(_fileSystem.ExistsDirectory);
		if (packagesRoot is null) {
			return EnvironmentPackageFolderResolution.NotFound(
				$"no {ConfigurationFolderName}{Path.DirectorySeparatorChar}{PackagesFolderName} folder was found under "
				+ $"'{environmentPath}' on this machine");
		}
		string packageFolderPath = _fileSystem.Combine(packagesRoot, packageName);
		return _fileSystem.ExistsDirectory(packageFolderPath)
			? EnvironmentPackageFolderResolution.Found(packageFolderPath)
			: EnvironmentPackageFolderResolution.NotFound($"package folder '{packageFolderPath}' does not exist");
	}

	private string[] GetPackagesRootCandidates(string environmentPath, bool isNetCore) {
		string netCoreRoot = _fileSystem.Combine(environmentPath, ConfigurationFolderName, PackagesFolderName);
		string netFrameworkRoot = _fileSystem.Combine(environmentPath, WebAppFolderName, ConfigurationFolderName,
			PackagesFolderName);
		return isNetCore ? [netCoreRoot, netFrameworkRoot] : [netFrameworkRoot, netCoreRoot];
	}
}
