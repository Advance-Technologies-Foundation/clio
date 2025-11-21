using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Clio.Common;
using Clio.Package;
using Clio.UserEnvironment;
using CommandLine;
using FluentValidation;
using FluentValidation.Results;
using static Clio.Common.OperationSystem;

namespace Clio.Command;

[Verb("link-package-store", Aliases = ["lps"], HelpText = "Link packages from PackageStore to environment packages.")]
public class LinkPackageStoreOptions : EnvironmentOptions {

	#region Properties: Public

	[Option("packageStorePath", Required = true,
		HelpText = "Path to PackageStore folder with structure: {Package_name}/{branches}/{version}/{content}")]
	public string PackageStorePath { get; set; }

	[Option("envPkgPath", Required = false,
		HelpText = "Path to environment package folder ({LOCAL_CREATIO_PATH}Terrasoft.WebApp\\Terrasoft.Configuration\\Pkg)")]
	public string EnvPkgPath { get; set; }

	#endregion

}

public class LinkPackageStoreOptionsValidator : AbstractValidator<LinkPackageStoreOptions> {

	#region Constructors: Public

	public LinkPackageStoreOptionsValidator() {
		RuleFor(o => o.PackageStorePath)
			.NotEmpty()
			.WithMessage("PackageStorePath is required");

		RuleFor(o => string.IsNullOrWhiteSpace(o.EnvPkgPath) && string.IsNullOrWhiteSpace(o.Environment))
			.Cascade(CascadeMode.Stop)
			.Custom((value, context) => {
				if (value) {
					context.AddFailure(new ValidationFailure {
						ErrorCode = "ArgumentParse.Error",
						ErrorMessage = "Either path to environment packages folder or environment name must be provided",
						Severity = Severity.Error
					});
				}
			});
	}

	#endregion

}

public class LinkPackageStoreCommand : Command<LinkPackageStoreOptions> {

	#region Fields: Private

	private readonly ILogger _logger;
	private readonly IFileSystem _fileSystem;
	private readonly IJsonConverter _jsonConverter;
	private readonly IValidator<LinkPackageStoreOptions> _validator;

	#endregion

	#region Constructors: Public

	public LinkPackageStoreCommand(ILogger logger, IFileSystem fileSystem,
		IJsonConverter jsonConverter, IValidator<LinkPackageStoreOptions> validator) {
		_logger = logger;
		_fileSystem = fileSystem;
		_jsonConverter = jsonConverter;
		_validator = validator;
	}

	#endregion

	#region Methods: Private

	private int PrintErrorsAndExit(IEnumerable<ValidationFailure> errors) {
		_logger.PrintValidationFailureErrors(errors);
		return 1;
	}

	/// <summary>
	/// Gets the version of a package from its descriptor.json file.
	/// </summary>
	/// <param name="packagePath">Path to the package directory</param>
	/// <returns>Package version or null if descriptor.json is not found or invalid</returns>
	private string GetPackageVersion(string packagePath) {
		try {
			string descriptorPath = Path.Combine(packagePath, "descriptor.json");
			if (!_fileSystem.ExistsFile(descriptorPath)) {
				_logger.WriteWarning($"descriptor.json not found in {packagePath}");
				return null;
			}

			var dto = _jsonConverter.DeserializeObjectFromFile<PackageDescriptorDto>(descriptorPath);
			return dto?.Descriptor?.PackageVersion;
		}
		catch (Exception ex) {
			_logger.WriteWarning($"Failed to read descriptor.json from {packagePath}: {ex.Message}");
			return null;
		}
	}

	/// <summary>
	/// Reads all packages from PackageStore.
	/// Expected structure: {PackageName}/{branches}/{version}/{content}
	/// </summary>
	/// <param name="packageStorePath">Path to PackageStore</param>
	/// <returns>Dictionary mapping package name to list of versions (across all branches)</returns>
	private Dictionary<string, List<string>> ReadPackageStorePackages(string packageStorePath) {
		var packageVersions = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

		try {
			if (!_fileSystem.ExistsDirectory(packageStorePath)) {
				_logger.WriteError($"PackageStore path does not exist: {packageStorePath}");
				return packageVersions;
			}

			// Level 1: Package names
			var packageDirs = _fileSystem.GetDirectories(packageStorePath);
			foreach (var packageDir in packageDirs) {
				var packageName = Path.GetFileName(packageDir);
				
				// Level 2: Branches (main, develop, feature, etc.)
				var branchDirs = _fileSystem.GetDirectories(packageDir);
				var allVersions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				
				foreach (var branchDir in branchDirs) {
					// Level 3: Versions
					var versionDirs = _fileSystem.GetDirectories(branchDir);
					foreach (var versionDir in versionDirs) {
						var version = Path.GetFileName(versionDir);
						allVersions.Add(version);
					}
				}
				
				packageVersions[packageName] = allVersions.ToList();
			}
		}
		catch (Exception ex) {
			_logger.WriteError($"Error reading PackageStore: {ex.Message}");
		}

		return packageVersions;
	}

	/// <summary>
	/// Reads all packages from environment packages folder.
	/// </summary>
	/// <param name="envPkgPath">Path to environment packages folder</param>
	/// <returns>Dictionary mapping package name to its version</returns>
	private Dictionary<string, string> ReadEnvironmentPackages(string envPkgPath) {
		var envPackages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		try {
			if (!_fileSystem.ExistsDirectory(envPkgPath)) {
				_logger.WriteError($"Environment packages path does not exist: {envPkgPath}");
				return envPackages;
			}

			var packageDirs = _fileSystem.GetDirectories(envPkgPath);
			foreach (var packageDir in packageDirs) {
				var packageName = Path.GetFileName(packageDir);
				var version = GetPackageVersion(packageDir);
				if (!string.IsNullOrEmpty(version)) {
					envPackages[packageName] = version;
				}
			}
		}
		catch (Exception ex) {
			_logger.WriteError($"Error reading environment packages: {ex.Message}");
		}

		return envPackages;
	}

	/// <summary>
	/// Finds the path to a specific package version in PackageStore.
	/// In PackageStore: {PackageName}/{branches}/{version}/{content}
	/// Returns the first matching version found in any branch.
	/// </summary>
	private string FindPackageVersionPath(string packageStorePath, string packageName, string packageVersion) {
		try {
			var packageDir = Path.Combine(packageStorePath, packageName);
			if (!_fileSystem.ExistsDirectory(packageDir)) {
				return null;
			}

			// Search through all branches for the target version
			var branchDirs = _fileSystem.GetDirectories(packageDir);
			foreach (var branchDir in branchDirs) {
				var versionPath = Path.Combine(branchDir, packageVersion);
				if (_fileSystem.ExistsDirectory(versionPath)) {
					return versionPath;
				}
			}

			return null;
		}
		catch {
			return null;
		}
	}

	/// <summary>
	/// Creates a symbolic link between source and destination.
	/// Removes existing link/directory at destination first.
	/// </summary>
	private bool CreateSymbolicLink(string sourcePath, string destinationPath) {
		try {
			// Remove existing link/directory at destination
			if (_fileSystem.ExistsDirectory(destinationPath)) {
				try {
					_fileSystem.DeleteDirectory(destinationPath, true);
				}
				catch (Exception ex) {
					_logger.WriteWarning($"Failed to remove existing directory {destinationPath}: {ex.Message}");
				}
			}

			// Ensure parent directory exists
			var parentDir = Path.GetDirectoryName(destinationPath);
			if (!_fileSystem.ExistsDirectory(parentDir)) {
				_fileSystem.CreateDirectory(parentDir);
			}

			// Create the symbolic link
			_fileSystem.CreateSymLink(sourcePath, destinationPath);
			_logger.WriteInfo($"Created link: {destinationPath} -> {sourcePath}");
			return true;
		}
		catch (Exception ex) {
			_logger.WriteError($"Failed to create symbolic link from {sourcePath} to {destinationPath}: {ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// Handles linking packages when environment path is provided.
	/// </summary>
	private int HandleLinkWithDirPath(string envPkgPath, string packageStorePath) {
		_logger.WriteInfo($"Reading environment packages from {envPkgPath}");
		_logger.WriteInfo($"Reading PackageStore from {packageStorePath}");

		var storePackages = ReadPackageStorePackages(packageStorePath);
		var envPackages = ReadEnvironmentPackages(envPkgPath);

		if (storePackages.Count == 0) {
			_logger.WriteError("No packages found in PackageStore");
			return 1;
		}

		if (envPackages.Count == 0) {
			_logger.WriteWarning("No packages found in environment");
			return 0;
		}

		int linkedCount = 0;
		int failedCount = 0;

		foreach (var envPkg in envPackages) {
			var packageName = envPkg.Key;
			var packageVersion = envPkg.Value;

			if (!storePackages.TryGetValue(packageName, out var storeVersions)) {
				_logger.WriteWarning($"Package '{packageName}' not found in PackageStore - skipping");
				continue;
			}

			if (!storeVersions.Contains(packageVersion)) {
				_logger.WriteWarning(
					$"Package '{packageName}' version '{packageVersion}' not found in PackageStore - skipping");
				continue;
			}

			// Find the actual version path (searches through branches)
			string sourceContentPath = FindPackageVersionPath(packageStorePath, packageName, packageVersion);
			if (string.IsNullOrEmpty(sourceContentPath)) {
				_logger.WriteWarning(
					$"Could not locate path for package '{packageName}' version '{packageVersion}' in PackageStore - skipping");
				failedCount++;
				continue;
			}

			string destinationPackagePath = Path.Combine(envPkgPath, packageName);

			_logger.WriteInfo(
				$"Linking package '{packageName}' (v{packageVersion}) from store to environment");

			if (CreateSymbolicLink(sourceContentPath, destinationPackagePath)) {
				linkedCount++;
			}
			else {
				failedCount++;
			}
		}

		_logger.WriteInfo($"Linking completed: {linkedCount} packages linked, {failedCount} failed");

		return failedCount > 0 ? 1 : 0;
	}

	#endregion

	#region Methods: Public

	public override int Execute(LinkPackageStoreOptions options) {
		ValidationResult validationResult = _validator.Validate(options);
		if (!validationResult.IsValid) {
			return PrintErrorsAndExit(validationResult.Errors);
		}

		try {
			string envPkgPath = options.EnvPkgPath;

			// If environment name is provided instead of path, resolve it
			if (string.IsNullOrWhiteSpace(envPkgPath) && !string.IsNullOrWhiteSpace(options.Environment)) {
				_ = Uri.TryCreate(options.Environment, UriKind.Absolute, out Uri pathUri);
				if (pathUri?.IsFile == true) {
					envPkgPath = options.Environment;
				}
				else {
					_logger.WriteError(
						"Environment name resolution is not supported for this command. Please use --envPkgPath with direct file path instead.");
					return 1;
				}
			}

			if (string.IsNullOrWhiteSpace(envPkgPath)) {
				_logger.WriteError("Environment packages path is required");
				return 1;
			}

			return HandleLinkWithDirPath(envPkgPath, options.PackageStorePath);
		}
		catch (Exception ex) {
			_logger.WriteError($"Error during linking: {ex.Message}");
			return 1;
		}
	}

	#endregion

}
