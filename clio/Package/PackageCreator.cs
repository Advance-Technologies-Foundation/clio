using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Clio.Common;
using Clio.Workspaces;
using JsonConverter = System.Text.Json.Serialization.JsonConverter;

namespace Clio.Package
{

	#region Interface: IPackageCreator

	public interface IPackageCreator
	{

		#region Methods: Public

		void Create(string packageName);
		void Create(string packagesPath, string packageName);

		#endregion

	}

	#endregion

	#region Class: PackageCreator

	public class PackageCreator : IPackageCreator
	{

		#region Fields: Private

		private readonly EnvironmentSettings _environmentSettings;
		private readonly IWorkspace _workspace;
		private readonly IWorkspaceSolutionCreator _workspaceSolutionCreator;
		private readonly ITemplateProvider _templateProvider;
		private readonly IWorkspacePathBuilder _workspacePathBuilder;
		private readonly IStandalonePackageFileManager _standalonePackageFileManager;
		private readonly IJsonConverter _jsonConverter;
		private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
		private readonly IFileSystem _fileSystem;

		#endregion

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

		#endregion

		#region Methods: Private

		private PackageDescriptorDto CreatePackageDescriptor(string packageName, bool isStandalonePackage = true) =>
			new PackageDescriptorDto {
				Descriptor = new PackageDescriptor {
					Name = packageName,
					Maintainer = _environmentSettings.Maintainer,
					UId = Guid.NewGuid(),
					PackageVersion = "7.8.0",
					ProjectPath = isStandalonePackage ? $"Files/{packageName}.csproj" : string.Empty,
					Type = isStandalonePackage ? PackageType.Assembly : PackageType.General,
					ModifiedOnUtc = PackageDescriptor.ConvertToModifiedOnUtc(DateTime.Now),
					DependsOn = new List<PackageDependency>()
				} 
			};

		private void RenameTemplatePackageNameCsproj(string packagesPath, string packageName) {
			string packageFilesPath = _standalonePackageFileManager.BuildFilesPath(packagesPath, packageName);
			string templatePackageNameCsprojPath = Path.Combine(packageFilesPath, "PackageName.csproj");
			string newPackageNameCsprojPath = _standalonePackageFileManager
				.BuildStandaloneProjectPath(packagesPath, packageName);
			_fileSystem.MoveFile(templatePackageNameCsprojPath, newPackageNameCsprojPath);
		}

		private void CreatePackageDescriptorToFileSystem(string packagePath, string packageName) {
			PackageDescriptorDto descriptor = CreatePackageDescriptor(packageName);
			string descriptorPath = Path.Combine(packagePath, "descriptor.json");
			_jsonConverter.SerializeObjectToFile(descriptor, descriptorPath);
		}

		private void CreatePackageIfNotExists(string packagesPath, string packageName) {
			string packagePath = Path.Combine(packagesPath, packageName);
			if (_fileSystem.ExistsDirectory(packagePath)) {
				return;
			}
			_templateProvider.CopyTemplateFolder("package", packagePath);
			CreatePackageDescriptorToFileSystem(packagePath, packageName);
			CreatePackageProj(packagesPath, packageName);
			
		}

		private void CreatePackageProj(string packagesPath, string packageName){
			ApplyMacrosToProjectFiles(packagesPath, packageName);
			RenameTemplatePackageNameCsproj(packagesPath, packageName);
		}

		private void ApplyMacrosToProjectFiles(string packagesPath, string packageName){
			string packageFilesPath = _standalonePackageFileManager.BuildFilesPath(packagesPath, packageName);
			string packageNameTargetPropsPath = Path.Combine(packageFilesPath, "Directory.Build.targets");
			string packageNameTargetPropsContent = _fileSystem.ReadAllText(packageNameTargetPropsPath);
			string newPackageNameCsprojContent = packageNameTargetPropsContent
				.Replace("#PackageName#", packageName);
			_fileSystem.WriteAllTextToFile(packageNameTargetPropsPath, newPackageNameCsprojContent);
		}

		private string GetPackagesPath() =>
			IsWorkspace
				? _workspacePathBuilder.PackagesFolderPath
				: _workingDirectoriesProvider.CurrentDirectory;

		private void AddPackageToWorkspaceIfNeeded(string packageName) {
			if (!IsWorkspace) {
				return;
			}
			var workspacePackages = _workspace.WorkspaceSettings.Packages;
			if (workspacePackages.Contains(packageName)) {
				return;
			}
			workspacePackages.Add(packageName);
			_workspace.SaveWorkspaceSettings();
			_workspaceSolutionCreator.Create();
		}

		#endregion

		#region Methods: Public

		public void Create(string packageName) {
			var packagesPath = GetPackagesPath();
			Create(packagesPath, packageName);
		}

		public void Create(string packagesPath, string packageName) {
			Create(packagesPath, packageName, false);
		}
		public void Create(string packagesPath, string packageName, bool asApp) {
			CreatePackageIfNotExists(packagesPath, packageName);
			AddPackageToWorkspaceIfNeeded(packageName);
			
			
			var descriptorContent = _fileSystem.ReadAllText(Path.Combine(packagesPath,packageName,"descriptor.json"));
			var descriptor = JsonSerializer.Deserialize<PackageDescriptorDto>(descriptorContent);
			
			AppDescriptorJson addDescriptorDto = new () {
				Name = packageName,
				Maintainer = "Customer",
				Description = "",
				Icon = "",
				IconName = "",
				MarketplaceLink = "",
				OrderLink = "",
				SupportEmail ="",
				HelpLink = "",
				Color = "#FFAC07",
				Version = "0.1.0",
				Code = packageName,
				Packages = new []{
					new Package() {
					UId = descriptor.Descriptor.UId.ToString(),
					Name = descriptor.Descriptor.Name
				}}
			};
			var options = new JsonSerializerOptions() {
				WriteIndented = true,
				
			};
			string appDescriptorContent = JsonSerializer.Serialize(addDescriptorDto,options);
			var appDescriptorPath = Path.Combine(packagesPath,packageName, "Files","app-descriptor.json");
			_fileSystem.WriteAllTextToFile(appDescriptorPath, appDescriptorContent);
		}

		#endregion

	}

	#endregion
public class AppDescriptorJson
{
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
    public IEnumerable<Package> Packages { get; set; }
}

public class Package
{
    public string UId { get; set; }
    public string Name { get; set; }

}
}


