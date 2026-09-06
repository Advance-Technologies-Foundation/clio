using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Clio.Common;
using Clio.Package.Responses;
using Newtonsoft.Json;

namespace Clio.Package;

/// <inheritdoc cref="IPackageDependencyManager"/>
internal sealed class PackageDependencyManager : BasePackageOperation, IPackageDependencyManager
{

	#region Fields: Private

	private readonly IApplicationPackageListProvider _applicationPackageListProvider;
	private readonly ILogger _logger;

	#endregion

	#region Constructors: Public

	public PackageDependencyManager(IApplicationPackageListProvider applicationPackageListProvider,
		IApplicationClient applicationClient, IServiceUrlBuilder serviceUrlBuilder, ILogger logger)
		: base(applicationPackageListProvider, applicationClient, serviceUrlBuilder) {
		_applicationPackageListProvider = applicationPackageListProvider;
		_logger = logger;
	}

	#endregion

	#region Methods: Protected

	/// <summary>
	/// Serializes the request payload as a bare JSON value. A <see cref="Guid"/> becomes a quoted string
	/// (the body shape <c>GetPackageProperties</c> expects) and a <see cref="WorkspacePackageDto"/> becomes
	/// the bare object body <c>SavePackageProperties</c> expects.
	/// </summary>
	protected override string CreateRequestData<TRequest>(TRequest request) =>
		JsonConvert.SerializeObject(request);

	#endregion

	#region Methods: Public

	/// <inheritdoc cref="IPackageDependencyManager.AddDependencies"/>
	public IReadOnlyList<string> AddDependencies(string packageName,
		IEnumerable<PackageDependencySpec> dependencies) {
		packageName.CheckArgumentNullOrWhiteSpace(nameof(packageName));
		dependencies.CheckArgumentNull(nameof(dependencies));

		List<PackageDependencySpec> requestedDependencies = dependencies
			.Where(dependency => !string.IsNullOrWhiteSpace(dependency?.Name))
			.ToList();
		if (requestedDependencies.Count == 0) {
			throw new ArgumentException("At least one dependency must be specified.", nameof(dependencies));
		}

		(List<PackageInfo> installedPackages, WorkspacePackageDto package) = LoadTargetPackage(packageName);
		package.DependsOnPackages ??= [];

		foreach (PackageDependencySpec dependency in requestedDependencies) {
			PackageInfo dependencyPackage = FindPackage(installedPackages, dependency.Name)
				?? throw new InvalidOperationException(
					$"Dependency package with name \"{dependency.Name}\" not found in the environment.");
			if (package.DependsOnPackages.Any(existing => existing.UId == dependencyPackage.Descriptor.UId)) {
				continue;
			}
			package.DependsOnPackages.Add(new WorkspacePackageDto {
				UId = dependencyPackage.Descriptor.UId,
				Name = dependencyPackage.Descriptor.Name,
				Version = string.IsNullOrWhiteSpace(dependency.Version)
					? dependencyPackage.Descriptor.PackageVersion
					: dependency.Version
			});
		}

		SavePackageProperties(package);

		return ToDependencyNames(package.DependsOnPackages);
	}

	/// <inheritdoc cref="IPackageDependencyManager.GetDependencies"/>
	public IReadOnlyList<string> GetDependencies(string packageName) {
		packageName.CheckArgumentNullOrWhiteSpace(nameof(packageName));
		(_, WorkspacePackageDto package) = LoadTargetPackage(packageName);
		return ToDependencyNames(package.DependsOnPackages);
	}

	/// <inheritdoc cref="IPackageDependencyManager.RemoveDependencies"/>
	public IReadOnlyList<string> RemoveDependencies(string packageName,
		IEnumerable<string> dependencyNames) {
		packageName.CheckArgumentNullOrWhiteSpace(nameof(packageName));
		dependencyNames.CheckArgumentNull(nameof(dependencyNames));

		HashSet<string> namesToRemove = dependencyNames
			.Where(name => !string.IsNullOrWhiteSpace(name))
			.Select(name => name.Trim())
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		if (namesToRemove.Count == 0) {
			throw new ArgumentException("At least one dependency must be specified.", nameof(dependencyNames));
		}

		(_, WorkspacePackageDto package) = LoadTargetPackage(packageName);
		package.DependsOnPackages ??= [];

		// Match by name only (case-insensitive): the version is irrelevant for wiring, and removing an absent
		// dependency is a no-op. Only persist when something actually changed so a no-op stays cheap.
		int removedCount = package.DependsOnPackages
			.RemoveAll(existing => existing.Name is not null && namesToRemove.Contains(existing.Name));
		if (removedCount > 0) {
			SavePackageProperties(package);
		}

		return ToDependencyNames(package.DependsOnPackages);
	}

	#endregion

	#region Methods: Private

	/// <summary>
	/// Resolves <paramref name="packageName"/> against the environment and loads its editable properties.
	/// </summary>
	/// <param name="packageName">Package to resolve.</param>
	/// <returns>The installed package list (the add path needs it to resolve dependencies) and the properties.</returns>
	/// <exception cref="InvalidOperationException">The package is not installed in the environment.</exception>
	private (List<PackageInfo> Installed, WorkspacePackageDto Package) LoadTargetPackage(string packageName) {
		List<PackageInfo> installedPackages = _applicationPackageListProvider.GetPackages("{}").ToList();
		PackageInfo targetPackage = FindPackage(installedPackages, packageName)
			?? throw new InvalidOperationException($"Package with name \"{packageName}\" not found in the environment.");
		return (installedPackages, LoadPackageProperties(targetPackage.Descriptor.UId, packageName));
	}

	/// <summary>
	/// Projects a dependency list to names, falling back to the UId for a row the server sent without one.
	/// </summary>
	/// <param name="dependencies">Dependency rows, possibly <see langword="null"/>.</param>
	/// <returns>The dependency names.</returns>
	private static IReadOnlyList<string> ToDependencyNames(List<WorkspacePackageDto>? dependencies) =>
		(dependencies ?? [])
			.Select(dependency => dependency.Name ?? dependency.UId.ToString())
			.ToList();

	private static PackageInfo FindPackage(IEnumerable<PackageInfo> packages, string packageName) =>
		packages.FirstOrDefault(package =>
			string.Equals(package.Descriptor.Name, packageName, StringComparison.OrdinalIgnoreCase));

	private WorkspacePackageDto LoadPackageProperties(Guid packageUId, string packageName) {
		PackagePropertiesResponse response =
			SendRequest<Guid, PackagePropertiesResponse>(PackageServiceUrl, "GetPackageProperties", packageUId);
		string couldNotReadMessage = $"Could not read properties of package \"{packageName}\".";
		if (!response.Success) {
			// Mirror the save path's null-safe guard: a failed GetPackageProperties response may carry a null
			// ErrorInfo (the permission / HTML-error failure modes this feature diagnoses), so fall back to a
			// descriptive message instead of dereferencing ErrorInfo.Message and surfacing a bare NullReferenceException.
			throw new InvalidOperationException(response.ErrorInfo?.Message ?? couldNotReadMessage);
		}
		return response.Package ?? throw new InvalidOperationException(couldNotReadMessage);
	}

	private void SavePackageProperties(WorkspacePackageDto package) {
		SavePackagePropertiesResponse response =
			SendRequest<WorkspacePackageDto, SavePackagePropertiesResponse>(
				PackageServiceUrl, "SavePackageProperties", package);
		if (response.ValidationErrors is { Length: > 0 }) {
			_logger.WriteWarning("Validation warnings from server: "
				+ string.Join("; ", response.ValidationErrors
					.Select(e => $"{e.PackageName}/{e.ItemName}: {e.Message}")));
		}
		if (!response.Success) {
			throw new InvalidOperationException(BuildSaveErrorMessage(response));
		}
		if (response.CompilationRequired) {
			_logger.WriteInfo(
				"The dependency change requires a configuration compilation. Run compile-configuration to apply.");
		}
	}

	private static string BuildSaveErrorMessage(SavePackagePropertiesResponse response) {
		StringBuilder message = new();
		message.Append(response.ErrorInfo?.Message ?? "Failed to save package dependencies.");
		if (response.ValidationErrors is { Length: > 0 }) {
			message.Append(" Validation errors: ");
			message.Append(string.Join("; ", response.ValidationErrors
				.Select(error => $"{error.PackageName}/{error.ItemName}: {error.Message}")));
		}
		return message.ToString();
	}

	#endregion

}
