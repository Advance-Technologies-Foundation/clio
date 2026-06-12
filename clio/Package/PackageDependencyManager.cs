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

	#endregion

	#region Constructors: Public

	public PackageDependencyManager(IApplicationPackageListProvider applicationPackageListProvider,
		IApplicationClient applicationClient, IServiceUrlBuilder serviceUrlBuilder)
		: base(applicationPackageListProvider, applicationClient, serviceUrlBuilder) {
		_applicationPackageListProvider = applicationPackageListProvider;
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

		List<PackageInfo> installedPackages = _applicationPackageListProvider.GetPackages("{}").ToList();
		PackageInfo targetPackage = FindPackage(installedPackages, packageName)
			?? throw new Exception($"Package with name \"{packageName}\" not found in the environment.");

		WorkspacePackageDto package = LoadPackageProperties(targetPackage.Descriptor.UId, packageName);
		package.DependsOnPackages ??= [];

		foreach (PackageDependencySpec dependency in requestedDependencies) {
			PackageInfo dependencyPackage = FindPackage(installedPackages, dependency.Name)
				?? throw new Exception(
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

		return package.DependsOnPackages
			.Select(dependency => dependency.Name ?? dependency.UId.ToString())
			.ToList();
	}

	#endregion

	#region Methods: Private

	private static PackageInfo FindPackage(IEnumerable<PackageInfo> packages, string packageName) =>
		packages.FirstOrDefault(package =>
			string.Equals(package.Descriptor.Name, packageName, StringComparison.OrdinalIgnoreCase));

	private WorkspacePackageDto LoadPackageProperties(Guid packageUId, string packageName) {
		PackagePropertiesResponse response =
			SendRequest<Guid, PackagePropertiesResponse>(PackageServiceUrl, "GetPackageProperties", packageUId);
		ThrowsErrorIfUnsuccessfulResponseReceived(response);
		return response.Package
			?? throw new Exception($"Could not read properties of package \"{packageName}\".");
	}

	private void SavePackageProperties(WorkspacePackageDto package) {
		SavePackagePropertiesResponse response =
			SendRequest<WorkspacePackageDto, SavePackagePropertiesResponse>(
				PackageServiceUrl, "SavePackageProperties", package);
		if (response.Success) {
			return;
		}
		throw new Exception(BuildSaveErrorMessage(response));
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
