using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using Clio.Common;
using Clio.Common.Responses;
using Clio.Package.Responses;
using Newtonsoft.Json;

namespace Clio.Package;

/// <inheritdoc cref="IRemotePackageCreator"/>
internal sealed class RemotePackageCreator : IRemotePackageCreator
{

	#region Fields: Private

	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly IApplicationPackageListProvider _packageListProvider;
	private readonly IPackageDependencyManager _dependencyManager;
	private readonly ISysSettingsManager _sysSettingsManager;
	private readonly IApplicationInfoService _applicationInfoService;
	private readonly EnvironmentSettings _environmentSettings;

	#endregion

	#region Constructors: Public

	public RemotePackageCreator(IApplicationClient applicationClient, IServiceUrlBuilder serviceUrlBuilder,
		IApplicationPackageListProvider packageListProvider, IPackageDependencyManager dependencyManager,
		ISysSettingsManager sysSettingsManager, IApplicationInfoService applicationInfoService,
		EnvironmentSettings environmentSettings) {
		_applicationClient = applicationClient;
		_serviceUrlBuilder = serviceUrlBuilder;
		_packageListProvider = packageListProvider;
		_dependencyManager = dependencyManager;
		_sysSettingsManager = sysSettingsManager;
		_applicationInfoService = applicationInfoService;
		_environmentSettings = environmentSettings;
	}

	#endregion

	#region Methods: Private

	private string ApplyPrefix(string requestedName) {
		string prefix = SysSettingCodes.ReadSchemaNamePrefix(_sysSettingsManager);
		return prefix.Length == 0 || requestedName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
			? requestedName
			: prefix + requestedName;
	}

	private static List<PackageDependencySpec> ResolveDependencies(IReadOnlyCollection<PackageInfo> installed,
		IEnumerable<string> requested) {
		List<string> names = (requested ?? [])
			.Where(name => !string.IsNullOrWhiteSpace(name))
			.Select(name => name.Trim())
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
		List<string> missing = names.Where(name => FindPackage(installed, name) is null).ToList();
		if (missing.Count > 0) {
			throw new InvalidOperationException(
				$"Dependency package(s) not found in the environment: {string.Join(", ", missing)}. "
				+ "Nothing was created.");
		}
		return names.Select(name => new PackageDependencySpec(name)).ToList();
	}

	private static PackageInfo FindPackage(IEnumerable<PackageInfo> packages, string packageName) =>
		packages.FirstOrDefault(package =>
			string.Equals(package.Descriptor.Name, packageName, StringComparison.OrdinalIgnoreCase));

	private void SendCreateRequest(CreatePackageDto package, Guid? applicationId) {
		(ServiceUrlBuilder.KnownRoute route, object body) = applicationId is { } appId
			? (ServiceUrlBuilder.KnownRoute.CreatePackageInApp, new CreatePackageInAppDto(package, appId))
			: (ServiceUrlBuilder.KnownRoute.CreatePackage, (object)package);
		// Non-replayable: an expired-session recovery must not send the create a second time, or the replay
		// is refused as a duplicate and a package that exists would be reported as never created.
		string responseText = _applicationClient.ExecuteNonReplayablePostRequest(
			_serviceUrlBuilder.Build(route), JsonConvert.SerializeObject(body));
		BaseResponse response = JsonConvert.DeserializeObject<BaseResponse>(responseText) ?? new BaseResponse();
		if (!response.Success) {
			throw new InvalidOperationException(
				$"The environment refused to create package \"{package.Name}\": "
				+ $"{response.ErrorInfo?.Message ?? "no error details returned"}. Nothing was created.");
		}
	}

	private WorkspacePackageDto ReadBack(Guid packageUId) {
		PackagePropertiesResponse response = _applicationClient.ExecutePostRequest<PackagePropertiesResponse>(
			_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetPackageProperties),
			JsonConvert.SerializeObject(packageUId));
		if (!response.Success || response.Package is null) {
			throw new InvalidOperationException(response.ErrorInfo?.Message ?? "GetPackageProperties returned no package.");
		}
		return response.Package;
	}

	/// <returns>
	/// <see langword="true"/> when the package is found, <see langword="false"/> when the environment answered
	/// that it is not, and <see langword="null"/> when the environment could not be asked.
	/// </returns>
	private bool? Exists(Guid packageUId) {
		try {
			ReadBack(packageUId);
			return true;
		} catch (InvalidOperationException) {
			return false;
		} catch (Exception) {
			return null;
		}
	}

	private static string ReadString(WorkspacePackageDto package, string key) =>
		package.AdditionalData is not null && package.AdditionalData.TryGetValue(key, out var token)
			? token?.ToString()
			: null;

	private static int? ReadInt(WorkspacePackageDto package, string key) =>
		int.TryParse(ReadString(package, key), out int value) ? value : null;

	#endregion

	#region Methods: Public

	/// <inheritdoc />
	public RemotePackageCreateResult Create(RemotePackageCreateRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		request.PackageName.CheckArgumentNullOrWhiteSpace(nameof(request.PackageName));
		string packageName = ApplyPrefix(request.PackageName.Trim());

		List<PackageInfo> installed = _packageListProvider.GetPackages("{}").ToList();
		if (FindPackage(installed, packageName) is { } existing) {
			throw new InvalidOperationException(
				$"Package \"{existing.Descriptor.Name}\" already exists in the environment. Nothing was created.");
		}
		List<PackageDependencySpec> dependencies = ResolveDependencies(installed, request.Dependencies);
		Guid? applicationId = string.IsNullOrWhiteSpace(request.ApplicationCode)
			? null
			: Guid.Parse(_applicationInfoService.FindApplicationId(_environmentSettings, request.ApplicationCode.Trim()).Id);

		Guid packageUId = Guid.NewGuid();
		try {
			SendCreateRequest(new CreatePackageDto(Guid.NewGuid(), packageUId, packageName,
				request.Description ?? string.Empty), applicationId);
		} catch (Exception exception) when (exception is not InvalidOperationException) {
			// The request may have been stored before the connection failed; the UId clio sent tells which.
			switch (Exists(packageUId)) {
				case false:
					throw new InvalidOperationException(
						$"Creating package \"{packageName}\" failed: {exception.Message} "
						+ "No package with the identifier clio sent was found afterwards. Nothing was created.",
						exception);
				case null:
					throw new PackageCreationOutcomeUnknownException(
						$"Creating package \"{packageName}\" failed: {exception.Message} The environment could not be "
						+ $"asked afterwards whether package UId {packageUId} exists. Check list-packages for "
						+ $"\"{packageName}\" before creating it again.", exception);
			}
		}

		string incompleteReason = null;
		if (dependencies.Count > 0) {
			try {
				_dependencyManager.AddDependencies(packageName, dependencies);
			} catch (Exception exception) {
				incompleteReason = $"Package \"{packageName}\" was created, but its dependencies were not applied: "
					+ $"{exception.Message} Add them with add-package-dependency.";
			}
		}
		try {
			WorkspacePackageDto stored = ReadBack(packageUId);
			return new RemotePackageCreateResult(packageUId, stored.Name ?? packageName,
				ReadString(stored, "maintainer"), ReadString(stored, "description"),
				(stored.DependsOnPackages ?? []).Select(dependency => dependency.Name).ToList(),
				ReadInt(stored, "installType"), request.ApplicationCode, incompleteReason);
		} catch (Exception exception) {
			return new RemotePackageCreateResult(packageUId, packageName, null, request.Description, [], null,
				request.ApplicationCode,
				incompleteReason ?? $"Package \"{packageName}\" was created, but reading it back failed: {exception.Message}");
		}
	}

	#endregion

	#region Class: CreatePackageDto

	/// <summary>
	/// Body of <c>PackageService.svc/CreatePackage</c>: the fields the platform stores on creation, with the
	/// values the Configuration section sends for a new general package.
	/// </summary>
	private sealed record CreatePackageDto(
		[property: JsonProperty("id")] Guid Id,
		[property: JsonProperty("uId")] Guid UId,
		[property: JsonProperty("name")] string Name,
		[property: JsonProperty("description")] string Description) {

		[JsonProperty("type")]
		public int Type => 0;

		[JsonProperty("installBehavior")]
		public int InstallBehavior => 0;

		[JsonProperty("isChanged")]
		public bool IsChanged => true;

		[JsonProperty("position")]
		public int Position => 0;
	}

	#endregion

	#region Class: CreatePackageInAppDto

	/// <summary>
	/// Body of <c>ApplicationPackagesService.svc/CreatePackageInApp</c>.
	/// </summary>
	private sealed record CreatePackageInAppDto(
		[property: JsonProperty("package")] CreatePackageDto Package,
		[property: JsonProperty("appId")] Guid AppId);

	#endregion

}
