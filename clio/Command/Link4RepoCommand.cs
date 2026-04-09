using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Clio.Common;
using Clio.Package;
using Clio.Requests;
using Clio.UserEnvironment;
using CommandLine;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using static Clio.Common.OperationSystem;

namespace Clio.Command;

/// <summary>
/// Options for the <c>link-from-repository</c> command.
/// </summary>
[Verb("link-from-repository", Aliases = ["l4r", "link4repo"], HelpText = "Link repository package(s) to environment.")]
public class Link4RepoOptions : EnvironmentOptions {

	#region Properties: Public

	/// <summary>
	/// Gets or sets the direct path to the environment package folder.
	/// </summary>
	[Option("envPkgPath", Required = false,
		HelpText
			= @"Path to environment package folder ({LOCAL_CREATIO_PATH}Terrasoft.WebApp\Terrasoft.Configuration\Pkg)",
		Default = null)]
	public string EnvPkgPath { get; set; }

	/// <summary>
	/// Gets or sets the package selector.
	/// </summary>
	[Option("packages", Required = false, HelpText = "Package(s)", Default = null)]
	public string Packages { get; set; }

	/// <summary>
	/// Gets or sets the repository path that contains workspace packages.
	/// </summary>
	[Option("repoPath", Required = true,
		HelpText = "Path to package repository folder", Default = null)]
	public string RepoPath { get; set; }

	/// <summary>
	/// Gets or sets whether to automatically determine packages by querying unlocked packages from the Creatio site.
	/// </summary>
	[Option("unlocked", Required = false, Default = false,
		HelpText = "Query the Creatio site for unlocked packages and link only those. Requires -e or -u for API connection.")]
	public bool Unlocked { get; set; }

	internal override bool RequiredEnvironment => false;

	#endregion

}

/// <summary>
/// Validates options for <see cref="Link4RepoOptions"/>.
/// </summary>
public class Link4RepoOptionsValidator : AbstractValidator<Link4RepoOptions> {

	#region Constructors: Public
	
	/// <summary>
	/// Initializes validation rules for <see cref="Link4RepoOptions"/>.
	/// </summary>
	public Link4RepoOptionsValidator(){
		RuleFor(o => string.IsNullOrWhiteSpace(o.EnvPkgPath) && string.IsNullOrWhiteSpace(o.Environment))
			.Cascade(CascadeMode.Stop)
			.Custom((value, context) => {
				if (value) {
					context.AddFailure(new ValidationFailure {
						ErrorCode = "ArgumentParse.Error",
						ErrorMessage = "Either path to creatio directory or environment name must be provided",
						Severity = Severity.Error
					});
				}
			});

		RuleFor(o => o)
			.Custom((options, context) => {
				if (options.Unlocked && string.IsNullOrWhiteSpace(options.Environment)
					&& string.IsNullOrWhiteSpace(options.Uri)) {
					context.AddFailure(new ValidationFailure {
						ErrorCode = "ArgumentParse.Error",
						ErrorMessage =
							"When --unlocked is set, environment name (-e) or URI (-u) must be provided for API connection",
						Severity = Severity.Error
					});
				}
				if (!options.Unlocked && string.IsNullOrWhiteSpace(options.Packages)) {
					context.AddFailure(new ValidationFailure {
						ErrorCode = "ArgumentParse.Error",
						ErrorMessage =
							"Either --packages or --unlocked must be specified",
						Severity = Severity.Error
					});
				}
			});
	}

	#endregion

}

/// <summary>
/// Links workspace packages into a local Creatio environment package directory.
/// </summary>
public class Link4RepoCommand : Command<Link4RepoOptions> {

	#region Fields: Private

	private readonly ILogger _logger;
	private readonly IMediator _mediator;
	private readonly ISettingsRepository _settingsRepository;
	private readonly IFileSystem _fileSystem;
	private readonly RfsEnvironment _rfsEnvironment;
	private readonly IValidator<Link4RepoOptions> _validator;
	private readonly IApplicationPackageListProvider _applicationPackageListProvider;
	private readonly IJsonConverter _jsonConverter;
	private readonly ISysSettingsManager _sysSettingsManager;
	private readonly IPackageLockManager _packageLockManager;
	private readonly IFileDesignModePackages _fileDesignModePackages;

	#endregion

	#region Constructors: Public

	/// <summary>
	/// Initializes a new instance of the <see cref="Link4RepoCommand"/> class.
	/// </summary>
	public Link4RepoCommand(ILogger logger, IMediator mediator, ISettingsRepository settingsRepository,
		IFileSystem fileSystem, RfsEnvironment rfsEnvironment, IValidator<Link4RepoOptions> validator,
		IApplicationPackageListProvider applicationPackageListProvider, IJsonConverter jsonConverter,
		ISysSettingsManager sysSettingsManager, IPackageLockManager packageLockManager,
		IFileDesignModePackages fileDesignModePackages){
		_logger = logger;
		_mediator = mediator;
		_settingsRepository = settingsRepository;
		_fileSystem = fileSystem;
		_rfsEnvironment = rfsEnvironment;
		_validator = validator;
		_applicationPackageListProvider = applicationPackageListProvider;
		_jsonConverter = jsonConverter;
		_sysSettingsManager = sysSettingsManager;
		_packageLockManager = packageLockManager;
		_fileDesignModePackages = fileDesignModePackages;
	}

	#endregion

	#region Properties: Private

	private IEnumerable<IISScannerHandler.RegisteredSite> AllSites { get; set; }

	/// <summary>
	/// Gets the action that stores all discovered registered IIS sites.
	/// </summary>
	private Action<IEnumerable<IISScannerHandler.RegisteredSite>> OnAllSitesRequestCompleted =>
		sites => { AllSites = sites; };

	#endregion

	#region Methods: Private

	private void ExecuteMediatorRequest(Action<IEnumerable<IISScannerHandler.RegisteredSite>> callback){
		AllRegisteredSitesRequest request = new() {
			Callback = callback
		};

		Task.Run(async () => { await _mediator.Send(request); })
			.ConfigureAwait(false)
			.GetAwaiter()
			.GetResult();
	}

	private IEnumerable<string> GetEnvironmentPackagePathCandidates(EnvironmentSettings environment) {
		if (environment is null || string.IsNullOrWhiteSpace(environment.EnvironmentPath)) {
			return [];
		}

		string environmentPath = environment.EnvironmentPath;
		return environment.IsNetCore
			? [
				_fileSystem.Combine(environmentPath, "Terrasoft.Configuration", "Pkg"),
				_fileSystem.Combine(environmentPath, "Terrasoft.WebApp", "Terrasoft.Configuration", "Pkg")
			]
			: [
				_fileSystem.Combine(environmentPath, "Terrasoft.WebApp", "Terrasoft.Configuration", "Pkg"),
				_fileSystem.Combine(environmentPath, "Terrasoft.Configuration", "Pkg")
			];
	}

	internal static bool TryResolveDirectoryPath(string value, IFileSystem fileSystem, out string resolvedPath) {
		resolvedPath = null;
		if (string.IsNullOrWhiteSpace(value)) {
			return false;
		}

		if (Uri.TryCreate(value, UriKind.Absolute, out Uri pathUri) && pathUri.IsFile) {
			resolvedPath = pathUri.LocalPath;
			return true;
		}

		bool looksLikePath = fileSystem.IsPathRooted(value)
			|| value.StartsWith(".", StringComparison.Ordinal)
			|| value.Contains('/')
			|| value.Contains('\\')
			|| fileSystem.ExistsDirectory(value);

		if (!looksLikePath) {
			return false;
		}

		resolvedPath = fileSystem.GetFullPath(value);
		return true;
	}

	/// <summary>
	/// Checks whether a package in the environment Pkg folder is incomplete
	/// (missing entirely or lacks descriptor.json, meaning it hasn't been synced from DB).
	/// </summary>
	private bool IsPackageIncomplete(string envPkgPath, string packageName) {
		string packageDir = _fileSystem.Combine(envPkgPath, packageName);
		if (!_fileSystem.ExistsDirectory(packageDir)) {
			return true;
		}
		string descriptorPath = _fileSystem.Combine(packageDir, "descriptor.json");
		return !_fileSystem.ExistsFile(descriptorPath);
	}

	/// <summary>
	/// Reads the Maintainer field from a package descriptor.json in the repository.
	/// </summary>
	private string ReadRepoPackageMaintainer(string repoPath, string packageName) {
		string packagesSubDir = _fileSystem.Combine(repoPath, "packages");
		string effectiveRepoPath = _fileSystem.ExistsDirectory(packagesSubDir) ? packagesSubDir : repoPath;
		string descriptorPath = _fileSystem.Combine(effectiveRepoPath, packageName, "descriptor.json");
		if (!_fileSystem.ExistsFile(descriptorPath)) {
			return null;
		}
		PackageDescriptorDto dto = _jsonConverter.DeserializeObjectFromFile<PackageDescriptorDto>(descriptorPath);
		return dto?.Descriptor?.Maintainer;
	}

	/// <summary>
	/// Prepares packages for linking: verifies maintainer, unlocks packages, syncs to file system.
	/// Called when packages in the Pkg folder are missing or incomplete.
	/// </summary>
	internal int PreparePackagesForLinking(string envPkgPath, string repoPath, IReadOnlyList<string> packageNames) {
		// 1. Read Maintainer from each package descriptor in repo
		Dictionary<string, string> packageMaintainers = new();
		foreach (string name in packageNames) {
			string maintainer = ReadRepoPackageMaintainer(repoPath, name);
			if (string.IsNullOrWhiteSpace(maintainer)) {
				_logger.WriteWarning($"Could not read Maintainer from descriptor.json for package '{name}' in repo — skipping preparation");
				continue;
			}
			packageMaintainers[name] = maintainer;
		}

		if (packageMaintainers.Count == 0) {
			_logger.WriteWarning("No package descriptors with Maintainer found in repository — skipping preparation");
			return 0;
		}

		// 2. Verify all maintainers are the same
		HashSet<string> distinctMaintainers = new(packageMaintainers.Values, StringComparer.OrdinalIgnoreCase);
		if (distinctMaintainers.Count > 1) {
			_logger.WriteError(
				$"Packages have different Maintainer values: {string.Join(", ", packageMaintainers.Select(kv => $"'{kv.Key}'='{kv.Value}'"))}. " +
				"All packages must have the same Maintainer.");
			return 1;
		}

		string requiredMaintainer = distinctMaintainers.First();
		_logger.WriteInfo($"Package Maintainer: '{requiredMaintainer}'");

		// 3. Check current SysSetting Maintainer on the site
		string currentMaintainer = _sysSettingsManager.GetSysSettingValueByCode("Maintainer");
		if (!string.Equals(currentMaintainer, requiredMaintainer, StringComparison.OrdinalIgnoreCase)) {
			_logger.WriteInfo($"Updating SysSetting 'Maintainer' from '{currentMaintainer}' to '{requiredMaintainer}'");
			bool updated = _sysSettingsManager.UpdateSysSetting("Maintainer", requiredMaintainer);
			if (!updated) {
				_logger.WriteError("Failed to update Maintainer sys setting.");
				return 1;
			}
		}

		// 4. Unlock the packages
		_logger.WriteInfo($"Unlocking packages: {string.Join(", ", packageNames)}");
		_packageLockManager.Unlock(packageNames);

		// 5. Sync packages to file system (2fs)
		_logger.WriteInfo("Loading packages to file system (2fs)...");
		_fileDesignModePackages.LoadPackagesToFileSystem();
		_logger.WriteInfo("Packages synced to file system successfully.");

		return 0;
	}

	/// <summary>
	/// Detects whether a repository uses a versioned PackageStore structure
	/// (PackageName/branch/version/) or a flat structure (PackageName/descriptor.json).
	/// </summary>
	internal bool IsVersionedRepo(string repoPath) {
		string[] packageDirs = _fileSystem.GetDirectories(repoPath);
		if (packageDirs.Length == 0) {
			return false;
		}

		string firstPkgDir = packageDirs[0];
		string descriptorPath = _fileSystem.Combine(firstPkgDir, "descriptor.json");
		if (_fileSystem.ExistsFile(descriptorPath)) {
			return false;
		}

		string[] subDirs = _fileSystem.GetDirectories(firstPkgDir);
		return subDirs.Length > 0;
	}

	/// <summary>
	/// Finds the path to a specific package version in a versioned PackageStore repo.
	/// Structure: repoPath/{PackageName}/{branch}/{version}/
	/// Returns the first matching version found in any branch.
	/// </summary>
	private string FindPackageVersionPath(string repoPath, string packageName, string packageVersion) {
		string packageDir = _fileSystem.Combine(repoPath, packageName);
		if (!_fileSystem.ExistsDirectory(packageDir)) {
			return null;
		}

		string[] branchDirs = _fileSystem.GetDirectories(packageDir);
		foreach (string branchDir in branchDirs) {
			string versionPath = _fileSystem.Combine(branchDir, packageVersion);
			if (_fileSystem.ExistsDirectory(versionPath)) {
				return versionPath;
			}
		}

		return null;
	}

	/// <summary>
	/// Handles linking when --unlocked is set: queries the Creatio API for unlocked packages,
	/// detects repo structure, and links matching packages.
	/// </summary>
	private int HandleUnlockedLinking(Link4RepoOptions options, string envPkgPath) {
		_logger.WriteInfo("Querying Creatio site for unlocked packages...");
		IEnumerable<PackageInfo> unlockedPackages =
			_applicationPackageListProvider.GetPackages("{\"isCustomer\": true}");
		List<PackageInfo> unlockedList = unlockedPackages.ToList();

		if (unlockedList.Count == 0) {
			_logger.WriteInfo("No unlocked packages found on the site.");
			return 0;
		}

		_logger.WriteInfo($"Found {unlockedList.Count} unlocked package(s): " +
			string.Join(", ", unlockedList.Select(p => p.Descriptor.Name)));

		string repoPath = options.RepoPath;
		if (!_fileSystem.ExistsDirectory(repoPath)) {
			_logger.WriteError($"Repository path does not exist: {repoPath}");
			return 1;
		}

		bool isVersioned = IsVersionedRepo(repoPath);
		if (isVersioned) {
			return HandleUnlockedVersionedRepo(envPkgPath, repoPath, unlockedList);
		}

		return HandleUnlockedFlatRepo(envPkgPath, repoPath, unlockedList);
	}

	/// <summary>
	/// Links unlocked packages from a flat repo structure (repo/PackageName/).
	/// </summary>
	private int HandleUnlockedFlatRepo(string envPkgPath, string repoPath,
		List<PackageInfo> unlockedPackages) {
		string packagesSubDir = _fileSystem.Combine(repoPath, "packages");
		string effectiveRepoPath = _fileSystem.ExistsDirectory(packagesSubDir) ? packagesSubDir : repoPath;

		string[] repoDirs = _fileSystem.GetDirectories(effectiveRepoPath);
		HashSet<string> repoPackageNames = new(
			repoDirs.Select(d => _fileSystem.GetDirectoryInfo(d).Name),
			StringComparer.OrdinalIgnoreCase);

		List<string> packagesToLink = [];
		foreach (PackageInfo pkg in unlockedPackages) {
			if (repoPackageNames.Contains(pkg.Descriptor.Name)) {
				packagesToLink.Add(pkg.Descriptor.Name);
			} else {
				_logger.WriteWarning(
					$"Unlocked package '{pkg.Descriptor.Name}' not found in repository — skipping");
			}
		}

		if (packagesToLink.Count == 0) {
			_logger.WriteWarning("No unlocked packages found in the repository.");
			return 0;
		}

		string packageNames = string.Join(",", packagesToLink);
		return HandleLinkWithDirPath(envPkgPath, repoPath, packageNames);
	}

	/// <summary>
	/// Links unlocked packages from a versioned repo structure (PackageName/branch/version/).
	/// </summary>
	protected virtual int HandleUnlockedVersionedRepo(string envPkgPath, string repoPath,
		List<PackageInfo> unlockedPackages) {
		int linkedCount = 0;
		int skippedCount = 0;

		foreach (PackageInfo pkg in unlockedPackages) {
			string packageName = pkg.Descriptor.Name;
			string packageVersion = pkg.Descriptor.PackageVersion;

			string versionPath = FindPackageVersionPath(repoPath, packageName, packageVersion);
			if (string.IsNullOrEmpty(versionPath)) {
				_logger.WriteWarning(
					$"Unlocked package '{packageName}' (v{packageVersion}) not found in repository — skipping");
				skippedCount++;
				continue;
			}

			string envPackagePath = _fileSystem.Combine(envPkgPath, packageName);
			if (_fileSystem.ExistsDirectory(envPackagePath)) {
				_fileSystem.DeleteDirectory(envPackagePath, true);
			}

			_fileSystem.CreateDirectorySymLink(envPackagePath, versionPath);
			_logger.WriteInfo($"Linked '{packageName}' (v{packageVersion}) → {versionPath}");
			linkedCount++;
		}

		_logger.WriteInfo($"Linking completed: {linkedCount} linked, {skippedCount} skipped");
		return 0;
	}

	private int HandleEnvironmentOption(Link4RepoOptions options){
		return TryResolveDirectoryPath(options.Environment, _fileSystem, out string resolvedPath)
			? HandleLinkWithDirPath(resolvedPath, options.RepoPath, options.Packages)
			: HandleLinkingByEnvName(options.Environment, options.RepoPath, options.Packages);
	}

	private int HandleEnvPkgPathOptions(Link4RepoOptions options){
		return TryResolveDirectoryPath(options.EnvPkgPath, _fileSystem, out string resolvedPath)
			? HandleLinkWithDirPath(resolvedPath, options.RepoPath, options.Packages)
			: HandleLinkingByEnvName(options.EnvPkgPath, options.RepoPath, options.Packages);
	}

	private int HandleLinkingByEnvNameOnWindows(string envName, string repoPath, string packages){
		ExecuteMediatorRequest(OnAllSitesRequestCompleted);
		EnvironmentSettings environment = _settingsRepository.GetEnvironment(envName);
		if (environment is null) {
			_logger.WriteError(
				$"Environment {envName} is not a registered environment. Please correct environment name an try again.");
			return 1;
		}

		bool isEnvUri = Uri.TryCreate(environment.Uri ?? string.Empty, UriKind.Absolute, out Uri envUri);
		if (!isEnvUri) {
			_logger.WriteError($"Environment {envName} missing Url. Please correct environment name an try again.");
			return 1;
		}

		List<IISScannerHandler.RegisteredSite> sites = AllSites
			.Where(s => s.Uris.Any(iisRegisteredUrl =>
				Uri.Compare(envUri,
					iisRegisteredUrl,
					UriComponents.StrongAuthority,
					UriFormat.SafeUnescaped,
					StringComparison.InvariantCulture) == 0)).ToList();
		if (sites.Count == 1) {
			IISScannerHandler.RegisteredSite site = sites[0];
			string sitePath = site.siteType switch {
				IISScannerHandler.SiteType.NetFramework => _fileSystem.Combine(site.siteBinding.path,
					"Terrasoft.WebApp", "Terrasoft.Configuration", "Pkg"),
				var _ => _fileSystem.Combine(site.siteBinding.path, "Terrasoft.Configuration", "Pkg")
			};

			if (_fileSystem.ExistsDirectory(sitePath)) {
				return HandleLinkWithDirPath(sitePath, repoPath, packages);
			}
		}

		_logger.WriteError($"Environment {envName} not found. Please check the environment name and try again.");
		return 1;
	}

	/// <summary>
	/// Handles repository linking by registered environment name.
	/// </summary>
	/// <param name="envName">Registered environment name.</param>
	/// <param name="repoPath">Repository path.</param>
	/// <param name="packages">Package selector.</param>
	/// <returns><c>0</c> on success; otherwise <c>1</c>.</returns>
	private int HandleLinkingByEnvName(string envName, string repoPath, string packages){
		if (!_settingsRepository.IsEnvironmentExists(envName)) {
			_logger.WriteError(
				$"Environment {envName} is not a registered environment. Please correct environment name an try again.");
			return 1;
		}

		EnvironmentSettings environment = _settingsRepository.GetEnvironment(envName);
		string environmentPackagePath = ResolveEnvironmentPackagePath(environment);
		if (!string.IsNullOrWhiteSpace(environmentPackagePath)) {
			return HandleLinkWithDirPath(environmentPackagePath, repoPath, packages);
		}

		if (!Current.IsWindows) {
			_logger.WriteError(
				$"Environment {envName} does not have a valid local package path. Configure EnvironmentPath or use --envPkgPath with the direct package folder path.");
			return 1;
		}

		return HandleLinkingByEnvNameOnWindows(envName, repoPath, packages);
	}

	/// <summary>
	/// Handles repository linking by explicit environment package path.
	/// </summary>
	/// <param name="sitePath">Target package folder path.</param>
	/// <param name="repoPath">Repository path.</param>
	/// <param name="packages">Package selector.</param>
	/// <returns><c>0</c> on success; otherwise <c>1</c>.</returns>
	protected virtual int HandleLinkWithDirPath(string sitePath, string repoPath, string packages){
		_rfsEnvironment.Link4Repo(sitePath, repoPath, packages);
		_logger.WriteInfo($"Linking repository package(s) to environment {sitePath} from {repoPath}");
		return 0;
	}

	private int PrintErrorsAndExit(IEnumerable<ValidationFailure> errors){
		_logger.PrintValidationFailureErrors(errors);
		return 1;
	}

	private string ResolveEnvironmentPackagePath(EnvironmentSettings environment) {
		return GetEnvironmentPackagePathCandidates(environment)
			.FirstOrDefault(_fileSystem.ExistsDirectory);
	}

	#endregion

	#region Methods: Public

	/// <inheritdoc />
	public override int Execute(Link4RepoOptions options){
		ValidationResult validationResult = _validator.Validate(options);
		if (!validationResult.IsValid) {
			return PrintErrorsAndExit(validationResult.Errors);
		}

		try {
			if (options.Unlocked) {
				string envPkgPath = ResolveEnvPkgPath(options);
				if (string.IsNullOrWhiteSpace(envPkgPath)) {
					_logger.WriteError("Could not resolve environment packages path.");
					return 1;
				}
				return HandleUnlockedLinking(options, envPkgPath);
			}

			// For --packages flow: check if packages need preparation (unlock + 2fs)
			if (!string.IsNullOrWhiteSpace(options.Packages)) {
				int prepResult = TryPreparePackages(options);
				if (prepResult != 0) {
					return prepResult;
				}
			}

			return options switch {
						not null when !string.IsNullOrWhiteSpace(options.Environment) => HandleEnvironmentOption(options),
						not null when !string.IsNullOrWhiteSpace(options.EnvPkgPath) => HandleEnvPkgPathOptions(options),
						var _ => 1
					};
		} catch (Exception ex) {
			_logger.WriteError($"Error during linking: {ex.Message}");
			return 1;
		}
	}

	/// <summary>
	/// Resolves the environment Pkg path from options, trying --envPkgPath first, then -e environment name.
	/// </summary>
	private string ResolveEnvPkgPath(Link4RepoOptions options) {
		if (!string.IsNullOrWhiteSpace(options.EnvPkgPath)) {
			return TryResolveDirectoryPath(options.EnvPkgPath, _fileSystem, out string resolved)
				? resolved
				: options.EnvPkgPath;
		}

		if (!string.IsNullOrWhiteSpace(options.Environment)) {
			if (TryResolveDirectoryPath(options.Environment, _fileSystem, out string resolved)) {
				return resolved;
			}
			if (_settingsRepository.IsEnvironmentExists(options.Environment)) {
				EnvironmentSettings environment = _settingsRepository.GetEnvironment(options.Environment);
				return ResolveEnvironmentPackagePath(environment);
			}
		}

		return null;
	}

	/// <summary>
	/// Checks if any of the specified packages are incomplete in the Pkg folder
	/// and runs preparation (Maintainer check → unlock → 2fs) if needed.
	/// Returns 0 if preparation succeeded or was not needed, non-zero on error.
	/// </summary>
	private int TryPreparePackages(Link4RepoOptions options) {
		string envPkgPath = ResolveEnvPkgPath(options);
		if (string.IsNullOrWhiteSpace(envPkgPath) || !_fileSystem.ExistsDirectory(envPkgPath)) {
			return 0;
		}

		IReadOnlyList<string> packageNames = options.Packages == "*"
			? []
			: options.Packages.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

		if (packageNames.Count == 0) {
			return 0;
		}

		bool anyIncomplete = packageNames.Any(name => IsPackageIncomplete(envPkgPath, name));
		if (!anyIncomplete) {
			return 0;
		}

		_logger.WriteInfo("Some packages are missing or incomplete in Pkg folder — running preparation...");
		return PreparePackagesForLinking(envPkgPath, options.RepoPath, packageNames);
	}

	#endregion

}
