using System;
using System.IO;
using System.Linq;
using Clio.Common;
using Clio.Workspaces;

namespace Clio.Package;

#region Interface: IWorkspacePackageProvisioner

/// <summary>
/// Resolves where workspace packages live and ensures a package exists locally before content is
/// scaffolded into it.
/// </summary>
public interface IWorkspacePackageProvisioner
{

	#region Properties: Public

	/// <summary>
	/// Absolute path to the folder that hosts workspace packages: the workspace <c>packages</c> folder
	/// inside a workspace, otherwise a <c>packages</c> folder under the current directory.
	/// </summary>
	string PackagesPath { get; }

	#endregion

	#region Methods: Public

	/// <summary>
	/// Ensures the package exists locally under <see cref="PackagesPath"/>. When it already exists on the
	/// target environment and <paramref name="enableDownloadPackage"/> returns <c>true</c>, the package is
	/// downloaded and registered in the workspace; otherwise a new local package is created.
	/// </summary>
	/// <param name="packageName">Package to ensure.</param>
	/// <param name="enableDownloadPackage">Callback deciding whether an existing remote package is downloaded.</param>
	void EnsurePackage(string packageName, Func<string, bool> enableDownloadPackage);

	#endregion

}

#endregion

#region Class: WorkspacePackageProvisioner

/// <inheritdoc cref="IWorkspacePackageProvisioner"/>
public class WorkspacePackageProvisioner : IWorkspacePackageProvisioner
{

	#region Constants: Private

	private const string PackagesDirectoryName = "packages";

	#endregion

	#region Fields: Private

	private readonly EnvironmentSettings _environmentSettings;
	private readonly IApplicationPackageListProvider _applicationPackageListProvider;
	private readonly IPackageCreator _packageCreator;
	private readonly IPackageDownloader _packageDownloader;
	private readonly IWorkspace _workspace;
	private readonly IWorkspacePathBuilder _workspacePathBuilder;
	private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
	private readonly IFileSystem _fileSystem;
	private readonly ILogger _logger;

	#endregion

	#region Constructors: Public

	public WorkspacePackageProvisioner(EnvironmentSettings environmentSettings,
		IApplicationPackageListProvider applicationPackageListProvider, IPackageCreator packageCreator,
		IPackageDownloader packageDownloader, IWorkspace workspace, IWorkspacePathBuilder workspacePathBuilder,
		IWorkingDirectoriesProvider workingDirectoriesProvider, IFileSystem fileSystem, ILogger logger) {
		environmentSettings.CheckArgumentNull(nameof(environmentSettings));
		applicationPackageListProvider.CheckArgumentNull(nameof(applicationPackageListProvider));
		packageCreator.CheckArgumentNull(nameof(packageCreator));
		packageDownloader.CheckArgumentNull(nameof(packageDownloader));
		workspace.CheckArgumentNull(nameof(workspace));
		workspacePathBuilder.CheckArgumentNull(nameof(workspacePathBuilder));
		workingDirectoriesProvider.CheckArgumentNull(nameof(workingDirectoriesProvider));
		fileSystem.CheckArgumentNull(nameof(fileSystem));
		logger.CheckArgumentNull(nameof(logger));
		_environmentSettings = environmentSettings;
		_applicationPackageListProvider = applicationPackageListProvider;
		_packageCreator = packageCreator;
		_packageDownloader = packageDownloader;
		_workspace = workspace;
		_workspacePathBuilder = workspacePathBuilder;
		_workingDirectoriesProvider = workingDirectoriesProvider;
		_fileSystem = fileSystem;
		_logger = logger;
	}

	#endregion

	#region Properties: Public

	/// <inheritdoc/>
	public string PackagesPath =>
		_workspacePathBuilder.IsWorkspace
			? _workspacePathBuilder.PackagesFolderPath
			: Path.Combine(_workingDirectoriesProvider.CurrentDirectory, PackagesDirectoryName);

	#endregion

	#region Methods: Private

	private PackageInfo FindExistingPackage(string packageName) {
		try {
			return _applicationPackageListProvider.GetPackages()
				.FirstOrDefault(p =>
					p.Descriptor.Name.Equals(packageName, StringComparison.InvariantCultureIgnoreCase));
		} catch (Exception e) {
			// GetPackages needs a reachable, configured environment. When none is available (no
			// environment, offline, or auth failure) we cannot check for a remote package, so we log and
			// fall back to creating the package locally instead of failing the scaffold.
			_logger.WriteWarning(
				$"Could not query existing packages from the environment ({e.Message}). "
				+ "A new local package will be created.");
			return null;
		}
	}

	#endregion

	#region Methods: Public

	/// <inheritdoc/>
	public void EnsurePackage(string packageName, Func<string, bool> enableDownloadPackage) {
		if (_fileSystem.ExistsDirectory(Path.Combine(PackagesPath, packageName))) {
			return;
		}
		PackageInfo existingPackage = FindExistingPackage(packageName);
		if (existingPackage != null && enableDownloadPackage(packageName)) {
			_packageDownloader.DownloadPackage(packageName, _environmentSettings,
				_workspacePathBuilder.PackagesFolderPath);
			_workspace.AddPackageIfNeeded(packageName);
		} else {
			_packageCreator.Create(PackagesPath, packageName);
		}
	}

	#endregion

}

#endregion
