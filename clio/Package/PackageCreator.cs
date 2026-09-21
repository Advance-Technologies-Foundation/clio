using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Clio.Common;
using Clio.Workspace;
using Clio.Workspaces;

namespace Clio.Package;

#region Interface: IPackageCreator

public interface IPackageCreator{
	#region Methods: Public

	/// <summary>Creates a package in the current workspace or working directory.</summary>
	/// <param name="packageName">Name of the package to create.</param>
	/// <param name="asApp">When <see langword="true"/>, also generates application-package artefacts.</param>
	/// <param name="schemaNamePrefix">
	/// Prefix for generated schema names. <see langword="null"/> resolves it from the target environment's
	/// <c>SchemaNamePrefix</c> system setting; a non-null value (including an empty one) wins over it.
	/// </param>
	void Create(string packageName, bool? asApp, string schemaNamePrefix = null);

	/// <summary>Creates a package that is not an application package at an explicit packages path.</summary>
	/// <param name="packagesPath">Directory that holds packages.</param>
	/// <param name="packageName">Name of the package to create.</param>
	void Create(string packagesPath, string packageName);

	#endregion
}

#endregion

#region Class: PackageCreator

public class PackageCreator : IPackageCreator{
	#region Constants: Internal
	internal const string InvalidPackageNameMessage =
		"Package name must start with a letter or underscore and contain only letters, digits, and underscores. " +
		"Its length must be 1 to 70 characters, and a lone underscore is not valid.";

	#endregion

	#region Fields: Private

	private const int MaxPackageNameLength = 70;

	/// <summary>
	/// Longest schema code Creatio stores: <c>SysSchema.Name</c> is a <c>MediumText</c> column.
	/// </summary>
	internal const int MaxSchemaNameLength = 250;

	private readonly EnvironmentSettings _environmentSettings;
	private readonly IFileSystem _fileSystem;
	private readonly IJsonConverter _jsonConverter;
	private readonly ISchemaBuilder _schemaBuilder;
	private readonly ISchemaNamePrefixResolver _schemaNamePrefixResolver;
	private readonly IStandalonePackageFileManager _standalonePackageFileManager;
	private readonly ITemplateProvider _templateProvider;
	private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
	private readonly IWorkspace _workspace;
	private readonly IWorkspacePathBuilder _workspacePathBuilder;
	private readonly IWorkspaceSolutionCreator _workspaceSolutionCreator;

	#endregion

	private static readonly Func<string, string> RootNameSpace = packageName => $"{packageName}App";

	#region Constructors: Public

	public PackageCreator(EnvironmentSettings environmentSettings, IWorkspace workspace,
		IWorkspaceSolutionCreator workspaceSolutionCreator, ITemplateProvider templateProvider,
		IWorkspacePathBuilder workspacePathBuilder, IStandalonePackageFileManager standalonePackageFileManager,
		IJsonConverter jsonConverter, IWorkingDirectoriesProvider workingDirectoriesProvider,
		IFileSystem fileSystem, ISchemaBuilder schemaBuilder,
		ISchemaNamePrefixResolver schemaNamePrefixResolver) {
		environmentSettings.CheckArgumentNull(nameof(environmentSettings));
		templateProvider.CheckArgumentNull(nameof(templateProvider));
		workspace.CheckArgumentNull(nameof(workspace));
		workspaceSolutionCreator.CheckArgumentNull(nameof(workspaceSolutionCreator));
		workspacePathBuilder.CheckArgumentNull(nameof(workspacePathBuilder));
		standalonePackageFileManager.CheckArgumentNull(nameof(standalonePackageFileManager));
		jsonConverter.CheckArgumentNull(nameof(jsonConverter));
		workingDirectoriesProvider.CheckArgumentNull(nameof(workingDirectoriesProvider));
		fileSystem.CheckArgumentNull(nameof(fileSystem));
		schemaBuilder.CheckArgumentNull(nameof(schemaBuilder));
		schemaNamePrefixResolver.CheckArgumentNull(nameof(schemaNamePrefixResolver));
		_environmentSettings = environmentSettings;
		_workspace = workspace;
		_workspaceSolutionCreator = workspaceSolutionCreator;
		_templateProvider = templateProvider;
		_workspacePathBuilder = workspacePathBuilder;
		_standalonePackageFileManager = standalonePackageFileManager;
		_jsonConverter = jsonConverter;
		_workingDirectoriesProvider = workingDirectoriesProvider;
		_fileSystem = fileSystem;
		_schemaBuilder = schemaBuilder;
		_schemaNamePrefixResolver = schemaNamePrefixResolver;
	}

	#endregion

	#region Properties: Private

	private bool IsWorkspace => _workspacePathBuilder.IsWorkspace;

	private string Maintainer => _environmentSettings.Maintainer ?? "Customer";

	#endregion

	#region Methods: Private

	private void AddAppDescriptor(string packagesPath, string packageName) {
		Package package = GetPackageFromDescriptor(packagesPath, packageName);
		AppDescriptorJson addDescriptorDto = new() {
			Name = packageName,
			Maintainer = Maintainer,
			Description = "",
			Icon = "",
			IconName = "",
			MarketplaceLink = "",
			OrderLink = "",
			SupportEmail = "",
			HelpLink = "",
			Color = "#FFAC07",
			Version = "0.1.0",
			RequiredPlatformVersion = "8.3.3",
			Code = packageName,
			Packages = [package]
		};
		string appDescriptorPath = _fileSystem.Combine(packagesPath, packageName, "Files", "app-descriptor.json");
		SaveAppDescriptorToFile(addDescriptorDto, appDescriptorPath);
	}

	private void AddLocalizationSchema(string packagesPath, string packageName, string schemaName) {
		string packagePath = _fileSystem.Combine(packagesPath, packageName);
		SourceCodeSchemaOptions options = new(
			RootNameSpace(packageName),
			"Owns package-level backend localizable values that have no more natural schema owner. " +
			"Page and other schema resources stay with the schema that renders or consumes them.",
			new Dictionary<string, string> {
				["LocalizableStrings.PackageLevelExample.Value"] = "Package-level localizable value"
			});
		_schemaBuilder.AddSchema("source-code", schemaName, packagePath, options);
	}

	/// <summary>
	/// Builds the code of the generated localization schema: the package name plus the suffix, carrying
	/// <paramref name="prefix"/>, and leaving a name that already carries it untouched so a package named
	/// after the prefix does not produce a doubled schema code.
	/// </summary>
	/// <remarks>
	/// Every artefact of the generated schema - descriptor Name/Caption, the C# class, the Schemas and
	/// Resources folder names and the metadata code - is derived from this one string inside
	/// <c>SchemaBuilder</c>, so producing it once prefixes all of them consistently and leaves exactly
	/// one string for <see cref="EnsureSchemaNameFits"/> to check.
	/// </remarks>
	private static string BuildLocalizationSchemaName(string prefix, string packageName) {
		string schemaName = $"{packageName}LocalizableStrings";
		return string.IsNullOrEmpty(prefix) || schemaName.StartsWith(prefix, StringComparison.Ordinal)
			? schemaName
			: prefix + schemaName;
	}

	/// <summary>
	/// Rejects a generated schema name Creatio could never store, before anything is written.
	/// </summary>
	/// <remarks>
	/// <c>SysSchema.Name</c> is a <c>MediumText</c> column, so 250 characters; a longer code is refused by
	/// the platform with "String or binary data would be truncated" (verified against a 10.1.725 stand
	/// with a 251-character name). The package name is capped at
	/// <see cref="MaxPackageNameLength"/> characters, so only a caller-supplied prefix can reach this
	/// length - and the failure it used to cause landed mid-write, leaving a package directory with no
	/// schema that every later attempt, valid prefix included, refused as already existing.
	/// </remarks>
	private static void EnsureSchemaNameFits(string schemaName) {
		if (schemaName.Length > MaxSchemaNameLength) {
			throw new ArgumentException(
				$"Generated schema name '{schemaName}' is {schemaName.Length} characters long; Creatio "
				+ $"stores a schema code in SysSchema.Name and accepts at most {MaxSchemaNameLength}. "
				+ "Shorten the schema-name prefix or the package name.");
		}
	}

	private void AddPackageToWorkspaceIfNeeded(string packageName) {
		if (!IsWorkspace) {
			return;
		}

		IList<string> workspacePackages = _workspace.WorkspaceSettings.Packages;
		if (workspacePackages.Contains(packageName)) {
			return;
		}

		workspacePackages.Add(packageName);
		_workspace.SaveWorkspaceSettings();
		_workspaceSolutionCreator.Create();
	}

	private void ApplyMacrosToCsFiles(string packagesPath, string packageName, bool includeLocalizationServices) {
		string packageFilesPath = _standalonePackageFileManager.BuildFilesPath(packagesPath, packageName);
		string localizationServices = string.Empty;
		if (includeLocalizationServices) {
			localizationServices = "serviceCollection.AddTransient<LocalizableStrings.ILocalizableStringResolver, " +
				"LocalizableStrings.LocalizableStringResolver>();";
		}
		string[] csFiles = _fileSystem.GetFiles(packageFilesPath, "*.cs", SearchOption.AllDirectories);
		foreach (string csFilePath in csFiles) {
			string csFileContent = _fileSystem.ReadAllText(csFilePath);
			string newCsFileContent = csFileContent
									  .Replace("#PackageName#", packageName)
									  .Replace("#RootNameSpace#", RootNameSpace(packageName))
									  .Replace("#LocalizationServices#", localizationServices);
			_fileSystem.WriteAllTextToFile(csFilePath, newCsFileContent);
		}
	}

	private void ApplyMacrosToCsProjFile(string packagesPath, string packageName) {
		string packageFilesPath = _standalonePackageFileManager.BuildFilesPath(packagesPath, packageName);
		string csProjPath = _fileSystem.Combine(packageFilesPath, $"{packageName}.csproj");
		string csProjContent = _fileSystem.ReadAllText(csProjPath);
		string newCsProjContent = csProjContent
								  .Replace("#PackageName#", packageName)
								  .Replace("#RootNameSpace#", RootNameSpace(packageName));
		_fileSystem.WriteAllTextToFile(csProjPath, newCsProjContent);
	}


	private void ApplyMacrosToProjectFiles(string packagesPath, string packageName) {
		string packageFilesPath = _standalonePackageFileManager.BuildFilesPath(packagesPath, packageName);
		string packageNameTargetPropsPath = _fileSystem.Combine(packageFilesPath, "Directory.Build.targets");
		string packageNameTargetPropsContent = _fileSystem.ReadAllText(packageNameTargetPropsPath);
		string newPackageNameTargetPropsContent = packageNameTargetPropsContent
												  .Replace("#PackageName#", packageName)
												  .Replace("#RootNameSpace#", RootNameSpace(packageName));
		_fileSystem.WriteAllTextToFile(packageNameTargetPropsPath, newPackageNameTargetPropsContent);
	}

	private PackageDescriptorDto CreatePackageDescriptor(string packageName, bool isStandalonePackage = true) {
		return new PackageDescriptorDto {
			Descriptor = new PackageDescriptor {
				Name = packageName,
				Maintainer = Maintainer,
				UId = Guid.NewGuid(),
				PackageVersion = "0.1.0",
				InstallBehavior = 1,
				ProjectPath = isStandalonePackage ? $"Files/{packageName}.csproj" : string.Empty,
				Type = isStandalonePackage ? PackageType.Assembly : PackageType.General,
				ModifiedOnUtc = PackageDescriptor.ConvertToModifiedOnUtc(DateTime.Now),
				DependsOn = new List<PackageDependency>() {
					new PackageDependency("CrtCoreBase", "7.8.0", 1, "3a71e376-9ac3-3049-c62b-9c43b9abe054")
				}
			}
		};
	}

	private void CreatePackageDescriptorToFileSystem(string packagePath, string packageName) {
		PackageDescriptorDto descriptor = CreatePackageDescriptor(packageName);
		string descriptorPath = _fileSystem.Combine(packagePath, "descriptor.json");
		_jsonConverter.SerializeObjectToFile(descriptor, descriptorPath);
	}

	/// <summary>
	/// Throws when the package directory already exists, so the caller learns it from a local check
	/// rather than after a Creatio round trip or a partial write.
	/// </summary>
	private void EnsurePackageDirectoryIsFree(string packagesPath, string packageName) {
		string packagePath = _fileSystem.Combine(packagesPath, packageName);
		if (_fileSystem.ExistsDirectory(packagePath)) {
			throw new InvalidOperationException($"Directory '{packagePath}' already exists");
		}
	}

	/// <summary>
	/// Writes the package skeleton. The already-exists check is NOT repeated here: <c>CreateCore</c> is
	/// the single guard site, and it runs before the prefix resolution that precedes this call.
	/// </summary>
	private void WritePackageSkeleton(string packagesPath, string packageName, bool includeLocalizationServices) {
		string packagePath = _fileSystem.Combine(packagesPath, packageName);
		_templateProvider.CopyTemplateFolder("package", packagePath);
		if (includeLocalizationServices) {
			_templateProvider.CopyTemplateFolder("package-localization", packagePath, "", "", false);
		}
		CreatePackageDescriptorToFileSystem(packagePath, packageName);
		CreatePackageProj(packagesPath, packageName, includeLocalizationServices);
	}

	private void CreatePackageProj(string packagesPath, string packageName, bool includeLocalizationServices) {
		ApplyMacrosToProjectFiles(packagesPath, packageName);
		RenameTemplatePackageNameCsproj(packagesPath, packageName);
		ApplyMacrosToCsFiles(packagesPath, packageName, includeLocalizationServices);
		ApplyMacrosToCsProjFile(packagesPath, packageName);
		RenameMainAppCs(packagesPath, packageName);
	}

	private Package GetPackageFromDescriptor(string packagePath, string packageName) {
		string descriptorContent = _fileSystem.ReadAllText(_fileSystem.Combine(packagePath, packageName, "descriptor.json"));
		PackageDescriptorDto packageDescriptor = JsonSerializer.Deserialize<PackageDescriptorDto>(descriptorContent);
		return new Package {
			UId = packageDescriptor.Descriptor.UId.ToString(),
			Name = packageDescriptor.Descriptor.Name
		};
	}


	private string GetPackagesPath() {
		return IsWorkspace
			? _workspacePathBuilder.PackagesFolderPath
			: _workingDirectoriesProvider.CurrentDirectory;
	}

	private void RenameMainAppCs(string packagesPath, string packageName) {
		string packageFilesPath = _standalonePackageFileManager.BuildFilesPath(packagesPath, packageName);
		string mainAppCsPath = Path.Combine(packageFilesPath, "src", "cs", "MainApp.cs");
		string newMainAppCsPath = Path.Combine(packageFilesPath, "src", "cs", $"{RootNameSpace(packageName)}.cs");
		_fileSystem.MoveFile(mainAppCsPath, newMainAppCsPath);
	}

	private void RenameTemplatePackageNameCsproj(string packagesPath, string packageName) {
		string packageFilesPath = _standalonePackageFileManager.BuildFilesPath(packagesPath, packageName);
		string templatePackageNameCsprojPath = _fileSystem.Combine(packageFilesPath, "PackageName.csproj");
		string newPackageNameCsprojPath = _standalonePackageFileManager
			.BuildStandaloneProjectPath(packagesPath, packageName);
		_fileSystem.MoveFile(templatePackageNameCsprojPath, newPackageNameCsprojPath);
	}

	private void UpdateAppDescriptorIfExists(string packagesPath, string packageName) {
		string[] appDescriptorFiles
			= _fileSystem.GetFiles(packagesPath, "app-descriptor.json", SearchOption.AllDirectories);
		if (appDescriptorFiles.Count() != 1) {
			return;
		}

		string appDescriptorFile = appDescriptorFiles[0];
		string appDescriptorContent = _fileSystem.ReadAllText(appDescriptorFile);
		AppDescriptorJson appDescriptor = JsonSerializer.Deserialize<AppDescriptorJson>(appDescriptorContent);
		List<Package> stalePackages = appDescriptor.Packages.FindAll(p => p.Name == packageName);
		foreach (Package package in stalePackages) {
			appDescriptor.Packages.Remove(package);
		}

		appDescriptor.Packages.Add(GetPackageFromDescriptor(packagesPath, packageName));
		SaveAppDescriptorToFile(appDescriptor, appDescriptorFile);
	}

	internal static bool IsValidPackageName(string packageName) {
		bool isValid = !string.IsNullOrWhiteSpace(packageName)
			&& packageName.Length <= MaxPackageNameLength
			&& packageName != "_"
			&& ClioIdentifier.IsIdentifierFragment(packageName);
		return isValid;
	}

	private static void ValidatePackageName(string packageName) {
		if (!IsValidPackageName(packageName)) {
			throw new ArgumentException(InvalidPackageNameMessage, nameof(packageName));
		}
	}

	private void CreateCore(string packagesPath, string packageName, bool? asApp, string schemaNamePrefix) {
		// Order matters three times over. The free-directory check is local and instant, so it runs
		// first: it must not cost the caller a Creatio request to be told the package already exists. The
		// prefix is then resolved BEFORE anything is written, because that resolution can reach Creatio
		// and a call interrupted mid-request would otherwise leave a package directory with no schema and
		// no descriptor - which the next attempt refuses, and a human has to delete by hand. The final
		// schema name is built and length-checked here for the same reason: a name the platform cannot
		// store used to fail deep inside the write, leaving exactly that unrecoverable directory.
		EnsurePackageDirectoryIsFree(packagesPath, packageName);
		string schemaName = null;
		if (asApp == true) {
			schemaName = BuildLocalizationSchemaName(_schemaNamePrefixResolver.Resolve(schemaNamePrefix),
				packageName);
			EnsureSchemaNameFits(schemaName);
		}
		WritePackageSkeleton(packagesPath, packageName, asApp == true);
		if (asApp == true) {
			AddLocalizationSchema(packagesPath, packageName, schemaName);
		}
		AddPackageToWorkspaceIfNeeded(packageName);
		if (asApp == true) {
			AddAppDescriptor(packagesPath, packageName);
		}
		else {
			UpdateAppDescriptorIfExists(packagesPath, packageName);
		}
		_workspaceSolutionCreator.Create();
	}

	#endregion

	#region Methods: Protected

	internal void SaveAppDescriptorToFile(AppDescriptorJson appDescriptor, string fileName) {
		JsonSerializerOptions options = new() {
			WriteIndented = true
		};
		string appDescriptorContent = JsonSerializer.Serialize(appDescriptor, options);
		_fileSystem.WriteAllTextToFile(fileName, appDescriptorContent);
	}

	#endregion

	#region Methods: Public

	/// <inheritdoc/>
	public void Create(string packageName, bool? asApp, string schemaNamePrefix = null) {
		string packagesPath = GetPackagesPath();
		Create(packagesPath, packageName, asApp, schemaNamePrefix);
	}

	/// <inheritdoc/>
	public void Create(string packagesPath, string packageName) {
		CreateCore(packagesPath, packageName, null, null);
	}

	/// <summary>Creates a package at an explicit packages path.</summary>
	/// <param name="packagesPath">Directory that holds packages.</param>
	/// <param name="packageName">Name of the package to create.</param>
	/// <param name="asApp">When <see langword="true"/>, also generates application-package artefacts.</param>
	/// <param name="schemaNamePrefix">
	/// Prefix for generated schema names; <see langword="null"/> resolves it from the target environment.
	/// </param>
	public void Create(string packagesPath, string packageName, bool? asApp, string schemaNamePrefix = null) {
		ValidatePackageName(packageName);
		CreateCore(packagesPath, packageName, asApp, schemaNamePrefix);
	}

	#endregion
}

#endregion

public class AppDescriptorJson{
	#region Properties: Public

	public string Name { get; set; }

	public string Description { get; set; }

	public string Maintainer { get; set; }

	public string Icon { get; set; }

	public string IconName { get; set; }

	public string Color { get; set; }

	public string Version { get; set; }
	public string RequiredPlatformVersion { get; set; } = "8.3.3";

	public string MarketplaceLink { get; set; }

	public string HelpLink { get; set; }

	public string OrderLink { get; set; }

	public string SupportEmail { get; set; }

	public string Code { get; set; }

	public List<Package> Packages { get; set; }

	#endregion
}

public class Package{
	#region Properties: Public

	public string UId { get; set; }

	public string Name { get; set; }

	#endregion
}
