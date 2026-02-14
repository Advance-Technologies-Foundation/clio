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

	void Create(string packageName, bool? asApp);
	void Create(string packagesPath, string packageName);

	#endregion
}

#endregion

#region Class: PackageCreator

public class PackageCreator : IPackageCreator{
	#region Fields: Private

	private readonly EnvironmentSettings _environmentSettings;
	private readonly IFileSystem _fileSystem;
	private readonly IJsonConverter _jsonConverter;
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
		IFileSystem fileSystem) {
		environmentSettings.CheckArgumentNull(nameof(environmentSettings));
		templateProvider.CheckArgumentNull(nameof(templateProvider));
		workspace.CheckArgumentNull(nameof(workspace));
		workspaceSolutionCreator.CheckArgumentNull(nameof(workspaceSolutionCreator));
		workspacePathBuilder.CheckArgumentNull(nameof(workspacePathBuilder));
		standalonePackageFileManager.CheckArgumentNull(nameof(standalonePackageFileManager));
		jsonConverter.CheckArgumentNull(nameof(jsonConverter));
		workingDirectoriesProvider.CheckArgumentNull(nameof(workingDirectoriesProvider));
		fileSystem.CheckArgumentNull(nameof(fileSystem));
		_environmentSettings = environmentSettings;
		_workspace = workspace;
		_workspaceSolutionCreator = workspaceSolutionCreator;
		_templateProvider = templateProvider;
		_workspacePathBuilder = workspacePathBuilder;
		_standalonePackageFileManager = standalonePackageFileManager;
		_jsonConverter = jsonConverter;
		_workingDirectoriesProvider = workingDirectoriesProvider;
		_fileSystem = fileSystem;
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
			Code = packageName,
			Packages = new List<Package> {
				package
			}
		};
		string appDescriptorPath = Path.Combine(packagesPath, packageName, "Files", "app-descriptor.json");
		SaveAppDescriptorToFile(addDescriptorDto, appDescriptorPath);
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

	private void ApplyMacrosToCsFiles(string packagesPath, string packageName) {
		string packageFilesPath = _standalonePackageFileManager.BuildFilesPath(packagesPath, packageName);
		string[] csFiles = _fileSystem.GetFiles(packageFilesPath, "*.cs", SearchOption.AllDirectories);
		foreach (string csFilePath in csFiles) {
			string csFileContent = _fileSystem.ReadAllText(csFilePath);
			string newCsFileContent = csFileContent
									  .Replace("#PackageName#", packageName)
									  .Replace("#RootNameSpace#", RootNameSpace(packageName));
			_fileSystem.WriteAllTextToFile(csFilePath, newCsFileContent);
		}
	}

	private void ApplyMacrosToCsProjFile(string packagesPath, string packageName) {
		string packageFilesPath = _standalonePackageFileManager.BuildFilesPath(packagesPath, packageName);
		string csProjPath = Path.Combine(packageFilesPath, $"{packageName}.csproj");
		string csProjContent = _fileSystem.ReadAllText(csProjPath);
		string newCsProjContent = csProjContent
								  .Replace("#PackageName#", packageName)
								  .Replace("#RootNameSpace#", RootNameSpace(packageName));
		_fileSystem.WriteAllTextToFile(csProjPath, newCsProjContent);
	}


	private void ApplyMacrosToProjectFiles(string packagesPath, string packageName) {
		string packageFilesPath = _standalonePackageFileManager.BuildFilesPath(packagesPath, packageName);
		string packageNameTargetPropsPath = Path.Combine(packageFilesPath, "Directory.Build.targets");
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
				ProjectPath = isStandalonePackage ? $"Files/{packageName}.csproj" : string.Empty,
				Type = isStandalonePackage ? PackageType.Assembly : PackageType.General,
				ModifiedOnUtc = PackageDescriptor.ConvertToModifiedOnUtc(DateTime.Now),
				DependsOn = new List<PackageDependency>()
			}
		};
	}

	private void CreatePackageDescriptorToFileSystem(string packagePath, string packageName) {
		PackageDescriptorDto descriptor = CreatePackageDescriptor(packageName);
		string descriptorPath = Path.Combine(packagePath, "descriptor.json");
		_jsonConverter.SerializeObjectToFile(descriptor, descriptorPath);
	}

	private void CreatePackageIfNotExists(string packagesPath, string packageName) {
		string packagePath = Path.Combine(packagesPath, packageName);
		if (_fileSystem.ExistsDirectory(packagePath)) {
			throw new InvalidOperationException($"Directory '{packagePath}' already exists");
		}

		_templateProvider.CopyTemplateFolder("package", packagePath);
		CreatePackageDescriptorToFileSystem(packagePath, packageName);
		CreatePackageProj(packagesPath, packageName);
	}

	private void CreatePackageProj(string packagesPath, string packageName) {
		ApplyMacrosToProjectFiles(packagesPath, packageName);
		RenameTemplatePackageNameCsproj(packagesPath, packageName);
		ApplyMacrosToCsFiles(packagesPath, packageName);
		ApplyMacrosToCsProjFile(packagesPath, packageName);
		RenameMainAppCs(packagesPath, packageName);
	}

	private Package GetPackageFromDescriptor(string packagePath, string packageName) {
		string descriptorContent = _fileSystem.ReadAllText(Path.Combine(packagePath, packageName, "descriptor.json"));
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
		string templatePackageNameCsprojPath = Path.Combine(packageFilesPath, "PackageName.csproj");
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

	public void Create(string packageName, bool? asApp) {
		string packagesPath = GetPackagesPath();
		Create(packagesPath, packageName, asApp);
	}

	public void Create(string packagesPath, string packageName) {
		Create(packagesPath, packageName, null);
	}

	public void Create(string packagesPath, string packageName, bool? asApp) {
		CreatePackageIfNotExists(packagesPath, packageName);
		AddPackageToWorkspaceIfNeeded(packageName);
		if ((asApp.HasValue && asApp.Value)
			|| (asApp.HasValue && asApp.Value && _fileSystem.GetDirectories(packagesPath).Length == 1)) {
			AddAppDescriptor(packagesPath, packageName);
		}
		else {
			UpdateAppDescriptorIfExists(packagesPath, packageName);
		}
		_workspaceSolutionCreator.Create();
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
